BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260803205213_AddKkdExcessApprovalAndEmployeeStockPreference'
)
BEGIN
    IF COL_LENGTH(N'dbo.RII_WT_HEADER', N'ProjectCode') IS NULL
        ALTER TABLE [RII_WT_HEADER] ADD [ProjectCode] nvarchar(50) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260803205213_AddKkdExcessApprovalAndEmployeeStockPreference'
)
BEGIN
    IF COL_LENGTH(N'dbo.RII_KKD_POLICY', N'RequireManagerApprovalForExcess') IS NULL
        ALTER TABLE [RII_KKD_POLICY] ADD [RequireManagerApprovalForExcess] bit NOT NULL DEFAULT CAST(1 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260803205213_AddKkdExcessApprovalAndEmployeeStockPreference'
)
BEGIN
    IF COL_LENGTH(N'dbo.RII_KKD_DISTRIBUTION', N'ExcessApprovalReason') IS NULL
        ALTER TABLE [RII_KKD_DISTRIBUTION] ADD [ExcessApprovalReason] nvarchar(1000) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260803205213_AddKkdExcessApprovalAndEmployeeStockPreference'
)
BEGIN
    IF COL_LENGTH(N'dbo.RII_KKD_DISTRIBUTION', N'ExcessApprovalStatus') IS NULL
        ALTER TABLE [RII_KKD_DISTRIBUTION] ADD [ExcessApprovalStatus] nvarchar(30) NOT NULL DEFAULT N'NotRequired';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260803205213_AddKkdExcessApprovalAndEmployeeStockPreference'
)
BEGIN
    IF COL_LENGTH(N'dbo.RII_KKD_DISTRIBUTION', N'ExcessApprovedAtUtc') IS NULL
        ALTER TABLE [RII_KKD_DISTRIBUTION] ADD [ExcessApprovedAtUtc] datetimeoffset NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260803205213_AddKkdExcessApprovalAndEmployeeStockPreference'
)
BEGIN
    IF COL_LENGTH(N'dbo.RII_KKD_DISTRIBUTION', N'ExcessApprovedBy') IS NULL
        ALTER TABLE [RII_KKD_DISTRIBUTION] ADD [ExcessApprovedBy] bigint NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260803205213_AddKkdExcessApprovalAndEmployeeStockPreference'
)
BEGIN
    IF OBJECT_ID(N'dbo.RII_KKD_EMPLOYEE_STOCK_PREFERENCE', N'U') IS NULL
    CREATE TABLE [RII_KKD_EMPLOYEE_STOCK_PREFERENCE] (
        [Id] bigint NOT NULL IDENTITY,
        [EmployeeId] bigint NOT NULL,
        [GroupCode] nvarchar(80) NOT NULL,
        [StockId] bigint NOT NULL,
        [LastSelectedAtUtc] datetimeoffset NOT NULL,
        [RowVersion] rowversion NOT NULL,
        [BranchCode] nvarchar(10) NOT NULL DEFAULT N'0',
        [CreatedDate] datetime2 NULL,
        [UpdatedDate] datetime2 NULL,
        [DeletedDate] datetime2 NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [CreatedBy] bigint NULL,
        [UpdatedBy] bigint NULL,
        [DeletedBy] bigint NULL,
        CONSTRAINT [PK_RII_KKD_EMPLOYEE_STOCK_PREFERENCE] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_RII_KKD_EMPLOYEE_STOCK_PREFERENCE_RII_KKD_EMPLOYEE_EmployeeId] FOREIGN KEY ([EmployeeId]) REFERENCES [RII_KKD_EMPLOYEE] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260803205213_AddKkdExcessApprovalAndEmployeeStockPreference'
)
BEGIN
    IF OBJECT_ID(N'dbo.RII_KKD_EMPLOYEE_STOCK_PREFERENCE', N'U') IS NOT NULL
       AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [object_id] = OBJECT_ID(N'dbo.RII_KKD_EMPLOYEE_STOCK_PREFERENCE') AND [name] = N'IX_RII_KKD_EMPLOYEE_STOCK_PREFERENCE_BranchCode_EmployeeId_GroupCode')
        EXEC(N'CREATE UNIQUE INDEX [IX_RII_KKD_EMPLOYEE_STOCK_PREFERENCE_BranchCode_EmployeeId_GroupCode] ON [RII_KKD_EMPLOYEE_STOCK_PREFERENCE] ([BranchCode], [EmployeeId], [GroupCode]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260803205213_AddKkdExcessApprovalAndEmployeeStockPreference'
)
BEGIN
    IF OBJECT_ID(N'dbo.RII_KKD_EMPLOYEE_STOCK_PREFERENCE', N'U') IS NOT NULL
       AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [object_id] = OBJECT_ID(N'dbo.RII_KKD_EMPLOYEE_STOCK_PREFERENCE') AND [name] = N'IX_RII_KKD_EMPLOYEE_STOCK_PREFERENCE_BranchCode_StockId')
        EXEC(N'CREATE INDEX [IX_RII_KKD_EMPLOYEE_STOCK_PREFERENCE_BranchCode_StockId] ON [RII_KKD_EMPLOYEE_STOCK_PREFERENCE] ([BranchCode], [StockId])');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260803205213_AddKkdExcessApprovalAndEmployeeStockPreference'
)
BEGIN
    IF OBJECT_ID(N'dbo.RII_KKD_EMPLOYEE_STOCK_PREFERENCE', N'U') IS NOT NULL
       AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [object_id] = OBJECT_ID(N'dbo.RII_KKD_EMPLOYEE_STOCK_PREFERENCE') AND [name] = N'IX_RII_KKD_EMPLOYEE_STOCK_PREFERENCE_EmployeeId')
        EXEC(N'CREATE INDEX [IX_RII_KKD_EMPLOYEE_STOCK_PREFERENCE_EmployeeId] ON [RII_KKD_EMPLOYEE_STOCK_PREFERENCE] ([EmployeeId])');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260803205213_AddKkdExcessApprovalAndEmployeeStockPreference'
)
BEGIN
    IF OBJECT_ID(N'dbo.RII_KKD_EMPLOYEE_STOCK_PREFERENCE', N'U') IS NOT NULL
       AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [object_id] = OBJECT_ID(N'dbo.RII_KKD_EMPLOYEE_STOCK_PREFERENCE') AND [name] = N'IX_RII_KKD_EMPLOYEE_STOCK_PREFERENCE_IsDeleted')
        EXEC(N'CREATE INDEX [IX_RII_KKD_EMPLOYEE_STOCK_PREFERENCE_IsDeleted] ON [RII_KKD_EMPLOYEE_STOCK_PREFERENCE] ([IsDeleted])');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260803205213_AddKkdExcessApprovalAndEmployeeStockPreference'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260803205213_AddKkdExcessApprovalAndEmployeeStockPreference', N'10.0.10');
END;

COMMIT;
GO
