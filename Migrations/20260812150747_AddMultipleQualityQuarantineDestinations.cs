using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace verii_wms_api_v2.Migrations
{
    /// <inheritdoc />
    public partial class AddMultipleQualityQuarantineDestinations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "QuarantineLocationId",
                table: "RII_QUALITY_INSPECTION_LINES",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "RII_QUALITY_QUARANTINE_DESTINATIONS",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    QualityParameterId = table.Column<long>(type: "bigint", nullable: false),
                    LocationId = table.Column<long>(type: "bigint", nullable: false),
                    Priority = table.Column<int>(type: "int", nullable: false, defaultValue: 100),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
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
                    table.PrimaryKey("PK_RII_QUALITY_QUARANTINE_DESTINATIONS", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RII_QUALITY_QUARANTINE_DESTINATIONS_RII_LOCATION_LocationId",
                        column: x => x.LocationId,
                        principalTable: "RII_LOCATION",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RII_QUALITY_QUARANTINE_DESTINATIONS_RII_QUALITY_PARAMETERS_QualityParameterId",
                        column: x => x.QualityParameterId,
                        principalTable: "RII_QUALITY_PARAMETERS",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RII_QUALITY_INSPECTION_LINES_QuarantineLocationId",
                table: "RII_QUALITY_INSPECTION_LINES",
                column: "QuarantineLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_RII_QUALITY_QUARANTINE_DESTINATIONS_IsDeleted",
                table: "RII_QUALITY_QUARANTINE_DESTINATIONS",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RII_QUALITY_QUARANTINE_DESTINATIONS_LocationId",
                table: "RII_QUALITY_QUARANTINE_DESTINATIONS",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_RII_QUALITY_QUARANTINE_DESTINATIONS_QualityParameterId_IsActive_Priority",
                table: "RII_QUALITY_QUARANTINE_DESTINATIONS",
                columns: new[] { "QualityParameterId", "IsActive", "Priority" });

            migrationBuilder.CreateIndex(
                name: "IX_RII_QUALITY_QUARANTINE_DESTINATIONS_QualityParameterId_LocationId",
                table: "RII_QUALITY_QUARANTINE_DESTINATIONS",
                columns: new[] { "QualityParameterId", "LocationId" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.Sql(
                """
                INSERT INTO [RII_QUALITY_QUARANTINE_DESTINATIONS]
                    ([QualityParameterId], [LocationId], [Priority], [IsActive], [BranchCode], [CreatedDate], [IsDeleted])
                SELECT [Id], [DefaultQuarantineLocationId], 100, 1, [BranchCode], SYSUTCDATETIME(), 0
                FROM [RII_QUALITY_PARAMETERS]
                WHERE [IsDeleted] = 0
                  AND [DefaultQuarantineLocationId] IS NOT NULL;

                UPDATE qualityLine
                SET [QuarantineLocationId] = parameter.[DefaultQuarantineLocationId]
                FROM [RII_QUALITY_INSPECTION_LINES] qualityLine
                INNER JOIN [RII_QUALITY_INSPECTIONS] inspection
                    ON inspection.[Id] = qualityLine.[QualityInspectionId]
                INNER JOIN [RII_QUALITY_PARAMETERS] parameter
                    ON parameter.[BranchCode] = inspection.[BranchCode]
                   AND parameter.[ParameterKey] = 'DEFAULT'
                   AND parameter.[IsDeleted] = 0
                WHERE qualityLine.[IsDeleted] = 0
                  AND qualityLine.[QuarantineQuantity] > 0
                  AND qualityLine.[QuarantineLocationId] IS NULL
                  AND parameter.[DefaultQuarantineLocationId] IS NOT NULL;
                """);

            migrationBuilder.AddForeignKey(
                name: "FK_RII_QUALITY_INSPECTION_LINES_RII_LOCATION_QuarantineLocationId",
                table: "RII_QUALITY_INSPECTION_LINES",
                column: "QuarantineLocationId",
                principalTable: "RII_LOCATION",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RII_QUALITY_INSPECTION_LINES_RII_LOCATION_QuarantineLocationId",
                table: "RII_QUALITY_INSPECTION_LINES");

            migrationBuilder.DropTable(
                name: "RII_QUALITY_QUARANTINE_DESTINATIONS");

            migrationBuilder.DropIndex(
                name: "IX_RII_QUALITY_INSPECTION_LINES_QuarantineLocationId",
                table: "RII_QUALITY_INSPECTION_LINES");

            migrationBuilder.DropColumn(
                name: "QuarantineLocationId",
                table: "RII_QUALITY_INSPECTION_LINES");
        }
    }
}
