using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace verii_wms_api_v2.Migrations
{
    /// <inheritdoc />
    public partial class AddBarcodeDesignerModule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RII_BARCODE_TEMPLATE",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TemplateCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    LabelType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    WidthMm = table.Column<decimal>(type: "decimal(8,2)", nullable: false),
                    HeightMm = table.Column<decimal>(type: "decimal(8,2)", nullable: false),
                    Dpi = table.Column<int>(type: "int", nullable: false),
                    EngineType = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    DraftVersionId = table.Column<long>(type: "bigint", nullable: true),
                    PublishedVersionId = table.Column<long>(type: "bigint", nullable: true),
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
                    table.PrimaryKey("PK_RII_BARCODE_TEMPLATE", x => x.Id);
                    table.CheckConstraint("CK_RII_BARCODE_TEMPLATE_DPI", "[Dpi] IN (203,300,600)");
                    table.CheckConstraint("CK_RII_BARCODE_TEMPLATE_SIZE", "[WidthMm] BETWEEN 10 AND 300 AND [HeightMm] BETWEEN 10 AND 500");
                });

            migrationBuilder.CreateTable(
                name: "RII_BARCODE_TEMPLATE_VERSION",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BarcodeTemplateId = table.Column<long>(type: "bigint", nullable: false),
                    VersionNo = table.Column<int>(type: "int", nullable: false),
                    IsPublished = table.Column<bool>(type: "bit", nullable: false),
                    PublishedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    TemplateJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
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
                    table.PrimaryKey("PK_RII_BARCODE_TEMPLATE_VERSION", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RII_BARCODE_TEMPLATE_VERSION_RII_BARCODE_TEMPLATE_BarcodeTemplateId",
                        column: x => x.BarcodeTemplateId,
                        principalTable: "RII_BARCODE_TEMPLATE",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "RII_PERMISSION_DEFINITIONS",
                columns: new[] { "Id", "AvailableOnMobile", "AvailableOnWeb", "BranchCode", "Code", "CreatedBy", "CreatedDate", "DeletedBy", "DeletedDate", "Description", "IsActive", "Name", "UpdatedBy", "UpdatedDate" },
                values: new object[,]
                {
                    { 1024L, false, true, "0", "WMS.BARCODE_DESIGNER.VIEW", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, true, "Barkod Şablonlarını Görüntüle", null, null },
                    { 1025L, false, true, "0", "WMS.BARCODE_DESIGNER.CREATE", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, true, "Barkod Şablonu Oluştur", null, null },
                    { 1026L, false, true, "0", "WMS.BARCODE_DESIGNER.UPDATE", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, true, "Barkod Şablonunu Güncelle", null, null },
                    { 1027L, false, true, "0", "WMS.BARCODE_DESIGNER.DELETE", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, true, "Barkod Şablonunu Sil", null, null },
                    { 1028L, false, true, "0", "WMS.BARCODE_DESIGNER.PUBLISH", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, true, "Barkod Şablonu Yayınla", null, null },
                    { 1029L, false, true, "0", "WMS.BARCODE_DESIGNER.PRINT", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, true, "Barkod Etiketi Yazdır", null, null }
                });

            migrationBuilder.InsertData(
                table: "RII_PERMISSION_GROUP_PERMISSIONS",
                columns: new[] { "Id", "BranchCode", "CreatedBy", "CreatedDate", "DeletedBy", "DeletedDate", "PermissionDefinitionId", "PermissionGroupId", "UpdatedBy", "UpdatedDate" },
                values: new object[,]
                {
                    { 1024L, "0", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, 1024L, 1001L, null, null },
                    { 1025L, "0", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, 1025L, 1001L, null, null },
                    { 1026L, "0", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, 1026L, 1001L, null, null },
                    { 1027L, "0", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, 1027L, 1001L, null, null },
                    { 1028L, "0", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, 1028L, 1001L, null, null },
                    { 1029L, "0", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, 1029L, 1001L, null, null }
                });

            migrationBuilder.CreateIndex(
                name: "IX_RII_BARCODE_TEMPLATE_IsDeleted",
                table: "RII_BARCODE_TEMPLATE",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RII_BARCODE_TEMPLATE_TYPE_ACTIVE",
                table: "RII_BARCODE_TEMPLATE",
                columns: new[] { "LabelType", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "UX_RII_BARCODE_TEMPLATE_BRANCH_CODE",
                table: "RII_BARCODE_TEMPLATE",
                columns: new[] { "BranchCode", "TemplateCode" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_RII_BARCODE_TEMPLATE_VERSION_IsDeleted",
                table: "RII_BARCODE_TEMPLATE_VERSION",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RII_BARCODE_TEMPLATE_VERSION_PUBLISHED",
                table: "RII_BARCODE_TEMPLATE_VERSION",
                columns: new[] { "BarcodeTemplateId", "IsPublished" });

            migrationBuilder.CreateIndex(
                name: "UX_RII_BARCODE_TEMPLATE_VERSION_NO",
                table: "RII_BARCODE_TEMPLATE_VERSION",
                columns: new[] { "BarcodeTemplateId", "VersionNo" },
                unique: true,
                filter: "[IsDeleted] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RII_BARCODE_TEMPLATE_VERSION");

            migrationBuilder.DropTable(
                name: "RII_BARCODE_TEMPLATE");

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_GROUP_PERMISSIONS",
                keyColumn: "Id",
                keyValue: 1024L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_GROUP_PERMISSIONS",
                keyColumn: "Id",
                keyValue: 1025L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_GROUP_PERMISSIONS",
                keyColumn: "Id",
                keyValue: 1026L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_GROUP_PERMISSIONS",
                keyColumn: "Id",
                keyValue: 1027L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_GROUP_PERMISSIONS",
                keyColumn: "Id",
                keyValue: 1028L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_GROUP_PERMISSIONS",
                keyColumn: "Id",
                keyValue: 1029L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_DEFINITIONS",
                keyColumn: "Id",
                keyValue: 1024L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_DEFINITIONS",
                keyColumn: "Id",
                keyValue: 1025L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_DEFINITIONS",
                keyColumn: "Id",
                keyValue: 1026L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_DEFINITIONS",
                keyColumn: "Id",
                keyValue: 1027L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_DEFINITIONS",
                keyColumn: "Id",
                keyValue: 1028L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_DEFINITIONS",
                keyColumn: "Id",
                keyValue: 1029L);
        }
    }
}
