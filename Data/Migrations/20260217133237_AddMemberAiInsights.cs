using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EJCFitnessGym.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMemberAiInsights : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MemberRetentionActions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MemberUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    ActionType = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    SegmentLabel = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    SuggestedOffer = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    DueDateUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    UpdatedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MemberRetentionActions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MemberSegmentSnapshots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MemberUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    ClusterId = table.Column<int>(type: "int", nullable: false),
                    SegmentLabel = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    SegmentDescription = table.Column<string>(type: "nvarchar(220)", maxLength: 220, nullable: false),
                    TotalSpending = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    BillingActivityCount = table.Column<int>(type: "int", nullable: false),
                    MembershipMonths = table.Column<decimal>(type: "decimal(8,2)", precision: 8, scale: 2, nullable: false),
                    CapturedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CapturedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MemberSegmentSnapshots", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MemberRetentionActions_MemberUserId_Status_ActionType",
                table: "MemberRetentionActions",
                columns: new[] { "MemberUserId", "Status", "ActionType" });

            migrationBuilder.CreateIndex(
                name: "IX_MemberRetentionActions_Status_DueDateUtc",
                table: "MemberRetentionActions",
                columns: new[] { "Status", "DueDateUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_MemberSegmentSnapshots_CapturedAtUtc",
                table: "MemberSegmentSnapshots",
                column: "CapturedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_MemberSegmentSnapshots_MemberUserId_CapturedAtUtc",
                table: "MemberSegmentSnapshots",
                columns: new[] { "MemberUserId", "CapturedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MemberRetentionActions");

            migrationBuilder.DropTable(
                name: "MemberSegmentSnapshots");
        }
    }
}
