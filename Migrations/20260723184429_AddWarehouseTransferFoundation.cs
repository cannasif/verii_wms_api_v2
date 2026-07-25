using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace verii_wms_api_v2.Migrations
{
    /// <inheritdoc />
    public partial class AddWarehouseTransferFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RII_WT_HEADER",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DocumentSeriesId = table.Column<long>(type: "bigint", nullable: false),
                    DocumentNo = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    DocumentDate = table.Column<DateOnly>(type: "date", nullable: false),
                    InitiationMode = table.Column<int>(type: "int", nullable: false),
                    ProcessType = table.Column<int>(type: "int", nullable: false),
                    SourceSystem = table.Column<int>(type: "int", nullable: false),
                    CorrelationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExternalReferenceNo = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    SourceWarehouseId = table.Column<long>(type: "bigint", nullable: false),
                    TargetWarehouseId = table.Column<long>(type: "bigint", nullable: false),
                    SourceStagingLocationId = table.Column<long>(type: "bigint", nullable: true),
                    TargetReceivingLocationId = table.Column<long>(type: "bigint", nullable: true),
                    TargetPutawayLocationId = table.Column<long>(type: "bigint", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ApprovalStatus = table.Column<int>(type: "int", nullable: false),
                    ErpIntegrationStatus = table.Column<int>(type: "int", nullable: false),
                    PlannedDispatchAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    PlannedArrivalAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ReleasedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ReleasedBy = table.Column<long>(type: "bigint", nullable: true),
                    ShippedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ShippedBy = table.Column<long>(type: "bigint", nullable: true),
                    ReceivedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ReceivedBy = table.Column<long>(type: "bigint", nullable: true),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CompletedBy = table.Column<long>(type: "bigint", nullable: true),
                    CancelledAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CancelledBy = table.Column<long>(type: "bigint", nullable: true),
                    CancellationReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ShipmentNo = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    WaybillNo = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    WaybillDate = table.Column<DateOnly>(type: "date", nullable: true),
                    CarrierCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    CarrierName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    VehiclePlate = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    TrailerPlate = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    DriverName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    SealNo = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    RequireApproval = table.Column<bool>(type: "bit", nullable: false),
                    AllowPartialPicking = table.Column<bool>(type: "bit", nullable: false),
                    AllowPartialShipment = table.Column<bool>(type: "bit", nullable: false),
                    AllowPartialReceipt = table.Column<bool>(type: "bit", nullable: false),
                    RequireDestinationAcceptance = table.Column<bool>(type: "bit", nullable: false),
                    RequirePutaway = table.Column<bool>(type: "bit", nullable: false),
                    CreateTransitInventory = table.Column<bool>(type: "bit", nullable: false),
                    DiscrepancyPolicy = table.Column<int>(type: "int", nullable: false),
                    Priority = table.Column<byte>(type: "tinyint", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
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
                    table.PrimaryKey("PK_RII_WT_HEADER", x => x.Id);
                    table.CheckConstraint("CK_RII_WT_HEADER_WAREHOUSE", "[SourceWarehouseId] <> [TargetWarehouseId]");
                });

            migrationBuilder.CreateTable(
                name: "RII_WT_LINE",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    WtHeaderId = table.Column<long>(type: "bigint", nullable: false),
                    LineNo = table.Column<int>(type: "int", nullable: false),
                    StockId = table.Column<long>(type: "bigint", nullable: false),
                    StockCodeSnapshot = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    StockNameSnapshot = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    YapCodeId = table.Column<long>(type: "bigint", nullable: true),
                    YapCodeSnapshot = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    UnitCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    BaseUnitCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    UnitConversionFactor = table.Column<decimal>(type: "decimal(20,8)", precision: 20, scale: 8, nullable: false),
                    RequestedQuantity = table.Column<decimal>(type: "decimal(20,6)", precision: 20, scale: 6, nullable: false),
                    ReservedQuantity = table.Column<decimal>(type: "decimal(20,6)", precision: 20, scale: 6, nullable: false),
                    PickedQuantity = table.Column<decimal>(type: "decimal(20,6)", precision: 20, scale: 6, nullable: false),
                    ShippedQuantity = table.Column<decimal>(type: "decimal(20,6)", precision: 20, scale: 6, nullable: false),
                    ReceivedQuantity = table.Column<decimal>(type: "decimal(20,6)", precision: 20, scale: 6, nullable: false),
                    PutawayQuantity = table.Column<decimal>(type: "decimal(20,6)", precision: 20, scale: 6, nullable: false),
                    DamagedQuantity = table.Column<decimal>(type: "decimal(20,6)", precision: 20, scale: 6, nullable: false),
                    LostQuantity = table.Column<decimal>(type: "decimal(20,6)", precision: 20, scale: 6, nullable: false),
                    ShortClosedQuantity = table.Column<decimal>(type: "decimal(20,6)", precision: 20, scale: 6, nullable: false),
                    TrackingType = table.Column<int>(type: "int", nullable: false),
                    RequireLot = table.Column<bool>(type: "bit", nullable: false),
                    RequireSerial = table.Column<bool>(type: "bit", nullable: false),
                    RequireHandlingUnit = table.Column<bool>(type: "bit", nullable: false),
                    SourceWarehouseId = table.Column<long>(type: "bigint", nullable: false),
                    TargetWarehouseId = table.Column<long>(type: "bigint", nullable: false),
                    DefaultSourceLocationId = table.Column<long>(type: "bigint", nullable: true),
                    DefaultTargetLocationId = table.Column<long>(type: "bigint", nullable: true),
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
                    table.PrimaryKey("PK_RII_WT_LINE", x => x.Id);
                    table.CheckConstraint("CK_RII_WT_LINE_QTY", "[RequestedQuantity] > 0 AND [ReservedQuantity] >= 0 AND [PickedQuantity] >= 0 AND [ShippedQuantity] >= 0 AND [ReceivedQuantity] >= 0 AND [PutawayQuantity] >= 0 AND [DamagedQuantity] >= 0 AND [LostQuantity] >= 0 AND [ShortClosedQuantity] >= 0");
                    table.CheckConstraint("CK_RII_WT_LINE_WAREHOUSE", "[SourceWarehouseId] <> [TargetWarehouseId]");
                    table.ForeignKey(
                        name: "FK_RII_WT_LINE_RII_WT_HEADER_WtHeaderId",
                        column: x => x.WtHeaderId,
                        principalTable: "RII_WT_HEADER",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RII_WT_SOURCE_DOCUMENT",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    WtHeaderId = table.Column<long>(type: "bigint", nullable: false),
                    SourceSystem = table.Column<int>(type: "int", nullable: false),
                    SourceDocumentType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ExternalDocumentNo = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ExternalDocumentDate = table.Column<DateOnly>(type: "date", nullable: true),
                    ExternalDocumentId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ExternalStatus = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    LastSynchronizedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
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
                    table.PrimaryKey("PK_RII_WT_SOURCE_DOCUMENT", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RII_WT_SOURCE_DOCUMENT_RII_WT_HEADER_WtHeaderId",
                        column: x => x.WtHeaderId,
                        principalTable: "RII_WT_HEADER",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RII_WT_STATUS_HISTORY",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    WtHeaderId = table.Column<long>(type: "bigint", nullable: false),
                    StatusArea = table.Column<int>(type: "int", nullable: false),
                    FromStatus = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ToStatus = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ChangedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ChangedBy = table.Column<long>(type: "bigint", nullable: true),
                    ReasonCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
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
                    table.PrimaryKey("PK_RII_WT_STATUS_HISTORY", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RII_WT_STATUS_HISTORY_RII_WT_HEADER_WtHeaderId",
                        column: x => x.WtHeaderId,
                        principalTable: "RII_WT_HEADER",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RII_WT_TASK",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    WtHeaderId = table.Column<long>(type: "bigint", nullable: false),
                    TaskNo = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    TaskType = table.Column<int>(type: "int", nullable: false),
                    WarehouseId = table.Column<long>(type: "bigint", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Priority = table.Column<byte>(type: "tinyint", nullable: false),
                    PlannedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    AcceptedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    AcceptedBy = table.Column<long>(type: "bigint", nullable: true),
                    StartedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    StartedBy = table.Column<long>(type: "bigint", nullable: true),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CompletedBy = table.Column<long>(type: "bigint", nullable: true),
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
                    table.PrimaryKey("PK_RII_WT_TASK", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RII_WT_TASK_RII_WT_HEADER_WtHeaderId",
                        column: x => x.WtHeaderId,
                        principalTable: "RII_WT_HEADER",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RII_WT_TRACKING",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    WtLineId = table.Column<long>(type: "bigint", nullable: false),
                    HandlingUnitNo = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    LotNo = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    SerialNo = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ManufacturingDate = table.Column<DateOnly>(type: "date", nullable: true),
                    ExpirationDate = table.Column<DateOnly>(type: "date", nullable: true),
                    PlannedQuantity = table.Column<decimal>(type: "decimal(20,6)", precision: 20, scale: 6, nullable: false),
                    ReservedQuantity = table.Column<decimal>(type: "decimal(20,6)", precision: 20, scale: 6, nullable: false),
                    PickedQuantity = table.Column<decimal>(type: "decimal(20,6)", precision: 20, scale: 6, nullable: false),
                    ShippedQuantity = table.Column<decimal>(type: "decimal(20,6)", precision: 20, scale: 6, nullable: false),
                    ReceivedQuantity = table.Column<decimal>(type: "decimal(20,6)", precision: 20, scale: 6, nullable: false),
                    PutawayQuantity = table.Column<decimal>(type: "decimal(20,6)", precision: 20, scale: 6, nullable: false),
                    SourceLocationId = table.Column<long>(type: "bigint", nullable: true),
                    TargetLocationId = table.Column<long>(type: "bigint", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
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
                    table.PrimaryKey("PK_RII_WT_TRACKING", x => x.Id);
                    table.CheckConstraint("CK_RII_WT_TRACKING_QTY", "[PlannedQuantity] > 0 AND [ReservedQuantity] >= 0 AND [PickedQuantity] >= 0 AND [ShippedQuantity] >= 0 AND [ReceivedQuantity] >= 0 AND [PutawayQuantity] >= 0");
                    table.ForeignKey(
                        name: "FK_RII_WT_TRACKING_RII_WT_LINE_WtLineId",
                        column: x => x.WtLineId,
                        principalTable: "RII_WT_LINE",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RII_WT_LINE_SOURCE",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    WtLineId = table.Column<long>(type: "bigint", nullable: false),
                    WtSourceDocumentId = table.Column<long>(type: "bigint", nullable: false),
                    ExternalLineId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ExternalLineNo = table.Column<int>(type: "int", nullable: true),
                    ExternalStockCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ExternalYapCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    OrderedQuantity = table.Column<decimal>(type: "decimal(20,6)", precision: 20, scale: 6, nullable: false),
                    PreviouslyTransferredQuantity = table.Column<decimal>(type: "decimal(20,6)", precision: 20, scale: 6, nullable: false),
                    AllocatedQuantity = table.Column<decimal>(type: "decimal(20,6)", precision: 20, scale: 6, nullable: false),
                    UnitCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
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
                    table.PrimaryKey("PK_RII_WT_LINE_SOURCE", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RII_WT_LINE_SOURCE_RII_WT_LINE_WtLineId",
                        column: x => x.WtLineId,
                        principalTable: "RII_WT_LINE",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RII_WT_LINE_SOURCE_RII_WT_SOURCE_DOCUMENT_WtSourceDocumentId",
                        column: x => x.WtSourceDocumentId,
                        principalTable: "RII_WT_SOURCE_DOCUMENT",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RII_WT_TASK_ASSIGNMENT",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    WtTaskId = table.Column<long>(type: "bigint", nullable: false),
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
                    table.PrimaryKey("PK_RII_WT_TASK_ASSIGNMENT", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RII_WT_TASK_ASSIGNMENT_RII_WT_TASK_WtTaskId",
                        column: x => x.WtTaskId,
                        principalTable: "RII_WT_TASK",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RII_WT_TASK_LINE",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    WtTaskId = table.Column<long>(type: "bigint", nullable: false),
                    WtLineId = table.Column<long>(type: "bigint", nullable: false),
                    PlannedQuantity = table.Column<decimal>(type: "decimal(20,6)", precision: 20, scale: 6, nullable: false),
                    ProcessedQuantity = table.Column<decimal>(type: "decimal(20,6)", precision: 20, scale: 6, nullable: false),
                    SourceLocationId = table.Column<long>(type: "bigint", nullable: true),
                    TargetLocationId = table.Column<long>(type: "bigint", nullable: true),
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
                    table.PrimaryKey("PK_RII_WT_TASK_LINE", x => x.Id);
                    table.CheckConstraint("CK_RII_WT_TASK_LINE_QTY", "[PlannedQuantity] > 0 AND [ProcessedQuantity] >= 0");
                    table.ForeignKey(
                        name: "FK_RII_WT_TASK_LINE_RII_WT_LINE_WtLineId",
                        column: x => x.WtLineId,
                        principalTable: "RII_WT_LINE",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RII_WT_TASK_LINE_RII_WT_TASK_WtTaskId",
                        column: x => x.WtTaskId,
                        principalTable: "RII_WT_TASK",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RII_WT_HEADER_BranchCode_DocumentNo",
                table: "RII_WT_HEADER",
                columns: new[] { "BranchCode", "DocumentNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RII_WT_HEADER_BranchCode_Status_PlannedDispatchAtUtc",
                table: "RII_WT_HEADER",
                columns: new[] { "BranchCode", "Status", "PlannedDispatchAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_RII_WT_HEADER_CorrelationId",
                table: "RII_WT_HEADER",
                column: "CorrelationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RII_WT_HEADER_IsDeleted",
                table: "RII_WT_HEADER",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RII_WT_LINE_IsDeleted",
                table: "RII_WT_LINE",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RII_WT_LINE_StockId_Status",
                table: "RII_WT_LINE",
                columns: new[] { "StockId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_RII_WT_LINE_WtHeaderId_LineNo",
                table: "RII_WT_LINE",
                columns: new[] { "WtHeaderId", "LineNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RII_WT_LINE_SOURCE_IsDeleted",
                table: "RII_WT_LINE_SOURCE",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RII_WT_LINE_SOURCE_WtLineId",
                table: "RII_WT_LINE_SOURCE",
                column: "WtLineId");

            migrationBuilder.CreateIndex(
                name: "IX_RII_WT_LINE_SOURCE_WtSourceDocumentId_ExternalLineId",
                table: "RII_WT_LINE_SOURCE",
                columns: new[] { "WtSourceDocumentId", "ExternalLineId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RII_WT_SOURCE_DOCUMENT_IsDeleted",
                table: "RII_WT_SOURCE_DOCUMENT",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RII_WT_SOURCE_DOCUMENT_WtHeaderId_SourceDocumentType_ExternalDocumentNo",
                table: "RII_WT_SOURCE_DOCUMENT",
                columns: new[] { "WtHeaderId", "SourceDocumentType", "ExternalDocumentNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RII_WT_STATUS_HISTORY_CorrelationId",
                table: "RII_WT_STATUS_HISTORY",
                column: "CorrelationId");

            migrationBuilder.CreateIndex(
                name: "IX_RII_WT_STATUS_HISTORY_IsDeleted",
                table: "RII_WT_STATUS_HISTORY",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RII_WT_STATUS_HISTORY_WtHeaderId_ChangedAtUtc",
                table: "RII_WT_STATUS_HISTORY",
                columns: new[] { "WtHeaderId", "ChangedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_RII_WT_TASK_BranchCode_TaskNo",
                table: "RII_WT_TASK",
                columns: new[] { "BranchCode", "TaskNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RII_WT_TASK_IsDeleted",
                table: "RII_WT_TASK",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RII_WT_TASK_WarehouseId_TaskType_Status",
                table: "RII_WT_TASK",
                columns: new[] { "WarehouseId", "TaskType", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_RII_WT_TASK_WtHeaderId",
                table: "RII_WT_TASK",
                column: "WtHeaderId");

            migrationBuilder.CreateIndex(
                name: "IX_RII_WT_TASK_ASSIGNMENT_IsDeleted",
                table: "RII_WT_TASK_ASSIGNMENT",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RII_WT_TASK_ASSIGNMENT_UserId_AcceptedAtUtc",
                table: "RII_WT_TASK_ASSIGNMENT",
                columns: new[] { "UserId", "AcceptedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_RII_WT_TASK_ASSIGNMENT_WtTaskId_UserId",
                table: "RII_WT_TASK_ASSIGNMENT",
                columns: new[] { "WtTaskId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RII_WT_TASK_LINE_IsDeleted",
                table: "RII_WT_TASK_LINE",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RII_WT_TASK_LINE_WtLineId",
                table: "RII_WT_TASK_LINE",
                column: "WtLineId");

            migrationBuilder.CreateIndex(
                name: "IX_RII_WT_TASK_LINE_WtTaskId_WtLineId",
                table: "RII_WT_TASK_LINE",
                columns: new[] { "WtTaskId", "WtLineId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RII_WT_TRACKING_IsDeleted",
                table: "RII_WT_TRACKING",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RII_WT_TRACKING_LotNo_SerialNo_Status",
                table: "RII_WT_TRACKING",
                columns: new[] { "LotNo", "SerialNo", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_RII_WT_TRACKING_WtLineId_SerialNo",
                table: "RII_WT_TRACKING",
                columns: new[] { "WtLineId", "SerialNo" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RII_WT_LINE_SOURCE");

            migrationBuilder.DropTable(
                name: "RII_WT_STATUS_HISTORY");

            migrationBuilder.DropTable(
                name: "RII_WT_TASK_ASSIGNMENT");

            migrationBuilder.DropTable(
                name: "RII_WT_TASK_LINE");

            migrationBuilder.DropTable(
                name: "RII_WT_TRACKING");

            migrationBuilder.DropTable(
                name: "RII_WT_SOURCE_DOCUMENT");

            migrationBuilder.DropTable(
                name: "RII_WT_TASK");

            migrationBuilder.DropTable(
                name: "RII_WT_LINE");

            migrationBuilder.DropTable(
                name: "RII_WT_HEADER");
        }
    }
}
