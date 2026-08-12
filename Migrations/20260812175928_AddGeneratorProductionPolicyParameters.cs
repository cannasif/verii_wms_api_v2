using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace verii_wms_api_v2.Migrations
{
    /// <inheritdoc />
    public partial class AddGeneratorProductionPolicyParameters : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "RII_GP_STATION_SHIFT",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "RII_GP_SHIFT",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.AddColumn<bool>(
                name: "IsSystemRequired",
                table: "RII_GP_RULE",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "RII_GP_RESOURCE",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.CreateTable(
                name: "RII_GP_POLICY",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PolicyKey = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    MinimumProjectPriority = table.Column<int>(type: "int", nullable: false),
                    MaximumProjectPriority = table.Column<int>(type: "int", nullable: false),
                    DefaultProjectPriority = table.Column<int>(type: "int", nullable: false),
                    DefaultProjectQuantity = table.Column<int>(type: "int", nullable: false),
                    MaximumProjectQuantity = table.Column<int>(type: "int", nullable: false),
                    DefaultLeadTimeDays = table.Column<int>(type: "int", nullable: false),
                    MinimumPlanReasonLength = table.Column<int>(type: "int", nullable: false),
                    MinimumOperationReasonLength = table.Column<int>(type: "int", nullable: false),
                    MaximumScheduleRangeDays = table.Column<int>(type: "int", nullable: false),
                    SchedulePastDays = table.Column<int>(type: "int", nullable: false),
                    ScheduleFutureDays = table.Column<int>(type: "int", nullable: false),
                    GanttDefaultWindowDays = table.Column<int>(type: "int", nullable: false),
                    AndonRefreshSeconds = table.Column<int>(type: "int", nullable: false),
                    WorkingCalendarSearchLimitDays = table.Column<int>(type: "int", nullable: false),
                    RequireComponentForFinalAssembly = table.Column<bool>(type: "bit", nullable: false),
                    RequireMaterialAvailabilityToStart = table.Column<bool>(type: "bit", nullable: false),
                    RequireProblemClosureToComplete = table.Column<bool>(type: "bit", nullable: false),
                    RequirePositiveCompletionQuantity = table.Column<bool>(type: "bit", nullable: false),
                    PlanningOrderStrategy = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
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
                    table.PrimaryKey("PK_RII_GP_POLICY", x => x.Id);
                    table.CheckConstraint("CK_RII_GP_POLICY_DAYS", "[DefaultLeadTimeDays] > 0 AND [MaximumScheduleRangeDays] > 0 AND [SchedulePastDays] >= 0 AND [ScheduleFutureDays] > 0 AND [SchedulePastDays] + [ScheduleFutureDays] <= [MaximumScheduleRangeDays] AND [GanttDefaultWindowDays] BETWEEN 1 AND [MaximumScheduleRangeDays] AND [WorkingCalendarSearchLimitDays] > 0");
                    table.CheckConstraint("CK_RII_GP_POLICY_PRIORITY", "[MinimumProjectPriority] >= 0 AND [MaximumProjectPriority] <= 100 AND [MinimumProjectPriority] <= [DefaultProjectPriority] AND [DefaultProjectPriority] <= [MaximumProjectPriority]");
                    table.CheckConstraint("CK_RII_GP_POLICY_QUANTITY", "[DefaultProjectQuantity] > 0 AND [DefaultProjectQuantity] <= [MaximumProjectQuantity] AND [MaximumProjectQuantity] <= 10000");
                    table.CheckConstraint("CK_RII_GP_POLICY_REASON", "[MinimumPlanReasonLength] BETWEEN 3 AND 1000 AND [MinimumOperationReasonLength] BETWEEN 3 AND 1000");
                    table.CheckConstraint("CK_RII_GP_POLICY_REFRESH", "[AndonRefreshSeconds] BETWEEN 5 AND 3600");
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_RII_GP_ROUTE_DEPENDENCY_LAG",
                table: "RII_GP_ROUTE_DEPENDENCY",
                sql: "[LagMinutes] BETWEEN -10080 AND 10080");

            migrationBuilder.CreateIndex(
                name: "IX_RII_GP_POLICY_BranchCode_PolicyKey",
                table: "RII_GP_POLICY",
                columns: new[] { "BranchCode", "PolicyKey" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_RII_GP_POLICY_IsDeleted",
                table: "RII_GP_POLICY",
                column: "IsDeleted");

            migrationBuilder.Sql("""
                INSERT INTO [RII_GP_POLICY]
                    ([PolicyKey], [MinimumProjectPriority], [MaximumProjectPriority], [DefaultProjectPriority],
                     [DefaultProjectQuantity], [MaximumProjectQuantity], [DefaultLeadTimeDays],
                     [MinimumPlanReasonLength], [MinimumOperationReasonLength], [MaximumScheduleRangeDays],
                     [SchedulePastDays], [ScheduleFutureDays], [GanttDefaultWindowDays], [AndonRefreshSeconds],
                     [WorkingCalendarSearchLimitDays], [RequireComponentForFinalAssembly],
                     [RequireMaterialAvailabilityToStart], [RequireProblemClosureToComplete],
                     [RequirePositiveCompletionQuantity], [PlanningOrderStrategy], [BranchCode], [CreatedDate], [IsDeleted])
                SELECT 'DEFAULT', 0, 100, 50, 1, 100, 30, 5, 3, 366, 60, 180, 45, 15, 3660,
                       1, 1, 1, 1, 'PriorityThenDelivery', branches.[BranchCode], SYSUTCDATETIME(), 0
                FROM (
                    SELECT [BranchCode] FROM [RII_GP_STATION]
                    UNION SELECT [BranchCode] FROM [RII_GP_RULE]
                    UNION SELECT [BranchCode] FROM [RII_GP_PROJECT]
                ) branches
                WHERE NOT EXISTS (
                    SELECT 1 FROM [RII_GP_POLICY] policy
                    WHERE policy.[BranchCode] = branches.[BranchCode]
                      AND policy.[PolicyKey] = 'DEFAULT'
                      AND policy.[IsDeleted] = 0);

                UPDATE [RII_GP_RULE]
                SET [IsSystemRequired] = 1
                WHERE [Code] IN ('RULE_DEFINITION', 'ROUTE_DEFINITION', 'CAPACITY_OVERLOAD',
                    'OPERATION_CONFLICT', 'DEPENDENCY_VIOLATION', 'MATERIAL_SHORTAGE', 'LINE_UNAVAILABLE',
                    'SHIFT_CAPACITY_EXCEEDED', 'HOLIDAY_CONFLICT', 'PARALLEL_JOB_LIMIT',
                    'MIN_MAX_OPERATION_DURATION', 'INACTIVE_LINE_USAGE')
                  AND [IsDeleted] = 0;

                UPDATE [RII_GP_RULE]
                SET [ParametersJson] = N'{"toleranceMinutes":0}'
                WHERE [Code] = 'DELIVERY_DATE_RISK'
                  AND ([ParametersJson] IS NULL OR LTRIM(RTRIM([ParametersJson])) = '')
                  AND [IsDeleted] = 0;

                INSERT INTO [RII_GP_RULE]
                    ([Code], [Name], [Description], [Severity], [IsEnabled], [IsSystemRequired],
                     [ParametersJson], [BranchCode], [CreatedDate], [IsDeleted])
                SELECT definitions.[Code], definitions.[Name], definitions.[Description], 'Error', 1, 1,
                       NULL, branches.[BranchCode], SYSUTCDATETIME(), 0
                FROM (
                    SELECT [BranchCode] FROM [RII_GP_STATION]
                    UNION SELECT [BranchCode] FROM [RII_GP_RULE]
                    UNION SELECT [BranchCode] FROM [RII_GP_PROJECT]
                ) branches
                CROSS JOIN (VALUES
                    ('RULE_DEFINITION', N'Kural tanım bütünlüğü', N'Plan motorunun kullandığı zorunlu kurallar eksiksiz tanımlı olmalıdır.'),
                    ('ROUTE_DEFINITION', N'Rota tanım bütünlüğü', N'Seçilen her bileşen için tek bir aktif ve geçerli rota bulunmalıdır.')
                ) definitions([Code], [Name], [Description])
                WHERE NOT EXISTS (
                    SELECT 1 FROM [RII_GP_RULE] rule
                    WHERE rule.[BranchCode] = branches.[BranchCode]
                      AND rule.[Code] = definitions.[Code]
                      AND rule.[IsDeleted] = 0);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RII_GP_POLICY");

            migrationBuilder.DropCheckConstraint(
                name: "CK_RII_GP_ROUTE_DEPENDENCY_LAG",
                table: "RII_GP_ROUTE_DEPENDENCY");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "RII_GP_STATION_SHIFT");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "RII_GP_SHIFT");

            migrationBuilder.DropColumn(
                name: "IsSystemRequired",
                table: "RII_GP_RULE");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "RII_GP_RESOURCE");
        }
    }
}
