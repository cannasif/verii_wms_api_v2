/*
  HOTFIX: Permission group TemplateKey / IsProtected migration resume
  Olusturma: 2026-08-06
  Onceki tam script TemplateKey hatasi verdiyse bu dosyayi calistirin.
  Idempotent'tir; ana scriptin kaldigi yerden devam eder.

  SSMS:
  1. USE [WmsVeritabani];
  2. Bu scripti calistirin
  3. LatestMigrationApplied = 1 olmali
*/

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260805131731_AddProtectedDefaultPermissionGroups'
)
BEGIN
    ALTER TABLE [RII_PERMISSION_GROUPS] ADD [IsProtected] bit NOT NULL DEFAULT CAST(0 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260805131731_AddProtectedDefaultPermissionGroups'
)
BEGIN
    ALTER TABLE [RII_PERMISSION_GROUPS] ADD [TemplateKey] nvarchar(80) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260805131731_AddProtectedDefaultPermissionGroups'
)
BEGIN
    EXEC(N'UPDATE [RII_PERMISSION_GROUPS] SET [IsProtected] = CAST(1 AS bit), [TemplateKey] = N''SYSTEM_ADMINISTRATORS''
    WHERE [Id] = CAST(1001 AS bigint);
    SELECT @@ROWCOUNT');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260805131731_AddProtectedDefaultPermissionGroups'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_RII_PERMISSION_GROUPS_TemplateKey] ON [RII_PERMISSION_GROUPS] ([TemplateKey]) WHERE [TemplateKey] IS NOT NULL AND [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260805131731_AddProtectedDefaultPermissionGroups'
)
BEGIN
    EXEC sys.sp_executesql N'INSERT INTO RII_PERMISSION_GROUPS
        (BranchCode, CreatedDate, IsDeleted, Name, Description, IsSystemAdmin, IsProtected, TemplateKey, IsActive)
    SELECT ''0'', SYSUTCDATETIME(), 0, seed.Name, seed.Description, 0, 1, seed.TemplateKey, 1
    FROM (VALUES
        (N''DEPO_YONETICILERI'', N''Depo Yöneticileri'', N''Depo operasyonlarını ve operasyon ayarlarını yöneten kurumsal varsayılan şablon.''),
        (N''DEPO_OPERATORLERI'', N''Depo Operatörleri'', N''Atanmış fiziksel depo işlerini yürüten kullanıcılar için güvenli varsayılan şablon.''),
        (N''KALITE_UZMANLARI'', N''Kalite Uzmanları'', N''Kalite kontrol, inceleme ve serbest bırakma işlemleri için varsayılan şablon.''),
        (N''SALT_OKUNUR_RAPORLAMA'', N''Salt Okunur ve Raporlama'', N''Operasyon verilerini değiştirmeden görüntüleme ve raporlama için varsayılan şablon.'')
    ) seed(TemplateKey, Name, Description)
    WHERE NOT EXISTS (
        SELECT 1 FROM RII_PERMISSION_GROUPS existing
        WHERE existing.TemplateKey = seed.TemplateKey AND existing.IsDeleted = 0
    );

    INSERT INTO RII_PERMISSION_GROUP_PERMISSIONS
        (BranchCode, CreatedDate, IsDeleted, PermissionGroupId, PermissionDefinitionId)
    SELECT ''0'', SYSUTCDATETIME(), 0, groups.Id, permissions.Id
    FROM RII_PERMISSION_GROUPS groups
    JOIN RII_PERMISSION_DEFINITIONS permissions ON permissions.IsDeleted = 0 AND permissions.IsActive = 1
    WHERE groups.IsDeleted = 0
      AND (
        (groups.TemplateKey = N''DEPO_YONETICILERI'' AND (permissions.Code LIKE N''WMS.%'' OR permissions.Code LIKE N''ERP.%''))
        OR
        (groups.TemplateKey = N''DEPO_OPERATORLERI''
            AND permissions.Code LIKE N''WMS.%''
            AND (
                permissions.Code LIKE N''%.VIEW''
                OR permissions.Code LIKE N''%.CREATE''
                OR permissions.Code LIKE N''%.OPERATE''
                OR permissions.Code LIKE N''%.RECEIVE''
                OR permissions.Code LIKE N''%.COMPLETE''
                OR permissions.Code LIKE N''%.PRINT''
                OR permissions.Code LIKE N''%.CHECK''
                OR permissions.Code LIKE N''%.POST''
            )
            AND permissions.Code NOT LIKE N''%.SETTINGS.%''
            AND permissions.Code NOT LIKE N''%.POLICY.%''
            AND permissions.Code NOT LIKE N''%.RULES.%'')
        OR
        (groups.TemplateKey = N''KALITE_UZMANLARI''
            AND (
                permissions.Code LIKE N''WMS.QUALITY.%''
                OR permissions.Code IN (
                    N''WMS.GOODS_RECEIPT.VIEW'',
                    N''WMS.STOCK_BALANCES.VIEW'',
                    N''WMS.STOCK_MOVEMENTS.VIEW''
                )
            ))
        OR
        (groups.TemplateKey = N''SALT_OKUNUR_RAPORLAMA''
            AND (permissions.Code LIKE N''%.VIEW'' OR permissions.Code LIKE N''%.REPORTS.VIEW''))
      )
      AND NOT EXISTS (
        SELECT 1 FROM RII_PERMISSION_GROUP_PERMISSIONS link
        WHERE link.PermissionGroupId = groups.Id
          AND link.PermissionDefinitionId = permissions.Id
          AND link.IsDeleted = 0
      );

    UPDATE users
    SET users.Role = N''Admin''
    FROM RII_USERS users
    WHERE LOWER(users.Role) <> N''superadmin''
      AND EXISTS (
        SELECT 1
        FROM RII_USER_PERMISSION_GROUPS userGroups
        JOIN RII_PERMISSION_GROUPS groups ON groups.Id = userGroups.PermissionGroupId
        WHERE userGroups.UserId = users.Id
          AND userGroups.IsDeleted = 0
          AND groups.IsDeleted = 0
          AND groups.IsActive = 1
          AND groups.IsSystemAdmin = 1
      );

    UPDATE users
    SET users.Role = N''User''
    FROM RII_USERS users
    WHERE LOWER(users.Role) = N''admin''
      AND NOT EXISTS (
        SELECT 1
        FROM RII_USER_PERMISSION_GROUPS userGroups
        JOIN RII_PERMISSION_GROUPS groups ON groups.Id = userGroups.PermissionGroupId
        WHERE userGroups.UserId = users.Id
          AND userGroups.IsDeleted = 0
          AND groups.IsDeleted = 0
          AND groups.IsActive = 1
          AND groups.IsSystemAdmin = 1
      );';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260805131731_AddProtectedDefaultPermissionGroups'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260805131731_AddProtectedDefaultPermissionGroups', N'10.0.10');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260805140037_AddOperationalPermissionGroupTemplates'
)
BEGIN
    EXEC sys.sp_executesql N'INSERT INTO RII_PERMISSION_GROUPS
        (BranchCode, CreatedDate, IsDeleted, Name, Description, IsSystemAdmin, IsProtected, TemplateKey, IsActive)
    SELECT ''0'', SYSUTCDATETIME(), 0, seed.Name, seed.Description, 0, 1, seed.TemplateKey, 1
    FROM (VALUES
        (N''VARDIYA_AMIRLERI'', N''Vardiya Amirleri'', N''Operasyonları izler; emir atama, onay, serbest bırakma, kısmi/tam tamamlama ve kontrollü iptal işlemlerini yürütür.''),
        (N''MAL_KABUL_OPERATORLERI'', N''Mal Kabul Operatörleri'', N''Siparişli, siparişsiz, doğrudan ve sac mal kabulünün fiziksel kabul adımlarını yürütür.''),
        (N''YERLESTIRME_TRANSFER_OPERATORLERI'', N''Yerleştirme ve Transfer Operatörleri'', N''Raf yerleştirme, ambar giriş ve depolar arası/üretim/fason transfer görevlerini yürütür.''),
        (N''TOPLAMA_SEVK_PAKETLEME_OPERATORLERI'', N''Toplama, Sevk ve Paketleme Operatörleri'', N''Toplama, ambar çıkış, paketleme, etiketleme ve sevk operasyonlarını yürütür.''),
        (N''KALITE_YONETICILERI'', N''Kalite Yöneticileri'', N''Kalite kurallarını, ayarlarını, inceleme kararlarını ve stok serbest bırakma yetkisini yönetir.''),
        (N''STOK_KONTROL_UZMANLARI'', N''Stok Kontrol Uzmanları'', N''Raf ve stok tanımları, stok hareketi ters kayıtları ve bakiye uzlaştırma işlemlerini yürütür.''),
        (N''URETIM_LOJISTIK_OPERATORLERI'', N''Üretim Lojistik Operatörleri'', N''Üretim emirleri ile üretime ve fasona transfer operasyonlarını yürütür.''),
        (N''SATINALMA_UZMANLARI'', N''Satınalma Uzmanları'', N''Satınalma talebi, teklif talebi, tedarikçi teklifi ve sipariş kayıtlarını onay yetkisi olmadan yönetir.''),
        (N''SATINALMA_ONAYLAYICILARI'', N''Satınalma Onaylayıcıları'', N''Satınalma belgelerini görüntüler ve görevler ayrılığına uygun olarak onaylar.''),
        (N''KKD_OPERATORLERI'', N''KKD Operatörleri'', N''KKD personel, hak sorgulama, dağıtım ve raporlama operasyonlarını yürütür.''),
        (N''KKD_YONETICILERI'', N''KKD Yöneticileri'', N''KKD tanım, matris, politika, kota aşımı ve operasyon kurallarını yönetir.''),
        (N''ERP_ENTEGRASYON_UZMANLARI'', N''ERP Entegrasyon Uzmanları'', N''ERP/Netsis veri okuma, eşleme, yeniden deneme, entegrasyon bağlantıları ve iş tetikleme işlemlerini yürütür.''),
        (N''SISTEM_DENETCILERI'', N''Sistem Denetçileri'', N''Operasyon ve ERP kayıtlarını değiştirmeden görüntüler; denetim kayıtlarına erişir.'')
    ) seed(TemplateKey, Name, Description)
    WHERE NOT EXISTS (
        SELECT 1 FROM RII_PERMISSION_GROUPS existing
        WHERE existing.TemplateKey = seed.TemplateKey AND existing.IsDeleted = 0
    );

    DELETE links
    FROM RII_PERMISSION_GROUP_PERMISSIONS links
    JOIN RII_PERMISSION_GROUPS groups ON groups.Id = links.PermissionGroupId
    WHERE groups.TemplateKey IN (
        N''DEPO_YONETICILERI'', N''KALITE_UZMANLARI'', N''SALT_OKUNUR_RAPORLAMA'',
        N''VARDIYA_AMIRLERI'', N''MAL_KABUL_OPERATORLERI'', N''YERLESTIRME_TRANSFER_OPERATORLERI'',
        N''TOPLAMA_SEVK_PAKETLEME_OPERATORLERI'', N''KALITE_YONETICILERI'', N''STOK_KONTROL_UZMANLARI'',
        N''URETIM_LOJISTIK_OPERATORLERI'', N''SATINALMA_UZMANLARI'', N''SATINALMA_ONAYLAYICILARI'',
        N''KKD_OPERATORLERI'', N''KKD_YONETICILERI'', N''ERP_ENTEGRASYON_UZMANLARI'', N''SISTEM_DENETCILERI''
    );

    INSERT INTO RII_PERMISSION_GROUP_PERMISSIONS
        (BranchCode, CreatedDate, IsDeleted, PermissionGroupId, PermissionDefinitionId)
    SELECT ''0'', SYSUTCDATETIME(), 0, groups.Id, permissions.Id
    FROM RII_PERMISSION_GROUPS groups
    CROSS JOIN RII_PERMISSION_DEFINITIONS permissions
    WHERE groups.IsDeleted = 0
      AND permissions.IsDeleted = 0
      AND permissions.IsActive = 1
      AND (
        (groups.TemplateKey = N''DEPO_YONETICILERI''
            AND permissions.Code LIKE N''WMS.%''
            AND permissions.Code NOT LIKE N''WMS.PROCUREMENT.%''
            AND permissions.Code NOT LIKE N''WMS.KKD.%'')
        OR
        (groups.TemplateKey = N''KALITE_UZMANLARI'' AND permissions.Code IN (
            N''WMS.QUALITY.INSPECTIONS.VIEW'', N''WMS.QUALITY.INSPECTIONS.DECIDE'',
            N''WMS.QUALITY.RULES.VIEW'', N''WMS.QUALITY.SETTINGS.VIEW'',
            N''WMS.GOODS_RECEIPT.VIEW'', N''WMS.STOCK_BALANCES.VIEW'', N''WMS.STOCK_MOVEMENTS.VIEW''))
        OR
        (groups.TemplateKey = N''SALT_OKUNUR_RAPORLAMA''
            AND (permissions.Code LIKE N''WMS.%.VIEW'' OR permissions.Code = N''ERP.MIRROR.VIEW''))
        OR
        (groups.TemplateKey = N''VARDIYA_AMIRLERI''
            AND permissions.Code LIKE N''WMS.%''
            AND (permissions.Code LIKE N''%.VIEW'' OR permissions.Code LIKE N''%.APPROVE''
                OR permissions.Code LIKE N''%.ASSIGN'' OR permissions.Code LIKE N''%.RELEASE''
                OR permissions.Code LIKE N''%.COMPLETE'' OR permissions.Code LIKE N''%.CANCEL''
                OR permissions.Code LIKE N''%.REOPEN'' OR permissions.Code LIKE N''%.DECIDE''))
        OR
        (groups.TemplateKey = N''MAL_KABUL_OPERATORLERI'' AND (
            permissions.Code IN (
                N''WMS.GOODS_RECEIPT.VIEW'', N''WMS.GOODS_RECEIPT.CREATE'', N''WMS.GOODS_RECEIPT.UPDATE'',
                N''WMS.GOODS_RECEIPT.RECEIVE'', N''WMS.GOODS_RECEIPT.COMPLETE'',
                N''WMS.GOODS_RECEIPT.SETTINGS.VIEW'', N''WMS.LOCATIONS.VIEW'', N''WMS.STOCK_BALANCES.VIEW'',
                N''WMS.BARCODE_DESIGNER.VIEW'', N''WMS.BARCODE_DESIGNER.PRINT'',
                N''WMS.BARCODE_POLICY.VIEW'', N''WMS.BARCODE_POLICY.GENERATE'', N''ERP.NETSIS_READ.VIEW'')
            OR permissions.Code LIKE N''WMS.STEEL_RECEIPT.%''))
        OR
        (groups.TemplateKey = N''YERLESTIRME_TRANSFER_OPERATORLERI'' AND (
            permissions.Code IN (N''WMS.LOCATIONS.VIEW'', N''WMS.STOCK_BALANCES.VIEW'', N''WMS.STOCK_MOVEMENTS.VIEW'')
            OR (permissions.Code LIKE N''WMS.WAREHOUSE_TRANSFER.%'' AND permissions.Code NOT LIKE N''%.APPROVE'')
            OR (permissions.Code LIKE N''WMS.WAREHOUSE_INBOUND.%'' AND permissions.Code NOT LIKE N''%.SETTINGS.MANAGE'')
            OR permissions.Code IN (N''WMS.PRODUCTION_TRANSFER.VIEW'', N''WMS.PRODUCTION_TRANSFER.OPERATE'',
                N''WMS.SUBCONTRACTING_TRANSFER.VIEW'', N''WMS.SUBCONTRACTING_TRANSFER.OPERATE'')))
        OR
        (groups.TemplateKey = N''TOPLAMA_SEVK_PAKETLEME_OPERATORLERI'' AND (
            permissions.Code IN (N''WMS.LOCATIONS.VIEW'', N''WMS.STOCK_BALANCES.VIEW'',
                N''WMS.BARCODE_DESIGNER.VIEW'', N''WMS.BARCODE_DESIGNER.PRINT'')
            OR (permissions.Code LIKE N''WMS.SHIPPING.%'' AND permissions.Code NOT LIKE N''%.APPROVE'' AND permissions.Code NOT LIKE N''%.SETTINGS.MANAGE'')
            OR (permissions.Code LIKE N''WMS.WAREHOUSE_OUTBOUND.%'' AND permissions.Code NOT LIKE N''%.APPROVE'' AND permissions.Code NOT LIKE N''%.SETTINGS.MANAGE'')
            OR (permissions.Code LIKE N''WMS.PACKING.%'' AND permissions.Code NOT LIKE N''%.DEFINITIONS.MANAGE'' AND permissions.Code NOT LIKE N''%.SETTINGS.MANAGE'')))
        OR
        (groups.TemplateKey = N''KALITE_YONETICILERI'' AND (
            permissions.Code LIKE N''WMS.QUALITY.%''
            OR permissions.Code IN (N''WMS.GOODS_RECEIPT.VIEW'', N''WMS.STOCK_BALANCES.VIEW'', N''WMS.STOCK_MOVEMENTS.VIEW'')))
        OR
        (groups.TemplateKey = N''STOK_KONTROL_UZMANLARI'' AND (
            permissions.Code LIKE N''WMS.LOCATIONS.%''
            OR permissions.Code LIKE N''WMS.STOCK_BALANCES.%''
            OR permissions.Code LIKE N''WMS.STOCK_MOVEMENTS.%''
            OR permissions.Code IN (N''WMS.DOCUMENT_SERIES.VIEW'', N''ERP.MIRROR.VIEW'')))
        OR
        (groups.TemplateKey = N''URETIM_LOJISTIK_OPERATORLERI'' AND (
            permissions.Code IN (N''WMS.LOCATIONS.VIEW'', N''WMS.STOCK_BALANCES.VIEW'', N''ERP.NETSIS_READ.VIEW'')
            OR permissions.Code LIKE N''WMS.PRODUCTION.%''
            OR (permissions.Code LIKE N''WMS.PRODUCTION_TRANSFER.%'' AND permissions.Code NOT LIKE N''%.SETTINGS.MANAGE'' AND permissions.Code NOT LIKE N''%.APPROVE'')
            OR (permissions.Code LIKE N''WMS.SUBCONTRACTING_TRANSFER.%'' AND permissions.Code NOT LIKE N''%.SETTINGS.MANAGE'' AND permissions.Code NOT LIKE N''%.APPROVE'')))
        OR
        (groups.TemplateKey = N''SATINALMA_UZMANLARI''
            AND permissions.Code LIKE N''WMS.PROCUREMENT.%'' AND permissions.Code <> N''WMS.PROCUREMENT.APPROVE'')
        OR
        (groups.TemplateKey = N''SATINALMA_ONAYLAYICILARI''
            AND permissions.Code IN (N''WMS.PROCUREMENT.VIEW'', N''WMS.PROCUREMENT.APPROVE''))
        OR
        (groups.TemplateKey = N''KKD_OPERATORLERI'' AND permissions.Code IN (
            N''WMS.KKD.DEFINITIONS.VIEW'', N''WMS.KKD.EMPLOYEES.VIEW'', N''WMS.KKD.EMPLOYEES.MANAGE'',
            N''WMS.KKD.MATRICES.VIEW'', N''WMS.KKD.ENTITLEMENT.CHECK'', N''WMS.KKD.DISTRIBUTION.OPERATE'',
            N''WMS.KKD.REPORTS.VIEW''))
        OR
        (groups.TemplateKey = N''KKD_YONETICILERI'' AND permissions.Code LIKE N''WMS.KKD.%'')
        OR
        (groups.TemplateKey = N''ERP_ENTEGRASYON_UZMANLARI'' AND (
            permissions.Code LIKE N''ERP.%''
            OR permissions.Code IN (N''SYSTEM.HANGFIRE.VIEW'', N''SYSTEM.HANGFIRE.TRIGGER'',
                N''WMS.GOODS_RECEIPT.ERP_RETRY'', N''WMS.INCOMING_INVOICE.VIEW'', N''WMS.INCOMING_INVOICE.IMPORT'',
                N''WMS.INCOMING_INVOICE.OCR_IMPORT'', N''WMS.INCOMING_INVOICE.CONNECTIONS.MANAGE'')))
        OR
        (groups.TemplateKey = N''SISTEM_DENETCILERI'' AND (
            permissions.Code = N''SYSTEM.AUDIT.VIEW''
            OR permissions.Code LIKE N''WMS.%.VIEW''
            OR permissions.Code IN (N''ERP.MIRROR.VIEW'', N''ERP.NETSIS_READ.VIEW'')))
      );';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260805140037_AddOperationalPermissionGroupTemplates'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260805140037_AddOperationalPermissionGroupTemplates', N'10.0.10');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260805192720_AddProductionTaskAssignmentReturn'
)
BEGIN
    ALTER TABLE [RII_WT_TASK] ADD [OriginTaskId] bigint NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260805192720_AddProductionTaskAssignmentReturn'
)
BEGIN
    ALTER TABLE [RII_WT_TASK] ADD [OriginUserId] bigint NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260805192720_AddProductionTaskAssignmentReturn'
)
BEGIN
    ALTER TABLE [RII_WT_TASK] ADD [PreviousTaskId] bigint NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260805192720_AddProductionTaskAssignmentReturn'
)
BEGIN
    CREATE INDEX [IX_RII_WT_TASK_OriginTaskId] ON [RII_WT_TASK] ([OriginTaskId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260805192720_AddProductionTaskAssignmentReturn'
)
BEGIN
    CREATE INDEX [IX_RII_WT_TASK_PreviousTaskId] ON [RII_WT_TASK] ([PreviousTaskId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260805192720_AddProductionTaskAssignmentReturn'
)
BEGIN
    ALTER TABLE [RII_WT_TASK] ADD CONSTRAINT [FK_RII_WT_TASK_RII_WT_TASK_OriginTaskId] FOREIGN KEY ([OriginTaskId]) REFERENCES [RII_WT_TASK] ([Id]) ON DELETE NO ACTION;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260805192720_AddProductionTaskAssignmentReturn'
)
BEGIN
    ALTER TABLE [RII_WT_TASK] ADD CONSTRAINT [FK_RII_WT_TASK_RII_WT_TASK_PreviousTaskId] FOREIGN KEY ([PreviousTaskId]) REFERENCES [RII_WT_TASK] ([Id]) ON DELETE NO ACTION;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260805192720_AddProductionTaskAssignmentReturn'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260805192720_AddProductionTaskAssignmentReturn', N'10.0.10');
END;

COMMIT;
GO

/* Calistirma sonu sema dogrulamasi. */
SELECT
    COL_LENGTH(N'dbo.RII_WAREHOUSE', N'DefaultGoodsReceiptLocationId') AS DefaultLocationColumnByteLength,
    (SELECT COUNT_BIG(*) FROM dbo.__EFMigrationsHistory) AS AppliedMigrationCount,
    CASE WHEN EXISTS
    (
        SELECT 1 FROM dbo.__EFMigrationsHistory
        WHERE MigrationId = N'20260805192720_AddProductionTaskAssignmentReturn'
    ) THEN 1 ELSE 0 END AS LatestMigrationApplied;
GO

