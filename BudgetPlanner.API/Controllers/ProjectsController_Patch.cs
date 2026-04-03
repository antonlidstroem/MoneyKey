// ════════════════════════════════════════════════════════════════════════════
// ProjectsController patch — Phase 3
//
// HOW TO APPLY:
//   In BudgetPlanner.API/Controllers/Controllers.cs, find ProjectsController
//   and replace its GetAll action with the version below (single DB query).
//   The rest of the controller is unchanged.
// ════════════════════════════════════════════════════════════════════════════

/*
    [HttpGet]
    public async Task<IActionResult> GetAll(int budgetId)
    {
        if (!await _auth.HasRoleAsync(budgetId, UserId, BudgetMemberRole.Viewer)) return Forbid();

        // FIX: single JOIN query — no N+1
        var withSpent = await _repo.GetForBudgetWithSpentAsync(budgetId);
        var dtos = withSpent.Select(x => Map(x.Project, x.SpentAmount)).ToList();
        return Ok(dtos);
    }
*/
