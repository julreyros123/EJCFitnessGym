using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EJCFitnessGym.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBranchScopeToBillingAndFinance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BranchId",
                table: "Payments",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BranchId",
                table: "Invoices",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BranchId",
                table: "GymEquipmentAssets",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BranchId",
                table: "FinanceExpenseRecords",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Payments_BranchId_PaidAtUtc",
                table: "Payments",
                columns: new[] { "BranchId", "PaidAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_BranchId_Status_DueDateUtc",
                table: "Invoices",
                columns: new[] { "BranchId", "Status", "DueDateUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_GymEquipmentAssets_BranchId_Category_Name",
                table: "GymEquipmentAssets",
                columns: new[] { "BranchId", "Category", "Name" });

            migrationBuilder.CreateIndex(
                name: "IX_FinanceExpenseRecords_BranchId_ExpenseDateUtc_Category",
                table: "FinanceExpenseRecords",
                columns: new[] { "BranchId", "ExpenseDateUtc", "Category" });

            migrationBuilder.Sql(
                """
                UPDATE invoice
                SET invoice.BranchId = branchClaim.ClaimValue
                FROM Invoices AS invoice
                CROSS APPLY
                (
                    SELECT TOP (1) claim.ClaimValue
                    FROM AspNetUserClaims AS claim
                    WHERE claim.UserId = invoice.MemberUserId
                      AND claim.ClaimType = 'branch_id'
                      AND claim.ClaimValue IS NOT NULL
                    ORDER BY claim.Id DESC
                ) AS branchClaim
                WHERE invoice.BranchId IS NULL;
                """);

            migrationBuilder.Sql(
                """
                UPDATE payment
                SET payment.BranchId = invoice.BranchId
                FROM Payments AS payment
                INNER JOIN Invoices AS invoice
                    ON invoice.Id = payment.InvoiceId
                WHERE payment.BranchId IS NULL
                  AND invoice.BranchId IS NOT NULL;
                """);

            migrationBuilder.Sql(
                """
                DECLARE @DefaultBranchId nvarchar(32) =
                (
                    SELECT TOP (1) branch.BranchId
                    FROM BranchRecords AS branch
                    WHERE branch.IsActive = 1
                    ORDER BY branch.UpdatedUtc DESC, branch.Id DESC
                );

                IF @DefaultBranchId IS NOT NULL
                BEGIN
                    UPDATE FinanceExpenseRecords
                    SET BranchId = @DefaultBranchId
                    WHERE BranchId IS NULL;

                    UPDATE GymEquipmentAssets
                    SET BranchId = @DefaultBranchId
                    WHERE BranchId IS NULL;
                END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Payments_BranchId_PaidAtUtc",
                table: "Payments");

            migrationBuilder.DropIndex(
                name: "IX_Invoices_BranchId_Status_DueDateUtc",
                table: "Invoices");

            migrationBuilder.DropIndex(
                name: "IX_GymEquipmentAssets_BranchId_Category_Name",
                table: "GymEquipmentAssets");

            migrationBuilder.DropIndex(
                name: "IX_FinanceExpenseRecords_BranchId_ExpenseDateUtc_Category",
                table: "FinanceExpenseRecords");

            migrationBuilder.DropColumn(
                name: "BranchId",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "BranchId",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "BranchId",
                table: "GymEquipmentAssets");

            migrationBuilder.DropColumn(
                name: "BranchId",
                table: "FinanceExpenseRecords");
        }
    }
}
