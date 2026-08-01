using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace verii_wms_api_v2.Migrations
{
    /// <inheritdoc />
    public partial class AddStockBalanceProjectionsAndYapDimension : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RII_STOCK_MOVEMENT_BALANCE_STREAM",
                table: "RII_STOCK_MOVEMENT");

            migrationBuilder.AddColumn<long>(
                name: "YapCodeId",
                table: "RII_STOCK_MOVEMENT",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "RII_LOCATION_STOCK_BALANCE",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DimensionKey = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    WarehouseId = table.Column<long>(type: "bigint", nullable: false),
                    LocationId = table.Column<long>(type: "bigint", nullable: false),
                    StockId = table.Column<long>(type: "bigint", nullable: false),
                    YapCodeId = table.Column<long>(type: "bigint", nullable: true),
                    UnitCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    LotNo = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    SerialNo = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    StockStatus = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    ReservedQuantity = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    AvailableQuantity = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    LastMovementEntryId = table.Column<long>(type: "bigint", nullable: false),
                    LastTransactionDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastReconciledAt = table.Column<DateTime>(type: "datetime2", nullable: true),
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
                    table.PrimaryKey("PK_RII_LOCATION_STOCK_BALANCE", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RII_LOCATION_STOCK_BALANCE_RII_LOCATION_LocationId",
                        column: x => x.LocationId,
                        principalTable: "RII_LOCATION",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RII_LOCATION_STOCK_BALANCE_RII_STOCK_StockId",
                        column: x => x.StockId,
                        principalTable: "RII_STOCK",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RII_LOCATION_STOCK_BALANCE_RII_WAREHOUSE_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "RII_WAREHOUSE",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RII_LOCATION_STOCK_BALANCE_RII_YAP_CODE_YapCodeId",
                        column: x => x.YapCodeId,
                        principalTable: "RII_YAP_CODE",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RII_STOCK_BALANCE_PROJECTION_STATE",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProjectionName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    LastMovementEntryId = table.Column<long>(type: "bigint", nullable: false),
                    LastProjectedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastReconciledAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastMismatchCount = table.Column<int>(type: "int", nullable: false),
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
                    table.PrimaryKey("PK_RII_STOCK_BALANCE_PROJECTION_STATE", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RII_WAREHOUSE_STOCK_BALANCE",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DimensionKey = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    WarehouseId = table.Column<long>(type: "bigint", nullable: false),
                    StockId = table.Column<long>(type: "bigint", nullable: false),
                    YapCodeId = table.Column<long>(type: "bigint", nullable: true),
                    UnitCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    StockStatus = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    ReservedQuantity = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    AvailableQuantity = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    DistinctLocationCount = table.Column<int>(type: "int", nullable: false),
                    DistinctLotCount = table.Column<int>(type: "int", nullable: false),
                    DistinctSerialCount = table.Column<int>(type: "int", nullable: false),
                    LastMovementEntryId = table.Column<long>(type: "bigint", nullable: false),
                    LastTransactionDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastReconciledAt = table.Column<DateTime>(type: "datetime2", nullable: true),
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
                    table.PrimaryKey("PK_RII_WAREHOUSE_STOCK_BALANCE", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RII_WAREHOUSE_STOCK_BALANCE_RII_STOCK_StockId",
                        column: x => x.StockId,
                        principalTable: "RII_STOCK",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RII_WAREHOUSE_STOCK_BALANCE_RII_WAREHOUSE_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "RII_WAREHOUSE",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RII_WAREHOUSE_STOCK_BALANCE_RII_YAP_CODE_YapCodeId",
                        column: x => x.YapCodeId,
                        principalTable: "RII_YAP_CODE",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "RII_PERMISSION_DEFINITIONS",
                columns: new[] { "Id", "AvailableOnMobile", "AvailableOnWeb", "BranchCode", "Code", "CreatedBy", "CreatedDate", "DeletedBy", "DeletedDate", "Description", "IsActive", "Name", "UpdatedBy", "UpdatedDate" },
                values: new object[,]
                {
                    { 1016L, false, true, "0", "WMS.STOCK_BALANCES.VIEW", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, true, "Stok Bakiyelerini Görüntüle", null, null },
                    { 1017L, false, true, "0", "WMS.STOCK_BALANCES.RECONCILE", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, true, "Stok Bakiyelerini Uzlaştır ve Onar", null, null }
                });

            migrationBuilder.InsertData(
                table: "RII_PERMISSION_GROUP_PERMISSIONS",
                columns: new[] { "Id", "BranchCode", "CreatedBy", "CreatedDate", "DeletedBy", "DeletedDate", "PermissionDefinitionId", "PermissionGroupId", "UpdatedBy", "UpdatedDate" },
                values: new object[,]
                {
                    { 1016L, "0", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, 1016L, 1001L, null, null },
                    { 1017L, "0", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, 1017L, 1001L, null, null }
                });

            migrationBuilder.Sql(SqlServerMigrationSql.Execute(
                "CREATE INDEX [IX_RII_STOCK_MOVEMENT_BALANCE_STREAM] ON [RII_STOCK_MOVEMENT] ([StockId], [YapCodeId], [WarehouseId], [LocationId], [UnitCode], [OccurredAt]);"));

            migrationBuilder.Sql(SqlServerMigrationSql.Execute(
                "CREATE INDEX [IX_RII_STOCK_MOVEMENT_YapCodeId] ON [RII_STOCK_MOVEMENT] ([YapCodeId]);"));

            migrationBuilder.CreateIndex(
                name: "IX_RII_LOCATION_STOCK_BALANCE_DIMENSIONS",
                table: "RII_LOCATION_STOCK_BALANCE",
                columns: new[] { "WarehouseId", "LocationId", "StockId", "YapCodeId", "UnitCode", "StockStatus" });

            migrationBuilder.CreateIndex(
                name: "IX_RII_LOCATION_STOCK_BALANCE_IsDeleted",
                table: "RII_LOCATION_STOCK_BALANCE",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RII_LOCATION_STOCK_BALANCE_LocationId",
                table: "RII_LOCATION_STOCK_BALANCE",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_RII_LOCATION_STOCK_BALANCE_PICKING",
                table: "RII_LOCATION_STOCK_BALANCE",
                columns: new[] { "StockId", "WarehouseId", "AvailableQuantity" });

            migrationBuilder.CreateIndex(
                name: "IX_RII_LOCATION_STOCK_BALANCE_YapCodeId",
                table: "RII_LOCATION_STOCK_BALANCE",
                column: "YapCodeId");

            migrationBuilder.CreateIndex(
                name: "UX_RII_LOCATION_STOCK_BALANCE_DIMENSION_KEY",
                table: "RII_LOCATION_STOCK_BALANCE",
                column: "DimensionKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RII_STOCK_BALANCE_PROJECTION_STATE_IsDeleted",
                table: "RII_STOCK_BALANCE_PROJECTION_STATE",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "UX_RII_STOCK_BALANCE_PROJECTION_STATE_NAME",
                table: "RII_STOCK_BALANCE_PROJECTION_STATE",
                column: "ProjectionName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RII_WAREHOUSE_STOCK_BALANCE_DIMENSIONS",
                table: "RII_WAREHOUSE_STOCK_BALANCE",
                columns: new[] { "WarehouseId", "StockId", "YapCodeId", "UnitCode", "StockStatus" });

            migrationBuilder.CreateIndex(
                name: "IX_RII_WAREHOUSE_STOCK_BALANCE_IsDeleted",
                table: "RII_WAREHOUSE_STOCK_BALANCE",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RII_WAREHOUSE_STOCK_BALANCE_STOCK",
                table: "RII_WAREHOUSE_STOCK_BALANCE",
                columns: new[] { "StockId", "AvailableQuantity" });

            migrationBuilder.CreateIndex(
                name: "IX_RII_WAREHOUSE_STOCK_BALANCE_YapCodeId",
                table: "RII_WAREHOUSE_STOCK_BALANCE",
                column: "YapCodeId");

            migrationBuilder.CreateIndex(
                name: "UX_RII_WAREHOUSE_STOCK_BALANCE_DIMENSION_KEY",
                table: "RII_WAREHOUSE_STOCK_BALANCE",
                column: "DimensionKey",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_RII_STOCK_MOVEMENT_RII_YAP_CODE_YapCodeId",
                table: "RII_STOCK_MOVEMENT",
                column: "YapCodeId",
                principalTable: "RII_YAP_CODE",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RII_STOCK_MOVEMENT_RII_YAP_CODE_YapCodeId",
                table: "RII_STOCK_MOVEMENT");

            migrationBuilder.DropTable(
                name: "RII_LOCATION_STOCK_BALANCE");

            migrationBuilder.DropTable(
                name: "RII_STOCK_BALANCE_PROJECTION_STATE");

            migrationBuilder.DropTable(
                name: "RII_WAREHOUSE_STOCK_BALANCE");

            migrationBuilder.DropIndex(
                name: "IX_RII_STOCK_MOVEMENT_BALANCE_STREAM",
                table: "RII_STOCK_MOVEMENT");

            migrationBuilder.DropIndex(
                name: "IX_RII_STOCK_MOVEMENT_YapCodeId",
                table: "RII_STOCK_MOVEMENT");

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_GROUP_PERMISSIONS",
                keyColumn: "Id",
                keyValue: 1016L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_GROUP_PERMISSIONS",
                keyColumn: "Id",
                keyValue: 1017L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_DEFINITIONS",
                keyColumn: "Id",
                keyValue: 1016L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_DEFINITIONS",
                keyColumn: "Id",
                keyValue: 1017L);

            migrationBuilder.DropColumn(
                name: "YapCodeId",
                table: "RII_STOCK_MOVEMENT");

            migrationBuilder.CreateIndex(
                name: "IX_RII_STOCK_MOVEMENT_BALANCE_STREAM",
                table: "RII_STOCK_MOVEMENT",
                columns: new[] { "StockId", "WarehouseId", "LocationId", "UnitCode", "OccurredAt" });
        }
    }
}
