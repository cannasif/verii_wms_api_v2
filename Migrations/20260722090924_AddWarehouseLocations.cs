using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace verii_wms_api_v2.Migrations
{
    /// <inheritdoc />
    public partial class AddWarehouseLocations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RII_LOCATION",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    WarehouseId = table.Column<long>(type: "bigint", nullable: false),
                    ParentLocationId = table.Column<long>(type: "bigint", nullable: true),
                    Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    LocationType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false, defaultValue: "Cell"),
                    BarcodeEntryMode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false, defaultValue: "Auto"),
                    Barcode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ZoneCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    AisleNo = table.Column<int>(type: "int", nullable: true),
                    RackNo = table.Column<int>(type: "int", nullable: true),
                    LevelNo = table.Column<int>(type: "int", nullable: true),
                    BinNo = table.Column<int>(type: "int", nullable: true),
                    CapacityQuantity = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: true),
                    CapacityWeight = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: true),
                    CapacityVolume = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: true),
                    CapacityUnit = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    AllowMixedStock = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    AllowMixedLot = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    AllowMixedStatus = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    AllowCycleCount = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    IsPickable = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    IsPutaway = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    IsQuarantine = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
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
                    table.PrimaryKey("PK_RII_LOCATION", x => x.Id);
                    table.CheckConstraint("CK_RII_LOCATION_CAPACITY_QUANTITY", "[CapacityQuantity] IS NULL OR [CapacityQuantity] >= 0");
                    table.CheckConstraint("CK_RII_LOCATION_CAPACITY_VOLUME", "[CapacityVolume] IS NULL OR [CapacityVolume] >= 0");
                    table.CheckConstraint("CK_RII_LOCATION_CAPACITY_WEIGHT", "[CapacityWeight] IS NULL OR [CapacityWeight] >= 0");
                    table.ForeignKey(
                        name: "FK_RII_LOCATION_RII_LOCATION_ParentLocationId",
                        column: x => x.ParentLocationId,
                        principalTable: "RII_LOCATION",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RII_LOCATION_RII_WAREHOUSE_WarehouseId",
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
                    { 1009L, false, true, "0", "WMS.LOCATIONS.VIEW", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, true, "Raf Tanımlarını Görüntüle", null, null },
                    { 1010L, false, true, "0", "WMS.LOCATIONS.CREATE", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, true, "Raf Tanımı Oluştur", null, null },
                    { 1011L, false, true, "0", "WMS.LOCATIONS.UPDATE", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, true, "Raf Tanımını Güncelle", null, null },
                    { 1012L, false, true, "0", "WMS.LOCATIONS.DELETE", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, true, "Raf Tanımını Sil", null, null }
                });

            migrationBuilder.InsertData(
                table: "RII_PERMISSION_GROUP_PERMISSIONS",
                columns: new[] { "Id", "BranchCode", "CreatedBy", "CreatedDate", "DeletedBy", "DeletedDate", "PermissionDefinitionId", "PermissionGroupId", "UpdatedBy", "UpdatedDate" },
                values: new object[,]
                {
                    { 1009L, "0", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, 1009L, 1001L, null, null },
                    { 1010L, "0", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, 1010L, 1001L, null, null },
                    { 1011L, "0", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, 1011L, 1001L, null, null },
                    { 1012L, "0", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, 1012L, 1001L, null, null }
                });

            migrationBuilder.CreateIndex(
                name: "IX_RII_LOCATION_IsDeleted",
                table: "RII_LOCATION",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RII_LOCATION_ParentLocationId",
                table: "RII_LOCATION",
                column: "ParentLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_RII_LOCATION_WAREHOUSE_PARENT",
                table: "RII_LOCATION",
                columns: new[] { "WarehouseId", "ParentLocationId" });

            migrationBuilder.CreateIndex(
                name: "IX_RII_LOCATION_WAREHOUSE_TYPE_ACTIVE",
                table: "RII_LOCATION",
                columns: new[] { "WarehouseId", "LocationType", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "UX_RII_LOCATION_BARCODE",
                table: "RII_LOCATION",
                column: "Barcode",
                unique: true,
                filter: "[Barcode] IS NOT NULL AND [IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "UX_RII_LOCATION_BRANCH_WAREHOUSE_CODE",
                table: "RII_LOCATION",
                columns: new[] { "BranchCode", "WarehouseId", "Code" },
                unique: true,
                filter: "[IsDeleted] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RII_LOCATION");

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_GROUP_PERMISSIONS",
                keyColumn: "Id",
                keyValue: 1009L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_GROUP_PERMISSIONS",
                keyColumn: "Id",
                keyValue: 1010L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_GROUP_PERMISSIONS",
                keyColumn: "Id",
                keyValue: 1011L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_GROUP_PERMISSIONS",
                keyColumn: "Id",
                keyValue: 1012L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_DEFINITIONS",
                keyColumn: "Id",
                keyValue: 1009L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_DEFINITIONS",
                keyColumn: "Id",
                keyValue: 1010L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_DEFINITIONS",
                keyColumn: "Id",
                keyValue: 1011L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_DEFINITIONS",
                keyColumn: "Id",
                keyValue: 1012L);
        }
    }
}
