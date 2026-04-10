namespace BudgetPlanner.Domain.Enums;

public enum TransactionType   { Income, Expense }
public enum AdjustmentType    { Deduction, Addition }
public enum Recurrence        { OneTime, Monthly, Yearly }
public enum BudgetMemberRole  { Owner, Editor, Viewer, Auditor }
public enum AuditAction       { Created, Updated, Deleted, Imported, Exported }
public enum ReceiptBatchStatus { Draft, Submitted, Approved, Rejected, Reimbursed }
public enum JournalEntryType  { Transaction, Milersattning, Vab, ReceiptBatch, TaskList }

public enum BudgetMonth
{
    January = 1, February, March, April, May, June,
    July, August, September, October, November, December
}

// ── NEW: Task list enums ───────────────────────────────────────────────────────
/// <summary>Visual / behavioural variant of a task list.</summary>
public enum TaskListType
{
    /// <summary>General-purpose to-do list.</summary>
    ToDo,
    /// <summary>Shopping list — checked items collapse to a "bought" section.</summary>
    Shopping,
    /// <summary>Free-form generic list.</summary>
    Generic
}

/// <summary>
/// The kind of budget entity a <see cref="BudgetPlanner.Domain.Models.TaskItem"/>
/// has been converted into.  Null means the item has not been converted.
/// </summary>
public enum LinkedEntityType
{
    Transaction,
    Milersattning,
    Vab,
    Receipt
}
