using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using verii_wms_api_v2.Modules.Identity.Infrastructure;

#nullable disable

namespace verii_wms_api_v2.Migrations
{
    [DbContext(typeof(WmsDbContext))]
    [Migration("20260817143000_AddQualityDispositionImages")]
    public partial class AddQualityDispositionImages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RII_QUALITY_INSPECTION_IMAGES_QualityInspectionLineId",
                table: "RII_QUALITY_INSPECTION_IMAGES");

            migrationBuilder.AddColumn<string>(
                name: "DraftDispositionKey",
                table: "RII_QUALITY_INSPECTION_IMAGES",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "QualityInspectionDispositionId",
                table: "RII_QUALITY_INSPECTION_IMAGES",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DraftDispositionKey",
                table: "RII_QUALITY_INSPECTION_DISPOSITIONS",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_RII_QUALITY_INSPECTION_IMAGES_QualityInspectionDispositionId",
                table: "RII_QUALITY_INSPECTION_IMAGES",
                column: "QualityInspectionDispositionId");

            migrationBuilder.CreateIndex(
                name: "IX_RII_QUALITY_INSPECTION_IMAGES_QualityInspectionLineId_DraftDispositionKey",
                table: "RII_QUALITY_INSPECTION_IMAGES",
                columns: new[] { "QualityInspectionLineId", "DraftDispositionKey" });

            migrationBuilder.CreateIndex(
                name: "IX_RII_QUALITY_INSPECTION_DISPOSITIONS_QualityInspectionLineId_DraftDispositionKey",
                table: "RII_QUALITY_INSPECTION_DISPOSITIONS",
                columns: new[] { "QualityInspectionLineId", "DraftDispositionKey" });

            migrationBuilder.AddForeignKey(
                name: "FK_RII_QUALITY_INSPECTION_IMAGES_RII_QUALITY_INSPECTION_DISPOSITIONS_QualityInspectionDispositionId",
                table: "RII_QUALITY_INSPECTION_IMAGES",
                column: "QualityInspectionDispositionId",
                principalTable: "RII_QUALITY_INSPECTION_DISPOSITIONS",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RII_QUALITY_INSPECTION_IMAGES_RII_QUALITY_INSPECTION_DISPOSITIONS_QualityInspectionDispositionId",
                table: "RII_QUALITY_INSPECTION_IMAGES");

            migrationBuilder.DropIndex(
                name: "IX_RII_QUALITY_INSPECTION_IMAGES_QualityInspectionDispositionId",
                table: "RII_QUALITY_INSPECTION_IMAGES");

            migrationBuilder.DropIndex(
                name: "IX_RII_QUALITY_INSPECTION_IMAGES_QualityInspectionLineId_DraftDispositionKey",
                table: "RII_QUALITY_INSPECTION_IMAGES");

            migrationBuilder.DropIndex(
                name: "IX_RII_QUALITY_INSPECTION_DISPOSITIONS_QualityInspectionLineId_DraftDispositionKey",
                table: "RII_QUALITY_INSPECTION_DISPOSITIONS");

            migrationBuilder.DropColumn(
                name: "DraftDispositionKey",
                table: "RII_QUALITY_INSPECTION_IMAGES");

            migrationBuilder.DropColumn(
                name: "QualityInspectionDispositionId",
                table: "RII_QUALITY_INSPECTION_IMAGES");

            migrationBuilder.DropColumn(
                name: "DraftDispositionKey",
                table: "RII_QUALITY_INSPECTION_DISPOSITIONS");

            migrationBuilder.CreateIndex(
                name: "IX_RII_QUALITY_INSPECTION_IMAGES_QualityInspectionLineId",
                table: "RII_QUALITY_INSPECTION_IMAGES",
                column: "QualityInspectionLineId");
        }
    }
}
