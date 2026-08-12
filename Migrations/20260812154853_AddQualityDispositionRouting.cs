using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace verii_wms_api_v2.Migrations
{
    /// <inheritdoc />
    public partial class AddQualityDispositionRouting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RII_QUALITY_INSPECTION_DISPOSITIONS",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    QualityInspectionId = table.Column<long>(type: "bigint", nullable: false),
                    QualityInspectionLineId = table.Column<long>(type: "bigint", nullable: false),
                    IdempotencyKey = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SequenceNo = table.Column<int>(type: "int", nullable: false),
                    Decision = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    SourceWarehouseId = table.Column<long>(type: "bigint", nullable: false),
                    SourceLocationId = table.Column<long>(type: "bigint", nullable: false),
                    TargetWarehouseId = table.Column<long>(type: "bigint", nullable: false),
                    TargetLocationId = table.Column<long>(type: "bigint", nullable: false),
                    SourceWarehouseCodeSnapshot = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    SourceLocationCodeSnapshot = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    TargetWarehouseCodeSnapshot = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    TargetLocationCodeSnapshot = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    SourceStockStatus = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    TargetStockStatus = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    StockMovementOperationId = table.Column<long>(type: "bigint", nullable: true),
                    WarehouseTransferId = table.Column<long>(type: "bigint", nullable: true),
                    ReasonCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ReasonNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    DecisionBy = table.Column<long>(type: "bigint", nullable: false),
                    DecisionAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
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
                    table.PrimaryKey("PK_RII_QUALITY_INSPECTION_DISPOSITIONS", x => x.Id);
                    table.CheckConstraint("CK_RII_QUALITY_DISPOSITION_QUANTITY", "[Quantity] > 0");
                    table.CheckConstraint("CK_RII_QUALITY_DISPOSITION_SEQUENCE", "[SequenceNo] > 0");
                    table.ForeignKey(
                        name: "FK_RII_QUALITY_INSPECTION_DISPOSITIONS_RII_LOCATION_SourceLocationId",
                        column: x => x.SourceLocationId,
                        principalTable: "RII_LOCATION",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RII_QUALITY_INSPECTION_DISPOSITIONS_RII_LOCATION_TargetLocationId",
                        column: x => x.TargetLocationId,
                        principalTable: "RII_LOCATION",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RII_QUALITY_INSPECTION_DISPOSITIONS_RII_QUALITY_INSPECTIONS_QualityInspectionId",
                        column: x => x.QualityInspectionId,
                        principalTable: "RII_QUALITY_INSPECTIONS",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RII_QUALITY_INSPECTION_DISPOSITIONS_RII_QUALITY_INSPECTION_LINES_QualityInspectionLineId",
                        column: x => x.QualityInspectionLineId,
                        principalTable: "RII_QUALITY_INSPECTION_LINES",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RII_QUALITY_INSPECTION_DISPOSITIONS_RII_WAREHOUSE_SourceWarehouseId",
                        column: x => x.SourceWarehouseId,
                        principalTable: "RII_WAREHOUSE",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RII_QUALITY_INSPECTION_DISPOSITIONS_RII_WAREHOUSE_TargetWarehouseId",
                        column: x => x.TargetWarehouseId,
                        principalTable: "RII_WAREHOUSE",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RII_QUALITY_INSPECTION_DISPOSITIONS_IsDeleted",
                table: "RII_QUALITY_INSPECTION_DISPOSITIONS",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RII_QUALITY_INSPECTION_DISPOSITIONS_QualityInspectionId_IdempotencyKey_SequenceNo",
                table: "RII_QUALITY_INSPECTION_DISPOSITIONS",
                columns: new[] { "QualityInspectionId", "IdempotencyKey", "SequenceNo" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_RII_QUALITY_INSPECTION_DISPOSITIONS_QualityInspectionLineId_Decision_DecisionAtUtc",
                table: "RII_QUALITY_INSPECTION_DISPOSITIONS",
                columns: new[] { "QualityInspectionLineId", "Decision", "DecisionAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_RII_QUALITY_INSPECTION_DISPOSITIONS_SourceLocationId",
                table: "RII_QUALITY_INSPECTION_DISPOSITIONS",
                column: "SourceLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_RII_QUALITY_INSPECTION_DISPOSITIONS_SourceWarehouseId",
                table: "RII_QUALITY_INSPECTION_DISPOSITIONS",
                column: "SourceWarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_RII_QUALITY_INSPECTION_DISPOSITIONS_TargetLocationId",
                table: "RII_QUALITY_INSPECTION_DISPOSITIONS",
                column: "TargetLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_RII_QUALITY_INSPECTION_DISPOSITIONS_TargetWarehouseId",
                table: "RII_QUALITY_INSPECTION_DISPOSITIONS",
                column: "TargetWarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_RII_QUALITY_INSPECTION_DISPOSITIONS_WarehouseTransferId",
                table: "RII_QUALITY_INSPECTION_DISPOSITIONS",
                column: "WarehouseTransferId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RII_QUALITY_INSPECTION_DISPOSITIONS");
        }
    }
}
