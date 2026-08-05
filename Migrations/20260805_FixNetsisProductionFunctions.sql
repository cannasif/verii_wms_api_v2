/*
  HOTFIX: RII_FN_ISEMRI SUBE_KODU / OLCUBR hatasi
  Netsis TBLISEMRI tablosunda kolon adi SUBEKODU'dur (SUBE_KODU degil).

  HOTFIX 2 (2026-08-05): TBLISEMRI.SUBEKODU bazi is emirlerinde NULL geliyor
  (subesiz/genel is emri). @SubeKodu = I.SUBEKODU esitligi NULL ile hicbir
  zaman TRUE donmedigi icin bu kayitlar oturum subesi ne olursa olsun
  filtreden dusuyordu. WHERE kosuluna "OR I.SUBEKODU IS NULL" eklendi.

  HOTFIX 3 (2026-08-05, proje lideri karari): NULL-fallback musteride hala
  sorunu cozmedi. @SubeKodu parametresi imzada kaldi (geriye donuk uyumluluk
  icin) ama WHERE kosulundaki sube filtrelemesi tamamen kaldirildi. Fonksiyon
  artik sube ayrimi yapmadan tum is emirlerini donduruyor.

  SSMS'te once bu scripti calistirin, ardindan ana migration scriptini
  tekrar calistirin (idempotent, kaldigi yerden devam eder).
*/

SET XACT_ABORT ON;

BEGIN TRY
    BEGIN TRANSACTION;

    IF NOT EXISTS
    (
        SELECT 1 FROM dbo.__EFMigrationsHistory
        WHERE MigrationId = N'20260804095239_AddNetsisProductionReadFunctions'
    )
    BEGIN
        INSERT INTO dbo.__EFMigrationsHistory ([MigrationId], [ProductVersion])
        VALUES (N'20260804095239_AddNetsisProductionReadFunctions', N'10.0.10');
    END;

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
GO

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
      AND (@KapaliDahil = 1 OR UPPER(LTRIM(RTRIM(ISNULL(I.KAPALI, '')))) NOT IN ('1', 'E', 'EVET', 'TRUE'))
);
GO

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
        CONVERT(INT, ISNULL(TRY_CONVERT(INT, NULLIF(LTRIM(RTRIM(R.OPNO)), '')), 0)) AS OperasyonNo,
        CONVERT(DECIMAL(28, 8), CASE WHEN ISNULL(M.FORMUL_TOPLAMI, 0) = 0 THEN 1 ELSE M.FORMUL_TOPLAMI END) AS ReceteToplami,
        CONVERT(DECIMAL(28, 8), ISNULL(R.MIKTAR, 0)) AS ReceteMiktari,
        CONVERT(DECIMAL(28, 8), ISNULL(R.MIKTAR, 0) / CASE WHEN ISNULL(M.FORMUL_TOPLAMI, 0) = 0 THEN 1 ELSE M.FORMUL_TOPLAMI END) AS BirMamulIcinMiktar,
        CONVERT(DECIMAL(28, 8), ISNULL(R.F_YEDEK1, 0)) AS FireDegeri,
        CONVERT(DECIMAL(28, 8), ISNULL(R.F_YEDEK2, 0)) AS SabitFireMiktari,
        CONVERT(BIT, CASE WHEN UPPER(LTRIM(RTRIM(ISNULL(R.MIKTARSABITLE, '')))) IN ('1', 'E', 'EVET', 'TRUE') THEN 1 ELSE 0 END) AS MiktarSabit
    FROM V3RIICO.dbo.TBLSTOKURM AS R
    CROSS APPLY
    (
        SELECT TOP (1) MX.SUBE_KODU, MX.STOK_ADI, MX.OLCU_BR1, MX.FORMUL_TOPLAMI
        FROM V3RIICO.dbo.TBLSTSABIT AS MX
        WHERE MX.STOK_KODU = R.MAMUL_KODU
          AND (@SubeKodu IS NULL OR MX.SUBE_KODU IN (@SubeKodu, 0))
        ORDER BY CASE WHEN @SubeKodu IS NOT NULL AND MX.SUBE_KODU = @SubeKodu THEN 0 WHEN MX.SUBE_KODU = 0 THEN 1 ELSE 2 END, MX.SUBE_KODU
    ) AS M
    OUTER APPLY
    (
        SELECT TOP (1) HX.STOK_ADI, HX.OLCU_BR1
        FROM V3RIICO.dbo.TBLSTSABIT AS HX
        WHERE HX.STOK_KODU = R.HAM_KODU
          AND HX.SUBE_KODU IN (COALESCE(@SubeKodu, M.SUBE_KODU), 0)
        ORDER BY CASE WHEN HX.SUBE_KODU = COALESCE(@SubeKodu, M.SUBE_KODU) THEN 0 ELSE 1 END
    ) AS H
    WHERE R.MAMUL_KODU = @StokKodu
      AND
      (
          ISNULL(NULLIF(LTRIM(RTRIM(R.MAMYAPKOD)), ''), '') = ISNULL(NULLIF(LTRIM(RTRIM(@YapilandirmaKodu)), ''), '')
          OR
          (
              ISNULL(NULLIF(LTRIM(RTRIM(@YapilandirmaKodu)), ''), '') <> ''
              AND ISNULL(NULLIF(LTRIM(RTRIM(R.MAMYAPKOD)), ''), '') = ''
              AND NOT EXISTS
              (
                  SELECT 1 FROM V3RIICO.dbo.TBLSTOKURM AS RX
                  WHERE RX.MAMUL_KODU = @StokKodu
                    AND ISNULL(NULLIF(LTRIM(RTRIM(RX.MAMYAPKOD)), ''), '') = ISNULL(NULLIF(LTRIM(RTRIM(@YapilandirmaKodu)), ''), '')
                    AND UPPER(ISNULL(NULLIF(LTRIM(RTRIM(RX.OPR_BIL)), ''), 'B')) <> 'O'
              )
          )
      )
      AND UPPER(ISNULL(NULLIF(LTRIM(RTRIM(R.OPR_BIL)), ''), 'B')) <> 'O'
);
GO

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
        I.IsEmriNo, I.SubeKodu, I.StokKodu AS MamulKodu, I.StokAdi AS MamulAdi,
        I.YapilandirmaKodu, I.IsEmriMiktari, I.BirimKodu AS MamulBirimKodu,
        R.ReceteToplami, R.BilesenStokKodu, R.BilesenStokAdi, R.BilesenBirimKodu,
        R.BilesenYapilandirmaKodu, R.OperasyonNo, R.ReceteMiktari,
        R.BirMamulIcinMiktar, R.FireDegeri, R.SabitFireMiktari, R.MiktarSabit,
        CONVERT(DECIMAL(28, 8), B.BazIhtiyacMiktari) AS BazIhtiyacMiktari,
        CONVERT(DECIMAL(28, 8), F.DegiskenFireMiktari) AS DegiskenFireMiktari,
        CONVERT(DECIMAL(28, 8), B.BazIhtiyacMiktari + F.DegiskenFireMiktari + R.SabitFireMiktari) AS ToplamIhtiyacMiktari
    FROM dbo.RII_FN_ISEMRI(@IsEmriNo, @SubeKodu, 1) AS I
    CROSS APPLY dbo.RII_FN_STOK_RECETE(I.StokKodu, I.SubeKodu, I.YapilandirmaKodu) AS R
    CROSS APPLY
    (
        SELECT CASE WHEN R.MiktarSabit = 1 THEN R.ReceteMiktari ELSE I.IsEmriMiktari * R.BirMamulIcinMiktar END AS BazIhtiyacMiktari
    ) AS B
    CROSS APPLY
    (
        SELECT CASE
            WHEN R.FireDegeri <= 0 THEN 0
            WHEN R.FireDegeri <= 1 THEN B.BazIhtiyacMiktari * R.FireDegeri
            ELSE (CASE WHEN R.MiktarSabit = 1 THEN 1 ELSE I.IsEmriMiktari / NULLIF(R.ReceteToplami, 0) END) * R.FireDegeri
        END AS DegiskenFireMiktari
    ) AS F
);
GO

BEGIN TRY
    BEGIN TRANSACTION;

    IF NOT EXISTS
    (
        SELECT 1 FROM dbo.__EFMigrationsHistory
        WHERE MigrationId = N'20260804121412_AddCompatibleNetsisProductionReadFunctions'
    )
    BEGIN
        INSERT INTO dbo.__EFMigrationsHistory ([MigrationId], [ProductVersion])
        VALUES (N'20260804121412_AddCompatibleNetsisProductionReadFunctions', N'10.0.10');
    END;

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
GO

SELECT
    OBJECT_ID(N'dbo.RII_FN_ISEMRI') AS IsEmriFunctionId,
    OBJECT_ID(N'dbo.RII_FN_STOK_RECETE') AS StokReceteFunctionId,
    OBJECT_ID(N'dbo.RII_FN_ISEMRI_RECETE') AS IsEmriReceteFunctionId,
    CASE WHEN EXISTS
    (
        SELECT 1 FROM dbo.__EFMigrationsHistory
        WHERE MigrationId = N'20260804121412_AddCompatibleNetsisProductionReadFunctions'
    ) THEN 1 ELSE 0 END AS CompatibleMigrationApplied;
GO
