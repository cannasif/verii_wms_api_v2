using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace verii_wms_api_v2.Migrations
{
    /// <inheritdoc />
    public partial class SeedDefaultBarcodeRule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "RII_BARCODE_RULE",
                columns: new[] { "Id", "BranchCode", "CreatedBy", "CreatedDate", "DeletedBy", "DeletedDate", "DisplayName", "IsActive", "NextSequence", "Prefix", "RuleCode", "Separator", "Target", "UpdatedBy", "UpdatedDate" },
                values: new object[] { 1L, "0", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, "Stok İzlenebilirlik Barkodu", true, 1L, "WMS", "STOCK_TRACE_UNIQUE", "/", "Serial", null, null });

            migrationBuilder.InsertData(
                table: "RII_BARCODE_RULE_SEGMENT",
                columns: new[] { "Id", "BarcodeRuleId", "BranchCode", "CreatedBy", "CreatedDate", "DateFormat", "DeletedBy", "DeletedDate", "IsRequired", "LiteralValue", "Order", "SegmentType", "SequenceLength", "SourceField", "Transform", "UpdatedBy", "UpdatedDate" },
                values: new object[,]
                {
                    { 1L, 1L, "0", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), "yyyyMMdd", null, null, true, null, 1, "Field", 8, "StockCode", "Upper", null, null },
                    { 2L, 1L, "0", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), "yyyyMMdd", null, null, false, null, 2, "Field", 8, "SerialNo", "Upper", null, null },
                    { 3L, 1L, "0", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), "yyyyMMdd", null, null, false, null, 3, "Field", 8, "YapCode", "Upper", null, null },
                    { 4L, 1L, "0", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), "yyyyMMdd", null, null, false, null, 4, "Field", 8, "LotNo", "Upper", null, null },
                    { 5L, 1L, "0", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), "yyyyMMdd", null, null, true, null, 5, "Sequence", 8, null, "None", null, null }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "RII_BARCODE_RULE_SEGMENT",
                keyColumn: "Id",
                keyValue: 1L);

            migrationBuilder.DeleteData(
                table: "RII_BARCODE_RULE_SEGMENT",
                keyColumn: "Id",
                keyValue: 2L);

            migrationBuilder.DeleteData(
                table: "RII_BARCODE_RULE_SEGMENT",
                keyColumn: "Id",
                keyValue: 3L);

            migrationBuilder.DeleteData(
                table: "RII_BARCODE_RULE_SEGMENT",
                keyColumn: "Id",
                keyValue: 4L);

            migrationBuilder.DeleteData(
                table: "RII_BARCODE_RULE_SEGMENT",
                keyColumn: "Id",
                keyValue: 5L);

            migrationBuilder.DeleteData(
                table: "RII_BARCODE_RULE",
                keyColumn: "Id",
                keyValue: 1L);
        }
    }
}
