// ═══════════════════════════════════════════════════════════════════════════════
// BudgetPlanner.Core/Services/JournalQueryService.cs
//
// REPLACE the entire contents of this file (if it already exists) OR
// SAVE as a new file at BudgetPlanner.Core/Services/JournalQueryService.cs
//
// Root-cause of all the CS0246 errors in document 16:
//   The file was missing ALL using directives so the compiler could not resolve
//   ITransactionRepository, JournalEntryDto, JournalQuery, ReceiptBatchStatus, etc.
//
// This file also adds ITaskListRepository as the 5th constructor parameter
// (Phase 3 journal integration) and keeps the sequential-await pattern required
// by EF Core's single-scoped DbContext rule.
// ═══════════════════════════════════════════════════════════════════════════════
using BudgetPlanner.Core.DTOs;          // JournalEntryDto, JournalQuery, SummaryDto,
                                         // KonteringRowDto, TransactionQuery, ReceiptQuery
using BudgetPlanner.DAL.Repositories;  // ITransactionRepository, IMilersattningRepository,
                                         // IVabRepository, IReceiptRepository, ITaskListRepository
using BudgetPlanner.Domain.Enums;      // JournalEntryType, ReceiptBatchStatus

namespace BudgetPlanner.Core.Services;

public class JournalQueryService
{
    private readonly ITransactionRepository   _txRepo;
    private readonly IMilersattningRepository _miRepo;
    private readonly IVabRepository           _vabRepo;
    private readonly IReceiptRepository       _receiptRepo;
    private readonly ITaskListRepository      _taskListRepo;

    public JournalQueryService(
        ITransactionRepository   txRepo,
        IMilersattningRepository miRepo,
        IVabRepository           vabRepo,
        IReceiptRepository       receiptRepo,
        ITaskListRepository      taskListRepo)   // 5th param — Phase 3
    {
        _txRepo       = txRepo;
        _miRepo       = miRepo;
        _vabRepo      = vabRepo;
        _receiptRepo  = receiptRepo;
        _taskListRepo = taskListRepo;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Returns (pagedItems, totalCount, summaryOverAllItems).
    // Summary is computed on ALL filtered items BEFORE pagination so the
    // KPI strip in the UI always shows totals for the whole dataset, not
    // just the visible page.
    //
    // IMPORTANT: all fetches are sequential awaits — never Task.WhenAll.
    // All repositories share one scoped BudgetDbContext; parallel async
    // operations on it throw "A second operation was started on this context".
    // ─────────────────────────────────────────────────────────────────────────
    public async Task<(List<JournalEntryDto> Items, int TotalCount, SummaryDto Summary)>
        QueryAsync(JournalQuery q)
    {
        var include = q.IncludeTypes.Count > 0
            ? q.IncludeTypes.ToHashSet()
            : new HashSet<JournalEntryType>(Enum.GetValues<JournalEntryType>());

        var all = new List<JournalEntryDto>();

        if (include.Contains(JournalEntryType.Transaction))
            all.AddRange(await FetchTransactionsAsync(q));

        if (include.Contains(JournalEntryType.Milersattning))
            all.AddRange(await FetchMilersattningAsync(q));

        if (include.Contains(JournalEntryType.Vab))
            all.AddRange(await FetchVabAsync(q));

        if (include.Contains(JournalEntryType.ReceiptBatch))
            all.AddRange(await FetchReceiptsAsync(q));

        if (include.Contains(JournalEntryType.TaskList))
            all.AddRange(await FetchTaskListsAsync(q));

        all = ApplySharedFilters(all, q);

        all = (q.SortBy?.ToLower(), q.SortDir?.ToLower()) switch
        {
            ("date",        "asc") => all.OrderBy(e => e.Date).ToList(),
            ("date",        _)     => all.OrderByDescending(e => e.Date).ToList(),
            ("amount",      "asc") => all.OrderBy(e => e.Amount).ToList(),
            ("amount",      _)     => all.OrderByDescending(e => e.Amount).ToList(),
            ("description", "asc") => all.OrderBy(e => e.Description).ToList(),
            ("description", _)     => all.OrderByDescending(e => e.Description).ToList(),
            ("type",        "asc") => all.OrderBy(e => e.TypeLabel).ThenByDescending(e => e.Date).ToList(),
            ("type",        _)     => all.OrderByDescending(e => e.TypeLabel).ThenByDescending(e => e.Date).ToList(),
            _                      => all.OrderByDescending(e => e.Date).ToList()
        };

        var total   = all.Count;
        var summary = ComputeSummary(all);
        var paged   = all.Skip((q.Page - 1) * q.PageSize).Take(q.PageSize).ToList();

        return (paged, total, summary);
    }

    public SummaryDto ComputeSummary(List<JournalEntryDto> entries)
    {
        // TaskLists have Amount = 0 and must not appear as income or expense.
        var countable = entries.Where(e =>
            (e.EntryType != JournalEntryType.ReceiptBatch &&
             e.EntryType != JournalEntryType.TaskList) ||
            e.Status is "Godkänd" or "Utbetald").ToList();

        return new SummaryDto
        {
            FilteredIncome   = countable.Where(e => e.Amount > 0).Sum(e => e.Amount),
            FilteredExpenses = countable.Where(e => e.Amount < 0).Sum(e => e.Amount),
            MonthlyIncome    = countable
                .Where(e => e.EntryType == JournalEntryType.Transaction && e.Amount > 0)
                .Sum(e => e.Amount),
            MonthlyExpenses  = countable
                .Where(e => e.EntryType == JournalEntryType.Transaction && e.Amount < 0)
                .Sum(e => e.Amount),
        };
    }

    // ── Private fetchers ──────────────────────────────────────────────────────

    private async Task<List<JournalEntryDto>> FetchTransactionsAsync(JournalQuery q)
    {
        var tq = new TransactionQuery
        {
            BudgetId         = q.BudgetId,
            Page             = 1,
            PageSize         = int.MaxValue,
            FilterByCategory = q.FilterByCategory,
            CategoryId       = q.CategoryId,
            ProjectId        = q.FilterByProject ? q.ProjectId : null
        };
        var (txs, _) = await _txRepo.GetPagedAsync(tq);
        return txs.Select(t => new JournalEntryDto
        {
            EntryType      = JournalEntryType.Transaction,
            TypeLabel      = "Transaktion",
            TypeCode       = "T",
            Date           = t.StartDate,
            EndDate        = t.EndDate,
            Amount         = t.NetAmount,
            Description    = t.Description,
            CategoryName   = t.Category?.Name,
            ProjectName    = t.Project?.Name,
            SourceId       = t.Id,
            HasDetail      = t.HasKontering,
            CreatedByEmail = t.CreatedByUserId,
            KonteringRows  = t.KonteringRows.Select(k => new KonteringRowDto
            {
                Id          = k.Id,
                KontoNr     = k.KontoNr,
                CostCenter  = k.CostCenter,
                Amount      = k.Amount,
                Percentage  = k.Percentage,
                Description = k.Description
            }).ToList()
        }).ToList();
    }

    private async Task<List<JournalEntryDto>> FetchMilersattningAsync(JournalQuery q)
    {
        var items = await _miRepo.GetForBudgetAsync(
            q.BudgetId, q.FilterByCreatedBy ? q.CreatedByUserId : null);
        return items.Select(m => new JournalEntryDto
        {
            EntryType      = JournalEntryType.Milersattning,
            TypeLabel      = "Milersättning",
            TypeCode       = "M",
            Date           = m.TripDate,
            Amount         = m.ReimbursementAmount,
            Description    = $"{m.FromLocation} → {m.ToLocation}",
            MetaLine       = $"{m.DistanceKm:N0} km · {m.RatePerKm:N2} kr/km",
            SourceId       = m.Id,
            HasDetail      = false,
            CreatedByEmail = m.UserId
        }).ToList();
    }

    private async Task<List<JournalEntryDto>> FetchVabAsync(JournalQuery q)
    {
        var items = await _vabRepo.GetForBudgetAsync(
            q.BudgetId, q.FilterByCreatedBy ? q.CreatedByUserId : null);
        return items.Select(v => new JournalEntryDto
        {
            EntryType      = JournalEntryType.Vab,
            TypeLabel      = "VAB",
            TypeCode       = "V",
            Date           = v.StartDate,
            EndDate        = v.EndDate,
            Amount         = -v.TotalAmount,
            Description    = v.ChildName != null ? $"VAB · {v.ChildName}" : "VAB",
            MetaLine       = $"{v.TotalDays} dagar · {v.StartDate:d}–{v.EndDate:d}",
            SourceId       = v.Id,
            HasDetail      = false,
            CreatedByEmail = v.UserId
        }).ToList();
    }

    private async Task<List<JournalEntryDto>> FetchReceiptsAsync(JournalQuery q)
    {
        var rq = new ReceiptQuery
        {
            BudgetId        = q.BudgetId,
            Page            = 1,
            PageSize        = int.MaxValue,
            ProjectId       = q.FilterByProject   ? q.ProjectId      : null,
            Statuses        = q.ReceiptStatuses.Count > 0 ? q.ReceiptStatuses : null,
            CreatedByUserId = q.FilterByCreatedBy  ? q.CreatedByUserId : null,
            FromDate        = q.FilterByStartDate  ? q.StartDate      : null,
            ToDate          = q.FilterByEndDate    ? q.EndDate        : null
        };
        var (batches, _) = await _receiptRepo.GetPagedAsync(rq);
        return batches.Select(b => new JournalEntryDto
        {
            EntryType        = JournalEntryType.ReceiptBatch,
            TypeLabel        = "Kvitto",
            TypeCode         = "K",
            Date             = b.CreatedAt,
            Amount           = b.Lines.Sum(l => l.Amount),
            Description      = b.Label,
            CategoryName     = b.Category?.Name,
            ProjectName      = b.Project?.Name,
            Status           = SwedishStatus(b.Status),
            ReferenceCode    = $"{DateTime.UtcNow.Year}-{b.Id:D3}-*",
            SourceId         = b.Id,
            HasDetail        = b.Lines.Count > 0,
            ReceiptLineCount = b.Lines.Count,
            MetaLine         = $"{b.Lines.Count} kvitton",
            CreatedByEmail   = b.CreatedByEmail
        }).ToList();
    }

    private async Task<List<JournalEntryDto>> FetchTaskListsAsync(JournalQuery q)
    {
        var lists = await _taskListRepo.GetForBudgetAsync(q.BudgetId, includeArchived: false);
        return lists.Select(l =>
        {
            var total    = l.Items.Count;
            var checked_ = l.Items.Count(i => i.IsChecked);
            return new JournalEntryDto
            {
                EntryType      = JournalEntryType.TaskList,
                TypeLabel      = "Lista",
                TypeCode       = "L",
                Date           = l.UpdatedAt ?? l.CreatedAt,
                Amount         = 0m,
                Description    = $"{l.Emoji} {l.Title}",
                MetaLine       = total > 0 ? $"{checked_}/{total} klara" : "Inga uppgifter",
                Status         = total > 0 && checked_ == total ? "Klar" : "Aktiv",
                SourceId       = l.Id,
                HasDetail      = total > 0,
                CreatedByEmail = l.CreatedByUserId
            };
        }).ToList();
    }

    private static List<JournalEntryDto> ApplySharedFilters(
        List<JournalEntryDto> all, JournalQuery q)
    {
        if (q.FilterByDescription && !string.IsNullOrWhiteSpace(q.Description))
            all = all.Where(e => e.Description?.Contains(
                q.Description, StringComparison.OrdinalIgnoreCase) == true).ToList();

        if (q.FilterByAmount)
        {
            if (q.AmountMin.HasValue) all = all.Where(e => e.Amount >= q.AmountMin.Value).ToList();
            if (q.AmountMax.HasValue) all = all.Where(e => e.Amount <= q.AmountMax.Value).ToList();
        }

        if (q.FilterByStartDate && q.StartDate.HasValue)
            all = all.Where(e => e.Date >= q.StartDate.Value).ToList();

        if (q.FilterByEndDate && q.EndDate.HasValue)
            all = all.Where(e => e.Date <= q.EndDate.Value).ToList();

        return all;
    }

    private static string SwedishStatus(ReceiptBatchStatus s) => s switch
    {
        ReceiptBatchStatus.Draft      => "Utkast",
        ReceiptBatchStatus.Submitted  => "Inskickad",
        ReceiptBatchStatus.Approved   => "Godkänd",
        ReceiptBatchStatus.Rejected   => "Avslagen",
        ReceiptBatchStatus.Reimbursed => "Utbetald",
        _                             => s.ToString()
    };
}
