// ════════════════════════════════════════════════════════════════════════════
// PHASE 4 FIX: Replace MilersattningService and VabService in Services.cs
// with these versions. Magic number category IDs replaced with CategoryConstants.
// ════════════════════════════════════════════════════════════════════════════
using BudgetPlanner.Core.DTOs;
using BudgetPlanner.DAL.Repositories;
using BudgetPlanner.Domain.Enums;
using BudgetPlanner.Domain.Models;

namespace BudgetPlanner.Core.Services;

public class MilersattningService
{
    private readonly IMilersattningRepository _repo;
    private readonly ITransactionRepository   _txRepo;
    private readonly IAppSettingRepository    _settings;

    public MilersattningService(
        IMilersattningRepository repo,
        ITransactionRepository   txRepo,
        IAppSettingRepository    settings)
    { _repo = repo; _txRepo = txRepo; _settings = settings; }

    public async Task<MilersattningEntry> CreateAsync(int budgetId, string userId, CreateMilersattningDto dto)
    {
        var rate  = await GetRateAsync(budgetId);
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
            BudgetId        = budgetId,
            StartDate       = dto.TripDate,
            NetAmount       = entry.ReimbursementAmount,
            Description     = $"Milersättning: {dto.FromLocation} → {dto.ToLocation} ({dto.DistanceKm} km)",
            // FIX: named constant replaces magic number 12
            CategoryId      = CategoryConstants.Milersattning,
            Type            = TransactionType.Income,
            Recurrence      = Recurrence.OneTime,
            IsActive        = true,
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
    private readonly IVabRepository          _repo;
    private readonly ITransactionRepository  _txRepo;

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
            // FIX: named constant replaces magic number 11
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
