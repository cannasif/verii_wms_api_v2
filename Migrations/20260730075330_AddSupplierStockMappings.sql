-- AddSupplierStockMappings
-- Güvenli ve tekrar çalıştırılabilir canlı kurulum scripti.

SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.RII_SUPPLIER_STOCK_MAPPING', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[RII_SUPPLIER_STOCK_MAPPING]
    (
        [Id] bigint IDENTITY(1,1) NOT NULL,
        [SupplierId] bigint NOT NULL,
        [SupplierStockCode] nvarchar(100) NOT NULL,
        [NormalizedSupplierStockCode] nvarchar(100) NOT NULL,
        [SupplierStockName] nvarchar(500) NULL,
        [SupplierUnitCode] nvarchar(20) NULL,
        [StockId] bigint NOT NULL,
        [ConversionFactor] decimal(28,8) NOT NULL
            CONSTRAINT [DF_RII_SUPPLIER_STOCK_MAPPING_ConversionFactor] DEFAULT (1),
        [IsActive] bit NOT NULL,
        [Notes] nvarchar(1000) NULL,
        [RowVersion] rowversion NOT NULL,
        [BranchCode] nvarchar(10) NOT NULL
            CONSTRAINT [DF_RII_SUPPLIER_STOCK_MAPPING_BranchCode] DEFAULT (N'0'),
        [CreatedDate] datetime2 NULL,
        [UpdatedDate] datetime2 NULL,
        [DeletedDate] datetime2 NULL,
        [IsDeleted] bit NOT NULL
            CONSTRAINT [DF_RII_SUPPLIER_STOCK_MAPPING_IsDeleted] DEFAULT (0),
        [CreatedBy] bigint NULL,
        [UpdatedBy] bigint NULL,
        [DeletedBy] bigint NULL,
        CONSTRAINT [PK_RII_SUPPLIER_STOCK_MAPPING] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_RII_SUPPLIER_STOCK_MAPPING_RII_CUSTOMER_SupplierId]
            FOREIGN KEY ([SupplierId]) REFERENCES [dbo].[RII_CUSTOMER] ([Id]),
        CONSTRAINT [FK_RII_SUPPLIER_STOCK_MAPPING_RII_STOCK_StockId]
            FOREIGN KEY ([StockId]) REFERENCES [dbo].[RII_STOCK] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE [object_id] = OBJECT_ID(N'dbo.RII_SUPPLIER_STOCK_MAPPING')
      AND [name] = N'IX_RII_SUPPLIER_STOCK_MAPPING_IsDeleted'
)
    CREATE INDEX [IX_RII_SUPPLIER_STOCK_MAPPING_IsDeleted]
        ON [dbo].[RII_SUPPLIER_STOCK_MAPPING] ([IsDeleted]);

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE [object_id] = OBJECT_ID(N'dbo.RII_SUPPLIER_STOCK_MAPPING')
      AND [name] = N'IX_RII_SUPPLIER_STOCK_MAPPING_STOCK_ACTIVE'
)
    CREATE INDEX [IX_RII_SUPPLIER_STOCK_MAPPING_STOCK_ACTIVE]
        ON [dbo].[RII_SUPPLIER_STOCK_MAPPING] ([BranchCode], [StockId], [IsActive]);

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE [object_id] = OBJECT_ID(N'dbo.RII_SUPPLIER_STOCK_MAPPING')
      AND [name] = N'IX_RII_SUPPLIER_STOCK_MAPPING_StockId'
)
    CREATE INDEX [IX_RII_SUPPLIER_STOCK_MAPPING_StockId]
        ON [dbo].[RII_SUPPLIER_STOCK_MAPPING] ([StockId]);

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE [object_id] = OBJECT_ID(N'dbo.RII_SUPPLIER_STOCK_MAPPING')
      AND [name] = N'IX_RII_SUPPLIER_STOCK_MAPPING_SUPPLIER_ACTIVE'
)
    CREATE INDEX [IX_RII_SUPPLIER_STOCK_MAPPING_SUPPLIER_ACTIVE]
        ON [dbo].[RII_SUPPLIER_STOCK_MAPPING] ([BranchCode], [SupplierId], [IsActive]);

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE [object_id] = OBJECT_ID(N'dbo.RII_SUPPLIER_STOCK_MAPPING')
      AND [name] = N'IX_RII_SUPPLIER_STOCK_MAPPING_SupplierId'
)
    CREATE INDEX [IX_RII_SUPPLIER_STOCK_MAPPING_SupplierId]
        ON [dbo].[RII_SUPPLIER_STOCK_MAPPING] ([SupplierId]);

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE [object_id] = OBJECT_ID(N'dbo.RII_SUPPLIER_STOCK_MAPPING')
      AND [name] = N'UX_RII_SUPPLIER_STOCK_MAPPING_IDENTITY'
)
    CREATE UNIQUE INDEX [UX_RII_SUPPLIER_STOCK_MAPPING_IDENTITY]
        ON [dbo].[RII_SUPPLIER_STOCK_MAPPING]
            ([BranchCode], [SupplierId], [NormalizedSupplierStockCode])
        WHERE [IsDeleted] = 0;

DECLARE @ViewPermissionId bigint;
DECLARE @ManagePermissionId bigint;

SELECT @ViewPermissionId = [Id]
FROM [dbo].[RII_PERMISSION_DEFINITIONS]
WHERE [Code] = N'WMS.GOODS_RECEIPT.SUPPLIER_STOCK_MAPPING.VIEW';

IF @ViewPermissionId IS NULL
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM [dbo].[RII_PERMISSION_DEFINITIONS] WHERE [Id] = 2304
    )
    BEGIN
        SET IDENTITY_INSERT [dbo].[RII_PERMISSION_DEFINITIONS] ON;
        INSERT INTO [dbo].[RII_PERMISSION_DEFINITIONS]
            ([Id], [AvailableOnMobile], [AvailableOnWeb], [BranchCode], [Code],
             [CreatedDate], [IsActive], [Name])
        VALUES
            (2304, 0, 1, N'0',
             N'WMS.GOODS_RECEIPT.SUPPLIER_STOCK_MAPPING.VIEW',
             '2026-07-21T00:00:00', 1,
             N'Tedarikçi stok eşlemelerini görüntüle');
        SET IDENTITY_INSERT [dbo].[RII_PERMISSION_DEFINITIONS] OFF;
    END
    ELSE
    BEGIN
        INSERT INTO [dbo].[RII_PERMISSION_DEFINITIONS]
            ([AvailableOnMobile], [AvailableOnWeb], [BranchCode], [Code],
             [CreatedDate], [IsActive], [Name])
        VALUES
            (0, 1, N'0',
             N'WMS.GOODS_RECEIPT.SUPPLIER_STOCK_MAPPING.VIEW',
             '2026-07-21T00:00:00', 1,
             N'Tedarikçi stok eşlemelerini görüntüle');
    END;
END;

SELECT @ManagePermissionId = [Id]
FROM [dbo].[RII_PERMISSION_DEFINITIONS]
WHERE [Code] = N'WMS.GOODS_RECEIPT.SUPPLIER_STOCK_MAPPING.MANAGE';

IF @ManagePermissionId IS NULL
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM [dbo].[RII_PERMISSION_DEFINITIONS] WHERE [Id] = 2305
    )
    BEGIN
        SET IDENTITY_INSERT [dbo].[RII_PERMISSION_DEFINITIONS] ON;
        INSERT INTO [dbo].[RII_PERMISSION_DEFINITIONS]
            ([Id], [AvailableOnMobile], [AvailableOnWeb], [BranchCode], [Code],
             [CreatedDate], [IsActive], [Name])
        VALUES
            (2305, 0, 1, N'0',
             N'WMS.GOODS_RECEIPT.SUPPLIER_STOCK_MAPPING.MANAGE',
             '2026-07-21T00:00:00', 1,
             N'Tedarikçi stok eşlemelerini yönet');
        SET IDENTITY_INSERT [dbo].[RII_PERMISSION_DEFINITIONS] OFF;
    END
    ELSE
    BEGIN
        INSERT INTO [dbo].[RII_PERMISSION_DEFINITIONS]
            ([AvailableOnMobile], [AvailableOnWeb], [BranchCode], [Code],
             [CreatedDate], [IsActive], [Name])
        VALUES
            (0, 1, N'0',
             N'WMS.GOODS_RECEIPT.SUPPLIER_STOCK_MAPPING.MANAGE',
             '2026-07-21T00:00:00', 1,
             N'Tedarikçi stok eşlemelerini yönet');
    END;
END;

SELECT @ViewPermissionId = [Id]
FROM [dbo].[RII_PERMISSION_DEFINITIONS]
WHERE [Code] = N'WMS.GOODS_RECEIPT.SUPPLIER_STOCK_MAPPING.VIEW';

SELECT @ManagePermissionId = [Id]
FROM [dbo].[RII_PERMISSION_DEFINITIONS]
WHERE [Code] = N'WMS.GOODS_RECEIPT.SUPPLIER_STOCK_MAPPING.MANAGE';

IF EXISTS (
    SELECT 1 FROM [dbo].[RII_PERMISSION_GROUPS]
    WHERE [Id] = 1001 AND [IsDeleted] = 0
)
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM [dbo].[RII_PERMISSION_GROUP_PERMISSIONS]
        WHERE [PermissionGroupId] = 1001
          AND [PermissionDefinitionId] = @ViewPermissionId
          AND [IsDeleted] = 0
    )
        INSERT INTO [dbo].[RII_PERMISSION_GROUP_PERMISSIONS]
            ([BranchCode], [CreatedDate], [PermissionDefinitionId], [PermissionGroupId])
        VALUES (N'0', '2026-07-21T00:00:00', @ViewPermissionId, 1001);

    IF NOT EXISTS (
        SELECT 1 FROM [dbo].[RII_PERMISSION_GROUP_PERMISSIONS]
        WHERE [PermissionGroupId] = 1001
          AND [PermissionDefinitionId] = @ManagePermissionId
          AND [IsDeleted] = 0
    )
        INSERT INTO [dbo].[RII_PERMISSION_GROUP_PERMISSIONS]
            ([BranchCode], [CreatedDate], [PermissionDefinitionId], [PermissionGroupId])
        VALUES (N'0', '2026-07-21T00:00:00', @ManagePermissionId, 1001);
END;

IF OBJECT_ID(N'dbo.__EFMigrationsHistory', N'U') IS NOT NULL
   AND NOT EXISTS (
       SELECT 1 FROM [dbo].[__EFMigrationsHistory]
       WHERE [MigrationId] = N'20260730075330_AddSupplierStockMappings'
   )
BEGIN
    INSERT INTO [dbo].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260730075330_AddSupplierStockMappings', N'10.0.10');
END;

COMMIT;
