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

        var income = countable.Where(e => e.Amount > 0).Sum(e => e.Amount);
        var expenses = countable.Where(e => e.Amount < 0).Sum(e => e.Amount);

        var monthlyIncome = countable.Where(e => e.EntryType == JournalEntryType.Transaction && e.Amount > 0).Sum(e => e.Amount);
        var monthlyExpenses = countable.Where(e => e.EntryType == JournalEntryType.Transaction && e.Amount < 0).Sum(e => e.Amount);

        return new SummaryDto
        {
            FilteredIncome = income,
            FilteredExpenses = expenses,
            MonthlyIncome = monthlyIncome,
            MonthlyExpenses = monthlyExpenses
        };
    }
}

public class MilersattningService
{
    private readonly IMilersattningRepository _repo;
    private readonly ITransactionRepository _txRepo;
    private readonly IAppSettingRepository _settings;

    public MilersattningService(
        IMilersattningRepository repo,
        ITransactionRepository txRepo,
        IAppSettingRepository settings)
    { _repo = repo; _txRepo = txRepo; _settings = settings; }

    public async Task<MilersattningEntry> CreateAsync(int budgetId, string userId, CreateMilersattningDto dto)
    {
        var rate = await GetRateAsync(budgetId);
        var entry = new MilersattningEntry
        {
            BudgetId = budgetId,
            UserId = userId,
            TripDate = dto.TripDate,
            FromLocation = dto.FromLocation,
            ToLocation = dto.ToLocation,
            DistanceKm = dto.DistanceKm,
            RatePerKm = dto.RatePerKm > 0 ? dto.RatePerKm : rate,
            Purpose = dto.Purpose
        };
        entry = await _repo.CreateAsync(entry);

        var tx = new Transaction
        {
            BudgetId = budgetId,
            StartDate = dto.TripDate,
            NetAmount = entry.ReimbursementAmount,
            Description = $"Milersättning: {dto.FromLocation} → {dto.ToLocation} ({dto.DistanceKm} km)",
            // FIX: named constant replaces magic number 12
            CategoryId = CategoryConstants.Milersattning,
            Type = TransactionType.Income,
            Recurrence = Recurrence.OneTime,
            IsActive = true,
            CreatedByUserId = userId,
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
            BudgetId = budgetId,
            UserId = userId,
            ChildName = dto.ChildName,
            StartDate = dto.StartDate,
            EndDate = dto.EndDate,
            DailyBenefit = dto.DailyBenefit,
            Rate = dto.Rate
        };
        entry = await _repo.CreateAsync(entry);

        var tx = new Transaction
        {
            BudgetId = budgetId,
            StartDate = dto.StartDate,
            EndDate = dto.EndDate,
            NetAmount = -entry.TotalAmount,
            Description = string.IsNullOrWhiteSpace(dto.ChildName)
                ? $"VAB {dto.StartDate:d}–{dto.EndDate:d} ({entry.TotalDays} dagar)"
                : $"VAB {dto.ChildName}: {dto.StartDate:d}–{dto.EndDate:d} ({entry.TotalDays} dagar)",
            // FIX: named constant replaces magic number 11
            CategoryId = CategoryConstants.VabSjukfranvaro,
            Type = TransactionType.Expense,
            Recurrence = Recurrence.OneTime,
            IsActive = true,
            CreatedByUserId = userId,
            VabEntryId = entry.Id
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
        var sw = seq > 999 ? "D4" : "D3";
        return $"{year}-{batchId.ToString(bw)}-{seq.ToString(sw)}";
    }

    public static void ValidateStatusTransition(
        ReceiptBatchStatus current, ReceiptBatchStatus next,
        bool isOwner, bool isCreator)
    {
        var ok = (current, next) switch
        {
            (ReceiptBatchStatus.Draft, ReceiptBatchStatus.Submitted) => isCreator,
            (ReceiptBatchStatus.Submitted, ReceiptBatchStatus.Draft) => isCreator,
            (ReceiptBatchStatus.Submitted, ReceiptBatchStatus.Approved) => isOwner,
            (ReceiptBatchStatus.Submitted, ReceiptBatchStatus.Rejected) => isOwner,
            (ReceiptBatchStatus.Approved, ReceiptBatchStatus.Reimbursed) => isOwner,
            (ReceiptBatchStatus.Rejected, ReceiptBatchStatus.Draft) => isCreator,
            _ => false
        };
        if (!ok) throw new InvalidOperationException($"Statusövergång {current} → {next} inte tillåten.");
    }

    public async Task<ReceiptBatch> CreateBatchAsync(int budgetId, string userId, string userEmail, CreateReceiptBatchDto dto)
    {
        var batch = new ReceiptBatch
        {
            BudgetId = budgetId,
            ProjectId = dto.ProjectId,
            Label = dto.Label,
            BatchCategoryId = dto.BatchCategoryId,
            CreatedByUserId = userId,
            CreatedByEmail = userEmail
        };
        return await _repo.CreateAsync(batch);
    }

    public async Task<ReceiptLine> AddLineAsync(int batchId, int budgetId, CreateReceiptLineDto dto)
    {
        var batch = await _repo.GetByIdAsync(batchId, budgetId)
            ?? throw new KeyNotFoundException("Batch hittades inte.");
        if (batch.Status != ReceiptBatchStatus.Draft)
            throw new InvalidOperationException("Kan bara lägga till kvitton i utkast.");

        var seq = await _repo.GetNextSequenceNumberAsync(batchId);
        var code = GenerateReferenceCode(DateTime.UtcNow.Year, batchId, seq);
        var line = new ReceiptLine
        {
            BatchId = batchId,
            SequenceNumber = seq,
            ReferenceCode = code,
            Date = dto.Date,
            Amount = dto.Amount,
            Vendor = dto.Vendor,
            Description = dto.Description
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
            case ReceiptBatchStatus.Submitted: batch.SubmittedAt = now; break;
            case ReceiptBatchStatus.Approved:
                batch.ApprovedAt = now; batch.ApprovedByUserId = actorUserId;
                await CreateLinkedTransactionsAsync(batch);
                break;
            case ReceiptBatchStatus.Rejected:
                batch.RejectedAt = now; batch.RejectedByUserId = actorUserId;
                batch.RejectionReason = rejectionReason;
                break;
            case ReceiptBatchStatus.Reimbursed: batch.ReimbursedAt = now; break;
            case ReceiptBatchStatus.Draft: batch.SubmittedAt = null; break;
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
                BudgetId = batch.BudgetId,
                ProjectId = batch.ProjectId,
                StartDate = line.Date,
                NetAmount = -Math.Abs(line.Amount),
                Description = $"Utlägg [{line.ReferenceCode}]{(line.Vendor != null ? $": {line.Vendor}" : "")}",
                CategoryId = CategoryConstants.Transport,
                Type = TransactionType.Expense,
                Recurrence = Recurrence.OneTime,
                IsActive = true,
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
        ReceiptBatchStatus.Draft => "Utkast",
        ReceiptBatchStatus.Submitted => "Inskickad",
        ReceiptBatchStatus.Approved => "Godkänd",
        ReceiptBatchStatus.Rejected => "Avslagen",
        ReceiptBatchStatus.Reimbursed => "Utbetald",
        _ => s.ToString()
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
    public Task<string?> GetUrlAsync(int lineId) => Task.FromResult<string?>(null);
    public Task DeleteAsync(int lineId) => Task.CompletedTask;
}

public class JournalQueryService
{
    private readonly ITransactionRepository _txRepo;
    private readonly IMilersattningRepository _miRepo;
    private readonly IVabRepository _vabRepo;
    private readonly IReceiptRepository _receiptRepo;

    public JournalQueryService(ITransactionRepository txRepo, IMilersattningRepository miRepo,
        IVabRepository vabRepo, IReceiptRepository receiptRepo)
    { _txRepo = txRepo; _miRepo = miRepo; _vabRepo = vabRepo; _receiptRepo = receiptRepo; }

    public async Task<(List<JournalEntryDto> Items, int TotalCount)> QueryAsync(JournalQuery q)
    {
        var include = q.IncludeTypes.Any()
            ? q.IncludeTypes.ToHashSet()
            : new HashSet<JournalEntryType>(Enum.GetValues<JournalEntryType>());

        var tasks = new List<Task<List<JournalEntryDto>>>();
        if (include.Contains(JournalEntryType.Transaction)) tasks.Add(FetchTransactionsAsync(q));
        if (include.Contains(JournalEntryType.Milersattning)) tasks.Add(FetchMilersattningAsync(q));
        if (include.Contains(JournalEntryType.Vab)) tasks.Add(FetchVabAsync(q));
        if (include.Contains(JournalEntryType.ReceiptBatch)) tasks.Add(FetchReceiptsAsync(q));

        var all = (await Task.WhenAll(tasks)).SelectMany(r => r).ToList();
        all = ApplySharedFilters(all, q);

        all = (q.SortBy?.ToLower(), q.SortDir?.ToLower()) switch
        {
            ("date", "asc") => all.OrderBy(e => e.Date).ToList(),
            ("date", _) => all.OrderByDescending(e => e.Date).ToList(),
            ("amount", "asc") => all.OrderBy(e => e.Amount).ToList(),
            ("amount", _) => all.OrderByDescending(e => e.Amount).ToList(),
            ("description", "asc") => all.OrderBy(e => e.Description).ToList(),
            ("description", _) => all.OrderByDescending(e => e.Description).ToList(),
            ("type", "asc") => all.OrderBy(e => e.TypeLabel).ThenByDescending(e => e.Date).ToList(),
            ("type", _) => all.OrderByDescending(e => e.TypeLabel).ThenByDescending(e => e.Date).ToList(),
            _ => all.OrderByDescending(e => e.Date).ToList()
        };

        var total = all.Count;
        return (all.Skip((q.Page - 1) * q.PageSize).Take(q.PageSize).ToList(), total);
    }

    public SummaryDto ComputeSummary(List<JournalEntryDto> entries)
    {
        var countable = entries.Where(e =>
            e.EntryType != JournalEntryType.ReceiptBatch ||
            e.Status is "Godkänd" or "Utbetald").ToList();

        return new SummaryDto
        {
            FilteredIncome = countable.Where(e => e.Amount > 0).Sum(e => e.Amount),
            FilteredExpenses = countable.Where(e => e.Amount < 0).Sum(e => e.Amount),
            MonthlyIncome = countable.Where(e => e.EntryType == JournalEntryType.Transaction && e.Amount > 0).Sum(e => e.Amount),
            MonthlyExpenses = countable.Where(e => e.EntryType == JournalEntryType.Transaction && e.Amount < 0).Sum(e => e.Amount),
        };
    }

    private async Task<List<JournalEntryDto>> FetchTransactionsAsync(JournalQuery q)
    {
        var tq = new TransactionQuery
        {
            BudgetId = q.BudgetId,
            Page = 1,
            PageSize = int.MaxValue,
            // FIX: date filtering is handled uniformly by ApplySharedFilters
            // for all entry types. Do NOT pre-filter by date here — it would
            // filter transactions differently from milersattning/vab.
            FilterByCategory = q.FilterByCategory,
            CategoryId = q.CategoryId,
            ProjectId = q.FilterByProject ? q.ProjectId : null
        };
        var (txs, _) = await _txRepo.GetPagedAsync(tq);
        return txs.Select(t => new JournalEntryDto
        {
            EntryType = JournalEntryType.Transaction,
            TypeLabel = "Transaktion",
            TypeCode = "T",
            Date = t.StartDate,
            EndDate = t.EndDate,
            Amount = t.NetAmount,
            Description = t.Description,
            CategoryName = t.Category?.Name,
            ProjectName = t.Project?.Name,
            SourceId = t.Id,
            HasDetail = t.HasKontering,
            CreatedByEmail = t.CreatedByUserId,
            KonteringRows = t.KonteringRows.Select(k => new KonteringRowDto
            {
                Id = k.Id,
                KontoNr = k.KontoNr,
                CostCenter = k.CostCenter,
                Amount = k.Amount,
                Percentage = k.Percentage,
                Description = k.Description
            }).ToList()
        }).ToList();
    }

    private async Task<List<JournalEntryDto>> FetchMilersattningAsync(JournalQuery q)
    {
        var items = await _miRepo.GetForBudgetAsync(q.BudgetId, q.FilterByCreatedBy ? q.CreatedByUserId : null);
        return items.Select(m => new JournalEntryDto
        {
            EntryType = JournalEntryType.Milersattning,
            TypeLabel = "Milersättning",
            TypeCode = "M",
            Date = m.TripDate,
            Amount = m.ReimbursementAmount,
            Description = $"{m.FromLocation} → {m.ToLocation}",
            MetaLine = $"{m.DistanceKm:N0} km · {m.RatePerKm:N2} kr/km",
            SourceId = m.Id,
            HasDetail = false,
            CreatedByEmail = m.UserId
        }).ToList();
    }

    private async Task<List<JournalEntryDto>> FetchVabAsync(JournalQuery q)
    {
        var items = await _vabRepo.GetForBudgetAsync(q.BudgetId, q.FilterByCreatedBy ? q.CreatedByUserId : null);
        return items.Select(v => new JournalEntryDto
        {
            EntryType = JournalEntryType.Vab,
            TypeLabel = "VAB",
            TypeCode = "V",
            Date = v.StartDate,
            EndDate = v.EndDate,
            Amount = -v.TotalAmount,
            Description = v.ChildName != null ? $"VAB · {v.ChildName}" : "VAB",
            MetaLine = $"{v.TotalDays} dagar · {v.StartDate:d}–{v.EndDate:d}",
            SourceId = v.Id,
            HasDetail = false,
            CreatedByEmail = v.UserId
        }).ToList();
    }

    private async Task<List<JournalEntryDto>> FetchReceiptsAsync(JournalQuery q)
    {
        var rq = new ReceiptQuery
        {
            BudgetId = q.BudgetId,
            Page = 1,
            PageSize = int.MaxValue,
            ProjectId = q.FilterByProject ? q.ProjectId : null,
            Statuses = q.ReceiptStatuses.Any() ? q.ReceiptStatuses : null,
            CreatedByUserId = q.FilterByCreatedBy ? q.CreatedByUserId : null,
            FromDate = q.FilterByStartDate ? q.StartDate : null,
            ToDate = q.FilterByEndDate ? q.EndDate : null
        };
        var (batches, _) = await _receiptRepo.GetPagedAsync(rq);
        return batches.Select(b => new JournalEntryDto
        {
            EntryType = JournalEntryType.ReceiptBatch,
            TypeLabel = "Kvitto",
            TypeCode = "K",
            Date = b.CreatedAt,
            Amount = b.Lines.Sum(l => l.Amount),
            Description = b.Label,
            CategoryName = b.Category?.Name,
            ProjectName = b.Project?.Name,
            Status = SwedishStatus(b.Status),
            ReferenceCode = $"{DateTime.UtcNow.Year}-{b.Id:D3}-*",
            SourceId = b.Id,
            HasDetail = b.Lines.Any(),
            ReceiptLineCount = b.Lines.Count,
            MetaLine = $"{b.Lines.Count} kvitton",
            CreatedByEmail = b.CreatedByEmail
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
        ReceiptBatchStatus.Draft => "Utkast",
        ReceiptBatchStatus.Submitted => "Inskickad",
        ReceiptBatchStatus.Approved => "Godkänd",
        ReceiptBatchStatus.Rejected => "Avslagen",
        ReceiptBatchStatus.Reimbursed => "Utbetald",
        _ => s.ToString()
    };
}

public class ExportService
{
    static ExportService() => QuestPDF.Settings.License = LicenseType.Community;

    public byte[] ExportToPdf(List<TransactionDto> transactions, string budgetName)
    {
        static IContainer H(IContainer c) => c.Background("#263238").Padding(4);
        IContainer D(IContainer c, int i) => c.Background(i % 2 == 0 ? "#FFFFFF" : "#F5F5F5").Padding(4);

        return Document.Create(cont =>
        {
            cont.Page(page =>
            {
                page.Size(PageSizes.A4.Landscape()); page.Margin(1.5f, Unit.Centimetre);
                page.DefaultTextStyle(x => x.FontSize(9).FontFamily("Arial"));

                page.Header().Column(col =>
                {
                    col.Item().Row(r =>
                    {
                        r.RelativeItem().Text(budgetName).Bold().FontSize(16).FontColor("#1565C0");
                        r.ConstantItem(200).AlignRight().Text($"Exporterat {DateTime.Now:yyyy-MM-dd HH:mm}").FontSize(8).FontColor("#78909C");
                    });
                    col.Item().PaddingTop(4).LineHorizontal(1).LineColor("#E0E0E0");
                });

                page.Content().PaddingTop(8).Table(table =>
                {
                    table.ColumnsDefinition(cols =>
                    {
                        cols.ConstantColumn(70); cols.RelativeColumn(3); cols.RelativeColumn(2);
                        cols.ConstantColumn(80); cols.ConstantColumn(70); cols.ConstantColumn(80);
                    });
                    table.Header(h =>
                    {
                        foreach (var t in new[] { "Datum", "Beskrivning", "Kategori", "Återkommande", "Brutto", "Belopp" })
                            h.Cell().Element(H).Text(t).Bold().FontColor("#FFFFFF");
                    });
                    for (var i = 0; i < transactions.Count; i++)
                    {
                        var tx = transactions[i]; var idx = i;
                        IContainer C(IContainer c) => D(c, idx);
                        table.Cell().Element(C).Text(tx.StartDate.ToString("yyyy-MM-dd"));
                        table.Cell().Element(C).Text(tx.Description ?? "");
                        table.Cell().Element(C).Text(tx.CategoryName);
                        table.Cell().Element(C).Text(tx.Recurrence.ToString());
                        table.Cell().Element(C).AlignRight().Text(tx.GrossAmount?.ToString("N2") ?? "");
                        table.Cell().Element(C).AlignRight().Text(tx.NetAmount.ToString("N2"))
                            .FontColor(tx.NetAmount >= 0 ? "#2E7D32" : "#C62828");
                    }
                });

                page.Footer().Row(r =>
                {
                    var inc = transactions.Where(t => t.NetAmount > 0).Sum(t => t.NetAmount);
                    var exp = transactions.Where(t => t.NetAmount < 0).Sum(t => t.NetAmount);
                    r.RelativeItem().Text($"{transactions.Count} poster | Inkomster: {inc:N2} kr | Utgifter: {exp:N2} kr | Netto: {inc + exp:N2} kr").FontSize(8);

                    r.ConstantItem(60)
                     .AlignRight()
                     .DefaultTextStyle(x => x.FontSize(8))
                     .Text(x => {
                         x.Span("Sida ");
                         x.CurrentPageNumber();
                         x.Span(" av ");
                         x.TotalPages();
                     });
                });
            });
        }).GeneratePdf();
    }

    public byte[] ExportToExcel(List<TransactionDto> transactions, List<ProjectDto> projects, string budgetName)
    {
        using var wb = new XLWorkbook();

        var ws = wb.AddWorksheet("Transaktioner");
        var hdrs = new[] { "Datum", "Slut", "Beskrivning", "Kategori", "Typ", "Återkommande", "Månad", "%", "Brutto", "Belopp", "Projekt", "Kontering" };
        for (var i = 0; i < hdrs.Length; i++)
        {
            var cell = ws.Cell(1, i + 1);
            cell.Value = hdrs[i]; cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#263238");
            cell.Style.Font.FontColor = XLColor.White;
        }
        for (var i = 0; i < transactions.Count; i++)
        {
            var tx = transactions[i]; var row = i + 2;
            ws.Cell(row, 1).Value = tx.StartDate.ToString("yyyy-MM-dd");
            ws.Cell(row, 2).Value = tx.EndDate?.ToString("yyyy-MM-dd") ?? "";
            ws.Cell(row, 3).Value = tx.Description ?? "";
            ws.Cell(row, 4).Value = tx.CategoryName;
            ws.Cell(row, 5).Value = tx.Type.ToString();
            ws.Cell(row, 6).Value = tx.Recurrence.ToString();
            ws.Cell(row, 7).Value = tx.Month?.ToString() ?? "";
            ws.Cell(row, 8).Value = (double?)tx.Rate ?? 0;
            ws.Cell(row, 9).Value = (double?)tx.GrossAmount ?? 0;
            ws.Cell(row, 10).Value = (double)tx.NetAmount;
            ws.Cell(row, 11).Value = tx.ProjectName ?? "";
            ws.Cell(row, 12).Value = tx.HasKontering ? "Ja" : "";
            ws.Cell(row, 10).Style.Font.FontColor = tx.NetAmount >= 0 ? XLColor.FromHtml("#2E7D32") : XLColor.FromHtml("#C62828");
        }
        ws.Columns().AdjustToContents(); ws.SheetView.FreezeRows(1);

        var ws2 = wb.AddWorksheet("Månadssammanfattning");
        ws2.Cell(1, 1).Value = "Månad"; ws2.Cell(1, 2).Value = "Inkomster"; ws2.Cell(1, 3).Value = "Utgifter"; ws2.Cell(1, 4).Value = "Netto";
        ws2.Row(1).Style.Font.Bold = true;
        ws2.Row(1).Style.Fill.BackgroundColor = XLColor.FromHtml("#1976D2");
        ws2.Row(1).Style.Font.FontColor = XLColor.White;
        var monthly = transactions.Where(t => t.Recurrence != Recurrence.OneTime)
            .GroupBy(t => t.StartDate.ToString("yyyy-MM")).OrderBy(g => g.Key);
        var rw2 = 2;
        foreach (var g in monthly)
        {
            ws2.Cell(rw2, 1).Value = g.Key;
            ws2.Cell(rw2, 2).Value = (double)g.Where(t => t.NetAmount > 0).Sum(t => t.NetAmount);
            ws2.Cell(rw2, 3).Value = (double)g.Where(t => t.NetAmount < 0).Sum(t => t.NetAmount);
            ws2.Cell(rw2, 4).Value = (double)g.Sum(t => t.NetAmount);
            rw2++;
        }
        ws2.Columns().AdjustToContents();

        var ws3 = wb.AddWorksheet("Projekt");
        ws3.Cell(1, 1).Value = "Projekt"; ws3.Cell(1, 2).Value = "Budget"; ws3.Cell(1, 3).Value = "Spenderat"; ws3.Cell(1, 4).Value = "Återstår"; ws3.Cell(1, 5).Value = "Progress %";
        ws3.Row(1).Style.Font.Bold = true;
        ws3.Row(1).Style.Fill.BackgroundColor = XLColor.FromHtml("#1976D2");
        ws3.Row(1).Style.Font.FontColor = XLColor.White;
        for (var i = 0; i < projects.Count; i++)
        {
            var p = projects[i];
            ws3.Cell(i + 2, 1).Value = (double)p.BudgetAmount; ws3.Cell(i + 2, 2).Value = p.Name;
            ws3.Cell(i + 2, 3).Value = (double)p.SpentAmount; ws3.Cell(i + 2, 4).Value = (double)p.RemainingAmount;
            ws3.Cell(i + 2, 5).Value = p.ProgressPercent;
        }
        ws3.Columns().AdjustToContents();

        var ws4 = wb.AddWorksheet("Kontering");
        ws4.Cell(1, 1).Value = "Transaktion"; ws4.Cell(1, 2).Value = "Konto Nr"; ws4.Cell(1, 3).Value = "Kostnadsst."; ws4.Cell(1, 4).Value = "Belopp"; ws4.Cell(1, 5).Value = "%"; ws4.Cell(1, 6).Value = "Beskrivning";
        ws4.Row(1).Style.Font.Bold = true;
        ws4.Row(1).Style.Fill.BackgroundColor = XLColor.FromHtml("#1976D2");
        ws4.Row(1).Style.Font.FontColor = XLColor.White;
        var kr = 2;
        foreach (var tx in transactions.Where(t => t.HasKontering))
            foreach (var k in tx.KonteringRows)
            {
                ws4.Cell(kr, 1).Value = $"{tx.StartDate:yyyy-MM-dd} {tx.Description}";
                ws4.Cell(kr, 2).Value = k.KontoNr; ws4.Cell(kr, 3).Value = k.CostCenter ?? "";
                ws4.Cell(kr, 4).Value = (double)k.Amount; ws4.Cell(kr, 5).Value = (double?)k.Percentage ?? 0;
                ws4.Cell(kr, 6).Value = k.Description ?? ""; kr++;
            }
        ws4.Columns().AdjustToContents();

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }
}

public abstract class BankCsvProfile
{
    public abstract string BankName { get; }
    public abstract char Delimiter { get; }
    public abstract int SkipRows { get; }
    public abstract string DateColumn { get; }
    public abstract string AmountColumn { get; }
    public abstract string DescriptionColumn { get; }
    public abstract string DateFormat { get; }
}

public class SebProfile : BankCsvProfile { public override string BankName => "SEB"; public override char Delimiter => ';'; public override int SkipRows => 1; public override string DateColumn => "Bokföringsdag"; public override string AmountColumn => "Belopp"; public override string DescriptionColumn => "Beskrivning"; public override string DateFormat => "yyyy-MM-dd"; }
public class SwedbankProfile : BankCsvProfile { public override string BankName => "Swedbank"; public override char Delimiter => ';'; public override int SkipRows => 4; public override string DateColumn => "Datum"; public override string AmountColumn => "Belopp"; public override string DescriptionColumn => "Text"; public override string DateFormat => "yyyy-MM-dd"; }
public class HandelsbankenProfile : BankCsvProfile { public override string BankName => "Handelsbanken"; public override char Delimiter => '\t'; public override int SkipRows => 1; public override string DateColumn => "Transaktionsdatum"; public override string AmountColumn => "Belopp"; public override string DescriptionColumn => "Transaktionstext"; public override string DateFormat => "dd/MM/yyyy"; }

// --- HÄR BÖRJAR FIXEN ---
public class BankImportService
{
    private readonly ITransactionRepository _txRepo;
    private readonly IMemoryCache _sessions;

    public BankImportService(ITransactionRepository txRepo, IMemoryCache sessions)
    {
        _txRepo = txRepo;
        _sessions = sessions;
    }

    public async Task<int> ConfirmAsync(ConfirmImportDto dto, int budgetId, string userId)
    {
        // Förutsätter att _sessions är IMemoryCache. 
        // Om det är en Dictionary, ändra deklarationen ovan.
        if (!_sessions.TryGetValue(dto.SessionId, out List<ImportRowDto>? allRows) || allRows == null)
            throw new InvalidOperationException("Import-sessionen har gått ut. Ladda upp filen igen.");

        var selected = allRows.Where(r => dto.SelectedRowIndices.Contains(r.RowIndex)).ToList();
        foreach (var r in selected)
        {
            await _txRepo.CreateAsync(new Transaction
            {
                BudgetId = budgetId,
                StartDate = r.Date,
                NetAmount = r.Amount,
                Description = r.Description,
                CategoryId = r.SuggestedCategoryId ?? dto.DefaultCategoryId,
                Type = r.Amount >= 0 ? TransactionType.Income : TransactionType.Expense,
                Recurrence = Recurrence.OneTime,
                IsActive = true,
                CreatedByUserId = userId
            });
        }
        _sessions.Remove(dto.SessionId);
        return selected.Count;
    }

    public List<ImportRowDto> ProcessFile(Stream stream, BankCsvProfile profile)
    {
        var rows = ParseCsv(stream, profile);
        foreach (var r in rows)
        {
            r.SuggestedCategoryId = SuggestCategory(r.Description);
            r.CategoryName = GetCategoryName(r.SuggestedCategoryId);
        }
        return rows;
    }

    private static List<ImportRowDto> ParseCsv(Stream stream, BankCsvProfile profile)
    {
        var rows = new List<ImportRowDto>();
        var config = new CsvConfiguration(new CultureInfo("sv-SE"))
        {
            Delimiter = profile.Delimiter.ToString(),
            HasHeaderRecord = true,
            BadDataFound = null,
            MissingFieldFound = null
        };

        using var reader = new StreamReader(stream);
        // Skippa rader enligt profil
        for (var i = 0; i < profile.SkipRows - 1; i++) reader.ReadLine();

        using var csv = new CsvReader(reader, config);
        var idx = 0;
        while (csv.Read())
        {
            try
            {
                var dateStr = csv.GetField(profile.DateColumn) ?? "";
                var amountStr = (csv.GetField(profile.AmountColumn) ?? "")
                    .Replace(" ", "").Replace(",", ".").Replace("\u00a0", "");
                var desc = csv.GetField(profile.DescriptionColumn)?.Trim();

                if (!DateTime.TryParseExact(dateStr, profile.DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) continue;
                if (!decimal.TryParse(amountStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var amount)) continue;

                rows.Add(new ImportRowDto { RowIndex = idx++, Date = date, Amount = amount, Description = desc, Selected = true });
            }
            catch { /* Logga fel vid behov */ }
        }
        return rows;
    }

    private static int? SuggestCategory(string? d)
    {
        if (string.IsNullOrWhiteSpace(d)) return null;
        d = d.ToLowerInvariant();
        if (d.Contains("ica") || d.Contains("coop") || d.Contains("willys") || d.Contains("lidl") || d.Contains("mat")) return 1;
        if (d.Contains("el ") || d.Contains("hyra") || d.Contains("försäkring") || d.Contains("bredband")) return 2;
        if (d.Contains("sl ") || d.Contains("tåg") || d.Contains("parkering") || d.Contains("bensin") || d.Contains("taxi")) return 3;
        if (d.Contains("netflix") || d.Contains("spotify") || d.Contains("hbo") || d.Contains("disney")) return 6;
        if (d.Contains("lön") || d.Contains("salary")) return 8;
        return null;
    }

    private static string? GetCategoryName(int? id) => id switch
    {
        1 => "Mat",
        2 => "Hus & drift",
        3 => "Transport",
        6 => "Streaming-tjänster",
        8 => "Lön",
        _ => null
    };
}