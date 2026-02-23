IF OBJECT_ID(N'[__EFMigrationsHistory]') IS NULL
BEGIN
    CREATE TABLE [__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'00000000000000_CreateIdentitySchema'
)
BEGIN
    CREATE TABLE [AspNetRoles] (
        [Id] nvarchar(450) NOT NULL,
        [Name] nvarchar(256) NULL,
        [NormalizedName] nvarchar(256) NULL,
        [ConcurrencyStamp] nvarchar(max) NULL,
        CONSTRAINT [PK_AspNetRoles] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'00000000000000_CreateIdentitySchema'
)
BEGIN
    CREATE TABLE [AspNetUsers] (
        [Id] nvarchar(450) NOT NULL,
        [UserName] nvarchar(256) NULL,
        [NormalizedUserName] nvarchar(256) NULL,
        [Email] nvarchar(256) NULL,
        [NormalizedEmail] nvarchar(256) NULL,
        [EmailConfirmed] bit NOT NULL,
        [PasswordHash] nvarchar(max) NULL,
        [SecurityStamp] nvarchar(max) NULL,
        [ConcurrencyStamp] nvarchar(max) NULL,
        [PhoneNumber] nvarchar(max) NULL,
        [PhoneNumberConfirmed] bit NOT NULL,
        [TwoFactorEnabled] bit NOT NULL,
        [LockoutEnd] datetimeoffset NULL,
        [LockoutEnabled] bit NOT NULL,
        [AccessFailedCount] int NOT NULL,
        CONSTRAINT [PK_AspNetUsers] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'00000000000000_CreateIdentitySchema'
)
BEGIN
    CREATE TABLE [AspNetRoleClaims] (
        [Id] int NOT NULL IDENTITY,
        [RoleId] nvarchar(450) NOT NULL,
        [ClaimType] nvarchar(max) NULL,
        [ClaimValue] nvarchar(max) NULL,
        CONSTRAINT [PK_AspNetRoleClaims] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_AspNetRoleClaims_AspNetRoles_RoleId] FOREIGN KEY ([RoleId]) REFERENCES [AspNetRoles] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'00000000000000_CreateIdentitySchema'
)
BEGIN
    CREATE TABLE [AspNetUserClaims] (
        [Id] int NOT NULL IDENTITY,
        [UserId] nvarchar(450) NOT NULL,
        [ClaimType] nvarchar(max) NULL,
        [ClaimValue] nvarchar(max) NULL,
        CONSTRAINT [PK_AspNetUserClaims] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_AspNetUserClaims_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'00000000000000_CreateIdentitySchema'
)
BEGIN
    CREATE TABLE [AspNetUserLogins] (
        [LoginProvider] nvarchar(128) NOT NULL,
        [ProviderKey] nvarchar(128) NOT NULL,
        [ProviderDisplayName] nvarchar(max) NULL,
        [UserId] nvarchar(450) NOT NULL,
        CONSTRAINT [PK_AspNetUserLogins] PRIMARY KEY ([LoginProvider], [ProviderKey]),
        CONSTRAINT [FK_AspNetUserLogins_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'00000000000000_CreateIdentitySchema'
)
BEGIN
    CREATE TABLE [AspNetUserRoles] (
        [UserId] nvarchar(450) NOT NULL,
        [RoleId] nvarchar(450) NOT NULL,
        CONSTRAINT [PK_AspNetUserRoles] PRIMARY KEY ([UserId], [RoleId]),
        CONSTRAINT [FK_AspNetUserRoles_AspNetRoles_RoleId] FOREIGN KEY ([RoleId]) REFERENCES [AspNetRoles] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_AspNetUserRoles_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'00000000000000_CreateIdentitySchema'
)
BEGIN
    CREATE TABLE [AspNetUserTokens] (
        [UserId] nvarchar(450) NOT NULL,
        [LoginProvider] nvarchar(128) NOT NULL,
        [Name] nvarchar(128) NOT NULL,
        [Value] nvarchar(max) NULL,
        CONSTRAINT [PK_AspNetUserTokens] PRIMARY KEY ([UserId], [LoginProvider], [Name]),
        CONSTRAINT [FK_AspNetUserTokens_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'00000000000000_CreateIdentitySchema'
)
BEGIN
    CREATE INDEX [IX_AspNetRoleClaims_RoleId] ON [AspNetRoleClaims] ([RoleId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'00000000000000_CreateIdentitySchema'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [RoleNameIndex] ON [AspNetRoles] ([NormalizedName]) WHERE [NormalizedName] IS NOT NULL');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'00000000000000_CreateIdentitySchema'
)
BEGIN
    CREATE INDEX [IX_AspNetUserClaims_UserId] ON [AspNetUserClaims] ([UserId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'00000000000000_CreateIdentitySchema'
)
BEGIN
    CREATE INDEX [IX_AspNetUserLogins_UserId] ON [AspNetUserLogins] ([UserId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'00000000000000_CreateIdentitySchema'
)
BEGIN
    CREATE INDEX [IX_AspNetUserRoles_RoleId] ON [AspNetUserRoles] ([RoleId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'00000000000000_CreateIdentitySchema'
)
BEGIN
    CREATE INDEX [EmailIndex] ON [AspNetUsers] ([NormalizedEmail]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'00000000000000_CreateIdentitySchema'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UserNameIndex] ON [AspNetUsers] ([NormalizedUserName]) WHERE [NormalizedUserName] IS NOT NULL');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'00000000000000_CreateIdentitySchema'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'00000000000000_CreateIdentitySchema', N'8.0.23');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260210062937_AddBillingModule'
)
BEGIN
    CREATE TABLE [SubscriptionPlans] (
        [Id] int NOT NULL IDENTITY,
        [Name] nvarchar(100) NOT NULL,
        [Description] nvarchar(500) NULL,
        [Price] decimal(18,2) NOT NULL,
        [BillingCycle] int NOT NULL,
        [IsActive] bit NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_SubscriptionPlans] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260210062937_AddBillingModule'
)
BEGIN
    CREATE TABLE [MemberSubscriptions] (
        [Id] int NOT NULL IDENTITY,
        [MemberUserId] nvarchar(max) NOT NULL,
        [SubscriptionPlanId] int NOT NULL,
        [StartDateUtc] datetime2 NOT NULL,
        [EndDateUtc] datetime2 NULL,
        [Status] int NOT NULL,
        [ExternalCustomerId] nvarchar(max) NULL,
        [ExternalSubscriptionId] nvarchar(max) NULL,
        CONSTRAINT [PK_MemberSubscriptions] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_MemberSubscriptions_SubscriptionPlans_SubscriptionPlanId] FOREIGN KEY ([SubscriptionPlanId]) REFERENCES [SubscriptionPlans] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260210062937_AddBillingModule'
)
BEGIN
    CREATE TABLE [Invoices] (
        [Id] int NOT NULL IDENTITY,
        [InvoiceNumber] nvarchar(50) NOT NULL,
        [MemberUserId] nvarchar(max) NOT NULL,
        [MemberSubscriptionId] int NULL,
        [IssueDateUtc] datetime2 NOT NULL,
        [DueDateUtc] datetime2 NOT NULL,
        [Amount] decimal(18,2) NOT NULL,
        [Status] int NOT NULL,
        [Notes] nvarchar(max) NULL,
        CONSTRAINT [PK_Invoices] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Invoices_MemberSubscriptions_MemberSubscriptionId] FOREIGN KEY ([MemberSubscriptionId]) REFERENCES [MemberSubscriptions] ([Id]) ON DELETE SET NULL
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260210062937_AddBillingModule'
)
BEGIN
    CREATE TABLE [Payments] (
        [Id] int NOT NULL IDENTITY,
        [InvoiceId] int NOT NULL,
        [Amount] decimal(18,2) NOT NULL,
        [Method] int NOT NULL,
        [Status] int NOT NULL,
        [PaidAtUtc] datetime2 NOT NULL,
        [ReferenceNumber] nvarchar(max) NULL,
        [ReceivedByUserId] nvarchar(max) NULL,
        [GatewayProvider] nvarchar(max) NULL,
        [GatewayPaymentId] nvarchar(max) NULL,
        CONSTRAINT [PK_Payments] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Payments_Invoices_InvoiceId] FOREIGN KEY ([InvoiceId]) REFERENCES [Invoices] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260210062937_AddBillingModule'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Invoices_InvoiceNumber] ON [Invoices] ([InvoiceNumber]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260210062937_AddBillingModule'
)
BEGIN
    CREATE INDEX [IX_Invoices_MemberSubscriptionId] ON [Invoices] ([MemberSubscriptionId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260210062937_AddBillingModule'
)
BEGIN
    CREATE INDEX [IX_MemberSubscriptions_SubscriptionPlanId] ON [MemberSubscriptions] ([SubscriptionPlanId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260210062937_AddBillingModule'
)
BEGIN
    CREATE INDEX [IX_Payments_InvoiceId] ON [Payments] ([InvoiceId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260210062937_AddBillingModule'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260210062937_AddBillingModule', N'8.0.23');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260210063023_BillingDecimalPrecision'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260210063023_BillingDecimalPrecision', N'8.0.23');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260211061332_IdentityDetails'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260211061332_IdentityDetails', N'8.0.23');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260211170305_AddMemberProfile'
)
BEGIN
    CREATE TABLE [MemberProfiles] (
        [Id] int NOT NULL IDENTITY,
        [UserId] nvarchar(450) NOT NULL,
        [FirstName] nvarchar(100) NULL,
        [LastName] nvarchar(100) NULL,
        [Age] int NULL,
        [PhoneNumber] nvarchar(30) NULL,
        [ProfileImagePath] nvarchar(300) NULL,
        [CreatedUtc] datetime2 NOT NULL,
        [UpdatedUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_MemberProfiles] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260211170305_AddMemberProfile'
)
BEGIN
    CREATE UNIQUE INDEX [IX_MemberProfiles_UserId] ON [MemberProfiles] ([UserId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260211170305_AddMemberProfile'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260211170305_AddMemberProfile', N'8.0.23');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260211170636_AddMemberHealthMetrics'
)
BEGIN
    ALTER TABLE [MemberProfiles] ADD [Bmi] decimal(5,2) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260211170636_AddMemberHealthMetrics'
)
BEGIN
    ALTER TABLE [MemberProfiles] ADD [HeightCm] decimal(5,2) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260211170636_AddMemberHealthMetrics'
)
BEGIN
    ALTER TABLE [MemberProfiles] ADD [WeightKg] decimal(5,2) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260211170636_AddMemberHealthMetrics'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260211170636_AddMemberHealthMetrics', N'8.0.23');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260215104214_AddGymEquipmentAssets'
)
BEGIN
    CREATE TABLE [GymEquipmentAssets] (
        [Id] int NOT NULL IDENTITY,
        [Name] nvarchar(140) NOT NULL,
        [Brand] nvarchar(120) NULL,
        [Category] nvarchar(80) NOT NULL,
        [Quantity] int NOT NULL,
        [UnitCost] decimal(18,2) NOT NULL,
        [UsefulLifeMonths] int NOT NULL,
        [PurchasedAtUtc] datetime2 NULL,
        [IsActive] bit NOT NULL,
        [Notes] nvarchar(500) NULL,
        [CreatedUtc] datetime2 NOT NULL,
        [UpdatedUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_GymEquipmentAssets] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260215104214_AddGymEquipmentAssets'
)
BEGIN
    CREATE INDEX [IX_GymEquipmentAssets_Name_Brand_Category] ON [GymEquipmentAssets] ([Name], [Brand], [Category]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260215104214_AddGymEquipmentAssets'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260215104214_AddGymEquipmentAssets', N'8.0.23');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260215105822_AddFinanceExpensesAndAlerts'
)
BEGIN
    CREATE TABLE [FinanceAlertLogs] (
        [Id] int NOT NULL IDENTITY,
        [AlertType] nvarchar(80) NOT NULL,
        [Trigger] nvarchar(80) NULL,
        [Severity] nvarchar(20) NOT NULL,
        [Message] nvarchar(500) NOT NULL,
        [RealtimePublished] bit NOT NULL,
        [EmailAttempted] bit NOT NULL,
        [EmailSucceeded] bit NOT NULL,
        [PayloadJson] nvarchar(4000) NULL,
        [CreatedUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_FinanceAlertLogs] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260215105822_AddFinanceExpensesAndAlerts'
)
BEGIN
    CREATE TABLE [FinanceExpenseRecords] (
        [Id] int NOT NULL IDENTITY,
        [Name] nvarchar(140) NOT NULL,
        [Category] nvarchar(80) NOT NULL,
        [Amount] decimal(18,2) NOT NULL,
        [ExpenseDateUtc] datetime2 NOT NULL,
        [IsRecurring] bit NOT NULL,
        [IsActive] bit NOT NULL,
        [Notes] nvarchar(500) NULL,
        [CreatedUtc] datetime2 NOT NULL,
        [UpdatedUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_FinanceExpenseRecords] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260215105822_AddFinanceExpensesAndAlerts'
)
BEGIN
    CREATE INDEX [IX_FinanceAlertLogs_AlertType_CreatedUtc] ON [FinanceAlertLogs] ([AlertType], [CreatedUtc]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260215105822_AddFinanceExpensesAndAlerts'
)
BEGIN
    CREATE INDEX [IX_FinanceExpenseRecords_ExpenseDateUtc_Category] ON [FinanceExpenseRecords] ([ExpenseDateUtc], [Category]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260215105822_AddFinanceExpensesAndAlerts'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260215105822_AddFinanceExpensesAndAlerts', N'8.0.23');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260215121348_AddIntegrationOutboxAndWebhookIdempotency'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260215121348_AddIntegrationOutboxAndWebhookIdempotency', N'8.0.23');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260215124234_AddIntegrationOutboxSchemaAndConstraints'
)
BEGIN
    DECLARE @var0 sysname;
    SELECT @var0 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Payments]') AND [c].[name] = N'ReferenceNumber');
    IF @var0 IS NOT NULL EXEC(N'ALTER TABLE [Payments] DROP CONSTRAINT [' + @var0 + '];');
    ALTER TABLE [Payments] ALTER COLUMN [ReferenceNumber] nvarchar(450) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260215124234_AddIntegrationOutboxSchemaAndConstraints'
)
BEGIN
    DECLARE @var1 sysname;
    SELECT @var1 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Payments]') AND [c].[name] = N'GatewayProvider');
    IF @var1 IS NOT NULL EXEC(N'ALTER TABLE [Payments] DROP CONSTRAINT [' + @var1 + '];');
    ALTER TABLE [Payments] ALTER COLUMN [GatewayProvider] nvarchar(450) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260215124234_AddIntegrationOutboxSchemaAndConstraints'
)
BEGIN
    DECLARE @var2 sysname;
    SELECT @var2 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Payments]') AND [c].[name] = N'GatewayPaymentId');
    IF @var2 IS NOT NULL EXEC(N'ALTER TABLE [Payments] DROP CONSTRAINT [' + @var2 + '];');
    ALTER TABLE [Payments] ALTER COLUMN [GatewayPaymentId] nvarchar(450) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260215124234_AddIntegrationOutboxSchemaAndConstraints'
)
BEGIN
    CREATE TABLE [InboundWebhookReceipts] (
        [Id] int NOT NULL IDENTITY,
        [Provider] nvarchar(80) NOT NULL,
        [EventKey] nvarchar(250) NOT NULL,
        [EventType] nvarchar(120) NULL,
        [ExternalReference] nvarchar(180) NULL,
        [Status] nvarchar(40) NOT NULL,
        [AttemptCount] int NOT NULL,
        [FirstReceivedUtc] datetime2 NOT NULL,
        [LastAttemptUtc] datetime2 NOT NULL,
        [ProcessedUtc] datetime2 NULL,
        [Notes] nvarchar(2000) NULL,
        [CreatedUtc] datetime2 NOT NULL,
        [UpdatedUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_InboundWebhookReceipts] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260215124234_AddIntegrationOutboxSchemaAndConstraints'
)
BEGIN
    CREATE TABLE [IntegrationOutboxMessages] (
        [Id] int NOT NULL IDENTITY,
        [Target] int NOT NULL,
        [EventType] nvarchar(120) NOT NULL,
        [Message] nvarchar(300) NOT NULL,
        [TargetValue] nvarchar(450) NULL,
        [PayloadJson] nvarchar(max) NULL,
        [Status] int NOT NULL,
        [AttemptCount] int NOT NULL,
        [LastError] nvarchar(2000) NULL,
        [NextAttemptUtc] datetime2 NOT NULL,
        [LastAttemptUtc] datetime2 NULL,
        [ProcessedUtc] datetime2 NULL,
        [CreatedUtc] datetime2 NOT NULL,
        [UpdatedUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_IntegrationOutboxMessages] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260215124234_AddIntegrationOutboxSchemaAndConstraints'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_Payments_GatewayProvider_GatewayPaymentId] ON [Payments] ([GatewayProvider], [GatewayPaymentId]) WHERE [GatewayProvider] IS NOT NULL AND [GatewayPaymentId] IS NOT NULL');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260215124234_AddIntegrationOutboxSchemaAndConstraints'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_Payments_GatewayProvider_ReferenceNumber] ON [Payments] ([GatewayProvider], [ReferenceNumber]) WHERE [GatewayProvider] IS NOT NULL AND [ReferenceNumber] IS NOT NULL');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260215124234_AddIntegrationOutboxSchemaAndConstraints'
)
BEGIN
    CREATE UNIQUE INDEX [IX_InboundWebhookReceipts_Provider_EventKey] ON [InboundWebhookReceipts] ([Provider], [EventKey]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260215124234_AddIntegrationOutboxSchemaAndConstraints'
)
BEGIN
    CREATE INDEX [IX_InboundWebhookReceipts_Provider_Status_UpdatedUtc] ON [InboundWebhookReceipts] ([Provider], [Status], [UpdatedUtc]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260215124234_AddIntegrationOutboxSchemaAndConstraints'
)
BEGIN
    CREATE INDEX [IX_IntegrationOutboxMessages_CreatedUtc] ON [IntegrationOutboxMessages] ([CreatedUtc]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260215124234_AddIntegrationOutboxSchemaAndConstraints'
)
BEGIN
    CREATE INDEX [IX_IntegrationOutboxMessages_Status_NextAttemptUtc] ON [IntegrationOutboxMessages] ([Status], [NextAttemptUtc]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260215124234_AddIntegrationOutboxSchemaAndConstraints'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260215124234_AddIntegrationOutboxSchemaAndConstraints', N'8.0.23');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260215150420_AddFinanceAlertLifecycleState'
)
BEGIN
    ALTER TABLE [FinanceAlertLogs] ADD [AcknowledgedBy] nvarchar(120) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260215150420_AddFinanceAlertLifecycleState'
)
BEGIN
    ALTER TABLE [FinanceAlertLogs] ADD [AcknowledgedUtc] datetime2 NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260215150420_AddFinanceAlertLifecycleState'
)
BEGIN
    ALTER TABLE [FinanceAlertLogs] ADD [ResolutionNote] nvarchar(500) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260215150420_AddFinanceAlertLifecycleState'
)
BEGIN
    ALTER TABLE [FinanceAlertLogs] ADD [ResolvedBy] nvarchar(120) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260215150420_AddFinanceAlertLifecycleState'
)
BEGIN
    ALTER TABLE [FinanceAlertLogs] ADD [ResolvedUtc] datetime2 NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260215150420_AddFinanceAlertLifecycleState'
)
BEGIN
    ALTER TABLE [FinanceAlertLogs] ADD [State] int NOT NULL DEFAULT 1;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260215150420_AddFinanceAlertLifecycleState'
)
BEGIN
    ALTER TABLE [FinanceAlertLogs] ADD [StateUpdatedUtc] datetime2 NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260215150420_AddFinanceAlertLifecycleState'
)
BEGIN
    CREATE INDEX [IX_FinanceAlertLogs_State_CreatedUtc] ON [FinanceAlertLogs] ([State], [CreatedUtc]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260215150420_AddFinanceAlertLifecycleState'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260215150420_AddFinanceAlertLifecycleState', N'8.0.23');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260217125542_AddBranchRegistryForSuperAdmin'
)
BEGIN
    CREATE TABLE [BranchRecords] (
        [Id] int NOT NULL IDENTITY,
        [BranchId] nvarchar(32) NOT NULL,
        [Name] nvarchar(120) NOT NULL,
        [IsActive] bit NOT NULL,
        [CreatedUtc] datetime2 NOT NULL,
        [UpdatedUtc] datetime2 NOT NULL,
        [CreatedByUserId] nvarchar(450) NULL,
        CONSTRAINT [PK_BranchRecords] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260217125542_AddBranchRegistryForSuperAdmin'
)
BEGIN
    CREATE UNIQUE INDEX [IX_BranchRecords_BranchId] ON [BranchRecords] ([BranchId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260217125542_AddBranchRegistryForSuperAdmin'
)
BEGIN
    CREATE INDEX [IX_BranchRecords_IsActive_BranchId] ON [BranchRecords] ([IsActive], [BranchId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260217125542_AddBranchRegistryForSuperAdmin'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260217125542_AddBranchRegistryForSuperAdmin', N'8.0.23');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260217133237_AddMemberAiInsights'
)
BEGIN
    CREATE TABLE [MemberRetentionActions] (
        [Id] int NOT NULL IDENTITY,
        [MemberUserId] nvarchar(450) NOT NULL,
        [ActionType] nvarchar(64) NOT NULL,
        [Status] int NOT NULL,
        [SegmentLabel] nvarchar(64) NOT NULL,
        [Reason] nvarchar(300) NOT NULL,
        [SuggestedOffer] nvarchar(200) NULL,
        [DueDateUtc] datetime2 NULL,
        [CreatedUtc] datetime2 NOT NULL,
        [UpdatedUtc] datetime2 NOT NULL,
        [CreatedByUserId] nvarchar(450) NULL,
        [UpdatedByUserId] nvarchar(450) NULL,
        [Notes] nvarchar(500) NULL,
        CONSTRAINT [PK_MemberRetentionActions] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260217133237_AddMemberAiInsights'
)
BEGIN
    CREATE TABLE [MemberSegmentSnapshots] (
        [Id] int NOT NULL IDENTITY,
        [MemberUserId] nvarchar(450) NOT NULL,
        [ClusterId] int NOT NULL,
        [SegmentLabel] nvarchar(64) NOT NULL,
        [SegmentDescription] nvarchar(220) NOT NULL,
        [TotalSpending] decimal(18,2) NOT NULL,
        [BillingActivityCount] int NOT NULL,
        [MembershipMonths] decimal(8,2) NOT NULL,
        [CapturedAtUtc] datetime2 NOT NULL,
        [CapturedByUserId] nvarchar(450) NULL,
        CONSTRAINT [PK_MemberSegmentSnapshots] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260217133237_AddMemberAiInsights'
)
BEGIN
    CREATE INDEX [IX_MemberRetentionActions_MemberUserId_Status_ActionType] ON [MemberRetentionActions] ([MemberUserId], [Status], [ActionType]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260217133237_AddMemberAiInsights'
)
BEGIN
    CREATE INDEX [IX_MemberRetentionActions_Status_DueDateUtc] ON [MemberRetentionActions] ([Status], [DueDateUtc]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260217133237_AddMemberAiInsights'
)
BEGIN
    CREATE INDEX [IX_MemberSegmentSnapshots_CapturedAtUtc] ON [MemberSegmentSnapshots] ([CapturedAtUtc]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260217133237_AddMemberAiInsights'
)
BEGIN
    CREATE INDEX [IX_MemberSegmentSnapshots_MemberUserId_CapturedAtUtc] ON [MemberSegmentSnapshots] ([MemberUserId], [CapturedAtUtc]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260217133237_AddMemberAiInsights'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260217133237_AddMemberAiInsights', N'8.0.23');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260218030505_AddBranchScopeToBillingAndFinance'
)
BEGIN
    ALTER TABLE [Payments] ADD [BranchId] nvarchar(32) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260218030505_AddBranchScopeToBillingAndFinance'
)
BEGIN
    ALTER TABLE [Invoices] ADD [BranchId] nvarchar(32) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260218030505_AddBranchScopeToBillingAndFinance'
)
BEGIN
    ALTER TABLE [GymEquipmentAssets] ADD [BranchId] nvarchar(32) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260218030505_AddBranchScopeToBillingAndFinance'
)
BEGIN
    ALTER TABLE [FinanceExpenseRecords] ADD [BranchId] nvarchar(32) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260218030505_AddBranchScopeToBillingAndFinance'
)
BEGIN
    CREATE INDEX [IX_Payments_BranchId_PaidAtUtc] ON [Payments] ([BranchId], [PaidAtUtc]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260218030505_AddBranchScopeToBillingAndFinance'
)
BEGIN
    CREATE INDEX [IX_Invoices_BranchId_Status_DueDateUtc] ON [Invoices] ([BranchId], [Status], [DueDateUtc]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260218030505_AddBranchScopeToBillingAndFinance'
)
BEGIN
    CREATE INDEX [IX_GymEquipmentAssets_BranchId_Category_Name] ON [GymEquipmentAssets] ([BranchId], [Category], [Name]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260218030505_AddBranchScopeToBillingAndFinance'
)
BEGIN
    CREATE INDEX [IX_FinanceExpenseRecords_BranchId_ExpenseDateUtc_Category] ON [FinanceExpenseRecords] ([BranchId], [ExpenseDateUtc], [Category]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260218030505_AddBranchScopeToBillingAndFinance'
)
BEGIN
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
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260218030505_AddBranchScopeToBillingAndFinance'
)
BEGIN
    UPDATE payment
    SET payment.BranchId = invoice.BranchId
    FROM Payments AS payment
    INNER JOIN Invoices AS invoice
        ON invoice.Id = payment.InvoiceId
    WHERE payment.BranchId IS NULL
      AND invoice.BranchId IS NOT NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260218030505_AddBranchScopeToBillingAndFinance'
)
BEGIN
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
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260218030505_AddBranchScopeToBillingAndFinance'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260218030505_AddBranchScopeToBillingAndFinance', N'8.0.23');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260220014036_AddStaffReplacementRequests'
)
BEGIN
    CREATE TABLE [ReplacementRequests] (
        [Id] int NOT NULL IDENTITY,
        [RequestNumber] nvarchar(32) NOT NULL,
        [BranchId] nvarchar(32) NOT NULL,
        [RequestedByUserId] nvarchar(450) NOT NULL,
        [Subject] nvarchar(160) NOT NULL,
        [Description] nvarchar(2000) NOT NULL,
        [RequestType] int NOT NULL,
        [Priority] int NOT NULL,
        [Status] int NOT NULL,
        [ReviewedByUserId] nvarchar(450) NULL,
        [AdminNotes] nvarchar(1000) NULL,
        [CreatedUtc] datetime2 NOT NULL,
        [UpdatedUtc] datetime2 NOT NULL,
        [ResolvedUtc] datetime2 NULL,
        CONSTRAINT [PK_ReplacementRequests] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260220014036_AddStaffReplacementRequests'
)
BEGIN
    CREATE INDEX [IX_ReplacementRequests_BranchId_Status_CreatedUtc] ON [ReplacementRequests] ([BranchId], [Status], [CreatedUtc]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260220014036_AddStaffReplacementRequests'
)
BEGIN
    CREATE INDEX [IX_ReplacementRequests_RequestedByUserId_CreatedUtc] ON [ReplacementRequests] ([RequestedByUserId], [CreatedUtc]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260220014036_AddStaffReplacementRequests'
)
BEGIN
    CREATE UNIQUE INDEX [IX_ReplacementRequests_RequestNumber] ON [ReplacementRequests] ([RequestNumber]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260220014036_AddStaffReplacementRequests'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260220014036_AddStaffReplacementRequests', N'8.0.23');
END;
GO

COMMIT;
GO

