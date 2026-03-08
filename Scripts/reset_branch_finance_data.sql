/*
Purpose
-------
Reset finance-related data for a single branch in the local development database
without touching users, roles, membership profiles, or subscription plans.

How to use
----------
1. Review the flags below.
2. Run once with @DryRun = 1 to preview row counts.
3. Change @DryRun = 0 and run again to execute the deletes.
4. Restart the app after the cleanup.

Notes
-----
- This script is meant for development/local cleanup.
- Billing reset deletes invoices and payments for the branch. That changes member
  billing history, but it also clears revenue and finance queue counts.
- Finance alert logs are not branch-scoped in the schema, so clearing them wipes
  all finance alerts. That section is off by default.
*/

SET NOCOUNT ON;
SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET QUOTED_IDENTIFIER ON;
SET NUMERIC_ROUNDABORT OFF;

DECLARE @BranchId nvarchar(32) = N'BR-CENTRAL';
DECLARE @DryRun bit = 1;

DECLARE @ResetBilling bit = 1;
DECLARE @ResetExpenses bit = 1;
DECLARE @ResetEquipment bit = 1;
DECLARE @ResetWeeklySalesAudit bit = 1;
DECLARE @ResetInventorySales bit = 1;
DECLARE @ResetSupplyRequests bit = 1;
DECLARE @ResetManualLedger bit = 0;
DECLARE @ResetFinanceAlerts bit = 0;

DECLARE @InvoiceIds TABLE (Id int PRIMARY KEY);
DECLARE @PaymentIds TABLE (Id int PRIMARY KEY);
DECLARE @ExpenseIds TABLE (Id int PRIMARY KEY);
DECLARE @ProductSaleIds TABLE (Id int PRIMARY KEY);
DECLARE @LedgerEntryIds TABLE (Id int PRIMARY KEY);

IF @ResetBilling = 1
BEGIN
    INSERT INTO @InvoiceIds (Id)
    SELECT i.Id
    FROM dbo.Invoices AS i
    WHERE i.BranchId = @BranchId;

    INSERT INTO @PaymentIds (Id)
    SELECT p.Id
    FROM dbo.Payments AS p
    WHERE p.BranchId = @BranchId
       OR EXISTS (
            SELECT 1
            FROM @InvoiceIds AS inv
            WHERE inv.Id = p.InvoiceId
       );
END

IF @ResetExpenses = 1
BEGIN
    INSERT INTO @ExpenseIds (Id)
    SELECT e.Id
    FROM dbo.FinanceExpenseRecords AS e
    WHERE e.BranchId = @BranchId;
END

IF @ResetInventorySales = 1
BEGIN
    INSERT INTO @ProductSaleIds (Id)
    SELECT s.Id
    FROM dbo.ProductSales AS s
    WHERE s.BranchId = @BranchId;
END

INSERT INTO @LedgerEntryIds (Id)
SELECT gle.Id
FROM dbo.GeneralLedgerEntries AS gle
WHERE gle.BranchId = @BranchId
  AND (
        (@ResetBilling = 1 AND gle.SourceType = N'Payment' AND EXISTS (
            SELECT 1 FROM @PaymentIds AS p WHERE CONVERT(nvarchar(64), p.Id) = gle.SourceId
        ))
        OR (@ResetExpenses = 1 AND gle.SourceType = N'Expense' AND EXISTS (
            SELECT 1 FROM @ExpenseIds AS e WHERE CONVERT(nvarchar(64), e.Id) = gle.SourceId
        ))
        OR (@ResetInventorySales = 1 AND gle.SourceType IN (N'ProductSale', N'ProductSaleVoid') AND EXISTS (
            SELECT 1 FROM @ProductSaleIds AS s WHERE CONVERT(nvarchar(64), s.Id) = gle.SourceId
        ))
        OR (@ResetManualLedger = 1 AND gle.SourceType = N'Manual')
      );

SELECT N'Invoices' AS [Entity], COUNT(*) AS [RowsToDelete] FROM @InvoiceIds
UNION ALL
SELECT N'Payments', COUNT(*) FROM @PaymentIds
UNION ALL
SELECT N'FinanceExpenseRecords', COUNT(*) FROM @ExpenseIds
UNION ALL
SELECT N'GymEquipmentAssets',
       CASE WHEN @ResetEquipment = 1 THEN COUNT(*) ELSE 0 END
FROM dbo.GymEquipmentAssets
WHERE BranchId = @BranchId
UNION ALL
SELECT N'WeeklySalesAuditRecords',
       CASE WHEN @ResetWeeklySalesAudit = 1 THEN COUNT(*) ELSE 0 END
FROM dbo.WeeklySalesAuditRecords
WHERE BranchId = @BranchId
UNION ALL
SELECT N'ProductSales', COUNT(*) FROM @ProductSaleIds
UNION ALL
SELECT N'ProductSaleLines',
       CASE WHEN @ResetInventorySales = 1 THEN COUNT(*) ELSE 0 END
FROM dbo.ProductSaleLines AS l
WHERE EXISTS (
    SELECT 1
    FROM @ProductSaleIds AS s
    WHERE s.Id = l.ProductSaleId
)
UNION ALL
SELECT N'SupplyRequests',
       CASE WHEN @ResetSupplyRequests = 1 THEN COUNT(*) ELSE 0 END
FROM dbo.SupplyRequests
WHERE BranchId = @BranchId
UNION ALL
SELECT N'GeneralLedgerEntries', COUNT(*) FROM @LedgerEntryIds
UNION ALL
SELECT N'GeneralLedgerLines',
       CASE WHEN EXISTS (SELECT 1 FROM @LedgerEntryIds) THEN COUNT(*) ELSE 0 END
FROM dbo.GeneralLedgerLines AS gll
WHERE EXISTS (
    SELECT 1
    FROM @LedgerEntryIds AS gle
    WHERE gle.Id = gll.EntryId
)
UNION ALL
SELECT N'FinanceAlertLogs',
       CASE WHEN @ResetFinanceAlerts = 1 THEN COUNT(*) ELSE 0 END
FROM dbo.FinanceAlertLogs;

IF @DryRun = 1
BEGIN
    PRINT N'Dry run only. Set @DryRun = 0 to execute.';
    RETURN;
END

BEGIN TRANSACTION;

DELETE gll
FROM dbo.GeneralLedgerLines AS gll
WHERE EXISTS (
    SELECT 1
    FROM @LedgerEntryIds AS gle
    WHERE gle.Id = gll.EntryId
);

DELETE gle
FROM dbo.GeneralLedgerEntries AS gle
WHERE EXISTS (
    SELECT 1
    FROM @LedgerEntryIds AS x
    WHERE x.Id = gle.Id
);

IF @ResetBilling = 1
BEGIN
    DELETE p
    FROM dbo.Payments AS p
    WHERE EXISTS (
        SELECT 1
        FROM @PaymentIds AS x
        WHERE x.Id = p.Id
    );

    DELETE i
    FROM dbo.Invoices AS i
    WHERE EXISTS (
        SELECT 1
        FROM @InvoiceIds AS x
        WHERE x.Id = i.Id
    );
END

IF @ResetExpenses = 1
BEGIN
    DELETE e
    FROM dbo.FinanceExpenseRecords AS e
    WHERE EXISTS (
        SELECT 1
        FROM @ExpenseIds AS x
        WHERE x.Id = e.Id
    );
END

IF @ResetEquipment = 1
BEGIN
    DELETE FROM dbo.GymEquipmentAssets
    WHERE BranchId = @BranchId;
END

IF @ResetWeeklySalesAudit = 1
BEGIN
    DELETE FROM dbo.WeeklySalesAuditRecords
    WHERE BranchId = @BranchId;
END

IF @ResetInventorySales = 1
BEGIN
    DELETE s
    FROM dbo.ProductSales AS s
    WHERE EXISTS (
        SELECT 1
        FROM @ProductSaleIds AS x
        WHERE x.Id = s.Id
    );
END

IF @ResetSupplyRequests = 1
BEGIN
    DELETE FROM dbo.SupplyRequests
    WHERE BranchId = @BranchId;
END

IF @ResetFinanceAlerts = 1
BEGIN
    DELETE FROM dbo.FinanceAlertLogs;
END

COMMIT TRANSACTION;

PRINT N'Finance branch reset completed.';
