using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace verii_wms_api_v2.Migrations
{
    /// <inheritdoc />
    public partial class AddProductionWorkOrderDescription : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "RII_PR_SOURCE_ORDER",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.Sql(SqlServerMigrationSql.CreateOrAlterFunction("dbo.RII_FN_ISEMRI", """
CREATE OR ALTER FUNCTION [dbo].[RII_FN_ISEMRI]
(
    @IsEmriNo NVARCHAR(50) = NULL,
    @SubeKodu INT = NULL,
    @KapaliDahil BIT = 0
)
RETURNS TABLE
AS
RETURN
(
    SELECT
        CONVERT(NVARCHAR(50), I.ISEMRINO) AS IsEmriNo,
        CONVERT(INT, I.SUBEKODU) AS SubeKodu,
        CONVERT(NVARCHAR(50), I.STOK_KODU) AS StokKodu,
        CONVERT(NVARCHAR(200), ISNULL(S.STOK_ADI, I.STOK_KODU)) AS StokAdi,
        CONVERT(NVARCHAR(50), NULLIF(LTRIM(RTRIM(I.YAPKOD)), '')) AS YapilandirmaKodu,
        CONVERT(DECIMAL(28, 8), ISNULL(I.MIKTAR, 0)) AS IsEmriMiktari,
        CONVERT(INT, 1) AS BirimSirasi,
        CONVERT(NVARCHAR(20), NULLIF(LTRIM(RTRIM(S.OLCU_BR1)), '')) AS BirimKodu,
        CONVERT(DECIMAL(28, 8), CASE WHEN ISNULL(S.FORMUL_TOPLAMI, 0) = 0 THEN 1 ELSE S.FORMUL_TOPLAMI END) AS ReceteToplami,
        CONVERT(DATETIME2, I.TARIH) AS Tarih,
        CONVERT(DATETIME2, I.TESLIM_TARIHI) AS TeslimTarihi,
        CONVERT(NVARCHAR(50), NULLIF(LTRIM(RTRIM(I.SIPARIS_NO)), '')) AS SiparisNo,
        CONVERT(INT, ISNULL(I.SIPKONT, 0)) AS SiparisSatirNo,
        CONVERT(NVARCHAR(50), NULLIF(LTRIM(RTRIM(I.PROJE_KODU)), '')) AS ProjeKodu,
        CONVERT(INT, ISNULL(I.DEPO_KODU, 0)) AS DepoKodu,
        CONVERT(INT, ISNULL(I.CIKIS_DEPO_KODU, 0)) AS CikisDepoKodu,
        CONVERT(BIT, CASE WHEN UPPER(LTRIM(RTRIM(ISNULL(I.KAPALI, '')))) IN ('1', 'E', 'EVET', 'TRUE') THEN 1 ELSE 0 END) AS Kapali,
        CONVERT(NVARCHAR(1000), NULLIF(LTRIM(RTRIM(I.ACIKLAMA)), '')) AS Aciklama
    FROM V3RIICO.dbo.TBLISEMRI AS I
    OUTER APPLY
    (
        SELECT TOP (1) SX.STOK_ADI, SX.OLCU_BR1, SX.FORMUL_TOPLAMI
        FROM V3RIICO.dbo.TBLSTSABIT AS SX
        WHERE SX.STOK_KODU = I.STOK_KODU
          AND SX.SUBE_KODU IN (I.SUBEKODU, 0)
        ORDER BY CASE WHEN SX.SUBE_KODU = I.SUBEKODU THEN 0 ELSE 1 END
    ) AS S
    WHERE (@IsEmriNo IS NULL OR I.ISEMRINO = @IsEmriNo)
      AND (@SubeKodu IS NULL OR I.SUBEKODU = @SubeKodu)
      AND (@KapaliDahil = 1 OR UPPER(LTRIM(RTRIM(ISNULL(I.KAPALI, '')))) NOT IN ('1', 'E', 'EVET', 'TRUE'))
);
"""));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(SqlServerMigrationSql.CreateOrAlterFunction("dbo.RII_FN_ISEMRI", """
CREATE OR ALTER FUNCTION [dbo].[RII_FN_ISEMRI]
(
    @IsEmriNo NVARCHAR(50) = NULL,
    @SubeKodu INT = NULL,
    @KapaliDahil BIT = 0
)
RETURNS TABLE
AS
RETURN
(
    SELECT
        CONVERT(NVARCHAR(50), I.ISEMRINO) AS IsEmriNo,
        CONVERT(INT, I.SUBEKODU) AS SubeKodu,
        CONVERT(NVARCHAR(50), I.STOK_KODU) AS StokKodu,
        CONVERT(NVARCHAR(200), ISNULL(S.STOK_ADI, I.STOK_KODU)) AS StokAdi,
        CONVERT(NVARCHAR(50), NULLIF(LTRIM(RTRIM(I.YAPKOD)), '')) AS YapilandirmaKodu,
        CONVERT(DECIMAL(28, 8), ISNULL(I.MIKTAR, 0)) AS IsEmriMiktari,
        CONVERT(INT, 1) AS BirimSirasi,
        CONVERT(NVARCHAR(20), NULLIF(LTRIM(RTRIM(S.OLCU_BR1)), '')) AS BirimKodu,
        CONVERT(DECIMAL(28, 8), CASE WHEN ISNULL(S.FORMUL_TOPLAMI, 0) = 0 THEN 1 ELSE S.FORMUL_TOPLAMI END) AS ReceteToplami,
        CONVERT(DATETIME2, I.TARIH) AS Tarih,
        CONVERT(DATETIME2, I.TESLIM_TARIHI) AS TeslimTarihi,
        CONVERT(NVARCHAR(50), NULLIF(LTRIM(RTRIM(I.SIPARIS_NO)), '')) AS SiparisNo,
        CONVERT(INT, ISNULL(I.SIPKONT, 0)) AS SiparisSatirNo,
        CONVERT(NVARCHAR(50), NULLIF(LTRIM(RTRIM(I.PROJE_KODU)), '')) AS ProjeKodu,
        CONVERT(INT, ISNULL(I.DEPO_KODU, 0)) AS DepoKodu,
        CONVERT(INT, ISNULL(I.CIKIS_DEPO_KODU, 0)) AS CikisDepoKodu,
        CONVERT(BIT, CASE WHEN UPPER(LTRIM(RTRIM(ISNULL(I.KAPALI, '')))) IN ('1', 'E', 'EVET', 'TRUE') THEN 1 ELSE 0 END) AS Kapali
    FROM V3RIICO.dbo.TBLISEMRI AS I
    OUTER APPLY
    (
        SELECT TOP (1) SX.STOK_ADI, SX.OLCU_BR1, SX.FORMUL_TOPLAMI
        FROM V3RIICO.dbo.TBLSTSABIT AS SX
        WHERE SX.STOK_KODU = I.STOK_KODU
          AND SX.SUBE_KODU IN (I.SUBEKODU, 0)
        ORDER BY CASE WHEN SX.SUBE_KODU = I.SUBEKODU THEN 0 ELSE 1 END
    ) AS S
    WHERE (@IsEmriNo IS NULL OR I.ISEMRINO = @IsEmriNo)
      AND (@SubeKodu IS NULL OR I.SUBEKODU = @SubeKodu)
      AND (@KapaliDahil = 1 OR UPPER(LTRIM(RTRIM(ISNULL(I.KAPALI, '')))) NOT IN ('1', 'E', 'EVET', 'TRUE'))
);
"""));

            migrationBuilder.DropColumn(
                name: "Description",
                table: "RII_PR_SOURCE_ORDER");
        }
    }
}
