using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace verii_wms_api_v2.Migrations
{
    /// <inheritdoc />
    public partial class AddStockBasedAutomaticSerialLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AutoGenerateSerials",
                table: "RII_STOCK_TRACKING_POLICIES",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<long>(
                name: "NextSequence",
                table: "RII_SERIAL_NUMBER_RULES",
                type: "bigint",
                nullable: false,
                defaultValue: 1L);

            migrationBuilder.CreateTable(
                name: "RII_STOCK_SERIAL_REGISTRY",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StockId = table.Column<long>(type: "bigint", nullable: false),
                    SerialNo = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    NormalizedSerialNo = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    SerialNumberRuleId = table.Column<long>(type: "bigint", nullable: true),
                    SequenceNumber = table.Column<long>(type: "bigint", nullable: false),
                    GenerationRequestKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    GenerationOrdinal = table.Column<int>(type: "int", nullable: false),
                    SourceOperationType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    SourceOperationId = table.Column<long>(type: "bigint", nullable: true),
                    ReservedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ActivatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ConsumedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    VoidedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    VoidedReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    LastStockMovementOperationId = table.Column<long>(type: "bigint", nullable: true),
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
                    table.PrimaryKey("PK_RII_STOCK_SERIAL_REGISTRY", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RII_STOCK_SERIAL_REGISTRY_RII_SERIAL_NUMBER_RULES_SerialNumberRuleId",
                        column: x => x.SerialNumberRuleId,
                        principalTable: "RII_SERIAL_NUMBER_RULES",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RII_STOCK_SERIAL_REGISTRY_RII_STOCK_MOVEMENT_OPERATION_LastStockMovementOperationId",
                        column: x => x.LastStockMovementOperationId,
                        principalTable: "RII_STOCK_MOVEMENT_OPERATION",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RII_STOCK_SERIAL_REGISTRY_RII_STOCK_StockId",
                        column: x => x.StockId,
                        principalTable: "RII_STOCK",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.Sql(
                """
                ;WITH AggregatedSerials AS
                (
                    SELECT
                        StockId,
                        UPPER(LTRIM(RTRIM(SerialNo))) AS NormalizedSerialNo,
                        MAX(LTRIM(RTRIM(SerialNo))) AS SerialNo,
                        MAX(BranchCode) AS BranchCode,
                        SUM(QuantityDelta) AS CurrentQuantity,
                        MIN(OccurredAt) AS FirstSeenAtUtc,
                        MAX(OccurredAt) AS LastSeenAtUtc,
                        MAX(OperationId) AS LastOperationId
                    FROM dbo.RII_STOCK_MOVEMENT
                    WHERE SerialNo IS NOT NULL
                      AND LTRIM(RTRIM(SerialNo)) <> ''
                    GROUP BY StockId, UPPER(LTRIM(RTRIM(SerialNo)))
                ),
                NumberedSerials AS
                (
                    SELECT *,
                        ROW_NUMBER() OVER
                        (
                            PARTITION BY StockId
                            ORDER BY NormalizedSerialNo
                        ) AS LegacyOrdinal
                    FROM AggregatedSerials
                )
                INSERT INTO dbo.RII_STOCK_SERIAL_REGISTRY
                (
                    StockId,
                    SerialNo,
                    NormalizedSerialNo,
                    Status,
                    SerialNumberRuleId,
                    SequenceNumber,
                    GenerationRequestKey,
                    GenerationOrdinal,
                    SourceOperationType,
                    SourceOperationId,
                    ReservedAtUtc,
                    ActivatedAtUtc,
                    ConsumedAtUtc,
                    VoidedAtUtc,
                    VoidedReason,
                    LastStockMovementOperationId,
                    BranchCode,
                    CreatedDate,
                    IsDeleted
                )
                SELECT
                    StockId,
                    UPPER(SerialNo),
                    NormalizedSerialNo,
                    CASE WHEN CurrentQuantity > 0 THEN 'Available' ELSE 'Consumed' END,
                    NULL,
                    0,
                    CONCAT('LEGACY-', StockId, '-', LegacyOrdinal),
                    1,
                    'LegacyMovementBackfill',
                    LastOperationId,
                    TODATETIMEOFFSET(FirstSeenAtUtc, '+00:00'),
                    TODATETIMEOFFSET(FirstSeenAtUtc, '+00:00'),
                    CASE
                        WHEN CurrentQuantity <= 0
                        THEN TODATETIMEOFFSET(LastSeenAtUtc, '+00:00')
                        ELSE NULL
                    END,
                    NULL,
                    NULL,
                    LastOperationId,
                    BranchCode,
                    FirstSeenAtUtc,
                    0
                FROM NumberedSerials;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_RII_STOCK_SERIAL_REGISTRY_IsDeleted",
                table: "RII_STOCK_SERIAL_REGISTRY",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RII_STOCK_SERIAL_REGISTRY_LastStockMovementOperationId",
                table: "RII_STOCK_SERIAL_REGISTRY",
                column: "LastStockMovementOperationId");

            migrationBuilder.CreateIndex(
                name: "IX_RII_STOCK_SERIAL_REGISTRY_SerialNumberRuleId",
                table: "RII_STOCK_SERIAL_REGISTRY",
                column: "SerialNumberRuleId");

            migrationBuilder.CreateIndex(
                name: "IX_RII_STOCK_SERIAL_STATUS",
                table: "RII_STOCK_SERIAL_REGISTRY",
                columns: new[] { "StockId", "Status" });

            migrationBuilder.CreateIndex(
                name: "UX_RII_STOCK_SERIAL_IDEMPOTENCY",
                table: "RII_STOCK_SERIAL_REGISTRY",
                columns: new[] { "StockId", "GenerationRequestKey", "GenerationOrdinal" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_RII_STOCK_SERIAL_STOCK_NUMBER",
                table: "RII_STOCK_SERIAL_REGISTRY",
                columns: new[] { "StockId", "NormalizedSerialNo" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RII_STOCK_SERIAL_REGISTRY");

            migrationBuilder.DropColumn(
                name: "AutoGenerateSerials",
                table: "RII_STOCK_TRACKING_POLICIES");

            migrationBuilder.DropColumn(
                name: "NextSequence",
                table: "RII_SERIAL_NUMBER_RULES");
        }
    }
}
