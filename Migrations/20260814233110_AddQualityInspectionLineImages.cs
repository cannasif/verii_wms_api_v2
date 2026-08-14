using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace verii_wms_api_v2.Migrations
{
    /// <inheritdoc />
    public partial class AddQualityInspectionLineImages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RII_QUALITY_INSPECTION_IMAGES",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    QualityInspectionId = table.Column<long>(type: "bigint", nullable: false),
                    QualityInspectionLineId = table.Column<long>(type: "bigint", nullable: false),
                    StoragePath = table.Column<string>(type: "nvarchar(700)", maxLength: 700, nullable: false),
                    OriginalFileName = table.Column<string>(type: "nvarchar(240)", maxLength: 240, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    FileLength = table.Column<long>(type: "bigint", nullable: false),
                    Caption = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
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
                    table.PrimaryKey("PK_RII_QUALITY_INSPECTION_IMAGES", x => x.Id);
                    table.CheckConstraint("CK_RII_QUALITY_IMAGE_LENGTH", "[FileLength] > 0");
                    table.ForeignKey(
                        name: "FK_RII_QUALITY_INSPECTION_IMAGES_RII_QUALITY_INSPECTIONS_QualityInspectionId",
                        column: x => x.QualityInspectionId,
                        principalTable: "RII_QUALITY_INSPECTIONS",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RII_QUALITY_INSPECTION_IMAGES_RII_QUALITY_INSPECTION_LINES_QualityInspectionLineId",
                        column: x => x.QualityInspectionLineId,
                        principalTable: "RII_QUALITY_INSPECTION_LINES",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "RII_PERMISSION_DEFINITIONS",
                columns: new[] { "Id", "AvailableOnMobile", "AvailableOnWeb", "BranchCode", "Code", "CreatedBy", "CreatedDate", "DeletedBy", "DeletedDate", "Description", "IsActive", "Name", "UpdatedBy", "UpdatedDate" },
                values: new object[,]
                {
                    { 10525L, true, true, "0", "WMS.QUALITY.INSPECTIONS.IMAGES.VIEW", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, "Kalite inceleme satırına eklenen özel görsel kanıtları görüntülemeye izin verir.", true, "GKK satır karar görsellerini görüntüle", null, null },
                    { 10526L, true, true, "0", "WMS.QUALITY.INSPECTIONS.IMAGES.UPLOAD", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, "Kalite inceleme satırına fotoğraf veya görsel kanıt eklemeye izin verir.", true, "GKK satır kararına görsel yükle", null, null },
                    { 10527L, false, true, "0", "WMS.QUALITY.INSPECTIONS.IMAGES.DELETE", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, "Kalite inceleme satırına eklenmiş görsel kanıtı denetim izi bırakarak silmeye izin verir.", true, "GKK satır karar görselini sil", null, null }
                });

            migrationBuilder.InsertData(
                table: "RII_PERMISSION_GROUP_PERMISSIONS",
                columns: new[] { "Id", "BranchCode", "CreatedBy", "CreatedDate", "DeletedBy", "DeletedDate", "PermissionDefinitionId", "PermissionGroupId", "UpdatedBy", "UpdatedDate" },
                values: new object[,]
                {
                    { 10525L, "0", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, 10525L, 1001L, null, null },
                    { 10526L, "0", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, 10526L, 1001L, null, null },
                    { 10527L, "0", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, 10527L, 1001L, null, null }
                });

            migrationBuilder.CreateIndex(
                name: "IX_RII_QUALITY_INSPECTION_IMAGES_BranchCode_QualityInspectionLineId_CreatedDate",
                table: "RII_QUALITY_INSPECTION_IMAGES",
                columns: new[] { "BranchCode", "QualityInspectionLineId", "CreatedDate" });

            migrationBuilder.CreateIndex(
                name: "IX_RII_QUALITY_INSPECTION_IMAGES_IsDeleted",
                table: "RII_QUALITY_INSPECTION_IMAGES",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RII_QUALITY_INSPECTION_IMAGES_QualityInspectionId_QualityInspectionLineId",
                table: "RII_QUALITY_INSPECTION_IMAGES",
                columns: new[] { "QualityInspectionId", "QualityInspectionLineId" });

            migrationBuilder.CreateIndex(
                name: "IX_RII_QUALITY_INSPECTION_IMAGES_QualityInspectionLineId",
                table: "RII_QUALITY_INSPECTION_IMAGES",
                column: "QualityInspectionLineId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RII_QUALITY_INSPECTION_IMAGES");

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_GROUP_PERMISSIONS",
                keyColumn: "Id",
                keyValue: 10525L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_GROUP_PERMISSIONS",
                keyColumn: "Id",
                keyValue: 10526L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_GROUP_PERMISSIONS",
                keyColumn: "Id",
                keyValue: 10527L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_DEFINITIONS",
                keyColumn: "Id",
                keyValue: 10525L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_DEFINITIONS",
                keyColumn: "Id",
                keyValue: 10526L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_DEFINITIONS",
                keyColumn: "Id",
                keyValue: 10527L);
        }
    }
}
