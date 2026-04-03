using BudgetPlanner.Domain.Enums;

namespace BudgetPlanner.Core.DTOs;

// ─── AUTH ─────────────────────────────────────────────────────────────────────
public record RegisterDto(string Email, string Password, string FirstName, string LastName);
public record LoginDto(string Email, string Password);
public record AuthResultDto(string AccessToken, UserDto User);
public record UserDto(string Id, string Email, string FirstName, string LastName, List<BudgetMembershipDto> Memberships);
/// <summary>Token sent by the accept-invite page to claim an invitation.</summary>
public record AcceptInviteDto(string Token);

// ─── BUDGET ───────────────────────────────────────────────────────────────────
public record BudgetDto(int Id, string Name, string? Description, bool IsActive, DateTime CreatedAt, BudgetMemberRole MyRole);
public record CreateBudgetDto(string Name, string? Description);
public record UpdateBudgetDto(string Name, string? Description);
public record InviteMemberDto(string Email, BudgetMemberRole Role);
public record BudgetMembershipDto(int BudgetId, string BudgetName, BudgetMemberRole Role);
public record MemberDto(string UserId, string Email, string FirstName, string LastName, BudgetMemberRole Role, DateTime JoinedAt);

// ─── TRANSACTION ──────────────────────────────────────────────────────────────
public class TransactionDto
{
    public int Id { get; set; }
    public int BudgetId { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public decimal NetAmount { get; set; }
    public decimal? GrossAmount { get; set; }
    public string? Description { get; set; }
    public int CategoryId { get; set; }
    public string CategoryName { get; set; } = string.Empty;
    public TransactionType Type { get; set; }
    public Recurrence Recurrence { get; set; }
    public bool IsActive { get; set; }
    public BudgetMonth? Month { get; set; }
    public decimal? Rate { get; set; }
    public int? ProjectId { get; set; }
    public string? ProjectName { get; set; }
    public bool HasKontering { get; set; }
    public string? CreatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; }
    public List<KonteringRowDto> KonteringRows { get; set; } = new();
}

public class CreateTransactionDto
{
    public DateTime StartDate { get; set; } = DateTime.Today;
    public DateTime? EndDate { get; set; }
    public decimal NetAmount { get; set; }
    public decimal? GrossAmount { get; set; }
    public string? Description { get; set; }
    public int CategoryId { get; set; }
    public TransactionType Type { get; set; }
    public Recurrence Recurrence { get; set; }
    public bool IsActive { get; set; } = true;
    public BudgetMonth? Month { get; set; }
    public decimal? Rate { get; set; }
    public int? ProjectId { get; set; }
    public List<KonteringRowDto> KonteringRows { get; set; } = new();
}

public class UpdateTransactionDto : CreateTransactionDto { public int Id { get; set; } }
public record BatchDeleteDto(List<int> Ids);

// ─── KONTERING ────────────────────────────────────────────────────────────────
public class KonteringRowDto
{
    public int Id { get; set; }
    public string KontoNr { get; set; } = string.Empty;
    public string? CostCenter { get; set; }
    public decimal Amount { get; set; }
    public decimal? Percentage { get; set; }
    public string? Description { get; set; }
}

// ─── PROJECT ──────────────────────────────────────────────────────────────────
public class ProjectDto
{
    public int Id { get; set; }
    public int BudgetId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal BudgetAmount { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public bool IsActive { get; set; }
    public decimal SpentAmount { get; set; }
    public decimal RemainingAmount => BudgetAmount + SpentAmount;
    public double ProgressPercent => BudgetAmount == 0 ? 0 : Math.Min(100, (double)(-SpentAmount / BudgetAmount * 100));
}

public record CreateProjectDto(string Name, string? Description, decimal BudgetAmount, DateTime StartDate, DateTime? EndDate);
public record UpdateProjectDto(int Id, string Name, string? Description, decimal BudgetAmount, DateTime StartDate, DateTime? EndDate, bool IsActive);

// ─── MILERSÄTTNING ────────────────────────────────────────────────────────────
public class MilersattningDto
{
    public int Id { get; set; }
    public int BudgetId { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string? UserEmail { get; set; }
    public DateTime TripDate { get; set; }
    public string FromLocation { get; set; } = string.Empty;
    public string ToLocation { get; set; } = string.Empty;
    public decimal DistanceKm { get; set; }
    public decimal RatePerKm { get; set; }
    public string? Purpose { get; set; }
    public decimal ReimbursementAmount { get; set; }
    public int? LinkedTransactionId { get; set; }
}

public record CreateMilersattningDto(DateTime TripDate, string FromLocation, string ToLocation, decimal DistanceKm, decimal RatePerKm, string? Purpose);

// ─── VAB ──────────────────────────────────────────────────────────────────────
public class VabDto
{
    public int Id { get; set; }
    public int BudgetId { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string? UserEmail { get; set; }
    public string? ChildName { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public decimal DailyBenefit { get; set; }
    public decimal Rate { get; set; }
    public int TotalDays { get; set; }
    public decimal TotalAmount { get; set; }
    public int? LinkedTransactionId { get; set; }
}

public record CreateVabDto(string? ChildName, DateTime StartDate, DateTime EndDate, decimal DailyBenefit, decimal Rate);

// ─── RECEIPTS ─────────────────────────────────────────────────────────────────
public class ReceiptBatchDto
{
    public int Id { get; set; }
    public int BudgetId { get; set; }
    public int? ProjectId { get; set; }
    public string? ProjectName { get; set; }
    public string Label { get; set; } = string.Empty;
    public int BatchCategoryId { get; set; }
    public string BatchCategoryName { get; set; } = string.Empty;
    public string? BatchCategoryIcon { get; set; }
    public ReceiptBatchStatus Status { get; set; }
    public string StatusLabel { get; set; } = string.Empty;
    public string CreatedByUserId { get; set; } = string.Empty;
    public string? CreatedByEmail { get; set; }
    public DateTime? SubmittedAt { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public DateTime? RejectedAt { get; set; }
    public string? RejectionReason { get; set; }
    public DateTime? ReimbursedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public decimal TotalAmount { get; set; }
    public int LineCount { get; set; }
    public List<ReceiptLineDto> Lines { get; set; } = new();
}

public class ReceiptLineDto
{
    public int Id { get; set; }
    public int BatchId { get; set; }
    public int SequenceNumber { get; set; }
    public string ReferenceCode { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "SEK";
    public string? Vendor { get; set; }
    public string? Description { get; set; }
    public int? LinkedTransactionId { get; set; }
    public string? DigitalReceiptUrl { get; set; }
}

public class ReceiptBatchCategoryDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? IconName { get; set; }
    public string? Description { get; set; }
}

public record CreateReceiptBatchDto(string Label, int BatchCategoryId, int? ProjectId);
public record UpdateReceiptBatchDto(string Label, int BatchCategoryId, int? ProjectId);
public record CreateReceiptLineDto(int BudgetId, DateTime Date, decimal Amount, string? Vendor, string? Description);
public record UpdateReceiptLineDto(DateTime Date, decimal Amount, string? Vendor, string? Description);
public record UpdateReceiptStatusDto(ReceiptBatchStatus NewStatus, string? RejectionReason);

// ─── JOURNAL ──────────────────────────────────────────────────────────────────
public class JournalEntryDto
{
    public JournalEntryType EntryType { get; set; }
    public string TypeLabel { get; set; } = string.Empty;
    public string TypeCode { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public DateTime? EndDate { get; set; }
    public decimal Amount { get; set; }
    public string? Description { get; set; }
    public string? CategoryName { get; set; }
    public string? ProjectName { get; set; }
    public string? Status { get; set; }
    public string? ReferenceCode { get; set; }
    public int SourceId { get; set; }
    public bool HasDetail { get; set; }
    public string? MetaLine { get; set; }
    public string? CreatedByEmail { get; set; }
    public List<KonteringRowDto> KonteringRows { get; set; } = new();
    public int? ReceiptLineCount { get; set; }
}

public class JournalQuery
{
    public int BudgetId { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
    public string? SortBy { get; set; } = "Date";
    public string? SortDir { get; set; } = "desc";
    public List<JournalEntryType> IncludeTypes { get; set; } = new();
    public bool FilterByStartDate { get; set; }
    public DateTime? StartDate { get; set; }
    public bool FilterByEndDate { get; set; }
    public DateTime? EndDate { get; set; }
    public bool FilterByDescription { get; set; }
    public string? Description { get; set; }
    public bool FilterByCategory { get; set; }
    public int? CategoryId { get; set; }
    public bool FilterByProject { get; set; }
    public int? ProjectId { get; set; }
    public bool FilterByAmount { get; set; }
    public decimal? AmountMin { get; set; }
    public decimal? AmountMax { get; set; }
    public bool FilterByCreatedBy { get; set; }
    public string? CreatedByUserId { get; set; }
    public List<ReceiptBatchStatus> ReceiptStatuses { get; set; } = new();
}

// ─── SUMMARY ──────────────────────────────────────────────────────────────────
public class SummaryDto
{
    public decimal FilteredIncome { get; set; }
    public decimal FilteredExpenses { get; set; }
    public decimal FilteredTotal => FilteredIncome + FilteredExpenses;
    public decimal MonthlyIncome { get; set; }
    public decimal MonthlyExpenses { get; set; }
    public decimal MonthlyTotal => MonthlyIncome + MonthlyExpenses;
}

public class MonthlySummary
{
    public List<MonthlyRow> Rows { get; set; } = new();
}

public class MonthlyRow
{
    public int Year { get; set; }
    public int Month { get; set; }
    public decimal Income { get; set; }
    public decimal Expenses { get; set; }
    public decimal Net => Income + Expenses;
}

public class CategoryBreakdownItem
{
    public string Category { get; set; } = string.Empty;
    public decimal Total { get; set; }
}

public class PagedResult<T>
{
    public List<T> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalPages => PageSize == 0 ? 0 : (int)Math.Ceiling((double)TotalCount / PageSize);
}

// ─── AUDIT ────────────────────────────────────────────────────────────────────
public class AuditLogDto
{
    public int Id { get; set; }
    public string? UserEmail { get; set; }
    public string EntityName { get; set; } = string.Empty;
    public string EntityId { get; set; } = string.Empty;
    public AuditAction Action { get; set; }
    public string? OldValues { get; set; }
    public string? NewValues { get; set; }
    public DateTime Timestamp { get; set; }
}

// ─── IMPORT ───────────────────────────────────────────────────────────────────
public class ImportPreviewDto
{
    public List<ImportRowDto> Rows { get; set; } = new();
    public int TotalRows { get; set; }
    public int DuplicateCount { get; set; }
}

public class ImportRowDto
{
    public int RowIndex { get; set; }
    public DateTime Date { get; set; }
    public decimal Amount { get; set; }
    public string? Description { get; set; }
    public bool IsDuplicate { get; set; }
    public bool Selected { get; set; } = true;
    public int? SuggestedCategoryId { get; set; }
    public string? SuggestedCategoryName { get; set; }
}

public record ConfirmImportDto(List<int> SelectedRowIndices, int DefaultCategoryId, string SessionId);
public record ImportSessionDto(string SessionId, ImportPreviewDto Preview);
