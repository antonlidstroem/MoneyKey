# ReceiptApiService patch — Phase 3

Replace the `GetAllAsync` method in `ReceiptApiService` (in Services.cs) with:

```csharp
public Task<PagedResult<ReceiptBatchDto>?> GetAllAsync(
    int budgetId,
    int page = 1,
    int pageSize = 25,
    ReceiptBatchStatus? status = null)
{
    var url = $"api/budgets/{budgetId}/receipts?page={page}&pageSize={pageSize}";
    if (status.HasValue) url += $"&statuses={(int)status.Value}";
    return GetAsync<PagedResult<ReceiptBatchDto>>(url);
}
```

This passes the status filter server-side instead of filtering client-side.
The API already supports `statuses` as a query parameter via `ReceiptQuery.Statuses`.
