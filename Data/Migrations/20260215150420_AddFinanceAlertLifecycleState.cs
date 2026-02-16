using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EJCFitnessGym.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddFinanceAlertLifecycleState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AcknowledgedBy",
                table: "FinanceAlertLogs",
                type: "nvarchar(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "AcknowledgedUtc",
                table: "FinanceAlertLogs",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResolutionNote",
                table: "FinanceAlertLogs",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResolvedBy",
                table: "FinanceAlertLogs",
                type: "nvarchar(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ResolvedUtc",
                table: "FinanceAlertLogs",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "State",
                table: "FinanceAlertLogs",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<DateTime>(
                name: "StateUpdatedUtc",
                table: "FinanceAlertLogs",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_FinanceAlertLogs_State_CreatedUtc",
                table: "FinanceAlertLogs",
                columns: new[] { "State", "CreatedUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_FinanceAlertLogs_State_CreatedUtc",
                table: "FinanceAlertLogs");

            migrationBuilder.DropColumn(
                name: "AcknowledgedBy",
                table: "FinanceAlertLogs");

            migrationBuilder.DropColumn(
                name: "AcknowledgedUtc",
                table: "FinanceAlertLogs");

            migrationBuilder.DropColumn(
                name: "ResolutionNote",
                table: "FinanceAlertLogs");

            migrationBuilder.DropColumn(
                name: "ResolvedBy",
                table: "FinanceAlertLogs");

            migrationBuilder.DropColumn(
                name: "ResolvedUtc",
                table: "FinanceAlertLogs");

            migrationBuilder.DropColumn(
                name: "State",
                table: "FinanceAlertLogs");

            migrationBuilder.DropColumn(
                name: "StateUpdatedUtc",
                table: "FinanceAlertLogs");
        }
    }
}
