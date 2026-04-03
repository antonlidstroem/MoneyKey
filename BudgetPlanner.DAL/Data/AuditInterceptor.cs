using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
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
/// PHASE 2 FIX: KonteringRow now resolves BudgetId via its parent Transaction
/// from the change tracker instead of returning 0 (which caused the row to be
/// skipped silently).
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

        // Materialise before iterating — prevents "collection modified" error
        // when we add AuditLog rows to the same context below.
        var entries = ctx.ChangeTracker.Entries().ToList();

        foreach (var entry in entries)
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

            // FIX: KonteringRow has no BudgetId — resolve from the parent
            // Transaction that is already tracked in the same SaveChanges batch.
            var budgetId = GetBudgetId(entry, entries);

            // Allow Budget creation (budgetId == 0 at INSERT time) but skip
            // all other entity types that still resolve to 0.
            if (budgetId == 0 && entry.Entity is not DomainModels.Budget) continue;

            string? oldVals = null, newVals = null;

            if (entry.State == EntityState.Modified)
            {
                var modProps = entry.Properties.Where(p => p.IsModified)
                                   .ToDictionary(p => p.Metadata.Name, p => p.OriginalValue);
                var curProps = entry.Properties.Where(p => p.IsModified)
                                   .ToDictionary(p => p.Metadata.Name, p => p.CurrentValue);
                oldVals = JsonSerializer.Serialize(modProps);
                newVals = JsonSerializer.Serialize(curProps);
            }
            else if (entry.State == EntityState.Added)
            {
                newVals = JsonSerializer.Serialize(
                    entry.Properties.ToDictionary(p => p.Metadata.Name, p => p.CurrentValue));
            }
            else
            {
                oldVals = JsonSerializer.Serialize(
                    entry.Properties.ToDictionary(p => p.Metadata.Name, p => p.OriginalValue));
            }

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
        DomainModels.Transaction       or DomainModels.Project         or
        DomainModels.KonteringRow      or DomainModels.MilersattningEntry or
        DomainModels.VabEntry          or DomainModels.ReceiptBatch    or
        DomainModels.ReceiptLine       or DomainModels.Budget          or
        DomainModels.BudgetMembership;

    /// <summary>
    /// Returns the BudgetId for the entity being audited.
    /// For KonteringRow: looks up the parent Transaction from the change tracker
    /// or the database to get its BudgetId.
    /// </summary>
    private static int GetBudgetId(EntityEntry entry, IReadOnlyList<EntityEntry> allEntries)
    {
        return entry.Entity switch
        {
            DomainModels.Transaction t        => t.BudgetId,
            DomainModels.Project p            => p.BudgetId,
            DomainModels.MilersattningEntry m => m.BudgetId,
            DomainModels.VabEntry v           => v.BudgetId,
            DomainModels.ReceiptBatch rb      => rb.BudgetId,
            DomainModels.Budget b             => b.Id,
            DomainModels.BudgetMembership bm  => bm.BudgetId,

            // FIX: KonteringRow — find the parent Transaction in the tracker
            DomainModels.KonteringRow k =>
                ResolveKonteringBudgetId(k, allEntries),

            _ => 0
        };
    }

    private static int ResolveKonteringBudgetId(
        DomainModels.KonteringRow row,
        IReadOnlyList<EntityEntry> allEntries)
    {
        // Try to find the parent Transaction already tracked in this batch.
        var parentEntry = allEntries.FirstOrDefault(e =>
            e.Entity is DomainModels.Transaction t && t.Id == row.TransactionId);

        if (parentEntry?.Entity is DomainModels.Transaction tx)
            return tx.BudgetId;

        // Navigation property may already be loaded.
        if (row.Transaction != null)
            return row.Transaction.BudgetId;

        // Cannot resolve — skip audit for this row (better than crashing).
        return 0;
    }
}
