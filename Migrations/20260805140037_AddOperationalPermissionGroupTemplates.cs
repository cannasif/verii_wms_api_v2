using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace verii_wms_api_v2.Migrations
{
    /// <inheritdoc />
    public partial class AddOperationalPermissionGroupTemplates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(SqlServerMigrationSql.Execute("""
                INSERT INTO RII_PERMISSION_GROUPS
                    (BranchCode, CreatedDate, IsDeleted, Name, Description, IsSystemAdmin, IsProtected, TemplateKey, IsActive)
                SELECT '0', SYSUTCDATETIME(), 0, seed.Name, seed.Description, 0, 1, seed.TemplateKey, 1
                FROM (VALUES
                    (N'VARDIYA_AMIRLERI', N'Vardiya Amirleri', N'Operasyonları izler; emir atama, onay, serbest bırakma, kısmi/tam tamamlama ve kontrollü iptal işlemlerini yürütür.'),
                    (N'MAL_KABUL_OPERATORLERI', N'Mal Kabul Operatörleri', N'Siparişli, siparişsiz, doğrudan ve sac mal kabulünün fiziksel kabul adımlarını yürütür.'),
                    (N'YERLESTIRME_TRANSFER_OPERATORLERI', N'Yerleştirme ve Transfer Operatörleri', N'Raf yerleştirme, ambar giriş ve depolar arası/üretim/fason transfer görevlerini yürütür.'),
                    (N'TOPLAMA_SEVK_PAKETLEME_OPERATORLERI', N'Toplama, Sevk ve Paketleme Operatörleri', N'Toplama, ambar çıkış, paketleme, etiketleme ve sevk operasyonlarını yürütür.'),
                    (N'KALITE_YONETICILERI', N'Kalite Yöneticileri', N'Kalite kurallarını, ayarlarını, inceleme kararlarını ve stok serbest bırakma yetkisini yönetir.'),
                    (N'STOK_KONTROL_UZMANLARI', N'Stok Kontrol Uzmanları', N'Raf ve stok tanımları, stok hareketi ters kayıtları ve bakiye uzlaştırma işlemlerini yürütür.'),
                    (N'URETIM_LOJISTIK_OPERATORLERI', N'Üretim Lojistik Operatörleri', N'Üretim emirleri ile üretime ve fasona transfer operasyonlarını yürütür.'),
                    (N'SATINALMA_UZMANLARI', N'Satınalma Uzmanları', N'Satınalma talebi, teklif talebi, tedarikçi teklifi ve sipariş kayıtlarını onay yetkisi olmadan yönetir.'),
                    (N'SATINALMA_ONAYLAYICILARI', N'Satınalma Onaylayıcıları', N'Satınalma belgelerini görüntüler ve görevler ayrılığına uygun olarak onaylar.'),
                    (N'KKD_OPERATORLERI', N'KKD Operatörleri', N'KKD personel, hak sorgulama, dağıtım ve raporlama operasyonlarını yürütür.'),
                    (N'KKD_YONETICILERI', N'KKD Yöneticileri', N'KKD tanım, matris, politika, kota aşımı ve operasyon kurallarını yönetir.'),
                    (N'ERP_ENTEGRASYON_UZMANLARI', N'ERP Entegrasyon Uzmanları', N'ERP/Netsis veri okuma, eşleme, yeniden deneme, entegrasyon bağlantıları ve iş tetikleme işlemlerini yürütür.'),
                    (N'SISTEM_DENETCILERI', N'Sistem Denetçileri', N'Operasyon ve ERP kayıtlarını değiştirmeden görüntüler; denetim kayıtlarına erişir.')
                ) seed(TemplateKey, Name, Description)
                WHERE NOT EXISTS (
                    SELECT 1 FROM RII_PERMISSION_GROUPS existing
                    WHERE existing.TemplateKey = seed.TemplateKey AND existing.IsDeleted = 0
                );

                DELETE links
                FROM RII_PERMISSION_GROUP_PERMISSIONS links
                JOIN RII_PERMISSION_GROUPS groups ON groups.Id = links.PermissionGroupId
                WHERE groups.TemplateKey IN (
                    N'DEPO_YONETICILERI', N'KALITE_UZMANLARI', N'SALT_OKUNUR_RAPORLAMA',
                    N'VARDIYA_AMIRLERI', N'MAL_KABUL_OPERATORLERI', N'YERLESTIRME_TRANSFER_OPERATORLERI',
                    N'TOPLAMA_SEVK_PAKETLEME_OPERATORLERI', N'KALITE_YONETICILERI', N'STOK_KONTROL_UZMANLARI',
                    N'URETIM_LOJISTIK_OPERATORLERI', N'SATINALMA_UZMANLARI', N'SATINALMA_ONAYLAYICILARI',
                    N'KKD_OPERATORLERI', N'KKD_YONETICILERI', N'ERP_ENTEGRASYON_UZMANLARI', N'SISTEM_DENETCILERI'
                );

                INSERT INTO RII_PERMISSION_GROUP_PERMISSIONS
                    (BranchCode, CreatedDate, IsDeleted, PermissionGroupId, PermissionDefinitionId)
                SELECT '0', SYSUTCDATETIME(), 0, groups.Id, permissions.Id
                FROM RII_PERMISSION_GROUPS groups
                CROSS JOIN RII_PERMISSION_DEFINITIONS permissions
                WHERE groups.IsDeleted = 0
                  AND permissions.IsDeleted = 0
                  AND permissions.IsActive = 1
                  AND (
                    (groups.TemplateKey = N'DEPO_YONETICILERI'
                        AND permissions.Code LIKE N'WMS.%'
                        AND permissions.Code NOT LIKE N'WMS.PROCUREMENT.%'
                        AND permissions.Code NOT LIKE N'WMS.KKD.%')
                    OR
                    (groups.TemplateKey = N'KALITE_UZMANLARI' AND permissions.Code IN (
                        N'WMS.QUALITY.INSPECTIONS.VIEW', N'WMS.QUALITY.INSPECTIONS.DECIDE',
                        N'WMS.QUALITY.RULES.VIEW', N'WMS.QUALITY.SETTINGS.VIEW',
                        N'WMS.GOODS_RECEIPT.VIEW', N'WMS.STOCK_BALANCES.VIEW', N'WMS.STOCK_MOVEMENTS.VIEW'))
                    OR
                    (groups.TemplateKey = N'SALT_OKUNUR_RAPORLAMA'
                        AND (permissions.Code LIKE N'WMS.%.VIEW' OR permissions.Code = N'ERP.MIRROR.VIEW'))
                    OR
                    (groups.TemplateKey = N'VARDIYA_AMIRLERI'
                        AND permissions.Code LIKE N'WMS.%'
                        AND (permissions.Code LIKE N'%.VIEW' OR permissions.Code LIKE N'%.APPROVE'
                            OR permissions.Code LIKE N'%.ASSIGN' OR permissions.Code LIKE N'%.RELEASE'
                            OR permissions.Code LIKE N'%.COMPLETE' OR permissions.Code LIKE N'%.CANCEL'
                            OR permissions.Code LIKE N'%.REOPEN' OR permissions.Code LIKE N'%.DECIDE'))
                    OR
                    (groups.TemplateKey = N'MAL_KABUL_OPERATORLERI' AND (
                        permissions.Code IN (
                            N'WMS.GOODS_RECEIPT.VIEW', N'WMS.GOODS_RECEIPT.CREATE', N'WMS.GOODS_RECEIPT.UPDATE',
                            N'WMS.GOODS_RECEIPT.RECEIVE', N'WMS.GOODS_RECEIPT.COMPLETE',
                            N'WMS.GOODS_RECEIPT.SETTINGS.VIEW', N'WMS.LOCATIONS.VIEW', N'WMS.STOCK_BALANCES.VIEW',
                            N'WMS.BARCODE_DESIGNER.VIEW', N'WMS.BARCODE_DESIGNER.PRINT',
                            N'WMS.BARCODE_POLICY.VIEW', N'WMS.BARCODE_POLICY.GENERATE', N'ERP.NETSIS_READ.VIEW')
                        OR permissions.Code LIKE N'WMS.STEEL_RECEIPT.%'))
                    OR
                    (groups.TemplateKey = N'YERLESTIRME_TRANSFER_OPERATORLERI' AND (
                        permissions.Code IN (N'WMS.LOCATIONS.VIEW', N'WMS.STOCK_BALANCES.VIEW', N'WMS.STOCK_MOVEMENTS.VIEW')
                        OR (permissions.Code LIKE N'WMS.WAREHOUSE_TRANSFER.%' AND permissions.Code NOT LIKE N'%.APPROVE')
                        OR (permissions.Code LIKE N'WMS.WAREHOUSE_INBOUND.%' AND permissions.Code NOT LIKE N'%.SETTINGS.MANAGE')
                        OR permissions.Code IN (N'WMS.PRODUCTION_TRANSFER.VIEW', N'WMS.PRODUCTION_TRANSFER.OPERATE',
                            N'WMS.SUBCONTRACTING_TRANSFER.VIEW', N'WMS.SUBCONTRACTING_TRANSFER.OPERATE')))
                    OR
                    (groups.TemplateKey = N'TOPLAMA_SEVK_PAKETLEME_OPERATORLERI' AND (
                        permissions.Code IN (N'WMS.LOCATIONS.VIEW', N'WMS.STOCK_BALANCES.VIEW',
                            N'WMS.BARCODE_DESIGNER.VIEW', N'WMS.BARCODE_DESIGNER.PRINT')
                        OR (permissions.Code LIKE N'WMS.SHIPPING.%' AND permissions.Code NOT LIKE N'%.APPROVE' AND permissions.Code NOT LIKE N'%.SETTINGS.MANAGE')
                        OR (permissions.Code LIKE N'WMS.WAREHOUSE_OUTBOUND.%' AND permissions.Code NOT LIKE N'%.APPROVE' AND permissions.Code NOT LIKE N'%.SETTINGS.MANAGE')
                        OR (permissions.Code LIKE N'WMS.PACKING.%' AND permissions.Code NOT LIKE N'%.DEFINITIONS.MANAGE' AND permissions.Code NOT LIKE N'%.SETTINGS.MANAGE')))
                    OR
                    (groups.TemplateKey = N'KALITE_YONETICILERI' AND (
                        permissions.Code LIKE N'WMS.QUALITY.%'
                        OR permissions.Code IN (N'WMS.GOODS_RECEIPT.VIEW', N'WMS.STOCK_BALANCES.VIEW', N'WMS.STOCK_MOVEMENTS.VIEW')))
                    OR
                    (groups.TemplateKey = N'STOK_KONTROL_UZMANLARI' AND (
                        permissions.Code LIKE N'WMS.LOCATIONS.%'
                        OR permissions.Code LIKE N'WMS.STOCK_BALANCES.%'
                        OR permissions.Code LIKE N'WMS.STOCK_MOVEMENTS.%'
                        OR permissions.Code IN (N'WMS.DOCUMENT_SERIES.VIEW', N'ERP.MIRROR.VIEW')))
                    OR
                    (groups.TemplateKey = N'URETIM_LOJISTIK_OPERATORLERI' AND (
                        permissions.Code IN (N'WMS.LOCATIONS.VIEW', N'WMS.STOCK_BALANCES.VIEW', N'ERP.NETSIS_READ.VIEW')
                        OR permissions.Code LIKE N'WMS.PRODUCTION.%'
                        OR (permissions.Code LIKE N'WMS.PRODUCTION_TRANSFER.%' AND permissions.Code NOT LIKE N'%.SETTINGS.MANAGE' AND permissions.Code NOT LIKE N'%.APPROVE')
                        OR (permissions.Code LIKE N'WMS.SUBCONTRACTING_TRANSFER.%' AND permissions.Code NOT LIKE N'%.SETTINGS.MANAGE' AND permissions.Code NOT LIKE N'%.APPROVE')))
                    OR
                    (groups.TemplateKey = N'SATINALMA_UZMANLARI'
                        AND permissions.Code LIKE N'WMS.PROCUREMENT.%' AND permissions.Code <> N'WMS.PROCUREMENT.APPROVE')
                    OR
                    (groups.TemplateKey = N'SATINALMA_ONAYLAYICILARI'
                        AND permissions.Code IN (N'WMS.PROCUREMENT.VIEW', N'WMS.PROCUREMENT.APPROVE'))
                    OR
                    (groups.TemplateKey = N'KKD_OPERATORLERI' AND permissions.Code IN (
                        N'WMS.KKD.DEFINITIONS.VIEW', N'WMS.KKD.EMPLOYEES.VIEW', N'WMS.KKD.EMPLOYEES.MANAGE',
                        N'WMS.KKD.MATRICES.VIEW', N'WMS.KKD.ENTITLEMENT.CHECK', N'WMS.KKD.DISTRIBUTION.OPERATE',
                        N'WMS.KKD.REPORTS.VIEW'))
                    OR
                    (groups.TemplateKey = N'KKD_YONETICILERI' AND permissions.Code LIKE N'WMS.KKD.%')
                    OR
                    (groups.TemplateKey = N'ERP_ENTEGRASYON_UZMANLARI' AND (
                        permissions.Code LIKE N'ERP.%'
                        OR permissions.Code IN (N'SYSTEM.HANGFIRE.VIEW', N'SYSTEM.HANGFIRE.TRIGGER',
                            N'WMS.GOODS_RECEIPT.ERP_RETRY', N'WMS.INCOMING_INVOICE.VIEW', N'WMS.INCOMING_INVOICE.IMPORT',
                            N'WMS.INCOMING_INVOICE.OCR_IMPORT', N'WMS.INCOMING_INVOICE.CONNECTIONS.MANAGE')))
                    OR
                    (groups.TemplateKey = N'SISTEM_DENETCILERI' AND (
                        permissions.Code = N'SYSTEM.AUDIT.VIEW'
                        OR permissions.Code LIKE N'WMS.%.VIEW'
                        OR permissions.Code IN (N'ERP.MIRROR.VIEW', N'ERP.NETSIS_READ.VIEW')))
                  );
                """));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DELETE links
                FROM RII_PERMISSION_GROUP_PERMISSIONS links
                JOIN RII_PERMISSION_GROUPS groups ON groups.Id = links.PermissionGroupId
                WHERE groups.TemplateKey IN (
                    N'DEPO_YONETICILERI', N'KALITE_UZMANLARI', N'SALT_OKUNUR_RAPORLAMA',
                    N'VARDIYA_AMIRLERI', N'MAL_KABUL_OPERATORLERI', N'YERLESTIRME_TRANSFER_OPERATORLERI',
                    N'TOPLAMA_SEVK_PAKETLEME_OPERATORLERI', N'KALITE_YONETICILERI', N'STOK_KONTROL_UZMANLARI',
                    N'URETIM_LOJISTIK_OPERATORLERI', N'SATINALMA_UZMANLARI', N'SATINALMA_ONAYLAYICILARI',
                    N'KKD_OPERATORLERI', N'KKD_YONETICILERI', N'ERP_ENTEGRASYON_UZMANLARI', N'SISTEM_DENETCILERI'
                );

                DELETE FROM RII_PERMISSION_GROUPS
                WHERE TemplateKey IN (
                    N'VARDIYA_AMIRLERI', N'MAL_KABUL_OPERATORLERI', N'YERLESTIRME_TRANSFER_OPERATORLERI',
                    N'TOPLAMA_SEVK_PAKETLEME_OPERATORLERI', N'KALITE_YONETICILERI', N'STOK_KONTROL_UZMANLARI',
                    N'URETIM_LOJISTIK_OPERATORLERI', N'SATINALMA_UZMANLARI', N'SATINALMA_ONAYLAYICILARI',
                    N'KKD_OPERATORLERI', N'KKD_YONETICILERI', N'ERP_ENTEGRASYON_UZMANLARI', N'SISTEM_DENETCILERI'
                );

                INSERT INTO RII_PERMISSION_GROUP_PERMISSIONS
                    (BranchCode, CreatedDate, IsDeleted, PermissionGroupId, PermissionDefinitionId)
                SELECT '0', SYSUTCDATETIME(), 0, groups.Id, permissions.Id
                FROM RII_PERMISSION_GROUPS groups
                CROSS JOIN RII_PERMISSION_DEFINITIONS permissions
                WHERE groups.IsDeleted = 0 AND permissions.IsDeleted = 0 AND permissions.IsActive = 1
                  AND (
                    (groups.TemplateKey = N'DEPO_YONETICILERI' AND (permissions.Code LIKE N'WMS.%' OR permissions.Code LIKE N'ERP.%'))
                    OR (groups.TemplateKey = N'KALITE_UZMANLARI' AND (
                        permissions.Code LIKE N'WMS.QUALITY.%'
                        OR permissions.Code IN (N'WMS.GOODS_RECEIPT.VIEW', N'WMS.STOCK_BALANCES.VIEW', N'WMS.STOCK_MOVEMENTS.VIEW')))
                    OR (groups.TemplateKey = N'SALT_OKUNUR_RAPORLAMA' AND (permissions.Code LIKE N'%.VIEW' OR permissions.Code LIKE N'%.REPORTS.VIEW'))
                  );
                """);
        }
    }
}
