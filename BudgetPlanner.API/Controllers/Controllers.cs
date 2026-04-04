using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using BudgetPlanner.API.Hubs;
using BudgetPlanner.API.Services;
using BudgetPlanner.Core.DTOs;
using BudgetPlanner.Core.Services;
using BudgetPlanner.DAL.Data;
using BudgetPlanner.DAL.Models;
using BudgetPlanner.DAL.Repositories;
using BudgetPlanner.Domain.Enums;
using BudgetPlanner.Domain.Models;

namespace BudgetPlanner.API.Controllers;

// ─── BASE ─────────────────────────────────────────────────────────────────────
[ApiController]
[Route("api/[controller]")]
public abstract class BaseApiController : ControllerBase
{
    protected string UserId =>
        User.FindFirst(ClaimTypes.NameIdentifier)?.Value
        ?? User.FindFirst("sub")?.Value
        ?? throw new UnauthorizedAccessException();

    protected string UserEmail =>
        User.FindFirst(ClaimTypes.Email)?.Value
        ?? User.FindFirst("email")?.Value
        ?? string.Empty;

    protected async Task BroadcastAsync(IHubContext<BudgetHub> hub, int budgetId, string evt, int? entityId = null) =>
        await hub.Clients.Group(BudgetHub.GroupName(budgetId))
            .SendAsync("BudgetEvent", new BudgetEvent(evt, budgetId, entityId, UserEmail, DateTime.UtcNow));
}

// ═══════════════════════════════════════════════════════════════════════════════
// AUTH
// ═══════════════════════════════════════════════════════════════════════════════
[Route("api/auth")]
public class AuthController : BaseApiController
{
    private readonly UserManager<ApplicationUser> _users;
    private readonly TokenService _tokens;
    private readonly BudgetDbContext _db;
    private const string CookieName = "bp_refresh";

    public AuthController(UserManager<ApplicationUser> users, TokenService tokens, BudgetDbContext db)
    { _users = users; _tokens = tokens; _db = db; }

    [HttpPost("register")]
    [EnableRateLimiting("AuthPolicy")]
    public async Task<IActionResult> Register([FromBody] RegisterDto dto)
    {
        var user = new ApplicationUser { UserName = dto.Email, Email = dto.Email, FirstName = dto.FirstName, LastName = dto.LastName };
        var result = await _users.CreateAsync(user, dto.Password);
        if (!result.Succeeded) return BadRequest(new { Errors = result.Errors.Select(e => e.Description) });

        var budget = new Budget { Name = $"{dto.FirstName}s budget", OwnerId = user.Id };
        _db.Budgets.Add(budget);
        await _db.SaveChangesAsync();
        _db.BudgetMemberships.Add(new BudgetMembership { BudgetId = budget.Id, UserId = user.Id, Role = BudgetMemberRole.Owner, AcceptedAt = DateTime.UtcNow });
        await _db.SaveChangesAsync();

        return await IssueTokensAsync(user);
    }

    [HttpPost("login")]
    [EnableRateLimiting("AuthPolicy")]
    public async Task<IActionResult> Login([FromBody] LoginDto dto)
    {
        var user = await _users.FindByEmailAsync(dto.Email);
        if (user == null || !await _users.CheckPasswordAsync(user, dto.Password))
            return Unauthorized(new { Message = "Ogiltiga inloggningsuppgifter." });
        return await IssueTokensAsync(user);
    }

    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh()
    {
        var raw = Request.Cookies[CookieName];
        if (string.IsNullOrEmpty(raw)) return Unauthorized(new { Message = "Refresh token saknas." });
        var token = await _tokens.ValidateRefreshTokenAsync(raw);
        if (token == null) return Unauthorized(new { Message = "Ogiltig eller utgången session." });
        await _tokens.RevokeRefreshTokenAsync(token.Id);
        var user = await _users.FindByIdAsync(token.UserId);
        if (user == null) return Unauthorized();
        return await IssueTokensAsync(user);
    }

    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout()
    {
        await _tokens.RevokeAllRefreshTokensAsync(UserId);
        Response.Cookies.Delete(CookieName);
        return Ok(new { Message = "Utloggad." });
    }

    [HttpGet("me")]
    [Authorize]
    public async Task<IActionResult> Me()
    {
        var user = await _users.FindByIdAsync(UserId);
        if (user == null) return NotFound();
        return Ok(new UserDto(user.Id, user.Email!, user.FirstName, user.LastName, await GetMembershipsAsync(user.Id)));
    }

    [HttpPost("accept-invite")]
    [Authorize]
    public async Task<IActionResult> AcceptInvite([FromBody] AcceptInviteDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Token))
            return BadRequest(new { Message = "Token saknas." });

        var membership = await _db.BudgetMemberships
            .Include(m => m.Budget)
            .FirstOrDefaultAsync(m => m.InviteToken == dto.Token && m.AcceptedAt == null);

        if (membership == null)
            return BadRequest(new { Message = "Ogiltig eller redan använd inbjudningslänk." });

        var alreadyMember = await _db.BudgetMemberships
            .AnyAsync(m => m.BudgetId == membership.BudgetId
                        && m.UserId   == UserId
                        && m.Id       != membership.Id);
        if (alreadyMember)
            return BadRequest(new { Message = "Du är redan medlem i denna budget." });

        membership.UserId      = UserId;
        membership.AcceptedAt  = DateTime.UtcNow;
        membership.InviteToken = null;
        await _db.SaveChangesAsync();

        var user = await _users.FindByIdAsync(UserId);
        if (user == null) return Unauthorized();
        var memberships = await GetMembershipsAsync(UserId);
        var access = _tokens.GenerateAccessToken(user, memberships);

        return Ok(new AuthResultDto(
            access,
            new UserDto(user.Id, user.Email!, user.FirstName, user.LastName, memberships)
        ));
    }

    private async Task<IActionResult> IssueTokensAsync(ApplicationUser user)
    {
        var memberships = await GetMembershipsAsync(user.Id);
        var access      = _tokens.GenerateAccessToken(user, memberships);
        var (raw, _)    = await _tokens.GenerateRefreshTokenAsync(user.Id);
        Response.Cookies.Append(CookieName, raw, new CookieOptions
        {
            HttpOnly  = true,
            Secure    = true,
            SameSite  = SameSiteMode.Strict,
            Expires   = DateTimeOffset.UtcNow.AddDays(30)
        });
        return Ok(new AuthResultDto(access, new UserDto(user.Id, user.Email!, user.FirstName, user.LastName, memberships)));
    }

    private async Task<List<BudgetMembershipDto>> GetMembershipsAsync(string userId) =>
        await _db.BudgetMemberships
            .Where(m => m.UserId == userId && m.AcceptedAt != null)
            .Include(m => m.Budget)
            .Select(m => new BudgetMembershipDto(m.BudgetId, m.Budget.Name, m.Role))
            .ToListAsync();
}

// ═══════════════════════════════════════════════════════════════════════════════
// BUDGETS
// ═══════════════════════════════════════════════════════════════════════════════
[Authorize, Route("api/budgets")]
public class BudgetsController : BaseApiController
{
    private readonly IBudgetRepository _repo;
    private readonly ICategoryRepository _cats;
    private readonly BudgetAuthorizationService _auth;
    private readonly IEmailService _email;
    private readonly IConfiguration _cfg;

    public BudgetsController(IBudgetRepository repo, ICategoryRepository cats,
        BudgetAuthorizationService auth, IEmailService email, IConfiguration cfg)
    { _repo = repo; _cats = cats; _auth = auth; _email = email; _cfg = cfg; }

    [HttpGet]
    public async Task<IActionResult> GetMyBudgets()
    {
        var budgets = await _repo.GetForUserAsync(UserId);
        var result  = new List<BudgetDto>();
        foreach (var b in budgets)
        {
            var m = await _auth.GetMembershipAsync(b.Id, UserId);
            result.Add(new BudgetDto(b.Id, b.Name, b.Description, b.IsActive, b.CreatedAt, m?.Role ?? BudgetMemberRole.Viewer));
        }
        return Ok(result);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateBudgetDto dto)
    {
        var budget = await _repo.CreateAsync(new Budget { Name = dto.Name, Description = dto.Description, OwnerId = UserId });
        await _repo.AddMemberAsync(new BudgetMembership { BudgetId = budget.Id, UserId = UserId, Role = BudgetMemberRole.Owner, AcceptedAt = DateTime.UtcNow });
        return CreatedAtAction(nameof(GetMyBudgets), new BudgetDto(budget.Id, budget.Name, budget.Description, budget.IsActive, budget.CreatedAt, BudgetMemberRole.Owner));
    }

    [HttpPut("{budgetId:int}")]
    public async Task<IActionResult> Update(int budgetId, [FromBody] UpdateBudgetDto dto)
    {
        if (!await _auth.HasRoleAsync(budgetId, UserId, BudgetMemberRole.Owner)) return Forbid();
        var b = await _repo.GetByIdAsync(budgetId);
        if (b == null) return NotFound();
        b.Name = dto.Name; b.Description = dto.Description;
        await _repo.UpdateAsync(b);
        return Ok();
    }

    [HttpDelete("{budgetId:int}")]
    public async Task<IActionResult> Delete(int budgetId)
    {
        if (!await _auth.HasRoleAsync(budgetId, UserId, BudgetMemberRole.Owner)) return Forbid();
        await _repo.DeleteAsync(budgetId);
        return NoContent();
    }

    // FIX: Returns a flat projection instead of the raw BudgetMembership domain objects.
    // The original returned b.Memberships which includes a Budget navigation property,
    // creating a Budget→Memberships→Budget circular reference that crashes System.Text.Json.
    [HttpGet("{budgetId:int}/members")]
    public async Task<IActionResult> GetMembers(int budgetId)
    {
        if (!await _auth.HasRoleAsync(budgetId, UserId, BudgetMemberRole.Viewer)) return Forbid();
        var b = await _repo.GetByIdAsync(budgetId);
        if (b == null) return NotFound();

        var result = b.Memberships
            .Where(m => m.AcceptedAt != null)
            .Select(m => new
            {
                m.Id,
                m.BudgetId,
                m.UserId,
                m.Role,
                m.InvitedByUserId,
                m.AcceptedAt
            })
            .ToList();

        return Ok(result);
    }

    [HttpPost("{budgetId:int}/invite")]
    public async Task<IActionResult> Invite(int budgetId, [FromBody] InviteMemberDto dto)
    {
        if (!await _auth.HasRoleAsync(budgetId, UserId, BudgetMemberRole.Owner)) return Forbid();
        var token = Guid.NewGuid().ToString("N");
        await _repo.AddMemberAsync(new BudgetMembership
        {
            BudgetId = budgetId, UserId = dto.Email, Role = dto.Role,
            InviteToken = token, InvitedByUserId = UserId
        });
        var baseUrl = _cfg["AppBaseUrl"] ?? "https://localhost:7001";
        var b = await _repo.GetByIdAsync(budgetId);
        await _email.SendInviteAsync(dto.Email, b?.Name ?? "Budget", token, baseUrl);
        return Ok(new { Message = "Inbjudan skickad." });
    }

    [HttpGet("{budgetId:int}/categories")]
    public async Task<IActionResult> GetCategories(int budgetId)
    {
        if (!await _auth.HasRoleAsync(budgetId, UserId, BudgetMemberRole.Viewer)) return Forbid();
        return Ok(await _cats.GetForBudgetAsync(budgetId));
    }
}

// ═══════════════════════════════════════════════════════════════════════════════
// JOURNAL
// ═══════════════════════════════════════════════════════════════════════════════
[Authorize, Route("api/budgets/{budgetId:int}/journal")]
public class JournalController : BaseApiController
{
    private readonly JournalQueryService _journal;
    private readonly BudgetAuthorizationService _auth;

    public JournalController(JournalQueryService journal, BudgetAuthorizationService auth)
    { _journal = journal; _auth = auth; }

    // FIX: QueryAsync now returns a 3-tuple (items, total, summary).
    // The summary is computed INSIDE QueryAsync on ALL filtered items before pagination,
    // so the totals reflect the complete filtered dataset — not just the current page.
    // The old pattern was:
    //   var (items, total) = await _journal.QueryAsync(query);
    //   var summary = _journal.ComputeSummary(items);  // BUG: items already paged
    [HttpGet]
    public async Task<IActionResult> Get(int budgetId, [FromQuery] JournalQuery query)
    {
        if (!await _auth.HasRoleAsync(budgetId, UserId, BudgetMemberRole.Viewer)) return Forbid();
        query.BudgetId = budgetId;

        var (items, total, summary) = await _journal.QueryAsync(query);

        return Ok(new
        {
            Result = new PagedResult<JournalEntryDto>
            {
                Items      = items,
                TotalCount = total,
                Page       = query.Page,
                PageSize   = query.PageSize
            },
            Summary = summary
        });
    }
}

// ═══════════════════════════════════════════════════════════════════════════════
// TRANSACTIONS
// ═══════════════════════════════════════════════════════════════════════════════
[Authorize, Route("api/budgets/{budgetId:int}/transactions")]
public class TransactionsController : BaseApiController
{
    private readonly ITransactionRepository _repo;
    private readonly IKonteringRepository _kontering;
    private readonly BudgetAuthorizationService _auth;
    private readonly ExportService _export;
    private readonly IHubContext<BudgetHub> _hub;

    public TransactionsController(ITransactionRepository repo, IKonteringRepository kontering,
        BudgetAuthorizationService auth, ExportService export, IHubContext<BudgetHub> hub)
    { _repo = repo; _kontering = kontering; _auth = auth; _export = export; _hub = hub; }

    [HttpGet]
    public async Task<IActionResult> GetAll(int budgetId, [FromQuery] TransactionQuery query)
    {
        if (!await _auth.HasRoleAsync(budgetId, UserId, BudgetMemberRole.Viewer)) return Forbid();
        query.BudgetId = budgetId;
        var (items, total) = await _repo.GetPagedAsync(query);
        var dtos = items.Select(MapDto).ToList();
        return Ok(new PagedResult<TransactionDto> { Items = dtos, TotalCount = total, Page = query.Page, PageSize = query.PageSize });
    }

    [HttpGet("{txId:int}")]
    public async Task<IActionResult> GetById(int budgetId, int txId)
    {
        if (!await _auth.HasRoleAsync(budgetId, UserId, BudgetMemberRole.Viewer)) return Forbid();
        var t = await _repo.GetByIdAsync(txId, budgetId);
        return t == null ? NotFound() : Ok(MapDto(t));
    }

    [HttpPost]
    public async Task<IActionResult> Create(int budgetId, [FromBody] CreateTransactionDto dto)
    {
        if (!await _auth.HasRoleAsync(budgetId, UserId, BudgetMemberRole.Editor)) return Forbid();
        var tx = MapFromDto(dto, budgetId, UserId);
        tx = await _repo.CreateAsync(tx);
        if (dto.KonteringRows.Any())
            await _kontering.SaveRowsAsync(tx.Id, dto.KonteringRows.Select(MapKontering).ToList());
        await BroadcastAsync(_hub, budgetId, "TransactionCreated", tx.Id);
        return CreatedAtAction(nameof(GetById), new { budgetId, txId = tx.Id }, MapDto(tx));
    }

    [HttpPut("{txId:int}")]
    public async Task<IActionResult> Update(int budgetId, int txId, [FromBody] UpdateTransactionDto dto)
    {
        if (!await _auth.HasRoleAsync(budgetId, UserId, BudgetMemberRole.Editor)) return Forbid();
        var tx = await _repo.GetByIdAsync(txId, budgetId);
        if (tx == null) return NotFound();
        ApplyDto(dto, tx, UserId);
        await _repo.UpdateAsync(tx);
        if (dto.KonteringRows.Any())
            await _kontering.SaveRowsAsync(tx.Id, dto.KonteringRows.Select(MapKontering).ToList());
        await BroadcastAsync(_hub, budgetId, "TransactionUpdated", tx.Id);
        return Ok(MapDto(tx));
    }

    [HttpDelete("{txId:int}")]
    public async Task<IActionResult> Delete(int budgetId, int txId)
    {
        if (!await _auth.HasRoleAsync(budgetId, UserId, BudgetMemberRole.Editor)) return Forbid();
        await _repo.DeleteAsync(txId, budgetId);
        await BroadcastAsync(_hub, budgetId, "TransactionDeleted", txId);
        return NoContent();
    }

    [HttpPost("batch-delete")]
    public async Task<IActionResult> BatchDelete(int budgetId, [FromBody] BatchDeleteDto dto)
    {
        if (!await _auth.HasRoleAsync(budgetId, UserId, BudgetMemberRole.Editor)) return Forbid();
        await _repo.DeleteBatchAsync(dto.Ids, budgetId);
        await BroadcastAsync(_hub, budgetId, "TransactionsBatchDeleted");
        return Ok(new { Deleted = dto.Ids.Count });
    }

    [HttpGet("export/pdf")]
    public async Task<IActionResult> ExportPdf(int budgetId, [FromQuery] TransactionQuery q)
    {
        if (!await _auth.HasRoleAsync(budgetId, UserId, BudgetMemberRole.Viewer)) return Forbid();
        q.BudgetId = budgetId;
        var txs = await _repo.GetForExportAsync(q);
        var pdf = _export.ExportToPdf(txs.Select(MapDto).ToList(), $"Budget {budgetId}");
        return File(pdf, "application/pdf", $"transaktioner_{DateTime.Today:yyyyMMdd}.pdf");
    }

    [HttpGet("export/excel")]
    public async Task<IActionResult> ExportExcel(int budgetId, [FromQuery] TransactionQuery q)
    {
        if (!await _auth.HasRoleAsync(budgetId, UserId, BudgetMemberRole.Viewer)) return Forbid();
        q.BudgetId = budgetId;
        var txs  = await _repo.GetForExportAsync(q);
        var xlsx = _export.ExportToExcel(txs.Select(MapDto).ToList(), new List<ProjectDto>(), $"Budget {budgetId}");
        return File(xlsx, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"transaktioner_{DateTime.Today:yyyyMMdd}.xlsx");
    }

    private static TransactionDto MapDto(Transaction t) => new()
    {
        Id = t.Id, BudgetId = t.BudgetId, StartDate = t.StartDate, EndDate = t.EndDate,
        NetAmount = t.NetAmount, GrossAmount = t.GrossAmount, Description = t.Description,
        CategoryId = t.CategoryId, CategoryName = t.Category?.Name ?? "",
        Type = t.Type, Recurrence = t.Recurrence, IsActive = t.IsActive,
        Month = t.Month, Rate = t.Rate, ProjectId = t.ProjectId, ProjectName = t.Project?.Name,
        HasKontering = t.HasKontering, CreatedByUserId = t.CreatedByUserId, CreatedAt = t.CreatedAt,
        KonteringRows = t.KonteringRows.Select(k => new KonteringRowDto
        {
            Id = k.Id, KontoNr = k.KontoNr, CostCenter = k.CostCenter,
            Amount = k.Amount, Percentage = k.Percentage, Description = k.Description
        }).ToList()
    };

    private static Transaction MapFromDto(CreateTransactionDto dto, int budgetId, string userId) => new()
    {
        BudgetId = budgetId, StartDate = dto.StartDate, EndDate = dto.EndDate,
        NetAmount = Adjust(dto.NetAmount, dto.Type), GrossAmount = dto.GrossAmount,
        Description = dto.Description, CategoryId = dto.CategoryId, Type = dto.Type,
        Recurrence = dto.Recurrence, IsActive = dto.IsActive, Month = dto.Month,
        Rate = dto.Rate, ProjectId = dto.ProjectId, HasKontering = dto.KonteringRows.Any(),
        CreatedByUserId = userId
    };

    private static void ApplyDto(CreateTransactionDto dto, Transaction tx, string userId)
    {
        tx.StartDate = dto.StartDate; tx.EndDate = dto.EndDate;
        tx.NetAmount = Adjust(dto.NetAmount, dto.Type); tx.GrossAmount = dto.GrossAmount;
        tx.Description = dto.Description; tx.CategoryId = dto.CategoryId; tx.Type = dto.Type;
        tx.Recurrence = dto.Recurrence; tx.IsActive = dto.IsActive; tx.Month = dto.Month;
        tx.Rate = dto.Rate; tx.ProjectId = dto.ProjectId; tx.HasKontering = dto.KonteringRows.Any();
        tx.UpdatedByUserId = userId;
    }

    private static decimal Adjust(decimal v, TransactionType t) =>
        t == TransactionType.Expense ? -Math.Abs(v) : Math.Abs(v);

    private static KonteringRow MapKontering(KonteringRowDto d) => new()
    { KontoNr = d.KontoNr, CostCenter = d.CostCenter, Amount = d.Amount, Percentage = d.Percentage, Description = d.Description };
}

// ═══════════════════════════════════════════════════════════════════════════════
// PROJECTS
// ═══════════════════════════════════════════════════════════════════════════════
[Authorize, Route("api/budgets/{budgetId:int}/projects")]
public class ProjectsController : BaseApiController
{
    private readonly IProjectRepository _repo;
    private readonly BudgetAuthorizationService _auth;
    private readonly IHubContext<BudgetHub> _hub;

    public ProjectsController(IProjectRepository repo, BudgetAuthorizationService auth, IHubContext<BudgetHub> hub)
    { _repo = repo; _auth = auth; _hub = hub; }

    [HttpGet]
    public async Task<IActionResult> GetAll(int budgetId)
    {
        if (!await _auth.HasRoleAsync(budgetId, UserId, BudgetMemberRole.Viewer)) return Forbid();
        var withSpent = await _repo.GetForBudgetWithSpentAsync(budgetId);
        var dtos = withSpent.Select(x => Map(x.Project, x.SpentAmount)).ToList();
        return Ok(dtos);
    }

    [HttpGet("{projectId:int}")]
    public async Task<IActionResult> GetById(int budgetId, int projectId)
    {
        if (!await _auth.HasRoleAsync(budgetId, UserId, BudgetMemberRole.Viewer)) return Forbid();
        var all   = await _repo.GetForBudgetWithSpentAsync(budgetId);
        var found = all.FirstOrDefault(x => x.Project.Id == projectId);
        if (found.Project == null) return NotFound();
        return Ok(Map(found.Project, found.SpentAmount));
    }

    [HttpPost]
    public async Task<IActionResult> Create(int budgetId, [FromBody] CreateProjectDto dto)
    {
        if (!await _auth.HasRoleAsync(budgetId, UserId, BudgetMemberRole.Editor)) return Forbid();
        var p = await _repo.CreateAsync(new Project
        {
            BudgetId     = budgetId,  Name        = dto.Name,
            Description  = dto.Description, BudgetAmount = dto.BudgetAmount,
            StartDate    = dto.StartDate,   EndDate      = dto.EndDate
        });
        await BroadcastAsync(_hub, budgetId, "ProjectCreated", p.Id);
        return CreatedAtAction(nameof(GetById), new { budgetId, projectId = p.Id }, Map(p, 0));
    }

    [HttpPut("{projectId:int}")]
    public async Task<IActionResult> Update(int budgetId, int projectId, [FromBody] UpdateProjectDto dto)
    {
        if (!await _auth.HasRoleAsync(budgetId, UserId, BudgetMemberRole.Editor)) return Forbid();
        var p = await _repo.GetByIdAsync(projectId, budgetId);
        if (p == null) return NotFound();
        p.Name = dto.Name; p.Description = dto.Description; p.BudgetAmount = dto.BudgetAmount;
        p.StartDate = dto.StartDate; p.EndDate = dto.EndDate; p.IsActive = dto.IsActive;
        await _repo.UpdateAsync(p);
        await BroadcastAsync(_hub, budgetId, "ProjectUpdated", p.Id);
        return Ok();
    }

    [HttpDelete("{projectId:int}")]
    public async Task<IActionResult> Delete(int budgetId, int projectId)
    {
        if (!await _auth.HasRoleAsync(budgetId, UserId, BudgetMemberRole.Editor)) return Forbid();
        await _repo.DeleteAsync(projectId, budgetId);
        await BroadcastAsync(_hub, budgetId, "ProjectDeleted", projectId);
        return NoContent();
    }

    private static ProjectDto Map(Project p, decimal spent) => new()
    {
        Id = p.Id, BudgetId = p.BudgetId, Name = p.Name, Description = p.Description,
        BudgetAmount = p.BudgetAmount, StartDate = p.StartDate, EndDate = p.EndDate,
        IsActive = p.IsActive, SpentAmount = spent
    };
}

// ═══════════════════════════════════════════════════════════════════════════════
// MILERSÄTTNING
// ═══════════════════════════════════════════════════════════════════════════════
[Authorize, Route("api/budgets/{budgetId:int}/milersattning")]
public class MilersattningController : BaseApiController
{
    private readonly IMilersattningRepository _repo;
    private readonly MilersattningService _svc;
    private readonly BudgetAuthorizationService _auth;
    private readonly IHubContext<BudgetHub> _hub;

    public MilersattningController(IMilersattningRepository repo, MilersattningService svc, BudgetAuthorizationService auth, IHubContext<BudgetHub> hub)
    { _repo = repo; _svc = svc; _auth = auth; _hub = hub; }

    [HttpGet]
    public async Task<IActionResult> GetAll(int budgetId)
    {
        if (!await _auth.HasRoleAsync(budgetId, UserId, BudgetMemberRole.Viewer)) return Forbid();
        var items = await _repo.GetForBudgetAsync(budgetId);
        return Ok(items.Select(m => new MilersattningDto
        {
            Id = m.Id, BudgetId = m.BudgetId, UserId = m.UserId, TripDate = m.TripDate,
            FromLocation = m.FromLocation, ToLocation = m.ToLocation,
            DistanceKm = m.DistanceKm, RatePerKm = m.RatePerKm, Purpose = m.Purpose,
            ReimbursementAmount = m.ReimbursementAmount, LinkedTransactionId = m.LinkedTransactionId
        }));
    }

    [HttpPost]
    public async Task<IActionResult> Create(int budgetId, [FromBody] CreateMilersattningDto dto)
    {
        if (!await _auth.HasRoleAsync(budgetId, UserId, BudgetMemberRole.Editor)) return Forbid();
        var entry = await _svc.CreateAsync(budgetId, UserId, dto);
        await BroadcastAsync(_hub, budgetId, "MilersattningCreated", entry.Id);
        return Ok(entry);
    }

    [HttpDelete("{entryId:int}")]
    public async Task<IActionResult> Delete(int budgetId, int entryId)
    {
        if (!await _auth.HasRoleAsync(budgetId, UserId, BudgetMemberRole.Editor)) return Forbid();
        await _svc.DeleteAsync(entryId, budgetId);
        await BroadcastAsync(_hub, budgetId, "MilersattningDeleted", entryId);
        return NoContent();
    }

    [HttpGet("rate")]
    public async Task<IActionResult> GetRate(int budgetId)
    {
        if (!await _auth.HasRoleAsync(budgetId, UserId, BudgetMemberRole.Viewer)) return Forbid();
        return Ok(new { Rate = await _svc.GetRateAsync(budgetId) });
    }
}

// ═══════════════════════════════════════════════════════════════════════════════
// VAB
// ═══════════════════════════════════════════════════════════════════════════════
[Authorize, Route("api/budgets/{budgetId:int}/vab")]
public class VabController : BaseApiController
{
    private readonly IVabRepository _repo;
    private readonly VabService _svc;
    private readonly BudgetAuthorizationService _auth;
    private readonly IHubContext<BudgetHub> _hub;

    public VabController(IVabRepository repo, VabService svc, BudgetAuthorizationService auth, IHubContext<BudgetHub> hub)
    { _repo = repo; _svc = svc; _auth = auth; _hub = hub; }

    [HttpGet]
    public async Task<IActionResult> GetAll(int budgetId)
    {
        if (!await _auth.HasRoleAsync(budgetId, UserId, BudgetMemberRole.Viewer)) return Forbid();
        var items = await _repo.GetForBudgetAsync(budgetId);
        return Ok(items.Select(v => new VabDto
        {
            Id = v.Id, BudgetId = v.BudgetId, UserId = v.UserId, ChildName = v.ChildName,
            StartDate = v.StartDate, EndDate = v.EndDate, DailyBenefit = v.DailyBenefit,
            Rate = v.Rate, TotalDays = v.TotalDays, TotalAmount = v.TotalAmount,
            LinkedTransactionId = v.LinkedTransactionId
        }));
    }

    [HttpPost]
    public async Task<IActionResult> Create(int budgetId, [FromBody] CreateVabDto dto)
    {
        if (!await _auth.HasRoleAsync(budgetId, UserId, BudgetMemberRole.Editor)) return Forbid();
        var entry = await _svc.CreateAsync(budgetId, UserId, dto);
        await BroadcastAsync(_hub, budgetId, "VabCreated", entry.Id);
        return Ok(entry);
    }

    [HttpDelete("{entryId:int}")]
    public async Task<IActionResult> Delete(int budgetId, int entryId)
    {
        if (!await _auth.HasRoleAsync(budgetId, UserId, BudgetMemberRole.Editor)) return Forbid();
        await _svc.DeleteAsync(entryId, budgetId);
        await BroadcastAsync(_hub, budgetId, "VabDeleted", entryId);
        return NoContent();
    }
}

// ═══════════════════════════════════════════════════════════════════════════════
// RECEIPTS
// ═══════════════════════════════════════════════════════════════════════════════
[Authorize, Route("api/budgets/{budgetId:int}/receipts")]
public class ReceiptsController : BaseApiController
{
    private readonly IReceiptRepository _repo;
    private readonly ReceiptService _svc;
    private readonly BudgetAuthorizationService _auth;
    private readonly IHubContext<BudgetHub> _hub;
    private readonly IEmailService _email;
    private readonly IConfiguration _cfg;

    public ReceiptsController(IReceiptRepository repo, ReceiptService svc, BudgetAuthorizationService auth,
        IHubContext<BudgetHub> hub, IEmailService email, IConfiguration cfg)
    { _repo = repo; _svc = svc; _auth = auth; _hub = hub; _email = email; _cfg = cfg; }

    [HttpGet]
    public async Task<IActionResult> GetAll(int budgetId, [FromQuery] ReceiptQuery query)
    {
        if (!await _auth.HasRoleAsync(budgetId, UserId, BudgetMemberRole.Viewer)) return Forbid();
        query.BudgetId = budgetId;
        var (batches, total) = await _repo.GetPagedAsync(query);
        return Ok(new PagedResult<ReceiptBatchDto>
        {
            Items      = batches.Select(Map).ToList(),
            TotalCount = total,
            Page       = query.Page,
            PageSize   = query.PageSize
        });
    }

    [HttpGet("{batchId:int}")]
    public async Task<IActionResult> GetById(int budgetId, int batchId)
    {
        if (!await _auth.HasRoleAsync(budgetId, UserId, BudgetMemberRole.Viewer)) return Forbid();
        var b = await _repo.GetByIdAsync(batchId, budgetId);
        return b == null ? NotFound() : Ok(Map(b));
    }

    [HttpPost]
    public async Task<IActionResult> Create(int budgetId, [FromBody] CreateReceiptBatchDto dto)
    {
        if (!await _auth.HasRoleAsync(budgetId, UserId, BudgetMemberRole.Editor)) return Forbid();
        var batch = await _svc.CreateBatchAsync(budgetId, UserId, UserEmail, dto);
        await BroadcastAsync(_hub, budgetId, "ReceiptBatchCreated", batch.Id);
        return CreatedAtAction(nameof(GetById), new { budgetId, batchId = batch.Id }, Map(batch));
    }

    [HttpPut("{batchId:int}")]
    public async Task<IActionResult> Update(int budgetId, int batchId, [FromBody] UpdateReceiptBatchDto dto)
    {
        if (!await _auth.HasRoleAsync(budgetId, UserId, BudgetMemberRole.Editor)) return Forbid();
        var b = await _repo.GetByIdAsync(batchId, budgetId);
        if (b == null) return NotFound();
        if (b.Status != ReceiptBatchStatus.Draft) return BadRequest("Kan bara redigera utkast.");
        b.Label = dto.Label; b.BatchCategoryId = dto.BatchCategoryId; b.ProjectId = dto.ProjectId;
        await _repo.UpdateAsync(b);
        return Ok(Map(b));
    }

    [HttpDelete("{batchId:int}")]
    public async Task<IActionResult> Delete(int budgetId, int batchId)
    {
        if (!await _auth.HasRoleAsync(budgetId, UserId, BudgetMemberRole.Editor)) return Forbid();
        var b = await _repo.GetByIdAsync(batchId, budgetId);
        if (b == null) return NotFound();
        if (b.Status is not (ReceiptBatchStatus.Draft or ReceiptBatchStatus.Rejected))
            return BadRequest("Kan bara ta bort utkast eller avslagna underlag.");
        await _repo.DeleteAsync(batchId, budgetId);
        await BroadcastAsync(_hub, budgetId, "ReceiptBatchDeleted", batchId);
        return NoContent();
    }

    [HttpPost("{batchId:int}/lines")]
    public async Task<IActionResult> AddLine(int budgetId, int batchId, [FromBody] CreateReceiptLineDto dto)
    {
        if (!await _auth.HasRoleAsync(budgetId, UserId, BudgetMemberRole.Editor)) return Forbid();
        var line = await _svc.AddLineAsync(batchId, budgetId, dto with { BudgetId = budgetId });
        await BroadcastAsync(_hub, budgetId, "ReceiptLineAdded", batchId);
        return Ok(MapLine(line));
    }

    [HttpPut("{batchId:int}/lines/{lineId:int}")]
    public async Task<IActionResult> UpdateLine(int budgetId, int batchId, int lineId, [FromBody] UpdateReceiptLineDto dto)
    {
        if (!await _auth.HasRoleAsync(budgetId, UserId, BudgetMemberRole.Editor)) return Forbid();
        var b = await _repo.GetByIdAsync(batchId, budgetId);
        if (b?.Status != ReceiptBatchStatus.Draft) return BadRequest("Kan bara redigera rader i utkast.");
        var line = await _repo.GetLineAsync(lineId, batchId);
        if (line == null) return NotFound();
        line.Date = dto.Date; line.Amount = dto.Amount; line.Vendor = dto.Vendor; line.Description = dto.Description;
        await _repo.UpdateLineAsync(line);
        return Ok(MapLine(line));
    }

    [HttpDelete("{batchId:int}/lines/{lineId:int}")]
    public async Task<IActionResult> DeleteLine(int budgetId, int batchId, int lineId)
    {
        if (!await _auth.HasRoleAsync(budgetId, UserId, BudgetMemberRole.Editor)) return Forbid();
        var b = await _repo.GetByIdAsync(batchId, budgetId);
        if (b?.Status != ReceiptBatchStatus.Draft) return BadRequest("Kan bara ta bort rader i utkast.");
        await _repo.DeleteLineAsync(lineId, batchId);
        return NoContent();
    }

    [HttpPatch("{batchId:int}/status")]
    public async Task<IActionResult> UpdateStatus(int budgetId, int batchId, [FromBody] UpdateReceiptStatusDto dto)
    {
        var membership = await _auth.GetMembershipAsync(budgetId, UserId);
        if (membership == null) return Forbid();
        try
        {
            var updated = await _svc.UpdateStatusAsync(batchId, budgetId, dto.NewStatus, UserId, membership.Role, dto.RejectionReason);
            if (dto.NewStatus == ReceiptBatchStatus.Submitted)
                await BroadcastAsync(_hub, budgetId, "ReceiptBatchSubmitted", batchId);
            else
            {
                var batch = await _repo.GetByIdAsync(batchId, budgetId);
                if (batch?.CreatedByEmail != null)
                    await _email.SendReceiptStatusChangedAsync(batch.CreatedByEmail, batch.Label, dto.NewStatus.ToString(), dto.RejectionReason);
                await BroadcastAsync(_hub, budgetId, "ReceiptBatchStatusChanged", batchId);
            }
            return Ok(Map(updated));
        }
        catch (InvalidOperationException ex) { return BadRequest(new { Message = ex.Message }); }
    }

    [HttpGet("{batchId:int}/export/pdf")]
    public async Task<IActionResult> ExportPdf(int budgetId, int batchId)
    {
        if (!await _auth.HasRoleAsync(budgetId, UserId, BudgetMemberRole.Viewer)) return Forbid();
        var b = await _repo.GetByIdAsync(batchId, budgetId);
        if (b == null) return NotFound();
        var pdf = _svc.ExportBatchToPdf(b, $"Budget {budgetId}");
        return File(pdf, "application/pdf", $"utlagg_{b.Label.Replace(" ", "_")}_{DateTime.Today:yyyyMMdd}.pdf");
    }

    [HttpGet("/api/budgets/{budgetId:int}/receipt-categories")]
    public async Task<IActionResult> GetCategories(int budgetId)
    {
        if (!await _auth.HasRoleAsync(budgetId, UserId, BudgetMemberRole.Viewer)) return Forbid();
        var cats = await _svc.GetCategoriesAsync();
        return Ok(cats.Select(c => new ReceiptBatchCategoryDto
        {
            Id = c.Id, Name = c.Name, IconName = c.IconName, Description = c.Description
        }));
    }

    private static ReceiptBatchDto Map(ReceiptBatch b) => new()
    {
        Id = b.Id, BudgetId = b.BudgetId, ProjectId = b.ProjectId, ProjectName = b.Project?.Name,
        Label = b.Label, BatchCategoryId = b.BatchCategoryId,
        BatchCategoryName = b.Category?.Name ?? "", BatchCategoryIcon = b.Category?.IconName,
        Status = b.Status, StatusLabel = Swedish(b.Status),
        CreatedByUserId = b.CreatedByUserId, CreatedByEmail = b.CreatedByEmail,
        SubmittedAt = b.SubmittedAt, ApprovedAt = b.ApprovedAt,
        RejectedAt = b.RejectedAt, RejectionReason = b.RejectionReason, ReimbursedAt = b.ReimbursedAt,
        CreatedAt = b.CreatedAt, TotalAmount = b.Lines.Sum(l => l.Amount), LineCount = b.Lines.Count,
        Lines = b.Lines.Select(MapLine).ToList()
    };

    private static ReceiptLineDto MapLine(ReceiptLine l) => new()
    {
        Id = l.Id, BatchId = l.BatchId, SequenceNumber = l.SequenceNumber, ReferenceCode = l.ReferenceCode,
        Date = l.Date, Amount = l.Amount, Currency = l.Currency, Vendor = l.Vendor,
        Description = l.Description, LinkedTransactionId = l.LinkedTransactionId, DigitalReceiptUrl = l.DigitalReceiptUrl
    };

    private static string Swedish(ReceiptBatchStatus s) => s switch
    {
        ReceiptBatchStatus.Draft      => "Utkast",
        ReceiptBatchStatus.Submitted  => "Inskickad",
        ReceiptBatchStatus.Approved   => "Godkänd",
        ReceiptBatchStatus.Rejected   => "Avslagen",
        ReceiptBatchStatus.Reimbursed => "Utbetald",
        _                             => s.ToString()
    };
}

// ═══════════════════════════════════════════════════════════════════════════════
// IMPORT
// ═══════════════════════════════════════════════════════════════════════════════
[Authorize, Route("api/budgets/{budgetId:int}/import")]
public class ImportController : BaseApiController
{
    private readonly ImportService _svc;
    private readonly BudgetAuthorizationService _auth;
    private readonly IHubContext<BudgetHub> _hub;

    public ImportController(ImportService svc, BudgetAuthorizationService auth, IHubContext<BudgetHub> hub)
    { _svc = svc; _auth = auth; _hub = hub; }

    [HttpGet("profiles")]
    public IActionResult GetProfiles() => Ok(ImportService.GetProfiles().Select(p => p.BankName));

    [HttpPost("preview")]
    [RequestSizeLimit(10 * 1024 * 1024)]
    public async Task<IActionResult> Preview(int budgetId, IFormFile file, [FromQuery] string bankProfile = "SEB")
    {
        if (!await _auth.HasRoleAsync(budgetId, UserId, BudgetMemberRole.Editor)) return Forbid();
        if (file == null || file.Length == 0) return BadRequest("Ingen fil uppladdad.");
        await using var stream = file.OpenReadStream();
        var session = await _svc.PreviewAsync(stream, bankProfile, budgetId, UserId);
        return Ok(session);
    }

    [HttpPost("confirm")]
    public async Task<IActionResult> Confirm(int budgetId, [FromBody] ConfirmImportDto dto)
    {
        if (!await _auth.HasRoleAsync(budgetId, UserId, BudgetMemberRole.Editor)) return Forbid();
        var count = await _svc.ConfirmAsync(dto, budgetId, UserId);
        await BroadcastAsync(_hub, budgetId, "TransactionsImported");
        return Ok(new { Imported = count });
    }
}

// ═══════════════════════════════════════════════════════════════════════════════
// REPORTS
// ═══════════════════════════════════════════════════════════════════════════════
[Authorize, Route("api/budgets/{budgetId:int}/reports")]
public class ReportsController : BaseApiController
{
    private readonly ITransactionRepository _txRepo;
    private readonly BudgetAuthorizationService _auth;

    public ReportsController(ITransactionRepository txRepo, BudgetAuthorizationService auth)
    { _txRepo = txRepo; _auth = auth; }

    [HttpGet("monthly-summary")]
    public async Task<IActionResult> MonthlySummary(int budgetId, [FromQuery] int year = 0)
    {
        if (!await _auth.HasRoleAsync(budgetId, UserId, BudgetMemberRole.Viewer)) return Forbid();
        if (year == 0) year = DateTime.Today.Year;
        var q = new TransactionQuery
        {
            BudgetId          = budgetId,
            FilterByStartDate = true, StartDate = new DateTime(year, 1, 1),
            FilterByEndDate   = true, EndDate   = new DateTime(year, 12, 31),
            PageSize          = int.MaxValue
        };
        var (txs, _) = await _txRepo.GetPagedAsync(q);
        var rows = txs
            .GroupBy(t => t.StartDate.Month)
            .Select(g => new MonthlyRow
            {
                Year     = year,
                Month    = g.Key,
                Income   = g.Where(t => t.NetAmount > 0).Sum(t => t.NetAmount),
                Expenses = g.Where(t => t.NetAmount < 0).Sum(t => t.NetAmount)
            })
            .OrderBy(r => r.Month)
            .ToList();
        return Ok(new MonthlySummary { Rows = rows });
    }

    [HttpGet("category-breakdown")]
    public async Task<IActionResult> CategoryBreakdown(int budgetId, [FromQuery] DateTime? from, [FromQuery] DateTime? to)
    {
        if (!await _auth.HasRoleAsync(budgetId, UserId, BudgetMemberRole.Viewer)) return Forbid();
        var q = new TransactionQuery
        {
            BudgetId          = budgetId,
            FilterByStartDate = from.HasValue, StartDate = from,
            FilterByEndDate   = to.HasValue,   EndDate   = to,
            PageSize          = int.MaxValue
        };
        var (txs, _) = await _txRepo.GetPagedAsync(q);
        var breakdown = txs
            .Where(t => t.NetAmount < 0)
            .GroupBy(t => t.Category?.Name ?? "Okänd")
            .Select(g => new CategoryBreakdownItem { Category = g.Key, Total = Math.Abs(g.Sum(t => t.NetAmount)) })
            .OrderByDescending(x => x.Total);
        return Ok(breakdown);
    }
}

// ═══════════════════════════════════════════════════════════════════════════════
// AUDIT
// ═══════════════════════════════════════════════════════════════════════════════
[Authorize, Route("api/budgets/{budgetId:int}/audit")]
public class AuditController : BaseApiController
{
    private readonly IAuditRepository _repo;
    private readonly BudgetAuthorizationService _auth;

    public AuditController(IAuditRepository repo, BudgetAuthorizationService auth)
    { _repo = repo; _auth = auth; }

    [HttpGet]
    public async Task<IActionResult> GetAuditLog(int budgetId, [FromQuery] int page = 1, [FromQuery] int pageSize = 50)
    {
        if (!await _auth.HasRoleAsync(budgetId, UserId, BudgetMemberRole.Auditor)) return Forbid();
        var (items, total) = await _repo.GetPagedAsync(budgetId, page, pageSize);
        return Ok(new PagedResult<AuditLogDto>
        {
            Items = items.Select(a => new AuditLogDto
            {
                Id         = a.Id,
                UserEmail  = a.UserEmail,
                EntityName = a.EntityName,
                EntityId   = a.EntityId,
                Action     = a.Action,
                OldValues  = a.OldValues,
                NewValues  = a.NewValues,
                Timestamp  = a.Timestamp
            }).ToList(),
            TotalCount = total,
            Page       = page,
            PageSize   = pageSize
        });
    }
}
