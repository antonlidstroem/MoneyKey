// ═══════════════════════════════════════════════════════════════════════════════
// ADD TO: BudgetPlanner.DAL/Repositories/Repositories.cs
// Place after AuditRepository / AppSettingRepository
// ═══════════════════════════════════════════════════════════════════════════════
using Microsoft.EntityFrameworkCore;
using BudgetPlanner.DAL.Data;
using BudgetPlanner.Domain.Models;

namespace BudgetPlanner.DAL.Repositories;

// ─── INTERFACES ───────────────────────────────────────────────────────────────

public interface ITaskListRepository
{
    Task<List<TaskList>> GetForBudgetAsync(int budgetId, bool includeArchived = false);
    Task<TaskList?>      GetByIdAsync(int id, int budgetId);
    Task<TaskList?>      GetBySharedTokenAsync(string token);
    Task<TaskList>       CreateAsync(TaskList list);
    Task<TaskList>       UpdateAsync(TaskList list);
    Task                 DeleteAsync(int id, int budgetId);
}

public interface ITaskItemRepository
{
    Task<List<TaskItem>>  GetForListAsync(int listId);
    Task<TaskItem?>       GetByIdAsync(int id, int listId);
    Task<TaskItem>        CreateAsync(TaskItem item);
    Task<TaskItem>        UpdateAsync(TaskItem item);
    Task                  DeleteAsync(int id, int listId);
    Task<int>             GetNextSortOrderAsync(int listId);
    /// <summary>
    /// Applies a new sort order in bulk.
    /// <paramref name="orderedIds"/> is the full ordered list of item IDs for the list.
    /// </summary>
    Task ReorderAsync(int listId, List<int> orderedIds);
}

// ─── IMPLEMENTATIONS ──────────────────────────────────────────────────────────

public class TaskListRepository : ITaskListRepository
{
    private readonly BudgetDbContext _db;
    public TaskListRepository(BudgetDbContext db) => _db = db;

    public async Task<List<TaskList>> GetForBudgetAsync(int budgetId, bool includeArchived = false)
    {
        var q = _db.TaskLists
            .Include(l => l.Items)
            .Where(l => l.BudgetId == budgetId);

        if (!includeArchived)
            q = q.Where(l => !l.IsArchived);

        // Order by most recently updated first, then by creation date.
        return await q.OrderByDescending(l => l.UpdatedAt ?? l.CreatedAt).ToListAsync();
    }

    public async Task<TaskList?> GetByIdAsync(int id, int budgetId) =>
        await _db.TaskLists
            .Include(l => l.Items.OrderBy(i => i.SortOrder))
            .FirstOrDefaultAsync(l => l.Id == id && l.BudgetId == budgetId);

    public async Task<TaskList?> GetBySharedTokenAsync(string token) =>
        await _db.TaskLists
            .Include(l => l.Items.OrderBy(i => i.SortOrder))
            .FirstOrDefaultAsync(l => l.SharedToken == token && !l.IsArchived);

    public async Task<TaskList> CreateAsync(TaskList list)
    {
        _db.TaskLists.Add(list);
        await _db.SaveChangesAsync();
        return list;
    }

    public async Task<TaskList> UpdateAsync(TaskList list)
    {
        list.UpdatedAt = DateTime.UtcNow;
        _db.TaskLists.Update(list);
        await _db.SaveChangesAsync();
        return list;
    }

    public async Task DeleteAsync(int id, int budgetId)
    {
        var list = await _db.TaskLists
            .FirstOrDefaultAsync(l => l.Id == id && l.BudgetId == budgetId);
        if (list != null) { _db.TaskLists.Remove(list); await _db.SaveChangesAsync(); }
    }
}

public class TaskItemRepository : ITaskItemRepository
{
    private readonly BudgetDbContext _db;
    public TaskItemRepository(BudgetDbContext db) => _db = db;

    public async Task<List<TaskItem>> GetForListAsync(int listId) =>
        await _db.TaskItems
            .Where(i => i.ListId == listId)
            .OrderBy(i => i.SortOrder)
            .ToListAsync();

    public async Task<TaskItem?> GetByIdAsync(int id, int listId) =>
        await _db.TaskItems.FirstOrDefaultAsync(i => i.Id == id && i.ListId == listId);

    public async Task<TaskItem> CreateAsync(TaskItem item)
    {
        _db.TaskItems.Add(item);
        await _db.SaveChangesAsync();
        return item;
    }

    public async Task<TaskItem> UpdateAsync(TaskItem item)
    {
        _db.TaskItems.Update(item);
        await _db.SaveChangesAsync();
        return item;
    }

    public async Task DeleteAsync(int id, int listId)
    {
        var item = await GetByIdAsync(id, listId);
        if (item != null) { _db.TaskItems.Remove(item); await _db.SaveChangesAsync(); }
    }

    public async Task<int> GetNextSortOrderAsync(int listId)
    {
        var max = await _db.TaskItems
            .Where(i => i.ListId == listId)
            .MaxAsync(i => (int?)i.SortOrder) ?? -1;
        return max + 1;
    }

    public async Task ReorderAsync(int listId, List<int> orderedIds)
    {
        var items = await _db.TaskItems
            .Where(i => i.ListId == listId && orderedIds.Contains(i.Id))
            .ToListAsync();

        for (var idx = 0; idx < orderedIds.Count; idx++)
        {
            var item = items.FirstOrDefault(i => i.Id == orderedIds[idx]);
            if (item != null) item.SortOrder = idx;
        }
        await _db.SaveChangesAsync();
    }
}
