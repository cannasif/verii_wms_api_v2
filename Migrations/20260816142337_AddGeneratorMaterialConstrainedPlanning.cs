using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace verii_wms_api_v2.Migrations
{
    /// <inheritdoc />
    public partial class AddGeneratorMaterialConstrainedPlanning : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_RII_GP_POLICY_REFRESH",
                table: "RII_GP_POLICY");

            migrationBuilder.AddColumn<long>(
                name: "ProductId",
                table: "RII_GP_PROJECT",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "InboundQualityBufferDays",
                table: "RII_GP_POLICY",
                type: "int",
                nullable: false,
                defaultValue: 2);

            migrationBuilder.AddColumn<bool>(
                name: "IsScheduleLocked",
                table: "RII_GP_OPERATION",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "ManualScheduleReason",
                table: "RII_GP_OPERATION",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ManualScheduledAtUtc",
                table: "RII_GP_OPERATION",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "ManualScheduledBy",
                table: "RII_GP_OPERATION",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "RII_GP_PRODUCT",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    GeneratorType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ProducedStockId = table.Column<long>(type: "bigint", nullable: true),
                    ProducedStockCodeSnapshot = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
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
                    table.PrimaryKey("PK_RII_GP_PRODUCT", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RII_GP_PRODUCT_RII_STOCK_ProducedStockId",
                        column: x => x.ProducedStockId,
                        principalTable: "RII_STOCK",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RII_GP_OPERATION_MATERIAL",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProductId = table.Column<long>(type: "bigint", nullable: false),
                    RouteOperationId = table.Column<long>(type: "bigint", nullable: false),
                    StockId = table.Column<long>(type: "bigint", nullable: false),
                    YapCodeId = table.Column<long>(type: "bigint", nullable: true),
                    WarehouseId = table.Column<long>(type: "bigint", nullable: false),
                    StockCodeSnapshot = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    StockNameSnapshot = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    UnitCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    QuantityPerUnit = table.Column<decimal>(type: "decimal(20,6)", precision: 20, scale: 6, nullable: false),
                    WasteRate = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: false),
                    NeedOffsetMinutes = table.Column<int>(type: "int", nullable: false),
                    IsMandatory = table.Column<bool>(type: "bit", nullable: false),
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
                    table.PrimaryKey("PK_RII_GP_OPERATION_MATERIAL", x => x.Id);
                    table.CheckConstraint("CK_RII_GP_OPERATION_MATERIAL_VALUES", "[QuantityPerUnit] > 0 AND [WasteRate] BETWEEN 0 AND 100 AND [NeedOffsetMinutes] BETWEEN -10080 AND 10080");
                    table.ForeignKey(
                        name: "FK_RII_GP_OPERATION_MATERIAL_RII_GP_PRODUCT_ProductId",
                        column: x => x.ProductId,
                        principalTable: "RII_GP_PRODUCT",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RII_GP_OPERATION_MATERIAL_RII_GP_ROUTE_OPERATION_RouteOperationId",
                        column: x => x.RouteOperationId,
                        principalTable: "RII_GP_ROUTE_OPERATION",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RII_GP_OPERATION_MATERIAL_RII_STOCK_StockId",
                        column: x => x.StockId,
                        principalTable: "RII_STOCK",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RII_GP_OPERATION_MATERIAL_RII_WAREHOUSE_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "RII_WAREHOUSE",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RII_GP_OPERATION_MATERIAL_RII_YAP_CODE_YapCodeId",
                        column: x => x.YapCodeId,
                        principalTable: "RII_YAP_CODE",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RII_GP_PRODUCT_ROUTE",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProductId = table.Column<long>(type: "bigint", nullable: false),
                    PartType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    RouteId = table.Column<long>(type: "bigint", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
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
                    table.PrimaryKey("PK_RII_GP_PRODUCT_ROUTE", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RII_GP_PRODUCT_ROUTE_RII_GP_PRODUCT_ProductId",
                        column: x => x.ProductId,
                        principalTable: "RII_GP_PRODUCT",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RII_GP_PRODUCT_ROUTE_RII_GP_ROUTE_RouteId",
                        column: x => x.RouteId,
                        principalTable: "RII_GP_ROUTE",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RII_GP_STATION_CAPABILITY",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProductId = table.Column<long>(type: "bigint", nullable: false),
                    RouteOperationId = table.Column<long>(type: "bigint", nullable: false),
                    StationId = table.Column<long>(type: "bigint", nullable: false),
                    IsPrimary = table.Column<bool>(type: "bit", nullable: false),
                    EfficiencyPercent = table.Column<int>(type: "int", nullable: false),
                    SetupMinutes = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
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
                    table.PrimaryKey("PK_RII_GP_STATION_CAPABILITY", x => x.Id);
                    table.CheckConstraint("CK_RII_GP_STATION_CAPABILITY_VALUES", "[EfficiencyPercent] BETWEEN 1 AND 300 AND [SetupMinutes] BETWEEN 0 AND 10080");
                    table.ForeignKey(
                        name: "FK_RII_GP_STATION_CAPABILITY_RII_GP_PRODUCT_ProductId",
                        column: x => x.ProductId,
                        principalTable: "RII_GP_PRODUCT",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RII_GP_STATION_CAPABILITY_RII_GP_ROUTE_OPERATION_RouteOperationId",
                        column: x => x.RouteOperationId,
                        principalTable: "RII_GP_ROUTE_OPERATION",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RII_GP_STATION_CAPABILITY_RII_GP_STATION_StationId",
                        column: x => x.StationId,
                        principalTable: "RII_GP_STATION",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RII_GP_PROJECT_ProductId",
                table: "RII_GP_PROJECT",
                column: "ProductId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_RII_GP_POLICY_REFRESH",
                table: "RII_GP_POLICY",
                sql: "[AndonRefreshSeconds] BETWEEN 5 AND 3600 AND [InboundQualityBufferDays] BETWEEN 0 AND 365");

            migrationBuilder.CreateIndex(
                name: "IX_RII_GP_OPERATION_MATERIAL_BranchCode_StockId_WarehouseId",
                table: "RII_GP_OPERATION_MATERIAL",
                columns: new[] { "BranchCode", "StockId", "WarehouseId" });

            migrationBuilder.CreateIndex(
                name: "IX_RII_GP_OPERATION_MATERIAL_IsDeleted",
                table: "RII_GP_OPERATION_MATERIAL",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RII_GP_OPERATION_MATERIAL_ProductId_RouteOperationId_StockId_YapCodeId_WarehouseId_UnitCode",
                table: "RII_GP_OPERATION_MATERIAL",
                columns: new[] { "ProductId", "RouteOperationId", "StockId", "YapCodeId", "WarehouseId", "UnitCode" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_RII_GP_OPERATION_MATERIAL_RouteOperationId",
                table: "RII_GP_OPERATION_MATERIAL",
                column: "RouteOperationId");

            migrationBuilder.CreateIndex(
                name: "IX_RII_GP_OPERATION_MATERIAL_StockId",
                table: "RII_GP_OPERATION_MATERIAL",
                column: "StockId");

            migrationBuilder.CreateIndex(
                name: "IX_RII_GP_OPERATION_MATERIAL_WarehouseId",
                table: "RII_GP_OPERATION_MATERIAL",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_RII_GP_OPERATION_MATERIAL_YapCodeId",
                table: "RII_GP_OPERATION_MATERIAL",
                column: "YapCodeId");

            migrationBuilder.CreateIndex(
                name: "IX_RII_GP_PRODUCT_BranchCode_Code",
                table: "RII_GP_PRODUCT",
                columns: new[] { "BranchCode", "Code" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_RII_GP_PRODUCT_BranchCode_IsActive_GeneratorType",
                table: "RII_GP_PRODUCT",
                columns: new[] { "BranchCode", "IsActive", "GeneratorType" });

            migrationBuilder.CreateIndex(
                name: "IX_RII_GP_PRODUCT_IsDeleted",
                table: "RII_GP_PRODUCT",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RII_GP_PRODUCT_ProducedStockId",
                table: "RII_GP_PRODUCT",
                column: "ProducedStockId");

            migrationBuilder.CreateIndex(
                name: "IX_RII_GP_PRODUCT_ROUTE_IsDeleted",
                table: "RII_GP_PRODUCT_ROUTE",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RII_GP_PRODUCT_ROUTE_ProductId_PartType",
                table: "RII_GP_PRODUCT_ROUTE",
                columns: new[] { "ProductId", "PartType" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_RII_GP_PRODUCT_ROUTE_RouteId",
                table: "RII_GP_PRODUCT_ROUTE",
                column: "RouteId");

            migrationBuilder.CreateIndex(
                name: "IX_RII_GP_STATION_CAPABILITY_BranchCode_StationId_IsActive",
                table: "RII_GP_STATION_CAPABILITY",
                columns: new[] { "BranchCode", "StationId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_RII_GP_STATION_CAPABILITY_IsDeleted",
                table: "RII_GP_STATION_CAPABILITY",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RII_GP_STATION_CAPABILITY_ProductId_RouteOperationId_StationId",
                table: "RII_GP_STATION_CAPABILITY",
                columns: new[] { "ProductId", "RouteOperationId", "StationId" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_RII_GP_STATION_CAPABILITY_RouteOperationId",
                table: "RII_GP_STATION_CAPABILITY",
                column: "RouteOperationId");

            migrationBuilder.CreateIndex(
                name: "IX_RII_GP_STATION_CAPABILITY_StationId",
                table: "RII_GP_STATION_CAPABILITY",
                column: "StationId");

            migrationBuilder.AddForeignKey(
                name: "FK_RII_GP_PROJECT_RII_GP_PRODUCT_ProductId",
                table: "RII_GP_PROJECT",
                column: "ProductId",
                principalTable: "RII_GP_PRODUCT",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RII_GP_PROJECT_RII_GP_PRODUCT_ProductId",
                table: "RII_GP_PROJECT");

            migrationBuilder.DropTable(
                name: "RII_GP_OPERATION_MATERIAL");

            migrationBuilder.DropTable(
                name: "RII_GP_PRODUCT_ROUTE");

            migrationBuilder.DropTable(
                name: "RII_GP_STATION_CAPABILITY");

            migrationBuilder.DropTable(
                name: "RII_GP_PRODUCT");

            migrationBuilder.DropIndex(
                name: "IX_RII_GP_PROJECT_ProductId",
                table: "RII_GP_PROJECT");

            migrationBuilder.DropCheckConstraint(
                name: "CK_RII_GP_POLICY_REFRESH",
                table: "RII_GP_POLICY");

            migrationBuilder.DropColumn(
                name: "ProductId",
                table: "RII_GP_PROJECT");

            migrationBuilder.DropColumn(
                name: "InboundQualityBufferDays",
                table: "RII_GP_POLICY");

            migrationBuilder.DropColumn(
                name: "IsScheduleLocked",
                table: "RII_GP_OPERATION");

            migrationBuilder.DropColumn(
                name: "ManualScheduleReason",
                table: "RII_GP_OPERATION");

            migrationBuilder.DropColumn(
                name: "ManualScheduledAtUtc",
                table: "RII_GP_OPERATION");

            migrationBuilder.DropColumn(
                name: "ManualScheduledBy",
                table: "RII_GP_OPERATION");

            migrationBuilder.AddCheckConstraint(
                name: "CK_RII_GP_POLICY_REFRESH",
                table: "RII_GP_POLICY",
                sql: "[AndonRefreshSeconds] BETWEEN 5 AND 3600");
        }
    }
}
