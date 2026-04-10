// ═══════════════════════════════════════════════════════════════════════════════
// ADD TO: BudgetPlanner.Core/Services/Services.cs
// Place after ReceiptService / before JournalQueryService
// ═══════════════════════════════════════════════════════════════════════════════
using BudgetPlanner.Core.DTOs;
using BudgetPlanner.DAL.Repositories;
using BudgetPlanner.Domain.Enums;
using BudgetPlanner.Domain.Models;

namespace BudgetPlanner.Core.Services;

public class TaskListService
{
    private readonly ITaskListRepository _lists;
    private readonly ITaskItemRepository _items;

    public TaskListService(ITaskListRepository lists, ITaskItemRepository items)
    { _lists = lists; _items = items; }

    // ── Lists ─────────────────────────────────────────────────────────────────

    public async Task<List<TaskListDto>> GetAllAsync(int budgetId, bool includeArchived = false)
    {
        var lists = await _lists.GetForBudgetAsync(budgetId, includeArchived);
        return lists.Select(MapList).ToList();
    }

    public async Task<TaskListDto?> GetByIdAsync(int id, int budgetId)
    {
        var list = await _lists.GetByIdAsync(id, budgetId);
        return list == null ? null : MapList(list);
    }

    public async Task<PublicTaskListDto?> GetPublicAsync(string token)
    {
        var list = await _lists.GetBySharedTokenAsync(token);
        if (list == null) return null;
        return new PublicTaskListDto
        {
            Title        = list.Title,
            ListType     = list.ListType,
            Emoji        = list.Emoji,
            ColorHex     = list.ColorHex,
            TotalItems   = list.Items.Count,
            CheckedItems = list.Items.Count(i => i.IsChecked),
            Items        = list.Items.Select(MapItem).ToList()
        };
    }

    public async Task<TaskListDto> CreateAsync(int budgetId, string userId, CreateTaskListDto dto)
    {
        var list = new TaskList
        {
            BudgetId        = budgetId,
            Title           = dto.Title.Trim(),
            ListType        = dto.ListType,
            Emoji           = dto.Emoji,
            ColorHex        = dto.ColorHex,
            CreatedByUserId = userId,
            SharedToken     = Guid.NewGuid().ToString("N")
        };
        list = await _lists.CreateAsync(list);
        return MapList(list);
    }

    public async Task<TaskListDto?> UpdateAsync(int id, int budgetId, UpdateTaskListDto dto)
    {
        var list = await _lists.GetByIdAsync(id, budgetId);
        if (list == null) return null;
        list.Title    = dto.Title.Trim();
        list.ListType = dto.ListType;
        list.Emoji    = dto.Emoji;
        list.ColorHex = dto.ColorHex;
        list = await _lists.UpdateAsync(list);
        return MapList(list);
    }

    public async Task ArchiveAsync(int id, int budgetId)
    {
        var list = await _lists.GetByIdAsync(id, budgetId);
        if (list == null) return;
        list.IsArchived = true;
        await _lists.UpdateAsync(list);
    }

    public async Task DeleteAsync(int id, int budgetId) =>
        await _lists.DeleteAsync(id, budgetId);

    // ── Items ─────────────────────────────────────────────────────────────────

    public async Task<TaskItemDto> AddItemAsync(int listId, int budgetId, string userId, CreateTaskItemDto dto)
    {
        // Verify the list belongs to this budget.
        var list = await _lists.GetByIdAsync(listId, budgetId)
            ?? throw new KeyNotFoundException("Listan hittades inte.");

        var sortOrder = await _items.GetNextSortOrderAsync(listId);
        var item = new TaskItem
        {
            ListId    = listId,
            Text      = dto.Text.Trim(),
            SortOrder = sortOrder
        };
        item = await _items.CreateAsync(item);

        // Touch the parent list's UpdatedAt so it floats to the top in the overview.
        list.UpdatedAt = DateTime.UtcNow;
        await _lists.UpdateAsync(list);

        return MapItem(item);
    }

    public async Task<TaskItemDto?> UpdateItemTextAsync(int itemId, int listId, int budgetId, UpdateTaskItemDto dto)
    {
        await VerifyListOwnership(listId, budgetId);
        var item = await _items.GetByIdAsync(itemId, listId);
        if (item == null) return null;
        item.Text = dto.Text.Trim();
        item = await _items.UpdateAsync(item);
        return MapItem(item);
    }

    public async Task<TaskItemDto?> CheckItemAsync(int itemId, int listId, int budgetId, string userId, CheckTaskItemDto dto)
    {
        await VerifyListOwnership(listId, budgetId);
        var item = await _items.GetByIdAsync(itemId, listId);
        if (item == null) return null;

        item.IsChecked       = dto.IsChecked;
        item.CheckedAt       = dto.IsChecked ? DateTime.UtcNow : null;
        item.CheckedByUserId = dto.IsChecked ? userId : null;
        item = await _items.UpdateAsync(item);

        // Touch parent list UpdatedAt.
        var list = await _lists.GetByIdAsync(listId, budgetId);
        if (list != null) { list.UpdatedAt = DateTime.UtcNow; await _lists.UpdateAsync(list); }

        return MapItem(item);
    }

    public async Task DeleteItemAsync(int itemId, int listId, int budgetId)
    {
        await VerifyListOwnership(listId, budgetId);
        await _items.DeleteAsync(itemId, listId);
    }

    public async Task ReorderItemsAsync(int listId, int budgetId, ReorderTaskItemDto dto)
    {
        await VerifyListOwnership(listId, budgetId);
        await _items.ReorderAsync(listId, dto.OrderedIds);
    }

    /// <summary>
    /// Links a task item to a budget entity that was just created from it.
    /// </summary>
    public async Task<TaskItemDto?> LinkItemAsync(int itemId, int listId, int budgetId, LinkTaskItemDto dto)
    {
        await VerifyListOwnership(listId, budgetId);
        var item = await _items.GetByIdAsync(itemId, listId);
        if (item == null) return null;
        item.LinkedEntityType = dto.LinkedEntityType;
        item.LinkedEntityId   = dto.LinkedEntityId;
        item = await _items.UpdateAsync(item);
        return MapItem(item);
    }

    // ── Mapping ───────────────────────────────────────────────────────────────

    private static TaskListDto MapList(TaskList l) => new()
    {
        Id              = l.Id,
        BudgetId        = l.BudgetId,
        Title           = l.Title,
        ListType        = l.ListType,
        Emoji           = l.Emoji,
        ColorHex        = l.ColorHex,
        CreatedByUserId = l.CreatedByUserId,
        CreatedAt       = l.CreatedAt,
        IsArchived      = l.IsArchived,
        SharedToken     = l.SharedToken,
        TotalItems      = l.Items.Count,
        CheckedItems    = l.Items.Count(i => i.IsChecked),
        Items           = l.Items.Select(MapItem).ToList()
    };

    private static TaskItemDto MapItem(TaskItem i) => new()
    {
        Id               = i.Id,
        ListId           = i.ListId,
        Text             = i.Text,
        IsChecked        = i.IsChecked,
        CheckedAt        = i.CheckedAt,
        CheckedByUserId  = i.CheckedByUserId,
        SortOrder        = i.SortOrder,
        CreatedAt        = i.CreatedAt,
        LinkedEntityType = i.LinkedEntityType,
        LinkedEntityId   = i.LinkedEntityId
    };

    private async Task VerifyListOwnership(int listId, int budgetId)
    {
        var exists = await _lists.GetByIdAsync(listId, budgetId);
        if (exists == null)
            throw new KeyNotFoundException("Listan hittades inte eller tillhör inte denna budget.");
    }
}
