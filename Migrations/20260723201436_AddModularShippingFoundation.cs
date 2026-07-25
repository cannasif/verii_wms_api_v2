using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace verii_wms_api_v2.Migrations
{
    /// <inheritdoc />
    public partial class AddModularShippingFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RII_SH_HEADER",
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
                    PlannedShipmentAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
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
                    AllowPartialShipment = table.Column<bool>(type: "bit", nullable: false),
                    RequireSourceLocation = table.Column<bool>(type: "bit", nullable: false),
                    RequireShipmentInformation = table.Column<bool>(type: "bit", nullable: false),
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
                    table.PrimaryKey("PK_RII_SH_HEADER", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RII_SH_POLICIES",
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
                    AllowPartialShipment = table.Column<bool>(type: "bit", nullable: false),
                    RequireSourceLocation = table.Column<bool>(type: "bit", nullable: false),
                    RequireShipmentInformation = table.Column<bool>(type: "bit", nullable: false),
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
                    table.PrimaryKey("PK_RII_SH_POLICIES", x => x.Id);
                    table.CheckConstraint("CK_RII_SH_POLICY_FULFILLMENT", "[MinimumFulfillmentPercent] >= 0 AND [MinimumFulfillmentPercent] <= 100");
                    table.CheckConstraint("CK_RII_SH_POLICY_OVERPICK", "[OverPickTolerancePercent] >= 0 AND [OverPickTolerancePercent] <= 100");
                });

            migrationBuilder.CreateTable(
                name: "RII_SH_LINE",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ShipmentHeaderId = table.Column<long>(type: "bigint", nullable: false),
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
                    table.PrimaryKey("PK_RII_SH_LINE", x => x.Id);
                    table.CheckConstraint("CK_RII_SH_LINE_QTY", "[RequestedQuantity] > 0 AND [ReservedQuantity] >= 0 AND [PickedQuantity] >= 0 AND [PackedQuantity] >= 0 AND [LoadedQuantity] >= 0 AND [ShippedQuantity] >= 0 AND [ShortClosedQuantity] >= 0");
                    table.ForeignKey(
                        name: "FK_RII_SH_LINE_RII_SH_HEADER_ShipmentHeaderId",
                        column: x => x.ShipmentHeaderId,
                        principalTable: "RII_SH_HEADER",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RII_SH_SOURCE_DOCUMENT",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ShipmentHeaderId = table.Column<long>(type: "bigint", nullable: false),
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
                    table.PrimaryKey("PK_RII_SH_SOURCE_DOCUMENT", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RII_SH_SOURCE_DOCUMENT_RII_SH_HEADER_ShipmentHeaderId",
                        column: x => x.ShipmentHeaderId,
                        principalTable: "RII_SH_HEADER",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RII_SH_STATUS_HISTORY",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ShipmentHeaderId = table.Column<long>(type: "bigint", nullable: false),
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
                    table.PrimaryKey("PK_RII_SH_STATUS_HISTORY", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RII_SH_STATUS_HISTORY_RII_SH_HEADER_ShipmentHeaderId",
                        column: x => x.ShipmentHeaderId,
                        principalTable: "RII_SH_HEADER",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RII_SH_TASK",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ShipmentHeaderId = table.Column<long>(type: "bigint", nullable: false),
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
                    table.PrimaryKey("PK_RII_SH_TASK", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RII_SH_TASK_RII_SH_HEADER_ShipmentHeaderId",
                        column: x => x.ShipmentHeaderId,
                        principalTable: "RII_SH_HEADER",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RII_SH_TRACKING",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ShipmentLineId = table.Column<long>(type: "bigint", nullable: false),
                    HandlingUnitNo = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ContainerNo = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    LotNo = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    SerialNo = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    PlannedQuantity = table.Column<decimal>(type: "decimal(20,6)", precision: 20, scale: 6, nullable: false),
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
                    table.PrimaryKey("PK_RII_SH_TRACKING", x => x.Id);
                    table.CheckConstraint("CK_RII_SH_TRACKING_QTY", "[PlannedQuantity] > 0 AND [PickedQuantity] >= 0 AND [PackedQuantity] >= 0 AND [LoadedQuantity] >= 0 AND [ShippedQuantity] >= 0");
                    table.ForeignKey(
                        name: "FK_RII_SH_TRACKING_RII_SH_LINE_ShipmentLineId",
                        column: x => x.ShipmentLineId,
                        principalTable: "RII_SH_LINE",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RII_SH_LINE_SOURCE",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ShipmentLineId = table.Column<long>(type: "bigint", nullable: false),
                    ShipmentSourceDocumentId = table.Column<long>(type: "bigint", nullable: false),
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
                    table.PrimaryKey("PK_RII_SH_LINE_SOURCE", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RII_SH_LINE_SOURCE_RII_SH_LINE_ShipmentLineId",
                        column: x => x.ShipmentLineId,
                        principalTable: "RII_SH_LINE",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RII_SH_LINE_SOURCE_RII_SH_SOURCE_DOCUMENT_ShipmentSourceDocumentId",
                        column: x => x.ShipmentSourceDocumentId,
                        principalTable: "RII_SH_SOURCE_DOCUMENT",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RII_SH_TASK_ASSIGNMENT",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ShipmentTaskId = table.Column<long>(type: "bigint", nullable: false),
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
                    table.PrimaryKey("PK_RII_SH_TASK_ASSIGNMENT", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RII_SH_TASK_ASSIGNMENT_RII_SH_TASK_ShipmentTaskId",
                        column: x => x.ShipmentTaskId,
                        principalTable: "RII_SH_TASK",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RII_SH_TASK_LINE",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ShipmentTaskId = table.Column<long>(type: "bigint", nullable: false),
                    ShipmentLineId = table.Column<long>(type: "bigint", nullable: false),
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
                    table.PrimaryKey("PK_RII_SH_TASK_LINE", x => x.Id);
                    table.CheckConstraint("CK_RII_SH_TASK_LINE_QTY", "[PlannedQuantity] > 0 AND [ProcessedQuantity] >= 0");
                    table.ForeignKey(
                        name: "FK_RII_SH_TASK_LINE_RII_SH_LINE_ShipmentLineId",
                        column: x => x.ShipmentLineId,
                        principalTable: "RII_SH_LINE",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RII_SH_TASK_LINE_RII_SH_TASK_ShipmentTaskId",
                        column: x => x.ShipmentTaskId,
                        principalTable: "RII_SH_TASK",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "RII_PERMISSION_DEFINITIONS",
                columns: new[] { "Id", "AvailableOnMobile", "AvailableOnWeb", "BranchCode", "Code", "CreatedBy", "CreatedDate", "DeletedBy", "DeletedDate", "Description", "IsActive", "Name", "UpdatedBy", "UpdatedDate" },
                values: new object[,]
                {
                    { 2000L, false, true, "0", "WMS.SHIPPING.VIEW", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, true, "Sevk kayıtlarını görüntüle", null, null },
                    { 2001L, false, true, "0", "WMS.SHIPPING.CREATE", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, true, "Sevk taslağı oluştur", null, null },
                    { 2002L, false, true, "0", "WMS.SHIPPING.OPERATE", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, true, "Toplama paketleme ve yükleme işlemlerini yürüt", null, null },
                    { 2003L, false, true, "0", "WMS.SHIPPING.APPROVE", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, true, "Sevki onayla", null, null },
                    { 2004L, false, true, "0", "WMS.SHIPPING.SETTINGS.VIEW", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, true, "Sevk ayarlarını görüntüle", null, null },
                    { 2005L, false, true, "0", "WMS.SHIPPING.SETTINGS.MANAGE", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, true, "Sevk ayarlarını yönet", null, null }
                });

            migrationBuilder.InsertData(
                table: "RII_PERMISSION_GROUP_PERMISSIONS",
                columns: new[] { "Id", "BranchCode", "CreatedBy", "CreatedDate", "DeletedBy", "DeletedDate", "PermissionDefinitionId", "PermissionGroupId", "UpdatedBy", "UpdatedDate" },
                values: new object[,]
                {
                    { 2000L, "0", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, 2000L, 1001L, null, null },
                    { 2001L, "0", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, 2001L, 1001L, null, null },
                    { 2002L, "0", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, 2002L, 1001L, null, null },
                    { 2003L, "0", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, 2003L, 1001L, null, null },
                    { 2004L, "0", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, 2004L, 1001L, null, null },
                    { 2005L, "0", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, 2005L, 1001L, null, null }
                });

            migrationBuilder.CreateIndex(
                name: "IX_RII_SH_HEADER_BranchCode_DocumentNo",
                table: "RII_SH_HEADER",
                columns: new[] { "BranchCode", "DocumentNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RII_SH_HEADER_BranchCode_Status_PlannedShipmentAtUtc",
                table: "RII_SH_HEADER",
                columns: new[] { "BranchCode", "Status", "PlannedShipmentAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_RII_SH_HEADER_CorrelationId",
                table: "RII_SH_HEADER",
                column: "CorrelationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RII_SH_HEADER_IsDeleted",
                table: "RII_SH_HEADER",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RII_SH_LINE_IsDeleted",
                table: "RII_SH_LINE",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RII_SH_LINE_ShipmentHeaderId_LineNo",
                table: "RII_SH_LINE",
                columns: new[] { "ShipmentHeaderId", "LineNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RII_SH_LINE_SOURCE_IsDeleted",
                table: "RII_SH_LINE_SOURCE",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RII_SH_LINE_SOURCE_ShipmentLineId",
                table: "RII_SH_LINE_SOURCE",
                column: "ShipmentLineId");

            migrationBuilder.CreateIndex(
                name: "IX_RII_SH_LINE_SOURCE_ShipmentSourceDocumentId_ExternalLineId",
                table: "RII_SH_LINE_SOURCE",
                columns: new[] { "ShipmentSourceDocumentId", "ExternalLineId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RII_SH_POLICIES_BranchCode_PolicyKey",
                table: "RII_SH_POLICIES",
                columns: new[] { "BranchCode", "PolicyKey" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_RII_SH_POLICIES_IsDeleted",
                table: "RII_SH_POLICIES",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RII_SH_SOURCE_DOCUMENT_IsDeleted",
                table: "RII_SH_SOURCE_DOCUMENT",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RII_SH_SOURCE_DOCUMENT_ShipmentHeaderId_SourceDocumentType_ExternalDocumentNo",
                table: "RII_SH_SOURCE_DOCUMENT",
                columns: new[] { "ShipmentHeaderId", "SourceDocumentType", "ExternalDocumentNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RII_SH_STATUS_HISTORY_IsDeleted",
                table: "RII_SH_STATUS_HISTORY",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RII_SH_STATUS_HISTORY_ShipmentHeaderId_ChangedAtUtc",
                table: "RII_SH_STATUS_HISTORY",
                columns: new[] { "ShipmentHeaderId", "ChangedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_RII_SH_TASK_BranchCode_TaskNo",
                table: "RII_SH_TASK",
                columns: new[] { "BranchCode", "TaskNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RII_SH_TASK_IsDeleted",
                table: "RII_SH_TASK",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RII_SH_TASK_ShipmentHeaderId",
                table: "RII_SH_TASK",
                column: "ShipmentHeaderId");

            migrationBuilder.CreateIndex(
                name: "IX_RII_SH_TASK_ASSIGNMENT_IsDeleted",
                table: "RII_SH_TASK_ASSIGNMENT",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RII_SH_TASK_ASSIGNMENT_ShipmentTaskId_UserId",
                table: "RII_SH_TASK_ASSIGNMENT",
                columns: new[] { "ShipmentTaskId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RII_SH_TASK_LINE_IsDeleted",
                table: "RII_SH_TASK_LINE",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RII_SH_TASK_LINE_ShipmentLineId",
                table: "RII_SH_TASK_LINE",
                column: "ShipmentLineId");

            migrationBuilder.CreateIndex(
                name: "IX_RII_SH_TASK_LINE_ShipmentTaskId_ShipmentLineId",
                table: "RII_SH_TASK_LINE",
                columns: new[] { "ShipmentTaskId", "ShipmentLineId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RII_SH_TRACKING_IsDeleted",
                table: "RII_SH_TRACKING",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RII_SH_TRACKING_ShipmentLineId_SerialNo",
                table: "RII_SH_TRACKING",
                columns: new[] { "ShipmentLineId", "SerialNo" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RII_SH_LINE_SOURCE");

            migrationBuilder.DropTable(
                name: "RII_SH_POLICIES");

            migrationBuilder.DropTable(
                name: "RII_SH_STATUS_HISTORY");

            migrationBuilder.DropTable(
                name: "RII_SH_TASK_ASSIGNMENT");

            migrationBuilder.DropTable(
                name: "RII_SH_TASK_LINE");

            migrationBuilder.DropTable(
                name: "RII_SH_TRACKING");

            migrationBuilder.DropTable(
                name: "RII_SH_SOURCE_DOCUMENT");

            migrationBuilder.DropTable(
                name: "RII_SH_TASK");

            migrationBuilder.DropTable(
                name: "RII_SH_LINE");

            migrationBuilder.DropTable(
                name: "RII_SH_HEADER");

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_GROUP_PERMISSIONS",
                keyColumn: "Id",
                keyValue: 2000L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_GROUP_PERMISSIONS",
                keyColumn: "Id",
                keyValue: 2001L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_GROUP_PERMISSIONS",
                keyColumn: "Id",
                keyValue: 2002L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_GROUP_PERMISSIONS",
                keyColumn: "Id",
                keyValue: 2003L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_GROUP_PERMISSIONS",
                keyColumn: "Id",
                keyValue: 2004L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_GROUP_PERMISSIONS",
                keyColumn: "Id",
                keyValue: 2005L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_DEFINITIONS",
                keyColumn: "Id",
                keyValue: 2000L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_DEFINITIONS",
                keyColumn: "Id",
                keyValue: 2001L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_DEFINITIONS",
                keyColumn: "Id",
                keyValue: 2002L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_DEFINITIONS",
                keyColumn: "Id",
                keyValue: 2003L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_DEFINITIONS",
                keyColumn: "Id",
                keyValue: 2004L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_DEFINITIONS",
                keyColumn: "Id",
                keyValue: 2005L);
        }
    }
}
