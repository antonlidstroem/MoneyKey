// ════════════════════════════════════════════════════════════════════════════
// BudgetPlanner.Core/Services/ImportService.cs
//
// PHASE 1 FIX: sessions are now stored in IMemoryCache, keyed per-user.
// - Cross-user session access is impossible.
// - Sessions expire automatically after 30 minutes.
// - No static mutable shared state.
//
// Replace the ImportService class in BudgetPlanner.Core/Services/Services.cs
// with this class, and add the following using to the top of that file:
//   using Microsoft.Extensions.Caching.Memory;
// ════════════════════════════════════════════════════════════════════════════
using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;
using Microsoft.Extensions.Caching.Memory;
using BudgetPlanner.Core.DTOs;
using BudgetPlanner.DAL.Repositories;
using BudgetPlanner.Domain.Models;
using BudgetPlanner.Domain.Enums;

namespace BudgetPlanner.Core.Services;

public class ImportService
{
    private readonly ITransactionRepository _txRepo;
    private readonly IMemoryCache _cache;

    public ImportService(ITransactionRepository txRepo, IMemoryCache cache)
    { _txRepo = txRepo; _cache = cache; }

    public static List<BankCsvProfile> GetProfiles() =>
        new() { new SebProfile(), new SwedbankProfile(), new HandelsbankenProfile() };

    /// <param name="userId">Used to scope the cache key — prevents cross-user access.</param>
    public async Task<ImportSessionDto> PreviewAsync(
        Stream fileStream, string bankProfile, int budgetId, string userId)
    {
        var profile = GetProfiles().FirstOrDefault(p => p.BankName == bankProfile) ?? new SebProfile();
        var rows    = ParseCsv(fileStream, profile);

        var existing = await _txRepo.GetForExportAsync(new TransactionQuery
        {
            BudgetId         = budgetId,
            FilterByStartDate = true,
            StartDate        = rows.Any() ? rows.Min(r => r.Date).AddDays(-1) : DateTime.Today.AddMonths(-6)
        });

        foreach (var row in rows)
        {
            row.IsDuplicate = existing.Any(t =>
                t.StartDate.Date == row.Date.Date &&
                Math.Abs(t.NetAmount - row.Amount) < 0.01m &&
                string.Equals(t.Description?.Trim(), row.Description?.Trim(), StringComparison.OrdinalIgnoreCase));
            row.SuggestedCategoryId   = SuggestCategory(row.Description);
            row.SuggestedCategoryName = GetCategoryName(row.SuggestedCategoryId);
        }

        var sessionId = Guid.NewGuid().ToString("N");
        _cache.Set(
            BuildKey(userId, sessionId),
            rows,
            new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(30)
            });

        return new ImportSessionDto(
            sessionId,
            new ImportPreviewDto
            {
                Rows           = rows,
                TotalRows      = rows.Count,
                DuplicateCount = rows.Count(r => r.IsDuplicate)
            });
    }

    public async Task<int> ConfirmAsync(ConfirmImportDto dto, int budgetId, string userId)
    {
        if (!_cache.TryGetValue(BuildKey(userId, dto.SessionId), out List<ImportRowDto>? allRows)
            || allRows == null)
        {
            throw new InvalidOperationException(
                "Import-sessionen har gått ut eller tillhör inte dig. Ladda upp filen igen.");
        }

        var selected = allRows.Where(r => dto.SelectedRowIndices.Contains(r.RowIndex)).ToList();

        foreach (var r in selected)
        {
            await _txRepo.CreateAsync(new Transaction
            {
                BudgetId        = budgetId,
                StartDate       = r.Date,
                NetAmount       = r.Amount,
                Description     = r.Description,
                CategoryId      = r.SuggestedCategoryId ?? dto.DefaultCategoryId,
                Type            = r.Amount >= 0 ? TransactionType.Income : TransactionType.Expense,
                Recurrence      = Recurrence.OneTime,
                IsActive        = true,
                CreatedByUserId = userId
            });
        }

        _cache.Remove(BuildKey(userId, dto.SessionId));
        return selected.Count;
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static string BuildKey(string userId, string sessionId) =>
        $"import_session:{userId}:{sessionId}";

    private static List<ImportRowDto> ParseCsv(Stream stream, BankCsvProfile profile)
    {
        var rows   = new List<ImportRowDto>();
        var config = new CsvConfiguration(new CultureInfo("sv-SE"))
        {
            Delimiter       = profile.Delimiter.ToString(),
            HasHeaderRecord = true,
            BadDataFound    = null,
            MissingFieldFound = null
        };
        using var reader = new StreamReader(stream);
        for (var i = 0; i < profile.SkipRows - 1; i++) reader.ReadLine();
        using var csv = new CsvReader(reader, config);
        var idx = 0;
        while (csv.Read())
        {
            try
            {
                var dateStr   = csv.GetField(profile.DateColumn) ?? "";
                var amountStr = (csv.GetField(profile.AmountColumn) ?? "")
                                .Replace(" ", "").Replace(",", ".").Replace("\u00a0", "");
                var desc = csv.GetField(profile.DescriptionColumn)?.Trim();

                if (!DateTime.TryParseExact(dateStr, profile.DateFormat,
                        CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) continue;
                if (!decimal.TryParse(amountStr, NumberStyles.Any,
                        CultureInfo.InvariantCulture, out var amount)) continue;

                rows.Add(new ImportRowDto
                {
                    RowIndex    = idx++,
                    Date        = date,
                    Amount      = amount,
                    Description = desc,
                    Selected    = true
                });
            }
            catch { /* skip malformed rows */ }
        }
        return rows;
    }

    private static int? SuggestCategory(string? d)
    {
        if (string.IsNullOrWhiteSpace(d)) return null;
        d = d.ToLowerInvariant();
        if (d.Contains("ica") || d.Contains("coop") || d.Contains("willys") || d.Contains("lidl") || d.Contains("mat")) return 1;
        if (d.Contains("el ")   || d.Contains("hyra") || d.Contains("försäkring") || d.Contains("bredband")) return 2;
        if (d.Contains("sl ")   || d.Contains("tåg")  || d.Contains("parkering")  || d.Contains("bensin") || d.Contains("taxi")) return 3;
        if (d.Contains("netflix") || d.Contains("spotify") || d.Contains("hbo") || d.Contains("disney")) return 6;
        if (d.Contains("lön") || d.Contains("salary")) return 8;
        return null;
    }

    private static string? GetCategoryName(int? id) => id switch
    {
        1 => "Mat", 2 => "Hus & drift", 3 => "Transport",
        6 => "Streaming-tjänster", 8 => "Lön", _ => null
    };
}
