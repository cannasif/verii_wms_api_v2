using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace verii_wms_api_v2.Migrations
{
    /// <inheritdoc />
    public partial class AddErpCancellationSaga : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "ErpRecordId",
                table: "RII_ERP_POSTING_RECORDS",
                type: "bigint",
                nullable: true);

            migrationBuilder.Sql(SqlServerMigrationSql.Execute(
                """
                UPDATE [RII_ERP_POSTING_RECORDS]
                SET [ErpRecordId] = TRY_CONVERT(bigint, [ErpRecordNo])
                WHERE [ErpRecordId] IS NULL
                  AND NULLIF(LTRIM(RTRIM([ErpRecordNo])), '') IS NOT NULL;
                """));

            migrationBuilder.CreateTable(
                name: "RII_ERP_CANCELLATION_RECORDS",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ErpPostingRecordId = table.Column<long>(type: "bigint", nullable: false),
                    IdempotencyKey = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    AttemptCount = table.Column<int>(type: "int", nullable: false),
                    StartedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ErpDeletedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    WmsReversedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LastHttpStatusCode = table.Column<int>(type: "int", nullable: true),
                    LastErrorCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    LastErrorMessage = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    TraceId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
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
                    table.PrimaryKey("PK_RII_ERP_CANCELLATION_RECORDS", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RII_ERP_CANCELLATION_RECORDS_RII_ERP_POSTING_RECORDS_ErpPostingRecordId",
                        column: x => x.ErpPostingRecordId,
                        principalTable: "RII_ERP_POSTING_RECORDS",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RII_ERP_CANCELLATION_ATTEMPTS",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ErpCancellationRecordId = table.Column<long>(type: "bigint", nullable: false),
                    AttemptNo = table.Column<int>(type: "int", nullable: false),
                    Operation = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    HttpMethod = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Endpoint = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    HttpStatusCode = table.Column<int>(type: "int", nullable: true),
                    IsSuccessful = table.Column<bool>(type: "bit", nullable: false),
                    CommitUncertain = table.Column<bool>(type: "bit", nullable: false),
                    DurationMs = table.Column<long>(type: "bigint", nullable: false),
                    ErrorCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ErrorMessage = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    ProviderResponse = table.Column<string>(type: "nvarchar(max)", maxLength: 8000, nullable: true),
                    TraceId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    StartedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
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
                    table.PrimaryKey("PK_RII_ERP_CANCELLATION_ATTEMPTS", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RII_ERP_CANCELLATION_ATTEMPTS_RII_ERP_CANCELLATION_RECORDS_ErpCancellationRecordId",
                        column: x => x.ErpCancellationRecordId,
                        principalTable: "RII_ERP_CANCELLATION_RECORDS",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RII_ERP_CANCELLATION_ATTEMPTS_IsDeleted",
                table: "RII_ERP_CANCELLATION_ATTEMPTS",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "UX_RII_ERP_CANCELLATION_ATTEMPT_NO",
                table: "RII_ERP_CANCELLATION_ATTEMPTS",
                columns: new[] { "ErpCancellationRecordId", "AttemptNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RII_ERP_CANCELLATION_RECORDS_IsDeleted",
                table: "RII_ERP_CANCELLATION_RECORDS",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_RII_ERP_CANCELLATION_STATUS_UPDATED",
                table: "RII_ERP_CANCELLATION_RECORDS",
                columns: new[] { "Status", "UpdatedDate" });

            migrationBuilder.CreateIndex(
                name: "UX_RII_ERP_CANCELLATION_POSTING",
                table: "RII_ERP_CANCELLATION_RECORDS",
                column: "ErpPostingRecordId",
                unique: true,
                filter: "[IsDeleted] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RII_ERP_CANCELLATION_ATTEMPTS");

            migrationBuilder.DropTable(
                name: "RII_ERP_CANCELLATION_RECORDS");

            migrationBuilder.DropColumn(
                name: "ErpRecordId",
                table: "RII_ERP_POSTING_RECORDS");
        }
    }
}
