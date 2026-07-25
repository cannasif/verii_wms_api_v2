using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace verii_wms_api_v2.Migrations
{
    /// <inheritdoc />
    public partial class AddDocumentSeriesModule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RII_DOCUMENT_SERIES",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    WarehouseId = table.Column<long>(type: "bigint", nullable: true),
                    Code = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    DocumentType = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Prefix = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Separator = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false, defaultValue: "-"),
                    YearFormat = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    NumberLength = table.Column<int>(type: "int", nullable: false, defaultValue: 8),
                    StartNumber = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L),
                    NextNumber = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L),
                    IncrementBy = table.Column<int>(type: "int", nullable: false, defaultValue: 1),
                    IsDefault = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    HasIssuedNumbers = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    LastIssuedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
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
                    table.PrimaryKey("PK_RII_DOCUMENT_SERIES", x => x.Id);
                    table.CheckConstraint("CK_RII_DOCUMENT_SERIES_INCREMENT", "[IncrementBy] BETWEEN 1 AND 1000");
                    table.CheckConstraint("CK_RII_DOCUMENT_SERIES_NEXT_NUMBER", "[NextNumber] >= [StartNumber]");
                    table.CheckConstraint("CK_RII_DOCUMENT_SERIES_NUMBER_LENGTH", "[NumberLength] BETWEEN 3 AND 18");
                    table.CheckConstraint("CK_RII_DOCUMENT_SERIES_START_NUMBER", "[StartNumber] > 0");
                    table.ForeignKey(
                        name: "FK_RII_DOCUMENT_SERIES_RII_WAREHOUSE_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "RII_WAREHOUSE",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "RII_PERMISSION_DEFINITIONS",
                columns: new[] { "Id", "AvailableOnMobile", "AvailableOnWeb", "BranchCode", "Code", "CreatedBy", "CreatedDate", "DeletedBy", "DeletedDate", "Description", "IsActive", "Name", "UpdatedBy", "UpdatedDate" },
                values: new object[,]
                {
                    { 1020L, false, true, "0", "WMS.DOCUMENT_SERIES.VIEW", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, true, "Belge Serilerini Görüntüle", null, null },
                    { 1021L, false, true, "0", "WMS.DOCUMENT_SERIES.CREATE", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, true, "Belge Serisi Oluştur", null, null },
                    { 1022L, false, true, "0", "WMS.DOCUMENT_SERIES.UPDATE", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, true, "Belge Serisini Güncelle", null, null },
                    { 1023L, false, true, "0", "WMS.DOCUMENT_SERIES.DELETE", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, true, "Belge Serisini Sil", null, null }
                });

            migrationBuilder.InsertData(
                table: "RII_PERMISSION_GROUP_PERMISSIONS",
                columns: new[] { "Id", "BranchCode", "CreatedBy", "CreatedDate", "DeletedBy", "DeletedDate", "PermissionDefinitionId", "PermissionGroupId", "UpdatedBy", "UpdatedDate" },
                values: new object[,]
                {
                    { 1020L, "0", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, 1020L, 1001L, null, null },
                    { 1021L, "0", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, 1021L, 1001L, null, null },
                    { 1022L, "0", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, 1022L, 1001L, null, null },
                    { 1023L, "0", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, 1023L, 1001L, null, null }
                });

            migrationBuilder.CreateIndex(
                name: "IX_RII_DOCUMENT_SERIES_IsDeleted",
                table: "RII_DOCUMENT_SERIES",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RII_DOCUMENT_SERIES_RESOLUTION",
                table: "RII_DOCUMENT_SERIES",
                columns: new[] { "DocumentType", "WarehouseId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_RII_DOCUMENT_SERIES_WarehouseId",
                table: "RII_DOCUMENT_SERIES",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "UX_RII_DOCUMENT_SERIES_DEFAULT_SCOPE",
                table: "RII_DOCUMENT_SERIES",
                columns: new[] { "BranchCode", "DocumentType", "WarehouseId" },
                unique: true,
                filter: "[IsDefault] = 1 AND [IsActive] = 1 AND [IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "UX_RII_DOCUMENT_SERIES_SCOPE_CODE",
                table: "RII_DOCUMENT_SERIES",
                columns: new[] { "BranchCode", "DocumentType", "Code" },
                unique: true,
                filter: "[IsDeleted] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RII_DOCUMENT_SERIES");

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_GROUP_PERMISSIONS",
                keyColumn: "Id",
                keyValue: 1020L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_GROUP_PERMISSIONS",
                keyColumn: "Id",
                keyValue: 1021L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_GROUP_PERMISSIONS",
                keyColumn: "Id",
                keyValue: 1022L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_GROUP_PERMISSIONS",
                keyColumn: "Id",
                keyValue: 1023L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_DEFINITIONS",
                keyColumn: "Id",
                keyValue: 1020L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_DEFINITIONS",
                keyColumn: "Id",
                keyValue: 1021L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_DEFINITIONS",
                keyColumn: "Id",
                keyValue: 1022L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_DEFINITIONS",
                keyColumn: "Id",
                keyValue: 1023L);
        }
    }
}
