using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EJCFitnessGym.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBranchRegistryForSuperAdmin : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BranchRecords",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BranchId = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BranchRecords", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BranchRecords_BranchId",
                table: "BranchRecords",
                column: "BranchId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BranchRecords_IsActive_BranchId",
                table: "BranchRecords",
                columns: new[] { "IsActive", "BranchId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BranchRecords");
        }
    }
}
