using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace verii_wms_api_v2.Migrations
{
    /// <inheritdoc />
    public partial class EnforceFixedLengthDocumentNumbers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_RII_DOCUMENT_SERIES_DOCUMENT_NO_LENGTH",
                table: "RII_DOCUMENT_SERIES");

            migrationBuilder.Sql(
                """
                UPDATE [RII_DOCUMENT_SERIES]
                SET [NumberLength] = 15 - LEN([Prefix])
                    - CASE [YearFormat]
                        WHEN N'TwoDigit' THEN 2
                        WHEN N'FourDigit' THEN 4
                        ELSE 0
                      END
                WHERE [NumberLength] <> 15 - LEN([Prefix])
                    - CASE [YearFormat]
                        WHEN N'TwoDigit' THEN 2
                        WHEN N'FourDigit' THEN 4
                        ELSE 0
                      END;
                """);

            migrationBuilder.AddCheckConstraint(
                name: "CK_RII_DOCUMENT_SERIES_DOCUMENT_NO_LENGTH",
                table: "RII_DOCUMENT_SERIES",
                sql: "LEN([Prefix]) + [NumberLength] + CASE [YearFormat] WHEN N'TwoDigit' THEN 2 WHEN N'FourDigit' THEN 4 ELSE 0 END = 15");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_RII_DOCUMENT_SERIES_DOCUMENT_NO_LENGTH",
                table: "RII_DOCUMENT_SERIES");

            migrationBuilder.AddCheckConstraint(
                name: "CK_RII_DOCUMENT_SERIES_DOCUMENT_NO_LENGTH",
                table: "RII_DOCUMENT_SERIES",
                sql: "LEN([Prefix]) + [NumberLength] + CASE [YearFormat] WHEN N'TwoDigit' THEN 2 WHEN N'FourDigit' THEN 4 ELSE 0 END <= 15");
        }
    }
}
