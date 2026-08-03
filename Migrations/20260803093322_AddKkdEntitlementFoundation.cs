using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace verii_wms_api_v2.Migrations
{
    /// <inheritdoc />
    public partial class AddKkdEntitlementFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RII_KKD_DEPARTMENT",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
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
                    table.PrimaryKey("PK_RII_KKD_DEPARTMENT", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RII_KKD_VALIDATION_LOG",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CorrelationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmployeeId = table.Column<long>(type: "bigint", nullable: true),
                    StockId = table.Column<long>(type: "bigint", nullable: true),
                    GroupCode = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    WarehouseId = table.Column<long>(type: "bigint", nullable: true),
                    AttemptedQuantity = table.Column<decimal>(type: "decimal(20,6)", precision: 20, scale: 6, nullable: false),
                    ReasonCode = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Message = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    DeviceInfo = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
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
                    table.PrimaryKey("PK_RII_KKD_VALIDATION_LOG", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RII_KKD_ROLE",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DepartmentId = table.Column<long>(type: "bigint", nullable: true),
                    Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
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
                    table.PrimaryKey("PK_RII_KKD_ROLE", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RII_KKD_ROLE_RII_KKD_DEPARTMENT_DepartmentId",
                        column: x => x.DepartmentId,
                        principalTable: "RII_KKD_DEPARTMENT",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RII_KKD_EMPLOYEE",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<long>(type: "bigint", nullable: true),
                    CustomerId = table.Column<long>(type: "bigint", nullable: false),
                    EmployeeCode = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    FirstName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    LastName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    DepartmentId = table.Column<long>(type: "bigint", nullable: false),
                    RoleId = table.Column<long>(type: "bigint", nullable: false),
                    QrCode = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    EmploymentStartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    LastSyncDate = table.Column<DateTime>(type: "datetime2", nullable: true),
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
                    table.PrimaryKey("PK_RII_KKD_EMPLOYEE", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RII_KKD_EMPLOYEE_RII_KKD_DEPARTMENT_DepartmentId",
                        column: x => x.DepartmentId,
                        principalTable: "RII_KKD_DEPARTMENT",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RII_KKD_EMPLOYEE_RII_KKD_ROLE_RoleId",
                        column: x => x.RoleId,
                        principalTable: "RII_KKD_ROLE",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RII_KKD_MATRIX",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CustomerId = table.Column<long>(type: "bigint", nullable: false),
                    DepartmentId = table.Column<long>(type: "bigint", nullable: false),
                    RoleId = table.Column<long>(type: "bigint", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: true),
                    EffectiveTo = table.Column<DateOnly>(type: "date", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
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
                    table.PrimaryKey("PK_RII_KKD_MATRIX", x => x.Id);
                    table.CheckConstraint("CK_RII_KKD_MATRIX_DATES", "[EffectiveTo] IS NULL OR [EffectiveFrom] IS NULL OR [EffectiveTo] >= [EffectiveFrom]");
                    table.ForeignKey(
                        name: "FK_RII_KKD_MATRIX_RII_KKD_DEPARTMENT_DepartmentId",
                        column: x => x.DepartmentId,
                        principalTable: "RII_KKD_DEPARTMENT",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RII_KKD_MATRIX_RII_KKD_ROLE_RoleId",
                        column: x => x.RoleId,
                        principalTable: "RII_KKD_ROLE",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RII_KKD_DISTRIBUTION",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CorrelationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmployeeId = table.Column<long>(type: "bigint", nullable: false),
                    CustomerId = table.Column<long>(type: "bigint", nullable: false),
                    WarehouseId = table.Column<long>(type: "bigint", nullable: false),
                    DocumentSeriesId = table.Column<long>(type: "bigint", nullable: false),
                    DocumentNo = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    WarehouseOutboundId = table.Column<long>(type: "bigint", nullable: true),
                    FailureReason = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
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
                    table.PrimaryKey("PK_RII_KKD_DISTRIBUTION", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RII_KKD_DISTRIBUTION_RII_KKD_EMPLOYEE_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "RII_KKD_EMPLOYEE",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RII_KKD_RULE",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MatrixId = table.Column<long>(type: "bigint", nullable: false),
                    GroupCode = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    GroupName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    StockId = table.Column<long>(type: "bigint", nullable: true),
                    StockCodeSnapshot = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    StockNameSnapshot = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    StandardCode = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    StandardName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    AnnualIssueCount = table.Column<int>(type: "int", nullable: true),
                    AnnualQuantity = table.Column<decimal>(type: "decimal(20,6)", precision: 20, scale: 6, nullable: true),
                    MaxCarryQuantity = table.Column<decimal>(type: "decimal(20,6)", precision: 20, scale: 6, nullable: true),
                    AllowBulkIssue = table.Column<bool>(type: "bit", nullable: false),
                    IsMandatory = table.Column<bool>(type: "bit", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
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
                    table.PrimaryKey("PK_RII_KKD_RULE", x => x.Id);
                    table.CheckConstraint("CK_RII_KKD_RULE_ANNUAL_COUNT", "[AnnualIssueCount] IS NULL OR [AnnualIssueCount] > 0");
                    table.CheckConstraint("CK_RII_KKD_RULE_QUANTITY", "([AnnualQuantity] IS NULL OR [AnnualQuantity] >= 0) AND ([MaxCarryQuantity] IS NULL OR [MaxCarryQuantity] >= 0)");
                    table.ForeignKey(
                        name: "FK_RII_KKD_RULE_RII_KKD_MATRIX_MatrixId",
                        column: x => x.MatrixId,
                        principalTable: "RII_KKD_MATRIX",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RII_KKD_DISTRIBUTION_LINE",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DistributionId = table.Column<long>(type: "bigint", nullable: false),
                    LineNo = table.Column<int>(type: "int", nullable: false),
                    StockId = table.Column<long>(type: "bigint", nullable: false),
                    StockCodeSnapshot = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    StockNameSnapshot = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    GroupCode = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(20,6)", precision: 20, scale: 6, nullable: false),
                    EntitledQuantity = table.Column<decimal>(type: "decimal(20,6)", precision: 20, scale: 6, nullable: false),
                    ExcessQuantity = table.Column<decimal>(type: "decimal(20,6)", precision: 20, scale: 6, nullable: false),
                    SourceLocationId = table.Column<long>(type: "bigint", nullable: false),
                    LotNo = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    SerialNo = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    OpenOrderNo = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    OpenOrderLineId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
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
                    table.PrimaryKey("PK_RII_KKD_DISTRIBUTION_LINE", x => x.Id);
                    table.CheckConstraint("CK_RII_KKD_DISTRIBUTION_LINE_QTY", "[Quantity] > 0 AND [EntitledQuantity] >= 0 AND [ExcessQuantity] >= 0 AND [EntitledQuantity] + [ExcessQuantity] = [Quantity]");
                    table.ForeignKey(
                        name: "FK_RII_KKD_DISTRIBUTION_LINE_RII_KKD_DISTRIBUTION_DistributionId",
                        column: x => x.DistributionId,
                        principalTable: "RII_KKD_DISTRIBUTION",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RII_KKD_OVERRIDE",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EmployeeId = table.Column<long>(type: "bigint", nullable: false),
                    RuleId = table.Column<long>(type: "bigint", nullable: true),
                    GroupCode = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(20,6)", precision: 20, scale: 6, nullable: false),
                    ConsumedQuantity = table.Column<decimal>(type: "decimal(20,6)", precision: 20, scale: 6, nullable: false),
                    ValidFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    ValidTo = table.Column<DateOnly>(type: "date", nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    ApprovedByUserId = table.Column<long>(type: "bigint", nullable: false),
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
                    table.PrimaryKey("PK_RII_KKD_OVERRIDE", x => x.Id);
                    table.CheckConstraint("CK_RII_KKD_OVERRIDE_QTY", "[Quantity] > 0 AND [ConsumedQuantity] >= 0 AND [ConsumedQuantity] <= [Quantity] AND ([ValidTo] IS NULL OR [ValidTo] >= [ValidFrom])");
                    table.ForeignKey(
                        name: "FK_RII_KKD_OVERRIDE_RII_KKD_EMPLOYEE_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "RII_KKD_EMPLOYEE",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RII_KKD_OVERRIDE_RII_KKD_RULE_RuleId",
                        column: x => x.RuleId,
                        principalTable: "RII_KKD_RULE",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RII_KKD_PHASE",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RuleId = table.Column<long>(type: "bigint", nullable: false),
                    PhaseType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    OffsetMonths = table.Column<int>(type: "int", nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(20,6)", precision: 20, scale: 6, nullable: false),
                    AllowBulkIssue = table.Column<bool>(type: "bit", nullable: false),
                    FrequencyDays = table.Column<int>(type: "int", nullable: true),
                    QuantityPerFrequency = table.Column<decimal>(type: "decimal(20,6)", precision: 20, scale: 6, nullable: true),
                    PeriodType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    PeriodInterval = table.Column<int>(type: "int", nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
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
                    table.PrimaryKey("PK_RII_KKD_PHASE", x => x.Id);
                    table.CheckConstraint("CK_RII_KKD_PHASE_VALUES", "[Quantity] >= 0 AND [OffsetMonths] >= 0 AND ([FrequencyDays] IS NULL OR [FrequencyDays] > 0) AND ([PeriodInterval] IS NULL OR [PeriodInterval] > 0)");
                    table.ForeignKey(
                        name: "FK_RII_KKD_PHASE_RII_KKD_RULE_RuleId",
                        column: x => x.RuleId,
                        principalTable: "RII_KKD_RULE",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RII_KKD_CONSUMPTION",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EmployeeId = table.Column<long>(type: "bigint", nullable: false),
                    DistributionId = table.Column<long>(type: "bigint", nullable: false),
                    DistributionLineId = table.Column<long>(type: "bigint", nullable: false),
                    StockId = table.Column<long>(type: "bigint", nullable: false),
                    GroupCode = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    SourceType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    MatrixId = table.Column<long>(type: "bigint", nullable: true),
                    RuleId = table.Column<long>(type: "bigint", nullable: true),
                    PhaseId = table.Column<long>(type: "bigint", nullable: true),
                    OverrideId = table.Column<long>(type: "bigint", nullable: true),
                    Quantity = table.Column<decimal>(type: "decimal(20,6)", precision: 20, scale: 6, nullable: false),
                    ConsumedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsReversal = table.Column<bool>(type: "bit", nullable: false),
                    ReversesConsumptionId = table.Column<long>(type: "bigint", nullable: true),
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
                    table.PrimaryKey("PK_RII_KKD_CONSUMPTION", x => x.Id);
                    table.CheckConstraint("CK_RII_KKD_CONSUMPTION_QTY", "[Quantity] > 0");
                    table.ForeignKey(
                        name: "FK_RII_KKD_CONSUMPTION_RII_KKD_DISTRIBUTION_LINE_DistributionLineId",
                        column: x => x.DistributionLineId,
                        principalTable: "RII_KKD_DISTRIBUTION_LINE",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RII_KKD_DISTRIBUTION_ALLOCATION",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DistributionLineId = table.Column<long>(type: "bigint", nullable: false),
                    SourceType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    SourceId = table.Column<long>(type: "bigint", nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(20,6)", precision: 20, scale: 6, nullable: false),
                    PeriodStart = table.Column<DateOnly>(type: "date", nullable: false),
                    PeriodEnd = table.Column<DateOnly>(type: "date", nullable: true),
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
                    table.PrimaryKey("PK_RII_KKD_DISTRIBUTION_ALLOCATION", x => x.Id);
                    table.CheckConstraint("CK_RII_KKD_DISTRIBUTION_ALLOCATION_DATES", "[PeriodEnd] IS NULL OR [PeriodEnd] >= [PeriodStart]");
                    table.CheckConstraint("CK_RII_KKD_DISTRIBUTION_ALLOCATION_QTY", "[Quantity] > 0");
                    table.ForeignKey(
                        name: "FK_RII_KKD_DISTRIBUTION_ALLOCATION_RII_KKD_DISTRIBUTION_LINE_DistributionLineId",
                        column: x => x.DistributionLineId,
                        principalTable: "RII_KKD_DISTRIBUTION_LINE",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "RII_PERMISSION_DEFINITIONS",
                columns: new[] { "Id", "AvailableOnMobile", "AvailableOnWeb", "BranchCode", "Code", "CreatedBy", "CreatedDate", "DeletedBy", "DeletedDate", "Description", "IsActive", "Name", "UpdatedBy", "UpdatedDate" },
                values: new object[,]
                {
                    { 2500L, false, true, "0", "WMS.KKD.DEFINITIONS.VIEW", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, true, "KKD departman ve rol tanımlarını görüntüle", null, null },
                    { 2501L, false, true, "0", "WMS.KKD.DEFINITIONS.MANAGE", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, true, "KKD departman ve rol tanımlarını yönet", null, null },
                    { 2502L, false, true, "0", "WMS.KKD.EMPLOYEES.VIEW", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, true, "KKD personellerini görüntüle", null, null },
                    { 2503L, false, true, "0", "WMS.KKD.EMPLOYEES.MANAGE", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, true, "KKD personellerini yönet", null, null },
                    { 2504L, false, true, "0", "WMS.KKD.MATRICES.VIEW", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, true, "KKD hak matrislerini görüntüle", null, null },
                    { 2505L, false, true, "0", "WMS.KKD.MATRICES.MANAGE", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, true, "KKD hak matrislerini yönet", null, null },
                    { 2506L, false, true, "0", "WMS.KKD.OVERRIDES.MANAGE", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, true, "KKD personel ek haklarını yönet", null, null },
                    { 2507L, false, true, "0", "WMS.KKD.ENTITLEMENT.CHECK", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, true, "KKD hak sorgulaması yap", null, null },
                    { 2508L, false, true, "0", "WMS.KKD.DISTRIBUTION.OPERATE", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, true, "KKD dağıtım ve ambar çıkış işlemini yürüt", null, null },
                    { 2509L, false, true, "0", "WMS.KKD.REPORTS.VIEW", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, true, "KKD raporlarını görüntüle", null, null }
                });

            migrationBuilder.InsertData(
                table: "RII_PERMISSION_GROUP_PERMISSIONS",
                columns: new[] { "Id", "BranchCode", "CreatedBy", "CreatedDate", "DeletedBy", "DeletedDate", "PermissionDefinitionId", "PermissionGroupId", "UpdatedBy", "UpdatedDate" },
                values: new object[,]
                {
                    { 2500L, "0", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, 2500L, 1001L, null, null },
                    { 2501L, "0", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, 2501L, 1001L, null, null },
                    { 2502L, "0", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, 2502L, 1001L, null, null },
                    { 2503L, "0", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, 2503L, 1001L, null, null },
                    { 2504L, "0", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, 2504L, 1001L, null, null },
                    { 2505L, "0", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, 2505L, 1001L, null, null },
                    { 2506L, "0", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, 2506L, 1001L, null, null },
                    { 2507L, "0", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, 2507L, 1001L, null, null },
                    { 2508L, "0", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, 2508L, 1001L, null, null },
                    { 2509L, "0", null, new DateTime(2026, 7, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, null, 2509L, 1001L, null, null }
                });

            migrationBuilder.CreateIndex(
                name: "IX_RII_KKD_CONSUMPTION_BranchCode_EmployeeId_GroupCode_ConsumedAtUtc",
                table: "RII_KKD_CONSUMPTION",
                columns: new[] { "BranchCode", "EmployeeId", "GroupCode", "ConsumedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_RII_KKD_CONSUMPTION_DistributionLineId",
                table: "RII_KKD_CONSUMPTION",
                column: "DistributionLineId");

            migrationBuilder.CreateIndex(
                name: "IX_RII_KKD_CONSUMPTION_IsDeleted",
                table: "RII_KKD_CONSUMPTION",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RII_KKD_CONSUMPTION_ReversesConsumptionId",
                table: "RII_KKD_CONSUMPTION",
                column: "ReversesConsumptionId",
                unique: true,
                filter: "[ReversesConsumptionId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_RII_KKD_DEPARTMENT_BranchCode_Code",
                table: "RII_KKD_DEPARTMENT",
                columns: new[] { "BranchCode", "Code" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_RII_KKD_DEPARTMENT_IsDeleted",
                table: "RII_KKD_DEPARTMENT",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RII_KKD_DISTRIBUTION_BranchCode_DocumentNo",
                table: "RII_KKD_DISTRIBUTION",
                columns: new[] { "BranchCode", "DocumentNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RII_KKD_DISTRIBUTION_CorrelationId",
                table: "RII_KKD_DISTRIBUTION",
                column: "CorrelationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RII_KKD_DISTRIBUTION_EmployeeId",
                table: "RII_KKD_DISTRIBUTION",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_RII_KKD_DISTRIBUTION_IsDeleted",
                table: "RII_KKD_DISTRIBUTION",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RII_KKD_DISTRIBUTION_WarehouseOutboundId",
                table: "RII_KKD_DISTRIBUTION",
                column: "WarehouseOutboundId",
                unique: true,
                filter: "[WarehouseOutboundId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_RII_KKD_DISTRIBUTION_ALLOCATION_BranchCode_SourceType_SourceId_PeriodStart_PeriodEnd",
                table: "RII_KKD_DISTRIBUTION_ALLOCATION",
                columns: new[] { "BranchCode", "SourceType", "SourceId", "PeriodStart", "PeriodEnd" });

            migrationBuilder.CreateIndex(
                name: "IX_RII_KKD_DISTRIBUTION_ALLOCATION_DistributionLineId_SourceType_SourceId_PeriodStart",
                table: "RII_KKD_DISTRIBUTION_ALLOCATION",
                columns: new[] { "DistributionLineId", "SourceType", "SourceId", "PeriodStart" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RII_KKD_DISTRIBUTION_ALLOCATION_IsDeleted",
                table: "RII_KKD_DISTRIBUTION_ALLOCATION",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RII_KKD_DISTRIBUTION_LINE_DistributionId_LineNo",
                table: "RII_KKD_DISTRIBUTION_LINE",
                columns: new[] { "DistributionId", "LineNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RII_KKD_DISTRIBUTION_LINE_IsDeleted",
                table: "RII_KKD_DISTRIBUTION_LINE",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RII_KKD_EMPLOYEE_BranchCode_EmployeeCode",
                table: "RII_KKD_EMPLOYEE",
                columns: new[] { "BranchCode", "EmployeeCode" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_RII_KKD_EMPLOYEE_BranchCode_QrCode",
                table: "RII_KKD_EMPLOYEE",
                columns: new[] { "BranchCode", "QrCode" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_RII_KKD_EMPLOYEE_DepartmentId",
                table: "RII_KKD_EMPLOYEE",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_RII_KKD_EMPLOYEE_IsDeleted",
                table: "RII_KKD_EMPLOYEE",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RII_KKD_EMPLOYEE_RoleId",
                table: "RII_KKD_EMPLOYEE",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "IX_RII_KKD_MATRIX_BranchCode_Code",
                table: "RII_KKD_MATRIX",
                columns: new[] { "BranchCode", "Code" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_RII_KKD_MATRIX_BranchCode_CustomerId_DepartmentId_RoleId_IsActive",
                table: "RII_KKD_MATRIX",
                columns: new[] { "BranchCode", "CustomerId", "DepartmentId", "RoleId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_RII_KKD_MATRIX_DepartmentId",
                table: "RII_KKD_MATRIX",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_RII_KKD_MATRIX_IsDeleted",
                table: "RII_KKD_MATRIX",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RII_KKD_MATRIX_RoleId",
                table: "RII_KKD_MATRIX",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "IX_RII_KKD_OVERRIDE_BranchCode_EmployeeId_GroupCode_IsActive",
                table: "RII_KKD_OVERRIDE",
                columns: new[] { "BranchCode", "EmployeeId", "GroupCode", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_RII_KKD_OVERRIDE_EmployeeId",
                table: "RII_KKD_OVERRIDE",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_RII_KKD_OVERRIDE_IsDeleted",
                table: "RII_KKD_OVERRIDE",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RII_KKD_OVERRIDE_RuleId",
                table: "RII_KKD_OVERRIDE",
                column: "RuleId");

            migrationBuilder.CreateIndex(
                name: "IX_RII_KKD_PHASE_IsDeleted",
                table: "RII_KKD_PHASE",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RII_KKD_PHASE_RuleId_PhaseType_OffsetMonths",
                table: "RII_KKD_PHASE",
                columns: new[] { "RuleId", "PhaseType", "OffsetMonths" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_RII_KKD_ROLE_BranchCode_DepartmentId_Code",
                table: "RII_KKD_ROLE",
                columns: new[] { "BranchCode", "DepartmentId", "Code" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_RII_KKD_ROLE_DepartmentId",
                table: "RII_KKD_ROLE",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_RII_KKD_ROLE_IsDeleted",
                table: "RII_KKD_ROLE",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RII_KKD_RULE_BranchCode_StockId_GroupCode_IsActive",
                table: "RII_KKD_RULE",
                columns: new[] { "BranchCode", "StockId", "GroupCode", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_RII_KKD_RULE_IsDeleted",
                table: "RII_KKD_RULE",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RII_KKD_RULE_MatrixId_StockId_GroupCode",
                table: "RII_KKD_RULE",
                columns: new[] { "MatrixId", "StockId", "GroupCode" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_RII_KKD_VALIDATION_LOG_BranchCode_CorrelationId",
                table: "RII_KKD_VALIDATION_LOG",
                columns: new[] { "BranchCode", "CorrelationId" });

            migrationBuilder.CreateIndex(
                name: "IX_RII_KKD_VALIDATION_LOG_BranchCode_EmployeeId_CreatedDate",
                table: "RII_KKD_VALIDATION_LOG",
                columns: new[] { "BranchCode", "EmployeeId", "CreatedDate" });

            migrationBuilder.CreateIndex(
                name: "IX_RII_KKD_VALIDATION_LOG_IsDeleted",
                table: "RII_KKD_VALIDATION_LOG",
                column: "IsDeleted");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RII_KKD_CONSUMPTION");

            migrationBuilder.DropTable(
                name: "RII_KKD_DISTRIBUTION_ALLOCATION");

            migrationBuilder.DropTable(
                name: "RII_KKD_OVERRIDE");

            migrationBuilder.DropTable(
                name: "RII_KKD_PHASE");

            migrationBuilder.DropTable(
                name: "RII_KKD_VALIDATION_LOG");

            migrationBuilder.DropTable(
                name: "RII_KKD_DISTRIBUTION_LINE");

            migrationBuilder.DropTable(
                name: "RII_KKD_RULE");

            migrationBuilder.DropTable(
                name: "RII_KKD_DISTRIBUTION");

            migrationBuilder.DropTable(
                name: "RII_KKD_MATRIX");

            migrationBuilder.DropTable(
                name: "RII_KKD_EMPLOYEE");

            migrationBuilder.DropTable(
                name: "RII_KKD_ROLE");

            migrationBuilder.DropTable(
                name: "RII_KKD_DEPARTMENT");

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_GROUP_PERMISSIONS",
                keyColumn: "Id",
                keyValue: 2500L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_GROUP_PERMISSIONS",
                keyColumn: "Id",
                keyValue: 2501L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_GROUP_PERMISSIONS",
                keyColumn: "Id",
                keyValue: 2502L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_GROUP_PERMISSIONS",
                keyColumn: "Id",
                keyValue: 2503L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_GROUP_PERMISSIONS",
                keyColumn: "Id",
                keyValue: 2504L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_GROUP_PERMISSIONS",
                keyColumn: "Id",
                keyValue: 2505L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_GROUP_PERMISSIONS",
                keyColumn: "Id",
                keyValue: 2506L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_GROUP_PERMISSIONS",
                keyColumn: "Id",
                keyValue: 2507L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_GROUP_PERMISSIONS",
                keyColumn: "Id",
                keyValue: 2508L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_GROUP_PERMISSIONS",
                keyColumn: "Id",
                keyValue: 2509L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_DEFINITIONS",
                keyColumn: "Id",
                keyValue: 2500L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_DEFINITIONS",
                keyColumn: "Id",
                keyValue: 2501L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_DEFINITIONS",
                keyColumn: "Id",
                keyValue: 2502L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_DEFINITIONS",
                keyColumn: "Id",
                keyValue: 2503L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_DEFINITIONS",
                keyColumn: "Id",
                keyValue: 2504L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_DEFINITIONS",
                keyColumn: "Id",
                keyValue: 2505L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_DEFINITIONS",
                keyColumn: "Id",
                keyValue: 2506L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_DEFINITIONS",
                keyColumn: "Id",
                keyValue: 2507L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_DEFINITIONS",
                keyColumn: "Id",
                keyValue: 2508L);

            migrationBuilder.DeleteData(
                table: "RII_PERMISSION_DEFINITIONS",
                keyColumn: "Id",
                keyValue: 2509L);
        }
    }
}
