using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace verii_wms_api_v2.Migrations
{
    /// <inheritdoc />
    public partial class AddGeneratorProductionFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RII_GP_PROJECT",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProductionHeaderId = table.Column<long>(type: "bigint", nullable: true),
                    ProjectCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ProjectName = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    GeneratorType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    SerialNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CustomerCodeSnapshot = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CustomerNameSnapshot = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    ExternalWorkOrderNo = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    SourceSystemCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    PlannedStartAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PlannedDeliveryAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Priority = table.Column<int>(type: "int", nullable: false),
                    Quantity = table.Column<int>(type: "int", nullable: false),
                    HasStator = table.Column<bool>(type: "bit", nullable: false),
                    HasRotor = table.Column<bool>(type: "bit", nullable: false),
                    HasStiffener = table.Column<bool>(type: "bit", nullable: false),
                    IncludeFinalAssembly = table.Column<bool>(type: "bit", nullable: false),
                    PlanningOrder = table.Column<int>(type: "int", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
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
                    table.PrimaryKey("PK_RII_GP_PROJECT", x => x.Id);
                    table.CheckConstraint("CK_RII_GP_PROJECT_VALUES", "[Quantity] > 0 AND [Priority] BETWEEN 0 AND 100 AND [PlannedDeliveryAtUtc] >= [PlannedStartAtUtc]");
                    table.ForeignKey(
                        name: "FK_RII_GP_PROJECT_RII_PR_HEADER_ProductionHeaderId",
                        column: x => x.ProductionHeaderId,
                        principalTable: "RII_PR_HEADER",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RII_GP_RESOURCE",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ResourceType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Capacity = table.Column<int>(type: "int", nullable: false),
                    IsExclusive = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
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
                    table.PrimaryKey("PK_RII_GP_RESOURCE", x => x.Id);
                    table.CheckConstraint("CK_RII_GP_RESOURCE_CAPACITY", "[Capacity] > 0");
                });

            migrationBuilder.CreateTable(
                name: "RII_GP_ROUTE",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    PartType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    VersionNumber = table.Column<int>(type: "int", nullable: false),
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
                    table.PrimaryKey("PK_RII_GP_ROUTE", x => x.Id);
                    table.CheckConstraint("CK_RII_GP_ROUTE_VERSION", "[VersionNumber] > 0");
                });

            migrationBuilder.CreateTable(
                name: "RII_GP_RULE",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Severity = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false),
                    ParametersJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
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
                    table.PrimaryKey("PK_RII_GP_RULE", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RII_GP_SHIFT",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    StartTime = table.Column<TimeOnly>(type: "time", nullable: false),
                    EndTime = table.Column<TimeOnly>(type: "time", nullable: false),
                    PlanningOrder = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
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
                    table.PrimaryKey("PK_RII_GP_SHIFT", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RII_GP_STATION",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Area = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    PlanningOrder = table.Column<int>(type: "int", nullable: false),
                    MaxParallelJobs = table.Column<int>(type: "int", nullable: false),
                    DefaultPersonnelCapacity = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    IsCritical = table.Column<bool>(type: "bit", nullable: false),
                    IsBottleneck = table.Column<bool>(type: "bit", nullable: false),
                    RequiresCrane = table.Column<bool>(type: "bit", nullable: false),
                    RequiresTransport = table.Column<bool>(type: "bit", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
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
                    table.PrimaryKey("PK_RII_GP_STATION", x => x.Id);
                    table.CheckConstraint("CK_RII_GP_STATION_CAPACITY", "[MaxParallelJobs] > 0 AND [DefaultPersonnelCapacity] >= 0");
                });

            migrationBuilder.CreateTable(
                name: "RII_GP_PLAN_REVISION",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProjectId = table.Column<long>(type: "bigint", nullable: true),
                    ActionType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    PreviousPlanJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    NewPlanJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ActorUserId = table.Column<long>(type: "bigint", nullable: false),
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
                    table.PrimaryKey("PK_RII_GP_PLAN_REVISION", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RII_GP_PLAN_REVISION_RII_GP_PROJECT_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "RII_GP_PROJECT",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RII_GP_CALENDAR_EXCEPTION",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StationId = table.Column<long>(type: "bigint", nullable: true),
                    ShiftId = table.Column<long>(type: "bigint", nullable: true),
                    ExceptionDate = table.Column<DateOnly>(type: "date", nullable: false),
                    IsWorking = table.Column<bool>(type: "bit", nullable: false),
                    CapacityMinutes = table.Column<int>(type: "int", nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
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
                    table.PrimaryKey("PK_RII_GP_CALENDAR_EXCEPTION", x => x.Id);
                    table.CheckConstraint("CK_RII_GP_CALENDAR_EXCEPTION_CAPACITY", "[CapacityMinutes] IS NULL OR [CapacityMinutes] >= 0");
                    table.ForeignKey(
                        name: "FK_RII_GP_CALENDAR_EXCEPTION_RII_GP_SHIFT_ShiftId",
                        column: x => x.ShiftId,
                        principalTable: "RII_GP_SHIFT",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RII_GP_CALENDAR_EXCEPTION_RII_GP_STATION_StationId",
                        column: x => x.StationId,
                        principalTable: "RII_GP_STATION",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RII_GP_ROUTE_OPERATION",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RouteId = table.Column<long>(type: "bigint", nullable: false),
                    StationId = table.Column<long>(type: "bigint", nullable: false),
                    OperationCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    OperationName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    DurationMinutes = table.Column<int>(type: "int", nullable: false),
                    MinimumDurationMinutes = table.Column<int>(type: "int", nullable: false),
                    MaximumDurationMinutes = table.Column<int>(type: "int", nullable: false),
                    IsCritical = table.Column<bool>(type: "bit", nullable: false),
                    IsRequired = table.Column<bool>(type: "bit", nullable: false),
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
                    table.PrimaryKey("PK_RII_GP_ROUTE_OPERATION", x => x.Id);
                    table.CheckConstraint("CK_RII_GP_ROUTE_OPERATION_DURATION", "[Sequence] > 0 AND [DurationMinutes] > 0 AND [MinimumDurationMinutes] > 0 AND [MaximumDurationMinutes] >= [MinimumDurationMinutes] AND [DurationMinutes] BETWEEN [MinimumDurationMinutes] AND [MaximumDurationMinutes]");
                    table.ForeignKey(
                        name: "FK_RII_GP_ROUTE_OPERATION_RII_GP_ROUTE_RouteId",
                        column: x => x.RouteId,
                        principalTable: "RII_GP_ROUTE",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RII_GP_ROUTE_OPERATION_RII_GP_STATION_StationId",
                        column: x => x.StationId,
                        principalTable: "RII_GP_STATION",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RII_GP_STATION_RESOURCE",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StationId = table.Column<long>(type: "bigint", nullable: false),
                    ResourceId = table.Column<long>(type: "bigint", nullable: false),
                    RequiredQuantity = table.Column<int>(type: "int", nullable: false),
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
                    table.PrimaryKey("PK_RII_GP_STATION_RESOURCE", x => x.Id);
                    table.CheckConstraint("CK_RII_GP_STATION_RESOURCE_QTY", "[RequiredQuantity] > 0");
                    table.ForeignKey(
                        name: "FK_RII_GP_STATION_RESOURCE_RII_GP_RESOURCE_ResourceId",
                        column: x => x.ResourceId,
                        principalTable: "RII_GP_RESOURCE",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RII_GP_STATION_RESOURCE_RII_GP_STATION_StationId",
                        column: x => x.StationId,
                        principalTable: "RII_GP_STATION",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RII_GP_STATION_SHIFT",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StationId = table.Column<long>(type: "bigint", nullable: false),
                    ShiftId = table.Column<long>(type: "bigint", nullable: false),
                    WeekdayMask = table.Column<int>(type: "int", nullable: false),
                    CapacityMinutes = table.Column<int>(type: "int", nullable: false),
                    PersonnelCapacity = table.Column<int>(type: "int", nullable: false),
                    MachineCapacity = table.Column<int>(type: "int", nullable: false),
                    CraneAvailable = table.Column<bool>(type: "bit", nullable: false),
                    TransportAvailable = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
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
                    table.PrimaryKey("PK_RII_GP_STATION_SHIFT", x => x.Id);
                    table.CheckConstraint("CK_RII_GP_STATION_SHIFT_CAPACITY", "[WeekdayMask] BETWEEN 0 AND 127 AND [CapacityMinutes] >= 0 AND [PersonnelCapacity] >= 0 AND [MachineCapacity] >= 0");
                    table.ForeignKey(
                        name: "FK_RII_GP_STATION_SHIFT_RII_GP_SHIFT_ShiftId",
                        column: x => x.ShiftId,
                        principalTable: "RII_GP_SHIFT",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RII_GP_STATION_SHIFT_RII_GP_STATION_StationId",
                        column: x => x.StationId,
                        principalTable: "RII_GP_STATION",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RII_GP_OPERATION",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProjectId = table.Column<long>(type: "bigint", nullable: false),
                    RouteOperationId = table.Column<long>(type: "bigint", nullable: false),
                    StationId = table.Column<long>(type: "bigint", nullable: false),
                    ProductionOrderId = table.Column<long>(type: "bigint", nullable: true),
                    UnitIndex = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    PlannedStartAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PlannedEndAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ActualStartAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ActualEndAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    GoodQuantity = table.Column<decimal>(type: "decimal(20,6)", precision: 20, scale: 6, nullable: false),
                    DefectQuantity = table.Column<decimal>(type: "decimal(20,6)", precision: 20, scale: 6, nullable: false),
                    ScrapQuantity = table.Column<decimal>(type: "decimal(20,6)", precision: 20, scale: 6, nullable: false),
                    HasMaterialShortage = table.Column<bool>(type: "bit", nullable: false),
                    HasProblem = table.Column<bool>(type: "bit", nullable: false),
                    ProblemDescription = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    IsCritical = table.Column<bool>(type: "bit", nullable: false),
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
                    table.PrimaryKey("PK_RII_GP_OPERATION", x => x.Id);
                    table.CheckConstraint("CK_RII_GP_OPERATION_VALUES", "[UnitIndex] > 0 AND [PlannedEndAtUtc] > [PlannedStartAtUtc] AND [GoodQuantity] >= 0 AND [DefectQuantity] >= 0 AND [ScrapQuantity] >= 0");
                    table.ForeignKey(
                        name: "FK_RII_GP_OPERATION_RII_GP_PROJECT_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "RII_GP_PROJECT",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RII_GP_OPERATION_RII_GP_ROUTE_OPERATION_RouteOperationId",
                        column: x => x.RouteOperationId,
                        principalTable: "RII_GP_ROUTE_OPERATION",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RII_GP_OPERATION_RII_GP_STATION_StationId",
                        column: x => x.StationId,
                        principalTable: "RII_GP_STATION",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RII_GP_OPERATION_RII_PR_ORDER_ProductionOrderId",
                        column: x => x.ProductionOrderId,
                        principalTable: "RII_PR_ORDER",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RII_GP_ROUTE_DEPENDENCY",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RouteId = table.Column<long>(type: "bigint", nullable: false),
                    PredecessorOperationId = table.Column<long>(type: "bigint", nullable: false),
                    SuccessorOperationId = table.Column<long>(type: "bigint", nullable: false),
                    DependencyType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    LagMinutes = table.Column<int>(type: "int", nullable: false),
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
                    table.PrimaryKey("PK_RII_GP_ROUTE_DEPENDENCY", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RII_GP_ROUTE_DEPENDENCY_RII_GP_ROUTE_OPERATION_PredecessorOperationId",
                        column: x => x.PredecessorOperationId,
                        principalTable: "RII_GP_ROUTE_OPERATION",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RII_GP_ROUTE_DEPENDENCY_RII_GP_ROUTE_OPERATION_SuccessorOperationId",
                        column: x => x.SuccessorOperationId,
                        principalTable: "RII_GP_ROUTE_OPERATION",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RII_GP_ROUTE_DEPENDENCY_RII_GP_ROUTE_RouteId",
                        column: x => x.RouteId,
                        principalTable: "RII_GP_ROUTE",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RII_GP_OPERATION_DEPENDENCY",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PredecessorOperationId = table.Column<long>(type: "bigint", nullable: false),
                    SuccessorOperationId = table.Column<long>(type: "bigint", nullable: false),
                    DependencyType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    LagMinutes = table.Column<int>(type: "int", nullable: false),
                    RequiresAcceptedOutput = table.Column<bool>(type: "bit", nullable: false),
                    RequiresWarehouseTransfer = table.Column<bool>(type: "bit", nullable: false),
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
                    table.PrimaryKey("PK_RII_GP_OPERATION_DEPENDENCY", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RII_GP_OPERATION_DEPENDENCY_RII_GP_OPERATION_PredecessorOperationId",
                        column: x => x.PredecessorOperationId,
                        principalTable: "RII_GP_OPERATION",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RII_GP_OPERATION_DEPENDENCY_RII_GP_OPERATION_SuccessorOperationId",
                        column: x => x.SuccessorOperationId,
                        principalTable: "RII_GP_OPERATION",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "RII_PERMISSION_DEFINITIONS",
                columns: new[] { "Id", "AvailableOnMobile", "AvailableOnWeb", "BranchCode", "Code", "CreatedBy", "CreatedDate", "DeletedBy", "DeletedDate", "Description", "IsActive", "Name", "UpdatedBy", "UpdatedDate" },
                values: new object[,]
                {
                    { 2900L, false, true, "0", "WMS.GENERATOR_PRODUCTION.VIEW", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, true, "Jeneratör üretim projelerini ve planını görüntüle", null, null },
                    { 2901L, false, true, "0", "WMS.GENERATOR_PRODUCTION.CREATE", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, true, "Jeneratör üretim projesi oluştur ve düzenle", null, null },
                    { 2902L, false, true, "0", "WMS.GENERATOR_PRODUCTION.PLAN", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, true, "Jeneratör üretim planını önizle ve uygula", null, null },
                    { 2903L, true, true, "0", "WMS.GENERATOR_PRODUCTION.OPERATE", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, true, "Jeneratör üretim operasyonlarını yürüt", null, null },
                    { 2904L, false, true, "0", "WMS.GENERATOR_PRODUCTION.SETTINGS.VIEW", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, true, "Jeneratör üretim tanımlarını görüntüle", null, null },
                    { 2905L, false, true, "0", "WMS.GENERATOR_PRODUCTION.SETTINGS.MANAGE", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, true, "Jeneratör üretim istasyon, rota, vardiya ve kurallarını yönet", null, null }
                });

            // Group-mapping identifiers belong to operational data and can already be
            // occupied in long-lived databases. Seed by business key and let SQL Server
            // allocate the identity value so deployment stays idempotent.
            migrationBuilder.Sql("""
                INSERT INTO [RII_PERMISSION_GROUP_PERMISSIONS]
                    ([BranchCode], [CreatedDate], [PermissionDefinitionId], [PermissionGroupId])
                SELECT N'0', '2026-07-21T00:00:00.0000000Z', permission.[Id], CAST(1001 AS bigint)
                FROM [RII_PERMISSION_DEFINITIONS] permission
                WHERE permission.[Id] BETWEEN 2900 AND 2905
                  AND NOT EXISTS
                  (
                      SELECT 1
                      FROM [RII_PERMISSION_GROUP_PERMISSIONS] mapping
                      WHERE mapping.[PermissionGroupId] = 1001
                        AND mapping.[PermissionDefinitionId] = permission.[Id]
                        AND mapping.[DeletedDate] IS NULL
                  );
                """);

            migrationBuilder.CreateIndex(
                name: "IX_RII_GP_CALENDAR_EXCEPTION_BranchCode_ExceptionDate_StationId_ShiftId",
                table: "RII_GP_CALENDAR_EXCEPTION",
                columns: new[] { "BranchCode", "ExceptionDate", "StationId", "ShiftId" });

            migrationBuilder.CreateIndex(
                name: "IX_RII_GP_CALENDAR_EXCEPTION_IsDeleted",
                table: "RII_GP_CALENDAR_EXCEPTION",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RII_GP_CALENDAR_EXCEPTION_ShiftId",
                table: "RII_GP_CALENDAR_EXCEPTION",
                column: "ShiftId");

            migrationBuilder.CreateIndex(
                name: "IX_RII_GP_CALENDAR_EXCEPTION_StationId",
                table: "RII_GP_CALENDAR_EXCEPTION",
                column: "StationId");

            migrationBuilder.CreateIndex(
                name: "IX_RII_GP_OPERATION_BranchCode_StationId_PlannedStartAtUtc_PlannedEndAtUtc",
                table: "RII_GP_OPERATION",
                columns: new[] { "BranchCode", "StationId", "PlannedStartAtUtc", "PlannedEndAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_RII_GP_OPERATION_IsDeleted",
                table: "RII_GP_OPERATION",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RII_GP_OPERATION_ProductionOrderId",
                table: "RII_GP_OPERATION",
                column: "ProductionOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_RII_GP_OPERATION_ProjectId_RouteOperationId_UnitIndex",
                table: "RII_GP_OPERATION",
                columns: new[] { "ProjectId", "RouteOperationId", "UnitIndex" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_RII_GP_OPERATION_RouteOperationId",
                table: "RII_GP_OPERATION",
                column: "RouteOperationId");

            migrationBuilder.CreateIndex(
                name: "IX_RII_GP_OPERATION_StationId",
                table: "RII_GP_OPERATION",
                column: "StationId");

            migrationBuilder.CreateIndex(
                name: "IX_RII_GP_OPERATION_DEPENDENCY_IsDeleted",
                table: "RII_GP_OPERATION_DEPENDENCY",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RII_GP_OPERATION_DEPENDENCY_PredecessorOperationId_SuccessorOperationId",
                table: "RII_GP_OPERATION_DEPENDENCY",
                columns: new[] { "PredecessorOperationId", "SuccessorOperationId" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_RII_GP_OPERATION_DEPENDENCY_SuccessorOperationId",
                table: "RII_GP_OPERATION_DEPENDENCY",
                column: "SuccessorOperationId");

            migrationBuilder.CreateIndex(
                name: "IX_RII_GP_PLAN_REVISION_BranchCode_ProjectId_OccurredAtUtc",
                table: "RII_GP_PLAN_REVISION",
                columns: new[] { "BranchCode", "ProjectId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_RII_GP_PLAN_REVISION_IsDeleted",
                table: "RII_GP_PLAN_REVISION",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RII_GP_PLAN_REVISION_ProjectId",
                table: "RII_GP_PLAN_REVISION",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_RII_GP_PROJECT_BranchCode_ProjectCode",
                table: "RII_GP_PROJECT",
                columns: new[] { "BranchCode", "ProjectCode" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_RII_GP_PROJECT_BranchCode_Status_PlannedDeliveryAtUtc",
                table: "RII_GP_PROJECT",
                columns: new[] { "BranchCode", "Status", "PlannedDeliveryAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_RII_GP_PROJECT_IsDeleted",
                table: "RII_GP_PROJECT",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RII_GP_PROJECT_ProductionHeaderId",
                table: "RII_GP_PROJECT",
                column: "ProductionHeaderId");

            migrationBuilder.CreateIndex(
                name: "IX_RII_GP_RESOURCE_BranchCode_Code",
                table: "RII_GP_RESOURCE",
                columns: new[] { "BranchCode", "Code" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_RII_GP_RESOURCE_IsDeleted",
                table: "RII_GP_RESOURCE",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RII_GP_ROUTE_BranchCode_Code_VersionNumber",
                table: "RII_GP_ROUTE",
                columns: new[] { "BranchCode", "Code", "VersionNumber" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_RII_GP_ROUTE_BranchCode_PartType_IsActive",
                table: "RII_GP_ROUTE",
                columns: new[] { "BranchCode", "PartType", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_RII_GP_ROUTE_IsDeleted",
                table: "RII_GP_ROUTE",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RII_GP_ROUTE_DEPENDENCY_IsDeleted",
                table: "RII_GP_ROUTE_DEPENDENCY",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RII_GP_ROUTE_DEPENDENCY_PredecessorOperationId_SuccessorOperationId",
                table: "RII_GP_ROUTE_DEPENDENCY",
                columns: new[] { "PredecessorOperationId", "SuccessorOperationId" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_RII_GP_ROUTE_DEPENDENCY_RouteId",
                table: "RII_GP_ROUTE_DEPENDENCY",
                column: "RouteId");

            migrationBuilder.CreateIndex(
                name: "IX_RII_GP_ROUTE_DEPENDENCY_SuccessorOperationId",
                table: "RII_GP_ROUTE_DEPENDENCY",
                column: "SuccessorOperationId");

            migrationBuilder.CreateIndex(
                name: "IX_RII_GP_ROUTE_OPERATION_BranchCode_StationId_IsActive",
                table: "RII_GP_ROUTE_OPERATION",
                columns: new[] { "BranchCode", "StationId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_RII_GP_ROUTE_OPERATION_IsDeleted",
                table: "RII_GP_ROUTE_OPERATION",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RII_GP_ROUTE_OPERATION_RouteId_Sequence",
                table: "RII_GP_ROUTE_OPERATION",
                columns: new[] { "RouteId", "Sequence" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_RII_GP_ROUTE_OPERATION_StationId",
                table: "RII_GP_ROUTE_OPERATION",
                column: "StationId");

            migrationBuilder.CreateIndex(
                name: "IX_RII_GP_RULE_BranchCode_Code",
                table: "RII_GP_RULE",
                columns: new[] { "BranchCode", "Code" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_RII_GP_RULE_IsDeleted",
                table: "RII_GP_RULE",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RII_GP_SHIFT_BranchCode_Code",
                table: "RII_GP_SHIFT",
                columns: new[] { "BranchCode", "Code" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_RII_GP_SHIFT_IsDeleted",
                table: "RII_GP_SHIFT",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RII_GP_STATION_BranchCode_Area_PlanningOrder",
                table: "RII_GP_STATION",
                columns: new[] { "BranchCode", "Area", "PlanningOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_RII_GP_STATION_BranchCode_Code",
                table: "RII_GP_STATION",
                columns: new[] { "BranchCode", "Code" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_RII_GP_STATION_IsDeleted",
                table: "RII_GP_STATION",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RII_GP_STATION_RESOURCE_IsDeleted",
                table: "RII_GP_STATION_RESOURCE",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RII_GP_STATION_RESOURCE_ResourceId",
                table: "RII_GP_STATION_RESOURCE",
                column: "ResourceId");

            migrationBuilder.CreateIndex(
                name: "IX_RII_GP_STATION_RESOURCE_StationId_ResourceId",
                table: "RII_GP_STATION_RESOURCE",
                columns: new[] { "StationId", "ResourceId" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_RII_GP_STATION_SHIFT_IsDeleted",
                table: "RII_GP_STATION_SHIFT",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RII_GP_STATION_SHIFT_ShiftId",
                table: "RII_GP_STATION_SHIFT",
                column: "ShiftId");

            migrationBuilder.CreateIndex(
                name: "IX_RII_GP_STATION_SHIFT_StationId_ShiftId",
                table: "RII_GP_STATION_SHIFT",
                columns: new[] { "StationId", "ShiftId" },
                unique: true,
                filter: "[IsDeleted] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RII_GP_CALENDAR_EXCEPTION");

            migrationBuilder.DropTable(
                name: "RII_GP_OPERATION_DEPENDENCY");

            migrationBuilder.DropTable(
                name: "RII_GP_PLAN_REVISION");

            migrationBuilder.DropTable(
                name: "RII_GP_ROUTE_DEPENDENCY");

            migrationBuilder.DropTable(
                name: "RII_GP_RULE");

            migrationBuilder.DropTable(
                name: "RII_GP_STATION_RESOURCE");

            migrationBuilder.DropTable(
                name: "RII_GP_STATION_SHIFT");

            migrationBuilder.DropTable(
                name: "RII_GP_OPERATION");

            migrationBuilder.DropTable(
                name: "RII_GP_RESOURCE");

            migrationBuilder.DropTable(
                name: "RII_GP_SHIFT");

            migrationBuilder.DropTable(
                name: "RII_GP_PROJECT");

            migrationBuilder.DropTable(
                name: "RII_GP_ROUTE_OPERATION");

            migrationBuilder.DropTable(
                name: "RII_GP_ROUTE");

            migrationBuilder.DropTable(
                name: "RII_GP_STATION");

            migrationBuilder.Sql("""
                DELETE FROM [RII_PERMISSION_GROUP_PERMISSIONS]
                WHERE [PermissionGroupId] = 1001
                  AND [PermissionDefinitionId] BETWEEN 2900 AND 2905;
                """);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_DEFINITIONS",
                keyColumn: "Id",
                keyValue: 2900L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_DEFINITIONS",
                keyColumn: "Id",
                keyValue: 2901L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_DEFINITIONS",
                keyColumn: "Id",
                keyValue: 2902L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_DEFINITIONS",
                keyColumn: "Id",
                keyValue: 2903L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_DEFINITIONS",
                keyColumn: "Id",
                keyValue: 2904L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_DEFINITIONS",
                keyColumn: "Id",
                keyValue: 2905L);
        }
    }
}
