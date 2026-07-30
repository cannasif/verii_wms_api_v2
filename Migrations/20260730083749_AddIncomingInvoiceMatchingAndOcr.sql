BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260730083749_AddIncomingInvoiceMatchingAndOcr'
)
BEGIN
    ALTER TABLE [RII_INCOMING_INVOICE_LINE] ADD [ConversionFactor] decimal(28,8) NOT NULL DEFAULT 1.0;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260730083749_AddIncomingInvoiceMatchingAndOcr'
)
BEGIN
    ALTER TABLE [RII_INCOMING_INVOICE_LINE] ADD [RecognitionConfidence] decimal(9,6) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260730083749_AddIncomingInvoiceMatchingAndOcr'
)
BEGIN
    ALTER TABLE [RII_INCOMING_INVOICE_LINE] ADD [SupplierStockMappingId] bigint NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260730083749_AddIncomingInvoiceMatchingAndOcr'
)
BEGIN
    DECLARE @var nvarchar(max);
    SELECT @var = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[RII_INCOMING_INVOICE_HEADER]') AND [c].[name] = N'ELogoConnectionId');
    IF @var IS NOT NULL EXEC(N'ALTER TABLE [RII_INCOMING_INVOICE_HEADER] DROP CONSTRAINT ' + @var + ';');
    ALTER TABLE [RII_INCOMING_INVOICE_HEADER] ALTER COLUMN [ELogoConnectionId] bigint NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260730083749_AddIncomingInvoiceMatchingAndOcr'
)
BEGIN
    ALTER TABLE [RII_INCOMING_INVOICE_HEADER] ADD [CaptureSource] int NOT NULL DEFAULT 1;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260730083749_AddIncomingInvoiceMatchingAndOcr'
)
BEGIN
    ALTER TABLE [RII_INCOMING_INVOICE_HEADER] ADD [RecognitionConfidence] decimal(9,6) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260730083749_AddIncomingInvoiceMatchingAndOcr'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'AvailableOnMobile', N'AvailableOnWeb', N'BranchCode', N'Code', N'CreatedBy', N'CreatedDate', N'DeletedBy', N'DeletedDate', N'Description', N'IsActive', N'Name', N'UpdatedBy', N'UpdatedDate') AND [object_id] = OBJECT_ID(N'[RII_PERMISSION_DEFINITIONS]'))
        SET IDENTITY_INSERT [RII_PERMISSION_DEFINITIONS] ON;
    EXEC(N'INSERT INTO [RII_PERMISSION_DEFINITIONS] ([Id], [AvailableOnMobile], [AvailableOnWeb], [BranchCode], [Code], [CreatedBy], [CreatedDate], [DeletedBy], [DeletedDate], [Description], [IsActive], [Name], [UpdatedBy], [UpdatedDate])
    VALUES (CAST(2306 AS bigint), CAST(0 AS bit), CAST(1 AS bit), N''0'', N''WMS.INCOMING_INVOICE.OCR_IMPORT'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, NULL, CAST(1 AS bit), N''Fatura belgesini OCR ile ön incelemeye al'', NULL, NULL)');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'AvailableOnMobile', N'AvailableOnWeb', N'BranchCode', N'Code', N'CreatedBy', N'CreatedDate', N'DeletedBy', N'DeletedDate', N'Description', N'IsActive', N'Name', N'UpdatedBy', N'UpdatedDate') AND [object_id] = OBJECT_ID(N'[RII_PERMISSION_DEFINITIONS]'))
        SET IDENTITY_INSERT [RII_PERMISSION_DEFINITIONS] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260730083749_AddIncomingInvoiceMatchingAndOcr'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'BranchCode', N'CreatedBy', N'CreatedDate', N'DeletedBy', N'DeletedDate', N'PermissionDefinitionId', N'PermissionGroupId', N'UpdatedBy', N'UpdatedDate') AND [object_id] = OBJECT_ID(N'[RII_PERMISSION_GROUP_PERMISSIONS]'))
        SET IDENTITY_INSERT [RII_PERMISSION_GROUP_PERMISSIONS] ON;
    EXEC(N'INSERT INTO [RII_PERMISSION_GROUP_PERMISSIONS] ([Id], [BranchCode], [CreatedBy], [CreatedDate], [DeletedBy], [DeletedDate], [PermissionDefinitionId], [PermissionGroupId], [UpdatedBy], [UpdatedDate])
    VALUES (CAST(2306 AS bigint), N''0'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, CAST(2306 AS bigint), CAST(1001 AS bigint), NULL, NULL)');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'BranchCode', N'CreatedBy', N'CreatedDate', N'DeletedBy', N'DeletedDate', N'PermissionDefinitionId', N'PermissionGroupId', N'UpdatedBy', N'UpdatedDate') AND [object_id] = OBJECT_ID(N'[RII_PERMISSION_GROUP_PERMISSIONS]'))
        SET IDENTITY_INSERT [RII_PERMISSION_GROUP_PERMISSIONS] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260730083749_AddIncomingInvoiceMatchingAndOcr'
)
BEGIN
    CREATE INDEX [IX_RII_INCOMING_INVOICE_LINE_SupplierStockMappingId] ON [RII_INCOMING_INVOICE_LINE] ([SupplierStockMappingId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260730083749_AddIncomingInvoiceMatchingAndOcr'
)
BEGIN
    ALTER TABLE [RII_INCOMING_INVOICE_LINE] ADD CONSTRAINT [FK_RII_INCOMING_INVOICE_LINE_RII_SUPPLIER_STOCK_MAPPING_SupplierStockMappingId] FOREIGN KEY ([SupplierStockMappingId]) REFERENCES [RII_SUPPLIER_STOCK_MAPPING] ([Id]) ON DELETE NO ACTION;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260730083749_AddIncomingInvoiceMatchingAndOcr'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260730083749_AddIncomingInvoiceMatchingAndOcr', N'10.0.10');
END;

COMMIT;
GO

