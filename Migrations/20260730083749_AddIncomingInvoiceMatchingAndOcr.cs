using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace verii_wms_api_v2.Migrations
{
    /// <inheritdoc />
    public partial class AddIncomingInvoiceMatchingAndOcr : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "ConversionFactor",
                table: "RII_INCOMING_INVOICE_LINE",
                type: "decimal(28,8)",
                precision: 28,
                scale: 8,
                nullable: false,
                defaultValue: 1m);

            migrationBuilder.AddColumn<decimal>(
                name: "RecognitionConfidence",
                table: "RII_INCOMING_INVOICE_LINE",
                type: "decimal(9,6)",
                precision: 9,
                scale: 6,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "SupplierStockMappingId",
                table: "RII_INCOMING_INVOICE_LINE",
                type: "bigint",
                nullable: true);

            migrationBuilder.AlterColumn<long>(
                name: "ELogoConnectionId",
                table: "RII_INCOMING_INVOICE_HEADER",
                type: "bigint",
                nullable: true,
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AddColumn<int>(
                name: "CaptureSource",
                table: "RII_INCOMING_INVOICE_HEADER",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<decimal>(
                name: "RecognitionConfidence",
                table: "RII_INCOMING_INVOICE_HEADER",
                type: "decimal(9,6)",
                precision: 9,
                scale: 6,
                nullable: true);

            migrationBuilder.InsertData(
                table: "RII_PERMISSION_DEFINITIONS",
                columns: new[] { "Id", "AvailableOnMobile", "AvailableOnWeb", "BranchCode", "Code", "CreatedBy", "CreatedDate", "DeletedBy", "DeletedDate", "Description", "IsActive", "Name", "UpdatedBy", "UpdatedDate" },
                values: new object[] { 2306L, false, true, "0", "WMS.INCOMING_INVOICE.OCR_IMPORT", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, true, "Fatura belgesini OCR ile ön incelemeye al", null, null });

            migrationBuilder.InsertData(
                table: "RII_PERMISSION_GROUP_PERMISSIONS",
                columns: new[] { "Id", "BranchCode", "CreatedBy", "CreatedDate", "DeletedBy", "DeletedDate", "PermissionDefinitionId", "PermissionGroupId", "UpdatedBy", "UpdatedDate" },
                values: new object[] { 2306L, "0", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, 2306L, 1001L, null, null });

            migrationBuilder.CreateIndex(
                name: "IX_RII_INCOMING_INVOICE_LINE_SupplierStockMappingId",
                table: "RII_INCOMING_INVOICE_LINE",
                column: "SupplierStockMappingId");

            migrationBuilder.AddForeignKey(
                name: "FK_RII_INCOMING_INVOICE_LINE_RII_SUPPLIER_STOCK_MAPPING_SupplierStockMappingId",
                table: "RII_INCOMING_INVOICE_LINE",
                column: "SupplierStockMappingId",
                principalTable: "RII_SUPPLIER_STOCK_MAPPING",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RII_INCOMING_INVOICE_LINE_RII_SUPPLIER_STOCK_MAPPING_SupplierStockMappingId",
                table: "RII_INCOMING_INVOICE_LINE");

            migrationBuilder.DropIndex(
                name: "IX_RII_INCOMING_INVOICE_LINE_SupplierStockMappingId",
                table: "RII_INCOMING_INVOICE_LINE");

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_GROUP_PERMISSIONS",
                keyColumn: "Id",
                keyValue: 2306L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_DEFINITIONS",
                keyColumn: "Id",
                keyValue: 2306L);

            migrationBuilder.DropColumn(
                name: "ConversionFactor",
                table: "RII_INCOMING_INVOICE_LINE");

            migrationBuilder.DropColumn(
                name: "RecognitionConfidence",
                table: "RII_INCOMING_INVOICE_LINE");

            migrationBuilder.DropColumn(
                name: "SupplierStockMappingId",
                table: "RII_INCOMING_INVOICE_LINE");

            migrationBuilder.DropColumn(
                name: "CaptureSource",
                table: "RII_INCOMING_INVOICE_HEADER");

            migrationBuilder.DropColumn(
                name: "RecognitionConfidence",
                table: "RII_INCOMING_INVOICE_HEADER");

            migrationBuilder.AlterColumn<long>(
                name: "ELogoConnectionId",
                table: "RII_INCOMING_INVOICE_HEADER",
                type: "bigint",
                nullable: false,
                defaultValue: 0L,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true);
        }
    }
}
