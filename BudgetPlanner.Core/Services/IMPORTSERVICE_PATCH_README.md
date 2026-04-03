# Core Services Patch — Remove OLD ImportService

`BudgetPlanner.Core/Services/Services.cs` still contains the OLD `ImportService`
class (with `static Dictionary<string, List<ImportRowDto>> _sessions`).

You MUST delete that class entirely from `Services.cs`.

The replacement is `ImportService.cs` (the new file in this Phase 1 archive,
same namespace `BudgetPlanner.Core.Services`).

## Steps

1. Open `BudgetPlanner.Core/Services/Services.cs`
2. Find `public class ImportService` near the bottom of the file
3. Delete from `public class ImportService` to its closing `}` (inclusive)
4. Add the following using at the very top of `Services.cs` if not present:

```csharp
using Microsoft.Extensions.Caching.Memory;
```

5. Run `dotnet build` — no duplicate class errors should remain.
