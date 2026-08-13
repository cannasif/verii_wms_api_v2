using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace verii_wms_api_v2.Migrations
{
    /// <inheritdoc />
    public partial class AddQualityInspectionPriority : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsPriority",
                table: "RII_QUALITY_INSPECTIONS",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.InsertData(
                table: "RII_PERMISSION_DEFINITIONS",
                columns: new[] { "Id", "AvailableOnMobile", "AvailableOnWeb", "BranchCode", "Code", "CreatedBy", "CreatedDate", "DeletedBy", "DeletedDate", "Description", "IsActive", "Name", "UpdatedBy", "UpdatedDate" },
                values: new object[] { 10520L, false, true, "0", "WMS.QUALITY.INSPECTIONS.PRIORITIZE", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, "Açık kalite inceleme kayıtlarının operasyon sırasını önceliklendirmeye izin verir.", true, "GKK kayıtlarına öncelik ver ve önceliği kaldır", null, null });

            migrationBuilder.CreateIndex(
                name: "IX_RII_QUALITY_INSPECTIONS_BranchCode_IsPriority_Status_QueuedAtUtc",
                table: "RII_QUALITY_INSPECTIONS",
                columns: new[] { "BranchCode", "IsPriority", "Status", "QueuedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RII_QUALITY_INSPECTIONS_BranchCode_IsPriority_Status_QueuedAtUtc",
                table: "RII_QUALITY_INSPECTIONS");

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_DEFINITIONS",
                keyColumn: "Id",
                keyValue: 10520L);

            migrationBuilder.DropColumn(
                name: "IsPriority",
                table: "RII_QUALITY_INSPECTIONS");
        }
    }
}
