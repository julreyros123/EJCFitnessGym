using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EJCFitnessGym.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddFinanceExpensesAndAlerts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FinanceAlertLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AlertType = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Trigger = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    Severity = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Message = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    RealtimePublished = table.Column<bool>(type: "bit", nullable: false),
                    EmailAttempted = table.Column<bool>(type: "bit", nullable: false),
                    EmailSucceeded = table.Column<bool>(type: "bit", nullable: false),
                    PayloadJson = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinanceAlertLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FinanceExpenseRecords",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(140)", maxLength: 140, nullable: false),
                    Category = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    ExpenseDateUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsRecurring = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinanceExpenseRecords", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FinanceAlertLogs_AlertType_CreatedUtc",
                table: "FinanceAlertLogs",
                columns: new[] { "AlertType", "CreatedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_FinanceExpenseRecords_ExpenseDateUtc_Category",
                table: "FinanceExpenseRecords",
                columns: new[] { "ExpenseDateUtc", "Category" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FinanceAlertLogs");

            migrationBuilder.DropTable(
                name: "FinanceExpenseRecords");
        }
    }
}
