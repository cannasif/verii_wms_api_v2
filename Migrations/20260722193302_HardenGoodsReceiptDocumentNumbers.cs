using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace verii_wms_api_v2.Migrations
{
    /// <inheritdoc />
    public partial class HardenGoodsReceiptDocumentNumbers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "WaybillNo",
                table: "RII_GR_HEADER",
                type: "varchar(15)",
                unicode: false,
                maxLength: 15,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(50)",
                oldMaxLength: 50,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ElectronicWaybillNo",
                table: "RII_GR_HEADER",
                type: "varchar(16)",
                unicode: false,
                maxLength: 16,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(50)",
                oldMaxLength: 50,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "UX_RII_GR_HEADER_SUPPLIER_EWAYBILL",
                table: "RII_GR_HEADER",
                columns: new[] { "BranchCode", "SupplierId", "ElectronicWaybillNo" },
                unique: true,
                filter: "[IsDeleted] = 0 AND [ElectronicWaybillNo] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_RII_GR_HEADER_SUPPLIER_WAYBILL",
                table: "RII_GR_HEADER",
                columns: new[] { "BranchCode", "SupplierId", "WaybillNo" },
                unique: true,
                filter: "[IsDeleted] = 0 AND [WaybillNo] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_RII_GR_HEADER_SUPPLIER_EWAYBILL",
                table: "RII_GR_HEADER");

            migrationBuilder.DropIndex(
                name: "UX_RII_GR_HEADER_SUPPLIER_WAYBILL",
                table: "RII_GR_HEADER");

            migrationBuilder.AlterColumn<string>(
                name: "WaybillNo",
                table: "RII_GR_HEADER",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "varchar(15)",
                oldUnicode: false,
                oldMaxLength: 15,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ElectronicWaybillNo",
                table: "RII_GR_HEADER",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "varchar(16)",
                oldUnicode: false,
                oldMaxLength: 16,
                oldNullable: true);
        }
    }
}
