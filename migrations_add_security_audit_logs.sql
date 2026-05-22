-- Idempotent script to add SecurityAuditLogs, DataAuditLogs, and SystemErrorLogs
-- Review and run on staging/production after taking a backup.

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[DataAuditLogs]') AND type = N'U')
BEGIN
  CREATE TABLE [dbo].[DataAuditLogs](
    [Id] int IDENTITY(1,1) NOT NULL PRIMARY KEY,
    [TimestampUtc] datetime2 NOT NULL,
    [UserId] nvarchar(450) NULL,
    [EntityName] nvarchar(100) NOT NULL,
    [Action] nvarchar(50) NOT NULL,
    [PrimaryKey] nvarchar(256) NULL,
    [OldValues] nvarchar(max) NULL,
    [NewValues] nvarchar(max) NULL
  );
END;

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[SecurityAuditLogs]') AND type = N'U')
BEGIN
  CREATE TABLE [dbo].[SecurityAuditLogs](
    [Id] int IDENTITY(1,1) NOT NULL PRIMARY KEY,
    [UserId] nvarchar(450) NULL,
    [Email] nvarchar(256) NULL,
    [EventType] nvarchar(100) NOT NULL,
    [EventStatus] nvarchar(50) NOT NULL,
    [EventDetails] nvarchar(max) NULL,
    [IpAddress] nvarchar(45) NULL,
    [UserAgent] nvarchar(500) NULL,
    [EventTimestampUtc] datetime2 NOT NULL
  );
END;

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[SystemErrorLogs]') AND type = N'U')
BEGIN
  CREATE TABLE [dbo].[SystemErrorLogs](
    [Id] int IDENTITY(1,1) NOT NULL PRIMARY KEY,
    [TimestampUtc] datetime2 NOT NULL,
    [Path] nvarchar(max) NULL,
    [ExceptionMessage] nvarchar(max) NULL,
    [StackTrace] nvarchar(max) NULL,
    [UserId] nvarchar(450) NULL
  );
END;
