using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace verii_wms_api_v2.Migrations;

public partial class AddGoodsReceiptLocationSelectionPolicy : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "LocationSelectionPolicy",
            table: "RII_GR_POLICIES",
            type: "nvarchar(50)",
            maxLength: 50,
            nullable: false,
            defaultValue: "ReceivingOrStagingOnly");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "LocationSelectionPolicy",
            table: "RII_GR_POLICIES");
    }
}
