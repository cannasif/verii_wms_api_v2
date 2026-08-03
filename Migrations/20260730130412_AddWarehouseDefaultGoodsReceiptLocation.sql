SET XACT_ABORT ON;
SET NOCOUNT ON;

BEGIN TRY
    BEGIN TRANSACTION;

    IF OBJECT_ID(N'dbo.RII_WAREHOUSE', N'U') IS NULL
        THROW 51000, N'RII_WAREHOUSE tablosu bulunamadi.', 1;

    IF OBJECT_ID(N'dbo.RII_LOCATION', N'U') IS NULL
        THROW 51000, N'RII_LOCATION tablosu bulunamadi.', 1;

    /* History kaydi bulunsa bile fiziksel semayi onar. */
    IF COL_LENGTH(N'dbo.RII_WAREHOUSE', N'DefaultGoodsReceiptLocationId') IS NULL
        EXEC sys.sp_executesql N'
            ALTER TABLE [dbo].[RII_WAREHOUSE]
            ADD [DefaultGoodsReceiptLocationId] bigint NULL;';

    /* Kolon ayni batch icinde eklenebilecegi icin referanslar dinamik SQL'dedir. */
    EXEC sys.sp_executesql N'
        UPDATE warehouse
        SET warehouse.DefaultGoodsReceiptLocationId = defaultLocation.Id
        FROM [dbo].[RII_WAREHOUSE] AS warehouse
        CROSS APPLY
        (
            SELECT TOP (1) location.Id
            FROM [dbo].[RII_LOCATION] AS location
            WHERE location.WarehouseId = warehouse.Id
              AND location.BranchCode = warehouse.BranchCode
              AND location.IsDeleted = 0
              AND location.IsActive = 1
              AND UPPER(LTRIM(RTRIM(location.Code))) = N''YER1''
            ORDER BY location.Id
        ) AS defaultLocation
        WHERE warehouse.IsDeleted = 0
          AND warehouse.DefaultGoodsReceiptLocationId IS NULL;';

    IF NOT EXISTS
    (
        SELECT 1 FROM sys.indexes
        WHERE [name] = N'IX_RII_WAREHOUSE_DEFAULT_GR_LOCATION'
          AND [object_id] = OBJECT_ID(N'dbo.RII_WAREHOUSE')
    )
        EXEC sys.sp_executesql N'
            CREATE INDEX [IX_RII_WAREHOUSE_DEFAULT_GR_LOCATION]
            ON [dbo].[RII_WAREHOUSE] ([DefaultGoodsReceiptLocationId]);';

    IF NOT EXISTS
    (
        SELECT 1 FROM sys.foreign_keys
        WHERE [name] = N'FK_RII_WAREHOUSE_RII_LOCATION_DefaultGoodsReceiptLocationId'
          AND [parent_object_id] = OBJECT_ID(N'dbo.RII_WAREHOUSE')
    )
        EXEC sys.sp_executesql N'
            ALTER TABLE [dbo].[RII_WAREHOUSE] WITH CHECK
            ADD CONSTRAINT [FK_RII_WAREHOUSE_RII_LOCATION_DefaultGoodsReceiptLocationId]
            FOREIGN KEY ([DefaultGoodsReceiptLocationId])
            REFERENCES [dbo].[RII_LOCATION] ([Id])
            ON DELETE SET NULL;

            ALTER TABLE [dbo].[RII_WAREHOUSE]
            CHECK CONSTRAINT [FK_RII_WAREHOUSE_RII_LOCATION_DefaultGoodsReceiptLocationId];';

    IF OBJECT_ID(N'dbo.__EFMigrationsHistory', N'U') IS NULL
        THROW 51000, N'__EFMigrationsHistory tablosu bulunamadi.', 1;

    IF NOT EXISTS
    (
        SELECT 1 FROM [dbo].[__EFMigrationsHistory]
        WHERE [MigrationId] = N'20260730130412_AddWarehouseDefaultGoodsReceiptLocation'
    )
    BEGIN
        INSERT INTO [dbo].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
        VALUES (N'20260730130412_AddWarehouseDefaultGoodsReceiptLocation', N'10.0.10');
    END;

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
GO

SELECT
    COL_LENGTH(N'dbo.RII_WAREHOUSE', N'DefaultGoodsReceiptLocationId') AS ColumnByteLength,
    CASE WHEN EXISTS
    (
        SELECT 1 FROM dbo.__EFMigrationsHistory
        WHERE MigrationId = N'20260730130412_AddWarehouseDefaultGoodsReceiptLocation'
    ) THEN 1 ELSE 0 END AS MigrationHistoryExists;
GO
