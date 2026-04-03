# BudgetService patch

Add the following method to `BudgetService` in
`BudgetPlanner.Blazor/Services/Services.cs`:

```csharp
public async Task UpdateAsync(int budgetId, UpdateBudgetDto dto)
{
    var r = await Http.PutAsJsonAsync($"api/budgets/{budgetId}", dto);
    r.EnsureSuccessStatusCode();
}
```

This is needed by `SettingsPage.SaveBudgetAsync`.

The full `Services.cs` from Phase 1 already contains the `BudgetService` class;
just add this one method to it.
