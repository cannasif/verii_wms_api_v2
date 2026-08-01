using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace verii_wms_api_v2.Migrations
{
    /// <inheritdoc />
    public partial class MakeDocumentSeriesOperationScopedAndNetsisCompatible : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RII_DOCUMENT_SERIES_RII_WAREHOUSE_WarehouseId",
                table: "RII_DOCUMENT_SERIES");

            migrationBuilder.DropIndex(
                name: "IX_RII_DOCUMENT_SERIES_RESOLUTION",
                table: "RII_DOCUMENT_SERIES");

            migrationBuilder.DropIndex(
                name: "IX_RII_DOCUMENT_SERIES_WarehouseId",
                table: "RII_DOCUMENT_SERIES");

            migrationBuilder.DropIndex(
                name: "UX_RII_DOCUMENT_SERIES_DEFAULT_SCOPE",
                table: "RII_DOCUMENT_SERIES");

            migrationBuilder.DropCheckConstraint(
                name: "CK_RII_DOCUMENT_SERIES_NUMBER_LENGTH",
                table: "RII_DOCUMENT_SERIES");

            migrationBuilder.Sql(SqlServerMigrationSql.Execute(
                """
                ;WITH [SeriesShape] AS
                (
                    SELECT
                        [Id],
                        CASE [YearFormat]
                            WHEN N'TwoDigit' THEN 2
                            WHEN N'FourDigit' THEN 4
                            ELSE 0
                        END AS [YearLength],
                        CASE
                            WHEN LEN(CONVERT(varchar(20), [StartNumber])) >= LEN(CONVERT(varchar(20), [NextNumber]))
                                THEN LEN(CONVERT(varchar(20), [StartNumber]))
                            ELSE LEN(CONVERT(varchar(20), [NextNumber]))
                        END AS [CounterLength],
                        [NumberLength]
                    FROM [RII_DOCUMENT_SERIES]
                ),
                [NormalizedShape] AS
                (
                    SELECT
                        [Id],
                        [YearLength],
                        CASE
                            WHEN [CounterLength] > 15 - [YearLength] THEN [CounterLength]
                            WHEN [NumberLength] < 3 THEN
                                CASE WHEN [CounterLength] > 3 THEN [CounterLength] ELSE 3 END
                            WHEN [NumberLength] > 15 - [YearLength] THEN 15 - [YearLength]
                            WHEN [NumberLength] < [CounterLength] THEN [CounterLength]
                            ELSE [NumberLength]
                        END AS [NormalizedNumberLength]
                    FROM [SeriesShape]
                )
                UPDATE [series]
                SET
                    [NumberLength] = [shape].[NormalizedNumberLength],
                    [Prefix] = LEFT([series].[Prefix],
                        CASE
                            WHEN 15 - [shape].[YearLength] - [shape].[NormalizedNumberLength] > 0
                                THEN 15 - [shape].[YearLength] - [shape].[NormalizedNumberLength]
                            ELSE 0
                        END)
                FROM [RII_DOCUMENT_SERIES] AS [series]
                INNER JOIN [NormalizedShape] AS [shape] ON [shape].[Id] = [series].[Id];

                IF EXISTS
                (
                    SELECT 1
                    FROM [RII_DOCUMENT_SERIES]
                    WHERE [NumberLength] NOT BETWEEN 3 AND 15
                       OR LEN([Prefix]) + [NumberLength]
                          + CASE [YearFormat]
                                WHEN N'TwoDigit' THEN 2
                                WHEN N'FourDigit' THEN 4
                                ELSE 0
                            END > 15
                       OR LEN(CONVERT(varchar(20), [StartNumber])) > [NumberLength]
                       OR LEN(CONVERT(varchar(20), [NextNumber])) > [NumberLength]
                )
                BEGIN
                    ;THROW 51000, 'Document series data is not compatible with the 15-character Netsis document number limit. Correct the affected series before running this migration.', 1;
                END;

                ;WITH [RankedDefaults] AS
                (
                    SELECT
                        [Id],
                        ROW_NUMBER() OVER
                        (
                            PARTITION BY [BranchCode], [DocumentType]
                            ORDER BY
                                CASE WHEN [WarehouseId] IS NULL THEN 0 ELSE 1 END,
                                CASE WHEN [HasIssuedNumbers] = 1 THEN 0 ELSE 1 END,
                                [Id]
                        ) AS [DefaultRank]
                    FROM [RII_DOCUMENT_SERIES]
                    WHERE [IsDefault] = 1
                      AND [IsActive] = 1
                      AND [IsDeleted] = 0
                )
                UPDATE [series]
                SET [IsDefault] = 0
                FROM [RII_DOCUMENT_SERIES] AS [series]
                INNER JOIN [RankedDefaults] AS [ranked] ON [ranked].[Id] = [series].[Id]
                WHERE [ranked].[DefaultRank] > 1;
                """));

            migrationBuilder.DropColumn(
                name: "Separator",
                table: "RII_DOCUMENT_SERIES");

            migrationBuilder.DropColumn(
                name: "WarehouseId",
                table: "RII_DOCUMENT_SERIES");

            migrationBuilder.Sql(SqlServerMigrationSql.Execute(
                "CREATE INDEX [IX_RII_DOCUMENT_SERIES_RESOLUTION] ON [RII_DOCUMENT_SERIES] ([BranchCode], [DocumentType], [IsActive]);"));

            migrationBuilder.CreateIndex(
                name: "UX_RII_DOCUMENT_SERIES_DEFAULT_SCOPE",
                table: "RII_DOCUMENT_SERIES",
                columns: new[] { "BranchCode", "DocumentType" },
                unique: true,
                filter: "[IsDefault] = 1 AND [IsActive] = 1 AND [IsDeleted] = 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_RII_DOCUMENT_SERIES_COUNTER_LENGTH",
                table: "RII_DOCUMENT_SERIES",
                sql: "LEN(CONVERT(varchar(20), [StartNumber])) <= [NumberLength] AND LEN(CONVERT(varchar(20), [NextNumber])) <= [NumberLength]");

            migrationBuilder.AddCheckConstraint(
                name: "CK_RII_DOCUMENT_SERIES_DOCUMENT_NO_LENGTH",
                table: "RII_DOCUMENT_SERIES",
                sql: "LEN([Prefix]) + [NumberLength] + CASE [YearFormat] WHEN N'TwoDigit' THEN 2 WHEN N'FourDigit' THEN 4 ELSE 0 END <= 15");

            migrationBuilder.AddCheckConstraint(
                name: "CK_RII_DOCUMENT_SERIES_NUMBER_LENGTH",
                table: "RII_DOCUMENT_SERIES",
                sql: "[NumberLength] BETWEEN 3 AND 15");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RII_DOCUMENT_SERIES_RESOLUTION",
                table: "RII_DOCUMENT_SERIES");

            migrationBuilder.DropIndex(
                name: "UX_RII_DOCUMENT_SERIES_DEFAULT_SCOPE",
                table: "RII_DOCUMENT_SERIES");

            migrationBuilder.DropCheckConstraint(
                name: "CK_RII_DOCUMENT_SERIES_COUNTER_LENGTH",
                table: "RII_DOCUMENT_SERIES");

            migrationBuilder.DropCheckConstraint(
                name: "CK_RII_DOCUMENT_SERIES_DOCUMENT_NO_LENGTH",
                table: "RII_DOCUMENT_SERIES");

            migrationBuilder.DropCheckConstraint(
                name: "CK_RII_DOCUMENT_SERIES_NUMBER_LENGTH",
                table: "RII_DOCUMENT_SERIES");

            migrationBuilder.AddColumn<string>(
                name: "Separator",
                table: "RII_DOCUMENT_SERIES",
                type: "nvarchar(3)",
                maxLength: 3,
                nullable: false,
                defaultValue: "-");

            migrationBuilder.AddColumn<long>(
                name: "WarehouseId",
                table: "RII_DOCUMENT_SERIES",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_RII_DOCUMENT_SERIES_RESOLUTION",
                table: "RII_DOCUMENT_SERIES",
                columns: new[] { "DocumentType", "WarehouseId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_RII_DOCUMENT_SERIES_WarehouseId",
                table: "RII_DOCUMENT_SERIES",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "UX_RII_DOCUMENT_SERIES_DEFAULT_SCOPE",
                table: "RII_DOCUMENT_SERIES",
                columns: new[] { "BranchCode", "DocumentType", "WarehouseId" },
                unique: true,
                filter: "[IsDefault] = 1 AND [IsActive] = 1 AND [IsDeleted] = 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_RII_DOCUMENT_SERIES_NUMBER_LENGTH",
                table: "RII_DOCUMENT_SERIES",
                sql: "[NumberLength] BETWEEN 3 AND 18");

            migrationBuilder.AddForeignKey(
                name: "FK_RII_DOCUMENT_SERIES_RII_WAREHOUSE_WarehouseId",
                table: "RII_DOCUMENT_SERIES",
                column: "WarehouseId",
                principalTable: "RII_WAREHOUSE",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
