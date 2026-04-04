using System.Globalization;
using ClosedXML.Excel;
using CsvHelper;
using CsvHelper.Configuration;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using BudgetPlanner.Core.DTOs;
using BudgetPlanner.DAL.Repositories;
using BudgetPlanner.Domain.Models;
using BudgetPlanner.Domain.Enums;
using Microsoft.Extensions.Caching.Memory;

namespace BudgetPlanner.Core.Services;

public class BudgetCalculationService
{
    public SummaryDto ComputeSummary(List<JournalEntryDto> entries)
    {
        var countable = entries.Where(e =>
            e.EntryType != JournalEntryType.ReceiptBatch ||
            e.Status is "Godkänd" or "Utbetald").ToList();

        var income   = countable.Where(e => e.Amount > 0).Sum(e => e.Amount);
        var expenses = countable.Where(e => e.Amount < 0).Sum(e => e.Amount);
        var monthlyIncome   = countable.Where(e => e.EntryType == JournalEntryType.Transaction && e.Amount > 0).Sum(e => e.Amount);
        var monthlyExpenses = countable.Where(e => e.EntryType == JournalEntryType.Transaction && e.Amount < 0).Sum(e => e.Amount);

        return new SummaryDto
        {
            FilteredIncome   = income,
            FilteredExpenses = expenses,
            MonthlyIncome    = monthlyIncome,
            MonthlyExpenses  = monthlyExpenses
        };
    }
}

public class MilersattningService
{
    private readonly IMilersattningRepository _repo;
    private readonly ITransactionRepository _txRepo;
    private readonly IAppSettingRepository _settings;

    public MilersattningService(IMilersattningRepository repo, ITransactionRepository txRepo, IAppSettingRepository settings)
    { _repo = repo; _txRepo = txRepo; _settings = settings; }

    public async Task<MilersattningEntry> CreateAsync(int budgetId, string userId, CreateMilersattningDto dto)
    {
        var rate = await GetRateAsync(budgetId);
        var entry = new MilersattningEntry
        {
            BudgetId     = budgetId,
            UserId       = userId,
            TripDate     = dto.TripDate,
            FromLocation = dto.FromLocation,
            ToLocation   = dto.ToLocation,
            DistanceKm   = dto.DistanceKm,
            RatePerKm    = dto.RatePerKm > 0 ? dto.RatePerKm : rate,
            Purpose      = dto.Purpose
        };
        entry = await _repo.CreateAsync(entry);

        var tx = new Transaction
        {
            BudgetId             = budgetId,
            StartDate            = dto.TripDate,
            NetAmount            = entry.ReimbursementAmount,
            Description          = $"Milersättning: {dto.FromLocation} → {dto.ToLocation} ({dto.DistanceKm} km)",
            CategoryId           = CategoryConstants.Milersattning,
            Type                 = TransactionType.Income,
            Recurrence           = Recurrence.OneTime,
            IsActive             = true,
            CreatedByUserId      = userId,
            MilersattningEntryId = entry.Id
        };
        tx = await _txRepo.CreateAsync(tx);
        entry.LinkedTransactionId = tx.Id;
        await _repo.UpdateAsync(entry);
        return entry;
    }

    public async Task DeleteAsync(int id, int budgetId)
    {
        var entry = await _repo.GetByIdAsync(id, budgetId);
        if (entry?.LinkedTransactionId != null)
            await _txRepo.DeleteAsync(entry.LinkedTransactionId.Value, budgetId);
        await _repo.DeleteAsync(id, budgetId);
    }

    public async Task<decimal> GetRateAsync(int budgetId)
    {
        var stored = await _settings.GetAsync(budgetId, "MilersattningRate");
        return decimal.TryParse(stored, out var r) ? r : 0.25m;
    }
}

public class VabService
{
    private readonly IVabRepository _repo;
    private readonly ITransactionRepository _txRepo;

    public VabService(IVabRepository repo, ITransactionRepository txRepo)
    { _repo = repo; _txRepo = txRepo; }

    public async Task<VabEntry> CreateAsync(int budgetId, string userId, CreateVabDto dto)
    {
        var entry = new VabEntry
        {
            BudgetId     = budgetId,
            UserId       = userId,
            ChildName    = dto.ChildName,
            StartDate    = dto.StartDate,
            EndDate      = dto.EndDate,
            DailyBenefit = dto.DailyBenefit,
            Rate         = dto.Rate
        };
        entry = await _repo.CreateAsync(entry);

        var tx = new Transaction
        {
            BudgetId        = budgetId,
            StartDate       = dto.StartDate,
            EndDate         = dto.EndDate,
            NetAmount       = -entry.TotalAmount,
            Description     = string.IsNullOrWhiteSpace(dto.ChildName)
                                ? $"VAB {dto.StartDate:d}–{dto.EndDate:d} ({entry.TotalDays} dagar)"
                                : $"VAB {dto.ChildName}: {dto.StartDate:d}–{dto.EndDate:d} ({entry.TotalDays} dagar)",
            CategoryId      = CategoryConstants.VabSjukfranvaro,
            Type            = TransactionType.Expense,
            Recurrence      = Recurrence.OneTime,
            IsActive        = true,
            CreatedByUserId = userId,
            VabEntryId      = entry.Id
        };
        tx = await _txRepo.CreateAsync(tx);
        entry.LinkedTransactionId = tx.Id;
        await _repo.UpdateAsync(entry);
        return entry;
    }

    public async Task DeleteAsync(int id, int budgetId)
    {
        var entry = await _repo.GetByIdAsync(id, budgetId);
        if (entry?.LinkedTransactionId != null)
            await _txRepo.DeleteAsync(entry.LinkedTransactionId.Value, budgetId);
        await _repo.DeleteAsync(id, budgetId);
    }
}

public class ReceiptService
{
    private readonly IReceiptRepository _repo;
    private readonly ITransactionRepository _txRepo;

    public ReceiptService(IReceiptRepository repo, ITransactionRepository txRepo)
    { _repo = repo; _txRepo = txRepo; }

    public static string GenerateReferenceCode(int year, int batchId, int seq)
    {
        var bw = batchId > 999 ? "D4" : "D3";
        var sw = seq     > 999 ? "D4" : "D3";
        return $"{year}-{batchId.ToString(bw)}-{seq.ToString(sw)}";
    }

    public static void ValidateStatusTransition(
        ReceiptBatchStatus current, ReceiptBatchStatus next,
        bool isOwner, bool isCreator)
    {
        var ok = (current, next) switch
        {
            (ReceiptBatchStatus.Draft,     ReceiptBatchStatus.Submitted)  => isCreator,
            (ReceiptBatchStatus.Submitted, ReceiptBatchStatus.Draft)      => isCreator,
            (ReceiptBatchStatus.Submitted, ReceiptBatchStatus.Approved)   => isOwner,
            (ReceiptBatchStatus.Submitted, ReceiptBatchStatus.Rejected)   => isOwner,
            (ReceiptBatchStatus.Approved,  ReceiptBatchStatus.Reimbursed) => isOwner,
            (ReceiptBatchStatus.Rejected,  ReceiptBatchStatus.Draft)      => isCreator,
            _ => false
        };
        if (!ok) throw new InvalidOperationException($"Statusövergång {current} → {next} inte tillåten.");
    }

    public async Task<ReceiptBatch> CreateBatchAsync(int budgetId, string userId, string userEmail, CreateReceiptBatchDto dto)
    {
        var batch = new ReceiptBatch
        {
            BudgetId        = budgetId,
            ProjectId       = dto.ProjectId,
            Label           = dto.Label,
            BatchCategoryId = dto.BatchCategoryId,
            CreatedByUserId = userId,
            CreatedByEmail  = userEmail
        };
        return await _repo.CreateAsync(batch);
    }

    public async Task<ReceiptLine> AddLineAsync(int batchId, int budgetId, CreateReceiptLineDto dto)
    {
        var batch = await _repo.GetByIdAsync(batchId, budgetId)
            ?? throw new KeyNotFoundException("Batch hittades inte.");
        if (batch.Status != ReceiptBatchStatus.Draft)
            throw new InvalidOperationException("Kan bara lägga till kvitton i utkast.");

        var seq  = await _repo.GetNextSequenceNumberAsync(batchId);
        var code = GenerateReferenceCode(DateTime.UtcNow.Year, batchId, seq);
        var line = new ReceiptLine
        {
            BatchId        = batchId,
            SequenceNumber = seq,
            ReferenceCode  = code,
            Date           = dto.Date,
            Amount         = dto.Amount,
            Vendor         = dto.Vendor,
            Description    = dto.Description
        };
        return await _repo.AddLineAsync(line);
    }

    public async Task<ReceiptBatch> UpdateStatusAsync(
        int batchId, int budgetId, ReceiptBatchStatus newStatus,
        string actorUserId, BudgetMemberRole actorRole, string? rejectionReason = null)
    {
        var batch = await _repo.GetByIdAsync(batchId, budgetId)
            ?? throw new KeyNotFoundException("Batch hittades inte.");
        ValidateStatusTransition(batch.Status, newStatus,
            actorRole == BudgetMemberRole.Owner,
            batch.CreatedByUserId == actorUserId);

        var now = DateTime.UtcNow;
        batch.Status = newStatus;
        switch (newStatus)
        {
            case ReceiptBatchStatus.Submitted:
                batch.SubmittedAt = now;
                break;
            case ReceiptBatchStatus.Approved:
                batch.ApprovedAt = now; batch.ApprovedByUserId = actorUserId;
                await CreateLinkedTransactionsAsync(batch);
                break;
            case ReceiptBatchStatus.Rejected:
                batch.RejectedAt = now; batch.RejectedByUserId = actorUserId;
                batch.RejectionReason = rejectionReason;
                break;
            case ReceiptBatchStatus.Reimbursed:
                batch.ReimbursedAt = now;
                break;
            case ReceiptBatchStatus.Draft:
                batch.SubmittedAt = null;
                break;
        }
        return await _repo.UpdateAsync(batch);
    }

    private async Task CreateLinkedTransactionsAsync(ReceiptBatch batch)
    {
        var loaded = await _repo.GetByIdAsync(batch.Id, batch.BudgetId);
        if (loaded?.Lines == null || !loaded.Lines.Any()) return;
        foreach (var line in loaded.Lines)
        {
            var tx = new Transaction
            {
                BudgetId        = batch.BudgetId,
                ProjectId       = batch.ProjectId,
                StartDate       = line.Date,
                NetAmount       = -Math.Abs(line.Amount),
                Description     = $"Utlägg [{line.ReferenceCode}]{(line.Vendor != null ? $": {line.Vendor}" : "")}",
                CategoryId      = CategoryConstants.Transport,
                Type            = TransactionType.Expense,
                Recurrence      = Recurrence.OneTime,
                IsActive        = true,
                CreatedByUserId = batch.CreatedByUserId
            };
            tx = await _txRepo.CreateAsync(tx);
            line.LinkedTransactionId = tx.Id;
            await _repo.UpdateLineAsync(line);
        }
    }

    public async Task<List<ReceiptBatchCategory>> GetCategoriesAsync() => await _repo.GetCategoriesAsync();

    public byte[] ExportBatchToPdf(ReceiptBatch batch, string budgetName)
    {
        static IContainer HeaderCell(IContainer c) => c.Background("#263238").Padding(5);
        IContainer DataCell(IContainer c, int i) => c.Background(i % 2 == 0 ? "#FFFFFF" : "#F5F5F5").Padding(5);

        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4); page.Margin(1.5f, Unit.Centimetre);
                page.DefaultTextStyle(x => x.FontSize(9).FontFamily("Arial"));

                page.Header().Column(col =>
                {
                    col.Item().Row(r =>
                    {
                        r.RelativeItem().Text(budgetName).Bold().FontSize(14).FontColor("#1565C0");
                        r.ConstantItem(180).AlignRight().Text($"Utskrivet {DateTime.Now:yyyy-MM-dd HH:mm}").FontSize(8).FontColor("#78909C");
                    });
                    col.Item().Text($"Utläggsunderlag: {batch.Label}").Bold().FontSize(12);
                    col.Item().Row(r =>
                    {
                        r.RelativeItem().Text($"Projektkategori: {batch.Category?.Name ?? "–"}").FontSize(8).FontColor("#78909C");
                        r.ConstantItem(120).AlignRight().Text($"Status: {SwedishStatus(batch.Status)}").FontSize(8).FontColor("#78909C");
                    });
                    col.Item().PaddingTop(4).LineHorizontal(1).LineColor("#E0E0E0");
                });

                page.Content().PaddingTop(8).Table(table =>
                {
                    table.ColumnsDefinition(cols =>
                    {
                        cols.ConstantColumn(22); cols.ConstantColumn(80); cols.ConstantColumn(62);
                        cols.RelativeColumn(2); cols.RelativeColumn(3); cols.ConstantColumn(55); cols.ConstantColumn(65);
                    });
                    table.Header(h =>
                    {
                        foreach (var t in new[] { "#", "Referenskod", "Datum", "Leverantör", "Beskrivning", "Valuta", "Belopp" })
                            h.Cell().Element(HeaderCell).Text(t).Bold().FontColor("#FFFFFF");
                    });
                    var lines = batch.Lines.OrderBy(l => l.SequenceNumber).ToList();
                    for (var i = 0; i < lines.Count; i++)
                    {
                        var l = lines[i]; var idx = i;
                        IContainer C(IContainer c) => DataCell(c, idx);
                        table.Cell().Element(C).Text(l.SequenceNumber.ToString());
                        table.Cell().Element(C).Text(l.ReferenceCode).Bold().FontFamily("Courier New").FontSize(8);
                        table.Cell().Element(C).Text(l.Date.ToString("yyyy-MM-dd"));
                        table.Cell().Element(C).Text(l.Vendor ?? "–");
                        table.Cell().Element(C).Text(l.Description ?? "–");
                        table.Cell().Element(C).AlignRight().Text(l.Currency);
                        table.Cell().Element(C).AlignRight().Text(l.Amount.ToString("N2"));
                    }
                });

                page.Footer().Column(col =>
                {
                    col.Item().LineHorizontal(1).LineColor("#E0E0E0");
                    col.Item().PaddingTop(4).Row(r =>
                    {
                        r.RelativeItem().Text($"Totalt: {batch.Lines.Count} kvitton · {batch.Lines.Sum(l => l.Amount):N2} SEK").Bold().FontSize(9);
                        r.ConstantItem(200).AlignRight()
                            .Text("Kvittona med ovanstående koder förvaras separat.").FontSize(7).FontColor("#78909C").Italic();
                    });
                    col.Item().PaddingTop(10).Row(r =>
                    {
                        r.RelativeItem().Column(c =>
                        {
                            c.Item().Text("Godkänd av:").FontSize(8).FontColor("#78909C");
                            c.Item().PaddingTop(14).LineHorizontal(0.5f).LineColor("#B0BEC5");
                            c.Item().PaddingTop(2).Text("Underskrift").FontSize(7).FontColor("#B0BEC5");
                        });
                        r.ConstantItem(24);
                        r.RelativeItem().Column(c =>
                        {
                            c.Item().Text("Datum:").FontSize(8).FontColor("#78909C");
                            c.Item().PaddingTop(14).LineHorizontal(0.5f).LineColor("#B0BEC5");
                            c.Item().PaddingTop(2).Text("ÅÅÅÅ-MM-DD").FontSize(7).FontColor("#B0BEC5");
                        });
                    });
                });
            });
        }).GeneratePdf();
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

public interface IReceiptAttachmentService
{
    Task<string?> UploadAsync(int lineId, Stream data, string fileName, string mimeType);
    Task<string?> GetUrlAsync(int lineId);
    Task DeleteAsync(int lineId);
}

public class NoOpReceiptAttachmentService : IReceiptAttachmentService
{
    public Task<string?> UploadAsync(int l, Stream d, string f, string m) => Task.FromResult<string?>(null);
    public Task<string?> GetUrlAsync(int lineId)                          => Task.FromResult<string?>(null);
    public Task DeleteAsync(int lineId)                                   => Task.CompletedTask;
}

// ═══════════════════════════════════════════════════════════════════════════════
// JOURNAL QUERY SERVICE
// FIX: QueryAsync now returns a 3-tuple (Items, TotalCount, Summary).
// Summary is computed on the FULL filtered+sorted list BEFORE pagination so
// the income/expense/net totals in the page header reflect all matching data,
// not just the current page.
// ═══════════════════════════════════════════════════════════════════════════════
public class JournalQueryService
{
    private readonly ITransactionRepository _txRepo;
    private readonly IMilersattningRepository _miRepo;
    private readonly IVabRepository _vabRepo;
    private readonly IReceiptRepository _receiptRepo;

    public JournalQueryService(ITransactionRepository txRepo, IMilersattningRepository miRepo,
        IVabRepository vabRepo, IReceiptRepository receiptRepo)
    { _txRepo = txRepo; _miRepo = miRepo; _vabRepo = vabRepo; _receiptRepo = receiptRepo; }

    // Returns (pagedItems, totalCount, summaryOverAllItems)
    public async Task<(List<JournalEntryDto> Items, int TotalCount, SummaryDto Summary)> QueryAsync(JournalQuery q)
    {
        var include = q.IncludeTypes.Any()
            ? q.IncludeTypes.ToHashSet()
            : new HashSet<JournalEntryType>(Enum.GetValues<JournalEntryType>());

        var tasks = new List<Task<List<JournalEntryDto>>>();
        if (include.Contains(JournalEntryType.Transaction))   tasks.Add(FetchTransactionsAsync(q));
        if (include.Contains(JournalEntryType.Milersattning)) tasks.Add(FetchMilersattningAsync(q));
        if (include.Contains(JournalEntryType.Vab))           tasks.Add(FetchVabAsync(q));
        if (include.Contains(JournalEntryType.ReceiptBatch))  tasks.Add(FetchReceiptsAsync(q));

        var all = (await Task.WhenAll(tasks)).SelectMany(r => r).ToList();
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
        // Summary computed here — on ALL filtered items before the .Skip/.Take below.
        var summary = ComputeSummary(all);
        var paged   = all.Skip((q.Page - 1) * q.PageSize).Take(q.PageSize).ToList();

        return (paged, total, summary);
    }

    public SummaryDto ComputeSummary(List<JournalEntryDto> entries)
    {
        var countable = entries.Where(e =>
            e.EntryType != JournalEntryType.ReceiptBatch ||
            e.Status is "Godkänd" or "Utbetald").ToList();

        return new SummaryDto
        {
            FilteredIncome   = countable.Where(e => e.Amount > 0).Sum(e => e.Amount),
            FilteredExpenses = countable.Where(e => e.Amount < 0).Sum(e => e.Amount),
            MonthlyIncome    = countable.Where(e => e.EntryType == JournalEntryType.Transaction && e.Amount > 0).Sum(e => e.Amount),
            MonthlyExpenses  = countable.Where(e => e.EntryType == JournalEntryType.Transaction && e.Amount < 0).Sum(e => e.Amount),
        };
    }

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
            EntryType     = JournalEntryType.Transaction,
            TypeLabel     = "Transaktion",
            TypeCode      = "T",
            Date          = t.StartDate,
            EndDate       = t.EndDate,
            Amount        = t.NetAmount,
            Description   = t.Description,
            CategoryName  = t.Category?.Name,
            ProjectName   = t.Project?.Name,
            SourceId      = t.Id,
            HasDetail     = t.HasKontering,
            CreatedByEmail = t.CreatedByUserId,
            KonteringRows = t.KonteringRows.Select(k => new KonteringRowDto
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
        var items = await _miRepo.GetForBudgetAsync(q.BudgetId, q.FilterByCreatedBy ? q.CreatedByUserId : null);
        return items.Select(m => new JournalEntryDto
        {
            EntryType     = JournalEntryType.Milersattning,
            TypeLabel     = "Milersättning",
            TypeCode      = "M",
            Date          = m.TripDate,
            Amount        = m.ReimbursementAmount,
            Description   = $"{m.FromLocation} → {m.ToLocation}",
            MetaLine      = $"{m.DistanceKm:N0} km · {m.RatePerKm:N2} kr/km",
            SourceId      = m.Id,
            HasDetail     = false,
            CreatedByEmail = m.UserId
        }).ToList();
    }

    private async Task<List<JournalEntryDto>> FetchVabAsync(JournalQuery q)
    {
        var items = await _vabRepo.GetForBudgetAsync(q.BudgetId, q.FilterByCreatedBy ? q.CreatedByUserId : null);
        return items.Select(v => new JournalEntryDto
        {
            EntryType     = JournalEntryType.Vab,
            TypeLabel     = "VAB",
            TypeCode      = "V",
            Date          = v.StartDate,
            EndDate       = v.EndDate,
            Amount        = -v.TotalAmount,
            Description   = v.ChildName != null ? $"VAB · {v.ChildName}" : "VAB",
            MetaLine      = $"{v.TotalDays} dagar · {v.StartDate:d}–{v.EndDate:d}",
            SourceId      = v.Id,
            HasDetail     = false,
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
            ProjectId       = q.FilterByProject ? q.ProjectId : null,
            Statuses        = q.ReceiptStatuses.Any() ? q.ReceiptStatuses : null,
            CreatedByUserId = q.FilterByCreatedBy ? q.CreatedByUserId : null,
            FromDate        = q.FilterByStartDate ? q.StartDate : null,
            ToDate          = q.FilterByEndDate   ? q.EndDate   : null
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
            HasDetail        = b.Lines.Any(),
            ReceiptLineCount = b.Lines.Count,
            MetaLine         = $"{b.Lines.Count} kvitton",
            CreatedByEmail   = b.CreatedByEmail
        }).ToList();
    }

    private static List<JournalEntryDto> ApplySharedFilters(List<JournalEntryDto> all, JournalQuery q)
    {
        if (q.FilterByDescription && !string.IsNullOrWhiteSpace(q.Description))
            all = all.Where(e => e.Description?.Contains(q.Description, StringComparison.OrdinalIgnoreCase) == true).ToList();
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

// ─── CSV BANK PROFILES ────────────────────────────────────────────────────────
public abstract class BankCsvProfile
{
    public abstract string BankName          { get; }
    public abstract char   Delimiter         { get; }
    public abstract int    SkipRows          { get; }
    public abstract string DateColumn        { get; }
    public abstract string AmountColumn      { get; }
    public abstract string DescriptionColumn { get; }
    public abstract string DateFormat        { get; }
}

public class SebProfile : BankCsvProfile
{
    public override string BankName          => "SEB";
    public override char   Delimiter         => ';';
    public override int    SkipRows          => 1;
    public override string DateColumn        => "Bokföringsdag";
    public override string AmountColumn      => "Belopp";
    public override string DescriptionColumn => "Beskrivning";
    public override string DateFormat        => "yyyy-MM-dd";
}

public class SwedbankProfile : BankCsvProfile
{
    public override string BankName          => "Swedbank";
    public override char   Delimiter         => ';';
    public override int    SkipRows          => 4;
    public override string DateColumn        => "Datum";
    public override string AmountColumn      => "Belopp";
    public override string DescriptionColumn => "Text";
    public override string DateFormat        => "yyyy-MM-dd";
}

public class HandelsbankenProfile : BankCsvProfile
{
    public override string BankName          => "Handelsbanken";
    public override char   Delimiter         => '\t';
    public override int    SkipRows          => 1;
    public override string DateColumn        => "Transaktionsdatum";
    public override string AmountColumn      => "Belopp";
    public override string DescriptionColumn => "Transaktionstext";
    public override string DateFormat        => "dd/MM/yyyy";
}
