using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace verii_wms_api_v2.Migrations
{
    /// <inheritdoc />
    public partial class ConfigurablePasswordPolicy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PasswordLength",
                table: "RII_USERS",
                type: "int",
                nullable: false,
                defaultValue: 15);

            migrationBuilder.AddColumn<int>(
                name: "PasswordMinimumLength",
                table: "RII_PROJECT_SETTINGS",
                type: "int",
                nullable: false,
                defaultValue: 6);

            migrationBuilder.UpdateData(
                table: "RII_PROJECT_SETTINGS",
                keyColumn: "Id",
                keyValue: 1L,
                column: "PasswordMinimumLength",
                value: 6);

            migrationBuilder.UpdateData(
                table: "RII_USERS",
                keyColumn: "Id",
                keyValue: 1L,
                column: "PasswordLength",
                value: 15);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PasswordLength",
                table: "RII_USERS");

            migrationBuilder.DropColumn(
                name: "PasswordMinimumLength",
                table: "RII_PROJECT_SETTINGS");
        }
    }
}
