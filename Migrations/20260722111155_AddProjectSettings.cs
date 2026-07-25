using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace verii_wms_api_v2.Migrations
{
    /// <inheritdoc />
    public partial class AddProjectSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RII_PROJECT_SETTINGS",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SettingKey = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    NumberLocale = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    DecimalPlaces = table.Column<int>(type: "int", nullable: false),
                    DateFormat = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    TimeFormat = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    YearFormat = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    TimeZoneId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
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
                    table.PrimaryKey("PK_RII_PROJECT_SETTINGS", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "RII_PERMISSION_DEFINITIONS",
                columns: new[] { "Id", "AvailableOnMobile", "AvailableOnWeb", "BranchCode", "Code", "CreatedBy", "CreatedDate", "DeletedBy", "DeletedDate", "Description", "IsActive", "Name", "UpdatedBy", "UpdatedDate" },
                values: new object[,]
                {
                    { 1018L, false, true, "0", "SYSTEM.PROJECT_SETTINGS.VIEW", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, true, "Proje Ayarlarını Görüntüle", null, null },
                    { 1019L, false, true, "0", "SYSTEM.PROJECT_SETTINGS.MANAGE", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, true, "Proje Ayarlarını Yönet", null, null }
                });

            migrationBuilder.InsertData(
                table: "RII_PROJECT_SETTINGS",
                columns: new[] { "Id", "BranchCode", "CreatedBy", "CreatedDate", "DateFormat", "DecimalPlaces", "DeletedBy", "DeletedDate", "NumberLocale", "SettingKey", "TimeFormat", "TimeZoneId", "UpdatedBy", "UpdatedDate", "YearFormat" },
                values: new object[] { 1L, "0", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), "dd.MM.yyyy", 2, null, null, "tr-TR", "GLOBAL", "HH:mm", "Europe/Istanbul", null, null, "yyyy" });

            migrationBuilder.InsertData(
                table: "RII_PERMISSION_GROUP_PERMISSIONS",
                columns: new[] { "Id", "BranchCode", "CreatedBy", "CreatedDate", "DeletedBy", "DeletedDate", "PermissionDefinitionId", "PermissionGroupId", "UpdatedBy", "UpdatedDate" },
                values: new object[,]
                {
                    { 1018L, "0", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, 1018L, 1001L, null, null },
                    { 1019L, "0", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, 1019L, 1001L, null, null }
                });

            migrationBuilder.CreateIndex(
                name: "IX_RII_PROJECT_SETTINGS_IsDeleted",
                table: "RII_PROJECT_SETTINGS",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "UX_RII_PROJECT_SETTINGS_KEY",
                table: "RII_PROJECT_SETTINGS",
                column: "SettingKey",
                unique: true,
                filter: "[IsDeleted] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RII_PROJECT_SETTINGS");

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_GROUP_PERMISSIONS",
                keyColumn: "Id",
                keyValue: 1018L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_GROUP_PERMISSIONS",
                keyColumn: "Id",
                keyValue: 1019L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_DEFINITIONS",
                keyColumn: "Id",
                keyValue: 1018L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_DEFINITIONS",
                keyColumn: "Id",
                keyValue: 1019L);
        }
    }
}
