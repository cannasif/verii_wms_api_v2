BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260803224536_AddStockMediaLibrary'
)
BEGIN
    CREATE TABLE [RII_STOCK_IMAGE] (
        [Id] bigint NOT NULL IDENTITY,
        [StockId] bigint NOT NULL,
        [FileUrl] nvarchar(500) NOT NULL,
        [OriginalFileName] nvarchar(240) NOT NULL,
        [ContentType] nvarchar(100) NOT NULL,
        [FileLength] bigint NOT NULL,
        [AltText] nvarchar(200) NULL,
        [SortOrder] int NOT NULL,
        [IsPrimary] bit NOT NULL DEFAULT CAST(0 AS bit),
        [BranchCode] nvarchar(10) NOT NULL DEFAULT N'0',
        [CreatedDate] datetime2 NULL,
        [UpdatedDate] datetime2 NULL,
        [DeletedDate] datetime2 NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [CreatedBy] bigint NULL,
        [UpdatedBy] bigint NULL,
        [DeletedBy] bigint NULL,
        CONSTRAINT [PK_RII_STOCK_IMAGE] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_RII_STOCK_IMAGE_RII_STOCK_StockId] FOREIGN KEY ([StockId]) REFERENCES [RII_STOCK] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260803224536_AddStockMediaLibrary'
)
BEGIN
    CREATE INDEX [IX_RII_STOCK_IMAGE_IsDeleted] ON [RII_STOCK_IMAGE] ([IsDeleted]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260803224536_AddStockMediaLibrary'
)
BEGIN
    CREATE INDEX [IX_RII_STOCK_IMAGE_StockId] ON [RII_STOCK_IMAGE] ([StockId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260803224536_AddStockMediaLibrary'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_StockImage_Branch_Stock_SortOrder] ON [RII_STOCK_IMAGE] ([BranchCode], [StockId], [SortOrder]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260803224536_AddStockMediaLibrary'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_StockImage_OnePrimary] ON [RII_STOCK_IMAGE] ([BranchCode], [StockId]) WHERE [IsDeleted] = 0 AND [IsPrimary] = 1');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260803224536_AddStockMediaLibrary'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260803224536_AddStockMediaLibrary', N'10.0.10');
END;

COMMIT;
GO

