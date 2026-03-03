using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EJCFitnessGym.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddGeneralLedgerModule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GeneralLedgerAccounts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BranchId = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    Code = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    AccountType = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GeneralLedgerAccounts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GeneralLedgerEntries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BranchId = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    EntryNumber = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    EntryDateUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    SourceType = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    SourceId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    CreatedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GeneralLedgerEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GeneralLedgerLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EntryId = table.Column<int>(type: "int", nullable: false),
                    AccountId = table.Column<int>(type: "int", nullable: false),
                    Memo = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Debit = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Credit = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GeneralLedgerLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GeneralLedgerLines_GeneralLedgerAccounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "GeneralLedgerAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GeneralLedgerLines_GeneralLedgerEntries_EntryId",
                        column: x => x.EntryId,
                        principalTable: "GeneralLedgerEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GeneralLedgerAccounts_BranchId_AccountType_IsActive",
                table: "GeneralLedgerAccounts",
                columns: new[] { "BranchId", "AccountType", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_GeneralLedgerAccounts_BranchId_Code",
                table: "GeneralLedgerAccounts",
                columns: new[] { "BranchId", "Code" },
                unique: true,
                filter: "[BranchId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_GeneralLedgerEntries_BranchId_EntryDateUtc",
                table: "GeneralLedgerEntries",
                columns: new[] { "BranchId", "EntryDateUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_GeneralLedgerEntries_BranchId_SourceType_SourceId",
                table: "GeneralLedgerEntries",
                columns: new[] { "BranchId", "SourceType", "SourceId" },
                unique: true,
                filter: "[SourceType] IS NOT NULL AND [SourceId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_GeneralLedgerEntries_EntryNumber",
                table: "GeneralLedgerEntries",
                column: "EntryNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GeneralLedgerLines_AccountId",
                table: "GeneralLedgerLines",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_GeneralLedgerLines_EntryId_AccountId",
                table: "GeneralLedgerLines",
                columns: new[] { "EntryId", "AccountId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GeneralLedgerLines");

            migrationBuilder.DropTable(
                name: "GeneralLedgerAccounts");

            migrationBuilder.DropTable(
                name: "GeneralLedgerEntries");
        }
    }
}
