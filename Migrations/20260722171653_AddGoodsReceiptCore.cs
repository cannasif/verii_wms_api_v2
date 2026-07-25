using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace verii_wms_api_v2.Migrations
{
    /// <inheritdoc />
    public partial class AddGoodsReceiptCore : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RII_GR_HEADER",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DocumentSeriesId = table.Column<long>(type: "bigint", nullable: false),
                    DocumentNo = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    DocumentDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ReceiptType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    SourceSystem = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ExternalReferenceNo = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CorrelationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SupplierId = table.Column<long>(type: "bigint", nullable: true),
                    SupplierCodeSnapshot = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    SupplierNameSnapshot = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    SupplierTaxNoSnapshot = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    TargetWarehouseId = table.Column<long>(type: "bigint", nullable: false),
                    ReceivingLocationId = table.Column<long>(type: "bigint", nullable: false),
                    DefaultPutawayZoneId = table.Column<long>(type: "bigint", nullable: true),
                    QualityLocationId = table.Column<long>(type: "bigint", nullable: true),
                    QuarantineLocationId = table.Column<long>(type: "bigint", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ApprovalStatus = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    QualityStatus = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    PutawayStatus = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ErpIntegrationStatus = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    PlannedArrivalAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true),
                    ActualArrivalAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true),
                    ReleasedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true),
                    ReleasedBy = table.Column<long>(type: "bigint", nullable: true),
                    StartedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true),
                    StartedBy = table.Column<long>(type: "bigint", nullable: true),
                    ReceivedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true),
                    ReceivedBy = table.Column<long>(type: "bigint", nullable: true),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true),
                    CompletedBy = table.Column<long>(type: "bigint", nullable: true),
                    CancelledAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true),
                    CancelledBy = table.Column<long>(type: "bigint", nullable: true),
                    CancellationReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    WaybillNo = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    WaybillDate = table.Column<DateOnly>(type: "date", nullable: true),
                    ElectronicWaybillNo = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ShipmentReferenceNo = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CarrierCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    CarrierName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    VehiclePlate = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    TrailerPlate = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    DriverName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    SealNo = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    AllowOverReceipt = table.Column<bool>(type: "bit", nullable: false),
                    OverReceiptTolerancePercent = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: false),
                    AllowUnderReceipt = table.Column<bool>(type: "bit", nullable: false),
                    RequireShortCloseApproval = table.Column<bool>(type: "bit", nullable: false),
                    RequireQualityControl = table.Column<bool>(type: "bit", nullable: false),
                    RequirePutaway = table.Column<bool>(type: "bit", nullable: false),
                    RequireHandlingUnit = table.Column<bool>(type: "bit", nullable: false),
                    Priority = table.Column<byte>(type: "tinyint", nullable: false, defaultValue: (byte)3),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
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
                    table.PrimaryKey("PK_RII_GR_HEADER", x => x.Id);
                    table.CheckConstraint("CK_RII_GR_HEADER_OVER_TOLERANCE", "[OverReceiptTolerancePercent] BETWEEN 0 AND 100");
                    table.CheckConstraint("CK_RII_GR_HEADER_PRIORITY", "[Priority] BETWEEN 1 AND 5");
                    table.ForeignKey(
                        name: "FK_RII_GR_HEADER_RII_CUSTOMER_SupplierId",
                        column: x => x.SupplierId,
                        principalTable: "RII_CUSTOMER",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RII_GR_HEADER_RII_DOCUMENT_SERIES_DocumentSeriesId",
                        column: x => x.DocumentSeriesId,
                        principalTable: "RII_DOCUMENT_SERIES",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RII_GR_HEADER_RII_LOCATION_QualityLocationId",
                        column: x => x.QualityLocationId,
                        principalTable: "RII_LOCATION",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RII_GR_HEADER_RII_LOCATION_QuarantineLocationId",
                        column: x => x.QuarantineLocationId,
                        principalTable: "RII_LOCATION",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RII_GR_HEADER_RII_LOCATION_ReceivingLocationId",
                        column: x => x.ReceivingLocationId,
                        principalTable: "RII_LOCATION",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RII_GR_HEADER_RII_WAREHOUSE_TargetWarehouseId",
                        column: x => x.TargetWarehouseId,
                        principalTable: "RII_WAREHOUSE",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RII_GR_LINE",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    GrHeaderId = table.Column<long>(type: "bigint", nullable: false),
                    LineNo = table.Column<int>(type: "int", nullable: false),
                    StockId = table.Column<long>(type: "bigint", nullable: false),
                    StockCodeSnapshot = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    StockNameSnapshot = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    YapCodeId = table.Column<long>(type: "bigint", nullable: true),
                    YapCodeSnapshot = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    UnitCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    BaseUnitCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    UnitConversionFactor = table.Column<decimal>(type: "decimal(18,8)", precision: 18, scale: 8, nullable: false),
                    ExpectedQuantity = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    ReceivedQuantity = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    AcceptedQuantity = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    RejectedQuantity = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    QuarantineQuantity = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    PutawayQuantity = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    ShortClosedQuantity = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    TrackingType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    RequireLot = table.Column<bool>(type: "bit", nullable: false),
                    RequireSerial = table.Column<bool>(type: "bit", nullable: false),
                    RequireManufacturingDate = table.Column<bool>(type: "bit", nullable: false),
                    RequireExpirationDate = table.Column<bool>(type: "bit", nullable: false),
                    MinimumShelfLifeDays = table.Column<int>(type: "int", nullable: true),
                    RequireQualityControl = table.Column<bool>(type: "bit", nullable: false),
                    RequireHandlingUnit = table.Column<bool>(type: "bit", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    AllowOverReceipt = table.Column<bool>(type: "bit", nullable: false),
                    OverReceiptTolerancePercent = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: false),
                    AllowUnderReceipt = table.Column<bool>(type: "bit", nullable: false),
                    DefaultReceivingLocationId = table.Column<long>(type: "bigint", nullable: true),
                    DefaultPutawayLocationId = table.Column<long>(type: "bigint", nullable: true),
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
                    table.PrimaryKey("PK_RII_GR_LINE", x => x.Id);
                    table.CheckConstraint("CK_RII_GR_LINE_LINE_NO", "[LineNo] > 0");
                    table.CheckConstraint("CK_RII_GR_LINE_OVER_TOLERANCE", "[OverReceiptTolerancePercent] BETWEEN 0 AND 100");
                    table.CheckConstraint("CK_RII_GR_LINE_PUTAWAY_TOTAL", "[PutawayQuantity] <= [AcceptedQuantity]");
                    table.CheckConstraint("CK_RII_GR_LINE_QUALITY_TOTAL", "[AcceptedQuantity] + [RejectedQuantity] + [QuarantineQuantity] <= [ReceivedQuantity]");
                    table.CheckConstraint("CK_RII_GR_LINE_QUANTITIES_NONNEGATIVE", "[ExpectedQuantity] >= 0 AND [ReceivedQuantity] >= 0 AND [AcceptedQuantity] >= 0 AND [RejectedQuantity] >= 0 AND [QuarantineQuantity] >= 0 AND [PutawayQuantity] >= 0 AND [ShortClosedQuantity] >= 0");
                    table.CheckConstraint("CK_RII_GR_LINE_UNIT_FACTOR", "[UnitConversionFactor] > 0");
                    table.ForeignKey(
                        name: "FK_RII_GR_LINE_RII_GR_HEADER_GrHeaderId",
                        column: x => x.GrHeaderId,
                        principalTable: "RII_GR_HEADER",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RII_GR_LINE_RII_LOCATION_DefaultPutawayLocationId",
                        column: x => x.DefaultPutawayLocationId,
                        principalTable: "RII_LOCATION",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RII_GR_LINE_RII_LOCATION_DefaultReceivingLocationId",
                        column: x => x.DefaultReceivingLocationId,
                        principalTable: "RII_LOCATION",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RII_GR_LINE_RII_STOCK_StockId",
                        column: x => x.StockId,
                        principalTable: "RII_STOCK",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RII_GR_LINE_RII_YAP_CODE_YapCodeId",
                        column: x => x.YapCodeId,
                        principalTable: "RII_YAP_CODE",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RII_GR_SOURCE_DOCUMENT",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    GrHeaderId = table.Column<long>(type: "bigint", nullable: false),
                    SourceDocumentType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    SourceSystem = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ExternalDocumentId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ExternalDocumentNo = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ExternalDocumentDate = table.Column<DateOnly>(type: "date", nullable: true),
                    SupplierCodeSnapshot = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    SupplierNameSnapshot = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CurrencyCode = table.Column<string>(type: "varchar(3)", unicode: false, maxLength: 3, nullable: true),
                    LastSynchronizedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true),
                    ExternalVersion = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ExternalStatus = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
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
                    table.PrimaryKey("PK_RII_GR_SOURCE_DOCUMENT", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RII_GR_SOURCE_DOCUMENT_RII_GR_HEADER_GrHeaderId",
                        column: x => x.GrHeaderId,
                        principalTable: "RII_GR_HEADER",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RII_GR_STATUS_HISTORY",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    GrHeaderId = table.Column<long>(type: "bigint", nullable: false),
                    StatusArea = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    FromStatus = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    ToStatus = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ChangedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                    ChangedBy = table.Column<long>(type: "bigint", nullable: true),
                    ReasonCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CorrelationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
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
                    table.PrimaryKey("PK_RII_GR_STATUS_HISTORY", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RII_GR_STATUS_HISTORY_RII_GR_HEADER_GrHeaderId",
                        column: x => x.GrHeaderId,
                        principalTable: "RII_GR_HEADER",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RII_GR_LINE_SOURCE",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    GrLineId = table.Column<long>(type: "bigint", nullable: false),
                    GrSourceDocumentId = table.Column<long>(type: "bigint", nullable: false),
                    ExternalLineId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ExternalLineNo = table.Column<int>(type: "int", nullable: true),
                    ExternalStockCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ExternalYapCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    OrderedQuantity = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    PreviouslyReceivedQuantity = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    AllocatedQuantity = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    ReceivedQuantity = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    UnitCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ExternalStatus = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
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
                    table.PrimaryKey("PK_RII_GR_LINE_SOURCE", x => x.Id);
                    table.CheckConstraint("CK_RII_GR_LINE_SOURCE_QUANTITIES", "[OrderedQuantity] >= 0 AND [PreviouslyReceivedQuantity] >= 0 AND [AllocatedQuantity] >= 0 AND [ReceivedQuantity] >= 0");
                    table.ForeignKey(
                        name: "FK_RII_GR_LINE_SOURCE_RII_GR_LINE_GrLineId",
                        column: x => x.GrLineId,
                        principalTable: "RII_GR_LINE",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RII_GR_LINE_SOURCE_RII_GR_SOURCE_DOCUMENT_GrSourceDocumentId",
                        column: x => x.GrSourceDocumentId,
                        principalTable: "RII_GR_SOURCE_DOCUMENT",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "RII_PERMISSION_DEFINITIONS",
                columns: new[] { "Id", "AvailableOnMobile", "AvailableOnWeb", "BranchCode", "Code", "CreatedBy", "CreatedDate", "DeletedBy", "DeletedDate", "Description", "IsActive", "Name", "UpdatedBy", "UpdatedDate" },
                values: new object[,]
                {
                    { 1036L, false, true, "0", "WMS.GOODS_RECEIPT.VIEW", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, true, "Mal kabulleri görüntüle", null, null },
                    { 1037L, false, true, "0", "WMS.GOODS_RECEIPT.CREATE", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, true, "Mal kabul oluştur", null, null },
                    { 1038L, false, true, "0", "WMS.GOODS_RECEIPT.UPDATE", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, true, "Mal kabul güncelle", null, null },
                    { 1039L, false, true, "0", "WMS.GOODS_RECEIPT.RELEASE", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, true, "Mal kabulü işleme aç", null, null },
                    { 1040L, false, true, "0", "WMS.GOODS_RECEIPT.RECEIVE", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, true, "Mal kabul işle", null, null },
                    { 1041L, false, true, "0", "WMS.GOODS_RECEIPT.COMPLETE", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, true, "Mal kabulü tamamla", null, null },
                    { 1042L, false, true, "0", "WMS.GOODS_RECEIPT.CANCEL", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, true, "Mal kabulü iptal et", null, null },
                    { 1043L, false, true, "0", "WMS.GOODS_RECEIPT.ERP_RETRY", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, true, "Mal kabul ERP aktarımını yeniden dene", null, null }
                });

            migrationBuilder.InsertData(
                table: "RII_PERMISSION_GROUP_PERMISSIONS",
                columns: new[] { "Id", "BranchCode", "CreatedBy", "CreatedDate", "DeletedBy", "DeletedDate", "PermissionDefinitionId", "PermissionGroupId", "UpdatedBy", "UpdatedDate" },
                values: new object[,]
                {
                    { 1033L, "0", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, 1033L, 1001L, null, null },
                    { 1034L, "0", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, 1034L, 1001L, null, null },
                    { 1035L, "0", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, 1035L, 1001L, null, null },
                    { 1036L, "0", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, 1036L, 1001L, null, null },
                    { 1037L, "0", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, 1037L, 1001L, null, null },
                    { 1038L, "0", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, 1038L, 1001L, null, null },
                    { 1039L, "0", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, 1039L, 1001L, null, null },
                    { 1040L, "0", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, 1040L, 1001L, null, null },
                    { 1041L, "0", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, 1041L, 1001L, null, null },
                    { 1042L, "0", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, 1042L, 1001L, null, null },
                    { 1043L, "0", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, 1043L, 1001L, null, null }
                });

            migrationBuilder.CreateIndex(
                name: "IX_RII_GR_HEADER_BRANCH_STATUS_PLANNED",
                table: "RII_GR_HEADER",
                columns: new[] { "BranchCode", "Status", "PlannedArrivalAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_RII_GR_HEADER_DocumentSeriesId",
                table: "RII_GR_HEADER",
                column: "DocumentSeriesId");

            migrationBuilder.CreateIndex(
                name: "IX_RII_GR_HEADER_IsDeleted",
                table: "RII_GR_HEADER",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RII_GR_HEADER_QualityLocationId",
                table: "RII_GR_HEADER",
                column: "QualityLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_RII_GR_HEADER_QuarantineLocationId",
                table: "RII_GR_HEADER",
                column: "QuarantineLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_RII_GR_HEADER_ReceivingLocationId",
                table: "RII_GR_HEADER",
                column: "ReceivingLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_RII_GR_HEADER_SUPPLIER_STATUS",
                table: "RII_GR_HEADER",
                columns: new[] { "SupplierId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_RII_GR_HEADER_WAREHOUSE_STATUS",
                table: "RII_GR_HEADER",
                columns: new[] { "TargetWarehouseId", "Status" });

            migrationBuilder.CreateIndex(
                name: "UX_RII_GR_HEADER_BRANCH_DOCUMENT_NO",
                table: "RII_GR_HEADER",
                columns: new[] { "BranchCode", "DocumentNo" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "UX_RII_GR_HEADER_CORRELATION_ID",
                table: "RII_GR_HEADER",
                column: "CorrelationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RII_GR_LINE_DefaultPutawayLocationId",
                table: "RII_GR_LINE",
                column: "DefaultPutawayLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_RII_GR_LINE_DefaultReceivingLocationId",
                table: "RII_GR_LINE",
                column: "DefaultReceivingLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_RII_GR_LINE_IsDeleted",
                table: "RII_GR_LINE",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RII_GR_LINE_STOCK_YAP_STATUS",
                table: "RII_GR_LINE",
                columns: new[] { "StockId", "YapCodeId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_RII_GR_LINE_YapCodeId",
                table: "RII_GR_LINE",
                column: "YapCodeId");

            migrationBuilder.CreateIndex(
                name: "UX_RII_GR_LINE_HEADER_LINE_NO",
                table: "RII_GR_LINE",
                columns: new[] { "GrHeaderId", "LineNo" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_RII_GR_LINE_SOURCE_DOCUMENT",
                table: "RII_GR_LINE_SOURCE",
                column: "GrSourceDocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_RII_GR_LINE_SOURCE_IsDeleted",
                table: "RII_GR_LINE_SOURCE",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "UX_RII_GR_LINE_SOURCE_EXTERNAL_LINE",
                table: "RII_GR_LINE_SOURCE",
                columns: new[] { "GrLineId", "GrSourceDocumentId", "ExternalLineId" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_RII_GR_SOURCE_DOCUMENT_HEADER",
                table: "RII_GR_SOURCE_DOCUMENT",
                column: "GrHeaderId");

            migrationBuilder.CreateIndex(
                name: "IX_RII_GR_SOURCE_DOCUMENT_IsDeleted",
                table: "RII_GR_SOURCE_DOCUMENT",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "UX_RII_GR_SOURCE_DOCUMENT_EXTERNAL",
                table: "RII_GR_SOURCE_DOCUMENT",
                columns: new[] { "GrHeaderId", "SourceSystem", "SourceDocumentType", "ExternalDocumentNo" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_RII_GR_STATUS_HISTORY_CORRELATION_ID",
                table: "RII_GR_STATUS_HISTORY",
                column: "CorrelationId");

            migrationBuilder.CreateIndex(
                name: "IX_RII_GR_STATUS_HISTORY_HEADER_CHANGED_AT",
                table: "RII_GR_STATUS_HISTORY",
                columns: new[] { "GrHeaderId", "ChangedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_RII_GR_STATUS_HISTORY_IsDeleted",
                table: "RII_GR_STATUS_HISTORY",
                column: "IsDeleted");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RII_GR_LINE_SOURCE");

            migrationBuilder.DropTable(
                name: "RII_GR_STATUS_HISTORY");

            migrationBuilder.DropTable(
                name: "RII_GR_LINE");

            migrationBuilder.DropTable(
                name: "RII_GR_SOURCE_DOCUMENT");

            migrationBuilder.DropTable(
                name: "RII_GR_HEADER");

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_GROUP_PERMISSIONS",
                keyColumn: "Id",
                keyValue: 1033L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_GROUP_PERMISSIONS",
                keyColumn: "Id",
                keyValue: 1034L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_GROUP_PERMISSIONS",
                keyColumn: "Id",
                keyValue: 1035L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_GROUP_PERMISSIONS",
                keyColumn: "Id",
                keyValue: 1036L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_GROUP_PERMISSIONS",
                keyColumn: "Id",
                keyValue: 1037L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_GROUP_PERMISSIONS",
                keyColumn: "Id",
                keyValue: 1038L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_GROUP_PERMISSIONS",
                keyColumn: "Id",
                keyValue: 1039L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_GROUP_PERMISSIONS",
                keyColumn: "Id",
                keyValue: 1040L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_GROUP_PERMISSIONS",
                keyColumn: "Id",
                keyValue: 1041L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_GROUP_PERMISSIONS",
                keyColumn: "Id",
                keyValue: 1042L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_GROUP_PERMISSIONS",
                keyColumn: "Id",
                keyValue: 1043L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_DEFINITIONS",
                keyColumn: "Id",
                keyValue: 1036L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_DEFINITIONS",
                keyColumn: "Id",
                keyValue: 1037L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_DEFINITIONS",
                keyColumn: "Id",
                keyValue: 1038L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_DEFINITIONS",
                keyColumn: "Id",
                keyValue: 1039L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_DEFINITIONS",
                keyColumn: "Id",
                keyValue: 1040L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_DEFINITIONS",
                keyColumn: "Id",
                keyValue: 1041L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_DEFINITIONS",
                keyColumn: "Id",
                keyValue: 1042L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_DEFINITIONS",
                keyColumn: "Id",
                keyValue: 1043L);
        }
    }
}
