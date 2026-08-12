using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace verii_wms_api_v2.Migrations
{
    /// <inheritdoc />
    public partial class AddDefaultQualityDecisionDestination : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "DefaultAcceptedLocationId",
                table: "RII_QUALITY_PARAMETERS",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_RII_QUALITY_PARAMETERS_DefaultAcceptedLocationId",
                table: "RII_QUALITY_PARAMETERS",
                column: "DefaultAcceptedLocationId");

            migrationBuilder.AddForeignKey(
                name: "FK_RII_QUALITY_PARAMETERS_RII_LOCATION_DefaultAcceptedLocationId",
                table: "RII_QUALITY_PARAMETERS",
                column: "DefaultAcceptedLocationId",
                principalTable: "RII_LOCATION",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RII_QUALITY_PARAMETERS_RII_LOCATION_DefaultAcceptedLocationId",
                table: "RII_QUALITY_PARAMETERS");

            migrationBuilder.DropIndex(
                name: "IX_RII_QUALITY_PARAMETERS_DefaultAcceptedLocationId",
                table: "RII_QUALITY_PARAMETERS");

            migrationBuilder.DropColumn(
                name: "DefaultAcceptedLocationId",
                table: "RII_QUALITY_PARAMETERS");
        }
    }
}
