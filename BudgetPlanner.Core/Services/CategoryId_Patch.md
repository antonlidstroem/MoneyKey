# Hardcoded Category ID Patch — Phase 4

## Problem
`MilersattningService.CreateAsync` uses `CategoryId = 12` (magic number).
`VabService.CreateAsync` uses `CategoryId = 11` (magic number).

If the seed order ever changes, linked transactions silently get the wrong category.

## Fix

### Step 1: Add `CategoryConstants.cs` to `BudgetPlanner.Domain`
The file `BudgetPlanner.Domain/Models/CategoryConstants.cs` is included in this archive.

### Step 2: Add using + update MilersattningService in Services.cs

In `BudgetPlanner.Core/Services/Services.cs`, add at the top:
```csharp
using BudgetPlanner.Domain.Models;  // for CategoryConstants
```

Then in `MilersattningService.CreateAsync`, replace:
```csharp
CategoryId = 12,
```
with:
```csharp
CategoryId = CategoryConstants.Milersattning,
```

### Step 3: Update VabService in Services.cs

In `VabService.CreateAsync`, replace:
```csharp
CategoryId = 11,
```
with:
```csharp
CategoryId = CategoryConstants.VabSjukfranvaro,
```

### Step 4: Update ReceiptService in Services.cs

In `ReceiptService.CreateLinkedTransactionsAsync`, replace:
```csharp
CategoryId = 3,
```
with:
```csharp
CategoryId = CategoryConstants.Transport,
```

No migration needed. The IDs remain the same — we're just replacing magic numbers
with named constants.
