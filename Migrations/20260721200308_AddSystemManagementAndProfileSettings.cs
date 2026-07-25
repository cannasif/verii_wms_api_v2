using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace verii_wms_api_v2.Migrations
{
    /// <inheritdoc />
    public partial class AddSystemManagementAndProfileSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Phone",
                table: "RII_USER_DETAILS",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(30)",
                oldMaxLength: 30,
                oldNullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedDate",
                table: "RII_USER_DETAILS",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "RII_USER_DETAILS",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Gender",
                table: "RII_USER_DETAILS",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Height",
                table: "RII_USER_DETAILS",
                type: "decimal(6,2)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProfilePictureUrl",
                table: "RII_USER_DETAILS",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedDate",
                table: "RII_USER_DETAILS",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Weight",
                table: "RII_USER_DETAILS",
                type: "decimal(6,2)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "RII_PERMISSION_DEFINITIONS",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    AvailableOnWeb = table.Column<bool>(type: "bit", nullable: false),
                    AvailableOnMobile = table.Column<bool>(type: "bit", nullable: false),
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
                    table.PrimaryKey("PK_RII_PERMISSION_DEFINITIONS", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RII_PERMISSION_GROUPS",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IsSystemAdmin = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
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
                    table.PrimaryKey("PK_RII_PERMISSION_GROUPS", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RII_SMTP_SETTING",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Host = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Port = table.Column<int>(type: "int", nullable: false),
                    EnableSsl = table.Column<bool>(type: "bit", nullable: false),
                    Username = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    PasswordEncrypted = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    FromEmail = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    FromName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Timeout = table.Column<int>(type: "int", nullable: false),
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
                    table.PrimaryKey("PK_RII_SMTP_SETTING", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RII_PERMISSION_GROUP_PERMISSIONS",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PermissionGroupId = table.Column<long>(type: "bigint", nullable: false),
                    PermissionDefinitionId = table.Column<long>(type: "bigint", nullable: false),
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
                    table.PrimaryKey("PK_RII_PERMISSION_GROUP_PERMISSIONS", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RII_PERMISSION_GROUP_PERMISSIONS_RII_PERMISSION_DEFINITIONS_PermissionDefinitionId",
                        column: x => x.PermissionDefinitionId,
                        principalTable: "RII_PERMISSION_DEFINITIONS",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RII_PERMISSION_GROUP_PERMISSIONS_RII_PERMISSION_GROUPS_PermissionGroupId",
                        column: x => x.PermissionGroupId,
                        principalTable: "RII_PERMISSION_GROUPS",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RII_USER_PERMISSION_GROUPS",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<long>(type: "bigint", nullable: false),
                    PermissionGroupId = table.Column<long>(type: "bigint", nullable: false),
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
                    table.PrimaryKey("PK_RII_USER_PERMISSION_GROUPS", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RII_USER_PERMISSION_GROUPS_RII_PERMISSION_GROUPS_PermissionGroupId",
                        column: x => x.PermissionGroupId,
                        principalTable: "RII_PERMISSION_GROUPS",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RII_USER_PERMISSION_GROUPS_RII_USERS_UserId",
                        column: x => x.UserId,
                        principalTable: "RII_USERS",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "RII_PERMISSION_DEFINITIONS",
                columns: new[] { "Id", "AvailableOnMobile", "AvailableOnWeb", "BranchCode", "Code", "CreatedBy", "CreatedDate", "DeletedBy", "DeletedDate", "Description", "IsActive", "Name", "UpdatedBy", "UpdatedDate" },
                values: new object[,]
                {
                    { 1001L, false, true, "0", "SYSTEM.USERS.VIEW", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, true, "Kullanıcıları Görüntüle", null, null },
                    { 1002L, false, true, "0", "SYSTEM.USERS.MANAGE", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, true, "Kullanıcıları Yönet", null, null },
                    { 1003L, false, true, "0", "SYSTEM.PERMISSIONS.VIEW", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, true, "İzinleri Görüntüle", null, null },
                    { 1004L, false, true, "0", "SYSTEM.PERMISSIONS.MANAGE", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, true, "İzinleri Yönet", null, null },
                    { 1005L, false, true, "0", "SYSTEM.SMTP.MANAGE", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, true, "SMTP Ayarlarını Yönet", null, null },
                    { 1006L, false, true, "0", "SYSTEM.HANGFIRE.VIEW", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, true, "Hangfire İzle", null, null },
                    { 1007L, false, true, "0", "SYSTEM.HANGFIRE.TRIGGER", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, true, "Hangfire Job Tetikle", null, null }
                });

            migrationBuilder.InsertData(
                table: "RII_PERMISSION_GROUPS",
                columns: new[] { "Id", "BranchCode", "CreatedBy", "CreatedDate", "DeletedBy", "DeletedDate", "Description", "IsActive", "IsSystemAdmin", "Name", "UpdatedBy", "UpdatedDate" },
                values: new object[] { 1001L, "0", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, "Tam sistem yönetimi", true, true, "System Administrators", null, null });

            migrationBuilder.UpdateData(
                table: "RII_USER_DETAILS",
                keyColumn: "UserId",
                keyValue: 1L,
                columns: new[] { "CreatedDate", "Description", "Gender", "Height", "ProfilePictureUrl", "UpdatedDate", "Weight" },
                values: new object[] { null, null, null, null, null, null, null });

            migrationBuilder.InsertData(
                table: "RII_PERMISSION_GROUP_PERMISSIONS",
                columns: new[] { "Id", "BranchCode", "CreatedBy", "CreatedDate", "DeletedBy", "DeletedDate", "PermissionDefinitionId", "PermissionGroupId", "UpdatedBy", "UpdatedDate" },
                values: new object[,]
                {
                    { 1001L, "0", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, 1001L, 1001L, null, null },
                    { 1002L, "0", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, 1002L, 1001L, null, null },
                    { 1003L, "0", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, 1003L, 1001L, null, null },
                    { 1004L, "0", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, 1004L, 1001L, null, null },
                    { 1005L, "0", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, 1005L, 1001L, null, null },
                    { 1006L, "0", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, 1006L, 1001L, null, null },
                    { 1007L, "0", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, 1007L, 1001L, null, null }
                });

            migrationBuilder.InsertData(
                table: "RII_USER_PERMISSION_GROUPS",
                columns: new[] { "Id", "BranchCode", "CreatedBy", "CreatedDate", "DeletedBy", "DeletedDate", "PermissionGroupId", "UpdatedBy", "UpdatedDate", "UserId" },
                values: new object[] { 1001L, "0", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, 1001L, null, null, 1L });

            migrationBuilder.CreateIndex(
                name: "IX_RII_PERMISSION_DEFINITIONS_Code",
                table: "RII_PERMISSION_DEFINITIONS",
                column: "Code",
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_RII_PERMISSION_DEFINITIONS_IsDeleted",
                table: "RII_PERMISSION_DEFINITIONS",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RII_PERMISSION_GROUP_PERMISSIONS_IsDeleted",
                table: "RII_PERMISSION_GROUP_PERMISSIONS",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RII_PERMISSION_GROUP_PERMISSIONS_PermissionDefinitionId",
                table: "RII_PERMISSION_GROUP_PERMISSIONS",
                column: "PermissionDefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_RII_PERMISSION_GROUP_PERMISSIONS_PermissionGroupId_PermissionDefinitionId",
                table: "RII_PERMISSION_GROUP_PERMISSIONS",
                columns: new[] { "PermissionGroupId", "PermissionDefinitionId" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_RII_PERMISSION_GROUPS_IsDeleted",
                table: "RII_PERMISSION_GROUPS",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RII_PERMISSION_GROUPS_Name",
                table: "RII_PERMISSION_GROUPS",
                column: "Name",
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_RII_SMTP_SETTING_IsDeleted",
                table: "RII_SMTP_SETTING",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RII_USER_PERMISSION_GROUPS_IsDeleted",
                table: "RII_USER_PERMISSION_GROUPS",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RII_USER_PERMISSION_GROUPS_PermissionGroupId",
                table: "RII_USER_PERMISSION_GROUPS",
                column: "PermissionGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_RII_USER_PERMISSION_GROUPS_UserId_PermissionGroupId",
                table: "RII_USER_PERMISSION_GROUPS",
                columns: new[] { "UserId", "PermissionGroupId" },
                unique: true,
                filter: "[IsDeleted] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RII_PERMISSION_GROUP_PERMISSIONS");

            migrationBuilder.DropTable(
                name: "RII_SMTP_SETTING");

            migrationBuilder.DropTable(
                name: "RII_USER_PERMISSION_GROUPS");

            migrationBuilder.DropTable(
                name: "RII_PERMISSION_DEFINITIONS");

            migrationBuilder.DropTable(
                name: "RII_PERMISSION_GROUPS");

            migrationBuilder.DropColumn(
                name: "CreatedDate",
                table: "RII_USER_DETAILS");

            migrationBuilder.DropColumn(
                name: "Description",
                table: "RII_USER_DETAILS");

            migrationBuilder.DropColumn(
                name: "Gender",
                table: "RII_USER_DETAILS");

            migrationBuilder.DropColumn(
                name: "Height",
                table: "RII_USER_DETAILS");

            migrationBuilder.DropColumn(
                name: "ProfilePictureUrl",
                table: "RII_USER_DETAILS");

            migrationBuilder.DropColumn(
                name: "UpdatedDate",
                table: "RII_USER_DETAILS");

            migrationBuilder.DropColumn(
                name: "Weight",
                table: "RII_USER_DETAILS");

            migrationBuilder.AlterColumn<string>(
                name: "Phone",
                table: "RII_USER_DETAILS",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(40)",
                oldMaxLength: 40,
                oldNullable: true);
        }
    }
}
