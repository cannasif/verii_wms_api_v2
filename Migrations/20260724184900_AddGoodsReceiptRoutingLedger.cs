using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace verii_wms_api_v2.Migrations
{
    /// <inheritdoc />
    public partial class AddGoodsReceiptRoutingLedger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RII_GR_ROUTING_BATCH",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    GrHeaderId = table.Column<long>(type: "bigint", nullable: false),
                    RouteType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    CorrelationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TargetDocumentId = table.Column<long>(type: "bigint", nullable: false),
                    TargetDocumentNo = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    RoutedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                    RoutedBy = table.Column<long>(type: "bigint", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
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
                    table.PrimaryKey("PK_RII_GR_ROUTING_BATCH", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RII_GR_ROUTING_BATCH_RII_GR_HEADER_GrHeaderId",
                        column: x => x.GrHeaderId,
                        principalTable: "RII_GR_HEADER",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RII_GR_ROUTING_ALLOCATION",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RoutingBatchId = table.Column<long>(type: "bigint", nullable: false),
                    GrLineId = table.Column<long>(type: "bigint", nullable: false),
                    TargetDocumentLineId = table.Column<long>(type: "bigint", nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
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
                    table.PrimaryKey("PK_RII_GR_ROUTING_ALLOCATION", x => x.Id);
                    table.CheckConstraint("CK_RII_GR_ROUTING_ALLOCATION_QUANTITY", "[Quantity] > 0");
                    table.ForeignKey(
                        name: "FK_RII_GR_ROUTING_ALLOCATION_RII_GR_LINE_GrLineId",
                        column: x => x.GrLineId,
                        principalTable: "RII_GR_LINE",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RII_GR_ROUTING_ALLOCATION_RII_GR_ROUTING_BATCH_RoutingBatchId",
                        column: x => x.RoutingBatchId,
                        principalTable: "RII_GR_ROUTING_BATCH",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.UpdateData(
                table: "RII_PERMISSION_DEFINITIONS",
                keyColumn: "Id",
                keyValue: 2100L,
                column: "Name",
                value: "Ambar girişlerini görüntüle");

            migrationBuilder.UpdateData(
                table: "RII_PERMISSION_DEFINITIONS",
                keyColumn: "Id",
                keyValue: 2101L,
                column: "Name",
                value: "Ambar girişi oluştur");

            migrationBuilder.UpdateData(
                table: "RII_PERMISSION_DEFINITIONS",
                keyColumn: "Id",
                keyValue: 2102L,
                column: "Name",
                value: "Ambar girişini güncelle");

            migrationBuilder.UpdateData(
                table: "RII_PERMISSION_DEFINITIONS",
                keyColumn: "Id",
                keyValue: 2103L,
                column: "Name",
                value: "Ambar girişini işleme aç");

            migrationBuilder.UpdateData(
                table: "RII_PERMISSION_DEFINITIONS",
                keyColumn: "Id",
                keyValue: 2104L,
                column: "Name",
                value: "Ambar girişini işle");

            migrationBuilder.UpdateData(
                table: "RII_PERMISSION_DEFINITIONS",
                keyColumn: "Id",
                keyValue: 2105L,
                column: "Name",
                value: "Ambar girişini tamamla");

            migrationBuilder.UpdateData(
                table: "RII_PERMISSION_DEFINITIONS",
                keyColumn: "Id",
                keyValue: 2106L,
                column: "Name",
                value: "Ambar girişini iptal et");

            migrationBuilder.UpdateData(
                table: "RII_PERMISSION_DEFINITIONS",
                keyColumn: "Id",
                keyValue: 2107L,
                column: "Name",
                value: "Ambar giriş ayarlarını görüntüle");

            migrationBuilder.UpdateData(
                table: "RII_PERMISSION_DEFINITIONS",
                keyColumn: "Id",
                keyValue: 2108L,
                column: "Name",
                value: "Ambar giriş ayarlarını yönet");

            migrationBuilder.UpdateData(
                table: "RII_PERMISSION_DEFINITIONS",
                keyColumn: "Id",
                keyValue: 2110L,
                column: "Name",
                value: "Ambar çıkışlarını görüntüle");

            migrationBuilder.UpdateData(
                table: "RII_PERMISSION_DEFINITIONS",
                keyColumn: "Id",
                keyValue: 2111L,
                column: "Name",
                value: "Ambar çıkışı oluştur");

            migrationBuilder.UpdateData(
                table: "RII_PERMISSION_DEFINITIONS",
                keyColumn: "Id",
                keyValue: 2112L,
                column: "Name",
                value: "Ambar çıkışını güncelle");

            migrationBuilder.UpdateData(
                table: "RII_PERMISSION_DEFINITIONS",
                keyColumn: "Id",
                keyValue: 2113L,
                column: "Name",
                value: "Ambar çıkış taslağını sil");

            migrationBuilder.UpdateData(
                table: "RII_PERMISSION_DEFINITIONS",
                keyColumn: "Id",
                keyValue: 2114L,
                column: "Name",
                value: "Ambar çıkış operasyonunu yürüt");

            migrationBuilder.UpdateData(
                table: "RII_PERMISSION_DEFINITIONS",
                keyColumn: "Id",
                keyValue: 2115L,
                column: "Name",
                value: "Ambar çıkışını onayla");

            migrationBuilder.UpdateData(
                table: "RII_PERMISSION_DEFINITIONS",
                keyColumn: "Id",
                keyValue: 2116L,
                column: "Name",
                value: "Ambar çıkışını iptal et");

            migrationBuilder.UpdateData(
                table: "RII_PERMISSION_DEFINITIONS",
                keyColumn: "Id",
                keyValue: 2117L,
                column: "Name",
                value: "Ambar çıkış ayarlarını görüntüle");

            migrationBuilder.UpdateData(
                table: "RII_PERMISSION_DEFINITIONS",
                keyColumn: "Id",
                keyValue: 2118L,
                column: "Name",
                value: "Ambar çıkış ayarlarını yönet");

            migrationBuilder.CreateIndex(
                name: "IX_RII_GR_ROUTING_ALLOCATION_IsDeleted",
                table: "RII_GR_ROUTING_ALLOCATION",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RII_GR_ROUTING_ALLOCATION_LINE_BATCH",
                table: "RII_GR_ROUTING_ALLOCATION",
                columns: new[] { "GrLineId", "RoutingBatchId" });

            migrationBuilder.CreateIndex(
                name: "UX_RII_GR_ROUTING_ALLOCATION_BATCH_LINE",
                table: "RII_GR_ROUTING_ALLOCATION",
                columns: new[] { "RoutingBatchId", "GrLineId" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_RII_GR_ROUTING_BATCH_HEADER_TYPE_DATE",
                table: "RII_GR_ROUTING_BATCH",
                columns: new[] { "GrHeaderId", "RouteType", "RoutedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_RII_GR_ROUTING_BATCH_IsDeleted",
                table: "RII_GR_ROUTING_BATCH",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "UX_RII_GR_ROUTING_BATCH_CORRELATION",
                table: "RII_GR_ROUTING_BATCH",
                column: "CorrelationId",
                unique: true,
                filter: "[IsDeleted] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RII_GR_ROUTING_ALLOCATION");

            migrationBuilder.DropTable(
                name: "RII_GR_ROUTING_BATCH");

            migrationBuilder.UpdateData(
                table: "RII_PERMISSION_DEFINITIONS",
                keyColumn: "Id",
                keyValue: 2100L,
                column: "Name",
                value: "Ambar giriÅŸlerini gÃ¶rÃ¼ntÃ¼le");

            migrationBuilder.UpdateData(
                table: "RII_PERMISSION_DEFINITIONS",
                keyColumn: "Id",
                keyValue: 2101L,
                column: "Name",
                value: "Ambar giriÅŸi oluÅŸtur");

            migrationBuilder.UpdateData(
                table: "RII_PERMISSION_DEFINITIONS",
                keyColumn: "Id",
                keyValue: 2102L,
                column: "Name",
                value: "Ambar giriÅŸini gÃ¼ncelle");

            migrationBuilder.UpdateData(
                table: "RII_PERMISSION_DEFINITIONS",
                keyColumn: "Id",
                keyValue: 2103L,
                column: "Name",
                value: "Ambar giriÅŸini iÅŸleme aÃ§");

            migrationBuilder.UpdateData(
                table: "RII_PERMISSION_DEFINITIONS",
                keyColumn: "Id",
                keyValue: 2104L,
                column: "Name",
                value: "Ambar giriÅŸini iÅŸle");

            migrationBuilder.UpdateData(
                table: "RII_PERMISSION_DEFINITIONS",
                keyColumn: "Id",
                keyValue: 2105L,
                column: "Name",
                value: "Ambar giriÅŸini tamamla");

            migrationBuilder.UpdateData(
                table: "RII_PERMISSION_DEFINITIONS",
                keyColumn: "Id",
                keyValue: 2106L,
                column: "Name",
                value: "Ambar giriÅŸini iptal et");

            migrationBuilder.UpdateData(
                table: "RII_PERMISSION_DEFINITIONS",
                keyColumn: "Id",
                keyValue: 2107L,
                column: "Name",
                value: "Ambar giriÅŸ ayarlarÄ±nÄ± gÃ¶rÃ¼ntÃ¼le");

            migrationBuilder.UpdateData(
                table: "RII_PERMISSION_DEFINITIONS",
                keyColumn: "Id",
                keyValue: 2108L,
                column: "Name",
                value: "Ambar giriÅŸ ayarlarÄ±nÄ± yÃ¶net");

            migrationBuilder.UpdateData(
                table: "RII_PERMISSION_DEFINITIONS",
                keyColumn: "Id",
                keyValue: 2110L,
                column: "Name",
                value: "Ambar Ã§Ä±kÄ±ÅŸlarÄ±nÄ± gÃ¶rÃ¼ntÃ¼le");

            migrationBuilder.UpdateData(
                table: "RII_PERMISSION_DEFINITIONS",
                keyColumn: "Id",
                keyValue: 2111L,
                column: "Name",
                value: "Ambar Ã§Ä±kÄ±ÅŸÄ± oluÅŸtur");

            migrationBuilder.UpdateData(
                table: "RII_PERMISSION_DEFINITIONS",
                keyColumn: "Id",
                keyValue: 2112L,
                column: "Name",
                value: "Ambar Ã§Ä±kÄ±ÅŸÄ±nÄ± gÃ¼ncelle");

            migrationBuilder.UpdateData(
                table: "RII_PERMISSION_DEFINITIONS",
                keyColumn: "Id",
                keyValue: 2113L,
                column: "Name",
                value: "Ambar Ã§Ä±kÄ±ÅŸ taslaÄŸÄ±nÄ± sil");

            migrationBuilder.UpdateData(
                table: "RII_PERMISSION_DEFINITIONS",
                keyColumn: "Id",
                keyValue: 2114L,
                column: "Name",
                value: "Ambar Ã§Ä±kÄ±ÅŸ operasyonunu yÃ¼rÃ¼t");

            migrationBuilder.UpdateData(
                table: "RII_PERMISSION_DEFINITIONS",
                keyColumn: "Id",
                keyValue: 2115L,
                column: "Name",
                value: "Ambar Ã§Ä±kÄ±ÅŸÄ±nÄ± onayla");

            migrationBuilder.UpdateData(
                table: "RII_PERMISSION_DEFINITIONS",
                keyColumn: "Id",
                keyValue: 2116L,
                column: "Name",
                value: "Ambar Ã§Ä±kÄ±ÅŸÄ±nÄ± iptal et");

            migrationBuilder.UpdateData(
                table: "RII_PERMISSION_DEFINITIONS",
                keyColumn: "Id",
                keyValue: 2117L,
                column: "Name",
                value: "Ambar Ã§Ä±kÄ±ÅŸ ayarlarÄ±nÄ± gÃ¶rÃ¼ntÃ¼le");

            migrationBuilder.UpdateData(
                table: "RII_PERMISSION_DEFINITIONS",
                keyColumn: "Id",
                keyValue: 2118L,
                column: "Name",
                value: "Ambar Ã§Ä±kÄ±ÅŸ ayarlarÄ±nÄ± yÃ¶net");
        }
    }
}
