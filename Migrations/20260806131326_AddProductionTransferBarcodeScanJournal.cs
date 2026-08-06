using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace verii_wms_api_v2.Migrations
{
    /// <inheritdoc />
    public partial class AddProductionTransferBarcodeScanJournal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RII_PT_BARCODE_SCAN",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProductionTransferHeaderLinkId = table.Column<long>(type: "bigint", nullable: false),
                    ProductionTransferLineLinkId = table.Column<long>(type: "bigint", nullable: false),
                    IdempotencyKey = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BarcodeValue = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    NormalizedBarcode = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    BarcodeSource = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    StockId = table.Column<long>(type: "bigint", nullable: false),
                    YapCodeId = table.Column<long>(type: "bigint", nullable: true),
                    UnitCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    LotNo = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    SerialNo = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Quantity = table.Column<decimal>(type: "decimal(20,6)", precision: 20, scale: 6, nullable: false),
                    SourceLocationId = table.Column<long>(type: "bigint", nullable: false),
                    TargetLocationId = table.Column<long>(type: "bigint", nullable: false),
                    ScannedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    BranchCode = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false, defaultValue: "0"),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    CreatedBy = table.Column<long>(type: "bigint", nullable: true),
                    UpdatedBy = table.Column<long>(type: "bigint", nullable: true),
                    DeletedBy = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RII_PT_BARCODE_SCAN", x => x.Id);
                    table.CheckConstraint("CK_RII_PT_BARCODE_SCAN_QTY", "[Quantity] > 0");
                    table.ForeignKey(
                        name: "FK_RII_PT_BARCODE_SCAN_RII_PT_HEADER_LINK_ProductionTransferHeaderLinkId",
                        column: x => x.ProductionTransferHeaderLinkId,
                        principalTable: "RII_PT_HEADER_LINK",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RII_PT_BARCODE_SCAN_RII_PT_LINE_LINK_ProductionTransferLineLinkId",
                        column: x => x.ProductionTransferLineLinkId,
                        principalTable: "RII_PT_LINE_LINK",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RII_PT_BARCODE_SCAN_IdempotencyKey",
                table: "RII_PT_BARCODE_SCAN",
                column: "IdempotencyKey",
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_RII_PT_BARCODE_SCAN_IsDeleted",
                table: "RII_PT_BARCODE_SCAN",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RII_PT_BARCODE_SCAN_ProductionTransferHeaderLinkId_NormalizedBarcode",
                table: "RII_PT_BARCODE_SCAN",
                columns: new[] { "ProductionTransferHeaderLinkId", "NormalizedBarcode" });

            migrationBuilder.CreateIndex(
                name: "IX_RII_PT_BARCODE_SCAN_ProductionTransferHeaderLinkId_ProductionTransferLineLinkId_ScannedAtUtc",
                table: "RII_PT_BARCODE_SCAN",
                columns: new[] { "ProductionTransferHeaderLinkId", "ProductionTransferLineLinkId", "ScannedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_RII_PT_BARCODE_SCAN_ProductionTransferLineLinkId",
                table: "RII_PT_BARCODE_SCAN",
                column: "ProductionTransferLineLinkId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RII_PT_BARCODE_SCAN");

        }
    }
}
