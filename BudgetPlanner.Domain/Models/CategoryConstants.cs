namespace BudgetPlanner.Domain.Models;

/// <summary>
/// Stable identifiers for system seed categories defined in BudgetDbContext.OnModelCreating.
///
/// WHY THIS EXISTS: Services that auto-create transactions (Milersättning, VAB,
/// Receipt approval) need a CategoryId. Using these named constants instead of
/// magic numbers makes the relationship traceable and grep-able.
///
/// IMPORTANT: These IDs must match the HasData seed in BudgetDbContext. If you
/// ever re-seed with different IDs, update both this file and the seed data.
/// </summary>
public static class CategoryConstants
{
    public const int Salary               = 8;   // "Lön"              — Income
    public const int Milersattning        = 12;  // "Milersättning"    — Income
    public const int VabSjukfranvaro      = 11;  // "VAB/Sjukfrånvaro" — Expense
    public const int Transport            = 3;   // "Transport"        — Expense (used by receipt approval)
    public const int Mat                  = 1;   // "Mat"              — Expense
    public const int HusDrift             = 2;   // "Hus & drift"      — Expense
    public const int Fritid               = 4;   // "Fritid"           — Expense
    public const int Barn                 = 5;   // "Barn"             — Expense
    public const int StreamingTjanster    = 6;   // "Streaming-tjänster" — Expense
    public const int SaasProdukter        = 7;   // "SaaS-produkter"   — Expense
    public const int Bidrag               = 9;   // "Bidrag"           — Income
    public const int Hobbyverksamhet      = 10;  // "Hobbyverksamhet"  — Income
}
