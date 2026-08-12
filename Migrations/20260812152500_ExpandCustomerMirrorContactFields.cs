using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace verii_wms_api_v2.Migrations
{
    /// <inheritdoc />
    public partial class ExpandCustomerMirrorContactFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Address",
                table: "RII_CUSTOMER",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "City",
                table: "RII_CUSTOMER",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CountryCode",
                table: "RII_CUSTOMER",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CustomerType",
                table: "RII_CUSTOMER",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "District",
                table: "RII_CUSTOMER",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Email",
                table: "RII_CUSTOMER",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Phone1",
                table: "RII_CUSTOMER",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Phone2",
                table: "RII_CUSTOMER",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Phone3",
                table: "RII_CUSTOMER",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TaxOffice",
                table: "RII_CUSTOMER",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Website",
                table: "RII_CUSTOMER",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "Address", table: "RII_CUSTOMER");
            migrationBuilder.DropColumn(name: "City", table: "RII_CUSTOMER");
            migrationBuilder.DropColumn(name: "CountryCode", table: "RII_CUSTOMER");
            migrationBuilder.DropColumn(name: "CustomerType", table: "RII_CUSTOMER");
            migrationBuilder.DropColumn(name: "District", table: "RII_CUSTOMER");
            migrationBuilder.DropColumn(name: "Email", table: "RII_CUSTOMER");
            migrationBuilder.DropColumn(name: "Phone1", table: "RII_CUSTOMER");
            migrationBuilder.DropColumn(name: "Phone2", table: "RII_CUSTOMER");
            migrationBuilder.DropColumn(name: "Phone3", table: "RII_CUSTOMER");
            migrationBuilder.DropColumn(name: "TaxOffice", table: "RII_CUSTOMER");
            migrationBuilder.DropColumn(name: "Website", table: "RII_CUSTOMER");
        }
    }
}
