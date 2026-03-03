using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EJCFitnessGym.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAutoBillingTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SavedPaymentMethods",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MemberUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    GatewayProvider = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    GatewayCustomerId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    GatewayPaymentMethodId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    PaymentMethodType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    DisplayLabel = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    IsDefault = table.Column<bool>(type: "bit", nullable: false),
                    AutoBillingEnabled = table.Column<bool>(type: "bit", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastUsedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    FailedAttempts = table.Column<int>(type: "int", nullable: false),
                    LastFailedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SavedPaymentMethods", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AutoBillingAttempts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    InvoiceId = table.Column<int>(type: "int", nullable: false),
                    SavedPaymentMethodId = table.Column<int>(type: "int", nullable: false),
                    AttemptedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Succeeded = table.Column<bool>(type: "bit", nullable: false),
                    GatewayStatus = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    GatewayPaymentIntentId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ErrorMessage = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    PaymentId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AutoBillingAttempts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AutoBillingAttempts_Invoices_InvoiceId",
                        column: x => x.InvoiceId,
                        principalTable: "Invoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.NoAction);
                    table.ForeignKey(
                        name: "FK_AutoBillingAttempts_Payments_PaymentId",
                        column: x => x.PaymentId,
                        principalTable: "Payments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.NoAction);
                    table.ForeignKey(
                        name: "FK_AutoBillingAttempts_SavedPaymentMethods_SavedPaymentMethodId",
                        column: x => x.SavedPaymentMethodId,
                        principalTable: "SavedPaymentMethods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.NoAction);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AutoBillingAttempts_InvoiceId_AttemptedAtUtc",
                table: "AutoBillingAttempts",
                columns: new[] { "InvoiceId", "AttemptedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AutoBillingAttempts_PaymentId",
                table: "AutoBillingAttempts",
                column: "PaymentId");

            migrationBuilder.CreateIndex(
                name: "IX_AutoBillingAttempts_SavedPaymentMethodId",
                table: "AutoBillingAttempts",
                column: "SavedPaymentMethodId");

            migrationBuilder.CreateIndex(
                name: "IX_SavedPaymentMethods_GatewayProvider_GatewayPaymentMethodId",
                table: "SavedPaymentMethods",
                columns: new[] { "GatewayProvider", "GatewayPaymentMethodId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SavedPaymentMethods_MemberUserId_GatewayProvider_IsActive",
                table: "SavedPaymentMethods",
                columns: new[] { "MemberUserId", "GatewayProvider", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_SavedPaymentMethods_MemberUserId_IsDefault_IsActive",
                table: "SavedPaymentMethods",
                columns: new[] { "MemberUserId", "IsDefault", "IsActive" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AutoBillingAttempts");

            migrationBuilder.DropTable(
                name: "SavedPaymentMethods");
        }
    }
}
