using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace verii_wms_api_v2.Migrations
{
    /// <inheritdoc />
    public partial class CompleteGoodsReceiptQualityAndSteelFlow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SteelSheetCount",
                table: "RII_VEHICLE_CHECKIN_HEADER",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "QueuedAtUtc",
                table: "RII_QUALITY_INSPECTIONS",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "QueuedBy",
                table: "RII_QUALITY_INSPECTIONS",
                type: "bigint",
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE [RII_QUALITY_INSPECTIONS]
                SET [QueuedAtUtc] = [CreatedAtUtc],
                    [QueuedBy] = [CreatedBy]
                WHERE [QueuedAtUtc] IS NULL;
                """);

            migrationBuilder.Sql("""
                ;WITH [OrderedPlacements] AS
                (
                    SELECT [Id],
                           ROW_NUMBER() OVER
                           (
                               PARTITION BY [LocationId]
                               ORDER BY [PlacedAtUtc], [Id]
                           ) AS [NextStackOrder]
                    FROM [RII_STEEL_RECEIPT_PLACEMENT]
                    WHERE [IsDeleted] = 0
                )
                UPDATE [Placement]
                SET [PlacementType] = N'Stacked',
                    [RowNo] = 1,
                    [PositionNo] = 1,
                    [StackOrderNo] = [Ordered].[NextStackOrder]
                FROM [RII_STEEL_RECEIPT_PLACEMENT] AS [Placement]
                INNER JOIN [OrderedPlacements] AS [Ordered] ON [Placement].[Id] = [Ordered].[Id];
                """);

            migrationBuilder.AddCheckConstraint(
                name: "CK_RII_VEHICLE_CHECKIN_STEEL_SHEET_COUNT",
                table: "RII_VEHICLE_CHECKIN_HEADER",
                sql: "[SteelSheetCount] >= 0");

            migrationBuilder.CreateIndex(
                name: "IX_RII_QUALITY_INSPECTIONS_BranchCode_QueuedAtUtc_Status",
                table: "RII_QUALITY_INSPECTIONS",
                columns: new[] { "BranchCode", "QueuedAtUtc", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_RII_VEHICLE_CHECKIN_STEEL_SHEET_COUNT",
                table: "RII_VEHICLE_CHECKIN_HEADER");

            migrationBuilder.DropIndex(
                name: "IX_RII_QUALITY_INSPECTIONS_BranchCode_QueuedAtUtc_Status",
                table: "RII_QUALITY_INSPECTIONS");

            migrationBuilder.DropColumn(
                name: "SteelSheetCount",
                table: "RII_VEHICLE_CHECKIN_HEADER");

            migrationBuilder.DropColumn(
                name: "QueuedAtUtc",
                table: "RII_QUALITY_INSPECTIONS");

            migrationBuilder.DropColumn(
                name: "QueuedBy",
                table: "RII_QUALITY_INSPECTIONS");
        }
    }
}
