namespace BudgetPlanner.Domain.Enums;

public enum TransactionType   { Income, Expense }
public enum AdjustmentType    { Deduction, Addition }
public enum Recurrence        { OneTime, Monthly, Yearly }
public enum BudgetMemberRole  { Owner, Editor, Viewer, Auditor }
public enum AuditAction       { Created, Updated, Deleted, Imported, Exported }
public enum ReceiptBatchStatus { Draft, Submitted, Approved, Rejected, Reimbursed }
public enum JournalEntryType  { Transaction, Milersattning, Vab, ReceiptBatch }

public enum BudgetMonth
{
    January = 1, February, March, April, May, June,
    July, August, September, October, November, December
}
