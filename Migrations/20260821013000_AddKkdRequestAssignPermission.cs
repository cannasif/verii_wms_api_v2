using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using verii_wms_api_v2.Modules.Identity.Infrastructure;

#nullable disable

namespace verii_wms_api_v2.Migrations
{
    [DbContext(typeof(WmsDbContext))]
    [Migration("20260821013000_AddKkdRequestAssignPermission")]
    public partial class AddKkdRequestAssignPermission : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // InsertData bu tablolarda entity-table eşlemesi yüzünden kırılıyor; idempotent SQL kullan.
            migrationBuilder.Sql("""
                IF NOT EXISTS (
                    SELECT 1 FROM RII_PERMISSION_DEFINITIONS
                    WHERE Id = 2516 OR Code = N'WMS.KKD.REQUESTS.ASSIGN'
                )
                BEGIN
                    SET IDENTITY_INSERT RII_PERMISSION_DEFINITIONS ON;
                    INSERT INTO RII_PERMISSION_DEFINITIONS
                        (Id, AvailableOnMobile, AvailableOnWeb, BranchCode, Code, CreatedDate, IsActive, IsDeleted, Name)
                    VALUES
                        (2516, 0, 1, N'0', N'WMS.KKD.REQUESTS.ASSIGN', '2026-07-21T00:00:00', 1, 0,
                         N'KKD hazırlama görevini başkasına ata veya devret');
                    SET IDENTITY_INSERT RII_PERMISSION_DEFINITIONS OFF;
                END;

                IF NOT EXISTS (
                    SELECT 1 FROM RII_PERMISSION_GROUP_PERMISSIONS WHERE Id = 2516
                )
                BEGIN
                    SET IDENTITY_INSERT RII_PERMISSION_GROUP_PERMISSIONS ON;
                    INSERT INTO RII_PERMISSION_GROUP_PERMISSIONS
                        (Id, BranchCode, CreatedDate, IsDeleted, PermissionDefinitionId, PermissionGroupId)
                    VALUES
                        (2516, N'0', '2026-07-21T00:00:00', 0, 2516, 1001);
                    SET IDENTITY_INSERT RII_PERMISSION_GROUP_PERMISSIONS OFF;
                END;

                -- Operatör: liste + kendi üzerine alma + toplama. Atama / devir yöneticiye.
                INSERT INTO RII_PERMISSION_GROUP_PERMISSIONS
                    (BranchCode, CreatedDate, IsDeleted, PermissionGroupId, PermissionDefinitionId)
                SELECT N'0', SYSUTCDATETIME(), 0, groups.Id, permissions.Id
                FROM RII_PERMISSION_GROUPS groups
                CROSS JOIN RII_PERMISSION_DEFINITIONS permissions
                WHERE groups.IsDeleted = 0
                  AND permissions.IsDeleted = 0
                  AND permissions.IsActive = 1
                  AND (
                    (groups.TemplateKey = N'KKD_OPERATORLERI'
                        AND permissions.Code IN (
                            N'WMS.KKD.REQUESTS.VIEW',
                            N'WMS.KKD.REQUESTS.RESOLVE'))
                    OR
                    (groups.TemplateKey IN (N'KKD_YONETICILERI', N'DEPO_YONETICILERI')
                        AND permissions.Code = N'WMS.KKD.REQUESTS.ASSIGN')
                  )
                  AND NOT EXISTS (
                    SELECT 1
                    FROM RII_PERMISSION_GROUP_PERMISSIONS existing
                    WHERE existing.PermissionGroupId = groups.Id
                      AND existing.PermissionDefinitionId = permissions.Id
                      AND existing.IsDeleted = 0
                  );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DELETE links
                FROM RII_PERMISSION_GROUP_PERMISSIONS links
                INNER JOIN RII_PERMISSION_DEFINITIONS permissions ON permissions.Id = links.PermissionDefinitionId
                WHERE permissions.Code = N'WMS.KKD.REQUESTS.ASSIGN';

                DELETE FROM RII_PERMISSION_DEFINITIONS WHERE Code = N'WMS.KKD.REQUESTS.ASSIGN';
                """);
        }
    }
}
