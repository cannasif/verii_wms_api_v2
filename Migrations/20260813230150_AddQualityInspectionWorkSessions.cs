using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814

namespace verii_wms_api_v2.Migrations
{
    public partial class AddQualityInspectionWorkSessions : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RII_QUALITY_INSPECTION_WORK_SESSIONS",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    QualityInspectionId = table.Column<long>(type: "bigint", nullable: false),
                    SequenceNo = table.Column<int>(type: "int", nullable: false),
                    WorkerUserId = table.Column<long>(type: "bigint", nullable: false),
                    WorkerNameSnapshot = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    StartedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    EndedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    DurationSeconds = table.Column<long>(type: "bigint", nullable: false),
                    StopReason = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    StopNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    StartIdempotencyKey = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EndIdempotencyKey = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    EndedByUserId = table.Column<long>(type: "bigint", nullable: true),
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
                    table.PrimaryKey("PK_RII_QUALITY_INSPECTION_WORK_SESSIONS", x => x.Id);
                    table.CheckConstraint("CK_RII_QUALITY_WORK_SESSION_DURATION", "[DurationSeconds] >= 0");
                    table.CheckConstraint("CK_RII_QUALITY_WORK_SESSION_END", "[EndedAtUtc] IS NULL OR [EndedAtUtc] >= [StartedAtUtc]");
                    table.ForeignKey(
                        name: "FK_RII_QUALITY_INSPECTION_WORK_SESSIONS_RII_QUALITY_INSPECTIONS_QualityInspectionId",
                        column: x => x.QualityInspectionId,
                        principalTable: "RII_QUALITY_INSPECTIONS",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "RII_PERMISSION_DEFINITIONS",
                columns: new[] { "Id", "AvailableOnMobile", "AvailableOnWeb", "BranchCode", "Code", "CreatedBy", "CreatedDate", "DeletedBy", "DeletedDate", "Description", "IsActive", "Name", "UpdatedBy", "UpdatedDate" },
                values: new object[,]
                {
                    { 10521L, true, true, "0", "WMS.QUALITY.INSPECTIONS.EXECUTE", null, new DateTime(2026, 7, 21, 0, 0, 0, DateTimeKind.Utc), null, null, "Kalite incelemesinde kişisel çalışma oturumu açmaya ve duruş bildirmeye izin verir.", true, "GKK incelemesini başlat, durdur ve devam ettir", null, null },
                    { 10522L, false, true, "0", "WMS.QUALITY.INSPECTIONS.SUPERVISE", null, new DateTime(2026, 7, 21, 0, 0, 0, DateTimeKind.Utc), null, null, "Başka bir kullanıcının açık GKK oturumunu durdurmaya ve vardiya devrini yönetmeye izin verir.", true, "GKK çalışma oturumlarını yönet", null, null }
                });

            migrationBuilder.InsertData(
                table: "RII_PERMISSION_GROUP_PERMISSIONS",
                columns: new[] { "Id", "BranchCode", "CreatedBy", "CreatedDate", "DeletedBy", "DeletedDate", "PermissionDefinitionId", "PermissionGroupId", "UpdatedBy", "UpdatedDate" },
                values: new object[,]
                {
                    { 10520L, "0", null, new DateTime(2026, 7, 21, 0, 0, 0, DateTimeKind.Utc), null, null, 10520L, 1001L, null, null },
                    { 10521L, "0", null, new DateTime(2026, 7, 21, 0, 0, 0, DateTimeKind.Utc), null, null, 10521L, 1001L, null, null },
                    { 10522L, "0", null, new DateTime(2026, 7, 21, 0, 0, 0, DateTimeKind.Utc), null, null, 10522L, 1001L, null, null }
                });

            migrationBuilder.CreateIndex(
                name: "IX_RII_QUALITY_INSPECTION_WORK_SESSIONS_EndIdempotencyKey",
                table: "RII_QUALITY_INSPECTION_WORK_SESSIONS",
                column: "EndIdempotencyKey",
                unique: true,
                filter: "[EndIdempotencyKey] IS NOT NULL AND [IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_RII_QUALITY_INSPECTION_WORK_SESSIONS_IsDeleted",
                table: "RII_QUALITY_INSPECTION_WORK_SESSIONS",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RII_QUALITY_INSPECTION_WORK_SESSIONS_QualityInspectionId",
                table: "RII_QUALITY_INSPECTION_WORK_SESSIONS",
                column: "QualityInspectionId",
                unique: true,
                filter: "[EndedAtUtc] IS NULL AND [IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_RII_QUALITY_INSPECTION_WORK_SESSIONS_QualityInspectionId_SequenceNo",
                table: "RII_QUALITY_INSPECTION_WORK_SESSIONS",
                columns: new[] { "QualityInspectionId", "SequenceNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RII_QUALITY_INSPECTION_WORK_SESSIONS_QualityInspectionId_StartIdempotencyKey",
                table: "RII_QUALITY_INSPECTION_WORK_SESSIONS",
                columns: new[] { "QualityInspectionId", "StartIdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RII_QUALITY_INSPECTION_WORK_SESSIONS_WorkerUserId_StartedAtUtc",
                table: "RII_QUALITY_INSPECTION_WORK_SESSIONS",
                columns: new[] { "WorkerUserId", "StartedAtUtc" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(table: "RII_PERMISSION_GROUP_PERMISSIONS", keyColumn: "Id", keyValue: 10520L);
            migrationBuilder.DeleteData(table: "RII_PERMISSION_GROUP_PERMISSIONS", keyColumn: "Id", keyValue: 10521L);
            migrationBuilder.DeleteData(table: "RII_PERMISSION_GROUP_PERMISSIONS", keyColumn: "Id", keyValue: 10522L);
            migrationBuilder.DeleteData(table: "RII_PERMISSION_DEFINITIONS", keyColumn: "Id", keyValue: 10521L);
            migrationBuilder.DeleteData(table: "RII_PERMISSION_DEFINITIONS", keyColumn: "Id", keyValue: 10522L);
            migrationBuilder.DropTable(name: "RII_QUALITY_INSPECTION_WORK_SESSIONS");
        }
    }
}
