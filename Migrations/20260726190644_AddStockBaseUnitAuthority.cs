using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace verii_wms_api_v2.Migrations
{
    /// <inheritdoc />
    public partial class AddStockBaseUnitAuthority : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BaseUnitCode",
                table: "RII_STOCK",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "ADET");

            migrationBuilder.Sql(
                """
                IF OBJECT_ID(N'dbo.RII_FN_STOK', N'IF') IS NOT NULL
                BEGIN
                    UPDATE target
                    SET target.BaseUnitCode = UPPER(LTRIM(RTRIM(source.OLCU_BR1)))
                    FROM dbo.RII_STOCK AS target
                    INNER JOIN dbo.RII_FN_STOK(NULL, NULL) AS source
                        ON target.BranchCode = CONVERT(nvarchar(20), source.SUBE_KODU)
                       AND UPPER(LTRIM(RTRIM(target.ErpStockCode))) = UPPER(LTRIM(RTRIM(source.STOK_KODU)))
                    WHERE NULLIF(LTRIM(RTRIM(source.OLCU_BR1)), N'') IS NOT NULL;
                END
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BaseUnitCode",
                table: "RII_STOCK");
        }
    }
}
