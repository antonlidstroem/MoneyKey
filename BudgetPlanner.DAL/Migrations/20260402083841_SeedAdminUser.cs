using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BudgetPlanner.DAL.Migrations
{
    /// <inheritdoc />
    public partial class SeedAdminUser : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "AspNetUsers",
                columns: new[] { "Id", "AccessFailedCount", "ConcurrencyStamp", "CreatedAt", "Email", "EmailConfirmed", "FirstName", "LastName", "LockoutEnabled", "LockoutEnd", "NormalizedEmail", "NormalizedUserName", "PasswordHash", "PhoneNumber", "PhoneNumberConfirmed", "PreferredCulture", "SecurityStamp", "TwoFactorEnabled", "UserName" },
                values: new object[] { "admin-uuid-123", 0, "1fa6e922-e0ff-4ee1-89dd-32cbde78b9fe", new DateTime(2026, 4, 2, 8, 38, 41, 198, DateTimeKind.Utc).AddTicks(1193), "admin@budget.se", true, "Admin", "Budgetsson", false, null, "ADMIN@BUDGET.SE", "ADMIN@BUDGET.SE", "AQAAAAIAAYagAAAAEIO/H45xTL+VJmplGyhwXyTJ1mkFirmVVqty3NhQGqvCgROmF18WKKKxa8MEjhTP/Q==", null, false, "sv-SE", "dadc6806-19f7-4949-9ea7-f3677e1a30f5", false, "admin@budget.se" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "AspNetUsers",
                keyColumn: "Id",
                keyValue: "admin-uuid-123");
        }
    }
}
