BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260730130412_AddWarehouseDefaultGoodsReceiptLocation'
)
BEGIN
    IF COL_LENGTH(N'dbo.RII_WAREHOUSE', N'DefaultGoodsReceiptLocationId') IS NULL
        EXEC(N'ALTER TABLE [dbo].[RII_WAREHOUSE] ADD [DefaultGoodsReceiptLocationId] bigint NULL;');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260730130412_AddWarehouseDefaultGoodsReceiptLocation'
)
BEGIN
    EXEC(N'
        UPDATE warehouse
        SET warehouse.DefaultGoodsReceiptLocationId = defaultLocation.Id
        FROM [dbo].[RII_WAREHOUSE] AS warehouse
        CROSS APPLY
        (
            SELECT TOP (1) location.Id
            FROM [dbo].[RII_LOCATION] AS location
            WHERE location.WarehouseId = warehouse.Id
              AND location.IsDeleted = 0
              AND location.IsActive = 1
              AND UPPER(LTRIM(RTRIM(location.Code))) = N''YER1''
            ORDER BY location.Id
        ) AS defaultLocation
        WHERE warehouse.IsDeleted = 0
          AND warehouse.DefaultGoodsReceiptLocationId IS NULL;
    ');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260730130412_AddWarehouseDefaultGoodsReceiptLocation'
)
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM sys.indexes
        WHERE [name] = N'IX_RII_WAREHOUSE_DEFAULT_GR_LOCATION'
          AND [object_id] = OBJECT_ID(N'[dbo].[RII_WAREHOUSE]')
    )
        EXEC(N'CREATE INDEX [IX_RII_WAREHOUSE_DEFAULT_GR_LOCATION]
            ON [dbo].[RII_WAREHOUSE] ([DefaultGoodsReceiptLocationId]);');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260730130412_AddWarehouseDefaultGoodsReceiptLocation'
)
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM sys.foreign_keys
        WHERE [name] = N'FK_RII_WAREHOUSE_RII_LOCATION_DefaultGoodsReceiptLocationId'
          AND [parent_object_id] = OBJECT_ID(N'[dbo].[RII_WAREHOUSE]')
    )
        EXEC(N'ALTER TABLE [dbo].[RII_WAREHOUSE]
            ADD CONSTRAINT [FK_RII_WAREHOUSE_RII_LOCATION_DefaultGoodsReceiptLocationId]
            FOREIGN KEY ([DefaultGoodsReceiptLocationId])
            REFERENCES [dbo].[RII_LOCATION] ([Id])
            ON DELETE SET NULL;');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260730130412_AddWarehouseDefaultGoodsReceiptLocation'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260730130412_AddWarehouseDefaultGoodsReceiptLocation', N'10.0.10');
END;

COMMIT;
GO
