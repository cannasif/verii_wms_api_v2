using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace verii_wms_api_v2.Migrations
{
    /// <inheritdoc />
    public partial class AddWarehouseInboundOutboundModules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "WarehouseInboundLineId",
                table: "RII_QUALITY_INSPECTION_LINES",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "RII_WI_HEADER",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DocumentSeriesId = table.Column<long>(type: "bigint", nullable: false),
                    DocumentNo = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    DocumentDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ReceiptType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    InitiationMode = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false, defaultValue: "OrderBasedTask"),
                    ProcessType = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false, defaultValue: "OrderBasedTask"),
                    LabelStrategy = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false, defaultValue: "None"),
                    SourceSystem = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ExternalReferenceNo = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CorrelationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SupplierId = table.Column<long>(type: "bigint", nullable: true),
                    SupplierCodeSnapshot = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    SupplierNameSnapshot = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    SupplierTaxNoSnapshot = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    TargetWarehouseId = table.Column<long>(type: "bigint", nullable: false),
                    ReceivingLocationId = table.Column<long>(type: "bigint", nullable: false),
                    DefaultPutawayZoneCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
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
                    WaybillNo = table.Column<string>(type: "varchar(15)", unicode: false, maxLength: 15, nullable: true),
                    WaybillDate = table.Column<DateOnly>(type: "date", nullable: true),
                    ElectronicWaybillNo = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: true),
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
                    OverReceiptPolicy = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false, defaultValue: "NotAllowed"),
                    RequireReceiptApproval = table.Column<bool>(type: "bit", nullable: false),
                    RequireQualityApproval = table.Column<bool>(type: "bit", nullable: false),
                    RequireErpApproval = table.Column<bool>(type: "bit", nullable: false),
                    HoldInventoryUntilQualityDecision = table.Column<bool>(type: "bit", nullable: false),
                    BlockPutawayUntilQualityDecision = table.Column<bool>(type: "bit", nullable: false),
                    InventoryAvailabilityPolicy = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false, defaultValue: "AfterQualityApproval"),
                    ErpPostingPolicy = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false, defaultValue: "AfterAllApprovals"),
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
                    table.PrimaryKey("PK_RII_WI_HEADER", x => x.Id);
                    table.CheckConstraint("CK_RII_WI_HEADER_OVER_TOLERANCE", "[OverReceiptTolerancePercent] BETWEEN 0 AND 100");
                    table.CheckConstraint("CK_RII_WI_HEADER_PRIORITY", "[Priority] BETWEEN 1 AND 5");
                    table.ForeignKey(
                        name: "FK_RII_WI_HEADER_RII_CUSTOMER_SupplierId",
                        column: x => x.SupplierId,
                        principalTable: "RII_CUSTOMER",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RII_WI_HEADER_RII_DOCUMENT_SERIES_DocumentSeriesId",
                        column: x => x.DocumentSeriesId,
                        principalTable: "RII_DOCUMENT_SERIES",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RII_WI_HEADER_RII_LOCATION_QualityLocationId",
                        column: x => x.QualityLocationId,
                        principalTable: "RII_LOCATION",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RII_WI_HEADER_RII_LOCATION_QuarantineLocationId",
                        column: x => x.QuarantineLocationId,
                        principalTable: "RII_LOCATION",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RII_WI_HEADER_RII_LOCATION_ReceivingLocationId",
                        column: x => x.ReceivingLocationId,
                        principalTable: "RII_LOCATION",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RII_WI_HEADER_RII_WAREHOUSE_TargetWarehouseId",
                        column: x => x.TargetWarehouseId,
                        principalTable: "RII_WAREHOUSE",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RII_WI_POLICIES",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PolicyKey = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    OverReceiptPolicy = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    OverReceiptTolerancePercent = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: false),
                    AllowUnderReceipt = table.Column<bool>(type: "bit", nullable: false),
                    RequireShortCloseApproval = table.Column<bool>(type: "bit", nullable: false),
                    RequireReceiptApproval = table.Column<bool>(type: "bit", nullable: false),
                    RequireQualityApproval = table.Column<bool>(type: "bit", nullable: false),
                    RequireErpApproval = table.Column<bool>(type: "bit", nullable: false),
                    HoldInventoryUntilQualityDecision = table.Column<bool>(type: "bit", nullable: false),
                    BlockPutawayUntilQualityDecision = table.Column<bool>(type: "bit", nullable: false),
                    InventoryAvailabilityPolicy = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    ErpPostingPolicy = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    AllowOrderlessReceipt = table.Column<bool>(type: "bit", nullable: false),
                    AllowUnplannedReceipt = table.Column<bool>(type: "bit", nullable: false),
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
                    table.PrimaryKey("PK_RII_WI_POLICIES", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RII_WO_HEADER",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DocumentSeriesId = table.Column<long>(type: "bigint", nullable: false),
                    DocumentNo = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    DocumentDate = table.Column<DateOnly>(type: "date", nullable: false),
                    InitiationMode = table.Column<int>(type: "int", nullable: false),
                    SourceSystem = table.Column<int>(type: "int", nullable: false),
                    CorrelationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CustomerId = table.Column<long>(type: "bigint", nullable: false),
                    CustomerCodeSnapshot = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CustomerNameSnapshot = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    SourceWarehouseId = table.Column<long>(type: "bigint", nullable: false),
                    StagingLocationId = table.Column<long>(type: "bigint", nullable: true),
                    LoadingLocationId = table.Column<long>(type: "bigint", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ApprovalStatus = table.Column<int>(type: "int", nullable: false),
                    ErpIntegrationStatus = table.Column<int>(type: "int", nullable: false),
                    PlannedWarehouseOutboundAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ShippedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ExternalReferenceNo = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    WaybillNo = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    IsEDispatch = table.Column<bool>(type: "bit", nullable: false),
                    CarrierCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    CarrierName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    VehiclePlate = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    TrailerPlate = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    DriverName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    SealNo = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    TrackingNo = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Priority = table.Column<byte>(type: "tinyint", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    RequireApproval = table.Column<bool>(type: "bit", nullable: false),
                    RequireAssignee = table.Column<bool>(type: "bit", nullable: false),
                    AllowPartialPicking = table.Column<bool>(type: "bit", nullable: false),
                    AllowPartialWarehouseOutbound = table.Column<bool>(type: "bit", nullable: false),
                    RequireSourceLocation = table.Column<bool>(type: "bit", nullable: false),
                    RequireWarehouseOutboundInformation = table.Column<bool>(type: "bit", nullable: false),
                    RequireLoadingConfirmation = table.Column<bool>(type: "bit", nullable: false),
                    AutoReleaseTaskBased = table.Column<bool>(type: "bit", nullable: false),
                    AutoPostErpAfterApproval = table.Column<bool>(type: "bit", nullable: false),
                    MinimumFulfillmentPercent = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: false),
                    OverPickTolerancePercent = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: false),
                    ReservationPolicy = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    PackingPolicy = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ShortagePolicy = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    OverPickPolicy = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
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
                    table.PrimaryKey("PK_RII_WO_HEADER", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RII_WO_POLICIES",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PolicyKey = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    AllowOrderBasedTask = table.Column<bool>(type: "bit", nullable: false),
                    AllowStockBasedTask = table.Column<bool>(type: "bit", nullable: false),
                    AllowOrderBasedDirect = table.Column<bool>(type: "bit", nullable: false),
                    AllowStockBasedDirect = table.Column<bool>(type: "bit", nullable: false),
                    RequireApproval = table.Column<bool>(type: "bit", nullable: false),
                    RequireAssigneeForTask = table.Column<bool>(type: "bit", nullable: false),
                    AllowMultipleAssignees = table.Column<bool>(type: "bit", nullable: false),
                    AutoReleaseTaskBased = table.Column<bool>(type: "bit", nullable: false),
                    AllowPartialPicking = table.Column<bool>(type: "bit", nullable: false),
                    AllowPartialWarehouseOutbound = table.Column<bool>(type: "bit", nullable: false),
                    RequireSourceLocation = table.Column<bool>(type: "bit", nullable: false),
                    RequireWarehouseOutboundInformation = table.Column<bool>(type: "bit", nullable: false),
                    RequireLoadingConfirmation = table.Column<bool>(type: "bit", nullable: false),
                    AutoPostErpAfterApproval = table.Column<bool>(type: "bit", nullable: false),
                    MinimumFulfillmentPercent = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: false),
                    OverPickTolerancePercent = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: false),
                    ReservationPolicy = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    PackingPolicy = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ShortagePolicy = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    OverPickPolicy = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
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
                    table.PrimaryKey("PK_RII_WO_POLICIES", x => x.Id);
                    table.CheckConstraint("CK_RII_WO_POLICY_FULFILLMENT", "[MinimumFulfillmentPercent] >= 0 AND [MinimumFulfillmentPercent] <= 100");
                    table.CheckConstraint("CK_RII_WO_POLICY_OVERPICK", "[OverPickTolerancePercent] >= 0 AND [OverPickTolerancePercent] <= 100");
                });

            migrationBuilder.CreateTable(
                name: "RII_WI_LABEL_BATCH",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    GrHeaderId = table.Column<long>(type: "bigint", nullable: false),
                    CorrelationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BatchNo = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    TotalLabelCount = table.Column<int>(type: "int", nullable: false),
                    PrintedLabelCount = table.Column<int>(type: "int", nullable: false),
                    ConsumedLabelCount = table.Column<int>(type: "int", nullable: false),
                    VoidLabelCount = table.Column<int>(type: "int", nullable: false),
                    LastPrintedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true),
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
                    table.PrimaryKey("PK_RII_WI_LABEL_BATCH", x => x.Id);
                    table.CheckConstraint("CK_RII_WI_LABEL_BATCH_COUNTS", "[TotalLabelCount] >= 0 AND [PrintedLabelCount] >= 0 AND [ConsumedLabelCount] >= 0 AND [VoidLabelCount] >= 0 AND [PrintedLabelCount] <= [TotalLabelCount] AND [ConsumedLabelCount] + [VoidLabelCount] <= [TotalLabelCount]");
                    table.ForeignKey(
                        name: "FK_RII_WI_LABEL_BATCH_RII_WI_HEADER_GrHeaderId",
                        column: x => x.GrHeaderId,
                        principalTable: "RII_WI_HEADER",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RII_WI_LINE",
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
                    TargetWarehouseId = table.Column<long>(type: "bigint", nullable: false),
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
                    table.PrimaryKey("PK_RII_WI_LINE", x => x.Id);
                    table.CheckConstraint("CK_RII_WI_LINE_LINE_NO", "[LineNo] > 0");
                    table.CheckConstraint("CK_RII_WI_LINE_OVER_TOLERANCE", "[OverReceiptTolerancePercent] BETWEEN 0 AND 100");
                    table.CheckConstraint("CK_RII_WI_LINE_PUTAWAY_TOTAL", "[PutawayQuantity] <= [AcceptedQuantity]");
                    table.CheckConstraint("CK_RII_WI_LINE_QUALITY_TOTAL", "[AcceptedQuantity] + [RejectedQuantity] + [QuarantineQuantity] <= [ReceivedQuantity]");
                    table.CheckConstraint("CK_RII_WI_LINE_QUANTITIES_NONNEGATIVE", "[ExpectedQuantity] >= 0 AND [ReceivedQuantity] >= 0 AND [AcceptedQuantity] >= 0 AND [RejectedQuantity] >= 0 AND [QuarantineQuantity] >= 0 AND [PutawayQuantity] >= 0 AND [ShortClosedQuantity] >= 0");
                    table.CheckConstraint("CK_RII_WI_LINE_UNIT_FACTOR", "[UnitConversionFactor] > 0");
                    table.ForeignKey(
                        name: "FK_RII_WI_LINE_RII_LOCATION_DefaultPutawayLocationId",
                        column: x => x.DefaultPutawayLocationId,
                        principalTable: "RII_LOCATION",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RII_WI_LINE_RII_LOCATION_DefaultReceivingLocationId",
                        column: x => x.DefaultReceivingLocationId,
                        principalTable: "RII_LOCATION",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RII_WI_LINE_RII_STOCK_StockId",
                        column: x => x.StockId,
                        principalTable: "RII_STOCK",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RII_WI_LINE_RII_WAREHOUSE_TargetWarehouseId",
                        column: x => x.TargetWarehouseId,
                        principalTable: "RII_WAREHOUSE",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RII_WI_LINE_RII_WI_HEADER_GrHeaderId",
                        column: x => x.GrHeaderId,
                        principalTable: "RII_WI_HEADER",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RII_WI_LINE_RII_YAP_CODE_YapCodeId",
                        column: x => x.YapCodeId,
                        principalTable: "RII_YAP_CODE",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RII_WI_SOURCE_DOCUMENT",
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
                    table.PrimaryKey("PK_RII_WI_SOURCE_DOCUMENT", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RII_WI_SOURCE_DOCUMENT_RII_WI_HEADER_GrHeaderId",
                        column: x => x.GrHeaderId,
                        principalTable: "RII_WI_HEADER",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RII_WI_STATUS_HISTORY",
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
                    RequestHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
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
                    table.PrimaryKey("PK_RII_WI_STATUS_HISTORY", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RII_WI_STATUS_HISTORY_RII_WI_HEADER_GrHeaderId",
                        column: x => x.GrHeaderId,
                        principalTable: "RII_WI_HEADER",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RII_WI_TASK",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    GrHeaderId = table.Column<long>(type: "bigint", nullable: false),
                    TaskNo = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    TaskType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Priority = table.Column<byte>(type: "tinyint", nullable: false, defaultValue: (byte)3),
                    WarehouseId = table.Column<long>(type: "bigint", nullable: false),
                    ZoneCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    PlannedStartAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true),
                    DueAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true),
                    ReleasedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true),
                    ReleasedBy = table.Column<long>(type: "bigint", nullable: true),
                    StartedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true),
                    CancelledAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true),
                    CancellationReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
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
                    table.PrimaryKey("PK_RII_WI_TASK", x => x.Id);
                    table.CheckConstraint("CK_RII_WI_TASK_PRIORITY", "[Priority] BETWEEN 1 AND 5");
                    table.ForeignKey(
                        name: "FK_RII_WI_TASK_RII_WAREHOUSE_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "RII_WAREHOUSE",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RII_WI_TASK_RII_WI_HEADER_GrHeaderId",
                        column: x => x.GrHeaderId,
                        principalTable: "RII_WI_HEADER",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RII_WO_LINE",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    WarehouseOutboundHeaderId = table.Column<long>(type: "bigint", nullable: false),
                    LineNo = table.Column<int>(type: "int", nullable: false),
                    StockId = table.Column<long>(type: "bigint", nullable: false),
                    StockCodeSnapshot = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    StockNameSnapshot = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    YapCodeId = table.Column<long>(type: "bigint", nullable: true),
                    YapCodeSnapshot = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    UnitCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    RequestedQuantity = table.Column<decimal>(type: "decimal(20,6)", precision: 20, scale: 6, nullable: false),
                    ReservedQuantity = table.Column<decimal>(type: "decimal(20,6)", precision: 20, scale: 6, nullable: false),
                    PickedQuantity = table.Column<decimal>(type: "decimal(20,6)", precision: 20, scale: 6, nullable: false),
                    PackedQuantity = table.Column<decimal>(type: "decimal(20,6)", precision: 20, scale: 6, nullable: false),
                    LoadedQuantity = table.Column<decimal>(type: "decimal(20,6)", precision: 20, scale: 6, nullable: false),
                    ShippedQuantity = table.Column<decimal>(type: "decimal(20,6)", precision: 20, scale: 6, nullable: false),
                    ShortClosedQuantity = table.Column<decimal>(type: "decimal(20,6)", precision: 20, scale: 6, nullable: false),
                    TrackingType = table.Column<int>(type: "int", nullable: false),
                    RequireHandlingUnit = table.Column<bool>(type: "bit", nullable: false),
                    DefaultSourceLocationId = table.Column<long>(type: "bigint", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
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
                    table.PrimaryKey("PK_RII_WO_LINE", x => x.Id);
                    table.CheckConstraint("CK_RII_WO_LINE_QTY", "[RequestedQuantity] > 0 AND [ReservedQuantity] >= 0 AND [PickedQuantity] >= 0 AND [PackedQuantity] >= 0 AND [LoadedQuantity] >= 0 AND [ShippedQuantity] >= 0 AND [ShortClosedQuantity] >= 0");
                    table.ForeignKey(
                        name: "FK_RII_WO_LINE_RII_WO_HEADER_WarehouseOutboundHeaderId",
                        column: x => x.WarehouseOutboundHeaderId,
                        principalTable: "RII_WO_HEADER",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RII_WO_SOURCE_DOCUMENT",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    WarehouseOutboundHeaderId = table.Column<long>(type: "bigint", nullable: false),
                    SourceDocumentType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ExternalDocumentNo = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ExternalDocumentId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ExternalDocumentDate = table.Column<DateOnly>(type: "date", nullable: true),
                    ExternalStatus = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
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
                    table.PrimaryKey("PK_RII_WO_SOURCE_DOCUMENT", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RII_WO_SOURCE_DOCUMENT_RII_WO_HEADER_WarehouseOutboundHeaderId",
                        column: x => x.WarehouseOutboundHeaderId,
                        principalTable: "RII_WO_HEADER",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RII_WO_STATUS_HISTORY",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    WarehouseOutboundHeaderId = table.Column<long>(type: "bigint", nullable: false),
                    FromStatus = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ToStatus = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ChangedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ChangedBy = table.Column<long>(type: "bigint", nullable: false),
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
                    table.PrimaryKey("PK_RII_WO_STATUS_HISTORY", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RII_WO_STATUS_HISTORY_RII_WO_HEADER_WarehouseOutboundHeaderId",
                        column: x => x.WarehouseOutboundHeaderId,
                        principalTable: "RII_WO_HEADER",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RII_WO_TASK",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    WarehouseOutboundHeaderId = table.Column<long>(type: "bigint", nullable: false),
                    TaskNo = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    TaskType = table.Column<int>(type: "int", nullable: false),
                    WarehouseId = table.Column<long>(type: "bigint", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Priority = table.Column<byte>(type: "tinyint", nullable: false),
                    PlannedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
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
                    table.PrimaryKey("PK_RII_WO_TASK", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RII_WO_TASK_RII_WO_HEADER_WarehouseOutboundHeaderId",
                        column: x => x.WarehouseOutboundHeaderId,
                        principalTable: "RII_WO_HEADER",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RII_WI_LINE_SOURCE",
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
                    table.PrimaryKey("PK_RII_WI_LINE_SOURCE", x => x.Id);
                    table.CheckConstraint("CK_RII_WI_LINE_SOURCE_QUANTITIES", "[OrderedQuantity] >= 0 AND [PreviouslyReceivedQuantity] >= 0 AND [AllocatedQuantity] >= 0 AND [ReceivedQuantity] >= 0");
                    table.ForeignKey(
                        name: "FK_RII_WI_LINE_SOURCE_RII_WI_LINE_GrLineId",
                        column: x => x.GrLineId,
                        principalTable: "RII_WI_LINE",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RII_WI_LINE_SOURCE_RII_WI_SOURCE_DOCUMENT_GrSourceDocumentId",
                        column: x => x.GrSourceDocumentId,
                        principalTable: "RII_WI_SOURCE_DOCUMENT",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RII_WI_EXECUTION",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    GrHeaderId = table.Column<long>(type: "bigint", nullable: false),
                    GrTaskId = table.Column<long>(type: "bigint", nullable: true),
                    IdempotencyKey = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    ExecutionNo = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Mode = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                    StockMovementOperationId = table.Column<long>(type: "bigint", nullable: true),
                    DeviceId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ReversalOfExecutionId = table.Column<long>(type: "bigint", nullable: true),
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
                    table.PrimaryKey("PK_RII_WI_EXECUTION", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RII_WI_EXECUTION_RII_STOCK_MOVEMENT_OPERATION_StockMovementOperationId",
                        column: x => x.StockMovementOperationId,
                        principalTable: "RII_STOCK_MOVEMENT_OPERATION",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RII_WI_EXECUTION_RII_WI_EXECUTION_ReversalOfExecutionId",
                        column: x => x.ReversalOfExecutionId,
                        principalTable: "RII_WI_EXECUTION",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RII_WI_EXECUTION_RII_WI_HEADER_GrHeaderId",
                        column: x => x.GrHeaderId,
                        principalTable: "RII_WI_HEADER",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RII_WI_EXECUTION_RII_WI_TASK_GrTaskId",
                        column: x => x.GrTaskId,
                        principalTable: "RII_WI_TASK",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RII_WI_TASK_ASSIGNMENT",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    GrTaskId = table.Column<long>(type: "bigint", nullable: false),
                    UserId = table.Column<long>(type: "bigint", nullable: false),
                    AssignmentRole = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    AssignedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                    AssignedBy = table.Column<long>(type: "bigint", nullable: true),
                    AcceptedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true),
                    StartedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true),
                    UnassignedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true),
                    UnassignedReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
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
                    table.PrimaryKey("PK_RII_WI_TASK_ASSIGNMENT", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RII_WI_TASK_ASSIGNMENT_RII_USERS_UserId",
                        column: x => x.UserId,
                        principalTable: "RII_USERS",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RII_WI_TASK_ASSIGNMENT_RII_WI_TASK_GrTaskId",
                        column: x => x.GrTaskId,
                        principalTable: "RII_WI_TASK",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RII_WI_TASK_LINE",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    GrTaskId = table.Column<long>(type: "bigint", nullable: false),
                    GrLineId = table.Column<long>(type: "bigint", nullable: false),
                    SequenceNo = table.Column<int>(type: "int", nullable: false),
                    FromLocationId = table.Column<long>(type: "bigint", nullable: true),
                    ToLocationId = table.Column<long>(type: "bigint", nullable: true),
                    HandlingUnitId = table.Column<long>(type: "bigint", nullable: true),
                    PlannedQuantity = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    ProcessedQuantity = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    UnitCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
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
                    table.PrimaryKey("PK_RII_WI_TASK_LINE", x => x.Id);
                    table.CheckConstraint("CK_RII_WI_TASK_LINE_QUANTITY", "[PlannedQuantity] > 0 AND [ProcessedQuantity] >= 0");
                    table.CheckConstraint("CK_RII_WI_TASK_LINE_SEQUENCE", "[SequenceNo] > 0");
                    table.ForeignKey(
                        name: "FK_RII_WI_TASK_LINE_RII_LOCATION_FromLocationId",
                        column: x => x.FromLocationId,
                        principalTable: "RII_LOCATION",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RII_WI_TASK_LINE_RII_LOCATION_ToLocationId",
                        column: x => x.ToLocationId,
                        principalTable: "RII_LOCATION",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RII_WI_TASK_LINE_RII_WI_LINE_GrLineId",
                        column: x => x.GrLineId,
                        principalTable: "RII_WI_LINE",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RII_WI_TASK_LINE_RII_WI_TASK_GrTaskId",
                        column: x => x.GrTaskId,
                        principalTable: "RII_WI_TASK",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RII_WO_TRACKING",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    WarehouseOutboundLineId = table.Column<long>(type: "bigint", nullable: false),
                    HandlingUnitNo = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ContainerNo = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    LotNo = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    SerialNo = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    PlannedQuantity = table.Column<decimal>(type: "decimal(20,6)", precision: 20, scale: 6, nullable: false),
                    ReservedQuantity = table.Column<decimal>(type: "decimal(20,6)", precision: 20, scale: 6, nullable: false),
                    PickedQuantity = table.Column<decimal>(type: "decimal(20,6)", precision: 20, scale: 6, nullable: false),
                    PackedQuantity = table.Column<decimal>(type: "decimal(20,6)", precision: 20, scale: 6, nullable: false),
                    LoadedQuantity = table.Column<decimal>(type: "decimal(20,6)", precision: 20, scale: 6, nullable: false),
                    ShippedQuantity = table.Column<decimal>(type: "decimal(20,6)", precision: 20, scale: 6, nullable: false),
                    SourceLocationId = table.Column<long>(type: "bigint", nullable: true),
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
                    table.PrimaryKey("PK_RII_WO_TRACKING", x => x.Id);
                    table.CheckConstraint("CK_RII_WO_TRACKING_QTY", "[PlannedQuantity] > 0 AND [ReservedQuantity] >= 0 AND [PickedQuantity] >= 0 AND [PackedQuantity] >= 0 AND [LoadedQuantity] >= 0 AND [ShippedQuantity] >= 0");
                    table.ForeignKey(
                        name: "FK_RII_WO_TRACKING_RII_WO_LINE_WarehouseOutboundLineId",
                        column: x => x.WarehouseOutboundLineId,
                        principalTable: "RII_WO_LINE",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RII_WO_LINE_SOURCE",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    WarehouseOutboundLineId = table.Column<long>(type: "bigint", nullable: false),
                    WarehouseOutboundSourceDocumentId = table.Column<long>(type: "bigint", nullable: false),
                    ExternalLineId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ExternalLineNo = table.Column<int>(type: "int", nullable: true),
                    ExternalStockCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ExternalYapCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    OrderedQuantity = table.Column<decimal>(type: "decimal(20,6)", precision: 20, scale: 6, nullable: false),
                    PreviouslyShippedQuantity = table.Column<decimal>(type: "decimal(20,6)", precision: 20, scale: 6, nullable: false),
                    AllocatedQuantity = table.Column<decimal>(type: "decimal(20,6)", precision: 20, scale: 6, nullable: false),
                    UnitCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
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
                    table.PrimaryKey("PK_RII_WO_LINE_SOURCE", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RII_WO_LINE_SOURCE_RII_WO_LINE_WarehouseOutboundLineId",
                        column: x => x.WarehouseOutboundLineId,
                        principalTable: "RII_WO_LINE",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RII_WO_LINE_SOURCE_RII_WO_SOURCE_DOCUMENT_WarehouseOutboundSourceDocumentId",
                        column: x => x.WarehouseOutboundSourceDocumentId,
                        principalTable: "RII_WO_SOURCE_DOCUMENT",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RII_WO_TASK_ASSIGNMENT",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    WarehouseOutboundTaskId = table.Column<long>(type: "bigint", nullable: false),
                    UserId = table.Column<long>(type: "bigint", nullable: false),
                    IsPrimary = table.Column<bool>(type: "bit", nullable: false),
                    AssignedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    AssignedBy = table.Column<long>(type: "bigint", nullable: false),
                    AcceptedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
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
                    table.PrimaryKey("PK_RII_WO_TASK_ASSIGNMENT", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RII_WO_TASK_ASSIGNMENT_RII_WO_TASK_WarehouseOutboundTaskId",
                        column: x => x.WarehouseOutboundTaskId,
                        principalTable: "RII_WO_TASK",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RII_WO_TASK_LINE",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    WarehouseOutboundTaskId = table.Column<long>(type: "bigint", nullable: false),
                    WarehouseOutboundLineId = table.Column<long>(type: "bigint", nullable: false),
                    PlannedQuantity = table.Column<decimal>(type: "decimal(20,6)", precision: 20, scale: 6, nullable: false),
                    ProcessedQuantity = table.Column<decimal>(type: "decimal(20,6)", precision: 20, scale: 6, nullable: false),
                    SourceLocationId = table.Column<long>(type: "bigint", nullable: true),
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
                    table.PrimaryKey("PK_RII_WO_TASK_LINE", x => x.Id);
                    table.CheckConstraint("CK_RII_WO_TASK_LINE_QTY", "[PlannedQuantity] > 0 AND [ProcessedQuantity] >= 0");
                    table.ForeignKey(
                        name: "FK_RII_WO_TASK_LINE_RII_WO_LINE_WarehouseOutboundLineId",
                        column: x => x.WarehouseOutboundLineId,
                        principalTable: "RII_WO_LINE",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RII_WO_TASK_LINE_RII_WO_TASK_WarehouseOutboundTaskId",
                        column: x => x.WarehouseOutboundTaskId,
                        principalTable: "RII_WO_TASK",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RII_WI_LABEL",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BatchId = table.Column<long>(type: "bigint", nullable: false),
                    GrHeaderId = table.Column<long>(type: "bigint", nullable: false),
                    GrLineId = table.Column<long>(type: "bigint", nullable: true),
                    GrTaskLineId = table.Column<long>(type: "bigint", nullable: true),
                    StockId = table.Column<long>(type: "bigint", nullable: true),
                    StockCodeSnapshot = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    StockNameSnapshot = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    YapCodeId = table.Column<long>(type: "bigint", nullable: true),
                    YapCodeSnapshot = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    LabelQuantity = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    UnitCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    LotNo = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    SerialNo = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ManufacturingDate = table.Column<DateOnly>(type: "date", nullable: true),
                    ExpirationDate = table.Column<DateOnly>(type: "date", nullable: true),
                    BarcodeValue = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    PrintCount = table.Column<int>(type: "int", nullable: false),
                    LastPrintedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true),
                    AssignedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true),
                    ConsumedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true),
                    VoidReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
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
                    table.PrimaryKey("PK_RII_WI_LABEL", x => x.Id);
                    table.CheckConstraint("CK_RII_WI_LABEL_PRINT_COUNT", "[PrintCount] >= 0");
                    table.CheckConstraint("CK_RII_WI_LABEL_QUANTITY", "[LabelQuantity] > 0");
                    table.ForeignKey(
                        name: "FK_RII_WI_LABEL_RII_STOCK_StockId",
                        column: x => x.StockId,
                        principalTable: "RII_STOCK",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RII_WI_LABEL_RII_WI_HEADER_GrHeaderId",
                        column: x => x.GrHeaderId,
                        principalTable: "RII_WI_HEADER",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RII_WI_LABEL_RII_WI_LABEL_BATCH_BatchId",
                        column: x => x.BatchId,
                        principalTable: "RII_WI_LABEL_BATCH",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RII_WI_LABEL_RII_WI_LINE_GrLineId",
                        column: x => x.GrLineId,
                        principalTable: "RII_WI_LINE",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RII_WI_LABEL_RII_WI_TASK_LINE_GrTaskLineId",
                        column: x => x.GrTaskLineId,
                        principalTable: "RII_WI_TASK_LINE",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RII_WI_LABEL_RII_YAP_CODE_YapCodeId",
                        column: x => x.YapCodeId,
                        principalTable: "RII_YAP_CODE",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RII_WI_TASK_LINE_TRACKING",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    GrTaskLineId = table.Column<long>(type: "bigint", nullable: false),
                    SequenceNo = table.Column<int>(type: "int", nullable: false),
                    StockId = table.Column<long>(type: "bigint", nullable: false),
                    PlannedQuantity = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    LotNo = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    SerialNo = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ManufacturingDate = table.Column<DateOnly>(type: "date", nullable: true),
                    ExpirationDate = table.Column<DateOnly>(type: "date", nullable: true),
                    TargetWarehouseId = table.Column<long>(type: "bigint", nullable: false),
                    ToLocationId = table.Column<long>(type: "bigint", nullable: false),
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
                    table.PrimaryKey("PK_RII_WI_TASK_LINE_TRACKING", x => x.Id);
                    table.CheckConstraint("CK_RII_WI_TASK_LINE_TRACKING_QTY", "[PlannedQuantity] > 0");
                    table.CheckConstraint("CK_RII_WI_TASK_LINE_TRACKING_SEQUENCE", "[SequenceNo] > 0");
                    table.ForeignKey(
                        name: "FK_RII_WI_TASK_LINE_TRACKING_RII_LOCATION_ToLocationId",
                        column: x => x.ToLocationId,
                        principalTable: "RII_LOCATION",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RII_WI_TASK_LINE_TRACKING_RII_STOCK_StockId",
                        column: x => x.StockId,
                        principalTable: "RII_STOCK",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RII_WI_TASK_LINE_TRACKING_RII_WAREHOUSE_TargetWarehouseId",
                        column: x => x.TargetWarehouseId,
                        principalTable: "RII_WAREHOUSE",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RII_WI_TASK_LINE_TRACKING_RII_WI_TASK_LINE_GrTaskLineId",
                        column: x => x.GrTaskLineId,
                        principalTable: "RII_WI_TASK_LINE",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RII_WI_EXECUTION_LINE",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    GrExecutionId = table.Column<long>(type: "bigint", nullable: false),
                    GrLineId = table.Column<long>(type: "bigint", nullable: false),
                    LineNo = table.Column<int>(type: "int", nullable: false),
                    StockId = table.Column<long>(type: "bigint", nullable: false),
                    YapCodeId = table.Column<long>(type: "bigint", nullable: true),
                    Quantity = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    UnitCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    LotNo = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    SerialNo = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    SerialNumberRuleId = table.Column<long>(type: "bigint", nullable: true),
                    SerialNumberRuleVersion = table.Column<int>(type: "int", nullable: true),
                    SerialNumberRuleCodeSnapshot = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    SerialMaskSnapshot = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    ManufacturingDate = table.Column<DateOnly>(type: "date", nullable: true),
                    ExpirationDate = table.Column<DateOnly>(type: "date", nullable: true),
                    ScannedBarcode = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    WarehouseId = table.Column<long>(type: "bigint", nullable: false),
                    LocationId = table.Column<long>(type: "bigint", nullable: false),
                    StockStatus = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    WarehouseInboundLabelId = table.Column<long>(type: "bigint", nullable: true),
                    QualityInspectionLineId = table.Column<long>(type: "bigint", nullable: true),
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
                    table.PrimaryKey("PK_RII_WI_EXECUTION_LINE", x => x.Id);
                    table.CheckConstraint("CK_RII_WI_EXECUTION_LINE_NO", "[LineNo] > 0");
                    table.CheckConstraint("CK_RII_WI_EXECUTION_LINE_QTY", "[Quantity] > 0");
                    table.ForeignKey(
                        name: "FK_RII_WI_EXECUTION_LINE_RII_LOCATION_LocationId",
                        column: x => x.LocationId,
                        principalTable: "RII_LOCATION",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RII_WI_EXECUTION_LINE_RII_QUALITY_INSPECTION_LINES_QualityInspectionLineId",
                        column: x => x.QualityInspectionLineId,
                        principalTable: "RII_QUALITY_INSPECTION_LINES",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RII_WI_EXECUTION_LINE_RII_SERIAL_NUMBER_RULES_SerialNumberRuleId",
                        column: x => x.SerialNumberRuleId,
                        principalTable: "RII_SERIAL_NUMBER_RULES",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RII_WI_EXECUTION_LINE_RII_STOCK_StockId",
                        column: x => x.StockId,
                        principalTable: "RII_STOCK",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RII_WI_EXECUTION_LINE_RII_WAREHOUSE_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "RII_WAREHOUSE",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RII_WI_EXECUTION_LINE_RII_WI_EXECUTION_GrExecutionId",
                        column: x => x.GrExecutionId,
                        principalTable: "RII_WI_EXECUTION",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RII_WI_EXECUTION_LINE_RII_WI_LABEL_WarehouseInboundLabelId",
                        column: x => x.WarehouseInboundLabelId,
                        principalTable: "RII_WI_LABEL",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RII_WI_EXECUTION_LINE_RII_WI_LINE_GrLineId",
                        column: x => x.GrLineId,
                        principalTable: "RII_WI_LINE",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RII_WI_EXECUTION_LINE_RII_YAP_CODE_YapCodeId",
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
                    { 2100L, false, true, "0", "WMS.WAREHOUSE_INBOUND.VIEW", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, true, "Ambar giriÅŸlerini gÃ¶rÃ¼ntÃ¼le", null, null },
                    { 2101L, false, true, "0", "WMS.WAREHOUSE_INBOUND.CREATE", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, true, "Ambar giriÅŸi oluÅŸtur", null, null },
                    { 2102L, false, true, "0", "WMS.WAREHOUSE_INBOUND.UPDATE", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, true, "Ambar giriÅŸini gÃ¼ncelle", null, null },
                    { 2103L, false, true, "0", "WMS.WAREHOUSE_INBOUND.RELEASE", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, true, "Ambar giriÅŸini iÅŸleme aÃ§", null, null },
                    { 2104L, false, true, "0", "WMS.WAREHOUSE_INBOUND.RECEIVE", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, true, "Ambar giriÅŸini iÅŸle", null, null },
                    { 2105L, false, true, "0", "WMS.WAREHOUSE_INBOUND.COMPLETE", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, true, "Ambar giriÅŸini tamamla", null, null },
                    { 2106L, false, true, "0", "WMS.WAREHOUSE_INBOUND.CANCEL", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, true, "Ambar giriÅŸini iptal et", null, null },
                    { 2107L, false, true, "0", "WMS.WAREHOUSE_INBOUND.SETTINGS.VIEW", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, true, "Ambar giriÅŸ ayarlarÄ±nÄ± gÃ¶rÃ¼ntÃ¼le", null, null },
                    { 2108L, false, true, "0", "WMS.WAREHOUSE_INBOUND.SETTINGS.MANAGE", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, true, "Ambar giriÅŸ ayarlarÄ±nÄ± yÃ¶net", null, null },
                    { 2110L, false, true, "0", "WMS.WAREHOUSE_OUTBOUND.VIEW", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, true, "Ambar Ã§Ä±kÄ±ÅŸlarÄ±nÄ± gÃ¶rÃ¼ntÃ¼le", null, null },
                    { 2111L, false, true, "0", "WMS.WAREHOUSE_OUTBOUND.CREATE", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, true, "Ambar Ã§Ä±kÄ±ÅŸÄ± oluÅŸtur", null, null },
                    { 2112L, false, true, "0", "WMS.WAREHOUSE_OUTBOUND.UPDATE", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, true, "Ambar Ã§Ä±kÄ±ÅŸÄ±nÄ± gÃ¼ncelle", null, null },
                    { 2113L, false, true, "0", "WMS.WAREHOUSE_OUTBOUND.DELETE", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, true, "Ambar Ã§Ä±kÄ±ÅŸ taslaÄŸÄ±nÄ± sil", null, null },
                    { 2114L, false, true, "0", "WMS.WAREHOUSE_OUTBOUND.OPERATE", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, true, "Ambar Ã§Ä±kÄ±ÅŸ operasyonunu yÃ¼rÃ¼t", null, null },
                    { 2115L, false, true, "0", "WMS.WAREHOUSE_OUTBOUND.APPROVE", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, true, "Ambar Ã§Ä±kÄ±ÅŸÄ±nÄ± onayla", null, null },
                    { 2116L, false, true, "0", "WMS.WAREHOUSE_OUTBOUND.CANCEL", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, true, "Ambar Ã§Ä±kÄ±ÅŸÄ±nÄ± iptal et", null, null },
                    { 2117L, false, true, "0", "WMS.WAREHOUSE_OUTBOUND.SETTINGS.VIEW", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, true, "Ambar Ã§Ä±kÄ±ÅŸ ayarlarÄ±nÄ± gÃ¶rÃ¼ntÃ¼le", null, null },
                    { 2118L, false, true, "0", "WMS.WAREHOUSE_OUTBOUND.SETTINGS.MANAGE", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, true, "Ambar Ã§Ä±kÄ±ÅŸ ayarlarÄ±nÄ± yÃ¶net", null, null }
                });

            migrationBuilder.InsertData(
                table: "RII_PERMISSION_GROUP_PERMISSIONS",
                columns: new[] { "Id", "BranchCode", "CreatedBy", "CreatedDate", "DeletedBy", "DeletedDate", "PermissionDefinitionId", "PermissionGroupId", "UpdatedBy", "UpdatedDate" },
                values: new object[,]
                {
                    { 2100L, "0", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, 2100L, 1001L, null, null },
                    { 2101L, "0", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, 2101L, 1001L, null, null },
                    { 2102L, "0", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, 2102L, 1001L, null, null },
                    { 2103L, "0", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, 2103L, 1001L, null, null },
                    { 2104L, "0", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, 2104L, 1001L, null, null },
                    { 2105L, "0", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, 2105L, 1001L, null, null },
                    { 2106L, "0", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, 2106L, 1001L, null, null },
                    { 2107L, "0", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, 2107L, 1001L, null, null },
                    { 2108L, "0", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, 2108L, 1001L, null, null },
                    { 2110L, "0", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, 2110L, 1001L, null, null },
                    { 2111L, "0", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, 2111L, 1001L, null, null },
                    { 2112L, "0", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, 2112L, 1001L, null, null },
                    { 2113L, "0", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, 2113L, 1001L, null, null },
                    { 2114L, "0", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, 2114L, 1001L, null, null },
                    { 2115L, "0", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, 2115L, 1001L, null, null },
                    { 2116L, "0", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, 2116L, 1001L, null, null },
                    { 2117L, "0", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, 2117L, 1001L, null, null },
                    { 2118L, "0", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, 2118L, 1001L, null, null }
                });

            migrationBuilder.Sql(SqlServerMigrationSql.Execute(
                "CREATE INDEX [IX_RII_QUALITY_INSPECTION_LINES_WarehouseInboundLineId] ON [RII_QUALITY_INSPECTION_LINES] ([WarehouseInboundLineId]);"));

            migrationBuilder.CreateIndex(
                name: "IX_RII_WI_EXECUTION_GrTaskId",
                table: "RII_WI_EXECUTION",
                column: "GrTaskId");

            migrationBuilder.CreateIndex(
                name: "IX_RII_WI_EXECUTION_HEADER_TIME",
                table: "RII_WI_EXECUTION",
                columns: new[] { "GrHeaderId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_RII_WI_EXECUTION_IsDeleted",
                table: "RII_WI_EXECUTION",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RII_WI_EXECUTION_ReversalOfExecutionId",
                table: "RII_WI_EXECUTION",
                column: "ReversalOfExecutionId");

            migrationBuilder.CreateIndex(
                name: "IX_RII_WI_EXECUTION_StockMovementOperationId",
                table: "RII_WI_EXECUTION",
                column: "StockMovementOperationId");

            migrationBuilder.CreateIndex(
                name: "UX_RII_WI_EXECUTION_BRANCH_NO",
                table: "RII_WI_EXECUTION",
                columns: new[] { "BranchCode", "ExecutionNo" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "UX_RII_WI_EXECUTION_IDEMPOTENCY",
                table: "RII_WI_EXECUTION",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RII_WI_EXECUTION_LINE_GR_LINE",
                table: "RII_WI_EXECUTION_LINE",
                columns: new[] { "GrLineId", "GrExecutionId" });

            migrationBuilder.CreateIndex(
                name: "IX_RII_WI_EXECUTION_LINE_IsDeleted",
                table: "RII_WI_EXECUTION_LINE",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RII_WI_EXECUTION_LINE_LocationId",
                table: "RII_WI_EXECUTION_LINE",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_RII_WI_EXECUTION_LINE_QualityInspectionLineId",
                table: "RII_WI_EXECUTION_LINE",
                column: "QualityInspectionLineId");

            migrationBuilder.CreateIndex(
                name: "IX_RII_WI_EXECUTION_LINE_SerialNumberRuleId",
                table: "RII_WI_EXECUTION_LINE",
                column: "SerialNumberRuleId");

            migrationBuilder.CreateIndex(
                name: "IX_RII_WI_EXECUTION_LINE_TRACE",
                table: "RII_WI_EXECUTION_LINE",
                columns: new[] { "StockId", "LotNo", "SerialNo" });

            migrationBuilder.CreateIndex(
                name: "IX_RII_WI_EXECUTION_LINE_WarehouseId",
                table: "RII_WI_EXECUTION_LINE",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_RII_WI_EXECUTION_LINE_WarehouseInboundLabelId",
                table: "RII_WI_EXECUTION_LINE",
                column: "WarehouseInboundLabelId");

            migrationBuilder.CreateIndex(
                name: "IX_RII_WI_EXECUTION_LINE_YapCodeId",
                table: "RII_WI_EXECUTION_LINE",
                column: "YapCodeId");

            migrationBuilder.CreateIndex(
                name: "UX_RII_WI_EXECUTION_LINE_SEQUENCE",
                table: "RII_WI_EXECUTION_LINE",
                columns: new[] { "GrExecutionId", "LineNo" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_RII_WI_HEADER_BRANCH_STATUS_PLANNED",
                table: "RII_WI_HEADER",
                columns: new[] { "BranchCode", "Status", "PlannedArrivalAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_RII_WI_HEADER_DocumentSeriesId",
                table: "RII_WI_HEADER",
                column: "DocumentSeriesId");

            migrationBuilder.CreateIndex(
                name: "IX_RII_WI_HEADER_IsDeleted",
                table: "RII_WI_HEADER",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RII_WI_HEADER_PROCESS_REPORTING",
                table: "RII_WI_HEADER",
                columns: new[] { "BranchCode", "ProcessType", "Status", "DocumentDate" });

            migrationBuilder.CreateIndex(
                name: "IX_RII_WI_HEADER_QualityLocationId",
                table: "RII_WI_HEADER",
                column: "QualityLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_RII_WI_HEADER_QuarantineLocationId",
                table: "RII_WI_HEADER",
                column: "QuarantineLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_RII_WI_HEADER_ReceivingLocationId",
                table: "RII_WI_HEADER",
                column: "ReceivingLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_RII_WI_HEADER_SUPPLIER_STATUS",
                table: "RII_WI_HEADER",
                columns: new[] { "SupplierId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_RII_WI_HEADER_WAREHOUSE_STATUS",
                table: "RII_WI_HEADER",
                columns: new[] { "TargetWarehouseId", "Status" });

            migrationBuilder.CreateIndex(
                name: "UX_RII_WI_HEADER_BRANCH_DOCUMENT_NO",
                table: "RII_WI_HEADER",
                columns: new[] { "BranchCode", "DocumentNo" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "UX_RII_WI_HEADER_CORRELATION_ID",
                table: "RII_WI_HEADER",
                column: "CorrelationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_RII_WI_HEADER_SUPPLIER_EWAYBILL",
                table: "RII_WI_HEADER",
                columns: new[] { "BranchCode", "SupplierId", "ElectronicWaybillNo" },
                unique: true,
                filter: "[IsDeleted] = 0 AND [ElectronicWaybillNo] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_RII_WI_HEADER_SUPPLIER_WAYBILL",
                table: "RII_WI_HEADER",
                columns: new[] { "BranchCode", "SupplierId", "WaybillNo" },
                unique: true,
                filter: "[IsDeleted] = 0 AND [WaybillNo] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_RII_WI_LABEL_BATCH_STATUS",
                table: "RII_WI_LABEL",
                columns: new[] { "BatchId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_RII_WI_LABEL_GrLineId",
                table: "RII_WI_LABEL",
                column: "GrLineId");

            migrationBuilder.CreateIndex(
                name: "IX_RII_WI_LABEL_GrTaskLineId",
                table: "RII_WI_LABEL",
                column: "GrTaskLineId");

            migrationBuilder.CreateIndex(
                name: "IX_RII_WI_LABEL_HEADER_LINE",
                table: "RII_WI_LABEL",
                columns: new[] { "GrHeaderId", "GrLineId" });

            migrationBuilder.CreateIndex(
                name: "IX_RII_WI_LABEL_IsDeleted",
                table: "RII_WI_LABEL",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RII_WI_LABEL_TRACE",
                table: "RII_WI_LABEL",
                columns: new[] { "StockId", "LotNo", "SerialNo" });

            migrationBuilder.CreateIndex(
                name: "IX_RII_WI_LABEL_YapCodeId",
                table: "RII_WI_LABEL",
                column: "YapCodeId");

            migrationBuilder.CreateIndex(
                name: "UX_RII_WI_LABEL_BARCODE",
                table: "RII_WI_LABEL",
                column: "BarcodeValue",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RII_WI_LABEL_BATCH_HEADER_STATUS",
                table: "RII_WI_LABEL_BATCH",
                columns: new[] { "GrHeaderId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_RII_WI_LABEL_BATCH_IsDeleted",
                table: "RII_WI_LABEL_BATCH",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "UX_RII_WI_LABEL_BATCH_BRANCH_BATCH_NO",
                table: "RII_WI_LABEL_BATCH",
                columns: new[] { "BranchCode", "BatchNo" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "UX_RII_WI_LABEL_BATCH_CORRELATION",
                table: "RII_WI_LABEL_BATCH",
                column: "CorrelationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RII_WI_LINE_DefaultPutawayLocationId",
                table: "RII_WI_LINE",
                column: "DefaultPutawayLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_RII_WI_LINE_DefaultReceivingLocationId",
                table: "RII_WI_LINE",
                column: "DefaultReceivingLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_RII_WI_LINE_IsDeleted",
                table: "RII_WI_LINE",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RII_WI_LINE_STOCK_YAP_STATUS",
                table: "RII_WI_LINE",
                columns: new[] { "StockId", "YapCodeId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_RII_WI_LINE_TARGET_WAREHOUSE_STATUS_STOCK",
                table: "RII_WI_LINE",
                columns: new[] { "TargetWarehouseId", "Status", "StockId" });

            migrationBuilder.CreateIndex(
                name: "IX_RII_WI_LINE_YapCodeId",
                table: "RII_WI_LINE",
                column: "YapCodeId");

            migrationBuilder.CreateIndex(
                name: "UX_RII_WI_LINE_HEADER_LINE_NO",
                table: "RII_WI_LINE",
                columns: new[] { "GrHeaderId", "LineNo" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_RII_WI_LINE_SOURCE_DOCUMENT",
                table: "RII_WI_LINE_SOURCE",
                column: "GrSourceDocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_RII_WI_LINE_SOURCE_IsDeleted",
                table: "RII_WI_LINE_SOURCE",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "UX_RII_WI_LINE_SOURCE_EXTERNAL_LINE",
                table: "RII_WI_LINE_SOURCE",
                columns: new[] { "GrLineId", "GrSourceDocumentId", "ExternalLineId" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_RII_WI_POLICIES_BranchCode_PolicyKey",
                table: "RII_WI_POLICIES",
                columns: new[] { "BranchCode", "PolicyKey" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_RII_WI_POLICIES_IsDeleted",
                table: "RII_WI_POLICIES",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RII_WI_SOURCE_DOCUMENT_HEADER",
                table: "RII_WI_SOURCE_DOCUMENT",
                column: "GrHeaderId");

            migrationBuilder.CreateIndex(
                name: "IX_RII_WI_SOURCE_DOCUMENT_IsDeleted",
                table: "RII_WI_SOURCE_DOCUMENT",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "UX_RII_WI_SOURCE_DOCUMENT_EXTERNAL",
                table: "RII_WI_SOURCE_DOCUMENT",
                columns: new[] { "GrHeaderId", "SourceSystem", "SourceDocumentType", "ExternalDocumentNo" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_RII_WI_STATUS_HISTORY_HEADER_CHANGED_AT",
                table: "RII_WI_STATUS_HISTORY",
                columns: new[] { "GrHeaderId", "ChangedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_RII_WI_STATUS_HISTORY_IsDeleted",
                table: "RII_WI_STATUS_HISTORY",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "UX_RII_WI_STATUS_HISTORY_HEADER_CORRELATION_ID",
                table: "RII_WI_STATUS_HISTORY",
                columns: new[] { "GrHeaderId", "CorrelationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RII_WI_TASK_HEADER_TYPE_STATUS",
                table: "RII_WI_TASK",
                columns: new[] { "GrHeaderId", "TaskType", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_RII_WI_TASK_IsDeleted",
                table: "RII_WI_TASK",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RII_WI_TASK_WORK_QUEUE",
                table: "RII_WI_TASK",
                columns: new[] { "WarehouseId", "Status", "Priority", "DueAtUtc" });

            migrationBuilder.CreateIndex(
                name: "UX_RII_WI_TASK_BRANCH_TASK_NO",
                table: "RII_WI_TASK",
                columns: new[] { "BranchCode", "TaskNo" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_RII_WI_TASK_ASSIGNMENT_IsDeleted",
                table: "RII_WI_TASK_ASSIGNMENT",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RII_WI_TASK_ASSIGNMENT_USER_QUEUE",
                table: "RII_WI_TASK_ASSIGNMENT",
                columns: new[] { "UserId", "Status", "AssignedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "UX_RII_WI_TASK_ASSIGNMENT_ACTIVE_USER",
                table: "RII_WI_TASK_ASSIGNMENT",
                columns: new[] { "GrTaskId", "UserId" },
                unique: true,
                filter: "[IsDeleted] = 0 AND [Status] <> N'Unassigned' AND [Status] <> N'Rejected'");

            migrationBuilder.CreateIndex(
                name: "IX_RII_WI_TASK_LINE_FromLocationId",
                table: "RII_WI_TASK_LINE",
                column: "FromLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_RII_WI_TASK_LINE_GR_LINE_STATUS",
                table: "RII_WI_TASK_LINE",
                columns: new[] { "GrLineId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_RII_WI_TASK_LINE_IsDeleted",
                table: "RII_WI_TASK_LINE",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RII_WI_TASK_LINE_ToLocationId",
                table: "RII_WI_TASK_LINE",
                column: "ToLocationId");

            migrationBuilder.CreateIndex(
                name: "UX_RII_WI_TASK_LINE_TASK_SEQUENCE",
                table: "RII_WI_TASK_LINE",
                columns: new[] { "GrTaskId", "SequenceNo" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_RII_WI_TASK_LINE_TRACKING_IsDeleted",
                table: "RII_WI_TASK_LINE_TRACKING",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RII_WI_TASK_LINE_TRACKING_TargetWarehouseId",
                table: "RII_WI_TASK_LINE_TRACKING",
                column: "TargetWarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_RII_WI_TASK_LINE_TRACKING_ToLocationId",
                table: "RII_WI_TASK_LINE_TRACKING",
                column: "ToLocationId");

            migrationBuilder.CreateIndex(
                name: "UX_RII_WI_TASK_LINE_TRACKING_SEQUENCE",
                table: "RII_WI_TASK_LINE_TRACKING",
                columns: new[] { "GrTaskLineId", "SequenceNo" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "UX_RII_WI_TASK_LINE_TRACKING_SERIAL",
                table: "RII_WI_TASK_LINE_TRACKING",
                columns: new[] { "GrTaskLineId", "SerialNo" },
                unique: true,
                filter: "[IsDeleted] = 0 AND [SerialNo] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_RII_WI_TASK_LINE_TRACKING_STOCK_SERIAL",
                table: "RII_WI_TASK_LINE_TRACKING",
                columns: new[] { "StockId", "SerialNo" },
                unique: true,
                filter: "[IsDeleted] = 0 AND [SerialNo] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_RII_WO_HEADER_BranchCode_DocumentNo",
                table: "RII_WO_HEADER",
                columns: new[] { "BranchCode", "DocumentNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RII_WO_HEADER_BranchCode_Status_PlannedWarehouseOutboundAtUtc",
                table: "RII_WO_HEADER",
                columns: new[] { "BranchCode", "Status", "PlannedWarehouseOutboundAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_RII_WO_HEADER_CorrelationId",
                table: "RII_WO_HEADER",
                column: "CorrelationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RII_WO_HEADER_IsDeleted",
                table: "RII_WO_HEADER",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RII_WO_LINE_IsDeleted",
                table: "RII_WO_LINE",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RII_WO_LINE_WarehouseOutboundHeaderId_LineNo",
                table: "RII_WO_LINE",
                columns: new[] { "WarehouseOutboundHeaderId", "LineNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RII_WO_LINE_SOURCE_IsDeleted",
                table: "RII_WO_LINE_SOURCE",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RII_WO_LINE_SOURCE_WarehouseOutboundLineId_WarehouseOutboundSourceDocumentId_ExternalLineId",
                table: "RII_WO_LINE_SOURCE",
                columns: new[] { "WarehouseOutboundLineId", "WarehouseOutboundSourceDocumentId", "ExternalLineId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RII_WO_LINE_SOURCE_WarehouseOutboundSourceDocumentId_ExternalLineId",
                table: "RII_WO_LINE_SOURCE",
                columns: new[] { "WarehouseOutboundSourceDocumentId", "ExternalLineId" });

            migrationBuilder.CreateIndex(
                name: "IX_RII_WO_POLICIES_BranchCode_PolicyKey",
                table: "RII_WO_POLICIES",
                columns: new[] { "BranchCode", "PolicyKey" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_RII_WO_POLICIES_IsDeleted",
                table: "RII_WO_POLICIES",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RII_WO_SOURCE_DOCUMENT_IsDeleted",
                table: "RII_WO_SOURCE_DOCUMENT",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RII_WO_SOURCE_DOCUMENT_WarehouseOutboundHeaderId_SourceDocumentType_ExternalDocumentNo",
                table: "RII_WO_SOURCE_DOCUMENT",
                columns: new[] { "WarehouseOutboundHeaderId", "SourceDocumentType", "ExternalDocumentNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RII_WO_STATUS_HISTORY_IsDeleted",
                table: "RII_WO_STATUS_HISTORY",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RII_WO_STATUS_HISTORY_WarehouseOutboundHeaderId_ChangedAtUtc",
                table: "RII_WO_STATUS_HISTORY",
                columns: new[] { "WarehouseOutboundHeaderId", "ChangedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_RII_WO_TASK_BranchCode_TaskNo",
                table: "RII_WO_TASK",
                columns: new[] { "BranchCode", "TaskNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RII_WO_TASK_IsDeleted",
                table: "RII_WO_TASK",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RII_WO_TASK_WarehouseOutboundHeaderId",
                table: "RII_WO_TASK",
                column: "WarehouseOutboundHeaderId");

            migrationBuilder.CreateIndex(
                name: "IX_RII_WO_TASK_ASSIGNMENT_IsDeleted",
                table: "RII_WO_TASK_ASSIGNMENT",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RII_WO_TASK_ASSIGNMENT_WarehouseOutboundTaskId_UserId",
                table: "RII_WO_TASK_ASSIGNMENT",
                columns: new[] { "WarehouseOutboundTaskId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RII_WO_TASK_LINE_IsDeleted",
                table: "RII_WO_TASK_LINE",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RII_WO_TASK_LINE_WarehouseOutboundLineId",
                table: "RII_WO_TASK_LINE",
                column: "WarehouseOutboundLineId");

            migrationBuilder.CreateIndex(
                name: "IX_RII_WO_TASK_LINE_WarehouseOutboundTaskId_WarehouseOutboundLineId",
                table: "RII_WO_TASK_LINE",
                columns: new[] { "WarehouseOutboundTaskId", "WarehouseOutboundLineId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RII_WO_TRACKING_IsDeleted",
                table: "RII_WO_TRACKING",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RII_WO_TRACKING_WarehouseOutboundLineId_SerialNo",
                table: "RII_WO_TRACKING",
                columns: new[] { "WarehouseOutboundLineId", "SerialNo" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RII_WI_EXECUTION_LINE");

            migrationBuilder.DropTable(
                name: "RII_WI_LINE_SOURCE");

            migrationBuilder.DropTable(
                name: "RII_WI_POLICIES");

            migrationBuilder.DropTable(
                name: "RII_WI_STATUS_HISTORY");

            migrationBuilder.DropTable(
                name: "RII_WI_TASK_ASSIGNMENT");

            migrationBuilder.DropTable(
                name: "RII_WI_TASK_LINE_TRACKING");

            migrationBuilder.DropTable(
                name: "RII_WO_LINE_SOURCE");

            migrationBuilder.DropTable(
                name: "RII_WO_POLICIES");

            migrationBuilder.DropTable(
                name: "RII_WO_STATUS_HISTORY");

            migrationBuilder.DropTable(
                name: "RII_WO_TASK_ASSIGNMENT");

            migrationBuilder.DropTable(
                name: "RII_WO_TASK_LINE");

            migrationBuilder.DropTable(
                name: "RII_WO_TRACKING");

            migrationBuilder.DropTable(
                name: "RII_WI_EXECUTION");

            migrationBuilder.DropTable(
                name: "RII_WI_LABEL");

            migrationBuilder.DropTable(
                name: "RII_WI_SOURCE_DOCUMENT");

            migrationBuilder.DropTable(
                name: "RII_WO_SOURCE_DOCUMENT");

            migrationBuilder.DropTable(
                name: "RII_WO_TASK");

            migrationBuilder.DropTable(
                name: "RII_WO_LINE");

            migrationBuilder.DropTable(
                name: "RII_WI_LABEL_BATCH");

            migrationBuilder.DropTable(
                name: "RII_WI_TASK_LINE");

            migrationBuilder.DropTable(
                name: "RII_WO_HEADER");

            migrationBuilder.DropTable(
                name: "RII_WI_LINE");

            migrationBuilder.DropTable(
                name: "RII_WI_TASK");

            migrationBuilder.DropTable(
                name: "RII_WI_HEADER");

            migrationBuilder.DropIndex(
                name: "IX_RII_QUALITY_INSPECTION_LINES_WarehouseInboundLineId",
                table: "RII_QUALITY_INSPECTION_LINES");

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_GROUP_PERMISSIONS",
                keyColumn: "Id",
                keyValue: 2100L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_GROUP_PERMISSIONS",
                keyColumn: "Id",
                keyValue: 2101L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_GROUP_PERMISSIONS",
                keyColumn: "Id",
                keyValue: 2102L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_GROUP_PERMISSIONS",
                keyColumn: "Id",
                keyValue: 2103L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_GROUP_PERMISSIONS",
                keyColumn: "Id",
                keyValue: 2104L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_GROUP_PERMISSIONS",
                keyColumn: "Id",
                keyValue: 2105L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_GROUP_PERMISSIONS",
                keyColumn: "Id",
                keyValue: 2106L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_GROUP_PERMISSIONS",
                keyColumn: "Id",
                keyValue: 2107L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_GROUP_PERMISSIONS",
                keyColumn: "Id",
                keyValue: 2108L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_GROUP_PERMISSIONS",
                keyColumn: "Id",
                keyValue: 2110L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_GROUP_PERMISSIONS",
                keyColumn: "Id",
                keyValue: 2111L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_GROUP_PERMISSIONS",
                keyColumn: "Id",
                keyValue: 2112L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_GROUP_PERMISSIONS",
                keyColumn: "Id",
                keyValue: 2113L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_GROUP_PERMISSIONS",
                keyColumn: "Id",
                keyValue: 2114L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_GROUP_PERMISSIONS",
                keyColumn: "Id",
                keyValue: 2115L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_GROUP_PERMISSIONS",
                keyColumn: "Id",
                keyValue: 2116L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_GROUP_PERMISSIONS",
                keyColumn: "Id",
                keyValue: 2117L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_GROUP_PERMISSIONS",
                keyColumn: "Id",
                keyValue: 2118L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_DEFINITIONS",
                keyColumn: "Id",
                keyValue: 2100L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_DEFINITIONS",
                keyColumn: "Id",
                keyValue: 2101L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_DEFINITIONS",
                keyColumn: "Id",
                keyValue: 2102L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_DEFINITIONS",
                keyColumn: "Id",
                keyValue: 2103L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_DEFINITIONS",
                keyColumn: "Id",
                keyValue: 2104L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_DEFINITIONS",
                keyColumn: "Id",
                keyValue: 2105L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_DEFINITIONS",
                keyColumn: "Id",
                keyValue: 2106L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_DEFINITIONS",
                keyColumn: "Id",
                keyValue: 2107L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_DEFINITIONS",
                keyColumn: "Id",
                keyValue: 2108L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_DEFINITIONS",
                keyColumn: "Id",
                keyValue: 2110L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_DEFINITIONS",
                keyColumn: "Id",
                keyValue: 2111L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_DEFINITIONS",
                keyColumn: "Id",
                keyValue: 2112L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_DEFINITIONS",
                keyColumn: "Id",
                keyValue: 2113L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_DEFINITIONS",
                keyColumn: "Id",
                keyValue: 2114L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_DEFINITIONS",
                keyColumn: "Id",
                keyValue: 2115L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_DEFINITIONS",
                keyColumn: "Id",
                keyValue: 2116L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_DEFINITIONS",
                keyColumn: "Id",
                keyValue: 2117L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_DEFINITIONS",
                keyColumn: "Id",
                keyValue: 2118L);

            migrationBuilder.DropColumn(
                name: "WarehouseInboundLineId",
                table: "RII_QUALITY_INSPECTION_LINES");
        }
    }
}
