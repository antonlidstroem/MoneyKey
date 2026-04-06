using Microsoft.EntityFrameworkCore;
using BudgetPlanner.DAL.Data;
using BudgetPlanner.Domain.Models;
using BudgetPlanner.Domain.Enums;

namespace BudgetPlanner.DAL.Repositories;

// ═══════════════════════════════════════════════════════════════════════════════
// QUERY MODELS
// ═══════════════════════════════════════════════════════════════════════════════

public class TransactionQuery
{
    public int BudgetId { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
    public string? SortBy { get; set; } = "StartDate";
    public string? SortDir { get; set; } = "desc";
    public bool FilterByStartDate { get; set; }
    public DateTime? StartDate { get; set; }
    public bool FilterByEndDate { get; set; }
    public DateTime? EndDate { get; set; }
    public bool FilterByDescription { get; set; }
    public string? Description { get; set; }
    public bool FilterByAmount { get; set; }
    public decimal? Amount { get; set; }
    public bool FilterByCategory { get; set; }
    public int? CategoryId { get; set; }
    public bool FilterByRecurrence { get; set; }
    public Recurrence? Recurrence { get; set; }
    public bool FilterByMonth { get; set; }
    public BudgetMonth? Month { get; set; }
    public int? ProjectId { get; set; }
    public TransactionType? Type { get; set; }
    public bool? IsActive { get; set; }
}

public class ReceiptQuery
{
    public int BudgetId { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
    public string? SortBy { get; set; } = "CreatedAt";
    public string? SortDir { get; set; } = "desc";
    public string? LabelSearch { get; set; }
    public int? BatchCategoryId { get; set; }
    public int? ProjectId { get; set; }
    public string? CreatedByUserId { get; set; }
    public List<ReceiptBatchStatus>? Statuses { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
}

// ═══════════════════════════════════════════════════════════════════════════════
// INTERFACES
// ═══════════════════════════════════════════════════════════════════════════════

public interface ITransactionRepository
{
    Task<(List<Transaction> Items, int TotalCount)> GetPagedAsync(TransactionQuery query);
    Task<Transaction?> GetByIdAsync(int id, int budgetId);
    Task<Transaction> CreateAsync(Transaction transaction);
    Task<Transaction> UpdateAsync(Transaction transaction);
    Task DeleteAsync(int id, int budgetId);
    Task DeleteBatchAsync(List<int> ids, int budgetId);
    Task<List<Transaction>> GetForExportAsync(TransactionQuery query);
}

public interface IBudgetRepository
{
    Task<List<Budget>> GetForUserAsync(string userId);
    Task<Budget?> GetByIdAsync(int id);
    Task<Budget> CreateAsync(Budget budget);
    Task<Budget> UpdateAsync(Budget budget);
    Task DeleteAsync(int id);
    Task<BudgetMembership?> GetMembershipAsync(int budgetId, string userId);
    Task<BudgetMembership> AddMemberAsync(BudgetMembership membership);
    Task UpdateMemberRoleAsync(int budgetId, string userId, BudgetMemberRole role);
    Task RemoveMemberAsync(int budgetId, string userId);
    Task<BudgetMembership?> GetByInviteTokenAsync(string token);
}

public interface ICategoryRepository
{
    Task<List<Category>> GetForBudgetAsync(int budgetId);
    Task<Category> CreateAsync(Category category);
    Task DeleteAsync(int id, int budgetId);
}

public interface IProjectRepository
{
    /// <summary>Returns all projects for a budget with their total spent amount
    /// in a single SQL query (LEFT JOIN + SUM).</summary>
    Task<List<(Project Project, decimal SpentAmount)>> GetForBudgetWithSpentAsync(int budgetId);

    Task<Project?> GetByIdAsync(int id, int budgetId);
    Task<Project> CreateAsync(Project project);
    Task<Project> UpdateAsync(Project project);
    Task DeleteAsync(int id, int budgetId);
}

public interface IMilersattningRepository
{
    Task<List<MilersattningEntry>> GetForBudgetAsync(int budgetId, string? userId = null);
    Task<MilersattningEntry?> GetByIdAsync(int id, int budgetId);
    Task<MilersattningEntry> CreateAsync(MilersattningEntry entry);
    Task<MilersattningEntry> UpdateAsync(MilersattningEntry entry);
    Task DeleteAsync(int id, int budgetId);
}

public interface IVabRepository
{
    Task<List<VabEntry>> GetForBudgetAsync(int budgetId, string? userId = null);
    Task<VabEntry?> GetByIdAsync(int id, int budgetId);
    Task<VabEntry> CreateAsync(VabEntry entry);
    Task<VabEntry> UpdateAsync(VabEntry entry);
    Task DeleteAsync(int id, int budgetId);
}

public interface IKonteringRepository
{
    Task<List<KonteringRow>> GetForTransactionAsync(int transactionId);
    Task SaveRowsAsync(int transactionId, List<KonteringRow> rows);
}

public interface IReceiptRepository
{
    Task<(List<ReceiptBatch> Items, int TotalCount)> GetPagedAsync(ReceiptQuery query);
    Task<ReceiptBatch?> GetByIdAsync(int id, int budgetId);
    Task<ReceiptBatch> CreateAsync(ReceiptBatch batch);
    Task<ReceiptBatch> UpdateAsync(ReceiptBatch batch);
    Task DeleteAsync(int id, int budgetId);
    Task<ReceiptLine> AddLineAsync(ReceiptLine line);
    Task<ReceiptLine?> GetLineAsync(int lineId, int batchId);
    Task<ReceiptLine> UpdateLineAsync(ReceiptLine line);
    Task DeleteLineAsync(int lineId, int batchId);
    Task<int> GetNextSequenceNumberAsync(int batchId);
    Task<List<ReceiptBatchCategory>> GetCategoriesAsync();
}

public interface IAuditRepository
{
    Task LogAsync(AuditLog entry);
    Task<(List<AuditLog> Items, int TotalCount)> GetPagedAsync(int budgetId, int page, int pageSize);
}

public interface IAppSettingRepository
{
    Task<string?> GetAsync(int budgetId, string key);
    Task SetAsync(int budgetId, string key, string value);
}

// ═══════════════════════════════════════════════════════════════════════════════
// IMPLEMENTATIONS
// ═══════════════════════════════════════════════════════════════════════════════

public class TransactionRepository : ITransactionRepository
{
    private readonly BudgetDbContext _db;
    public TransactionRepository(BudgetDbContext db) => _db = db;

    public async Task<(List<Transaction> Items, int TotalCount)> GetPagedAsync(TransactionQuery q)
    {
        var query = _db.Transactions
            .Include(t => t.Category)
            .Include(t => t.Project)
            .Include(t => t.KonteringRows)
            .Where(t => t.BudgetId == q.BudgetId);

        if (q.FilterByStartDate   && q.StartDate.HasValue)   query = query.Where(t => t.StartDate >= q.StartDate.Value);
        if (q.FilterByEndDate     && q.EndDate.HasValue)      query = query.Where(t => t.EndDate == null || t.EndDate <= q.EndDate.Value);
        if (q.FilterByDescription && !string.IsNullOrWhiteSpace(q.Description))
            query = query.Where(t => t.Description != null && t.Description.Contains(q.Description));
        if (q.FilterByAmount      && q.Amount.HasValue)       query = query.Where(t => t.NetAmount == q.Amount.Value);
        if (q.FilterByCategory    && q.CategoryId.HasValue)   query = query.Where(t => t.CategoryId == q.CategoryId.Value);
        if (q.FilterByRecurrence  && q.Recurrence.HasValue)   query = query.Where(t => t.Recurrence == q.Recurrence.Value);
        if (q.FilterByMonth       && q.Month.HasValue)        query = query.Where(t => t.Month == q.Month.Value);
        if (q.ProjectId.HasValue)   query = query.Where(t => t.ProjectId == q.ProjectId.Value);
        if (q.Type.HasValue)        query = query.Where(t => t.Type == q.Type.Value);
        if (q.IsActive.HasValue)    query = query.Where(t => t.IsActive == q.IsActive.Value);

        var total = await query.CountAsync();

        query = (q.SortBy?.ToLower(), q.SortDir?.ToLower()) switch
        {
            ("startdate",  "asc") => query.OrderBy(t => t.StartDate),
            ("startdate",  _)     => query.OrderByDescending(t => t.StartDate),
            ("netamount",  "asc") => query.OrderBy(t => t.NetAmount),
            ("netamount",  _)     => query.OrderByDescending(t => t.NetAmount),
            ("description","asc") => query.OrderBy(t => t.Description),
            ("description",_)     => query.OrderByDescending(t => t.Description),
            ("category",   "asc") => query.OrderBy(t => t.Category.Name),
            ("category",   _)     => query.OrderByDescending(t => t.Category.Name),
            _                     => query.OrderByDescending(t => t.StartDate)
        };

        var items = await query.Skip((q.Page - 1) * q.PageSize).Take(q.PageSize).ToListAsync();
        return (items, total);
    }

    public async Task<Transaction?> GetByIdAsync(int id, int budgetId) =>
        await _db.Transactions.Include(t => t.Category).Include(t => t.Project)
            .Include(t => t.KonteringRows)
            .FirstOrDefaultAsync(t => t.Id == id && t.BudgetId == budgetId);

    public async Task<Transaction> CreateAsync(Transaction t)
    {
        _db.Transactions.Add(t);
        await _db.SaveChangesAsync();
        return t;
    }

    public async Task<Transaction> UpdateAsync(Transaction t)
    {
        t.UpdatedAt = DateTime.UtcNow;
        _db.Transactions.Update(t);
        await _db.SaveChangesAsync();
        return t;
    }

    public async Task DeleteAsync(int id, int budgetId)
    {
        var t = await _db.Transactions.FirstOrDefaultAsync(x => x.Id == id && x.BudgetId == budgetId);
        if (t != null) { _db.Transactions.Remove(t); await _db.SaveChangesAsync(); }
    }

    public async Task DeleteBatchAsync(List<int> ids, int budgetId)
    {
        var txs = await _db.Transactions.Where(t => ids.Contains(t.Id) && t.BudgetId == budgetId).ToListAsync();
        _db.Transactions.RemoveRange(txs);
        await _db.SaveChangesAsync();
    }

    public async Task<List<Transaction>> GetForExportAsync(TransactionQuery q)
    {
        q.Page = 1; q.PageSize = int.MaxValue;
        var (items, _) = await GetPagedAsync(q);
        return items;
    }
}

public class BudgetRepository : IBudgetRepository
{
    private readonly BudgetDbContext _db;
    public BudgetRepository(BudgetDbContext db) => _db = db;

    public async Task<List<Budget>> GetForUserAsync(string userId) =>
        await _db.BudgetMemberships
            .Where(m => m.UserId == userId && m.AcceptedAt != null)
            .Include(m => m.Budget)
            .Select(m => m.Budget)
            .ToListAsync();

    public async Task<Budget?> GetByIdAsync(int id) =>
        await _db.Budgets.Include(b => b.Memberships).FirstOrDefaultAsync(b => b.Id == id);

    public async Task<Budget> CreateAsync(Budget b) { _db.Budgets.Add(b); await _db.SaveChangesAsync(); return b; }
    public async Task<Budget> UpdateAsync(Budget b) { _db.Budgets.Update(b); await _db.SaveChangesAsync(); return b; }
    public async Task DeleteAsync(int id)
    {
        var b = await _db.Budgets.FindAsync(id);
        if (b != null) { _db.Budgets.Remove(b); await _db.SaveChangesAsync(); }
    }

    public async Task<BudgetMembership?> GetMembershipAsync(int budgetId, string userId) =>
        await _db.BudgetMemberships.FirstOrDefaultAsync(m => m.BudgetId == budgetId && m.UserId == userId);

    public async Task<BudgetMembership> AddMemberAsync(BudgetMembership m)
    {
        _db.BudgetMemberships.Add(m); await _db.SaveChangesAsync(); return m;
    }

    public async Task UpdateMemberRoleAsync(int budgetId, string userId, BudgetMemberRole role)
    {
        var m = await GetMembershipAsync(budgetId, userId);
        if (m != null) { m.Role = role; await _db.SaveChangesAsync(); }
    }

    public async Task RemoveMemberAsync(int budgetId, string userId)
    {
        var m = await GetMembershipAsync(budgetId, userId);
        if (m != null) { _db.BudgetMemberships.Remove(m); await _db.SaveChangesAsync(); }
    }

    public async Task<BudgetMembership?> GetByInviteTokenAsync(string token) =>
        await _db.BudgetMemberships.Include(m => m.Budget)
            .FirstOrDefaultAsync(m => m.InviteToken == token && m.AcceptedAt == null);
}

public class CategoryRepository : ICategoryRepository
{
    private readonly BudgetDbContext _db;
    public CategoryRepository(BudgetDbContext db) => _db = db;

    public async Task<List<Category>> GetForBudgetAsync(int budgetId) =>
        await _db.Categories
            .Where(c => c.IsSystemCategory || c.BudgetId == budgetId)
            .OrderBy(c => c.Type).ThenBy(c => c.Name)
            .ToListAsync();

    public async Task<Category> CreateAsync(Category c) { _db.Categories.Add(c); await _db.SaveChangesAsync(); return c; }

    public async Task DeleteAsync(int id, int budgetId)
    {
        var c = await _db.Categories.FirstOrDefaultAsync(x => x.Id == id && x.BudgetId == budgetId && !x.IsSystemCategory);
        if (c != null) { _db.Categories.Remove(c); await _db.SaveChangesAsync(); }
    }
}

public class ProjectRepository : IProjectRepository
{
    private readonly BudgetDbContext _db;
    public ProjectRepository(BudgetDbContext db) => _db = db;

    /// <summary>
    /// Single query: projects LEFT JOIN transactions GROUP BY projectId.
    /// Replaces the old pattern that looped and issued one query per project.
    /// </summary>
    public async Task<List<(Project Project, decimal SpentAmount)>> GetForBudgetWithSpentAsync(int budgetId)
    {
        // Pull projects with their transactions in one round-trip using EF navigation.
        var projects = await _db.Projects
            .Where(p => p.BudgetId == budgetId)
            .OrderByDescending(p => p.CreatedAt)
            .Select(p => new
            {
                Project = p,
                SpentAmount = _db.Transactions
                    .Where(t => t.ProjectId == p.Id && t.BudgetId == budgetId)
                    .Sum(t => (decimal?)t.NetAmount) ?? 0m
            })
            .ToListAsync();

        return projects
            .Select(x => (x.Project, x.SpentAmount))
            .ToList();
    }

    public async Task<Project?> GetByIdAsync(int id, int budgetId) =>
        await _db.Projects.FirstOrDefaultAsync(p => p.Id == id && p.BudgetId == budgetId);

    public async Task<Project> CreateAsync(Project p) { _db.Projects.Add(p); await _db.SaveChangesAsync(); return p; }
    public async Task<Project> UpdateAsync(Project p) { _db.Projects.Update(p); await _db.SaveChangesAsync(); return p; }
    public async Task DeleteAsync(int id, int budgetId)
    {
        var p = await GetByIdAsync(id, budgetId);
        if (p != null) { _db.Projects.Remove(p); await _db.SaveChangesAsync(); }
    }
}

public class MilersattningRepository : IMilersattningRepository
{
    private readonly BudgetDbContext _db;
    public MilersattningRepository(BudgetDbContext db) => _db = db;

    public async Task<List<MilersattningEntry>> GetForBudgetAsync(int budgetId, string? userId = null)
    {
        var q = _db.MilersattningEntries.Where(e => e.BudgetId == budgetId);
        if (userId != null) q = q.Where(e => e.UserId == userId);
        return await q.OrderByDescending(e => e.TripDate).ToListAsync();
    }

    public async Task<MilersattningEntry?> GetByIdAsync(int id, int budgetId) =>
        await _db.MilersattningEntries.FirstOrDefaultAsync(e => e.Id == id && e.BudgetId == budgetId);

    public async Task<MilersattningEntry> CreateAsync(MilersattningEntry e) { _db.MilersattningEntries.Add(e); await _db.SaveChangesAsync(); return e; }
    public async Task<MilersattningEntry> UpdateAsync(MilersattningEntry e) { _db.MilersattningEntries.Update(e); await _db.SaveChangesAsync(); return e; }
    public async Task DeleteAsync(int id, int budgetId)
    {
        var e = await GetByIdAsync(id, budgetId);
        if (e != null) { _db.MilersattningEntries.Remove(e); await _db.SaveChangesAsync(); }
    }
}

public class VabRepository : IVabRepository
{
    private readonly BudgetDbContext _db;
    public VabRepository(BudgetDbContext db) => _db = db;

    public async Task<List<VabEntry>> GetForBudgetAsync(int budgetId, string? userId = null)
    {
        var q = _db.VabEntries.Where(e => e.BudgetId == budgetId);
        if (userId != null) q = q.Where(e => e.UserId == userId);
        return await q.OrderByDescending(e => e.StartDate).ToListAsync();
    }

    public async Task<VabEntry?> GetByIdAsync(int id, int budgetId) =>
        await _db.VabEntries.FirstOrDefaultAsync(e => e.Id == id && e.BudgetId == budgetId);

    public async Task<VabEntry> CreateAsync(VabEntry e) { _db.VabEntries.Add(e); await _db.SaveChangesAsync(); return e; }
    public async Task<VabEntry> UpdateAsync(VabEntry e) { _db.VabEntries.Update(e); await _db.SaveChangesAsync(); return e; }
    public async Task DeleteAsync(int id, int budgetId)
    {
        var e = await GetByIdAsync(id, budgetId);
        if (e != null) { _db.VabEntries.Remove(e); await _db.SaveChangesAsync(); }
    }
}

public class KonteringRepository : IKonteringRepository
{
    private readonly BudgetDbContext _db;
    public KonteringRepository(BudgetDbContext db) => _db = db;

    public async Task<List<KonteringRow>> GetForTransactionAsync(int transactionId) =>
        await _db.KonteringRows.Where(k => k.TransactionId == transactionId).ToListAsync();

    public async Task SaveRowsAsync(int transactionId, List<KonteringRow> rows)
    {
        var existing = await _db.KonteringRows.Where(k => k.TransactionId == transactionId).ToListAsync();
        _db.KonteringRows.RemoveRange(existing);
        foreach (var r in rows) r.TransactionId = transactionId;
        _db.KonteringRows.AddRange(rows);
        await _db.SaveChangesAsync();
    }
}

public class ReceiptRepository : IReceiptRepository
{
    private readonly BudgetDbContext _db;
    public ReceiptRepository(BudgetDbContext db) => _db = db;

    public async Task<(List<ReceiptBatch> Items, int TotalCount)> GetPagedAsync(ReceiptQuery q)
    {
        var query = _db.ReceiptBatches
            .Include(b => b.Category)
            .Include(b => b.Project)
            .Include(b => b.Lines)
            .Where(b => b.BudgetId == q.BudgetId);

        if (!string.IsNullOrWhiteSpace(q.LabelSearch)) query = query.Where(b => b.Label.Contains(q.LabelSearch));
        if (q.BatchCategoryId.HasValue)  query = query.Where(b => b.BatchCategoryId == q.BatchCategoryId.Value);
        if (q.ProjectId.HasValue)        query = query.Where(b => b.ProjectId == q.ProjectId.Value);
        if (q.CreatedByUserId != null)   query = query.Where(b => b.CreatedByUserId == q.CreatedByUserId);
        if (q.Statuses?.Any() == true)   query = query.Where(b => q.Statuses.Contains(b.Status));
        if (q.FromDate.HasValue)         query = query.Where(b => b.CreatedAt >= q.FromDate.Value);
        if (q.ToDate.HasValue)           query = query.Where(b => b.CreatedAt <= q.ToDate.Value);

        var total = await query.CountAsync();
        query = (q.SortBy?.ToLower(), q.SortDir?.ToLower()) switch
        {
            ("label",     "asc") => query.OrderBy(b => b.Label),
            ("label",     _)     => query.OrderByDescending(b => b.Label),
            ("status",    "asc") => query.OrderBy(b => b.Status),
            ("status",    _)     => query.OrderByDescending(b => b.Status),
            ("createdat", "asc") => query.OrderBy(b => b.CreatedAt),
            _                    => query.OrderByDescending(b => b.CreatedAt)
        };

        var items = await query.Skip((q.Page - 1) * q.PageSize).Take(q.PageSize).ToListAsync();
        return (items, total);
    }

    public async Task<ReceiptBatch?> GetByIdAsync(int id, int budgetId) =>
        await _db.ReceiptBatches
            .Include(b => b.Category)
            .Include(b => b.Project)
            .Include(b => b.Lines.OrderBy(l => l.SequenceNumber))
            .FirstOrDefaultAsync(b => b.Id == id && b.BudgetId == budgetId);

    public async Task<ReceiptBatch> CreateAsync(ReceiptBatch b) { _db.ReceiptBatches.Add(b); await _db.SaveChangesAsync(); return b; }
    public async Task<ReceiptBatch> UpdateAsync(ReceiptBatch b) { b.UpdatedAt = DateTime.UtcNow; _db.ReceiptBatches.Update(b); await _db.SaveChangesAsync(); return b; }
    public async Task DeleteAsync(int id, int budgetId)
    {
        var b = await _db.ReceiptBatches.FirstOrDefaultAsync(x => x.Id == id && x.BudgetId == budgetId);
        if (b != null) { _db.ReceiptBatches.Remove(b); await _db.SaveChangesAsync(); }
    }

    public async Task<ReceiptLine> AddLineAsync(ReceiptLine l) { _db.ReceiptLines.Add(l); await _db.SaveChangesAsync(); return l; }
    public async Task<ReceiptLine?> GetLineAsync(int lineId, int batchId) =>
        await _db.ReceiptLines.FirstOrDefaultAsync(l => l.Id == lineId && l.BatchId == batchId);
    public async Task<ReceiptLine> UpdateLineAsync(ReceiptLine l) { _db.ReceiptLines.Update(l); await _db.SaveChangesAsync(); return l; }
    public async Task DeleteLineAsync(int lineId, int batchId)
    {
        var l = await GetLineAsync(lineId, batchId);
        if (l != null) { _db.ReceiptLines.Remove(l); await _db.SaveChangesAsync(); }
    }

    public async Task<int> GetNextSequenceNumberAsync(int batchId)
    {
        var max = await _db.ReceiptLines.Where(l => l.BatchId == batchId).MaxAsync(l => (int?)l.SequenceNumber) ?? 0;
        return max + 1;
    }

    public async Task<List<ReceiptBatchCategory>> GetCategoriesAsync() =>
        await _db.ReceiptBatchCategories.Where(c => c.IsActive).OrderBy(c => c.SortOrder).ToListAsync();
}

public class AuditRepository : IAuditRepository
{
    private readonly BudgetDbContext _db;
    public AuditRepository(BudgetDbContext db) => _db = db;

    public async Task LogAsync(AuditLog entry) { _db.AuditLogs.Add(entry); await _db.SaveChangesAsync(); }

    public async Task<(List<AuditLog> Items, int TotalCount)> GetPagedAsync(int budgetId, int page, int pageSize)
    {
        var q = _db.AuditLogs.Where(a => a.BudgetId == budgetId).OrderByDescending(a => a.Timestamp);
        var total = await q.CountAsync();
        var items = await q.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
        return (items, total);
    }
}

public class AppSettingRepository : IAppSettingRepository
{
    private readonly BudgetDbContext _db;
    public AppSettingRepository(BudgetDbContext db) => _db = db;

    public async Task<string?> GetAsync(int budgetId, string key) =>
        (await _db.AppSettings.FirstOrDefaultAsync(s => s.BudgetId == budgetId && s.Key == key))?.Value;

    public async Task SetAsync(int budgetId, string key, string value)
    {
        var s = await _db.AppSettings.FirstOrDefaultAsync(x => x.BudgetId == budgetId && x.Key == key);
        if (s == null) _db.AppSettings.Add(new AppSetting { BudgetId = budgetId, Key = key, Value = value });
        else s.Value = value;
        await _db.SaveChangesAsync();
    }
}
