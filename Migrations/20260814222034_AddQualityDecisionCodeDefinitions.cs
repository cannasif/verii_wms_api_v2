using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace verii_wms_api_v2.Migrations
{
    /// <inheritdoc />
    public partial class AddQualityDecisionCodeDefinitions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "DecisionCodeId",
                table: "RII_QUALITY_INSPECTION_LINES",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "DecisionCodeId",
                table: "RII_QUALITY_INSPECTION_DISPOSITIONS",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "RII_QUALITY_DECISION_CODES",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ApplicableDecision = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    RequiresNote = table.Column<bool>(type: "bit", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
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
                    table.PrimaryKey("PK_RII_QUALITY_DECISION_CODES", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "RII_PERMISSION_DEFINITIONS",
                columns: new[] { "Id", "AvailableOnMobile", "AvailableOnWeb", "BranchCode", "Code", "CreatedBy", "CreatedDate", "DeletedBy", "DeletedDate", "Description", "IsActive", "Name", "UpdatedBy", "UpdatedDate" },
                values: new object[,]
                {
                    { 10523L, false, true, "0", "WMS.QUALITY.DECISION_CODES.VIEW", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, "Kalite kararında kullanılacak tanımlı karar kodlarını görüntülemeye izin verir.", true, "Kalite karar kodlarını görüntüle", null, null },
                    { 10524L, false, true, "0", "WMS.QUALITY.DECISION_CODES.MANAGE", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, "Kalite karar kodu oluşturma, güncelleme ve pasife alma işlemlerine izin verir.", true, "Kalite karar kodlarını yönet", null, null }
                });

            migrationBuilder.InsertData(
                table: "RII_PERMISSION_GROUP_PERMISSIONS",
                columns: new[] { "Id", "BranchCode", "CreatedBy", "CreatedDate", "DeletedBy", "DeletedDate", "PermissionDefinitionId", "PermissionGroupId", "UpdatedBy", "UpdatedDate" },
                values: new object[,]
                {
                    { 10523L, "0", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, 10523L, 1001L, null, null },
                    { 10524L, "0", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, 10524L, 1001L, null, null }
                });

            migrationBuilder.CreateIndex(
                name: "IX_RII_QUALITY_INSPECTION_LINES_DecisionCodeId",
                table: "RII_QUALITY_INSPECTION_LINES",
                column: "DecisionCodeId");

            migrationBuilder.CreateIndex(
                name: "IX_RII_QUALITY_INSPECTION_DISPOSITIONS_DecisionCodeId",
                table: "RII_QUALITY_INSPECTION_DISPOSITIONS",
                column: "DecisionCodeId");

            migrationBuilder.CreateIndex(
                name: "IX_RII_QUALITY_DECISION_CODES_BranchCode_Code",
                table: "RII_QUALITY_DECISION_CODES",
                columns: new[] { "BranchCode", "Code" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_RII_QUALITY_DECISION_CODES_BranchCode_IsActive_ApplicableDecision_SortOrder",
                table: "RII_QUALITY_DECISION_CODES",
                columns: new[] { "BranchCode", "IsActive", "ApplicableDecision", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_RII_QUALITY_DECISION_CODES_IsDeleted",
                table: "RII_QUALITY_DECISION_CODES",
                column: "IsDeleted");

            migrationBuilder.AddForeignKey(
                name: "FK_RII_QUALITY_INSPECTION_DISPOSITIONS_RII_QUALITY_DECISION_CODES_DecisionCodeId",
                table: "RII_QUALITY_INSPECTION_DISPOSITIONS",
                column: "DecisionCodeId",
                principalTable: "RII_QUALITY_DECISION_CODES",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_RII_QUALITY_INSPECTION_LINES_RII_QUALITY_DECISION_CODES_DecisionCodeId",
                table: "RII_QUALITY_INSPECTION_LINES",
                column: "DecisionCodeId",
                principalTable: "RII_QUALITY_DECISION_CODES",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RII_QUALITY_INSPECTION_DISPOSITIONS_RII_QUALITY_DECISION_CODES_DecisionCodeId",
                table: "RII_QUALITY_INSPECTION_DISPOSITIONS");

            migrationBuilder.DropForeignKey(
                name: "FK_RII_QUALITY_INSPECTION_LINES_RII_QUALITY_DECISION_CODES_DecisionCodeId",
                table: "RII_QUALITY_INSPECTION_LINES");

            migrationBuilder.DropTable(
                name: "RII_QUALITY_DECISION_CODES");

            migrationBuilder.DropIndex(
                name: "IX_RII_QUALITY_INSPECTION_LINES_DecisionCodeId",
                table: "RII_QUALITY_INSPECTION_LINES");

            migrationBuilder.DropIndex(
                name: "IX_RII_QUALITY_INSPECTION_DISPOSITIONS_DecisionCodeId",
                table: "RII_QUALITY_INSPECTION_DISPOSITIONS");

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_GROUP_PERMISSIONS",
                keyColumn: "Id",
                keyValue: 10523L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_GROUP_PERMISSIONS",
                keyColumn: "Id",
                keyValue: 10524L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_DEFINITIONS",
                keyColumn: "Id",
                keyValue: 10523L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_DEFINITIONS",
                keyColumn: "Id",
                keyValue: 10524L);

            migrationBuilder.DropColumn(
                name: "DecisionCodeId",
                table: "RII_QUALITY_INSPECTION_LINES");

            migrationBuilder.DropColumn(
                name: "DecisionCodeId",
                table: "RII_QUALITY_INSPECTION_DISPOSITIONS");
        }
    }
}
