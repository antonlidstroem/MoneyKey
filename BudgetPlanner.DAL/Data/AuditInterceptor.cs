using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using BudgetPlanner.Domain.Enums;
using DomainModels = BudgetPlanner.Domain.Models;

namespace BudgetPlanner.DAL.Data;

public interface ICurrentUserAccessor
{
    string? UserId    { get; }
    string? UserEmail { get; }
}

/// <summary>
/// Automatically captures all CRUD changes and writes AuditLog rows.
/// Zero manual instrumentation needed — every EF Core save is covered.
/// </summary>
public class AuditInterceptor : SaveChangesInterceptor
{
    private readonly ICurrentUserAccessor _user;
    public AuditInterceptor(ICurrentUserAccessor user) => _user = user;

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData data, InterceptionResult<int> result)
    { AddAuditEntries(data.Context); return base.SavingChanges(data, result); }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData data, InterceptionResult<int> result, CancellationToken ct = default)
    { AddAuditEntries(data.Context); return base.SavingChangesAsync(data, result, ct); }

    private void AddAuditEntries(DbContext? ctx)
    {
        if (ctx == null) return;
        foreach (var entry in ctx.ChangeTracker.Entries())
        {
            if (!IsAuditable(entry.Entity)) continue;
            if (entry.State is EntityState.Unchanged or EntityState.Detached) continue;

            var action = entry.State switch
            {
                EntityState.Added    => AuditAction.Created,
                EntityState.Modified => AuditAction.Updated,
                EntityState.Deleted  => AuditAction.Deleted,
                _                    => AuditAction.Updated
            };

            var budgetId = GetBudgetId(entry.Entity);
            if (budgetId == 0) continue;

            string? oldVals = null, newVals = null;
            if (entry.State == EntityState.Modified)
            {
                oldVals = JsonSerializer.Serialize(entry.Properties.Where(p => p.IsModified).ToDictionary(p => p.Metadata.Name, p => p.OriginalValue));
                newVals = JsonSerializer.Serialize(entry.Properties.Where(p => p.IsModified).ToDictionary(p => p.Metadata.Name, p => p.CurrentValue));
            }
            else if (entry.State == EntityState.Added)
                newVals = JsonSerializer.Serialize(entry.Properties.ToDictionary(p => p.Metadata.Name, p => p.CurrentValue));
            else
                oldVals = JsonSerializer.Serialize(entry.Properties.ToDictionary(p => p.Metadata.Name, p => p.CurrentValue));

            var key = entry.Metadata.FindPrimaryKey()?.Properties
                .Select(p => entry.Property(p.Name).CurrentValue?.ToString())
                .FirstOrDefault() ?? "0";

            ctx.Set<DomainModels.AuditLog>().Add(new DomainModels.AuditLog
            {
                BudgetId   = budgetId,
                UserId     = _user.UserId,
                UserEmail  = _user.UserEmail,
                EntityName = entry.Entity.GetType().Name,
                EntityId   = key,
                Action     = action,
                OldValues  = oldVals,
                NewValues  = newVals,
                Timestamp  = DateTime.UtcNow
            });
        }
    }

    private static bool IsAuditable(object e) => e is
        DomainModels.Transaction      or DomainModels.Project       or
        DomainModels.KonteringRow     or DomainModels.MilersattningEntry or
        DomainModels.VabEntry         or DomainModels.ReceiptBatch  or
        DomainModels.ReceiptLine      or DomainModels.Budget        or
        DomainModels.BudgetMembership;

    private static int GetBudgetId(object e) => e switch
    {
        DomainModels.Transaction t        => t.BudgetId,
        DomainModels.Project p            => p.BudgetId,
        DomainModels.MilersattningEntry m => m.BudgetId,
        DomainModels.VabEntry v           => v.BudgetId,
        DomainModels.ReceiptBatch rb      => rb.BudgetId,
        DomainModels.Budget b             => b.Id,
        DomainModels.BudgetMembership bm  => bm.BudgetId,
        _                                 => 0
    };
}
