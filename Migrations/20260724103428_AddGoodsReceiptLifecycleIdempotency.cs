using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace verii_wms_api_v2.Migrations
{
    /// <inheritdoc />
    public partial class AddGoodsReceiptLifecycleIdempotency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RII_GR_STATUS_HISTORY_CORRELATION_ID",
                table: "RII_GR_STATUS_HISTORY");

            migrationBuilder.AddColumn<string>(
                name: "RequestHash",
                table: "RII_GR_STATUS_HISTORY",
                type: "varchar(64)",
                unicode: false,
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "UX_RII_GR_STATUS_HISTORY_HEADER_CORRELATION_ID",
                table: "RII_GR_STATUS_HISTORY",
                columns: new[] { "GrHeaderId", "CorrelationId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_RII_GR_STATUS_HISTORY_HEADER_CORRELATION_ID",
                table: "RII_GR_STATUS_HISTORY");

            migrationBuilder.DropColumn(
                name: "RequestHash",
                table: "RII_GR_STATUS_HISTORY");

            migrationBuilder.CreateIndex(
                name: "IX_RII_GR_STATUS_HISTORY_CORRELATION_ID",
                table: "RII_GR_STATUS_HISTORY",
                column: "CorrelationId");
        }
    }
}
