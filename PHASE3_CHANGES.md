# Phase 3 – Performance & UX Fixes

## Files changed

| File | Fix |
|------|-----|
| `BudgetPlanner.API/Controllers/Controllers.cs` | ProjectsController.GetAll: N+1 → single JOIN query via ProjectRepository |
| `BudgetPlanner.DAL/Repositories/Repositories.cs` | IProjectRepository + ProjectRepository: GetForBudgetWithSpentAsync added |
| `BudgetPlanner.Blazor/Pages/Receipts/ReceiptsPage.razor` | Status filter passed server-side; pagination added |
| `BudgetPlanner.Blazor/Components/Mobile/MobileJournalList.razor` | async void handlers replaced; LoadMore appends correctly |
| `BudgetPlanner.Core/Services/Services.cs` | JournalQueryService: double date-filter removed for transactions |

## No migration required.

## Fixes in detail

### 1. ProjectsController N+1 query  (was O(N) DB round-trips)
Previously fetched each project's transactions with a separate query per project.
With 50 projects = 51 round-trips.

Fix: new `GetForBudgetWithSpentAsync` on `IProjectRepository` uses a single
LEFT JOIN with SUM(NetAmount) grouping — one round-trip regardless of project count.

### 2. ReceiptsPage client-side filter
`_sf` status filter was applied after fetching the first 50 rows — silently
incomplete beyond page 1 and wrong counts shown.

Fix: `Statuses` list passed directly to the API query; pagination added.

### 3. MobileJournalList async void handlers
Same pattern as JournalPage — `OnFilterChanged` / `OnStateChanged` were
`async void`, silently swallowing exceptions.

Fix: `InvokeAsync` wrapper pattern applied (matches JournalPage Phase 2 fix).

### 4. JournalQueryService double date filter
Date filters were pushed to EF/SQL inside `FetchTransactionsAsync` AND then
applied again in-memory via `ApplySharedFilters`. For transactions this
doubled the work and could produce inconsistent results if boundary conditions
differed between SQL and in-memory LINQ.

Fix: date filters applied once — inside `FetchTransactionsAsync` at the DB
level. `ApplySharedFilters` no longer re-applies start/end date for entries
that were already filtered upstream.
