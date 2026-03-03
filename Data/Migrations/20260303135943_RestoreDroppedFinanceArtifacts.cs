using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EJCFitnessGym.Data.Migrations
{
    /// <inheritdoc />
    public partial class RestoreDroppedFinanceArtifacts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                IF COL_LENGTH(N'dbo.ReplacementRequests', N'LinkedEquipmentAssetId') IS NULL
                BEGIN
                    ALTER TABLE [dbo].[ReplacementRequests]
                    ADD [LinkedEquipmentAssetId] int NULL;
                END
                """);

            migrationBuilder.Sql(
                """
                IF OBJECT_ID(N'[dbo].[FinanceBudgets]', N'U') IS NULL
                BEGIN
                    CREATE TABLE [dbo].[FinanceBudgets]
                    (
                        [Id] int IDENTITY(1,1) NOT NULL,
                        [BranchId] nvarchar(32) NULL,
                        [Year] int NOT NULL,
                        [Month] int NOT NULL,
                        [Category] nvarchar(80) NOT NULL,
                        [PlannedAmount] decimal(18,2) NOT NULL,
                        [Notes] nvarchar(300) NULL,
                        [CreatedUtc] datetime2 NOT NULL,
                        [UpdatedUtc] datetime2 NOT NULL,
                        CONSTRAINT [PK_FinanceBudgets] PRIMARY KEY ([Id])
                    );
                END
                """);

            migrationBuilder.Sql(
                """
                IF OBJECT_ID(N'[dbo].[FinanceBudgets]', N'U') IS NOT NULL
                   AND NOT EXISTS
                   (
                       SELECT 1
                       FROM sys.indexes
                       WHERE name = N'IX_FinanceBudgets_Branch_Period_Category'
                         AND object_id = OBJECT_ID(N'[dbo].[FinanceBudgets]')
                   )
                BEGIN
                    CREATE UNIQUE INDEX [IX_FinanceBudgets_Branch_Period_Category]
                    ON [dbo].[FinanceBudgets] ([BranchId], [Year], [Month], [Category])
                    WHERE [BranchId] IS NOT NULL;
                END
                """);

            migrationBuilder.Sql(
                """
                IF OBJECT_ID(N'[dbo].[WeeklySalesAuditRecords]', N'U') IS NULL
                BEGIN
                    CREATE TABLE [dbo].[WeeklySalesAuditRecords]
                    (
                        [Id] int IDENTITY(1,1) NOT NULL,
                        [WeekStartUtc] datetime2 NOT NULL,
                        [BranchId] nvarchar(32) NULL,
                        [Status] nvarchar(20) NOT NULL,
                        [SnapshotTotalSales] decimal(18,2) NOT NULL,
                        [SnapshotStaffCollected] decimal(18,2) NOT NULL,
                        [SnapshotGateway] decimal(18,2) NOT NULL,
                        [SnapshotTransactionCount] int NOT NULL,
                        [Notes] nvarchar(500) NULL,
                        [ReviewedByUserId] nvarchar(450) NULL,
                        [ReviewedAtUtc] datetime2 NULL,
                        [BalanceCheckPassed] bit NOT NULL,
                        [OutlierCheckPassed] bit NOT NULL,
                        [VerificationSummary] nvarchar(300) NULL,
                        [CreatedUtc] datetime2 NOT NULL,
                        [UpdatedUtc] datetime2 NOT NULL,
                        CONSTRAINT [PK_WeeklySalesAuditRecords] PRIMARY KEY ([Id])
                    );
                END
                """);

            migrationBuilder.Sql(
                """
                IF OBJECT_ID(N'[dbo].[WeeklySalesAuditRecords]', N'U') IS NOT NULL
                   AND NOT EXISTS
                   (
                       SELECT 1
                       FROM sys.indexes
                       WHERE name = N'IX_WeeklySalesAudit_Branch_Week'
                         AND object_id = OBJECT_ID(N'[dbo].[WeeklySalesAuditRecords]')
                   )
                BEGIN
                    CREATE UNIQUE INDEX [IX_WeeklySalesAudit_Branch_Week]
                    ON [dbo].[WeeklySalesAuditRecords] ([BranchId], [WeekStartUtc])
                    WHERE [BranchId] IS NOT NULL;
                END
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                IF COL_LENGTH(N'dbo.ReplacementRequests', N'LinkedEquipmentAssetId') IS NOT NULL
                BEGIN
                    ALTER TABLE [dbo].[ReplacementRequests]
                    DROP COLUMN [LinkedEquipmentAssetId];
                END
                """);

            migrationBuilder.Sql(
                """
                IF OBJECT_ID(N'[dbo].[FinanceBudgets]', N'U') IS NOT NULL
                BEGIN
                    DROP TABLE [dbo].[FinanceBudgets];
                END
                """);

            migrationBuilder.Sql(
                """
                IF OBJECT_ID(N'[dbo].[WeeklySalesAuditRecords]', N'U') IS NOT NULL
                BEGIN
                    DROP TABLE [dbo].[WeeklySalesAuditRecords];
                END
                """);
        }
    }
}
