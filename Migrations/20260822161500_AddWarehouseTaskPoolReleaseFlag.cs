using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using verii_wms_api_v2.Modules.Identity.Infrastructure;

#nullable disable

namespace verii_wms_api_v2.Migrations;

[DbContext(typeof(WmsDbContext))]
[Migration("20260822161500_AddWarehouseTaskPoolReleaseFlag")]
public partial class AddWarehouseTaskPoolReleaseFlag : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "ReleasedToWarehousePool",
            table: "RII_WT_TASK",
            type: "bit",
            nullable: false,
            defaultValue: false);

        // Önceki release-to-pool sürümünün Description damgasını kalıcı alana taşı.
        migrationBuilder.Sql("""
            UPDATE [RII_WT_TASK]
            SET [ReleasedToWarehousePool] = CAST(1 AS bit)
            WHERE [ReleasedToWarehousePool] = CAST(0 AS bit)
              AND [Description] IS NOT NULL
              AND [Description] LIKE N'%depo havuzuna bırakıldı%';
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "ReleasedToWarehousePool",
            table: "RII_WT_TASK");
    }
}
