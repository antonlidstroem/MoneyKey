
# Phase 2 – Self-Criticism & Manual Steps Required

## 1. Remove OLD ExportService from Core/Services.cs  (ACTION REQUIRED)
`BudgetPlanner.Core/Services/Services.cs` still contains the OLD `ExportService`
class with the swapped columns and wrong monthly filter.
You MUST delete that class from `Services.cs`.
The replacement is `ExportService.cs` in this archive (same namespace).

Steps:
1. Open `BudgetPlanner.Core/Services/Services.cs`
2. Find `public class ExportService` and delete it plus its closing `}`
3. `dotnet build` should compile cleanly

## 2. SignalR.ConnectAsync signature change
`ConnectAsync` now has an optional 4th parameter `AuthenticationStateProvider? authProvider`.
This is backward-compatible (optional param with default null).
Any other call sites that already pass positional args will still compile.

## 3. MobileJournalList.razor NOT fixed
`MobileJournalList.razor` still has `async void OnFilterChanged` and
`async void OnStateChanged`. These will be fixed in Phase 3.

## 4. BudgetService_Patch.md
This file was auto-generated as a guide but `UpdateAsync` was injected directly
into `Services.cs`. You can ignore the `.md` file.
