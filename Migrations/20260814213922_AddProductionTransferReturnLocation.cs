using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace verii_wms_api_v2.Migrations
{
    /// <inheritdoc />
    public partial class AddProductionTransferReturnLocation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "DefaultProductionTransferReturnLocationId",
                table: "RII_WAREHOUSE",
                type: "bigint",
                nullable: true);

            // Önceki sürümde üretim ayar ekranı yanlışlıkla normal DAT iade kolonunu
            // kullanıyordu. Mevcut müşteri seçimini kaybetmeden üretime özel alana taşı.
            migrationBuilder.Sql(
                """
                UPDATE [RII_WAREHOUSE]
                SET [DefaultProductionTransferReturnLocationId] = [DefaultTransferReturnLocationId]
                WHERE [DefaultProductionTransferReturnLocationId] IS NULL
                  AND [DefaultTransferReturnLocationId] IS NOT NULL;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_RII_WAREHOUSE_DEFAULT_PRODUCTION_TRANSFER_RETURN_LOCATION",
                table: "RII_WAREHOUSE",
                column: "DefaultProductionTransferReturnLocationId");

            migrationBuilder.AddForeignKey(
                name: "FK_RII_WAREHOUSE_RII_LOCATION_DefaultProductionTransferReturnLocationId",
                table: "RII_WAREHOUSE",
                column: "DefaultProductionTransferReturnLocationId",
                principalTable: "RII_LOCATION",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RII_WAREHOUSE_RII_LOCATION_DefaultProductionTransferReturnLocationId",
                table: "RII_WAREHOUSE");

            migrationBuilder.DropIndex(
                name: "IX_RII_WAREHOUSE_DEFAULT_PRODUCTION_TRANSFER_RETURN_LOCATION",
                table: "RII_WAREHOUSE");

            migrationBuilder.DropColumn(
                name: "DefaultProductionTransferReturnLocationId",
                table: "RII_WAREHOUSE");
        }
    }
}
