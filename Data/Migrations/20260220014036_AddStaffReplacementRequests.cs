using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EJCFitnessGym.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddStaffReplacementRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ReplacementRequests",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RequestNumber = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    BranchId = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    RequestedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    Subject = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    RequestType = table.Column<int>(type: "int", nullable: false),
                    Priority = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ReviewedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    AdminNotes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ResolvedUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReplacementRequests", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ReplacementRequests_BranchId_Status_CreatedUtc",
                table: "ReplacementRequests",
                columns: new[] { "BranchId", "Status", "CreatedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ReplacementRequests_RequestedByUserId_CreatedUtc",
                table: "ReplacementRequests",
                columns: new[] { "RequestedByUserId", "CreatedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ReplacementRequests_RequestNumber",
                table: "ReplacementRequests",
                column: "RequestNumber",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReplacementRequests");
        }
    }
}
