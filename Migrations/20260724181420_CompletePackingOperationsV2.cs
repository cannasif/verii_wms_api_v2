using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace verii_wms_api_v2.Migrations
{
    /// <inheritdoc />
    public partial class CompletePackingOperationsV2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_RII_WT_TRACKING_QTY",
                table: "RII_WT_TRACKING");

            migrationBuilder.DropCheckConstraint(
                name: "CK_RII_WT_LINE_QTY",
                table: "RII_WT_LINE");

            migrationBuilder.AddColumn<decimal>(
                name: "PackedQuantity",
                table: "RII_WT_TRACKING",
                type: "decimal(20,6)",
                precision: 20,
                scale: 6,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "PackedQuantity",
                table: "RII_WT_LINE",
                type: "decimal(20,6)",
                precision: 20,
                scale: 6,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "RII_PACKING_PRINT_JOB",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    HandlingUnitId = table.Column<long>(type: "bigint", nullable: false),
                    PackingStationId = table.Column<long>(type: "bigint", nullable: false),
                    PrinterDefinitionId = table.Column<long>(type: "bigint", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Copies = table.Column<int>(type: "int", nullable: false),
                    PayloadJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IdempotencyKey = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AttemptCount = table.Column<int>(type: "int", nullable: false),
                    RequestedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ProcessingStartedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    NextAttemptAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LastError = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
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
                    table.PrimaryKey("PK_RII_PACKING_PRINT_JOB", x => x.Id);
                    table.CheckConstraint("CK_RII_PACKING_PRINT_JOB_COPIES", "[Copies] > 0 AND [AttemptCount] >= 0");
                });

            migrationBuilder.CreateTable(
                name: "RII_PACKING_SCALE_READING",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PackingStationId = table.Column<long>(type: "bigint", nullable: false),
                    HandlingUnitId = table.Column<long>(type: "bigint", nullable: true),
                    DeviceCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    GrossWeight = table.Column<decimal>(type: "decimal(20,6)", precision: 20, scale: 6, nullable: false),
                    IsStable = table.Column<bool>(type: "bit", nullable: false),
                    IdempotencyKey = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CapturedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RawPayload = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
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
                    table.PrimaryKey("PK_RII_PACKING_SCALE_READING", x => x.Id);
                    table.CheckConstraint("CK_RII_PACKING_SCALE_READING_WEIGHT", "[GrossWeight] > 0");
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_RII_WT_TRACKING_QTY",
                table: "RII_WT_TRACKING",
                sql: "[PlannedQuantity] > 0 AND [ReservedQuantity] >= 0 AND [PickedQuantity] >= 0 AND [PackedQuantity] >= 0 AND [ShippedQuantity] >= 0 AND [ReceivedQuantity] >= 0 AND [PutawayQuantity] >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_RII_WT_LINE_QTY",
                table: "RII_WT_LINE",
                sql: "[RequestedQuantity] > 0 AND [ReservedQuantity] >= 0 AND [PickedQuantity] >= 0 AND [PackedQuantity] >= 0 AND [ShippedQuantity] >= 0 AND [ReceivedQuantity] >= 0 AND [PutawayQuantity] >= 0 AND [DamagedQuantity] >= 0 AND [LostQuantity] >= 0 AND [ShortClosedQuantity] >= 0");

            migrationBuilder.CreateIndex(
                name: "IX_RII_PACKING_PRINT_JOB_HandlingUnitId",
                table: "RII_PACKING_PRINT_JOB",
                column: "HandlingUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_RII_PACKING_PRINT_JOB_IdempotencyKey",
                table: "RII_PACKING_PRINT_JOB",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RII_PACKING_PRINT_JOB_IsDeleted",
                table: "RII_PACKING_PRINT_JOB",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RII_PACKING_PRINT_JOB_Status_NextAttemptAtUtc_RequestedAtUtc",
                table: "RII_PACKING_PRINT_JOB",
                columns: new[] { "Status", "NextAttemptAtUtc", "RequestedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_RII_PACKING_SCALE_READING_HandlingUnitId_CapturedAtUtc",
                table: "RII_PACKING_SCALE_READING",
                columns: new[] { "HandlingUnitId", "CapturedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_RII_PACKING_SCALE_READING_IdempotencyKey",
                table: "RII_PACKING_SCALE_READING",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RII_PACKING_SCALE_READING_IsDeleted",
                table: "RII_PACKING_SCALE_READING",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RII_PACKING_SCALE_READING_PackingStationId_CapturedAtUtc",
                table: "RII_PACKING_SCALE_READING",
                columns: new[] { "PackingStationId", "CapturedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RII_PACKING_PRINT_JOB");

            migrationBuilder.DropTable(
                name: "RII_PACKING_SCALE_READING");

            migrationBuilder.DropCheckConstraint(
                name: "CK_RII_WT_TRACKING_QTY",
                table: "RII_WT_TRACKING");

            migrationBuilder.DropCheckConstraint(
                name: "CK_RII_WT_LINE_QTY",
                table: "RII_WT_LINE");

            migrationBuilder.DropColumn(
                name: "PackedQuantity",
                table: "RII_WT_TRACKING");

            migrationBuilder.DropColumn(
                name: "PackedQuantity",
                table: "RII_WT_LINE");

            migrationBuilder.AddCheckConstraint(
                name: "CK_RII_WT_TRACKING_QTY",
                table: "RII_WT_TRACKING",
                sql: "[PlannedQuantity] > 0 AND [ReservedQuantity] >= 0 AND [PickedQuantity] >= 0 AND [ShippedQuantity] >= 0 AND [ReceivedQuantity] >= 0 AND [PutawayQuantity] >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_RII_WT_LINE_QTY",
                table: "RII_WT_LINE",
                sql: "[RequestedQuantity] > 0 AND [ReservedQuantity] >= 0 AND [PickedQuantity] >= 0 AND [ShippedQuantity] >= 0 AND [ReceivedQuantity] >= 0 AND [PutawayQuantity] >= 0 AND [DamagedQuantity] >= 0 AND [LostQuantity] >= 0 AND [ShortClosedQuantity] >= 0");
        }
    }
}
