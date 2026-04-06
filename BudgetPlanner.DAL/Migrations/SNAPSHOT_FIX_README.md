# Snapshot Fix — Remove admin HasData

In `BudgetPlanner.DAL/Migrations/BudgetDbContextModelSnapshot.cs`, find and
delete the entire `b.HasData(...)` block inside the `ApplicationUser` entity
configuration. It looks like this:

```csharp
b.HasData(
    new
    {
        Id = "admin-uuid-123",
        AccessFailedCount = 0,
        ConcurrencyStamp = "...",
        ...
        UserName = "admin@budget.se"
    });
```

After deleting that block the snapshot will match the new `BudgetDbContext`
(which no longer seeds a user). Run:

    dotnet build BudgetPlanner.DAL

to confirm no warnings.

**Alternatively**, regenerate the snapshot automatically:

    cd BudgetPlanner.API
    dotnet ef migrations add Phase1_SnapshotCleanup --project ../BudgetPlanner.DAL

(This creates an empty migration that syncs the snapshot — safe to apply.)
