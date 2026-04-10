// ═══════════════════════════════════════════════════════════════════════════════
// TASK LISTS — append these two classes to the bottom of
// BudgetPlanner.Domain/Models/Models.cs (inside the existing namespace)
// ═══════════════════════════════════════════════════════════════════════════════
using BudgetPlanner.Domain.Enums;

namespace BudgetPlanner.Domain.Models;

/// <summary>
/// A named list of tasks that lives inside a budget.
/// Lists never affect the budget balance on their own.
/// A task can be individually converted into a Transaction, Milersattning,
/// VAB entry, or Receipt which then carries budget impact.
/// </summary>
public class TaskList
{
    public int           Id              { get; set; }
    public int           BudgetId        { get; set; }
    public string        Title           { get; set; } = string.Empty;
    public TaskListType  ListType        { get; set; } = TaskListType.ToDo;
    /// <summary>Single emoji character shown on the list card.</summary>
    public string        Emoji           { get; set; } = "??";
    /// <summary>Hex colour string e.g. "#1565C0" for the card accent.</summary>
    public string        ColorHex        { get; set; } = "#1565C0";
    public string        CreatedByUserId { get; set; } = string.Empty;
    public DateTime      CreatedAt       { get; set; } = DateTime.UtcNow;
    public DateTime?     UpdatedAt       { get; set; }
    public bool          IsArchived      { get; set; } = false;
    /// <summary>
    /// Token for the public read-only share link.
    /// Generated on creation.  Anyone with this token can read the list
    /// without authentication via GET /api/lists/public/{token}.
    /// </summary>
    public string        SharedToken     { get; set; } = Guid.NewGuid().ToString("N");

    public Budget              Budget { get; set; } = null!;
    public ICollection<TaskItem> Items { get; set; } = new List<TaskItem>();
}

/// <summary>
/// A single item (task) inside a <see cref="TaskList"/>.
/// </summary>
public class TaskItem
{
    public int      Id              { get; set; }
    public int      ListId          { get; set; }
    public string   Text            { get; set; } = string.Empty;
    public bool     IsChecked       { get; set; } = false;
    public DateTime? CheckedAt      { get; set; }
    public string?  CheckedByUserId { get; set; }
    /// <summary>Display order within the list.  Lower = shown first.</summary>
    public int      SortOrder       { get; set; }
    public DateTime CreatedAt       { get; set; } = DateTime.UtcNow;

    // ── Conversion link ───────────────────────────────────────────────────────
    /// <summary>Set when this item has been converted to a budget entity.</summary>
    public LinkedEntityType? LinkedEntityType { get; set; }
    /// <summary>The Id of the linked budget entity (Transaction.Id etc.).</summary>
    public int?              LinkedEntityId   { get; set; }

    public TaskList List { get; set; } = null!;
}
