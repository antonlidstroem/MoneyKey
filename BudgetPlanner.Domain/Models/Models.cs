using BudgetPlanner.Domain.Enums;

namespace BudgetPlanner.Domain.Models;

public class Budget
{
    public int     Id          { get; set; }
    public string  Name        { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string  OwnerId     { get; set; } = string.Empty;
    public DateTime CreatedAt  { get; set; } = DateTime.UtcNow;
    public bool    IsActive    { get; set; } = true;
    public ICollection<BudgetMembership> Memberships     { get; set; } = new List<BudgetMembership>();
    public ICollection<Transaction>      Transactions    { get; set; } = new List<Transaction>();
    public ICollection<Project>          Projects        { get; set; } = new List<Project>();
    public ICollection<Category>         CustomCategories { get; set; } = new List<Category>();
    public ICollection<AppSetting>       Settings        { get; set; } = new List<AppSetting>();
}

public class BudgetMembership
{
    public int     Id              { get; set; }
    public int     BudgetId        { get; set; }
    public string  UserId          { get; set; } = string.Empty;
    public BudgetMemberRole Role   { get; set; }
    public string? InvitedByUserId { get; set; }
    public string? InviteToken     { get; set; }
    public DateTime InvitedAt      { get; set; } = DateTime.UtcNow;
    public DateTime? AcceptedAt    { get; set; }
    public bool IsAccepted         => AcceptedAt.HasValue;
    public Budget Budget           { get; set; } = null!;
}

public class Category
{
    public int     Id               { get; set; }
    public string  Name             { get; set; } = string.Empty;
    public TransactionType Type     { get; set; }
    public bool    ToggleGrossNet   { get; set; } = false;
    public int?    DefaultRate      { get; set; }
    public AdjustmentType? AdjustmentType { get; set; }
    public string? Description      { get; set; }
    public bool    HasEndDate       { get; set; } = false;
    public bool    IsSystemCategory { get; set; } = true;
    public int?    BudgetId         { get; set; }
    public string? IconName         { get; set; }
}

public class Transaction
{
    public int      Id                   { get; set; }
    public int      BudgetId             { get; set; }
    public DateTime StartDate            { get; set; }
    public DateTime? EndDate             { get; set; }
    public decimal  NetAmount            { get; set; }
    public decimal? GrossAmount          { get; set; }
    public string?  Description          { get; set; }
    public int      CategoryId           { get; set; }
    public Recurrence Recurrence         { get; set; }
    public bool     IsActive             { get; set; } = true;
    public BudgetMonth? Month            { get; set; }
    public decimal? Rate                 { get; set; }
    public TransactionType Type          { get; set; }
    public int?     ProjectId            { get; set; }
    public bool     HasKontering         { get; set; } = false;
    public int?     MilersattningEntryId { get; set; }
    public int?     VabEntryId           { get; set; }
    public string?  CreatedByUserId      { get; set; }
    public string?  UpdatedByUserId      { get; set; }
    public DateTime CreatedAt            { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt           { get; set; }
    public Budget    Budget              { get; set; } = null!;
    public Category  Category            { get; set; } = null!;
    public Project?  Project             { get; set; }
    public ICollection<KonteringRow> KonteringRows { get; set; } = new List<KonteringRow>();
}

public class Project
{
    public int      Id           { get; set; }
    public int      BudgetId     { get; set; }
    public string   Name         { get; set; } = string.Empty;
    public string?  Description  { get; set; }
    public decimal  BudgetAmount { get; set; }
    public DateTime StartDate    { get; set; }
    public DateTime? EndDate     { get; set; }
    public bool     IsActive     { get; set; } = true;
    public DateTime CreatedAt    { get; set; } = DateTime.UtcNow;
    public Budget   Budget       { get; set; } = null!;
    public ICollection<Transaction>  Transactions  { get; set; } = new List<Transaction>();
    public ICollection<ReceiptBatch> ReceiptBatches { get; set; } = new List<ReceiptBatch>();
}

public class KonteringRow
{
    public int      Id            { get; set; }
    public int      TransactionId { get; set; }
    public string   KontoNr       { get; set; } = string.Empty;
    public string?  CostCenter    { get; set; }
    public decimal  Amount        { get; set; }
    public decimal? Percentage    { get; set; }
    public string?  Description   { get; set; }
    public Transaction Transaction { get; set; } = null!;
}

public class MilersattningEntry
{
    public int      Id                  { get; set; }
    public int      BudgetId            { get; set; }
    public string   UserId              { get; set; } = string.Empty;
    public DateTime TripDate            { get; set; }
    public string   FromLocation        { get; set; } = string.Empty;
    public string   ToLocation          { get; set; } = string.Empty;
    public decimal  DistanceKm          { get; set; }
    public decimal  RatePerKm           { get; set; } = 0.25m;
    public string?  Purpose             { get; set; }
    public decimal  ReimbursementAmount => DistanceKm * RatePerKm;
    public int?     LinkedTransactionId { get; set; }
    public DateTime CreatedAt           { get; set; } = DateTime.UtcNow;
    public Budget   Budget              { get; set; } = null!;
    public Transaction? LinkedTransaction { get; set; }
}

public class VabEntry
{
    public int      Id                  { get; set; }
    public int      BudgetId            { get; set; }
    public string   UserId              { get; set; } = string.Empty;
    public string?  ChildName           { get; set; }
    public DateTime StartDate           { get; set; }
    public DateTime EndDate             { get; set; }
    public decimal  DailyBenefit        { get; set; }
    public decimal  Rate                { get; set; } = 0.80m;
    public int      TotalDays           => Math.Max(1, (int)(EndDate - StartDate).TotalDays + 1);
    public decimal  TotalAmount         => TotalDays * DailyBenefit * Rate;
    public int?     LinkedTransactionId { get; set; }
    public DateTime CreatedAt           { get; set; } = DateTime.UtcNow;
    public Budget   Budget              { get; set; } = null!;
    public Transaction? LinkedTransaction { get; set; }
}

public class ReceiptBatch
{
    public int      Id               { get; set; }
    public int      BudgetId         { get; set; }
    public int?     ProjectId        { get; set; }
    public string   Label            { get; set; } = string.Empty;
    public int      BatchCategoryId  { get; set; }
    public ReceiptBatchStatus Status { get; set; } = ReceiptBatchStatus.Draft;
    public string   CreatedByUserId  { get; set; } = string.Empty;
    public string?  CreatedByEmail   { get; set; }
    public DateTime? SubmittedAt     { get; set; }
    public DateTime? ApprovedAt      { get; set; }
    public string?  ApprovedByUserId { get; set; }
    public DateTime? RejectedAt      { get; set; }
    public string?  RejectedByUserId { get; set; }
    public string?  RejectionReason  { get; set; }
    public DateTime? ReimbursedAt    { get; set; }
    public DateTime CreatedAt        { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt       { get; set; }
    public Budget   Budget           { get; set; } = null!;
    public Project? Project          { get; set; }
    public ReceiptBatchCategory Category { get; set; } = null!;
    public ICollection<ReceiptLine> Lines { get; set; } = new List<ReceiptLine>();
}

public class ReceiptLine
{
    public int      Id                  { get; set; }
    public int      BatchId             { get; set; }
    public int      SequenceNumber      { get; set; }
    public string   ReferenceCode       { get; set; } = string.Empty;
    public DateTime Date                { get; set; }
    public decimal  Amount              { get; set; }
    public string   Currency            { get; set; } = "SEK";
    public string?  Vendor              { get; set; }
    public string?  Description         { get; set; }
    public int?     LinkedTransactionId { get; set; }
    public string?  AttachmentPath      { get; set; }   // reserved for future digital attachment
    public string?  AttachmentMimeType  { get; set; }   // reserved
    public string?  DigitalReceiptUrl   { get; set; }   // reserved
    public DateTime CreatedAt           { get; set; } = DateTime.UtcNow;
    public ReceiptBatch Batch           { get; set; } = null!;
    public Transaction? LinkedTransaction { get; set; }
}

public class ReceiptBatchCategory
{
    public int     Id          { get; set; }
    public string  Name        { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? IconName    { get; set; }
    public int     SortOrder   { get; set; }
    public bool    IsActive    { get; set; } = true;
}

public class RefreshToken
{
    public int      Id              { get; set; }
    public string   UserId          { get; set; } = string.Empty;
    public string   TokenHash       { get; set; } = string.Empty;
    public DateTime ExpiresAt       { get; set; }
    public DateTime CreatedAt       { get; set; } = DateTime.UtcNow;
    public DateTime? RevokedAt      { get; set; }
    public string?  ReplacedByToken { get; set; }
    public bool IsExpired           => DateTime.UtcNow >= ExpiresAt;
    public bool IsRevoked           => RevokedAt.HasValue;
    public bool IsActive            => !IsRevoked && !IsExpired;
}

public class AuditLog
{
    public int      Id         { get; set; }
    public int      BudgetId   { get; set; }
    public string?  UserId     { get; set; }
    public string?  UserEmail  { get; set; }
    public string   EntityName { get; set; } = string.Empty;
    public string   EntityId   { get; set; } = string.Empty;
    public AuditAction Action  { get; set; }
    public string?  OldValues  { get; set; }
    public string?  NewValues  { get; set; }
    public DateTime Timestamp  { get; set; } = DateTime.UtcNow;
}

public class AppSetting
{
    public int    Id       { get; set; }
    public int    BudgetId { get; set; }
    public string Key      { get; set; } = string.Empty;
    public string Value    { get; set; } = string.Empty;
    public Budget Budget   { get; set; } = null!;
}
