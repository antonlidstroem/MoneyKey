# JournalQueryService patch — Phase 3

## Problem
Date filters were applied twice:
1. Inside `FetchTransactionsAsync` — pushed to EF/SQL (correct)
2. Inside `ApplySharedFilters` — applied again in-memory on ALL entry types

This was redundant for transactions and could produce subtly different results
for milersattning/VAB entries (which don't pre-filter by date in their fetch
methods but DO get filtered by `ApplySharedFilters`).

## Fix
`ApplySharedFilters` is the authoritative in-memory filter for ALL entry types.
`FetchTransactionsAsync` no longer passes date filters to EF — it lets
`ApplySharedFilters` handle them uniformly.

## How to apply
In `BudgetPlanner.Core/Services/Services.cs`, find `FetchTransactionsAsync`
inside `JournalQueryService` and replace the `tq` construction with:

```csharp
private async Task<List<JournalEntryDto>> FetchTransactionsAsync(JournalQuery q)
{
    var tq = new TransactionQuery
    {
        BudgetId         = q.BudgetId,
        Page             = 1,
        PageSize         = int.MaxValue,
        // FIX: date filtering is handled uniformly by ApplySharedFilters
        // for all entry types. Do NOT pre-filter by date here — it would
        // filter transactions differently from milersattning/vab.
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
```

No other methods need changing — `ApplySharedFilters` already handles date
filtering correctly for all entry types.
