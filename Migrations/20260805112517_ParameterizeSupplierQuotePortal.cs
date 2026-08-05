using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace verii_wms_api_v2.Migrations
{
    /// <inheritdoc />
    public partial class ParameterizeSupplierQuotePortal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AllowSupplierDraftSave",
                table: "RII_PC_POLICY",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "AllowSupplierQuantityChange",
                table: "RII_PC_POLICY",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "AllowSupplierRevisions",
                table: "RII_PC_POLICY",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "AllowZeroUnitPrice",
                table: "RII_PC_POLICY",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "InvitationValidityDays",
                table: "RII_PC_POLICY",
                type: "int",
                nullable: false,
                defaultValue: 7);

            migrationBuilder.AddColumn<int>(
                name: "MaximumSupplierRevisionCount",
                table: "RII_PC_POLICY",
                type: "int",
                nullable: false,
                defaultValue: 3);

            migrationBuilder.AddColumn<bool>(
                name: "RequireSupplierDeliveryDate",
                table: "RII_PC_POLICY",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "SupplierQuoteChannelMode",
                table: "RII_PC_POLICY",
                type: "int",
                nullable: false,
                defaultValue: 2);

            migrationBuilder.AddCheckConstraint(
                name: "CK_RII_PC_POLICY_SUPPLIER_PORTAL",
                table: "RII_PC_POLICY",
                sql: "[SupplierQuoteChannelMode] IN (1, 2, 3) AND [InvitationValidityDays] BETWEEN 1 AND 30 AND [MaximumSupplierRevisionCount] BETWEEN 0 AND 20");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_RII_PC_POLICY_SUPPLIER_PORTAL",
                table: "RII_PC_POLICY");

            migrationBuilder.DropColumn(
                name: "AllowSupplierDraftSave",
                table: "RII_PC_POLICY");

            migrationBuilder.DropColumn(
                name: "AllowSupplierQuantityChange",
                table: "RII_PC_POLICY");

            migrationBuilder.DropColumn(
                name: "AllowSupplierRevisions",
                table: "RII_PC_POLICY");

            migrationBuilder.DropColumn(
                name: "AllowZeroUnitPrice",
                table: "RII_PC_POLICY");

            migrationBuilder.DropColumn(
                name: "InvitationValidityDays",
                table: "RII_PC_POLICY");

            migrationBuilder.DropColumn(
                name: "MaximumSupplierRevisionCount",
                table: "RII_PC_POLICY");

            migrationBuilder.DropColumn(
                name: "RequireSupplierDeliveryDate",
                table: "RII_PC_POLICY");

            migrationBuilder.DropColumn(
                name: "SupplierQuoteChannelMode",
                table: "RII_PC_POLICY");
        }
    }
}
