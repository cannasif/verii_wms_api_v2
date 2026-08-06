using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace verii_wms_api_v2.Migrations
{
    /// <inheritdoc />
    public partial class AddGoodsReceiptErpQualityGate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ErpQualityGatePolicy",
                table: "RII_GR_POLICIES",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "AnyQualityPlan");

            migrationBuilder.AddColumn<string>(
                name: "QualityRoutingSource",
                table: "RII_GR_LINE",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "None");

            migrationBuilder.AddColumn<string>(
                name: "ErpQualityGatePolicy",
                table: "RII_GR_HEADER",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "AnyQualityPlan");

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ErpQualityGatePolicy",
                table: "RII_GR_POLICIES");

            migrationBuilder.DropColumn(
                name: "QualityRoutingSource",
                table: "RII_GR_LINE");

            migrationBuilder.DropColumn(
                name: "ErpQualityGatePolicy",
                table: "RII_GR_HEADER");
        }
    }
}
