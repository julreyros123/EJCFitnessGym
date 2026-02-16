using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EJCFitnessGym.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddIntegrationOutboxSchemaAndConstraints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "ReferenceNumber",
                table: "Payments",
                type: "nvarchar(450)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "GatewayProvider",
                table: "Payments",
                type: "nvarchar(450)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "GatewayPaymentId",
                table: "Payments",
                type: "nvarchar(450)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.CreateTable(
                name: "InboundWebhookReceipts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Provider = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    EventKey = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    EventType = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    ExternalReference = table.Column<string>(type: "nvarchar(180)", maxLength: 180, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    AttemptCount = table.Column<int>(type: "int", nullable: false),
                    FirstReceivedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastAttemptUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ProcessedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InboundWebhookReceipts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "IntegrationOutboxMessages",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Target = table.Column<int>(type: "int", nullable: false),
                    EventType = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Message = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    TargetValue = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    PayloadJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    AttemptCount = table.Column<int>(type: "int", nullable: false),
                    LastError = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    NextAttemptUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastAttemptUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ProcessedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IntegrationOutboxMessages", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Payments_GatewayProvider_GatewayPaymentId",
                table: "Payments",
                columns: new[] { "GatewayProvider", "GatewayPaymentId" },
                unique: true,
                filter: "[GatewayProvider] IS NOT NULL AND [GatewayPaymentId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_GatewayProvider_ReferenceNumber",
                table: "Payments",
                columns: new[] { "GatewayProvider", "ReferenceNumber" },
                unique: true,
                filter: "[GatewayProvider] IS NOT NULL AND [ReferenceNumber] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_InboundWebhookReceipts_Provider_EventKey",
                table: "InboundWebhookReceipts",
                columns: new[] { "Provider", "EventKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InboundWebhookReceipts_Provider_Status_UpdatedUtc",
                table: "InboundWebhookReceipts",
                columns: new[] { "Provider", "Status", "UpdatedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_IntegrationOutboxMessages_CreatedUtc",
                table: "IntegrationOutboxMessages",
                column: "CreatedUtc");

            migrationBuilder.CreateIndex(
                name: "IX_IntegrationOutboxMessages_Status_NextAttemptUtc",
                table: "IntegrationOutboxMessages",
                columns: new[] { "Status", "NextAttemptUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InboundWebhookReceipts");

            migrationBuilder.DropTable(
                name: "IntegrationOutboxMessages");

            migrationBuilder.DropIndex(
                name: "IX_Payments_GatewayProvider_GatewayPaymentId",
                table: "Payments");

            migrationBuilder.DropIndex(
                name: "IX_Payments_GatewayProvider_ReferenceNumber",
                table: "Payments");

            migrationBuilder.AlterColumn<string>(
                name: "ReferenceNumber",
                table: "Payments",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "GatewayProvider",
                table: "Payments",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "GatewayPaymentId",
                table: "Payments",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)",
                oldNullable: true);
        }
    }
}
