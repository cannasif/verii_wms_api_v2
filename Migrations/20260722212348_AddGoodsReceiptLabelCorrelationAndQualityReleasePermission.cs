using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace verii_wms_api_v2.Migrations
{
    /// <inheritdoc />
    public partial class AddGoodsReceiptLabelCorrelationAndQualityReleasePermission : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CorrelationId",
                table: "RII_GR_LABEL_BATCH",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.Sql("UPDATE [RII_GR_LABEL_BATCH] SET [CorrelationId] = NEWID() WHERE [CorrelationId] IS NULL;");

            migrationBuilder.AlterColumn<Guid>(
                name: "CorrelationId",
                table: "RII_GR_LABEL_BATCH",
                type: "uniqueidentifier",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.InsertData(
                table: "RII_PERMISSION_DEFINITIONS",
                columns: new[] { "Id", "AvailableOnMobile", "AvailableOnWeb", "BranchCode", "Code", "CreatedBy", "CreatedDate", "DeletedBy", "DeletedDate", "Description", "IsActive", "Name", "UpdatedBy", "UpdatedDate" },
                values: new object[] { 1052L, false, true, "0", "WMS.QUALITY.INSPECTIONS.RELEASE", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, true, "Karantinadaki ürünü serbest bırak", null, null });

            migrationBuilder.InsertData(
                table: "RII_PERMISSION_GROUP_PERMISSIONS",
                columns: new[] { "Id", "BranchCode", "CreatedBy", "CreatedDate", "DeletedBy", "DeletedDate", "PermissionDefinitionId", "PermissionGroupId", "UpdatedBy", "UpdatedDate" },
                values: new object[] { 1052L, "0", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, 1052L, 1001L, null, null });

            migrationBuilder.CreateIndex(
                name: "UX_RII_GR_LABEL_BATCH_CORRELATION",
                table: "RII_GR_LABEL_BATCH",
                column: "CorrelationId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_RII_GR_LABEL_BATCH_CORRELATION",
                table: "RII_GR_LABEL_BATCH");

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_GROUP_PERMISSIONS",
                keyColumn: "Id",
                keyValue: 1052L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_DEFINITIONS",
                keyColumn: "Id",
                keyValue: 1052L);

            migrationBuilder.DropColumn(
                name: "CorrelationId",
                table: "RII_GR_LABEL_BATCH");
        }
    }
}
