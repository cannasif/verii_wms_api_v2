using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace verii_wms_api_v2.Migrations
{
    /// <inheritdoc />
    public partial class AddGoodsReceiptWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "InitiationMode",
                table: "RII_GR_HEADER",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "OrderBasedTask");

            migrationBuilder.AddColumn<string>(
                name: "LabelStrategy",
                table: "RII_GR_HEADER",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "None");

            migrationBuilder.CreateTable(
                name: "RII_GR_LABEL_BATCH",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    GrHeaderId = table.Column<long>(type: "bigint", nullable: false),
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
                    table.PrimaryKey("PK_RII_GR_LABEL_BATCH", x => x.Id);
                    table.CheckConstraint("CK_RII_GR_LABEL_BATCH_COUNTS", "[TotalLabelCount] >= 0 AND [PrintedLabelCount] >= 0 AND [ConsumedLabelCount] >= 0 AND [VoidLabelCount] >= 0 AND [PrintedLabelCount] <= [TotalLabelCount] AND [ConsumedLabelCount] + [VoidLabelCount] <= [TotalLabelCount]");
                    table.ForeignKey(
                        name: "FK_RII_GR_LABEL_BATCH_RII_GR_HEADER_GrHeaderId",
                        column: x => x.GrHeaderId,
                        principalTable: "RII_GR_HEADER",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RII_GR_TASK",
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
                    table.PrimaryKey("PK_RII_GR_TASK", x => x.Id);
                    table.CheckConstraint("CK_RII_GR_TASK_PRIORITY", "[Priority] BETWEEN 1 AND 5");
                    table.ForeignKey(
                        name: "FK_RII_GR_TASK_RII_GR_HEADER_GrHeaderId",
                        column: x => x.GrHeaderId,
                        principalTable: "RII_GR_HEADER",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RII_GR_TASK_RII_WAREHOUSE_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "RII_WAREHOUSE",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RII_GR_TASK_ASSIGNMENT",
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
                    table.PrimaryKey("PK_RII_GR_TASK_ASSIGNMENT", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RII_GR_TASK_ASSIGNMENT_RII_GR_TASK_GrTaskId",
                        column: x => x.GrTaskId,
                        principalTable: "RII_GR_TASK",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RII_GR_TASK_ASSIGNMENT_RII_USERS_UserId",
                        column: x => x.UserId,
                        principalTable: "RII_USERS",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RII_GR_TASK_LINE",
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
                    table.PrimaryKey("PK_RII_GR_TASK_LINE", x => x.Id);
                    table.CheckConstraint("CK_RII_GR_TASK_LINE_QUANTITY", "[PlannedQuantity] > 0 AND [ProcessedQuantity] >= 0 AND [ProcessedQuantity] <= [PlannedQuantity]");
                    table.CheckConstraint("CK_RII_GR_TASK_LINE_SEQUENCE", "[SequenceNo] > 0");
                    table.ForeignKey(
                        name: "FK_RII_GR_TASK_LINE_RII_GR_LINE_GrLineId",
                        column: x => x.GrLineId,
                        principalTable: "RII_GR_LINE",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RII_GR_TASK_LINE_RII_GR_TASK_GrTaskId",
                        column: x => x.GrTaskId,
                        principalTable: "RII_GR_TASK",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RII_GR_TASK_LINE_RII_LOCATION_FromLocationId",
                        column: x => x.FromLocationId,
                        principalTable: "RII_LOCATION",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RII_GR_TASK_LINE_RII_LOCATION_ToLocationId",
                        column: x => x.ToLocationId,
                        principalTable: "RII_LOCATION",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RII_GR_LABEL",
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
                    table.PrimaryKey("PK_RII_GR_LABEL", x => x.Id);
                    table.CheckConstraint("CK_RII_GR_LABEL_PRINT_COUNT", "[PrintCount] >= 0");
                    table.CheckConstraint("CK_RII_GR_LABEL_QUANTITY", "[LabelQuantity] > 0");
                    table.ForeignKey(
                        name: "FK_RII_GR_LABEL_RII_GR_HEADER_GrHeaderId",
                        column: x => x.GrHeaderId,
                        principalTable: "RII_GR_HEADER",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RII_GR_LABEL_RII_GR_LABEL_BATCH_BatchId",
                        column: x => x.BatchId,
                        principalTable: "RII_GR_LABEL_BATCH",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RII_GR_LABEL_RII_GR_LINE_GrLineId",
                        column: x => x.GrLineId,
                        principalTable: "RII_GR_LINE",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RII_GR_LABEL_RII_GR_TASK_LINE_GrTaskLineId",
                        column: x => x.GrTaskLineId,
                        principalTable: "RII_GR_TASK_LINE",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RII_GR_LABEL_RII_STOCK_StockId",
                        column: x => x.StockId,
                        principalTable: "RII_STOCK",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RII_GR_LABEL_RII_YAP_CODE_YapCodeId",
                        column: x => x.YapCodeId,
                        principalTable: "RII_YAP_CODE",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RII_GR_LABEL_BATCH_STATUS",
                table: "RII_GR_LABEL",
                columns: new[] { "BatchId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_RII_GR_LABEL_GrLineId",
                table: "RII_GR_LABEL",
                column: "GrLineId");

            migrationBuilder.CreateIndex(
                name: "IX_RII_GR_LABEL_GrTaskLineId",
                table: "RII_GR_LABEL",
                column: "GrTaskLineId");

            migrationBuilder.CreateIndex(
                name: "IX_RII_GR_LABEL_HEADER_LINE",
                table: "RII_GR_LABEL",
                columns: new[] { "GrHeaderId", "GrLineId" });

            migrationBuilder.CreateIndex(
                name: "IX_RII_GR_LABEL_IsDeleted",
                table: "RII_GR_LABEL",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RII_GR_LABEL_TRACE",
                table: "RII_GR_LABEL",
                columns: new[] { "StockId", "LotNo", "SerialNo" });

            migrationBuilder.CreateIndex(
                name: "IX_RII_GR_LABEL_YapCodeId",
                table: "RII_GR_LABEL",
                column: "YapCodeId");

            migrationBuilder.CreateIndex(
                name: "UX_RII_GR_LABEL_BARCODE",
                table: "RII_GR_LABEL",
                column: "BarcodeValue",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RII_GR_LABEL_BATCH_HEADER_STATUS",
                table: "RII_GR_LABEL_BATCH",
                columns: new[] { "GrHeaderId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_RII_GR_LABEL_BATCH_IsDeleted",
                table: "RII_GR_LABEL_BATCH",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "UX_RII_GR_LABEL_BATCH_BRANCH_BATCH_NO",
                table: "RII_GR_LABEL_BATCH",
                columns: new[] { "BranchCode", "BatchNo" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_RII_GR_TASK_HEADER_TYPE_STATUS",
                table: "RII_GR_TASK",
                columns: new[] { "GrHeaderId", "TaskType", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_RII_GR_TASK_IsDeleted",
                table: "RII_GR_TASK",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RII_GR_TASK_WORK_QUEUE",
                table: "RII_GR_TASK",
                columns: new[] { "WarehouseId", "Status", "Priority", "DueAtUtc" });

            migrationBuilder.CreateIndex(
                name: "UX_RII_GR_TASK_BRANCH_TASK_NO",
                table: "RII_GR_TASK",
                columns: new[] { "BranchCode", "TaskNo" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_RII_GR_TASK_ASSIGNMENT_IsDeleted",
                table: "RII_GR_TASK_ASSIGNMENT",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RII_GR_TASK_ASSIGNMENT_USER_QUEUE",
                table: "RII_GR_TASK_ASSIGNMENT",
                columns: new[] { "UserId", "Status", "AssignedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "UX_RII_GR_TASK_ASSIGNMENT_ACTIVE_USER",
                table: "RII_GR_TASK_ASSIGNMENT",
                columns: new[] { "GrTaskId", "UserId" },
                unique: true,
                filter: "[IsDeleted] = 0 AND [Status] <> N'Unassigned'");

            migrationBuilder.CreateIndex(
                name: "IX_RII_GR_TASK_LINE_FromLocationId",
                table: "RII_GR_TASK_LINE",
                column: "FromLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_RII_GR_TASK_LINE_GR_LINE_STATUS",
                table: "RII_GR_TASK_LINE",
                columns: new[] { "GrLineId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_RII_GR_TASK_LINE_IsDeleted",
                table: "RII_GR_TASK_LINE",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RII_GR_TASK_LINE_ToLocationId",
                table: "RII_GR_TASK_LINE",
                column: "ToLocationId");

            migrationBuilder.CreateIndex(
                name: "UX_RII_GR_TASK_LINE_TASK_SEQUENCE",
                table: "RII_GR_TASK_LINE",
                columns: new[] { "GrTaskId", "SequenceNo" },
                unique: true,
                filter: "[IsDeleted] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RII_GR_LABEL");

            migrationBuilder.DropTable(
                name: "RII_GR_TASK_ASSIGNMENT");

            migrationBuilder.DropTable(
                name: "RII_GR_LABEL_BATCH");

            migrationBuilder.DropTable(
                name: "RII_GR_TASK_LINE");

            migrationBuilder.DropTable(
                name: "RII_GR_TASK");

            migrationBuilder.DropColumn(
                name: "InitiationMode",
                table: "RII_GR_HEADER");

            migrationBuilder.DropColumn(
                name: "LabelStrategy",
                table: "RII_GR_HEADER");
        }
    }
}
