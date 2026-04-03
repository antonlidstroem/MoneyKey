// ════════════════════════════════════════════════════════════════════════════
// BudgetPlanner.DAL/Repositories/ProjectRepository_Patch.cs
//
// PHASE 3 FIX: Replace the IProjectRepository interface and ProjectRepository
// class in Repositories.cs with the versions below. The new method
// GetForBudgetWithSpentAsync performs a single LEFT JOIN query that returns
// projects with their total spent amount — eliminating the N+1 query in
// ProjectsController.GetAll.
//
// HOW TO APPLY:
//   1. Open BudgetPlanner.DAL/Repositories/Repositories.cs
//   2. Replace the IProjectRepository interface with the one below.
//   3. Replace the ProjectRepository class with the one below.
//   4. No migration needed.
// ════════════════════════════════════════════════════════════════════════════
using Microsoft.EntityFrameworkCore;
using BudgetPlanner.DAL.Data;
using BudgetPlanner.Domain.Models;

namespace BudgetPlanner.DAL.Repositories;

// ── Updated interface ─────────────────────────────────────────────────────────
public interface IProjectRepository
{
    /// <summary>Returns all projects for a budget with their total spent amount
    /// in a single SQL query (LEFT JOIN + SUM).</summary>
    Task<List<(Project Project, decimal SpentAmount)>> GetForBudgetWithSpentAsync(int budgetId);

    Task<Project?> GetByIdAsync(int id, int budgetId);
    Task<Project> CreateAsync(Project project);
    Task<Project> UpdateAsync(Project project);
    Task DeleteAsync(int id, int budgetId);
}

// ── Updated implementation ────────────────────────────────────────────────────
public class ProjectRepository : IProjectRepository
{
    private readonly BudgetDbContext _db;
    public ProjectRepository(BudgetDbContext db) => _db = db;

    /// <summary>
    /// Single query: projects LEFT JOIN transactions GROUP BY projectId.
    /// Replaces the old pattern that looped and issued one query per project.
    /// </summary>
    public async Task<List<(Project Project, decimal SpentAmount)>> GetForBudgetWithSpentAsync(int budgetId)
    {
        // Pull projects with their transactions in one round-trip using EF navigation.
        var projects = await _db.Projects
            .Where(p => p.BudgetId == budgetId)
            .OrderByDescending(p => p.CreatedAt)
            .Select(p => new
            {
                Project    = p,
                SpentAmount = _db.Transactions
                    .Where(t => t.ProjectId == p.Id && t.BudgetId == budgetId)
                    .Sum(t => (decimal?)t.NetAmount) ?? 0m
            })
            .ToListAsync();

        return projects
            .Select(x => (x.Project, x.SpentAmount))
            .ToList();
    }

    public async Task<Project?> GetByIdAsync(int id, int budgetId) =>
        await _db.Projects.FirstOrDefaultAsync(p => p.Id == id && p.BudgetId == budgetId);

    public async Task<Project> CreateAsync(Project p) { _db.Projects.Add(p); await _db.SaveChangesAsync(); return p; }
    public async Task<Project> UpdateAsync(Project p) { _db.Projects.Update(p); await _db.SaveChangesAsync(); return p; }
    public async Task DeleteAsync(int id, int budgetId)
    {
        var p = await GetByIdAsync(id, budgetId);
        if (p != null) { _db.Projects.Remove(p); await _db.SaveChangesAsync(); }
    }
}
