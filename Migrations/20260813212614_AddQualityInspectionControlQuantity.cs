using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace verii_wms_api_v2.Migrations
{
    /// <inheritdoc />
    public partial class AddQualityInspectionControlQuantity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "InspectedQuantity",
                table: "RII_QUALITY_INSPECTION_LINES",
                type: "decimal(18,6)",
                precision: 18,
                scale: 6,
                nullable: false,
                defaultValue: 0m);

            // Historical terminal decisions predate physical-control evidence. Preserve truthful
            // reporting by marking the configured minimum sample as the inferred inspected amount;
            // no immutable control-history row is fabricated for those legacy decisions.
            migrationBuilder.Sql("""
                UPDATE [RII_QUALITY_INSPECTION_LINES]
                SET [InspectedQuantity] = [SampleQuantity]
                WHERE [IsDeleted] = 0
                  AND [Decision] NOT IN ('Pending', 'Hold')
                  AND [SampleQuantity] > 0;
                """);

            migrationBuilder.CreateTable(
                name: "RII_QUALITY_INSPECTION_CONTROLS",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    QualityInspectionId = table.Column<long>(type: "bigint", nullable: false),
                    QualityInspectionLineId = table.Column<long>(type: "bigint", nullable: false),
                    IdempotencyKey = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LotQuantitySnapshot = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    RequiredQuantitySnapshot = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    InspectedQuantity = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    OutcomeSummary = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    Note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    InspectedBy = table.Column<long>(type: "bigint", nullable: false),
                    InspectedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
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
                    table.PrimaryKey("PK_RII_QUALITY_INSPECTION_CONTROLS", x => x.Id);
                    table.CheckConstraint("CK_RII_QUALITY_CONTROL_INSPECTED_QUANTITY", "[InspectedQuantity] > 0");
                    table.CheckConstraint("CK_RII_QUALITY_CONTROL_LOT_QUANTITY", "[LotQuantitySnapshot] > 0");
                    table.CheckConstraint("CK_RII_QUALITY_CONTROL_REQUIRED_QUANTITY", "[RequiredQuantitySnapshot] >= 0");
                    table.ForeignKey(
                        name: "FK_RII_QUALITY_INSPECTION_CONTROLS_RII_QUALITY_INSPECTIONS_QualityInspectionId",
                        column: x => x.QualityInspectionId,
                        principalTable: "RII_QUALITY_INSPECTIONS",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RII_QUALITY_INSPECTION_CONTROLS_RII_QUALITY_INSPECTION_LINES_QualityInspectionLineId",
                        column: x => x.QualityInspectionLineId,
                        principalTable: "RII_QUALITY_INSPECTION_LINES",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RII_QUALITY_INSPECTION_CONTROLS_IsDeleted",
                table: "RII_QUALITY_INSPECTION_CONTROLS",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RII_QUALITY_INSPECTION_CONTROLS_QualityInspectionId_IdempotencyKey_QualityInspectionLineId",
                table: "RII_QUALITY_INSPECTION_CONTROLS",
                columns: new[] { "QualityInspectionId", "IdempotencyKey", "QualityInspectionLineId" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_RII_QUALITY_INSPECTION_CONTROLS_QualityInspectionLineId_InspectedAtUtc",
                table: "RII_QUALITY_INSPECTION_CONTROLS",
                columns: new[] { "QualityInspectionLineId", "InspectedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RII_QUALITY_INSPECTION_CONTROLS");

            migrationBuilder.DropColumn(
                name: "InspectedQuantity",
                table: "RII_QUALITY_INSPECTION_LINES");
        }
    }
}
