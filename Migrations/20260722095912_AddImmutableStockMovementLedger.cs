using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace verii_wms_api_v2.Migrations
{
    /// <inheritdoc />
    public partial class AddImmutableStockMovementLedger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RII_STOCK_MOVEMENT_OPERATION",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OperationCode = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    RequestHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    OperationType = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ReferenceType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ReferenceNo = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ReferenceId = table.Column<long>(type: "bigint", nullable: true),
                    OccurredAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ReversalOfOperationId = table.Column<long>(type: "bigint", nullable: true),
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
                    table.PrimaryKey("PK_RII_STOCK_MOVEMENT_OPERATION", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RII_STOCK_MOVEMENT_OPERATION_RII_STOCK_MOVEMENT_OPERATION_ReversalOfOperationId",
                        column: x => x.ReversalOfOperationId,
                        principalTable: "RII_STOCK_MOVEMENT_OPERATION",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RII_STOCK_MOVEMENT",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OperationId = table.Column<long>(type: "bigint", nullable: false),
                    LineNo = table.Column<int>(type: "int", nullable: false),
                    StockId = table.Column<long>(type: "bigint", nullable: false),
                    WarehouseId = table.Column<long>(type: "bigint", nullable: false),
                    LocationId = table.Column<long>(type: "bigint", nullable: false),
                    QuantityDelta = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    UnitCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    LotNo = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    SerialNo = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    StockStatus = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    OccurredAt = table.Column<DateTime>(type: "datetime2", nullable: false),
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
                    table.PrimaryKey("PK_RII_STOCK_MOVEMENT", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RII_STOCK_MOVEMENT_RII_LOCATION_LocationId",
                        column: x => x.LocationId,
                        principalTable: "RII_LOCATION",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RII_STOCK_MOVEMENT_RII_STOCK_MOVEMENT_OPERATION_OperationId",
                        column: x => x.OperationId,
                        principalTable: "RII_STOCK_MOVEMENT_OPERATION",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RII_STOCK_MOVEMENT_RII_STOCK_StockId",
                        column: x => x.StockId,
                        principalTable: "RII_STOCK",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RII_STOCK_MOVEMENT_RII_WAREHOUSE_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "RII_WAREHOUSE",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "RII_PERMISSION_DEFINITIONS",
                columns: new[] { "Id", "AvailableOnMobile", "AvailableOnWeb", "BranchCode", "Code", "CreatedBy", "CreatedDate", "DeletedBy", "DeletedDate", "Description", "IsActive", "Name", "UpdatedBy", "UpdatedDate" },
                values: new object[,]
                {
                    { 1013L, false, true, "0", "WMS.STOCK_MOVEMENTS.VIEW", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, true, "Stok Hareketlerini Görüntüle", null, null },
                    { 1014L, false, true, "0", "WMS.STOCK_MOVEMENTS.POST", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, true, "Stok Hareketi Kaydet", null, null },
                    { 1015L, false, true, "0", "WMS.STOCK_MOVEMENTS.REVERSE", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, true, "Stok Hareketini Ters Çevir", null, null }
                });

            migrationBuilder.InsertData(
                table: "RII_PERMISSION_GROUP_PERMISSIONS",
                columns: new[] { "Id", "BranchCode", "CreatedBy", "CreatedDate", "DeletedBy", "DeletedDate", "PermissionDefinitionId", "PermissionGroupId", "UpdatedBy", "UpdatedDate" },
                values: new object[,]
                {
                    { 1013L, "0", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, 1013L, 1001L, null, null },
                    { 1014L, "0", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, 1014L, 1001L, null, null },
                    { 1015L, "0", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, 1015L, 1001L, null, null }
                });

            migrationBuilder.CreateIndex(
                name: "IX_RII_STOCK_MOVEMENT_BALANCE_STREAM",
                table: "RII_STOCK_MOVEMENT",
                columns: new[] { "StockId", "WarehouseId", "LocationId", "UnitCode", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_RII_STOCK_MOVEMENT_IsDeleted",
                table: "RII_STOCK_MOVEMENT",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RII_STOCK_MOVEMENT_LOCATION_TIME",
                table: "RII_STOCK_MOVEMENT",
                columns: new[] { "WarehouseId", "LocationId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_RII_STOCK_MOVEMENT_LocationId",
                table: "RII_STOCK_MOVEMENT",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_RII_STOCK_MOVEMENT_TRACE",
                table: "RII_STOCK_MOVEMENT",
                columns: new[] { "StockId", "LotNo", "SerialNo" });

            migrationBuilder.CreateIndex(
                name: "UX_RII_STOCK_MOVEMENT_OPERATION_LINE",
                table: "RII_STOCK_MOVEMENT",
                columns: new[] { "OperationId", "LineNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RII_STOCK_MOVEMENT_OPERATION_IsDeleted",
                table: "RII_STOCK_MOVEMENT_OPERATION",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RII_STOCK_MOVEMENT_OPERATION_OCCURRED_TYPE",
                table: "RII_STOCK_MOVEMENT_OPERATION",
                columns: new[] { "OccurredAt", "OperationType" });

            migrationBuilder.CreateIndex(
                name: "UX_RII_STOCK_MOVEMENT_OPERATION_CODE",
                table: "RII_STOCK_MOVEMENT_OPERATION",
                column: "OperationCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_RII_STOCK_MOVEMENT_OPERATION_IDEMPOTENCY",
                table: "RII_STOCK_MOVEMENT_OPERATION",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_RII_STOCK_MOVEMENT_OPERATION_REVERSAL",
                table: "RII_STOCK_MOVEMENT_OPERATION",
                column: "ReversalOfOperationId",
                unique: true,
                filter: "[ReversalOfOperationId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RII_STOCK_MOVEMENT");

            migrationBuilder.DropTable(
                name: "RII_STOCK_MOVEMENT_OPERATION");

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_GROUP_PERMISSIONS",
                keyColumn: "Id",
                keyValue: 1013L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_GROUP_PERMISSIONS",
                keyColumn: "Id",
                keyValue: 1014L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_GROUP_PERMISSIONS",
                keyColumn: "Id",
                keyValue: 1015L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_DEFINITIONS",
                keyColumn: "Id",
                keyValue: 1013L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_DEFINITIONS",
                keyColumn: "Id",
                keyValue: 1014L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_DEFINITIONS",
                keyColumn: "Id",
                keyValue: 1015L);
        }
    }
}
