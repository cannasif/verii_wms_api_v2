using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace verii_wms_api_v2.Migrations
{
    /// <inheritdoc />
    public partial class AddUserBackgroundMotionPreferences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "BackgroundMotionEnabled",
                table: "RII_USER_DETAILS",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "BackgroundMotionVariant",
                table: "RII_USER_DETAILS",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "rack-scanner");

            migrationBuilder.UpdateData(
                table: "RII_USER_DETAILS",
                keyColumn: "UserId",
                keyValue: 1L,
                columns: new[] { "BackgroundMotionEnabled", "BackgroundMotionVariant" },
                values: new object[] { false, "rack-scanner" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BackgroundMotionEnabled",
                table: "RII_USER_DETAILS");

            migrationBuilder.DropColumn(
                name: "BackgroundMotionVariant",
                table: "RII_USER_DETAILS");
        }
    }
}
