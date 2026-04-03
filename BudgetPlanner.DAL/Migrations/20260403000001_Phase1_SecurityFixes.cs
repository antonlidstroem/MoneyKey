using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BudgetPlanner.DAL.Migrations
{
    /// <summary>
    /// Phase 1 security fix: removes the hard-coded admin user that was seeded
    /// by <c>SeedAdminUser</c> migration.  The new <c>DbInitializer</c> creates
    /// an admin account on first run using credentials from configuration
    /// (never from source code).
    /// </summary>
    public partial class Phase1_SecurityFixes : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Conditional delete — safe whether or not the row exists.
            migrationBuilder.Sql(
                "IF EXISTS (SELECT 1 FROM [AspNetUsers] WHERE [Id] = 'admin-uuid-123') " +
                "DELETE FROM [AspNetUsers] WHERE [Id] = 'admin-uuid-123';");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Re-inserting the seeded admin on rollback is intentionally omitted.
            // To restore a dev admin, set AdminSetup config and let DbInitializer run.
        }
    }
}
