# Phase 2 – Critical Logic Fixes

## Files changed

| File | Fix |
|------|-----|
| `BudgetPlanner.Blazor/Pages/Journal/JournalPage.razor` | EditEntry loads full transaction via API before opening form |
| `BudgetPlanner.Blazor/Pages/Settings/SettingsPage.razor` | SaveBudgetAsync now calls PUT /api/budgets/{id} |
| `BudgetPlanner.Core/Services/Services.cs` | ExportToExcel: projects columns corrected; MonthlySummary includes all transactions |
| `BudgetPlanner.DAL/Data/AuditInterceptor.cs` | KonteringRow now resolves BudgetId via its parent Transaction |
| `BudgetPlanner.Blazor/State/State.cs` | SignalR reconnects with a fresh token; SetBudgets always refreshes state |
| `BudgetPlanner.Blazor/Pages/Journal/JournalPage.razor` | async void handlers replaced with safe pattern |

## How to apply
Drop each file into your solution replacing the existing copy. No migration needed.

## Fixes in detail

### 1. EditEntry — corrupt form state (CRITICAL)
Previously `EditEntry` built a `TransactionDto` from `JournalEntryDto` fields,
leaving `CategoryId = 0`, `Type` unset, `Recurrence` unset. Form saved corrupt data.

Fix: `EditEntry` now calls `GET /api/budgets/{id}/transactions/{txId}` to load
the full `TransactionDto` before opening the form. A loading spinner is shown
while the fetch is in progress.

### 2. SettingsPage.SaveBudgetAsync — silent no-op (CRITICAL)
Previously showed a success toast but made no API call. The budget name was
never persisted.

Fix: calls `PUT /api/budgets/{budgetId}` with the new name/description.

### 3. Excel export — project columns swapped (CRITICAL)
Column 1 ("Projekt") received `BudgetAmount` and column 2 ("Budget") received
`Name`. Swapped.

Fix: columns now correctly mapped Name→col1, BudgetAmount→col2.

### 4. Excel monthly summary — excludes one-time transactions
The summary sheet previously filtered `Recurrence != OneTime`, excluding all
bank imports and manual one-time entries. This made the summary misleading.

Fix: filter removed — all transactions are included in the monthly summary.

### 5. KonteringRow audit — BudgetId always 0 (skipped)
`AuditInterceptor.GetBudgetId` returned 0 for `KonteringRow` because it has no
`BudgetId` property. The interceptor then skipped it. KonteringRow changes were
never audited.

Fix: when the entry is a `KonteringRow`, the interceptor looks up the parent
`Transaction` from the change tracker to get its `BudgetId`.

### 6. SignalR — token expiry on reconnect
After a 15-minute access token expiry the SignalR hub rejects reconnection.
Fix: `SignalRService` refreshes the token via `JwtAuthenticationStateProvider`
before attempting to reconnect, and stores the `AuthenticationStateProvider`
reference for future refreshes.

### 7. async void event handlers — silent exception swallowing
`OnFilterChanged` and `OnStateChanged` were `async void`. Exceptions were
silently dropped, causing the UI to freeze without feedback.

Fix: handlers use a fire-and-forget wrapper that logs/toasts exceptions.
