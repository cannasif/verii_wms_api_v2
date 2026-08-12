using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace verii_wms_api_v2.Migrations
{
    /// <inheritdoc />
    public partial class AddInventoryCountV2Foundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RII_INVENTORY_COUNT_HEADER",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CountCode = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DocumentSeriesId = table.Column<long>(type: "bigint", nullable: true),
                    DocumentNo = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    CountType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    CountMode = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    MovementPolicy = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    WarehouseId = table.Column<long>(type: "bigint", nullable: false),
                    Priority = table.Column<int>(type: "int", nullable: false),
                    PlannedStartUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PlannedEndUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    QuantityTolerance = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    PercentageTolerance = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: false),
                    MaxCountAttempts = table.Column<int>(type: "int", nullable: false),
                    RequireIndependentRecount = table.Column<bool>(type: "bit", nullable: false),
                    AllowUnexpectedStock = table.Column<bool>(type: "bit", nullable: false),
                    AutoApproveWithinTolerance = table.Column<bool>(type: "bit", nullable: false),
                    IncludeEmptyLocations = table.Column<bool>(type: "bit", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    SnapshotMovementEntryId = table.Column<long>(type: "bigint", nullable: true),
                    ReleaseIdempotencyKey = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: true),
                    SnapshotAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReleasedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReleasedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    StartedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CompletedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    TaskCount = table.Column<int>(type: "int", nullable: false),
                    CompletedTaskCount = table.Column<int>(type: "int", nullable: false),
                    LineCount = table.Column<int>(type: "int", nullable: false),
                    CountedLineCount = table.Column<int>(type: "int", nullable: false),
                    VarianceLineCount = table.Column<int>(type: "int", nullable: false),
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
                    table.PrimaryKey("PK_RII_INVENTORY_COUNT_HEADER", x => x.Id);
                    table.CheckConstraint("CK_RII_IC_HEADER_ATTEMPTS", "[MaxCountAttempts] BETWEEN 1 AND 10");
                    table.CheckConstraint("CK_RII_IC_HEADER_PRIORITY", "[Priority] BETWEEN 1 AND 5");
                    table.CheckConstraint("CK_RII_IC_HEADER_TOLERANCE", "[QuantityTolerance] >= 0 AND [PercentageTolerance] >= 0");
                    table.ForeignKey(
                        name: "FK_RII_INVENTORY_COUNT_HEADER_RII_DOCUMENT_SERIES_DocumentSeriesId",
                        column: x => x.DocumentSeriesId,
                        principalTable: "RII_DOCUMENT_SERIES",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RII_INVENTORY_COUNT_HEADER_RII_WAREHOUSE_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "RII_WAREHOUSE",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RII_INVENTORY_COUNT_POLICY",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    WarehouseId = table.Column<long>(type: "bigint", nullable: true),
                    DefaultCountMode = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    DefaultMovementPolicy = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    QuantityTolerance = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    PercentageTolerance = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: false),
                    MaxCountAttempts = table.Column<int>(type: "int", nullable: false),
                    RequireIndependentRecount = table.Column<bool>(type: "bit", nullable: false),
                    AllowUnexpectedStock = table.Column<bool>(type: "bit", nullable: false),
                    AutoApproveWithinTolerance = table.Column<bool>(type: "bit", nullable: false),
                    RequireDifferenceReason = table.Column<bool>(type: "bit", nullable: false),
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
                    table.PrimaryKey("PK_RII_INVENTORY_COUNT_POLICY", x => x.Id);
                    table.CheckConstraint("CK_RII_IC_POLICY_ATTEMPTS", "[MaxCountAttempts] BETWEEN 1 AND 10");
                    table.CheckConstraint("CK_RII_IC_POLICY_TOLERANCE", "[QuantityTolerance] >= 0 AND [PercentageTolerance] >= 0");
                    table.ForeignKey(
                        name: "FK_RII_INVENTORY_COUNT_POLICY_RII_WAREHOUSE_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "RII_WAREHOUSE",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RII_INVENTORY_COUNT_SCOPE",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    HeaderId = table.Column<long>(type: "bigint", nullable: false),
                    SequenceNo = table.Column<int>(type: "int", nullable: false),
                    LocationId = table.Column<long>(type: "bigint", nullable: true),
                    StockId = table.Column<long>(type: "bigint", nullable: true),
                    YapCodeId = table.Column<long>(type: "bigint", nullable: true),
                    StockGroupCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    IncludeDescendantLocations = table.Column<bool>(type: "bit", nullable: false),
                    IncludeEmptyLocations = table.Column<bool>(type: "bit", nullable: false),
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
                    table.PrimaryKey("PK_RII_INVENTORY_COUNT_SCOPE", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RII_INVENTORY_COUNT_SCOPE_RII_INVENTORY_COUNT_HEADER_HeaderId",
                        column: x => x.HeaderId,
                        principalTable: "RII_INVENTORY_COUNT_HEADER",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RII_INVENTORY_COUNT_SCOPE_RII_LOCATION_LocationId",
                        column: x => x.LocationId,
                        principalTable: "RII_LOCATION",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RII_INVENTORY_COUNT_SCOPE_RII_STOCK_StockId",
                        column: x => x.StockId,
                        principalTable: "RII_STOCK",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RII_INVENTORY_COUNT_SCOPE_RII_YAP_CODE_YapCodeId",
                        column: x => x.YapCodeId,
                        principalTable: "RII_YAP_CODE",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RII_INVENTORY_COUNT_TASK",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    HeaderId = table.Column<long>(type: "bigint", nullable: false),
                    TaskCode = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TaskNo = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    WarehouseId = table.Column<long>(type: "bigint", nullable: false),
                    LocationId = table.Column<long>(type: "bigint", nullable: false),
                    RouteSequence = table.Column<int>(type: "int", nullable: false),
                    CountRound = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    AssignedUserId = table.Column<long>(type: "bigint", nullable: true),
                    AssignedTeamId = table.Column<long>(type: "bigint", nullable: true),
                    PreviousTaskId = table.Column<long>(type: "bigint", nullable: true),
                    AssignedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AssignedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    StartedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CompletedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    LocationBarcodeConfirmed = table.Column<bool>(type: "bit", nullable: false),
                    LocationConfirmedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LineCount = table.Column<int>(type: "int", nullable: false),
                    CountedLineCount = table.Column<int>(type: "int", nullable: false),
                    VarianceLineCount = table.Column<int>(type: "int", nullable: false),
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
                    table.PrimaryKey("PK_RII_INVENTORY_COUNT_TASK", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RII_INVENTORY_COUNT_TASK_RII_INVENTORY_COUNT_HEADER_HeaderId",
                        column: x => x.HeaderId,
                        principalTable: "RII_INVENTORY_COUNT_HEADER",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RII_INVENTORY_COUNT_TASK_RII_INVENTORY_COUNT_TASK_PreviousTaskId",
                        column: x => x.PreviousTaskId,
                        principalTable: "RII_INVENTORY_COUNT_TASK",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RII_INVENTORY_COUNT_TASK_RII_LOCATION_LocationId",
                        column: x => x.LocationId,
                        principalTable: "RII_LOCATION",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RII_INVENTORY_COUNT_TASK_RII_WAREHOUSE_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "RII_WAREHOUSE",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RII_INVENTORY_COUNT_LINE",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    HeaderId = table.Column<long>(type: "bigint", nullable: false),
                    TaskId = table.Column<long>(type: "bigint", nullable: false),
                    SequenceNo = table.Column<int>(type: "int", nullable: false),
                    CountRound = table.Column<int>(type: "int", nullable: false),
                    WarehouseId = table.Column<long>(type: "bigint", nullable: false),
                    LocationId = table.Column<long>(type: "bigint", nullable: false),
                    StockId = table.Column<long>(type: "bigint", nullable: false),
                    YapCodeId = table.Column<long>(type: "bigint", nullable: true),
                    UnitCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    LotNo = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    SerialNo = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    StockStatus = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    SnapshotQuantity = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    CountedQuantity = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    VarianceQuantity = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    VariancePercentage = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: false),
                    IsUnexpectedStock = table.Column<bool>(type: "bit", nullable: false),
                    IsZeroConfirmed = table.Column<bool>(type: "bit", nullable: false),
                    IsWithinTolerance = table.Column<bool>(type: "bit", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    FirstCountedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastCountedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastCountedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    DifferenceReasonCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ReviewNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
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
                    table.PrimaryKey("PK_RII_INVENTORY_COUNT_LINE", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RII_INVENTORY_COUNT_LINE_RII_INVENTORY_COUNT_HEADER_HeaderId",
                        column: x => x.HeaderId,
                        principalTable: "RII_INVENTORY_COUNT_HEADER",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_RII_INVENTORY_COUNT_LINE_RII_INVENTORY_COUNT_TASK_TaskId",
                        column: x => x.TaskId,
                        principalTable: "RII_INVENTORY_COUNT_TASK",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RII_INVENTORY_COUNT_LINE_RII_LOCATION_LocationId",
                        column: x => x.LocationId,
                        principalTable: "RII_LOCATION",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RII_INVENTORY_COUNT_LINE_RII_STOCK_StockId",
                        column: x => x.StockId,
                        principalTable: "RII_STOCK",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RII_INVENTORY_COUNT_LINE_RII_WAREHOUSE_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "RII_WAREHOUSE",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RII_INVENTORY_COUNT_LINE_RII_YAP_CODE_YapCodeId",
                        column: x => x.YapCodeId,
                        principalTable: "RII_YAP_CODE",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RII_INVENTORY_COUNT_ADJUSTMENT",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    HeaderId = table.Column<long>(type: "bigint", nullable: false),
                    LineId = table.Column<long>(type: "bigint", nullable: false),
                    StockMovementOperationId = table.Column<long>(type: "bigint", nullable: false),
                    QuantityDelta = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    PostedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PostedByUserId = table.Column<long>(type: "bigint", nullable: false),
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
                    table.PrimaryKey("PK_RII_INVENTORY_COUNT_ADJUSTMENT", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RII_INVENTORY_COUNT_ADJUSTMENT_RII_INVENTORY_COUNT_HEADER_HeaderId",
                        column: x => x.HeaderId,
                        principalTable: "RII_INVENTORY_COUNT_HEADER",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RII_INVENTORY_COUNT_ADJUSTMENT_RII_INVENTORY_COUNT_LINE_LineId",
                        column: x => x.LineId,
                        principalTable: "RII_INVENTORY_COUNT_LINE",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RII_INVENTORY_COUNT_ADJUSTMENT_RII_STOCK_MOVEMENT_OPERATION_StockMovementOperationId",
                        column: x => x.StockMovementOperationId,
                        principalTable: "RII_STOCK_MOVEMENT_OPERATION",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RII_INVENTORY_COUNT_ENTRY",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    HeaderId = table.Column<long>(type: "bigint", nullable: false),
                    TaskId = table.Column<long>(type: "bigint", nullable: false),
                    LineId = table.Column<long>(type: "bigint", nullable: false),
                    IdempotencyKey = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CountRound = table.Column<int>(type: "int", nullable: false),
                    EntryType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    Barcode = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    DeviceCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    SessionCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    EnteredByUserId = table.Column<long>(type: "bigint", nullable: false),
                    EnteredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
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
                    table.PrimaryKey("PK_RII_INVENTORY_COUNT_ENTRY", x => x.Id);
                    table.CheckConstraint("CK_RII_IC_ENTRY_QUANTITY", "[Quantity] >= 0");
                    table.ForeignKey(
                        name: "FK_RII_INVENTORY_COUNT_ENTRY_RII_INVENTORY_COUNT_HEADER_HeaderId",
                        column: x => x.HeaderId,
                        principalTable: "RII_INVENTORY_COUNT_HEADER",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_RII_INVENTORY_COUNT_ENTRY_RII_INVENTORY_COUNT_LINE_LineId",
                        column: x => x.LineId,
                        principalTable: "RII_INVENTORY_COUNT_LINE",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RII_INVENTORY_COUNT_ENTRY_RII_INVENTORY_COUNT_TASK_TaskId",
                        column: x => x.TaskId,
                        principalTable: "RII_INVENTORY_COUNT_TASK",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "RII_INVENTORY_COUNT_REVIEW",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    HeaderId = table.Column<long>(type: "bigint", nullable: false),
                    TaskId = table.Column<long>(type: "bigint", nullable: true),
                    LineId = table.Column<long>(type: "bigint", nullable: true),
                    Action = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    PreviousQuantity = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: true),
                    ApprovedQuantity = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: true),
                    ReasonCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ReviewedByUserId = table.Column<long>(type: "bigint", nullable: false),
                    ReviewedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
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
                    table.PrimaryKey("PK_RII_INVENTORY_COUNT_REVIEW", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RII_INVENTORY_COUNT_REVIEW_RII_INVENTORY_COUNT_HEADER_HeaderId",
                        column: x => x.HeaderId,
                        principalTable: "RII_INVENTORY_COUNT_HEADER",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RII_INVENTORY_COUNT_REVIEW_RII_INVENTORY_COUNT_LINE_LineId",
                        column: x => x.LineId,
                        principalTable: "RII_INVENTORY_COUNT_LINE",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RII_INVENTORY_COUNT_REVIEW_RII_INVENTORY_COUNT_TASK_TaskId",
                        column: x => x.TaskId,
                        principalTable: "RII_INVENTORY_COUNT_TASK",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RII_INVENTORY_COUNT_SCAN_EVENT",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    HeaderId = table.Column<long>(type: "bigint", nullable: false),
                    TaskId = table.Column<long>(type: "bigint", nullable: false),
                    LineId = table.Column<long>(type: "bigint", nullable: true),
                    IdempotencyKey = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Barcode = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    IsAccepted = table.Column<bool>(type: "bit", nullable: false),
                    ResultCode = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    ResultDetail = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    DeviceCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ScannedByUserId = table.Column<long>(type: "bigint", nullable: false),
                    ScannedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
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
                    table.PrimaryKey("PK_RII_INVENTORY_COUNT_SCAN_EVENT", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RII_INVENTORY_COUNT_SCAN_EVENT_RII_INVENTORY_COUNT_HEADER_HeaderId",
                        column: x => x.HeaderId,
                        principalTable: "RII_INVENTORY_COUNT_HEADER",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RII_INVENTORY_COUNT_SCAN_EVENT_RII_INVENTORY_COUNT_LINE_LineId",
                        column: x => x.LineId,
                        principalTable: "RII_INVENTORY_COUNT_LINE",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RII_INVENTORY_COUNT_SCAN_EVENT_RII_INVENTORY_COUNT_TASK_TaskId",
                        column: x => x.TaskId,
                        principalTable: "RII_INVENTORY_COUNT_TASK",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "RII_PERMISSION_DEFINITIONS",
                columns: new[] { "Id", "AvailableOnMobile", "AvailableOnWeb", "BranchCode", "Code", "CreatedBy", "CreatedDate", "DeletedBy", "DeletedDate", "Description", "IsActive", "Name", "UpdatedBy", "UpdatedDate" },
                values: new object[,]
                {
                    { 2800L, true, true, "0", "WMS.INVENTORY_COUNT.VIEW", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, true, "Sayım emirlerini görüntüle", null, null },
                    { 2801L, false, true, "0", "WMS.INVENTORY_COUNT.CREATE", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, true, "Sayım emri oluştur", null, null },
                    { 2802L, false, true, "0", "WMS.INVENTORY_COUNT.UPDATE", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, true, "Sayım taslağını güncelle veya sil", null, null },
                    { 2803L, false, true, "0", "WMS.INVENTORY_COUNT.RELEASE", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, true, "Sayım emrini serbest bırak", null, null },
                    { 2804L, false, true, "0", "WMS.INVENTORY_COUNT.ASSIGN", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, true, "Sayım görevlerini ata", null, null },
                    { 2805L, true, true, "0", "WMS.INVENTORY_COUNT.COUNT", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, true, "Fiziksel sayım yap", null, null },
                    { 2806L, false, true, "0", "WMS.INVENTORY_COUNT.REVIEW", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, true, "Sayım farklarını ve defter miktarını incele", null, null },
                    { 2807L, false, true, "0", "WMS.INVENTORY_COUNT.APPROVE", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, true, "Sayım farkını onayla veya yeniden sayıma gönder", null, null },
                    { 2808L, false, true, "0", "WMS.INVENTORY_COUNT.POST", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, true, "Onaylanan sayım farkını stok hareketine işle", null, null },
                    { 2809L, false, true, "0", "WMS.INVENTORY_COUNT.CANCEL", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, true, "Sayım emrini güvenli biçimde iptal et", null, null },
                    { 2810L, false, true, "0", "WMS.INVENTORY_COUNT.POLICY.VIEW", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, true, "Sayım politikalarını görüntüle", null, null },
                    { 2811L, false, true, "0", "WMS.INVENTORY_COUNT.POLICY.MANAGE", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, true, "Sayım politikalarını yönet", null, null }
                });

            // Permission-group mapping identifiers are operational identity values and can
            // already be occupied in long-lived databases. Seed by the business key and let
            // SQL Server allocate the primary key so the migration remains idempotent.
            migrationBuilder.Sql("""
                INSERT INTO [RII_PERMISSION_GROUP_PERMISSIONS]
                    ([BranchCode], [CreatedDate], [PermissionDefinitionId], [PermissionGroupId])
                SELECT N'0', '2026-07-21T00:00:00.0000000Z', permission.[Id], CAST(1001 AS bigint)
                FROM [RII_PERMISSION_DEFINITIONS] permission
                WHERE permission.[Id] BETWEEN 2800 AND 2811
                  AND NOT EXISTS
                  (
                      SELECT 1
                      FROM [RII_PERMISSION_GROUP_PERMISSIONS] mapping
                      WHERE mapping.[PermissionGroupId] = 1001
                        AND mapping.[PermissionDefinitionId] = permission.[Id]
                        AND mapping.[DeletedDate] IS NULL
                  );
                """);

            migrationBuilder.CreateIndex(
                name: "IX_RII_INVENTORY_COUNT_ADJUSTMENT_HeaderId",
                table: "RII_INVENTORY_COUNT_ADJUSTMENT",
                column: "HeaderId");

            migrationBuilder.CreateIndex(
                name: "IX_RII_INVENTORY_COUNT_ADJUSTMENT_IsDeleted",
                table: "RII_INVENTORY_COUNT_ADJUSTMENT",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RII_INVENTORY_COUNT_ADJUSTMENT_StockMovementOperationId",
                table: "RII_INVENTORY_COUNT_ADJUSTMENT",
                column: "StockMovementOperationId");

            migrationBuilder.CreateIndex(
                name: "UX_RII_IC_ADJUSTMENT_LINE_OPERATION",
                table: "RII_INVENTORY_COUNT_ADJUSTMENT",
                columns: new[] { "LineId", "StockMovementOperationId" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_RII_IC_ENTRY_TASK_ROUND_TIME",
                table: "RII_INVENTORY_COUNT_ENTRY",
                columns: new[] { "TaskId", "CountRound", "EnteredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_RII_INVENTORY_COUNT_ENTRY_HeaderId",
                table: "RII_INVENTORY_COUNT_ENTRY",
                column: "HeaderId");

            migrationBuilder.CreateIndex(
                name: "IX_RII_INVENTORY_COUNT_ENTRY_IsDeleted",
                table: "RII_INVENTORY_COUNT_ENTRY",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RII_INVENTORY_COUNT_ENTRY_LineId",
                table: "RII_INVENTORY_COUNT_ENTRY",
                column: "LineId");

            migrationBuilder.CreateIndex(
                name: "UX_RII_IC_ENTRY_IDEMPOTENCY",
                table: "RII_INVENTORY_COUNT_ENTRY",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RII_IC_HEADER_WAREHOUSE_STATUS_PLAN",
                table: "RII_INVENTORY_COUNT_HEADER",
                columns: new[] { "WarehouseId", "Status", "PlannedStartUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_RII_INVENTORY_COUNT_HEADER_DocumentSeriesId",
                table: "RII_INVENTORY_COUNT_HEADER",
                column: "DocumentSeriesId");

            migrationBuilder.CreateIndex(
                name: "IX_RII_INVENTORY_COUNT_HEADER_IsDeleted",
                table: "RII_INVENTORY_COUNT_HEADER",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "UX_RII_IC_HEADER_BRANCH_DOCUMENT",
                table: "RII_INVENTORY_COUNT_HEADER",
                columns: new[] { "BranchCode", "DocumentNo" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "UX_RII_IC_HEADER_COUNT_CODE",
                table: "RII_INVENTORY_COUNT_HEADER",
                column: "CountCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_RII_IC_HEADER_RELEASE_IDEMPOTENCY",
                table: "RII_INVENTORY_COUNT_HEADER",
                column: "ReleaseIdempotencyKey",
                unique: true,
                filter: "[ReleaseIdempotencyKey] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_RII_IC_LINE_DIMENSION",
                table: "RII_INVENTORY_COUNT_LINE",
                columns: new[] { "HeaderId", "LocationId", "StockId", "YapCodeId", "UnitCode", "StockStatus" });

            migrationBuilder.CreateIndex(
                name: "IX_RII_INVENTORY_COUNT_LINE_IsDeleted",
                table: "RII_INVENTORY_COUNT_LINE",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RII_INVENTORY_COUNT_LINE_LocationId",
                table: "RII_INVENTORY_COUNT_LINE",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_RII_INVENTORY_COUNT_LINE_StockId",
                table: "RII_INVENTORY_COUNT_LINE",
                column: "StockId");

            migrationBuilder.CreateIndex(
                name: "IX_RII_INVENTORY_COUNT_LINE_WarehouseId",
                table: "RII_INVENTORY_COUNT_LINE",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_RII_INVENTORY_COUNT_LINE_YapCodeId",
                table: "RII_INVENTORY_COUNT_LINE",
                column: "YapCodeId");

            migrationBuilder.CreateIndex(
                name: "UX_RII_IC_LINE_TASK_SEQUENCE",
                table: "RII_INVENTORY_COUNT_LINE",
                columns: new[] { "TaskId", "SequenceNo" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_RII_INVENTORY_COUNT_POLICY_IsDeleted",
                table: "RII_INVENTORY_COUNT_POLICY",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RII_INVENTORY_COUNT_POLICY_WarehouseId",
                table: "RII_INVENTORY_COUNT_POLICY",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "UX_RII_IC_POLICY_BRANCH_WAREHOUSE",
                table: "RII_INVENTORY_COUNT_POLICY",
                columns: new[] { "BranchCode", "WarehouseId" },
                unique: true,
                filter: "[IsDeleted] = 0 AND [IsActive] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_RII_IC_REVIEW_HEADER_TIME",
                table: "RII_INVENTORY_COUNT_REVIEW",
                columns: new[] { "HeaderId", "ReviewedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_RII_INVENTORY_COUNT_REVIEW_IsDeleted",
                table: "RII_INVENTORY_COUNT_REVIEW",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RII_INVENTORY_COUNT_REVIEW_LineId",
                table: "RII_INVENTORY_COUNT_REVIEW",
                column: "LineId");

            migrationBuilder.CreateIndex(
                name: "IX_RII_INVENTORY_COUNT_REVIEW_TaskId",
                table: "RII_INVENTORY_COUNT_REVIEW",
                column: "TaskId");

            migrationBuilder.CreateIndex(
                name: "IX_RII_IC_SCAN_EVENT_TASK_TIME",
                table: "RII_INVENTORY_COUNT_SCAN_EVENT",
                columns: new[] { "TaskId", "ScannedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_RII_INVENTORY_COUNT_SCAN_EVENT_HeaderId",
                table: "RII_INVENTORY_COUNT_SCAN_EVENT",
                column: "HeaderId");

            migrationBuilder.CreateIndex(
                name: "IX_RII_INVENTORY_COUNT_SCAN_EVENT_IsDeleted",
                table: "RII_INVENTORY_COUNT_SCAN_EVENT",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RII_INVENTORY_COUNT_SCAN_EVENT_LineId",
                table: "RII_INVENTORY_COUNT_SCAN_EVENT",
                column: "LineId");

            migrationBuilder.CreateIndex(
                name: "UX_RII_IC_SCAN_EVENT_IDEMPOTENCY",
                table: "RII_INVENTORY_COUNT_SCAN_EVENT",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RII_INVENTORY_COUNT_SCOPE_IsDeleted",
                table: "RII_INVENTORY_COUNT_SCOPE",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RII_INVENTORY_COUNT_SCOPE_LocationId",
                table: "RII_INVENTORY_COUNT_SCOPE",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_RII_INVENTORY_COUNT_SCOPE_StockId",
                table: "RII_INVENTORY_COUNT_SCOPE",
                column: "StockId");

            migrationBuilder.CreateIndex(
                name: "IX_RII_INVENTORY_COUNT_SCOPE_YapCodeId",
                table: "RII_INVENTORY_COUNT_SCOPE",
                column: "YapCodeId");

            migrationBuilder.CreateIndex(
                name: "UX_RII_IC_SCOPE_HEADER_SEQUENCE",
                table: "RII_INVENTORY_COUNT_SCOPE",
                columns: new[] { "HeaderId", "SequenceNo" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_RII_IC_TASK_ASSIGNEE_STATUS_ROUTE",
                table: "RII_INVENTORY_COUNT_TASK",
                columns: new[] { "AssignedUserId", "Status", "RouteSequence" });

            migrationBuilder.CreateIndex(
                name: "IX_RII_IC_TASK_LOCATION_STATUS",
                table: "RII_INVENTORY_COUNT_TASK",
                columns: new[] { "LocationId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_RII_INVENTORY_COUNT_TASK_HeaderId",
                table: "RII_INVENTORY_COUNT_TASK",
                column: "HeaderId");

            migrationBuilder.CreateIndex(
                name: "IX_RII_INVENTORY_COUNT_TASK_IsDeleted",
                table: "RII_INVENTORY_COUNT_TASK",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RII_INVENTORY_COUNT_TASK_PreviousTaskId",
                table: "RII_INVENTORY_COUNT_TASK",
                column: "PreviousTaskId");

            migrationBuilder.CreateIndex(
                name: "IX_RII_INVENTORY_COUNT_TASK_WarehouseId",
                table: "RII_INVENTORY_COUNT_TASK",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "UX_RII_IC_TASK_BRANCH_NO",
                table: "RII_INVENTORY_COUNT_TASK",
                columns: new[] { "BranchCode", "TaskNo" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "UX_RII_IC_TASK_CODE",
                table: "RII_INVENTORY_COUNT_TASK",
                column: "TaskCode",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RII_INVENTORY_COUNT_ADJUSTMENT");

            migrationBuilder.DropTable(
                name: "RII_INVENTORY_COUNT_ENTRY");

            migrationBuilder.DropTable(
                name: "RII_INVENTORY_COUNT_POLICY");

            migrationBuilder.DropTable(
                name: "RII_INVENTORY_COUNT_REVIEW");

            migrationBuilder.DropTable(
                name: "RII_INVENTORY_COUNT_SCAN_EVENT");

            migrationBuilder.DropTable(
                name: "RII_INVENTORY_COUNT_SCOPE");

            migrationBuilder.DropTable(
                name: "RII_INVENTORY_COUNT_LINE");

            migrationBuilder.DropTable(
                name: "RII_INVENTORY_COUNT_TASK");

            migrationBuilder.DropTable(
                name: "RII_INVENTORY_COUNT_HEADER");

            migrationBuilder.Sql("""
                DELETE FROM [RII_PERMISSION_GROUP_PERMISSIONS]
                WHERE [PermissionGroupId] = 1001
                  AND [PermissionDefinitionId] BETWEEN 2800 AND 2811;
                """);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_DEFINITIONS",
                keyColumn: "Id",
                keyValue: 2800L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_DEFINITIONS",
                keyColumn: "Id",
                keyValue: 2801L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_DEFINITIONS",
                keyColumn: "Id",
                keyValue: 2802L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_DEFINITIONS",
                keyColumn: "Id",
                keyValue: 2803L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_DEFINITIONS",
                keyColumn: "Id",
                keyValue: 2804L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_DEFINITIONS",
                keyColumn: "Id",
                keyValue: 2805L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_DEFINITIONS",
                keyColumn: "Id",
                keyValue: 2806L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_DEFINITIONS",
                keyColumn: "Id",
                keyValue: 2807L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_DEFINITIONS",
                keyColumn: "Id",
                keyValue: 2808L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_DEFINITIONS",
                keyColumn: "Id",
                keyValue: 2809L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_DEFINITIONS",
                keyColumn: "Id",
                keyValue: 2810L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_DEFINITIONS",
                keyColumn: "Id",
                keyValue: 2811L);
        }
    }
}
