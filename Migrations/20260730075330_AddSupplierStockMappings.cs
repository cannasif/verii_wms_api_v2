using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace verii_wms_api_v2.Migrations
{
    /// <inheritdoc />
    public partial class AddSupplierStockMappings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RII_SUPPLIER_STOCK_MAPPING",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SupplierId = table.Column<long>(type: "bigint", nullable: false),
                    SupplierStockCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    NormalizedSupplierStockCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    SupplierStockName = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    SupplierUnitCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    StockId = table.Column<long>(type: "bigint", nullable: false),
                    ConversionFactor = table.Column<decimal>(type: "decimal(28,8)", precision: 28, scale: 8, nullable: false, defaultValue: 1m),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
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
                    table.PrimaryKey("PK_RII_SUPPLIER_STOCK_MAPPING", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RII_SUPPLIER_STOCK_MAPPING_RII_CUSTOMER_SupplierId",
                        column: x => x.SupplierId,
                        principalTable: "RII_CUSTOMER",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RII_SUPPLIER_STOCK_MAPPING_RII_STOCK_StockId",
                        column: x => x.StockId,
                        principalTable: "RII_STOCK",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "RII_PERMISSION_DEFINITIONS",
                columns: new[] { "Id", "AvailableOnMobile", "AvailableOnWeb", "BranchCode", "Code", "CreatedBy", "CreatedDate", "DeletedBy", "DeletedDate", "Description", "IsActive", "Name", "UpdatedBy", "UpdatedDate" },
                values: new object[,]
                {
                    { 2304L, false, true, "0", "WMS.GOODS_RECEIPT.SUPPLIER_STOCK_MAPPING.VIEW", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, true, "Tedarikçi stok eşlemelerini görüntüle", null, null },
                    { 2305L, false, true, "0", "WMS.GOODS_RECEIPT.SUPPLIER_STOCK_MAPPING.MANAGE", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, true, "Tedarikçi stok eşlemelerini yönet", null, null }
                });

            migrationBuilder.InsertData(
                table: "RII_PERMISSION_GROUP_PERMISSIONS",
                columns: new[] { "Id", "BranchCode", "CreatedBy", "CreatedDate", "DeletedBy", "DeletedDate", "PermissionDefinitionId", "PermissionGroupId", "UpdatedBy", "UpdatedDate" },
                values: new object[,]
                {
                    { 2304L, "0", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, 2304L, 1001L, null, null },
                    { 2305L, "0", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, 2305L, 1001L, null, null }
                });

            migrationBuilder.CreateIndex(
                name: "IX_RII_SUPPLIER_STOCK_MAPPING_IsDeleted",
                table: "RII_SUPPLIER_STOCK_MAPPING",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RII_SUPPLIER_STOCK_MAPPING_STOCK_ACTIVE",
                table: "RII_SUPPLIER_STOCK_MAPPING",
                columns: new[] { "BranchCode", "StockId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_RII_SUPPLIER_STOCK_MAPPING_StockId",
                table: "RII_SUPPLIER_STOCK_MAPPING",
                column: "StockId");

            migrationBuilder.CreateIndex(
                name: "IX_RII_SUPPLIER_STOCK_MAPPING_SUPPLIER_ACTIVE",
                table: "RII_SUPPLIER_STOCK_MAPPING",
                columns: new[] { "BranchCode", "SupplierId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_RII_SUPPLIER_STOCK_MAPPING_SupplierId",
                table: "RII_SUPPLIER_STOCK_MAPPING",
                column: "SupplierId");

            migrationBuilder.CreateIndex(
                name: "UX_RII_SUPPLIER_STOCK_MAPPING_IDENTITY",
                table: "RII_SUPPLIER_STOCK_MAPPING",
                columns: new[] { "BranchCode", "SupplierId", "NormalizedSupplierStockCode" },
                unique: true,
                filter: "[IsDeleted] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RII_SUPPLIER_STOCK_MAPPING");

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_GROUP_PERMISSIONS",
                keyColumn: "Id",
                keyValue: 2304L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_GROUP_PERMISSIONS",
                keyColumn: "Id",
                keyValue: 2305L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_DEFINITIONS",
                keyColumn: "Id",
                keyValue: 2304L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_DEFINITIONS",
                keyColumn: "Id",
                keyValue: 2305L);
        }
    }
}
