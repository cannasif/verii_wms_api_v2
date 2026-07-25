using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace verii_wms_api_v2.Migrations
{
    /// <inheritdoc />
    public partial class AddPackingModuleV2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RII_PACKAGING_MATERIAL",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Type = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    TareWeight = table.Column<decimal>(type: "decimal(20,6)", precision: 20, scale: 6, nullable: false),
                    MaxNetWeight = table.Column<decimal>(type: "decimal(20,6)", precision: 20, scale: 6, nullable: true),
                    MaxGrossWeight = table.Column<decimal>(type: "decimal(20,6)", precision: 20, scale: 6, nullable: true),
                    InnerLength = table.Column<decimal>(type: "decimal(20,6)", precision: 20, scale: 6, nullable: true),
                    InnerWidth = table.Column<decimal>(type: "decimal(20,6)", precision: 20, scale: 6, nullable: true),
                    InnerHeight = table.Column<decimal>(type: "decimal(20,6)", precision: 20, scale: 6, nullable: true),
                    MaxVolume = table.Column<decimal>(type: "decimal(20,6)", precision: 20, scale: 6, nullable: true),
                    IsReturnable = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
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
                    table.PrimaryKey("PK_RII_PACKAGING_MATERIAL", x => x.Id);
                    table.CheckConstraint("CK_RII_PACKAGING_MATERIAL_CAPACITY", "[TareWeight] >= 0 AND ([MaxNetWeight] IS NULL OR [MaxNetWeight] > 0) AND ([MaxGrossWeight] IS NULL OR [MaxGrossWeight] > 0)");
                });

            migrationBuilder.CreateTable(
                name: "RII_PACKAGING_SPECIFICATION",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StockId = table.Column<long>(type: "bigint", nullable: true),
                    StockGroupCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CustomerId = table.Column<long>(type: "bigint", nullable: true),
                    PackagingMaterialId = table.Column<long>(type: "bigint", nullable: false),
                    UnitsPerHandlingUnit = table.Column<decimal>(type: "decimal(20,6)", precision: 20, scale: 6, nullable: true),
                    MaxNetWeight = table.Column<decimal>(type: "decimal(20,6)", precision: 20, scale: 6, nullable: true),
                    MaxVolume = table.Column<decimal>(type: "decimal(20,6)", precision: 20, scale: 6, nullable: true),
                    Priority = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
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
                    table.PrimaryKey("PK_RII_PACKAGING_SPECIFICATION", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RII_PACKING_EVENT",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PackingSessionId = table.Column<long>(type: "bigint", nullable: false),
                    HandlingUnitId = table.Column<long>(type: "bigint", nullable: true),
                    EventType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    FromStatus = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    ToStatus = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    IdempotencyKey = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ActorId = table.Column<long>(type: "bigint", nullable: false),
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
                    table.PrimaryKey("PK_RII_PACKING_EVENT", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RII_PACKING_HEADER",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PackingNo = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    SourceType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    SourceHeaderId = table.Column<long>(type: "bigint", nullable: true),
                    SourceDocumentNo = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    WarehouseId = table.Column<long>(type: "bigint", nullable: false),
                    PackingStationId = table.Column<long>(type: "bigint", nullable: false),
                    CustomerId = table.Column<long>(type: "bigint", nullable: true),
                    CustomerCodeSnapshot = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    IdempotencyKey = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OpenedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ClosedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ReleasedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
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
                    table.PrimaryKey("PK_RII_PACKING_HEADER", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RII_PACKING_POLICY",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PolicyKey = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    RequirePacking = table.Column<bool>(type: "bit", nullable: false),
                    AllowPartialPacking = table.Column<bool>(type: "bit", nullable: false),
                    AllowMixedStock = table.Column<bool>(type: "bit", nullable: false),
                    AllowMixedLot = table.Column<bool>(type: "bit", nullable: false),
                    AllowMixedCustomer = table.Column<bool>(type: "bit", nullable: false),
                    RequireSerialLotScan = table.Column<bool>(type: "bit", nullable: false),
                    RequireWeight = table.Column<bool>(type: "bit", nullable: false),
                    WeightTolerancePercent = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: false),
                    RequireDimensions = table.Column<bool>(type: "bit", nullable: false),
                    RequireSscc = table.Column<bool>(type: "bit", nullable: false),
                    AutoGenerateSscc = table.Column<bool>(type: "bit", nullable: false),
                    AutoPrintLabelOnClose = table.Column<bool>(type: "bit", nullable: false),
                    AllowReopen = table.Column<bool>(type: "bit", nullable: false),
                    AllowRepack = table.Column<bool>(type: "bit", nullable: false),
                    ClosePolicy = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ReleasePolicy = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
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
                    table.PrimaryKey("PK_RII_PACKING_POLICY", x => x.Id);
                    table.CheckConstraint("CK_RII_PACKING_POLICY_WEIGHT_TOLERANCE", "[WeightTolerancePercent] BETWEEN 0 AND 100");
                });

            migrationBuilder.CreateTable(
                name: "RII_PACKING_STATION",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    WarehouseId = table.Column<long>(type: "bigint", nullable: false),
                    LocationId = table.Column<long>(type: "bigint", nullable: true),
                    Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    ScaleDeviceCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    PrinterDefinitionId = table.Column<long>(type: "bigint", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
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
                    table.PrimaryKey("PK_RII_PACKING_STATION", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RII_HANDLING_UNIT",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PackingSessionId = table.Column<long>(type: "bigint", nullable: false),
                    ParentHandlingUnitId = table.Column<long>(type: "bigint", nullable: true),
                    PackagingMaterialId = table.Column<long>(type: "bigint", nullable: false),
                    HandlingUnitNo = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Sscc = table.Column<string>(type: "nvarchar(18)", maxLength: 18, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    TareWeight = table.Column<decimal>(type: "decimal(20,6)", precision: 20, scale: 6, nullable: false),
                    NetWeight = table.Column<decimal>(type: "decimal(20,6)", precision: 20, scale: 6, nullable: false),
                    MeasuredGrossWeight = table.Column<decimal>(type: "decimal(20,6)", precision: 20, scale: 6, nullable: true),
                    GrossWeight = table.Column<decimal>(type: "decimal(20,6)", precision: 20, scale: 6, nullable: false),
                    Length = table.Column<decimal>(type: "decimal(20,6)", precision: 20, scale: 6, nullable: true),
                    Width = table.Column<decimal>(type: "decimal(20,6)", precision: 20, scale: 6, nullable: true),
                    Height = table.Column<decimal>(type: "decimal(20,6)", precision: 20, scale: 6, nullable: true),
                    Volume = table.Column<decimal>(type: "decimal(20,6)", precision: 20, scale: 6, nullable: true),
                    ClosedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ClosedBy = table.Column<long>(type: "bigint", nullable: true),
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
                    table.PrimaryKey("PK_RII_HANDLING_UNIT", x => x.Id);
                    table.CheckConstraint("CK_RII_HANDLING_UNIT_WEIGHT", "[TareWeight] >= 0 AND [NetWeight] >= 0 AND [GrossWeight] >= 0");
                    table.ForeignKey(
                        name: "FK_RII_HANDLING_UNIT_RII_HANDLING_UNIT_ParentHandlingUnitId",
                        column: x => x.ParentHandlingUnitId,
                        principalTable: "RII_HANDLING_UNIT",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RII_HANDLING_UNIT_RII_PACKING_HEADER_PackingSessionId",
                        column: x => x.PackingSessionId,
                        principalTable: "RII_PACKING_HEADER",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RII_HANDLING_UNIT_LINE",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    HandlingUnitId = table.Column<long>(type: "bigint", nullable: false),
                    SourceLineId = table.Column<long>(type: "bigint", nullable: false),
                    StockId = table.Column<long>(type: "bigint", nullable: false),
                    StockCodeSnapshot = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    YapCodeId = table.Column<long>(type: "bigint", nullable: true),
                    YapCodeSnapshot = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    UnitCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(20,6)", precision: 20, scale: 6, nullable: false),
                    LotNo = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    SerialNo = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    PackedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    PackedBy = table.Column<long>(type: "bigint", nullable: false),
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
                    table.PrimaryKey("PK_RII_HANDLING_UNIT_LINE", x => x.Id);
                    table.CheckConstraint("CK_RII_HANDLING_UNIT_LINE_QUANTITY", "[Quantity] > 0");
                    table.ForeignKey(
                        name: "FK_RII_HANDLING_UNIT_LINE_RII_HANDLING_UNIT_HandlingUnitId",
                        column: x => x.HandlingUnitId,
                        principalTable: "RII_HANDLING_UNIT",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "RII_PERMISSION_DEFINITIONS",
                columns: new[] { "Id", "AvailableOnMobile", "AvailableOnWeb", "BranchCode", "Code", "CreatedBy", "CreatedDate", "DeletedBy", "DeletedDate", "Description", "IsActive", "Name", "UpdatedBy", "UpdatedDate" },
                values: new object[,]
                {
                    { 2200L, false, true, "0", "WMS.PACKING.VIEW", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, true, "Paketlemeyi görüntüle", null, null },
                    { 2201L, false, true, "0", "WMS.PACKING.OPERATE", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, true, "Paketleme operasyonu yürüt", null, null },
                    { 2202L, false, true, "0", "WMS.PACKING.CLOSE", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, true, "Paketi kapat ve serbest bırak", null, null },
                    { 2203L, false, true, "0", "WMS.PACKING.REOPEN", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, true, "Kapalı paketi yeniden aç", null, null },
                    { 2204L, false, true, "0", "WMS.PACKING.DEFINITIONS.VIEW", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, true, "Paketleme tanımlarını görüntüle", null, null },
                    { 2205L, false, true, "0", "WMS.PACKING.DEFINITIONS.MANAGE", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, true, "Paketleme tanımlarını yönet", null, null },
                    { 2206L, false, true, "0", "WMS.PACKING.SETTINGS.VIEW", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, true, "Paketleme ayarlarını görüntüle", null, null },
                    { 2207L, false, true, "0", "WMS.PACKING.SETTINGS.MANAGE", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, true, "Paketleme ayarlarını yönet", null, null }
                });

            migrationBuilder.InsertData(
                table: "RII_PERMISSION_GROUP_PERMISSIONS",
                columns: new[] { "Id", "BranchCode", "CreatedBy", "CreatedDate", "DeletedBy", "DeletedDate", "PermissionDefinitionId", "PermissionGroupId", "UpdatedBy", "UpdatedDate" },
                values: new object[,]
                {
                    { 2200L, "0", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, 2200L, 1001L, null, null },
                    { 2201L, "0", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, 2201L, 1001L, null, null },
                    { 2202L, "0", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, 2202L, 1001L, null, null },
                    { 2203L, "0", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, 2203L, 1001L, null, null },
                    { 2204L, "0", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, 2204L, 1001L, null, null },
                    { 2205L, "0", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, 2205L, 1001L, null, null },
                    { 2206L, "0", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, 2206L, 1001L, null, null },
                    { 2207L, "0", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, 2207L, 1001L, null, null }
                });

            migrationBuilder.CreateIndex(
                name: "IX_RII_HANDLING_UNIT_BranchCode_HandlingUnitNo",
                table: "RII_HANDLING_UNIT",
                columns: new[] { "BranchCode", "HandlingUnitNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RII_HANDLING_UNIT_IsDeleted",
                table: "RII_HANDLING_UNIT",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RII_HANDLING_UNIT_PackingSessionId",
                table: "RII_HANDLING_UNIT",
                column: "PackingSessionId");

            migrationBuilder.CreateIndex(
                name: "IX_RII_HANDLING_UNIT_ParentHandlingUnitId",
                table: "RII_HANDLING_UNIT",
                column: "ParentHandlingUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_RII_HANDLING_UNIT_Sscc",
                table: "RII_HANDLING_UNIT",
                column: "Sscc",
                unique: true,
                filter: "[Sscc] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_RII_HANDLING_UNIT_LINE_HandlingUnitId",
                table: "RII_HANDLING_UNIT_LINE",
                column: "HandlingUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_RII_HANDLING_UNIT_LINE_IsDeleted",
                table: "RII_HANDLING_UNIT_LINE",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RII_HANDLING_UNIT_LINE_SourceLineId_LotNo_SerialNo",
                table: "RII_HANDLING_UNIT_LINE",
                columns: new[] { "SourceLineId", "LotNo", "SerialNo" });

            migrationBuilder.CreateIndex(
                name: "IX_RII_PACKAGING_MATERIAL_BranchCode_Code",
                table: "RII_PACKAGING_MATERIAL",
                columns: new[] { "BranchCode", "Code" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_RII_PACKAGING_MATERIAL_IsDeleted",
                table: "RII_PACKAGING_MATERIAL",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RII_PACKAGING_SPECIFICATION_BranchCode_StockId_StockGroupCode_CustomerId_Priority",
                table: "RII_PACKAGING_SPECIFICATION",
                columns: new[] { "BranchCode", "StockId", "StockGroupCode", "CustomerId", "Priority" });

            migrationBuilder.CreateIndex(
                name: "IX_RII_PACKAGING_SPECIFICATION_IsDeleted",
                table: "RII_PACKAGING_SPECIFICATION",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RII_PACKING_EVENT_IsDeleted",
                table: "RII_PACKING_EVENT",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RII_PACKING_EVENT_PackingSessionId_IdempotencyKey",
                table: "RII_PACKING_EVENT",
                columns: new[] { "PackingSessionId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RII_PACKING_EVENT_PackingSessionId_OccurredAtUtc",
                table: "RII_PACKING_EVENT",
                columns: new[] { "PackingSessionId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_RII_PACKING_HEADER_BranchCode_PackingNo",
                table: "RII_PACKING_HEADER",
                columns: new[] { "BranchCode", "PackingNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RII_PACKING_HEADER_IdempotencyKey",
                table: "RII_PACKING_HEADER",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RII_PACKING_HEADER_IsDeleted",
                table: "RII_PACKING_HEADER",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RII_PACKING_HEADER_SourceType_SourceHeaderId",
                table: "RII_PACKING_HEADER",
                columns: new[] { "SourceType", "SourceHeaderId" });

            migrationBuilder.CreateIndex(
                name: "IX_RII_PACKING_POLICY_BranchCode_PolicyKey",
                table: "RII_PACKING_POLICY",
                columns: new[] { "BranchCode", "PolicyKey" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_RII_PACKING_POLICY_IsDeleted",
                table: "RII_PACKING_POLICY",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RII_PACKING_STATION_BranchCode_WarehouseId_Code",
                table: "RII_PACKING_STATION",
                columns: new[] { "BranchCode", "WarehouseId", "Code" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_RII_PACKING_STATION_IsDeleted",
                table: "RII_PACKING_STATION",
                column: "IsDeleted");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RII_HANDLING_UNIT_LINE");

            migrationBuilder.DropTable(
                name: "RII_PACKAGING_MATERIAL");

            migrationBuilder.DropTable(
                name: "RII_PACKAGING_SPECIFICATION");

            migrationBuilder.DropTable(
                name: "RII_PACKING_EVENT");

            migrationBuilder.DropTable(
                name: "RII_PACKING_POLICY");

            migrationBuilder.DropTable(
                name: "RII_PACKING_STATION");

            migrationBuilder.DropTable(
                name: "RII_HANDLING_UNIT");

            migrationBuilder.DropTable(
                name: "RII_PACKING_HEADER");

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_GROUP_PERMISSIONS",
                keyColumn: "Id",
                keyValue: 2200L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_GROUP_PERMISSIONS",
                keyColumn: "Id",
                keyValue: 2201L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_GROUP_PERMISSIONS",
                keyColumn: "Id",
                keyValue: 2202L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_GROUP_PERMISSIONS",
                keyColumn: "Id",
                keyValue: 2203L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_GROUP_PERMISSIONS",
                keyColumn: "Id",
                keyValue: 2204L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_GROUP_PERMISSIONS",
                keyColumn: "Id",
                keyValue: 2205L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_GROUP_PERMISSIONS",
                keyColumn: "Id",
                keyValue: 2206L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_GROUP_PERMISSIONS",
                keyColumn: "Id",
                keyValue: 2207L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_DEFINITIONS",
                keyColumn: "Id",
                keyValue: 2200L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_DEFINITIONS",
                keyColumn: "Id",
                keyValue: 2201L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_DEFINITIONS",
                keyColumn: "Id",
                keyValue: 2202L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_DEFINITIONS",
                keyColumn: "Id",
                keyValue: 2203L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_DEFINITIONS",
                keyColumn: "Id",
                keyValue: 2204L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_DEFINITIONS",
                keyColumn: "Id",
                keyValue: 2205L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_DEFINITIONS",
                keyColumn: "Id",
                keyValue: 2206L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_DEFINITIONS",
                keyColumn: "Id",
                keyValue: 2207L);
        }
    }
}
