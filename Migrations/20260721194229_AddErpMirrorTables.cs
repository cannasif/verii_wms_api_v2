using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace verii_wms_api_v2.Migrations
{
    /// <inheritdoc />
    public partial class AddErpMirrorTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RII_CUSTOMER",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BusinessUnitCode = table.Column<short>(type: "smallint", nullable: false),
                    CustomerCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    CustomerName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    LastSyncDate = table.Column<DateTime>(type: "datetime2", nullable: true),
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
                    table.PrimaryKey("PK_RII_CUSTOMER", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RII_STOCK",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BusinessUnitCode = table.Column<short>(type: "smallint", nullable: false),
                    ErpStockCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    StockName = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    ManufacturerCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    GroupCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Code1 = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Code2 = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Code3 = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Code4 = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Code5 = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    LastSyncDate = table.Column<DateTime>(type: "datetime2", nullable: true),
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
                    table.PrimaryKey("PK_RII_STOCK", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RII_WAREHOUSE",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    WarehouseCode = table.Column<int>(type: "int", nullable: false),
                    WarehouseName = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    LastSyncDate = table.Column<DateTime>(type: "datetime2", nullable: true),
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
                    table.PrimaryKey("PK_RII_WAREHOUSE", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RII_YAP_CODE",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ConfigurationCode = table.Column<string>(type: "nvarchar(15)", maxLength: 15, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    ConfigurableStockCode = table.Column<string>(type: "nvarchar(35)", maxLength: 35, nullable: true),
                    StockId = table.Column<long>(type: "bigint", nullable: true),
                    LastSyncDate = table.Column<DateTime>(type: "datetime2", nullable: true),
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
                    table.PrimaryKey("PK_RII_YAP_CODE", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RII_YAP_CODE_RII_STOCK_StockId",
                        column: x => x.StockId,
                        principalTable: "RII_STOCK",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Customer_BranchCode_CustomerCode",
                table: "RII_CUSTOMER",
                columns: new[] { "BranchCode", "CustomerCode" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_Customer_CustomerName",
                table: "RII_CUSTOMER",
                column: "CustomerName");

            migrationBuilder.CreateIndex(
                name: "IX_RII_CUSTOMER_IsDeleted",
                table: "RII_CUSTOMER",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RII_STOCK_IsDeleted",
                table: "RII_STOCK",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_Stock_BranchCode_ErpStockCode",
                table: "RII_STOCK",
                columns: new[] { "BranchCode", "ErpStockCode" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_Stock_StockName",
                table: "RII_STOCK",
                column: "StockName");

            migrationBuilder.CreateIndex(
                name: "IX_RII_WAREHOUSE_IsDeleted",
                table: "RII_WAREHOUSE",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_Warehouse_BranchCode_WarehouseCode",
                table: "RII_WAREHOUSE",
                columns: new[] { "BranchCode", "WarehouseCode" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_Warehouse_WarehouseName",
                table: "RII_WAREHOUSE",
                column: "WarehouseName");

            migrationBuilder.CreateIndex(
                name: "IX_RII_YAP_CODE_IsDeleted",
                table: "RII_YAP_CODE",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_YapCode_BranchCode_ConfigurationCode",
                table: "RII_YAP_CODE",
                columns: new[] { "BranchCode", "ConfigurationCode" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_YapCode_Description",
                table: "RII_YAP_CODE",
                column: "Description");

            migrationBuilder.CreateIndex(
                name: "IX_YapCode_StockId",
                table: "RII_YAP_CODE",
                column: "StockId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RII_CUSTOMER");

            migrationBuilder.DropTable(
                name: "RII_WAREHOUSE");

            migrationBuilder.DropTable(
                name: "RII_YAP_CODE");

            migrationBuilder.DropTable(
                name: "RII_STOCK");
        }
    }
}
