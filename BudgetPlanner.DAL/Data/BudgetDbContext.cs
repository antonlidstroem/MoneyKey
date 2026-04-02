using BudgetPlanner.DAL.Models;
using BudgetPlanner.Domain.Enums;
using BudgetPlanner.Domain.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace BudgetPlanner.DAL.Data;

public class BudgetDbContext : IdentityDbContext<ApplicationUser>
{
    public BudgetDbContext(DbContextOptions<BudgetDbContext> options) : base(options) { }

    public DbSet<Budget>               Budgets               => Set<Budget>();
    public DbSet<BudgetMembership>     BudgetMemberships     => Set<BudgetMembership>();
    public DbSet<Category>             Categories            => Set<Category>();
    public DbSet<Transaction>          Transactions          => Set<Transaction>();
    public DbSet<Project>              Projects              => Set<Project>();
    public DbSet<KonteringRow>         KonteringRows         => Set<KonteringRow>();
    public DbSet<MilersattningEntry>   MilersattningEntries  => Set<MilersattningEntry>();
    public DbSet<VabEntry>             VabEntries            => Set<VabEntry>();
    public DbSet<ReceiptBatch>         ReceiptBatches        => Set<ReceiptBatch>();
    public DbSet<ReceiptLine>          ReceiptLines          => Set<ReceiptLine>();
    public DbSet<ReceiptBatchCategory> ReceiptBatchCategories => Set<ReceiptBatchCategory>();
    public DbSet<RefreshToken>         RefreshTokens         => Set<RefreshToken>();
    public DbSet<AuditLog>             AuditLogs             => Set<AuditLog>();
    public DbSet<AppSetting>           AppSettings           => Set<AppSetting>();

    protected override void OnModelCreating(ModelBuilder mb)
    {
        base.OnModelCreating(mb);

        mb.Entity<Budget>(e => {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.HasMany(x => x.Memberships).WithOne(m => m.Budget).HasForeignKey(m => m.BudgetId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Transactions).WithOne(t => t.Budget).HasForeignKey(t => t.BudgetId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Projects).WithOne(p => p.Budget).HasForeignKey(p => p.BudgetId).OnDelete(DeleteBehavior.Cascade);
        });

        mb.Entity<BudgetMembership>(e => {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.BudgetId, x.UserId }).IsUnique();
            e.Ignore(x => x.IsAccepted);
        });

        mb.Entity<Category>(e => {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(100).IsRequired();
        });

        mb.Entity<Transaction>(e => {
            e.HasKey(x => x.Id);
            e.Property(x => x.NetAmount).HasColumnType("decimal(18,2)");
            e.Property(x => x.GrossAmount).HasColumnType("decimal(18,2)");
            e.Property(x => x.Rate).HasColumnType("decimal(18,4)");

            e.HasOne(x => x.Category)
                .WithMany()
                .HasForeignKey(x => x.CategoryId)
                .OnDelete(DeleteBehavior.Restrict);

            // ÄNDRA DENNA RAD: Från SetNull till Restrict
            e.HasOne(x => x.Project)
                .WithMany(p => p.Transactions)
                .HasForeignKey(x => x.ProjectId)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.Restrict); // <--- Ändrad här från SetNull

            e.HasMany(x => x.KonteringRows)
                .WithOne(k => k.Transaction)
                .HasForeignKey(k => k.TransactionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        mb.Entity<Project>(e => {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.BudgetAmount).HasColumnType("decimal(18,2)");
        });

        mb.Entity<KonteringRow>(e => {
            e.HasKey(x => x.Id);
            e.Property(x => x.Amount).HasColumnType("decimal(18,2)");
            e.Property(x => x.Percentage).HasColumnType("decimal(5,2)");
            e.Property(x => x.KontoNr).HasMaxLength(20);
        });

        mb.Entity<MilersattningEntry>(e => {
            e.HasKey(x => x.Id);
            e.Property(x => x.DistanceKm).HasColumnType("decimal(10,2)");
            e.Property(x => x.RatePerKm).HasColumnType("decimal(10,4)");
            e.Ignore(x => x.ReimbursementAmount);

            // ÄNDRA HÄR: Från SetNull till Restrict
            e.HasOne(x => x.LinkedTransaction)
                .WithMany()
                .HasForeignKey(x => x.LinkedTransactionId)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.Restrict);
        });

        mb.Entity<VabEntry>(e => {
            e.HasKey(x => x.Id);
            e.Property(x => x.DailyBenefit).HasColumnType("decimal(18,2)");
            e.Property(x => x.Rate).HasColumnType("decimal(5,4)");
            e.Ignore(x => x.TotalDays);
            e.Ignore(x => x.TotalAmount);

            // ÄNDRA HÄR: Från SetNull till Restrict
            e.HasOne(x => x.LinkedTransaction)
                .WithMany()
                .HasForeignKey(x => x.LinkedTransactionId)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.Restrict);
        });

        mb.Entity<ReceiptBatchCategory>(e => {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(100).IsRequired();
        });

        mb.Entity<ReceiptBatch>(e => {
            e.HasKey(x => x.Id);
            e.Property(x => x.Label).HasMaxLength(200).IsRequired();

            e.HasOne(x => x.Budget)
                .WithMany()
                .HasForeignKey(x => x.BudgetId)
                .OnDelete(DeleteBehavior.Cascade);

            // ÄNDRA DENNA RAD: Från SetNull till Restrict
            e.HasOne(x => x.Project)
                .WithMany(p => p.ReceiptBatches)
                .HasForeignKey(x => x.ProjectId)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.Restrict); // <--- Ändrad här

            e.HasOne(x => x.Category)
                .WithMany()
                .HasForeignKey(x => x.BatchCategoryId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasMany(x => x.Lines)
                .WithOne(l => l.Batch)
                .HasForeignKey(l => l.BatchId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        mb.Entity<ReceiptLine>(e => {
            e.HasKey(x => x.Id);
            e.Property(x => x.Amount).HasColumnType("decimal(18,2)");
            e.Property(x => x.ReferenceCode).HasMaxLength(20);
            e.Property(x => x.Currency).HasMaxLength(3);

            // ÄNDRA HÄR: Från SetNull till Restrict
            e.HasOne(x => x.LinkedTransaction)
                .WithMany()
                .HasForeignKey(x => x.LinkedTransactionId)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.Restrict); // <--- Fixen här

            e.HasIndex(x => new { x.BatchId, x.SequenceNumber }).IsUnique();
        });

        mb.Entity<RefreshToken>(e => {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.TokenHash);
            e.Ignore(x => x.IsExpired);
            e.Ignore(x => x.IsRevoked);
            e.Ignore(x => x.IsActive);
        });

        mb.Entity<AuditLog>(e => {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.BudgetId, x.Timestamp });
        });

        mb.Entity<AppSetting>(e => {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.BudgetId, x.Key }).IsUnique();
        });

        // Seed system categories
        mb.Entity<Category>().HasData(
            new Category { Id = 1,  Name = "Mat",                Type = TransactionType.Expense, IsSystemCategory = true },
            new Category { Id = 2,  Name = "Hus & drift",        Type = TransactionType.Expense, IsSystemCategory = true },
            new Category { Id = 3,  Name = "Transport",          Type = TransactionType.Expense, IsSystemCategory = true },
            new Category { Id = 4,  Name = "Fritid",             Type = TransactionType.Expense, IsSystemCategory = true },
            new Category { Id = 5,  Name = "Barn",               Type = TransactionType.Expense, IsSystemCategory = true },
            new Category { Id = 6,  Name = "Streaming-tjänster", Type = TransactionType.Expense, IsSystemCategory = true },
            new Category { Id = 7,  Name = "SaaS-produkter",     Type = TransactionType.Expense, IsSystemCategory = true },
            new Category { Id = 8,  Name = "Lön",                Type = TransactionType.Income,  IsSystemCategory = true, ToggleGrossNet = true, DefaultRate = 30, AdjustmentType = AdjustmentType.Deduction },
            new Category { Id = 9,  Name = "Bidrag",             Type = TransactionType.Income,  IsSystemCategory = true },
            new Category { Id = 10, Name = "Hobbyverksamhet",    Type = TransactionType.Income,  IsSystemCategory = true },
            new Category { Id = 11, Name = "VAB/Sjukfrånvaro",   Type = TransactionType.Expense, IsSystemCategory = true, HasEndDate = true, ToggleGrossNet = true, DefaultRate = 80, AdjustmentType = AdjustmentType.Deduction },
            new Category { Id = 12, Name = "Milersättning",      Type = TransactionType.Income,  IsSystemCategory = true }
        );

        // Seed ReceiptBatchCategories
        mb.Entity<ReceiptBatchCategory>().HasData(
            new ReceiptBatchCategory { Id = 1, Name = "Resor & Transport",      IconName = "directions_car", SortOrder = 1, Description = "Tåg, flyg, hotell, parkering, taxi" },
            new ReceiptBatchCategory { Id = 2, Name = "Representation",         IconName = "restaurant",     SortOrder = 2, Description = "Kundluncher, middag, presentkort" },
            new ReceiptBatchCategory { Id = 3, Name = "Kontor & Förbrukning",   IconName = "shopping_bag",   SortOrder = 3, Description = "Papper, pennor, rengöring" },
            new ReceiptBatchCategory { Id = 4, Name = "IT & Utrustning",        IconName = "devices",        SortOrder = 4, Description = "Kablar, tillbehör, mjukvara" },
            new ReceiptBatchCategory { Id = 5, Name = "Utbildning & Konferens", IconName = "school",         SortOrder = 5, Description = "Kurser, litteratur, konferensavgifter" },
            new ReceiptBatchCategory { Id = 6, Name = "Tjänster & Konsulting",  IconName = "handshake",      SortOrder = 6, Description = "Externa tjänster, prenumerationer" },
            new ReceiptBatchCategory { Id = 7, Name = "Övrigt",                 IconName = "more_horiz",     SortOrder = 7, Description = "Utlägg som inte passar annan kategori" }
        );


        // I BudgetDbContext.cs -> OnModelCreating
        var hasher = new PasswordHasher<ApplicationUser>();
        var adminUser = new ApplicationUser
        {
            Id = "admin-uuid-123", // Ge den ett statiskt ID
            UserName = "admin@budget.se",
            NormalizedUserName = "ADMIN@BUDGET.SE",
            Email = "admin@budget.se",
            NormalizedEmail = "ADMIN@BUDGET.SE",
            EmailConfirmed = true,
            FirstName = "Admin",
            LastName = "Budgetsson",
            SecurityStamp = Guid.NewGuid().ToString()
        };

        adminUser.PasswordHash = hasher.HashPassword(adminUser, "Nygatan12!");

        mb.Entity<ApplicationUser>().HasData(adminUser);


    }
}
