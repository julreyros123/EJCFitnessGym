-- SQL Script: Add Retail Inventory Tables
-- For: EJCFitnessGym - Product Sales & Supply Request features
-- Created: 2026-03-02

-- =====================================================
-- Table: RetailProducts - Product catalog for POS
-- =====================================================
IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='RetailProducts' AND xtype='U')
BEGIN
    CREATE TABLE [RetailProducts] (
        [Id] INT NOT NULL IDENTITY(1,1),
        [Name] NVARCHAR(100) NOT NULL,
        [Sku] NVARCHAR(50) NULL,
        [Category] NVARCHAR(50) NOT NULL,
        [Unit] NVARCHAR(20) NOT NULL,
        [UnitPrice] DECIMAL(18, 2) NOT NULL,
        [CostPrice] DECIMAL(18, 2) NOT NULL DEFAULT 0,
        [StockQuantity] INT NOT NULL DEFAULT 0,
        [ReorderLevel] INT NOT NULL DEFAULT 10,
        [BranchId] NVARCHAR(32) NULL,
        [IsActive] BIT NOT NULL DEFAULT 1,
        [CreatedAtUtc] DATETIME2 NOT NULL,
        [UpdatedAtUtc] DATETIME2 NULL,
        CONSTRAINT [PK_RetailProducts] PRIMARY KEY CLUSTERED ([Id])
    );
    
    CREATE UNIQUE INDEX [IX_RetailProducts_Sku] 
        ON [RetailProducts]([Sku]) 
        WHERE [Sku] IS NOT NULL;
    
    CREATE INDEX [IX_RetailProducts_BranchId_Category] 
        ON [RetailProducts]([BranchId], [Category]);
        
    PRINT 'Created table: RetailProducts';
END
ELSE
BEGIN
    PRINT 'Table RetailProducts already exists - skipping';
END
GO

-- =====================================================
-- Table: ProductSales - Sale transactions from POS
-- =====================================================
IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='ProductSales' AND xtype='U')
BEGIN
    CREATE TABLE [ProductSales] (
        [Id] INT NOT NULL IDENTITY(1,1),
        [ReceiptNumber] NVARCHAR(50) NOT NULL,
        [BranchId] NVARCHAR(32) NULL,
        [MemberUserId] NVARCHAR(450) NULL,
        [CustomerName] NVARCHAR(100) NULL,
        [Subtotal] DECIMAL(18, 2) NOT NULL,
        [VatAmount] DECIMAL(18, 2) NOT NULL,
        [TotalAmount] DECIMAL(18, 2) NOT NULL,
        [PaymentMethod] INT NOT NULL,
        [Status] INT NOT NULL DEFAULT 0,
        [LinesJson] NVARCHAR(MAX) NOT NULL,
        [SaleDateUtc] DATETIME2 NOT NULL,
        [ProcessedByUserId] NVARCHAR(450) NULL,
        [VoidedAtUtc] DATETIME2 NULL,
        [VoidReason] NVARCHAR(500) NULL,
        CONSTRAINT [PK_ProductSales] PRIMARY KEY CLUSTERED ([Id])
    );
    
    CREATE UNIQUE INDEX [IX_ProductSales_ReceiptNumber] 
        ON [ProductSales]([ReceiptNumber]);
    
    CREATE INDEX [IX_ProductSales_BranchId_SaleDateUtc] 
        ON [ProductSales]([BranchId], [SaleDateUtc] DESC);
    
    CREATE INDEX [IX_ProductSales_MemberUserId] 
        ON [ProductSales]([MemberUserId]) 
        WHERE [MemberUserId] IS NOT NULL;
        
    PRINT 'Created table: ProductSales';
END
ELSE
BEGIN
    PRINT 'Table ProductSales already exists - skipping';
END
GO

-- =====================================================
-- Table: SupplyRequests - Supply request workflow
-- =====================================================
IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='SupplyRequests' AND xtype='U')
BEGIN
    CREATE TABLE [SupplyRequests] (
        [Id] INT NOT NULL IDENTITY(1,1),
        [RequestNumber] NVARCHAR(30) NOT NULL,
        [BranchId] NVARCHAR(32) NULL,
        [ItemName] NVARCHAR(200) NOT NULL,
        [Category] NVARCHAR(100) NULL,
        [RequestedQuantity] INT NOT NULL,
        [Unit] NVARCHAR(30) NOT NULL,
        [EstimatedUnitCost] DECIMAL(18, 2) NULL,
        [ActualUnitCost] DECIMAL(18, 2) NULL,
        [ReceivedQuantity] INT NULL,
        [Stage] INT NOT NULL DEFAULT 0,
        [RequestedByUserId] NVARCHAR(450) NULL,
        [ApprovedByUserId] NVARCHAR(450) NULL,
        [ReceivedByUserId] NVARCHAR(450) NULL,
        [LinkedExpenseId] INT NULL,
        [CreatedAtUtc] DATETIME2 NOT NULL,
        [UpdatedAtUtc] DATETIME2 NULL,
        CONSTRAINT [PK_SupplyRequests] PRIMARY KEY CLUSTERED ([Id])
    );
    
    CREATE UNIQUE INDEX [IX_SupplyRequests_RequestNumber] 
        ON [SupplyRequests]([RequestNumber]);
    
    CREATE INDEX [IX_SupplyRequests_Stage_BranchId] 
        ON [SupplyRequests]([Stage], [BranchId]);
    
    CREATE INDEX [IX_SupplyRequests_BranchId_CreatedAtUtc] 
        ON [SupplyRequests]([BranchId], [CreatedAtUtc] DESC);
        
    PRINT 'Created table: SupplyRequests';
END
ELSE
BEGIN
    PRINT 'Table SupplyRequests already exists - skipping';
END
GO

-- =====================================================
-- Seed some sample retail products for testing
-- =====================================================
IF NOT EXISTS (SELECT 1 FROM [RetailProducts])
BEGIN
    INSERT INTO [RetailProducts] 
        ([Name], [Sku], [Category], [Unit], [UnitPrice], [CostPrice], [StockQuantity], [ReorderLevel], [BranchId], [IsActive], [CreatedAtUtc])
    VALUES
        ('Resistance Band', 'ACC-001', 'Accessories', 'piece', 350.00, 180.00, 42, 15, 'BR-CENTRAL', 1, GETUTCDATE()),
        ('Bottled Water (500ml)', 'HYD-001', 'Hydration', 'bottle', 35.00, 15.00, 96, 50, 'BR-CENTRAL', 1, GETUTCDATE()),
        ('Creatine Monohydrate', 'SUP-001', 'Supplements', 'tub', 950.00, 650.00, 18, 10, 'BR-CENTRAL', 1, GETUTCDATE()),
        ('Whey Protein (2 lbs)', 'SUP-002', 'Supplements', 'pack', 2100.00, 1500.00, 11, 8, 'BR-CENTRAL', 1, GETUTCDATE()),
        ('Protein Bar', 'NUT-001', 'Nutrition', 'bar', 120.00, 70.00, 57, 30, 'BR-CENTRAL', 1, GETUTCDATE()),
        ('Gym Towel', 'ACC-002', 'Accessories', 'piece', 250.00, 120.00, 35, 20, 'BR-CENTRAL', 1, GETUTCDATE()),
        ('Pre-Workout Drink', 'SUP-003', 'Supplements', 'sachet', 85.00, 45.00, 80, 40, 'BR-CENTRAL', 1, GETUTCDATE()),
        ('Sports Drink (1L)', 'HYD-002', 'Hydration', 'bottle', 65.00, 35.00, 60, 30, 'BR-CENTRAL', 1, GETUTCDATE());
        
    PRINT 'Seeded sample retail products';
END
GO

PRINT '======================================';
PRINT 'Retail Inventory tables setup complete';
PRINT '======================================';
