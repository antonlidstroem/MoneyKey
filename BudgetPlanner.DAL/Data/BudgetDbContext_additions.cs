// ═══════════════════════════════════════════════════════════════════════════════
// BudgetDbContext additions for task lists.
//
// 1. Add these two DbSet properties inside BudgetDbContext:
//
//    public DbSet<TaskList>  TaskLists  => Set<TaskList>();
//    public DbSet<TaskItem>  TaskItems  => Set<TaskItem>();
//
// 2. Add the following entity configuration blocks inside OnModelCreating,
//    after the existing ReceiptLine configuration:
// ═══════════════════════════════════════════════════════════════════════════════

// ── Paste inside OnModelCreating ──────────────────────────────────────────────

