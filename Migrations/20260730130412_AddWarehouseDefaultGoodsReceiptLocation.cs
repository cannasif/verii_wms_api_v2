using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace verii_wms_api_v2.Migrations
{
    /// <inheritdoc />
    public partial class AddWarehouseDefaultGoodsReceiptLocation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "DefaultGoodsReceiptLocationId",
                table: "RII_WAREHOUSE",
                type: "bigint",
                nullable: true);

            migrationBuilder.Sql(SqlServerMigrationSql.Execute(
                """
                UPDATE warehouse
                SET warehouse.DefaultGoodsReceiptLocationId = defaultLocation.Id
                FROM RII_WAREHOUSE AS warehouse
                CROSS APPLY
                (
                    SELECT TOP (1) location.Id
                    FROM RII_LOCATION AS location
                    WHERE location.WarehouseId = warehouse.Id
                      AND location.IsDeleted = 0
                      AND location.IsActive = 1
                      AND UPPER(LTRIM(RTRIM(location.Code))) = N'YER1'
                    ORDER BY location.Id
                ) AS defaultLocation
                WHERE warehouse.IsDeleted = 0
                  AND warehouse.DefaultGoodsReceiptLocationId IS NULL;
                """));

            migrationBuilder.Sql(SqlServerMigrationSql.Execute(
                "CREATE INDEX [IX_RII_WAREHOUSE_DEFAULT_GR_LOCATION] ON [RII_WAREHOUSE] ([DefaultGoodsReceiptLocationId]);"));

            migrationBuilder.AddForeignKey(
                name: "FK_RII_WAREHOUSE_RII_LOCATION_DefaultGoodsReceiptLocationId",
                table: "RII_WAREHOUSE",
                column: "DefaultGoodsReceiptLocationId",
                principalTable: "RII_LOCATION",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RII_WAREHOUSE_RII_LOCATION_DefaultGoodsReceiptLocationId",
                table: "RII_WAREHOUSE");

            migrationBuilder.DropIndex(
                name: "IX_RII_WAREHOUSE_DEFAULT_GR_LOCATION",
                table: "RII_WAREHOUSE");

            migrationBuilder.DropColumn(
                name: "DefaultGoodsReceiptLocationId",
                table: "RII_WAREHOUSE");
        }
    }
}
