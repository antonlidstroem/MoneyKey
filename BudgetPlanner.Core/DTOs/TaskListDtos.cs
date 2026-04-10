// ═══════════════════════════════════════════════════════════════════════════════
// APPEND TO: BudgetPlanner.Core/DTOs/Dtos.cs
// (replace any existing task-list DTO block if you applied the previous version)
// ═══════════════════════════════════════════════════════════════════════════════
using BudgetPlanner.Domain.Enums;

namespace BudgetPlanner.Core.DTOs;

// ─── TASK LIST ────────────────────────────────────────────────────────────────

public class TaskListDto
{
    public int          Id              { get; set; }
    public int          BudgetId        { get; set; }
    public string       Title           { get; set; } = string.Empty;
    public TaskListType ListType        { get; set; }
    public string       Emoji           { get; set; } = "📋";
    public string       ColorHex        { get; set; } = "#1565C0";
    public string       CreatedByUserId { get; set; } = string.Empty;
    public DateTime     CreatedAt       { get; set; }
    public bool         IsArchived      { get; set; }
    public string       SharedToken     { get; set; } = string.Empty;
    public int          TotalItems      { get; set; }
    public int          CheckedItems    { get; set; }
    public List<TaskItemDto> Items      { get; set; } = new();

    public double ProgressPercent =>
        TotalItems == 0 ? 0 : Math.Round((double)CheckedItems / TotalItems * 100, 0);
    public bool   IsComplete    => TotalItems > 0 && CheckedItems == TotalItems;
    public string ProgressLabel => $"{CheckedItems}/{TotalItems} klara";
}

public class TaskItemDto
{
    public int               Id               { get; set; }
    public int               ListId           { get; set; }
    public string            Text             { get; set; } = string.Empty;
    public bool              IsChecked        { get; set; }
    public DateTime?         CheckedAt        { get; set; }
    public string?           CheckedByUserId  { get; set; }
    public int               SortOrder        { get; set; }
    public DateTime          CreatedAt        { get; set; }
    public LinkedEntityType? LinkedEntityType { get; set; }
    public int?              LinkedEntityId   { get; set; }
    public bool              LinkBroken       { get; set; }
}

// ─── COMMANDS ─────────────────────────────────────────────────────────────────

public record CreateTaskListDto(
    string       Title,
    TaskListType ListType,
    string       Emoji,
    string       ColorHex);

public record UpdateTaskListDto(
    string       Title,
    TaskListType ListType,
    string       Emoji,
    string       ColorHex);

public record CreateTaskItemDto(string Text);
public record UpdateTaskItemDto(string Text);
public record CheckTaskItemDto(bool IsChecked);
public record ReorderTaskItemDto(List<int> OrderedIds);
public record LinkTaskItemDto(LinkedEntityType LinkedEntityType, int LinkedEntityId);

// ─── PUBLIC SHARE VIEW ────────────────────────────────────────────────────────
// FIX: Added ProgressPercent + ProgressLabel — PublicListPage.razor uses both.

public class PublicTaskListDto
{
    public string       Title        { get; set; } = string.Empty;
    public TaskListType ListType     { get; set; }
    public string       Emoji        { get; set; } = "📋";
    public string       ColorHex     { get; set; } = "#1565C0";
    public int          TotalItems   { get; set; }
    public int          CheckedItems { get; set; }
    public List<TaskItemDto> Items   { get; set; } = new();

    public double ProgressPercent =>
        TotalItems == 0 ? 0 : Math.Round((double)CheckedItems / TotalItems * 100, 0);
    public string ProgressLabel => $"{CheckedItems}/{TotalItems} klara";
}
