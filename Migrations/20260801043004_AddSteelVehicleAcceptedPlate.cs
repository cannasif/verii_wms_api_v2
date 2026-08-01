using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace verii_wms_api_v2.Migrations
{
    /// <inheritdoc />
    public partial class AddSteelVehicleAcceptedPlate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_RII_STEEL_VEHICLE_ACCEPTANCE_QTY",
                table: "RII_STEEL_VEHICLE_ACCEPTANCE");

            migrationBuilder.CreateTable(
                name: "RII_STEEL_VEHICLE_ACCEPTED_PLATE",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    VehicleCheckInId = table.Column<long>(type: "bigint", nullable: false),
                    VehicleAcceptanceId = table.Column<long>(type: "bigint", nullable: false),
                    SequenceNo = table.Column<int>(type: "int", nullable: false),
                    IdentityStatus = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    PlanLineId = table.Column<long>(type: "bigint", nullable: true),
                    ResolvedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ResolvedBy = table.Column<long>(type: "bigint", nullable: true),
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
                    table.PrimaryKey("PK_RII_STEEL_VEHICLE_ACCEPTED_PLATE", x => x.Id);
                    table.CheckConstraint("CK_RII_STEEL_ACCEPTED_PLATE_SEQUENCE", "[SequenceNo] > 0");
                    table.ForeignKey(
                        name: "FK_RII_STEEL_VEHICLE_ACCEPTED_PLATE_RII_STEEL_RECEIPT_PLAN_LINE_PlanLineId",
                        column: x => x.PlanLineId,
                        principalTable: "RII_STEEL_RECEIPT_PLAN_LINE",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RII_STEEL_VEHICLE_ACCEPTED_PLATE_RII_STEEL_VEHICLE_ACCEPTANCE_VehicleAcceptanceId",
                        column: x => x.VehicleAcceptanceId,
                        principalTable: "RII_STEEL_VEHICLE_ACCEPTANCE",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RII_STEEL_VEHICLE_ACCEPTED_PLATE_RII_VEHICLE_CHECKIN_HEADER_VehicleCheckInId",
                        column: x => x.VehicleCheckInId,
                        principalTable: "RII_VEHICLE_CHECKIN_HEADER",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_RII_STEEL_VEHICLE_ACCEPTANCE_QTY",
                table: "RII_STEEL_VEHICLE_ACCEPTANCE",
                sql: "[TotalAcceptedQuantity] >= 0");

            migrationBuilder.CreateIndex(
                name: "IX_RII_STEEL_VEHICLE_ACCEPTED_PLATE_IsDeleted",
                table: "RII_STEEL_VEHICLE_ACCEPTED_PLATE",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RII_STEEL_VEHICLE_ACCEPTED_PLATE_PlanLineId",
                table: "RII_STEEL_VEHICLE_ACCEPTED_PLATE",
                column: "PlanLineId",
                unique: true,
                filter: "[PlanLineId] IS NOT NULL AND [IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_RII_STEEL_VEHICLE_ACCEPTED_PLATE_VehicleAcceptanceId",
                table: "RII_STEEL_VEHICLE_ACCEPTED_PLATE",
                column: "VehicleAcceptanceId");

            migrationBuilder.CreateIndex(
                name: "IX_RII_STEEL_VEHICLE_ACCEPTED_PLATE_VehicleAcceptanceId_SequenceNo",
                table: "RII_STEEL_VEHICLE_ACCEPTED_PLATE",
                columns: new[] { "VehicleAcceptanceId", "SequenceNo" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_RII_STEEL_VEHICLE_ACCEPTED_PLATE_VehicleCheckInId",
                table: "RII_STEEL_VEHICLE_ACCEPTED_PLATE",
                column: "VehicleCheckInId");

            migrationBuilder.Sql(SqlServerMigrationSql.Execute(
                """
                INSERT INTO [RII_PERMISSION_DEFINITIONS]
                (
                    [AvailableOnMobile],
                    [AvailableOnWeb],
                    [BranchCode],
                    [Code],
                    [CreatedDate],
                    [Description],
                    [IsActive],
                    [Name],
                    [IsDeleted]
                )
                SELECT
                    CAST(0 AS bit),
                    CAST(1 AS bit),
                    N'0',
                    N'WMS.VEHICLECHECKIN.UNKNOWN_PLATE_RESOLVE',
                    SYSUTCDATETIME(),
                    N'Created by migration 20260801043004_AddSteelVehicleAcceptedPlate',
                    CAST(1 AS bit),
                    N'Bilinmeyen SAC levhalarını eşleştir',
                    CAST(0 AS bit)
                WHERE NOT EXISTS
                (
                    SELECT 1
                    FROM [RII_PERMISSION_DEFINITIONS]
                    WHERE [Code] = N'WMS.VEHICLECHECKIN.UNKNOWN_PLATE_RESOLVE'
                      AND [IsDeleted] = CAST(0 AS bit)
                );
                """));

            migrationBuilder.Sql(SqlServerMigrationSql.Execute(
                """
                ;WITH LegacyAcceptedLines AS
                (
                    SELECT
                        planLine.[Id] AS [PlanLineId],
                        planLine.[VehicleAcceptanceId],
                        acceptance.[VehicleCheckInId],
                        acceptance.[BranchCode],
                        CAST(ROW_NUMBER() OVER
                        (
                            PARTITION BY planLine.[VehicleAcceptanceId]
                            ORDER BY planLine.[Id]
                        ) AS int) AS [SequenceNo],
                        COALESCE(acceptance.[CreatedBy], acceptance.[AcceptedBy]) AS [CreatedBy],
                        COALESCE(
                            acceptance.[CreatedDate],
                            CONVERT(datetime2, acceptance.[AcceptedAtUtc])
                        ) AS [CreatedDate]
                    FROM [RII_STEEL_RECEIPT_PLAN_LINE] AS planLine
                    INNER JOIN [RII_STEEL_VEHICLE_ACCEPTANCE] AS acceptance
                        ON acceptance.[Id] = planLine.[VehicleAcceptanceId]
                    WHERE planLine.[VehicleAcceptanceId] IS NOT NULL
                      AND planLine.[IsDeleted] = CAST(0 AS bit)
                      AND acceptance.[IsDeleted] = CAST(0 AS bit)
                )
                INSERT INTO [RII_STEEL_VEHICLE_ACCEPTED_PLATE]
                (
                    [VehicleCheckInId],
                    [VehicleAcceptanceId],
                    [SequenceNo],
                    [IdentityStatus],
                    [PlanLineId],
                    [ResolvedAtUtc],
                    [ResolvedBy],
                    [BranchCode],
                    [CreatedDate],
                    [UpdatedDate],
                    [DeletedDate],
                    [IsDeleted],
                    [CreatedBy],
                    [UpdatedBy],
                    [DeletedBy]
                )
                SELECT
                    legacy.[VehicleCheckInId],
                    legacy.[VehicleAcceptanceId],
                    legacy.[SequenceNo],
                    N'Known',
                    legacy.[PlanLineId],
                    NULL,
                    NULL,
                    legacy.[BranchCode],
                    legacy.[CreatedDate],
                    NULL,
                    NULL,
                    CAST(0 AS bit),
                    legacy.[CreatedBy],
                    NULL,
                    NULL
                FROM LegacyAcceptedLines AS legacy
                WHERE NOT EXISTS
                (
                    SELECT 1
                    FROM [RII_STEEL_VEHICLE_ACCEPTED_PLATE] AS existing
                    WHERE existing.[PlanLineId] = legacy.[PlanLineId]
                        AND existing.[IsDeleted] = CAST(0 AS bit)
                );
                """));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                IF EXISTS
                (
                    SELECT 1
                    FROM [RII_STEEL_VEHICLE_ACCEPTED_PLATE]
                    WHERE [IdentityStatus] = N'Unknown'
                      AND [IsDeleted] = CAST(0 AS bit)
                )
                    ;THROW 51000, N'AddSteelVehicleAcceptedPlate geri alınamaz: aktif bilinmeyen levhalar eski şemada temsil edilemez. Önce tüm bilinmeyen levhaları eşleştirin.', 1;

                IF EXISTS
                (
                    SELECT 1
                    FROM [RII_STEEL_VEHICLE_ACCEPTANCE]
                    WHERE [TotalAcceptedQuantity] <= 0
                )
                    ;THROW 51001, N'AddSteelVehicleAcceptedPlate geri alınamaz: sıfır kabul miktarlı kayıtlar eski pozitif miktar kuralına güvenli dönüştürülemiyor.', 1;
                """);

            migrationBuilder.Sql(SqlServerMigrationSql.Execute(
                """
                DELETE groupPermission
                FROM [RII_PERMISSION_GROUP_PERMISSIONS] AS groupPermission
                INNER JOIN [RII_PERMISSION_DEFINITIONS] AS permission
                    ON permission.[Id] = groupPermission.[PermissionDefinitionId]
                WHERE permission.[Code] = N'WMS.VEHICLECHECKIN.UNKNOWN_PLATE_RESOLVE'
                  AND permission.[Description] = N'Created by migration 20260801043004_AddSteelVehicleAcceptedPlate';

                DELETE FROM [RII_PERMISSION_DEFINITIONS]
                WHERE [Code] = N'WMS.VEHICLECHECKIN.UNKNOWN_PLATE_RESOLVE'
                  AND [Description] = N'Created by migration 20260801043004_AddSteelVehicleAcceptedPlate';
                """));

            migrationBuilder.DropTable(
                name: "RII_STEEL_VEHICLE_ACCEPTED_PLATE");

            migrationBuilder.DropCheckConstraint(
                name: "CK_RII_STEEL_VEHICLE_ACCEPTANCE_QTY",
                table: "RII_STEEL_VEHICLE_ACCEPTANCE");

            migrationBuilder.AddCheckConstraint(
                name: "CK_RII_STEEL_VEHICLE_ACCEPTANCE_QTY",
                table: "RII_STEEL_VEHICLE_ACCEPTANCE",
                sql: "[TotalAcceptedQuantity] > 0");
        }
    }
}
