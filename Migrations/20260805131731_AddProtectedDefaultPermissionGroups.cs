using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace verii_wms_api_v2.Migrations
{
    /// <inheritdoc />
    public partial class AddProtectedDefaultPermissionGroups : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsProtected",
                table: "RII_PERMISSION_GROUPS",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "TemplateKey",
                table: "RII_PERMISSION_GROUPS",
                type: "nvarchar(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.UpdateData(
                table: "RII_PERMISSION_GROUPS",
                keyColumn: "Id",
                keyValue: 1001L,
                columns: new[] { "IsProtected", "TemplateKey" },
                values: new object[] { true, "SYSTEM_ADMINISTRATORS" });

            migrationBuilder.CreateIndex(
                name: "IX_RII_PERMISSION_GROUPS_TemplateKey",
                table: "RII_PERMISSION_GROUPS",
                column: "TemplateKey",
                unique: true,
                filter: "[TemplateKey] IS NOT NULL AND [IsDeleted] = 0");

            migrationBuilder.Sql(SqlServerMigrationSql.Execute("""
                INSERT INTO RII_PERMISSION_GROUPS
                    (BranchCode, CreatedDate, IsDeleted, Name, Description, IsSystemAdmin, IsProtected, TemplateKey, IsActive)
                SELECT '0', SYSUTCDATETIME(), 0, seed.Name, seed.Description, 0, 1, seed.TemplateKey, 1
                FROM (VALUES
                    (N'DEPO_YONETICILERI', N'Depo Yöneticileri', N'Depo operasyonlarını ve operasyon ayarlarını yöneten kurumsal varsayılan şablon.'),
                    (N'DEPO_OPERATORLERI', N'Depo Operatörleri', N'Atanmış fiziksel depo işlerini yürüten kullanıcılar için güvenli varsayılan şablon.'),
                    (N'KALITE_UZMANLARI', N'Kalite Uzmanları', N'Kalite kontrol, inceleme ve serbest bırakma işlemleri için varsayılan şablon.'),
                    (N'SALT_OKUNUR_RAPORLAMA', N'Salt Okunur ve Raporlama', N'Operasyon verilerini değiştirmeden görüntüleme ve raporlama için varsayılan şablon.')
                ) seed(TemplateKey, Name, Description)
                WHERE NOT EXISTS (
                    SELECT 1 FROM RII_PERMISSION_GROUPS existing
                    WHERE existing.TemplateKey = seed.TemplateKey AND existing.IsDeleted = 0
                );

                INSERT INTO RII_PERMISSION_GROUP_PERMISSIONS
                    (BranchCode, CreatedDate, IsDeleted, PermissionGroupId, PermissionDefinitionId)
                SELECT '0', SYSUTCDATETIME(), 0, groups.Id, permissions.Id
                FROM RII_PERMISSION_GROUPS groups
                JOIN RII_PERMISSION_DEFINITIONS permissions ON permissions.IsDeleted = 0 AND permissions.IsActive = 1
                WHERE groups.IsDeleted = 0
                  AND (
                    (groups.TemplateKey = N'DEPO_YONETICILERI' AND (permissions.Code LIKE N'WMS.%' OR permissions.Code LIKE N'ERP.%'))
                    OR
                    (groups.TemplateKey = N'DEPO_OPERATORLERI'
                        AND permissions.Code LIKE N'WMS.%'
                        AND (
                            permissions.Code LIKE N'%.VIEW'
                            OR permissions.Code LIKE N'%.CREATE'
                            OR permissions.Code LIKE N'%.OPERATE'
                            OR permissions.Code LIKE N'%.RECEIVE'
                            OR permissions.Code LIKE N'%.COMPLETE'
                            OR permissions.Code LIKE N'%.PRINT'
                            OR permissions.Code LIKE N'%.CHECK'
                            OR permissions.Code LIKE N'%.POST'
                        )
                        AND permissions.Code NOT LIKE N'%.SETTINGS.%'
                        AND permissions.Code NOT LIKE N'%.POLICY.%'
                        AND permissions.Code NOT LIKE N'%.RULES.%')
                    OR
                    (groups.TemplateKey = N'KALITE_UZMANLARI'
                        AND (
                            permissions.Code LIKE N'WMS.QUALITY.%'
                            OR permissions.Code IN (
                                N'WMS.GOODS_RECEIPT.VIEW',
                                N'WMS.STOCK_BALANCES.VIEW',
                                N'WMS.STOCK_MOVEMENTS.VIEW'
                            )
                        ))
                    OR
                    (groups.TemplateKey = N'SALT_OKUNUR_RAPORLAMA'
                        AND (permissions.Code LIKE N'%.VIEW' OR permissions.Code LIKE N'%.REPORTS.VIEW'))
                  )
                  AND NOT EXISTS (
                    SELECT 1 FROM RII_PERMISSION_GROUP_PERMISSIONS link
                    WHERE link.PermissionGroupId = groups.Id
                      AND link.PermissionDefinitionId = permissions.Id
                      AND link.IsDeleted = 0
                  );

                UPDATE users
                SET users.Role = N'Admin'
                FROM RII_USERS users
                WHERE LOWER(users.Role) <> N'superadmin'
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
                SET users.Role = N'User'
                FROM RII_USERS users
                WHERE LOWER(users.Role) = N'admin'
                  AND NOT EXISTS (
                    SELECT 1
                    FROM RII_USER_PERMISSION_GROUPS userGroups
                    JOIN RII_PERMISSION_GROUPS groups ON groups.Id = userGroups.PermissionGroupId
                    WHERE userGroups.UserId = users.Id
                      AND userGroups.IsDeleted = 0
                      AND groups.IsDeleted = 0
                      AND groups.IsActive = 1
                      AND groups.IsSystemAdmin = 1
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
                WHERE groups.TemplateKey IN (N'DEPO_YONETICILERI', N'DEPO_OPERATORLERI', N'KALITE_UZMANLARI', N'SALT_OKUNUR_RAPORLAMA');

                DELETE FROM RII_PERMISSION_GROUPS
                WHERE TemplateKey IN (N'DEPO_YONETICILERI', N'DEPO_OPERATORLERI', N'KALITE_UZMANLARI', N'SALT_OKUNUR_RAPORLAMA');
                """);

            migrationBuilder.DropIndex(
                name: "IX_RII_PERMISSION_GROUPS_TemplateKey",
                table: "RII_PERMISSION_GROUPS");

            migrationBuilder.DropColumn(
                name: "IsProtected",
                table: "RII_PERMISSION_GROUPS");

            migrationBuilder.DropColumn(
                name: "TemplateKey",
                table: "RII_PERMISSION_GROUPS");
        }
    }
}
