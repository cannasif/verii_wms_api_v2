using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace verii_wms_api_v2.Migrations
{
    /// <inheritdoc />
    public partial class AddNetsisProductionReadFunctions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
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
        CONVERT(INT, I.SUBE_KODU) AS SubeKodu,
        CONVERT(NVARCHAR(50), I.STOK_KODU) AS StokKodu,
        CONVERT(NVARCHAR(200), ISNULL(S.STOK_ADI, I.STOK_KODU)) AS StokAdi,
        CONVERT(NVARCHAR(50), NULLIF(LTRIM(RTRIM(I.YAPKOD)), '')) AS YapilandirmaKodu,
        CONVERT(DECIMAL(28, 8), ISNULL(I.MIKTAR, 0)) AS IsEmriMiktari,
        CONVERT(INT, ISNULL(I.OLCUBR, 1)) AS BirimSirasi,
        CONVERT(NVARCHAR(20), CASE CONVERT(INT, ISNULL(I.OLCUBR, 1))
            WHEN 2 THEN NULLIF(LTRIM(RTRIM(S.OLCU_BR2)), '')
            WHEN 3 THEN NULLIF(LTRIM(RTRIM(S.OLCU_BR3)), '')
            ELSE NULLIF(LTRIM(RTRIM(S.OLCU_BR1)), '')
        END) AS BirimKodu,
        CONVERT(DECIMAL(28, 8), CASE
            WHEN ISNULL(S.FORMUL_TOPLAMI, 0) = 0 THEN 1
            ELSE S.FORMUL_TOPLAMI
        END) AS ReceteToplami,
        CONVERT(DATETIME2, I.TARIH) AS Tarih,
        CONVERT(DATETIME2, I.TESLIM_TARIHI) AS TeslimTarihi,
        CONVERT(NVARCHAR(50), NULLIF(LTRIM(RTRIM(I.SIPARIS_NO)), '')) AS SiparisNo,
        CONVERT(INT, ISNULL(I.SIPKONT, 0)) AS SiparisSatirNo,
        CONVERT(NVARCHAR(50), NULLIF(LTRIM(RTRIM(I.PROJE_KODU)), '')) AS ProjeKodu,
        CONVERT(INT, ISNULL(I.DEPO_KODU, 0)) AS DepoKodu,
        CONVERT(INT, ISNULL(I.CIKIS_DEPO_KODU, 0)) AS CikisDepoKodu,
        CONVERT(BIT, CASE
            WHEN UPPER(LTRIM(RTRIM(CONVERT(NVARCHAR(10), ISNULL(I.KAPALI, 0))))) IN ('1', 'E', 'EVET', 'TRUE') THEN 1
            ELSE 0
        END) AS Kapali
    FROM V3RIICO.dbo.TBLISEMRI AS I
    OUTER APPLY
    (
        SELECT TOP (1) STOK_ADI, OLCU_BR1, OLCU_BR2, OLCU_BR3, FORMUL_TOPLAMI
        FROM V3RIICO.dbo.TBLSTSABIT AS SX
        WHERE SX.STOK_KODU = I.STOK_KODU
          AND SX.SUBE_KODU IN (I.SUBE_KODU, 0)
        ORDER BY CASE WHEN SX.SUBE_KODU = I.SUBE_KODU THEN 0 ELSE 1 END
    ) AS S
    WHERE (@IsEmriNo IS NULL OR I.ISEMRINO = @IsEmriNo)
      AND (@SubeKodu IS NULL OR I.SUBE_KODU = @SubeKodu)
      AND (@KapaliDahil = 1 OR
           UPPER(LTRIM(RTRIM(CONVERT(NVARCHAR(10), ISNULL(I.KAPALI, 0))))) NOT IN ('1', 'E', 'EVET', 'TRUE'))
);
"""));

            migrationBuilder.Sql(SqlServerMigrationSql.CreateOrAlterFunction("dbo.RII_FN_STOK_RECETE", """
CREATE OR ALTER FUNCTION [dbo].[RII_FN_STOK_RECETE]
(
    @StokKodu NVARCHAR(50),
    @SubeKodu INT = NULL,
    @YapilandirmaKodu NVARCHAR(50) = NULL
)
RETURNS TABLE
AS
RETURN
(
    SELECT
        CONVERT(INT, COALESCE(@SubeKodu, M.SUBE_KODU)) AS SubeKodu,
        CONVERT(NVARCHAR(50), R.MAMUL_KODU) AS MamulKodu,
        CONVERT(NVARCHAR(200), M.STOK_ADI) AS MamulAdi,
        CONVERT(NVARCHAR(20), NULLIF(LTRIM(RTRIM(M.OLCU_BR1)), '')) AS MamulBirimKodu,
        CONVERT(NVARCHAR(50), NULLIF(LTRIM(RTRIM(R.MAMYAPKOD)), '')) AS MamulYapilandirmaKodu,
        CONVERT(NVARCHAR(50), R.HAM_KODU) AS BilesenStokKodu,
        CONVERT(NVARCHAR(200), H.STOK_ADI) AS BilesenStokAdi,
        CONVERT(NVARCHAR(20), NULLIF(LTRIM(RTRIM(H.OLCU_BR1)), '')) AS BilesenBirimKodu,
        CONVERT(NVARCHAR(50), NULLIF(LTRIM(RTRIM(R.HAMYAPKOD)), '')) AS BilesenYapilandirmaKodu,
        CONVERT(INT, ISNULL(R.OPNO, 0)) AS OperasyonNo,
        CONVERT(DECIMAL(28, 8), CASE
            WHEN ISNULL(M.FORMUL_TOPLAMI, 0) = 0 THEN 1
            ELSE M.FORMUL_TOPLAMI
        END) AS ReceteToplami,
        CONVERT(DECIMAL(28, 8), ISNULL(R.MIKTAR, 0)) AS ReceteMiktari,
        CONVERT(DECIMAL(28, 8), ISNULL(R.MIKTAR, 0) /
            CASE WHEN ISNULL(M.FORMUL_TOPLAMI, 0) = 0 THEN 1 ELSE M.FORMUL_TOPLAMI END) AS BirMamulIcinMiktar,
        CONVERT(DECIMAL(28, 8), ISNULL(R.F_YEDEK1, 0)) AS FireDegeri,
        CONVERT(DECIMAL(28, 8), ISNULL(R.F_YEDEK2, 0)) AS SabitFireMiktari,
        CONVERT(BIT, CASE
            WHEN UPPER(LTRIM(RTRIM(CONVERT(NVARCHAR(10), ISNULL(R.MIKTARSABITLE, 0))))) IN ('1', 'E', 'EVET', 'TRUE') THEN 1
            ELSE 0
        END) AS MiktarSabit
    FROM V3RIICO.dbo.TBLSTOKURM AS R
    CROSS APPLY
    (
        SELECT TOP (1) MX.SUBE_KODU, MX.STOK_ADI, MX.OLCU_BR1, MX.FORMUL_TOPLAMI
        FROM V3RIICO.dbo.TBLSTSABIT AS MX
        WHERE MX.STOK_KODU = R.MAMUL_KODU
          AND (@SubeKodu IS NULL OR MX.SUBE_KODU IN (@SubeKodu, 0))
        ORDER BY CASE
            WHEN @SubeKodu IS NOT NULL AND MX.SUBE_KODU = @SubeKodu THEN 0
            WHEN MX.SUBE_KODU = 0 THEN 1
            ELSE 2
        END, MX.SUBE_KODU
    ) AS M
    OUTER APPLY
    (
        SELECT TOP (1) HX.STOK_ADI, HX.OLCU_BR1
        FROM V3RIICO.dbo.TBLSTSABIT AS HX
        WHERE HX.STOK_KODU = R.HAM_KODU
          AND HX.SUBE_KODU IN (COALESCE(@SubeKodu, M.SUBE_KODU), 0)
        ORDER BY CASE
            WHEN HX.SUBE_KODU = COALESCE(@SubeKodu, M.SUBE_KODU) THEN 0
            ELSE 1
        END
    ) AS H
    WHERE R.MAMUL_KODU = @StokKodu
      AND
      (
          ISNULL(NULLIF(LTRIM(RTRIM(R.MAMYAPKOD)), ''), '') =
              ISNULL(NULLIF(LTRIM(RTRIM(@YapilandirmaKodu)), ''), '')
          OR
          (
              ISNULL(NULLIF(LTRIM(RTRIM(@YapilandirmaKodu)), ''), '') <> ''
              AND ISNULL(NULLIF(LTRIM(RTRIM(R.MAMYAPKOD)), ''), '') = ''
              AND NOT EXISTS
              (
                  SELECT 1
                  FROM V3RIICO.dbo.TBLSTOKURM AS RX
                  WHERE RX.MAMUL_KODU = @StokKodu
                    AND ISNULL(NULLIF(LTRIM(RTRIM(RX.MAMYAPKOD)), ''), '') =
                        ISNULL(NULLIF(LTRIM(RTRIM(@YapilandirmaKodu)), ''), '')
                    AND UPPER(ISNULL(NULLIF(LTRIM(RTRIM(RX.OPR_BIL)), ''), 'B')) <> 'O'
              )
          )
      )
      AND UPPER(ISNULL(NULLIF(LTRIM(RTRIM(R.OPR_BIL)), ''), 'B')) <> 'O'
);
"""));

            migrationBuilder.Sql(SqlServerMigrationSql.CreateOrAlterFunction("dbo.RII_FN_ISEMRI_RECETE", """
CREATE OR ALTER FUNCTION [dbo].[RII_FN_ISEMRI_RECETE]
(
    @IsEmriNo NVARCHAR(50),
    @SubeKodu INT = NULL
)
RETURNS TABLE
AS
RETURN
(
    SELECT
        I.IsEmriNo,
        I.SubeKodu,
        I.StokKodu AS MamulKodu,
        I.StokAdi AS MamulAdi,
        I.YapilandirmaKodu,
        I.IsEmriMiktari,
        I.BirimKodu AS MamulBirimKodu,
        R.ReceteToplami,
        R.BilesenStokKodu,
        R.BilesenStokAdi,
        R.BilesenBirimKodu,
        R.BilesenYapilandirmaKodu,
        R.OperasyonNo,
        R.ReceteMiktari,
        R.BirMamulIcinMiktar,
        R.FireDegeri,
        R.SabitFireMiktari,
        R.MiktarSabit,
        CONVERT(DECIMAL(28, 8), CASE
            WHEN R.MiktarSabit = 1 THEN R.ReceteMiktari
            ELSE I.IsEmriMiktari * R.BirMamulIcinMiktar
        END) AS BazIhtiyacMiktari,
        CONVERT(DECIMAL(28, 8), CASE
            WHEN R.FireDegeri <= 0 THEN 0
            WHEN R.FireDegeri <= 1 THEN
                (CASE
                    WHEN R.MiktarSabit = 1 THEN R.ReceteMiktari
                    ELSE I.IsEmriMiktari * R.BirMamulIcinMiktar
                 END) * R.FireDegeri
            ELSE
                (CASE
                    WHEN R.MiktarSabit = 1 THEN 1
                    ELSE I.IsEmriMiktari / NULLIF(R.ReceteToplami, 0)
                 END) * R.FireDegeri
        END) AS DegiskenFireMiktari,
        CONVERT(DECIMAL(28, 8),
            (CASE
                WHEN R.MiktarSabit = 1 THEN R.ReceteMiktari
                ELSE I.IsEmriMiktari * R.BirMamulIcinMiktar
             END)
             + R.SabitFireMiktari) AS ToplamIhtiyacMiktari
    FROM dbo.RII_FN_ISEMRI(@IsEmriNo, @SubeKodu, 1) AS I
    CROSS APPLY dbo.RII_FN_STOK_RECETE(I.StokKodu, I.SubeKodu, I.YapilandirmaKodu) AS R
);
"""));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(SqlServerMigrationSql.DropFunction("dbo.RII_FN_ISEMRI_RECETE"));
            migrationBuilder.Sql(SqlServerMigrationSql.DropFunction("dbo.RII_FN_STOK_RECETE"));
            migrationBuilder.Sql(SqlServerMigrationSql.DropFunction("dbo.RII_FN_ISEMRI"));
        }
    }
}
