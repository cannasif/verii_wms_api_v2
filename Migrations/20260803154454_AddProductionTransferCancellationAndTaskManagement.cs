using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace verii_wms_api_v2.Migrations
{
    /// <inheritdoc />
    public partial class AddProductionTransferCancellationAndTaskManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CancellationReturnPolicy",
                table: "RII_WT_POLICIES",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "OriginalSourceLocation");

            migrationBuilder.AddColumn<long>(
                name: "CancellationReturnLocationId",
                table: "RII_WT_HEADER",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CancellationReturnPolicy",
                table: "RII_WT_HEADER",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "OriginalSourceLocation");

            migrationBuilder.AddColumn<long>(
                name: "DefaultTransferReturnLocationId",
                table: "RII_WAREHOUSE",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CancellationReturnPolicy",
                table: "RII_PT_POLICIES",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "OriginalSourceLocation");

            migrationBuilder.InsertData(
                table: "RII_PERMISSION_DEFINITIONS",
                columns: new[] { "Id", "AvailableOnMobile", "AvailableOnWeb", "BranchCode", "Code", "CreatedBy", "CreatedDate", "DeletedBy", "DeletedDate", "Description", "IsActive", "Name", "UpdatedBy", "UpdatedDate" },
                values: new object[] { 2409L, false, true, "0", "WMS.PRODUCTION_TRANSFER.ASSIGN", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, true, "Üretim transfer görevlerini ata ve kaldır", null, null });

            migrationBuilder.InsertData(
                table: "RII_PERMISSION_GROUP_PERMISSIONS",
                columns: new[] { "Id", "BranchCode", "CreatedBy", "CreatedDate", "DeletedBy", "DeletedDate", "PermissionDefinitionId", "PermissionGroupId", "UpdatedBy", "UpdatedDate" },
                values: new object[] { 2409L, "0", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, 2409L, 1001L, null, null });

            migrationBuilder.CreateIndex(
                name: "IX_RII_WAREHOUSE_DEFAULT_TRANSFER_RETURN_LOCATION",
                table: "RII_WAREHOUSE",
                column: "DefaultTransferReturnLocationId");

            migrationBuilder.AddForeignKey(
                name: "FK_RII_WAREHOUSE_RII_LOCATION_DefaultTransferReturnLocationId",
                table: "RII_WAREHOUSE",
                column: "DefaultTransferReturnLocationId",
                principalTable: "RII_LOCATION",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RII_WAREHOUSE_RII_LOCATION_DefaultTransferReturnLocationId",
                table: "RII_WAREHOUSE");

            migrationBuilder.DropIndex(
                name: "IX_RII_WAREHOUSE_DEFAULT_TRANSFER_RETURN_LOCATION",
                table: "RII_WAREHOUSE");

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_GROUP_PERMISSIONS",
                keyColumn: "Id",
                keyValue: 2409L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_DEFINITIONS",
                keyColumn: "Id",
                keyValue: 2409L);

            migrationBuilder.DropColumn(
                name: "CancellationReturnPolicy",
                table: "RII_WT_POLICIES");

            migrationBuilder.DropColumn(
                name: "CancellationReturnLocationId",
                table: "RII_WT_HEADER");

            migrationBuilder.DropColumn(
                name: "CancellationReturnPolicy",
                table: "RII_WT_HEADER");

            migrationBuilder.DropColumn(
                name: "DefaultTransferReturnLocationId",
                table: "RII_WAREHOUSE");

            migrationBuilder.DropColumn(
                name: "CancellationReturnPolicy",
                table: "RII_PT_POLICIES");
        }
    }
}
