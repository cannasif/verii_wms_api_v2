/*
  WMS v2 - Tam Idempotent Migration Script
  Olusturma: 2026-08-06 (rev2: permission group seed EXEC fix)
  Son migration: 20260805192720_AddProductionTaskAssignmentReturn
  Toplam migration: 86

  SSMS KULLANIMI
  1. Hedef WMS veritabanini secin (USE [VeritabaniAdi];)
  2. (Onerilen) Once Migrations/20260805_FixNetsisProductionFunctions.sql calistirin
  3. Bu scripti tek seferde calistirin
  4. Script idempotent'tir; daha once uygulanmis migration'lari atlar
  5. Son satirdaki dogrulama sorgusunu kontrol edin

  NOT: Netsis fonksiyonlari V3RIICO linked server/schema erisimi gerektirir.
  RII_FN_ISEMRI icin TBLISEMRI.SUBEKODU kolonu kullanilir (SUBE_KODU degil).
  FixNetsisProductionFunctions scripti sube filtrelemesini kaldirilmis surumu uygular.
*/
IF OBJECT_ID(N'[__EFMigrationsHistory]') IS NULL
BEGIN
    CREATE TABLE [__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

/*
  CANLI SEMA UZLASTIRMA
  Eski bir calistirmada migration history kaydi yazildigi halde fiziksel kolon
  eksik kalmis olabilir. EF'nin idempotent bloklari history kaydi nedeniyle bu
  durumu kendiliginden onaramaz. Bu blok gercek semayi esas alir ve tekrar
  calistirilmaya uygundur. Bos veritabaninda tablolar henuz olmadigi icin atlanir.
*/
IF OBJECT_ID(N'dbo.RII_WAREHOUSE', N'U') IS NOT NULL
   AND OBJECT_ID(N'dbo.RII_LOCATION', N'U') IS NOT NULL
BEGIN
    BEGIN TRY
        BEGIN TRANSACTION;

        IF COL_LENGTH(N'dbo.RII_WAREHOUSE', N'DefaultGoodsReceiptLocationId') IS NULL
            EXEC sys.sp_executesql N'
                ALTER TABLE [dbo].[RII_WAREHOUSE]
                ADD [DefaultGoodsReceiptLocationId] bigint NULL;';

        EXEC sys.sp_executesql N'
            UPDATE warehouse
            SET warehouse.DefaultGoodsReceiptLocationId = defaultLocation.Id
            FROM [dbo].[RII_WAREHOUSE] AS warehouse
            CROSS APPLY
            (
                SELECT TOP (1) location.Id
                FROM [dbo].[RII_LOCATION] AS location
                WHERE location.WarehouseId = warehouse.Id
                  AND location.BranchCode = warehouse.BranchCode
                  AND location.IsDeleted = 0
                  AND location.IsActive = 1
                  AND UPPER(LTRIM(RTRIM(location.Code))) = N''YER1''
                ORDER BY location.Id
            ) AS defaultLocation
            WHERE warehouse.IsDeleted = 0
              AND warehouse.DefaultGoodsReceiptLocationId IS NULL;';

        IF NOT EXISTS
        (
            SELECT 1 FROM sys.indexes
            WHERE [name] = N'IX_RII_WAREHOUSE_DEFAULT_GR_LOCATION'
              AND [object_id] = OBJECT_ID(N'dbo.RII_WAREHOUSE')
        )
            EXEC sys.sp_executesql N'
                CREATE INDEX [IX_RII_WAREHOUSE_DEFAULT_GR_LOCATION]
                ON [dbo].[RII_WAREHOUSE] ([DefaultGoodsReceiptLocationId]);';

        IF NOT EXISTS
        (
            SELECT 1 FROM sys.foreign_keys
            WHERE [name] = N'FK_RII_WAREHOUSE_RII_LOCATION_DefaultGoodsReceiptLocationId'
              AND [parent_object_id] = OBJECT_ID(N'dbo.RII_WAREHOUSE')
        )
            EXEC sys.sp_executesql N'
                ALTER TABLE [dbo].[RII_WAREHOUSE] WITH CHECK
                ADD CONSTRAINT [FK_RII_WAREHOUSE_RII_LOCATION_DefaultGoodsReceiptLocationId]
                FOREIGN KEY ([DefaultGoodsReceiptLocationId])
                REFERENCES [dbo].[RII_LOCATION] ([Id])
                ON DELETE SET NULL;';

        IF NOT EXISTS
        (
            SELECT 1 FROM [dbo].[__EFMigrationsHistory]
            WHERE [MigrationId] = N'20260730130412_AddWarehouseDefaultGoodsReceiptLocation'
        )
        BEGIN
            INSERT INTO [dbo].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
            VALUES (N'20260730130412_AddWarehouseDefaultGoodsReceiptLocation', N'10.0.10');
        END;

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH;
END;
GO

IF OBJECT_ID(N'[__EFMigrationsHistory]') IS NULL
BEGIN
    CREATE TABLE [__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260721190510_InitialIdentityAndNetsisReadSeed'
)
BEGIN
    IF OBJECT_ID(N'dbo.RII_FN_BRANCHES') IS NULL
    BEGIN
        EXEC sys.sp_executesql N'-- RII_FN_BRANCHES
    CREATE FUNCTION [dbo].[RII_FN_BRANCHES]  
    (  
        @branchNo INT = NULL  
    )  
    RETURNS TABLE  
    AS  
    RETURN  
    (  
        SELECT   
            SUBE_KODU,  
            UNVAN  
        FROM V3RIICO..TBLSUBELER WHERE SUBE_KODU NOT IN(''-1'',''32767'')  
        AND   
            -- Eğer @branchNo NULL ise tüm satırlar döner  
            (@branchNo IS NULL OR SUBE_KODU = @branchNo)  
            -- TBLSUBELER’de MERKEZMI = ''E'' olan satırlarda UNVAN boş olabilir.  
            -- İstersen NULL yerine SUBE_KODU döndürebilirsin.  
    );';
    END
    ELSE
    BEGIN
        EXEC sys.sp_executesql N'-- RII_FN_BRANCHES
    ALTER FUNCTION [dbo].[RII_FN_BRANCHES]  
    (  
        @branchNo INT = NULL  
    )  
    RETURNS TABLE  
    AS  
    RETURN  
    (  
        SELECT   
            SUBE_KODU,  
            UNVAN  
        FROM V3RIICO..TBLSUBELER WHERE SUBE_KODU NOT IN(''-1'',''32767'')  
        AND   
            -- Eğer @branchNo NULL ise tüm satırlar döner  
            (@branchNo IS NULL OR SUBE_KODU = @branchNo)  
            -- TBLSUBELER’de MERKEZMI = ''E'' olan satırlarda UNVAN boş olabilir.  
            -- İstersen NULL yerine SUBE_KODU döndürebilirsin.  
    );';
    END
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260721190510_InitialIdentityAndNetsisReadSeed'
)
BEGIN
    IF OBJECT_ID(N'dbo.RII_FN_CARI') IS NULL
    BEGIN
        EXEC sys.sp_executesql N'-- RII_FN_CARI
    CREATE FUNCTION [dbo].[RII_FN_CARI]
    (    
        @CariKodu NVARCHAR(MAX) = NULL,    
        @SubeKodu NVARCHAR(MAX) = NULL    
    )    
    RETURNS TABLE    
    AS    
    RETURN    
    (  
        SELECT     
            CS.[SUBE_KODU],    
            CS.[ISLETME_KODU],    
            CS.[CARI_KOD],    
            CS.[CARI_TEL],    
            CS.[CARI_IL],    
            CS.[ULKE_KODU],    
            CS.[CARI_ISIM],    
            CS.[CARI_TIP],    
            CS.[GRUP_KODU],    
            CS.[RAPOR_KODU1],    
            CS.[RAPOR_KODU2],    
            CS.[RAPOR_KODU3],    
            CS.[RAPOR_KODU4],    
            CS.[RAPOR_KODU5],    
            CS.[CARI_ADRES],    
            CS.[CARI_ILCE],    
            CS.[VERGI_DAIRESI],    
            CS.[VERGI_NUMARASI],    
            CS.[FAX],    
            CS.[POSTAKODU],    
            CS.[DETAY_KODU],    
            CS.[NAKLIYE_KATSAYISI],    
            CS.[RISK_SINIRI],    
            CS.[TEMINATI],    
            CS.[CARISK],    
            CS.[CCRISK],    
            CS.[SARISK],    
            CS.[SCRISK],    
            CS.[CM_BORCT],    
            CS.[CM_ALACT],    
            CS.[CM_RAP_TARIH],    
            CS.[KOSULKODU],    
            CS.[ISKONTO_ORANI],    
            CS.[VADE_GUNU],    
            CS.[LISTE_FIATI],    
            CS.[ACIK1],    
            CS.[ACIK2],    
            CS.[ACIK3],    
            CS.[M_KOD],    
            CS.[DOVIZ_TIPI],    
            CS.[DOVIZ_TURU],    
            CS.[HESAPTUTMASEKLI],    
            CS.[DOVIZLIMI],    
            CS.[UPDATE_KODU],    
            CS.[PLASIYER_KODU],    
            CS.[LOKALDEPO],    
            CS.[EMAIL],    
            CS.[WEB],    
            CS.[KURFARKIBORC],    
            CS.[KURFARKIALAC],    
            CS.[S_YEDEK1],    
            CS.[S_YEDEK2],    
            CS.[F_YEDEK1],    
            CS.[F_YEDEK2],    
            CS.[C_YEDEK1],    
            CS.[C_YEDEK2],    
            CS.[B_YEDEK1],    
            CS.[I_YEDEK1],    
            CS.[L_YEDEK1],    
            CS.[FIYATGRUBU],    
            CS.[KAYITYAPANKUL],    
            CS.[KAYITTARIHI],    
            CS.[DUZELTMEYAPANKUL],    
            CS.[DUZELTMETARIHI],    
            CS.[ODEMETIPI],    
            CS.[ONAYTIPI],    
            CS.[ONAYNUM],    
            CS.[MUSTERIBAZIKDV],    
            CS.[AGIRLIK_ISK],    
            CS.[CARI_TEL2],    
            CS.[CARI_TEL3],    
            CS.[FAX2],    
            CS.[GSM1],    
            CS.[GSM2],    
            CS.[GEKAPHESAPLANMASIN],    
            CS.[ONCEKI_KOD],    
            CS.[SONRAKI_KOD],    
            CS.[SONCARIKODU],    
            CS.[TESLIMCARIBAGLIMI],    
            CS.[BAGLICARIKOD],    
            CS.[FABRIKA_KODU],    
            CS.[NAKLIYE_SURESI],    
            CS.[TESLIMAT_PERIYOD_TIPI],    
            CS.[TESLIMAT_GUNU],    
            CS.[TESLIMAT_EXTRAINFO],    
            CE.[TCKIMLIKNO]    
        FROM V3RIICO..TBLCASABIT CS    
        LEFT JOIN V3RIICO..TBLCASABITEK CE   
            ON CS.CARI_KOD = CE.CARI_KOD    
        WHERE    
            (NULLIF(REPLACE(LTRIM(RTRIM(@CariKodu)), N'' '', N''''), N'''') IS NULL
             OR CHARINDEX(
                 N'','' + LTRIM(RTRIM(CONVERT(NVARCHAR(100), CS.CARI_KOD))) + N'','',
                 N'','' + REPLACE(@CariKodu, N'' '', N'''') + N'','') > 0)
            AND
            (NULLIF(REPLACE(LTRIM(RTRIM(@SubeKodu)), N'' '', N''''), N'''') IS NULL
             OR CHARINDEX(
                 N'','' + LTRIM(RTRIM(CONVERT(NVARCHAR(50), CS.SUBE_KODU))) + N'','',
                 N'','' + REPLACE(@SubeKodu, N'' '', N'''') + N'','') > 0)
    );';
    END
    ELSE
    BEGIN
        EXEC sys.sp_executesql N'-- RII_FN_CARI
    ALTER FUNCTION [dbo].[RII_FN_CARI]
    (    
        @CariKodu NVARCHAR(MAX) = NULL,    
        @SubeKodu NVARCHAR(MAX) = NULL    
    )    
    RETURNS TABLE    
    AS    
    RETURN    
    (  
        SELECT     
            CS.[SUBE_KODU],    
            CS.[ISLETME_KODU],    
            CS.[CARI_KOD],    
            CS.[CARI_TEL],    
            CS.[CARI_IL],    
            CS.[ULKE_KODU],    
            CS.[CARI_ISIM],    
            CS.[CARI_TIP],    
            CS.[GRUP_KODU],    
            CS.[RAPOR_KODU1],    
            CS.[RAPOR_KODU2],    
            CS.[RAPOR_KODU3],    
            CS.[RAPOR_KODU4],    
            CS.[RAPOR_KODU5],    
            CS.[CARI_ADRES],    
            CS.[CARI_ILCE],    
            CS.[VERGI_DAIRESI],    
            CS.[VERGI_NUMARASI],    
            CS.[FAX],    
            CS.[POSTAKODU],    
            CS.[DETAY_KODU],    
            CS.[NAKLIYE_KATSAYISI],    
            CS.[RISK_SINIRI],    
            CS.[TEMINATI],    
            CS.[CARISK],    
            CS.[CCRISK],    
            CS.[SARISK],    
            CS.[SCRISK],    
            CS.[CM_BORCT],    
            CS.[CM_ALACT],    
            CS.[CM_RAP_TARIH],    
            CS.[KOSULKODU],    
            CS.[ISKONTO_ORANI],    
            CS.[VADE_GUNU],    
            CS.[LISTE_FIATI],    
            CS.[ACIK1],    
            CS.[ACIK2],    
            CS.[ACIK3],    
            CS.[M_KOD],    
            CS.[DOVIZ_TIPI],    
            CS.[DOVIZ_TURU],    
            CS.[HESAPTUTMASEKLI],    
            CS.[DOVIZLIMI],    
            CS.[UPDATE_KODU],    
            CS.[PLASIYER_KODU],    
            CS.[LOKALDEPO],    
            CS.[EMAIL],    
            CS.[WEB],    
            CS.[KURFARKIBORC],    
            CS.[KURFARKIALAC],    
            CS.[S_YEDEK1],    
            CS.[S_YEDEK2],    
            CS.[F_YEDEK1],    
            CS.[F_YEDEK2],    
            CS.[C_YEDEK1],    
            CS.[C_YEDEK2],    
            CS.[B_YEDEK1],    
            CS.[I_YEDEK1],    
            CS.[L_YEDEK1],    
            CS.[FIYATGRUBU],    
            CS.[KAYITYAPANKUL],    
            CS.[KAYITTARIHI],    
            CS.[DUZELTMEYAPANKUL],    
            CS.[DUZELTMETARIHI],    
            CS.[ODEMETIPI],    
            CS.[ONAYTIPI],    
            CS.[ONAYNUM],    
            CS.[MUSTERIBAZIKDV],    
            CS.[AGIRLIK_ISK],    
            CS.[CARI_TEL2],    
            CS.[CARI_TEL3],    
            CS.[FAX2],    
            CS.[GSM1],    
            CS.[GSM2],    
            CS.[GEKAPHESAPLANMASIN],    
            CS.[ONCEKI_KOD],    
            CS.[SONRAKI_KOD],    
            CS.[SONCARIKODU],    
            CS.[TESLIMCARIBAGLIMI],    
            CS.[BAGLICARIKOD],    
            CS.[FABRIKA_KODU],    
            CS.[NAKLIYE_SURESI],    
            CS.[TESLIMAT_PERIYOD_TIPI],    
            CS.[TESLIMAT_GUNU],    
            CS.[TESLIMAT_EXTRAINFO],    
            CE.[TCKIMLIKNO]    
        FROM V3RIICO..TBLCASABIT CS    
        LEFT JOIN V3RIICO..TBLCASABITEK CE   
            ON CS.CARI_KOD = CE.CARI_KOD    
        WHERE    
            (NULLIF(REPLACE(LTRIM(RTRIM(@CariKodu)), N'' '', N''''), N'''') IS NULL
             OR CHARINDEX(
                 N'','' + LTRIM(RTRIM(CONVERT(NVARCHAR(100), CS.CARI_KOD))) + N'','',
                 N'','' + REPLACE(@CariKodu, N'' '', N'''') + N'','') > 0)
            AND
            (NULLIF(REPLACE(LTRIM(RTRIM(@SubeKodu)), N'' '', N''''), N'''') IS NULL
             OR CHARINDEX(
                 N'','' + LTRIM(RTRIM(CONVERT(NVARCHAR(50), CS.SUBE_KODU))) + N'','',
                 N'','' + REPLACE(@SubeKodu, N'' '', N'''') + N'','') > 0)
    );';
    END
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260721190510_InitialIdentityAndNetsisReadSeed'
)
BEGIN
    IF OBJECT_ID(N'dbo.RII_FN_DEPO') IS NULL
    BEGIN
        EXEC sys.sp_executesql N'-- RII_FN_DEPO
    CREATE FUNCTION [dbo].[RII_FN_DEPO]
    (  
        @DepoKodu NVARCHAR(MAX) = NULL,   -- A11,A12,A13 gibi  
        @SubeKodu NVARCHAR(50) = NULL  
    )  
    RETURNS TABLE  
    AS  
    RETURN  
    (  
        SELECT   
              [DEPO_KODU]  
            , [DEPO_ISMI]  
            , [DEPO_KILITLE]  
            , [CARI_KODU]  
            , [EKSIBAKIYE]  
            , [FIAT_TIPI]  
            , [SUBE_KODU]  
            , [S_YEDEK1]  
            , [S_YEDEK2]  
            , [I_YEDEK1]  
            , [I_YEDEK2]  
            , [C_YEDEK1]  
            , [C_YEDEK2]  
            , [D_YEDEK1]  
            , [KAYITYAPANKUL]  
            , [KAYITTARIHI]  
            , [DUZELTMEYAPANKUL]  
            , [DUZELTMETARIHI]  
            , [EMANETDEPO]  
            , [KILIT_POLITIKASI]  
        FROM V3RIICO..TBLSTOKDP D  
        WHERE  
            (  
                @DepoKodu IS NULL OR @DepoKodu = ''''   
                OR CHARINDEX(
                    N'','' + LTRIM(RTRIM(CONVERT(NVARCHAR(100), D.DEPO_KODU))) + N'','',
                    N'','' + REPLACE(@DepoKodu, N'' '', N'''') + N'','') > 0
            )  
            AND (@SubeKodu IS NULL OR @SubeKodu = '''' OR D.SUBE_KODU = @SubeKodu)  
    );';
    END
    ELSE
    BEGIN
        EXEC sys.sp_executesql N'-- RII_FN_DEPO
    ALTER FUNCTION [dbo].[RII_FN_DEPO]
    (  
        @DepoKodu NVARCHAR(MAX) = NULL,   -- A11,A12,A13 gibi  
        @SubeKodu NVARCHAR(50) = NULL  
    )  
    RETURNS TABLE  
    AS  
    RETURN  
    (  
        SELECT   
              [DEPO_KODU]  
            , [DEPO_ISMI]  
            , [DEPO_KILITLE]  
            , [CARI_KODU]  
            , [EKSIBAKIYE]  
            , [FIAT_TIPI]  
            , [SUBE_KODU]  
            , [S_YEDEK1]  
            , [S_YEDEK2]  
            , [I_YEDEK1]  
            , [I_YEDEK2]  
            , [C_YEDEK1]  
            , [C_YEDEK2]  
            , [D_YEDEK1]  
            , [KAYITYAPANKUL]  
            , [KAYITTARIHI]  
            , [DUZELTMEYAPANKUL]  
            , [DUZELTMETARIHI]  
            , [EMANETDEPO]  
            , [KILIT_POLITIKASI]  
        FROM V3RIICO..TBLSTOKDP D  
        WHERE  
            (  
                @DepoKodu IS NULL OR @DepoKodu = ''''   
                OR CHARINDEX(
                    N'','' + LTRIM(RTRIM(CONVERT(NVARCHAR(100), D.DEPO_KODU))) + N'','',
                    N'','' + REPLACE(@DepoKodu, N'' '', N'''') + N'','') > 0
            )  
            AND (@SubeKodu IS NULL OR @SubeKodu = '''' OR D.SUBE_KODU = @SubeKodu)  
    );';
    END
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260721190510_InitialIdentityAndNetsisReadSeed'
)
BEGIN
    IF OBJECT_ID(N'dbo.RII_FN_ESNYAPMAS') IS NULL
    BEGIN
        EXEC sys.sp_executesql N'-- RII_FN_ESNYAPMAS
    CREATE FUNCTION [dbo].[RII_FN_ESNYAPMAS] ()
    RETURNS TABLE    
    AS    
    RETURN    
    (    
        SELECT     
            YAPKOD,    
            YAPACIK,    
            SUBE_KODU,    
            YPLNDRSTOKKOD,
            CAST(NULL AS BIGINT) AS StockId
        FROM V3RIICO..TBLESNYAPMAS AS ESNYAPMAS
    );';
    END
    ELSE
    BEGIN
        EXEC sys.sp_executesql N'-- RII_FN_ESNYAPMAS
    ALTER FUNCTION [dbo].[RII_FN_ESNYAPMAS] ()
    RETURNS TABLE    
    AS    
    RETURN    
    (    
        SELECT     
            YAPKOD,    
            YAPACIK,    
            SUBE_KODU,    
            YPLNDRSTOKKOD,
            CAST(NULL AS BIGINT) AS StockId
        FROM V3RIICO..TBLESNYAPMAS AS ESNYAPMAS
    );';
    END
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260721190510_InitialIdentityAndNetsisReadSeed'
)
BEGIN
    IF OBJECT_ID(N'dbo.RII_FN_STOK') IS NULL
    BEGIN
        EXEC sys.sp_executesql N'-- RII_FN_STOK
    CREATE FUNCTION [dbo].[RII_FN_STOK]
    (    
        @StokKodu NVARCHAR(MAX) = NULL,   -- Artık birden fazla değer içerebilir  
        @SubeKodu NVARCHAR(MAX) = NULL    
    )    
    RETURNS TABLE    
    AS    
    RETURN    
    (  
        SELECT       
            X.SUBE_KODU,    
            X.ISLETME_KODU,    
            X.STOK_KODU,    
            X.URETICI_KODU,    
            X.STOK_ADI,    
            X.GRUP_KODU,    
            X.KOD_1,    
            X.KOD_2,    
            X.KOD_3,    
            X.KOD_4,    
            X.KOD_5,    
            X.SATICI_KODU,    
            X.OLCU_BR1,    
            X.OLCU_BR2,    
            X.PAY_1,    
            X.PAYDA_1,    
            X.OLCU_BR3,    
            X.PAY2,    
            X.PAYDA2,    
            X.FIAT_BIRIMI,    
            X.AZAMI_STOK,    
            X.ASGARI_STOK,    
            X.TEMIN_SURESI,    
            X.KUL_MIK,    
            X.RISK_SURESI,    
            X.ZAMAN_BIRIMI,    
            X.SATIS_FIAT1,    
            X.SATIS_FIAT2,    
            X.SATIS_FIAT3,    
            X.SATIS_FIAT4,    
            X.SAT_DOV_TIP,    
            X.DOV_ALIS_FIAT,    
            X.DOV_MAL_FIAT,    
            X.DOV_SATIS_FIAT,    
            X.MUH_DETAYKODU,    
            X.BIRIM_AGIRLIK,    
            X.NAKLIYET_TUT,    
            X.KDV_ORANI,    
            X.ALIS_DOV_TIP,    
            X.DEPO_KODU,    
            X.DOV_TUR,    
            X.URET_OLCU_BR,    
            X.BILESENMI,    
            X.MAMULMU,    
            X.FORMUL_TOPLAMI,    
            X.UPDATE_KODU,    
            X.MAX_ISKONTO,    
            X.ECZACI_KARI,    
            X.MIKTAR,    
            X.MAL_FAZLASI,    
            X.KDV_TENZIL_ORAN,    
            X.KILIT,    
            X.ONCEKI_KOD,    
            X.SONRAKI_KOD,    
            X.BARKOD1,    
            X.BARKOD2,    
            X.BARKOD3,    
            X.ALIS_KDV_KODU,    
            X.ALIS_FIAT1,    
            X.ALIS_FIAT2,    
            X.ALIS_FIAT3,    
            X.ALIS_FIAT4,    
            X.LOT_SIZE,    
            X.MIN_SIP_MIKTAR,    
            X.SABIT_SIP_ARALIK,    
            X.SIP_POLITIKASI,    
            X.OZELLIK_KODU1,    
            X.OZELLIK_KODU2,    
            X.OZELLIK_KODU3,    
            X.OZELLIK_KODU4,    
            X.OZELLIK_KODU5,    
            X.OPSIYON_KODU1,    
            X.OPSIYON_KODU2,    
            X.OPSIYON_KODU3,    
            X.OPSIYON_KODU4,    
            X.OPSIYON_KODU5,    
            X.BILESEN_OP_KODU,    
            X.SIP_VER_MAL,    
            X.ELDE_BUL_MAL,    
            X.YIL_TAH_KUL_MIK,    
            X.EKON_SIP_MIKTAR,    
            X.ESKI_RECETE,    
            X.OTOMATIK_URETIM,    
            X.ALFKOD,    
            X.SAFKOD,    
            X.KODTURU,    
            X.S_YEDEK1,    
            X.S_YEDEK2,    
            X.F_YEDEK3,    
            X.F_YEDEK4,    
            X.C_YEDEK5,    
            X.C_YEDEK6,    
            X.B_YEDEK7,    
            X.I_YEDEK8,    
            X.L_YEDEK9,    
            X.D_YEDEK10,    
      
            ISNULL(X.GIRIS_SERI, ''H'') AS GIRIS_SERI,    
            ISNULL(X.CIKIS_SERI, ''H'') AS CIKIS_SERI,    
            ISNULL(X.SERI_BAK, ''H'') AS SERI_BAK,    
            ISNULL(X.SERI_MIK, ''H'') AS SERI_MIK,    
            ISNULL(X.SERI_GIR_OT, ''H'') AS SERI_GIR_OT,    
            ISNULL(X.SERI_CIK_OT, ''H'') AS SERI_CIK_OT,    
      
            X.SERI_BASLANGIC,    
            X.FIYATKODU,    
            X.FIYATSIRASI,    
            X.PLANLANACAK,    
            X.LOT_SIZECUSTOMER,    
            X.MIN_SIP_MIKTARCUSTOMER,    
            X.GUMRUKTARIFEKODU,    
            X.ABCKODU,    
            X.PERFORMANSKODU,    
            X.SATICISIPKILIT,    
            X.MUSTERISIPKILIT,    
            X.SATINALMAKILIT,    
            X.SATISKILIT,    
            X.EN,    
            X.BOY,    
            X.GENISLIK,    
            X.SIPLIMITVAR,    
            X.SONSTOKKODU,    
            X.ONAYTIPI,    
            X.ONAYNUM,    
            X.FIKTIF_MAM,    
            X.YAPILANDIR,    
            X.SBOMVARMI,    
            X.BAGLISTOKKOD,    
            X.YAPKOD,    
            X.ALISTALTEKKILIT,    
            X.SATISTALTEKKILIT,    
       X.S_YEDEK3,    
            X.STOKMEVZUAT,    
            X.OTVTEVKIFAT,    
            X.SERIBARKOD,    
            X.ATIK_URUN,    
            Y.TUR,    
            Y.KAYITTARIHI,    
            Y.INGISIM    
        FROM v3riico..TBLSTSABIT X    
        LEFT JOIN V3RIICO..TBLSTSABITEK Y WITH (NOLOCK)     
            ON X.STOK_KODU = Y.STOK_KODU    
        WHERE    
            (NULLIF(REPLACE(LTRIM(RTRIM(@StokKodu)), N'' '', N''''), N'''') IS NULL
             OR CHARINDEX(
                 N'','' + LTRIM(RTRIM(CONVERT(NVARCHAR(100), X.STOK_KODU))) + N'','',
                 N'','' + REPLACE(@StokKodu, N'' '', N'''') + N'','') > 0)
            AND
            (NULLIF(REPLACE(LTRIM(RTRIM(@SubeKodu)), N'' '', N''''), N'''') IS NULL
             OR CHARINDEX(
                 N'','' + LTRIM(RTRIM(CONVERT(NVARCHAR(50), X.SUBE_KODU))) + N'','',
                 N'','' + REPLACE(@SubeKodu, N'' '', N'''') + N'','') > 0)
    );';
    END
    ELSE
    BEGIN
        EXEC sys.sp_executesql N'-- RII_FN_STOK
    ALTER FUNCTION [dbo].[RII_FN_STOK]
    (    
        @StokKodu NVARCHAR(MAX) = NULL,   -- Artık birden fazla değer içerebilir  
        @SubeKodu NVARCHAR(MAX) = NULL    
    )    
    RETURNS TABLE    
    AS    
    RETURN    
    (  
        SELECT       
            X.SUBE_KODU,    
            X.ISLETME_KODU,    
            X.STOK_KODU,    
            X.URETICI_KODU,    
            X.STOK_ADI,    
            X.GRUP_KODU,    
            X.KOD_1,    
            X.KOD_2,    
            X.KOD_3,    
            X.KOD_4,    
            X.KOD_5,    
            X.SATICI_KODU,    
            X.OLCU_BR1,    
            X.OLCU_BR2,    
            X.PAY_1,    
            X.PAYDA_1,    
            X.OLCU_BR3,    
            X.PAY2,    
            X.PAYDA2,    
            X.FIAT_BIRIMI,    
            X.AZAMI_STOK,    
            X.ASGARI_STOK,    
            X.TEMIN_SURESI,    
            X.KUL_MIK,    
            X.RISK_SURESI,    
            X.ZAMAN_BIRIMI,    
            X.SATIS_FIAT1,    
            X.SATIS_FIAT2,    
            X.SATIS_FIAT3,    
            X.SATIS_FIAT4,    
            X.SAT_DOV_TIP,    
            X.DOV_ALIS_FIAT,    
            X.DOV_MAL_FIAT,    
            X.DOV_SATIS_FIAT,    
            X.MUH_DETAYKODU,    
            X.BIRIM_AGIRLIK,    
            X.NAKLIYET_TUT,    
            X.KDV_ORANI,    
            X.ALIS_DOV_TIP,    
            X.DEPO_KODU,    
            X.DOV_TUR,    
            X.URET_OLCU_BR,    
            X.BILESENMI,    
            X.MAMULMU,    
            X.FORMUL_TOPLAMI,    
            X.UPDATE_KODU,    
            X.MAX_ISKONTO,    
            X.ECZACI_KARI,    
            X.MIKTAR,    
            X.MAL_FAZLASI,    
            X.KDV_TENZIL_ORAN,    
            X.KILIT,    
            X.ONCEKI_KOD,    
            X.SONRAKI_KOD,    
            X.BARKOD1,    
            X.BARKOD2,    
            X.BARKOD3,    
            X.ALIS_KDV_KODU,    
            X.ALIS_FIAT1,    
            X.ALIS_FIAT2,    
            X.ALIS_FIAT3,    
            X.ALIS_FIAT4,    
            X.LOT_SIZE,    
            X.MIN_SIP_MIKTAR,    
            X.SABIT_SIP_ARALIK,    
            X.SIP_POLITIKASI,    
            X.OZELLIK_KODU1,    
            X.OZELLIK_KODU2,    
            X.OZELLIK_KODU3,    
            X.OZELLIK_KODU4,    
            X.OZELLIK_KODU5,    
            X.OPSIYON_KODU1,    
            X.OPSIYON_KODU2,    
            X.OPSIYON_KODU3,    
            X.OPSIYON_KODU4,    
            X.OPSIYON_KODU5,    
            X.BILESEN_OP_KODU,    
            X.SIP_VER_MAL,    
            X.ELDE_BUL_MAL,    
            X.YIL_TAH_KUL_MIK,    
            X.EKON_SIP_MIKTAR,    
            X.ESKI_RECETE,    
            X.OTOMATIK_URETIM,    
            X.ALFKOD,    
            X.SAFKOD,    
            X.KODTURU,    
            X.S_YEDEK1,    
            X.S_YEDEK2,    
            X.F_YEDEK3,    
            X.F_YEDEK4,    
            X.C_YEDEK5,    
            X.C_YEDEK6,    
            X.B_YEDEK7,    
            X.I_YEDEK8,    
            X.L_YEDEK9,    
            X.D_YEDEK10,    
      
            ISNULL(X.GIRIS_SERI, ''H'') AS GIRIS_SERI,    
            ISNULL(X.CIKIS_SERI, ''H'') AS CIKIS_SERI,    
            ISNULL(X.SERI_BAK, ''H'') AS SERI_BAK,    
            ISNULL(X.SERI_MIK, ''H'') AS SERI_MIK,    
            ISNULL(X.SERI_GIR_OT, ''H'') AS SERI_GIR_OT,    
            ISNULL(X.SERI_CIK_OT, ''H'') AS SERI_CIK_OT,    
      
            X.SERI_BASLANGIC,    
            X.FIYATKODU,    
            X.FIYATSIRASI,    
            X.PLANLANACAK,    
            X.LOT_SIZECUSTOMER,    
            X.MIN_SIP_MIKTARCUSTOMER,    
            X.GUMRUKTARIFEKODU,    
            X.ABCKODU,    
            X.PERFORMANSKODU,    
            X.SATICISIPKILIT,    
            X.MUSTERISIPKILIT,    
            X.SATINALMAKILIT,    
            X.SATISKILIT,    
            X.EN,    
            X.BOY,    
            X.GENISLIK,    
            X.SIPLIMITVAR,    
            X.SONSTOKKODU,    
            X.ONAYTIPI,    
            X.ONAYNUM,    
            X.FIKTIF_MAM,    
            X.YAPILANDIR,    
            X.SBOMVARMI,    
            X.BAGLISTOKKOD,    
            X.YAPKOD,    
            X.ALISTALTEKKILIT,    
            X.SATISTALTEKKILIT,    
       X.S_YEDEK3,    
            X.STOKMEVZUAT,    
            X.OTVTEVKIFAT,    
            X.SERIBARKOD,    
            X.ATIK_URUN,    
            Y.TUR,    
            Y.KAYITTARIHI,    
            Y.INGISIM    
        FROM v3riico..TBLSTSABIT X    
        LEFT JOIN V3RIICO..TBLSTSABITEK Y WITH (NOLOCK)     
            ON X.STOK_KODU = Y.STOK_KODU    
        WHERE    
            (NULLIF(REPLACE(LTRIM(RTRIM(@StokKodu)), N'' '', N''''), N'''') IS NULL
             OR CHARINDEX(
                 N'','' + LTRIM(RTRIM(CONVERT(NVARCHAR(100), X.STOK_KODU))) + N'','',
                 N'','' + REPLACE(@StokKodu, N'' '', N'''') + N'','') > 0)
            AND
            (NULLIF(REPLACE(LTRIM(RTRIM(@SubeKodu)), N'' '', N''''), N'''') IS NULL
             OR CHARINDEX(
                 N'','' + LTRIM(RTRIM(CONVERT(NVARCHAR(50), X.SUBE_KODU))) + N'','',
                 N'','' + REPLACE(@SubeKodu, N'' '', N'''') + N'','') > 0)
    );';
    END
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260721190510_InitialIdentityAndNetsisReadSeed'
)
BEGIN
    CREATE TABLE [RII_USERS] (
        [Id] bigint NOT NULL IDENTITY,
        [Username] nvarchar(100) NOT NULL,
        [Email] nvarchar(200) NOT NULL,
        [PasswordHash] nvarchar(500) NOT NULL,
        [Role] nvarchar(50) NOT NULL,
        [IsActive] bit NOT NULL,
        [LastLoginAt] datetime2 NULL,
        [RefreshToken] nvarchar(500) NULL,
        [RefreshTokenExpiresAt] datetime2 NULL,
        CONSTRAINT [PK_RII_USERS] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260721190510_InitialIdentityAndNetsisReadSeed'
)
BEGIN
    CREATE TABLE [RII_USER_DETAILS] (
        [UserId] bigint NOT NULL,
        [FirstName] nvarchar(100) NOT NULL,
        [LastName] nvarchar(100) NOT NULL,
        [Phone] nvarchar(30) NULL,
        CONSTRAINT [PK_RII_USER_DETAILS] PRIMARY KEY ([UserId]),
        CONSTRAINT [FK_RII_USER_DETAILS_RII_USERS_UserId] FOREIGN KEY ([UserId]) REFERENCES [RII_USERS] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260721190510_InitialIdentityAndNetsisReadSeed'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'Email', N'IsActive', N'LastLoginAt', N'PasswordHash', N'RefreshToken', N'RefreshTokenExpiresAt', N'Role', N'Username') AND [object_id] = OBJECT_ID(N'[RII_USERS]'))
        SET IDENTITY_INSERT [RII_USERS] ON;
    EXEC(N'INSERT INTO [RII_USERS] ([Id], [Email], [IsActive], [LastLoginAt], [PasswordHash], [RefreshToken], [RefreshTokenExpiresAt], [Role], [Username])
    VALUES (CAST(1 AS bigint), N''admin@v3rii.com'', CAST(1 AS bit), NULL, N''$2a$11$/miyTaLTVkU0keOJabjkQ.bKF4Rb6a2jhuLWDz67I4LLxjwWQ6IJW'', NULL, NULL, N''superadmin'', N''admin'')');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'Email', N'IsActive', N'LastLoginAt', N'PasswordHash', N'RefreshToken', N'RefreshTokenExpiresAt', N'Role', N'Username') AND [object_id] = OBJECT_ID(N'[RII_USERS]'))
        SET IDENTITY_INSERT [RII_USERS] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260721190510_InitialIdentityAndNetsisReadSeed'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'UserId', N'FirstName', N'LastName', N'Phone') AND [object_id] = OBJECT_ID(N'[RII_USER_DETAILS]'))
        SET IDENTITY_INSERT [RII_USER_DETAILS] ON;
    EXEC(N'INSERT INTO [RII_USER_DETAILS] ([UserId], [FirstName], [LastName], [Phone])
    VALUES (CAST(1 AS bigint), N''System'', N''Administrator'', NULL)');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'UserId', N'FirstName', N'LastName', N'Phone') AND [object_id] = OBJECT_ID(N'[RII_USER_DETAILS]'))
        SET IDENTITY_INSERT [RII_USER_DETAILS] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260721190510_InitialIdentityAndNetsisReadSeed'
)
BEGIN
    CREATE UNIQUE INDEX [IX_RII_USERS_Email] ON [RII_USERS] ([Email]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260721190510_InitialIdentityAndNetsisReadSeed'
)
BEGIN
    CREATE UNIQUE INDEX [IX_RII_USERS_Username] ON [RII_USERS] ([Username]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260721190510_InitialIdentityAndNetsisReadSeed'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260721190510_InitialIdentityAndNetsisReadSeed', N'10.0.10');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260721194229_AddErpMirrorTables'
)
BEGIN
    CREATE TABLE [RII_CUSTOMER] (
        [Id] bigint NOT NULL IDENTITY,
        [BusinessUnitCode] smallint NOT NULL,
        [CustomerCode] nvarchar(50) NOT NULL,
        [CustomerName] nvarchar(200) NOT NULL,
        [LastSyncDate] datetime2 NULL,
        [BranchCode] nvarchar(10) NOT NULL DEFAULT N'0',
        [CreatedDate] datetime2 NULL,
        [UpdatedDate] datetime2 NULL,
        [DeletedDate] datetime2 NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [CreatedBy] bigint NULL,
        [UpdatedBy] bigint NULL,
        [DeletedBy] bigint NULL,
        CONSTRAINT [PK_RII_CUSTOMER] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260721194229_AddErpMirrorTables'
)
BEGIN
    CREATE TABLE [RII_STOCK] (
        [Id] bigint NOT NULL IDENTITY,
        [BusinessUnitCode] smallint NOT NULL,
        [ErpStockCode] nvarchar(50) NOT NULL,
        [StockName] nvarchar(250) NOT NULL,
        [ManufacturerCode] nvarchar(50) NULL,
        [GroupCode] nvarchar(50) NULL,
        [Code1] nvarchar(50) NULL,
        [Code2] nvarchar(50) NULL,
        [Code3] nvarchar(50) NULL,
        [Code4] nvarchar(50) NULL,
        [Code5] nvarchar(50) NULL,
        [LastSyncDate] datetime2 NULL,
        [BranchCode] nvarchar(10) NOT NULL DEFAULT N'0',
        [CreatedDate] datetime2 NULL,
        [UpdatedDate] datetime2 NULL,
        [DeletedDate] datetime2 NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [CreatedBy] bigint NULL,
        [UpdatedBy] bigint NULL,
        [DeletedBy] bigint NULL,
        CONSTRAINT [PK_RII_STOCK] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260721194229_AddErpMirrorTables'
)
BEGIN
    CREATE TABLE [RII_WAREHOUSE] (
        [Id] bigint NOT NULL IDENTITY,
        [WarehouseCode] int NOT NULL,
        [WarehouseName] nvarchar(250) NOT NULL,
        [LastSyncDate] datetime2 NULL,
        [BranchCode] nvarchar(10) NOT NULL DEFAULT N'0',
        [CreatedDate] datetime2 NULL,
        [UpdatedDate] datetime2 NULL,
        [DeletedDate] datetime2 NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [CreatedBy] bigint NULL,
        [UpdatedBy] bigint NULL,
        [DeletedBy] bigint NULL,
        CONSTRAINT [PK_RII_WAREHOUSE] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260721194229_AddErpMirrorTables'
)
BEGIN
    CREATE TABLE [RII_YAP_CODE] (
        [Id] bigint NOT NULL IDENTITY,
        [ConfigurationCode] nvarchar(15) NOT NULL,
        [Description] nvarchar(255) NOT NULL,
        [ConfigurableStockCode] nvarchar(35) NULL,
        [StockId] bigint NULL,
        [LastSyncDate] datetime2 NULL,
        [BranchCode] nvarchar(10) NOT NULL DEFAULT N'0',
        [CreatedDate] datetime2 NULL,
        [UpdatedDate] datetime2 NULL,
        [DeletedDate] datetime2 NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [CreatedBy] bigint NULL,
        [UpdatedBy] bigint NULL,
        [DeletedBy] bigint NULL,
        CONSTRAINT [PK_RII_YAP_CODE] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_RII_YAP_CODE_RII_STOCK_StockId] FOREIGN KEY ([StockId]) REFERENCES [RII_STOCK] ([Id]) ON DELETE SET NULL
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260721194229_AddErpMirrorTables'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_Customer_BranchCode_CustomerCode] ON [RII_CUSTOMER] ([BranchCode], [CustomerCode]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260721194229_AddErpMirrorTables'
)
BEGIN
    CREATE INDEX [IX_Customer_CustomerName] ON [RII_CUSTOMER] ([CustomerName]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260721194229_AddErpMirrorTables'
)
BEGIN
    CREATE INDEX [IX_RII_CUSTOMER_IsDeleted] ON [RII_CUSTOMER] ([IsDeleted]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260721194229_AddErpMirrorTables'
)
BEGIN
    CREATE INDEX [IX_RII_STOCK_IsDeleted] ON [RII_STOCK] ([IsDeleted]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260721194229_AddErpMirrorTables'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_Stock_BranchCode_ErpStockCode] ON [RII_STOCK] ([BranchCode], [ErpStockCode]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260721194229_AddErpMirrorTables'
)
BEGIN
    CREATE INDEX [IX_Stock_StockName] ON [RII_STOCK] ([StockName]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260721194229_AddErpMirrorTables'
)
BEGIN
    CREATE INDEX [IX_RII_WAREHOUSE_IsDeleted] ON [RII_WAREHOUSE] ([IsDeleted]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260721194229_AddErpMirrorTables'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_Warehouse_BranchCode_WarehouseCode] ON [RII_WAREHOUSE] ([BranchCode], [WarehouseCode]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260721194229_AddErpMirrorTables'
)
BEGIN
    CREATE INDEX [IX_Warehouse_WarehouseName] ON [RII_WAREHOUSE] ([WarehouseName]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260721194229_AddErpMirrorTables'
)
BEGIN
    CREATE INDEX [IX_RII_YAP_CODE_IsDeleted] ON [RII_YAP_CODE] ([IsDeleted]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260721194229_AddErpMirrorTables'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_YapCode_BranchCode_ConfigurationCode] ON [RII_YAP_CODE] ([BranchCode], [ConfigurationCode]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260721194229_AddErpMirrorTables'
)
BEGIN
    CREATE INDEX [IX_YapCode_Description] ON [RII_YAP_CODE] ([Description]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260721194229_AddErpMirrorTables'
)
BEGIN
    CREATE INDEX [IX_YapCode_StockId] ON [RII_YAP_CODE] ([StockId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260721194229_AddErpMirrorTables'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260721194229_AddErpMirrorTables', N'10.0.10');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260721200308_AddSystemManagementAndProfileSettings'
)
BEGIN
    DECLARE @var nvarchar(max);
    SELECT @var = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[RII_USER_DETAILS]') AND [c].[name] = N'Phone');
    IF @var IS NOT NULL EXEC(N'ALTER TABLE [RII_USER_DETAILS] DROP CONSTRAINT ' + @var + ';');
    ALTER TABLE [RII_USER_DETAILS] ALTER COLUMN [Phone] nvarchar(40) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260721200308_AddSystemManagementAndProfileSettings'
)
BEGIN
    ALTER TABLE [RII_USER_DETAILS] ADD [CreatedDate] datetime2 NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260721200308_AddSystemManagementAndProfileSettings'
)
BEGIN
    ALTER TABLE [RII_USER_DETAILS] ADD [Description] nvarchar(2000) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260721200308_AddSystemManagementAndProfileSettings'
)
BEGIN
    ALTER TABLE [RII_USER_DETAILS] ADD [Gender] int NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260721200308_AddSystemManagementAndProfileSettings'
)
BEGIN
    ALTER TABLE [RII_USER_DETAILS] ADD [Height] decimal(6,2) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260721200308_AddSystemManagementAndProfileSettings'
)
BEGIN
    ALTER TABLE [RII_USER_DETAILS] ADD [ProfilePictureUrl] nvarchar(500) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260721200308_AddSystemManagementAndProfileSettings'
)
BEGIN
    ALTER TABLE [RII_USER_DETAILS] ADD [UpdatedDate] datetime2 NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260721200308_AddSystemManagementAndProfileSettings'
)
BEGIN
    ALTER TABLE [RII_USER_DETAILS] ADD [Weight] decimal(6,2) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260721200308_AddSystemManagementAndProfileSettings'
)
BEGIN
    CREATE TABLE [RII_PERMISSION_DEFINITIONS] (
        [Id] bigint NOT NULL IDENTITY,
        [Code] nvarchar(150) NOT NULL,
        [Name] nvarchar(200) NOT NULL,
        [Description] nvarchar(500) NULL,
        [IsActive] bit NOT NULL,
        [AvailableOnWeb] bit NOT NULL,
        [AvailableOnMobile] bit NOT NULL,
        [BranchCode] nvarchar(10) NOT NULL DEFAULT N'0',
        [CreatedDate] datetime2 NULL,
        [UpdatedDate] datetime2 NULL,
        [DeletedDate] datetime2 NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [CreatedBy] bigint NULL,
        [UpdatedBy] bigint NULL,
        [DeletedBy] bigint NULL,
        CONSTRAINT [PK_RII_PERMISSION_DEFINITIONS] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260721200308_AddSystemManagementAndProfileSettings'
)
BEGIN
    CREATE TABLE [RII_PERMISSION_GROUPS] (
        [Id] bigint NOT NULL IDENTITY,
        [Name] nvarchar(150) NOT NULL,
        [Description] nvarchar(500) NULL,
        [IsSystemAdmin] bit NOT NULL,
        [IsActive] bit NOT NULL,
        [BranchCode] nvarchar(10) NOT NULL DEFAULT N'0',
        [CreatedDate] datetime2 NULL,
        [UpdatedDate] datetime2 NULL,
        [DeletedDate] datetime2 NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [CreatedBy] bigint NULL,
        [UpdatedBy] bigint NULL,
        [DeletedBy] bigint NULL,
        CONSTRAINT [PK_RII_PERMISSION_GROUPS] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260721200308_AddSystemManagementAndProfileSettings'
)
BEGIN
    CREATE TABLE [RII_SMTP_SETTING] (
        [Id] bigint NOT NULL IDENTITY,
        [Host] nvarchar(200) NOT NULL,
        [Port] int NOT NULL,
        [EnableSsl] bit NOT NULL,
        [Username] nvarchar(200) NOT NULL,
        [PasswordEncrypted] nvarchar(2000) NOT NULL,
        [FromEmail] nvarchar(200) NOT NULL,
        [FromName] nvarchar(200) NOT NULL,
        [Timeout] int NOT NULL,
        [BranchCode] nvarchar(10) NOT NULL DEFAULT N'0',
        [CreatedDate] datetime2 NULL,
        [UpdatedDate] datetime2 NULL,
        [DeletedDate] datetime2 NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [CreatedBy] bigint NULL,
        [UpdatedBy] bigint NULL,
        [DeletedBy] bigint NULL,
        CONSTRAINT [PK_RII_SMTP_SETTING] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260721200308_AddSystemManagementAndProfileSettings'
)
BEGIN
    CREATE TABLE [RII_PERMISSION_GROUP_PERMISSIONS] (
        [Id] bigint NOT NULL IDENTITY,
        [PermissionGroupId] bigint NOT NULL,
        [PermissionDefinitionId] bigint NOT NULL,
        [BranchCode] nvarchar(10) NOT NULL DEFAULT N'0',
        [CreatedDate] datetime2 NULL,
        [UpdatedDate] datetime2 NULL,
        [DeletedDate] datetime2 NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [CreatedBy] bigint NULL,
        [UpdatedBy] bigint NULL,
        [DeletedBy] bigint NULL,
        CONSTRAINT [PK_RII_PERMISSION_GROUP_PERMISSIONS] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_RII_PERMISSION_GROUP_PERMISSIONS_RII_PERMISSION_DEFINITIONS_PermissionDefinitionId] FOREIGN KEY ([PermissionDefinitionId]) REFERENCES [RII_PERMISSION_DEFINITIONS] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_RII_PERMISSION_GROUP_PERMISSIONS_RII_PERMISSION_GROUPS_PermissionGroupId] FOREIGN KEY ([PermissionGroupId]) REFERENCES [RII_PERMISSION_GROUPS] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260721200308_AddSystemManagementAndProfileSettings'
)
BEGIN
    CREATE TABLE [RII_USER_PERMISSION_GROUPS] (
        [Id] bigint NOT NULL IDENTITY,
        [UserId] bigint NOT NULL,
        [PermissionGroupId] bigint NOT NULL,
        [BranchCode] nvarchar(10) NOT NULL DEFAULT N'0',
        [CreatedDate] datetime2 NULL,
        [UpdatedDate] datetime2 NULL,
        [DeletedDate] datetime2 NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [CreatedBy] bigint NULL,
        [UpdatedBy] bigint NULL,
        [DeletedBy] bigint NULL,
        CONSTRAINT [PK_RII_USER_PERMISSION_GROUPS] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_RII_USER_PERMISSION_GROUPS_RII_PERMISSION_GROUPS_PermissionGroupId] FOREIGN KEY ([PermissionGroupId]) REFERENCES [RII_PERMISSION_GROUPS] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_RII_USER_PERMISSION_GROUPS_RII_USERS_UserId] FOREIGN KEY ([UserId]) REFERENCES [RII_USERS] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260721200308_AddSystemManagementAndProfileSettings'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'AvailableOnMobile', N'AvailableOnWeb', N'BranchCode', N'Code', N'CreatedBy', N'CreatedDate', N'DeletedBy', N'DeletedDate', N'Description', N'IsActive', N'Name', N'UpdatedBy', N'UpdatedDate') AND [object_id] = OBJECT_ID(N'[RII_PERMISSION_DEFINITIONS]'))
        SET IDENTITY_INSERT [RII_PERMISSION_DEFINITIONS] ON;
    EXEC(N'INSERT INTO [RII_PERMISSION_DEFINITIONS] ([Id], [AvailableOnMobile], [AvailableOnWeb], [BranchCode], [Code], [CreatedBy], [CreatedDate], [DeletedBy], [DeletedDate], [Description], [IsActive], [Name], [UpdatedBy], [UpdatedDate])
    VALUES (CAST(1001 AS bigint), CAST(0 AS bit), CAST(1 AS bit), N''0'', N''SYSTEM.USERS.VIEW'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, NULL, CAST(1 AS bit), N''Kullanıcıları Görüntüle'', NULL, NULL),
    (CAST(1002 AS bigint), CAST(0 AS bit), CAST(1 AS bit), N''0'', N''SYSTEM.USERS.MANAGE'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, NULL, CAST(1 AS bit), N''Kullanıcıları Yönet'', NULL, NULL),
    (CAST(1003 AS bigint), CAST(0 AS bit), CAST(1 AS bit), N''0'', N''SYSTEM.PERMISSIONS.VIEW'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, NULL, CAST(1 AS bit), N''İzinleri Görüntüle'', NULL, NULL),
    (CAST(1004 AS bigint), CAST(0 AS bit), CAST(1 AS bit), N''0'', N''SYSTEM.PERMISSIONS.MANAGE'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, NULL, CAST(1 AS bit), N''İzinleri Yönet'', NULL, NULL),
    (CAST(1005 AS bigint), CAST(0 AS bit), CAST(1 AS bit), N''0'', N''SYSTEM.SMTP.MANAGE'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, NULL, CAST(1 AS bit), N''SMTP Ayarlarını Yönet'', NULL, NULL),
    (CAST(1006 AS bigint), CAST(0 AS bit), CAST(1 AS bit), N''0'', N''SYSTEM.HANGFIRE.VIEW'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, NULL, CAST(1 AS bit), N''Hangfire İzle'', NULL, NULL),
    (CAST(1007 AS bigint), CAST(0 AS bit), CAST(1 AS bit), N''0'', N''SYSTEM.HANGFIRE.TRIGGER'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, NULL, CAST(1 AS bit), N''Hangfire Job Tetikle'', NULL, NULL)');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'AvailableOnMobile', N'AvailableOnWeb', N'BranchCode', N'Code', N'CreatedBy', N'CreatedDate', N'DeletedBy', N'DeletedDate', N'Description', N'IsActive', N'Name', N'UpdatedBy', N'UpdatedDate') AND [object_id] = OBJECT_ID(N'[RII_PERMISSION_DEFINITIONS]'))
        SET IDENTITY_INSERT [RII_PERMISSION_DEFINITIONS] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260721200308_AddSystemManagementAndProfileSettings'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'BranchCode', N'CreatedBy', N'CreatedDate', N'DeletedBy', N'DeletedDate', N'Description', N'IsActive', N'IsSystemAdmin', N'Name', N'UpdatedBy', N'UpdatedDate') AND [object_id] = OBJECT_ID(N'[RII_PERMISSION_GROUPS]'))
        SET IDENTITY_INSERT [RII_PERMISSION_GROUPS] ON;
    EXEC(N'INSERT INTO [RII_PERMISSION_GROUPS] ([Id], [BranchCode], [CreatedBy], [CreatedDate], [DeletedBy], [DeletedDate], [Description], [IsActive], [IsSystemAdmin], [Name], [UpdatedBy], [UpdatedDate])
    VALUES (CAST(1001 AS bigint), N''0'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, N''Tam sistem yönetimi'', CAST(1 AS bit), CAST(1 AS bit), N''System Administrators'', NULL, NULL)');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'BranchCode', N'CreatedBy', N'CreatedDate', N'DeletedBy', N'DeletedDate', N'Description', N'IsActive', N'IsSystemAdmin', N'Name', N'UpdatedBy', N'UpdatedDate') AND [object_id] = OBJECT_ID(N'[RII_PERMISSION_GROUPS]'))
        SET IDENTITY_INSERT [RII_PERMISSION_GROUPS] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260721200308_AddSystemManagementAndProfileSettings'
)
BEGIN
    EXEC(N'UPDATE [RII_USER_DETAILS] SET [CreatedDate] = NULL, [Description] = NULL, [Gender] = NULL, [Height] = NULL, [ProfilePictureUrl] = NULL, [UpdatedDate] = NULL, [Weight] = NULL
    WHERE [UserId] = CAST(1 AS bigint);
    SELECT @@ROWCOUNT');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260721200308_AddSystemManagementAndProfileSettings'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'BranchCode', N'CreatedBy', N'CreatedDate', N'DeletedBy', N'DeletedDate', N'PermissionDefinitionId', N'PermissionGroupId', N'UpdatedBy', N'UpdatedDate') AND [object_id] = OBJECT_ID(N'[RII_PERMISSION_GROUP_PERMISSIONS]'))
        SET IDENTITY_INSERT [RII_PERMISSION_GROUP_PERMISSIONS] ON;
    EXEC(N'INSERT INTO [RII_PERMISSION_GROUP_PERMISSIONS] ([Id], [BranchCode], [CreatedBy], [CreatedDate], [DeletedBy], [DeletedDate], [PermissionDefinitionId], [PermissionGroupId], [UpdatedBy], [UpdatedDate])
    VALUES (CAST(1001 AS bigint), N''0'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, CAST(1001 AS bigint), CAST(1001 AS bigint), NULL, NULL),
    (CAST(1002 AS bigint), N''0'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, CAST(1002 AS bigint), CAST(1001 AS bigint), NULL, NULL),
    (CAST(1003 AS bigint), N''0'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, CAST(1003 AS bigint), CAST(1001 AS bigint), NULL, NULL),
    (CAST(1004 AS bigint), N''0'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, CAST(1004 AS bigint), CAST(1001 AS bigint), NULL, NULL),
    (CAST(1005 AS bigint), N''0'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, CAST(1005 AS bigint), CAST(1001 AS bigint), NULL, NULL),
    (CAST(1006 AS bigint), N''0'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, CAST(1006 AS bigint), CAST(1001 AS bigint), NULL, NULL),
    (CAST(1007 AS bigint), N''0'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, CAST(1007 AS bigint), CAST(1001 AS bigint), NULL, NULL)');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'BranchCode', N'CreatedBy', N'CreatedDate', N'DeletedBy', N'DeletedDate', N'PermissionDefinitionId', N'PermissionGroupId', N'UpdatedBy', N'UpdatedDate') AND [object_id] = OBJECT_ID(N'[RII_PERMISSION_GROUP_PERMISSIONS]'))
        SET IDENTITY_INSERT [RII_PERMISSION_GROUP_PERMISSIONS] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260721200308_AddSystemManagementAndProfileSettings'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'BranchCode', N'CreatedBy', N'CreatedDate', N'DeletedBy', N'DeletedDate', N'PermissionGroupId', N'UpdatedBy', N'UpdatedDate', N'UserId') AND [object_id] = OBJECT_ID(N'[RII_USER_PERMISSION_GROUPS]'))
        SET IDENTITY_INSERT [RII_USER_PERMISSION_GROUPS] ON;
    EXEC(N'INSERT INTO [RII_USER_PERMISSION_GROUPS] ([Id], [BranchCode], [CreatedBy], [CreatedDate], [DeletedBy], [DeletedDate], [PermissionGroupId], [UpdatedBy], [UpdatedDate], [UserId])
    VALUES (CAST(1001 AS bigint), N''0'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, CAST(1001 AS bigint), NULL, NULL, CAST(1 AS bigint))');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'BranchCode', N'CreatedBy', N'CreatedDate', N'DeletedBy', N'DeletedDate', N'PermissionGroupId', N'UpdatedBy', N'UpdatedDate', N'UserId') AND [object_id] = OBJECT_ID(N'[RII_USER_PERMISSION_GROUPS]'))
        SET IDENTITY_INSERT [RII_USER_PERMISSION_GROUPS] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260721200308_AddSystemManagementAndProfileSettings'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_RII_PERMISSION_DEFINITIONS_Code] ON [RII_PERMISSION_DEFINITIONS] ([Code]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260721200308_AddSystemManagementAndProfileSettings'
)
BEGIN
    CREATE INDEX [IX_RII_PERMISSION_DEFINITIONS_IsDeleted] ON [RII_PERMISSION_DEFINITIONS] ([IsDeleted]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260721200308_AddSystemManagementAndProfileSettings'
)
BEGIN
    CREATE INDEX [IX_RII_PERMISSION_GROUP_PERMISSIONS_IsDeleted] ON [RII_PERMISSION_GROUP_PERMISSIONS] ([IsDeleted]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260721200308_AddSystemManagementAndProfileSettings'
)
BEGIN
    CREATE INDEX [IX_RII_PERMISSION_GROUP_PERMISSIONS_PermissionDefinitionId] ON [RII_PERMISSION_GROUP_PERMISSIONS] ([PermissionDefinitionId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260721200308_AddSystemManagementAndProfileSettings'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_RII_PERMISSION_GROUP_PERMISSIONS_PermissionGroupId_PermissionDefinitionId] ON [RII_PERMISSION_GROUP_PERMISSIONS] ([PermissionGroupId], [PermissionDefinitionId]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260721200308_AddSystemManagementAndProfileSettings'
)
BEGIN
    CREATE INDEX [IX_RII_PERMISSION_GROUPS_IsDeleted] ON [RII_PERMISSION_GROUPS] ([IsDeleted]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260721200308_AddSystemManagementAndProfileSettings'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_RII_PERMISSION_GROUPS_Name] ON [RII_PERMISSION_GROUPS] ([Name]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260721200308_AddSystemManagementAndProfileSettings'
)
BEGIN
    CREATE INDEX [IX_RII_SMTP_SETTING_IsDeleted] ON [RII_SMTP_SETTING] ([IsDeleted]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260721200308_AddSystemManagementAndProfileSettings'
)
BEGIN
    CREATE INDEX [IX_RII_USER_PERMISSION_GROUPS_IsDeleted] ON [RII_USER_PERMISSION_GROUPS] ([IsDeleted]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260721200308_AddSystemManagementAndProfileSettings'
)
BEGIN
    CREATE INDEX [IX_RII_USER_PERMISSION_GROUPS_PermissionGroupId] ON [RII_USER_PERMISSION_GROUPS] ([PermissionGroupId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260721200308_AddSystemManagementAndProfileSettings'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_RII_USER_PERMISSION_GROUPS_UserId_PermissionGroupId] ON [RII_USER_PERMISSION_GROUPS] ([UserId], [PermissionGroupId]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260721200308_AddSystemManagementAndProfileSettings'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260721200308_AddSystemManagementAndProfileSettings', N'10.0.10');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260721222616_AddHangfireExecutionLogs'
)
BEGIN
    CREATE TABLE [RII_HANGFIRE_EXECUTION_LOGS] (
        [Id] bigint NOT NULL IDENTITY,
        [JobKey] nvarchar(150) NOT NULL,
        [HangfireJobId] nvarchar(100) NULL,
        [TriggerSource] nvarchar(30) NOT NULL,
        [Status] nvarchar(30) NOT NULL,
        [StartedAt] datetime2 NOT NULL,
        [CompletedAt] datetime2 NULL,
        [DurationMs] bigint NULL,
        [SourceCount] int NULL,
        [InsertedCount] int NULL,
        [UpdatedCount] int NULL,
        [DeactivatedCount] int NULL,
        [ResultSummary] nvarchar(2000) NULL,
        [ErrorType] nvarchar(500) NULL,
        [ErrorMessage] nvarchar(4000) NULL,
        [StackTrace] nvarchar(max) NULL,
        [BranchCode] nvarchar(10) NOT NULL DEFAULT N'0',
        [CreatedDate] datetime2 NULL,
        [UpdatedDate] datetime2 NULL,
        [DeletedDate] datetime2 NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [CreatedBy] bigint NULL,
        [UpdatedBy] bigint NULL,
        [DeletedBy] bigint NULL,
        CONSTRAINT [PK_RII_HANGFIRE_EXECUTION_LOGS] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260721222616_AddHangfireExecutionLogs'
)
BEGIN
    CREATE INDEX [IX_RII_HANGFIRE_EXECUTION_LOGS_IsDeleted] ON [RII_HANGFIRE_EXECUTION_LOGS] ([IsDeleted]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260721222616_AddHangfireExecutionLogs'
)
BEGIN
    CREATE INDEX [IX_RII_HANGFIRE_EXECUTION_LOGS_JobKey_Status] ON [RII_HANGFIRE_EXECUTION_LOGS] ([JobKey], [Status]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260721222616_AddHangfireExecutionLogs'
)
BEGIN
    CREATE INDEX [IX_RII_HANGFIRE_EXECUTION_LOGS_StartedAt] ON [RII_HANGFIRE_EXECUTION_LOGS] ([StartedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260721222616_AddHangfireExecutionLogs'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260721222616_AddHangfireExecutionLogs', N'10.0.10');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260721230247_AddSeniorUserAccessAudit'
)
BEGIN
    CREATE TABLE [RII_AUDIT_LOGS] (
        [Id] bigint NOT NULL IDENTITY,
        [TraceId] nvarchar(64) NOT NULL,
        [ActionType] nvarchar(100) NOT NULL,
        [EntityType] nvarchar(128) NOT NULL,
        [EntityId] nvarchar(128) NOT NULL,
        [Result] nvarchar(32) NOT NULL,
        [Source] nvarchar(64) NOT NULL,
        [Reason] nvarchar(512) NULL,
        [FailureReason] nvarchar(2000) NULL,
        [RequestPath] nvarchar(256) NULL,
        [RequestMethod] nvarchar(16) NULL,
        [PerformedByUserId] bigint NULL,
        [PerformedByUserEmail] nvarchar(256) NULL,
        [OldValuesJson] nvarchar(max) NULL,
        [NewValuesJson] nvarchar(max) NULL,
        [ChangedFieldsJson] nvarchar(max) NULL,
        [BranchCode] nvarchar(10) NOT NULL DEFAULT N'0',
        [CreatedDate] datetime2 NULL,
        [UpdatedDate] datetime2 NULL,
        [DeletedDate] datetime2 NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [CreatedBy] bigint NULL,
        [UpdatedBy] bigint NULL,
        [DeletedBy] bigint NULL,
        CONSTRAINT [PK_RII_AUDIT_LOGS] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260721230247_AddSeniorUserAccessAudit'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'AvailableOnMobile', N'AvailableOnWeb', N'BranchCode', N'Code', N'CreatedBy', N'CreatedDate', N'DeletedBy', N'DeletedDate', N'Description', N'IsActive', N'Name', N'UpdatedBy', N'UpdatedDate') AND [object_id] = OBJECT_ID(N'[RII_PERMISSION_DEFINITIONS]'))
        SET IDENTITY_INSERT [RII_PERMISSION_DEFINITIONS] ON;
    EXEC(N'INSERT INTO [RII_PERMISSION_DEFINITIONS] ([Id], [AvailableOnMobile], [AvailableOnWeb], [BranchCode], [Code], [CreatedBy], [CreatedDate], [DeletedBy], [DeletedDate], [Description], [IsActive], [Name], [UpdatedBy], [UpdatedDate])
    VALUES (CAST(1008 AS bigint), CAST(0 AS bit), CAST(1 AS bit), N''0'', N''SYSTEM.AUDIT.VIEW'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, NULL, CAST(1 AS bit), N''Audit Kayıtlarını Görüntüle'', NULL, NULL)');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'AvailableOnMobile', N'AvailableOnWeb', N'BranchCode', N'Code', N'CreatedBy', N'CreatedDate', N'DeletedBy', N'DeletedDate', N'Description', N'IsActive', N'Name', N'UpdatedBy', N'UpdatedDate') AND [object_id] = OBJECT_ID(N'[RII_PERMISSION_DEFINITIONS]'))
        SET IDENTITY_INSERT [RII_PERMISSION_DEFINITIONS] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260721230247_AddSeniorUserAccessAudit'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'BranchCode', N'CreatedBy', N'CreatedDate', N'DeletedBy', N'DeletedDate', N'PermissionDefinitionId', N'PermissionGroupId', N'UpdatedBy', N'UpdatedDate') AND [object_id] = OBJECT_ID(N'[RII_PERMISSION_GROUP_PERMISSIONS]'))
        SET IDENTITY_INSERT [RII_PERMISSION_GROUP_PERMISSIONS] ON;
    EXEC(N'INSERT INTO [RII_PERMISSION_GROUP_PERMISSIONS] ([Id], [BranchCode], [CreatedBy], [CreatedDate], [DeletedBy], [DeletedDate], [PermissionDefinitionId], [PermissionGroupId], [UpdatedBy], [UpdatedDate])
    VALUES (CAST(1008 AS bigint), N''0'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, CAST(1008 AS bigint), CAST(1001 AS bigint), NULL, NULL)');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'BranchCode', N'CreatedBy', N'CreatedDate', N'DeletedBy', N'DeletedDate', N'PermissionDefinitionId', N'PermissionGroupId', N'UpdatedBy', N'UpdatedDate') AND [object_id] = OBJECT_ID(N'[RII_PERMISSION_GROUP_PERMISSIONS]'))
        SET IDENTITY_INSERT [RII_PERMISSION_GROUP_PERMISSIONS] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260721230247_AddSeniorUserAccessAudit'
)
BEGIN
    CREATE INDEX [IX_RII_AUDIT_LOGS_Entity_CreatedDate] ON [RII_AUDIT_LOGS] ([EntityType], [EntityId], [CreatedDate]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260721230247_AddSeniorUserAccessAudit'
)
BEGIN
    CREATE INDEX [IX_RII_AUDIT_LOGS_IsDeleted] ON [RII_AUDIT_LOGS] ([IsDeleted]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260721230247_AddSeniorUserAccessAudit'
)
BEGIN
    CREATE INDEX [IX_RII_AUDIT_LOGS_Source_Action_CreatedDate] ON [RII_AUDIT_LOGS] ([Source], [ActionType], [CreatedDate]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260721230247_AddSeniorUserAccessAudit'
)
BEGIN
    CREATE INDEX [IX_RII_AUDIT_LOGS_TraceId] ON [RII_AUDIT_LOGS] ([TraceId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260721230247_AddSeniorUserAccessAudit'
)
BEGIN
    CREATE INDEX [IX_RII_AUDIT_LOGS_User_CreatedDate] ON [RII_AUDIT_LOGS] ([PerformedByUserId], [CreatedDate]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260721230247_AddSeniorUserAccessAudit'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260721230247_AddSeniorUserAccessAudit', N'10.0.10');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722090924_AddWarehouseLocations'
)
BEGIN
    CREATE TABLE [RII_LOCATION] (
        [Id] bigint NOT NULL IDENTITY,
        [WarehouseId] bigint NOT NULL,
        [ParentLocationId] bigint NULL,
        [Code] nvarchar(50) NOT NULL,
        [Name] nvarchar(150) NOT NULL,
        [LocationType] nvarchar(30) NOT NULL DEFAULT N'Cell',
        [BarcodeEntryMode] nvarchar(20) NOT NULL DEFAULT N'Auto',
        [Barcode] nvarchar(100) NULL,
        [ZoneCode] nvarchar(50) NULL,
        [AisleNo] int NULL,
        [RackNo] int NULL,
        [LevelNo] int NULL,
        [BinNo] int NULL,
        [CapacityQuantity] decimal(18,6) NULL,
        [CapacityWeight] decimal(18,6) NULL,
        [CapacityVolume] decimal(18,6) NULL,
        [CapacityUnit] nvarchar(20) NULL,
        [AllowMixedStock] bit NOT NULL DEFAULT CAST(0 AS bit),
        [AllowMixedLot] bit NOT NULL DEFAULT CAST(0 AS bit),
        [AllowMixedStatus] bit NOT NULL DEFAULT CAST(0 AS bit),
        [AllowCycleCount] bit NOT NULL DEFAULT CAST(1 AS bit),
        [IsPickable] bit NOT NULL DEFAULT CAST(1 AS bit),
        [IsPutaway] bit NOT NULL DEFAULT CAST(1 AS bit),
        [IsQuarantine] bit NOT NULL DEFAULT CAST(0 AS bit),
        [IsActive] bit NOT NULL DEFAULT CAST(1 AS bit),
        [Description] nvarchar(500) NULL,
        [RowVersion] rowversion NOT NULL,
        [BranchCode] nvarchar(10) NOT NULL DEFAULT N'0',
        [CreatedDate] datetime2 NULL,
        [UpdatedDate] datetime2 NULL,
        [DeletedDate] datetime2 NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [CreatedBy] bigint NULL,
        [UpdatedBy] bigint NULL,
        [DeletedBy] bigint NULL,
        CONSTRAINT [PK_RII_LOCATION] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_RII_LOCATION_CAPACITY_QUANTITY] CHECK ([CapacityQuantity] IS NULL OR [CapacityQuantity] >= 0),
        CONSTRAINT [CK_RII_LOCATION_CAPACITY_VOLUME] CHECK ([CapacityVolume] IS NULL OR [CapacityVolume] >= 0),
        CONSTRAINT [CK_RII_LOCATION_CAPACITY_WEIGHT] CHECK ([CapacityWeight] IS NULL OR [CapacityWeight] >= 0),
        CONSTRAINT [FK_RII_LOCATION_RII_LOCATION_ParentLocationId] FOREIGN KEY ([ParentLocationId]) REFERENCES [RII_LOCATION] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_RII_LOCATION_RII_WAREHOUSE_WarehouseId] FOREIGN KEY ([WarehouseId]) REFERENCES [RII_WAREHOUSE] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722090924_AddWarehouseLocations'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'AvailableOnMobile', N'AvailableOnWeb', N'BranchCode', N'Code', N'CreatedBy', N'CreatedDate', N'DeletedBy', N'DeletedDate', N'Description', N'IsActive', N'Name', N'UpdatedBy', N'UpdatedDate') AND [object_id] = OBJECT_ID(N'[RII_PERMISSION_DEFINITIONS]'))
        SET IDENTITY_INSERT [RII_PERMISSION_DEFINITIONS] ON;
    EXEC(N'INSERT INTO [RII_PERMISSION_DEFINITIONS] ([Id], [AvailableOnMobile], [AvailableOnWeb], [BranchCode], [Code], [CreatedBy], [CreatedDate], [DeletedBy], [DeletedDate], [Description], [IsActive], [Name], [UpdatedBy], [UpdatedDate])
    VALUES (CAST(1009 AS bigint), CAST(0 AS bit), CAST(1 AS bit), N''0'', N''WMS.LOCATIONS.VIEW'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, NULL, CAST(1 AS bit), N''Raf Tanımlarını Görüntüle'', NULL, NULL),
    (CAST(1010 AS bigint), CAST(0 AS bit), CAST(1 AS bit), N''0'', N''WMS.LOCATIONS.CREATE'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, NULL, CAST(1 AS bit), N''Raf Tanımı Oluştur'', NULL, NULL),
    (CAST(1011 AS bigint), CAST(0 AS bit), CAST(1 AS bit), N''0'', N''WMS.LOCATIONS.UPDATE'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, NULL, CAST(1 AS bit), N''Raf Tanımını Güncelle'', NULL, NULL),
    (CAST(1012 AS bigint), CAST(0 AS bit), CAST(1 AS bit), N''0'', N''WMS.LOCATIONS.DELETE'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, NULL, CAST(1 AS bit), N''Raf Tanımını Sil'', NULL, NULL)');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'AvailableOnMobile', N'AvailableOnWeb', N'BranchCode', N'Code', N'CreatedBy', N'CreatedDate', N'DeletedBy', N'DeletedDate', N'Description', N'IsActive', N'Name', N'UpdatedBy', N'UpdatedDate') AND [object_id] = OBJECT_ID(N'[RII_PERMISSION_DEFINITIONS]'))
        SET IDENTITY_INSERT [RII_PERMISSION_DEFINITIONS] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722090924_AddWarehouseLocations'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'BranchCode', N'CreatedBy', N'CreatedDate', N'DeletedBy', N'DeletedDate', N'PermissionDefinitionId', N'PermissionGroupId', N'UpdatedBy', N'UpdatedDate') AND [object_id] = OBJECT_ID(N'[RII_PERMISSION_GROUP_PERMISSIONS]'))
        SET IDENTITY_INSERT [RII_PERMISSION_GROUP_PERMISSIONS] ON;
    EXEC(N'INSERT INTO [RII_PERMISSION_GROUP_PERMISSIONS] ([Id], [BranchCode], [CreatedBy], [CreatedDate], [DeletedBy], [DeletedDate], [PermissionDefinitionId], [PermissionGroupId], [UpdatedBy], [UpdatedDate])
    VALUES (CAST(1009 AS bigint), N''0'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, CAST(1009 AS bigint), CAST(1001 AS bigint), NULL, NULL),
    (CAST(1010 AS bigint), N''0'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, CAST(1010 AS bigint), CAST(1001 AS bigint), NULL, NULL),
    (CAST(1011 AS bigint), N''0'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, CAST(1011 AS bigint), CAST(1001 AS bigint), NULL, NULL),
    (CAST(1012 AS bigint), N''0'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, CAST(1012 AS bigint), CAST(1001 AS bigint), NULL, NULL)');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'BranchCode', N'CreatedBy', N'CreatedDate', N'DeletedBy', N'DeletedDate', N'PermissionDefinitionId', N'PermissionGroupId', N'UpdatedBy', N'UpdatedDate') AND [object_id] = OBJECT_ID(N'[RII_PERMISSION_GROUP_PERMISSIONS]'))
        SET IDENTITY_INSERT [RII_PERMISSION_GROUP_PERMISSIONS] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722090924_AddWarehouseLocations'
)
BEGIN
    CREATE INDEX [IX_RII_LOCATION_IsDeleted] ON [RII_LOCATION] ([IsDeleted]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722090924_AddWarehouseLocations'
)
BEGIN
    CREATE INDEX [IX_RII_LOCATION_ParentLocationId] ON [RII_LOCATION] ([ParentLocationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722090924_AddWarehouseLocations'
)
BEGIN
    CREATE INDEX [IX_RII_LOCATION_WAREHOUSE_PARENT] ON [RII_LOCATION] ([WarehouseId], [ParentLocationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722090924_AddWarehouseLocations'
)
BEGIN
    CREATE INDEX [IX_RII_LOCATION_WAREHOUSE_TYPE_ACTIVE] ON [RII_LOCATION] ([WarehouseId], [LocationType], [IsActive]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722090924_AddWarehouseLocations'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_RII_LOCATION_BARCODE] ON [RII_LOCATION] ([Barcode]) WHERE [Barcode] IS NOT NULL AND [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722090924_AddWarehouseLocations'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_RII_LOCATION_BRANCH_WAREHOUSE_CODE] ON [RII_LOCATION] ([BranchCode], [WarehouseId], [Code]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722090924_AddWarehouseLocations'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260722090924_AddWarehouseLocations', N'10.0.10');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722095912_AddImmutableStockMovementLedger'
)
BEGIN
    CREATE TABLE [RII_STOCK_MOVEMENT_OPERATION] (
        [Id] bigint NOT NULL IDENTITY,
        [OperationCode] uniqueidentifier NOT NULL,
        [IdempotencyKey] nvarchar(100) NOT NULL,
        [RequestHash] nvarchar(64) NOT NULL,
        [OperationType] nvarchar(40) NOT NULL,
        [Status] nvarchar(20) NOT NULL,
        [ReferenceType] nvarchar(50) NULL,
        [ReferenceNo] nvarchar(100) NULL,
        [ReferenceId] bigint NULL,
        [OccurredAt] datetime2 NOT NULL,
        [Reason] nvarchar(500) NULL,
        [Description] nvarchar(1000) NULL,
        [ReversalOfOperationId] bigint NULL,
        [BranchCode] nvarchar(10) NOT NULL DEFAULT N'0',
        [CreatedDate] datetime2 NULL,
        [UpdatedDate] datetime2 NULL,
        [DeletedDate] datetime2 NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [CreatedBy] bigint NULL,
        [UpdatedBy] bigint NULL,
        [DeletedBy] bigint NULL,
        CONSTRAINT [PK_RII_STOCK_MOVEMENT_OPERATION] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_RII_STOCK_MOVEMENT_OPERATION_RII_STOCK_MOVEMENT_OPERATION_ReversalOfOperationId] FOREIGN KEY ([ReversalOfOperationId]) REFERENCES [RII_STOCK_MOVEMENT_OPERATION] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722095912_AddImmutableStockMovementLedger'
)
BEGIN
    CREATE TABLE [RII_STOCK_MOVEMENT] (
        [Id] bigint NOT NULL IDENTITY,
        [OperationId] bigint NOT NULL,
        [LineNo] int NOT NULL,
        [StockId] bigint NOT NULL,
        [WarehouseId] bigint NOT NULL,
        [LocationId] bigint NOT NULL,
        [QuantityDelta] decimal(18,6) NOT NULL,
        [UnitCode] nvarchar(20) NOT NULL,
        [LotNo] nvarchar(100) NULL,
        [SerialNo] nvarchar(100) NULL,
        [StockStatus] nvarchar(30) NOT NULL,
        [OccurredAt] datetime2 NOT NULL,
        [BranchCode] nvarchar(10) NOT NULL DEFAULT N'0',
        [CreatedDate] datetime2 NULL,
        [UpdatedDate] datetime2 NULL,
        [DeletedDate] datetime2 NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [CreatedBy] bigint NULL,
        [UpdatedBy] bigint NULL,
        [DeletedBy] bigint NULL,
        CONSTRAINT [PK_RII_STOCK_MOVEMENT] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_RII_STOCK_MOVEMENT_RII_LOCATION_LocationId] FOREIGN KEY ([LocationId]) REFERENCES [RII_LOCATION] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_RII_STOCK_MOVEMENT_RII_STOCK_MOVEMENT_OPERATION_OperationId] FOREIGN KEY ([OperationId]) REFERENCES [RII_STOCK_MOVEMENT_OPERATION] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_RII_STOCK_MOVEMENT_RII_STOCK_StockId] FOREIGN KEY ([StockId]) REFERENCES [RII_STOCK] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_RII_STOCK_MOVEMENT_RII_WAREHOUSE_WarehouseId] FOREIGN KEY ([WarehouseId]) REFERENCES [RII_WAREHOUSE] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722095912_AddImmutableStockMovementLedger'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'AvailableOnMobile', N'AvailableOnWeb', N'BranchCode', N'Code', N'CreatedBy', N'CreatedDate', N'DeletedBy', N'DeletedDate', N'Description', N'IsActive', N'Name', N'UpdatedBy', N'UpdatedDate') AND [object_id] = OBJECT_ID(N'[RII_PERMISSION_DEFINITIONS]'))
        SET IDENTITY_INSERT [RII_PERMISSION_DEFINITIONS] ON;
    EXEC(N'INSERT INTO [RII_PERMISSION_DEFINITIONS] ([Id], [AvailableOnMobile], [AvailableOnWeb], [BranchCode], [Code], [CreatedBy], [CreatedDate], [DeletedBy], [DeletedDate], [Description], [IsActive], [Name], [UpdatedBy], [UpdatedDate])
    VALUES (CAST(1013 AS bigint), CAST(0 AS bit), CAST(1 AS bit), N''0'', N''WMS.STOCK_MOVEMENTS.VIEW'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, NULL, CAST(1 AS bit), N''Stok Hareketlerini Görüntüle'', NULL, NULL),
    (CAST(1014 AS bigint), CAST(0 AS bit), CAST(1 AS bit), N''0'', N''WMS.STOCK_MOVEMENTS.POST'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, NULL, CAST(1 AS bit), N''Stok Hareketi Kaydet'', NULL, NULL),
    (CAST(1015 AS bigint), CAST(0 AS bit), CAST(1 AS bit), N''0'', N''WMS.STOCK_MOVEMENTS.REVERSE'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, NULL, CAST(1 AS bit), N''Stok Hareketini Ters Çevir'', NULL, NULL)');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'AvailableOnMobile', N'AvailableOnWeb', N'BranchCode', N'Code', N'CreatedBy', N'CreatedDate', N'DeletedBy', N'DeletedDate', N'Description', N'IsActive', N'Name', N'UpdatedBy', N'UpdatedDate') AND [object_id] = OBJECT_ID(N'[RII_PERMISSION_DEFINITIONS]'))
        SET IDENTITY_INSERT [RII_PERMISSION_DEFINITIONS] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722095912_AddImmutableStockMovementLedger'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'BranchCode', N'CreatedBy', N'CreatedDate', N'DeletedBy', N'DeletedDate', N'PermissionDefinitionId', N'PermissionGroupId', N'UpdatedBy', N'UpdatedDate') AND [object_id] = OBJECT_ID(N'[RII_PERMISSION_GROUP_PERMISSIONS]'))
        SET IDENTITY_INSERT [RII_PERMISSION_GROUP_PERMISSIONS] ON;
    EXEC(N'INSERT INTO [RII_PERMISSION_GROUP_PERMISSIONS] ([Id], [BranchCode], [CreatedBy], [CreatedDate], [DeletedBy], [DeletedDate], [PermissionDefinitionId], [PermissionGroupId], [UpdatedBy], [UpdatedDate])
    VALUES (CAST(1013 AS bigint), N''0'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, CAST(1013 AS bigint), CAST(1001 AS bigint), NULL, NULL),
    (CAST(1014 AS bigint), N''0'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, CAST(1014 AS bigint), CAST(1001 AS bigint), NULL, NULL),
    (CAST(1015 AS bigint), N''0'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, CAST(1015 AS bigint), CAST(1001 AS bigint), NULL, NULL)');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'BranchCode', N'CreatedBy', N'CreatedDate', N'DeletedBy', N'DeletedDate', N'PermissionDefinitionId', N'PermissionGroupId', N'UpdatedBy', N'UpdatedDate') AND [object_id] = OBJECT_ID(N'[RII_PERMISSION_GROUP_PERMISSIONS]'))
        SET IDENTITY_INSERT [RII_PERMISSION_GROUP_PERMISSIONS] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722095912_AddImmutableStockMovementLedger'
)
BEGIN
    CREATE INDEX [IX_RII_STOCK_MOVEMENT_BALANCE_STREAM] ON [RII_STOCK_MOVEMENT] ([StockId], [WarehouseId], [LocationId], [UnitCode], [OccurredAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722095912_AddImmutableStockMovementLedger'
)
BEGIN
    CREATE INDEX [IX_RII_STOCK_MOVEMENT_IsDeleted] ON [RII_STOCK_MOVEMENT] ([IsDeleted]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722095912_AddImmutableStockMovementLedger'
)
BEGIN
    CREATE INDEX [IX_RII_STOCK_MOVEMENT_LOCATION_TIME] ON [RII_STOCK_MOVEMENT] ([WarehouseId], [LocationId], [OccurredAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722095912_AddImmutableStockMovementLedger'
)
BEGIN
    CREATE INDEX [IX_RII_STOCK_MOVEMENT_LocationId] ON [RII_STOCK_MOVEMENT] ([LocationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722095912_AddImmutableStockMovementLedger'
)
BEGIN
    CREATE INDEX [IX_RII_STOCK_MOVEMENT_TRACE] ON [RII_STOCK_MOVEMENT] ([StockId], [LotNo], [SerialNo]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722095912_AddImmutableStockMovementLedger'
)
BEGIN
    CREATE UNIQUE INDEX [UX_RII_STOCK_MOVEMENT_OPERATION_LINE] ON [RII_STOCK_MOVEMENT] ([OperationId], [LineNo]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722095912_AddImmutableStockMovementLedger'
)
BEGIN
    CREATE INDEX [IX_RII_STOCK_MOVEMENT_OPERATION_IsDeleted] ON [RII_STOCK_MOVEMENT_OPERATION] ([IsDeleted]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722095912_AddImmutableStockMovementLedger'
)
BEGIN
    CREATE INDEX [IX_RII_STOCK_MOVEMENT_OPERATION_OCCURRED_TYPE] ON [RII_STOCK_MOVEMENT_OPERATION] ([OccurredAt], [OperationType]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722095912_AddImmutableStockMovementLedger'
)
BEGIN
    CREATE UNIQUE INDEX [UX_RII_STOCK_MOVEMENT_OPERATION_CODE] ON [RII_STOCK_MOVEMENT_OPERATION] ([OperationCode]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722095912_AddImmutableStockMovementLedger'
)
BEGIN
    CREATE UNIQUE INDEX [UX_RII_STOCK_MOVEMENT_OPERATION_IDEMPOTENCY] ON [RII_STOCK_MOVEMENT_OPERATION] ([IdempotencyKey]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722095912_AddImmutableStockMovementLedger'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_RII_STOCK_MOVEMENT_OPERATION_REVERSAL] ON [RII_STOCK_MOVEMENT_OPERATION] ([ReversalOfOperationId]) WHERE [ReversalOfOperationId] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722095912_AddImmutableStockMovementLedger'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260722095912_AddImmutableStockMovementLedger', N'10.0.10');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722102851_AddStockBalanceProjectionsAndYapDimension'
)
BEGIN
    DROP INDEX [IX_RII_STOCK_MOVEMENT_BALANCE_STREAM] ON [RII_STOCK_MOVEMENT];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722102851_AddStockBalanceProjectionsAndYapDimension'
)
BEGIN
    ALTER TABLE [RII_STOCK_MOVEMENT] ADD [YapCodeId] bigint NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722102851_AddStockBalanceProjectionsAndYapDimension'
)
BEGIN
    CREATE TABLE [RII_LOCATION_STOCK_BALANCE] (
        [Id] bigint NOT NULL IDENTITY,
        [DimensionKey] varchar(64) NOT NULL,
        [WarehouseId] bigint NOT NULL,
        [LocationId] bigint NOT NULL,
        [StockId] bigint NOT NULL,
        [YapCodeId] bigint NULL,
        [UnitCode] nvarchar(20) NOT NULL,
        [LotNo] nvarchar(100) NOT NULL,
        [SerialNo] nvarchar(100) NOT NULL,
        [StockStatus] nvarchar(30) NOT NULL,
        [Quantity] decimal(18,6) NOT NULL,
        [ReservedQuantity] decimal(18,6) NOT NULL,
        [AvailableQuantity] decimal(18,6) NOT NULL,
        [LastMovementEntryId] bigint NOT NULL,
        [LastTransactionDate] datetime2 NOT NULL,
        [LastReconciledAt] datetime2 NULL,
        [BranchCode] nvarchar(10) NOT NULL DEFAULT N'0',
        [CreatedDate] datetime2 NULL,
        [UpdatedDate] datetime2 NULL,
        [DeletedDate] datetime2 NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [CreatedBy] bigint NULL,
        [UpdatedBy] bigint NULL,
        [DeletedBy] bigint NULL,
        CONSTRAINT [PK_RII_LOCATION_STOCK_BALANCE] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_RII_LOCATION_STOCK_BALANCE_RII_LOCATION_LocationId] FOREIGN KEY ([LocationId]) REFERENCES [RII_LOCATION] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_RII_LOCATION_STOCK_BALANCE_RII_STOCK_StockId] FOREIGN KEY ([StockId]) REFERENCES [RII_STOCK] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_RII_LOCATION_STOCK_BALANCE_RII_WAREHOUSE_WarehouseId] FOREIGN KEY ([WarehouseId]) REFERENCES [RII_WAREHOUSE] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_RII_LOCATION_STOCK_BALANCE_RII_YAP_CODE_YapCodeId] FOREIGN KEY ([YapCodeId]) REFERENCES [RII_YAP_CODE] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722102851_AddStockBalanceProjectionsAndYapDimension'
)
BEGIN
    CREATE TABLE [RII_STOCK_BALANCE_PROJECTION_STATE] (
        [Id] bigint NOT NULL IDENTITY,
        [ProjectionName] nvarchar(100) NOT NULL,
        [LastMovementEntryId] bigint NOT NULL,
        [LastProjectedAt] datetime2 NULL,
        [LastReconciledAt] datetime2 NULL,
        [LastMismatchCount] int NOT NULL,
        [BranchCode] nvarchar(10) NOT NULL DEFAULT N'0',
        [CreatedDate] datetime2 NULL,
        [UpdatedDate] datetime2 NULL,
        [DeletedDate] datetime2 NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [CreatedBy] bigint NULL,
        [UpdatedBy] bigint NULL,
        [DeletedBy] bigint NULL,
        CONSTRAINT [PK_RII_STOCK_BALANCE_PROJECTION_STATE] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722102851_AddStockBalanceProjectionsAndYapDimension'
)
BEGIN
    CREATE TABLE [RII_WAREHOUSE_STOCK_BALANCE] (
        [Id] bigint NOT NULL IDENTITY,
        [DimensionKey] varchar(64) NOT NULL,
        [WarehouseId] bigint NOT NULL,
        [StockId] bigint NOT NULL,
        [YapCodeId] bigint NULL,
        [UnitCode] nvarchar(20) NOT NULL,
        [StockStatus] nvarchar(30) NOT NULL,
        [Quantity] decimal(18,6) NOT NULL,
        [ReservedQuantity] decimal(18,6) NOT NULL,
        [AvailableQuantity] decimal(18,6) NOT NULL,
        [DistinctLocationCount] int NOT NULL,
        [DistinctLotCount] int NOT NULL,
        [DistinctSerialCount] int NOT NULL,
        [LastMovementEntryId] bigint NOT NULL,
        [LastTransactionDate] datetime2 NOT NULL,
        [LastReconciledAt] datetime2 NULL,
        [BranchCode] nvarchar(10) NOT NULL DEFAULT N'0',
        [CreatedDate] datetime2 NULL,
        [UpdatedDate] datetime2 NULL,
        [DeletedDate] datetime2 NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [CreatedBy] bigint NULL,
        [UpdatedBy] bigint NULL,
        [DeletedBy] bigint NULL,
        CONSTRAINT [PK_RII_WAREHOUSE_STOCK_BALANCE] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_RII_WAREHOUSE_STOCK_BALANCE_RII_STOCK_StockId] FOREIGN KEY ([StockId]) REFERENCES [RII_STOCK] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_RII_WAREHOUSE_STOCK_BALANCE_RII_WAREHOUSE_WarehouseId] FOREIGN KEY ([WarehouseId]) REFERENCES [RII_WAREHOUSE] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_RII_WAREHOUSE_STOCK_BALANCE_RII_YAP_CODE_YapCodeId] FOREIGN KEY ([YapCodeId]) REFERENCES [RII_YAP_CODE] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722102851_AddStockBalanceProjectionsAndYapDimension'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'AvailableOnMobile', N'AvailableOnWeb', N'BranchCode', N'Code', N'CreatedBy', N'CreatedDate', N'DeletedBy', N'DeletedDate', N'Description', N'IsActive', N'Name', N'UpdatedBy', N'UpdatedDate') AND [object_id] = OBJECT_ID(N'[RII_PERMISSION_DEFINITIONS]'))
        SET IDENTITY_INSERT [RII_PERMISSION_DEFINITIONS] ON;
    EXEC(N'INSERT INTO [RII_PERMISSION_DEFINITIONS] ([Id], [AvailableOnMobile], [AvailableOnWeb], [BranchCode], [Code], [CreatedBy], [CreatedDate], [DeletedBy], [DeletedDate], [Description], [IsActive], [Name], [UpdatedBy], [UpdatedDate])
    VALUES (CAST(1016 AS bigint), CAST(0 AS bit), CAST(1 AS bit), N''0'', N''WMS.STOCK_BALANCES.VIEW'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, NULL, CAST(1 AS bit), N''Stok Bakiyelerini Görüntüle'', NULL, NULL),
    (CAST(1017 AS bigint), CAST(0 AS bit), CAST(1 AS bit), N''0'', N''WMS.STOCK_BALANCES.RECONCILE'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, NULL, CAST(1 AS bit), N''Stok Bakiyelerini Uzlaştır ve Onar'', NULL, NULL)');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'AvailableOnMobile', N'AvailableOnWeb', N'BranchCode', N'Code', N'CreatedBy', N'CreatedDate', N'DeletedBy', N'DeletedDate', N'Description', N'IsActive', N'Name', N'UpdatedBy', N'UpdatedDate') AND [object_id] = OBJECT_ID(N'[RII_PERMISSION_DEFINITIONS]'))
        SET IDENTITY_INSERT [RII_PERMISSION_DEFINITIONS] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722102851_AddStockBalanceProjectionsAndYapDimension'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'BranchCode', N'CreatedBy', N'CreatedDate', N'DeletedBy', N'DeletedDate', N'PermissionDefinitionId', N'PermissionGroupId', N'UpdatedBy', N'UpdatedDate') AND [object_id] = OBJECT_ID(N'[RII_PERMISSION_GROUP_PERMISSIONS]'))
        SET IDENTITY_INSERT [RII_PERMISSION_GROUP_PERMISSIONS] ON;
    EXEC(N'INSERT INTO [RII_PERMISSION_GROUP_PERMISSIONS] ([Id], [BranchCode], [CreatedBy], [CreatedDate], [DeletedBy], [DeletedDate], [PermissionDefinitionId], [PermissionGroupId], [UpdatedBy], [UpdatedDate])
    VALUES (CAST(1016 AS bigint), N''0'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, CAST(1016 AS bigint), CAST(1001 AS bigint), NULL, NULL),
    (CAST(1017 AS bigint), N''0'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, CAST(1017 AS bigint), CAST(1001 AS bigint), NULL, NULL)');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'BranchCode', N'CreatedBy', N'CreatedDate', N'DeletedBy', N'DeletedDate', N'PermissionDefinitionId', N'PermissionGroupId', N'UpdatedBy', N'UpdatedDate') AND [object_id] = OBJECT_ID(N'[RII_PERMISSION_GROUP_PERMISSIONS]'))
        SET IDENTITY_INSERT [RII_PERMISSION_GROUP_PERMISSIONS] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722102851_AddStockBalanceProjectionsAndYapDimension'
)
BEGIN
    EXEC sys.sp_executesql N'CREATE INDEX [IX_RII_STOCK_MOVEMENT_BALANCE_STREAM] ON [RII_STOCK_MOVEMENT] ([StockId], [YapCodeId], [WarehouseId], [LocationId], [UnitCode], [OccurredAt]);';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722102851_AddStockBalanceProjectionsAndYapDimension'
)
BEGIN
    EXEC sys.sp_executesql N'CREATE INDEX [IX_RII_STOCK_MOVEMENT_YapCodeId] ON [RII_STOCK_MOVEMENT] ([YapCodeId]);';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722102851_AddStockBalanceProjectionsAndYapDimension'
)
BEGIN
    CREATE INDEX [IX_RII_LOCATION_STOCK_BALANCE_DIMENSIONS] ON [RII_LOCATION_STOCK_BALANCE] ([WarehouseId], [LocationId], [StockId], [YapCodeId], [UnitCode], [StockStatus]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722102851_AddStockBalanceProjectionsAndYapDimension'
)
BEGIN
    CREATE INDEX [IX_RII_LOCATION_STOCK_BALANCE_IsDeleted] ON [RII_LOCATION_STOCK_BALANCE] ([IsDeleted]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722102851_AddStockBalanceProjectionsAndYapDimension'
)
BEGIN
    CREATE INDEX [IX_RII_LOCATION_STOCK_BALANCE_LocationId] ON [RII_LOCATION_STOCK_BALANCE] ([LocationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722102851_AddStockBalanceProjectionsAndYapDimension'
)
BEGIN
    CREATE INDEX [IX_RII_LOCATION_STOCK_BALANCE_PICKING] ON [RII_LOCATION_STOCK_BALANCE] ([StockId], [WarehouseId], [AvailableQuantity]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722102851_AddStockBalanceProjectionsAndYapDimension'
)
BEGIN
    CREATE INDEX [IX_RII_LOCATION_STOCK_BALANCE_YapCodeId] ON [RII_LOCATION_STOCK_BALANCE] ([YapCodeId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722102851_AddStockBalanceProjectionsAndYapDimension'
)
BEGIN
    CREATE UNIQUE INDEX [UX_RII_LOCATION_STOCK_BALANCE_DIMENSION_KEY] ON [RII_LOCATION_STOCK_BALANCE] ([DimensionKey]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722102851_AddStockBalanceProjectionsAndYapDimension'
)
BEGIN
    CREATE INDEX [IX_RII_STOCK_BALANCE_PROJECTION_STATE_IsDeleted] ON [RII_STOCK_BALANCE_PROJECTION_STATE] ([IsDeleted]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722102851_AddStockBalanceProjectionsAndYapDimension'
)
BEGIN
    CREATE UNIQUE INDEX [UX_RII_STOCK_BALANCE_PROJECTION_STATE_NAME] ON [RII_STOCK_BALANCE_PROJECTION_STATE] ([ProjectionName]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722102851_AddStockBalanceProjectionsAndYapDimension'
)
BEGIN
    CREATE INDEX [IX_RII_WAREHOUSE_STOCK_BALANCE_DIMENSIONS] ON [RII_WAREHOUSE_STOCK_BALANCE] ([WarehouseId], [StockId], [YapCodeId], [UnitCode], [StockStatus]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722102851_AddStockBalanceProjectionsAndYapDimension'
)
BEGIN
    CREATE INDEX [IX_RII_WAREHOUSE_STOCK_BALANCE_IsDeleted] ON [RII_WAREHOUSE_STOCK_BALANCE] ([IsDeleted]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722102851_AddStockBalanceProjectionsAndYapDimension'
)
BEGIN
    CREATE INDEX [IX_RII_WAREHOUSE_STOCK_BALANCE_STOCK] ON [RII_WAREHOUSE_STOCK_BALANCE] ([StockId], [AvailableQuantity]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722102851_AddStockBalanceProjectionsAndYapDimension'
)
BEGIN
    CREATE INDEX [IX_RII_WAREHOUSE_STOCK_BALANCE_YapCodeId] ON [RII_WAREHOUSE_STOCK_BALANCE] ([YapCodeId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722102851_AddStockBalanceProjectionsAndYapDimension'
)
BEGIN
    CREATE UNIQUE INDEX [UX_RII_WAREHOUSE_STOCK_BALANCE_DIMENSION_KEY] ON [RII_WAREHOUSE_STOCK_BALANCE] ([DimensionKey]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722102851_AddStockBalanceProjectionsAndYapDimension'
)
BEGIN
    ALTER TABLE [RII_STOCK_MOVEMENT] ADD CONSTRAINT [FK_RII_STOCK_MOVEMENT_RII_YAP_CODE_YapCodeId] FOREIGN KEY ([YapCodeId]) REFERENCES [RII_YAP_CODE] ([Id]) ON DELETE NO ACTION;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722102851_AddStockBalanceProjectionsAndYapDimension'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260722102851_AddStockBalanceProjectionsAndYapDimension', N'10.0.10');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722111155_AddProjectSettings'
)
BEGIN
    CREATE TABLE [RII_PROJECT_SETTINGS] (
        [Id] bigint NOT NULL IDENTITY,
        [SettingKey] nvarchar(30) NOT NULL,
        [NumberLocale] nvarchar(20) NOT NULL,
        [DecimalPlaces] int NOT NULL,
        [DateFormat] nvarchar(30) NOT NULL,
        [TimeFormat] nvarchar(30) NOT NULL,
        [YearFormat] nvarchar(10) NOT NULL,
        [TimeZoneId] nvarchar(100) NOT NULL,
        [BranchCode] nvarchar(10) NOT NULL DEFAULT N'0',
        [CreatedDate] datetime2 NULL,
        [UpdatedDate] datetime2 NULL,
        [DeletedDate] datetime2 NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [CreatedBy] bigint NULL,
        [UpdatedBy] bigint NULL,
        [DeletedBy] bigint NULL,
        CONSTRAINT [PK_RII_PROJECT_SETTINGS] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722111155_AddProjectSettings'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'AvailableOnMobile', N'AvailableOnWeb', N'BranchCode', N'Code', N'CreatedBy', N'CreatedDate', N'DeletedBy', N'DeletedDate', N'Description', N'IsActive', N'Name', N'UpdatedBy', N'UpdatedDate') AND [object_id] = OBJECT_ID(N'[RII_PERMISSION_DEFINITIONS]'))
        SET IDENTITY_INSERT [RII_PERMISSION_DEFINITIONS] ON;
    EXEC(N'INSERT INTO [RII_PERMISSION_DEFINITIONS] ([Id], [AvailableOnMobile], [AvailableOnWeb], [BranchCode], [Code], [CreatedBy], [CreatedDate], [DeletedBy], [DeletedDate], [Description], [IsActive], [Name], [UpdatedBy], [UpdatedDate])
    VALUES (CAST(1018 AS bigint), CAST(0 AS bit), CAST(1 AS bit), N''0'', N''SYSTEM.PROJECT_SETTINGS.VIEW'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, NULL, CAST(1 AS bit), N''Proje Ayarlarını Görüntüle'', NULL, NULL),
    (CAST(1019 AS bigint), CAST(0 AS bit), CAST(1 AS bit), N''0'', N''SYSTEM.PROJECT_SETTINGS.MANAGE'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, NULL, CAST(1 AS bit), N''Proje Ayarlarını Yönet'', NULL, NULL)');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'AvailableOnMobile', N'AvailableOnWeb', N'BranchCode', N'Code', N'CreatedBy', N'CreatedDate', N'DeletedBy', N'DeletedDate', N'Description', N'IsActive', N'Name', N'UpdatedBy', N'UpdatedDate') AND [object_id] = OBJECT_ID(N'[RII_PERMISSION_DEFINITIONS]'))
        SET IDENTITY_INSERT [RII_PERMISSION_DEFINITIONS] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722111155_AddProjectSettings'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'BranchCode', N'CreatedBy', N'CreatedDate', N'DateFormat', N'DecimalPlaces', N'DeletedBy', N'DeletedDate', N'NumberLocale', N'SettingKey', N'TimeFormat', N'TimeZoneId', N'UpdatedBy', N'UpdatedDate', N'YearFormat') AND [object_id] = OBJECT_ID(N'[RII_PROJECT_SETTINGS]'))
        SET IDENTITY_INSERT [RII_PROJECT_SETTINGS] ON;
    EXEC(N'INSERT INTO [RII_PROJECT_SETTINGS] ([Id], [BranchCode], [CreatedBy], [CreatedDate], [DateFormat], [DecimalPlaces], [DeletedBy], [DeletedDate], [NumberLocale], [SettingKey], [TimeFormat], [TimeZoneId], [UpdatedBy], [UpdatedDate], [YearFormat])
    VALUES (CAST(1 AS bigint), N''0'', NULL, ''2026-07-21T00:00:00.0000000Z'', N''dd.MM.yyyy'', 2, NULL, NULL, N''tr-TR'', N''GLOBAL'', N''HH:mm'', N''Europe/Istanbul'', NULL, NULL, N''yyyy'')');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'BranchCode', N'CreatedBy', N'CreatedDate', N'DateFormat', N'DecimalPlaces', N'DeletedBy', N'DeletedDate', N'NumberLocale', N'SettingKey', N'TimeFormat', N'TimeZoneId', N'UpdatedBy', N'UpdatedDate', N'YearFormat') AND [object_id] = OBJECT_ID(N'[RII_PROJECT_SETTINGS]'))
        SET IDENTITY_INSERT [RII_PROJECT_SETTINGS] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722111155_AddProjectSettings'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'BranchCode', N'CreatedBy', N'CreatedDate', N'DeletedBy', N'DeletedDate', N'PermissionDefinitionId', N'PermissionGroupId', N'UpdatedBy', N'UpdatedDate') AND [object_id] = OBJECT_ID(N'[RII_PERMISSION_GROUP_PERMISSIONS]'))
        SET IDENTITY_INSERT [RII_PERMISSION_GROUP_PERMISSIONS] ON;
    EXEC(N'INSERT INTO [RII_PERMISSION_GROUP_PERMISSIONS] ([Id], [BranchCode], [CreatedBy], [CreatedDate], [DeletedBy], [DeletedDate], [PermissionDefinitionId], [PermissionGroupId], [UpdatedBy], [UpdatedDate])
    VALUES (CAST(1018 AS bigint), N''0'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, CAST(1018 AS bigint), CAST(1001 AS bigint), NULL, NULL),
    (CAST(1019 AS bigint), N''0'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, CAST(1019 AS bigint), CAST(1001 AS bigint), NULL, NULL)');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'BranchCode', N'CreatedBy', N'CreatedDate', N'DeletedBy', N'DeletedDate', N'PermissionDefinitionId', N'PermissionGroupId', N'UpdatedBy', N'UpdatedDate') AND [object_id] = OBJECT_ID(N'[RII_PERMISSION_GROUP_PERMISSIONS]'))
        SET IDENTITY_INSERT [RII_PERMISSION_GROUP_PERMISSIONS] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722111155_AddProjectSettings'
)
BEGIN
    CREATE INDEX [IX_RII_PROJECT_SETTINGS_IsDeleted] ON [RII_PROJECT_SETTINGS] ([IsDeleted]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722111155_AddProjectSettings'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_RII_PROJECT_SETTINGS_KEY] ON [RII_PROJECT_SETTINGS] ([SettingKey]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722111155_AddProjectSettings'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260722111155_AddProjectSettings', N'10.0.10');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722130429_AddDocumentSeriesModule'
)
BEGIN
    CREATE TABLE [RII_DOCUMENT_SERIES] (
        [Id] bigint NOT NULL IDENTITY,
        [WarehouseId] bigint NULL,
        [Code] nvarchar(20) NOT NULL,
        [Name] nvarchar(150) NOT NULL,
        [DocumentType] nvarchar(40) NOT NULL,
        [Prefix] nvarchar(10) NOT NULL,
        [Separator] nvarchar(3) NOT NULL DEFAULT N'-',
        [YearFormat] nvarchar(20) NOT NULL,
        [NumberLength] int NOT NULL DEFAULT 8,
        [StartNumber] bigint NOT NULL DEFAULT CAST(1 AS bigint),
        [NextNumber] bigint NOT NULL DEFAULT CAST(1 AS bigint),
        [IncrementBy] int NOT NULL DEFAULT 1,
        [IsDefault] bit NOT NULL DEFAULT CAST(0 AS bit),
        [IsActive] bit NOT NULL DEFAULT CAST(1 AS bit),
        [HasIssuedNumbers] bit NOT NULL DEFAULT CAST(0 AS bit),
        [LastIssuedAt] datetime2 NULL,
        [Description] nvarchar(500) NULL,
        [RowVersion] rowversion NOT NULL,
        [BranchCode] nvarchar(10) NOT NULL DEFAULT N'0',
        [CreatedDate] datetime2 NULL,
        [UpdatedDate] datetime2 NULL,
        [DeletedDate] datetime2 NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [CreatedBy] bigint NULL,
        [UpdatedBy] bigint NULL,
        [DeletedBy] bigint NULL,
        CONSTRAINT [PK_RII_DOCUMENT_SERIES] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_RII_DOCUMENT_SERIES_INCREMENT] CHECK ([IncrementBy] BETWEEN 1 AND 1000),
        CONSTRAINT [CK_RII_DOCUMENT_SERIES_NEXT_NUMBER] CHECK ([NextNumber] >= [StartNumber]),
        CONSTRAINT [CK_RII_DOCUMENT_SERIES_NUMBER_LENGTH] CHECK ([NumberLength] BETWEEN 3 AND 18),
        CONSTRAINT [CK_RII_DOCUMENT_SERIES_START_NUMBER] CHECK ([StartNumber] > 0),
        CONSTRAINT [FK_RII_DOCUMENT_SERIES_RII_WAREHOUSE_WarehouseId] FOREIGN KEY ([WarehouseId]) REFERENCES [RII_WAREHOUSE] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722130429_AddDocumentSeriesModule'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'AvailableOnMobile', N'AvailableOnWeb', N'BranchCode', N'Code', N'CreatedBy', N'CreatedDate', N'DeletedBy', N'DeletedDate', N'Description', N'IsActive', N'Name', N'UpdatedBy', N'UpdatedDate') AND [object_id] = OBJECT_ID(N'[RII_PERMISSION_DEFINITIONS]'))
        SET IDENTITY_INSERT [RII_PERMISSION_DEFINITIONS] ON;
    EXEC(N'INSERT INTO [RII_PERMISSION_DEFINITIONS] ([Id], [AvailableOnMobile], [AvailableOnWeb], [BranchCode], [Code], [CreatedBy], [CreatedDate], [DeletedBy], [DeletedDate], [Description], [IsActive], [Name], [UpdatedBy], [UpdatedDate])
    VALUES (CAST(1020 AS bigint), CAST(0 AS bit), CAST(1 AS bit), N''0'', N''WMS.DOCUMENT_SERIES.VIEW'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, NULL, CAST(1 AS bit), N''Belge Serilerini Görüntüle'', NULL, NULL),
    (CAST(1021 AS bigint), CAST(0 AS bit), CAST(1 AS bit), N''0'', N''WMS.DOCUMENT_SERIES.CREATE'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, NULL, CAST(1 AS bit), N''Belge Serisi Oluştur'', NULL, NULL),
    (CAST(1022 AS bigint), CAST(0 AS bit), CAST(1 AS bit), N''0'', N''WMS.DOCUMENT_SERIES.UPDATE'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, NULL, CAST(1 AS bit), N''Belge Serisini Güncelle'', NULL, NULL),
    (CAST(1023 AS bigint), CAST(0 AS bit), CAST(1 AS bit), N''0'', N''WMS.DOCUMENT_SERIES.DELETE'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, NULL, CAST(1 AS bit), N''Belge Serisini Sil'', NULL, NULL)');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'AvailableOnMobile', N'AvailableOnWeb', N'BranchCode', N'Code', N'CreatedBy', N'CreatedDate', N'DeletedBy', N'DeletedDate', N'Description', N'IsActive', N'Name', N'UpdatedBy', N'UpdatedDate') AND [object_id] = OBJECT_ID(N'[RII_PERMISSION_DEFINITIONS]'))
        SET IDENTITY_INSERT [RII_PERMISSION_DEFINITIONS] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722130429_AddDocumentSeriesModule'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'BranchCode', N'CreatedBy', N'CreatedDate', N'DeletedBy', N'DeletedDate', N'PermissionDefinitionId', N'PermissionGroupId', N'UpdatedBy', N'UpdatedDate') AND [object_id] = OBJECT_ID(N'[RII_PERMISSION_GROUP_PERMISSIONS]'))
        SET IDENTITY_INSERT [RII_PERMISSION_GROUP_PERMISSIONS] ON;
    EXEC(N'INSERT INTO [RII_PERMISSION_GROUP_PERMISSIONS] ([Id], [BranchCode], [CreatedBy], [CreatedDate], [DeletedBy], [DeletedDate], [PermissionDefinitionId], [PermissionGroupId], [UpdatedBy], [UpdatedDate])
    VALUES (CAST(1020 AS bigint), N''0'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, CAST(1020 AS bigint), CAST(1001 AS bigint), NULL, NULL),
    (CAST(1021 AS bigint), N''0'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, CAST(1021 AS bigint), CAST(1001 AS bigint), NULL, NULL),
    (CAST(1022 AS bigint), N''0'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, CAST(1022 AS bigint), CAST(1001 AS bigint), NULL, NULL),
    (CAST(1023 AS bigint), N''0'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, CAST(1023 AS bigint), CAST(1001 AS bigint), NULL, NULL)');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'BranchCode', N'CreatedBy', N'CreatedDate', N'DeletedBy', N'DeletedDate', N'PermissionDefinitionId', N'PermissionGroupId', N'UpdatedBy', N'UpdatedDate') AND [object_id] = OBJECT_ID(N'[RII_PERMISSION_GROUP_PERMISSIONS]'))
        SET IDENTITY_INSERT [RII_PERMISSION_GROUP_PERMISSIONS] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722130429_AddDocumentSeriesModule'
)
BEGIN
    CREATE INDEX [IX_RII_DOCUMENT_SERIES_IsDeleted] ON [RII_DOCUMENT_SERIES] ([IsDeleted]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722130429_AddDocumentSeriesModule'
)
BEGIN
    CREATE INDEX [IX_RII_DOCUMENT_SERIES_RESOLUTION] ON [RII_DOCUMENT_SERIES] ([DocumentType], [WarehouseId], [IsActive]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722130429_AddDocumentSeriesModule'
)
BEGIN
    CREATE INDEX [IX_RII_DOCUMENT_SERIES_WarehouseId] ON [RII_DOCUMENT_SERIES] ([WarehouseId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722130429_AddDocumentSeriesModule'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_RII_DOCUMENT_SERIES_DEFAULT_SCOPE] ON [RII_DOCUMENT_SERIES] ([BranchCode], [DocumentType], [WarehouseId]) WHERE [IsDefault] = 1 AND [IsActive] = 1 AND [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722130429_AddDocumentSeriesModule'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_RII_DOCUMENT_SERIES_SCOPE_CODE] ON [RII_DOCUMENT_SERIES] ([BranchCode], [DocumentType], [Code]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722130429_AddDocumentSeriesModule'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260722130429_AddDocumentSeriesModule', N'10.0.10');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722133245_AddBarcodeDesignerModule'
)
BEGIN
    CREATE TABLE [RII_BARCODE_TEMPLATE] (
        [Id] bigint NOT NULL IDENTITY,
        [TemplateCode] nvarchar(50) NOT NULL,
        [DisplayName] nvarchar(150) NOT NULL,
        [LabelType] nvarchar(30) NOT NULL,
        [WidthMm] decimal(8,2) NOT NULL,
        [HeightMm] decimal(8,2) NOT NULL,
        [Dpi] int NOT NULL,
        [EngineType] nvarchar(40) NOT NULL,
        [IsActive] bit NOT NULL DEFAULT CAST(1 AS bit),
        [DraftVersionId] bigint NULL,
        [PublishedVersionId] bigint NULL,
        [RowVersion] rowversion NOT NULL,
        [BranchCode] nvarchar(10) NOT NULL DEFAULT N'0',
        [CreatedDate] datetime2 NULL,
        [UpdatedDate] datetime2 NULL,
        [DeletedDate] datetime2 NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [CreatedBy] bigint NULL,
        [UpdatedBy] bigint NULL,
        [DeletedBy] bigint NULL,
        CONSTRAINT [PK_RII_BARCODE_TEMPLATE] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_RII_BARCODE_TEMPLATE_DPI] CHECK ([Dpi] IN (203,300,600)),
        CONSTRAINT [CK_RII_BARCODE_TEMPLATE_SIZE] CHECK ([WidthMm] BETWEEN 10 AND 300 AND [HeightMm] BETWEEN 10 AND 500)
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722133245_AddBarcodeDesignerModule'
)
BEGIN
    CREATE TABLE [RII_BARCODE_TEMPLATE_VERSION] (
        [Id] bigint NOT NULL IDENTITY,
        [BarcodeTemplateId] bigint NOT NULL,
        [VersionNo] int NOT NULL,
        [IsPublished] bit NOT NULL,
        [PublishedAt] datetime2 NULL,
        [Notes] nvarchar(500) NULL,
        [TemplateJson] nvarchar(max) NOT NULL,
        [RowVersion] rowversion NOT NULL,
        [BranchCode] nvarchar(10) NOT NULL DEFAULT N'0',
        [CreatedDate] datetime2 NULL,
        [UpdatedDate] datetime2 NULL,
        [DeletedDate] datetime2 NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [CreatedBy] bigint NULL,
        [UpdatedBy] bigint NULL,
        [DeletedBy] bigint NULL,
        CONSTRAINT [PK_RII_BARCODE_TEMPLATE_VERSION] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_RII_BARCODE_TEMPLATE_VERSION_RII_BARCODE_TEMPLATE_BarcodeTemplateId] FOREIGN KEY ([BarcodeTemplateId]) REFERENCES [RII_BARCODE_TEMPLATE] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722133245_AddBarcodeDesignerModule'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'AvailableOnMobile', N'AvailableOnWeb', N'BranchCode', N'Code', N'CreatedBy', N'CreatedDate', N'DeletedBy', N'DeletedDate', N'Description', N'IsActive', N'Name', N'UpdatedBy', N'UpdatedDate') AND [object_id] = OBJECT_ID(N'[RII_PERMISSION_DEFINITIONS]'))
        SET IDENTITY_INSERT [RII_PERMISSION_DEFINITIONS] ON;
    EXEC(N'INSERT INTO [RII_PERMISSION_DEFINITIONS] ([Id], [AvailableOnMobile], [AvailableOnWeb], [BranchCode], [Code], [CreatedBy], [CreatedDate], [DeletedBy], [DeletedDate], [Description], [IsActive], [Name], [UpdatedBy], [UpdatedDate])
    VALUES (CAST(1024 AS bigint), CAST(0 AS bit), CAST(1 AS bit), N''0'', N''WMS.BARCODE_DESIGNER.VIEW'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, NULL, CAST(1 AS bit), N''Barkod Şablonlarını Görüntüle'', NULL, NULL),
    (CAST(1025 AS bigint), CAST(0 AS bit), CAST(1 AS bit), N''0'', N''WMS.BARCODE_DESIGNER.CREATE'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, NULL, CAST(1 AS bit), N''Barkod Şablonu Oluştur'', NULL, NULL),
    (CAST(1026 AS bigint), CAST(0 AS bit), CAST(1 AS bit), N''0'', N''WMS.BARCODE_DESIGNER.UPDATE'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, NULL, CAST(1 AS bit), N''Barkod Şablonunu Güncelle'', NULL, NULL),
    (CAST(1027 AS bigint), CAST(0 AS bit), CAST(1 AS bit), N''0'', N''WMS.BARCODE_DESIGNER.DELETE'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, NULL, CAST(1 AS bit), N''Barkod Şablonunu Sil'', NULL, NULL),
    (CAST(1028 AS bigint), CAST(0 AS bit), CAST(1 AS bit), N''0'', N''WMS.BARCODE_DESIGNER.PUBLISH'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, NULL, CAST(1 AS bit), N''Barkod Şablonu Yayınla'', NULL, NULL),
    (CAST(1029 AS bigint), CAST(0 AS bit), CAST(1 AS bit), N''0'', N''WMS.BARCODE_DESIGNER.PRINT'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, NULL, CAST(1 AS bit), N''Barkod Etiketi Yazdır'', NULL, NULL)');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'AvailableOnMobile', N'AvailableOnWeb', N'BranchCode', N'Code', N'CreatedBy', N'CreatedDate', N'DeletedBy', N'DeletedDate', N'Description', N'IsActive', N'Name', N'UpdatedBy', N'UpdatedDate') AND [object_id] = OBJECT_ID(N'[RII_PERMISSION_DEFINITIONS]'))
        SET IDENTITY_INSERT [RII_PERMISSION_DEFINITIONS] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722133245_AddBarcodeDesignerModule'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'BranchCode', N'CreatedBy', N'CreatedDate', N'DeletedBy', N'DeletedDate', N'PermissionDefinitionId', N'PermissionGroupId', N'UpdatedBy', N'UpdatedDate') AND [object_id] = OBJECT_ID(N'[RII_PERMISSION_GROUP_PERMISSIONS]'))
        SET IDENTITY_INSERT [RII_PERMISSION_GROUP_PERMISSIONS] ON;
    EXEC(N'INSERT INTO [RII_PERMISSION_GROUP_PERMISSIONS] ([Id], [BranchCode], [CreatedBy], [CreatedDate], [DeletedBy], [DeletedDate], [PermissionDefinitionId], [PermissionGroupId], [UpdatedBy], [UpdatedDate])
    VALUES (CAST(1024 AS bigint), N''0'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, CAST(1024 AS bigint), CAST(1001 AS bigint), NULL, NULL),
    (CAST(1025 AS bigint), N''0'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, CAST(1025 AS bigint), CAST(1001 AS bigint), NULL, NULL),
    (CAST(1026 AS bigint), N''0'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, CAST(1026 AS bigint), CAST(1001 AS bigint), NULL, NULL),
    (CAST(1027 AS bigint), N''0'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, CAST(1027 AS bigint), CAST(1001 AS bigint), NULL, NULL),
    (CAST(1028 AS bigint), N''0'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, CAST(1028 AS bigint), CAST(1001 AS bigint), NULL, NULL),
    (CAST(1029 AS bigint), N''0'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, CAST(1029 AS bigint), CAST(1001 AS bigint), NULL, NULL)');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'BranchCode', N'CreatedBy', N'CreatedDate', N'DeletedBy', N'DeletedDate', N'PermissionDefinitionId', N'PermissionGroupId', N'UpdatedBy', N'UpdatedDate') AND [object_id] = OBJECT_ID(N'[RII_PERMISSION_GROUP_PERMISSIONS]'))
        SET IDENTITY_INSERT [RII_PERMISSION_GROUP_PERMISSIONS] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722133245_AddBarcodeDesignerModule'
)
BEGIN
    CREATE INDEX [IX_RII_BARCODE_TEMPLATE_IsDeleted] ON [RII_BARCODE_TEMPLATE] ([IsDeleted]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722133245_AddBarcodeDesignerModule'
)
BEGIN
    CREATE INDEX [IX_RII_BARCODE_TEMPLATE_TYPE_ACTIVE] ON [RII_BARCODE_TEMPLATE] ([LabelType], [IsActive]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722133245_AddBarcodeDesignerModule'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_RII_BARCODE_TEMPLATE_BRANCH_CODE] ON [RII_BARCODE_TEMPLATE] ([BranchCode], [TemplateCode]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722133245_AddBarcodeDesignerModule'
)
BEGIN
    CREATE INDEX [IX_RII_BARCODE_TEMPLATE_VERSION_IsDeleted] ON [RII_BARCODE_TEMPLATE_VERSION] ([IsDeleted]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722133245_AddBarcodeDesignerModule'
)
BEGIN
    CREATE INDEX [IX_RII_BARCODE_TEMPLATE_VERSION_PUBLISHED] ON [RII_BARCODE_TEMPLATE_VERSION] ([BarcodeTemplateId], [IsPublished]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722133245_AddBarcodeDesignerModule'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_RII_BARCODE_TEMPLATE_VERSION_NO] ON [RII_BARCODE_TEMPLATE_VERSION] ([BarcodeTemplateId], [VersionNo]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722133245_AddBarcodeDesignerModule'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260722133245_AddBarcodeDesignerModule', N'10.0.10');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722140804_AddBarcodeRulesAndGeneratedRegistry'
)
BEGIN
    CREATE TABLE [RII_BARCODE_RULE] (
        [Id] bigint NOT NULL IDENTITY,
        [RuleCode] nvarchar(50) NOT NULL,
        [DisplayName] nvarchar(150) NOT NULL,
        [Target] nvarchar(30) NOT NULL,
        [Prefix] nvarchar(30) NULL,
        [Separator] nvarchar(5) NOT NULL,
        [NextSequence] bigint NOT NULL,
        [IsActive] bit NOT NULL DEFAULT CAST(1 AS bit),
        [RowVersion] rowversion NOT NULL,
        [BranchCode] nvarchar(10) NOT NULL DEFAULT N'0',
        [CreatedDate] datetime2 NULL,
        [UpdatedDate] datetime2 NULL,
        [DeletedDate] datetime2 NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [CreatedBy] bigint NULL,
        [UpdatedBy] bigint NULL,
        [DeletedBy] bigint NULL,
        CONSTRAINT [PK_RII_BARCODE_RULE] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_RII_BARCODE_RULE_SEQUENCE] CHECK ([NextSequence] > 0)
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722140804_AddBarcodeRulesAndGeneratedRegistry'
)
BEGIN
    CREATE TABLE [RII_BARCODE_RULE_SEGMENT] (
        [Id] bigint NOT NULL IDENTITY,
        [BarcodeRuleId] bigint NOT NULL,
        [Order] int NOT NULL,
        [SegmentType] nvarchar(20) NOT NULL,
        [SourceField] nvarchar(30) NULL,
        [LiteralValue] nvarchar(50) NULL,
        [IsRequired] bit NOT NULL,
        [Transform] nvarchar(20) NOT NULL,
        [SequenceLength] int NOT NULL,
        [DateFormat] nvarchar(20) NOT NULL,
        [BranchCode] nvarchar(10) NOT NULL DEFAULT N'0',
        [CreatedDate] datetime2 NULL,
        [UpdatedDate] datetime2 NULL,
        [DeletedDate] datetime2 NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [CreatedBy] bigint NULL,
        [UpdatedBy] bigint NULL,
        [DeletedBy] bigint NULL,
        CONSTRAINT [PK_RII_BARCODE_RULE_SEGMENT] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_RII_BARCODE_RULE_SEGMENT_RII_BARCODE_RULE_BarcodeRuleId] FOREIGN KEY ([BarcodeRuleId]) REFERENCES [RII_BARCODE_RULE] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722140804_AddBarcodeRulesAndGeneratedRegistry'
)
BEGIN
    CREATE TABLE [RII_GENERATED_BARCODE] (
        [Id] bigint NOT NULL IDENTITY,
        [BarcodeRuleId] bigint NOT NULL,
        [BarcodeValue] nvarchar(120) NOT NULL,
        [BarcodeHash] nvarchar(64) NOT NULL,
        [IdempotencyHash] nvarchar(64) NOT NULL,
        [StockCode] nvarchar(50) NULL,
        [SerialNo] nvarchar(100) NULL,
        [YapCode] nvarchar(100) NULL,
        [LotNo] nvarchar(100) NULL,
        [SequenceNo] bigint NOT NULL,
        [GeneratedAt] datetime2 NOT NULL,
        [BranchCode] nvarchar(10) NOT NULL DEFAULT N'0',
        [CreatedDate] datetime2 NULL,
        [UpdatedDate] datetime2 NULL,
        [DeletedDate] datetime2 NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [CreatedBy] bigint NULL,
        [UpdatedBy] bigint NULL,
        [DeletedBy] bigint NULL,
        CONSTRAINT [PK_RII_GENERATED_BARCODE] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_RII_GENERATED_BARCODE_RII_BARCODE_RULE_BarcodeRuleId] FOREIGN KEY ([BarcodeRuleId]) REFERENCES [RII_BARCODE_RULE] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722140804_AddBarcodeRulesAndGeneratedRegistry'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'AvailableOnMobile', N'AvailableOnWeb', N'BranchCode', N'Code', N'CreatedBy', N'CreatedDate', N'DeletedBy', N'DeletedDate', N'Description', N'IsActive', N'Name', N'UpdatedBy', N'UpdatedDate') AND [object_id] = OBJECT_ID(N'[RII_PERMISSION_DEFINITIONS]'))
        SET IDENTITY_INSERT [RII_PERMISSION_DEFINITIONS] ON;
    EXEC(N'INSERT INTO [RII_PERMISSION_DEFINITIONS] ([Id], [AvailableOnMobile], [AvailableOnWeb], [BranchCode], [Code], [CreatedBy], [CreatedDate], [DeletedBy], [DeletedDate], [Description], [IsActive], [Name], [UpdatedBy], [UpdatedDate])
    VALUES (CAST(1030 AS bigint), CAST(0 AS bit), CAST(1 AS bit), N''0'', N''WMS.BARCODE_RULES.VIEW'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, NULL, CAST(1 AS bit), N''Barkod Kurallarını Görüntüle'', NULL, NULL),
    (CAST(1031 AS bigint), CAST(0 AS bit), CAST(1 AS bit), N''0'', N''WMS.BARCODE_RULES.MANAGE'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, NULL, CAST(1 AS bit), N''Barkod Kurallarını Yönet'', NULL, NULL),
    (CAST(1032 AS bigint), CAST(0 AS bit), CAST(1 AS bit), N''0'', N''WMS.BARCODE_RULES.GENERATE'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, NULL, CAST(1 AS bit), N''Benzersiz Barkod Üret'', NULL, NULL)');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'AvailableOnMobile', N'AvailableOnWeb', N'BranchCode', N'Code', N'CreatedBy', N'CreatedDate', N'DeletedBy', N'DeletedDate', N'Description', N'IsActive', N'Name', N'UpdatedBy', N'UpdatedDate') AND [object_id] = OBJECT_ID(N'[RII_PERMISSION_DEFINITIONS]'))
        SET IDENTITY_INSERT [RII_PERMISSION_DEFINITIONS] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722140804_AddBarcodeRulesAndGeneratedRegistry'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'BranchCode', N'CreatedBy', N'CreatedDate', N'DeletedBy', N'DeletedDate', N'PermissionDefinitionId', N'PermissionGroupId', N'UpdatedBy', N'UpdatedDate') AND [object_id] = OBJECT_ID(N'[RII_PERMISSION_GROUP_PERMISSIONS]'))
        SET IDENTITY_INSERT [RII_PERMISSION_GROUP_PERMISSIONS] ON;
    EXEC(N'INSERT INTO [RII_PERMISSION_GROUP_PERMISSIONS] ([Id], [BranchCode], [CreatedBy], [CreatedDate], [DeletedBy], [DeletedDate], [PermissionDefinitionId], [PermissionGroupId], [UpdatedBy], [UpdatedDate])
    VALUES (CAST(1030 AS bigint), N''0'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, CAST(1030 AS bigint), CAST(1001 AS bigint), NULL, NULL),
    (CAST(1031 AS bigint), N''0'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, CAST(1031 AS bigint), CAST(1001 AS bigint), NULL, NULL),
    (CAST(1032 AS bigint), N''0'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, CAST(1032 AS bigint), CAST(1001 AS bigint), NULL, NULL)');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'BranchCode', N'CreatedBy', N'CreatedDate', N'DeletedBy', N'DeletedDate', N'PermissionDefinitionId', N'PermissionGroupId', N'UpdatedBy', N'UpdatedDate') AND [object_id] = OBJECT_ID(N'[RII_PERMISSION_GROUP_PERMISSIONS]'))
        SET IDENTITY_INSERT [RII_PERMISSION_GROUP_PERMISSIONS] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722140804_AddBarcodeRulesAndGeneratedRegistry'
)
BEGIN
    CREATE INDEX [IX_RII_BARCODE_RULE_IsDeleted] ON [RII_BARCODE_RULE] ([IsDeleted]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722140804_AddBarcodeRulesAndGeneratedRegistry'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_RII_BARCODE_RULE_BRANCH_CODE] ON [RII_BARCODE_RULE] ([BranchCode], [RuleCode]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722140804_AddBarcodeRulesAndGeneratedRegistry'
)
BEGIN
    CREATE INDEX [IX_RII_BARCODE_RULE_SEGMENT_IsDeleted] ON [RII_BARCODE_RULE_SEGMENT] ([IsDeleted]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722140804_AddBarcodeRulesAndGeneratedRegistry'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_RII_BARCODE_RULE_SEGMENT_ORDER] ON [RII_BARCODE_RULE_SEGMENT] ([BarcodeRuleId], [Order]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722140804_AddBarcodeRulesAndGeneratedRegistry'
)
BEGIN
    CREATE INDEX [IX_RII_GENERATED_BARCODE_IsDeleted] ON [RII_GENERATED_BARCODE] ([IsDeleted]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722140804_AddBarcodeRulesAndGeneratedRegistry'
)
BEGIN
    CREATE UNIQUE INDEX [UX_RII_GENERATED_BARCODE_HASH] ON [RII_GENERATED_BARCODE] ([BarcodeHash]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722140804_AddBarcodeRulesAndGeneratedRegistry'
)
BEGIN
    CREATE UNIQUE INDEX [UX_RII_GENERATED_BARCODE_IDEMPOTENCY] ON [RII_GENERATED_BARCODE] ([BarcodeRuleId], [IdempotencyHash]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722140804_AddBarcodeRulesAndGeneratedRegistry'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260722140804_AddBarcodeRulesAndGeneratedRegistry', N'10.0.10');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722141031_SeedDefaultBarcodeRule'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'BranchCode', N'CreatedBy', N'CreatedDate', N'DeletedBy', N'DeletedDate', N'DisplayName', N'IsActive', N'NextSequence', N'Prefix', N'RuleCode', N'Separator', N'Target', N'UpdatedBy', N'UpdatedDate') AND [object_id] = OBJECT_ID(N'[RII_BARCODE_RULE]'))
        SET IDENTITY_INSERT [RII_BARCODE_RULE] ON;
    EXEC(N'INSERT INTO [RII_BARCODE_RULE] ([Id], [BranchCode], [CreatedBy], [CreatedDate], [DeletedBy], [DeletedDate], [DisplayName], [IsActive], [NextSequence], [Prefix], [RuleCode], [Separator], [Target], [UpdatedBy], [UpdatedDate])
    VALUES (CAST(1 AS bigint), N''0'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, N''Stok İzlenebilirlik Barkodu'', CAST(1 AS bit), CAST(1 AS bigint), N''WMS'', N''STOCK_TRACE_UNIQUE'', N''/'', N''Serial'', NULL, NULL)');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'BranchCode', N'CreatedBy', N'CreatedDate', N'DeletedBy', N'DeletedDate', N'DisplayName', N'IsActive', N'NextSequence', N'Prefix', N'RuleCode', N'Separator', N'Target', N'UpdatedBy', N'UpdatedDate') AND [object_id] = OBJECT_ID(N'[RII_BARCODE_RULE]'))
        SET IDENTITY_INSERT [RII_BARCODE_RULE] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722141031_SeedDefaultBarcodeRule'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'BarcodeRuleId', N'BranchCode', N'CreatedBy', N'CreatedDate', N'DateFormat', N'DeletedBy', N'DeletedDate', N'IsRequired', N'LiteralValue', N'Order', N'SegmentType', N'SequenceLength', N'SourceField', N'Transform', N'UpdatedBy', N'UpdatedDate') AND [object_id] = OBJECT_ID(N'[RII_BARCODE_RULE_SEGMENT]'))
        SET IDENTITY_INSERT [RII_BARCODE_RULE_SEGMENT] ON;
    EXEC(N'INSERT INTO [RII_BARCODE_RULE_SEGMENT] ([Id], [BarcodeRuleId], [BranchCode], [CreatedBy], [CreatedDate], [DateFormat], [DeletedBy], [DeletedDate], [IsRequired], [LiteralValue], [Order], [SegmentType], [SequenceLength], [SourceField], [Transform], [UpdatedBy], [UpdatedDate])
    VALUES (CAST(1 AS bigint), CAST(1 AS bigint), N''0'', NULL, ''2026-07-21T00:00:00.0000000Z'', N''yyyyMMdd'', NULL, NULL, CAST(1 AS bit), NULL, 1, N''Field'', 8, N''StockCode'', N''Upper'', NULL, NULL),
    (CAST(2 AS bigint), CAST(1 AS bigint), N''0'', NULL, ''2026-07-21T00:00:00.0000000Z'', N''yyyyMMdd'', NULL, NULL, CAST(0 AS bit), NULL, 2, N''Field'', 8, N''SerialNo'', N''Upper'', NULL, NULL),
    (CAST(3 AS bigint), CAST(1 AS bigint), N''0'', NULL, ''2026-07-21T00:00:00.0000000Z'', N''yyyyMMdd'', NULL, NULL, CAST(0 AS bit), NULL, 3, N''Field'', 8, N''YapCode'', N''Upper'', NULL, NULL),
    (CAST(4 AS bigint), CAST(1 AS bigint), N''0'', NULL, ''2026-07-21T00:00:00.0000000Z'', N''yyyyMMdd'', NULL, NULL, CAST(0 AS bit), NULL, 4, N''Field'', 8, N''LotNo'', N''Upper'', NULL, NULL),
    (CAST(5 AS bigint), CAST(1 AS bigint), N''0'', NULL, ''2026-07-21T00:00:00.0000000Z'', N''yyyyMMdd'', NULL, NULL, CAST(1 AS bit), NULL, 5, N''Sequence'', 8, NULL, N''None'', NULL, NULL)');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'BarcodeRuleId', N'BranchCode', N'CreatedBy', N'CreatedDate', N'DateFormat', N'DeletedBy', N'DeletedDate', N'IsRequired', N'LiteralValue', N'Order', N'SegmentType', N'SequenceLength', N'SourceField', N'Transform', N'UpdatedBy', N'UpdatedDate') AND [object_id] = OBJECT_ID(N'[RII_BARCODE_RULE_SEGMENT]'))
        SET IDENTITY_INSERT [RII_BARCODE_RULE_SEGMENT] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722141031_SeedDefaultBarcodeRule'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260722141031_SeedDefaultBarcodeRule', N'10.0.10');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722153951_RefactorBarcodeRulesToGlobalPolicyAndAddErpAccessPermissions'
)
BEGIN
    ALTER TABLE [RII_GENERATED_BARCODE] DROP CONSTRAINT [FK_RII_GENERATED_BARCODE_RII_BARCODE_RULE_BarcodeRuleId];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722153951_RefactorBarcodeRulesToGlobalPolicyAndAddErpAccessPermissions'
)
BEGIN
    DROP TABLE [RII_BARCODE_RULE_SEGMENT];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722153951_RefactorBarcodeRulesToGlobalPolicyAndAddErpAccessPermissions'
)
BEGIN
    DROP TABLE [RII_BARCODE_RULE];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722153951_RefactorBarcodeRulesToGlobalPolicyAndAddErpAccessPermissions'
)
BEGIN
    DROP INDEX [UX_RII_GENERATED_BARCODE_IDEMPOTENCY] ON [RII_GENERATED_BARCODE];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722153951_RefactorBarcodeRulesToGlobalPolicyAndAddErpAccessPermissions'
)
BEGIN
    EXEC sp_rename N'[RII_GENERATED_BARCODE].[BarcodeRuleId]', N'BarcodePolicyProfileId', 'COLUMN';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722153951_RefactorBarcodeRulesToGlobalPolicyAndAddErpAccessPermissions'
)
BEGIN
    ALTER TABLE [RII_GENERATED_BARCODE] ADD [BarcodePolicyId] bigint NOT NULL DEFAULT CAST(0 AS bigint);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722153951_RefactorBarcodeRulesToGlobalPolicyAndAddErpAccessPermissions'
)
BEGIN
    ALTER TABLE [RII_GENERATED_BARCODE] ADD [DocumentNo] nvarchar(100) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722153951_RefactorBarcodeRulesToGlobalPolicyAndAddErpAccessPermissions'
)
BEGIN
    ALTER TABLE [RII_GENERATED_BARCODE] ADD [LocationCode] nvarchar(100) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722153951_RefactorBarcodeRulesToGlobalPolicyAndAddErpAccessPermissions'
)
BEGIN
    ALTER TABLE [RII_GENERATED_BARCODE] ADD [PolicyVersion] int NOT NULL DEFAULT 0;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722153951_RefactorBarcodeRulesToGlobalPolicyAndAddErpAccessPermissions'
)
BEGIN
    ALTER TABLE [RII_GENERATED_BARCODE] ADD [Scope] nvarchar(30) NOT NULL DEFAULT N'';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722153951_RefactorBarcodeRulesToGlobalPolicyAndAddErpAccessPermissions'
)
BEGIN
    ALTER TABLE [RII_GENERATED_BARCODE] ADD [WarehouseCode] nvarchar(50) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722153951_RefactorBarcodeRulesToGlobalPolicyAndAddErpAccessPermissions'
)
BEGIN
    CREATE TABLE [RII_BARCODE_POLICY] (
        [Id] bigint NOT NULL IDENTITY,
        [PolicyKey] nvarchar(30) NOT NULL,
        [DisplayName] nvarchar(150) NOT NULL,
        [CurrentVersion] int NOT NULL,
        [IsActive] bit NOT NULL DEFAULT CAST(1 AS bit),
        [RowVersion] rowversion NOT NULL,
        [BranchCode] nvarchar(10) NOT NULL DEFAULT N'0',
        [CreatedDate] datetime2 NULL,
        [UpdatedDate] datetime2 NULL,
        [DeletedDate] datetime2 NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [CreatedBy] bigint NULL,
        [UpdatedBy] bigint NULL,
        [DeletedBy] bigint NULL,
        CONSTRAINT [PK_RII_BARCODE_POLICY] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_RII_BARCODE_POLICY_VERSION] CHECK ([CurrentVersion] > 0)
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722153951_RefactorBarcodeRulesToGlobalPolicyAndAddErpAccessPermissions'
)
BEGIN
    CREATE TABLE [RII_BARCODE_POLICY_PROFILE] (
        [Id] bigint NOT NULL IDENTITY,
        [BarcodePolicyId] bigint NOT NULL,
        [Scope] nvarchar(30) NOT NULL,
        [DisplayName] nvarchar(150) NOT NULL,
        [Prefix] nvarchar(30) NULL,
        [Separator] nvarchar(5) NOT NULL,
        [NextSequence] bigint NOT NULL,
        [IsEnabled] bit NOT NULL DEFAULT CAST(1 AS bit),
        [RowVersion] rowversion NOT NULL,
        [BranchCode] nvarchar(10) NOT NULL DEFAULT N'0',
        [CreatedDate] datetime2 NULL,
        [UpdatedDate] datetime2 NULL,
        [DeletedDate] datetime2 NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [CreatedBy] bigint NULL,
        [UpdatedBy] bigint NULL,
        [DeletedBy] bigint NULL,
        CONSTRAINT [PK_RII_BARCODE_POLICY_PROFILE] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_RII_BARCODE_POLICY_PROFILE_SEQUENCE] CHECK ([NextSequence] > 0),
        CONSTRAINT [FK_RII_BARCODE_POLICY_PROFILE_RII_BARCODE_POLICY_BarcodePolicyId] FOREIGN KEY ([BarcodePolicyId]) REFERENCES [RII_BARCODE_POLICY] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722153951_RefactorBarcodeRulesToGlobalPolicyAndAddErpAccessPermissions'
)
BEGIN
    CREATE TABLE [RII_BARCODE_POLICY_PROFILE_SEGMENT] (
        [Id] bigint NOT NULL IDENTITY,
        [BarcodePolicyProfileId] bigint NOT NULL,
        [Order] int NOT NULL,
        [SegmentType] nvarchar(20) NOT NULL,
        [SourceField] nvarchar(30) NULL,
        [LiteralValue] nvarchar(50) NULL,
        [IsRequired] bit NOT NULL,
        [Transform] nvarchar(20) NOT NULL,
        [SequenceLength] int NOT NULL,
        [DateFormat] nvarchar(20) NOT NULL,
        [BranchCode] nvarchar(10) NOT NULL DEFAULT N'0',
        [CreatedDate] datetime2 NULL,
        [UpdatedDate] datetime2 NULL,
        [DeletedDate] datetime2 NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [CreatedBy] bigint NULL,
        [UpdatedBy] bigint NULL,
        [DeletedBy] bigint NULL,
        CONSTRAINT [PK_RII_BARCODE_POLICY_PROFILE_SEGMENT] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_RII_BARCODE_POLICY_PROFILE_SEGMENT_RII_BARCODE_POLICY_PROFILE_BarcodePolicyProfileId] FOREIGN KEY ([BarcodePolicyProfileId]) REFERENCES [RII_BARCODE_POLICY_PROFILE] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722153951_RefactorBarcodeRulesToGlobalPolicyAndAddErpAccessPermissions'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'BranchCode', N'CreatedBy', N'CreatedDate', N'CurrentVersion', N'DeletedBy', N'DeletedDate', N'DisplayName', N'IsActive', N'PolicyKey', N'UpdatedBy', N'UpdatedDate') AND [object_id] = OBJECT_ID(N'[RII_BARCODE_POLICY]'))
        SET IDENTITY_INSERT [RII_BARCODE_POLICY] ON;
    EXEC(N'INSERT INTO [RII_BARCODE_POLICY] ([Id], [BranchCode], [CreatedBy], [CreatedDate], [CurrentVersion], [DeletedBy], [DeletedDate], [DisplayName], [IsActive], [PolicyKey], [UpdatedBy], [UpdatedDate])
    VALUES (CAST(1 AS bigint), N''0'', NULL, ''2026-07-21T00:00:00.0000000Z'', 1, NULL, NULL, N''Genel Barkod Politikası'', CAST(1 AS bit), N''GLOBAL'', NULL, NULL)');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'BranchCode', N'CreatedBy', N'CreatedDate', N'CurrentVersion', N'DeletedBy', N'DeletedDate', N'DisplayName', N'IsActive', N'PolicyKey', N'UpdatedBy', N'UpdatedDate') AND [object_id] = OBJECT_ID(N'[RII_BARCODE_POLICY]'))
        SET IDENTITY_INSERT [RII_BARCODE_POLICY] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722153951_RefactorBarcodeRulesToGlobalPolicyAndAddErpAccessPermissions'
)
BEGIN
    EXEC(N'UPDATE [RII_PERMISSION_DEFINITIONS] SET [Code] = N''WMS.BARCODE_POLICY.VIEW'', [Name] = N''Genel Barkod Politikasını Görüntüle''
    WHERE [Id] = CAST(1030 AS bigint);
    SELECT @@ROWCOUNT');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722153951_RefactorBarcodeRulesToGlobalPolicyAndAddErpAccessPermissions'
)
BEGIN
    EXEC(N'UPDATE [RII_PERMISSION_DEFINITIONS] SET [Code] = N''WMS.BARCODE_POLICY.MANAGE'', [Name] = N''Genel Barkod Politikasını Yönet''
    WHERE [Id] = CAST(1031 AS bigint);
    SELECT @@ROWCOUNT');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722153951_RefactorBarcodeRulesToGlobalPolicyAndAddErpAccessPermissions'
)
BEGIN
    EXEC(N'UPDATE [RII_PERMISSION_DEFINITIONS] SET [Code] = N''WMS.BARCODE_POLICY.GENERATE'', [Name] = N''Politikaya Göre Benzersiz Barkod Üret''
    WHERE [Id] = CAST(1032 AS bigint);
    SELECT @@ROWCOUNT');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722153951_RefactorBarcodeRulesToGlobalPolicyAndAddErpAccessPermissions'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'AvailableOnMobile', N'AvailableOnWeb', N'BranchCode', N'Code', N'CreatedBy', N'CreatedDate', N'DeletedBy', N'DeletedDate', N'Description', N'IsActive', N'Name', N'UpdatedBy', N'UpdatedDate') AND [object_id] = OBJECT_ID(N'[RII_PERMISSION_DEFINITIONS]'))
        SET IDENTITY_INSERT [RII_PERMISSION_DEFINITIONS] ON;
    EXEC(N'INSERT INTO [RII_PERMISSION_DEFINITIONS] ([Id], [AvailableOnMobile], [AvailableOnWeb], [BranchCode], [Code], [CreatedBy], [CreatedDate], [DeletedBy], [DeletedDate], [Description], [IsActive], [Name], [UpdatedBy], [UpdatedDate])
    VALUES (CAST(1033 AS bigint), CAST(0 AS bit), CAST(1 AS bit), N''0'', N''ERP.MIRROR.VIEW'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, NULL, CAST(1 AS bit), N''ERP Eşlenmiş Verilerini Görüntüle'', NULL, NULL),
    (CAST(1034 AS bigint), CAST(0 AS bit), CAST(1 AS bit), N''0'', N''ERP.MIRROR.SYNC'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, NULL, CAST(1 AS bit), N''ERP Eşleme İşlemlerini Tetikle'', NULL, NULL),
    (CAST(1035 AS bigint), CAST(0 AS bit), CAST(1 AS bit), N''0'', N''ERP.NETSIS_READ.VIEW'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, NULL, CAST(1 AS bit), N''Netsis Okuma Servislerini Kullan'', NULL, NULL)');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'AvailableOnMobile', N'AvailableOnWeb', N'BranchCode', N'Code', N'CreatedBy', N'CreatedDate', N'DeletedBy', N'DeletedDate', N'Description', N'IsActive', N'Name', N'UpdatedBy', N'UpdatedDate') AND [object_id] = OBJECT_ID(N'[RII_PERMISSION_DEFINITIONS]'))
        SET IDENTITY_INSERT [RII_PERMISSION_DEFINITIONS] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722153951_RefactorBarcodeRulesToGlobalPolicyAndAddErpAccessPermissions'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'BarcodePolicyId', N'BranchCode', N'CreatedBy', N'CreatedDate', N'DeletedBy', N'DeletedDate', N'DisplayName', N'IsEnabled', N'NextSequence', N'Prefix', N'Scope', N'Separator', N'UpdatedBy', N'UpdatedDate') AND [object_id] = OBJECT_ID(N'[RII_BARCODE_POLICY_PROFILE]'))
        SET IDENTITY_INSERT [RII_BARCODE_POLICY_PROFILE] ON;
    EXEC(N'INSERT INTO [RII_BARCODE_POLICY_PROFILE] ([Id], [BarcodePolicyId], [BranchCode], [CreatedBy], [CreatedDate], [DeletedBy], [DeletedDate], [DisplayName], [IsEnabled], [NextSequence], [Prefix], [Scope], [Separator], [UpdatedBy], [UpdatedDate])
    VALUES (CAST(1 AS bigint), CAST(1 AS bigint), N''0'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, N''Ürün / Seri'', CAST(1 AS bit), CAST(1 AS bigint), N''WMS-S'', N''ProductSerial'', N''/'', NULL, NULL),
    (CAST(2 AS bigint), CAST(1 AS bigint), N''0'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, N''Ürün / Lot'', CAST(1 AS bit), CAST(1 AS bigint), N''WMS-L'', N''ProductLot'', N''/'', NULL, NULL),
    (CAST(3 AS bigint), CAST(1 AS bigint), N''0'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, N''Raf / Konum'', CAST(1 AS bit), CAST(1 AS bigint), N''WMS-R'', N''Location'', N''/'', NULL, NULL),
    (CAST(4 AS bigint), CAST(1 AS bigint), N''0'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, N''Palet / Koli / Lojistik'', CAST(1 AS bit), CAST(1 AS bigint), N''WMS-P'', N''Logistics'', N''/'', NULL, NULL),
    (CAST(5 AS bigint), CAST(1 AS bigint), N''0'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, N''Belge / Operasyon'', CAST(1 AS bit), CAST(1 AS bigint), N''WMS-B'', N''Document'', N''/'', NULL, NULL)');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'BarcodePolicyId', N'BranchCode', N'CreatedBy', N'CreatedDate', N'DeletedBy', N'DeletedDate', N'DisplayName', N'IsEnabled', N'NextSequence', N'Prefix', N'Scope', N'Separator', N'UpdatedBy', N'UpdatedDate') AND [object_id] = OBJECT_ID(N'[RII_BARCODE_POLICY_PROFILE]'))
        SET IDENTITY_INSERT [RII_BARCODE_POLICY_PROFILE] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722153951_RefactorBarcodeRulesToGlobalPolicyAndAddErpAccessPermissions'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'BarcodePolicyProfileId', N'BranchCode', N'CreatedBy', N'CreatedDate', N'DateFormat', N'DeletedBy', N'DeletedDate', N'IsRequired', N'LiteralValue', N'Order', N'SegmentType', N'SequenceLength', N'SourceField', N'Transform', N'UpdatedBy', N'UpdatedDate') AND [object_id] = OBJECT_ID(N'[RII_BARCODE_POLICY_PROFILE_SEGMENT]'))
        SET IDENTITY_INSERT [RII_BARCODE_POLICY_PROFILE_SEGMENT] ON;
    EXEC(N'INSERT INTO [RII_BARCODE_POLICY_PROFILE_SEGMENT] ([Id], [BarcodePolicyProfileId], [BranchCode], [CreatedBy], [CreatedDate], [DateFormat], [DeletedBy], [DeletedDate], [IsRequired], [LiteralValue], [Order], [SegmentType], [SequenceLength], [SourceField], [Transform], [UpdatedBy], [UpdatedDate])
    VALUES (CAST(1 AS bigint), CAST(1 AS bigint), N''0'', NULL, ''2026-07-21T00:00:00.0000000Z'', N''yyyyMMdd'', NULL, NULL, CAST(1 AS bit), NULL, 1, N''Field'', 8, N''StockCode'', N''Upper'', NULL, NULL),
    (CAST(2 AS bigint), CAST(1 AS bigint), N''0'', NULL, ''2026-07-21T00:00:00.0000000Z'', N''yyyyMMdd'', NULL, NULL, CAST(1 AS bit), NULL, 2, N''Field'', 8, N''SerialNo'', N''Upper'', NULL, NULL),
    (CAST(3 AS bigint), CAST(1 AS bigint), N''0'', NULL, ''2026-07-21T00:00:00.0000000Z'', N''yyyyMMdd'', NULL, NULL, CAST(0 AS bit), NULL, 3, N''Field'', 8, N''YapCode'', N''Upper'', NULL, NULL),
    (CAST(4 AS bigint), CAST(1 AS bigint), N''0'', NULL, ''2026-07-21T00:00:00.0000000Z'', N''yyyyMMdd'', NULL, NULL, CAST(1 AS bit), NULL, 4, N''Sequence'', 8, NULL, N''None'', NULL, NULL),
    (CAST(5 AS bigint), CAST(2 AS bigint), N''0'', NULL, ''2026-07-21T00:00:00.0000000Z'', N''yyyyMMdd'', NULL, NULL, CAST(1 AS bit), NULL, 1, N''Field'', 8, N''StockCode'', N''Upper'', NULL, NULL),
    (CAST(6 AS bigint), CAST(2 AS bigint), N''0'', NULL, ''2026-07-21T00:00:00.0000000Z'', N''yyyyMMdd'', NULL, NULL, CAST(1 AS bit), NULL, 2, N''Field'', 8, N''LotNo'', N''Upper'', NULL, NULL),
    (CAST(7 AS bigint), CAST(2 AS bigint), N''0'', NULL, ''2026-07-21T00:00:00.0000000Z'', N''yyyyMMdd'', NULL, NULL, CAST(0 AS bit), NULL, 3, N''Field'', 8, N''YapCode'', N''Upper'', NULL, NULL),
    (CAST(8 AS bigint), CAST(2 AS bigint), N''0'', NULL, ''2026-07-21T00:00:00.0000000Z'', N''yyyyMMdd'', NULL, NULL, CAST(1 AS bit), NULL, 4, N''Sequence'', 8, NULL, N''None'', NULL, NULL),
    (CAST(9 AS bigint), CAST(3 AS bigint), N''0'', NULL, ''2026-07-21T00:00:00.0000000Z'', N''yyyyMMdd'', NULL, NULL, CAST(1 AS bit), NULL, 1, N''Field'', 8, N''WarehouseCode'', N''Upper'', NULL, NULL),
    (CAST(10 AS bigint), CAST(3 AS bigint), N''0'', NULL, ''2026-07-21T00:00:00.0000000Z'', N''yyyyMMdd'', NULL, NULL, CAST(1 AS bit), NULL, 2, N''Field'', 8, N''LocationCode'', N''Upper'', NULL, NULL),
    (CAST(11 AS bigint), CAST(3 AS bigint), N''0'', NULL, ''2026-07-21T00:00:00.0000000Z'', N''yyyyMMdd'', NULL, NULL, CAST(1 AS bit), NULL, 3, N''Sequence'', 8, NULL, N''None'', NULL, NULL),
    (CAST(12 AS bigint), CAST(4 AS bigint), N''0'', NULL, ''2026-07-21T00:00:00.0000000Z'', N''yyyyMMdd'', NULL, NULL, CAST(1 AS bit), NULL, 1, N''Field'', 8, N''DocumentNo'', N''Upper'', NULL, NULL),
    (CAST(13 AS bigint), CAST(4 AS bigint), N''0'', NULL, ''2026-07-21T00:00:00.0000000Z'', N''yyyyMMdd'', NULL, NULL, CAST(1 AS bit), NULL, 2, N''Date'', 8, NULL, N''None'', NULL, NULL),
    (CAST(14 AS bigint), CAST(4 AS bigint), N''0'', NULL, ''2026-07-21T00:00:00.0000000Z'', N''yyyyMMdd'', NULL, NULL, CAST(1 AS bit), NULL, 3, N''Sequence'', 8, NULL, N''None'', NULL, NULL),
    (CAST(15 AS bigint), CAST(5 AS bigint), N''0'', NULL, ''2026-07-21T00:00:00.0000000Z'', N''yyyyMMdd'', NULL, NULL, CAST(1 AS bit), NULL, 1, N''Field'', 8, N''DocumentNo'', N''Upper'', NULL, NULL),
    (CAST(16 AS bigint), CAST(5 AS bigint), N''0'', NULL, ''2026-07-21T00:00:00.0000000Z'', N''yyyyMMdd'', NULL, NULL, CAST(1 AS bit), NULL, 2, N''Sequence'', 8, NULL, N''None'', NULL, NULL)');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'BarcodePolicyProfileId', N'BranchCode', N'CreatedBy', N'CreatedDate', N'DateFormat', N'DeletedBy', N'DeletedDate', N'IsRequired', N'LiteralValue', N'Order', N'SegmentType', N'SequenceLength', N'SourceField', N'Transform', N'UpdatedBy', N'UpdatedDate') AND [object_id] = OBJECT_ID(N'[RII_BARCODE_POLICY_PROFILE_SEGMENT]'))
        SET IDENTITY_INSERT [RII_BARCODE_POLICY_PROFILE_SEGMENT] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722153951_RefactorBarcodeRulesToGlobalPolicyAndAddErpAccessPermissions'
)
BEGIN
    EXEC sys.sp_executesql N'UPDATE [RII_GENERATED_BARCODE]
    SET [BarcodePolicyId] = 1,
        [BarcodePolicyProfileId] = 1,
        [Scope] = N''ProductSerial'',
        [PolicyVersion] = 1
    WHERE [BarcodePolicyId] = 0;

    UPDATE [RII_BARCODE_POLICY_PROFILE]
    SET [NextSequence] =
        CASE
            WHEN (SELECT COUNT_BIG(1) + 1 FROM [RII_GENERATED_BARCODE] WHERE [BarcodePolicyProfileId] = 1) > [NextSequence]
            THEN (SELECT COUNT_BIG(1) + 1 FROM [RII_GENERATED_BARCODE] WHERE [BarcodePolicyProfileId] = 1)
            ELSE [NextSequence]
        END
    WHERE [Id] = 1;';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722153951_RefactorBarcodeRulesToGlobalPolicyAndAddErpAccessPermissions'
)
BEGIN
    EXEC sys.sp_executesql N'CREATE INDEX [IX_RII_GENERATED_BARCODE_BarcodePolicyProfileId] ON [RII_GENERATED_BARCODE] ([BarcodePolicyProfileId]);';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722153951_RefactorBarcodeRulesToGlobalPolicyAndAddErpAccessPermissions'
)
BEGIN
    EXEC sys.sp_executesql N'CREATE UNIQUE INDEX [UX_RII_GENERATED_BARCODE_IDEMPOTENCY] ON [RII_GENERATED_BARCODE] ([BarcodePolicyId], [Scope], [IdempotencyHash]);';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722153951_RefactorBarcodeRulesToGlobalPolicyAndAddErpAccessPermissions'
)
BEGIN
    CREATE INDEX [IX_RII_BARCODE_POLICY_IsDeleted] ON [RII_BARCODE_POLICY] ([IsDeleted]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722153951_RefactorBarcodeRulesToGlobalPolicyAndAddErpAccessPermissions'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_RII_BARCODE_POLICY_BRANCH_KEY] ON [RII_BARCODE_POLICY] ([BranchCode], [PolicyKey]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722153951_RefactorBarcodeRulesToGlobalPolicyAndAddErpAccessPermissions'
)
BEGIN
    CREATE INDEX [IX_RII_BARCODE_POLICY_PROFILE_IsDeleted] ON [RII_BARCODE_POLICY_PROFILE] ([IsDeleted]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722153951_RefactorBarcodeRulesToGlobalPolicyAndAddErpAccessPermissions'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_RII_BARCODE_POLICY_PROFILE_SCOPE] ON [RII_BARCODE_POLICY_PROFILE] ([BarcodePolicyId], [Scope]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722153951_RefactorBarcodeRulesToGlobalPolicyAndAddErpAccessPermissions'
)
BEGIN
    CREATE INDEX [IX_RII_BARCODE_POLICY_PROFILE_SEGMENT_IsDeleted] ON [RII_BARCODE_POLICY_PROFILE_SEGMENT] ([IsDeleted]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722153951_RefactorBarcodeRulesToGlobalPolicyAndAddErpAccessPermissions'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_RII_BARCODE_POLICY_PROFILE_SEGMENT_ORDER] ON [RII_BARCODE_POLICY_PROFILE_SEGMENT] ([BarcodePolicyProfileId], [Order]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722153951_RefactorBarcodeRulesToGlobalPolicyAndAddErpAccessPermissions'
)
BEGIN
    ALTER TABLE [RII_GENERATED_BARCODE] ADD CONSTRAINT [FK_RII_GENERATED_BARCODE_RII_BARCODE_POLICY_BarcodePolicyId] FOREIGN KEY ([BarcodePolicyId]) REFERENCES [RII_BARCODE_POLICY] ([Id]) ON DELETE NO ACTION;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722153951_RefactorBarcodeRulesToGlobalPolicyAndAddErpAccessPermissions'
)
BEGIN
    ALTER TABLE [RII_GENERATED_BARCODE] ADD CONSTRAINT [FK_RII_GENERATED_BARCODE_RII_BARCODE_POLICY_PROFILE_BarcodePolicyProfileId] FOREIGN KEY ([BarcodePolicyProfileId]) REFERENCES [RII_BARCODE_POLICY_PROFILE] ([Id]) ON DELETE NO ACTION;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722153951_RefactorBarcodeRulesToGlobalPolicyAndAddErpAccessPermissions'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260722153951_RefactorBarcodeRulesToGlobalPolicyAndAddErpAccessPermissions', N'10.0.10');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722160817_AddIdentitySecuritySessions'
)
BEGIN
    DECLARE @var1 nvarchar(max);
    SELECT @var1 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[RII_USERS]') AND [c].[name] = N'RefreshToken');
    IF @var1 IS NOT NULL EXEC(N'ALTER TABLE [RII_USERS] DROP CONSTRAINT ' + @var1 + ';');
    ALTER TABLE [RII_USERS] DROP COLUMN [RefreshToken];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722160817_AddIdentitySecuritySessions'
)
BEGIN
    DECLARE @var2 nvarchar(max);
    SELECT @var2 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[RII_USERS]') AND [c].[name] = N'RefreshTokenExpiresAt');
    IF @var2 IS NOT NULL EXEC(N'ALTER TABLE [RII_USERS] DROP CONSTRAINT ' + @var2 + ';');
    ALTER TABLE [RII_USERS] DROP COLUMN [RefreshTokenExpiresAt];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722160817_AddIdentitySecuritySessions'
)
BEGIN
    ALTER TABLE [RII_USERS] ADD [TokenVersion] int NOT NULL DEFAULT 1;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722160817_AddIdentitySecuritySessions'
)
BEGIN
    CREATE TABLE [RII_PASSWORD_RESET_TOKENS] (
        [Id] bigint NOT NULL IDENTITY,
        [UserId] bigint NOT NULL,
        [TokenHash] nchar(64) NOT NULL,
        [ExpiresAt] datetime2 NOT NULL,
        [ConsumedAt] datetime2 NULL,
        [RequestedByIp] nvarchar(64) NULL,
        [RowVersion] rowversion NOT NULL,
        [BranchCode] nvarchar(10) NOT NULL DEFAULT N'0',
        [CreatedDate] datetime2 NULL,
        [UpdatedDate] datetime2 NULL,
        [DeletedDate] datetime2 NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [CreatedBy] bigint NULL,
        [UpdatedBy] bigint NULL,
        [DeletedBy] bigint NULL,
        CONSTRAINT [PK_RII_PASSWORD_RESET_TOKENS] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_RII_PASSWORD_RESET_TOKENS_RII_USERS_UserId] FOREIGN KEY ([UserId]) REFERENCES [RII_USERS] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722160817_AddIdentitySecuritySessions'
)
BEGIN
    CREATE TABLE [RII_REFRESH_TOKEN_SESSIONS] (
        [Id] bigint NOT NULL IDENTITY,
        [UserId] bigint NOT NULL,
        [FamilyId] uniqueidentifier NOT NULL,
        [TokenHash] nchar(64) NOT NULL,
        [ExpiresAt] datetime2 NOT NULL,
        [RevokedAt] datetime2 NULL,
        [RevokedReason] nvarchar(100) NULL,
        [ReplacedByTokenHash] nchar(64) NULL,
        [CreatedByIp] nvarchar(64) NULL,
        [RevokedByIp] nvarchar(64) NULL,
        [UserAgent] nvarchar(500) NULL,
        [RowVersion] rowversion NOT NULL,
        [BranchCode] nvarchar(10) NOT NULL DEFAULT N'0',
        [CreatedDate] datetime2 NULL,
        [UpdatedDate] datetime2 NULL,
        [DeletedDate] datetime2 NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [CreatedBy] bigint NULL,
        [UpdatedBy] bigint NULL,
        [DeletedBy] bigint NULL,
        CONSTRAINT [PK_RII_REFRESH_TOKEN_SESSIONS] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_RII_REFRESH_TOKEN_SESSIONS_RII_USERS_UserId] FOREIGN KEY ([UserId]) REFERENCES [RII_USERS] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722160817_AddIdentitySecuritySessions'
)
BEGIN
    EXEC(N'UPDATE [RII_USERS] SET [TokenVersion] = 1
    WHERE [Id] = CAST(1 AS bigint);
    SELECT @@ROWCOUNT');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722160817_AddIdentitySecuritySessions'
)
BEGIN
    CREATE INDEX [IX_RII_PASSWORD_RESET_TOKENS_IsDeleted] ON [RII_PASSWORD_RESET_TOKENS] ([IsDeleted]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722160817_AddIdentitySecuritySessions'
)
BEGIN
    CREATE UNIQUE INDEX [IX_RII_PASSWORD_RESET_TOKENS_TokenHash] ON [RII_PASSWORD_RESET_TOKENS] ([TokenHash]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722160817_AddIdentitySecuritySessions'
)
BEGIN
    CREATE INDEX [IX_RII_PASSWORD_RESET_TOKENS_UserId_ExpiresAt] ON [RII_PASSWORD_RESET_TOKENS] ([UserId], [ExpiresAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722160817_AddIdentitySecuritySessions'
)
BEGIN
    CREATE INDEX [IX_RII_REFRESH_TOKEN_SESSIONS_ExpiresAt] ON [RII_REFRESH_TOKEN_SESSIONS] ([ExpiresAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722160817_AddIdentitySecuritySessions'
)
BEGIN
    CREATE INDEX [IX_RII_REFRESH_TOKEN_SESSIONS_IsDeleted] ON [RII_REFRESH_TOKEN_SESSIONS] ([IsDeleted]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722160817_AddIdentitySecuritySessions'
)
BEGIN
    CREATE UNIQUE INDEX [IX_RII_REFRESH_TOKEN_SESSIONS_TokenHash] ON [RII_REFRESH_TOKEN_SESSIONS] ([TokenHash]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722160817_AddIdentitySecuritySessions'
)
BEGIN
    CREATE INDEX [IX_RII_REFRESH_TOKEN_SESSIONS_UserId_FamilyId] ON [RII_REFRESH_TOKEN_SESSIONS] ([UserId], [FamilyId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722160817_AddIdentitySecuritySessions'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260722160817_AddIdentitySecuritySessions', N'10.0.10');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722171653_AddGoodsReceiptCore'
)
BEGIN
    CREATE TABLE [RII_GR_HEADER] (
        [Id] bigint NOT NULL IDENTITY,
        [DocumentSeriesId] bigint NOT NULL,
        [DocumentNo] nvarchar(50) NOT NULL,
        [DocumentDate] date NOT NULL,
        [ReceiptType] nvarchar(30) NOT NULL,
        [SourceSystem] nvarchar(30) NOT NULL,
        [ExternalReferenceNo] nvarchar(100) NULL,
        [CorrelationId] uniqueidentifier NOT NULL,
        [SupplierId] bigint NULL,
        [SupplierCodeSnapshot] nvarchar(50) NULL,
        [SupplierNameSnapshot] nvarchar(200) NULL,
        [SupplierTaxNoSnapshot] nvarchar(20) NULL,
        [TargetWarehouseId] bigint NOT NULL,
        [ReceivingLocationId] bigint NOT NULL,
        [DefaultPutawayZoneId] bigint NULL,
        [QualityLocationId] bigint NULL,
        [QuarantineLocationId] bigint NULL,
        [Status] nvarchar(30) NOT NULL,
        [ApprovalStatus] nvarchar(30) NOT NULL,
        [QualityStatus] nvarchar(30) NOT NULL,
        [PutawayStatus] nvarchar(30) NOT NULL,
        [ErpIntegrationStatus] nvarchar(30) NOT NULL,
        [PlannedArrivalAtUtc] datetimeoffset(7) NULL,
        [ActualArrivalAtUtc] datetimeoffset(7) NULL,
        [ReleasedAtUtc] datetimeoffset(7) NULL,
        [ReleasedBy] bigint NULL,
        [StartedAtUtc] datetimeoffset(7) NULL,
        [StartedBy] bigint NULL,
        [ReceivedAtUtc] datetimeoffset(7) NULL,
        [ReceivedBy] bigint NULL,
        [CompletedAtUtc] datetimeoffset(7) NULL,
        [CompletedBy] bigint NULL,
        [CancelledAtUtc] datetimeoffset(7) NULL,
        [CancelledBy] bigint NULL,
        [CancellationReason] nvarchar(500) NULL,
        [WaybillNo] nvarchar(50) NULL,
        [WaybillDate] date NULL,
        [ElectronicWaybillNo] nvarchar(50) NULL,
        [ShipmentReferenceNo] nvarchar(100) NULL,
        [CarrierCode] nvarchar(50) NULL,
        [CarrierName] nvarchar(200) NULL,
        [VehiclePlate] nvarchar(20) NULL,
        [TrailerPlate] nvarchar(20) NULL,
        [DriverName] nvarchar(150) NULL,
        [SealNo] nvarchar(50) NULL,
        [AllowOverReceipt] bit NOT NULL,
        [OverReceiptTolerancePercent] decimal(9,4) NOT NULL,
        [AllowUnderReceipt] bit NOT NULL,
        [RequireShortCloseApproval] bit NOT NULL,
        [RequireQualityControl] bit NOT NULL,
        [RequirePutaway] bit NOT NULL,
        [RequireHandlingUnit] bit NOT NULL,
        [Priority] tinyint NOT NULL DEFAULT CAST(3 AS tinyint),
        [Description] nvarchar(1000) NULL,
        [RowVersion] rowversion NOT NULL,
        [BranchCode] nvarchar(10) NOT NULL DEFAULT N'0',
        [CreatedDate] datetime2 NULL,
        [UpdatedDate] datetime2 NULL,
        [DeletedDate] datetime2 NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [CreatedBy] bigint NULL,
        [UpdatedBy] bigint NULL,
        [DeletedBy] bigint NULL,
        CONSTRAINT [PK_RII_GR_HEADER] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_RII_GR_HEADER_OVER_TOLERANCE] CHECK ([OverReceiptTolerancePercent] BETWEEN 0 AND 100),
        CONSTRAINT [CK_RII_GR_HEADER_PRIORITY] CHECK ([Priority] BETWEEN 1 AND 5),
        CONSTRAINT [FK_RII_GR_HEADER_RII_CUSTOMER_SupplierId] FOREIGN KEY ([SupplierId]) REFERENCES [RII_CUSTOMER] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_RII_GR_HEADER_RII_DOCUMENT_SERIES_DocumentSeriesId] FOREIGN KEY ([DocumentSeriesId]) REFERENCES [RII_DOCUMENT_SERIES] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_RII_GR_HEADER_RII_LOCATION_QualityLocationId] FOREIGN KEY ([QualityLocationId]) REFERENCES [RII_LOCATION] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_RII_GR_HEADER_RII_LOCATION_QuarantineLocationId] FOREIGN KEY ([QuarantineLocationId]) REFERENCES [RII_LOCATION] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_RII_GR_HEADER_RII_LOCATION_ReceivingLocationId] FOREIGN KEY ([ReceivingLocationId]) REFERENCES [RII_LOCATION] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_RII_GR_HEADER_RII_WAREHOUSE_TargetWarehouseId] FOREIGN KEY ([TargetWarehouseId]) REFERENCES [RII_WAREHOUSE] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722171653_AddGoodsReceiptCore'
)
BEGIN
    CREATE TABLE [RII_GR_LINE] (
        [Id] bigint NOT NULL IDENTITY,
        [GrHeaderId] bigint NOT NULL,
        [LineNo] int NOT NULL,
        [StockId] bigint NOT NULL,
        [StockCodeSnapshot] nvarchar(50) NOT NULL,
        [StockNameSnapshot] nvarchar(250) NULL,
        [YapCodeId] bigint NULL,
        [YapCodeSnapshot] nvarchar(50) NULL,
        [UnitCode] nvarchar(20) NOT NULL,
        [BaseUnitCode] nvarchar(20) NOT NULL,
        [UnitConversionFactor] decimal(18,8) NOT NULL,
        [ExpectedQuantity] decimal(18,6) NOT NULL,
        [ReceivedQuantity] decimal(18,6) NOT NULL,
        [AcceptedQuantity] decimal(18,6) NOT NULL,
        [RejectedQuantity] decimal(18,6) NOT NULL,
        [QuarantineQuantity] decimal(18,6) NOT NULL,
        [PutawayQuantity] decimal(18,6) NOT NULL,
        [ShortClosedQuantity] decimal(18,6) NOT NULL,
        [TrackingType] nvarchar(30) NOT NULL,
        [RequireLot] bit NOT NULL,
        [RequireSerial] bit NOT NULL,
        [RequireManufacturingDate] bit NOT NULL,
        [RequireExpirationDate] bit NOT NULL,
        [MinimumShelfLifeDays] int NULL,
        [RequireQualityControl] bit NOT NULL,
        [RequireHandlingUnit] bit NOT NULL,
        [Status] nvarchar(30) NOT NULL,
        [AllowOverReceipt] bit NOT NULL,
        [OverReceiptTolerancePercent] decimal(9,4) NOT NULL,
        [AllowUnderReceipt] bit NOT NULL,
        [DefaultReceivingLocationId] bigint NULL,
        [DefaultPutawayLocationId] bigint NULL,
        [Description] nvarchar(500) NULL,
        [RowVersion] rowversion NOT NULL,
        [BranchCode] nvarchar(10) NOT NULL DEFAULT N'0',
        [CreatedDate] datetime2 NULL,
        [UpdatedDate] datetime2 NULL,
        [DeletedDate] datetime2 NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [CreatedBy] bigint NULL,
        [UpdatedBy] bigint NULL,
        [DeletedBy] bigint NULL,
        CONSTRAINT [PK_RII_GR_LINE] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_RII_GR_LINE_LINE_NO] CHECK ([LineNo] > 0),
        CONSTRAINT [CK_RII_GR_LINE_OVER_TOLERANCE] CHECK ([OverReceiptTolerancePercent] BETWEEN 0 AND 100),
        CONSTRAINT [CK_RII_GR_LINE_PUTAWAY_TOTAL] CHECK ([PutawayQuantity] <= [AcceptedQuantity]),
        CONSTRAINT [CK_RII_GR_LINE_QUALITY_TOTAL] CHECK ([AcceptedQuantity] + [RejectedQuantity] + [QuarantineQuantity] <= [ReceivedQuantity]),
        CONSTRAINT [CK_RII_GR_LINE_QUANTITIES_NONNEGATIVE] CHECK ([ExpectedQuantity] >= 0 AND [ReceivedQuantity] >= 0 AND [AcceptedQuantity] >= 0 AND [RejectedQuantity] >= 0 AND [QuarantineQuantity] >= 0 AND [PutawayQuantity] >= 0 AND [ShortClosedQuantity] >= 0),
        CONSTRAINT [CK_RII_GR_LINE_UNIT_FACTOR] CHECK ([UnitConversionFactor] > 0),
        CONSTRAINT [FK_RII_GR_LINE_RII_GR_HEADER_GrHeaderId] FOREIGN KEY ([GrHeaderId]) REFERENCES [RII_GR_HEADER] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_RII_GR_LINE_RII_LOCATION_DefaultPutawayLocationId] FOREIGN KEY ([DefaultPutawayLocationId]) REFERENCES [RII_LOCATION] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_RII_GR_LINE_RII_LOCATION_DefaultReceivingLocationId] FOREIGN KEY ([DefaultReceivingLocationId]) REFERENCES [RII_LOCATION] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_RII_GR_LINE_RII_STOCK_StockId] FOREIGN KEY ([StockId]) REFERENCES [RII_STOCK] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_RII_GR_LINE_RII_YAP_CODE_YapCodeId] FOREIGN KEY ([YapCodeId]) REFERENCES [RII_YAP_CODE] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722171653_AddGoodsReceiptCore'
)
BEGIN
    CREATE TABLE [RII_GR_SOURCE_DOCUMENT] (
        [Id] bigint NOT NULL IDENTITY,
        [GrHeaderId] bigint NOT NULL,
        [SourceDocumentType] nvarchar(30) NOT NULL,
        [SourceSystem] nvarchar(30) NOT NULL,
        [ExternalDocumentId] nvarchar(100) NULL,
        [ExternalDocumentNo] nvarchar(50) NOT NULL,
        [ExternalDocumentDate] date NULL,
        [SupplierCodeSnapshot] nvarchar(50) NULL,
        [SupplierNameSnapshot] nvarchar(200) NULL,
        [CurrencyCode] varchar(3) NULL,
        [LastSynchronizedAtUtc] datetimeoffset(7) NULL,
        [ExternalVersion] nvarchar(100) NULL,
        [ExternalStatus] nvarchar(30) NULL,
        [BranchCode] nvarchar(10) NOT NULL DEFAULT N'0',
        [CreatedDate] datetime2 NULL,
        [UpdatedDate] datetime2 NULL,
        [DeletedDate] datetime2 NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [CreatedBy] bigint NULL,
        [UpdatedBy] bigint NULL,
        [DeletedBy] bigint NULL,
        CONSTRAINT [PK_RII_GR_SOURCE_DOCUMENT] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_RII_GR_SOURCE_DOCUMENT_RII_GR_HEADER_GrHeaderId] FOREIGN KEY ([GrHeaderId]) REFERENCES [RII_GR_HEADER] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722171653_AddGoodsReceiptCore'
)
BEGIN
    CREATE TABLE [RII_GR_STATUS_HISTORY] (
        [Id] bigint NOT NULL IDENTITY,
        [GrHeaderId] bigint NOT NULL,
        [StatusArea] nvarchar(30) NOT NULL,
        [FromStatus] nvarchar(30) NULL,
        [ToStatus] nvarchar(30) NOT NULL,
        [ChangedAtUtc] datetimeoffset(7) NOT NULL,
        [ChangedBy] bigint NULL,
        [ReasonCode] nvarchar(50) NULL,
        [Description] nvarchar(500) NULL,
        [CorrelationId] uniqueidentifier NOT NULL,
        [BranchCode] nvarchar(10) NOT NULL DEFAULT N'0',
        [CreatedDate] datetime2 NULL,
        [UpdatedDate] datetime2 NULL,
        [DeletedDate] datetime2 NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [CreatedBy] bigint NULL,
        [UpdatedBy] bigint NULL,
        [DeletedBy] bigint NULL,
        CONSTRAINT [PK_RII_GR_STATUS_HISTORY] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_RII_GR_STATUS_HISTORY_RII_GR_HEADER_GrHeaderId] FOREIGN KEY ([GrHeaderId]) REFERENCES [RII_GR_HEADER] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722171653_AddGoodsReceiptCore'
)
BEGIN
    CREATE TABLE [RII_GR_LINE_SOURCE] (
        [Id] bigint NOT NULL IDENTITY,
        [GrLineId] bigint NOT NULL,
        [GrSourceDocumentId] bigint NOT NULL,
        [ExternalLineId] nvarchar(100) NOT NULL,
        [ExternalLineNo] int NULL,
        [ExternalStockCode] nvarchar(50) NOT NULL,
        [ExternalYapCode] nvarchar(50) NULL,
        [OrderedQuantity] decimal(18,6) NOT NULL,
        [PreviouslyReceivedQuantity] decimal(18,6) NOT NULL,
        [AllocatedQuantity] decimal(18,6) NOT NULL,
        [ReceivedQuantity] decimal(18,6) NOT NULL,
        [UnitCode] nvarchar(20) NOT NULL,
        [ExternalStatus] nvarchar(30) NULL,
        [BranchCode] nvarchar(10) NOT NULL DEFAULT N'0',
        [CreatedDate] datetime2 NULL,
        [UpdatedDate] datetime2 NULL,
        [DeletedDate] datetime2 NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [CreatedBy] bigint NULL,
        [UpdatedBy] bigint NULL,
        [DeletedBy] bigint NULL,
        CONSTRAINT [PK_RII_GR_LINE_SOURCE] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_RII_GR_LINE_SOURCE_QUANTITIES] CHECK ([OrderedQuantity] >= 0 AND [PreviouslyReceivedQuantity] >= 0 AND [AllocatedQuantity] >= 0 AND [ReceivedQuantity] >= 0),
        CONSTRAINT [FK_RII_GR_LINE_SOURCE_RII_GR_LINE_GrLineId] FOREIGN KEY ([GrLineId]) REFERENCES [RII_GR_LINE] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_RII_GR_LINE_SOURCE_RII_GR_SOURCE_DOCUMENT_GrSourceDocumentId] FOREIGN KEY ([GrSourceDocumentId]) REFERENCES [RII_GR_SOURCE_DOCUMENT] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722171653_AddGoodsReceiptCore'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'AvailableOnMobile', N'AvailableOnWeb', N'BranchCode', N'Code', N'CreatedBy', N'CreatedDate', N'DeletedBy', N'DeletedDate', N'Description', N'IsActive', N'Name', N'UpdatedBy', N'UpdatedDate') AND [object_id] = OBJECT_ID(N'[RII_PERMISSION_DEFINITIONS]'))
        SET IDENTITY_INSERT [RII_PERMISSION_DEFINITIONS] ON;
    EXEC(N'INSERT INTO [RII_PERMISSION_DEFINITIONS] ([Id], [AvailableOnMobile], [AvailableOnWeb], [BranchCode], [Code], [CreatedBy], [CreatedDate], [DeletedBy], [DeletedDate], [Description], [IsActive], [Name], [UpdatedBy], [UpdatedDate])
    VALUES (CAST(1036 AS bigint), CAST(0 AS bit), CAST(1 AS bit), N''0'', N''WMS.GOODS_RECEIPT.VIEW'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, NULL, CAST(1 AS bit), N''Mal kabulleri görüntüle'', NULL, NULL),
    (CAST(1037 AS bigint), CAST(0 AS bit), CAST(1 AS bit), N''0'', N''WMS.GOODS_RECEIPT.CREATE'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, NULL, CAST(1 AS bit), N''Mal kabul oluştur'', NULL, NULL),
    (CAST(1038 AS bigint), CAST(0 AS bit), CAST(1 AS bit), N''0'', N''WMS.GOODS_RECEIPT.UPDATE'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, NULL, CAST(1 AS bit), N''Mal kabul güncelle'', NULL, NULL),
    (CAST(1039 AS bigint), CAST(0 AS bit), CAST(1 AS bit), N''0'', N''WMS.GOODS_RECEIPT.RELEASE'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, NULL, CAST(1 AS bit), N''Mal kabulü işleme aç'', NULL, NULL),
    (CAST(1040 AS bigint), CAST(0 AS bit), CAST(1 AS bit), N''0'', N''WMS.GOODS_RECEIPT.RECEIVE'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, NULL, CAST(1 AS bit), N''Mal kabul işle'', NULL, NULL),
    (CAST(1041 AS bigint), CAST(0 AS bit), CAST(1 AS bit), N''0'', N''WMS.GOODS_RECEIPT.COMPLETE'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, NULL, CAST(1 AS bit), N''Mal kabulü tamamla'', NULL, NULL),
    (CAST(1042 AS bigint), CAST(0 AS bit), CAST(1 AS bit), N''0'', N''WMS.GOODS_RECEIPT.CANCEL'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, NULL, CAST(1 AS bit), N''Mal kabulü iptal et'', NULL, NULL),
    (CAST(1043 AS bigint), CAST(0 AS bit), CAST(1 AS bit), N''0'', N''WMS.GOODS_RECEIPT.ERP_RETRY'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, NULL, CAST(1 AS bit), N''Mal kabul ERP aktarımını yeniden dene'', NULL, NULL)');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'AvailableOnMobile', N'AvailableOnWeb', N'BranchCode', N'Code', N'CreatedBy', N'CreatedDate', N'DeletedBy', N'DeletedDate', N'Description', N'IsActive', N'Name', N'UpdatedBy', N'UpdatedDate') AND [object_id] = OBJECT_ID(N'[RII_PERMISSION_DEFINITIONS]'))
        SET IDENTITY_INSERT [RII_PERMISSION_DEFINITIONS] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722171653_AddGoodsReceiptCore'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'BranchCode', N'CreatedBy', N'CreatedDate', N'DeletedBy', N'DeletedDate', N'PermissionDefinitionId', N'PermissionGroupId', N'UpdatedBy', N'UpdatedDate') AND [object_id] = OBJECT_ID(N'[RII_PERMISSION_GROUP_PERMISSIONS]'))
        SET IDENTITY_INSERT [RII_PERMISSION_GROUP_PERMISSIONS] ON;
    EXEC(N'INSERT INTO [RII_PERMISSION_GROUP_PERMISSIONS] ([Id], [BranchCode], [CreatedBy], [CreatedDate], [DeletedBy], [DeletedDate], [PermissionDefinitionId], [PermissionGroupId], [UpdatedBy], [UpdatedDate])
    VALUES (CAST(1033 AS bigint), N''0'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, CAST(1033 AS bigint), CAST(1001 AS bigint), NULL, NULL),
    (CAST(1034 AS bigint), N''0'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, CAST(1034 AS bigint), CAST(1001 AS bigint), NULL, NULL),
    (CAST(1035 AS bigint), N''0'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, CAST(1035 AS bigint), CAST(1001 AS bigint), NULL, NULL),
    (CAST(1036 AS bigint), N''0'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, CAST(1036 AS bigint), CAST(1001 AS bigint), NULL, NULL),
    (CAST(1037 AS bigint), N''0'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, CAST(1037 AS bigint), CAST(1001 AS bigint), NULL, NULL),
    (CAST(1038 AS bigint), N''0'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, CAST(1038 AS bigint), CAST(1001 AS bigint), NULL, NULL),
    (CAST(1039 AS bigint), N''0'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, CAST(1039 AS bigint), CAST(1001 AS bigint), NULL, NULL),
    (CAST(1040 AS bigint), N''0'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, CAST(1040 AS bigint), CAST(1001 AS bigint), NULL, NULL),
    (CAST(1041 AS bigint), N''0'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, CAST(1041 AS bigint), CAST(1001 AS bigint), NULL, NULL),
    (CAST(1042 AS bigint), N''0'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, CAST(1042 AS bigint), CAST(1001 AS bigint), NULL, NULL),
    (CAST(1043 AS bigint), N''0'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, CAST(1043 AS bigint), CAST(1001 AS bigint), NULL, NULL)');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'BranchCode', N'CreatedBy', N'CreatedDate', N'DeletedBy', N'DeletedDate', N'PermissionDefinitionId', N'PermissionGroupId', N'UpdatedBy', N'UpdatedDate') AND [object_id] = OBJECT_ID(N'[RII_PERMISSION_GROUP_PERMISSIONS]'))
        SET IDENTITY_INSERT [RII_PERMISSION_GROUP_PERMISSIONS] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722171653_AddGoodsReceiptCore'
)
BEGIN
    CREATE INDEX [IX_RII_GR_HEADER_BRANCH_STATUS_PLANNED] ON [RII_GR_HEADER] ([BranchCode], [Status], [PlannedArrivalAtUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722171653_AddGoodsReceiptCore'
)
BEGIN
    CREATE INDEX [IX_RII_GR_HEADER_DocumentSeriesId] ON [RII_GR_HEADER] ([DocumentSeriesId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722171653_AddGoodsReceiptCore'
)
BEGIN
    CREATE INDEX [IX_RII_GR_HEADER_IsDeleted] ON [RII_GR_HEADER] ([IsDeleted]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722171653_AddGoodsReceiptCore'
)
BEGIN
    CREATE INDEX [IX_RII_GR_HEADER_QualityLocationId] ON [RII_GR_HEADER] ([QualityLocationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722171653_AddGoodsReceiptCore'
)
BEGIN
    CREATE INDEX [IX_RII_GR_HEADER_QuarantineLocationId] ON [RII_GR_HEADER] ([QuarantineLocationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722171653_AddGoodsReceiptCore'
)
BEGIN
    CREATE INDEX [IX_RII_GR_HEADER_ReceivingLocationId] ON [RII_GR_HEADER] ([ReceivingLocationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722171653_AddGoodsReceiptCore'
)
BEGIN
    CREATE INDEX [IX_RII_GR_HEADER_SUPPLIER_STATUS] ON [RII_GR_HEADER] ([SupplierId], [Status]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722171653_AddGoodsReceiptCore'
)
BEGIN
    CREATE INDEX [IX_RII_GR_HEADER_WAREHOUSE_STATUS] ON [RII_GR_HEADER] ([TargetWarehouseId], [Status]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722171653_AddGoodsReceiptCore'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_RII_GR_HEADER_BRANCH_DOCUMENT_NO] ON [RII_GR_HEADER] ([BranchCode], [DocumentNo]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722171653_AddGoodsReceiptCore'
)
BEGIN
    CREATE UNIQUE INDEX [UX_RII_GR_HEADER_CORRELATION_ID] ON [RII_GR_HEADER] ([CorrelationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722171653_AddGoodsReceiptCore'
)
BEGIN
    CREATE INDEX [IX_RII_GR_LINE_DefaultPutawayLocationId] ON [RII_GR_LINE] ([DefaultPutawayLocationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722171653_AddGoodsReceiptCore'
)
BEGIN
    CREATE INDEX [IX_RII_GR_LINE_DefaultReceivingLocationId] ON [RII_GR_LINE] ([DefaultReceivingLocationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722171653_AddGoodsReceiptCore'
)
BEGIN
    CREATE INDEX [IX_RII_GR_LINE_IsDeleted] ON [RII_GR_LINE] ([IsDeleted]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722171653_AddGoodsReceiptCore'
)
BEGIN
    CREATE INDEX [IX_RII_GR_LINE_STOCK_YAP_STATUS] ON [RII_GR_LINE] ([StockId], [YapCodeId], [Status]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722171653_AddGoodsReceiptCore'
)
BEGIN
    CREATE INDEX [IX_RII_GR_LINE_YapCodeId] ON [RII_GR_LINE] ([YapCodeId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722171653_AddGoodsReceiptCore'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_RII_GR_LINE_HEADER_LINE_NO] ON [RII_GR_LINE] ([GrHeaderId], [LineNo]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722171653_AddGoodsReceiptCore'
)
BEGIN
    CREATE INDEX [IX_RII_GR_LINE_SOURCE_DOCUMENT] ON [RII_GR_LINE_SOURCE] ([GrSourceDocumentId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722171653_AddGoodsReceiptCore'
)
BEGIN
    CREATE INDEX [IX_RII_GR_LINE_SOURCE_IsDeleted] ON [RII_GR_LINE_SOURCE] ([IsDeleted]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722171653_AddGoodsReceiptCore'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_RII_GR_LINE_SOURCE_EXTERNAL_LINE] ON [RII_GR_LINE_SOURCE] ([GrLineId], [GrSourceDocumentId], [ExternalLineId]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722171653_AddGoodsReceiptCore'
)
BEGIN
    CREATE INDEX [IX_RII_GR_SOURCE_DOCUMENT_HEADER] ON [RII_GR_SOURCE_DOCUMENT] ([GrHeaderId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722171653_AddGoodsReceiptCore'
)
BEGIN
    CREATE INDEX [IX_RII_GR_SOURCE_DOCUMENT_IsDeleted] ON [RII_GR_SOURCE_DOCUMENT] ([IsDeleted]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722171653_AddGoodsReceiptCore'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_RII_GR_SOURCE_DOCUMENT_EXTERNAL] ON [RII_GR_SOURCE_DOCUMENT] ([GrHeaderId], [SourceSystem], [SourceDocumentType], [ExternalDocumentNo]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722171653_AddGoodsReceiptCore'
)
BEGIN
    CREATE INDEX [IX_RII_GR_STATUS_HISTORY_CORRELATION_ID] ON [RII_GR_STATUS_HISTORY] ([CorrelationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722171653_AddGoodsReceiptCore'
)
BEGIN
    CREATE INDEX [IX_RII_GR_STATUS_HISTORY_HEADER_CHANGED_AT] ON [RII_GR_STATUS_HISTORY] ([GrHeaderId], [ChangedAtUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722171653_AddGoodsReceiptCore'
)
BEGIN
    CREATE INDEX [IX_RII_GR_STATUS_HISTORY_IsDeleted] ON [RII_GR_STATUS_HISTORY] ([IsDeleted]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722171653_AddGoodsReceiptCore'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260722171653_AddGoodsReceiptCore', N'10.0.10');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722173435_AddGoodsReceiptWorkflow'
)
BEGIN
    ALTER TABLE [RII_GR_HEADER] ADD [InitiationMode] nvarchar(30) NOT NULL DEFAULT N'OrderBasedTask';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722173435_AddGoodsReceiptWorkflow'
)
BEGIN
    ALTER TABLE [RII_GR_HEADER] ADD [LabelStrategy] nvarchar(30) NOT NULL DEFAULT N'None';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722173435_AddGoodsReceiptWorkflow'
)
BEGIN
    CREATE TABLE [RII_GR_LABEL_BATCH] (
        [Id] bigint NOT NULL IDENTITY,
        [GrHeaderId] bigint NOT NULL,
        [BatchNo] nvarchar(50) NOT NULL,
        [Status] nvarchar(30) NOT NULL,
        [TotalLabelCount] int NOT NULL,
        [PrintedLabelCount] int NOT NULL,
        [ConsumedLabelCount] int NOT NULL,
        [VoidLabelCount] int NOT NULL,
        [LastPrintedAtUtc] datetimeoffset(7) NULL,
        [CompletedAtUtc] datetimeoffset(7) NULL,
        [Description] nvarchar(500) NULL,
        [RowVersion] rowversion NOT NULL,
        [BranchCode] nvarchar(10) NOT NULL DEFAULT N'0',
        [CreatedDate] datetime2 NULL,
        [UpdatedDate] datetime2 NULL,
        [DeletedDate] datetime2 NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [CreatedBy] bigint NULL,
        [UpdatedBy] bigint NULL,
        [DeletedBy] bigint NULL,
        CONSTRAINT [PK_RII_GR_LABEL_BATCH] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_RII_GR_LABEL_BATCH_COUNTS] CHECK ([TotalLabelCount] >= 0 AND [PrintedLabelCount] >= 0 AND [ConsumedLabelCount] >= 0 AND [VoidLabelCount] >= 0 AND [PrintedLabelCount] <= [TotalLabelCount] AND [ConsumedLabelCount] + [VoidLabelCount] <= [TotalLabelCount]),
        CONSTRAINT [FK_RII_GR_LABEL_BATCH_RII_GR_HEADER_GrHeaderId] FOREIGN KEY ([GrHeaderId]) REFERENCES [RII_GR_HEADER] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722173435_AddGoodsReceiptWorkflow'
)
BEGIN
    CREATE TABLE [RII_GR_TASK] (
        [Id] bigint NOT NULL IDENTITY,
        [GrHeaderId] bigint NOT NULL,
        [TaskNo] nvarchar(50) NOT NULL,
        [TaskType] nvarchar(30) NOT NULL,
        [Status] nvarchar(30) NOT NULL,
        [Priority] tinyint NOT NULL DEFAULT CAST(3 AS tinyint),
        [WarehouseId] bigint NOT NULL,
        [ZoneCode] nvarchar(50) NULL,
        [PlannedStartAtUtc] datetimeoffset(7) NULL,
        [DueAtUtc] datetimeoffset(7) NULL,
        [ReleasedAtUtc] datetimeoffset(7) NULL,
        [ReleasedBy] bigint NULL,
        [StartedAtUtc] datetimeoffset(7) NULL,
        [CompletedAtUtc] datetimeoffset(7) NULL,
        [CancelledAtUtc] datetimeoffset(7) NULL,
        [CancellationReason] nvarchar(500) NULL,
        [Description] nvarchar(500) NULL,
        [RowVersion] rowversion NOT NULL,
        [BranchCode] nvarchar(10) NOT NULL DEFAULT N'0',
        [CreatedDate] datetime2 NULL,
        [UpdatedDate] datetime2 NULL,
        [DeletedDate] datetime2 NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [CreatedBy] bigint NULL,
        [UpdatedBy] bigint NULL,
        [DeletedBy] bigint NULL,
        CONSTRAINT [PK_RII_GR_TASK] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_RII_GR_TASK_PRIORITY] CHECK ([Priority] BETWEEN 1 AND 5),
        CONSTRAINT [FK_RII_GR_TASK_RII_GR_HEADER_GrHeaderId] FOREIGN KEY ([GrHeaderId]) REFERENCES [RII_GR_HEADER] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_RII_GR_TASK_RII_WAREHOUSE_WarehouseId] FOREIGN KEY ([WarehouseId]) REFERENCES [RII_WAREHOUSE] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722173435_AddGoodsReceiptWorkflow'
)
BEGIN
    CREATE TABLE [RII_GR_TASK_ASSIGNMENT] (
        [Id] bigint NOT NULL IDENTITY,
        [GrTaskId] bigint NOT NULL,
        [UserId] bigint NOT NULL,
        [AssignmentRole] nvarchar(30) NOT NULL,
        [Status] nvarchar(30) NOT NULL,
        [AssignedAtUtc] datetimeoffset(7) NOT NULL,
        [AssignedBy] bigint NULL,
        [AcceptedAtUtc] datetimeoffset(7) NULL,
        [StartedAtUtc] datetimeoffset(7) NULL,
        [CompletedAtUtc] datetimeoffset(7) NULL,
        [UnassignedAtUtc] datetimeoffset(7) NULL,
        [UnassignedReason] nvarchar(500) NULL,
        [RowVersion] rowversion NOT NULL,
        [BranchCode] nvarchar(10) NOT NULL DEFAULT N'0',
        [CreatedDate] datetime2 NULL,
        [UpdatedDate] datetime2 NULL,
        [DeletedDate] datetime2 NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [CreatedBy] bigint NULL,
        [UpdatedBy] bigint NULL,
        [DeletedBy] bigint NULL,
        CONSTRAINT [PK_RII_GR_TASK_ASSIGNMENT] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_RII_GR_TASK_ASSIGNMENT_RII_GR_TASK_GrTaskId] FOREIGN KEY ([GrTaskId]) REFERENCES [RII_GR_TASK] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_RII_GR_TASK_ASSIGNMENT_RII_USERS_UserId] FOREIGN KEY ([UserId]) REFERENCES [RII_USERS] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722173435_AddGoodsReceiptWorkflow'
)
BEGIN
    CREATE TABLE [RII_GR_TASK_LINE] (
        [Id] bigint NOT NULL IDENTITY,
        [GrTaskId] bigint NOT NULL,
        [GrLineId] bigint NOT NULL,
        [SequenceNo] int NOT NULL,
        [FromLocationId] bigint NULL,
        [ToLocationId] bigint NULL,
        [HandlingUnitId] bigint NULL,
        [PlannedQuantity] decimal(18,6) NOT NULL,
        [ProcessedQuantity] decimal(18,6) NOT NULL,
        [UnitCode] nvarchar(20) NOT NULL,
        [Status] nvarchar(30) NOT NULL,
        [Description] nvarchar(500) NULL,
        [RowVersion] rowversion NOT NULL,
        [BranchCode] nvarchar(10) NOT NULL DEFAULT N'0',
        [CreatedDate] datetime2 NULL,
        [UpdatedDate] datetime2 NULL,
        [DeletedDate] datetime2 NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [CreatedBy] bigint NULL,
        [UpdatedBy] bigint NULL,
        [DeletedBy] bigint NULL,
        CONSTRAINT [PK_RII_GR_TASK_LINE] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_RII_GR_TASK_LINE_QUANTITY] CHECK ([PlannedQuantity] > 0 AND [ProcessedQuantity] >= 0 AND [ProcessedQuantity] <= [PlannedQuantity]),
        CONSTRAINT [CK_RII_GR_TASK_LINE_SEQUENCE] CHECK ([SequenceNo] > 0),
        CONSTRAINT [FK_RII_GR_TASK_LINE_RII_GR_LINE_GrLineId] FOREIGN KEY ([GrLineId]) REFERENCES [RII_GR_LINE] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_RII_GR_TASK_LINE_RII_GR_TASK_GrTaskId] FOREIGN KEY ([GrTaskId]) REFERENCES [RII_GR_TASK] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_RII_GR_TASK_LINE_RII_LOCATION_FromLocationId] FOREIGN KEY ([FromLocationId]) REFERENCES [RII_LOCATION] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_RII_GR_TASK_LINE_RII_LOCATION_ToLocationId] FOREIGN KEY ([ToLocationId]) REFERENCES [RII_LOCATION] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722173435_AddGoodsReceiptWorkflow'
)
BEGIN
    CREATE TABLE [RII_GR_LABEL] (
        [Id] bigint NOT NULL IDENTITY,
        [BatchId] bigint NOT NULL,
        [GrHeaderId] bigint NOT NULL,
        [GrLineId] bigint NULL,
        [GrTaskLineId] bigint NULL,
        [StockId] bigint NULL,
        [StockCodeSnapshot] nvarchar(50) NOT NULL,
        [StockNameSnapshot] nvarchar(250) NULL,
        [YapCodeId] bigint NULL,
        [YapCodeSnapshot] nvarchar(50) NULL,
        [LabelQuantity] decimal(18,6) NOT NULL,
        [UnitCode] nvarchar(20) NOT NULL,
        [LotNo] nvarchar(100) NULL,
        [SerialNo] nvarchar(100) NULL,
        [ManufacturingDate] date NULL,
        [ExpirationDate] date NULL,
        [BarcodeValue] nvarchar(200) NOT NULL,
        [Status] nvarchar(30) NOT NULL,
        [PrintCount] int NOT NULL,
        [LastPrintedAtUtc] datetimeoffset(7) NULL,
        [AssignedAtUtc] datetimeoffset(7) NULL,
        [ConsumedAtUtc] datetimeoffset(7) NULL,
        [VoidReason] nvarchar(500) NULL,
        [Description] nvarchar(500) NULL,
        [RowVersion] rowversion NOT NULL,
        [BranchCode] nvarchar(10) NOT NULL DEFAULT N'0',
        [CreatedDate] datetime2 NULL,
        [UpdatedDate] datetime2 NULL,
        [DeletedDate] datetime2 NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [CreatedBy] bigint NULL,
        [UpdatedBy] bigint NULL,
        [DeletedBy] bigint NULL,
        CONSTRAINT [PK_RII_GR_LABEL] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_RII_GR_LABEL_PRINT_COUNT] CHECK ([PrintCount] >= 0),
        CONSTRAINT [CK_RII_GR_LABEL_QUANTITY] CHECK ([LabelQuantity] > 0),
        CONSTRAINT [FK_RII_GR_LABEL_RII_GR_HEADER_GrHeaderId] FOREIGN KEY ([GrHeaderId]) REFERENCES [RII_GR_HEADER] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_RII_GR_LABEL_RII_GR_LABEL_BATCH_BatchId] FOREIGN KEY ([BatchId]) REFERENCES [RII_GR_LABEL_BATCH] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_RII_GR_LABEL_RII_GR_LINE_GrLineId] FOREIGN KEY ([GrLineId]) REFERENCES [RII_GR_LINE] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_RII_GR_LABEL_RII_GR_TASK_LINE_GrTaskLineId] FOREIGN KEY ([GrTaskLineId]) REFERENCES [RII_GR_TASK_LINE] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_RII_GR_LABEL_RII_STOCK_StockId] FOREIGN KEY ([StockId]) REFERENCES [RII_STOCK] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_RII_GR_LABEL_RII_YAP_CODE_YapCodeId] FOREIGN KEY ([YapCodeId]) REFERENCES [RII_YAP_CODE] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722173435_AddGoodsReceiptWorkflow'
)
BEGIN
    CREATE INDEX [IX_RII_GR_LABEL_BATCH_STATUS] ON [RII_GR_LABEL] ([BatchId], [Status]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722173435_AddGoodsReceiptWorkflow'
)
BEGIN
    CREATE INDEX [IX_RII_GR_LABEL_GrLineId] ON [RII_GR_LABEL] ([GrLineId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722173435_AddGoodsReceiptWorkflow'
)
BEGIN
    CREATE INDEX [IX_RII_GR_LABEL_GrTaskLineId] ON [RII_GR_LABEL] ([GrTaskLineId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722173435_AddGoodsReceiptWorkflow'
)
BEGIN
    CREATE INDEX [IX_RII_GR_LABEL_HEADER_LINE] ON [RII_GR_LABEL] ([GrHeaderId], [GrLineId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722173435_AddGoodsReceiptWorkflow'
)
BEGIN
    CREATE INDEX [IX_RII_GR_LABEL_IsDeleted] ON [RII_GR_LABEL] ([IsDeleted]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722173435_AddGoodsReceiptWorkflow'
)
BEGIN
    CREATE INDEX [IX_RII_GR_LABEL_TRACE] ON [RII_GR_LABEL] ([StockId], [LotNo], [SerialNo]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722173435_AddGoodsReceiptWorkflow'
)
BEGIN
    CREATE INDEX [IX_RII_GR_LABEL_YapCodeId] ON [RII_GR_LABEL] ([YapCodeId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722173435_AddGoodsReceiptWorkflow'
)
BEGIN
    CREATE UNIQUE INDEX [UX_RII_GR_LABEL_BARCODE] ON [RII_GR_LABEL] ([BarcodeValue]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722173435_AddGoodsReceiptWorkflow'
)
BEGIN
    CREATE INDEX [IX_RII_GR_LABEL_BATCH_HEADER_STATUS] ON [RII_GR_LABEL_BATCH] ([GrHeaderId], [Status]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722173435_AddGoodsReceiptWorkflow'
)
BEGIN
    CREATE INDEX [IX_RII_GR_LABEL_BATCH_IsDeleted] ON [RII_GR_LABEL_BATCH] ([IsDeleted]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722173435_AddGoodsReceiptWorkflow'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_RII_GR_LABEL_BATCH_BRANCH_BATCH_NO] ON [RII_GR_LABEL_BATCH] ([BranchCode], [BatchNo]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722173435_AddGoodsReceiptWorkflow'
)
BEGIN
    CREATE INDEX [IX_RII_GR_TASK_HEADER_TYPE_STATUS] ON [RII_GR_TASK] ([GrHeaderId], [TaskType], [Status]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722173435_AddGoodsReceiptWorkflow'
)
BEGIN
    CREATE INDEX [IX_RII_GR_TASK_IsDeleted] ON [RII_GR_TASK] ([IsDeleted]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722173435_AddGoodsReceiptWorkflow'
)
BEGIN
    CREATE INDEX [IX_RII_GR_TASK_WORK_QUEUE] ON [RII_GR_TASK] ([WarehouseId], [Status], [Priority], [DueAtUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722173435_AddGoodsReceiptWorkflow'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_RII_GR_TASK_BRANCH_TASK_NO] ON [RII_GR_TASK] ([BranchCode], [TaskNo]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722173435_AddGoodsReceiptWorkflow'
)
BEGIN
    CREATE INDEX [IX_RII_GR_TASK_ASSIGNMENT_IsDeleted] ON [RII_GR_TASK_ASSIGNMENT] ([IsDeleted]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722173435_AddGoodsReceiptWorkflow'
)
BEGIN
    CREATE INDEX [IX_RII_GR_TASK_ASSIGNMENT_USER_QUEUE] ON [RII_GR_TASK_ASSIGNMENT] ([UserId], [Status], [AssignedAtUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722173435_AddGoodsReceiptWorkflow'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_RII_GR_TASK_ASSIGNMENT_ACTIVE_USER] ON [RII_GR_TASK_ASSIGNMENT] ([GrTaskId], [UserId]) WHERE [IsDeleted] = 0 AND [Status] <> N''Unassigned''');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722173435_AddGoodsReceiptWorkflow'
)
BEGIN
    CREATE INDEX [IX_RII_GR_TASK_LINE_FromLocationId] ON [RII_GR_TASK_LINE] ([FromLocationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722173435_AddGoodsReceiptWorkflow'
)
BEGIN
    CREATE INDEX [IX_RII_GR_TASK_LINE_GR_LINE_STATUS] ON [RII_GR_TASK_LINE] ([GrLineId], [Status]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722173435_AddGoodsReceiptWorkflow'
)
BEGIN
    CREATE INDEX [IX_RII_GR_TASK_LINE_IsDeleted] ON [RII_GR_TASK_LINE] ([IsDeleted]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722173435_AddGoodsReceiptWorkflow'
)
BEGIN
    CREATE INDEX [IX_RII_GR_TASK_LINE_ToLocationId] ON [RII_GR_TASK_LINE] ([ToLocationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722173435_AddGoodsReceiptWorkflow'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_RII_GR_TASK_LINE_TASK_SEQUENCE] ON [RII_GR_TASK_LINE] ([GrTaskId], [SequenceNo]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722173435_AddGoodsReceiptWorkflow'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260722173435_AddGoodsReceiptWorkflow', N'10.0.10');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722174441_RefineGoodsReceiptAndAddNetsisOpenOrders'
)
BEGIN
    ALTER TABLE [RII_GR_TASK_LINE] DROP CONSTRAINT [CK_RII_GR_TASK_LINE_QUANTITY];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722174441_RefineGoodsReceiptAndAddNetsisOpenOrders'
)
BEGIN
    DROP INDEX [UX_RII_GR_TASK_ASSIGNMENT_ACTIVE_USER] ON [RII_GR_TASK_ASSIGNMENT];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722174441_RefineGoodsReceiptAndAddNetsisOpenOrders'
)
BEGIN
    DECLARE @var3 nvarchar(max);
    SELECT @var3 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[RII_GR_HEADER]') AND [c].[name] = N'DefaultPutawayZoneId');
    IF @var3 IS NOT NULL EXEC(N'ALTER TABLE [RII_GR_HEADER] DROP CONSTRAINT ' + @var3 + ';');
    ALTER TABLE [RII_GR_HEADER] DROP COLUMN [DefaultPutawayZoneId];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722174441_RefineGoodsReceiptAndAddNetsisOpenOrders'
)
BEGIN
    ALTER TABLE [RII_GR_HEADER] ADD [DefaultPutawayZoneCode] nvarchar(50) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722174441_RefineGoodsReceiptAndAddNetsisOpenOrders'
)
BEGIN
    EXEC(N'ALTER TABLE [RII_GR_TASK_LINE] ADD CONSTRAINT [CK_RII_GR_TASK_LINE_QUANTITY] CHECK ([PlannedQuantity] > 0 AND [ProcessedQuantity] >= 0)');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722174441_RefineGoodsReceiptAndAddNetsisOpenOrders'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_RII_GR_TASK_ASSIGNMENT_ACTIVE_USER] ON [RII_GR_TASK_ASSIGNMENT] ([GrTaskId], [UserId]) WHERE [IsDeleted] = 0 AND [Status] <> N''Unassigned'' AND [Status] <> N''Rejected''');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722174441_RefineGoodsReceiptAndAddNetsisOpenOrders'
)
BEGIN
    IF OBJECT_ID(N'dbo.RII_FN_GR_OPENORDERS_HEADER') IS NULL
    BEGIN
        EXEC sys.sp_executesql N'CREATE FUNCTION dbo.RII_FN_GR_OPENORDERS_HEADER
    (
        @CustomerCode VARCHAR(30),
        @BranchCode VARCHAR(10) = NULL
    )
    RETURNS TABLE
    AS
    RETURN
    WITH FilteredOrders AS
    (
        SELECT DISTINCT S.FISNO
        FROM V3RIICO.dbo.TBLSIPATRA AS S WITH (NOLOCK)
        WHERE S.STHAR_ACIKLAMA = @CustomerCode
          AND (@BranchCode IS NULL OR @BranchCode = '''' OR S.SUBE_KODU = @BranchCode)

        UNION

        SELECT DISTINCT M.FATIRS_NO
        FROM V3RIICO.dbo.TBLSIPAMAS AS M WITH (NOLOCK)
        WHERE M.CARI_KODU = @CustomerCode
          AND (@BranchCode IS NULL OR @BranchCode = '''' OR M.SUBE_KODU = @BranchCode)
    ),
    OrderTotals AS
    (
        SELECT
            S.FISNO,
            MIN(S.SUBE_KODU) AS BranchCode,
            MIN(S.DEPO_KODU) AS TargetWh,
            MIN(S.STHAR_TARIH) AS OrderDate,
            SUM(CASE WHEN ISNULL(S.L_YEDEK9, 0) = -1 THEN S.STHAR_GCMIK2 ELSE S.STHAR_GCMIK END) AS OrderedQty,
            SUM(S.FIRMA_DOVTUT) AS DeliveredQty
        FROM V3RIICO.dbo.TBLSIPATRA AS S WITH (NOLOCK)
        INNER JOIN FilteredOrders AS F ON F.FISNO = S.FISNO
        WHERE S.STHAR_FTIRSIP = ''7''
          AND S.STHAR_GCKOD = ''G''
          AND S.STHAR_HTUR <> ''K''
          AND ISNULL(S.L_YEDEK9, 0) <= 0
          AND NOT (S.REDNEDEN = 2 AND EXISTS
              (SELECT 1 FROM V3RIICO.dbo.TBLASORTIMAS AS A WITH (NOLOCK) WHERE A.ASORTIKOD = S.EKALAN1))
          AND (@BranchCode IS NULL OR @BranchCode = '''' OR S.SUBE_KODU = @BranchCode)
        GROUP BY S.FISNO
        HAVING SUM(CASE WHEN ISNULL(S.L_YEDEK9, 0) = -1 THEN S.STHAR_GCMIK2 ELSE S.STHAR_GCMIK END)
             - SUM(S.FIRMA_DOVTUT) > 0
    ),
    ActiveAllocations AS
    (
        SELECT
            SD.ExternalDocumentNo,
            SUM(ISNULL(LS.AllocatedQuantity, 0)) AS PlannedQtyAllocated
        FROM dbo.RII_GR_LINE_SOURCE AS LS
        INNER JOIN dbo.RII_GR_SOURCE_DOCUMENT AS SD ON SD.Id = LS.GrSourceDocumentId
        INNER JOIN dbo.RII_GR_LINE AS L ON L.Id = LS.GrLineId
        INNER JOIN dbo.RII_GR_HEADER AS H ON H.Id = L.GrHeaderId
        WHERE LS.IsDeleted = 0 AND SD.IsDeleted = 0 AND L.IsDeleted = 0 AND H.IsDeleted = 0
          AND SD.SourceSystem = N''Netsis'' AND SD.SourceDocumentType = N''PurchaseOrder''
          AND H.Status <> N''Cancelled'' AND H.ErpIntegrationStatus <> N''Succeeded''
          AND (@BranchCode IS NULL OR @BranchCode = '''' OR H.BranchCode = @BranchCode)
        GROUP BY SD.ExternalDocumentNo
    )
    SELECT
        ''H'' AS Mode,
        H.FISNO AS SiparisNo,
        CAST(NULL AS INT) AS OrderID,
        @CustomerCode AS CustomerCode,
        (SELECT TOP (1) C.CARI_ISIM FROM V3RIICO.dbo.TBLCASABIT AS C WITH (NOLOCK)
         WHERE C.CARI_KOD = @CustomerCode) AS CustomerName,
        H.BranchCode,
        H.TargetWh,
        CAST(NULL AS VARCHAR(50)) AS ProjectCode,
        H.OrderDate,
        CAST(H.OrderedQty AS DECIMAL(18, 4)) AS OrderedQty,
        CAST(H.DeliveredQty AS DECIMAL(18, 4)) AS DeliveredQty,
        CAST(H.OrderedQty - H.DeliveredQty AS DECIMAL(18, 4)) AS RemainingHamax,
        CAST(ISNULL(A.PlannedQtyAllocated, 0) AS DECIMAL(18, 4)) AS PlannedQtyAllocated,
        CAST((H.OrderedQty - H.DeliveredQty) - ISNULL(A.PlannedQtyAllocated, 0) AS DECIMAL(18, 4)) AS RemainingForImport
    FROM OrderTotals AS H
    LEFT JOIN ActiveAllocations AS A ON A.ExternalDocumentNo = H.FISNO
    WHERE (H.OrderedQty - H.DeliveredQty) - ISNULL(A.PlannedQtyAllocated, 0) > 0;';
    END
    ELSE
    BEGIN
        EXEC sys.sp_executesql N'ALTER FUNCTION dbo.RII_FN_GR_OPENORDERS_HEADER
    (
        @CustomerCode VARCHAR(30),
        @BranchCode VARCHAR(10) = NULL
    )
    RETURNS TABLE
    AS
    RETURN
    WITH FilteredOrders AS
    (
        SELECT DISTINCT S.FISNO
        FROM V3RIICO.dbo.TBLSIPATRA AS S WITH (NOLOCK)
        WHERE S.STHAR_ACIKLAMA = @CustomerCode
          AND (@BranchCode IS NULL OR @BranchCode = '''' OR S.SUBE_KODU = @BranchCode)

        UNION

        SELECT DISTINCT M.FATIRS_NO
        FROM V3RIICO.dbo.TBLSIPAMAS AS M WITH (NOLOCK)
        WHERE M.CARI_KODU = @CustomerCode
          AND (@BranchCode IS NULL OR @BranchCode = '''' OR M.SUBE_KODU = @BranchCode)
    ),
    OrderTotals AS
    (
        SELECT
            S.FISNO,
            MIN(S.SUBE_KODU) AS BranchCode,
            MIN(S.DEPO_KODU) AS TargetWh,
            MIN(S.STHAR_TARIH) AS OrderDate,
            SUM(CASE WHEN ISNULL(S.L_YEDEK9, 0) = -1 THEN S.STHAR_GCMIK2 ELSE S.STHAR_GCMIK END) AS OrderedQty,
            SUM(S.FIRMA_DOVTUT) AS DeliveredQty
        FROM V3RIICO.dbo.TBLSIPATRA AS S WITH (NOLOCK)
        INNER JOIN FilteredOrders AS F ON F.FISNO = S.FISNO
        WHERE S.STHAR_FTIRSIP = ''7''
          AND S.STHAR_GCKOD = ''G''
          AND S.STHAR_HTUR <> ''K''
          AND ISNULL(S.L_YEDEK9, 0) <= 0
          AND NOT (S.REDNEDEN = 2 AND EXISTS
              (SELECT 1 FROM V3RIICO.dbo.TBLASORTIMAS AS A WITH (NOLOCK) WHERE A.ASORTIKOD = S.EKALAN1))
          AND (@BranchCode IS NULL OR @BranchCode = '''' OR S.SUBE_KODU = @BranchCode)
        GROUP BY S.FISNO
        HAVING SUM(CASE WHEN ISNULL(S.L_YEDEK9, 0) = -1 THEN S.STHAR_GCMIK2 ELSE S.STHAR_GCMIK END)
             - SUM(S.FIRMA_DOVTUT) > 0
    ),
    ActiveAllocations AS
    (
        SELECT
            SD.ExternalDocumentNo,
            SUM(ISNULL(LS.AllocatedQuantity, 0)) AS PlannedQtyAllocated
        FROM dbo.RII_GR_LINE_SOURCE AS LS
        INNER JOIN dbo.RII_GR_SOURCE_DOCUMENT AS SD ON SD.Id = LS.GrSourceDocumentId
        INNER JOIN dbo.RII_GR_LINE AS L ON L.Id = LS.GrLineId
        INNER JOIN dbo.RII_GR_HEADER AS H ON H.Id = L.GrHeaderId
        WHERE LS.IsDeleted = 0 AND SD.IsDeleted = 0 AND L.IsDeleted = 0 AND H.IsDeleted = 0
          AND SD.SourceSystem = N''Netsis'' AND SD.SourceDocumentType = N''PurchaseOrder''
          AND H.Status <> N''Cancelled'' AND H.ErpIntegrationStatus <> N''Succeeded''
          AND (@BranchCode IS NULL OR @BranchCode = '''' OR H.BranchCode = @BranchCode)
        GROUP BY SD.ExternalDocumentNo
    )
    SELECT
        ''H'' AS Mode,
        H.FISNO AS SiparisNo,
        CAST(NULL AS INT) AS OrderID,
        @CustomerCode AS CustomerCode,
        (SELECT TOP (1) C.CARI_ISIM FROM V3RIICO.dbo.TBLCASABIT AS C WITH (NOLOCK)
         WHERE C.CARI_KOD = @CustomerCode) AS CustomerName,
        H.BranchCode,
        H.TargetWh,
        CAST(NULL AS VARCHAR(50)) AS ProjectCode,
        H.OrderDate,
        CAST(H.OrderedQty AS DECIMAL(18, 4)) AS OrderedQty,
        CAST(H.DeliveredQty AS DECIMAL(18, 4)) AS DeliveredQty,
        CAST(H.OrderedQty - H.DeliveredQty AS DECIMAL(18, 4)) AS RemainingHamax,
        CAST(ISNULL(A.PlannedQtyAllocated, 0) AS DECIMAL(18, 4)) AS PlannedQtyAllocated,
        CAST((H.OrderedQty - H.DeliveredQty) - ISNULL(A.PlannedQtyAllocated, 0) AS DECIMAL(18, 4)) AS RemainingForImport
    FROM OrderTotals AS H
    LEFT JOIN ActiveAllocations AS A ON A.ExternalDocumentNo = H.FISNO
    WHERE (H.OrderedQty - H.DeliveredQty) - ISNULL(A.PlannedQtyAllocated, 0) > 0;';
    END
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722174441_RefineGoodsReceiptAndAddNetsisOpenOrders'
)
BEGIN
    IF OBJECT_ID(N'dbo.RII_FN_GR_OPENORDERS_LINE') IS NULL
    BEGIN
        EXEC sys.sp_executesql N'CREATE FUNCTION dbo.RII_FN_GR_OPENORDERS_LINE
    (
        @SiparisNoCsv NVARCHAR(MAX) = NULL,
        @CustomerCode VARCHAR(30) = NULL,
        @BranchCode VARCHAR(10) = NULL
    )
    RETURNS TABLE
    AS
    RETURN
    WITH OrderRows AS
    (
        SELECT
            S.FISNO AS FisNo,
            S.SUBE_KODU AS BranchCode,
            S.DEPO_KODU AS TargetWh,
            CAST(NULL AS VARCHAR(50)) AS ProjectCode,
            S.STHAR_TARIH AS OrderDate,
            S.INCKEYNO AS OrderID,
            S.STOK_KODU AS StockCode,
            ST.STOK_ADI AS StockName,
            COALESCE(M.CARI_KODU, S.STHAR_ACIKLAMA) AS CustomerCode,
            C.CARI_ISIM AS CustomerName,
            CAST(CASE WHEN ISNULL(S.L_YEDEK9, 0) = -1 THEN S.STHAR_GCMIK2 ELSE S.STHAR_GCMIK END AS DECIMAL(18, 4)) AS OrderedQty,
            CAST(S.FIRMA_DOVTUT AS DECIMAL(18, 4)) AS DeliveredQty
        FROM V3RIICO.dbo.TBLSIPATRA AS S WITH (NOLOCK)
        LEFT JOIN V3RIICO.dbo.TBLSIPAMAS AS M WITH (NOLOCK)
          ON S.FISNO = M.FATIRS_NO AND S.STHAR_FTIRSIP = M.FTIRSIP AND S.STHAR_ACIKLAMA = M.CARI_KODU
        LEFT JOIN V3RIICO.dbo.TBLCASABIT AS C WITH (NOLOCK)
          ON C.CARI_KOD = COALESCE(M.CARI_KODU, S.STHAR_ACIKLAMA)
        LEFT JOIN V3RIICO.dbo.TBLASORTIMAS AS A WITH (NOLOCK) ON A.ASORTIKOD = S.EKALAN1
        LEFT JOIN V3RIICO.dbo.TBLSTSABIT AS ST WITH (NOLOCK) ON ST.STOK_KODU = S.STOK_KODU
        WHERE S.STHAR_FTIRSIP = ''7''
          AND S.STHAR_GCKOD = ''G''
          AND S.STHAR_HTUR <> ''K''
          AND ISNULL(S.L_YEDEK9, 0) <= 0
          AND (A.ASORTIKOD IS NULL OR S.REDNEDEN <> 2)
          AND (CASE WHEN ISNULL(S.L_YEDEK9, 0) = -1 THEN S.STHAR_GCMIK2 ELSE S.STHAR_GCMIK END) - S.FIRMA_DOVTUT > 0
          AND (@CustomerCode IS NULL OR @CustomerCode = '''' OR COALESCE(M.CARI_KODU, S.STHAR_ACIKLAMA) = @CustomerCode)
          AND (@BranchCode IS NULL OR @BranchCode = '''' OR S.SUBE_KODU = @BranchCode)
          AND (NULLIF(REPLACE(LTRIM(RTRIM(@SiparisNoCsv)), N'' '', N''''), N'''') IS NULL
               OR CHARINDEX(
                   N'','' + LTRIM(RTRIM(CONVERT(NVARCHAR(100), S.FISNO))) + N'','',
                   N'','' + REPLACE(@SiparisNoCsv, N'' '', N'''') + N'','') > 0)
    ),
    OrderLines AS
    (
        SELECT
            FisNo, BranchCode, TargetWh, ProjectCode, MIN(OrderDate) AS OrderDate,
            OrderID, StockCode, MAX(StockName) AS StockName,
            MAX(CustomerCode) AS CustomerCode, MAX(CustomerName) AS CustomerName,
            SUM(OrderedQty) AS OrderedQty, SUM(DeliveredQty) AS DeliveredQty,
            CAST(SUM(OrderedQty - DeliveredQty) AS DECIMAL(18, 4)) AS RemainingHamax
        FROM OrderRows
        GROUP BY FisNo, BranchCode, TargetWh, ProjectCode, OrderID, StockCode
    ),
    ActiveAllocations AS
    (
        SELECT
            LS.ExternalLineId,
            SUM(ISNULL(LS.AllocatedQuantity, 0)) AS PlannedQtyAllocated
        FROM dbo.RII_GR_LINE_SOURCE AS LS
        INNER JOIN dbo.RII_GR_SOURCE_DOCUMENT AS SD ON SD.Id = LS.GrSourceDocumentId
        INNER JOIN dbo.RII_GR_LINE AS L ON L.Id = LS.GrLineId
        INNER JOIN dbo.RII_GR_HEADER AS H ON H.Id = L.GrHeaderId
        WHERE LS.IsDeleted = 0 AND SD.IsDeleted = 0 AND L.IsDeleted = 0 AND H.IsDeleted = 0
          AND SD.SourceSystem = N''Netsis'' AND SD.SourceDocumentType = N''PurchaseOrder''
          AND H.Status <> N''Cancelled'' AND H.ErpIntegrationStatus <> N''Succeeded''
          AND (@BranchCode IS NULL OR @BranchCode = '''' OR H.BranchCode = @BranchCode)
        GROUP BY LS.ExternalLineId
    )
    SELECT
        ''L'' AS Mode,
        X.FisNo AS SiparisNo,
        X.OrderID,
        X.StockCode,
        X.StockName,
        CAST('''' AS VARCHAR(100)) AS YapKod,
        CAST('''' AS VARCHAR(100)) AS YapAcik,
        X.CustomerCode,
        X.CustomerName,
        X.BranchCode,
        X.TargetWh,
        X.ProjectCode,
        X.OrderDate,
        X.OrderedQty,
        X.DeliveredQty,
        X.RemainingHamax,
        CAST(ISNULL(A.PlannedQtyAllocated, 0) AS DECIMAL(18, 4)) AS PlannedQtyAllocated,
        CAST(X.RemainingHamax - ISNULL(A.PlannedQtyAllocated, 0) AS DECIMAL(18, 4)) AS RemainingForImport
    FROM OrderLines AS X
    LEFT JOIN ActiveAllocations AS A ON A.ExternalLineId = CONVERT(NVARCHAR(100), X.OrderID)
    WHERE X.RemainingHamax - ISNULL(A.PlannedQtyAllocated, 0) > 0;';
    END
    ELSE
    BEGIN
        EXEC sys.sp_executesql N'ALTER FUNCTION dbo.RII_FN_GR_OPENORDERS_LINE
    (
        @SiparisNoCsv NVARCHAR(MAX) = NULL,
        @CustomerCode VARCHAR(30) = NULL,
        @BranchCode VARCHAR(10) = NULL
    )
    RETURNS TABLE
    AS
    RETURN
    WITH OrderRows AS
    (
        SELECT
            S.FISNO AS FisNo,
            S.SUBE_KODU AS BranchCode,
            S.DEPO_KODU AS TargetWh,
            CAST(NULL AS VARCHAR(50)) AS ProjectCode,
            S.STHAR_TARIH AS OrderDate,
            S.INCKEYNO AS OrderID,
            S.STOK_KODU AS StockCode,
            ST.STOK_ADI AS StockName,
            COALESCE(M.CARI_KODU, S.STHAR_ACIKLAMA) AS CustomerCode,
            C.CARI_ISIM AS CustomerName,
            CAST(CASE WHEN ISNULL(S.L_YEDEK9, 0) = -1 THEN S.STHAR_GCMIK2 ELSE S.STHAR_GCMIK END AS DECIMAL(18, 4)) AS OrderedQty,
            CAST(S.FIRMA_DOVTUT AS DECIMAL(18, 4)) AS DeliveredQty
        FROM V3RIICO.dbo.TBLSIPATRA AS S WITH (NOLOCK)
        LEFT JOIN V3RIICO.dbo.TBLSIPAMAS AS M WITH (NOLOCK)
          ON S.FISNO = M.FATIRS_NO AND S.STHAR_FTIRSIP = M.FTIRSIP AND S.STHAR_ACIKLAMA = M.CARI_KODU
        LEFT JOIN V3RIICO.dbo.TBLCASABIT AS C WITH (NOLOCK)
          ON C.CARI_KOD = COALESCE(M.CARI_KODU, S.STHAR_ACIKLAMA)
        LEFT JOIN V3RIICO.dbo.TBLASORTIMAS AS A WITH (NOLOCK) ON A.ASORTIKOD = S.EKALAN1
        LEFT JOIN V3RIICO.dbo.TBLSTSABIT AS ST WITH (NOLOCK) ON ST.STOK_KODU = S.STOK_KODU
        WHERE S.STHAR_FTIRSIP = ''7''
          AND S.STHAR_GCKOD = ''G''
          AND S.STHAR_HTUR <> ''K''
          AND ISNULL(S.L_YEDEK9, 0) <= 0
          AND (A.ASORTIKOD IS NULL OR S.REDNEDEN <> 2)
          AND (CASE WHEN ISNULL(S.L_YEDEK9, 0) = -1 THEN S.STHAR_GCMIK2 ELSE S.STHAR_GCMIK END) - S.FIRMA_DOVTUT > 0
          AND (@CustomerCode IS NULL OR @CustomerCode = '''' OR COALESCE(M.CARI_KODU, S.STHAR_ACIKLAMA) = @CustomerCode)
          AND (@BranchCode IS NULL OR @BranchCode = '''' OR S.SUBE_KODU = @BranchCode)
          AND (NULLIF(REPLACE(LTRIM(RTRIM(@SiparisNoCsv)), N'' '', N''''), N'''') IS NULL
               OR CHARINDEX(
                   N'','' + LTRIM(RTRIM(CONVERT(NVARCHAR(100), S.FISNO))) + N'','',
                   N'','' + REPLACE(@SiparisNoCsv, N'' '', N'''') + N'','') > 0)
    ),
    OrderLines AS
    (
        SELECT
            FisNo, BranchCode, TargetWh, ProjectCode, MIN(OrderDate) AS OrderDate,
            OrderID, StockCode, MAX(StockName) AS StockName,
            MAX(CustomerCode) AS CustomerCode, MAX(CustomerName) AS CustomerName,
            SUM(OrderedQty) AS OrderedQty, SUM(DeliveredQty) AS DeliveredQty,
            CAST(SUM(OrderedQty - DeliveredQty) AS DECIMAL(18, 4)) AS RemainingHamax
        FROM OrderRows
        GROUP BY FisNo, BranchCode, TargetWh, ProjectCode, OrderID, StockCode
    ),
    ActiveAllocations AS
    (
        SELECT
            LS.ExternalLineId,
            SUM(ISNULL(LS.AllocatedQuantity, 0)) AS PlannedQtyAllocated
        FROM dbo.RII_GR_LINE_SOURCE AS LS
        INNER JOIN dbo.RII_GR_SOURCE_DOCUMENT AS SD ON SD.Id = LS.GrSourceDocumentId
        INNER JOIN dbo.RII_GR_LINE AS L ON L.Id = LS.GrLineId
        INNER JOIN dbo.RII_GR_HEADER AS H ON H.Id = L.GrHeaderId
        WHERE LS.IsDeleted = 0 AND SD.IsDeleted = 0 AND L.IsDeleted = 0 AND H.IsDeleted = 0
          AND SD.SourceSystem = N''Netsis'' AND SD.SourceDocumentType = N''PurchaseOrder''
          AND H.Status <> N''Cancelled'' AND H.ErpIntegrationStatus <> N''Succeeded''
          AND (@BranchCode IS NULL OR @BranchCode = '''' OR H.BranchCode = @BranchCode)
        GROUP BY LS.ExternalLineId
    )
    SELECT
        ''L'' AS Mode,
        X.FisNo AS SiparisNo,
        X.OrderID,
        X.StockCode,
        X.StockName,
        CAST('''' AS VARCHAR(100)) AS YapKod,
        CAST('''' AS VARCHAR(100)) AS YapAcik,
        X.CustomerCode,
        X.CustomerName,
        X.BranchCode,
        X.TargetWh,
        X.ProjectCode,
        X.OrderDate,
        X.OrderedQty,
        X.DeliveredQty,
        X.RemainingHamax,
        CAST(ISNULL(A.PlannedQtyAllocated, 0) AS DECIMAL(18, 4)) AS PlannedQtyAllocated,
        CAST(X.RemainingHamax - ISNULL(A.PlannedQtyAllocated, 0) AS DECIMAL(18, 4)) AS RemainingForImport
    FROM OrderLines AS X
    LEFT JOIN ActiveAllocations AS A ON A.ExternalLineId = CONVERT(NVARCHAR(100), X.OrderID)
    WHERE X.RemainingHamax - ISNULL(A.PlannedQtyAllocated, 0) > 0;';
    END
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722174441_RefineGoodsReceiptAndAddNetsisOpenOrders'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260722174441_RefineGoodsReceiptAndAddNetsisOpenOrders', N'10.0.10');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722180559_ConfigureGoodsReceiptWorkflowDefaults'
)
BEGIN
    INSERT INTO dbo.RII_LOCATION
        (WarehouseId, ParentLocationId, Code, Name, LocationType, BarcodeEntryMode, Barcode, ZoneCode,
         AllowMixedStock, AllowMixedLot, AllowMixedStatus, AllowCycleCount, IsPickable, IsPutaway, IsQuarantine,
         IsActive, Description, BranchCode, CreatedDate, IsDeleted)
    SELECT W.Id, NULL, N'KABUL', N'Mal Kabul Alanı', N'Receiving', N'Auto', NULL, N'RECEIVING',
           1, 1, 1, 0, 0, 0, 0, 1, N'SYSTEM:GOODS_RECEIPT_DEFAULT', W.BranchCode, SYSUTCDATETIME(), 0
    FROM dbo.RII_WAREHOUSE AS W
    WHERE W.IsDeleted = 0
      AND NOT EXISTS (SELECT 1 FROM dbo.RII_LOCATION AS L WHERE L.WarehouseId = W.Id AND L.Code = N'KABUL' AND L.IsDeleted = 0);

    IF COL_LENGTH(N'dbo.RII_DOCUMENT_SERIES', N'WarehouseId') IS NOT NULL
       AND COL_LENGTH(N'dbo.RII_DOCUMENT_SERIES', N'Separator') IS NOT NULL
    BEGIN
        EXEC sys.sp_executesql N'
            INSERT INTO dbo.RII_DOCUMENT_SERIES
                (WarehouseId, Code, Name, DocumentType, Prefix, Separator, YearFormat, NumberLength, StartNumber, NextNumber,
                 IncrementBy, IsDefault, IsActive, HasIssuedNumbers, Description, BranchCode, CreatedDate, IsDeleted)
            SELECT W.Id,
                   LEFT(CONCAT(N''GR-'', W.WarehouseCode), 20),
                   CONCAT(N''Mal Kabul '', W.WarehouseName),
                   N''GoodsReceipt'',
                   LEFT(CONCAT(N''GR'', W.WarehouseCode), 10),
                   N''-'', N''FourDigit'', 8, 1, 1, 1, 1, 1, 0,
                   N''SYSTEM:GOODS_RECEIPT_DEFAULT'', W.BranchCode, SYSUTCDATETIME(), 0
            FROM dbo.RII_WAREHOUSE AS W
            WHERE W.IsDeleted = 0
              AND NOT EXISTS
              (
                  SELECT 1
                  FROM dbo.RII_DOCUMENT_SERIES AS S
                  WHERE S.WarehouseId = W.Id
                    AND S.DocumentType = N''GoodsReceipt''
                    AND S.IsDeleted = 0
              );';
    END
    ELSE
    BEGIN
        EXEC sys.sp_executesql N'
            INSERT INTO dbo.RII_DOCUMENT_SERIES
                (Code, Name, DocumentType, Prefix, YearFormat, NumberLength, StartNumber, NextNumber,
                 IncrementBy, IsDefault, IsActive, HasIssuedNumbers, Description, BranchCode, CreatedDate, IsDeleted)
            SELECT LEFT(CONCAT(N''GR-'', W.WarehouseCode), 20),
                   CONCAT(N''Mal Kabul '', W.WarehouseName),
                   N''GoodsReceipt'',
                   LEFT(CONCAT(N''GR'', W.WarehouseCode), 10),
                   N''FourDigit'', 8, 1, 1, 1, 1, 1, 0,
                   N''SYSTEM:GOODS_RECEIPT_DEFAULT'', W.BranchCode, SYSUTCDATETIME(), 0
            FROM dbo.RII_WAREHOUSE AS W
            WHERE W.IsDeleted = 0
              AND W.Id =
              (
                  SELECT MIN(candidate.Id)
                  FROM dbo.RII_WAREHOUSE AS candidate
                  WHERE candidate.BranchCode = W.BranchCode
                    AND candidate.IsDeleted = 0
              )
              AND NOT EXISTS
              (
                  SELECT 1
                  FROM dbo.RII_DOCUMENT_SERIES AS S
                  WHERE S.BranchCode = W.BranchCode
                    AND S.DocumentType = N''GoodsReceipt''
                    AND S.IsDeleted = 0
              );';
    END;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722180559_ConfigureGoodsReceiptWorkflowDefaults'
)
BEGIN
    DECLARE @definition nvarchar(max) = OBJECT_DEFINITION(OBJECT_ID(N'dbo.RII_FN_GR_OPENORDERS_LINE'));
    IF @definition IS NULL
    BEGIN
        ;THROW 50001, 'RII_FN_GR_OPENORDERS_LINE not found.', 1;
    END;
    IF @definition NOT LIKE N'%AS UnitCode%'
    BEGIN
        SET @definition = N'ALTER ' + SUBSTRING(@definition, CHARINDEX(N'FUNCTION', UPPER(@definition)), LEN(@definition));
        SET @definition = REPLACE(@definition, N'ST.STOK_ADI AS StockName,', N'ST.STOK_ADI AS StockName,' + CHAR(13) + CHAR(10) + N'        ST.OLCU_BR1 AS UnitCode,');
        SET @definition = REPLACE(@definition, N'MAX(StockName) AS StockName,', N'MAX(StockName) AS StockName, MAX(UnitCode) AS UnitCode,');
        SET @definition = REPLACE(@definition, N'    X.StockName,', N'    X.StockName,' + CHAR(13) + CHAR(10) + N'    X.UnitCode,');
        EXEC sys.sp_executesql @definition;
    END;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722180559_ConfigureGoodsReceiptWorkflowDefaults'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260722180559_ConfigureGoodsReceiptWorkflowDefaults', N'10.0.10');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722183631_AddQualityAndGoodsReceiptPolicies'
)
BEGIN
    ALTER TABLE [RII_GR_HEADER] ADD [BlockPutawayUntilQualityDecision] bit NOT NULL DEFAULT CAST(0 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722183631_AddQualityAndGoodsReceiptPolicies'
)
BEGIN
    ALTER TABLE [RII_GR_HEADER] ADD [ErpPostingPolicy] nvarchar(40) NOT NULL DEFAULT N'AfterAllApprovals';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722183631_AddQualityAndGoodsReceiptPolicies'
)
BEGIN
    ALTER TABLE [RII_GR_HEADER] ADD [HoldInventoryUntilQualityDecision] bit NOT NULL DEFAULT CAST(0 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722183631_AddQualityAndGoodsReceiptPolicies'
)
BEGIN
    ALTER TABLE [RII_GR_HEADER] ADD [InventoryAvailabilityPolicy] nvarchar(40) NOT NULL DEFAULT N'AfterQualityApproval';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722183631_AddQualityAndGoodsReceiptPolicies'
)
BEGIN
    ALTER TABLE [RII_GR_HEADER] ADD [OverReceiptPolicy] nvarchar(30) NOT NULL DEFAULT N'NotAllowed';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722183631_AddQualityAndGoodsReceiptPolicies'
)
BEGIN
    ALTER TABLE [RII_GR_HEADER] ADD [RequireErpApproval] bit NOT NULL DEFAULT CAST(0 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722183631_AddQualityAndGoodsReceiptPolicies'
)
BEGIN
    ALTER TABLE [RII_GR_HEADER] ADD [RequireQualityApproval] bit NOT NULL DEFAULT CAST(0 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722183631_AddQualityAndGoodsReceiptPolicies'
)
BEGIN
    ALTER TABLE [RII_GR_HEADER] ADD [RequireReceiptApproval] bit NOT NULL DEFAULT CAST(0 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722183631_AddQualityAndGoodsReceiptPolicies'
)
BEGIN
    CREATE TABLE [RII_GR_POLICIES] (
        [Id] bigint NOT NULL IDENTITY,
        [PolicyKey] nvarchar(30) NOT NULL,
        [OverReceiptPolicy] nvarchar(30) NOT NULL,
        [OverReceiptTolerancePercent] decimal(9,4) NOT NULL,
        [AllowUnderReceipt] bit NOT NULL,
        [RequireShortCloseApproval] bit NOT NULL,
        [RequireReceiptApproval] bit NOT NULL,
        [RequireQualityApproval] bit NOT NULL,
        [RequireErpApproval] bit NOT NULL,
        [HoldInventoryUntilQualityDecision] bit NOT NULL,
        [BlockPutawayUntilQualityDecision] bit NOT NULL,
        [InventoryAvailabilityPolicy] nvarchar(40) NOT NULL,
        [ErpPostingPolicy] nvarchar(40) NOT NULL,
        [AllowOrderlessReceipt] bit NOT NULL,
        [AllowUnplannedReceipt] bit NOT NULL,
        [RowVersion] rowversion NOT NULL,
        [BranchCode] nvarchar(10) NOT NULL DEFAULT N'0',
        [CreatedDate] datetime2 NULL,
        [UpdatedDate] datetime2 NULL,
        [DeletedDate] datetime2 NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [CreatedBy] bigint NULL,
        [UpdatedBy] bigint NULL,
        [DeletedBy] bigint NULL,
        CONSTRAINT [PK_RII_GR_POLICIES] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722183631_AddQualityAndGoodsReceiptPolicies'
)
BEGIN
    CREATE TABLE [RII_QUALITY_INSPECTIONS] (
        [Id] bigint NOT NULL IDENTITY,
        [CorrelationId] uniqueidentifier NOT NULL,
        [InspectionNo] nvarchar(60) NOT NULL,
        [SourceDocumentType] nvarchar(50) NOT NULL,
        [SourceDocumentId] bigint NOT NULL,
        [SourceDocumentNo] nvarchar(100) NOT NULL,
        [WarehouseId] bigint NOT NULL,
        [SupplierId] bigint NULL,
        [Status] nvarchar(30) NOT NULL,
        [CreatedAtUtc] datetimeoffset NOT NULL,
        [StartedAtUtc] datetimeoffset NULL,
        [DecidedAtUtc] datetimeoffset NULL,
        [InspectorUserId] bigint NULL,
        [Note] nvarchar(1000) NULL,
        [RowVersion] rowversion NOT NULL,
        [BranchCode] nvarchar(10) NOT NULL DEFAULT N'0',
        [CreatedDate] datetime2 NULL,
        [UpdatedDate] datetime2 NULL,
        [DeletedDate] datetime2 NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [CreatedBy] bigint NULL,
        [UpdatedBy] bigint NULL,
        [DeletedBy] bigint NULL,
        CONSTRAINT [PK_RII_QUALITY_INSPECTIONS] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722183631_AddQualityAndGoodsReceiptPolicies'
)
BEGIN
    CREATE TABLE [RII_QUALITY_PARAMETERS] (
        [Id] bigint NOT NULL IDENTITY,
        [ParameterKey] nvarchar(30) NOT NULL,
        [AutoCreateInspectionOnReceipt] bit NOT NULL,
        [DefaultInspectionMode] nvarchar(30) NOT NULL,
        [DefaultFailAction] nvarchar(30) NOT NULL,
        [HoldInventoryUntilDecision] bit NOT NULL,
        [BlockPutawayUntilDecision] bit NOT NULL,
        [BlockErpPostingUntilDecision] bit NOT NULL,
        [RequireManagerApprovalForRelease] bit NOT NULL,
        [AllowPartialDecision] bit NOT NULL,
        [AllowDirectReceiptWhenNoRule] bit NOT NULL,
        [BlockReceiptWhenLotMissing] bit NOT NULL,
        [BlockReceiptWhenSerialMissing] bit NOT NULL,
        [BlockReceiptWhenExpiryMissing] bit NOT NULL,
        [DefaultQualityLocationId] bigint NULL,
        [DefaultQuarantineLocationId] bigint NULL,
        [DefaultRejectLocationId] bigint NULL,
        [RowVersion] rowversion NOT NULL,
        [BranchCode] nvarchar(10) NOT NULL DEFAULT N'0',
        [CreatedDate] datetime2 NULL,
        [UpdatedDate] datetime2 NULL,
        [DeletedDate] datetime2 NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [CreatedBy] bigint NULL,
        [UpdatedBy] bigint NULL,
        [DeletedBy] bigint NULL,
        CONSTRAINT [PK_RII_QUALITY_PARAMETERS] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722183631_AddQualityAndGoodsReceiptPolicies'
)
BEGIN
    CREATE TABLE [RII_QUALITY_RULES] (
        [Id] bigint NOT NULL IDENTITY,
        [ScopeType] nvarchar(30) NOT NULL,
        [StockId] bigint NULL,
        [StockGroupCode] nvarchar(50) NULL,
        [InspectionMode] nvarchar(30) NOT NULL,
        [SamplingMode] nvarchar(30) NOT NULL,
        [SamplingValue] decimal(18,6) NOT NULL,
        [FailAction] nvarchar(30) NOT NULL,
        [AutoQuarantine] bit NOT NULL,
        [RequireLot] bit NOT NULL,
        [RequireSerial] bit NOT NULL,
        [RequireExpiryDate] bit NOT NULL,
        [MinimumRemainingShelfLifeDays] int NULL,
        [IsActive] bit NOT NULL,
        [Description] nvarchar(500) NULL,
        [RowVersion] rowversion NOT NULL,
        [BranchCode] nvarchar(10) NOT NULL DEFAULT N'0',
        [CreatedDate] datetime2 NULL,
        [UpdatedDate] datetime2 NULL,
        [DeletedDate] datetime2 NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [CreatedBy] bigint NULL,
        [UpdatedBy] bigint NULL,
        [DeletedBy] bigint NULL,
        CONSTRAINT [PK_RII_QUALITY_RULES] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722183631_AddQualityAndGoodsReceiptPolicies'
)
BEGIN
    CREATE TABLE [RII_QUALITY_INSPECTION_LINES] (
        [Id] bigint NOT NULL IDENTITY,
        [QualityInspectionId] bigint NOT NULL,
        [GoodsReceiptLineId] bigint NULL,
        [StockId] bigint NOT NULL,
        [StockCodeSnapshot] nvarchar(100) NOT NULL,
        [StockNameSnapshot] nvarchar(300) NULL,
        [YapCodeId] bigint NULL,
        [YapCodeSnapshot] nvarchar(100) NULL,
        [LotNo] nvarchar(100) NULL,
        [SerialNo] nvarchar(100) NULL,
        [ExpiryDate] date NULL,
        [Quantity] decimal(18,6) NOT NULL,
        [SampleQuantity] decimal(18,6) NOT NULL,
        [AcceptedQuantity] decimal(18,6) NOT NULL,
        [RejectedQuantity] decimal(18,6) NOT NULL,
        [QuarantineQuantity] decimal(18,6) NOT NULL,
        [Decision] nvarchar(30) NOT NULL,
        [ReasonCode] nvarchar(100) NULL,
        [ReasonNote] nvarchar(1000) NULL,
        [DecisionBy] bigint NULL,
        [DecisionAtUtc] datetimeoffset NULL,
        [RowVersion] rowversion NOT NULL,
        [BranchCode] nvarchar(10) NOT NULL DEFAULT N'0',
        [CreatedDate] datetime2 NULL,
        [UpdatedDate] datetime2 NULL,
        [DeletedDate] datetime2 NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [CreatedBy] bigint NULL,
        [UpdatedBy] bigint NULL,
        [DeletedBy] bigint NULL,
        CONSTRAINT [PK_RII_QUALITY_INSPECTION_LINES] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_RII_QUALITY_INSPECTION_LINES_RII_QUALITY_INSPECTIONS_QualityInspectionId] FOREIGN KEY ([QualityInspectionId]) REFERENCES [RII_QUALITY_INSPECTIONS] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722183631_AddQualityAndGoodsReceiptPolicies'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'AvailableOnMobile', N'AvailableOnWeb', N'BranchCode', N'Code', N'CreatedBy', N'CreatedDate', N'DeletedBy', N'DeletedDate', N'Description', N'IsActive', N'Name', N'UpdatedBy', N'UpdatedDate') AND [object_id] = OBJECT_ID(N'[RII_PERMISSION_DEFINITIONS]'))
        SET IDENTITY_INSERT [RII_PERMISSION_DEFINITIONS] ON;
    EXEC(N'INSERT INTO [RII_PERMISSION_DEFINITIONS] ([Id], [AvailableOnMobile], [AvailableOnWeb], [BranchCode], [Code], [CreatedBy], [CreatedDate], [DeletedBy], [DeletedDate], [Description], [IsActive], [Name], [UpdatedBy], [UpdatedDate])
    VALUES (CAST(1044 AS bigint), CAST(0 AS bit), CAST(1 AS bit), N''0'', N''WMS.GOODS_RECEIPT.SETTINGS.VIEW'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, NULL, CAST(1 AS bit), N''Mal kabul ayarlarını görüntüle'', NULL, NULL),
    (CAST(1045 AS bigint), CAST(0 AS bit), CAST(1 AS bit), N''0'', N''WMS.GOODS_RECEIPT.SETTINGS.MANAGE'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, NULL, CAST(1 AS bit), N''Mal kabul ayarlarını yönet'', NULL, NULL),
    (CAST(1046 AS bigint), CAST(0 AS bit), CAST(1 AS bit), N''0'', N''WMS.QUALITY.SETTINGS.VIEW'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, NULL, CAST(1 AS bit), N''Kalite ayarlarını görüntüle'', NULL, NULL),
    (CAST(1047 AS bigint), CAST(0 AS bit), CAST(1 AS bit), N''0'', N''WMS.QUALITY.SETTINGS.MANAGE'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, NULL, CAST(1 AS bit), N''Kalite ayarlarını yönet'', NULL, NULL),
    (CAST(1048 AS bigint), CAST(0 AS bit), CAST(1 AS bit), N''0'', N''WMS.QUALITY.RULES.VIEW'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, NULL, CAST(1 AS bit), N''Kalite kurallarını görüntüle'', NULL, NULL),
    (CAST(1049 AS bigint), CAST(0 AS bit), CAST(1 AS bit), N''0'', N''WMS.QUALITY.RULES.MANAGE'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, NULL, CAST(1 AS bit), N''Kalite kurallarını yönet'', NULL, NULL),
    (CAST(1050 AS bigint), CAST(0 AS bit), CAST(1 AS bit), N''0'', N''WMS.QUALITY.INSPECTIONS.VIEW'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, NULL, CAST(1 AS bit), N''Kalite kontrollerini görüntüle'', NULL, NULL),
    (CAST(1051 AS bigint), CAST(0 AS bit), CAST(1 AS bit), N''0'', N''WMS.QUALITY.INSPECTIONS.DECIDE'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, NULL, CAST(1 AS bit), N''Kalite kararı ver'', NULL, NULL)');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'AvailableOnMobile', N'AvailableOnWeb', N'BranchCode', N'Code', N'CreatedBy', N'CreatedDate', N'DeletedBy', N'DeletedDate', N'Description', N'IsActive', N'Name', N'UpdatedBy', N'UpdatedDate') AND [object_id] = OBJECT_ID(N'[RII_PERMISSION_DEFINITIONS]'))
        SET IDENTITY_INSERT [RII_PERMISSION_DEFINITIONS] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722183631_AddQualityAndGoodsReceiptPolicies'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'BranchCode', N'CreatedBy', N'CreatedDate', N'DeletedBy', N'DeletedDate', N'PermissionDefinitionId', N'PermissionGroupId', N'UpdatedBy', N'UpdatedDate') AND [object_id] = OBJECT_ID(N'[RII_PERMISSION_GROUP_PERMISSIONS]'))
        SET IDENTITY_INSERT [RII_PERMISSION_GROUP_PERMISSIONS] ON;
    EXEC(N'INSERT INTO [RII_PERMISSION_GROUP_PERMISSIONS] ([Id], [BranchCode], [CreatedBy], [CreatedDate], [DeletedBy], [DeletedDate], [PermissionDefinitionId], [PermissionGroupId], [UpdatedBy], [UpdatedDate])
    VALUES (CAST(1044 AS bigint), N''0'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, CAST(1044 AS bigint), CAST(1001 AS bigint), NULL, NULL),
    (CAST(1045 AS bigint), N''0'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, CAST(1045 AS bigint), CAST(1001 AS bigint), NULL, NULL),
    (CAST(1046 AS bigint), N''0'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, CAST(1046 AS bigint), CAST(1001 AS bigint), NULL, NULL),
    (CAST(1047 AS bigint), N''0'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, CAST(1047 AS bigint), CAST(1001 AS bigint), NULL, NULL),
    (CAST(1048 AS bigint), N''0'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, CAST(1048 AS bigint), CAST(1001 AS bigint), NULL, NULL),
    (CAST(1049 AS bigint), N''0'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, CAST(1049 AS bigint), CAST(1001 AS bigint), NULL, NULL),
    (CAST(1050 AS bigint), N''0'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, CAST(1050 AS bigint), CAST(1001 AS bigint), NULL, NULL),
    (CAST(1051 AS bigint), N''0'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, CAST(1051 AS bigint), CAST(1001 AS bigint), NULL, NULL)');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'BranchCode', N'CreatedBy', N'CreatedDate', N'DeletedBy', N'DeletedDate', N'PermissionDefinitionId', N'PermissionGroupId', N'UpdatedBy', N'UpdatedDate') AND [object_id] = OBJECT_ID(N'[RII_PERMISSION_GROUP_PERMISSIONS]'))
        SET IDENTITY_INSERT [RII_PERMISSION_GROUP_PERMISSIONS] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722183631_AddQualityAndGoodsReceiptPolicies'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_RII_GR_POLICIES_BranchCode_PolicyKey] ON [RII_GR_POLICIES] ([BranchCode], [PolicyKey]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722183631_AddQualityAndGoodsReceiptPolicies'
)
BEGIN
    CREATE INDEX [IX_RII_GR_POLICIES_IsDeleted] ON [RII_GR_POLICIES] ([IsDeleted]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722183631_AddQualityAndGoodsReceiptPolicies'
)
BEGIN
    CREATE INDEX [IX_RII_QUALITY_INSPECTION_LINES_GoodsReceiptLineId] ON [RII_QUALITY_INSPECTION_LINES] ([GoodsReceiptLineId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722183631_AddQualityAndGoodsReceiptPolicies'
)
BEGIN
    CREATE INDEX [IX_RII_QUALITY_INSPECTION_LINES_IsDeleted] ON [RII_QUALITY_INSPECTION_LINES] ([IsDeleted]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722183631_AddQualityAndGoodsReceiptPolicies'
)
BEGIN
    CREATE INDEX [IX_RII_QUALITY_INSPECTION_LINES_QualityInspectionId] ON [RII_QUALITY_INSPECTION_LINES] ([QualityInspectionId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722183631_AddQualityAndGoodsReceiptPolicies'
)
BEGIN
    CREATE INDEX [IX_RII_QUALITY_INSPECTION_LINES_StockId_LotNo_SerialNo] ON [RII_QUALITY_INSPECTION_LINES] ([StockId], [LotNo], [SerialNo]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722183631_AddQualityAndGoodsReceiptPolicies'
)
BEGIN
    CREATE INDEX [IX_RII_QUALITY_INSPECTIONS_BranchCode_Status_CreatedAtUtc] ON [RII_QUALITY_INSPECTIONS] ([BranchCode], [Status], [CreatedAtUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722183631_AddQualityAndGoodsReceiptPolicies'
)
BEGIN
    CREATE UNIQUE INDEX [IX_RII_QUALITY_INSPECTIONS_CorrelationId] ON [RII_QUALITY_INSPECTIONS] ([CorrelationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722183631_AddQualityAndGoodsReceiptPolicies'
)
BEGIN
    CREATE INDEX [IX_RII_QUALITY_INSPECTIONS_IsDeleted] ON [RII_QUALITY_INSPECTIONS] ([IsDeleted]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722183631_AddQualityAndGoodsReceiptPolicies'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_RII_QUALITY_PARAMETERS_BranchCode_ParameterKey] ON [RII_QUALITY_PARAMETERS] ([BranchCode], [ParameterKey]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722183631_AddQualityAndGoodsReceiptPolicies'
)
BEGIN
    CREATE INDEX [IX_RII_QUALITY_PARAMETERS_IsDeleted] ON [RII_QUALITY_PARAMETERS] ([IsDeleted]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722183631_AddQualityAndGoodsReceiptPolicies'
)
BEGIN
    CREATE INDEX [IX_RII_QUALITY_RULES_BranchCode_ScopeType_StockId_StockGroupCode_IsActive] ON [RII_QUALITY_RULES] ([BranchCode], [ScopeType], [StockId], [StockGroupCode], [IsActive]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722183631_AddQualityAndGoodsReceiptPolicies'
)
BEGIN
    CREATE INDEX [IX_RII_QUALITY_RULES_IsDeleted] ON [RII_QUALITY_RULES] ([IsDeleted]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722183631_AddQualityAndGoodsReceiptPolicies'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260722183631_AddQualityAndGoodsReceiptPolicies', N'10.0.10');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722190349_ExpandGoodsReceiptOperations'
)
BEGIN
    CREATE TABLE [RII_GR_EXECUTION] (
        [Id] bigint NOT NULL IDENTITY,
        [GrHeaderId] bigint NOT NULL,
        [GrTaskId] bigint NULL,
        [IdempotencyKey] uniqueidentifier NOT NULL,
        [RequestHash] varchar(64) NOT NULL,
        [ExecutionNo] nvarchar(60) NOT NULL,
        [Mode] nvarchar(30) NOT NULL,
        [Status] nvarchar(20) NOT NULL,
        [OccurredAtUtc] datetimeoffset(7) NOT NULL,
        [StockMovementOperationId] bigint NULL,
        [DeviceId] nvarchar(100) NULL,
        [Description] nvarchar(500) NULL,
        [ReversalOfExecutionId] bigint NULL,
        [RowVersion] rowversion NOT NULL,
        [BranchCode] nvarchar(10) NOT NULL DEFAULT N'0',
        [CreatedDate] datetime2 NULL,
        [UpdatedDate] datetime2 NULL,
        [DeletedDate] datetime2 NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [CreatedBy] bigint NULL,
        [UpdatedBy] bigint NULL,
        [DeletedBy] bigint NULL,
        CONSTRAINT [PK_RII_GR_EXECUTION] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_RII_GR_EXECUTION_RII_GR_EXECUTION_ReversalOfExecutionId] FOREIGN KEY ([ReversalOfExecutionId]) REFERENCES [RII_GR_EXECUTION] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_RII_GR_EXECUTION_RII_GR_HEADER_GrHeaderId] FOREIGN KEY ([GrHeaderId]) REFERENCES [RII_GR_HEADER] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_RII_GR_EXECUTION_RII_GR_TASK_GrTaskId] FOREIGN KEY ([GrTaskId]) REFERENCES [RII_GR_TASK] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_RII_GR_EXECUTION_RII_STOCK_MOVEMENT_OPERATION_StockMovementOperationId] FOREIGN KEY ([StockMovementOperationId]) REFERENCES [RII_STOCK_MOVEMENT_OPERATION] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722190349_ExpandGoodsReceiptOperations'
)
BEGIN
    CREATE TABLE [RII_GR_EXECUTION_LINE] (
        [Id] bigint NOT NULL IDENTITY,
        [GrExecutionId] bigint NOT NULL,
        [GrLineId] bigint NOT NULL,
        [LineNo] int NOT NULL,
        [StockId] bigint NOT NULL,
        [YapCodeId] bigint NULL,
        [Quantity] decimal(18,6) NOT NULL,
        [UnitCode] nvarchar(20) NOT NULL,
        [LotNo] nvarchar(100) NULL,
        [SerialNo] nvarchar(100) NULL,
        [ManufacturingDate] date NULL,
        [ExpirationDate] date NULL,
        [ScannedBarcode] nvarchar(250) NULL,
        [WarehouseId] bigint NOT NULL,
        [LocationId] bigint NOT NULL,
        [StockStatus] nvarchar(30) NOT NULL,
        [GoodsReceiptLabelId] bigint NULL,
        [QualityInspectionLineId] bigint NULL,
        [BranchCode] nvarchar(10) NOT NULL DEFAULT N'0',
        [CreatedDate] datetime2 NULL,
        [UpdatedDate] datetime2 NULL,
        [DeletedDate] datetime2 NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [CreatedBy] bigint NULL,
        [UpdatedBy] bigint NULL,
        [DeletedBy] bigint NULL,
        CONSTRAINT [PK_RII_GR_EXECUTION_LINE] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_RII_GR_EXECUTION_LINE_NO] CHECK ([LineNo] > 0),
        CONSTRAINT [CK_RII_GR_EXECUTION_LINE_QTY] CHECK ([Quantity] > 0),
        CONSTRAINT [FK_RII_GR_EXECUTION_LINE_RII_GR_EXECUTION_GrExecutionId] FOREIGN KEY ([GrExecutionId]) REFERENCES [RII_GR_EXECUTION] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_RII_GR_EXECUTION_LINE_RII_GR_LABEL_GoodsReceiptLabelId] FOREIGN KEY ([GoodsReceiptLabelId]) REFERENCES [RII_GR_LABEL] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_RII_GR_EXECUTION_LINE_RII_GR_LINE_GrLineId] FOREIGN KEY ([GrLineId]) REFERENCES [RII_GR_LINE] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_RII_GR_EXECUTION_LINE_RII_LOCATION_LocationId] FOREIGN KEY ([LocationId]) REFERENCES [RII_LOCATION] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_RII_GR_EXECUTION_LINE_RII_QUALITY_INSPECTION_LINES_QualityInspectionLineId] FOREIGN KEY ([QualityInspectionLineId]) REFERENCES [RII_QUALITY_INSPECTION_LINES] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_RII_GR_EXECUTION_LINE_RII_STOCK_StockId] FOREIGN KEY ([StockId]) REFERENCES [RII_STOCK] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_RII_GR_EXECUTION_LINE_RII_WAREHOUSE_WarehouseId] FOREIGN KEY ([WarehouseId]) REFERENCES [RII_WAREHOUSE] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_RII_GR_EXECUTION_LINE_RII_YAP_CODE_YapCodeId] FOREIGN KEY ([YapCodeId]) REFERENCES [RII_YAP_CODE] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722190349_ExpandGoodsReceiptOperations'
)
BEGIN
    CREATE INDEX [IX_RII_GR_EXECUTION_GrTaskId] ON [RII_GR_EXECUTION] ([GrTaskId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722190349_ExpandGoodsReceiptOperations'
)
BEGIN
    CREATE INDEX [IX_RII_GR_EXECUTION_HEADER_TIME] ON [RII_GR_EXECUTION] ([GrHeaderId], [OccurredAtUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722190349_ExpandGoodsReceiptOperations'
)
BEGIN
    CREATE INDEX [IX_RII_GR_EXECUTION_IsDeleted] ON [RII_GR_EXECUTION] ([IsDeleted]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722190349_ExpandGoodsReceiptOperations'
)
BEGIN
    CREATE INDEX [IX_RII_GR_EXECUTION_ReversalOfExecutionId] ON [RII_GR_EXECUTION] ([ReversalOfExecutionId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722190349_ExpandGoodsReceiptOperations'
)
BEGIN
    CREATE INDEX [IX_RII_GR_EXECUTION_StockMovementOperationId] ON [RII_GR_EXECUTION] ([StockMovementOperationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722190349_ExpandGoodsReceiptOperations'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_RII_GR_EXECUTION_BRANCH_NO] ON [RII_GR_EXECUTION] ([BranchCode], [ExecutionNo]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722190349_ExpandGoodsReceiptOperations'
)
BEGIN
    CREATE UNIQUE INDEX [UX_RII_GR_EXECUTION_IDEMPOTENCY] ON [RII_GR_EXECUTION] ([IdempotencyKey]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722190349_ExpandGoodsReceiptOperations'
)
BEGIN
    CREATE INDEX [IX_RII_GR_EXECUTION_LINE_GoodsReceiptLabelId] ON [RII_GR_EXECUTION_LINE] ([GoodsReceiptLabelId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722190349_ExpandGoodsReceiptOperations'
)
BEGIN
    CREATE INDEX [IX_RII_GR_EXECUTION_LINE_GR_LINE] ON [RII_GR_EXECUTION_LINE] ([GrLineId], [GrExecutionId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722190349_ExpandGoodsReceiptOperations'
)
BEGIN
    CREATE INDEX [IX_RII_GR_EXECUTION_LINE_IsDeleted] ON [RII_GR_EXECUTION_LINE] ([IsDeleted]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722190349_ExpandGoodsReceiptOperations'
)
BEGIN
    CREATE INDEX [IX_RII_GR_EXECUTION_LINE_LocationId] ON [RII_GR_EXECUTION_LINE] ([LocationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722190349_ExpandGoodsReceiptOperations'
)
BEGIN
    CREATE INDEX [IX_RII_GR_EXECUTION_LINE_QualityInspectionLineId] ON [RII_GR_EXECUTION_LINE] ([QualityInspectionLineId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722190349_ExpandGoodsReceiptOperations'
)
BEGIN
    CREATE INDEX [IX_RII_GR_EXECUTION_LINE_TRACE] ON [RII_GR_EXECUTION_LINE] ([StockId], [LotNo], [SerialNo]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722190349_ExpandGoodsReceiptOperations'
)
BEGIN
    CREATE INDEX [IX_RII_GR_EXECUTION_LINE_WarehouseId] ON [RII_GR_EXECUTION_LINE] ([WarehouseId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722190349_ExpandGoodsReceiptOperations'
)
BEGIN
    CREATE INDEX [IX_RII_GR_EXECUTION_LINE_YapCodeId] ON [RII_GR_EXECUTION_LINE] ([YapCodeId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722190349_ExpandGoodsReceiptOperations'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_RII_GR_EXECUTION_LINE_SEQUENCE] ON [RII_GR_EXECUTION_LINE] ([GrExecutionId], [LineNo]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722190349_ExpandGoodsReceiptOperations'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260722190349_ExpandGoodsReceiptOperations', N'10.0.10');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722193302_HardenGoodsReceiptDocumentNumbers'
)
BEGIN
    DECLARE @var4 nvarchar(max);
    SELECT @var4 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[RII_GR_HEADER]') AND [c].[name] = N'WaybillNo');
    IF @var4 IS NOT NULL EXEC(N'ALTER TABLE [RII_GR_HEADER] DROP CONSTRAINT ' + @var4 + ';');
    ALTER TABLE [RII_GR_HEADER] ALTER COLUMN [WaybillNo] varchar(15) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722193302_HardenGoodsReceiptDocumentNumbers'
)
BEGIN
    DECLARE @var5 nvarchar(max);
    SELECT @var5 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[RII_GR_HEADER]') AND [c].[name] = N'ElectronicWaybillNo');
    IF @var5 IS NOT NULL EXEC(N'ALTER TABLE [RII_GR_HEADER] DROP CONSTRAINT ' + @var5 + ';');
    ALTER TABLE [RII_GR_HEADER] ALTER COLUMN [ElectronicWaybillNo] varchar(16) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722193302_HardenGoodsReceiptDocumentNumbers'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_RII_GR_HEADER_SUPPLIER_EWAYBILL] ON [RII_GR_HEADER] ([BranchCode], [SupplierId], [ElectronicWaybillNo]) WHERE [IsDeleted] = 0 AND [ElectronicWaybillNo] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722193302_HardenGoodsReceiptDocumentNumbers'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_RII_GR_HEADER_SUPPLIER_WAYBILL] ON [RII_GR_HEADER] ([BranchCode], [SupplierId], [WaybillNo]) WHERE [IsDeleted] = 0 AND [WaybillNo] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722193302_HardenGoodsReceiptDocumentNumbers'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260722193302_HardenGoodsReceiptDocumentNumbers', N'10.0.10');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722200346_AddGoodsReceiptProcessType'
)
BEGIN
    ALTER TABLE [RII_GR_HEADER] ADD [ProcessType] nvarchar(40) NOT NULL DEFAULT N'OrderBasedTask';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722200346_AddGoodsReceiptProcessType'
)
BEGIN
    EXEC sys.sp_executesql N'UPDATE [RII_GR_HEADER]
    SET [ProcessType] = CASE [InitiationMode]
        WHEN N''UnplannedTask'' THEN N''OrderlessTask''
        WHEN N''DirectReceipt'' THEN N''OrderlessDirectReceipt''
        ELSE N''OrderBasedTask''
    END;';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722200346_AddGoodsReceiptProcessType'
)
BEGIN
    EXEC sys.sp_executesql N'CREATE INDEX [IX_RII_GR_HEADER_PROCESS_REPORTING] ON [RII_GR_HEADER] ([BranchCode], [ProcessType], [Status], [DocumentDate]);';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722200346_AddGoodsReceiptProcessType'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260722200346_AddGoodsReceiptProcessType', N'10.0.10');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722202242_AddGoodsReceiptLinePlanning'
)
BEGIN
    ALTER TABLE [RII_GR_LINE] ADD [TargetWarehouseId] bigint NOT NULL DEFAULT CAST(0 AS bigint);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722202242_AddGoodsReceiptLinePlanning'
)
BEGIN
    EXEC sys.sp_executesql N'UPDATE [line]
    SET [line].[TargetWarehouseId] = [header].[TargetWarehouseId]
    FROM [RII_GR_LINE] AS [line]
    INNER JOIN [RII_GR_HEADER] AS [header] ON [header].[Id] = [line].[GrHeaderId]
    WHERE [line].[TargetWarehouseId] = 0;';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722202242_AddGoodsReceiptLinePlanning'
)
BEGIN
    CREATE TABLE [RII_GR_TASK_LINE_TRACKING] (
        [Id] bigint NOT NULL IDENTITY,
        [GrTaskLineId] bigint NOT NULL,
        [SequenceNo] int NOT NULL,
        [StockId] bigint NOT NULL,
        [PlannedQuantity] decimal(18,6) NOT NULL,
        [LotNo] nvarchar(100) NULL,
        [SerialNo] nvarchar(100) NULL,
        [ManufacturingDate] date NULL,
        [ExpirationDate] date NULL,
        [TargetWarehouseId] bigint NOT NULL,
        [ToLocationId] bigint NOT NULL,
        [Description] nvarchar(500) NULL,
        [RowVersion] rowversion NOT NULL,
        [BranchCode] nvarchar(10) NOT NULL DEFAULT N'0',
        [CreatedDate] datetime2 NULL,
        [UpdatedDate] datetime2 NULL,
        [DeletedDate] datetime2 NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [CreatedBy] bigint NULL,
        [UpdatedBy] bigint NULL,
        [DeletedBy] bigint NULL,
        CONSTRAINT [PK_RII_GR_TASK_LINE_TRACKING] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_RII_GR_TASK_LINE_TRACKING_QTY] CHECK ([PlannedQuantity] > 0),
        CONSTRAINT [CK_RII_GR_TASK_LINE_TRACKING_SEQUENCE] CHECK ([SequenceNo] > 0),
        CONSTRAINT [FK_RII_GR_TASK_LINE_TRACKING_RII_GR_TASK_LINE_GrTaskLineId] FOREIGN KEY ([GrTaskLineId]) REFERENCES [RII_GR_TASK_LINE] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_RII_GR_TASK_LINE_TRACKING_RII_LOCATION_ToLocationId] FOREIGN KEY ([ToLocationId]) REFERENCES [RII_LOCATION] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_RII_GR_TASK_LINE_TRACKING_RII_STOCK_StockId] FOREIGN KEY ([StockId]) REFERENCES [RII_STOCK] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_RII_GR_TASK_LINE_TRACKING_RII_WAREHOUSE_TargetWarehouseId] FOREIGN KEY ([TargetWarehouseId]) REFERENCES [RII_WAREHOUSE] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722202242_AddGoodsReceiptLinePlanning'
)
BEGIN
    EXEC sys.sp_executesql N'CREATE INDEX [IX_RII_GR_LINE_TARGET_WAREHOUSE_STATUS_STOCK] ON [RII_GR_LINE] ([TargetWarehouseId], [Status], [StockId]);';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722202242_AddGoodsReceiptLinePlanning'
)
BEGIN
    CREATE INDEX [IX_RII_GR_TASK_LINE_TRACKING_IsDeleted] ON [RII_GR_TASK_LINE_TRACKING] ([IsDeleted]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722202242_AddGoodsReceiptLinePlanning'
)
BEGIN
    CREATE INDEX [IX_RII_GR_TASK_LINE_TRACKING_TargetWarehouseId] ON [RII_GR_TASK_LINE_TRACKING] ([TargetWarehouseId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722202242_AddGoodsReceiptLinePlanning'
)
BEGIN
    CREATE INDEX [IX_RII_GR_TASK_LINE_TRACKING_ToLocationId] ON [RII_GR_TASK_LINE_TRACKING] ([ToLocationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722202242_AddGoodsReceiptLinePlanning'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_RII_GR_TASK_LINE_TRACKING_SEQUENCE] ON [RII_GR_TASK_LINE_TRACKING] ([GrTaskLineId], [SequenceNo]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722202242_AddGoodsReceiptLinePlanning'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_RII_GR_TASK_LINE_TRACKING_SERIAL] ON [RII_GR_TASK_LINE_TRACKING] ([GrTaskLineId], [SerialNo]) WHERE [IsDeleted] = 0 AND [SerialNo] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722202242_AddGoodsReceiptLinePlanning'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_RII_GR_TASK_LINE_TRACKING_STOCK_SERIAL] ON [RII_GR_TASK_LINE_TRACKING] ([StockId], [SerialNo]) WHERE [IsDeleted] = 0 AND [SerialNo] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722202242_AddGoodsReceiptLinePlanning'
)
BEGIN
    ALTER TABLE [RII_GR_LINE] ADD CONSTRAINT [FK_RII_GR_LINE_RII_WAREHOUSE_TargetWarehouseId] FOREIGN KEY ([TargetWarehouseId]) REFERENCES [RII_WAREHOUSE] ([Id]) ON DELETE NO ACTION;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722202242_AddGoodsReceiptLinePlanning'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260722202242_AddGoodsReceiptLinePlanning', N'10.0.10');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722212348_AddGoodsReceiptLabelCorrelationAndQualityReleasePermission'
)
BEGIN
    ALTER TABLE [RII_GR_LABEL_BATCH] ADD [CorrelationId] uniqueidentifier NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722212348_AddGoodsReceiptLabelCorrelationAndQualityReleasePermission'
)
BEGIN
    EXEC sys.sp_executesql N'UPDATE [RII_GR_LABEL_BATCH] SET [CorrelationId] = NEWID() WHERE [CorrelationId] IS NULL;';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722212348_AddGoodsReceiptLabelCorrelationAndQualityReleasePermission'
)
BEGIN
    EXEC sys.sp_executesql N'ALTER TABLE [RII_GR_LABEL_BATCH] ALTER COLUMN [CorrelationId] uniqueidentifier NOT NULL;';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722212348_AddGoodsReceiptLabelCorrelationAndQualityReleasePermission'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'AvailableOnMobile', N'AvailableOnWeb', N'BranchCode', N'Code', N'CreatedBy', N'CreatedDate', N'DeletedBy', N'DeletedDate', N'Description', N'IsActive', N'Name', N'UpdatedBy', N'UpdatedDate') AND [object_id] = OBJECT_ID(N'[RII_PERMISSION_DEFINITIONS]'))
        SET IDENTITY_INSERT [RII_PERMISSION_DEFINITIONS] ON;
    EXEC(N'INSERT INTO [RII_PERMISSION_DEFINITIONS] ([Id], [AvailableOnMobile], [AvailableOnWeb], [BranchCode], [Code], [CreatedBy], [CreatedDate], [DeletedBy], [DeletedDate], [Description], [IsActive], [Name], [UpdatedBy], [UpdatedDate])
    VALUES (CAST(1052 AS bigint), CAST(0 AS bit), CAST(1 AS bit), N''0'', N''WMS.QUALITY.INSPECTIONS.RELEASE'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, NULL, CAST(1 AS bit), N''Karantinadaki ürünü serbest bırak'', NULL, NULL)');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'AvailableOnMobile', N'AvailableOnWeb', N'BranchCode', N'Code', N'CreatedBy', N'CreatedDate', N'DeletedBy', N'DeletedDate', N'Description', N'IsActive', N'Name', N'UpdatedBy', N'UpdatedDate') AND [object_id] = OBJECT_ID(N'[RII_PERMISSION_DEFINITIONS]'))
        SET IDENTITY_INSERT [RII_PERMISSION_DEFINITIONS] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722212348_AddGoodsReceiptLabelCorrelationAndQualityReleasePermission'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'BranchCode', N'CreatedBy', N'CreatedDate', N'DeletedBy', N'DeletedDate', N'PermissionDefinitionId', N'PermissionGroupId', N'UpdatedBy', N'UpdatedDate') AND [object_id] = OBJECT_ID(N'[RII_PERMISSION_GROUP_PERMISSIONS]'))
        SET IDENTITY_INSERT [RII_PERMISSION_GROUP_PERMISSIONS] ON;
    EXEC(N'INSERT INTO [RII_PERMISSION_GROUP_PERMISSIONS] ([Id], [BranchCode], [CreatedBy], [CreatedDate], [DeletedBy], [DeletedDate], [PermissionDefinitionId], [PermissionGroupId], [UpdatedBy], [UpdatedDate])
    VALUES (CAST(1052 AS bigint), N''0'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, CAST(1052 AS bigint), CAST(1001 AS bigint), NULL, NULL)');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'BranchCode', N'CreatedBy', N'CreatedDate', N'DeletedBy', N'DeletedDate', N'PermissionDefinitionId', N'PermissionGroupId', N'UpdatedBy', N'UpdatedDate') AND [object_id] = OBJECT_ID(N'[RII_PERMISSION_GROUP_PERMISSIONS]'))
        SET IDENTITY_INSERT [RII_PERMISSION_GROUP_PERMISSIONS] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722212348_AddGoodsReceiptLabelCorrelationAndQualityReleasePermission'
)
BEGIN
    EXEC sys.sp_executesql N'CREATE UNIQUE INDEX [UX_RII_GR_LABEL_BATCH_CORRELATION] ON [RII_GR_LABEL_BATCH] ([CorrelationId]);';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722212348_AddGoodsReceiptLabelCorrelationAndQualityReleasePermission'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260722212348_AddGoodsReceiptLabelCorrelationAndQualityReleasePermission', N'10.0.10');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723125132_AddSteelReceiptModule'
)
BEGIN
    ALTER TABLE [RII_GR_EXECUTION_LINE] ADD [SerialMaskSnapshot] nvarchar(250) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723125132_AddSteelReceiptModule'
)
BEGIN
    ALTER TABLE [RII_GR_EXECUTION_LINE] ADD [SerialNumberRuleCodeSnapshot] nvarchar(50) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723125132_AddSteelReceiptModule'
)
BEGIN
    ALTER TABLE [RII_GR_EXECUTION_LINE] ADD [SerialNumberRuleId] bigint NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723125132_AddSteelReceiptModule'
)
BEGIN
    ALTER TABLE [RII_GR_EXECUTION_LINE] ADD [SerialNumberRuleVersion] int NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723125132_AddSteelReceiptModule'
)
BEGIN
    CREATE TABLE [RII_SERIAL_NUMBER_RULES] (
        [Id] bigint NOT NULL IDENTITY,
        [RuleCode] nvarchar(50) NOT NULL,
        [DisplayName] nvarchar(150) NOT NULL,
        [Scope] nvarchar(30) NOT NULL,
        [StockId] bigint NULL,
        [StockGroupCode] nvarchar(50) NULL,
        [Version] int NOT NULL,
        [Priority] int NOT NULL,
        [MaskTemplate] nvarchar(250) NOT NULL,
        [CharacterSet] nvarchar(30) NOT NULL,
        [UniquenessScope] nvarchar(30) NOT NULL,
        [MinLength] int NOT NULL,
        [MaxLength] int NOT NULL,
        [TrimWhitespace] bit NOT NULL,
        [NormalizeToUpper] bit NOT NULL,
        [IsRequired] bit NOT NULL,
        [IsActive] bit NOT NULL,
        [EffectiveFromUtc] datetimeoffset NOT NULL,
        [EffectiveToUtc] datetimeoffset NULL,
        [Description] nvarchar(500) NULL,
        [RowVersion] rowversion NOT NULL,
        [BranchCode] nvarchar(10) NOT NULL DEFAULT N'0',
        [CreatedDate] datetime2 NULL,
        [UpdatedDate] datetime2 NULL,
        [DeletedDate] datetime2 NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [CreatedBy] bigint NULL,
        [UpdatedBy] bigint NULL,
        [DeletedBy] bigint NULL,
        CONSTRAINT [PK_RII_SERIAL_NUMBER_RULES] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723125132_AddSteelReceiptModule'
)
BEGIN
    CREATE TABLE [RII_STEEL_RECEIPT_PLAN] (
        [Id] bigint NOT NULL IDENTITY,
        [CorrelationId] uniqueidentifier NOT NULL,
        [ImportReferenceNo] nvarchar(100) NOT NULL,
        [SourceFileName] nvarchar(260) NOT NULL,
        [ExportReferenceNo] nvarchar(100) NULL,
        [SupplierId] bigint NOT NULL,
        [SupplierCodeSnapshot] nvarchar(100) NOT NULL,
        [SupplierNameSnapshot] nvarchar(300) NOT NULL,
        [TargetWarehouseId] bigint NOT NULL,
        [ReceivingLocationId] bigint NOT NULL,
        [DocumentSeriesId] bigint NOT NULL,
        [WaybillNo] nvarchar(50) NULL,
        [WaybillDate] date NULL,
        [PlannedArrivalAtUtc] datetimeoffset NULL,
        [Status] nvarchar(40) NOT NULL,
        [TotalLineCount] int NOT NULL,
        [TotalExpectedQuantity] decimal(18,6) NOT NULL,
        [ImportedAtUtc] datetimeoffset NOT NULL,
        [ImportedBy] bigint NOT NULL,
        [Description] nvarchar(1000) NULL,
        [RowVersion] rowversion NOT NULL,
        [BranchCode] nvarchar(10) NOT NULL DEFAULT N'0',
        [CreatedDate] datetime2 NULL,
        [UpdatedDate] datetime2 NULL,
        [DeletedDate] datetime2 NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [CreatedBy] bigint NULL,
        [UpdatedBy] bigint NULL,
        [DeletedBy] bigint NULL,
        CONSTRAINT [PK_RII_STEEL_RECEIPT_PLAN] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_RII_STEEL_PLAN_LINE_COUNT] CHECK ([TotalLineCount] >= 0),
        CONSTRAINT [CK_RII_STEEL_PLAN_QUANTITY] CHECK ([TotalExpectedQuantity] >= 0),
        CONSTRAINT [FK_RII_STEEL_RECEIPT_PLAN_RII_CUSTOMER_SupplierId] FOREIGN KEY ([SupplierId]) REFERENCES [RII_CUSTOMER] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_RII_STEEL_RECEIPT_PLAN_RII_DOCUMENT_SERIES_DocumentSeriesId] FOREIGN KEY ([DocumentSeriesId]) REFERENCES [RII_DOCUMENT_SERIES] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_RII_STEEL_RECEIPT_PLAN_RII_LOCATION_ReceivingLocationId] FOREIGN KEY ([ReceivingLocationId]) REFERENCES [RII_LOCATION] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_RII_STEEL_RECEIPT_PLAN_RII_WAREHOUSE_TargetWarehouseId] FOREIGN KEY ([TargetWarehouseId]) REFERENCES [RII_WAREHOUSE] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723125132_AddSteelReceiptModule'
)
BEGIN
    CREATE TABLE [RII_STEEL_RECEIPT_PLAN_LINE] (
        [Id] bigint NOT NULL IDENTITY,
        [PlanId] bigint NOT NULL,
        [LineNo] int NOT NULL,
        [DCode] nvarchar(60) NOT NULL,
        [ExternalLineKey] nvarchar(450) NOT NULL,
        [NetsisOrderNo] nvarchar(50) NULL,
        [NetsisOrderLineNo] nvarchar(50) NULL,
        [StockId] bigint NOT NULL,
        [StockCodeSnapshot] nvarchar(100) NOT NULL,
        [StockNameSnapshot] nvarchar(300) NULL,
        [YapCodeId] bigint NULL,
        [YapCodeSnapshot] nvarchar(100) NULL,
        [UnitCode] nvarchar(20) NOT NULL,
        [SupplierSerialNo] nvarchar(100) NOT NULL,
        [SecondarySerialNo] nvarchar(100) NULL,
        [CombinedSize] nvarchar(100) NULL,
        [MaterialGrade] nvarchar(100) NULL,
        [HeatNumber] nvarchar(100) NULL,
        [CertificateNumber] nvarchar(100) NULL,
        [ExpectedQuantity] decimal(18,6) NOT NULL,
        [ArrivedQuantity] decimal(18,6) NOT NULL,
        [ApprovedQuantity] decimal(18,6) NOT NULL,
        [RejectedQuantity] decimal(18,6) NOT NULL,
        [TargetWarehouseId] bigint NOT NULL,
        [ReceivingLocationId] bigint NOT NULL,
        [ArrivalStatus] nvarchar(30) NOT NULL,
        [InspectionStatus] nvarchar(30) NOT NULL,
        [ConversionStatus] nvarchar(30) NOT NULL,
        [PutawayStatus] nvarchar(30) NOT NULL,
        [RejectReason] nvarchar(500) NULL,
        [InspectionNote] nvarchar(1000) NULL,
        [InspectedBy] bigint NULL,
        [InspectedAtUtc] datetimeoffset NULL,
        [GoodsReceiptId] bigint NULL,
        [GoodsReceiptLineId] bigint NULL,
        [RowVersion] rowversion NOT NULL,
        [BranchCode] nvarchar(10) NOT NULL DEFAULT N'0',
        [CreatedDate] datetime2 NULL,
        [UpdatedDate] datetime2 NULL,
        [DeletedDate] datetime2 NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [CreatedBy] bigint NULL,
        [UpdatedBy] bigint NULL,
        [DeletedBy] bigint NULL,
        CONSTRAINT [PK_RII_STEEL_RECEIPT_PLAN_LINE] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_RII_STEEL_LINE_NO] CHECK ([LineNo] > 0),
        CONSTRAINT [CK_RII_STEEL_LINE_QTY] CHECK ([ExpectedQuantity] > 0 AND [ArrivedQuantity] >= 0 AND [ApprovedQuantity] >= 0 AND [RejectedQuantity] >= 0 AND [ApprovedQuantity] + [RejectedQuantity] <= [ArrivedQuantity] AND [ArrivedQuantity] <= [ExpectedQuantity]),
        CONSTRAINT [FK_RII_STEEL_RECEIPT_PLAN_LINE_RII_GR_HEADER_GoodsReceiptId] FOREIGN KEY ([GoodsReceiptId]) REFERENCES [RII_GR_HEADER] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_RII_STEEL_RECEIPT_PLAN_LINE_RII_GR_LINE_GoodsReceiptLineId] FOREIGN KEY ([GoodsReceiptLineId]) REFERENCES [RII_GR_LINE] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_RII_STEEL_RECEIPT_PLAN_LINE_RII_LOCATION_ReceivingLocationId] FOREIGN KEY ([ReceivingLocationId]) REFERENCES [RII_LOCATION] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_RII_STEEL_RECEIPT_PLAN_LINE_RII_STEEL_RECEIPT_PLAN_PlanId] FOREIGN KEY ([PlanId]) REFERENCES [RII_STEEL_RECEIPT_PLAN] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_RII_STEEL_RECEIPT_PLAN_LINE_RII_STOCK_StockId] FOREIGN KEY ([StockId]) REFERENCES [RII_STOCK] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_RII_STEEL_RECEIPT_PLAN_LINE_RII_WAREHOUSE_TargetWarehouseId] FOREIGN KEY ([TargetWarehouseId]) REFERENCES [RII_WAREHOUSE] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_RII_STEEL_RECEIPT_PLAN_LINE_RII_YAP_CODE_YapCodeId] FOREIGN KEY ([YapCodeId]) REFERENCES [RII_YAP_CODE] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723125132_AddSteelReceiptModule'
)
BEGIN
    CREATE TABLE [RII_STEEL_RECEIPT_ATTACHMENT] (
        [Id] bigint NOT NULL IDENTITY,
        [PlanLineId] bigint NOT NULL,
        [FileName] nvarchar(260) NOT NULL,
        [ContentType] nvarchar(100) NOT NULL,
        [StoragePath] nvarchar(500) NOT NULL,
        [Caption] nvarchar(500) NULL,
        [FileSize] bigint NOT NULL,
        [BranchCode] nvarchar(10) NOT NULL DEFAULT N'0',
        [CreatedDate] datetime2 NULL,
        [UpdatedDate] datetime2 NULL,
        [DeletedDate] datetime2 NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [CreatedBy] bigint NULL,
        [UpdatedBy] bigint NULL,
        [DeletedBy] bigint NULL,
        CONSTRAINT [PK_RII_STEEL_RECEIPT_ATTACHMENT] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_RII_STEEL_RECEIPT_ATTACHMENT_RII_STEEL_RECEIPT_PLAN_LINE_PlanLineId] FOREIGN KEY ([PlanLineId]) REFERENCES [RII_STEEL_RECEIPT_PLAN_LINE] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723125132_AddSteelReceiptModule'
)
BEGIN
    CREATE TABLE [RII_STEEL_RECEIPT_PLACEMENT] (
        [Id] bigint NOT NULL IDENTITY,
        [PlanLineId] bigint NOT NULL,
        [WarehouseId] bigint NOT NULL,
        [LocationId] bigint NOT NULL,
        [PlacementType] nvarchar(30) NOT NULL,
        [RowNo] int NULL,
        [PositionNo] int NULL,
        [StackOrderNo] int NULL,
        [StockMovementOperationId] bigint NOT NULL,
        [PlacedAtUtc] datetimeoffset NOT NULL,
        [PlacedBy] bigint NOT NULL,
        [RowVersion] rowversion NOT NULL,
        [BranchCode] nvarchar(10) NOT NULL DEFAULT N'0',
        [CreatedDate] datetime2 NULL,
        [UpdatedDate] datetime2 NULL,
        [DeletedDate] datetime2 NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [CreatedBy] bigint NULL,
        [UpdatedBy] bigint NULL,
        [DeletedBy] bigint NULL,
        CONSTRAINT [PK_RII_STEEL_RECEIPT_PLACEMENT] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_RII_STEEL_PLACEMENT_COORDINATES] CHECK ([RowNo] > 0 AND [PositionNo] > 0),
        CONSTRAINT [CK_RII_STEEL_PLACEMENT_STACK] CHECK ([PlacementType] <> 'Stacked' OR [StackOrderNo] > 0),
        CONSTRAINT [FK_RII_STEEL_RECEIPT_PLACEMENT_RII_LOCATION_LocationId] FOREIGN KEY ([LocationId]) REFERENCES [RII_LOCATION] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_RII_STEEL_RECEIPT_PLACEMENT_RII_STEEL_RECEIPT_PLAN_LINE_PlanLineId] FOREIGN KEY ([PlanLineId]) REFERENCES [RII_STEEL_RECEIPT_PLAN_LINE] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_RII_STEEL_RECEIPT_PLACEMENT_RII_STOCK_MOVEMENT_OPERATION_StockMovementOperationId] FOREIGN KEY ([StockMovementOperationId]) REFERENCES [RII_STOCK_MOVEMENT_OPERATION] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_RII_STEEL_RECEIPT_PLACEMENT_RII_WAREHOUSE_WarehouseId] FOREIGN KEY ([WarehouseId]) REFERENCES [RII_WAREHOUSE] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723125132_AddSteelReceiptModule'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'AvailableOnMobile', N'AvailableOnWeb', N'BranchCode', N'Code', N'CreatedBy', N'CreatedDate', N'DeletedBy', N'DeletedDate', N'Description', N'IsActive', N'Name', N'UpdatedBy', N'UpdatedDate') AND [object_id] = OBJECT_ID(N'[RII_PERMISSION_DEFINITIONS]'))
        SET IDENTITY_INSERT [RII_PERMISSION_DEFINITIONS] ON;
    EXEC(N'INSERT INTO [RII_PERMISSION_DEFINITIONS] ([Id], [AvailableOnMobile], [AvailableOnWeb], [BranchCode], [Code], [CreatedBy], [CreatedDate], [DeletedBy], [DeletedDate], [Description], [IsActive], [Name], [UpdatedBy], [UpdatedDate])
    VALUES (CAST(1053 AS bigint), CAST(0 AS bit), CAST(1 AS bit), N''0'', N''WMS.SERIAL_RULES.VIEW'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, NULL, CAST(1 AS bit), N''Seri maske kurallarını görüntüle'', NULL, NULL),
    (CAST(1054 AS bigint), CAST(0 AS bit), CAST(1 AS bit), N''0'', N''WMS.SERIAL_RULES.MANAGE'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, NULL, CAST(1 AS bit), N''Seri maske kurallarını yönet'', NULL, NULL),
    (CAST(1055 AS bigint), CAST(0 AS bit), CAST(1 AS bit), N''0'', N''WMS.STEEL_RECEIPT.VIEW'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, NULL, CAST(1 AS bit), N''SAC mal kabul planlarını görüntüle'', NULL, NULL),
    (CAST(1056 AS bigint), CAST(0 AS bit), CAST(1 AS bit), N''0'', N''WMS.STEEL_RECEIPT.IMPORT'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, NULL, CAST(1 AS bit), N''SAC beklenti aktarımı yap'', NULL, NULL),
    (CAST(1057 AS bigint), CAST(0 AS bit), CAST(1 AS bit), N''0'', N''WMS.STEEL_RECEIPT.INSPECT'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, NULL, CAST(1 AS bit), N''SAC varış kontrolü yap'', NULL, NULL),
    (CAST(1058 AS bigint), CAST(0 AS bit), CAST(1 AS bit), N''0'', N''WMS.STEEL_RECEIPT.CONVERT'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, NULL, CAST(1 AS bit), N''SAC levhalarını ortak mal kabule aktar'', NULL, NULL),
    (CAST(1059 AS bigint), CAST(0 AS bit), CAST(1 AS bit), N''0'', N''WMS.STEEL_RECEIPT.PUTAWAY'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, NULL, CAST(1 AS bit), N''SAC levhasını nihai rafa yerleştir'', NULL, NULL)');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'AvailableOnMobile', N'AvailableOnWeb', N'BranchCode', N'Code', N'CreatedBy', N'CreatedDate', N'DeletedBy', N'DeletedDate', N'Description', N'IsActive', N'Name', N'UpdatedBy', N'UpdatedDate') AND [object_id] = OBJECT_ID(N'[RII_PERMISSION_DEFINITIONS]'))
        SET IDENTITY_INSERT [RII_PERMISSION_DEFINITIONS] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723125132_AddSteelReceiptModule'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'BranchCode', N'CreatedBy', N'CreatedDate', N'DeletedBy', N'DeletedDate', N'PermissionDefinitionId', N'PermissionGroupId', N'UpdatedBy', N'UpdatedDate') AND [object_id] = OBJECT_ID(N'[RII_PERMISSION_GROUP_PERMISSIONS]'))
        SET IDENTITY_INSERT [RII_PERMISSION_GROUP_PERMISSIONS] ON;
    EXEC(N'INSERT INTO [RII_PERMISSION_GROUP_PERMISSIONS] ([Id], [BranchCode], [CreatedBy], [CreatedDate], [DeletedBy], [DeletedDate], [PermissionDefinitionId], [PermissionGroupId], [UpdatedBy], [UpdatedDate])
    VALUES (CAST(1055 AS bigint), N''0'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, CAST(1055 AS bigint), CAST(1001 AS bigint), NULL, NULL),
    (CAST(1056 AS bigint), N''0'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, CAST(1056 AS bigint), CAST(1001 AS bigint), NULL, NULL),
    (CAST(1057 AS bigint), N''0'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, CAST(1057 AS bigint), CAST(1001 AS bigint), NULL, NULL),
    (CAST(1058 AS bigint), N''0'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, CAST(1058 AS bigint), CAST(1001 AS bigint), NULL, NULL),
    (CAST(1059 AS bigint), N''0'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, CAST(1059 AS bigint), CAST(1001 AS bigint), NULL, NULL)');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'BranchCode', N'CreatedBy', N'CreatedDate', N'DeletedBy', N'DeletedDate', N'PermissionDefinitionId', N'PermissionGroupId', N'UpdatedBy', N'UpdatedDate') AND [object_id] = OBJECT_ID(N'[RII_PERMISSION_GROUP_PERMISSIONS]'))
        SET IDENTITY_INSERT [RII_PERMISSION_GROUP_PERMISSIONS] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723125132_AddSteelReceiptModule'
)
BEGIN
    EXEC sys.sp_executesql N'CREATE INDEX [IX_RII_GR_EXECUTION_LINE_SerialNumberRuleId] ON [RII_GR_EXECUTION_LINE] ([SerialNumberRuleId]);';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723125132_AddSteelReceiptModule'
)
BEGIN
    CREATE INDEX [IX_RII_SERIAL_NUMBER_RULES_IsDeleted] ON [RII_SERIAL_NUMBER_RULES] ([IsDeleted]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723125132_AddSteelReceiptModule'
)
BEGIN
    CREATE INDEX [IX_RII_SERIAL_RULE_RESOLVE] ON [RII_SERIAL_NUMBER_RULES] ([BranchCode], [Scope], [StockId], [StockGroupCode], [IsActive], [EffectiveFromUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723125132_AddSteelReceiptModule'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_RII_SERIAL_RULE_CODE_VERSION] ON [RII_SERIAL_NUMBER_RULES] ([BranchCode], [RuleCode], [Version]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723125132_AddSteelReceiptModule'
)
BEGIN
    CREATE INDEX [IX_RII_STEEL_RECEIPT_ATTACHMENT_IsDeleted] ON [RII_STEEL_RECEIPT_ATTACHMENT] ([IsDeleted]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723125132_AddSteelReceiptModule'
)
BEGIN
    CREATE INDEX [IX_RII_STEEL_RECEIPT_ATTACHMENT_PlanLineId_CreatedDate] ON [RII_STEEL_RECEIPT_ATTACHMENT] ([PlanLineId], [CreatedDate]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723125132_AddSteelReceiptModule'
)
BEGIN
    CREATE INDEX [IX_RII_STEEL_RECEIPT_PLACEMENT_IsDeleted] ON [RII_STEEL_RECEIPT_PLACEMENT] ([IsDeleted]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723125132_AddSteelReceiptModule'
)
BEGIN
    CREATE INDEX [IX_RII_STEEL_RECEIPT_PLACEMENT_LocationId] ON [RII_STEEL_RECEIPT_PLACEMENT] ([LocationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723125132_AddSteelReceiptModule'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_RII_STEEL_RECEIPT_PLACEMENT_PlanLineId] ON [RII_STEEL_RECEIPT_PLACEMENT] ([PlanLineId]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723125132_AddSteelReceiptModule'
)
BEGIN
    CREATE INDEX [IX_RII_STEEL_RECEIPT_PLACEMENT_StockMovementOperationId] ON [RII_STEEL_RECEIPT_PLACEMENT] ([StockMovementOperationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723125132_AddSteelReceiptModule'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_RII_STEEL_RECEIPT_PLACEMENT_WarehouseId_LocationId_RowNo_PositionNo_StackOrderNo] ON [RII_STEEL_RECEIPT_PLACEMENT] ([WarehouseId], [LocationId], [RowNo], [PositionNo], [StackOrderNo]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723125132_AddSteelReceiptModule'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_RII_STEEL_RECEIPT_PLAN_BranchCode_ImportReferenceNo] ON [RII_STEEL_RECEIPT_PLAN] ([BranchCode], [ImportReferenceNo]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723125132_AddSteelReceiptModule'
)
BEGIN
    CREATE UNIQUE INDEX [IX_RII_STEEL_RECEIPT_PLAN_CorrelationId] ON [RII_STEEL_RECEIPT_PLAN] ([CorrelationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723125132_AddSteelReceiptModule'
)
BEGIN
    CREATE INDEX [IX_RII_STEEL_RECEIPT_PLAN_DocumentSeriesId] ON [RII_STEEL_RECEIPT_PLAN] ([DocumentSeriesId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723125132_AddSteelReceiptModule'
)
BEGIN
    CREATE INDEX [IX_RII_STEEL_RECEIPT_PLAN_IsDeleted] ON [RII_STEEL_RECEIPT_PLAN] ([IsDeleted]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723125132_AddSteelReceiptModule'
)
BEGIN
    CREATE INDEX [IX_RII_STEEL_RECEIPT_PLAN_ReceivingLocationId] ON [RII_STEEL_RECEIPT_PLAN] ([ReceivingLocationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723125132_AddSteelReceiptModule'
)
BEGIN
    CREATE INDEX [IX_RII_STEEL_RECEIPT_PLAN_SupplierId_Status_PlannedArrivalAtUtc] ON [RII_STEEL_RECEIPT_PLAN] ([SupplierId], [Status], [PlannedArrivalAtUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723125132_AddSteelReceiptModule'
)
BEGIN
    CREATE INDEX [IX_RII_STEEL_RECEIPT_PLAN_TargetWarehouseId] ON [RII_STEEL_RECEIPT_PLAN] ([TargetWarehouseId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723125132_AddSteelReceiptModule'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_RII_STEEL_RECEIPT_PLAN_LINE_DCode] ON [RII_STEEL_RECEIPT_PLAN_LINE] ([DCode]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723125132_AddSteelReceiptModule'
)
BEGIN
    CREATE INDEX [IX_RII_STEEL_RECEIPT_PLAN_LINE_GoodsReceiptId] ON [RII_STEEL_RECEIPT_PLAN_LINE] ([GoodsReceiptId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723125132_AddSteelReceiptModule'
)
BEGIN
    CREATE INDEX [IX_RII_STEEL_RECEIPT_PLAN_LINE_GoodsReceiptLineId] ON [RII_STEEL_RECEIPT_PLAN_LINE] ([GoodsReceiptLineId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723125132_AddSteelReceiptModule'
)
BEGIN
    CREATE INDEX [IX_RII_STEEL_RECEIPT_PLAN_LINE_InspectionStatus_ConversionStatus] ON [RII_STEEL_RECEIPT_PLAN_LINE] ([InspectionStatus], [ConversionStatus]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723125132_AddSteelReceiptModule'
)
BEGIN
    CREATE INDEX [IX_RII_STEEL_RECEIPT_PLAN_LINE_IsDeleted] ON [RII_STEEL_RECEIPT_PLAN_LINE] ([IsDeleted]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723125132_AddSteelReceiptModule'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_RII_STEEL_RECEIPT_PLAN_LINE_PlanId_ExternalLineKey] ON [RII_STEEL_RECEIPT_PLAN_LINE] ([PlanId], [ExternalLineKey]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723125132_AddSteelReceiptModule'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_RII_STEEL_RECEIPT_PLAN_LINE_PlanId_LineNo] ON [RII_STEEL_RECEIPT_PLAN_LINE] ([PlanId], [LineNo]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723125132_AddSteelReceiptModule'
)
BEGIN
    CREATE INDEX [IX_RII_STEEL_RECEIPT_PLAN_LINE_ReceivingLocationId] ON [RII_STEEL_RECEIPT_PLAN_LINE] ([ReceivingLocationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723125132_AddSteelReceiptModule'
)
BEGIN
    CREATE INDEX [IX_RII_STEEL_RECEIPT_PLAN_LINE_StockId_SupplierSerialNo] ON [RII_STEEL_RECEIPT_PLAN_LINE] ([StockId], [SupplierSerialNo]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723125132_AddSteelReceiptModule'
)
BEGIN
    CREATE INDEX [IX_RII_STEEL_RECEIPT_PLAN_LINE_TargetWarehouseId] ON [RII_STEEL_RECEIPT_PLAN_LINE] ([TargetWarehouseId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723125132_AddSteelReceiptModule'
)
BEGIN
    CREATE INDEX [IX_RII_STEEL_RECEIPT_PLAN_LINE_YapCodeId] ON [RII_STEEL_RECEIPT_PLAN_LINE] ([YapCodeId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723125132_AddSteelReceiptModule'
)
BEGIN
    ALTER TABLE [RII_GR_EXECUTION_LINE] ADD CONSTRAINT [FK_RII_GR_EXECUTION_LINE_RII_SERIAL_NUMBER_RULES_SerialNumberRuleId] FOREIGN KEY ([SerialNumberRuleId]) REFERENCES [RII_SERIAL_NUMBER_RULES] ([Id]) ON DELETE NO ACTION;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723125132_AddSteelReceiptModule'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260723125132_AddSteelReceiptModule', N'10.0.10');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723141356_AddVehicleCheckInAndSteelFlow'
)
BEGIN
    ALTER TABLE [RII_STEEL_RECEIPT_PLAN] ADD [VehicleCheckInId] bigint NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723141356_AddVehicleCheckInAndSteelFlow'
)
BEGIN
    CREATE TABLE [RII_VEHICLE_CHECKIN_HEADER] (
        [Id] bigint NOT NULL IDENTITY,
        [PlateNo] nvarchar(25) NOT NULL,
        [PlateNoNormalized] nvarchar(25) NOT NULL,
        [TrailerPlateNo] nvarchar(25) NULL,
        [TrailerPlateNoNormalized] nvarchar(25) NULL,
        [DriverFirstName] nvarchar(100) NULL,
        [DriverLastName] nvarchar(100) NULL,
        [DriverPhone] nvarchar(40) NULL,
        [CarrierName] nvarchar(200) NULL,
        [CustomerId] bigint NULL,
        [CustomerCodeSnapshot] nvarchar(100) NULL,
        [CustomerNameSnapshot] nvarchar(300) NULL,
        [CheckedInAtUtc] datetimeoffset NOT NULL,
        [BusinessDate] date NOT NULL,
        [Status] nvarchar(30) NOT NULL,
        [Note] nvarchar(1000) NULL,
        [RowVersion] rowversion NOT NULL,
        [BranchCode] nvarchar(10) NOT NULL DEFAULT N'0',
        [CreatedDate] datetime2 NULL,
        [UpdatedDate] datetime2 NULL,
        [DeletedDate] datetime2 NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [CreatedBy] bigint NULL,
        [UpdatedBy] bigint NULL,
        [DeletedBy] bigint NULL,
        CONSTRAINT [PK_RII_VEHICLE_CHECKIN_HEADER] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_RII_VEHICLE_CHECKIN_HEADER_RII_CUSTOMER_CustomerId] FOREIGN KEY ([CustomerId]) REFERENCES [RII_CUSTOMER] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723141356_AddVehicleCheckInAndSteelFlow'
)
BEGIN
    CREATE TABLE [RII_VEHICLE_CHECKIN_IMAGE] (
        [Id] bigint NOT NULL IDENTITY,
        [HeaderId] bigint NOT NULL,
        [FileName] nvarchar(260) NOT NULL,
        [ContentType] nvarchar(100) NOT NULL,
        [StoragePath] nvarchar(500) NOT NULL,
        [FileSize] bigint NOT NULL,
        [SortOrder] int NOT NULL,
        [BranchCode] nvarchar(10) NOT NULL DEFAULT N'0',
        [CreatedDate] datetime2 NULL,
        [UpdatedDate] datetime2 NULL,
        [DeletedDate] datetime2 NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [CreatedBy] bigint NULL,
        [UpdatedBy] bigint NULL,
        [DeletedBy] bigint NULL,
        CONSTRAINT [PK_RII_VEHICLE_CHECKIN_IMAGE] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_RII_VEHICLE_CHECKIN_IMAGE_RII_VEHICLE_CHECKIN_HEADER_HeaderId] FOREIGN KEY ([HeaderId]) REFERENCES [RII_VEHICLE_CHECKIN_HEADER] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723141356_AddVehicleCheckInAndSteelFlow'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'AvailableOnMobile', N'AvailableOnWeb', N'BranchCode', N'Code', N'CreatedBy', N'CreatedDate', N'DeletedBy', N'DeletedDate', N'Description', N'IsActive', N'Name', N'UpdatedBy', N'UpdatedDate') AND [object_id] = OBJECT_ID(N'[RII_PERMISSION_DEFINITIONS]'))
        SET IDENTITY_INSERT [RII_PERMISSION_DEFINITIONS] ON;
    EXEC(N'INSERT INTO [RII_PERMISSION_DEFINITIONS] ([Id], [AvailableOnMobile], [AvailableOnWeb], [BranchCode], [Code], [CreatedBy], [CreatedDate], [DeletedBy], [DeletedDate], [Description], [IsActive], [Name], [UpdatedBy], [UpdatedDate])
    VALUES (CAST(1060 AS bigint), CAST(0 AS bit), CAST(1 AS bit), N''0'', N''WMS.STEEL_RECEIPT.VEHICLE.VIEW'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, NULL, CAST(1 AS bit), N''SAC araç girişlerini görüntüle'', NULL, NULL),
    (CAST(1061 AS bigint), CAST(0 AS bit), CAST(1 AS bit), N''0'', N''WMS.STEEL_RECEIPT.VEHICLE.MANAGE'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, NULL, CAST(1 AS bit), N''SAC araç girişlerini yönet'', NULL, NULL)');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'AvailableOnMobile', N'AvailableOnWeb', N'BranchCode', N'Code', N'CreatedBy', N'CreatedDate', N'DeletedBy', N'DeletedDate', N'Description', N'IsActive', N'Name', N'UpdatedBy', N'UpdatedDate') AND [object_id] = OBJECT_ID(N'[RII_PERMISSION_DEFINITIONS]'))
        SET IDENTITY_INSERT [RII_PERMISSION_DEFINITIONS] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723141356_AddVehicleCheckInAndSteelFlow'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'BranchCode', N'CreatedBy', N'CreatedDate', N'DeletedBy', N'DeletedDate', N'PermissionDefinitionId', N'PermissionGroupId', N'UpdatedBy', N'UpdatedDate') AND [object_id] = OBJECT_ID(N'[RII_PERMISSION_GROUP_PERMISSIONS]'))
        SET IDENTITY_INSERT [RII_PERMISSION_GROUP_PERMISSIONS] ON;
    EXEC(N'INSERT INTO [RII_PERMISSION_GROUP_PERMISSIONS] ([Id], [BranchCode], [CreatedBy], [CreatedDate], [DeletedBy], [DeletedDate], [PermissionDefinitionId], [PermissionGroupId], [UpdatedBy], [UpdatedDate])
    VALUES (CAST(1060 AS bigint), N''0'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, CAST(1060 AS bigint), CAST(1001 AS bigint), NULL, NULL),
    (CAST(1061 AS bigint), N''0'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, CAST(1061 AS bigint), CAST(1001 AS bigint), NULL, NULL)');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'BranchCode', N'CreatedBy', N'CreatedDate', N'DeletedBy', N'DeletedDate', N'PermissionDefinitionId', N'PermissionGroupId', N'UpdatedBy', N'UpdatedDate') AND [object_id] = OBJECT_ID(N'[RII_PERMISSION_GROUP_PERMISSIONS]'))
        SET IDENTITY_INSERT [RII_PERMISSION_GROUP_PERMISSIONS] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723141356_AddVehicleCheckInAndSteelFlow'
)
BEGIN
    EXEC sys.sp_executesql N'CREATE INDEX [IX_RII_STEEL_RECEIPT_PLAN_VehicleCheckInId] ON [RII_STEEL_RECEIPT_PLAN] ([VehicleCheckInId]);';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723141356_AddVehicleCheckInAndSteelFlow'
)
BEGIN
    CREATE INDEX [IX_RII_VEHICLE_CHECKIN_HEADER_BranchCode_CheckedInAtUtc] ON [RII_VEHICLE_CHECKIN_HEADER] ([BranchCode], [CheckedInAtUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723141356_AddVehicleCheckInAndSteelFlow'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_RII_VEHICLE_CHECKIN_HEADER_BranchCode_PlateNoNormalized_BusinessDate] ON [RII_VEHICLE_CHECKIN_HEADER] ([BranchCode], [PlateNoNormalized], [BusinessDate]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723141356_AddVehicleCheckInAndSteelFlow'
)
BEGIN
    CREATE INDEX [IX_RII_VEHICLE_CHECKIN_HEADER_CustomerId] ON [RII_VEHICLE_CHECKIN_HEADER] ([CustomerId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723141356_AddVehicleCheckInAndSteelFlow'
)
BEGIN
    CREATE INDEX [IX_RII_VEHICLE_CHECKIN_HEADER_IsDeleted] ON [RII_VEHICLE_CHECKIN_HEADER] ([IsDeleted]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723141356_AddVehicleCheckInAndSteelFlow'
)
BEGIN
    CREATE INDEX [IX_RII_VEHICLE_CHECKIN_IMAGE_HeaderId_SortOrder] ON [RII_VEHICLE_CHECKIN_IMAGE] ([HeaderId], [SortOrder]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723141356_AddVehicleCheckInAndSteelFlow'
)
BEGIN
    CREATE INDEX [IX_RII_VEHICLE_CHECKIN_IMAGE_IsDeleted] ON [RII_VEHICLE_CHECKIN_IMAGE] ([IsDeleted]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723141356_AddVehicleCheckInAndSteelFlow'
)
BEGIN
    ALTER TABLE [RII_STEEL_RECEIPT_PLAN] ADD CONSTRAINT [FK_RII_STEEL_RECEIPT_PLAN_RII_VEHICLE_CHECKIN_HEADER_VehicleCheckInId] FOREIGN KEY ([VehicleCheckInId]) REFERENCES [RII_VEHICLE_CHECKIN_HEADER] ([Id]) ON DELETE NO ACTION;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723141356_AddVehicleCheckInAndSteelFlow'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260723141356_AddVehicleCheckInAndSteelFlow', N'10.0.10');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723184429_AddWarehouseTransferFoundation'
)
BEGIN
    CREATE TABLE [RII_WT_HEADER] (
        [Id] bigint NOT NULL IDENTITY,
        [DocumentSeriesId] bigint NOT NULL,
        [DocumentNo] nvarchar(50) NOT NULL,
        [DocumentDate] date NOT NULL,
        [InitiationMode] int NOT NULL,
        [ProcessType] int NOT NULL,
        [SourceSystem] int NOT NULL,
        [CorrelationId] uniqueidentifier NOT NULL,
        [ExternalReferenceNo] nvarchar(100) NULL,
        [SourceWarehouseId] bigint NOT NULL,
        [TargetWarehouseId] bigint NOT NULL,
        [SourceStagingLocationId] bigint NULL,
        [TargetReceivingLocationId] bigint NULL,
        [TargetPutawayLocationId] bigint NULL,
        [Status] int NOT NULL,
        [ApprovalStatus] int NOT NULL,
        [ErpIntegrationStatus] int NOT NULL,
        [PlannedDispatchAtUtc] datetimeoffset NULL,
        [PlannedArrivalAtUtc] datetimeoffset NULL,
        [ReleasedAtUtc] datetimeoffset NULL,
        [ReleasedBy] bigint NULL,
        [ShippedAtUtc] datetimeoffset NULL,
        [ShippedBy] bigint NULL,
        [ReceivedAtUtc] datetimeoffset NULL,
        [ReceivedBy] bigint NULL,
        [CompletedAtUtc] datetimeoffset NULL,
        [CompletedBy] bigint NULL,
        [CancelledAtUtc] datetimeoffset NULL,
        [CancelledBy] bigint NULL,
        [CancellationReason] nvarchar(1000) NULL,
        [ShipmentNo] nvarchar(50) NULL,
        [WaybillNo] nvarchar(50) NULL,
        [WaybillDate] date NULL,
        [CarrierCode] nvarchar(50) NULL,
        [CarrierName] nvarchar(200) NULL,
        [VehiclePlate] nvarchar(20) NULL,
        [TrailerPlate] nvarchar(20) NULL,
        [DriverName] nvarchar(200) NULL,
        [SealNo] nvarchar(50) NULL,
        [RequireApproval] bit NOT NULL,
        [AllowPartialPicking] bit NOT NULL,
        [AllowPartialShipment] bit NOT NULL,
        [AllowPartialReceipt] bit NOT NULL,
        [RequireDestinationAcceptance] bit NOT NULL,
        [RequirePutaway] bit NOT NULL,
        [CreateTransitInventory] bit NOT NULL,
        [DiscrepancyPolicy] int NOT NULL,
        [Priority] tinyint NOT NULL,
        [Description] nvarchar(2000) NULL,
        [RowVersion] rowversion NOT NULL,
        [BranchCode] nvarchar(10) NOT NULL DEFAULT N'0',
        [CreatedDate] datetime2 NULL,
        [UpdatedDate] datetime2 NULL,
        [DeletedDate] datetime2 NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [CreatedBy] bigint NULL,
        [UpdatedBy] bigint NULL,
        [DeletedBy] bigint NULL,
        CONSTRAINT [PK_RII_WT_HEADER] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_RII_WT_HEADER_WAREHOUSE] CHECK ([SourceWarehouseId] <> [TargetWarehouseId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723184429_AddWarehouseTransferFoundation'
)
BEGIN
    CREATE TABLE [RII_WT_LINE] (
        [Id] bigint NOT NULL IDENTITY,
        [WtHeaderId] bigint NOT NULL,
        [LineNo] int NOT NULL,
        [StockId] bigint NOT NULL,
        [StockCodeSnapshot] nvarchar(100) NOT NULL,
        [StockNameSnapshot] nvarchar(300) NULL,
        [YapCodeId] bigint NULL,
        [YapCodeSnapshot] nvarchar(100) NULL,
        [UnitCode] nvarchar(20) NOT NULL,
        [BaseUnitCode] nvarchar(20) NOT NULL,
        [UnitConversionFactor] decimal(20,8) NOT NULL,
        [RequestedQuantity] decimal(20,6) NOT NULL,
        [ReservedQuantity] decimal(20,6) NOT NULL,
        [PickedQuantity] decimal(20,6) NOT NULL,
        [ShippedQuantity] decimal(20,6) NOT NULL,
        [ReceivedQuantity] decimal(20,6) NOT NULL,
        [PutawayQuantity] decimal(20,6) NOT NULL,
        [DamagedQuantity] decimal(20,6) NOT NULL,
        [LostQuantity] decimal(20,6) NOT NULL,
        [ShortClosedQuantity] decimal(20,6) NOT NULL,
        [TrackingType] int NOT NULL,
        [RequireLot] bit NOT NULL,
        [RequireSerial] bit NOT NULL,
        [RequireHandlingUnit] bit NOT NULL,
        [SourceWarehouseId] bigint NOT NULL,
        [TargetWarehouseId] bigint NOT NULL,
        [DefaultSourceLocationId] bigint NULL,
        [DefaultTargetLocationId] bigint NULL,
        [Status] int NOT NULL,
        [Description] nvarchar(1000) NULL,
        [RowVersion] rowversion NOT NULL,
        [BranchCode] nvarchar(10) NOT NULL DEFAULT N'0',
        [CreatedDate] datetime2 NULL,
        [UpdatedDate] datetime2 NULL,
        [DeletedDate] datetime2 NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [CreatedBy] bigint NULL,
        [UpdatedBy] bigint NULL,
        [DeletedBy] bigint NULL,
        CONSTRAINT [PK_RII_WT_LINE] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_RII_WT_LINE_QTY] CHECK ([RequestedQuantity] > 0 AND [ReservedQuantity] >= 0 AND [PickedQuantity] >= 0 AND [ShippedQuantity] >= 0 AND [ReceivedQuantity] >= 0 AND [PutawayQuantity] >= 0 AND [DamagedQuantity] >= 0 AND [LostQuantity] >= 0 AND [ShortClosedQuantity] >= 0),
        CONSTRAINT [CK_RII_WT_LINE_WAREHOUSE] CHECK ([SourceWarehouseId] <> [TargetWarehouseId]),
        CONSTRAINT [FK_RII_WT_LINE_RII_WT_HEADER_WtHeaderId] FOREIGN KEY ([WtHeaderId]) REFERENCES [RII_WT_HEADER] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723184429_AddWarehouseTransferFoundation'
)
BEGIN
    CREATE TABLE [RII_WT_SOURCE_DOCUMENT] (
        [Id] bigint NOT NULL IDENTITY,
        [WtHeaderId] bigint NOT NULL,
        [SourceSystem] int NOT NULL,
        [SourceDocumentType] nvarchar(50) NOT NULL,
        [ExternalDocumentNo] nvarchar(100) NOT NULL,
        [ExternalDocumentDate] date NULL,
        [ExternalDocumentId] nvarchar(100) NULL,
        [ExternalStatus] nvarchar(50) NULL,
        [LastSynchronizedAtUtc] datetimeoffset NULL,
        [BranchCode] nvarchar(10) NOT NULL DEFAULT N'0',
        [CreatedDate] datetime2 NULL,
        [UpdatedDate] datetime2 NULL,
        [DeletedDate] datetime2 NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [CreatedBy] bigint NULL,
        [UpdatedBy] bigint NULL,
        [DeletedBy] bigint NULL,
        CONSTRAINT [PK_RII_WT_SOURCE_DOCUMENT] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_RII_WT_SOURCE_DOCUMENT_RII_WT_HEADER_WtHeaderId] FOREIGN KEY ([WtHeaderId]) REFERENCES [RII_WT_HEADER] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723184429_AddWarehouseTransferFoundation'
)
BEGIN
    CREATE TABLE [RII_WT_STATUS_HISTORY] (
        [Id] bigint NOT NULL IDENTITY,
        [WtHeaderId] bigint NOT NULL,
        [StatusArea] int NOT NULL,
        [FromStatus] nvarchar(50) NULL,
        [ToStatus] nvarchar(50) NOT NULL,
        [ChangedAtUtc] datetimeoffset NOT NULL,
        [ChangedBy] bigint NULL,
        [ReasonCode] nvarchar(100) NULL,
        [Description] nvarchar(1000) NULL,
        [CorrelationId] uniqueidentifier NOT NULL,
        [BranchCode] nvarchar(10) NOT NULL DEFAULT N'0',
        [CreatedDate] datetime2 NULL,
        [UpdatedDate] datetime2 NULL,
        [DeletedDate] datetime2 NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [CreatedBy] bigint NULL,
        [UpdatedBy] bigint NULL,
        [DeletedBy] bigint NULL,
        CONSTRAINT [PK_RII_WT_STATUS_HISTORY] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_RII_WT_STATUS_HISTORY_RII_WT_HEADER_WtHeaderId] FOREIGN KEY ([WtHeaderId]) REFERENCES [RII_WT_HEADER] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723184429_AddWarehouseTransferFoundation'
)
BEGIN
    CREATE TABLE [RII_WT_TASK] (
        [Id] bigint NOT NULL IDENTITY,
        [WtHeaderId] bigint NOT NULL,
        [TaskNo] nvarchar(50) NOT NULL,
        [TaskType] int NOT NULL,
        [WarehouseId] bigint NOT NULL,
        [Status] int NOT NULL,
        [Priority] tinyint NOT NULL,
        [PlannedAtUtc] datetimeoffset NULL,
        [AcceptedAtUtc] datetimeoffset NULL,
        [AcceptedBy] bigint NULL,
        [StartedAtUtc] datetimeoffset NULL,
        [StartedBy] bigint NULL,
        [CompletedAtUtc] datetimeoffset NULL,
        [CompletedBy] bigint NULL,
        [Description] nvarchar(1000) NULL,
        [RowVersion] rowversion NOT NULL,
        [BranchCode] nvarchar(10) NOT NULL DEFAULT N'0',
        [CreatedDate] datetime2 NULL,
        [UpdatedDate] datetime2 NULL,
        [DeletedDate] datetime2 NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [CreatedBy] bigint NULL,
        [UpdatedBy] bigint NULL,
        [DeletedBy] bigint NULL,
        CONSTRAINT [PK_RII_WT_TASK] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_RII_WT_TASK_RII_WT_HEADER_WtHeaderId] FOREIGN KEY ([WtHeaderId]) REFERENCES [RII_WT_HEADER] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723184429_AddWarehouseTransferFoundation'
)
BEGIN
    CREATE TABLE [RII_WT_TRACKING] (
        [Id] bigint NOT NULL IDENTITY,
        [WtLineId] bigint NOT NULL,
        [HandlingUnitNo] nvarchar(100) NULL,
        [LotNo] nvarchar(100) NULL,
        [SerialNo] nvarchar(200) NULL,
        [ManufacturingDate] date NULL,
        [ExpirationDate] date NULL,
        [PlannedQuantity] decimal(20,6) NOT NULL,
        [ReservedQuantity] decimal(20,6) NOT NULL,
        [PickedQuantity] decimal(20,6) NOT NULL,
        [ShippedQuantity] decimal(20,6) NOT NULL,
        [ReceivedQuantity] decimal(20,6) NOT NULL,
        [PutawayQuantity] decimal(20,6) NOT NULL,
        [SourceLocationId] bigint NULL,
        [TargetLocationId] bigint NULL,
        [Status] int NOT NULL,
        [RowVersion] rowversion NOT NULL,
        [BranchCode] nvarchar(10) NOT NULL DEFAULT N'0',
        [CreatedDate] datetime2 NULL,
        [UpdatedDate] datetime2 NULL,
        [DeletedDate] datetime2 NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [CreatedBy] bigint NULL,
        [UpdatedBy] bigint NULL,
        [DeletedBy] bigint NULL,
        CONSTRAINT [PK_RII_WT_TRACKING] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_RII_WT_TRACKING_QTY] CHECK ([PlannedQuantity] > 0 AND [ReservedQuantity] >= 0 AND [PickedQuantity] >= 0 AND [ShippedQuantity] >= 0 AND [ReceivedQuantity] >= 0 AND [PutawayQuantity] >= 0),
        CONSTRAINT [FK_RII_WT_TRACKING_RII_WT_LINE_WtLineId] FOREIGN KEY ([WtLineId]) REFERENCES [RII_WT_LINE] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723184429_AddWarehouseTransferFoundation'
)
BEGIN
    CREATE TABLE [RII_WT_LINE_SOURCE] (
        [Id] bigint NOT NULL IDENTITY,
        [WtLineId] bigint NOT NULL,
        [WtSourceDocumentId] bigint NOT NULL,
        [ExternalLineId] nvarchar(100) NOT NULL,
        [ExternalLineNo] int NULL,
        [ExternalStockCode] nvarchar(100) NOT NULL,
        [ExternalYapCode] nvarchar(100) NULL,
        [OrderedQuantity] decimal(20,6) NOT NULL,
        [PreviouslyTransferredQuantity] decimal(20,6) NOT NULL,
        [AllocatedQuantity] decimal(20,6) NOT NULL,
        [UnitCode] nvarchar(20) NOT NULL,
        [ExternalStatus] nvarchar(50) NULL,
        [BranchCode] nvarchar(10) NOT NULL DEFAULT N'0',
        [CreatedDate] datetime2 NULL,
        [UpdatedDate] datetime2 NULL,
        [DeletedDate] datetime2 NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [CreatedBy] bigint NULL,
        [UpdatedBy] bigint NULL,
        [DeletedBy] bigint NULL,
        CONSTRAINT [PK_RII_WT_LINE_SOURCE] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_RII_WT_LINE_SOURCE_RII_WT_LINE_WtLineId] FOREIGN KEY ([WtLineId]) REFERENCES [RII_WT_LINE] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_RII_WT_LINE_SOURCE_RII_WT_SOURCE_DOCUMENT_WtSourceDocumentId] FOREIGN KEY ([WtSourceDocumentId]) REFERENCES [RII_WT_SOURCE_DOCUMENT] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723184429_AddWarehouseTransferFoundation'
)
BEGIN
    CREATE TABLE [RII_WT_TASK_ASSIGNMENT] (
        [Id] bigint NOT NULL IDENTITY,
        [WtTaskId] bigint NOT NULL,
        [UserId] bigint NOT NULL,
        [IsPrimary] bit NOT NULL,
        [AssignedAtUtc] datetimeoffset NOT NULL,
        [AssignedBy] bigint NOT NULL,
        [AcceptedAtUtc] datetimeoffset NULL,
        [BranchCode] nvarchar(10) NOT NULL DEFAULT N'0',
        [CreatedDate] datetime2 NULL,
        [UpdatedDate] datetime2 NULL,
        [DeletedDate] datetime2 NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [CreatedBy] bigint NULL,
        [UpdatedBy] bigint NULL,
        [DeletedBy] bigint NULL,
        CONSTRAINT [PK_RII_WT_TASK_ASSIGNMENT] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_RII_WT_TASK_ASSIGNMENT_RII_WT_TASK_WtTaskId] FOREIGN KEY ([WtTaskId]) REFERENCES [RII_WT_TASK] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723184429_AddWarehouseTransferFoundation'
)
BEGIN
    CREATE TABLE [RII_WT_TASK_LINE] (
        [Id] bigint NOT NULL IDENTITY,
        [WtTaskId] bigint NOT NULL,
        [WtLineId] bigint NOT NULL,
        [PlannedQuantity] decimal(20,6) NOT NULL,
        [ProcessedQuantity] decimal(20,6) NOT NULL,
        [SourceLocationId] bigint NULL,
        [TargetLocationId] bigint NULL,
        [RowVersion] rowversion NOT NULL,
        [BranchCode] nvarchar(10) NOT NULL DEFAULT N'0',
        [CreatedDate] datetime2 NULL,
        [UpdatedDate] datetime2 NULL,
        [DeletedDate] datetime2 NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [CreatedBy] bigint NULL,
        [UpdatedBy] bigint NULL,
        [DeletedBy] bigint NULL,
        CONSTRAINT [PK_RII_WT_TASK_LINE] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_RII_WT_TASK_LINE_QTY] CHECK ([PlannedQuantity] > 0 AND [ProcessedQuantity] >= 0),
        CONSTRAINT [FK_RII_WT_TASK_LINE_RII_WT_LINE_WtLineId] FOREIGN KEY ([WtLineId]) REFERENCES [RII_WT_LINE] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_RII_WT_TASK_LINE_RII_WT_TASK_WtTaskId] FOREIGN KEY ([WtTaskId]) REFERENCES [RII_WT_TASK] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723184429_AddWarehouseTransferFoundation'
)
BEGIN
    CREATE UNIQUE INDEX [IX_RII_WT_HEADER_BranchCode_DocumentNo] ON [RII_WT_HEADER] ([BranchCode], [DocumentNo]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723184429_AddWarehouseTransferFoundation'
)
BEGIN
    CREATE INDEX [IX_RII_WT_HEADER_BranchCode_Status_PlannedDispatchAtUtc] ON [RII_WT_HEADER] ([BranchCode], [Status], [PlannedDispatchAtUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723184429_AddWarehouseTransferFoundation'
)
BEGIN
    CREATE UNIQUE INDEX [IX_RII_WT_HEADER_CorrelationId] ON [RII_WT_HEADER] ([CorrelationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723184429_AddWarehouseTransferFoundation'
)
BEGIN
    CREATE INDEX [IX_RII_WT_HEADER_IsDeleted] ON [RII_WT_HEADER] ([IsDeleted]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723184429_AddWarehouseTransferFoundation'
)
BEGIN
    CREATE INDEX [IX_RII_WT_LINE_IsDeleted] ON [RII_WT_LINE] ([IsDeleted]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723184429_AddWarehouseTransferFoundation'
)
BEGIN
    CREATE INDEX [IX_RII_WT_LINE_StockId_Status] ON [RII_WT_LINE] ([StockId], [Status]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723184429_AddWarehouseTransferFoundation'
)
BEGIN
    CREATE UNIQUE INDEX [IX_RII_WT_LINE_WtHeaderId_LineNo] ON [RII_WT_LINE] ([WtHeaderId], [LineNo]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723184429_AddWarehouseTransferFoundation'
)
BEGIN
    CREATE INDEX [IX_RII_WT_LINE_SOURCE_IsDeleted] ON [RII_WT_LINE_SOURCE] ([IsDeleted]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723184429_AddWarehouseTransferFoundation'
)
BEGIN
    CREATE INDEX [IX_RII_WT_LINE_SOURCE_WtLineId] ON [RII_WT_LINE_SOURCE] ([WtLineId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723184429_AddWarehouseTransferFoundation'
)
BEGIN
    CREATE UNIQUE INDEX [IX_RII_WT_LINE_SOURCE_WtSourceDocumentId_ExternalLineId] ON [RII_WT_LINE_SOURCE] ([WtSourceDocumentId], [ExternalLineId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723184429_AddWarehouseTransferFoundation'
)
BEGIN
    CREATE INDEX [IX_RII_WT_SOURCE_DOCUMENT_IsDeleted] ON [RII_WT_SOURCE_DOCUMENT] ([IsDeleted]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723184429_AddWarehouseTransferFoundation'
)
BEGIN
    CREATE UNIQUE INDEX [IX_RII_WT_SOURCE_DOCUMENT_WtHeaderId_SourceDocumentType_ExternalDocumentNo] ON [RII_WT_SOURCE_DOCUMENT] ([WtHeaderId], [SourceDocumentType], [ExternalDocumentNo]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723184429_AddWarehouseTransferFoundation'
)
BEGIN
    CREATE INDEX [IX_RII_WT_STATUS_HISTORY_CorrelationId] ON [RII_WT_STATUS_HISTORY] ([CorrelationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723184429_AddWarehouseTransferFoundation'
)
BEGIN
    CREATE INDEX [IX_RII_WT_STATUS_HISTORY_IsDeleted] ON [RII_WT_STATUS_HISTORY] ([IsDeleted]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723184429_AddWarehouseTransferFoundation'
)
BEGIN
    CREATE INDEX [IX_RII_WT_STATUS_HISTORY_WtHeaderId_ChangedAtUtc] ON [RII_WT_STATUS_HISTORY] ([WtHeaderId], [ChangedAtUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723184429_AddWarehouseTransferFoundation'
)
BEGIN
    CREATE UNIQUE INDEX [IX_RII_WT_TASK_BranchCode_TaskNo] ON [RII_WT_TASK] ([BranchCode], [TaskNo]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723184429_AddWarehouseTransferFoundation'
)
BEGIN
    CREATE INDEX [IX_RII_WT_TASK_IsDeleted] ON [RII_WT_TASK] ([IsDeleted]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723184429_AddWarehouseTransferFoundation'
)
BEGIN
    CREATE INDEX [IX_RII_WT_TASK_WarehouseId_TaskType_Status] ON [RII_WT_TASK] ([WarehouseId], [TaskType], [Status]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723184429_AddWarehouseTransferFoundation'
)
BEGIN
    CREATE INDEX [IX_RII_WT_TASK_WtHeaderId] ON [RII_WT_TASK] ([WtHeaderId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723184429_AddWarehouseTransferFoundation'
)
BEGIN
    CREATE INDEX [IX_RII_WT_TASK_ASSIGNMENT_IsDeleted] ON [RII_WT_TASK_ASSIGNMENT] ([IsDeleted]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723184429_AddWarehouseTransferFoundation'
)
BEGIN
    CREATE INDEX [IX_RII_WT_TASK_ASSIGNMENT_UserId_AcceptedAtUtc] ON [RII_WT_TASK_ASSIGNMENT] ([UserId], [AcceptedAtUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723184429_AddWarehouseTransferFoundation'
)
BEGIN
    CREATE UNIQUE INDEX [IX_RII_WT_TASK_ASSIGNMENT_WtTaskId_UserId] ON [RII_WT_TASK_ASSIGNMENT] ([WtTaskId], [UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723184429_AddWarehouseTransferFoundation'
)
BEGIN
    CREATE INDEX [IX_RII_WT_TASK_LINE_IsDeleted] ON [RII_WT_TASK_LINE] ([IsDeleted]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723184429_AddWarehouseTransferFoundation'
)
BEGIN
    CREATE INDEX [IX_RII_WT_TASK_LINE_WtLineId] ON [RII_WT_TASK_LINE] ([WtLineId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723184429_AddWarehouseTransferFoundation'
)
BEGIN
    CREATE UNIQUE INDEX [IX_RII_WT_TASK_LINE_WtTaskId_WtLineId] ON [RII_WT_TASK_LINE] ([WtTaskId], [WtLineId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723184429_AddWarehouseTransferFoundation'
)
BEGIN
    CREATE INDEX [IX_RII_WT_TRACKING_IsDeleted] ON [RII_WT_TRACKING] ([IsDeleted]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723184429_AddWarehouseTransferFoundation'
)
BEGIN
    CREATE INDEX [IX_RII_WT_TRACKING_LotNo_SerialNo_Status] ON [RII_WT_TRACKING] ([LotNo], [SerialNo], [Status]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723184429_AddWarehouseTransferFoundation'
)
BEGIN
    CREATE INDEX [IX_RII_WT_TRACKING_WtLineId_SerialNo] ON [RII_WT_TRACKING] ([WtLineId], [SerialNo]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723184429_AddWarehouseTransferFoundation'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260723184429_AddWarehouseTransferFoundation', N'10.0.10');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723192825_ExpandWarehouseTransferModesAndPolicy'
)
BEGIN
    ALTER TABLE [RII_WT_HEADER] ADD [AutoRelease] bit NOT NULL DEFAULT CAST(0 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723192825_ExpandWarehouseTransferModesAndPolicy'
)
BEGIN
    ALTER TABLE [RII_WT_HEADER] ADD [DirectPostingPolicy] nvarchar(30) NOT NULL DEFAULT N'TwoStepTransit';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723192825_ExpandWarehouseTransferModesAndPolicy'
)
BEGIN
    ALTER TABLE [RII_WT_HEADER] ADD [MinimumFulfillmentPercent] decimal(9,4) NOT NULL DEFAULT 100.0;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723192825_ExpandWarehouseTransferModesAndPolicy'
)
BEGIN
    ALTER TABLE [RII_WT_HEADER] ADD [RequireAssignee] bit NOT NULL DEFAULT CAST(1 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723192825_ExpandWarehouseTransferModesAndPolicy'
)
BEGIN
    ALTER TABLE [RII_WT_HEADER] ADD [RequireShipmentInformation] bit NOT NULL DEFAULT CAST(0 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723192825_ExpandWarehouseTransferModesAndPolicy'
)
BEGIN
    ALTER TABLE [RII_WT_HEADER] ADD [RequireSourceLocation] bit NOT NULL DEFAULT CAST(1 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723192825_ExpandWarehouseTransferModesAndPolicy'
)
BEGIN
    ALTER TABLE [RII_WT_HEADER] ADD [RequireTargetLocation] bit NOT NULL DEFAULT CAST(1 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723192825_ExpandWarehouseTransferModesAndPolicy'
)
BEGIN
    ALTER TABLE [RII_WT_HEADER] ADD [ReservationPolicy] nvarchar(30) NOT NULL DEFAULT N'OnRelease';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723192825_ExpandWarehouseTransferModesAndPolicy'
)
BEGIN
    CREATE TABLE [RII_WT_POLICIES] (
        [Id] bigint NOT NULL IDENTITY,
        [PolicyKey] nvarchar(30) NOT NULL,
        [AllowOrderBasedTask] bit NOT NULL,
        [AllowStockBasedTask] bit NOT NULL,
        [AllowOrderBasedDirect] bit NOT NULL,
        [AllowStockBasedDirect] bit NOT NULL,
        [RequireApproval] bit NOT NULL,
        [RequireAssigneeForTask] bit NOT NULL,
        [AllowMultipleAssignees] bit NOT NULL,
        [AutoReleaseTaskBased] bit NOT NULL,
        [ReservationPolicy] nvarchar(30) NOT NULL,
        [MinimumFulfillmentPercent] decimal(9,4) NOT NULL,
        [AllowPartialPicking] bit NOT NULL,
        [AllowPartialShipment] bit NOT NULL,
        [AllowPartialReceipt] bit NOT NULL,
        [RequireDestinationAcceptance] bit NOT NULL,
        [CreateTransitInventory] bit NOT NULL,
        [RequirePutaway] bit NOT NULL,
        [RequireSourceLocation] bit NOT NULL,
        [RequireTargetLocation] bit NOT NULL,
        [RequireShipmentInformation] bit NOT NULL,
        [DirectPostingPolicy] nvarchar(30) NOT NULL,
        [DiscrepancyPolicy] nvarchar(30) NOT NULL,
        [RowVersion] rowversion NOT NULL,
        [BranchCode] nvarchar(10) NOT NULL DEFAULT N'0',
        [CreatedDate] datetime2 NULL,
        [UpdatedDate] datetime2 NULL,
        [DeletedDate] datetime2 NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [CreatedBy] bigint NULL,
        [UpdatedBy] bigint NULL,
        [DeletedBy] bigint NULL,
        CONSTRAINT [PK_RII_WT_POLICIES] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_RII_WT_POLICY_FULFILLMENT] CHECK ([MinimumFulfillmentPercent] >= 0 AND [MinimumFulfillmentPercent] <= 100)
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723192825_ExpandWarehouseTransferModesAndPolicy'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_RII_WT_POLICIES_BranchCode_PolicyKey] ON [RII_WT_POLICIES] ([BranchCode], [PolicyKey]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723192825_ExpandWarehouseTransferModesAndPolicy'
)
BEGIN
    CREATE INDEX [IX_RII_WT_POLICIES_IsDeleted] ON [RII_WT_POLICIES] ([IsDeleted]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723192825_ExpandWarehouseTransferModesAndPolicy'
)
BEGIN
    INSERT dbo.RII_PERMISSION_DEFINITIONS
        (AvailableOnMobile,AvailableOnWeb,BranchCode,Code,CreatedDate,IsActive,Name)
    SELECT 0,1,'0',V.Code,SYSUTCDATETIME(),1,V.Name
    FROM (VALUES
        ('WMS.WAREHOUSE_TRANSFER.VIEW',N'Depolar arası transferleri görüntüle'),
        ('WMS.WAREHOUSE_TRANSFER.CREATE',N'Depolar arası transfer oluştur'),
        ('WMS.WAREHOUSE_TRANSFER.OPERATE',N'Depolar arası transfer operasyonu yürüt'),
        ('WMS.WAREHOUSE_TRANSFER.APPROVE',N'Depolar arası transferi onayla'),
        ('WMS.WAREHOUSE_TRANSFER.SETTINGS.VIEW',N'Transfer süreç ayarlarını görüntüle'),
        ('WMS.WAREHOUSE_TRANSFER.SETTINGS.MANAGE',N'Transfer süreç ayarlarını yönet')
    ) V(Code,Name)
    WHERE NOT EXISTS(SELECT 1 FROM dbo.RII_PERMISSION_DEFINITIONS P WHERE P.Code=V.Code);
    INSERT dbo.RII_PERMISSION_GROUP_PERMISSIONS
        (BranchCode,CreatedDate,PermissionDefinitionId,PermissionGroupId)
    SELECT '0',SYSUTCDATETIME(),P.Id,1001
    FROM dbo.RII_PERMISSION_DEFINITIONS P
    WHERE P.Code LIKE 'WMS.WAREHOUSE_TRANSFER.%'
      AND NOT EXISTS (SELECT 1 FROM dbo.RII_PERMISSION_GROUP_PERMISSIONS G WHERE G.PermissionDefinitionId=P.Id AND G.PermissionGroupId=1001);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723192825_ExpandWarehouseTransferModesAndPolicy'
)
BEGIN
    IF OBJECT_ID(N'dbo.RII_FN_WT_HEADER') IS NULL
    BEGIN
        EXEC sys.sp_executesql N'CREATE FUNCTION dbo.RII_FN_WT_HEADER
    (
        @CustomerCode varchar(30),
        @BranchCode varchar(10)=NULL
    )
    RETURNS TABLE
    AS
    RETURN
    WITH OrderNumbers AS
    (
        SELECT DISTINCT S.FISNO
        FROM V3RIICO.dbo.TBLSIPATRA S WITH (NOLOCK)
        WHERE S.STHAR_ACIKLAMA=@CustomerCode
          AND (@BranchCode IS NULL OR @BranchCode='''' OR CONVERT(varchar(10),S.SUBE_KODU)=@BranchCode)
        UNION
        SELECT DISTINCT M.FATIRS_NO
        FROM V3RIICO.dbo.TBLSIPAMAS M WITH (NOLOCK)
        WHERE M.CARI_KODU=@CustomerCode
          AND (@BranchCode IS NULL OR @BranchCode='''' OR CONVERT(varchar(10),M.SUBE_KODU)=@BranchCode)
    ),
    Orders AS
    (
        SELECT S.FISNO,MIN(S.SUBE_KODU) BranchCode,MIN(S.DEPO_KODU) TargetWh,MIN(S.STHAR_TARIH) OrderDate,
               SUM(CASE WHEN ISNULL(S.L_YEDEK9,0)=-1 THEN ISNULL(S.STHAR_GCMIK2,0) ELSE ISNULL(S.STHAR_GCMIK,0) END) OrderedQty,
               SUM(ISNULL(S.FIRMA_DOVTUT,0)) DeliveredQty
        FROM V3RIICO.dbo.TBLSIPATRA S WITH (NOLOCK)
        JOIN OrderNumbers N ON N.FISNO=S.FISNO
        WHERE S.STHAR_FTIRSIP=''6'' AND S.STHAR_GCKOD=''C'' AND S.STHAR_HTUR<>''K'' AND ISNULL(S.L_YEDEK9,0)<=0
          AND NOT(S.REDNEDEN=2 AND EXISTS(SELECT 1 FROM V3RIICO.dbo.TBLASORTIMAS A WITH(NOLOCK) WHERE A.ASORTIKOD=S.EKALAN1))
          AND (@BranchCode IS NULL OR @BranchCode='''' OR CONVERT(varchar(10),S.SUBE_KODU)=@BranchCode)
        GROUP BY S.FISNO
    ),
    Allocated AS
    (
        SELECT D.ExternalDocumentNo,SUM(ISNULL(L.AllocatedQuantity,0)) PlannedQtyAllocated
        FROM dbo.RII_WT_LINE_SOURCE L
        JOIN dbo.RII_WT_SOURCE_DOCUMENT D ON D.Id=L.WtSourceDocumentId
        JOIN dbo.RII_WT_HEADER H ON H.Id=D.WtHeaderId
        WHERE L.IsDeleted=0 AND D.IsDeleted=0 AND H.IsDeleted=0 AND H.Status<>11
        GROUP BY D.ExternalDocumentNo
    )
    SELECT ''H'' Mode,O.FISNO SiparisNo,CAST(NULL AS int) OrderID,@CustomerCode CustomerCode,
           (SELECT TOP(1) C.CARI_ISIM FROM V3RIICO.dbo.TBLCASABIT C WITH(NOLOCK) WHERE C.CARI_KOD=@CustomerCode) CustomerName,
           O.BranchCode,O.TargetWh,CAST(NULL AS varchar(50)) ProjectCode,O.OrderDate,
           CAST(O.OrderedQty AS decimal(18,4)) OrderedQty,CAST(O.DeliveredQty AS decimal(18,4)) DeliveredQty,
           CAST(O.OrderedQty-O.DeliveredQty AS decimal(18,4)) RemainingHamax,
           CAST(ISNULL(A.PlannedQtyAllocated,0) AS decimal(18,4)) PlannedQtyAllocated,
           CAST((O.OrderedQty-O.DeliveredQty)-ISNULL(A.PlannedQtyAllocated,0) AS decimal(18,4)) RemainingForImport
    FROM Orders O
    LEFT JOIN Allocated A ON A.ExternalDocumentNo=O.FISNO
    WHERE (O.OrderedQty-O.DeliveredQty)-ISNULL(A.PlannedQtyAllocated,0)>0;';
    END
    ELSE
    BEGIN
        EXEC sys.sp_executesql N'ALTER FUNCTION dbo.RII_FN_WT_HEADER
    (
        @CustomerCode varchar(30),
        @BranchCode varchar(10)=NULL
    )
    RETURNS TABLE
    AS
    RETURN
    WITH OrderNumbers AS
    (
        SELECT DISTINCT S.FISNO
        FROM V3RIICO.dbo.TBLSIPATRA S WITH (NOLOCK)
        WHERE S.STHAR_ACIKLAMA=@CustomerCode
          AND (@BranchCode IS NULL OR @BranchCode='''' OR CONVERT(varchar(10),S.SUBE_KODU)=@BranchCode)
        UNION
        SELECT DISTINCT M.FATIRS_NO
        FROM V3RIICO.dbo.TBLSIPAMAS M WITH (NOLOCK)
        WHERE M.CARI_KODU=@CustomerCode
          AND (@BranchCode IS NULL OR @BranchCode='''' OR CONVERT(varchar(10),M.SUBE_KODU)=@BranchCode)
    ),
    Orders AS
    (
        SELECT S.FISNO,MIN(S.SUBE_KODU) BranchCode,MIN(S.DEPO_KODU) TargetWh,MIN(S.STHAR_TARIH) OrderDate,
               SUM(CASE WHEN ISNULL(S.L_YEDEK9,0)=-1 THEN ISNULL(S.STHAR_GCMIK2,0) ELSE ISNULL(S.STHAR_GCMIK,0) END) OrderedQty,
               SUM(ISNULL(S.FIRMA_DOVTUT,0)) DeliveredQty
        FROM V3RIICO.dbo.TBLSIPATRA S WITH (NOLOCK)
        JOIN OrderNumbers N ON N.FISNO=S.FISNO
        WHERE S.STHAR_FTIRSIP=''6'' AND S.STHAR_GCKOD=''C'' AND S.STHAR_HTUR<>''K'' AND ISNULL(S.L_YEDEK9,0)<=0
          AND NOT(S.REDNEDEN=2 AND EXISTS(SELECT 1 FROM V3RIICO.dbo.TBLASORTIMAS A WITH(NOLOCK) WHERE A.ASORTIKOD=S.EKALAN1))
          AND (@BranchCode IS NULL OR @BranchCode='''' OR CONVERT(varchar(10),S.SUBE_KODU)=@BranchCode)
        GROUP BY S.FISNO
    ),
    Allocated AS
    (
        SELECT D.ExternalDocumentNo,SUM(ISNULL(L.AllocatedQuantity,0)) PlannedQtyAllocated
        FROM dbo.RII_WT_LINE_SOURCE L
        JOIN dbo.RII_WT_SOURCE_DOCUMENT D ON D.Id=L.WtSourceDocumentId
        JOIN dbo.RII_WT_HEADER H ON H.Id=D.WtHeaderId
        WHERE L.IsDeleted=0 AND D.IsDeleted=0 AND H.IsDeleted=0 AND H.Status<>11
        GROUP BY D.ExternalDocumentNo
    )
    SELECT ''H'' Mode,O.FISNO SiparisNo,CAST(NULL AS int) OrderID,@CustomerCode CustomerCode,
           (SELECT TOP(1) C.CARI_ISIM FROM V3RIICO.dbo.TBLCASABIT C WITH(NOLOCK) WHERE C.CARI_KOD=@CustomerCode) CustomerName,
           O.BranchCode,O.TargetWh,CAST(NULL AS varchar(50)) ProjectCode,O.OrderDate,
           CAST(O.OrderedQty AS decimal(18,4)) OrderedQty,CAST(O.DeliveredQty AS decimal(18,4)) DeliveredQty,
           CAST(O.OrderedQty-O.DeliveredQty AS decimal(18,4)) RemainingHamax,
           CAST(ISNULL(A.PlannedQtyAllocated,0) AS decimal(18,4)) PlannedQtyAllocated,
           CAST((O.OrderedQty-O.DeliveredQty)-ISNULL(A.PlannedQtyAllocated,0) AS decimal(18,4)) RemainingForImport
    FROM Orders O
    LEFT JOIN Allocated A ON A.ExternalDocumentNo=O.FISNO
    WHERE (O.OrderedQty-O.DeliveredQty)-ISNULL(A.PlannedQtyAllocated,0)>0;';
    END
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723192825_ExpandWarehouseTransferModesAndPolicy'
)
BEGIN
    IF OBJECT_ID(N'dbo.RII_FN_WT_LINE') IS NULL
    BEGIN
        EXEC sys.sp_executesql N'CREATE FUNCTION dbo.RII_FN_WT_LINE
    (
        @SiparisNoCsv nvarchar(max),
        @BranchCode varchar(10)=NULL
    )
    RETURNS TABLE
    AS
    RETURN
    WITH Base AS
    (
        SELECT S.FISNO,S.SUBE_KODU BranchCode,S.DEPO_KODU TargetWh,S.PROJE_KODU ProjectCode,S.STHAR_TARIH OrderDate,
               S.INCKEYNO OrderID,S.STOK_KODU StockCode,ST.STOK_ADI StockName,
               COALESCE(M.CARI_KODU,S.STHAR_ACIKLAMA) CustomerCode,C.CARI_ISIM CustomerName,
               CASE WHEN ISNULL(S.L_YEDEK9,0)=-1 THEN ISNULL(S.STHAR_GCMIK2,0) ELSE ISNULL(S.STHAR_GCMIK,0) END OrderedQty,
               ISNULL(S.FIRMA_DOVTUT,0) DeliveredQty,CAST('''' AS varchar(50)) YapKod,CAST('''' AS varchar(200)) YapAcik
        FROM V3RIICO.dbo.TBLSIPATRA S WITH(NOLOCK)
        LEFT JOIN V3RIICO.dbo.TBLSIPAMAS M WITH(NOLOCK) ON M.FATIRS_NO=S.FISNO AND M.FTIRSIP=S.STHAR_FTIRSIP
        LEFT JOIN V3RIICO.dbo.TBLCASABIT C WITH(NOLOCK) ON C.CARI_KOD=COALESCE(M.CARI_KODU,S.STHAR_ACIKLAMA)
        LEFT JOIN V3RIICO.dbo.TBLSTSABIT ST WITH(NOLOCK) ON ST.STOK_KODU=S.STOK_KODU
        WHERE S.STHAR_FTIRSIP=''6'' AND S.STHAR_GCKOD=''C'' AND S.STHAR_HTUR<>''K'' AND ISNULL(S.L_YEDEK9,0)<=0
          AND NOT(S.REDNEDEN=2 AND EXISTS(SELECT 1 FROM V3RIICO.dbo.TBLASORTIMAS A WITH(NOLOCK) WHERE A.ASORTIKOD=S.EKALAN1))
          AND (@BranchCode IS NULL OR @BranchCode='''' OR CONVERT(varchar(10),S.SUBE_KODU)=@BranchCode)
          AND (NULLIF(REPLACE(LTRIM(RTRIM(@SiparisNoCsv)),N'' '',N''''),N'''') IS NULL
               OR CHARINDEX(
                   N'',''+LTRIM(RTRIM(CONVERT(nvarchar(100),S.FISNO)))+N'','',
                   N'',''+REPLACE(@SiparisNoCsv,N'' '',N'''')+N'','')>0)
    ),
    Orders AS
    (
        SELECT FISNO,BranchCode,TargetWh,ProjectCode,MIN(OrderDate) OrderDate,OrderID,StockCode,MAX(StockName) StockName,
               MAX(CustomerCode) CustomerCode,MAX(CustomerName) CustomerName,SUM(OrderedQty) OrderedQty,
               SUM(DeliveredQty) DeliveredQty,SUM(OrderedQty-DeliveredQty) RemainingHamax,MAX(YapKod) YapKod,MAX(YapAcik) YapAcik
        FROM Base GROUP BY FISNO,BranchCode,TargetWh,ProjectCode,OrderID,StockCode
    ),
    Allocated AS
    (
        SELECT D.ExternalDocumentNo,L.ExternalLineId,SUM(ISNULL(L.AllocatedQuantity,0)) PlannedQtyAllocated
        FROM dbo.RII_WT_LINE_SOURCE L
        JOIN dbo.RII_WT_SOURCE_DOCUMENT D ON D.Id=L.WtSourceDocumentId
        JOIN dbo.RII_WT_HEADER H ON H.Id=D.WtHeaderId
        WHERE L.IsDeleted=0 AND D.IsDeleted=0 AND H.IsDeleted=0 AND H.Status<>11
        GROUP BY D.ExternalDocumentNo,L.ExternalLineId
    )
    SELECT ''L'' Mode,O.FISNO SiparisNo,O.OrderID,O.StockCode,O.StockName,O.CustomerCode,O.CustomerName,
           O.BranchCode,O.TargetWh,O.ProjectCode,O.OrderDate,
           CAST(O.OrderedQty AS decimal(18,4)) OrderedQty,CAST(O.DeliveredQty AS decimal(18,4)) DeliveredQty,
           CAST(O.RemainingHamax AS decimal(18,4)) RemainingHamax,
           CAST(ISNULL(A.PlannedQtyAllocated,0) AS decimal(18,4)) PlannedQtyAllocated,
           CAST(O.RemainingHamax-ISNULL(A.PlannedQtyAllocated,0) AS decimal(18,4)) RemainingForImport,
           O.YapKod,O.YapAcik
    FROM Orders O
    LEFT JOIN Allocated A ON A.ExternalDocumentNo=O.FISNO AND A.ExternalLineId=CONVERT(varchar(100),O.OrderID)
    WHERE O.RemainingHamax-ISNULL(A.PlannedQtyAllocated,0)>0;';
    END
    ELSE
    BEGIN
        EXEC sys.sp_executesql N'ALTER FUNCTION dbo.RII_FN_WT_LINE
    (
        @SiparisNoCsv nvarchar(max),
        @BranchCode varchar(10)=NULL
    )
    RETURNS TABLE
    AS
    RETURN
    WITH Base AS
    (
        SELECT S.FISNO,S.SUBE_KODU BranchCode,S.DEPO_KODU TargetWh,S.PROJE_KODU ProjectCode,S.STHAR_TARIH OrderDate,
               S.INCKEYNO OrderID,S.STOK_KODU StockCode,ST.STOK_ADI StockName,
               COALESCE(M.CARI_KODU,S.STHAR_ACIKLAMA) CustomerCode,C.CARI_ISIM CustomerName,
               CASE WHEN ISNULL(S.L_YEDEK9,0)=-1 THEN ISNULL(S.STHAR_GCMIK2,0) ELSE ISNULL(S.STHAR_GCMIK,0) END OrderedQty,
               ISNULL(S.FIRMA_DOVTUT,0) DeliveredQty,CAST('''' AS varchar(50)) YapKod,CAST('''' AS varchar(200)) YapAcik
        FROM V3RIICO.dbo.TBLSIPATRA S WITH(NOLOCK)
        LEFT JOIN V3RIICO.dbo.TBLSIPAMAS M WITH(NOLOCK) ON M.FATIRS_NO=S.FISNO AND M.FTIRSIP=S.STHAR_FTIRSIP
        LEFT JOIN V3RIICO.dbo.TBLCASABIT C WITH(NOLOCK) ON C.CARI_KOD=COALESCE(M.CARI_KODU,S.STHAR_ACIKLAMA)
        LEFT JOIN V3RIICO.dbo.TBLSTSABIT ST WITH(NOLOCK) ON ST.STOK_KODU=S.STOK_KODU
        WHERE S.STHAR_FTIRSIP=''6'' AND S.STHAR_GCKOD=''C'' AND S.STHAR_HTUR<>''K'' AND ISNULL(S.L_YEDEK9,0)<=0
          AND NOT(S.REDNEDEN=2 AND EXISTS(SELECT 1 FROM V3RIICO.dbo.TBLASORTIMAS A WITH(NOLOCK) WHERE A.ASORTIKOD=S.EKALAN1))
          AND (@BranchCode IS NULL OR @BranchCode='''' OR CONVERT(varchar(10),S.SUBE_KODU)=@BranchCode)
          AND (NULLIF(REPLACE(LTRIM(RTRIM(@SiparisNoCsv)),N'' '',N''''),N'''') IS NULL
               OR CHARINDEX(
                   N'',''+LTRIM(RTRIM(CONVERT(nvarchar(100),S.FISNO)))+N'','',
                   N'',''+REPLACE(@SiparisNoCsv,N'' '',N'''')+N'','')>0)
    ),
    Orders AS
    (
        SELECT FISNO,BranchCode,TargetWh,ProjectCode,MIN(OrderDate) OrderDate,OrderID,StockCode,MAX(StockName) StockName,
               MAX(CustomerCode) CustomerCode,MAX(CustomerName) CustomerName,SUM(OrderedQty) OrderedQty,
               SUM(DeliveredQty) DeliveredQty,SUM(OrderedQty-DeliveredQty) RemainingHamax,MAX(YapKod) YapKod,MAX(YapAcik) YapAcik
        FROM Base GROUP BY FISNO,BranchCode,TargetWh,ProjectCode,OrderID,StockCode
    ),
    Allocated AS
    (
        SELECT D.ExternalDocumentNo,L.ExternalLineId,SUM(ISNULL(L.AllocatedQuantity,0)) PlannedQtyAllocated
        FROM dbo.RII_WT_LINE_SOURCE L
        JOIN dbo.RII_WT_SOURCE_DOCUMENT D ON D.Id=L.WtSourceDocumentId
        JOIN dbo.RII_WT_HEADER H ON H.Id=D.WtHeaderId
        WHERE L.IsDeleted=0 AND D.IsDeleted=0 AND H.IsDeleted=0 AND H.Status<>11
        GROUP BY D.ExternalDocumentNo,L.ExternalLineId
    )
    SELECT ''L'' Mode,O.FISNO SiparisNo,O.OrderID,O.StockCode,O.StockName,O.CustomerCode,O.CustomerName,
           O.BranchCode,O.TargetWh,O.ProjectCode,O.OrderDate,
           CAST(O.OrderedQty AS decimal(18,4)) OrderedQty,CAST(O.DeliveredQty AS decimal(18,4)) DeliveredQty,
           CAST(O.RemainingHamax AS decimal(18,4)) RemainingHamax,
           CAST(ISNULL(A.PlannedQtyAllocated,0) AS decimal(18,4)) PlannedQtyAllocated,
           CAST(O.RemainingHamax-ISNULL(A.PlannedQtyAllocated,0) AS decimal(18,4)) RemainingForImport,
           O.YapKod,O.YapAcik
    FROM Orders O
    LEFT JOIN Allocated A ON A.ExternalDocumentNo=O.FISNO AND A.ExternalLineId=CONVERT(varchar(100),O.OrderID)
    WHERE O.RemainingHamax-ISNULL(A.PlannedQtyAllocated,0)>0;';
    END
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723192825_ExpandWarehouseTransferModesAndPolicy'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260723192825_ExpandWarehouseTransferModesAndPolicy', N'10.0.10');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723194049_AlignWarehouseTransferHeaderDefaults'
)
BEGIN
    DECLARE @var6 nvarchar(max);
    SELECT @var6 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[RII_WT_HEADER]') AND [c].[name] = N'ReservationPolicy');
    IF @var6 IS NOT NULL EXEC(N'ALTER TABLE [RII_WT_HEADER] DROP CONSTRAINT ' + @var6 + ';');
    ALTER TABLE [RII_WT_HEADER] ADD DEFAULT N'OnRelease' FOR [ReservationPolicy];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723194049_AlignWarehouseTransferHeaderDefaults'
)
BEGIN
    DECLARE @var7 nvarchar(max);
    SELECT @var7 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[RII_WT_HEADER]') AND [c].[name] = N'RequireTargetLocation');
    IF @var7 IS NOT NULL EXEC(N'ALTER TABLE [RII_WT_HEADER] DROP CONSTRAINT ' + @var7 + ';');
    ALTER TABLE [RII_WT_HEADER] ADD DEFAULT CAST(1 AS bit) FOR [RequireTargetLocation];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723194049_AlignWarehouseTransferHeaderDefaults'
)
BEGIN
    DECLARE @var8 nvarchar(max);
    SELECT @var8 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[RII_WT_HEADER]') AND [c].[name] = N'RequireSourceLocation');
    IF @var8 IS NOT NULL EXEC(N'ALTER TABLE [RII_WT_HEADER] DROP CONSTRAINT ' + @var8 + ';');
    ALTER TABLE [RII_WT_HEADER] ADD DEFAULT CAST(1 AS bit) FOR [RequireSourceLocation];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723194049_AlignWarehouseTransferHeaderDefaults'
)
BEGIN
    DECLARE @var9 nvarchar(max);
    SELECT @var9 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[RII_WT_HEADER]') AND [c].[name] = N'RequireAssignee');
    IF @var9 IS NOT NULL EXEC(N'ALTER TABLE [RII_WT_HEADER] DROP CONSTRAINT ' + @var9 + ';');
    ALTER TABLE [RII_WT_HEADER] ADD DEFAULT CAST(1 AS bit) FOR [RequireAssignee];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723194049_AlignWarehouseTransferHeaderDefaults'
)
BEGIN
    DECLARE @var10 nvarchar(max);
    SELECT @var10 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[RII_WT_HEADER]') AND [c].[name] = N'MinimumFulfillmentPercent');
    IF @var10 IS NOT NULL EXEC(N'ALTER TABLE [RII_WT_HEADER] DROP CONSTRAINT ' + @var10 + ';');
    ALTER TABLE [RII_WT_HEADER] ADD DEFAULT 100.0 FOR [MinimumFulfillmentPercent];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723194049_AlignWarehouseTransferHeaderDefaults'
)
BEGIN
    DECLARE @var11 nvarchar(max);
    SELECT @var11 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[RII_WT_HEADER]') AND [c].[name] = N'DirectPostingPolicy');
    IF @var11 IS NOT NULL EXEC(N'ALTER TABLE [RII_WT_HEADER] DROP CONSTRAINT ' + @var11 + ';');
    ALTER TABLE [RII_WT_HEADER] ADD DEFAULT N'TwoStepTransit' FOR [DirectPostingPolicy];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723194049_AlignWarehouseTransferHeaderDefaults'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260723194049_AlignWarehouseTransferHeaderDefaults', N'10.0.10');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723201436_AddModularShippingFoundation'
)
BEGIN
    CREATE TABLE [RII_SH_HEADER] (
        [Id] bigint NOT NULL IDENTITY,
        [DocumentSeriesId] bigint NOT NULL,
        [DocumentNo] nvarchar(50) NOT NULL,
        [DocumentDate] date NOT NULL,
        [InitiationMode] int NOT NULL,
        [SourceSystem] int NOT NULL,
        [CorrelationId] uniqueidentifier NOT NULL,
        [CustomerId] bigint NOT NULL,
        [CustomerCodeSnapshot] nvarchar(100) NOT NULL,
        [CustomerNameSnapshot] nvarchar(300) NULL,
        [SourceWarehouseId] bigint NOT NULL,
        [StagingLocationId] bigint NULL,
        [LoadingLocationId] bigint NULL,
        [Status] int NOT NULL,
        [ApprovalStatus] int NOT NULL,
        [ErpIntegrationStatus] int NOT NULL,
        [PlannedShipmentAtUtc] datetimeoffset NULL,
        [ShippedAtUtc] datetimeoffset NULL,
        [ExternalReferenceNo] nvarchar(100) NULL,
        [WaybillNo] nvarchar(50) NULL,
        [IsEDispatch] bit NOT NULL,
        [CarrierCode] nvarchar(50) NULL,
        [CarrierName] nvarchar(200) NULL,
        [VehiclePlate] nvarchar(20) NULL,
        [TrailerPlate] nvarchar(20) NULL,
        [DriverName] nvarchar(200) NULL,
        [SealNo] nvarchar(50) NULL,
        [TrackingNo] nvarchar(100) NULL,
        [Priority] tinyint NOT NULL,
        [Description] nvarchar(2000) NULL,
        [RequireApproval] bit NOT NULL,
        [RequireAssignee] bit NOT NULL,
        [AllowPartialPicking] bit NOT NULL,
        [AllowPartialShipment] bit NOT NULL,
        [RequireSourceLocation] bit NOT NULL,
        [RequireShipmentInformation] bit NOT NULL,
        [RequireLoadingConfirmation] bit NOT NULL,
        [AutoReleaseTaskBased] bit NOT NULL,
        [AutoPostErpAfterApproval] bit NOT NULL,
        [MinimumFulfillmentPercent] decimal(9,4) NOT NULL,
        [OverPickTolerancePercent] decimal(9,4) NOT NULL,
        [ReservationPolicy] nvarchar(30) NOT NULL,
        [PackingPolicy] nvarchar(30) NOT NULL,
        [ShortagePolicy] nvarchar(30) NOT NULL,
        [OverPickPolicy] nvarchar(30) NOT NULL,
        [RowVersion] rowversion NOT NULL,
        [BranchCode] nvarchar(10) NOT NULL DEFAULT N'0',
        [CreatedDate] datetime2 NULL,
        [UpdatedDate] datetime2 NULL,
        [DeletedDate] datetime2 NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [CreatedBy] bigint NULL,
        [UpdatedBy] bigint NULL,
        [DeletedBy] bigint NULL,
        CONSTRAINT [PK_RII_SH_HEADER] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723201436_AddModularShippingFoundation'
)
BEGIN
    CREATE TABLE [RII_SH_POLICIES] (
        [Id] bigint NOT NULL IDENTITY,
        [PolicyKey] nvarchar(30) NOT NULL,
        [AllowOrderBasedTask] bit NOT NULL,
        [AllowStockBasedTask] bit NOT NULL,
        [AllowOrderBasedDirect] bit NOT NULL,
        [AllowStockBasedDirect] bit NOT NULL,
        [RequireApproval] bit NOT NULL,
        [RequireAssigneeForTask] bit NOT NULL,
        [AllowMultipleAssignees] bit NOT NULL,
        [AutoReleaseTaskBased] bit NOT NULL,
        [AllowPartialPicking] bit NOT NULL,
        [AllowPartialShipment] bit NOT NULL,
        [RequireSourceLocation] bit NOT NULL,
        [RequireShipmentInformation] bit NOT NULL,
        [RequireLoadingConfirmation] bit NOT NULL,
        [AutoPostErpAfterApproval] bit NOT NULL,
        [MinimumFulfillmentPercent] decimal(9,4) NOT NULL,
        [OverPickTolerancePercent] decimal(9,4) NOT NULL,
        [ReservationPolicy] nvarchar(30) NOT NULL,
        [PackingPolicy] nvarchar(30) NOT NULL,
        [ShortagePolicy] nvarchar(30) NOT NULL,
        [OverPickPolicy] nvarchar(30) NOT NULL,
        [RowVersion] rowversion NOT NULL,
        [BranchCode] nvarchar(10) NOT NULL DEFAULT N'0',
        [CreatedDate] datetime2 NULL,
        [UpdatedDate] datetime2 NULL,
        [DeletedDate] datetime2 NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [CreatedBy] bigint NULL,
        [UpdatedBy] bigint NULL,
        [DeletedBy] bigint NULL,
        CONSTRAINT [PK_RII_SH_POLICIES] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_RII_SH_POLICY_FULFILLMENT] CHECK ([MinimumFulfillmentPercent] >= 0 AND [MinimumFulfillmentPercent] <= 100),
        CONSTRAINT [CK_RII_SH_POLICY_OVERPICK] CHECK ([OverPickTolerancePercent] >= 0 AND [OverPickTolerancePercent] <= 100)
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723201436_AddModularShippingFoundation'
)
BEGIN
    CREATE TABLE [RII_SH_LINE] (
        [Id] bigint NOT NULL IDENTITY,
        [ShipmentHeaderId] bigint NOT NULL,
        [LineNo] int NOT NULL,
        [StockId] bigint NOT NULL,
        [StockCodeSnapshot] nvarchar(100) NOT NULL,
        [StockNameSnapshot] nvarchar(300) NULL,
        [YapCodeId] bigint NULL,
        [YapCodeSnapshot] nvarchar(100) NULL,
        [UnitCode] nvarchar(20) NOT NULL,
        [RequestedQuantity] decimal(20,6) NOT NULL,
        [ReservedQuantity] decimal(20,6) NOT NULL,
        [PickedQuantity] decimal(20,6) NOT NULL,
        [PackedQuantity] decimal(20,6) NOT NULL,
        [LoadedQuantity] decimal(20,6) NOT NULL,
        [ShippedQuantity] decimal(20,6) NOT NULL,
        [ShortClosedQuantity] decimal(20,6) NOT NULL,
        [TrackingType] int NOT NULL,
        [RequireHandlingUnit] bit NOT NULL,
        [DefaultSourceLocationId] bigint NULL,
        [Status] int NOT NULL,
        [Description] nvarchar(1000) NULL,
        [RowVersion] rowversion NOT NULL,
        [BranchCode] nvarchar(10) NOT NULL DEFAULT N'0',
        [CreatedDate] datetime2 NULL,
        [UpdatedDate] datetime2 NULL,
        [DeletedDate] datetime2 NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [CreatedBy] bigint NULL,
        [UpdatedBy] bigint NULL,
        [DeletedBy] bigint NULL,
        CONSTRAINT [PK_RII_SH_LINE] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_RII_SH_LINE_QTY] CHECK ([RequestedQuantity] > 0 AND [ReservedQuantity] >= 0 AND [PickedQuantity] >= 0 AND [PackedQuantity] >= 0 AND [LoadedQuantity] >= 0 AND [ShippedQuantity] >= 0 AND [ShortClosedQuantity] >= 0),
        CONSTRAINT [FK_RII_SH_LINE_RII_SH_HEADER_ShipmentHeaderId] FOREIGN KEY ([ShipmentHeaderId]) REFERENCES [RII_SH_HEADER] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723201436_AddModularShippingFoundation'
)
BEGIN
    CREATE TABLE [RII_SH_SOURCE_DOCUMENT] (
        [Id] bigint NOT NULL IDENTITY,
        [ShipmentHeaderId] bigint NOT NULL,
        [SourceDocumentType] nvarchar(50) NOT NULL,
        [ExternalDocumentNo] nvarchar(100) NOT NULL,
        [ExternalDocumentId] nvarchar(100) NULL,
        [ExternalDocumentDate] date NULL,
        [ExternalStatus] nvarchar(50) NULL,
        [BranchCode] nvarchar(10) NOT NULL DEFAULT N'0',
        [CreatedDate] datetime2 NULL,
        [UpdatedDate] datetime2 NULL,
        [DeletedDate] datetime2 NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [CreatedBy] bigint NULL,
        [UpdatedBy] bigint NULL,
        [DeletedBy] bigint NULL,
        CONSTRAINT [PK_RII_SH_SOURCE_DOCUMENT] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_RII_SH_SOURCE_DOCUMENT_RII_SH_HEADER_ShipmentHeaderId] FOREIGN KEY ([ShipmentHeaderId]) REFERENCES [RII_SH_HEADER] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723201436_AddModularShippingFoundation'
)
BEGIN
    CREATE TABLE [RII_SH_STATUS_HISTORY] (
        [Id] bigint NOT NULL IDENTITY,
        [ShipmentHeaderId] bigint NOT NULL,
        [FromStatus] nvarchar(50) NULL,
        [ToStatus] nvarchar(50) NOT NULL,
        [Description] nvarchar(1000) NULL,
        [ChangedAtUtc] datetimeoffset NOT NULL,
        [ChangedBy] bigint NOT NULL,
        [CorrelationId] uniqueidentifier NOT NULL,
        [BranchCode] nvarchar(10) NOT NULL DEFAULT N'0',
        [CreatedDate] datetime2 NULL,
        [UpdatedDate] datetime2 NULL,
        [DeletedDate] datetime2 NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [CreatedBy] bigint NULL,
        [UpdatedBy] bigint NULL,
        [DeletedBy] bigint NULL,
        CONSTRAINT [PK_RII_SH_STATUS_HISTORY] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_RII_SH_STATUS_HISTORY_RII_SH_HEADER_ShipmentHeaderId] FOREIGN KEY ([ShipmentHeaderId]) REFERENCES [RII_SH_HEADER] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723201436_AddModularShippingFoundation'
)
BEGIN
    CREATE TABLE [RII_SH_TASK] (
        [Id] bigint NOT NULL IDENTITY,
        [ShipmentHeaderId] bigint NOT NULL,
        [TaskNo] nvarchar(50) NOT NULL,
        [TaskType] int NOT NULL,
        [WarehouseId] bigint NOT NULL,
        [Status] int NOT NULL,
        [Priority] tinyint NOT NULL,
        [PlannedAtUtc] datetimeoffset NULL,
        [RowVersion] rowversion NOT NULL,
        [BranchCode] nvarchar(10) NOT NULL DEFAULT N'0',
        [CreatedDate] datetime2 NULL,
        [UpdatedDate] datetime2 NULL,
        [DeletedDate] datetime2 NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [CreatedBy] bigint NULL,
        [UpdatedBy] bigint NULL,
        [DeletedBy] bigint NULL,
        CONSTRAINT [PK_RII_SH_TASK] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_RII_SH_TASK_RII_SH_HEADER_ShipmentHeaderId] FOREIGN KEY ([ShipmentHeaderId]) REFERENCES [RII_SH_HEADER] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723201436_AddModularShippingFoundation'
)
BEGIN
    CREATE TABLE [RII_SH_TRACKING] (
        [Id] bigint NOT NULL IDENTITY,
        [ShipmentLineId] bigint NOT NULL,
        [HandlingUnitNo] nvarchar(100) NULL,
        [ContainerNo] nvarchar(100) NULL,
        [LotNo] nvarchar(100) NULL,
        [SerialNo] nvarchar(200) NULL,
        [PlannedQuantity] decimal(20,6) NOT NULL,
        [PickedQuantity] decimal(20,6) NOT NULL,
        [PackedQuantity] decimal(20,6) NOT NULL,
        [LoadedQuantity] decimal(20,6) NOT NULL,
        [ShippedQuantity] decimal(20,6) NOT NULL,
        [SourceLocationId] bigint NULL,
        [RowVersion] rowversion NOT NULL,
        [BranchCode] nvarchar(10) NOT NULL DEFAULT N'0',
        [CreatedDate] datetime2 NULL,
        [UpdatedDate] datetime2 NULL,
        [DeletedDate] datetime2 NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [CreatedBy] bigint NULL,
        [UpdatedBy] bigint NULL,
        [DeletedBy] bigint NULL,
        CONSTRAINT [PK_RII_SH_TRACKING] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_RII_SH_TRACKING_QTY] CHECK ([PlannedQuantity] > 0 AND [PickedQuantity] >= 0 AND [PackedQuantity] >= 0 AND [LoadedQuantity] >= 0 AND [ShippedQuantity] >= 0),
        CONSTRAINT [FK_RII_SH_TRACKING_RII_SH_LINE_ShipmentLineId] FOREIGN KEY ([ShipmentLineId]) REFERENCES [RII_SH_LINE] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723201436_AddModularShippingFoundation'
)
BEGIN
    CREATE TABLE [RII_SH_LINE_SOURCE] (
        [Id] bigint NOT NULL IDENTITY,
        [ShipmentLineId] bigint NOT NULL,
        [ShipmentSourceDocumentId] bigint NOT NULL,
        [ExternalLineId] nvarchar(100) NOT NULL,
        [ExternalLineNo] int NULL,
        [ExternalStockCode] nvarchar(100) NOT NULL,
        [ExternalYapCode] nvarchar(100) NULL,
        [OrderedQuantity] decimal(20,6) NOT NULL,
        [PreviouslyShippedQuantity] decimal(20,6) NOT NULL,
        [AllocatedQuantity] decimal(20,6) NOT NULL,
        [UnitCode] nvarchar(20) NOT NULL,
        [BranchCode] nvarchar(10) NOT NULL DEFAULT N'0',
        [CreatedDate] datetime2 NULL,
        [UpdatedDate] datetime2 NULL,
        [DeletedDate] datetime2 NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [CreatedBy] bigint NULL,
        [UpdatedBy] bigint NULL,
        [DeletedBy] bigint NULL,
        CONSTRAINT [PK_RII_SH_LINE_SOURCE] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_RII_SH_LINE_SOURCE_RII_SH_LINE_ShipmentLineId] FOREIGN KEY ([ShipmentLineId]) REFERENCES [RII_SH_LINE] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_RII_SH_LINE_SOURCE_RII_SH_SOURCE_DOCUMENT_ShipmentSourceDocumentId] FOREIGN KEY ([ShipmentSourceDocumentId]) REFERENCES [RII_SH_SOURCE_DOCUMENT] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723201436_AddModularShippingFoundation'
)
BEGIN
    CREATE TABLE [RII_SH_TASK_ASSIGNMENT] (
        [Id] bigint NOT NULL IDENTITY,
        [ShipmentTaskId] bigint NOT NULL,
        [UserId] bigint NOT NULL,
        [IsPrimary] bit NOT NULL,
        [AssignedAtUtc] datetimeoffset NOT NULL,
        [AssignedBy] bigint NOT NULL,
        [AcceptedAtUtc] datetimeoffset NULL,
        [BranchCode] nvarchar(10) NOT NULL DEFAULT N'0',
        [CreatedDate] datetime2 NULL,
        [UpdatedDate] datetime2 NULL,
        [DeletedDate] datetime2 NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [CreatedBy] bigint NULL,
        [UpdatedBy] bigint NULL,
        [DeletedBy] bigint NULL,
        CONSTRAINT [PK_RII_SH_TASK_ASSIGNMENT] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_RII_SH_TASK_ASSIGNMENT_RII_SH_TASK_ShipmentTaskId] FOREIGN KEY ([ShipmentTaskId]) REFERENCES [RII_SH_TASK] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723201436_AddModularShippingFoundation'
)
BEGIN
    CREATE TABLE [RII_SH_TASK_LINE] (
        [Id] bigint NOT NULL IDENTITY,
        [ShipmentTaskId] bigint NOT NULL,
        [ShipmentLineId] bigint NOT NULL,
        [PlannedQuantity] decimal(20,6) NOT NULL,
        [ProcessedQuantity] decimal(20,6) NOT NULL,
        [SourceLocationId] bigint NULL,
        [RowVersion] rowversion NOT NULL,
        [BranchCode] nvarchar(10) NOT NULL DEFAULT N'0',
        [CreatedDate] datetime2 NULL,
        [UpdatedDate] datetime2 NULL,
        [DeletedDate] datetime2 NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [CreatedBy] bigint NULL,
        [UpdatedBy] bigint NULL,
        [DeletedBy] bigint NULL,
        CONSTRAINT [PK_RII_SH_TASK_LINE] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_RII_SH_TASK_LINE_QTY] CHECK ([PlannedQuantity] > 0 AND [ProcessedQuantity] >= 0),
        CONSTRAINT [FK_RII_SH_TASK_LINE_RII_SH_LINE_ShipmentLineId] FOREIGN KEY ([ShipmentLineId]) REFERENCES [RII_SH_LINE] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_RII_SH_TASK_LINE_RII_SH_TASK_ShipmentTaskId] FOREIGN KEY ([ShipmentTaskId]) REFERENCES [RII_SH_TASK] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723201436_AddModularShippingFoundation'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'AvailableOnMobile', N'AvailableOnWeb', N'BranchCode', N'Code', N'CreatedBy', N'CreatedDate', N'DeletedBy', N'DeletedDate', N'Description', N'IsActive', N'Name', N'UpdatedBy', N'UpdatedDate') AND [object_id] = OBJECT_ID(N'[RII_PERMISSION_DEFINITIONS]'))
        SET IDENTITY_INSERT [RII_PERMISSION_DEFINITIONS] ON;
    EXEC(N'INSERT INTO [RII_PERMISSION_DEFINITIONS] ([Id], [AvailableOnMobile], [AvailableOnWeb], [BranchCode], [Code], [CreatedBy], [CreatedDate], [DeletedBy], [DeletedDate], [Description], [IsActive], [Name], [UpdatedBy], [UpdatedDate])
    VALUES (CAST(2000 AS bigint), CAST(0 AS bit), CAST(1 AS bit), N''0'', N''WMS.SHIPPING.VIEW'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, NULL, CAST(1 AS bit), N''Sevk kayıtlarını görüntüle'', NULL, NULL),
    (CAST(2001 AS bigint), CAST(0 AS bit), CAST(1 AS bit), N''0'', N''WMS.SHIPPING.CREATE'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, NULL, CAST(1 AS bit), N''Sevk taslağı oluştur'', NULL, NULL),
    (CAST(2002 AS bigint), CAST(0 AS bit), CAST(1 AS bit), N''0'', N''WMS.SHIPPING.OPERATE'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, NULL, CAST(1 AS bit), N''Toplama paketleme ve yükleme işlemlerini yürüt'', NULL, NULL),
    (CAST(2003 AS bigint), CAST(0 AS bit), CAST(1 AS bit), N''0'', N''WMS.SHIPPING.APPROVE'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, NULL, CAST(1 AS bit), N''Sevki onayla'', NULL, NULL),
    (CAST(2004 AS bigint), CAST(0 AS bit), CAST(1 AS bit), N''0'', N''WMS.SHIPPING.SETTINGS.VIEW'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, NULL, CAST(1 AS bit), N''Sevk ayarlarını görüntüle'', NULL, NULL),
    (CAST(2005 AS bigint), CAST(0 AS bit), CAST(1 AS bit), N''0'', N''WMS.SHIPPING.SETTINGS.MANAGE'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, NULL, CAST(1 AS bit), N''Sevk ayarlarını yönet'', NULL, NULL)');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'AvailableOnMobile', N'AvailableOnWeb', N'BranchCode', N'Code', N'CreatedBy', N'CreatedDate', N'DeletedBy', N'DeletedDate', N'Description', N'IsActive', N'Name', N'UpdatedBy', N'UpdatedDate') AND [object_id] = OBJECT_ID(N'[RII_PERMISSION_DEFINITIONS]'))
        SET IDENTITY_INSERT [RII_PERMISSION_DEFINITIONS] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723201436_AddModularShippingFoundation'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'BranchCode', N'CreatedBy', N'CreatedDate', N'DeletedBy', N'DeletedDate', N'PermissionDefinitionId', N'PermissionGroupId', N'UpdatedBy', N'UpdatedDate') AND [object_id] = OBJECT_ID(N'[RII_PERMISSION_GROUP_PERMISSIONS]'))
        SET IDENTITY_INSERT [RII_PERMISSION_GROUP_PERMISSIONS] ON;
    EXEC(N'INSERT INTO [RII_PERMISSION_GROUP_PERMISSIONS] ([Id], [BranchCode], [CreatedBy], [CreatedDate], [DeletedBy], [DeletedDate], [PermissionDefinitionId], [PermissionGroupId], [UpdatedBy], [UpdatedDate])
    VALUES (CAST(2000 AS bigint), N''0'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, CAST(2000 AS bigint), CAST(1001 AS bigint), NULL, NULL),
    (CAST(2001 AS bigint), N''0'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, CAST(2001 AS bigint), CAST(1001 AS bigint), NULL, NULL),
    (CAST(2002 AS bigint), N''0'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, CAST(2002 AS bigint), CAST(1001 AS bigint), NULL, NULL),
    (CAST(2003 AS bigint), N''0'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, CAST(2003 AS bigint), CAST(1001 AS bigint), NULL, NULL),
    (CAST(2004 AS bigint), N''0'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, CAST(2004 AS bigint), CAST(1001 AS bigint), NULL, NULL),
    (CAST(2005 AS bigint), N''0'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, CAST(2005 AS bigint), CAST(1001 AS bigint), NULL, NULL)');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'BranchCode', N'CreatedBy', N'CreatedDate', N'DeletedBy', N'DeletedDate', N'PermissionDefinitionId', N'PermissionGroupId', N'UpdatedBy', N'UpdatedDate') AND [object_id] = OBJECT_ID(N'[RII_PERMISSION_GROUP_PERMISSIONS]'))
        SET IDENTITY_INSERT [RII_PERMISSION_GROUP_PERMISSIONS] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723201436_AddModularShippingFoundation'
)
BEGIN
    CREATE UNIQUE INDEX [IX_RII_SH_HEADER_BranchCode_DocumentNo] ON [RII_SH_HEADER] ([BranchCode], [DocumentNo]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723201436_AddModularShippingFoundation'
)
BEGIN
    CREATE INDEX [IX_RII_SH_HEADER_BranchCode_Status_PlannedShipmentAtUtc] ON [RII_SH_HEADER] ([BranchCode], [Status], [PlannedShipmentAtUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723201436_AddModularShippingFoundation'
)
BEGIN
    CREATE UNIQUE INDEX [IX_RII_SH_HEADER_CorrelationId] ON [RII_SH_HEADER] ([CorrelationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723201436_AddModularShippingFoundation'
)
BEGIN
    CREATE INDEX [IX_RII_SH_HEADER_IsDeleted] ON [RII_SH_HEADER] ([IsDeleted]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723201436_AddModularShippingFoundation'
)
BEGIN
    CREATE INDEX [IX_RII_SH_LINE_IsDeleted] ON [RII_SH_LINE] ([IsDeleted]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723201436_AddModularShippingFoundation'
)
BEGIN
    CREATE UNIQUE INDEX [IX_RII_SH_LINE_ShipmentHeaderId_LineNo] ON [RII_SH_LINE] ([ShipmentHeaderId], [LineNo]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723201436_AddModularShippingFoundation'
)
BEGIN
    CREATE INDEX [IX_RII_SH_LINE_SOURCE_IsDeleted] ON [RII_SH_LINE_SOURCE] ([IsDeleted]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723201436_AddModularShippingFoundation'
)
BEGIN
    CREATE INDEX [IX_RII_SH_LINE_SOURCE_ShipmentLineId] ON [RII_SH_LINE_SOURCE] ([ShipmentLineId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723201436_AddModularShippingFoundation'
)
BEGIN
    CREATE UNIQUE INDEX [IX_RII_SH_LINE_SOURCE_ShipmentSourceDocumentId_ExternalLineId] ON [RII_SH_LINE_SOURCE] ([ShipmentSourceDocumentId], [ExternalLineId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723201436_AddModularShippingFoundation'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_RII_SH_POLICIES_BranchCode_PolicyKey] ON [RII_SH_POLICIES] ([BranchCode], [PolicyKey]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723201436_AddModularShippingFoundation'
)
BEGIN
    CREATE INDEX [IX_RII_SH_POLICIES_IsDeleted] ON [RII_SH_POLICIES] ([IsDeleted]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723201436_AddModularShippingFoundation'
)
BEGIN
    CREATE INDEX [IX_RII_SH_SOURCE_DOCUMENT_IsDeleted] ON [RII_SH_SOURCE_DOCUMENT] ([IsDeleted]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723201436_AddModularShippingFoundation'
)
BEGIN
    CREATE UNIQUE INDEX [IX_RII_SH_SOURCE_DOCUMENT_ShipmentHeaderId_SourceDocumentType_ExternalDocumentNo] ON [RII_SH_SOURCE_DOCUMENT] ([ShipmentHeaderId], [SourceDocumentType], [ExternalDocumentNo]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723201436_AddModularShippingFoundation'
)
BEGIN
    CREATE INDEX [IX_RII_SH_STATUS_HISTORY_IsDeleted] ON [RII_SH_STATUS_HISTORY] ([IsDeleted]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723201436_AddModularShippingFoundation'
)
BEGIN
    CREATE INDEX [IX_RII_SH_STATUS_HISTORY_ShipmentHeaderId_ChangedAtUtc] ON [RII_SH_STATUS_HISTORY] ([ShipmentHeaderId], [ChangedAtUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723201436_AddModularShippingFoundation'
)
BEGIN
    CREATE UNIQUE INDEX [IX_RII_SH_TASK_BranchCode_TaskNo] ON [RII_SH_TASK] ([BranchCode], [TaskNo]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723201436_AddModularShippingFoundation'
)
BEGIN
    CREATE INDEX [IX_RII_SH_TASK_IsDeleted] ON [RII_SH_TASK] ([IsDeleted]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723201436_AddModularShippingFoundation'
)
BEGIN
    CREATE INDEX [IX_RII_SH_TASK_ShipmentHeaderId] ON [RII_SH_TASK] ([ShipmentHeaderId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723201436_AddModularShippingFoundation'
)
BEGIN
    CREATE INDEX [IX_RII_SH_TASK_ASSIGNMENT_IsDeleted] ON [RII_SH_TASK_ASSIGNMENT] ([IsDeleted]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723201436_AddModularShippingFoundation'
)
BEGIN
    CREATE UNIQUE INDEX [IX_RII_SH_TASK_ASSIGNMENT_ShipmentTaskId_UserId] ON [RII_SH_TASK_ASSIGNMENT] ([ShipmentTaskId], [UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723201436_AddModularShippingFoundation'
)
BEGIN
    CREATE INDEX [IX_RII_SH_TASK_LINE_IsDeleted] ON [RII_SH_TASK_LINE] ([IsDeleted]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723201436_AddModularShippingFoundation'
)
BEGIN
    CREATE INDEX [IX_RII_SH_TASK_LINE_ShipmentLineId] ON [RII_SH_TASK_LINE] ([ShipmentLineId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723201436_AddModularShippingFoundation'
)
BEGIN
    CREATE UNIQUE INDEX [IX_RII_SH_TASK_LINE_ShipmentTaskId_ShipmentLineId] ON [RII_SH_TASK_LINE] ([ShipmentTaskId], [ShipmentLineId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723201436_AddModularShippingFoundation'
)
BEGIN
    CREATE INDEX [IX_RII_SH_TRACKING_IsDeleted] ON [RII_SH_TRACKING] ([IsDeleted]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723201436_AddModularShippingFoundation'
)
BEGIN
    CREATE INDEX [IX_RII_SH_TRACKING_ShipmentLineId_SerialNo] ON [RII_SH_TRACKING] ([ShipmentLineId], [SerialNo]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723201436_AddModularShippingFoundation'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260723201436_AddModularShippingFoundation', N'10.0.10');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723204130_HardenShippingAndTransferAllocations'
)
BEGIN
    DROP INDEX [IX_RII_WT_LINE_SOURCE_WtLineId] ON [RII_WT_LINE_SOURCE];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723204130_HardenShippingAndTransferAllocations'
)
BEGIN
    DROP INDEX [IX_RII_WT_LINE_SOURCE_WtSourceDocumentId_ExternalLineId] ON [RII_WT_LINE_SOURCE];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723204130_HardenShippingAndTransferAllocations'
)
BEGIN
    DROP INDEX [IX_RII_SH_LINE_SOURCE_ShipmentLineId] ON [RII_SH_LINE_SOURCE];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723204130_HardenShippingAndTransferAllocations'
)
BEGIN
    DROP INDEX [IX_RII_SH_LINE_SOURCE_ShipmentSourceDocumentId_ExternalLineId] ON [RII_SH_LINE_SOURCE];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723204130_HardenShippingAndTransferAllocations'
)
BEGIN
    CREATE UNIQUE INDEX [IX_RII_WT_LINE_SOURCE_WtLineId_WtSourceDocumentId_ExternalLineId] ON [RII_WT_LINE_SOURCE] ([WtLineId], [WtSourceDocumentId], [ExternalLineId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723204130_HardenShippingAndTransferAllocations'
)
BEGIN
    EXEC sys.sp_executesql N'CREATE INDEX [IX_RII_WT_LINE_SOURCE_WtSourceDocumentId_ExternalLineId] ON [RII_WT_LINE_SOURCE] ([WtSourceDocumentId], [ExternalLineId]);';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723204130_HardenShippingAndTransferAllocations'
)
BEGIN
    CREATE UNIQUE INDEX [IX_RII_SH_LINE_SOURCE_ShipmentLineId_ShipmentSourceDocumentId_ExternalLineId] ON [RII_SH_LINE_SOURCE] ([ShipmentLineId], [ShipmentSourceDocumentId], [ExternalLineId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723204130_HardenShippingAndTransferAllocations'
)
BEGIN
    EXEC sys.sp_executesql N'CREATE INDEX [IX_RII_SH_LINE_SOURCE_ShipmentSourceDocumentId_ExternalLineId] ON [RII_SH_LINE_SOURCE] ([ShipmentSourceDocumentId], [ExternalLineId]);';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723204130_HardenShippingAndTransferAllocations'
)
BEGIN
    IF OBJECT_ID(N'dbo.RII_FN_SH_HEADER') IS NULL
    BEGIN
        EXEC sys.sp_executesql N'CREATE FUNCTION dbo.RII_FN_SH_HEADER
    (
        @CustomerCode varchar(30),
        @BranchCode varchar(10)=NULL
    )
    RETURNS TABLE
    AS
    RETURN
    WITH OrderNumbers AS
    (
        SELECT DISTINCT S.FISNO
        FROM V3RIICO.dbo.TBLSIPATRA S WITH (NOLOCK)
        WHERE S.STHAR_ACIKLAMA=@CustomerCode
          AND (@BranchCode IS NULL OR @BranchCode='''' OR CONVERT(varchar(10),S.SUBE_KODU)=@BranchCode)
        UNION
        SELECT DISTINCT M.FATIRS_NO
        FROM V3RIICO.dbo.TBLSIPAMAS M WITH (NOLOCK)
        WHERE M.CARI_KODU=@CustomerCode
          AND (@BranchCode IS NULL OR @BranchCode='''' OR CONVERT(varchar(10),M.SUBE_KODU)=@BranchCode)
    ),
    Orders AS
    (
        SELECT S.FISNO,MIN(S.SUBE_KODU) BranchCode,MIN(S.DEPO_KODU) TargetWh,MIN(S.STHAR_TARIH) OrderDate,
               SUM(CASE WHEN ISNULL(S.L_YEDEK9,0)=-1 THEN ISNULL(S.STHAR_GCMIK2,0) ELSE ISNULL(S.STHAR_GCMIK,0) END) OrderedQty,
               SUM(ISNULL(S.FIRMA_DOVTUT,0)) DeliveredQty
        FROM V3RIICO.dbo.TBLSIPATRA S WITH (NOLOCK)
        JOIN OrderNumbers N ON N.FISNO=S.FISNO
        WHERE S.STHAR_FTIRSIP=''6'' AND S.STHAR_GCKOD=''C'' AND S.STHAR_HTUR<>''K'' AND ISNULL(S.L_YEDEK9,0)<=0
          AND NOT(S.REDNEDEN=2 AND EXISTS(SELECT 1 FROM V3RIICO.dbo.TBLASORTIMAS A WITH(NOLOCK) WHERE A.ASORTIKOD=S.EKALAN1))
          AND (@BranchCode IS NULL OR @BranchCode='''' OR CONVERT(varchar(10),S.SUBE_KODU)=@BranchCode)
        GROUP BY S.FISNO
    ),
    Allocated AS
    (
        SELECT D.ExternalDocumentNo,SUM(ISNULL(L.AllocatedQuantity,0)) PlannedQtyAllocated
        FROM dbo.RII_SH_LINE_SOURCE L
        JOIN dbo.RII_SH_SOURCE_DOCUMENT D ON D.Id=L.ShipmentSourceDocumentId
        JOIN dbo.RII_SH_HEADER H ON H.Id=D.ShipmentHeaderId
        WHERE L.IsDeleted=0 AND D.IsDeleted=0 AND H.IsDeleted=0 AND H.Status<>11
        GROUP BY D.ExternalDocumentNo
    )
    SELECT ''H'' Mode,O.FISNO SiparisNo,CAST(NULL AS int) OrderID,@CustomerCode CustomerCode,
           (SELECT TOP(1) C.CARI_ISIM FROM V3RIICO.dbo.TBLCASABIT C WITH(NOLOCK) WHERE C.CARI_KOD=@CustomerCode) CustomerName,
           O.BranchCode,O.TargetWh,CAST(NULL AS varchar(50)) ProjectCode,O.OrderDate,
           CAST(O.OrderedQty AS decimal(18,4)) OrderedQty,CAST(O.DeliveredQty AS decimal(18,4)) DeliveredQty,
           CAST(O.OrderedQty-O.DeliveredQty AS decimal(18,4)) RemainingHamax,
           CAST(ISNULL(A.PlannedQtyAllocated,0) AS decimal(18,4)) PlannedQtyAllocated,
           CAST((O.OrderedQty-O.DeliveredQty)-ISNULL(A.PlannedQtyAllocated,0) AS decimal(18,4)) RemainingForImport
    FROM Orders O
    LEFT JOIN Allocated A ON A.ExternalDocumentNo=O.FISNO
    WHERE (O.OrderedQty-O.DeliveredQty)-ISNULL(A.PlannedQtyAllocated,0)>0;';
    END
    ELSE
    BEGIN
        EXEC sys.sp_executesql N'ALTER FUNCTION dbo.RII_FN_SH_HEADER
    (
        @CustomerCode varchar(30),
        @BranchCode varchar(10)=NULL
    )
    RETURNS TABLE
    AS
    RETURN
    WITH OrderNumbers AS
    (
        SELECT DISTINCT S.FISNO
        FROM V3RIICO.dbo.TBLSIPATRA S WITH (NOLOCK)
        WHERE S.STHAR_ACIKLAMA=@CustomerCode
          AND (@BranchCode IS NULL OR @BranchCode='''' OR CONVERT(varchar(10),S.SUBE_KODU)=@BranchCode)
        UNION
        SELECT DISTINCT M.FATIRS_NO
        FROM V3RIICO.dbo.TBLSIPAMAS M WITH (NOLOCK)
        WHERE M.CARI_KODU=@CustomerCode
          AND (@BranchCode IS NULL OR @BranchCode='''' OR CONVERT(varchar(10),M.SUBE_KODU)=@BranchCode)
    ),
    Orders AS
    (
        SELECT S.FISNO,MIN(S.SUBE_KODU) BranchCode,MIN(S.DEPO_KODU) TargetWh,MIN(S.STHAR_TARIH) OrderDate,
               SUM(CASE WHEN ISNULL(S.L_YEDEK9,0)=-1 THEN ISNULL(S.STHAR_GCMIK2,0) ELSE ISNULL(S.STHAR_GCMIK,0) END) OrderedQty,
               SUM(ISNULL(S.FIRMA_DOVTUT,0)) DeliveredQty
        FROM V3RIICO.dbo.TBLSIPATRA S WITH (NOLOCK)
        JOIN OrderNumbers N ON N.FISNO=S.FISNO
        WHERE S.STHAR_FTIRSIP=''6'' AND S.STHAR_GCKOD=''C'' AND S.STHAR_HTUR<>''K'' AND ISNULL(S.L_YEDEK9,0)<=0
          AND NOT(S.REDNEDEN=2 AND EXISTS(SELECT 1 FROM V3RIICO.dbo.TBLASORTIMAS A WITH(NOLOCK) WHERE A.ASORTIKOD=S.EKALAN1))
          AND (@BranchCode IS NULL OR @BranchCode='''' OR CONVERT(varchar(10),S.SUBE_KODU)=@BranchCode)
        GROUP BY S.FISNO
    ),
    Allocated AS
    (
        SELECT D.ExternalDocumentNo,SUM(ISNULL(L.AllocatedQuantity,0)) PlannedQtyAllocated
        FROM dbo.RII_SH_LINE_SOURCE L
        JOIN dbo.RII_SH_SOURCE_DOCUMENT D ON D.Id=L.ShipmentSourceDocumentId
        JOIN dbo.RII_SH_HEADER H ON H.Id=D.ShipmentHeaderId
        WHERE L.IsDeleted=0 AND D.IsDeleted=0 AND H.IsDeleted=0 AND H.Status<>11
        GROUP BY D.ExternalDocumentNo
    )
    SELECT ''H'' Mode,O.FISNO SiparisNo,CAST(NULL AS int) OrderID,@CustomerCode CustomerCode,
           (SELECT TOP(1) C.CARI_ISIM FROM V3RIICO.dbo.TBLCASABIT C WITH(NOLOCK) WHERE C.CARI_KOD=@CustomerCode) CustomerName,
           O.BranchCode,O.TargetWh,CAST(NULL AS varchar(50)) ProjectCode,O.OrderDate,
           CAST(O.OrderedQty AS decimal(18,4)) OrderedQty,CAST(O.DeliveredQty AS decimal(18,4)) DeliveredQty,
           CAST(O.OrderedQty-O.DeliveredQty AS decimal(18,4)) RemainingHamax,
           CAST(ISNULL(A.PlannedQtyAllocated,0) AS decimal(18,4)) PlannedQtyAllocated,
           CAST((O.OrderedQty-O.DeliveredQty)-ISNULL(A.PlannedQtyAllocated,0) AS decimal(18,4)) RemainingForImport
    FROM Orders O
    LEFT JOIN Allocated A ON A.ExternalDocumentNo=O.FISNO
    WHERE (O.OrderedQty-O.DeliveredQty)-ISNULL(A.PlannedQtyAllocated,0)>0;';
    END
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723204130_HardenShippingAndTransferAllocations'
)
BEGIN
    IF OBJECT_ID(N'dbo.RII_FN_SH_LINE') IS NULL
    BEGIN
        EXEC sys.sp_executesql N'CREATE FUNCTION dbo.RII_FN_SH_LINE
    (
        @SiparisNoCsv nvarchar(max),
        @BranchCode varchar(10)=NULL
    )
    RETURNS TABLE
    AS
    RETURN
    WITH Base AS
    (
        SELECT S.FISNO,S.SUBE_KODU BranchCode,S.DEPO_KODU TargetWh,S.PROJE_KODU ProjectCode,S.STHAR_TARIH OrderDate,
               S.INCKEYNO OrderID,S.STOK_KODU StockCode,ST.STOK_ADI StockName,
               COALESCE(M.CARI_KODU,S.STHAR_ACIKLAMA) CustomerCode,C.CARI_ISIM CustomerName,
               CASE WHEN ISNULL(S.L_YEDEK9,0)=-1 THEN ISNULL(S.STHAR_GCMIK2,0) ELSE ISNULL(S.STHAR_GCMIK,0) END OrderedQty,
               ISNULL(S.FIRMA_DOVTUT,0) DeliveredQty,CAST('''' AS varchar(50)) YapKod,CAST('''' AS varchar(200)) YapAcik
        FROM V3RIICO.dbo.TBLSIPATRA S WITH(NOLOCK)
        LEFT JOIN V3RIICO.dbo.TBLSIPAMAS M WITH(NOLOCK) ON M.FATIRS_NO=S.FISNO AND M.FTIRSIP=S.STHAR_FTIRSIP
        LEFT JOIN V3RIICO.dbo.TBLCASABIT C WITH(NOLOCK) ON C.CARI_KOD=COALESCE(M.CARI_KODU,S.STHAR_ACIKLAMA)
        LEFT JOIN V3RIICO.dbo.TBLSTSABIT ST WITH(NOLOCK) ON ST.STOK_KODU=S.STOK_KODU
        WHERE S.STHAR_FTIRSIP=''6'' AND S.STHAR_GCKOD=''C'' AND S.STHAR_HTUR<>''K'' AND ISNULL(S.L_YEDEK9,0)<=0
          AND NOT(S.REDNEDEN=2 AND EXISTS(SELECT 1 FROM V3RIICO.dbo.TBLASORTIMAS A WITH(NOLOCK) WHERE A.ASORTIKOD=S.EKALAN1))
          AND (@BranchCode IS NULL OR @BranchCode='''' OR CONVERT(varchar(10),S.SUBE_KODU)=@BranchCode)
          AND (NULLIF(REPLACE(LTRIM(RTRIM(@SiparisNoCsv)),N'' '',N''''),N'''') IS NULL
               OR CHARINDEX(
                   N'',''+LTRIM(RTRIM(CONVERT(nvarchar(100),S.FISNO)))+N'','',
                   N'',''+REPLACE(@SiparisNoCsv,N'' '',N'''')+N'','')>0)
    ),
    Orders AS
    (
        SELECT FISNO,BranchCode,TargetWh,ProjectCode,MIN(OrderDate) OrderDate,OrderID,StockCode,MAX(StockName) StockName,
               MAX(CustomerCode) CustomerCode,MAX(CustomerName) CustomerName,SUM(OrderedQty) OrderedQty,
               SUM(DeliveredQty) DeliveredQty,SUM(OrderedQty-DeliveredQty) RemainingHamax,MAX(YapKod) YapKod,MAX(YapAcik) YapAcik
        FROM Base
        GROUP BY FISNO,BranchCode,TargetWh,ProjectCode,OrderID,StockCode
    ),
    Allocated AS
    (
        SELECT D.ExternalDocumentNo,L.ExternalLineId,SUM(ISNULL(L.AllocatedQuantity,0)) PlannedQtyAllocated
        FROM dbo.RII_SH_LINE_SOURCE L
        JOIN dbo.RII_SH_SOURCE_DOCUMENT D ON D.Id=L.ShipmentSourceDocumentId
        JOIN dbo.RII_SH_HEADER H ON H.Id=D.ShipmentHeaderId
        WHERE L.IsDeleted=0 AND D.IsDeleted=0 AND H.IsDeleted=0 AND H.Status<>11
        GROUP BY D.ExternalDocumentNo,L.ExternalLineId
    )
    SELECT ''L'' Mode,O.FISNO SiparisNo,O.OrderID,O.StockCode,O.StockName,O.CustomerCode,O.CustomerName,
           O.BranchCode,O.TargetWh,O.ProjectCode,O.OrderDate,
           CAST(O.OrderedQty AS decimal(18,4)) OrderedQty,CAST(O.DeliveredQty AS decimal(18,4)) DeliveredQty,
           CAST(O.RemainingHamax AS decimal(18,4)) RemainingHamax,
           CAST(ISNULL(A.PlannedQtyAllocated,0) AS decimal(18,4)) PlannedQtyAllocated,
           CAST(O.RemainingHamax-ISNULL(A.PlannedQtyAllocated,0) AS decimal(18,4)) RemainingForImport,
           O.YapKod,O.YapAcik
    FROM Orders O
    LEFT JOIN Allocated A ON A.ExternalDocumentNo=O.FISNO AND A.ExternalLineId=CONVERT(varchar(100),O.OrderID)
    WHERE O.RemainingHamax-ISNULL(A.PlannedQtyAllocated,0)>0;';
    END
    ELSE
    BEGIN
        EXEC sys.sp_executesql N'ALTER FUNCTION dbo.RII_FN_SH_LINE
    (
        @SiparisNoCsv nvarchar(max),
        @BranchCode varchar(10)=NULL
    )
    RETURNS TABLE
    AS
    RETURN
    WITH Base AS
    (
        SELECT S.FISNO,S.SUBE_KODU BranchCode,S.DEPO_KODU TargetWh,S.PROJE_KODU ProjectCode,S.STHAR_TARIH OrderDate,
               S.INCKEYNO OrderID,S.STOK_KODU StockCode,ST.STOK_ADI StockName,
               COALESCE(M.CARI_KODU,S.STHAR_ACIKLAMA) CustomerCode,C.CARI_ISIM CustomerName,
               CASE WHEN ISNULL(S.L_YEDEK9,0)=-1 THEN ISNULL(S.STHAR_GCMIK2,0) ELSE ISNULL(S.STHAR_GCMIK,0) END OrderedQty,
               ISNULL(S.FIRMA_DOVTUT,0) DeliveredQty,CAST('''' AS varchar(50)) YapKod,CAST('''' AS varchar(200)) YapAcik
        FROM V3RIICO.dbo.TBLSIPATRA S WITH(NOLOCK)
        LEFT JOIN V3RIICO.dbo.TBLSIPAMAS M WITH(NOLOCK) ON M.FATIRS_NO=S.FISNO AND M.FTIRSIP=S.STHAR_FTIRSIP
        LEFT JOIN V3RIICO.dbo.TBLCASABIT C WITH(NOLOCK) ON C.CARI_KOD=COALESCE(M.CARI_KODU,S.STHAR_ACIKLAMA)
        LEFT JOIN V3RIICO.dbo.TBLSTSABIT ST WITH(NOLOCK) ON ST.STOK_KODU=S.STOK_KODU
        WHERE S.STHAR_FTIRSIP=''6'' AND S.STHAR_GCKOD=''C'' AND S.STHAR_HTUR<>''K'' AND ISNULL(S.L_YEDEK9,0)<=0
          AND NOT(S.REDNEDEN=2 AND EXISTS(SELECT 1 FROM V3RIICO.dbo.TBLASORTIMAS A WITH(NOLOCK) WHERE A.ASORTIKOD=S.EKALAN1))
          AND (@BranchCode IS NULL OR @BranchCode='''' OR CONVERT(varchar(10),S.SUBE_KODU)=@BranchCode)
          AND (NULLIF(REPLACE(LTRIM(RTRIM(@SiparisNoCsv)),N'' '',N''''),N'''') IS NULL
               OR CHARINDEX(
                   N'',''+LTRIM(RTRIM(CONVERT(nvarchar(100),S.FISNO)))+N'','',
                   N'',''+REPLACE(@SiparisNoCsv,N'' '',N'''')+N'','')>0)
    ),
    Orders AS
    (
        SELECT FISNO,BranchCode,TargetWh,ProjectCode,MIN(OrderDate) OrderDate,OrderID,StockCode,MAX(StockName) StockName,
               MAX(CustomerCode) CustomerCode,MAX(CustomerName) CustomerName,SUM(OrderedQty) OrderedQty,
               SUM(DeliveredQty) DeliveredQty,SUM(OrderedQty-DeliveredQty) RemainingHamax,MAX(YapKod) YapKod,MAX(YapAcik) YapAcik
        FROM Base
        GROUP BY FISNO,BranchCode,TargetWh,ProjectCode,OrderID,StockCode
    ),
    Allocated AS
    (
        SELECT D.ExternalDocumentNo,L.ExternalLineId,SUM(ISNULL(L.AllocatedQuantity,0)) PlannedQtyAllocated
        FROM dbo.RII_SH_LINE_SOURCE L
        JOIN dbo.RII_SH_SOURCE_DOCUMENT D ON D.Id=L.ShipmentSourceDocumentId
        JOIN dbo.RII_SH_HEADER H ON H.Id=D.ShipmentHeaderId
        WHERE L.IsDeleted=0 AND D.IsDeleted=0 AND H.IsDeleted=0 AND H.Status<>11
        GROUP BY D.ExternalDocumentNo,L.ExternalLineId
    )
    SELECT ''L'' Mode,O.FISNO SiparisNo,O.OrderID,O.StockCode,O.StockName,O.CustomerCode,O.CustomerName,
           O.BranchCode,O.TargetWh,O.ProjectCode,O.OrderDate,
           CAST(O.OrderedQty AS decimal(18,4)) OrderedQty,CAST(O.DeliveredQty AS decimal(18,4)) DeliveredQty,
           CAST(O.RemainingHamax AS decimal(18,4)) RemainingHamax,
           CAST(ISNULL(A.PlannedQtyAllocated,0) AS decimal(18,4)) PlannedQtyAllocated,
           CAST(O.RemainingHamax-ISNULL(A.PlannedQtyAllocated,0) AS decimal(18,4)) RemainingForImport,
           O.YapKod,O.YapAcik
    FROM Orders O
    LEFT JOIN Allocated A ON A.ExternalDocumentNo=O.FISNO AND A.ExternalLineId=CONVERT(varchar(100),O.OrderID)
    WHERE O.RemainingHamax-ISNULL(A.PlannedQtyAllocated,0)>0;';
    END
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723204130_HardenShippingAndTransferAllocations'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260723204130_HardenShippingAndTransferAllocations', N'10.0.10');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724075250_AddStockReservationLedgerAndOperationLifecycle'
)
BEGIN
    ALTER TABLE [RII_SH_TRACKING] DROP CONSTRAINT [CK_RII_SH_TRACKING_QTY];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724075250_AddStockReservationLedgerAndOperationLifecycle'
)
BEGIN
    ALTER TABLE [RII_SH_TRACKING] ADD [ReservedQuantity] decimal(20,6) NOT NULL DEFAULT 0.0;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724075250_AddStockReservationLedgerAndOperationLifecycle'
)
BEGIN
    CREATE TABLE [RII_STOCK_RESERVATION_OPERATIONS] (
        [Id] bigint NOT NULL IDENTITY,
        [IdempotencyKey] varchar(100) NOT NULL,
        [RequestHash] varchar(64) NOT NULL,
        [ReferenceType] nvarchar(50) NOT NULL,
        [ReferenceId] bigint NOT NULL,
        [ReferenceNo] nvarchar(100) NULL,
        [OperationType] nvarchar(20) NOT NULL,
        [Reason] nvarchar(500) NULL,
        [OccurredAtUtc] datetime2 NOT NULL,
        [BranchCode] nvarchar(10) NOT NULL DEFAULT N'0',
        [CreatedDate] datetime2 NULL,
        [UpdatedDate] datetime2 NULL,
        [DeletedDate] datetime2 NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [CreatedBy] bigint NULL,
        [UpdatedBy] bigint NULL,
        [DeletedBy] bigint NULL,
        CONSTRAINT [PK_RII_STOCK_RESERVATION_OPERATIONS] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724075250_AddStockReservationLedgerAndOperationLifecycle'
)
BEGIN
    CREATE TABLE [RII_STOCK_RESERVATION_ENTRIES] (
        [Id] bigint NOT NULL IDENTITY,
        [OperationId] bigint NOT NULL,
        [LineNo] int NOT NULL,
        [ReferenceLineId] bigint NOT NULL,
        [WarehouseId] bigint NOT NULL,
        [LocationId] bigint NOT NULL,
        [StockId] bigint NOT NULL,
        [YapCodeId] bigint NULL,
        [UnitCode] nvarchar(20) NOT NULL,
        [LotNo] nvarchar(100) NOT NULL,
        [SerialNo] nvarchar(100) NOT NULL,
        [StockStatus] nvarchar(30) NOT NULL,
        [QuantityDelta] decimal(18,6) NOT NULL,
        [OccurredAtUtc] datetime2 NOT NULL,
        [BranchCode] nvarchar(10) NOT NULL DEFAULT N'0',
        [CreatedDate] datetime2 NULL,
        [UpdatedDate] datetime2 NULL,
        [DeletedDate] datetime2 NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [CreatedBy] bigint NULL,
        [UpdatedBy] bigint NULL,
        [DeletedBy] bigint NULL,
        CONSTRAINT [PK_RII_STOCK_RESERVATION_ENTRIES] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_RII_STOCK_RESERVATION_ENTRIES_RII_STOCK_RESERVATION_OPERATIONS_OperationId] FOREIGN KEY ([OperationId]) REFERENCES [RII_STOCK_RESERVATION_OPERATIONS] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724075250_AddStockReservationLedgerAndOperationLifecycle'
)
BEGIN
    EXEC(N'ALTER TABLE [RII_SH_TRACKING] ADD CONSTRAINT [CK_RII_SH_TRACKING_QTY] CHECK ([PlannedQuantity] > 0 AND [ReservedQuantity] >= 0 AND [PickedQuantity] >= 0 AND [PackedQuantity] >= 0 AND [LoadedQuantity] >= 0 AND [ShippedQuantity] >= 0)');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724075250_AddStockReservationLedgerAndOperationLifecycle'
)
BEGIN
    DECLARE @Marker nvarchar(200)=N'Migration:AddStockReservationLedgerAndOperationLifecycle';
    DECLARE @Permissions TABLE(Code nvarchar(150) NOT NULL, Name nvarchar(200) NOT NULL);
    INSERT INTO @Permissions(Code,Name) VALUES
    (N'WMS.WAREHOUSE_TRANSFER.UPDATE',N'Transfer taslağını güncelle'),
    (N'WMS.WAREHOUSE_TRANSFER.DELETE',N'Transfer taslağını sil'),
    (N'WMS.WAREHOUSE_TRANSFER.CANCEL',N'Transferi iptal et ve stok hareketlerini ters çevir'),
    (N'WMS.SHIPPING.UPDATE',N'Sevk taslağını güncelle'),
    (N'WMS.SHIPPING.DELETE',N'Sevk taslağını sil'),
    (N'WMS.SHIPPING.CANCEL',N'Sevki iptal et ve stok hareketlerini ters çevir');

    INSERT INTO dbo.RII_PERMISSION_DEFINITIONS
        (BranchCode,Code,Name,Description,IsActive,AvailableOnWeb,AvailableOnMobile,IsDeleted,CreatedDate)
    SELECT N'0',p.Code,p.Name,@Marker,1,1,0,0,SYSUTCDATETIME()
    FROM @Permissions p
    WHERE NOT EXISTS(SELECT 1 FROM dbo.RII_PERMISSION_DEFINITIONS d WHERE d.Code=p.Code);

    IF EXISTS(SELECT 1 FROM dbo.RII_PERMISSION_GROUPS WHERE Id=1001 AND IsDeleted=0)
    BEGIN
        INSERT INTO dbo.RII_PERMISSION_GROUP_PERMISSIONS
            (BranchCode,PermissionGroupId,PermissionDefinitionId,IsDeleted,CreatedDate)
        SELECT N'0',1001,d.Id,0,SYSUTCDATETIME()
        FROM dbo.RII_PERMISSION_DEFINITIONS d
        JOIN @Permissions p ON p.Code=d.Code
        WHERE NOT EXISTS(
            SELECT 1 FROM dbo.RII_PERMISSION_GROUP_PERMISSIONS gp
            WHERE gp.PermissionGroupId=1001 AND gp.PermissionDefinitionId=d.Id AND gp.IsDeleted=0);
    END
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724075250_AddStockReservationLedgerAndOperationLifecycle'
)
BEGIN
    CREATE INDEX [IX_RII_STOCK_RESERVATION_ENTRIES_DIMENSIONS] ON [RII_STOCK_RESERVATION_ENTRIES] ([WarehouseId], [LocationId], [StockId], [YapCodeId], [UnitCode], [StockStatus]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724075250_AddStockReservationLedgerAndOperationLifecycle'
)
BEGIN
    CREATE INDEX [IX_RII_STOCK_RESERVATION_ENTRIES_IsDeleted] ON [RII_STOCK_RESERVATION_ENTRIES] ([IsDeleted]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724075250_AddStockReservationLedgerAndOperationLifecycle'
)
BEGIN
    CREATE INDEX [IX_RII_STOCK_RESERVATION_ENTRIES_OperationId] ON [RII_STOCK_RESERVATION_ENTRIES] ([OperationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724075250_AddStockReservationLedgerAndOperationLifecycle'
)
BEGIN
    CREATE INDEX [IX_RII_STOCK_RESERVATION_ENTRIES_REFERENCE_LINE] ON [RII_STOCK_RESERVATION_ENTRIES] ([ReferenceLineId], [WarehouseId], [LocationId], [StockId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724075250_AddStockReservationLedgerAndOperationLifecycle'
)
BEGIN
    CREATE INDEX [IX_RII_STOCK_RESERVATION_OPERATIONS_IsDeleted] ON [RII_STOCK_RESERVATION_OPERATIONS] ([IsDeleted]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724075250_AddStockReservationLedgerAndOperationLifecycle'
)
BEGIN
    CREATE INDEX [IX_RII_STOCK_RESERVATION_OPERATIONS_REFERENCE] ON [RII_STOCK_RESERVATION_OPERATIONS] ([ReferenceType], [ReferenceId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724075250_AddStockReservationLedgerAndOperationLifecycle'
)
BEGIN
    CREATE UNIQUE INDEX [UX_RII_STOCK_RESERVATION_OPERATIONS_IDEMPOTENCY] ON [RII_STOCK_RESERVATION_OPERATIONS] ([IdempotencyKey]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724075250_AddStockReservationLedgerAndOperationLifecycle'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260724075250_AddStockReservationLedgerAndOperationLifecycle', N'10.0.10');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724103428_AddGoodsReceiptLifecycleIdempotency'
)
BEGIN
    DROP INDEX [IX_RII_GR_STATUS_HISTORY_CORRELATION_ID] ON [RII_GR_STATUS_HISTORY];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724103428_AddGoodsReceiptLifecycleIdempotency'
)
BEGIN
    ALTER TABLE [RII_GR_STATUS_HISTORY] ADD [RequestHash] varchar(64) NOT NULL DEFAULT '';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724103428_AddGoodsReceiptLifecycleIdempotency'
)
BEGIN
    CREATE UNIQUE INDEX [UX_RII_GR_STATUS_HISTORY_HEADER_CORRELATION_ID] ON [RII_GR_STATUS_HISTORY] ([GrHeaderId], [CorrelationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724103428_AddGoodsReceiptLifecycleIdempotency'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260724103428_AddGoodsReceiptLifecycleIdempotency', N'10.0.10');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724110747_AddWarehouseInboundOutboundModules'
)
BEGIN
    ALTER TABLE [RII_QUALITY_INSPECTION_LINES] ADD [WarehouseInboundLineId] bigint NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724110747_AddWarehouseInboundOutboundModules'
)
BEGIN
    CREATE TABLE [RII_WI_HEADER] (
        [Id] bigint NOT NULL IDENTITY,
        [DocumentSeriesId] bigint NOT NULL,
        [DocumentNo] nvarchar(50) NOT NULL,
        [DocumentDate] date NOT NULL,
        [ReceiptType] nvarchar(30) NOT NULL,
        [InitiationMode] nvarchar(30) NOT NULL DEFAULT N'OrderBasedTask',
        [ProcessType] nvarchar(40) NOT NULL DEFAULT N'OrderBasedTask',
        [LabelStrategy] nvarchar(30) NOT NULL DEFAULT N'None',
        [SourceSystem] nvarchar(30) NOT NULL,
        [ExternalReferenceNo] nvarchar(100) NULL,
        [CorrelationId] uniqueidentifier NOT NULL,
        [SupplierId] bigint NULL,
        [SupplierCodeSnapshot] nvarchar(50) NULL,
        [SupplierNameSnapshot] nvarchar(200) NULL,
        [SupplierTaxNoSnapshot] nvarchar(20) NULL,
        [TargetWarehouseId] bigint NOT NULL,
        [ReceivingLocationId] bigint NOT NULL,
        [DefaultPutawayZoneCode] nvarchar(50) NULL,
        [QualityLocationId] bigint NULL,
        [QuarantineLocationId] bigint NULL,
        [Status] nvarchar(30) NOT NULL,
        [ApprovalStatus] nvarchar(30) NOT NULL,
        [QualityStatus] nvarchar(30) NOT NULL,
        [PutawayStatus] nvarchar(30) NOT NULL,
        [ErpIntegrationStatus] nvarchar(30) NOT NULL,
        [PlannedArrivalAtUtc] datetimeoffset(7) NULL,
        [ActualArrivalAtUtc] datetimeoffset(7) NULL,
        [ReleasedAtUtc] datetimeoffset(7) NULL,
        [ReleasedBy] bigint NULL,
        [StartedAtUtc] datetimeoffset(7) NULL,
        [StartedBy] bigint NULL,
        [ReceivedAtUtc] datetimeoffset(7) NULL,
        [ReceivedBy] bigint NULL,
        [CompletedAtUtc] datetimeoffset(7) NULL,
        [CompletedBy] bigint NULL,
        [CancelledAtUtc] datetimeoffset(7) NULL,
        [CancelledBy] bigint NULL,
        [CancellationReason] nvarchar(500) NULL,
        [WaybillNo] varchar(15) NULL,
        [WaybillDate] date NULL,
        [ElectronicWaybillNo] varchar(16) NULL,
        [ShipmentReferenceNo] nvarchar(100) NULL,
        [CarrierCode] nvarchar(50) NULL,
        [CarrierName] nvarchar(200) NULL,
        [VehiclePlate] nvarchar(20) NULL,
        [TrailerPlate] nvarchar(20) NULL,
        [DriverName] nvarchar(150) NULL,
        [SealNo] nvarchar(50) NULL,
        [AllowOverReceipt] bit NOT NULL,
        [OverReceiptTolerancePercent] decimal(9,4) NOT NULL,
        [AllowUnderReceipt] bit NOT NULL,
        [RequireShortCloseApproval] bit NOT NULL,
        [RequireQualityControl] bit NOT NULL,
        [RequirePutaway] bit NOT NULL,
        [RequireHandlingUnit] bit NOT NULL,
        [OverReceiptPolicy] nvarchar(30) NOT NULL DEFAULT N'NotAllowed',
        [RequireReceiptApproval] bit NOT NULL,
        [RequireQualityApproval] bit NOT NULL,
        [RequireErpApproval] bit NOT NULL,
        [HoldInventoryUntilQualityDecision] bit NOT NULL,
        [BlockPutawayUntilQualityDecision] bit NOT NULL,
        [InventoryAvailabilityPolicy] nvarchar(40) NOT NULL DEFAULT N'AfterQualityApproval',
        [ErpPostingPolicy] nvarchar(40) NOT NULL DEFAULT N'AfterAllApprovals',
        [Priority] tinyint NOT NULL DEFAULT CAST(3 AS tinyint),
        [Description] nvarchar(1000) NULL,
        [RowVersion] rowversion NOT NULL,
        [BranchCode] nvarchar(10) NOT NULL DEFAULT N'0',
        [CreatedDate] datetime2 NULL,
        [UpdatedDate] datetime2 NULL,
        [DeletedDate] datetime2 NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [CreatedBy] bigint NULL,
        [UpdatedBy] bigint NULL,
        [DeletedBy] bigint NULL,
        CONSTRAINT [PK_RII_WI_HEADER] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_RII_WI_HEADER_OVER_TOLERANCE] CHECK ([OverReceiptTolerancePercent] BETWEEN 0 AND 100),
        CONSTRAINT [CK_RII_WI_HEADER_PRIORITY] CHECK ([Priority] BETWEEN 1 AND 5),
        CONSTRAINT [FK_RII_WI_HEADER_RII_CUSTOMER_SupplierId] FOREIGN KEY ([SupplierId]) REFERENCES [RII_CUSTOMER] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_RII_WI_HEADER_RII_DOCUMENT_SERIES_DocumentSeriesId] FOREIGN KEY ([DocumentSeriesId]) REFERENCES [RII_DOCUMENT_SERIES] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_RII_WI_HEADER_RII_LOCATION_QualityLocationId] FOREIGN KEY ([QualityLocationId]) REFERENCES [RII_LOCATION] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_RII_WI_HEADER_RII_LOCATION_QuarantineLocationId] FOREIGN KEY ([QuarantineLocationId]) REFERENCES [RII_LOCATION] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_RII_WI_HEADER_RII_LOCATION_ReceivingLocationId] FOREIGN KEY ([ReceivingLocationId]) REFERENCES [RII_LOCATION] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_RII_WI_HEADER_RII_WAREHOUSE_TargetWarehouseId] FOREIGN KEY ([TargetWarehouseId]) REFERENCES [RII_WAREHOUSE] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724110747_AddWarehouseInboundOutboundModules'
)
BEGIN
    CREATE TABLE [RII_WI_POLICIES] (
        [Id] bigint NOT NULL IDENTITY,
        [PolicyKey] nvarchar(30) NOT NULL,
        [OverReceiptPolicy] nvarchar(30) NOT NULL,
        [OverReceiptTolerancePercent] decimal(9,4) NOT NULL,
        [AllowUnderReceipt] bit NOT NULL,
        [RequireShortCloseApproval] bit NOT NULL,
        [RequireReceiptApproval] bit NOT NULL,
        [RequireQualityApproval] bit NOT NULL,
        [RequireErpApproval] bit NOT NULL,
        [HoldInventoryUntilQualityDecision] bit NOT NULL,
        [BlockPutawayUntilQualityDecision] bit NOT NULL,
        [InventoryAvailabilityPolicy] nvarchar(40) NOT NULL,
        [ErpPostingPolicy] nvarchar(40) NOT NULL,
        [AllowOrderlessReceipt] bit NOT NULL,
        [AllowUnplannedReceipt] bit NOT NULL,
        [RowVersion] rowversion NOT NULL,
        [BranchCode] nvarchar(10) NOT NULL DEFAULT N'0',
        [CreatedDate] datetime2 NULL,
        [UpdatedDate] datetime2 NULL,
        [DeletedDate] datetime2 NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [CreatedBy] bigint NULL,
        [UpdatedBy] bigint NULL,
        [DeletedBy] bigint NULL,
        CONSTRAINT [PK_RII_WI_POLICIES] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724110747_AddWarehouseInboundOutboundModules'
)
BEGIN
    CREATE TABLE [RII_WO_HEADER] (
        [Id] bigint NOT NULL IDENTITY,
        [DocumentSeriesId] bigint NOT NULL,
        [DocumentNo] nvarchar(50) NOT NULL,
        [DocumentDate] date NOT NULL,
        [InitiationMode] int NOT NULL,
        [SourceSystem] int NOT NULL,
        [CorrelationId] uniqueidentifier NOT NULL,
        [CustomerId] bigint NOT NULL,
        [CustomerCodeSnapshot] nvarchar(100) NOT NULL,
        [CustomerNameSnapshot] nvarchar(300) NULL,
        [SourceWarehouseId] bigint NOT NULL,
        [StagingLocationId] bigint NULL,
        [LoadingLocationId] bigint NULL,
        [Status] int NOT NULL,
        [ApprovalStatus] int NOT NULL,
        [ErpIntegrationStatus] int NOT NULL,
        [PlannedWarehouseOutboundAtUtc] datetimeoffset NULL,
        [ShippedAtUtc] datetimeoffset NULL,
        [ExternalReferenceNo] nvarchar(100) NULL,
        [WaybillNo] nvarchar(50) NULL,
        [IsEDispatch] bit NOT NULL,
        [CarrierCode] nvarchar(50) NULL,
        [CarrierName] nvarchar(200) NULL,
        [VehiclePlate] nvarchar(20) NULL,
        [TrailerPlate] nvarchar(20) NULL,
        [DriverName] nvarchar(200) NULL,
        [SealNo] nvarchar(50) NULL,
        [TrackingNo] nvarchar(100) NULL,
        [Priority] tinyint NOT NULL,
        [Description] nvarchar(2000) NULL,
        [RequireApproval] bit NOT NULL,
        [RequireAssignee] bit NOT NULL,
        [AllowPartialPicking] bit NOT NULL,
        [AllowPartialWarehouseOutbound] bit NOT NULL,
        [RequireSourceLocation] bit NOT NULL,
        [RequireWarehouseOutboundInformation] bit NOT NULL,
        [RequireLoadingConfirmation] bit NOT NULL,
        [AutoReleaseTaskBased] bit NOT NULL,
        [AutoPostErpAfterApproval] bit NOT NULL,
        [MinimumFulfillmentPercent] decimal(9,4) NOT NULL,
        [OverPickTolerancePercent] decimal(9,4) NOT NULL,
        [ReservationPolicy] nvarchar(30) NOT NULL,
        [PackingPolicy] nvarchar(30) NOT NULL,
        [ShortagePolicy] nvarchar(30) NOT NULL,
        [OverPickPolicy] nvarchar(30) NOT NULL,
        [RowVersion] rowversion NOT NULL,
        [BranchCode] nvarchar(10) NOT NULL DEFAULT N'0',
        [CreatedDate] datetime2 NULL,
        [UpdatedDate] datetime2 NULL,
        [DeletedDate] datetime2 NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [CreatedBy] bigint NULL,
        [UpdatedBy] bigint NULL,
        [DeletedBy] bigint NULL,
        CONSTRAINT [PK_RII_WO_HEADER] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724110747_AddWarehouseInboundOutboundModules'
)
BEGIN
    CREATE TABLE [RII_WO_POLICIES] (
        [Id] bigint NOT NULL IDENTITY,
        [PolicyKey] nvarchar(30) NOT NULL,
        [AllowOrderBasedTask] bit NOT NULL,
        [AllowStockBasedTask] bit NOT NULL,
        [AllowOrderBasedDirect] bit NOT NULL,
        [AllowStockBasedDirect] bit NOT NULL,
        [RequireApproval] bit NOT NULL,
        [RequireAssigneeForTask] bit NOT NULL,
        [AllowMultipleAssignees] bit NOT NULL,
        [AutoReleaseTaskBased] bit NOT NULL,
        [AllowPartialPicking] bit NOT NULL,
        [AllowPartialWarehouseOutbound] bit NOT NULL,
        [RequireSourceLocation] bit NOT NULL,
        [RequireWarehouseOutboundInformation] bit NOT NULL,
        [RequireLoadingConfirmation] bit NOT NULL,
        [AutoPostErpAfterApproval] bit NOT NULL,
        [MinimumFulfillmentPercent] decimal(9,4) NOT NULL,
        [OverPickTolerancePercent] decimal(9,4) NOT NULL,
        [ReservationPolicy] nvarchar(30) NOT NULL,
        [PackingPolicy] nvarchar(30) NOT NULL,
        [ShortagePolicy] nvarchar(30) NOT NULL,
        [OverPickPolicy] nvarchar(30) NOT NULL,
        [RowVersion] rowversion NOT NULL,
        [BranchCode] nvarchar(10) NOT NULL DEFAULT N'0',
        [CreatedDate] datetime2 NULL,
        [UpdatedDate] datetime2 NULL,
        [DeletedDate] datetime2 NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [CreatedBy] bigint NULL,
        [UpdatedBy] bigint NULL,
        [DeletedBy] bigint NULL,
        CONSTRAINT [PK_RII_WO_POLICIES] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_RII_WO_POLICY_FULFILLMENT] CHECK ([MinimumFulfillmentPercent] >= 0 AND [MinimumFulfillmentPercent] <= 100),
        CONSTRAINT [CK_RII_WO_POLICY_OVERPICK] CHECK ([OverPickTolerancePercent] >= 0 AND [OverPickTolerancePercent] <= 100)
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724110747_AddWarehouseInboundOutboundModules'
)
BEGIN
    CREATE TABLE [RII_WI_LABEL_BATCH] (
        [Id] bigint NOT NULL IDENTITY,
        [GrHeaderId] bigint NOT NULL,
        [CorrelationId] uniqueidentifier NOT NULL,
        [BatchNo] nvarchar(50) NOT NULL,
        [Status] nvarchar(30) NOT NULL,
        [TotalLabelCount] int NOT NULL,
        [PrintedLabelCount] int NOT NULL,
        [ConsumedLabelCount] int NOT NULL,
        [VoidLabelCount] int NOT NULL,
        [LastPrintedAtUtc] datetimeoffset(7) NULL,
        [CompletedAtUtc] datetimeoffset(7) NULL,
        [Description] nvarchar(500) NULL,
        [RowVersion] rowversion NOT NULL,
        [BranchCode] nvarchar(10) NOT NULL DEFAULT N'0',
        [CreatedDate] datetime2 NULL,
        [UpdatedDate] datetime2 NULL,
        [DeletedDate] datetime2 NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [CreatedBy] bigint NULL,
        [UpdatedBy] bigint NULL,
        [DeletedBy] bigint NULL,
        CONSTRAINT [PK_RII_WI_LABEL_BATCH] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_RII_WI_LABEL_BATCH_COUNTS] CHECK ([TotalLabelCount] >= 0 AND [PrintedLabelCount] >= 0 AND [ConsumedLabelCount] >= 0 AND [VoidLabelCount] >= 0 AND [PrintedLabelCount] <= [TotalLabelCount] AND [ConsumedLabelCount] + [VoidLabelCount] <= [TotalLabelCount]),
        CONSTRAINT [FK_RII_WI_LABEL_BATCH_RII_WI_HEADER_GrHeaderId] FOREIGN KEY ([GrHeaderId]) REFERENCES [RII_WI_HEADER] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724110747_AddWarehouseInboundOutboundModules'
)
BEGIN
    CREATE TABLE [RII_WI_LINE] (
        [Id] bigint NOT NULL IDENTITY,
        [GrHeaderId] bigint NOT NULL,
        [LineNo] int NOT NULL,
        [StockId] bigint NOT NULL,
        [StockCodeSnapshot] nvarchar(50) NOT NULL,
        [StockNameSnapshot] nvarchar(250) NULL,
        [YapCodeId] bigint NULL,
        [YapCodeSnapshot] nvarchar(50) NULL,
        [UnitCode] nvarchar(20) NOT NULL,
        [BaseUnitCode] nvarchar(20) NOT NULL,
        [UnitConversionFactor] decimal(18,8) NOT NULL,
        [ExpectedQuantity] decimal(18,6) NOT NULL,
        [ReceivedQuantity] decimal(18,6) NOT NULL,
        [AcceptedQuantity] decimal(18,6) NOT NULL,
        [RejectedQuantity] decimal(18,6) NOT NULL,
        [QuarantineQuantity] decimal(18,6) NOT NULL,
        [PutawayQuantity] decimal(18,6) NOT NULL,
        [ShortClosedQuantity] decimal(18,6) NOT NULL,
        [TrackingType] nvarchar(30) NOT NULL,
        [RequireLot] bit NOT NULL,
        [RequireSerial] bit NOT NULL,
        [RequireManufacturingDate] bit NOT NULL,
        [RequireExpirationDate] bit NOT NULL,
        [MinimumShelfLifeDays] int NULL,
        [RequireQualityControl] bit NOT NULL,
        [RequireHandlingUnit] bit NOT NULL,
        [TargetWarehouseId] bigint NOT NULL,
        [Status] nvarchar(30) NOT NULL,
        [AllowOverReceipt] bit NOT NULL,
        [OverReceiptTolerancePercent] decimal(9,4) NOT NULL,
        [AllowUnderReceipt] bit NOT NULL,
        [DefaultReceivingLocationId] bigint NULL,
        [DefaultPutawayLocationId] bigint NULL,
        [Description] nvarchar(500) NULL,
        [RowVersion] rowversion NOT NULL,
        [BranchCode] nvarchar(10) NOT NULL DEFAULT N'0',
        [CreatedDate] datetime2 NULL,
        [UpdatedDate] datetime2 NULL,
        [DeletedDate] datetime2 NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [CreatedBy] bigint NULL,
        [UpdatedBy] bigint NULL,
        [DeletedBy] bigint NULL,
        CONSTRAINT [PK_RII_WI_LINE] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_RII_WI_LINE_LINE_NO] CHECK ([LineNo] > 0),
        CONSTRAINT [CK_RII_WI_LINE_OVER_TOLERANCE] CHECK ([OverReceiptTolerancePercent] BETWEEN 0 AND 100),
        CONSTRAINT [CK_RII_WI_LINE_PUTAWAY_TOTAL] CHECK ([PutawayQuantity] <= [AcceptedQuantity]),
        CONSTRAINT [CK_RII_WI_LINE_QUALITY_TOTAL] CHECK ([AcceptedQuantity] + [RejectedQuantity] + [QuarantineQuantity] <= [ReceivedQuantity]),
        CONSTRAINT [CK_RII_WI_LINE_QUANTITIES_NONNEGATIVE] CHECK ([ExpectedQuantity] >= 0 AND [ReceivedQuantity] >= 0 AND [AcceptedQuantity] >= 0 AND [RejectedQuantity] >= 0 AND [QuarantineQuantity] >= 0 AND [PutawayQuantity] >= 0 AND [ShortClosedQuantity] >= 0),
        CONSTRAINT [CK_RII_WI_LINE_UNIT_FACTOR] CHECK ([UnitConversionFactor] > 0),
        CONSTRAINT [FK_RII_WI_LINE_RII_LOCATION_DefaultPutawayLocationId] FOREIGN KEY ([DefaultPutawayLocationId]) REFERENCES [RII_LOCATION] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_RII_WI_LINE_RII_LOCATION_DefaultReceivingLocationId] FOREIGN KEY ([DefaultReceivingLocationId]) REFERENCES [RII_LOCATION] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_RII_WI_LINE_RII_STOCK_StockId] FOREIGN KEY ([StockId]) REFERENCES [RII_STOCK] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_RII_WI_LINE_RII_WAREHOUSE_TargetWarehouseId] FOREIGN KEY ([TargetWarehouseId]) REFERENCES [RII_WAREHOUSE] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_RII_WI_LINE_RII_WI_HEADER_GrHeaderId] FOREIGN KEY ([GrHeaderId]) REFERENCES [RII_WI_HEADER] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_RII_WI_LINE_RII_YAP_CODE_YapCodeId] FOREIGN KEY ([YapCodeId]) REFERENCES [RII_YAP_CODE] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724110747_AddWarehouseInboundOutboundModules'
)
BEGIN
    CREATE TABLE [RII_WI_SOURCE_DOCUMENT] (
        [Id] bigint NOT NULL IDENTITY,
        [GrHeaderId] bigint NOT NULL,
        [SourceDocumentType] nvarchar(30) NOT NULL,
        [SourceSystem] nvarchar(30) NOT NULL,
        [ExternalDocumentId] nvarchar(100) NULL,
        [ExternalDocumentNo] nvarchar(50) NOT NULL,
        [ExternalDocumentDate] date NULL,
        [SupplierCodeSnapshot] nvarchar(50) NULL,
        [SupplierNameSnapshot] nvarchar(200) NULL,
        [CurrencyCode] varchar(3) NULL,
        [LastSynchronizedAtUtc] datetimeoffset(7) NULL,
        [ExternalVersion] nvarchar(100) NULL,
        [ExternalStatus] nvarchar(30) NULL,
        [BranchCode] nvarchar(10) NOT NULL DEFAULT N'0',
        [CreatedDate] datetime2 NULL,
        [UpdatedDate] datetime2 NULL,
        [DeletedDate] datetime2 NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [CreatedBy] bigint NULL,
        [UpdatedBy] bigint NULL,
        [DeletedBy] bigint NULL,
        CONSTRAINT [PK_RII_WI_SOURCE_DOCUMENT] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_RII_WI_SOURCE_DOCUMENT_RII_WI_HEADER_GrHeaderId] FOREIGN KEY ([GrHeaderId]) REFERENCES [RII_WI_HEADER] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724110747_AddWarehouseInboundOutboundModules'
)
BEGIN
    CREATE TABLE [RII_WI_STATUS_HISTORY] (
        [Id] bigint NOT NULL IDENTITY,
        [GrHeaderId] bigint NOT NULL,
        [StatusArea] nvarchar(30) NOT NULL,
        [FromStatus] nvarchar(30) NULL,
        [ToStatus] nvarchar(30) NOT NULL,
        [ChangedAtUtc] datetimeoffset(7) NOT NULL,
        [ChangedBy] bigint NULL,
        [ReasonCode] nvarchar(50) NULL,
        [Description] nvarchar(500) NULL,
        [CorrelationId] uniqueidentifier NOT NULL,
        [RequestHash] varchar(64) NOT NULL,
        [BranchCode] nvarchar(10) NOT NULL DEFAULT N'0',
        [CreatedDate] datetime2 NULL,
        [UpdatedDate] datetime2 NULL,
        [DeletedDate] datetime2 NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [CreatedBy] bigint NULL,
        [UpdatedBy] bigint NULL,
        [DeletedBy] bigint NULL,
        CONSTRAINT [PK_RII_WI_STATUS_HISTORY] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_RII_WI_STATUS_HISTORY_RII_WI_HEADER_GrHeaderId] FOREIGN KEY ([GrHeaderId]) REFERENCES [RII_WI_HEADER] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724110747_AddWarehouseInboundOutboundModules'
)
BEGIN
    CREATE TABLE [RII_WI_TASK] (
        [Id] bigint NOT NULL IDENTITY,
        [GrHeaderId] bigint NOT NULL,
        [TaskNo] nvarchar(50) NOT NULL,
        [TaskType] nvarchar(30) NOT NULL,
        [Status] nvarchar(30) NOT NULL,
        [Priority] tinyint NOT NULL DEFAULT CAST(3 AS tinyint),
        [WarehouseId] bigint NOT NULL,
        [ZoneCode] nvarchar(50) NULL,
        [PlannedStartAtUtc] datetimeoffset(7) NULL,
        [DueAtUtc] datetimeoffset(7) NULL,
        [ReleasedAtUtc] datetimeoffset(7) NULL,
        [ReleasedBy] bigint NULL,
        [StartedAtUtc] datetimeoffset(7) NULL,
        [CompletedAtUtc] datetimeoffset(7) NULL,
        [CancelledAtUtc] datetimeoffset(7) NULL,
        [CancellationReason] nvarchar(500) NULL,
        [Description] nvarchar(500) NULL,
        [RowVersion] rowversion NOT NULL,
        [BranchCode] nvarchar(10) NOT NULL DEFAULT N'0',
        [CreatedDate] datetime2 NULL,
        [UpdatedDate] datetime2 NULL,
        [DeletedDate] datetime2 NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [CreatedBy] bigint NULL,
        [UpdatedBy] bigint NULL,
        [DeletedBy] bigint NULL,
        CONSTRAINT [PK_RII_WI_TASK] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_RII_WI_TASK_PRIORITY] CHECK ([Priority] BETWEEN 1 AND 5),
        CONSTRAINT [FK_RII_WI_TASK_RII_WAREHOUSE_WarehouseId] FOREIGN KEY ([WarehouseId]) REFERENCES [RII_WAREHOUSE] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_RII_WI_TASK_RII_WI_HEADER_GrHeaderId] FOREIGN KEY ([GrHeaderId]) REFERENCES [RII_WI_HEADER] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724110747_AddWarehouseInboundOutboundModules'
)
BEGIN
    CREATE TABLE [RII_WO_LINE] (
        [Id] bigint NOT NULL IDENTITY,
        [WarehouseOutboundHeaderId] bigint NOT NULL,
        [LineNo] int NOT NULL,
        [StockId] bigint NOT NULL,
        [StockCodeSnapshot] nvarchar(100) NOT NULL,
        [StockNameSnapshot] nvarchar(300) NULL,
        [YapCodeId] bigint NULL,
        [YapCodeSnapshot] nvarchar(100) NULL,
        [UnitCode] nvarchar(20) NOT NULL,
        [RequestedQuantity] decimal(20,6) NOT NULL,
        [ReservedQuantity] decimal(20,6) NOT NULL,
        [PickedQuantity] decimal(20,6) NOT NULL,
        [PackedQuantity] decimal(20,6) NOT NULL,
        [LoadedQuantity] decimal(20,6) NOT NULL,
        [ShippedQuantity] decimal(20,6) NOT NULL,
        [ShortClosedQuantity] decimal(20,6) NOT NULL,
        [TrackingType] int NOT NULL,
        [RequireHandlingUnit] bit NOT NULL,
        [DefaultSourceLocationId] bigint NULL,
        [Status] int NOT NULL,
        [Description] nvarchar(1000) NULL,
        [RowVersion] rowversion NOT NULL,
        [BranchCode] nvarchar(10) NOT NULL DEFAULT N'0',
        [CreatedDate] datetime2 NULL,
        [UpdatedDate] datetime2 NULL,
        [DeletedDate] datetime2 NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [CreatedBy] bigint NULL,
        [UpdatedBy] bigint NULL,
        [DeletedBy] bigint NULL,
        CONSTRAINT [PK_RII_WO_LINE] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_RII_WO_LINE_QTY] CHECK ([RequestedQuantity] > 0 AND [ReservedQuantity] >= 0 AND [PickedQuantity] >= 0 AND [PackedQuantity] >= 0 AND [LoadedQuantity] >= 0 AND [ShippedQuantity] >= 0 AND [ShortClosedQuantity] >= 0),
        CONSTRAINT [FK_RII_WO_LINE_RII_WO_HEADER_WarehouseOutboundHeaderId] FOREIGN KEY ([WarehouseOutboundHeaderId]) REFERENCES [RII_WO_HEADER] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724110747_AddWarehouseInboundOutboundModules'
)
BEGIN
    CREATE TABLE [RII_WO_SOURCE_DOCUMENT] (
        [Id] bigint NOT NULL IDENTITY,
        [WarehouseOutboundHeaderId] bigint NOT NULL,
        [SourceDocumentType] nvarchar(50) NOT NULL,
        [ExternalDocumentNo] nvarchar(100) NOT NULL,
        [ExternalDocumentId] nvarchar(100) NULL,
        [ExternalDocumentDate] date NULL,
        [ExternalStatus] nvarchar(50) NULL,
        [BranchCode] nvarchar(10) NOT NULL DEFAULT N'0',
        [CreatedDate] datetime2 NULL,
        [UpdatedDate] datetime2 NULL,
        [DeletedDate] datetime2 NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [CreatedBy] bigint NULL,
        [UpdatedBy] bigint NULL,
        [DeletedBy] bigint NULL,
        CONSTRAINT [PK_RII_WO_SOURCE_DOCUMENT] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_RII_WO_SOURCE_DOCUMENT_RII_WO_HEADER_WarehouseOutboundHeaderId] FOREIGN KEY ([WarehouseOutboundHeaderId]) REFERENCES [RII_WO_HEADER] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724110747_AddWarehouseInboundOutboundModules'
)
BEGIN
    CREATE TABLE [RII_WO_STATUS_HISTORY] (
        [Id] bigint NOT NULL IDENTITY,
        [WarehouseOutboundHeaderId] bigint NOT NULL,
        [FromStatus] nvarchar(50) NULL,
        [ToStatus] nvarchar(50) NOT NULL,
        [Description] nvarchar(1000) NULL,
        [ChangedAtUtc] datetimeoffset NOT NULL,
        [ChangedBy] bigint NOT NULL,
        [CorrelationId] uniqueidentifier NOT NULL,
        [BranchCode] nvarchar(10) NOT NULL DEFAULT N'0',
        [CreatedDate] datetime2 NULL,
        [UpdatedDate] datetime2 NULL,
        [DeletedDate] datetime2 NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [CreatedBy] bigint NULL,
        [UpdatedBy] bigint NULL,
        [DeletedBy] bigint NULL,
        CONSTRAINT [PK_RII_WO_STATUS_HISTORY] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_RII_WO_STATUS_HISTORY_RII_WO_HEADER_WarehouseOutboundHeaderId] FOREIGN KEY ([WarehouseOutboundHeaderId]) REFERENCES [RII_WO_HEADER] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724110747_AddWarehouseInboundOutboundModules'
)
BEGIN
    CREATE TABLE [RII_WO_TASK] (
        [Id] bigint NOT NULL IDENTITY,
        [WarehouseOutboundHeaderId] bigint NOT NULL,
        [TaskNo] nvarchar(50) NOT NULL,
        [TaskType] int NOT NULL,
        [WarehouseId] bigint NOT NULL,
        [Status] int NOT NULL,
        [Priority] tinyint NOT NULL,
        [PlannedAtUtc] datetimeoffset NULL,
        [RowVersion] rowversion NOT NULL,
        [BranchCode] nvarchar(10) NOT NULL DEFAULT N'0',
        [CreatedDate] datetime2 NULL,
        [UpdatedDate] datetime2 NULL,
        [DeletedDate] datetime2 NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [CreatedBy] bigint NULL,
        [UpdatedBy] bigint NULL,
        [DeletedBy] bigint NULL,
        CONSTRAINT [PK_RII_WO_TASK] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_RII_WO_TASK_RII_WO_HEADER_WarehouseOutboundHeaderId] FOREIGN KEY ([WarehouseOutboundHeaderId]) REFERENCES [RII_WO_HEADER] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724110747_AddWarehouseInboundOutboundModules'
)
BEGIN
    CREATE TABLE [RII_WI_LINE_SOURCE] (
        [Id] bigint NOT NULL IDENTITY,
        [GrLineId] bigint NOT NULL,
        [GrSourceDocumentId] bigint NOT NULL,
        [ExternalLineId] nvarchar(100) NOT NULL,
        [ExternalLineNo] int NULL,
        [ExternalStockCode] nvarchar(50) NOT NULL,
        [ExternalYapCode] nvarchar(50) NULL,
        [OrderedQuantity] decimal(18,6) NOT NULL,
        [PreviouslyReceivedQuantity] decimal(18,6) NOT NULL,
        [AllocatedQuantity] decimal(18,6) NOT NULL,
        [ReceivedQuantity] decimal(18,6) NOT NULL,
        [UnitCode] nvarchar(20) NOT NULL,
        [ExternalStatus] nvarchar(30) NULL,
        [BranchCode] nvarchar(10) NOT NULL DEFAULT N'0',
        [CreatedDate] datetime2 NULL,
        [UpdatedDate] datetime2 NULL,
        [DeletedDate] datetime2 NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [CreatedBy] bigint NULL,
        [UpdatedBy] bigint NULL,
        [DeletedBy] bigint NULL,
        CONSTRAINT [PK_RII_WI_LINE_SOURCE] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_RII_WI_LINE_SOURCE_QUANTITIES] CHECK ([OrderedQuantity] >= 0 AND [PreviouslyReceivedQuantity] >= 0 AND [AllocatedQuantity] >= 0 AND [ReceivedQuantity] >= 0),
        CONSTRAINT [FK_RII_WI_LINE_SOURCE_RII_WI_LINE_GrLineId] FOREIGN KEY ([GrLineId]) REFERENCES [RII_WI_LINE] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_RII_WI_LINE_SOURCE_RII_WI_SOURCE_DOCUMENT_GrSourceDocumentId] FOREIGN KEY ([GrSourceDocumentId]) REFERENCES [RII_WI_SOURCE_DOCUMENT] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724110747_AddWarehouseInboundOutboundModules'
)
BEGIN
    CREATE TABLE [RII_WI_EXECUTION] (
        [Id] bigint NOT NULL IDENTITY,
        [GrHeaderId] bigint NOT NULL,
        [GrTaskId] bigint NULL,
        [IdempotencyKey] uniqueidentifier NOT NULL,
        [RequestHash] varchar(64) NOT NULL,
        [ExecutionNo] nvarchar(60) NOT NULL,
        [Mode] nvarchar(30) NOT NULL,
        [Status] nvarchar(20) NOT NULL,
        [OccurredAtUtc] datetimeoffset(7) NOT NULL,
        [StockMovementOperationId] bigint NULL,
        [DeviceId] nvarchar(100) NULL,
        [Description] nvarchar(500) NULL,
        [ReversalOfExecutionId] bigint NULL,
        [RowVersion] rowversion NOT NULL,
        [BranchCode] nvarchar(10) NOT NULL DEFAULT N'0',
        [CreatedDate] datetime2 NULL,
        [UpdatedDate] datetime2 NULL,
        [DeletedDate] datetime2 NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [CreatedBy] bigint NULL,
        [UpdatedBy] bigint NULL,
        [DeletedBy] bigint NULL,
        CONSTRAINT [PK_RII_WI_EXECUTION] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_RII_WI_EXECUTION_RII_STOCK_MOVEMENT_OPERATION_StockMovementOperationId] FOREIGN KEY ([StockMovementOperationId]) REFERENCES [RII_STOCK_MOVEMENT_OPERATION] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_RII_WI_EXECUTION_RII_WI_EXECUTION_ReversalOfExecutionId] FOREIGN KEY ([ReversalOfExecutionId]) REFERENCES [RII_WI_EXECUTION] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_RII_WI_EXECUTION_RII_WI_HEADER_GrHeaderId] FOREIGN KEY ([GrHeaderId]) REFERENCES [RII_WI_HEADER] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_RII_WI_EXECUTION_RII_WI_TASK_GrTaskId] FOREIGN KEY ([GrTaskId]) REFERENCES [RII_WI_TASK] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724110747_AddWarehouseInboundOutboundModules'
)
BEGIN
    CREATE TABLE [RII_WI_TASK_ASSIGNMENT] (
        [Id] bigint NOT NULL IDENTITY,
        [GrTaskId] bigint NOT NULL,
        [UserId] bigint NOT NULL,
        [AssignmentRole] nvarchar(30) NOT NULL,
        [Status] nvarchar(30) NOT NULL,
        [AssignedAtUtc] datetimeoffset(7) NOT NULL,
        [AssignedBy] bigint NULL,
        [AcceptedAtUtc] datetimeoffset(7) NULL,
        [StartedAtUtc] datetimeoffset(7) NULL,
        [CompletedAtUtc] datetimeoffset(7) NULL,
        [UnassignedAtUtc] datetimeoffset(7) NULL,
        [UnassignedReason] nvarchar(500) NULL,
        [RowVersion] rowversion NOT NULL,
        [BranchCode] nvarchar(10) NOT NULL DEFAULT N'0',
        [CreatedDate] datetime2 NULL,
        [UpdatedDate] datetime2 NULL,
        [DeletedDate] datetime2 NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [CreatedBy] bigint NULL,
        [UpdatedBy] bigint NULL,
        [DeletedBy] bigint NULL,
        CONSTRAINT [PK_RII_WI_TASK_ASSIGNMENT] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_RII_WI_TASK_ASSIGNMENT_RII_USERS_UserId] FOREIGN KEY ([UserId]) REFERENCES [RII_USERS] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_RII_WI_TASK_ASSIGNMENT_RII_WI_TASK_GrTaskId] FOREIGN KEY ([GrTaskId]) REFERENCES [RII_WI_TASK] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724110747_AddWarehouseInboundOutboundModules'
)
BEGIN
    CREATE TABLE [RII_WI_TASK_LINE] (
        [Id] bigint NOT NULL IDENTITY,
        [GrTaskId] bigint NOT NULL,
        [GrLineId] bigint NOT NULL,
        [SequenceNo] int NOT NULL,
        [FromLocationId] bigint NULL,
        [ToLocationId] bigint NULL,
        [HandlingUnitId] bigint NULL,
        [PlannedQuantity] decimal(18,6) NOT NULL,
        [ProcessedQuantity] decimal(18,6) NOT NULL,
        [UnitCode] nvarchar(20) NOT NULL,
        [Status] nvarchar(30) NOT NULL,
        [Description] nvarchar(500) NULL,
        [RowVersion] rowversion NOT NULL,
        [BranchCode] nvarchar(10) NOT NULL DEFAULT N'0',
        [CreatedDate] datetime2 NULL,
        [UpdatedDate] datetime2 NULL,
        [DeletedDate] datetime2 NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [CreatedBy] bigint NULL,
        [UpdatedBy] bigint NULL,
        [DeletedBy] bigint NULL,
        CONSTRAINT [PK_RII_WI_TASK_LINE] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_RII_WI_TASK_LINE_QUANTITY] CHECK ([PlannedQuantity] > 0 AND [ProcessedQuantity] >= 0),
        CONSTRAINT [CK_RII_WI_TASK_LINE_SEQUENCE] CHECK ([SequenceNo] > 0),
        CONSTRAINT [FK_RII_WI_TASK_LINE_RII_LOCATION_FromLocationId] FOREIGN KEY ([FromLocationId]) REFERENCES [RII_LOCATION] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_RII_WI_TASK_LINE_RII_LOCATION_ToLocationId] FOREIGN KEY ([ToLocationId]) REFERENCES [RII_LOCATION] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_RII_WI_TASK_LINE_RII_WI_LINE_GrLineId] FOREIGN KEY ([GrLineId]) REFERENCES [RII_WI_LINE] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_RII_WI_TASK_LINE_RII_WI_TASK_GrTaskId] FOREIGN KEY ([GrTaskId]) REFERENCES [RII_WI_TASK] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724110747_AddWarehouseInboundOutboundModules'
)
BEGIN
    CREATE TABLE [RII_WO_TRACKING] (
        [Id] bigint NOT NULL IDENTITY,
        [WarehouseOutboundLineId] bigint NOT NULL,
        [HandlingUnitNo] nvarchar(100) NULL,
        [ContainerNo] nvarchar(100) NULL,
        [LotNo] nvarchar(100) NULL,
        [SerialNo] nvarchar(200) NULL,
        [PlannedQuantity] decimal(20,6) NOT NULL,
        [ReservedQuantity] decimal(20,6) NOT NULL,
        [PickedQuantity] decimal(20,6) NOT NULL,
        [PackedQuantity] decimal(20,6) NOT NULL,
        [LoadedQuantity] decimal(20,6) NOT NULL,
        [ShippedQuantity] decimal(20,6) NOT NULL,
        [SourceLocationId] bigint NULL,
        [RowVersion] rowversion NOT NULL,
        [BranchCode] nvarchar(10) NOT NULL DEFAULT N'0',
        [CreatedDate] datetime2 NULL,
        [UpdatedDate] datetime2 NULL,
        [DeletedDate] datetime2 NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [CreatedBy] bigint NULL,
        [UpdatedBy] bigint NULL,
        [DeletedBy] bigint NULL,
        CONSTRAINT [PK_RII_WO_TRACKING] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_RII_WO_TRACKING_QTY] CHECK ([PlannedQuantity] > 0 AND [ReservedQuantity] >= 0 AND [PickedQuantity] >= 0 AND [PackedQuantity] >= 0 AND [LoadedQuantity] >= 0 AND [ShippedQuantity] >= 0),
        CONSTRAINT [FK_RII_WO_TRACKING_RII_WO_LINE_WarehouseOutboundLineId] FOREIGN KEY ([WarehouseOutboundLineId]) REFERENCES [RII_WO_LINE] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724110747_AddWarehouseInboundOutboundModules'
)
BEGIN
    CREATE TABLE [RII_WO_LINE_SOURCE] (
        [Id] bigint NOT NULL IDENTITY,
        [WarehouseOutboundLineId] bigint NOT NULL,
        [WarehouseOutboundSourceDocumentId] bigint NOT NULL,
        [ExternalLineId] nvarchar(100) NOT NULL,
        [ExternalLineNo] int NULL,
        [ExternalStockCode] nvarchar(100) NOT NULL,
        [ExternalYapCode] nvarchar(100) NULL,
        [OrderedQuantity] decimal(20,6) NOT NULL,
        [PreviouslyShippedQuantity] decimal(20,6) NOT NULL,
        [AllocatedQuantity] decimal(20,6) NOT NULL,
        [UnitCode] nvarchar(20) NOT NULL,
        [BranchCode] nvarchar(10) NOT NULL DEFAULT N'0',
        [CreatedDate] datetime2 NULL,
        [UpdatedDate] datetime2 NULL,
        [DeletedDate] datetime2 NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [CreatedBy] bigint NULL,
        [UpdatedBy] bigint NULL,
        [DeletedBy] bigint NULL,
        CONSTRAINT [PK_RII_WO_LINE_SOURCE] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_RII_WO_LINE_SOURCE_RII_WO_LINE_WarehouseOutboundLineId] FOREIGN KEY ([WarehouseOutboundLineId]) REFERENCES [RII_WO_LINE] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_RII_WO_LINE_SOURCE_RII_WO_SOURCE_DOCUMENT_WarehouseOutboundSourceDocumentId] FOREIGN KEY ([WarehouseOutboundSourceDocumentId]) REFERENCES [RII_WO_SOURCE_DOCUMENT] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724110747_AddWarehouseInboundOutboundModules'
)
BEGIN
    CREATE TABLE [RII_WO_TASK_ASSIGNMENT] (
        [Id] bigint NOT NULL IDENTITY,
        [WarehouseOutboundTaskId] bigint NOT NULL,
        [UserId] bigint NOT NULL,
        [IsPrimary] bit NOT NULL,
        [AssignedAtUtc] datetimeoffset NOT NULL,
        [AssignedBy] bigint NOT NULL,
        [AcceptedAtUtc] datetimeoffset NULL,
        [BranchCode] nvarchar(10) NOT NULL DEFAULT N'0',
        [CreatedDate] datetime2 NULL,
        [UpdatedDate] datetime2 NULL,
        [DeletedDate] datetime2 NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [CreatedBy] bigint NULL,
        [UpdatedBy] bigint NULL,
        [DeletedBy] bigint NULL,
        CONSTRAINT [PK_RII_WO_TASK_ASSIGNMENT] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_RII_WO_TASK_ASSIGNMENT_RII_WO_TASK_WarehouseOutboundTaskId] FOREIGN KEY ([WarehouseOutboundTaskId]) REFERENCES [RII_WO_TASK] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724110747_AddWarehouseInboundOutboundModules'
)
BEGIN
    CREATE TABLE [RII_WO_TASK_LINE] (
        [Id] bigint NOT NULL IDENTITY,
        [WarehouseOutboundTaskId] bigint NOT NULL,
        [WarehouseOutboundLineId] bigint NOT NULL,
        [PlannedQuantity] decimal(20,6) NOT NULL,
        [ProcessedQuantity] decimal(20,6) NOT NULL,
        [SourceLocationId] bigint NULL,
        [RowVersion] rowversion NOT NULL,
        [BranchCode] nvarchar(10) NOT NULL DEFAULT N'0',
        [CreatedDate] datetime2 NULL,
        [UpdatedDate] datetime2 NULL,
        [DeletedDate] datetime2 NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [CreatedBy] bigint NULL,
        [UpdatedBy] bigint NULL,
        [DeletedBy] bigint NULL,
        CONSTRAINT [PK_RII_WO_TASK_LINE] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_RII_WO_TASK_LINE_QTY] CHECK ([PlannedQuantity] > 0 AND [ProcessedQuantity] >= 0),
        CONSTRAINT [FK_RII_WO_TASK_LINE_RII_WO_LINE_WarehouseOutboundLineId] FOREIGN KEY ([WarehouseOutboundLineId]) REFERENCES [RII_WO_LINE] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_RII_WO_TASK_LINE_RII_WO_TASK_WarehouseOutboundTaskId] FOREIGN KEY ([WarehouseOutboundTaskId]) REFERENCES [RII_WO_TASK] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724110747_AddWarehouseInboundOutboundModules'
)
BEGIN
    CREATE TABLE [RII_WI_LABEL] (
        [Id] bigint NOT NULL IDENTITY,
        [BatchId] bigint NOT NULL,
        [GrHeaderId] bigint NOT NULL,
        [GrLineId] bigint NULL,
        [GrTaskLineId] bigint NULL,
        [StockId] bigint NULL,
        [StockCodeSnapshot] nvarchar(50) NOT NULL,
        [StockNameSnapshot] nvarchar(250) NULL,
        [YapCodeId] bigint NULL,
        [YapCodeSnapshot] nvarchar(50) NULL,
        [LabelQuantity] decimal(18,6) NOT NULL,
        [UnitCode] nvarchar(20) NOT NULL,
        [LotNo] nvarchar(100) NULL,
        [SerialNo] nvarchar(100) NULL,
        [ManufacturingDate] date NULL,
        [ExpirationDate] date NULL,
        [BarcodeValue] nvarchar(200) NOT NULL,
        [Status] nvarchar(30) NOT NULL,
        [PrintCount] int NOT NULL,
        [LastPrintedAtUtc] datetimeoffset(7) NULL,
        [AssignedAtUtc] datetimeoffset(7) NULL,
        [ConsumedAtUtc] datetimeoffset(7) NULL,
        [VoidReason] nvarchar(500) NULL,
        [Description] nvarchar(500) NULL,
        [RowVersion] rowversion NOT NULL,
        [BranchCode] nvarchar(10) NOT NULL DEFAULT N'0',
        [CreatedDate] datetime2 NULL,
        [UpdatedDate] datetime2 NULL,
        [DeletedDate] datetime2 NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [CreatedBy] bigint NULL,
        [UpdatedBy] bigint NULL,
        [DeletedBy] bigint NULL,
        CONSTRAINT [PK_RII_WI_LABEL] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_RII_WI_LABEL_PRINT_COUNT] CHECK ([PrintCount] >= 0),
        CONSTRAINT [CK_RII_WI_LABEL_QUANTITY] CHECK ([LabelQuantity] > 0),
        CONSTRAINT [FK_RII_WI_LABEL_RII_STOCK_StockId] FOREIGN KEY ([StockId]) REFERENCES [RII_STOCK] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_RII_WI_LABEL_RII_WI_HEADER_GrHeaderId] FOREIGN KEY ([GrHeaderId]) REFERENCES [RII_WI_HEADER] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_RII_WI_LABEL_RII_WI_LABEL_BATCH_BatchId] FOREIGN KEY ([BatchId]) REFERENCES [RII_WI_LABEL_BATCH] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_RII_WI_LABEL_RII_WI_LINE_GrLineId] FOREIGN KEY ([GrLineId]) REFERENCES [RII_WI_LINE] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_RII_WI_LABEL_RII_WI_TASK_LINE_GrTaskLineId] FOREIGN KEY ([GrTaskLineId]) REFERENCES [RII_WI_TASK_LINE] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_RII_WI_LABEL_RII_YAP_CODE_YapCodeId] FOREIGN KEY ([YapCodeId]) REFERENCES [RII_YAP_CODE] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724110747_AddWarehouseInboundOutboundModules'
)
BEGIN
    CREATE TABLE [RII_WI_TASK_LINE_TRACKING] (
        [Id] bigint NOT NULL IDENTITY,
        [GrTaskLineId] bigint NOT NULL,
        [SequenceNo] int NOT NULL,
        [StockId] bigint NOT NULL,
        [PlannedQuantity] decimal(18,6) NOT NULL,
        [LotNo] nvarchar(100) NULL,
        [SerialNo] nvarchar(100) NULL,
        [ManufacturingDate] date NULL,
        [ExpirationDate] date NULL,
        [TargetWarehouseId] bigint NOT NULL,
        [ToLocationId] bigint NOT NULL,
        [Description] nvarchar(500) NULL,
        [RowVersion] rowversion NOT NULL,
        [BranchCode] nvarchar(10) NOT NULL DEFAULT N'0',
        [CreatedDate] datetime2 NULL,
        [UpdatedDate] datetime2 NULL,
        [DeletedDate] datetime2 NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [CreatedBy] bigint NULL,
        [UpdatedBy] bigint NULL,
        [DeletedBy] bigint NULL,
        CONSTRAINT [PK_RII_WI_TASK_LINE_TRACKING] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_RII_WI_TASK_LINE_TRACKING_QTY] CHECK ([PlannedQuantity] > 0),
        CONSTRAINT [CK_RII_WI_TASK_LINE_TRACKING_SEQUENCE] CHECK ([SequenceNo] > 0),
        CONSTRAINT [FK_RII_WI_TASK_LINE_TRACKING_RII_LOCATION_ToLocationId] FOREIGN KEY ([ToLocationId]) REFERENCES [RII_LOCATION] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_RII_WI_TASK_LINE_TRACKING_RII_STOCK_StockId] FOREIGN KEY ([StockId]) REFERENCES [RII_STOCK] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_RII_WI_TASK_LINE_TRACKING_RII_WAREHOUSE_TargetWarehouseId] FOREIGN KEY ([TargetWarehouseId]) REFERENCES [RII_WAREHOUSE] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_RII_WI_TASK_LINE_TRACKING_RII_WI_TASK_LINE_GrTaskLineId] FOREIGN KEY ([GrTaskLineId]) REFERENCES [RII_WI_TASK_LINE] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724110747_AddWarehouseInboundOutboundModules'
)
BEGIN
    CREATE TABLE [RII_WI_EXECUTION_LINE] (
        [Id] bigint NOT NULL IDENTITY,
        [GrExecutionId] bigint NOT NULL,
        [GrLineId] bigint NOT NULL,
        [LineNo] int NOT NULL,
        [StockId] bigint NOT NULL,
        [YapCodeId] bigint NULL,
        [Quantity] decimal(18,6) NOT NULL,
        [UnitCode] nvarchar(20) NOT NULL,
        [LotNo] nvarchar(100) NULL,
        [SerialNo] nvarchar(100) NULL,
        [SerialNumberRuleId] bigint NULL,
        [SerialNumberRuleVersion] int NULL,
        [SerialNumberRuleCodeSnapshot] nvarchar(50) NULL,
        [SerialMaskSnapshot] nvarchar(250) NULL,
        [ManufacturingDate] date NULL,
        [ExpirationDate] date NULL,
        [ScannedBarcode] nvarchar(250) NULL,
        [WarehouseId] bigint NOT NULL,
        [LocationId] bigint NOT NULL,
        [StockStatus] nvarchar(30) NOT NULL,
        [WarehouseInboundLabelId] bigint NULL,
        [QualityInspectionLineId] bigint NULL,
        [BranchCode] nvarchar(10) NOT NULL DEFAULT N'0',
        [CreatedDate] datetime2 NULL,
        [UpdatedDate] datetime2 NULL,
        [DeletedDate] datetime2 NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [CreatedBy] bigint NULL,
        [UpdatedBy] bigint NULL,
        [DeletedBy] bigint NULL,
        CONSTRAINT [PK_RII_WI_EXECUTION_LINE] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_RII_WI_EXECUTION_LINE_NO] CHECK ([LineNo] > 0),
        CONSTRAINT [CK_RII_WI_EXECUTION_LINE_QTY] CHECK ([Quantity] > 0),
        CONSTRAINT [FK_RII_WI_EXECUTION_LINE_RII_LOCATION_LocationId] FOREIGN KEY ([LocationId]) REFERENCES [RII_LOCATION] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_RII_WI_EXECUTION_LINE_RII_QUALITY_INSPECTION_LINES_QualityInspectionLineId] FOREIGN KEY ([QualityInspectionLineId]) REFERENCES [RII_QUALITY_INSPECTION_LINES] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_RII_WI_EXECUTION_LINE_RII_SERIAL_NUMBER_RULES_SerialNumberRuleId] FOREIGN KEY ([SerialNumberRuleId]) REFERENCES [RII_SERIAL_NUMBER_RULES] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_RII_WI_EXECUTION_LINE_RII_STOCK_StockId] FOREIGN KEY ([StockId]) REFERENCES [RII_STOCK] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_RII_WI_EXECUTION_LINE_RII_WAREHOUSE_WarehouseId] FOREIGN KEY ([WarehouseId]) REFERENCES [RII_WAREHOUSE] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_RII_WI_EXECUTION_LINE_RII_WI_EXECUTION_GrExecutionId] FOREIGN KEY ([GrExecutionId]) REFERENCES [RII_WI_EXECUTION] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_RII_WI_EXECUTION_LINE_RII_WI_LABEL_WarehouseInboundLabelId] FOREIGN KEY ([WarehouseInboundLabelId]) REFERENCES [RII_WI_LABEL] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_RII_WI_EXECUTION_LINE_RII_WI_LINE_GrLineId] FOREIGN KEY ([GrLineId]) REFERENCES [RII_WI_LINE] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_RII_WI_EXECUTION_LINE_RII_YAP_CODE_YapCodeId] FOREIGN KEY ([YapCodeId]) REFERENCES [RII_YAP_CODE] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724110747_AddWarehouseInboundOutboundModules'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'AvailableOnMobile', N'AvailableOnWeb', N'BranchCode', N'Code', N'CreatedBy', N'CreatedDate', N'DeletedBy', N'DeletedDate', N'Description', N'IsActive', N'Name', N'UpdatedBy', N'UpdatedDate') AND [object_id] = OBJECT_ID(N'[RII_PERMISSION_DEFINITIONS]'))
        SET IDENTITY_INSERT [RII_PERMISSION_DEFINITIONS] ON;
    EXEC(N'INSERT INTO [RII_PERMISSION_DEFINITIONS] ([Id], [AvailableOnMobile], [AvailableOnWeb], [BranchCode], [Code], [CreatedBy], [CreatedDate], [DeletedBy], [DeletedDate], [Description], [IsActive], [Name], [UpdatedBy], [UpdatedDate])
    VALUES (CAST(2100 AS bigint), CAST(0 AS bit), CAST(1 AS bit), N''0'', N''WMS.WAREHOUSE_INBOUND.VIEW'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, NULL, CAST(1 AS bit), N''Ambar giriÅŸlerini gÃ¶rÃ¼ntÃ¼le'', NULL, NULL),
    (CAST(2101 AS bigint), CAST(0 AS bit), CAST(1 AS bit), N''0'', N''WMS.WAREHOUSE_INBOUND.CREATE'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, NULL, CAST(1 AS bit), N''Ambar giriÅŸi oluÅŸtur'', NULL, NULL),
    (CAST(2102 AS bigint), CAST(0 AS bit), CAST(1 AS bit), N''0'', N''WMS.WAREHOUSE_INBOUND.UPDATE'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, NULL, CAST(1 AS bit), N''Ambar giriÅŸini gÃ¼ncelle'', NULL, NULL),
    (CAST(2103 AS bigint), CAST(0 AS bit), CAST(1 AS bit), N''0'', N''WMS.WAREHOUSE_INBOUND.RELEASE'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, NULL, CAST(1 AS bit), N''Ambar giriÅŸini iÅŸleme aÃ§'', NULL, NULL),
    (CAST(2104 AS bigint), CAST(0 AS bit), CAST(1 AS bit), N''0'', N''WMS.WAREHOUSE_INBOUND.RECEIVE'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, NULL, CAST(1 AS bit), N''Ambar giriÅŸini iÅŸle'', NULL, NULL),
    (CAST(2105 AS bigint), CAST(0 AS bit), CAST(1 AS bit), N''0'', N''WMS.WAREHOUSE_INBOUND.COMPLETE'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, NULL, CAST(1 AS bit), N''Ambar giriÅŸini tamamla'', NULL, NULL),
    (CAST(2106 AS bigint), CAST(0 AS bit), CAST(1 AS bit), N''0'', N''WMS.WAREHOUSE_INBOUND.CANCEL'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, NULL, CAST(1 AS bit), N''Ambar giriÅŸini iptal et'', NULL, NULL),
    (CAST(2107 AS bigint), CAST(0 AS bit), CAST(1 AS bit), N''0'', N''WMS.WAREHOUSE_INBOUND.SETTINGS.VIEW'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, NULL, CAST(1 AS bit), N''Ambar giriÅŸ ayarlarÄ±nÄ± gÃ¶rÃ¼ntÃ¼le'', NULL, NULL),
    (CAST(2108 AS bigint), CAST(0 AS bit), CAST(1 AS bit), N''0'', N''WMS.WAREHOUSE_INBOUND.SETTINGS.MANAGE'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, NULL, CAST(1 AS bit), N''Ambar giriÅŸ ayarlarÄ±nÄ± yÃ¶net'', NULL, NULL),
    (CAST(2110 AS bigint), CAST(0 AS bit), CAST(1 AS bit), N''0'', N''WMS.WAREHOUSE_OUTBOUND.VIEW'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, NULL, CAST(1 AS bit), N''Ambar Ã§Ä±kÄ±ÅŸlarÄ±nÄ± gÃ¶rÃ¼ntÃ¼le'', NULL, NULL),
    (CAST(2111 AS bigint), CAST(0 AS bit), CAST(1 AS bit), N''0'', N''WMS.WAREHOUSE_OUTBOUND.CREATE'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, NULL, CAST(1 AS bit), N''Ambar Ã§Ä±kÄ±ÅŸÄ± oluÅŸtur'', NULL, NULL),
    (CAST(2112 AS bigint), CAST(0 AS bit), CAST(1 AS bit), N''0'', N''WMS.WAREHOUSE_OUTBOUND.UPDATE'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, NULL, CAST(1 AS bit), N''Ambar Ã§Ä±kÄ±ÅŸÄ±nÄ± gÃ¼ncelle'', NULL, NULL),
    (CAST(2113 AS bigint), CAST(0 AS bit), CAST(1 AS bit), N''0'', N''WMS.WAREHOUSE_OUTBOUND.DELETE'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, NULL, CAST(1 AS bit), N''Ambar Ã§Ä±kÄ±ÅŸ taslaÄŸÄ±nÄ± sil'', NULL, NULL),
    (CAST(2114 AS bigint), CAST(0 AS bit), CAST(1 AS bit), N''0'', N''WMS.WAREHOUSE_OUTBOUND.OPERATE'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, NULL, CAST(1 AS bit), N''Ambar Ã§Ä±kÄ±ÅŸ operasyonunu yÃ¼rÃ¼t'', NULL, NULL),
    (CAST(2115 AS bigint), CAST(0 AS bit), CAST(1 AS bit), N''0'', N''WMS.WAREHOUSE_OUTBOUND.APPROVE'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, NULL, CAST(1 AS bit), N''Ambar Ã§Ä±kÄ±ÅŸÄ±nÄ± onayla'', NULL, NULL),
    (CAST(2116 AS bigint), CAST(0 AS bit), CAST(1 AS bit), N''0'', N''WMS.WAREHOUSE_OUTBOUND.CANCEL'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, NULL, CAST(1 AS bit), N''Ambar Ã§Ä±kÄ±ÅŸÄ±nÄ± iptal et'', NULL, NULL),
    (CAST(2117 AS bigint), CAST(0 AS bit), CAST(1 AS bit), N''0'', N''WMS.WAREHOUSE_OUTBOUND.SETTINGS.VIEW'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, NULL, CAST(1 AS bit), N''Ambar Ã§Ä±kÄ±ÅŸ ayarlarÄ±nÄ± gÃ¶rÃ¼ntÃ¼le'', NULL, NULL),
    (CAST(2118 AS bigint), CAST(0 AS bit), CAST(1 AS bit), N''0'', N''WMS.WAREHOUSE_OUTBOUND.SETTINGS.MANAGE'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, NULL, CAST(1 AS bit), N''Ambar Ã§Ä±kÄ±ÅŸ ayarlarÄ±nÄ± yÃ¶net'', NULL, NULL)');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'AvailableOnMobile', N'AvailableOnWeb', N'BranchCode', N'Code', N'CreatedBy', N'CreatedDate', N'DeletedBy', N'DeletedDate', N'Description', N'IsActive', N'Name', N'UpdatedBy', N'UpdatedDate') AND [object_id] = OBJECT_ID(N'[RII_PERMISSION_DEFINITIONS]'))
        SET IDENTITY_INSERT [RII_PERMISSION_DEFINITIONS] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724110747_AddWarehouseInboundOutboundModules'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'BranchCode', N'CreatedBy', N'CreatedDate', N'DeletedBy', N'DeletedDate', N'PermissionDefinitionId', N'PermissionGroupId', N'UpdatedBy', N'UpdatedDate') AND [object_id] = OBJECT_ID(N'[RII_PERMISSION_GROUP_PERMISSIONS]'))
        SET IDENTITY_INSERT [RII_PERMISSION_GROUP_PERMISSIONS] ON;
    EXEC(N'INSERT INTO [RII_PERMISSION_GROUP_PERMISSIONS] ([Id], [BranchCode], [CreatedBy], [CreatedDate], [DeletedBy], [DeletedDate], [PermissionDefinitionId], [PermissionGroupId], [UpdatedBy], [UpdatedDate])
    VALUES (CAST(2100 AS bigint), N''0'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, CAST(2100 AS bigint), CAST(1001 AS bigint), NULL, NULL),
    (CAST(2101 AS bigint), N''0'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, CAST(2101 AS bigint), CAST(1001 AS bigint), NULL, NULL),
    (CAST(2102 AS bigint), N''0'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, CAST(2102 AS bigint), CAST(1001 AS bigint), NULL, NULL),
    (CAST(2103 AS bigint), N''0'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, CAST(2103 AS bigint), CAST(1001 AS bigint), NULL, NULL),
    (CAST(2104 AS bigint), N''0'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, CAST(2104 AS bigint), CAST(1001 AS bigint), NULL, NULL),
    (CAST(2105 AS bigint), N''0'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, CAST(2105 AS bigint), CAST(1001 AS bigint), NULL, NULL),
    (CAST(2106 AS bigint), N''0'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, CAST(2106 AS bigint), CAST(1001 AS bigint), NULL, NULL),
    (CAST(2107 AS bigint), N''0'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, CAST(2107 AS bigint), CAST(1001 AS bigint), NULL, NULL),
    (CAST(2108 AS bigint), N''0'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, CAST(2108 AS bigint), CAST(1001 AS bigint), NULL, NULL),
    (CAST(2110 AS bigint), N''0'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, CAST(2110 AS bigint), CAST(1001 AS bigint), NULL, NULL),
    (CAST(2111 AS bigint), N''0'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, CAST(2111 AS bigint), CAST(1001 AS bigint), NULL, NULL),
    (CAST(2112 AS bigint), N''0'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, CAST(2112 AS bigint), CAST(1001 AS bigint), NULL, NULL),
    (CAST(2113 AS bigint), N''0'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, CAST(2113 AS bigint), CAST(1001 AS bigint), NULL, NULL),
    (CAST(2114 AS bigint), N''0'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, CAST(2114 AS bigint), CAST(1001 AS bigint), NULL, NULL),
    (CAST(2115 AS bigint), N''0'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, CAST(2115 AS bigint), CAST(1001 AS bigint), NULL, NULL),
    (CAST(2116 AS bigint), N''0'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, CAST(2116 AS bigint), CAST(1001 AS bigint), NULL, NULL),
    (CAST(2117 AS bigint), N''0'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, CAST(2117 AS bigint), CAST(1001 AS bigint), NULL, NULL),
    (CAST(2118 AS bigint), N''0'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, CAST(2118 AS bigint), CAST(1001 AS bigint), NULL, NULL)');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'BranchCode', N'CreatedBy', N'CreatedDate', N'DeletedBy', N'DeletedDate', N'PermissionDefinitionId', N'PermissionGroupId', N'UpdatedBy', N'UpdatedDate') AND [object_id] = OBJECT_ID(N'[RII_PERMISSION_GROUP_PERMISSIONS]'))
        SET IDENTITY_INSERT [RII_PERMISSION_GROUP_PERMISSIONS] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724110747_AddWarehouseInboundOutboundModules'
)
BEGIN
    EXEC sys.sp_executesql N'CREATE INDEX [IX_RII_QUALITY_INSPECTION_LINES_WarehouseInboundLineId] ON [RII_QUALITY_INSPECTION_LINES] ([WarehouseInboundLineId]);';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724110747_AddWarehouseInboundOutboundModules'
)
BEGIN
    CREATE INDEX [IX_RII_WI_EXECUTION_GrTaskId] ON [RII_WI_EXECUTION] ([GrTaskId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724110747_AddWarehouseInboundOutboundModules'
)
BEGIN
    CREATE INDEX [IX_RII_WI_EXECUTION_HEADER_TIME] ON [RII_WI_EXECUTION] ([GrHeaderId], [OccurredAtUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724110747_AddWarehouseInboundOutboundModules'
)
BEGIN
    CREATE INDEX [IX_RII_WI_EXECUTION_IsDeleted] ON [RII_WI_EXECUTION] ([IsDeleted]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724110747_AddWarehouseInboundOutboundModules'
)
BEGIN
    CREATE INDEX [IX_RII_WI_EXECUTION_ReversalOfExecutionId] ON [RII_WI_EXECUTION] ([ReversalOfExecutionId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724110747_AddWarehouseInboundOutboundModules'
)
BEGIN
    CREATE INDEX [IX_RII_WI_EXECUTION_StockMovementOperationId] ON [RII_WI_EXECUTION] ([StockMovementOperationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724110747_AddWarehouseInboundOutboundModules'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_RII_WI_EXECUTION_BRANCH_NO] ON [RII_WI_EXECUTION] ([BranchCode], [ExecutionNo]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724110747_AddWarehouseInboundOutboundModules'
)
BEGIN
    CREATE UNIQUE INDEX [UX_RII_WI_EXECUTION_IDEMPOTENCY] ON [RII_WI_EXECUTION] ([IdempotencyKey]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724110747_AddWarehouseInboundOutboundModules'
)
BEGIN
    CREATE INDEX [IX_RII_WI_EXECUTION_LINE_GR_LINE] ON [RII_WI_EXECUTION_LINE] ([GrLineId], [GrExecutionId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724110747_AddWarehouseInboundOutboundModules'
)
BEGIN
    CREATE INDEX [IX_RII_WI_EXECUTION_LINE_IsDeleted] ON [RII_WI_EXECUTION_LINE] ([IsDeleted]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724110747_AddWarehouseInboundOutboundModules'
)
BEGIN
    CREATE INDEX [IX_RII_WI_EXECUTION_LINE_LocationId] ON [RII_WI_EXECUTION_LINE] ([LocationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724110747_AddWarehouseInboundOutboundModules'
)
BEGIN
    CREATE INDEX [IX_RII_WI_EXECUTION_LINE_QualityInspectionLineId] ON [RII_WI_EXECUTION_LINE] ([QualityInspectionLineId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724110747_AddWarehouseInboundOutboundModules'
)
BEGIN
    CREATE INDEX [IX_RII_WI_EXECUTION_LINE_SerialNumberRuleId] ON [RII_WI_EXECUTION_LINE] ([SerialNumberRuleId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724110747_AddWarehouseInboundOutboundModules'
)
BEGIN
    CREATE INDEX [IX_RII_WI_EXECUTION_LINE_TRACE] ON [RII_WI_EXECUTION_LINE] ([StockId], [LotNo], [SerialNo]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724110747_AddWarehouseInboundOutboundModules'
)
BEGIN
    CREATE INDEX [IX_RII_WI_EXECUTION_LINE_WarehouseId] ON [RII_WI_EXECUTION_LINE] ([WarehouseId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724110747_AddWarehouseInboundOutboundModules'
)
BEGIN
    CREATE INDEX [IX_RII_WI_EXECUTION_LINE_WarehouseInboundLabelId] ON [RII_WI_EXECUTION_LINE] ([WarehouseInboundLabelId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724110747_AddWarehouseInboundOutboundModules'
)
BEGIN
    CREATE INDEX [IX_RII_WI_EXECUTION_LINE_YapCodeId] ON [RII_WI_EXECUTION_LINE] ([YapCodeId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724110747_AddWarehouseInboundOutboundModules'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_RII_WI_EXECUTION_LINE_SEQUENCE] ON [RII_WI_EXECUTION_LINE] ([GrExecutionId], [LineNo]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724110747_AddWarehouseInboundOutboundModules'
)
BEGIN
    CREATE INDEX [IX_RII_WI_HEADER_BRANCH_STATUS_PLANNED] ON [RII_WI_HEADER] ([BranchCode], [Status], [PlannedArrivalAtUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724110747_AddWarehouseInboundOutboundModules'
)
BEGIN
    CREATE INDEX [IX_RII_WI_HEADER_DocumentSeriesId] ON [RII_WI_HEADER] ([DocumentSeriesId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724110747_AddWarehouseInboundOutboundModules'
)
BEGIN
    CREATE INDEX [IX_RII_WI_HEADER_IsDeleted] ON [RII_WI_HEADER] ([IsDeleted]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724110747_AddWarehouseInboundOutboundModules'
)
BEGIN
    CREATE INDEX [IX_RII_WI_HEADER_PROCESS_REPORTING] ON [RII_WI_HEADER] ([BranchCode], [ProcessType], [Status], [DocumentDate]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724110747_AddWarehouseInboundOutboundModules'
)
BEGIN
    CREATE INDEX [IX_RII_WI_HEADER_QualityLocationId] ON [RII_WI_HEADER] ([QualityLocationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724110747_AddWarehouseInboundOutboundModules'
)
BEGIN
    CREATE INDEX [IX_RII_WI_HEADER_QuarantineLocationId] ON [RII_WI_HEADER] ([QuarantineLocationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724110747_AddWarehouseInboundOutboundModules'
)
BEGIN
    CREATE INDEX [IX_RII_WI_HEADER_ReceivingLocationId] ON [RII_WI_HEADER] ([ReceivingLocationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724110747_AddWarehouseInboundOutboundModules'
)
BEGIN
    CREATE INDEX [IX_RII_WI_HEADER_SUPPLIER_STATUS] ON [RII_WI_HEADER] ([SupplierId], [Status]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724110747_AddWarehouseInboundOutboundModules'
)
BEGIN
    CREATE INDEX [IX_RII_WI_HEADER_WAREHOUSE_STATUS] ON [RII_WI_HEADER] ([TargetWarehouseId], [Status]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724110747_AddWarehouseInboundOutboundModules'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_RII_WI_HEADER_BRANCH_DOCUMENT_NO] ON [RII_WI_HEADER] ([BranchCode], [DocumentNo]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724110747_AddWarehouseInboundOutboundModules'
)
BEGIN
    CREATE UNIQUE INDEX [UX_RII_WI_HEADER_CORRELATION_ID] ON [RII_WI_HEADER] ([CorrelationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724110747_AddWarehouseInboundOutboundModules'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_RII_WI_HEADER_SUPPLIER_EWAYBILL] ON [RII_WI_HEADER] ([BranchCode], [SupplierId], [ElectronicWaybillNo]) WHERE [IsDeleted] = 0 AND [ElectronicWaybillNo] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724110747_AddWarehouseInboundOutboundModules'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_RII_WI_HEADER_SUPPLIER_WAYBILL] ON [RII_WI_HEADER] ([BranchCode], [SupplierId], [WaybillNo]) WHERE [IsDeleted] = 0 AND [WaybillNo] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724110747_AddWarehouseInboundOutboundModules'
)
BEGIN
    CREATE INDEX [IX_RII_WI_LABEL_BATCH_STATUS] ON [RII_WI_LABEL] ([BatchId], [Status]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724110747_AddWarehouseInboundOutboundModules'
)
BEGIN
    CREATE INDEX [IX_RII_WI_LABEL_GrLineId] ON [RII_WI_LABEL] ([GrLineId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724110747_AddWarehouseInboundOutboundModules'
)
BEGIN
    CREATE INDEX [IX_RII_WI_LABEL_GrTaskLineId] ON [RII_WI_LABEL] ([GrTaskLineId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724110747_AddWarehouseInboundOutboundModules'
)
BEGIN
    CREATE INDEX [IX_RII_WI_LABEL_HEADER_LINE] ON [RII_WI_LABEL] ([GrHeaderId], [GrLineId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724110747_AddWarehouseInboundOutboundModules'
)
BEGIN
    CREATE INDEX [IX_RII_WI_LABEL_IsDeleted] ON [RII_WI_LABEL] ([IsDeleted]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724110747_AddWarehouseInboundOutboundModules'
)
BEGIN
    CREATE INDEX [IX_RII_WI_LABEL_TRACE] ON [RII_WI_LABEL] ([StockId], [LotNo], [SerialNo]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724110747_AddWarehouseInboundOutboundModules'
)
BEGIN
    CREATE INDEX [IX_RII_WI_LABEL_YapCodeId] ON [RII_WI_LABEL] ([YapCodeId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724110747_AddWarehouseInboundOutboundModules'
)
BEGIN
    CREATE UNIQUE INDEX [UX_RII_WI_LABEL_BARCODE] ON [RII_WI_LABEL] ([BarcodeValue]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724110747_AddWarehouseInboundOutboundModules'
)
BEGIN
    CREATE INDEX [IX_RII_WI_LABEL_BATCH_HEADER_STATUS] ON [RII_WI_LABEL_BATCH] ([GrHeaderId], [Status]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724110747_AddWarehouseInboundOutboundModules'
)
BEGIN
    CREATE INDEX [IX_RII_WI_LABEL_BATCH_IsDeleted] ON [RII_WI_LABEL_BATCH] ([IsDeleted]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724110747_AddWarehouseInboundOutboundModules'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_RII_WI_LABEL_BATCH_BRANCH_BATCH_NO] ON [RII_WI_LABEL_BATCH] ([BranchCode], [BatchNo]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724110747_AddWarehouseInboundOutboundModules'
)
BEGIN
    CREATE UNIQUE INDEX [UX_RII_WI_LABEL_BATCH_CORRELATION] ON [RII_WI_LABEL_BATCH] ([CorrelationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724110747_AddWarehouseInboundOutboundModules'
)
BEGIN
    CREATE INDEX [IX_RII_WI_LINE_DefaultPutawayLocationId] ON [RII_WI_LINE] ([DefaultPutawayLocationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724110747_AddWarehouseInboundOutboundModules'
)
BEGIN
    CREATE INDEX [IX_RII_WI_LINE_DefaultReceivingLocationId] ON [RII_WI_LINE] ([DefaultReceivingLocationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724110747_AddWarehouseInboundOutboundModules'
)
BEGIN
    CREATE INDEX [IX_RII_WI_LINE_IsDeleted] ON [RII_WI_LINE] ([IsDeleted]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724110747_AddWarehouseInboundOutboundModules'
)
BEGIN
    CREATE INDEX [IX_RII_WI_LINE_STOCK_YAP_STATUS] ON [RII_WI_LINE] ([StockId], [YapCodeId], [Status]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724110747_AddWarehouseInboundOutboundModules'
)
BEGIN
    CREATE INDEX [IX_RII_WI_LINE_TARGET_WAREHOUSE_STATUS_STOCK] ON [RII_WI_LINE] ([TargetWarehouseId], [Status], [StockId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724110747_AddWarehouseInboundOutboundModules'
)
BEGIN
    CREATE INDEX [IX_RII_WI_LINE_YapCodeId] ON [RII_WI_LINE] ([YapCodeId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724110747_AddWarehouseInboundOutboundModules'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_RII_WI_LINE_HEADER_LINE_NO] ON [RII_WI_LINE] ([GrHeaderId], [LineNo]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724110747_AddWarehouseInboundOutboundModules'
)
BEGIN
    CREATE INDEX [IX_RII_WI_LINE_SOURCE_DOCUMENT] ON [RII_WI_LINE_SOURCE] ([GrSourceDocumentId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724110747_AddWarehouseInboundOutboundModules'
)
BEGIN
    CREATE INDEX [IX_RII_WI_LINE_SOURCE_IsDeleted] ON [RII_WI_LINE_SOURCE] ([IsDeleted]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724110747_AddWarehouseInboundOutboundModules'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_RII_WI_LINE_SOURCE_EXTERNAL_LINE] ON [RII_WI_LINE_SOURCE] ([GrLineId], [GrSourceDocumentId], [ExternalLineId]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724110747_AddWarehouseInboundOutboundModules'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_RII_WI_POLICIES_BranchCode_PolicyKey] ON [RII_WI_POLICIES] ([BranchCode], [PolicyKey]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724110747_AddWarehouseInboundOutboundModules'
)
BEGIN
    CREATE INDEX [IX_RII_WI_POLICIES_IsDeleted] ON [RII_WI_POLICIES] ([IsDeleted]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724110747_AddWarehouseInboundOutboundModules'
)
BEGIN
    CREATE INDEX [IX_RII_WI_SOURCE_DOCUMENT_HEADER] ON [RII_WI_SOURCE_DOCUMENT] ([GrHeaderId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724110747_AddWarehouseInboundOutboundModules'
)
BEGIN
    CREATE INDEX [IX_RII_WI_SOURCE_DOCUMENT_IsDeleted] ON [RII_WI_SOURCE_DOCUMENT] ([IsDeleted]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724110747_AddWarehouseInboundOutboundModules'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_RII_WI_SOURCE_DOCUMENT_EXTERNAL] ON [RII_WI_SOURCE_DOCUMENT] ([GrHeaderId], [SourceSystem], [SourceDocumentType], [ExternalDocumentNo]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724110747_AddWarehouseInboundOutboundModules'
)
BEGIN
    CREATE INDEX [IX_RII_WI_STATUS_HISTORY_HEADER_CHANGED_AT] ON [RII_WI_STATUS_HISTORY] ([GrHeaderId], [ChangedAtUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724110747_AddWarehouseInboundOutboundModules'
)
BEGIN
    CREATE INDEX [IX_RII_WI_STATUS_HISTORY_IsDeleted] ON [RII_WI_STATUS_HISTORY] ([IsDeleted]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724110747_AddWarehouseInboundOutboundModules'
)
BEGIN
    CREATE UNIQUE INDEX [UX_RII_WI_STATUS_HISTORY_HEADER_CORRELATION_ID] ON [RII_WI_STATUS_HISTORY] ([GrHeaderId], [CorrelationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724110747_AddWarehouseInboundOutboundModules'
)
BEGIN
    CREATE INDEX [IX_RII_WI_TASK_HEADER_TYPE_STATUS] ON [RII_WI_TASK] ([GrHeaderId], [TaskType], [Status]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724110747_AddWarehouseInboundOutboundModules'
)
BEGIN
    CREATE INDEX [IX_RII_WI_TASK_IsDeleted] ON [RII_WI_TASK] ([IsDeleted]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724110747_AddWarehouseInboundOutboundModules'
)
BEGIN
    CREATE INDEX [IX_RII_WI_TASK_WORK_QUEUE] ON [RII_WI_TASK] ([WarehouseId], [Status], [Priority], [DueAtUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724110747_AddWarehouseInboundOutboundModules'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_RII_WI_TASK_BRANCH_TASK_NO] ON [RII_WI_TASK] ([BranchCode], [TaskNo]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724110747_AddWarehouseInboundOutboundModules'
)
BEGIN
    CREATE INDEX [IX_RII_WI_TASK_ASSIGNMENT_IsDeleted] ON [RII_WI_TASK_ASSIGNMENT] ([IsDeleted]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724110747_AddWarehouseInboundOutboundModules'
)
BEGIN
    CREATE INDEX [IX_RII_WI_TASK_ASSIGNMENT_USER_QUEUE] ON [RII_WI_TASK_ASSIGNMENT] ([UserId], [Status], [AssignedAtUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724110747_AddWarehouseInboundOutboundModules'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_RII_WI_TASK_ASSIGNMENT_ACTIVE_USER] ON [RII_WI_TASK_ASSIGNMENT] ([GrTaskId], [UserId]) WHERE [IsDeleted] = 0 AND [Status] <> N''Unassigned'' AND [Status] <> N''Rejected''');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724110747_AddWarehouseInboundOutboundModules'
)
BEGIN
    CREATE INDEX [IX_RII_WI_TASK_LINE_FromLocationId] ON [RII_WI_TASK_LINE] ([FromLocationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724110747_AddWarehouseInboundOutboundModules'
)
BEGIN
    CREATE INDEX [IX_RII_WI_TASK_LINE_GR_LINE_STATUS] ON [RII_WI_TASK_LINE] ([GrLineId], [Status]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724110747_AddWarehouseInboundOutboundModules'
)
BEGIN
    CREATE INDEX [IX_RII_WI_TASK_LINE_IsDeleted] ON [RII_WI_TASK_LINE] ([IsDeleted]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724110747_AddWarehouseInboundOutboundModules'
)
BEGIN
    CREATE INDEX [IX_RII_WI_TASK_LINE_ToLocationId] ON [RII_WI_TASK_LINE] ([ToLocationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724110747_AddWarehouseInboundOutboundModules'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_RII_WI_TASK_LINE_TASK_SEQUENCE] ON [RII_WI_TASK_LINE] ([GrTaskId], [SequenceNo]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724110747_AddWarehouseInboundOutboundModules'
)
BEGIN
    CREATE INDEX [IX_RII_WI_TASK_LINE_TRACKING_IsDeleted] ON [RII_WI_TASK_LINE_TRACKING] ([IsDeleted]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724110747_AddWarehouseInboundOutboundModules'
)
BEGIN
    CREATE INDEX [IX_RII_WI_TASK_LINE_TRACKING_TargetWarehouseId] ON [RII_WI_TASK_LINE_TRACKING] ([TargetWarehouseId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724110747_AddWarehouseInboundOutboundModules'
)
BEGIN
    CREATE INDEX [IX_RII_WI_TASK_LINE_TRACKING_ToLocationId] ON [RII_WI_TASK_LINE_TRACKING] ([ToLocationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724110747_AddWarehouseInboundOutboundModules'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_RII_WI_TASK_LINE_TRACKING_SEQUENCE] ON [RII_WI_TASK_LINE_TRACKING] ([GrTaskLineId], [SequenceNo]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724110747_AddWarehouseInboundOutboundModules'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_RII_WI_TASK_LINE_TRACKING_SERIAL] ON [RII_WI_TASK_LINE_TRACKING] ([GrTaskLineId], [SerialNo]) WHERE [IsDeleted] = 0 AND [SerialNo] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724110747_AddWarehouseInboundOutboundModules'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_RII_WI_TASK_LINE_TRACKING_STOCK_SERIAL] ON [RII_WI_TASK_LINE_TRACKING] ([StockId], [SerialNo]) WHERE [IsDeleted] = 0 AND [SerialNo] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724110747_AddWarehouseInboundOutboundModules'
)
BEGIN
    CREATE UNIQUE INDEX [IX_RII_WO_HEADER_BranchCode_DocumentNo] ON [RII_WO_HEADER] ([BranchCode], [DocumentNo]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724110747_AddWarehouseInboundOutboundModules'
)
BEGIN
    CREATE INDEX [IX_RII_WO_HEADER_BranchCode_Status_PlannedWarehouseOutboundAtUtc] ON [RII_WO_HEADER] ([BranchCode], [Status], [PlannedWarehouseOutboundAtUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724110747_AddWarehouseInboundOutboundModules'
)
BEGIN
    CREATE UNIQUE INDEX [IX_RII_WO_HEADER_CorrelationId] ON [RII_WO_HEADER] ([CorrelationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724110747_AddWarehouseInboundOutboundModules'
)
BEGIN
    CREATE INDEX [IX_RII_WO_HEADER_IsDeleted] ON [RII_WO_HEADER] ([IsDeleted]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724110747_AddWarehouseInboundOutboundModules'
)
BEGIN
    CREATE INDEX [IX_RII_WO_LINE_IsDeleted] ON [RII_WO_LINE] ([IsDeleted]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724110747_AddWarehouseInboundOutboundModules'
)
BEGIN
    CREATE UNIQUE INDEX [IX_RII_WO_LINE_WarehouseOutboundHeaderId_LineNo] ON [RII_WO_LINE] ([WarehouseOutboundHeaderId], [LineNo]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724110747_AddWarehouseInboundOutboundModules'
)
BEGIN
    CREATE INDEX [IX_RII_WO_LINE_SOURCE_IsDeleted] ON [RII_WO_LINE_SOURCE] ([IsDeleted]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724110747_AddWarehouseInboundOutboundModules'
)
BEGIN
    CREATE UNIQUE INDEX [IX_RII_WO_LINE_SOURCE_WarehouseOutboundLineId_WarehouseOutboundSourceDocumentId_ExternalLineId] ON [RII_WO_LINE_SOURCE] ([WarehouseOutboundLineId], [WarehouseOutboundSourceDocumentId], [ExternalLineId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724110747_AddWarehouseInboundOutboundModules'
)
BEGIN
    CREATE INDEX [IX_RII_WO_LINE_SOURCE_WarehouseOutboundSourceDocumentId_ExternalLineId] ON [RII_WO_LINE_SOURCE] ([WarehouseOutboundSourceDocumentId], [ExternalLineId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724110747_AddWarehouseInboundOutboundModules'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_RII_WO_POLICIES_BranchCode_PolicyKey] ON [RII_WO_POLICIES] ([BranchCode], [PolicyKey]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724110747_AddWarehouseInboundOutboundModules'
)
BEGIN
    CREATE INDEX [IX_RII_WO_POLICIES_IsDeleted] ON [RII_WO_POLICIES] ([IsDeleted]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724110747_AddWarehouseInboundOutboundModules'
)
BEGIN
    CREATE INDEX [IX_RII_WO_SOURCE_DOCUMENT_IsDeleted] ON [RII_WO_SOURCE_DOCUMENT] ([IsDeleted]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724110747_AddWarehouseInboundOutboundModules'
)
BEGIN
    CREATE UNIQUE INDEX [IX_RII_WO_SOURCE_DOCUMENT_WarehouseOutboundHeaderId_SourceDocumentType_ExternalDocumentNo] ON [RII_WO_SOURCE_DOCUMENT] ([WarehouseOutboundHeaderId], [SourceDocumentType], [ExternalDocumentNo]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724110747_AddWarehouseInboundOutboundModules'
)
BEGIN
    CREATE INDEX [IX_RII_WO_STATUS_HISTORY_IsDeleted] ON [RII_WO_STATUS_HISTORY] ([IsDeleted]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724110747_AddWarehouseInboundOutboundModules'
)
BEGIN
    CREATE INDEX [IX_RII_WO_STATUS_HISTORY_WarehouseOutboundHeaderId_ChangedAtUtc] ON [RII_WO_STATUS_HISTORY] ([WarehouseOutboundHeaderId], [ChangedAtUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724110747_AddWarehouseInboundOutboundModules'
)
BEGIN
    CREATE UNIQUE INDEX [IX_RII_WO_TASK_BranchCode_TaskNo] ON [RII_WO_TASK] ([BranchCode], [TaskNo]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724110747_AddWarehouseInboundOutboundModules'
)
BEGIN
    CREATE INDEX [IX_RII_WO_TASK_IsDeleted] ON [RII_WO_TASK] ([IsDeleted]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724110747_AddWarehouseInboundOutboundModules'
)
BEGIN
    CREATE INDEX [IX_RII_WO_TASK_WarehouseOutboundHeaderId] ON [RII_WO_TASK] ([WarehouseOutboundHeaderId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724110747_AddWarehouseInboundOutboundModules'
)
BEGIN
    CREATE INDEX [IX_RII_WO_TASK_ASSIGNMENT_IsDeleted] ON [RII_WO_TASK_ASSIGNMENT] ([IsDeleted]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724110747_AddWarehouseInboundOutboundModules'
)
BEGIN
    CREATE UNIQUE INDEX [IX_RII_WO_TASK_ASSIGNMENT_WarehouseOutboundTaskId_UserId] ON [RII_WO_TASK_ASSIGNMENT] ([WarehouseOutboundTaskId], [UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724110747_AddWarehouseInboundOutboundModules'
)
BEGIN
    CREATE INDEX [IX_RII_WO_TASK_LINE_IsDeleted] ON [RII_WO_TASK_LINE] ([IsDeleted]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724110747_AddWarehouseInboundOutboundModules'
)
BEGIN
    CREATE INDEX [IX_RII_WO_TASK_LINE_WarehouseOutboundLineId] ON [RII_WO_TASK_LINE] ([WarehouseOutboundLineId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724110747_AddWarehouseInboundOutboundModules'
)
BEGIN
    CREATE UNIQUE INDEX [IX_RII_WO_TASK_LINE_WarehouseOutboundTaskId_WarehouseOutboundLineId] ON [RII_WO_TASK_LINE] ([WarehouseOutboundTaskId], [WarehouseOutboundLineId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724110747_AddWarehouseInboundOutboundModules'
)
BEGIN
    CREATE INDEX [IX_RII_WO_TRACKING_IsDeleted] ON [RII_WO_TRACKING] ([IsDeleted]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724110747_AddWarehouseInboundOutboundModules'
)
BEGIN
    CREATE INDEX [IX_RII_WO_TRACKING_WarehouseOutboundLineId_SerialNo] ON [RII_WO_TRACKING] ([WarehouseOutboundLineId], [SerialNo]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724110747_AddWarehouseInboundOutboundModules'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260724110747_AddWarehouseInboundOutboundModules', N'10.0.10');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724132652_AddPackingModuleV2'
)
BEGIN
    CREATE TABLE [RII_PACKAGING_MATERIAL] (
        [Id] bigint NOT NULL IDENTITY,
        [Code] nvarchar(50) NOT NULL,
        [Name] nvarchar(150) NOT NULL,
        [Type] nvarchar(30) NOT NULL,
        [TareWeight] decimal(20,6) NOT NULL,
        [MaxNetWeight] decimal(20,6) NULL,
        [MaxGrossWeight] decimal(20,6) NULL,
        [InnerLength] decimal(20,6) NULL,
        [InnerWidth] decimal(20,6) NULL,
        [InnerHeight] decimal(20,6) NULL,
        [MaxVolume] decimal(20,6) NULL,
        [IsReturnable] bit NOT NULL,
        [IsActive] bit NOT NULL,
        [Description] nvarchar(500) NULL,
        [RowVersion] rowversion NOT NULL,
        [BranchCode] nvarchar(10) NOT NULL DEFAULT N'0',
        [CreatedDate] datetime2 NULL,
        [UpdatedDate] datetime2 NULL,
        [DeletedDate] datetime2 NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [CreatedBy] bigint NULL,
        [UpdatedBy] bigint NULL,
        [DeletedBy] bigint NULL,
        CONSTRAINT [PK_RII_PACKAGING_MATERIAL] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_RII_PACKAGING_MATERIAL_CAPACITY] CHECK ([TareWeight] >= 0 AND ([MaxNetWeight] IS NULL OR [MaxNetWeight] > 0) AND ([MaxGrossWeight] IS NULL OR [MaxGrossWeight] > 0))
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724132652_AddPackingModuleV2'
)
BEGIN
    CREATE TABLE [RII_PACKAGING_SPECIFICATION] (
        [Id] bigint NOT NULL IDENTITY,
        [StockId] bigint NULL,
        [StockGroupCode] nvarchar(100) NULL,
        [CustomerId] bigint NULL,
        [PackagingMaterialId] bigint NOT NULL,
        [UnitsPerHandlingUnit] decimal(20,6) NULL,
        [MaxNetWeight] decimal(20,6) NULL,
        [MaxVolume] decimal(20,6) NULL,
        [Priority] int NOT NULL,
        [IsActive] bit NOT NULL,
        [Notes] nvarchar(500) NULL,
        [BranchCode] nvarchar(10) NOT NULL DEFAULT N'0',
        [CreatedDate] datetime2 NULL,
        [UpdatedDate] datetime2 NULL,
        [DeletedDate] datetime2 NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [CreatedBy] bigint NULL,
        [UpdatedBy] bigint NULL,
        [DeletedBy] bigint NULL,
        CONSTRAINT [PK_RII_PACKAGING_SPECIFICATION] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724132652_AddPackingModuleV2'
)
BEGIN
    CREATE TABLE [RII_PACKING_EVENT] (
        [Id] bigint NOT NULL IDENTITY,
        [PackingSessionId] bigint NOT NULL,
        [HandlingUnitId] bigint NULL,
        [EventType] nvarchar(50) NOT NULL,
        [FromStatus] nvarchar(30) NULL,
        [ToStatus] nvarchar(30) NOT NULL,
        [Description] nvarchar(1000) NULL,
        [IdempotencyKey] uniqueidentifier NOT NULL,
        [OccurredAtUtc] datetimeoffset NOT NULL,
        [ActorId] bigint NOT NULL,
        [BranchCode] nvarchar(10) NOT NULL DEFAULT N'0',
        [CreatedDate] datetime2 NULL,
        [UpdatedDate] datetime2 NULL,
        [DeletedDate] datetime2 NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [CreatedBy] bigint NULL,
        [UpdatedBy] bigint NULL,
        [DeletedBy] bigint NULL,
        CONSTRAINT [PK_RII_PACKING_EVENT] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724132652_AddPackingModuleV2'
)
BEGIN
    CREATE TABLE [RII_PACKING_HEADER] (
        [Id] bigint NOT NULL IDENTITY,
        [PackingNo] nvarchar(50) NOT NULL,
        [SourceType] nvarchar(30) NOT NULL,
        [SourceHeaderId] bigint NULL,
        [SourceDocumentNo] nvarchar(100) NULL,
        [WarehouseId] bigint NOT NULL,
        [PackingStationId] bigint NOT NULL,
        [CustomerId] bigint NULL,
        [CustomerCodeSnapshot] nvarchar(100) NULL,
        [Status] nvarchar(30) NOT NULL,
        [IdempotencyKey] uniqueidentifier NOT NULL,
        [OpenedAtUtc] datetimeoffset NOT NULL,
        [ClosedAtUtc] datetimeoffset NULL,
        [ReleasedAtUtc] datetimeoffset NULL,
        [Notes] nvarchar(1000) NULL,
        [RowVersion] rowversion NOT NULL,
        [BranchCode] nvarchar(10) NOT NULL DEFAULT N'0',
        [CreatedDate] datetime2 NULL,
        [UpdatedDate] datetime2 NULL,
        [DeletedDate] datetime2 NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [CreatedBy] bigint NULL,
        [UpdatedBy] bigint NULL,
        [DeletedBy] bigint NULL,
        CONSTRAINT [PK_RII_PACKING_HEADER] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724132652_AddPackingModuleV2'
)
BEGIN
    CREATE TABLE [RII_PACKING_POLICY] (
        [Id] bigint NOT NULL IDENTITY,
        [PolicyKey] nvarchar(30) NOT NULL,
        [RequirePacking] bit NOT NULL,
        [AllowPartialPacking] bit NOT NULL,
        [AllowMixedStock] bit NOT NULL,
        [AllowMixedLot] bit NOT NULL,
        [AllowMixedCustomer] bit NOT NULL,
        [RequireSerialLotScan] bit NOT NULL,
        [RequireWeight] bit NOT NULL,
        [WeightTolerancePercent] decimal(9,4) NOT NULL,
        [RequireDimensions] bit NOT NULL,
        [RequireSscc] bit NOT NULL,
        [AutoGenerateSscc] bit NOT NULL,
        [AutoPrintLabelOnClose] bit NOT NULL,
        [AllowReopen] bit NOT NULL,
        [AllowRepack] bit NOT NULL,
        [ClosePolicy] nvarchar(30) NOT NULL,
        [ReleasePolicy] nvarchar(30) NOT NULL,
        [RowVersion] rowversion NOT NULL,
        [BranchCode] nvarchar(10) NOT NULL DEFAULT N'0',
        [CreatedDate] datetime2 NULL,
        [UpdatedDate] datetime2 NULL,
        [DeletedDate] datetime2 NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [CreatedBy] bigint NULL,
        [UpdatedBy] bigint NULL,
        [DeletedBy] bigint NULL,
        CONSTRAINT [PK_RII_PACKING_POLICY] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_RII_PACKING_POLICY_WEIGHT_TOLERANCE] CHECK ([WeightTolerancePercent] BETWEEN 0 AND 100)
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724132652_AddPackingModuleV2'
)
BEGIN
    CREATE TABLE [RII_PACKING_STATION] (
        [Id] bigint NOT NULL IDENTITY,
        [WarehouseId] bigint NOT NULL,
        [LocationId] bigint NULL,
        [Code] nvarchar(50) NOT NULL,
        [Name] nvarchar(150) NOT NULL,
        [ScaleDeviceCode] nvarchar(100) NULL,
        [PrinterDefinitionId] bigint NULL,
        [IsActive] bit NOT NULL,
        [Description] nvarchar(500) NULL,
        [RowVersion] rowversion NOT NULL,
        [BranchCode] nvarchar(10) NOT NULL DEFAULT N'0',
        [CreatedDate] datetime2 NULL,
        [UpdatedDate] datetime2 NULL,
        [DeletedDate] datetime2 NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [CreatedBy] bigint NULL,
        [UpdatedBy] bigint NULL,
        [DeletedBy] bigint NULL,
        CONSTRAINT [PK_RII_PACKING_STATION] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724132652_AddPackingModuleV2'
)
BEGIN
    CREATE TABLE [RII_HANDLING_UNIT] (
        [Id] bigint NOT NULL IDENTITY,
        [PackingSessionId] bigint NOT NULL,
        [ParentHandlingUnitId] bigint NULL,
        [PackagingMaterialId] bigint NOT NULL,
        [HandlingUnitNo] nvarchar(100) NOT NULL,
        [Sscc] nvarchar(18) NULL,
        [Status] nvarchar(30) NOT NULL,
        [TareWeight] decimal(20,6) NOT NULL,
        [NetWeight] decimal(20,6) NOT NULL,
        [MeasuredGrossWeight] decimal(20,6) NULL,
        [GrossWeight] decimal(20,6) NOT NULL,
        [Length] decimal(20,6) NULL,
        [Width] decimal(20,6) NULL,
        [Height] decimal(20,6) NULL,
        [Volume] decimal(20,6) NULL,
        [ClosedAtUtc] datetimeoffset NULL,
        [ClosedBy] bigint NULL,
        [RowVersion] rowversion NOT NULL,
        [BranchCode] nvarchar(10) NOT NULL DEFAULT N'0',
        [CreatedDate] datetime2 NULL,
        [UpdatedDate] datetime2 NULL,
        [DeletedDate] datetime2 NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [CreatedBy] bigint NULL,
        [UpdatedBy] bigint NULL,
        [DeletedBy] bigint NULL,
        CONSTRAINT [PK_RII_HANDLING_UNIT] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_RII_HANDLING_UNIT_WEIGHT] CHECK ([TareWeight] >= 0 AND [NetWeight] >= 0 AND [GrossWeight] >= 0),
        CONSTRAINT [FK_RII_HANDLING_UNIT_RII_HANDLING_UNIT_ParentHandlingUnitId] FOREIGN KEY ([ParentHandlingUnitId]) REFERENCES [RII_HANDLING_UNIT] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_RII_HANDLING_UNIT_RII_PACKING_HEADER_PackingSessionId] FOREIGN KEY ([PackingSessionId]) REFERENCES [RII_PACKING_HEADER] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724132652_AddPackingModuleV2'
)
BEGIN
    CREATE TABLE [RII_HANDLING_UNIT_LINE] (
        [Id] bigint NOT NULL IDENTITY,
        [HandlingUnitId] bigint NOT NULL,
        [SourceLineId] bigint NOT NULL,
        [StockId] bigint NOT NULL,
        [StockCodeSnapshot] nvarchar(100) NOT NULL,
        [YapCodeId] bigint NULL,
        [YapCodeSnapshot] nvarchar(100) NULL,
        [UnitCode] nvarchar(20) NOT NULL,
        [Quantity] decimal(20,6) NOT NULL,
        [LotNo] nvarchar(100) NULL,
        [SerialNo] nvarchar(200) NULL,
        [PackedAtUtc] datetimeoffset NOT NULL,
        [PackedBy] bigint NOT NULL,
        [BranchCode] nvarchar(10) NOT NULL DEFAULT N'0',
        [CreatedDate] datetime2 NULL,
        [UpdatedDate] datetime2 NULL,
        [DeletedDate] datetime2 NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [CreatedBy] bigint NULL,
        [UpdatedBy] bigint NULL,
        [DeletedBy] bigint NULL,
        CONSTRAINT [PK_RII_HANDLING_UNIT_LINE] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_RII_HANDLING_UNIT_LINE_QUANTITY] CHECK ([Quantity] > 0),
        CONSTRAINT [FK_RII_HANDLING_UNIT_LINE_RII_HANDLING_UNIT_HandlingUnitId] FOREIGN KEY ([HandlingUnitId]) REFERENCES [RII_HANDLING_UNIT] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724132652_AddPackingModuleV2'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'AvailableOnMobile', N'AvailableOnWeb', N'BranchCode', N'Code', N'CreatedBy', N'CreatedDate', N'DeletedBy', N'DeletedDate', N'Description', N'IsActive', N'Name', N'UpdatedBy', N'UpdatedDate') AND [object_id] = OBJECT_ID(N'[RII_PERMISSION_DEFINITIONS]'))
        SET IDENTITY_INSERT [RII_PERMISSION_DEFINITIONS] ON;
    EXEC(N'INSERT INTO [RII_PERMISSION_DEFINITIONS] ([Id], [AvailableOnMobile], [AvailableOnWeb], [BranchCode], [Code], [CreatedBy], [CreatedDate], [DeletedBy], [DeletedDate], [Description], [IsActive], [Name], [UpdatedBy], [UpdatedDate])
    VALUES (CAST(2200 AS bigint), CAST(0 AS bit), CAST(1 AS bit), N''0'', N''WMS.PACKING.VIEW'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, NULL, CAST(1 AS bit), N''Paketlemeyi görüntüle'', NULL, NULL),
    (CAST(2201 AS bigint), CAST(0 AS bit), CAST(1 AS bit), N''0'', N''WMS.PACKING.OPERATE'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, NULL, CAST(1 AS bit), N''Paketleme operasyonu yürüt'', NULL, NULL),
    (CAST(2202 AS bigint), CAST(0 AS bit), CAST(1 AS bit), N''0'', N''WMS.PACKING.CLOSE'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, NULL, CAST(1 AS bit), N''Paketi kapat ve serbest bırak'', NULL, NULL),
    (CAST(2203 AS bigint), CAST(0 AS bit), CAST(1 AS bit), N''0'', N''WMS.PACKING.REOPEN'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, NULL, CAST(1 AS bit), N''Kapalı paketi yeniden aç'', NULL, NULL),
    (CAST(2204 AS bigint), CAST(0 AS bit), CAST(1 AS bit), N''0'', N''WMS.PACKING.DEFINITIONS.VIEW'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, NULL, CAST(1 AS bit), N''Paketleme tanımlarını görüntüle'', NULL, NULL),
    (CAST(2205 AS bigint), CAST(0 AS bit), CAST(1 AS bit), N''0'', N''WMS.PACKING.DEFINITIONS.MANAGE'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, NULL, CAST(1 AS bit), N''Paketleme tanımlarını yönet'', NULL, NULL),
    (CAST(2206 AS bigint), CAST(0 AS bit), CAST(1 AS bit), N''0'', N''WMS.PACKING.SETTINGS.VIEW'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, NULL, CAST(1 AS bit), N''Paketleme ayarlarını görüntüle'', NULL, NULL),
    (CAST(2207 AS bigint), CAST(0 AS bit), CAST(1 AS bit), N''0'', N''WMS.PACKING.SETTINGS.MANAGE'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, NULL, CAST(1 AS bit), N''Paketleme ayarlarını yönet'', NULL, NULL)');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'AvailableOnMobile', N'AvailableOnWeb', N'BranchCode', N'Code', N'CreatedBy', N'CreatedDate', N'DeletedBy', N'DeletedDate', N'Description', N'IsActive', N'Name', N'UpdatedBy', N'UpdatedDate') AND [object_id] = OBJECT_ID(N'[RII_PERMISSION_DEFINITIONS]'))
        SET IDENTITY_INSERT [RII_PERMISSION_DEFINITIONS] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724132652_AddPackingModuleV2'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'BranchCode', N'CreatedBy', N'CreatedDate', N'DeletedBy', N'DeletedDate', N'PermissionDefinitionId', N'PermissionGroupId', N'UpdatedBy', N'UpdatedDate') AND [object_id] = OBJECT_ID(N'[RII_PERMISSION_GROUP_PERMISSIONS]'))
        SET IDENTITY_INSERT [RII_PERMISSION_GROUP_PERMISSIONS] ON;
    EXEC(N'INSERT INTO [RII_PERMISSION_GROUP_PERMISSIONS] ([Id], [BranchCode], [CreatedBy], [CreatedDate], [DeletedBy], [DeletedDate], [PermissionDefinitionId], [PermissionGroupId], [UpdatedBy], [UpdatedDate])
    VALUES (CAST(2200 AS bigint), N''0'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, CAST(2200 AS bigint), CAST(1001 AS bigint), NULL, NULL),
    (CAST(2201 AS bigint), N''0'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, CAST(2201 AS bigint), CAST(1001 AS bigint), NULL, NULL),
    (CAST(2202 AS bigint), N''0'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, CAST(2202 AS bigint), CAST(1001 AS bigint), NULL, NULL),
    (CAST(2203 AS bigint), N''0'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, CAST(2203 AS bigint), CAST(1001 AS bigint), NULL, NULL),
    (CAST(2204 AS bigint), N''0'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, CAST(2204 AS bigint), CAST(1001 AS bigint), NULL, NULL),
    (CAST(2205 AS bigint), N''0'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, CAST(2205 AS bigint), CAST(1001 AS bigint), NULL, NULL),
    (CAST(2206 AS bigint), N''0'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, CAST(2206 AS bigint), CAST(1001 AS bigint), NULL, NULL),
    (CAST(2207 AS bigint), N''0'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, CAST(2207 AS bigint), CAST(1001 AS bigint), NULL, NULL)');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'BranchCode', N'CreatedBy', N'CreatedDate', N'DeletedBy', N'DeletedDate', N'PermissionDefinitionId', N'PermissionGroupId', N'UpdatedBy', N'UpdatedDate') AND [object_id] = OBJECT_ID(N'[RII_PERMISSION_GROUP_PERMISSIONS]'))
        SET IDENTITY_INSERT [RII_PERMISSION_GROUP_PERMISSIONS] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724132652_AddPackingModuleV2'
)
BEGIN
    CREATE UNIQUE INDEX [IX_RII_HANDLING_UNIT_BranchCode_HandlingUnitNo] ON [RII_HANDLING_UNIT] ([BranchCode], [HandlingUnitNo]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724132652_AddPackingModuleV2'
)
BEGIN
    CREATE INDEX [IX_RII_HANDLING_UNIT_IsDeleted] ON [RII_HANDLING_UNIT] ([IsDeleted]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724132652_AddPackingModuleV2'
)
BEGIN
    CREATE INDEX [IX_RII_HANDLING_UNIT_PackingSessionId] ON [RII_HANDLING_UNIT] ([PackingSessionId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724132652_AddPackingModuleV2'
)
BEGIN
    CREATE INDEX [IX_RII_HANDLING_UNIT_ParentHandlingUnitId] ON [RII_HANDLING_UNIT] ([ParentHandlingUnitId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724132652_AddPackingModuleV2'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_RII_HANDLING_UNIT_Sscc] ON [RII_HANDLING_UNIT] ([Sscc]) WHERE [Sscc] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724132652_AddPackingModuleV2'
)
BEGIN
    CREATE INDEX [IX_RII_HANDLING_UNIT_LINE_HandlingUnitId] ON [RII_HANDLING_UNIT_LINE] ([HandlingUnitId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724132652_AddPackingModuleV2'
)
BEGIN
    CREATE INDEX [IX_RII_HANDLING_UNIT_LINE_IsDeleted] ON [RII_HANDLING_UNIT_LINE] ([IsDeleted]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724132652_AddPackingModuleV2'
)
BEGIN
    CREATE INDEX [IX_RII_HANDLING_UNIT_LINE_SourceLineId_LotNo_SerialNo] ON [RII_HANDLING_UNIT_LINE] ([SourceLineId], [LotNo], [SerialNo]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724132652_AddPackingModuleV2'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_RII_PACKAGING_MATERIAL_BranchCode_Code] ON [RII_PACKAGING_MATERIAL] ([BranchCode], [Code]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724132652_AddPackingModuleV2'
)
BEGIN
    CREATE INDEX [IX_RII_PACKAGING_MATERIAL_IsDeleted] ON [RII_PACKAGING_MATERIAL] ([IsDeleted]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724132652_AddPackingModuleV2'
)
BEGIN
    CREATE INDEX [IX_RII_PACKAGING_SPECIFICATION_BranchCode_StockId_StockGroupCode_CustomerId_Priority] ON [RII_PACKAGING_SPECIFICATION] ([BranchCode], [StockId], [StockGroupCode], [CustomerId], [Priority]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724132652_AddPackingModuleV2'
)
BEGIN
    CREATE INDEX [IX_RII_PACKAGING_SPECIFICATION_IsDeleted] ON [RII_PACKAGING_SPECIFICATION] ([IsDeleted]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724132652_AddPackingModuleV2'
)
BEGIN
    CREATE INDEX [IX_RII_PACKING_EVENT_IsDeleted] ON [RII_PACKING_EVENT] ([IsDeleted]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724132652_AddPackingModuleV2'
)
BEGIN
    CREATE UNIQUE INDEX [IX_RII_PACKING_EVENT_PackingSessionId_IdempotencyKey] ON [RII_PACKING_EVENT] ([PackingSessionId], [IdempotencyKey]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724132652_AddPackingModuleV2'
)
BEGIN
    CREATE INDEX [IX_RII_PACKING_EVENT_PackingSessionId_OccurredAtUtc] ON [RII_PACKING_EVENT] ([PackingSessionId], [OccurredAtUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724132652_AddPackingModuleV2'
)
BEGIN
    CREATE UNIQUE INDEX [IX_RII_PACKING_HEADER_BranchCode_PackingNo] ON [RII_PACKING_HEADER] ([BranchCode], [PackingNo]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724132652_AddPackingModuleV2'
)
BEGIN
    CREATE UNIQUE INDEX [IX_RII_PACKING_HEADER_IdempotencyKey] ON [RII_PACKING_HEADER] ([IdempotencyKey]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724132652_AddPackingModuleV2'
)
BEGIN
    CREATE INDEX [IX_RII_PACKING_HEADER_IsDeleted] ON [RII_PACKING_HEADER] ([IsDeleted]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724132652_AddPackingModuleV2'
)
BEGIN
    CREATE INDEX [IX_RII_PACKING_HEADER_SourceType_SourceHeaderId] ON [RII_PACKING_HEADER] ([SourceType], [SourceHeaderId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724132652_AddPackingModuleV2'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_RII_PACKING_POLICY_BranchCode_PolicyKey] ON [RII_PACKING_POLICY] ([BranchCode], [PolicyKey]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724132652_AddPackingModuleV2'
)
BEGIN
    CREATE INDEX [IX_RII_PACKING_POLICY_IsDeleted] ON [RII_PACKING_POLICY] ([IsDeleted]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724132652_AddPackingModuleV2'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_RII_PACKING_STATION_BranchCode_WarehouseId_Code] ON [RII_PACKING_STATION] ([BranchCode], [WarehouseId], [Code]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724132652_AddPackingModuleV2'
)
BEGIN
    CREATE INDEX [IX_RII_PACKING_STATION_IsDeleted] ON [RII_PACKING_STATION] ([IsDeleted]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724132652_AddPackingModuleV2'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260724132652_AddPackingModuleV2', N'10.0.10');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724181420_CompletePackingOperationsV2'
)
BEGIN
    ALTER TABLE [RII_WT_TRACKING] DROP CONSTRAINT [CK_RII_WT_TRACKING_QTY];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724181420_CompletePackingOperationsV2'
)
BEGIN
    ALTER TABLE [RII_WT_LINE] DROP CONSTRAINT [CK_RII_WT_LINE_QTY];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724181420_CompletePackingOperationsV2'
)
BEGIN
    ALTER TABLE [RII_WT_TRACKING] ADD [PackedQuantity] decimal(20,6) NOT NULL DEFAULT 0.0;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724181420_CompletePackingOperationsV2'
)
BEGIN
    ALTER TABLE [RII_WT_LINE] ADD [PackedQuantity] decimal(20,6) NOT NULL DEFAULT 0.0;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724181420_CompletePackingOperationsV2'
)
BEGIN
    CREATE TABLE [RII_PACKING_PRINT_JOB] (
        [Id] bigint NOT NULL IDENTITY,
        [HandlingUnitId] bigint NOT NULL,
        [PackingStationId] bigint NOT NULL,
        [PrinterDefinitionId] bigint NULL,
        [Status] nvarchar(30) NOT NULL,
        [Copies] int NOT NULL,
        [PayloadJson] nvarchar(max) NOT NULL,
        [IdempotencyKey] uniqueidentifier NOT NULL,
        [AttemptCount] int NOT NULL,
        [RequestedAtUtc] datetimeoffset NOT NULL,
        [ProcessingStartedAtUtc] datetimeoffset NULL,
        [CompletedAtUtc] datetimeoffset NULL,
        [NextAttemptAtUtc] datetimeoffset NULL,
        [LastError] nvarchar(2000) NULL,
        [BranchCode] nvarchar(10) NOT NULL DEFAULT N'0',
        [CreatedDate] datetime2 NULL,
        [UpdatedDate] datetime2 NULL,
        [DeletedDate] datetime2 NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [CreatedBy] bigint NULL,
        [UpdatedBy] bigint NULL,
        [DeletedBy] bigint NULL,
        CONSTRAINT [PK_RII_PACKING_PRINT_JOB] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_RII_PACKING_PRINT_JOB_COPIES] CHECK ([Copies] > 0 AND [AttemptCount] >= 0)
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724181420_CompletePackingOperationsV2'
)
BEGIN
    CREATE TABLE [RII_PACKING_SCALE_READING] (
        [Id] bigint NOT NULL IDENTITY,
        [PackingStationId] bigint NOT NULL,
        [HandlingUnitId] bigint NULL,
        [DeviceCode] nvarchar(100) NOT NULL,
        [GrossWeight] decimal(20,6) NOT NULL,
        [IsStable] bit NOT NULL,
        [IdempotencyKey] uniqueidentifier NOT NULL,
        [CapturedAtUtc] datetimeoffset NOT NULL,
        [RawPayload] nvarchar(2000) NULL,
        [BranchCode] nvarchar(10) NOT NULL DEFAULT N'0',
        [CreatedDate] datetime2 NULL,
        [UpdatedDate] datetime2 NULL,
        [DeletedDate] datetime2 NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [CreatedBy] bigint NULL,
        [UpdatedBy] bigint NULL,
        [DeletedBy] bigint NULL,
        CONSTRAINT [PK_RII_PACKING_SCALE_READING] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_RII_PACKING_SCALE_READING_WEIGHT] CHECK ([GrossWeight] > 0)
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724181420_CompletePackingOperationsV2'
)
BEGIN
    EXEC(N'ALTER TABLE [RII_WT_TRACKING] ADD CONSTRAINT [CK_RII_WT_TRACKING_QTY] CHECK ([PlannedQuantity] > 0 AND [ReservedQuantity] >= 0 AND [PickedQuantity] >= 0 AND [PackedQuantity] >= 0 AND [ShippedQuantity] >= 0 AND [ReceivedQuantity] >= 0 AND [PutawayQuantity] >= 0)');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724181420_CompletePackingOperationsV2'
)
BEGIN
    EXEC(N'ALTER TABLE [RII_WT_LINE] ADD CONSTRAINT [CK_RII_WT_LINE_QTY] CHECK ([RequestedQuantity] > 0 AND [ReservedQuantity] >= 0 AND [PickedQuantity] >= 0 AND [PackedQuantity] >= 0 AND [ShippedQuantity] >= 0 AND [ReceivedQuantity] >= 0 AND [PutawayQuantity] >= 0 AND [DamagedQuantity] >= 0 AND [LostQuantity] >= 0 AND [ShortClosedQuantity] >= 0)');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724181420_CompletePackingOperationsV2'
)
BEGIN
    CREATE INDEX [IX_RII_PACKING_PRINT_JOB_HandlingUnitId] ON [RII_PACKING_PRINT_JOB] ([HandlingUnitId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724181420_CompletePackingOperationsV2'
)
BEGIN
    CREATE UNIQUE INDEX [IX_RII_PACKING_PRINT_JOB_IdempotencyKey] ON [RII_PACKING_PRINT_JOB] ([IdempotencyKey]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724181420_CompletePackingOperationsV2'
)
BEGIN
    CREATE INDEX [IX_RII_PACKING_PRINT_JOB_IsDeleted] ON [RII_PACKING_PRINT_JOB] ([IsDeleted]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724181420_CompletePackingOperationsV2'
)
BEGIN
    CREATE INDEX [IX_RII_PACKING_PRINT_JOB_Status_NextAttemptAtUtc_RequestedAtUtc] ON [RII_PACKING_PRINT_JOB] ([Status], [NextAttemptAtUtc], [RequestedAtUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724181420_CompletePackingOperationsV2'
)
BEGIN
    CREATE INDEX [IX_RII_PACKING_SCALE_READING_HandlingUnitId_CapturedAtUtc] ON [RII_PACKING_SCALE_READING] ([HandlingUnitId], [CapturedAtUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724181420_CompletePackingOperationsV2'
)
BEGIN
    CREATE UNIQUE INDEX [IX_RII_PACKING_SCALE_READING_IdempotencyKey] ON [RII_PACKING_SCALE_READING] ([IdempotencyKey]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724181420_CompletePackingOperationsV2'
)
BEGIN
    CREATE INDEX [IX_RII_PACKING_SCALE_READING_IsDeleted] ON [RII_PACKING_SCALE_READING] ([IsDeleted]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724181420_CompletePackingOperationsV2'
)
BEGIN
    CREATE INDEX [IX_RII_PACKING_SCALE_READING_PackingStationId_CapturedAtUtc] ON [RII_PACKING_SCALE_READING] ([PackingStationId], [CapturedAtUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724181420_CompletePackingOperationsV2'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260724181420_CompletePackingOperationsV2', N'10.0.10');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724181502_AddPackingDeviceForeignKeys'
)
BEGIN
    CREATE INDEX [IX_RII_PACKING_PRINT_JOB_PackingStationId] ON [RII_PACKING_PRINT_JOB] ([PackingStationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724181502_AddPackingDeviceForeignKeys'
)
BEGIN
    ALTER TABLE [RII_PACKING_PRINT_JOB] ADD CONSTRAINT [FK_RII_PACKING_PRINT_JOB_RII_HANDLING_UNIT_HandlingUnitId] FOREIGN KEY ([HandlingUnitId]) REFERENCES [RII_HANDLING_UNIT] ([Id]) ON DELETE NO ACTION;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724181502_AddPackingDeviceForeignKeys'
)
BEGIN
    ALTER TABLE [RII_PACKING_PRINT_JOB] ADD CONSTRAINT [FK_RII_PACKING_PRINT_JOB_RII_PACKING_STATION_PackingStationId] FOREIGN KEY ([PackingStationId]) REFERENCES [RII_PACKING_STATION] ([Id]) ON DELETE NO ACTION;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724181502_AddPackingDeviceForeignKeys'
)
BEGIN
    ALTER TABLE [RII_PACKING_SCALE_READING] ADD CONSTRAINT [FK_RII_PACKING_SCALE_READING_RII_HANDLING_UNIT_HandlingUnitId] FOREIGN KEY ([HandlingUnitId]) REFERENCES [RII_HANDLING_UNIT] ([Id]) ON DELETE NO ACTION;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724181502_AddPackingDeviceForeignKeys'
)
BEGIN
    ALTER TABLE [RII_PACKING_SCALE_READING] ADD CONSTRAINT [FK_RII_PACKING_SCALE_READING_RII_PACKING_STATION_PackingStationId] FOREIGN KEY ([PackingStationId]) REFERENCES [RII_PACKING_STATION] ([Id]) ON DELETE NO ACTION;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724181502_AddPackingDeviceForeignKeys'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260724181502_AddPackingDeviceForeignKeys', N'10.0.10');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724182117_AlignWarehouseTransferDiscrepancyPolicy'
)
BEGIN
    ALTER TABLE dbo.RII_WT_HEADER ALTER COLUMN DiscrepancyPolicy nvarchar(30) NOT NULL;
    UPDATE dbo.RII_WT_HEADER
    SET DiscrepancyPolicy = CASE DiscrepancyPolicy
        WHEN '1' THEN 'Block'
        WHEN '2' THEN 'AllowWithReason'
        WHEN '3' THEN 'RequireApproval'
        ELSE DiscrepancyPolicy
    END;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724182117_AlignWarehouseTransferDiscrepancyPolicy'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260724182117_AlignWarehouseTransferDiscrepancyPolicy', N'10.0.10');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724184900_AddGoodsReceiptRoutingLedger'
)
BEGIN
    CREATE TABLE [RII_GR_ROUTING_BATCH] (
        [Id] bigint NOT NULL IDENTITY,
        [GrHeaderId] bigint NOT NULL,
        [RouteType] nvarchar(30) NOT NULL,
        [CorrelationId] uniqueidentifier NOT NULL,
        [TargetDocumentId] bigint NOT NULL,
        [TargetDocumentNo] nvarchar(50) NOT NULL,
        [RoutedAtUtc] datetimeoffset(7) NOT NULL,
        [RoutedBy] bigint NOT NULL,
        [Description] nvarchar(500) NULL,
        [BranchCode] nvarchar(10) NOT NULL DEFAULT N'0',
        [CreatedDate] datetime2 NULL,
        [UpdatedDate] datetime2 NULL,
        [DeletedDate] datetime2 NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [CreatedBy] bigint NULL,
        [UpdatedBy] bigint NULL,
        [DeletedBy] bigint NULL,
        CONSTRAINT [PK_RII_GR_ROUTING_BATCH] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_RII_GR_ROUTING_BATCH_RII_GR_HEADER_GrHeaderId] FOREIGN KEY ([GrHeaderId]) REFERENCES [RII_GR_HEADER] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724184900_AddGoodsReceiptRoutingLedger'
)
BEGIN
    CREATE TABLE [RII_GR_ROUTING_ALLOCATION] (
        [Id] bigint NOT NULL IDENTITY,
        [RoutingBatchId] bigint NOT NULL,
        [GrLineId] bigint NOT NULL,
        [TargetDocumentLineId] bigint NOT NULL,
        [Quantity] decimal(18,6) NOT NULL,
        [BranchCode] nvarchar(10) NOT NULL DEFAULT N'0',
        [CreatedDate] datetime2 NULL,
        [UpdatedDate] datetime2 NULL,
        [DeletedDate] datetime2 NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [CreatedBy] bigint NULL,
        [UpdatedBy] bigint NULL,
        [DeletedBy] bigint NULL,
        CONSTRAINT [PK_RII_GR_ROUTING_ALLOCATION] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_RII_GR_ROUTING_ALLOCATION_QUANTITY] CHECK ([Quantity] > 0),
        CONSTRAINT [FK_RII_GR_ROUTING_ALLOCATION_RII_GR_LINE_GrLineId] FOREIGN KEY ([GrLineId]) REFERENCES [RII_GR_LINE] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_RII_GR_ROUTING_ALLOCATION_RII_GR_ROUTING_BATCH_RoutingBatchId] FOREIGN KEY ([RoutingBatchId]) REFERENCES [RII_GR_ROUTING_BATCH] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724184900_AddGoodsReceiptRoutingLedger'
)
BEGIN
    EXEC(N'UPDATE [RII_PERMISSION_DEFINITIONS] SET [Name] = N''Ambar girişlerini görüntüle''
    WHERE [Id] = CAST(2100 AS bigint);
    SELECT @@ROWCOUNT');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724184900_AddGoodsReceiptRoutingLedger'
)
BEGIN
    EXEC(N'UPDATE [RII_PERMISSION_DEFINITIONS] SET [Name] = N''Ambar girişi oluştur''
    WHERE [Id] = CAST(2101 AS bigint);
    SELECT @@ROWCOUNT');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724184900_AddGoodsReceiptRoutingLedger'
)
BEGIN
    EXEC(N'UPDATE [RII_PERMISSION_DEFINITIONS] SET [Name] = N''Ambar girişini güncelle''
    WHERE [Id] = CAST(2102 AS bigint);
    SELECT @@ROWCOUNT');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724184900_AddGoodsReceiptRoutingLedger'
)
BEGIN
    EXEC(N'UPDATE [RII_PERMISSION_DEFINITIONS] SET [Name] = N''Ambar girişini işleme aç''
    WHERE [Id] = CAST(2103 AS bigint);
    SELECT @@ROWCOUNT');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724184900_AddGoodsReceiptRoutingLedger'
)
BEGIN
    EXEC(N'UPDATE [RII_PERMISSION_DEFINITIONS] SET [Name] = N''Ambar girişini işle''
    WHERE [Id] = CAST(2104 AS bigint);
    SELECT @@ROWCOUNT');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724184900_AddGoodsReceiptRoutingLedger'
)
BEGIN
    EXEC(N'UPDATE [RII_PERMISSION_DEFINITIONS] SET [Name] = N''Ambar girişini tamamla''
    WHERE [Id] = CAST(2105 AS bigint);
    SELECT @@ROWCOUNT');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724184900_AddGoodsReceiptRoutingLedger'
)
BEGIN
    EXEC(N'UPDATE [RII_PERMISSION_DEFINITIONS] SET [Name] = N''Ambar girişini iptal et''
    WHERE [Id] = CAST(2106 AS bigint);
    SELECT @@ROWCOUNT');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724184900_AddGoodsReceiptRoutingLedger'
)
BEGIN
    EXEC(N'UPDATE [RII_PERMISSION_DEFINITIONS] SET [Name] = N''Ambar giriş ayarlarını görüntüle''
    WHERE [Id] = CAST(2107 AS bigint);
    SELECT @@ROWCOUNT');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724184900_AddGoodsReceiptRoutingLedger'
)
BEGIN
    EXEC(N'UPDATE [RII_PERMISSION_DEFINITIONS] SET [Name] = N''Ambar giriş ayarlarını yönet''
    WHERE [Id] = CAST(2108 AS bigint);
    SELECT @@ROWCOUNT');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724184900_AddGoodsReceiptRoutingLedger'
)
BEGIN
    EXEC(N'UPDATE [RII_PERMISSION_DEFINITIONS] SET [Name] = N''Ambar çıkışlarını görüntüle''
    WHERE [Id] = CAST(2110 AS bigint);
    SELECT @@ROWCOUNT');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724184900_AddGoodsReceiptRoutingLedger'
)
BEGIN
    EXEC(N'UPDATE [RII_PERMISSION_DEFINITIONS] SET [Name] = N''Ambar çıkışı oluştur''
    WHERE [Id] = CAST(2111 AS bigint);
    SELECT @@ROWCOUNT');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724184900_AddGoodsReceiptRoutingLedger'
)
BEGIN
    EXEC(N'UPDATE [RII_PERMISSION_DEFINITIONS] SET [Name] = N''Ambar çıkışını güncelle''
    WHERE [Id] = CAST(2112 AS bigint);
    SELECT @@ROWCOUNT');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724184900_AddGoodsReceiptRoutingLedger'
)
BEGIN
    EXEC(N'UPDATE [RII_PERMISSION_DEFINITIONS] SET [Name] = N''Ambar çıkış taslağını sil''
    WHERE [Id] = CAST(2113 AS bigint);
    SELECT @@ROWCOUNT');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724184900_AddGoodsReceiptRoutingLedger'
)
BEGIN
    EXEC(N'UPDATE [RII_PERMISSION_DEFINITIONS] SET [Name] = N''Ambar çıkış operasyonunu yürüt''
    WHERE [Id] = CAST(2114 AS bigint);
    SELECT @@ROWCOUNT');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724184900_AddGoodsReceiptRoutingLedger'
)
BEGIN
    EXEC(N'UPDATE [RII_PERMISSION_DEFINITIONS] SET [Name] = N''Ambar çıkışını onayla''
    WHERE [Id] = CAST(2115 AS bigint);
    SELECT @@ROWCOUNT');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724184900_AddGoodsReceiptRoutingLedger'
)
BEGIN
    EXEC(N'UPDATE [RII_PERMISSION_DEFINITIONS] SET [Name] = N''Ambar çıkışını iptal et''
    WHERE [Id] = CAST(2116 AS bigint);
    SELECT @@ROWCOUNT');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724184900_AddGoodsReceiptRoutingLedger'
)
BEGIN
    EXEC(N'UPDATE [RII_PERMISSION_DEFINITIONS] SET [Name] = N''Ambar çıkış ayarlarını görüntüle''
    WHERE [Id] = CAST(2117 AS bigint);
    SELECT @@ROWCOUNT');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724184900_AddGoodsReceiptRoutingLedger'
)
BEGIN
    EXEC(N'UPDATE [RII_PERMISSION_DEFINITIONS] SET [Name] = N''Ambar çıkış ayarlarını yönet''
    WHERE [Id] = CAST(2118 AS bigint);
    SELECT @@ROWCOUNT');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724184900_AddGoodsReceiptRoutingLedger'
)
BEGIN
    CREATE INDEX [IX_RII_GR_ROUTING_ALLOCATION_IsDeleted] ON [RII_GR_ROUTING_ALLOCATION] ([IsDeleted]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724184900_AddGoodsReceiptRoutingLedger'
)
BEGIN
    CREATE INDEX [IX_RII_GR_ROUTING_ALLOCATION_LINE_BATCH] ON [RII_GR_ROUTING_ALLOCATION] ([GrLineId], [RoutingBatchId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724184900_AddGoodsReceiptRoutingLedger'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_RII_GR_ROUTING_ALLOCATION_BATCH_LINE] ON [RII_GR_ROUTING_ALLOCATION] ([RoutingBatchId], [GrLineId]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724184900_AddGoodsReceiptRoutingLedger'
)
BEGIN
    CREATE INDEX [IX_RII_GR_ROUTING_BATCH_HEADER_TYPE_DATE] ON [RII_GR_ROUTING_BATCH] ([GrHeaderId], [RouteType], [RoutedAtUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724184900_AddGoodsReceiptRoutingLedger'
)
BEGIN
    CREATE INDEX [IX_RII_GR_ROUTING_BATCH_IsDeleted] ON [RII_GR_ROUTING_BATCH] ([IsDeleted]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724184900_AddGoodsReceiptRoutingLedger'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_RII_GR_ROUTING_BATCH_CORRELATION] ON [RII_GR_ROUTING_BATCH] ([CorrelationId]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724184900_AddGoodsReceiptRoutingLedger'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260724184900_AddGoodsReceiptRoutingLedger', N'10.0.10');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724194417_AddStockTrackingPolicies'
)
BEGIN
    CREATE TABLE [RII_STOCK_TRACKING_POLICIES] (
        [Id] bigint NOT NULL IDENTITY,
        [PolicyCode] nvarchar(50) NOT NULL,
        [DisplayName] nvarchar(150) NOT NULL,
        [Scope] nvarchar(30) NOT NULL,
        [StockId] bigint NULL,
        [StockGroupCode] nvarchar(50) NULL,
        [Version] int NOT NULL,
        [Priority] int NOT NULL,
        [TrackingType] nvarchar(30) NOT NULL,
        [RequireSerial] bit NOT NULL,
        [SerialQuantityRule] nvarchar(30) NOT NULL,
        [RequireLot] bit NOT NULL,
        [RequireManufacturingDate] bit NOT NULL,
        [RequireExpirationDate] bit NOT NULL,
        [MinimumRemainingShelfLifeDays] int NULL,
        [IsActive] bit NOT NULL,
        [EffectiveFromUtc] datetimeoffset NOT NULL,
        [EffectiveToUtc] datetimeoffset NULL,
        [Description] nvarchar(500) NULL,
        [RowVersion] rowversion NOT NULL,
        [BranchCode] nvarchar(10) NOT NULL DEFAULT N'0',
        [CreatedDate] datetime2 NULL,
        [UpdatedDate] datetime2 NULL,
        [DeletedDate] datetime2 NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [CreatedBy] bigint NULL,
        [UpdatedBy] bigint NULL,
        [DeletedBy] bigint NULL,
        CONSTRAINT [PK_RII_STOCK_TRACKING_POLICIES] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724194417_AddStockTrackingPolicies'
)
BEGIN
    CREATE INDEX [IX_RII_STOCK_TRACKING_POLICIES_IsDeleted] ON [RII_STOCK_TRACKING_POLICIES] ([IsDeleted]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724194417_AddStockTrackingPolicies'
)
BEGIN
    CREATE INDEX [IX_RII_STOCK_TRACKING_POLICY_RESOLVE] ON [RII_STOCK_TRACKING_POLICIES] ([BranchCode], [Scope], [StockId], [StockGroupCode], [IsActive], [EffectiveFromUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724194417_AddStockTrackingPolicies'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_RII_STOCK_TRACKING_POLICY_VERSION] ON [RII_STOCK_TRACKING_POLICIES] ([BranchCode], [PolicyCode], [Version]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724194417_AddStockTrackingPolicies'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260724194417_AddStockTrackingPolicies', N'10.0.10');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724203050_EnforceCentralStockTrackingOnOutbound'
)
BEGIN
    ALTER TABLE [RII_WO_TRACKING] ADD [ExpirationDate] date NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724203050_EnforceCentralStockTrackingOnOutbound'
)
BEGIN
    ALTER TABLE [RII_WO_TRACKING] ADD [ManufacturingDate] date NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724203050_EnforceCentralStockTrackingOnOutbound'
)
BEGIN
    ALTER TABLE [RII_SH_TRACKING] ADD [ExpirationDate] date NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724203050_EnforceCentralStockTrackingOnOutbound'
)
BEGIN
    ALTER TABLE [RII_SH_TRACKING] ADD [ManufacturingDate] date NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724203050_EnforceCentralStockTrackingOnOutbound'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260724203050_EnforceCentralStockTrackingOnOutbound', N'10.0.10');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260725161939_AddStockBasedAutomaticSerialLifecycle'
)
BEGIN
    ALTER TABLE [RII_STOCK_TRACKING_POLICIES] ADD [AutoGenerateSerials] bit NOT NULL DEFAULT CAST(0 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260725161939_AddStockBasedAutomaticSerialLifecycle'
)
BEGIN
    ALTER TABLE [RII_SERIAL_NUMBER_RULES] ADD [NextSequence] bigint NOT NULL DEFAULT CAST(1 AS bigint);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260725161939_AddStockBasedAutomaticSerialLifecycle'
)
BEGIN
    CREATE TABLE [RII_STOCK_SERIAL_REGISTRY] (
        [Id] bigint NOT NULL IDENTITY,
        [StockId] bigint NOT NULL,
        [SerialNo] nvarchar(100) NOT NULL,
        [NormalizedSerialNo] nvarchar(100) NOT NULL,
        [Status] nvarchar(20) NOT NULL,
        [SerialNumberRuleId] bigint NULL,
        [SequenceNumber] bigint NOT NULL,
        [GenerationRequestKey] nvarchar(100) NOT NULL,
        [GenerationOrdinal] int NOT NULL,
        [SourceOperationType] nvarchar(50) NULL,
        [SourceOperationId] bigint NULL,
        [ReservedAtUtc] datetimeoffset NOT NULL,
        [ActivatedAtUtc] datetimeoffset NULL,
        [ConsumedAtUtc] datetimeoffset NULL,
        [VoidedAtUtc] datetimeoffset NULL,
        [VoidedReason] nvarchar(500) NULL,
        [LastStockMovementOperationId] bigint NULL,
        [RowVersion] rowversion NOT NULL,
        [BranchCode] nvarchar(10) NOT NULL DEFAULT N'0',
        [CreatedDate] datetime2 NULL,
        [UpdatedDate] datetime2 NULL,
        [DeletedDate] datetime2 NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [CreatedBy] bigint NULL,
        [UpdatedBy] bigint NULL,
        [DeletedBy] bigint NULL,
        CONSTRAINT [PK_RII_STOCK_SERIAL_REGISTRY] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_RII_STOCK_SERIAL_REGISTRY_RII_SERIAL_NUMBER_RULES_SerialNumberRuleId] FOREIGN KEY ([SerialNumberRuleId]) REFERENCES [RII_SERIAL_NUMBER_RULES] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_RII_STOCK_SERIAL_REGISTRY_RII_STOCK_MOVEMENT_OPERATION_LastStockMovementOperationId] FOREIGN KEY ([LastStockMovementOperationId]) REFERENCES [RII_STOCK_MOVEMENT_OPERATION] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_RII_STOCK_SERIAL_REGISTRY_RII_STOCK_StockId] FOREIGN KEY ([StockId]) REFERENCES [RII_STOCK] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260725161939_AddStockBasedAutomaticSerialLifecycle'
)
BEGIN
    ;WITH AggregatedSerials AS
    (
        SELECT
            StockId,
            UPPER(LTRIM(RTRIM(SerialNo))) AS NormalizedSerialNo,
            MAX(LTRIM(RTRIM(SerialNo))) AS SerialNo,
            MAX(BranchCode) AS BranchCode,
            SUM(QuantityDelta) AS CurrentQuantity,
            MIN(OccurredAt) AS FirstSeenAtUtc,
            MAX(OccurredAt) AS LastSeenAtUtc,
            MAX(OperationId) AS LastOperationId
        FROM dbo.RII_STOCK_MOVEMENT
        WHERE SerialNo IS NOT NULL
          AND LTRIM(RTRIM(SerialNo)) <> ''
        GROUP BY StockId, UPPER(LTRIM(RTRIM(SerialNo)))
    ),
    NumberedSerials AS
    (
        SELECT *,
            ROW_NUMBER() OVER
            (
                PARTITION BY StockId
                ORDER BY NormalizedSerialNo
            ) AS LegacyOrdinal
        FROM AggregatedSerials
    )
    INSERT INTO dbo.RII_STOCK_SERIAL_REGISTRY
    (
        StockId,
        SerialNo,
        NormalizedSerialNo,
        Status,
        SerialNumberRuleId,
        SequenceNumber,
        GenerationRequestKey,
        GenerationOrdinal,
        SourceOperationType,
        SourceOperationId,
        ReservedAtUtc,
        ActivatedAtUtc,
        ConsumedAtUtc,
        VoidedAtUtc,
        VoidedReason,
        LastStockMovementOperationId,
        BranchCode,
        CreatedDate,
        IsDeleted
    )
    SELECT
        StockId,
        UPPER(SerialNo),
        NormalizedSerialNo,
        CASE WHEN CurrentQuantity > 0 THEN 'Available' ELSE 'Consumed' END,
        NULL,
        0,
        CONCAT('LEGACY-', StockId, '-', LegacyOrdinal),
        1,
        'LegacyMovementBackfill',
        LastOperationId,
        TODATETIMEOFFSET(FirstSeenAtUtc, '+00:00'),
        TODATETIMEOFFSET(FirstSeenAtUtc, '+00:00'),
        CASE
            WHEN CurrentQuantity <= 0
            THEN TODATETIMEOFFSET(LastSeenAtUtc, '+00:00')
            ELSE NULL
        END,
        NULL,
        NULL,
        LastOperationId,
        BranchCode,
        FirstSeenAtUtc,
        0
    FROM NumberedSerials;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260725161939_AddStockBasedAutomaticSerialLifecycle'
)
BEGIN
    CREATE INDEX [IX_RII_STOCK_SERIAL_REGISTRY_IsDeleted] ON [RII_STOCK_SERIAL_REGISTRY] ([IsDeleted]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260725161939_AddStockBasedAutomaticSerialLifecycle'
)
BEGIN
    CREATE INDEX [IX_RII_STOCK_SERIAL_REGISTRY_LastStockMovementOperationId] ON [RII_STOCK_SERIAL_REGISTRY] ([LastStockMovementOperationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260725161939_AddStockBasedAutomaticSerialLifecycle'
)
BEGIN
    CREATE INDEX [IX_RII_STOCK_SERIAL_REGISTRY_SerialNumberRuleId] ON [RII_STOCK_SERIAL_REGISTRY] ([SerialNumberRuleId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260725161939_AddStockBasedAutomaticSerialLifecycle'
)
BEGIN
    CREATE INDEX [IX_RII_STOCK_SERIAL_STATUS] ON [RII_STOCK_SERIAL_REGISTRY] ([StockId], [Status]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260725161939_AddStockBasedAutomaticSerialLifecycle'
)
BEGIN
    CREATE UNIQUE INDEX [UX_RII_STOCK_SERIAL_IDEMPOTENCY] ON [RII_STOCK_SERIAL_REGISTRY] ([StockId], [GenerationRequestKey], [GenerationOrdinal]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260725161939_AddStockBasedAutomaticSerialLifecycle'
)
BEGIN
    CREATE UNIQUE INDEX [UX_RII_STOCK_SERIAL_STOCK_NUMBER] ON [RII_STOCK_SERIAL_REGISTRY] ([StockId], [NormalizedSerialNo]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260725161939_AddStockBasedAutomaticSerialLifecycle'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260725161939_AddStockBasedAutomaticSerialLifecycle', N'10.0.10');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260725172722_CompleteGoodsReceiptQualityAndSteelFlow'
)
BEGIN
    ALTER TABLE [RII_VEHICLE_CHECKIN_HEADER] ADD [SteelSheetCount] int NOT NULL DEFAULT 0;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260725172722_CompleteGoodsReceiptQualityAndSteelFlow'
)
BEGIN
    ALTER TABLE [RII_QUALITY_INSPECTIONS] ADD [QueuedAtUtc] datetimeoffset NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260725172722_CompleteGoodsReceiptQualityAndSteelFlow'
)
BEGIN
    ALTER TABLE [RII_QUALITY_INSPECTIONS] ADD [QueuedBy] bigint NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260725172722_CompleteGoodsReceiptQualityAndSteelFlow'
)
BEGIN
    EXEC sys.sp_executesql N'UPDATE [RII_QUALITY_INSPECTIONS]
    SET [QueuedAtUtc] = [CreatedAtUtc],
        [QueuedBy] = [CreatedBy]
    WHERE [QueuedAtUtc] IS NULL;';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260725172722_CompleteGoodsReceiptQualityAndSteelFlow'
)
BEGIN
    ;WITH [OrderedPlacements] AS
    (
        SELECT [Id],
               ROW_NUMBER() OVER
               (
                   PARTITION BY [LocationId]
                   ORDER BY [PlacedAtUtc], [Id]
               ) AS [NextStackOrder]
        FROM [RII_STEEL_RECEIPT_PLACEMENT]
        WHERE [IsDeleted] = 0
    )
    UPDATE [Placement]
    SET [PlacementType] = N'Stacked',
        [RowNo] = 1,
        [PositionNo] = 1,
        [StackOrderNo] = [Ordered].[NextStackOrder]
    FROM [RII_STEEL_RECEIPT_PLACEMENT] AS [Placement]
    INNER JOIN [OrderedPlacements] AS [Ordered] ON [Placement].[Id] = [Ordered].[Id];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260725172722_CompleteGoodsReceiptQualityAndSteelFlow'
)
BEGIN
    EXEC(N'ALTER TABLE [RII_VEHICLE_CHECKIN_HEADER] ADD CONSTRAINT [CK_RII_VEHICLE_CHECKIN_STEEL_SHEET_COUNT] CHECK ([SteelSheetCount] >= 0)');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260725172722_CompleteGoodsReceiptQualityAndSteelFlow'
)
BEGIN
    EXEC sys.sp_executesql N'CREATE INDEX [IX_RII_QUALITY_INSPECTIONS_BranchCode_QueuedAtUtc_Status] ON [RII_QUALITY_INSPECTIONS] ([BranchCode], [QueuedAtUtc], [Status]);';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260725172722_CompleteGoodsReceiptQualityAndSteelFlow'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260725172722_CompleteGoodsReceiptQualityAndSteelFlow', N'10.0.10');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726120931_AddAtomicSteelVehicleAcceptance'
)
BEGIN
    ALTER TABLE [RII_STEEL_RECEIPT_PLAN_LINE] ADD [VehicleAcceptanceId] bigint NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726120931_AddAtomicSteelVehicleAcceptance'
)
BEGIN
    CREATE TABLE [RII_STEEL_VEHICLE_ACCEPTANCE] (
        [Id] bigint NOT NULL IDENTITY,
        [IdempotencyKey] uniqueidentifier NOT NULL,
        [VehicleCheckInId] bigint NOT NULL,
        [PlateCount] int NOT NULL,
        [TotalAcceptedQuantity] decimal(18,6) NOT NULL,
        [Status] nvarchar(30) NOT NULL,
        [AcceptedAtUtc] datetimeoffset NOT NULL,
        [AcceptedBy] bigint NOT NULL,
        [Note] nvarchar(1000) NULL,
        [BranchCode] nvarchar(10) NOT NULL DEFAULT N'0',
        [CreatedDate] datetime2 NULL,
        [UpdatedDate] datetime2 NULL,
        [DeletedDate] datetime2 NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [CreatedBy] bigint NULL,
        [UpdatedBy] bigint NULL,
        [DeletedBy] bigint NULL,
        CONSTRAINT [PK_RII_STEEL_VEHICLE_ACCEPTANCE] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_RII_STEEL_VEHICLE_ACCEPTANCE_COUNT] CHECK ([PlateCount] > 0),
        CONSTRAINT [CK_RII_STEEL_VEHICLE_ACCEPTANCE_QTY] CHECK ([TotalAcceptedQuantity] > 0),
        CONSTRAINT [FK_RII_STEEL_VEHICLE_ACCEPTANCE_RII_VEHICLE_CHECKIN_HEADER_VehicleCheckInId] FOREIGN KEY ([VehicleCheckInId]) REFERENCES [RII_VEHICLE_CHECKIN_HEADER] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726120931_AddAtomicSteelVehicleAcceptance'
)
BEGIN
    EXEC(N'CREATE INDEX [IX_RII_STEEL_RECEIPT_PLAN_LINE_VehicleAcceptanceId] ON [RII_STEEL_RECEIPT_PLAN_LINE] ([VehicleAcceptanceId]) WHERE [VehicleAcceptanceId] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726120931_AddAtomicSteelVehicleAcceptance'
)
BEGIN
    CREATE UNIQUE INDEX [IX_RII_STEEL_VEHICLE_ACCEPTANCE_IdempotencyKey] ON [RII_STEEL_VEHICLE_ACCEPTANCE] ([IdempotencyKey]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726120931_AddAtomicSteelVehicleAcceptance'
)
BEGIN
    CREATE INDEX [IX_RII_STEEL_VEHICLE_ACCEPTANCE_IsDeleted] ON [RII_STEEL_VEHICLE_ACCEPTANCE] ([IsDeleted]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726120931_AddAtomicSteelVehicleAcceptance'
)
BEGIN
    CREATE INDEX [IX_RII_STEEL_VEHICLE_ACCEPTANCE_VehicleCheckInId_AcceptedAtUtc] ON [RII_STEEL_VEHICLE_ACCEPTANCE] ([VehicleCheckInId], [AcceptedAtUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726120931_AddAtomicSteelVehicleAcceptance'
)
BEGIN
    ALTER TABLE [RII_STEEL_RECEIPT_PLAN_LINE] ADD CONSTRAINT [FK_RII_STEEL_RECEIPT_PLAN_LINE_RII_STEEL_VEHICLE_ACCEPTANCE_VehicleAcceptanceId] FOREIGN KEY ([VehicleAcceptanceId]) REFERENCES [RII_STEEL_VEHICLE_ACCEPTANCE] ([Id]) ON DELETE NO ACTION;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726120931_AddAtomicSteelVehicleAcceptance'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260726120931_AddAtomicSteelVehicleAcceptance', N'10.0.10');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726143913_AddErpPostingIntegration'
)
BEGIN
    ALTER TABLE [RII_PROJECT_SETTINGS] ADD [SendSerialsToErp] bit NOT NULL DEFAULT CAST(1 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726143913_AddErpPostingIntegration'
)
BEGIN
    CREATE TABLE [RII_ERP_POSTING_RECORDS] (
        [Id] bigint NOT NULL IDENTITY,
        [SourceType] int NOT NULL,
        [SourceEntityId] bigint NOT NULL,
        [SourceDocumentNo] nvarchar(100) NOT NULL,
        [IdempotencyKey] uniqueidentifier NOT NULL,
        [RequestHash] nvarchar(64) NOT NULL,
        [Status] int NOT NULL,
        [AttemptCount] int NOT NULL,
        [StartedAtUtc] datetimeoffset NULL,
        [CompletedAtUtc] datetimeoffset NULL,
        [LastHttpStatusCode] int NULL,
        [ErpDocumentNo] nvarchar(100) NULL,
        [ErpWaybillNo] nvarchar(100) NULL,
        [ErpRecordNo] nvarchar(100) NULL,
        [ErpReferenceNo] nvarchar(150) NULL,
        [LastErrorCode] nvarchar(100) NULL,
        [LastErrorMessage] nvarchar(4000) NULL,
        [TraceId] nvarchar(100) NULL,
        [RowVersion] rowversion NOT NULL,
        [BranchCode] nvarchar(10) NOT NULL DEFAULT N'0',
        [CreatedDate] datetime2 NULL,
        [UpdatedDate] datetime2 NULL,
        [DeletedDate] datetime2 NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [CreatedBy] bigint NULL,
        [UpdatedBy] bigint NULL,
        [DeletedBy] bigint NULL,
        CONSTRAINT [PK_RII_ERP_POSTING_RECORDS] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726143913_AddErpPostingIntegration'
)
BEGIN
    CREATE TABLE [RII_ERP_INTEGRATION_ATTEMPTS] (
        [Id] bigint NOT NULL IDENTITY,
        [ErpPostingRecordId] bigint NOT NULL,
        [AttemptNo] int NOT NULL,
        [Operation] nvarchar(100) NOT NULL,
        [HttpMethod] nvarchar(10) NOT NULL,
        [Endpoint] nvarchar(500) NOT NULL,
        [RequestHash] nvarchar(64) NOT NULL,
        [HttpStatusCode] int NULL,
        [IsSuccessful] bit NOT NULL,
        [CommitUncertain] bit NOT NULL,
        [DurationMs] bigint NOT NULL,
        [ErrorCode] nvarchar(100) NULL,
        [ErrorMessage] nvarchar(4000) NULL,
        [ProviderResponse] nvarchar(max) NULL,
        [TraceId] nvarchar(100) NULL,
        [StartedAtUtc] datetimeoffset NOT NULL,
        [CompletedAtUtc] datetimeoffset NOT NULL,
        [BranchCode] nvarchar(10) NOT NULL DEFAULT N'0',
        [CreatedDate] datetime2 NULL,
        [UpdatedDate] datetime2 NULL,
        [DeletedDate] datetime2 NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [CreatedBy] bigint NULL,
        [UpdatedBy] bigint NULL,
        [DeletedBy] bigint NULL,
        CONSTRAINT [PK_RII_ERP_INTEGRATION_ATTEMPTS] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_RII_ERP_INTEGRATION_ATTEMPTS_RII_ERP_POSTING_RECORDS_ErpPostingRecordId] FOREIGN KEY ([ErpPostingRecordId]) REFERENCES [RII_ERP_POSTING_RECORDS] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726143913_AddErpPostingIntegration'
)
BEGIN
    EXEC(N'UPDATE [RII_PROJECT_SETTINGS] SET [SendSerialsToErp] = CAST(1 AS bit)
    WHERE [Id] = CAST(1 AS bigint);
    SELECT @@ROWCOUNT');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726143913_AddErpPostingIntegration'
)
BEGIN
    CREATE INDEX [IX_RII_ERP_ATTEMPT_STARTED_STATUS] ON [RII_ERP_INTEGRATION_ATTEMPTS] ([StartedAtUtc], [IsSuccessful]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726143913_AddErpPostingIntegration'
)
BEGIN
    CREATE INDEX [IX_RII_ERP_INTEGRATION_ATTEMPTS_IsDeleted] ON [RII_ERP_INTEGRATION_ATTEMPTS] ([IsDeleted]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726143913_AddErpPostingIntegration'
)
BEGIN
    CREATE UNIQUE INDEX [UX_RII_ERP_ATTEMPT_NO] ON [RII_ERP_INTEGRATION_ATTEMPTS] ([ErpPostingRecordId], [AttemptNo]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726143913_AddErpPostingIntegration'
)
BEGIN
    CREATE INDEX [IX_RII_ERP_POSTING_RECORDS_IsDeleted] ON [RII_ERP_POSTING_RECORDS] ([IsDeleted]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726143913_AddErpPostingIntegration'
)
BEGIN
    CREATE INDEX [IX_RII_ERP_POSTING_STATUS_UPDATED] ON [RII_ERP_POSTING_RECORDS] ([Status], [UpdatedDate]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726143913_AddErpPostingIntegration'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_RII_ERP_POSTING_SOURCE] ON [RII_ERP_POSTING_RECORDS] ([BranchCode], [SourceType], [SourceEntityId]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726143913_AddErpPostingIntegration'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260726143913_AddErpPostingIntegration', N'10.0.10');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726150127_AddErpCancellationSaga'
)
BEGIN
    ALTER TABLE [RII_ERP_POSTING_RECORDS] ADD [ErpRecordId] bigint NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726150127_AddErpCancellationSaga'
)
BEGIN
    EXEC sys.sp_executesql N'UPDATE [RII_ERP_POSTING_RECORDS]
    SET [ErpRecordId] = TRY_CONVERT(bigint, [ErpRecordNo])
    WHERE [ErpRecordId] IS NULL
      AND NULLIF(LTRIM(RTRIM([ErpRecordNo])), '''') IS NOT NULL;';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726150127_AddErpCancellationSaga'
)
BEGIN
    CREATE TABLE [RII_ERP_CANCELLATION_RECORDS] (
        [Id] bigint NOT NULL IDENTITY,
        [ErpPostingRecordId] bigint NOT NULL,
        [IdempotencyKey] uniqueidentifier NOT NULL,
        [RequestHash] nvarchar(64) NOT NULL,
        [Reason] nvarchar(1000) NOT NULL,
        [Status] int NOT NULL,
        [AttemptCount] int NOT NULL,
        [StartedAtUtc] datetimeoffset NULL,
        [ErpDeletedAtUtc] datetimeoffset NULL,
        [WmsReversedAtUtc] datetimeoffset NULL,
        [CompletedAtUtc] datetimeoffset NULL,
        [LastHttpStatusCode] int NULL,
        [LastErrorCode] nvarchar(100) NULL,
        [LastErrorMessage] nvarchar(4000) NULL,
        [TraceId] nvarchar(100) NULL,
        [RowVersion] rowversion NOT NULL,
        [BranchCode] nvarchar(10) NOT NULL DEFAULT N'0',
        [CreatedDate] datetime2 NULL,
        [UpdatedDate] datetime2 NULL,
        [DeletedDate] datetime2 NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [CreatedBy] bigint NULL,
        [UpdatedBy] bigint NULL,
        [DeletedBy] bigint NULL,
        CONSTRAINT [PK_RII_ERP_CANCELLATION_RECORDS] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_RII_ERP_CANCELLATION_RECORDS_RII_ERP_POSTING_RECORDS_ErpPostingRecordId] FOREIGN KEY ([ErpPostingRecordId]) REFERENCES [RII_ERP_POSTING_RECORDS] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726150127_AddErpCancellationSaga'
)
BEGIN
    CREATE TABLE [RII_ERP_CANCELLATION_ATTEMPTS] (
        [Id] bigint NOT NULL IDENTITY,
        [ErpCancellationRecordId] bigint NOT NULL,
        [AttemptNo] int NOT NULL,
        [Operation] nvarchar(100) NOT NULL,
        [HttpMethod] nvarchar(10) NOT NULL,
        [Endpoint] nvarchar(500) NOT NULL,
        [HttpStatusCode] int NULL,
        [IsSuccessful] bit NOT NULL,
        [CommitUncertain] bit NOT NULL,
        [DurationMs] bigint NOT NULL,
        [ErrorCode] nvarchar(100) NULL,
        [ErrorMessage] nvarchar(4000) NULL,
        [ProviderResponse] nvarchar(max) NULL,
        [TraceId] nvarchar(100) NULL,
        [StartedAtUtc] datetimeoffset NOT NULL,
        [CompletedAtUtc] datetimeoffset NOT NULL,
        [BranchCode] nvarchar(10) NOT NULL DEFAULT N'0',
        [CreatedDate] datetime2 NULL,
        [UpdatedDate] datetime2 NULL,
        [DeletedDate] datetime2 NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [CreatedBy] bigint NULL,
        [UpdatedBy] bigint NULL,
        [DeletedBy] bigint NULL,
        CONSTRAINT [PK_RII_ERP_CANCELLATION_ATTEMPTS] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_RII_ERP_CANCELLATION_ATTEMPTS_RII_ERP_CANCELLATION_RECORDS_ErpCancellationRecordId] FOREIGN KEY ([ErpCancellationRecordId]) REFERENCES [RII_ERP_CANCELLATION_RECORDS] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726150127_AddErpCancellationSaga'
)
BEGIN
    CREATE INDEX [IX_RII_ERP_CANCELLATION_ATTEMPTS_IsDeleted] ON [RII_ERP_CANCELLATION_ATTEMPTS] ([IsDeleted]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726150127_AddErpCancellationSaga'
)
BEGIN
    CREATE UNIQUE INDEX [UX_RII_ERP_CANCELLATION_ATTEMPT_NO] ON [RII_ERP_CANCELLATION_ATTEMPTS] ([ErpCancellationRecordId], [AttemptNo]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726150127_AddErpCancellationSaga'
)
BEGIN
    CREATE INDEX [IX_RII_ERP_CANCELLATION_RECORDS_IsDeleted] ON [RII_ERP_CANCELLATION_RECORDS] ([IsDeleted]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726150127_AddErpCancellationSaga'
)
BEGIN
    CREATE INDEX [IX_RII_ERP_CANCELLATION_STATUS_UPDATED] ON [RII_ERP_CANCELLATION_RECORDS] ([Status], [UpdatedDate]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726150127_AddErpCancellationSaga'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_RII_ERP_CANCELLATION_POSTING] ON [RII_ERP_CANCELLATION_RECORDS] ([ErpPostingRecordId]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726150127_AddErpCancellationSaga'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260726150127_AddErpCancellationSaga', N'10.0.10');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726184007_AddIncomingInvoiceArchiveModule'
)
BEGIN
    CREATE TABLE [RII_ELOGO_CONNECTION] (
        [Id] bigint NOT NULL IDENTITY,
        [Key] nvarchar(80) NOT NULL,
        [DisplayName] nvarchar(200) NOT NULL,
        [Vkn] nvarchar(20) NOT NULL,
        [Username] nvarchar(100) NOT NULL,
        [PasswordCipherText] nvarchar(max) NULL,
        [Source] nvarchar(100) NOT NULL,
        [EndpointUrl] nvarchar(500) NULL,
        [ApplicationName] nvarchar(100) NULL,
        [Version] nvarchar(20) NULL,
        [TimeoutSeconds] int NULL,
        [IsActive] bit NOT NULL,
        [IsDefault] bit NOT NULL,
        [Description] nvarchar(500) NULL,
        [RowVersion] rowversion NOT NULL,
        [BranchCode] nvarchar(10) NOT NULL DEFAULT N'0',
        [CreatedDate] datetime2 NULL,
        [UpdatedDate] datetime2 NULL,
        [DeletedDate] datetime2 NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [CreatedBy] bigint NULL,
        [UpdatedBy] bigint NULL,
        [DeletedBy] bigint NULL,
        CONSTRAINT [PK_RII_ELOGO_CONNECTION] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726184007_AddIncomingInvoiceArchiveModule'
)
BEGIN
    CREATE TABLE [RII_INCOMING_INVOICE_HEADER] (
        [Id] bigint NOT NULL IDENTITY,
        [ELogoConnectionId] bigint NOT NULL,
        [OwnerVkn] nvarchar(20) NOT NULL,
        [Uuid] uniqueidentifier NOT NULL,
        [DocumentKind] int NOT NULL,
        [ProfileId] nvarchar(50) NULL,
        [InvoiceNo] nvarchar(50) NOT NULL,
        [InvoiceTypeCode] nvarchar(50) NOT NULL,
        [IssueDate] date NOT NULL,
        [IssueTime] time NULL,
        [CurrencyCode] nvarchar(3) NOT NULL,
        [OrderReferenceNo] nvarchar(100) NULL,
        [DespatchReferenceNo] nvarchar(100) NULL,
        [SupplierVknOrTckn] nvarchar(20) NOT NULL,
        [SupplierName] nvarchar(300) NOT NULL,
        [SupplierTaxOffice] nvarchar(100) NULL,
        [SupplierCustomerId] bigint NULL,
        [CustomerVknOrTckn] nvarchar(20) NOT NULL,
        [CustomerName] nvarchar(300) NOT NULL,
        [LineExtensionAmount] decimal(28,8) NOT NULL,
        [TaxExclusiveAmount] decimal(28,8) NOT NULL,
        [TaxAmount] decimal(28,8) NOT NULL,
        [TaxInclusiveAmount] decimal(28,8) NOT NULL,
        [AllowanceTotalAmount] decimal(28,8) NOT NULL,
        [PayableAmount] decimal(28,8) NOT NULL,
        [ArchiveStatus] int NOT NULL,
        [ValidationStatus] int NOT NULL,
        [ValidationMessage] nvarchar(1000) NULL,
        [SourceHash] nvarchar(64) NOT NULL,
        [ImportedAtUtc] datetimeoffset NOT NULL,
        [LastSynchronizedAtUtc] datetimeoffset NOT NULL,
        [RowVersion] rowversion NOT NULL,
        [BranchCode] nvarchar(10) NOT NULL DEFAULT N'0',
        [CreatedDate] datetime2 NULL,
        [UpdatedDate] datetime2 NULL,
        [DeletedDate] datetime2 NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [CreatedBy] bigint NULL,
        [UpdatedBy] bigint NULL,
        [DeletedBy] bigint NULL,
        CONSTRAINT [PK_RII_INCOMING_INVOICE_HEADER] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_RII_INCOMING_INVOICE_HEADER_RII_ELOGO_CONNECTION_ELogoConnectionId] FOREIGN KEY ([ELogoConnectionId]) REFERENCES [RII_ELOGO_CONNECTION] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726184007_AddIncomingInvoiceArchiveModule'
)
BEGIN
    CREATE TABLE [RII_INCOMING_INVOICE_DOCUMENT] (
        [Id] bigint NOT NULL IDENTITY,
        [IncomingInvoiceId] bigint NOT NULL,
        [Format] int NOT NULL,
        [FileName] nvarchar(260) NOT NULL,
        [ContentType] nvarchar(100) NOT NULL,
        [StoragePath] nvarchar(1000) NOT NULL,
        [FileSize] bigint NOT NULL,
        [Sha256] nvarchar(64) NOT NULL,
        [StoredAtUtc] datetimeoffset NOT NULL,
        [BranchCode] nvarchar(10) NOT NULL DEFAULT N'0',
        [CreatedDate] datetime2 NULL,
        [UpdatedDate] datetime2 NULL,
        [DeletedDate] datetime2 NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [CreatedBy] bigint NULL,
        [UpdatedBy] bigint NULL,
        [DeletedBy] bigint NULL,
        CONSTRAINT [PK_RII_INCOMING_INVOICE_DOCUMENT] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_RII_INCOMING_INVOICE_DOCUMENT_RII_INCOMING_INVOICE_HEADER_IncomingInvoiceId] FOREIGN KEY ([IncomingInvoiceId]) REFERENCES [RII_INCOMING_INVOICE_HEADER] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726184007_AddIncomingInvoiceArchiveModule'
)
BEGIN
    CREATE TABLE [RII_INCOMING_INVOICE_GR_LINK] (
        [Id] bigint NOT NULL IDENTITY,
        [IncomingInvoiceId] bigint NOT NULL,
        [GoodsReceiptId] bigint NOT NULL,
        [IdempotencyKey] uniqueidentifier NOT NULL,
        [RequestHash] nvarchar(64) NOT NULL,
        [LinkedQuantity] decimal(28,8) NOT NULL,
        [LinkedAtUtc] datetimeoffset NOT NULL,
        [LinkedBy] bigint NOT NULL,
        [BranchCode] nvarchar(10) NOT NULL DEFAULT N'0',
        [CreatedDate] datetime2 NULL,
        [UpdatedDate] datetime2 NULL,
        [DeletedDate] datetime2 NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [CreatedBy] bigint NULL,
        [UpdatedBy] bigint NULL,
        [DeletedBy] bigint NULL,
        CONSTRAINT [PK_RII_INCOMING_INVOICE_GR_LINK] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_RII_INCOMING_INVOICE_GR_LINK_RII_GR_HEADER_GoodsReceiptId] FOREIGN KEY ([GoodsReceiptId]) REFERENCES [RII_GR_HEADER] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_RII_INCOMING_INVOICE_GR_LINK_RII_INCOMING_INVOICE_HEADER_IncomingInvoiceId] FOREIGN KEY ([IncomingInvoiceId]) REFERENCES [RII_INCOMING_INVOICE_HEADER] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726184007_AddIncomingInvoiceArchiveModule'
)
BEGIN
    CREATE TABLE [RII_INCOMING_INVOICE_LINE] (
        [Id] bigint NOT NULL IDENTITY,
        [IncomingInvoiceId] bigint NOT NULL,
        [LineNo] int NOT NULL,
        [ExternalLineId] nvarchar(50) NOT NULL,
        [StockCode] nvarchar(100) NOT NULL,
        [BuyerStockCode] nvarchar(100) NULL,
        [StockName] nvarchar(500) NOT NULL,
        [Description] nvarchar(2000) NULL,
        [Quantity] decimal(28,8) NOT NULL,
        [UnitCode] nvarchar(20) NOT NULL,
        [UnitPrice] decimal(28,8) NOT NULL,
        [LineExtensionAmount] decimal(28,8) NOT NULL,
        [TaxRate] decimal(18,6) NOT NULL,
        [TaxAmount] decimal(28,8) NOT NULL,
        [StockId] bigint NULL,
        [YapCodeId] bigint NULL,
        [YapCode] nvarchar(100) NULL,
        [MatchStatus] int NOT NULL,
        [MatchMessage] nvarchar(500) NULL,
        [RowVersion] rowversion NOT NULL,
        [BranchCode] nvarchar(10) NOT NULL DEFAULT N'0',
        [CreatedDate] datetime2 NULL,
        [UpdatedDate] datetime2 NULL,
        [DeletedDate] datetime2 NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [CreatedBy] bigint NULL,
        [UpdatedBy] bigint NULL,
        [DeletedBy] bigint NULL,
        CONSTRAINT [PK_RII_INCOMING_INVOICE_LINE] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_RII_INCOMING_INVOICE_LINE_RII_INCOMING_INVOICE_HEADER_IncomingInvoiceId] FOREIGN KEY ([IncomingInvoiceId]) REFERENCES [RII_INCOMING_INVOICE_HEADER] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_RII_INCOMING_INVOICE_LINE_RII_STOCK_StockId] FOREIGN KEY ([StockId]) REFERENCES [RII_STOCK] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_RII_INCOMING_INVOICE_LINE_RII_YAP_CODE_YapCodeId] FOREIGN KEY ([YapCodeId]) REFERENCES [RII_YAP_CODE] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726184007_AddIncomingInvoiceArchiveModule'
)
BEGIN
    CREATE TABLE [RII_INCOMING_INVOICE_GR_LINE_LINK] (
        [Id] bigint NOT NULL IDENTITY,
        [IncomingInvoiceGoodsReceiptLinkId] bigint NOT NULL,
        [IncomingInvoiceLineId] bigint NOT NULL,
        [GoodsReceiptLineId] bigint NOT NULL,
        [LinkedQuantity] decimal(28,8) NOT NULL,
        [BranchCode] nvarchar(10) NOT NULL DEFAULT N'0',
        [CreatedDate] datetime2 NULL,
        [UpdatedDate] datetime2 NULL,
        [DeletedDate] datetime2 NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [CreatedBy] bigint NULL,
        [UpdatedBy] bigint NULL,
        [DeletedBy] bigint NULL,
        CONSTRAINT [PK_RII_INCOMING_INVOICE_GR_LINE_LINK] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_RII_INCOMING_INVOICE_GR_LINE_LINK_RII_GR_LINE_GoodsReceiptLineId] FOREIGN KEY ([GoodsReceiptLineId]) REFERENCES [RII_GR_LINE] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_RII_INCOMING_INVOICE_GR_LINE_LINK_RII_INCOMING_INVOICE_GR_LINK_IncomingInvoiceGoodsReceiptLinkId] FOREIGN KEY ([IncomingInvoiceGoodsReceiptLinkId]) REFERENCES [RII_INCOMING_INVOICE_GR_LINK] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_RII_INCOMING_INVOICE_GR_LINE_LINK_RII_INCOMING_INVOICE_LINE_IncomingInvoiceLineId] FOREIGN KEY ([IncomingInvoiceLineId]) REFERENCES [RII_INCOMING_INVOICE_LINE] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726184007_AddIncomingInvoiceArchiveModule'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'AvailableOnMobile', N'AvailableOnWeb', N'BranchCode', N'Code', N'CreatedBy', N'CreatedDate', N'DeletedBy', N'DeletedDate', N'Description', N'IsActive', N'Name', N'UpdatedBy', N'UpdatedDate') AND [object_id] = OBJECT_ID(N'[RII_PERMISSION_DEFINITIONS]'))
        SET IDENTITY_INSERT [RII_PERMISSION_DEFINITIONS] ON;
    EXEC(N'INSERT INTO [RII_PERMISSION_DEFINITIONS] ([Id], [AvailableOnMobile], [AvailableOnWeb], [BranchCode], [Code], [CreatedBy], [CreatedDate], [DeletedBy], [DeletedDate], [Description], [IsActive], [Name], [UpdatedBy], [UpdatedDate])
    VALUES (CAST(2300 AS bigint), CAST(0 AS bit), CAST(1 AS bit), N''0'', N''WMS.INCOMING_INVOICE.VIEW'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, NULL, CAST(1 AS bit), N''Gelen e-Fatura/e-Arşiv kayıtlarını görüntüle'', NULL, NULL),
    (CAST(2301 AS bigint), CAST(0 AS bit), CAST(1 AS bit), N''0'', N''WMS.INCOMING_INVOICE.IMPORT'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, NULL, CAST(1 AS bit), N''Gelen e-Fatura/e-Arşiv belgesi arşivle'', NULL, NULL),
    (CAST(2302 AS bigint), CAST(0 AS bit), CAST(1 AS bit), N''0'', N''WMS.INCOMING_INVOICE.CONNECTIONS.MANAGE'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, NULL, CAST(1 AS bit), N''eLogo bağlantılarını yönet'', NULL, NULL),
    (CAST(2303 AS bigint), CAST(0 AS bit), CAST(1 AS bit), N''0'', N''WMS.INCOMING_INVOICE.CREATE_GOODS_RECEIPT'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, NULL, CAST(1 AS bit), N''Faturadan mal kabul taslağı oluştur'', NULL, NULL)');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'AvailableOnMobile', N'AvailableOnWeb', N'BranchCode', N'Code', N'CreatedBy', N'CreatedDate', N'DeletedBy', N'DeletedDate', N'Description', N'IsActive', N'Name', N'UpdatedBy', N'UpdatedDate') AND [object_id] = OBJECT_ID(N'[RII_PERMISSION_DEFINITIONS]'))
        SET IDENTITY_INSERT [RII_PERMISSION_DEFINITIONS] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726184007_AddIncomingInvoiceArchiveModule'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'BranchCode', N'CreatedBy', N'CreatedDate', N'DeletedBy', N'DeletedDate', N'PermissionDefinitionId', N'PermissionGroupId', N'UpdatedBy', N'UpdatedDate') AND [object_id] = OBJECT_ID(N'[RII_PERMISSION_GROUP_PERMISSIONS]'))
        SET IDENTITY_INSERT [RII_PERMISSION_GROUP_PERMISSIONS] ON;
    EXEC(N'INSERT INTO [RII_PERMISSION_GROUP_PERMISSIONS] ([Id], [BranchCode], [CreatedBy], [CreatedDate], [DeletedBy], [DeletedDate], [PermissionDefinitionId], [PermissionGroupId], [UpdatedBy], [UpdatedDate])
    VALUES (CAST(2300 AS bigint), N''0'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, CAST(2300 AS bigint), CAST(1001 AS bigint), NULL, NULL),
    (CAST(2301 AS bigint), N''0'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, CAST(2301 AS bigint), CAST(1001 AS bigint), NULL, NULL),
    (CAST(2302 AS bigint), N''0'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, CAST(2302 AS bigint), CAST(1001 AS bigint), NULL, NULL),
    (CAST(2303 AS bigint), N''0'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, CAST(2303 AS bigint), CAST(1001 AS bigint), NULL, NULL)');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'BranchCode', N'CreatedBy', N'CreatedDate', N'DeletedBy', N'DeletedDate', N'PermissionDefinitionId', N'PermissionGroupId', N'UpdatedBy', N'UpdatedDate') AND [object_id] = OBJECT_ID(N'[RII_PERMISSION_GROUP_PERMISSIONS]'))
        SET IDENTITY_INSERT [RII_PERMISSION_GROUP_PERMISSIONS] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726184007_AddIncomingInvoiceArchiveModule'
)
BEGIN
    CREATE INDEX [IX_RII_ELOGO_CONNECTION_BRANCH_ACTIVE_NAME] ON [RII_ELOGO_CONNECTION] ([BranchCode], [IsActive], [DisplayName]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726184007_AddIncomingInvoiceArchiveModule'
)
BEGIN
    CREATE INDEX [IX_RII_ELOGO_CONNECTION_IsDeleted] ON [RII_ELOGO_CONNECTION] ([IsDeleted]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726184007_AddIncomingInvoiceArchiveModule'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_RII_ELOGO_CONNECTION_BRANCH_KEY] ON [RII_ELOGO_CONNECTION] ([BranchCode], [Key]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726184007_AddIncomingInvoiceArchiveModule'
)
BEGIN
    CREATE INDEX [IX_RII_INCOMING_INVOICE_DOCUMENT_IsDeleted] ON [RII_INCOMING_INVOICE_DOCUMENT] ([IsDeleted]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726184007_AddIncomingInvoiceArchiveModule'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_RII_INCOMING_INVOICE_DOCUMENT_FORMAT] ON [RII_INCOMING_INVOICE_DOCUMENT] ([IncomingInvoiceId], [Format]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726184007_AddIncomingInvoiceArchiveModule'
)
BEGIN
    CREATE INDEX [IX_RII_INCOMING_INVOICE_GR_LINE_LINK_GoodsReceiptLineId] ON [RII_INCOMING_INVOICE_GR_LINE_LINK] ([GoodsReceiptLineId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726184007_AddIncomingInvoiceArchiveModule'
)
BEGIN
    CREATE INDEX [IX_RII_INCOMING_INVOICE_GR_LINE_LINK_IncomingInvoiceGoodsReceiptLinkId] ON [RII_INCOMING_INVOICE_GR_LINE_LINK] ([IncomingInvoiceGoodsReceiptLinkId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726184007_AddIncomingInvoiceArchiveModule'
)
BEGIN
    CREATE INDEX [IX_RII_INCOMING_INVOICE_GR_LINE_LINK_IsDeleted] ON [RII_INCOMING_INVOICE_GR_LINE_LINK] ([IsDeleted]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726184007_AddIncomingInvoiceArchiveModule'
)
BEGIN
    CREATE INDEX [IX_RII_INCOMING_INVOICE_LINE_LINK_REMAINING] ON [RII_INCOMING_INVOICE_GR_LINE_LINK] ([IncomingInvoiceLineId], [IsDeleted]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726184007_AddIncomingInvoiceArchiveModule'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_RII_INCOMING_INVOICE_GR_LINE] ON [RII_INCOMING_INVOICE_GR_LINE_LINK] ([IncomingInvoiceLineId], [GoodsReceiptLineId]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726184007_AddIncomingInvoiceArchiveModule'
)
BEGIN
    CREATE INDEX [IX_RII_INCOMING_INVOICE_GR_LINK_GoodsReceiptId] ON [RII_INCOMING_INVOICE_GR_LINK] ([GoodsReceiptId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726184007_AddIncomingInvoiceArchiveModule'
)
BEGIN
    CREATE INDEX [IX_RII_INCOMING_INVOICE_GR_LINK_IsDeleted] ON [RII_INCOMING_INVOICE_GR_LINK] ([IsDeleted]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726184007_AddIncomingInvoiceArchiveModule'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_RII_INCOMING_INVOICE_GR_IDEMPOTENCY] ON [RII_INCOMING_INVOICE_GR_LINK] ([IncomingInvoiceId], [IdempotencyKey]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726184007_AddIncomingInvoiceArchiveModule'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_RII_INCOMING_INVOICE_GR_LINK] ON [RII_INCOMING_INVOICE_GR_LINK] ([IncomingInvoiceId], [GoodsReceiptId]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726184007_AddIncomingInvoiceArchiveModule'
)
BEGIN
    CREATE INDEX [IX_RII_INCOMING_INVOICE_BRANCH_DATE_NO] ON [RII_INCOMING_INVOICE_HEADER] ([BranchCode], [IssueDate], [InvoiceNo]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726184007_AddIncomingInvoiceArchiveModule'
)
BEGIN
    CREATE INDEX [IX_RII_INCOMING_INVOICE_HEADER_ELogoConnectionId] ON [RII_INCOMING_INVOICE_HEADER] ([ELogoConnectionId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726184007_AddIncomingInvoiceArchiveModule'
)
BEGIN
    CREATE INDEX [IX_RII_INCOMING_INVOICE_HEADER_IsDeleted] ON [RII_INCOMING_INVOICE_HEADER] ([IsDeleted]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726184007_AddIncomingInvoiceArchiveModule'
)
BEGIN
    CREATE INDEX [IX_RII_INCOMING_INVOICE_STATUS_IMPORTED] ON [RII_INCOMING_INVOICE_HEADER] ([BranchCode], [ArchiveStatus], [ImportedAtUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726184007_AddIncomingInvoiceArchiveModule'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_RII_INCOMING_INVOICE_OWNER_UUID] ON [RII_INCOMING_INVOICE_HEADER] ([BranchCode], [OwnerVkn], [Uuid]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726184007_AddIncomingInvoiceArchiveModule'
)
BEGIN
    CREATE INDEX [IX_RII_INCOMING_INVOICE_LINE_IsDeleted] ON [RII_INCOMING_INVOICE_LINE] ([IsDeleted]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726184007_AddIncomingInvoiceArchiveModule'
)
BEGIN
    CREATE INDEX [IX_RII_INCOMING_INVOICE_LINE_STOCK] ON [RII_INCOMING_INVOICE_LINE] ([BranchCode], [StockCode]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726184007_AddIncomingInvoiceArchiveModule'
)
BEGIN
    CREATE INDEX [IX_RII_INCOMING_INVOICE_LINE_StockId] ON [RII_INCOMING_INVOICE_LINE] ([StockId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726184007_AddIncomingInvoiceArchiveModule'
)
BEGIN
    CREATE INDEX [IX_RII_INCOMING_INVOICE_LINE_YapCodeId] ON [RII_INCOMING_INVOICE_LINE] ([YapCodeId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726184007_AddIncomingInvoiceArchiveModule'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_RII_INCOMING_INVOICE_LINE_NO] ON [RII_INCOMING_INVOICE_LINE] ([IncomingInvoiceId], [LineNo]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726184007_AddIncomingInvoiceArchiveModule'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260726184007_AddIncomingInvoiceArchiveModule', N'10.0.10');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726185217_AddUserBackgroundMotionPreferences'
)
BEGIN
    ALTER TABLE [RII_USER_DETAILS] ADD [BackgroundMotionEnabled] bit NOT NULL DEFAULT CAST(0 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726185217_AddUserBackgroundMotionPreferences'
)
BEGIN
    ALTER TABLE [RII_USER_DETAILS] ADD [BackgroundMotionVariant] nvarchar(40) NOT NULL DEFAULT N'rack-scanner';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726185217_AddUserBackgroundMotionPreferences'
)
BEGIN
    EXEC(N'UPDATE [RII_USER_DETAILS] SET [BackgroundMotionEnabled] = CAST(0 AS bit), [BackgroundMotionVariant] = N''rack-scanner''
    WHERE [UserId] = CAST(1 AS bigint);
    SELECT @@ROWCOUNT');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726185217_AddUserBackgroundMotionPreferences'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260726185217_AddUserBackgroundMotionPreferences', N'10.0.10');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726190644_AddStockBaseUnitAuthority'
)
BEGIN
    ALTER TABLE [RII_STOCK] ADD [BaseUnitCode] nvarchar(20) NOT NULL DEFAULT N'ADET';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726190644_AddStockBaseUnitAuthority'
)
BEGIN
    EXEC sys.sp_executesql N'IF OBJECT_ID(N''dbo.RII_FN_STOK'', N''IF'') IS NOT NULL
    BEGIN
        UPDATE target
        SET target.BaseUnitCode = UPPER(LTRIM(RTRIM(source.OLCU_BR1)))
        FROM dbo.RII_STOCK AS target
        INNER JOIN dbo.RII_FN_STOK(NULL, NULL) AS source
            ON target.BranchCode = CONVERT(nvarchar(20), source.SUBE_KODU)
           AND UPPER(LTRIM(RTRIM(target.ErpStockCode))) = UPPER(LTRIM(RTRIM(source.STOK_KODU)))
        WHERE NULLIF(LTRIM(RTRIM(source.OLCU_BR1)), N'''') IS NOT NULL;
    END';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726190644_AddStockBaseUnitAuthority'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260726190644_AddStockBaseUnitAuthority', N'10.0.10');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726194449_AddProductionAndSubcontractingTransferModules'
)
BEGIN
    ALTER TABLE [RII_WT_LINE] DROP CONSTRAINT [CK_RII_WT_LINE_WAREHOUSE];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726194449_AddProductionAndSubcontractingTransferModules'
)
BEGIN
    ALTER TABLE [RII_WT_HEADER] DROP CONSTRAINT [CK_RII_WT_HEADER_WAREHOUSE];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726194449_AddProductionAndSubcontractingTransferModules'
)
BEGIN
    ALTER TABLE [RII_WT_HEADER] ADD [BusinessContext] nvarchar(40) NOT NULL DEFAULT N'InterWarehouse';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726194449_AddProductionAndSubcontractingTransferModules'
)
BEGIN
    CREATE TABLE [RII_PT_HEADER_LINK] (
        [Id] bigint NOT NULL IDENTITY,
        [WarehouseTransferHeaderId] bigint NOT NULL,
        [Purpose] nvarchar(30) NOT NULL,
        [ProductionHeaderId] bigint NULL,
        [ProductionOrderId] bigint NULL,
        [ProductionOperationId] bigint NULL,
        [ProductionPlanNo] nvarchar(100) NULL,
        [ProductionOrderNo] nvarchar(100) NULL,
        [ProductionOperationCode] nvarchar(100) NULL,
        [SourceWorkCenterCode] nvarchar(100) NULL,
        [TargetWorkCenterCode] nvarchar(100) NULL,
        [TriggeredByProduction] bit NOT NULL,
        [AutoGenerated] bit NOT NULL,
        [RequiredForOrderStart] bit NOT NULL,
        [RequiredForOrderCompletion] bit NOT NULL,
        [MaterialAvailabilityStatus] nvarchar(30) NOT NULL,
        [RequirementCalculatedAtUtc] datetimeoffset NULL,
        [RowVersion] rowversion NOT NULL,
        [BranchCode] nvarchar(10) NOT NULL DEFAULT N'0',
        [CreatedDate] datetime2 NULL,
        [UpdatedDate] datetime2 NULL,
        [DeletedDate] datetime2 NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [CreatedBy] bigint NULL,
        [UpdatedBy] bigint NULL,
        [DeletedBy] bigint NULL,
        CONSTRAINT [PK_RII_PT_HEADER_LINK] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_RII_PT_HEADER_LINK_RII_WT_HEADER_WarehouseTransferHeaderId] FOREIGN KEY ([WarehouseTransferHeaderId]) REFERENCES [RII_WT_HEADER] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726194449_AddProductionAndSubcontractingTransferModules'
)
BEGIN
    CREATE TABLE [RII_PT_POLICIES] (
        [Id] bigint NOT NULL IDENTITY,
        [PolicyKey] nvarchar(30) NOT NULL,
        [RequireProductionOrderReference] bit NOT NULL,
        [AllowManualTransfer] bit NOT NULL,
        [AllowAutomaticGeneration] bit NOT NULL,
        [CheckMaterialAvailability] bit NOT NULL,
        [BlockOnShortage] bit NOT NULL,
        [RequireTaskAssignment] bit NOT NULL,
        [RequireSourceProductionLocation] bit NOT NULL,
        [RequireTargetProductionLocation] bit NOT NULL,
        [AllowPartialSupply] bit NOT NULL,
        [AllowOverIssue] bit NOT NULL,
        [OverIssueTolerancePercent] decimal(9,4) NOT NULL,
        [RequireApproval] bit NOT NULL,
        [RowVersion] rowversion NOT NULL,
        [BranchCode] nvarchar(10) NOT NULL DEFAULT N'0',
        [CreatedDate] datetime2 NULL,
        [UpdatedDate] datetime2 NULL,
        [DeletedDate] datetime2 NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [CreatedBy] bigint NULL,
        [UpdatedBy] bigint NULL,
        [DeletedBy] bigint NULL,
        CONSTRAINT [PK_RII_PT_POLICIES] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_RII_PT_POLICY_OVER_ISSUE] CHECK ([OverIssueTolerancePercent] >= 0 AND [OverIssueTolerancePercent] <= 100)
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726194449_AddProductionAndSubcontractingTransferModules'
)
BEGIN
    CREATE TABLE [RII_ST_HEADER_LINK] (
        [Id] bigint NOT NULL IDENTITY,
        [WarehouseTransferHeaderId] bigint NOT NULL,
        [Direction] nvarchar(30) NOT NULL,
        [SupplierId] bigint NOT NULL,
        [SupplierCodeSnapshot] nvarchar(100) NOT NULL,
        [SupplierNameSnapshot] nvarchar(300) NOT NULL,
        [SubcontractOrderNo] nvarchar(100) NULL,
        [SubcontractOrderDate] date NULL,
        [ParentIssueTransferId] bigint NULL,
        [ExpectedReturnAtUtc] datetimeoffset NULL,
        [OwnershipType] nvarchar(30) NOT NULL,
        [QualityInspectionRequired] bit NOT NULL,
        [ComponentsIssuedConfirmed] bit NOT NULL,
        [OperationCode] nvarchar(100) NULL,
        [SupplierDispatchNo] nvarchar(100) NULL,
        [RowVersion] rowversion NOT NULL,
        [BranchCode] nvarchar(10) NOT NULL DEFAULT N'0',
        [CreatedDate] datetime2 NULL,
        [UpdatedDate] datetime2 NULL,
        [DeletedDate] datetime2 NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [CreatedBy] bigint NULL,
        [UpdatedBy] bigint NULL,
        [DeletedBy] bigint NULL,
        CONSTRAINT [PK_RII_ST_HEADER_LINK] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_RII_ST_HEADER_LINK_RII_CUSTOMER_SupplierId] FOREIGN KEY ([SupplierId]) REFERENCES [RII_CUSTOMER] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_RII_ST_HEADER_LINK_RII_WT_HEADER_ParentIssueTransferId] FOREIGN KEY ([ParentIssueTransferId]) REFERENCES [RII_WT_HEADER] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_RII_ST_HEADER_LINK_RII_WT_HEADER_WarehouseTransferHeaderId] FOREIGN KEY ([WarehouseTransferHeaderId]) REFERENCES [RII_WT_HEADER] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726194449_AddProductionAndSubcontractingTransferModules'
)
BEGIN
    CREATE TABLE [RII_ST_POLICIES] (
        [Id] bigint NOT NULL IDENTITY,
        [PolicyKey] nvarchar(30) NOT NULL,
        [RequireSupplier] bit NOT NULL,
        [RequireSubcontractOrderForReceipt] bit NOT NULL,
        [RequireIssueBeforeReceipt] bit NOT NULL,
        [AllowOrderlessIssue] bit NOT NULL,
        [AllowOrderlessReceipt] bit NOT NULL,
        [AllowSupplierToSupplier] bit NOT NULL,
        [AllowPartialIssue] bit NOT NULL,
        [AllowPartialReceipt] bit NOT NULL,
        [RequireQualityOnReceipt] bit NOT NULL,
        [RequireTaskAssignment] bit NOT NULL,
        [RequireApproval] bit NOT NULL,
        [AllowOverReceipt] bit NOT NULL,
        [OverReceiptTolerancePercent] decimal(9,4) NOT NULL,
        [DefaultLeadTimeDays] int NOT NULL,
        [RowVersion] rowversion NOT NULL,
        [BranchCode] nvarchar(10) NOT NULL DEFAULT N'0',
        [CreatedDate] datetime2 NULL,
        [UpdatedDate] datetime2 NULL,
        [DeletedDate] datetime2 NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [CreatedBy] bigint NULL,
        [UpdatedBy] bigint NULL,
        [DeletedBy] bigint NULL,
        CONSTRAINT [PK_RII_ST_POLICIES] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_RII_ST_POLICY_LEAD_TIME] CHECK ([DefaultLeadTimeDays] >= 0 AND [DefaultLeadTimeDays] <= 3650),
        CONSTRAINT [CK_RII_ST_POLICY_OVER_RECEIPT] CHECK ([OverReceiptTolerancePercent] >= 0 AND [OverReceiptTolerancePercent] <= 100)
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726194449_AddProductionAndSubcontractingTransferModules'
)
BEGIN
    CREATE TABLE [RII_PT_LINE_LINK] (
        [Id] bigint NOT NULL IDENTITY,
        [ProductionTransferHeaderLinkId] bigint NOT NULL,
        [WarehouseTransferLineId] bigint NOT NULL,
        [LineRole] nvarchar(30) NOT NULL,
        [ProductionConsumptionId] bigint NULL,
        [ProductionOutputId] bigint NULL,
        [RequirementReference] nvarchar(150) NULL,
        [RequiredQuantity] decimal(20,6) NOT NULL,
        [BranchCode] nvarchar(10) NOT NULL DEFAULT N'0',
        [CreatedDate] datetime2 NULL,
        [UpdatedDate] datetime2 NULL,
        [DeletedDate] datetime2 NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [CreatedBy] bigint NULL,
        [UpdatedBy] bigint NULL,
        [DeletedBy] bigint NULL,
        CONSTRAINT [PK_RII_PT_LINE_LINK] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_RII_PT_LINE_LINK_REQUIRED_QTY] CHECK ([RequiredQuantity] > 0),
        CONSTRAINT [FK_RII_PT_LINE_LINK_RII_PT_HEADER_LINK_ProductionTransferHeaderLinkId] FOREIGN KEY ([ProductionTransferHeaderLinkId]) REFERENCES [RII_PT_HEADER_LINK] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_RII_PT_LINE_LINK_RII_WT_LINE_WarehouseTransferLineId] FOREIGN KEY ([WarehouseTransferLineId]) REFERENCES [RII_WT_LINE] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726194449_AddProductionAndSubcontractingTransferModules'
)
BEGIN
    CREATE TABLE [RII_ST_LINE_LINK] (
        [Id] bigint NOT NULL IDENTITY,
        [SubcontractingTransferHeaderLinkId] bigint NOT NULL,
        [WarehouseTransferLineId] bigint NOT NULL,
        [LineRole] nvarchar(30) NOT NULL,
        [SourceIssueLineId] bigint NULL,
        [ExpectedQuantity] decimal(20,6) NOT NULL,
        [ScrapQuantity] decimal(20,6) NOT NULL,
        [RequirementReference] nvarchar(150) NULL,
        [BranchCode] nvarchar(10) NOT NULL DEFAULT N'0',
        [CreatedDate] datetime2 NULL,
        [UpdatedDate] datetime2 NULL,
        [DeletedDate] datetime2 NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [CreatedBy] bigint NULL,
        [UpdatedBy] bigint NULL,
        [DeletedBy] bigint NULL,
        CONSTRAINT [PK_RII_ST_LINE_LINK] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_RII_ST_LINE_LINK_QTY] CHECK ([ExpectedQuantity] > 0 AND [ScrapQuantity] >= 0 AND [ScrapQuantity] <= [ExpectedQuantity]),
        CONSTRAINT [FK_RII_ST_LINE_LINK_RII_ST_HEADER_LINK_SubcontractingTransferHeaderLinkId] FOREIGN KEY ([SubcontractingTransferHeaderLinkId]) REFERENCES [RII_ST_HEADER_LINK] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_RII_ST_LINE_LINK_RII_WT_LINE_SourceIssueLineId] FOREIGN KEY ([SourceIssueLineId]) REFERENCES [RII_WT_LINE] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_RII_ST_LINE_LINK_RII_WT_LINE_WarehouseTransferLineId] FOREIGN KEY ([WarehouseTransferLineId]) REFERENCES [RII_WT_LINE] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726194449_AddProductionAndSubcontractingTransferModules'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'AvailableOnMobile', N'AvailableOnWeb', N'BranchCode', N'Code', N'CreatedBy', N'CreatedDate', N'DeletedBy', N'DeletedDate', N'Description', N'IsActive', N'Name', N'UpdatedBy', N'UpdatedDate') AND [object_id] = OBJECT_ID(N'[RII_PERMISSION_DEFINITIONS]'))
        SET IDENTITY_INSERT [RII_PERMISSION_DEFINITIONS] ON;
    EXEC(N'INSERT INTO [RII_PERMISSION_DEFINITIONS] ([Id], [AvailableOnMobile], [AvailableOnWeb], [BranchCode], [Code], [CreatedBy], [CreatedDate], [DeletedBy], [DeletedDate], [Description], [IsActive], [Name], [UpdatedBy], [UpdatedDate])
    VALUES (CAST(2400 AS bigint), CAST(0 AS bit), CAST(1 AS bit), N''0'', N''WMS.PRODUCTION_TRANSFER.VIEW'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, NULL, CAST(1 AS bit), N''Üretim transferlerini görüntüle'', NULL, NULL),
    (CAST(2401 AS bigint), CAST(0 AS bit), CAST(1 AS bit), N''0'', N''WMS.PRODUCTION_TRANSFER.CREATE'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, NULL, CAST(1 AS bit), N''Üretim transferi oluştur'', NULL, NULL),
    (CAST(2402 AS bigint), CAST(0 AS bit), CAST(1 AS bit), N''0'', N''WMS.PRODUCTION_TRANSFER.OPERATE'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, NULL, CAST(1 AS bit), N''Üretim transfer operasyonunu yürüt'', NULL, NULL),
    (CAST(2403 AS bigint), CAST(0 AS bit), CAST(1 AS bit), N''0'', N''WMS.PRODUCTION_TRANSFER.APPROVE'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, NULL, CAST(1 AS bit), N''Üretim transferini onayla'', NULL, NULL),
    (CAST(2404 AS bigint), CAST(0 AS bit), CAST(1 AS bit), N''0'', N''WMS.PRODUCTION_TRANSFER.CANCEL'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, NULL, CAST(1 AS bit), N''Üretim transferini iptal et'', NULL, NULL),
    (CAST(2405 AS bigint), CAST(0 AS bit), CAST(1 AS bit), N''0'', N''WMS.PRODUCTION_TRANSFER.SETTINGS.VIEW'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, NULL, CAST(1 AS bit), N''Üretim transfer ayarlarını görüntüle'', NULL, NULL),
    (CAST(2406 AS bigint), CAST(0 AS bit), CAST(1 AS bit), N''0'', N''WMS.PRODUCTION_TRANSFER.SETTINGS.MANAGE'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, NULL, CAST(1 AS bit), N''Üretim transfer ayarlarını yönet'', NULL, NULL),
    (CAST(2407 AS bigint), CAST(0 AS bit), CAST(1 AS bit), N''0'', N''WMS.PRODUCTION_TRANSFER.UPDATE'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, NULL, CAST(1 AS bit), N''Üretim transfer taslağını güncelle'', NULL, NULL),
    (CAST(2408 AS bigint), CAST(0 AS bit), CAST(1 AS bit), N''0'', N''WMS.PRODUCTION_TRANSFER.DELETE'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, NULL, CAST(1 AS bit), N''Üretim transfer taslağını sil'', NULL, NULL),
    (CAST(2410 AS bigint), CAST(0 AS bit), CAST(1 AS bit), N''0'', N''WMS.SUBCONTRACTING_TRANSFER.VIEW'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, NULL, CAST(1 AS bit), N''Fason transferlerini görüntüle'', NULL, NULL),
    (CAST(2411 AS bigint), CAST(0 AS bit), CAST(1 AS bit), N''0'', N''WMS.SUBCONTRACTING_TRANSFER.CREATE'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, NULL, CAST(1 AS bit), N''Fason transferi oluştur'', NULL, NULL),
    (CAST(2412 AS bigint), CAST(0 AS bit), CAST(1 AS bit), N''0'', N''WMS.SUBCONTRACTING_TRANSFER.OPERATE'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, NULL, CAST(1 AS bit), N''Fason transfer operasyonunu yürüt'', NULL, NULL),
    (CAST(2413 AS bigint), CAST(0 AS bit), CAST(1 AS bit), N''0'', N''WMS.SUBCONTRACTING_TRANSFER.APPROVE'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, NULL, CAST(1 AS bit), N''Fason transferini onayla'', NULL, NULL),
    (CAST(2414 AS bigint), CAST(0 AS bit), CAST(1 AS bit), N''0'', N''WMS.SUBCONTRACTING_TRANSFER.CANCEL'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, NULL, CAST(1 AS bit), N''Fason transferini iptal et'', NULL, NULL),
    (CAST(2415 AS bigint), CAST(0 AS bit), CAST(1 AS bit), N''0'', N''WMS.SUBCONTRACTING_TRANSFER.SETTINGS.VIEW'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, NULL, CAST(1 AS bit), N''Fason transfer ayarlarını görüntüle'', NULL, NULL),
    (CAST(2416 AS bigint), CAST(0 AS bit), CAST(1 AS bit), N''0'', N''WMS.SUBCONTRACTING_TRANSFER.SETTINGS.MANAGE'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, NULL, CAST(1 AS bit), N''Fason transfer ayarlarını yönet'', NULL, NULL),
    (CAST(2417 AS bigint), CAST(0 AS bit), CAST(1 AS bit), N''0'', N''WMS.SUBCONTRACTING_TRANSFER.UPDATE'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, NULL, CAST(1 AS bit), N''Fason transfer taslağını güncelle'', NULL, NULL),
    (CAST(2418 AS bigint), CAST(0 AS bit), CAST(1 AS bit), N''0'', N''WMS.SUBCONTRACTING_TRANSFER.DELETE'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, NULL, CAST(1 AS bit), N''Fason transfer taslağını sil'', NULL, NULL)');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'AvailableOnMobile', N'AvailableOnWeb', N'BranchCode', N'Code', N'CreatedBy', N'CreatedDate', N'DeletedBy', N'DeletedDate', N'Description', N'IsActive', N'Name', N'UpdatedBy', N'UpdatedDate') AND [object_id] = OBJECT_ID(N'[RII_PERMISSION_DEFINITIONS]'))
        SET IDENTITY_INSERT [RII_PERMISSION_DEFINITIONS] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726194449_AddProductionAndSubcontractingTransferModules'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'BranchCode', N'CreatedBy', N'CreatedDate', N'DeletedBy', N'DeletedDate', N'PermissionDefinitionId', N'PermissionGroupId', N'UpdatedBy', N'UpdatedDate') AND [object_id] = OBJECT_ID(N'[RII_PERMISSION_GROUP_PERMISSIONS]'))
        SET IDENTITY_INSERT [RII_PERMISSION_GROUP_PERMISSIONS] ON;
    EXEC(N'INSERT INTO [RII_PERMISSION_GROUP_PERMISSIONS] ([Id], [BranchCode], [CreatedBy], [CreatedDate], [DeletedBy], [DeletedDate], [PermissionDefinitionId], [PermissionGroupId], [UpdatedBy], [UpdatedDate])
    VALUES (CAST(2400 AS bigint), N''0'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, CAST(2400 AS bigint), CAST(1001 AS bigint), NULL, NULL),
    (CAST(2401 AS bigint), N''0'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, CAST(2401 AS bigint), CAST(1001 AS bigint), NULL, NULL),
    (CAST(2402 AS bigint), N''0'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, CAST(2402 AS bigint), CAST(1001 AS bigint), NULL, NULL),
    (CAST(2403 AS bigint), N''0'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, CAST(2403 AS bigint), CAST(1001 AS bigint), NULL, NULL),
    (CAST(2404 AS bigint), N''0'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, CAST(2404 AS bigint), CAST(1001 AS bigint), NULL, NULL),
    (CAST(2405 AS bigint), N''0'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, CAST(2405 AS bigint), CAST(1001 AS bigint), NULL, NULL),
    (CAST(2406 AS bigint), N''0'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, CAST(2406 AS bigint), CAST(1001 AS bigint), NULL, NULL),
    (CAST(2407 AS bigint), N''0'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, CAST(2407 AS bigint), CAST(1001 AS bigint), NULL, NULL),
    (CAST(2408 AS bigint), N''0'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, CAST(2408 AS bigint), CAST(1001 AS bigint), NULL, NULL),
    (CAST(2410 AS bigint), N''0'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, CAST(2410 AS bigint), CAST(1001 AS bigint), NULL, NULL),
    (CAST(2411 AS bigint), N''0'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, CAST(2411 AS bigint), CAST(1001 AS bigint), NULL, NULL),
    (CAST(2412 AS bigint), N''0'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, CAST(2412 AS bigint), CAST(1001 AS bigint), NULL, NULL),
    (CAST(2413 AS bigint), N''0'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, CAST(2413 AS bigint), CAST(1001 AS bigint), NULL, NULL),
    (CAST(2414 AS bigint), N''0'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, CAST(2414 AS bigint), CAST(1001 AS bigint), NULL, NULL),
    (CAST(2415 AS bigint), N''0'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, CAST(2415 AS bigint), CAST(1001 AS bigint), NULL, NULL),
    (CAST(2416 AS bigint), N''0'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, CAST(2416 AS bigint), CAST(1001 AS bigint), NULL, NULL),
    (CAST(2417 AS bigint), N''0'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, CAST(2417 AS bigint), CAST(1001 AS bigint), NULL, NULL),
    (CAST(2418 AS bigint), N''0'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, CAST(2418 AS bigint), CAST(1001 AS bigint), NULL, NULL)');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'BranchCode', N'CreatedBy', N'CreatedDate', N'DeletedBy', N'DeletedDate', N'PermissionDefinitionId', N'PermissionGroupId', N'UpdatedBy', N'UpdatedDate') AND [object_id] = OBJECT_ID(N'[RII_PERMISSION_GROUP_PERMISSIONS]'))
        SET IDENTITY_INSERT [RII_PERMISSION_GROUP_PERMISSIONS] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726194449_AddProductionAndSubcontractingTransferModules'
)
BEGIN
    CREATE INDEX [IX_RII_PT_HEADER_LINK_BranchCode_ProductionOrderNo_Purpose] ON [RII_PT_HEADER_LINK] ([BranchCode], [ProductionOrderNo], [Purpose]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726194449_AddProductionAndSubcontractingTransferModules'
)
BEGIN
    CREATE INDEX [IX_RII_PT_HEADER_LINK_IsDeleted] ON [RII_PT_HEADER_LINK] ([IsDeleted]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726194449_AddProductionAndSubcontractingTransferModules'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_RII_PT_HEADER_LINK_WarehouseTransferHeaderId] ON [RII_PT_HEADER_LINK] ([WarehouseTransferHeaderId]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726194449_AddProductionAndSubcontractingTransferModules'
)
BEGIN
    CREATE INDEX [IX_RII_PT_LINE_LINK_IsDeleted] ON [RII_PT_LINE_LINK] ([IsDeleted]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726194449_AddProductionAndSubcontractingTransferModules'
)
BEGIN
    CREATE INDEX [IX_RII_PT_LINE_LINK_ProductionTransferHeaderLinkId] ON [RII_PT_LINE_LINK] ([ProductionTransferHeaderLinkId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726194449_AddProductionAndSubcontractingTransferModules'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_RII_PT_LINE_LINK_WarehouseTransferLineId] ON [RII_PT_LINE_LINK] ([WarehouseTransferLineId]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726194449_AddProductionAndSubcontractingTransferModules'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_RII_PT_POLICIES_BranchCode_PolicyKey] ON [RII_PT_POLICIES] ([BranchCode], [PolicyKey]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726194449_AddProductionAndSubcontractingTransferModules'
)
BEGIN
    CREATE INDEX [IX_RII_PT_POLICIES_IsDeleted] ON [RII_PT_POLICIES] ([IsDeleted]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726194449_AddProductionAndSubcontractingTransferModules'
)
BEGIN
    CREATE INDEX [IX_RII_ST_HEADER_LINK_BranchCode_SubcontractOrderNo_Direction] ON [RII_ST_HEADER_LINK] ([BranchCode], [SubcontractOrderNo], [Direction]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726194449_AddProductionAndSubcontractingTransferModules'
)
BEGIN
    CREATE INDEX [IX_RII_ST_HEADER_LINK_BranchCode_SupplierId_Direction_ExpectedReturnAtUtc] ON [RII_ST_HEADER_LINK] ([BranchCode], [SupplierId], [Direction], [ExpectedReturnAtUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726194449_AddProductionAndSubcontractingTransferModules'
)
BEGIN
    CREATE INDEX [IX_RII_ST_HEADER_LINK_IsDeleted] ON [RII_ST_HEADER_LINK] ([IsDeleted]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726194449_AddProductionAndSubcontractingTransferModules'
)
BEGIN
    CREATE INDEX [IX_RII_ST_HEADER_LINK_ParentIssueTransferId] ON [RII_ST_HEADER_LINK] ([ParentIssueTransferId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726194449_AddProductionAndSubcontractingTransferModules'
)
BEGIN
    CREATE INDEX [IX_RII_ST_HEADER_LINK_SupplierId] ON [RII_ST_HEADER_LINK] ([SupplierId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726194449_AddProductionAndSubcontractingTransferModules'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_RII_ST_HEADER_LINK_WarehouseTransferHeaderId] ON [RII_ST_HEADER_LINK] ([WarehouseTransferHeaderId]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726194449_AddProductionAndSubcontractingTransferModules'
)
BEGIN
    CREATE INDEX [IX_RII_ST_LINE_LINK_IsDeleted] ON [RII_ST_LINE_LINK] ([IsDeleted]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726194449_AddProductionAndSubcontractingTransferModules'
)
BEGIN
    CREATE INDEX [IX_RII_ST_LINE_LINK_SourceIssueLineId] ON [RII_ST_LINE_LINK] ([SourceIssueLineId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726194449_AddProductionAndSubcontractingTransferModules'
)
BEGIN
    CREATE INDEX [IX_RII_ST_LINE_LINK_SubcontractingTransferHeaderLinkId] ON [RII_ST_LINE_LINK] ([SubcontractingTransferHeaderLinkId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726194449_AddProductionAndSubcontractingTransferModules'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_RII_ST_LINE_LINK_WarehouseTransferLineId] ON [RII_ST_LINE_LINK] ([WarehouseTransferLineId]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726194449_AddProductionAndSubcontractingTransferModules'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_RII_ST_POLICIES_BranchCode_PolicyKey] ON [RII_ST_POLICIES] ([BranchCode], [PolicyKey]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726194449_AddProductionAndSubcontractingTransferModules'
)
BEGIN
    CREATE INDEX [IX_RII_ST_POLICIES_IsDeleted] ON [RII_ST_POLICIES] ([IsDeleted]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726194449_AddProductionAndSubcontractingTransferModules'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260726194449_AddProductionAndSubcontractingTransferModules', N'10.0.10');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726202341_AddProductionPlanningFoundation'
)
BEGIN
    CREATE TABLE [RII_PR_HEADER] (
        [Id] bigint NOT NULL IDENTITY,
        [DocumentSeriesId] bigint NOT NULL,
        [DocumentNo] nvarchar(50) NOT NULL,
        [DocumentDate] date NOT NULL,
        [CorrelationId] uniqueidentifier NOT NULL,
        [PlanType] nvarchar(30) NOT NULL,
        [ExecutionMode] nvarchar(20) NOT NULL,
        [Status] nvarchar(30) NOT NULL,
        [Priority] tinyint NOT NULL,
        [CustomerId] bigint NULL,
        [CustomerCodeSnapshot] nvarchar(100) NULL,
        [CustomerNameSnapshot] nvarchar(300) NULL,
        [PlannedStartAtUtc] datetimeoffset NULL,
        [PlannedEndAtUtc] datetimeoffset NULL,
        [ActualStartAtUtc] datetimeoffset NULL,
        [ActualEndAtUtc] datetimeoffset NULL,
        [ReleasedAtUtc] datetimeoffset NULL,
        [ReleasedBy] bigint NULL,
        [Description] nvarchar(2000) NULL,
        [RowVersion] rowversion NOT NULL,
        [BranchCode] nvarchar(10) NOT NULL DEFAULT N'0',
        [CreatedDate] datetime2 NULL,
        [UpdatedDate] datetime2 NULL,
        [DeletedDate] datetime2 NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [CreatedBy] bigint NULL,
        [UpdatedBy] bigint NULL,
        [DeletedBy] bigint NULL,
        CONSTRAINT [PK_RII_PR_HEADER] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726202341_AddProductionPlanningFoundation'
)
BEGIN
    CREATE TABLE [RII_PR_ORDER] (
        [Id] bigint NOT NULL IDENTITY,
        [ProductionHeaderId] bigint NOT NULL,
        [LineNo] int NOT NULL,
        [OrderNo] nvarchar(70) NOT NULL,
        [ExternalOrderNo] nvarchar(100) NULL,
        [Status] nvarchar(30) NOT NULL,
        [SequenceNo] int NOT NULL,
        [ParallelGroupNo] int NULL,
        [BomReference] nvarchar(100) NULL,
        [RoutingReference] nvarchar(100) NULL,
        [WorkCenterCode] nvarchar(100) NULL,
        [ProducedStockId] bigint NOT NULL,
        [ProducedStockCodeSnapshot] nvarchar(100) NOT NULL,
        [ProducedStockNameSnapshot] nvarchar(300) NULL,
        [ProducedYapCodeId] bigint NULL,
        [ProducedYapCodeSnapshot] nvarchar(100) NULL,
        [UnitCode] nvarchar(20) NOT NULL,
        [PlannedQuantity] decimal(20,6) NOT NULL,
        [CompletedQuantity] decimal(20,6) NOT NULL,
        [ScrapQuantity] decimal(20,6) NOT NULL,
        [SourceWarehouseId] bigint NOT NULL,
        [TargetWarehouseId] bigint NOT NULL,
        [RequireMaterialTransferBeforeStart] bit NOT NULL,
        [PlannedStartAtUtc] datetimeoffset NULL,
        [PlannedEndAtUtc] datetimeoffset NULL,
        [ActualStartAtUtc] datetimeoffset NULL,
        [ActualEndAtUtc] datetimeoffset NULL,
        [BlockedReason] nvarchar(max) NULL,
        [Description] nvarchar(1000) NULL,
        [RowVersion] rowversion NOT NULL,
        [BranchCode] nvarchar(10) NOT NULL DEFAULT N'0',
        [CreatedDate] datetime2 NULL,
        [UpdatedDate] datetime2 NULL,
        [DeletedDate] datetime2 NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [CreatedBy] bigint NULL,
        [UpdatedBy] bigint NULL,
        [DeletedBy] bigint NULL,
        CONSTRAINT [PK_RII_PR_ORDER] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_RII_PR_ORDER_QTY] CHECK ([PlannedQuantity] > 0 AND [CompletedQuantity] >= 0 AND [ScrapQuantity] >= 0),
        CONSTRAINT [FK_RII_PR_ORDER_RII_PR_HEADER_ProductionHeaderId] FOREIGN KEY ([ProductionHeaderId]) REFERENCES [RII_PR_HEADER] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726202341_AddProductionPlanningFoundation'
)
BEGIN
    CREATE TABLE [RII_PR_ASSIGNMENT] (
        [Id] bigint NOT NULL IDENTITY,
        [ProductionOrderId] bigint NOT NULL,
        [UserId] bigint NOT NULL,
        [IsPrimary] bit NOT NULL,
        [AssignedAtUtc] datetimeoffset NOT NULL,
        [AssignedBy] bigint NOT NULL,
        [AcceptedAtUtc] datetimeoffset NULL,
        [CompletedAtUtc] datetimeoffset NULL,
        [Note] nvarchar(500) NULL,
        [BranchCode] nvarchar(10) NOT NULL DEFAULT N'0',
        [CreatedDate] datetime2 NULL,
        [UpdatedDate] datetime2 NULL,
        [DeletedDate] datetime2 NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [CreatedBy] bigint NULL,
        [UpdatedBy] bigint NULL,
        [DeletedBy] bigint NULL,
        CONSTRAINT [PK_RII_PR_ASSIGNMENT] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_RII_PR_ASSIGNMENT_RII_PR_ORDER_ProductionOrderId] FOREIGN KEY ([ProductionOrderId]) REFERENCES [RII_PR_ORDER] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726202341_AddProductionPlanningFoundation'
)
BEGIN
    CREATE TABLE [RII_PR_DEPENDENCY] (
        [Id] bigint NOT NULL IDENTITY,
        [ProductionHeaderId] bigint NOT NULL,
        [PredecessorOrderId] bigint NOT NULL,
        [SuccessorOrderId] bigint NOT NULL,
        [DependencyType] nvarchar(30) NOT NULL,
        [LagMinutes] int NOT NULL,
        [RequireOutputAvailable] bit NOT NULL,
        [RequireTransferCompleted] bit NOT NULL,
        [BranchCode] nvarchar(10) NOT NULL DEFAULT N'0',
        [CreatedDate] datetime2 NULL,
        [UpdatedDate] datetime2 NULL,
        [DeletedDate] datetime2 NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [CreatedBy] bigint NULL,
        [UpdatedBy] bigint NULL,
        [DeletedBy] bigint NULL,
        CONSTRAINT [PK_RII_PR_DEPENDENCY] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_RII_PR_DEPENDENCY_SELF] CHECK ([PredecessorOrderId] <> [SuccessorOrderId]),
        CONSTRAINT [FK_RII_PR_DEPENDENCY_RII_PR_HEADER_ProductionHeaderId] FOREIGN KEY ([ProductionHeaderId]) REFERENCES [RII_PR_HEADER] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_RII_PR_DEPENDENCY_RII_PR_ORDER_PredecessorOrderId] FOREIGN KEY ([PredecessorOrderId]) REFERENCES [RII_PR_ORDER] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_RII_PR_DEPENDENCY_RII_PR_ORDER_SuccessorOrderId] FOREIGN KEY ([SuccessorOrderId]) REFERENCES [RII_PR_ORDER] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726202341_AddProductionPlanningFoundation'
)
BEGIN
    CREATE TABLE [RII_PR_MATERIAL] (
        [Id] bigint NOT NULL IDENTITY,
        [ProductionOrderId] bigint NOT NULL,
        [LineNo] int NOT NULL,
        [StockId] bigint NOT NULL,
        [StockCodeSnapshot] nvarchar(100) NOT NULL,
        [StockNameSnapshot] nvarchar(300) NULL,
        [YapCodeId] bigint NULL,
        [YapCodeSnapshot] nvarchar(100) NULL,
        [UnitCode] nvarchar(20) NOT NULL,
        [RequiredQuantity] decimal(20,6) NOT NULL,
        [IssuedQuantity] decimal(20,6) NOT NULL,
        [ConsumedQuantity] decimal(20,6) NOT NULL,
        [IssueMode] nvarchar(30) NOT NULL,
        [IsMandatory] bit NOT NULL,
        [SourceWarehouseId] bigint NOT NULL,
        [PreferredSourceLocationId] bigint NULL,
        [TrackingType] nvarchar(30) NOT NULL,
        [BranchCode] nvarchar(10) NOT NULL DEFAULT N'0',
        [CreatedDate] datetime2 NULL,
        [UpdatedDate] datetime2 NULL,
        [DeletedDate] datetime2 NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [CreatedBy] bigint NULL,
        [UpdatedBy] bigint NULL,
        [DeletedBy] bigint NULL,
        CONSTRAINT [PK_RII_PR_MATERIAL] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_RII_PR_MATERIAL_QTY] CHECK ([RequiredQuantity] > 0 AND [IssuedQuantity] >= 0 AND [ConsumedQuantity] >= 0),
        CONSTRAINT [FK_RII_PR_MATERIAL_RII_PR_ORDER_ProductionOrderId] FOREIGN KEY ([ProductionOrderId]) REFERENCES [RII_PR_ORDER] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726202341_AddProductionPlanningFoundation'
)
BEGIN
    CREATE TABLE [RII_PR_OUTPUT] (
        [Id] bigint NOT NULL IDENTITY,
        [ProductionOrderId] bigint NOT NULL,
        [LineNo] int NOT NULL,
        [StockId] bigint NOT NULL,
        [StockCodeSnapshot] nvarchar(100) NOT NULL,
        [StockNameSnapshot] nvarchar(300) NULL,
        [YapCodeId] bigint NULL,
        [YapCodeSnapshot] nvarchar(100) NULL,
        [UnitCode] nvarchar(20) NOT NULL,
        [PlannedQuantity] decimal(20,6) NOT NULL,
        [ProducedQuantity] decimal(20,6) NOT NULL,
        [ScrapQuantity] decimal(20,6) NOT NULL,
        [TargetWarehouseId] bigint NOT NULL,
        [PreferredTargetLocationId] bigint NULL,
        [TrackingType] nvarchar(30) NOT NULL,
        [IsPrimary] bit NOT NULL,
        [BranchCode] nvarchar(10) NOT NULL DEFAULT N'0',
        [CreatedDate] datetime2 NULL,
        [UpdatedDate] datetime2 NULL,
        [DeletedDate] datetime2 NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [CreatedBy] bigint NULL,
        [UpdatedBy] bigint NULL,
        [DeletedBy] bigint NULL,
        CONSTRAINT [PK_RII_PR_OUTPUT] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_RII_PR_OUTPUT_QTY] CHECK ([PlannedQuantity] > 0 AND [ProducedQuantity] >= 0 AND [ScrapQuantity] >= 0),
        CONSTRAINT [FK_RII_PR_OUTPUT_RII_PR_ORDER_ProductionOrderId] FOREIGN KEY ([ProductionOrderId]) REFERENCES [RII_PR_ORDER] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726202341_AddProductionPlanningFoundation'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'AvailableOnMobile', N'AvailableOnWeb', N'BranchCode', N'Code', N'CreatedBy', N'CreatedDate', N'DeletedBy', N'DeletedDate', N'Description', N'IsActive', N'Name', N'UpdatedBy', N'UpdatedDate') AND [object_id] = OBJECT_ID(N'[RII_PERMISSION_DEFINITIONS]'))
        SET IDENTITY_INSERT [RII_PERMISSION_DEFINITIONS] ON;
    EXEC(N'INSERT INTO [RII_PERMISSION_DEFINITIONS] ([Id], [AvailableOnMobile], [AvailableOnWeb], [BranchCode], [Code], [CreatedBy], [CreatedDate], [DeletedBy], [DeletedDate], [Description], [IsActive], [Name], [UpdatedBy], [UpdatedDate])
    VALUES (CAST(2420 AS bigint), CAST(0 AS bit), CAST(1 AS bit), N''0'', N''WMS.PRODUCTION.VIEW'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, NULL, CAST(1 AS bit), N''Üretim plan ve emirlerini görüntüle'', NULL, NULL),
    (CAST(2421 AS bigint), CAST(0 AS bit), CAST(1 AS bit), N''0'', N''WMS.PRODUCTION.CREATE'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, NULL, CAST(1 AS bit), N''Üretim planı ve emri oluştur'', NULL, NULL),
    (CAST(2422 AS bigint), CAST(0 AS bit), CAST(1 AS bit), N''0'', N''WMS.PRODUCTION.RELEASE'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, NULL, CAST(1 AS bit), N''Üretim planını serbest bırak'', NULL, NULL),
    (CAST(2423 AS bigint), CAST(0 AS bit), CAST(1 AS bit), N''0'', N''WMS.PRODUCTION.DELETE'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, NULL, CAST(1 AS bit), N''Taslak üretim planını sil'', NULL, NULL),
    (CAST(2424 AS bigint), CAST(0 AS bit), CAST(1 AS bit), N''0'', N''WMS.PRODUCTION.OPERATE'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, NULL, CAST(1 AS bit), N''Üretim operasyonunu yürüt'', NULL, NULL)');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'AvailableOnMobile', N'AvailableOnWeb', N'BranchCode', N'Code', N'CreatedBy', N'CreatedDate', N'DeletedBy', N'DeletedDate', N'Description', N'IsActive', N'Name', N'UpdatedBy', N'UpdatedDate') AND [object_id] = OBJECT_ID(N'[RII_PERMISSION_DEFINITIONS]'))
        SET IDENTITY_INSERT [RII_PERMISSION_DEFINITIONS] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726202341_AddProductionPlanningFoundation'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'BranchCode', N'CreatedBy', N'CreatedDate', N'DeletedBy', N'DeletedDate', N'PermissionDefinitionId', N'PermissionGroupId', N'UpdatedBy', N'UpdatedDate') AND [object_id] = OBJECT_ID(N'[RII_PERMISSION_GROUP_PERMISSIONS]'))
        SET IDENTITY_INSERT [RII_PERMISSION_GROUP_PERMISSIONS] ON;
    EXEC(N'INSERT INTO [RII_PERMISSION_GROUP_PERMISSIONS] ([Id], [BranchCode], [CreatedBy], [CreatedDate], [DeletedBy], [DeletedDate], [PermissionDefinitionId], [PermissionGroupId], [UpdatedBy], [UpdatedDate])
    VALUES (CAST(2420 AS bigint), N''0'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, CAST(2420 AS bigint), CAST(1001 AS bigint), NULL, NULL),
    (CAST(2421 AS bigint), N''0'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, CAST(2421 AS bigint), CAST(1001 AS bigint), NULL, NULL),
    (CAST(2422 AS bigint), N''0'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, CAST(2422 AS bigint), CAST(1001 AS bigint), NULL, NULL),
    (CAST(2423 AS bigint), N''0'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, CAST(2423 AS bigint), CAST(1001 AS bigint), NULL, NULL),
    (CAST(2424 AS bigint), N''0'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, CAST(2424 AS bigint), CAST(1001 AS bigint), NULL, NULL)');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'BranchCode', N'CreatedBy', N'CreatedDate', N'DeletedBy', N'DeletedDate', N'PermissionDefinitionId', N'PermissionGroupId', N'UpdatedBy', N'UpdatedDate') AND [object_id] = OBJECT_ID(N'[RII_PERMISSION_GROUP_PERMISSIONS]'))
        SET IDENTITY_INSERT [RII_PERMISSION_GROUP_PERMISSIONS] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726202341_AddProductionPlanningFoundation'
)
BEGIN
    CREATE INDEX [IX_RII_PT_LINE_LINK_ProductionConsumptionId] ON [RII_PT_LINE_LINK] ([ProductionConsumptionId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726202341_AddProductionPlanningFoundation'
)
BEGIN
    CREATE INDEX [IX_RII_PT_LINE_LINK_ProductionOutputId] ON [RII_PT_LINE_LINK] ([ProductionOutputId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726202341_AddProductionPlanningFoundation'
)
BEGIN
    CREATE INDEX [IX_RII_PT_HEADER_LINK_ProductionHeaderId] ON [RII_PT_HEADER_LINK] ([ProductionHeaderId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726202341_AddProductionPlanningFoundation'
)
BEGIN
    CREATE INDEX [IX_RII_PT_HEADER_LINK_ProductionOrderId] ON [RII_PT_HEADER_LINK] ([ProductionOrderId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726202341_AddProductionPlanningFoundation'
)
BEGIN
    CREATE INDEX [IX_RII_PR_ASSIGNMENT_IsDeleted] ON [RII_PR_ASSIGNMENT] ([IsDeleted]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726202341_AddProductionPlanningFoundation'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_RII_PR_ASSIGNMENT_ProductionOrderId_UserId] ON [RII_PR_ASSIGNMENT] ([ProductionOrderId], [UserId]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726202341_AddProductionPlanningFoundation'
)
BEGIN
    CREATE INDEX [IX_RII_PR_ASSIGNMENT_UserId_AcceptedAtUtc_CompletedAtUtc] ON [RII_PR_ASSIGNMENT] ([UserId], [AcceptedAtUtc], [CompletedAtUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726202341_AddProductionPlanningFoundation'
)
BEGIN
    CREATE INDEX [IX_RII_PR_DEPENDENCY_IsDeleted] ON [RII_PR_DEPENDENCY] ([IsDeleted]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726202341_AddProductionPlanningFoundation'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_RII_PR_DEPENDENCY_PredecessorOrderId_SuccessorOrderId] ON [RII_PR_DEPENDENCY] ([PredecessorOrderId], [SuccessorOrderId]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726202341_AddProductionPlanningFoundation'
)
BEGIN
    CREATE INDEX [IX_RII_PR_DEPENDENCY_ProductionHeaderId] ON [RII_PR_DEPENDENCY] ([ProductionHeaderId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726202341_AddProductionPlanningFoundation'
)
BEGIN
    CREATE INDEX [IX_RII_PR_DEPENDENCY_SuccessorOrderId] ON [RII_PR_DEPENDENCY] ([SuccessorOrderId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726202341_AddProductionPlanningFoundation'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_RII_PR_HEADER_BranchCode_DocumentNo] ON [RII_PR_HEADER] ([BranchCode], [DocumentNo]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726202341_AddProductionPlanningFoundation'
)
BEGIN
    CREATE INDEX [IX_RII_PR_HEADER_BranchCode_Status_PlannedStartAtUtc] ON [RII_PR_HEADER] ([BranchCode], [Status], [PlannedStartAtUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726202341_AddProductionPlanningFoundation'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_RII_PR_HEADER_CorrelationId] ON [RII_PR_HEADER] ([CorrelationId]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726202341_AddProductionPlanningFoundation'
)
BEGIN
    CREATE INDEX [IX_RII_PR_HEADER_IsDeleted] ON [RII_PR_HEADER] ([IsDeleted]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726202341_AddProductionPlanningFoundation'
)
BEGIN
    CREATE INDEX [IX_RII_PR_MATERIAL_IsDeleted] ON [RII_PR_MATERIAL] ([IsDeleted]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726202341_AddProductionPlanningFoundation'
)
BEGIN
    CREATE UNIQUE INDEX [IX_RII_PR_MATERIAL_ProductionOrderId_LineNo] ON [RII_PR_MATERIAL] ([ProductionOrderId], [LineNo]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726202341_AddProductionPlanningFoundation'
)
BEGIN
    CREATE INDEX [IX_RII_PR_MATERIAL_StockId_SourceWarehouseId] ON [RII_PR_MATERIAL] ([StockId], [SourceWarehouseId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726202341_AddProductionPlanningFoundation'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_RII_PR_ORDER_BranchCode_OrderNo] ON [RII_PR_ORDER] ([BranchCode], [OrderNo]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726202341_AddProductionPlanningFoundation'
)
BEGIN
    CREATE INDEX [IX_RII_PR_ORDER_IsDeleted] ON [RII_PR_ORDER] ([IsDeleted]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726202341_AddProductionPlanningFoundation'
)
BEGIN
    CREATE INDEX [IX_RII_PR_ORDER_ProducedStockId_Status] ON [RII_PR_ORDER] ([ProducedStockId], [Status]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726202341_AddProductionPlanningFoundation'
)
BEGIN
    CREATE UNIQUE INDEX [IX_RII_PR_ORDER_ProductionHeaderId_LineNo] ON [RII_PR_ORDER] ([ProductionHeaderId], [LineNo]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726202341_AddProductionPlanningFoundation'
)
BEGIN
    CREATE INDEX [IX_RII_PR_OUTPUT_IsDeleted] ON [RII_PR_OUTPUT] ([IsDeleted]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726202341_AddProductionPlanningFoundation'
)
BEGIN
    CREATE UNIQUE INDEX [IX_RII_PR_OUTPUT_ProductionOrderId_LineNo] ON [RII_PR_OUTPUT] ([ProductionOrderId], [LineNo]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726202341_AddProductionPlanningFoundation'
)
BEGIN
    CREATE INDEX [IX_RII_PR_OUTPUT_StockId_TargetWarehouseId] ON [RII_PR_OUTPUT] ([StockId], [TargetWarehouseId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726202341_AddProductionPlanningFoundation'
)
BEGIN
    ALTER TABLE [RII_PT_HEADER_LINK] ADD CONSTRAINT [FK_RII_PT_HEADER_LINK_RII_PR_HEADER_ProductionHeaderId] FOREIGN KEY ([ProductionHeaderId]) REFERENCES [RII_PR_HEADER] ([Id]) ON DELETE NO ACTION;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726202341_AddProductionPlanningFoundation'
)
BEGIN
    ALTER TABLE [RII_PT_HEADER_LINK] ADD CONSTRAINT [FK_RII_PT_HEADER_LINK_RII_PR_ORDER_ProductionOrderId] FOREIGN KEY ([ProductionOrderId]) REFERENCES [RII_PR_ORDER] ([Id]) ON DELETE NO ACTION;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726202341_AddProductionPlanningFoundation'
)
BEGIN
    ALTER TABLE [RII_PT_LINE_LINK] ADD CONSTRAINT [FK_RII_PT_LINE_LINK_RII_PR_MATERIAL_ProductionConsumptionId] FOREIGN KEY ([ProductionConsumptionId]) REFERENCES [RII_PR_MATERIAL] ([Id]) ON DELETE NO ACTION;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726202341_AddProductionPlanningFoundation'
)
BEGIN
    ALTER TABLE [RII_PT_LINE_LINK] ADD CONSTRAINT [FK_RII_PT_LINE_LINK_RII_PR_OUTPUT_ProductionOutputId] FOREIGN KEY ([ProductionOutputId]) REFERENCES [RII_PR_OUTPUT] ([Id]) ON DELETE NO ACTION;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726202341_AddProductionPlanningFoundation'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260726202341_AddProductionPlanningFoundation', N'10.0.10');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260728092619_ConfigurablePasswordPolicy'
)
BEGIN
    ALTER TABLE [RII_USERS] ADD [PasswordLength] int NOT NULL DEFAULT 15;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260728092619_ConfigurablePasswordPolicy'
)
BEGIN
    ALTER TABLE [RII_PROJECT_SETTINGS] ADD [PasswordMinimumLength] int NOT NULL DEFAULT 6;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260728092619_ConfigurablePasswordPolicy'
)
BEGIN
    EXEC(N'UPDATE [RII_PROJECT_SETTINGS] SET [PasswordMinimumLength] = 6
    WHERE [Id] = CAST(1 AS bigint);
    SELECT @@ROWCOUNT');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260728092619_ConfigurablePasswordPolicy'
)
BEGIN
    EXEC(N'UPDATE [RII_USERS] SET [PasswordLength] = 15
    WHERE [Id] = CAST(1 AS bigint);
    SELECT @@ROWCOUNT');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260728092619_ConfigurablePasswordPolicy'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260728092619_ConfigurablePasswordPolicy', N'10.0.10');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260728175041_AddUserWarehouseAssignments'
)
BEGIN
    CREATE TABLE [RII_USER_WAREHOUSE] (
        [Id] bigint NOT NULL IDENTITY,
        [UserId] bigint NOT NULL,
        [WarehouseId] bigint NOT NULL,
        [BranchCode] nvarchar(20) NOT NULL,
        [CreatedDate] datetime2 NULL,
        [UpdatedDate] datetime2 NULL,
        [DeletedDate] datetime2 NULL,
        [IsDeleted] bit NOT NULL,
        [CreatedBy] bigint NULL,
        [UpdatedBy] bigint NULL,
        [DeletedBy] bigint NULL,
        CONSTRAINT [PK_RII_USER_WAREHOUSE] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_RII_USER_WAREHOUSE_RII_USERS_UserId] FOREIGN KEY ([UserId]) REFERENCES [RII_USERS] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_RII_USER_WAREHOUSE_RII_WAREHOUSE_WarehouseId] FOREIGN KEY ([WarehouseId]) REFERENCES [RII_WAREHOUSE] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260728175041_AddUserWarehouseAssignments'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_RII_USER_WAREHOUSE_UserId_WarehouseId] ON [RII_USER_WAREHOUSE] ([UserId], [WarehouseId]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260728175041_AddUserWarehouseAssignments'
)
BEGIN
    CREATE INDEX [IX_RII_USER_WAREHOUSE_WarehouseId] ON [RII_USER_WAREHOUSE] ([WarehouseId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260728175041_AddUserWarehouseAssignments'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260728175041_AddUserWarehouseAssignments', N'10.0.10');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260728194607_ShowFullyAllocatedGoodsReceiptOpenOrders'
)
BEGIN
    DECLARE @definition nvarchar(max) = OBJECT_DEFINITION(OBJECT_ID(N'dbo.RII_FN_GR_OPENORDERS_LINE'));
    DECLARE @availabilityFilter nvarchar(300) =
        N'WHERE X.RemainingHamax - ISNULL(A.PlannedQtyAllocated, 0) > 0;';

    IF @definition IS NULL
    BEGIN
        ;THROW 50001, 'RII_FN_GR_OPENORDERS_LINE not found.', 1;
    END;
    IF CHARINDEX(@availabilityFilter, @definition) = 0
    BEGIN
        ;THROW 50002, 'RII_FN_GR_OPENORDERS_LINE availability filter not found.', 1;
    END;

    SET @definition =
        N'ALTER ' + SUBSTRING(@definition, CHARINDEX(N'FUNCTION', UPPER(@definition)), LEN(@definition));
    SET @definition = REPLACE(@definition, @availabilityFilter, N';');
    EXEC sys.sp_executesql @definition;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260728194607_ShowFullyAllocatedGoodsReceiptOpenOrders'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260728194607_ShowFullyAllocatedGoodsReceiptOpenOrders', N'10.0.10');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260728205204_MakeDocumentSeriesOperationScopedAndNetsisCompatible'
)
BEGIN
    ALTER TABLE [RII_DOCUMENT_SERIES] DROP CONSTRAINT [FK_RII_DOCUMENT_SERIES_RII_WAREHOUSE_WarehouseId];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260728205204_MakeDocumentSeriesOperationScopedAndNetsisCompatible'
)
BEGIN
    DROP INDEX [IX_RII_DOCUMENT_SERIES_RESOLUTION] ON [RII_DOCUMENT_SERIES];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260728205204_MakeDocumentSeriesOperationScopedAndNetsisCompatible'
)
BEGIN
    DROP INDEX [IX_RII_DOCUMENT_SERIES_WarehouseId] ON [RII_DOCUMENT_SERIES];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260728205204_MakeDocumentSeriesOperationScopedAndNetsisCompatible'
)
BEGIN
    DROP INDEX [UX_RII_DOCUMENT_SERIES_DEFAULT_SCOPE] ON [RII_DOCUMENT_SERIES];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260728205204_MakeDocumentSeriesOperationScopedAndNetsisCompatible'
)
BEGIN
    ALTER TABLE [RII_DOCUMENT_SERIES] DROP CONSTRAINT [CK_RII_DOCUMENT_SERIES_NUMBER_LENGTH];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260728205204_MakeDocumentSeriesOperationScopedAndNetsisCompatible'
)
BEGIN
    EXEC sys.sp_executesql N';WITH [SeriesShape] AS
    (
        SELECT
            [Id],
            CASE [YearFormat]
                WHEN N''TwoDigit'' THEN 2
                WHEN N''FourDigit'' THEN 4
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
                    WHEN N''TwoDigit'' THEN 2
                    WHEN N''FourDigit'' THEN 4
                    ELSE 0
                END > 15
           OR LEN(CONVERT(varchar(20), [StartNumber])) > [NumberLength]
           OR LEN(CONVERT(varchar(20), [NextNumber])) > [NumberLength]
    )
    BEGIN
        ;THROW 51000, ''Document series data is not compatible with the 15-character Netsis document number limit. Correct the affected series before running this migration.'', 1;
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
    WHERE [ranked].[DefaultRank] > 1;';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260728205204_MakeDocumentSeriesOperationScopedAndNetsisCompatible'
)
BEGIN
    DECLARE @var12 nvarchar(max);
    SELECT @var12 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[RII_DOCUMENT_SERIES]') AND [c].[name] = N'Separator');
    IF @var12 IS NOT NULL EXEC(N'ALTER TABLE [RII_DOCUMENT_SERIES] DROP CONSTRAINT ' + @var12 + ';');
    ALTER TABLE [RII_DOCUMENT_SERIES] DROP COLUMN [Separator];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260728205204_MakeDocumentSeriesOperationScopedAndNetsisCompatible'
)
BEGIN
    DECLARE @var13 nvarchar(max);
    SELECT @var13 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[RII_DOCUMENT_SERIES]') AND [c].[name] = N'WarehouseId');
    IF @var13 IS NOT NULL EXEC(N'ALTER TABLE [RII_DOCUMENT_SERIES] DROP CONSTRAINT ' + @var13 + ';');
    ALTER TABLE [RII_DOCUMENT_SERIES] DROP COLUMN [WarehouseId];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260728205204_MakeDocumentSeriesOperationScopedAndNetsisCompatible'
)
BEGIN
    EXEC sys.sp_executesql N'CREATE INDEX [IX_RII_DOCUMENT_SERIES_RESOLUTION] ON [RII_DOCUMENT_SERIES] ([BranchCode], [DocumentType], [IsActive]);';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260728205204_MakeDocumentSeriesOperationScopedAndNetsisCompatible'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_RII_DOCUMENT_SERIES_DEFAULT_SCOPE] ON [RII_DOCUMENT_SERIES] ([BranchCode], [DocumentType]) WHERE [IsDefault] = 1 AND [IsActive] = 1 AND [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260728205204_MakeDocumentSeriesOperationScopedAndNetsisCompatible'
)
BEGIN
    EXEC(N'ALTER TABLE [RII_DOCUMENT_SERIES] ADD CONSTRAINT [CK_RII_DOCUMENT_SERIES_COUNTER_LENGTH] CHECK (LEN(CONVERT(varchar(20), [StartNumber])) <= [NumberLength] AND LEN(CONVERT(varchar(20), [NextNumber])) <= [NumberLength])');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260728205204_MakeDocumentSeriesOperationScopedAndNetsisCompatible'
)
BEGIN
    EXEC(N'ALTER TABLE [RII_DOCUMENT_SERIES] ADD CONSTRAINT [CK_RII_DOCUMENT_SERIES_DOCUMENT_NO_LENGTH] CHECK (LEN([Prefix]) + [NumberLength] + CASE [YearFormat] WHEN N''TwoDigit'' THEN 2 WHEN N''FourDigit'' THEN 4 ELSE 0 END <= 15)');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260728205204_MakeDocumentSeriesOperationScopedAndNetsisCompatible'
)
BEGIN
    EXEC(N'ALTER TABLE [RII_DOCUMENT_SERIES] ADD CONSTRAINT [CK_RII_DOCUMENT_SERIES_NUMBER_LENGTH] CHECK ([NumberLength] BETWEEN 3 AND 15)');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260728205204_MakeDocumentSeriesOperationScopedAndNetsisCompatible'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260728205204_MakeDocumentSeriesOperationScopedAndNetsisCompatible', N'10.0.10');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260728225841_AddWarehouseTransferStockStatuses'
)
BEGIN
    ALTER TABLE [RII_WT_LINE] ADD [SourceStockStatus] nvarchar(40) NOT NULL DEFAULT N'Available';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260728225841_AddWarehouseTransferStockStatuses'
)
BEGIN
    ALTER TABLE [RII_WT_LINE] ADD [TargetStockStatus] nvarchar(40) NOT NULL DEFAULT N'Available';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260728225841_AddWarehouseTransferStockStatuses'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260728225841_AddWarehouseTransferStockStatuses', N'10.0.10');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260729123000_ShowAllocatedOpenOrderLinesGoodsReceiptPolicy'
)
BEGIN
    ALTER TABLE [RII_GR_POLICIES] ADD [ShowAllocatedOpenOrderLines] bit NOT NULL DEFAULT CAST(0 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260729123000_ShowAllocatedOpenOrderLinesGoodsReceiptPolicy'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260729123000_ShowAllocatedOpenOrderLinesGoodsReceiptPolicy', N'10.0.10');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260729210236_AddGoodsReceiptLocationSelectionPolicy'
)
BEGIN
    ALTER TABLE [RII_GR_POLICIES] ADD [LocationSelectionPolicy] nvarchar(50) NOT NULL DEFAULT N'ReceivingOrStagingOnly';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260729210236_AddGoodsReceiptLocationSelectionPolicy'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260729210236_AddGoodsReceiptLocationSelectionPolicy', N'10.0.10');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260730075330_AddSupplierStockMappings'
)
BEGIN
    CREATE TABLE [RII_SUPPLIER_STOCK_MAPPING] (
        [Id] bigint NOT NULL IDENTITY,
        [SupplierId] bigint NOT NULL,
        [SupplierStockCode] nvarchar(100) NOT NULL,
        [NormalizedSupplierStockCode] nvarchar(100) NOT NULL,
        [SupplierStockName] nvarchar(500) NULL,
        [SupplierUnitCode] nvarchar(20) NULL,
        [StockId] bigint NOT NULL,
        [ConversionFactor] decimal(28,8) NOT NULL DEFAULT 1.0,
        [IsActive] bit NOT NULL,
        [Notes] nvarchar(1000) NULL,
        [RowVersion] rowversion NOT NULL,
        [BranchCode] nvarchar(10) NOT NULL DEFAULT N'0',
        [CreatedDate] datetime2 NULL,
        [UpdatedDate] datetime2 NULL,
        [DeletedDate] datetime2 NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [CreatedBy] bigint NULL,
        [UpdatedBy] bigint NULL,
        [DeletedBy] bigint NULL,
        CONSTRAINT [PK_RII_SUPPLIER_STOCK_MAPPING] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_RII_SUPPLIER_STOCK_MAPPING_RII_CUSTOMER_SupplierId] FOREIGN KEY ([SupplierId]) REFERENCES [RII_CUSTOMER] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_RII_SUPPLIER_STOCK_MAPPING_RII_STOCK_StockId] FOREIGN KEY ([StockId]) REFERENCES [RII_STOCK] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260730075330_AddSupplierStockMappings'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'AvailableOnMobile', N'AvailableOnWeb', N'BranchCode', N'Code', N'CreatedBy', N'CreatedDate', N'DeletedBy', N'DeletedDate', N'Description', N'IsActive', N'Name', N'UpdatedBy', N'UpdatedDate') AND [object_id] = OBJECT_ID(N'[RII_PERMISSION_DEFINITIONS]'))
        SET IDENTITY_INSERT [RII_PERMISSION_DEFINITIONS] ON;
    EXEC(N'INSERT INTO [RII_PERMISSION_DEFINITIONS] ([Id], [AvailableOnMobile], [AvailableOnWeb], [BranchCode], [Code], [CreatedBy], [CreatedDate], [DeletedBy], [DeletedDate], [Description], [IsActive], [Name], [UpdatedBy], [UpdatedDate])
    VALUES (CAST(2304 AS bigint), CAST(0 AS bit), CAST(1 AS bit), N''0'', N''WMS.GOODS_RECEIPT.SUPPLIER_STOCK_MAPPING.VIEW'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, NULL, CAST(1 AS bit), N''Tedarikçi stok eşlemelerini görüntüle'', NULL, NULL),
    (CAST(2305 AS bigint), CAST(0 AS bit), CAST(1 AS bit), N''0'', N''WMS.GOODS_RECEIPT.SUPPLIER_STOCK_MAPPING.MANAGE'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, NULL, CAST(1 AS bit), N''Tedarikçi stok eşlemelerini yönet'', NULL, NULL)');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'AvailableOnMobile', N'AvailableOnWeb', N'BranchCode', N'Code', N'CreatedBy', N'CreatedDate', N'DeletedBy', N'DeletedDate', N'Description', N'IsActive', N'Name', N'UpdatedBy', N'UpdatedDate') AND [object_id] = OBJECT_ID(N'[RII_PERMISSION_DEFINITIONS]'))
        SET IDENTITY_INSERT [RII_PERMISSION_DEFINITIONS] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260730075330_AddSupplierStockMappings'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'BranchCode', N'CreatedBy', N'CreatedDate', N'DeletedBy', N'DeletedDate', N'PermissionDefinitionId', N'PermissionGroupId', N'UpdatedBy', N'UpdatedDate') AND [object_id] = OBJECT_ID(N'[RII_PERMISSION_GROUP_PERMISSIONS]'))
        SET IDENTITY_INSERT [RII_PERMISSION_GROUP_PERMISSIONS] ON;
    EXEC(N'INSERT INTO [RII_PERMISSION_GROUP_PERMISSIONS] ([Id], [BranchCode], [CreatedBy], [CreatedDate], [DeletedBy], [DeletedDate], [PermissionDefinitionId], [PermissionGroupId], [UpdatedBy], [UpdatedDate])
    VALUES (CAST(2304 AS bigint), N''0'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, CAST(2304 AS bigint), CAST(1001 AS bigint), NULL, NULL),
    (CAST(2305 AS bigint), N''0'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, CAST(2305 AS bigint), CAST(1001 AS bigint), NULL, NULL)');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'BranchCode', N'CreatedBy', N'CreatedDate', N'DeletedBy', N'DeletedDate', N'PermissionDefinitionId', N'PermissionGroupId', N'UpdatedBy', N'UpdatedDate') AND [object_id] = OBJECT_ID(N'[RII_PERMISSION_GROUP_PERMISSIONS]'))
        SET IDENTITY_INSERT [RII_PERMISSION_GROUP_PERMISSIONS] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260730075330_AddSupplierStockMappings'
)
BEGIN
    CREATE INDEX [IX_RII_SUPPLIER_STOCK_MAPPING_IsDeleted] ON [RII_SUPPLIER_STOCK_MAPPING] ([IsDeleted]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260730075330_AddSupplierStockMappings'
)
BEGIN
    CREATE INDEX [IX_RII_SUPPLIER_STOCK_MAPPING_STOCK_ACTIVE] ON [RII_SUPPLIER_STOCK_MAPPING] ([BranchCode], [StockId], [IsActive]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260730075330_AddSupplierStockMappings'
)
BEGIN
    CREATE INDEX [IX_RII_SUPPLIER_STOCK_MAPPING_StockId] ON [RII_SUPPLIER_STOCK_MAPPING] ([StockId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260730075330_AddSupplierStockMappings'
)
BEGIN
    CREATE INDEX [IX_RII_SUPPLIER_STOCK_MAPPING_SUPPLIER_ACTIVE] ON [RII_SUPPLIER_STOCK_MAPPING] ([BranchCode], [SupplierId], [IsActive]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260730075330_AddSupplierStockMappings'
)
BEGIN
    CREATE INDEX [IX_RII_SUPPLIER_STOCK_MAPPING_SupplierId] ON [RII_SUPPLIER_STOCK_MAPPING] ([SupplierId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260730075330_AddSupplierStockMappings'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_RII_SUPPLIER_STOCK_MAPPING_IDENTITY] ON [RII_SUPPLIER_STOCK_MAPPING] ([BranchCode], [SupplierId], [NormalizedSupplierStockCode]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260730075330_AddSupplierStockMappings'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260730075330_AddSupplierStockMappings', N'10.0.10');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260730083749_AddIncomingInvoiceMatchingAndOcr'
)
BEGIN
    ALTER TABLE [RII_INCOMING_INVOICE_LINE] ADD [ConversionFactor] decimal(28,8) NOT NULL DEFAULT 1.0;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260730083749_AddIncomingInvoiceMatchingAndOcr'
)
BEGIN
    ALTER TABLE [RII_INCOMING_INVOICE_LINE] ADD [RecognitionConfidence] decimal(9,6) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260730083749_AddIncomingInvoiceMatchingAndOcr'
)
BEGIN
    ALTER TABLE [RII_INCOMING_INVOICE_LINE] ADD [SupplierStockMappingId] bigint NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260730083749_AddIncomingInvoiceMatchingAndOcr'
)
BEGIN
    DECLARE @var14 nvarchar(max);
    SELECT @var14 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[RII_INCOMING_INVOICE_HEADER]') AND [c].[name] = N'ELogoConnectionId');
    IF @var14 IS NOT NULL EXEC(N'ALTER TABLE [RII_INCOMING_INVOICE_HEADER] DROP CONSTRAINT ' + @var14 + ';');
    ALTER TABLE [RII_INCOMING_INVOICE_HEADER] ALTER COLUMN [ELogoConnectionId] bigint NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260730083749_AddIncomingInvoiceMatchingAndOcr'
)
BEGIN
    ALTER TABLE [RII_INCOMING_INVOICE_HEADER] ADD [CaptureSource] int NOT NULL DEFAULT 1;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260730083749_AddIncomingInvoiceMatchingAndOcr'
)
BEGIN
    ALTER TABLE [RII_INCOMING_INVOICE_HEADER] ADD [RecognitionConfidence] decimal(9,6) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260730083749_AddIncomingInvoiceMatchingAndOcr'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'AvailableOnMobile', N'AvailableOnWeb', N'BranchCode', N'Code', N'CreatedBy', N'CreatedDate', N'DeletedBy', N'DeletedDate', N'Description', N'IsActive', N'Name', N'UpdatedBy', N'UpdatedDate') AND [object_id] = OBJECT_ID(N'[RII_PERMISSION_DEFINITIONS]'))
        SET IDENTITY_INSERT [RII_PERMISSION_DEFINITIONS] ON;
    EXEC(N'INSERT INTO [RII_PERMISSION_DEFINITIONS] ([Id], [AvailableOnMobile], [AvailableOnWeb], [BranchCode], [Code], [CreatedBy], [CreatedDate], [DeletedBy], [DeletedDate], [Description], [IsActive], [Name], [UpdatedBy], [UpdatedDate])
    VALUES (CAST(2306 AS bigint), CAST(0 AS bit), CAST(1 AS bit), N''0'', N''WMS.INCOMING_INVOICE.OCR_IMPORT'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, NULL, CAST(1 AS bit), N''Fatura belgesini OCR ile ön incelemeye al'', NULL, NULL)');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'AvailableOnMobile', N'AvailableOnWeb', N'BranchCode', N'Code', N'CreatedBy', N'CreatedDate', N'DeletedBy', N'DeletedDate', N'Description', N'IsActive', N'Name', N'UpdatedBy', N'UpdatedDate') AND [object_id] = OBJECT_ID(N'[RII_PERMISSION_DEFINITIONS]'))
        SET IDENTITY_INSERT [RII_PERMISSION_DEFINITIONS] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260730083749_AddIncomingInvoiceMatchingAndOcr'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'BranchCode', N'CreatedBy', N'CreatedDate', N'DeletedBy', N'DeletedDate', N'PermissionDefinitionId', N'PermissionGroupId', N'UpdatedBy', N'UpdatedDate') AND [object_id] = OBJECT_ID(N'[RII_PERMISSION_GROUP_PERMISSIONS]'))
        SET IDENTITY_INSERT [RII_PERMISSION_GROUP_PERMISSIONS] ON;
    EXEC(N'INSERT INTO [RII_PERMISSION_GROUP_PERMISSIONS] ([Id], [BranchCode], [CreatedBy], [CreatedDate], [DeletedBy], [DeletedDate], [PermissionDefinitionId], [PermissionGroupId], [UpdatedBy], [UpdatedDate])
    VALUES (CAST(2306 AS bigint), N''0'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, CAST(2306 AS bigint), CAST(1001 AS bigint), NULL, NULL)');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'BranchCode', N'CreatedBy', N'CreatedDate', N'DeletedBy', N'DeletedDate', N'PermissionDefinitionId', N'PermissionGroupId', N'UpdatedBy', N'UpdatedDate') AND [object_id] = OBJECT_ID(N'[RII_PERMISSION_GROUP_PERMISSIONS]'))
        SET IDENTITY_INSERT [RII_PERMISSION_GROUP_PERMISSIONS] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260730083749_AddIncomingInvoiceMatchingAndOcr'
)
BEGIN
    EXEC sys.sp_executesql N'CREATE INDEX [IX_RII_INCOMING_INVOICE_LINE_SupplierStockMappingId] ON [RII_INCOMING_INVOICE_LINE] ([SupplierStockMappingId]);';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260730083749_AddIncomingInvoiceMatchingAndOcr'
)
BEGIN
    ALTER TABLE [RII_INCOMING_INVOICE_LINE] ADD CONSTRAINT [FK_RII_INCOMING_INVOICE_LINE_RII_SUPPLIER_STOCK_MAPPING_SupplierStockMappingId] FOREIGN KEY ([SupplierStockMappingId]) REFERENCES [RII_SUPPLIER_STOCK_MAPPING] ([Id]) ON DELETE NO ACTION;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260730083749_AddIncomingInvoiceMatchingAndOcr'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260730083749_AddIncomingInvoiceMatchingAndOcr', N'10.0.10');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260730130412_AddWarehouseDefaultGoodsReceiptLocation'
)
BEGIN
    ALTER TABLE [RII_WAREHOUSE] ADD [DefaultGoodsReceiptLocationId] bigint NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260730130412_AddWarehouseDefaultGoodsReceiptLocation'
)
BEGIN
    EXEC sys.sp_executesql N'UPDATE warehouse
    SET warehouse.DefaultGoodsReceiptLocationId = defaultLocation.Id
    FROM RII_WAREHOUSE AS warehouse
    CROSS APPLY
    (
        SELECT TOP (1) location.Id
        FROM RII_LOCATION AS location
        WHERE location.WarehouseId = warehouse.Id
          AND location.IsDeleted = 0
          AND location.IsActive = 1
          AND UPPER(LTRIM(RTRIM(location.Code))) = N''YER1''
        ORDER BY location.Id
    ) AS defaultLocation
    WHERE warehouse.IsDeleted = 0
      AND warehouse.DefaultGoodsReceiptLocationId IS NULL;';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260730130412_AddWarehouseDefaultGoodsReceiptLocation'
)
BEGIN
    EXEC sys.sp_executesql N'CREATE INDEX [IX_RII_WAREHOUSE_DEFAULT_GR_LOCATION] ON [RII_WAREHOUSE] ([DefaultGoodsReceiptLocationId]);';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260730130412_AddWarehouseDefaultGoodsReceiptLocation'
)
BEGIN
    ALTER TABLE [RII_WAREHOUSE] ADD CONSTRAINT [FK_RII_WAREHOUSE_RII_LOCATION_DefaultGoodsReceiptLocationId] FOREIGN KEY ([DefaultGoodsReceiptLocationId]) REFERENCES [RII_LOCATION] ([Id]) ON DELETE SET NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260730130412_AddWarehouseDefaultGoodsReceiptLocation'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260730130412_AddWarehouseDefaultGoodsReceiptLocation', N'10.0.10');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260730150939_LinkGoodsReceiptSlipsToNetsisOrders'
)
BEGIN
    IF OBJECT_ID(N'dbo.RII_FN_GR_OPENORDERS_HEADER') IS NULL
    BEGIN
        EXEC sys.sp_executesql N'CREATE FUNCTION dbo.RII_FN_GR_OPENORDERS_HEADER
    (
        @CustomerCode VARCHAR(30),
        @BranchCode VARCHAR(10) = NULL
    )
    RETURNS TABLE
    AS
    RETURN
    WITH FilteredOrders AS
    (
        SELECT DISTINCT S.FISNO
        FROM V3RIICO.dbo.TBLSIPATRA AS S WITH (NOLOCK)
        WHERE S.STHAR_ACIKLAMA = @CustomerCode
          AND (@BranchCode IS NULL OR @BranchCode = '''' OR S.SUBE_KODU = @BranchCode)

        UNION

        SELECT DISTINCT M.FATIRS_NO
        FROM V3RIICO.dbo.TBLSIPAMAS AS M WITH (NOLOCK)
        WHERE M.CARI_KODU = @CustomerCode
          AND (@BranchCode IS NULL OR @BranchCode = '''' OR M.SUBE_KODU = @BranchCode)
    ),
    OrderTotals AS
    (
        SELECT
            S.FISNO,
            MIN(S.SUBE_KODU) AS BranchCode,
            MIN(S.DEPO_KODU) AS TargetWh,
            CASE
                WHEN COUNT(DISTINCT NULLIF(LTRIM(RTRIM(S.PROJE_KODU)), '''')) = 1
                    THEN MAX(NULLIF(LTRIM(RTRIM(S.PROJE_KODU)), ''''))
                ELSE ''0''
            END AS ProjectCode,
            MIN(COALESCE(
                TRY_CONVERT(DATETIME, S.STHAR_TESTAR, 104),
                TRY_CONVERT(DATETIME, M.SIPARIS_TEST, 104))) AS DeliveryDate,
            MIN(S.STHAR_TARIH) AS OrderDate,
            SUM(CASE WHEN ISNULL(S.L_YEDEK9, 0) = -1 THEN S.STHAR_GCMIK2 ELSE S.STHAR_GCMIK END) AS OrderedQty,
            SUM(S.FIRMA_DOVTUT) AS DeliveredQty
        FROM V3RIICO.dbo.TBLSIPATRA AS S WITH (NOLOCK)
        INNER JOIN FilteredOrders AS F ON F.FISNO = S.FISNO
        LEFT JOIN V3RIICO.dbo.TBLSIPAMAS AS M WITH (NOLOCK)
          ON M.FATIRS_NO = S.FISNO
         AND M.FTIRSIP = S.STHAR_FTIRSIP
         AND M.SUBE_KODU = S.SUBE_KODU
         AND M.CARI_KODU = S.STHAR_ACIKLAMA
        WHERE S.STHAR_FTIRSIP = ''7''
          AND S.STHAR_GCKOD = ''G''
          AND S.STHAR_HTUR <> ''K''
          AND ISNULL(S.L_YEDEK9, 0) <= 0
          AND NOT (S.REDNEDEN = 2 AND EXISTS
              (SELECT 1 FROM V3RIICO.dbo.TBLASORTIMAS AS A WITH (NOLOCK) WHERE A.ASORTIKOD = S.EKALAN1))
          AND (@BranchCode IS NULL OR @BranchCode = '''' OR S.SUBE_KODU = @BranchCode)
        GROUP BY S.FISNO
        HAVING SUM(CASE WHEN ISNULL(S.L_YEDEK9, 0) = -1 THEN S.STHAR_GCMIK2 ELSE S.STHAR_GCMIK END)
             - SUM(S.FIRMA_DOVTUT) > 0
    ),
    ActiveAllocations AS
    (
        SELECT
            SD.ExternalDocumentNo,
            SUM(ISNULL(LS.AllocatedQuantity, 0)) AS PlannedQtyAllocated
        FROM dbo.RII_GR_LINE_SOURCE AS LS
        INNER JOIN dbo.RII_GR_SOURCE_DOCUMENT AS SD ON SD.Id = LS.GrSourceDocumentId
        INNER JOIN dbo.RII_GR_LINE AS L ON L.Id = LS.GrLineId
        INNER JOIN dbo.RII_GR_HEADER AS H ON H.Id = L.GrHeaderId
        WHERE LS.IsDeleted = 0 AND SD.IsDeleted = 0 AND L.IsDeleted = 0 AND H.IsDeleted = 0
          AND SD.SourceSystem = N''Netsis'' AND SD.SourceDocumentType = N''PurchaseOrder''
          AND H.Status <> N''Cancelled'' AND H.ErpIntegrationStatus <> N''Succeeded''
          AND (@BranchCode IS NULL OR @BranchCode = '''' OR H.BranchCode = @BranchCode)
        GROUP BY SD.ExternalDocumentNo
    )
    SELECT
        ''H'' AS Mode,
        H.FISNO AS SiparisNo,
        CAST(NULL AS INT) AS OrderID,
        @CustomerCode AS CustomerCode,
        (SELECT TOP (1) C.CARI_ISIM FROM V3RIICO.dbo.TBLCASABIT AS C WITH (NOLOCK)
         WHERE C.CARI_KOD = @CustomerCode) AS CustomerName,
        H.BranchCode,
        H.TargetWh,
        H.ProjectCode,
        H.OrderDate,
        H.DeliveryDate,
        CAST(H.OrderedQty AS DECIMAL(18, 4)) AS OrderedQty,
        CAST(H.DeliveredQty AS DECIMAL(18, 4)) AS DeliveredQty,
        CAST(H.OrderedQty - H.DeliveredQty AS DECIMAL(18, 4)) AS RemainingHamax,
        CAST(ISNULL(A.PlannedQtyAllocated, 0) AS DECIMAL(18, 4)) AS PlannedQtyAllocated,
        CAST((H.OrderedQty - H.DeliveredQty) - ISNULL(A.PlannedQtyAllocated, 0) AS DECIMAL(18, 4)) AS RemainingForImport
    FROM OrderTotals AS H
    LEFT JOIN ActiveAllocations AS A ON A.ExternalDocumentNo = H.FISNO
    WHERE (H.OrderedQty - H.DeliveredQty) - ISNULL(A.PlannedQtyAllocated, 0) > 0;';
    END
    ELSE
    BEGIN
        EXEC sys.sp_executesql N'ALTER FUNCTION dbo.RII_FN_GR_OPENORDERS_HEADER
    (
        @CustomerCode VARCHAR(30),
        @BranchCode VARCHAR(10) = NULL
    )
    RETURNS TABLE
    AS
    RETURN
    WITH FilteredOrders AS
    (
        SELECT DISTINCT S.FISNO
        FROM V3RIICO.dbo.TBLSIPATRA AS S WITH (NOLOCK)
        WHERE S.STHAR_ACIKLAMA = @CustomerCode
          AND (@BranchCode IS NULL OR @BranchCode = '''' OR S.SUBE_KODU = @BranchCode)

        UNION

        SELECT DISTINCT M.FATIRS_NO
        FROM V3RIICO.dbo.TBLSIPAMAS AS M WITH (NOLOCK)
        WHERE M.CARI_KODU = @CustomerCode
          AND (@BranchCode IS NULL OR @BranchCode = '''' OR M.SUBE_KODU = @BranchCode)
    ),
    OrderTotals AS
    (
        SELECT
            S.FISNO,
            MIN(S.SUBE_KODU) AS BranchCode,
            MIN(S.DEPO_KODU) AS TargetWh,
            CASE
                WHEN COUNT(DISTINCT NULLIF(LTRIM(RTRIM(S.PROJE_KODU)), '''')) = 1
                    THEN MAX(NULLIF(LTRIM(RTRIM(S.PROJE_KODU)), ''''))
                ELSE ''0''
            END AS ProjectCode,
            MIN(COALESCE(
                TRY_CONVERT(DATETIME, S.STHAR_TESTAR, 104),
                TRY_CONVERT(DATETIME, M.SIPARIS_TEST, 104))) AS DeliveryDate,
            MIN(S.STHAR_TARIH) AS OrderDate,
            SUM(CASE WHEN ISNULL(S.L_YEDEK9, 0) = -1 THEN S.STHAR_GCMIK2 ELSE S.STHAR_GCMIK END) AS OrderedQty,
            SUM(S.FIRMA_DOVTUT) AS DeliveredQty
        FROM V3RIICO.dbo.TBLSIPATRA AS S WITH (NOLOCK)
        INNER JOIN FilteredOrders AS F ON F.FISNO = S.FISNO
        LEFT JOIN V3RIICO.dbo.TBLSIPAMAS AS M WITH (NOLOCK)
          ON M.FATIRS_NO = S.FISNO
         AND M.FTIRSIP = S.STHAR_FTIRSIP
         AND M.SUBE_KODU = S.SUBE_KODU
         AND M.CARI_KODU = S.STHAR_ACIKLAMA
        WHERE S.STHAR_FTIRSIP = ''7''
          AND S.STHAR_GCKOD = ''G''
          AND S.STHAR_HTUR <> ''K''
          AND ISNULL(S.L_YEDEK9, 0) <= 0
          AND NOT (S.REDNEDEN = 2 AND EXISTS
              (SELECT 1 FROM V3RIICO.dbo.TBLASORTIMAS AS A WITH (NOLOCK) WHERE A.ASORTIKOD = S.EKALAN1))
          AND (@BranchCode IS NULL OR @BranchCode = '''' OR S.SUBE_KODU = @BranchCode)
        GROUP BY S.FISNO
        HAVING SUM(CASE WHEN ISNULL(S.L_YEDEK9, 0) = -1 THEN S.STHAR_GCMIK2 ELSE S.STHAR_GCMIK END)
             - SUM(S.FIRMA_DOVTUT) > 0
    ),
    ActiveAllocations AS
    (
        SELECT
            SD.ExternalDocumentNo,
            SUM(ISNULL(LS.AllocatedQuantity, 0)) AS PlannedQtyAllocated
        FROM dbo.RII_GR_LINE_SOURCE AS LS
        INNER JOIN dbo.RII_GR_SOURCE_DOCUMENT AS SD ON SD.Id = LS.GrSourceDocumentId
        INNER JOIN dbo.RII_GR_LINE AS L ON L.Id = LS.GrLineId
        INNER JOIN dbo.RII_GR_HEADER AS H ON H.Id = L.GrHeaderId
        WHERE LS.IsDeleted = 0 AND SD.IsDeleted = 0 AND L.IsDeleted = 0 AND H.IsDeleted = 0
          AND SD.SourceSystem = N''Netsis'' AND SD.SourceDocumentType = N''PurchaseOrder''
          AND H.Status <> N''Cancelled'' AND H.ErpIntegrationStatus <> N''Succeeded''
          AND (@BranchCode IS NULL OR @BranchCode = '''' OR H.BranchCode = @BranchCode)
        GROUP BY SD.ExternalDocumentNo
    )
    SELECT
        ''H'' AS Mode,
        H.FISNO AS SiparisNo,
        CAST(NULL AS INT) AS OrderID,
        @CustomerCode AS CustomerCode,
        (SELECT TOP (1) C.CARI_ISIM FROM V3RIICO.dbo.TBLCASABIT AS C WITH (NOLOCK)
         WHERE C.CARI_KOD = @CustomerCode) AS CustomerName,
        H.BranchCode,
        H.TargetWh,
        H.ProjectCode,
        H.OrderDate,
        H.DeliveryDate,
        CAST(H.OrderedQty AS DECIMAL(18, 4)) AS OrderedQty,
        CAST(H.DeliveredQty AS DECIMAL(18, 4)) AS DeliveredQty,
        CAST(H.OrderedQty - H.DeliveredQty AS DECIMAL(18, 4)) AS RemainingHamax,
        CAST(ISNULL(A.PlannedQtyAllocated, 0) AS DECIMAL(18, 4)) AS PlannedQtyAllocated,
        CAST((H.OrderedQty - H.DeliveredQty) - ISNULL(A.PlannedQtyAllocated, 0) AS DECIMAL(18, 4)) AS RemainingForImport
    FROM OrderTotals AS H
    LEFT JOIN ActiveAllocations AS A ON A.ExternalDocumentNo = H.FISNO
    WHERE (H.OrderedQty - H.DeliveredQty) - ISNULL(A.PlannedQtyAllocated, 0) > 0;';
    END
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260730150939_LinkGoodsReceiptSlipsToNetsisOrders'
)
BEGIN
    IF OBJECT_ID(N'dbo.RII_FN_GR_OPENORDERS_LINE') IS NULL
    BEGIN
        EXEC sys.sp_executesql N'CREATE FUNCTION dbo.RII_FN_GR_OPENORDERS_LINE
    (
        @SiparisNoCsv NVARCHAR(MAX) = NULL,
        @CustomerCode VARCHAR(30) = NULL,
        @BranchCode VARCHAR(10) = NULL
    )
    RETURNS TABLE
    AS
    RETURN
    WITH OrderRows AS
    (
        SELECT
            S.FISNO AS FisNo,
            S.SUBE_KODU AS BranchCode,
            S.DEPO_KODU AS TargetWh,
            NULLIF(LTRIM(RTRIM(S.PROJE_KODU)), '''') AS ProjectCode,
            COALESCE(
                TRY_CONVERT(DATETIME, S.STHAR_TESTAR, 104),
                TRY_CONVERT(DATETIME, M.SIPARIS_TEST, 104)) AS DeliveryDate,
            CAST(ISNULL(S.STHAR_NF, 0) AS DECIMAL(28, 8)) AS NetUnitPrice,
            CAST(ISNULL(S.STHAR_BF, 0) AS DECIMAL(28, 8)) AS GrossUnitPrice,
            S.STHAR_TARIH AS OrderDate,
            S.INCKEYNO AS OrderID,
            S.SIRA AS OrderLineSequence,
            S.STOK_KODU AS StockCode,
            ST.STOK_ADI AS StockName,
            ST.OLCU_BR1 AS UnitCode,
            COALESCE(M.CARI_KODU, S.STHAR_ACIKLAMA) AS CustomerCode,
            C.CARI_ISIM AS CustomerName,
            CAST(CASE WHEN ISNULL(S.L_YEDEK9, 0) = -1 THEN S.STHAR_GCMIK2 ELSE S.STHAR_GCMIK END AS DECIMAL(18, 4)) AS OrderedQty,
            CAST(S.FIRMA_DOVTUT AS DECIMAL(18, 4)) AS DeliveredQty
        FROM V3RIICO.dbo.TBLSIPATRA AS S WITH (NOLOCK)
        LEFT JOIN V3RIICO.dbo.TBLSIPAMAS AS M WITH (NOLOCK)
          ON M.FATIRS_NO = S.FISNO
         AND M.FTIRSIP = S.STHAR_FTIRSIP
         AND M.SUBE_KODU = S.SUBE_KODU
         AND M.CARI_KODU = S.STHAR_ACIKLAMA
        LEFT JOIN V3RIICO.dbo.TBLCASABIT AS C WITH (NOLOCK)
          ON C.CARI_KOD = COALESCE(M.CARI_KODU, S.STHAR_ACIKLAMA)
        LEFT JOIN V3RIICO.dbo.TBLASORTIMAS AS A WITH (NOLOCK) ON A.ASORTIKOD = S.EKALAN1
        LEFT JOIN V3RIICO.dbo.TBLSTSABIT AS ST WITH (NOLOCK) ON ST.STOK_KODU = S.STOK_KODU
        WHERE S.STHAR_FTIRSIP = ''7''
          AND S.STHAR_GCKOD = ''G''
          AND S.STHAR_HTUR <> ''K''
          AND ISNULL(S.L_YEDEK9, 0) <= 0
          AND (A.ASORTIKOD IS NULL OR S.REDNEDEN <> 2)
          AND (CASE WHEN ISNULL(S.L_YEDEK9, 0) = -1 THEN S.STHAR_GCMIK2 ELSE S.STHAR_GCMIK END) - S.FIRMA_DOVTUT > 0
          AND (@CustomerCode IS NULL OR @CustomerCode = '''' OR COALESCE(M.CARI_KODU, S.STHAR_ACIKLAMA) = @CustomerCode)
          AND (@BranchCode IS NULL OR @BranchCode = '''' OR S.SUBE_KODU = @BranchCode)
          AND (NULLIF(REPLACE(LTRIM(RTRIM(@SiparisNoCsv)), N'' '', N''''), N'''') IS NULL
               OR CHARINDEX(
                   N'','' + LTRIM(RTRIM(CONVERT(NVARCHAR(100), S.FISNO))) + N'','',
                   N'','' + REPLACE(@SiparisNoCsv, N'' '', N'''') + N'','') > 0)
    ),
    OrderLines AS
    (
        SELECT
            FisNo, BranchCode, TargetWh, MAX(ProjectCode) AS ProjectCode,
            MIN(OrderDate) AS OrderDate, MIN(DeliveryDate) AS DeliveryDate,
            MAX(NetUnitPrice) AS NetUnitPrice, MAX(GrossUnitPrice) AS GrossUnitPrice,
            OrderID, MAX(OrderLineSequence) AS OrderLineSequence,
            StockCode, MAX(StockName) AS StockName, MAX(UnitCode) AS UnitCode,
            MAX(CustomerCode) AS CustomerCode, MAX(CustomerName) AS CustomerName,
            SUM(OrderedQty) AS OrderedQty, SUM(DeliveredQty) AS DeliveredQty,
            CAST(SUM(OrderedQty - DeliveredQty) AS DECIMAL(18, 4)) AS RemainingHamax
        FROM OrderRows
        GROUP BY FisNo, BranchCode, TargetWh, OrderID, StockCode
    ),
    ActiveAllocations AS
    (
        SELECT
            LS.ExternalLineId,
            SUM(ISNULL(LS.AllocatedQuantity, 0)) AS PlannedQtyAllocated
        FROM dbo.RII_GR_LINE_SOURCE AS LS
        INNER JOIN dbo.RII_GR_SOURCE_DOCUMENT AS SD ON SD.Id = LS.GrSourceDocumentId
        INNER JOIN dbo.RII_GR_LINE AS L ON L.Id = LS.GrLineId
        INNER JOIN dbo.RII_GR_HEADER AS H ON H.Id = L.GrHeaderId
        WHERE LS.IsDeleted = 0 AND SD.IsDeleted = 0 AND L.IsDeleted = 0 AND H.IsDeleted = 0
          AND SD.SourceSystem = N''Netsis'' AND SD.SourceDocumentType = N''PurchaseOrder''
          AND H.Status <> N''Cancelled'' AND H.ErpIntegrationStatus <> N''Succeeded''
          AND (@BranchCode IS NULL OR @BranchCode = '''' OR H.BranchCode = @BranchCode)
        GROUP BY LS.ExternalLineId
    )
    SELECT
        ''L'' AS Mode,
        X.FisNo AS SiparisNo,
        X.OrderID,
        X.OrderLineSequence,
        X.StockCode,
        X.StockName,
        X.UnitCode,
        CAST('''' AS VARCHAR(100)) AS YapKod,
        CAST('''' AS VARCHAR(100)) AS YapAcik,
        X.CustomerCode,
        X.CustomerName,
        X.BranchCode,
        X.TargetWh,
        X.ProjectCode,
        X.OrderDate,
        X.DeliveryDate,
        X.NetUnitPrice,
        X.GrossUnitPrice,
        X.OrderedQty,
        X.DeliveredQty,
        X.RemainingHamax,
        CAST(ISNULL(A.PlannedQtyAllocated, 0) AS DECIMAL(18, 4)) AS PlannedQtyAllocated,
        CAST(X.RemainingHamax - ISNULL(A.PlannedQtyAllocated, 0) AS DECIMAL(18, 4)) AS RemainingForImport
    FROM OrderLines AS X
    LEFT JOIN ActiveAllocations AS A ON A.ExternalLineId = CONVERT(NVARCHAR(100), X.OrderID);';
    END
    ELSE
    BEGIN
        EXEC sys.sp_executesql N'ALTER FUNCTION dbo.RII_FN_GR_OPENORDERS_LINE
    (
        @SiparisNoCsv NVARCHAR(MAX) = NULL,
        @CustomerCode VARCHAR(30) = NULL,
        @BranchCode VARCHAR(10) = NULL
    )
    RETURNS TABLE
    AS
    RETURN
    WITH OrderRows AS
    (
        SELECT
            S.FISNO AS FisNo,
            S.SUBE_KODU AS BranchCode,
            S.DEPO_KODU AS TargetWh,
            NULLIF(LTRIM(RTRIM(S.PROJE_KODU)), '''') AS ProjectCode,
            COALESCE(
                TRY_CONVERT(DATETIME, S.STHAR_TESTAR, 104),
                TRY_CONVERT(DATETIME, M.SIPARIS_TEST, 104)) AS DeliveryDate,
            CAST(ISNULL(S.STHAR_NF, 0) AS DECIMAL(28, 8)) AS NetUnitPrice,
            CAST(ISNULL(S.STHAR_BF, 0) AS DECIMAL(28, 8)) AS GrossUnitPrice,
            S.STHAR_TARIH AS OrderDate,
            S.INCKEYNO AS OrderID,
            S.SIRA AS OrderLineSequence,
            S.STOK_KODU AS StockCode,
            ST.STOK_ADI AS StockName,
            ST.OLCU_BR1 AS UnitCode,
            COALESCE(M.CARI_KODU, S.STHAR_ACIKLAMA) AS CustomerCode,
            C.CARI_ISIM AS CustomerName,
            CAST(CASE WHEN ISNULL(S.L_YEDEK9, 0) = -1 THEN S.STHAR_GCMIK2 ELSE S.STHAR_GCMIK END AS DECIMAL(18, 4)) AS OrderedQty,
            CAST(S.FIRMA_DOVTUT AS DECIMAL(18, 4)) AS DeliveredQty
        FROM V3RIICO.dbo.TBLSIPATRA AS S WITH (NOLOCK)
        LEFT JOIN V3RIICO.dbo.TBLSIPAMAS AS M WITH (NOLOCK)
          ON M.FATIRS_NO = S.FISNO
         AND M.FTIRSIP = S.STHAR_FTIRSIP
         AND M.SUBE_KODU = S.SUBE_KODU
         AND M.CARI_KODU = S.STHAR_ACIKLAMA
        LEFT JOIN V3RIICO.dbo.TBLCASABIT AS C WITH (NOLOCK)
          ON C.CARI_KOD = COALESCE(M.CARI_KODU, S.STHAR_ACIKLAMA)
        LEFT JOIN V3RIICO.dbo.TBLASORTIMAS AS A WITH (NOLOCK) ON A.ASORTIKOD = S.EKALAN1
        LEFT JOIN V3RIICO.dbo.TBLSTSABIT AS ST WITH (NOLOCK) ON ST.STOK_KODU = S.STOK_KODU
        WHERE S.STHAR_FTIRSIP = ''7''
          AND S.STHAR_GCKOD = ''G''
          AND S.STHAR_HTUR <> ''K''
          AND ISNULL(S.L_YEDEK9, 0) <= 0
          AND (A.ASORTIKOD IS NULL OR S.REDNEDEN <> 2)
          AND (CASE WHEN ISNULL(S.L_YEDEK9, 0) = -1 THEN S.STHAR_GCMIK2 ELSE S.STHAR_GCMIK END) - S.FIRMA_DOVTUT > 0
          AND (@CustomerCode IS NULL OR @CustomerCode = '''' OR COALESCE(M.CARI_KODU, S.STHAR_ACIKLAMA) = @CustomerCode)
          AND (@BranchCode IS NULL OR @BranchCode = '''' OR S.SUBE_KODU = @BranchCode)
          AND (NULLIF(REPLACE(LTRIM(RTRIM(@SiparisNoCsv)), N'' '', N''''), N'''') IS NULL
               OR CHARINDEX(
                   N'','' + LTRIM(RTRIM(CONVERT(NVARCHAR(100), S.FISNO))) + N'','',
                   N'','' + REPLACE(@SiparisNoCsv, N'' '', N'''') + N'','') > 0)
    ),
    OrderLines AS
    (
        SELECT
            FisNo, BranchCode, TargetWh, MAX(ProjectCode) AS ProjectCode,
            MIN(OrderDate) AS OrderDate, MIN(DeliveryDate) AS DeliveryDate,
            MAX(NetUnitPrice) AS NetUnitPrice, MAX(GrossUnitPrice) AS GrossUnitPrice,
            OrderID, MAX(OrderLineSequence) AS OrderLineSequence,
            StockCode, MAX(StockName) AS StockName, MAX(UnitCode) AS UnitCode,
            MAX(CustomerCode) AS CustomerCode, MAX(CustomerName) AS CustomerName,
            SUM(OrderedQty) AS OrderedQty, SUM(DeliveredQty) AS DeliveredQty,
            CAST(SUM(OrderedQty - DeliveredQty) AS DECIMAL(18, 4)) AS RemainingHamax
        FROM OrderRows
        GROUP BY FisNo, BranchCode, TargetWh, OrderID, StockCode
    ),
    ActiveAllocations AS
    (
        SELECT
            LS.ExternalLineId,
            SUM(ISNULL(LS.AllocatedQuantity, 0)) AS PlannedQtyAllocated
        FROM dbo.RII_GR_LINE_SOURCE AS LS
        INNER JOIN dbo.RII_GR_SOURCE_DOCUMENT AS SD ON SD.Id = LS.GrSourceDocumentId
        INNER JOIN dbo.RII_GR_LINE AS L ON L.Id = LS.GrLineId
        INNER JOIN dbo.RII_GR_HEADER AS H ON H.Id = L.GrHeaderId
        WHERE LS.IsDeleted = 0 AND SD.IsDeleted = 0 AND L.IsDeleted = 0 AND H.IsDeleted = 0
          AND SD.SourceSystem = N''Netsis'' AND SD.SourceDocumentType = N''PurchaseOrder''
          AND H.Status <> N''Cancelled'' AND H.ErpIntegrationStatus <> N''Succeeded''
          AND (@BranchCode IS NULL OR @BranchCode = '''' OR H.BranchCode = @BranchCode)
        GROUP BY LS.ExternalLineId
    )
    SELECT
        ''L'' AS Mode,
        X.FisNo AS SiparisNo,
        X.OrderID,
        X.OrderLineSequence,
        X.StockCode,
        X.StockName,
        X.UnitCode,
        CAST('''' AS VARCHAR(100)) AS YapKod,
        CAST('''' AS VARCHAR(100)) AS YapAcik,
        X.CustomerCode,
        X.CustomerName,
        X.BranchCode,
        X.TargetWh,
        X.ProjectCode,
        X.OrderDate,
        X.DeliveryDate,
        X.NetUnitPrice,
        X.GrossUnitPrice,
        X.OrderedQty,
        X.DeliveredQty,
        X.RemainingHamax,
        CAST(ISNULL(A.PlannedQtyAllocated, 0) AS DECIMAL(18, 4)) AS PlannedQtyAllocated,
        CAST(X.RemainingHamax - ISNULL(A.PlannedQtyAllocated, 0) AS DECIMAL(18, 4)) AS RemainingForImport
    FROM OrderLines AS X
    LEFT JOIN ActiveAllocations AS A ON A.ExternalLineId = CONVERT(NVARCHAR(100), X.OrderID);';
    END
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260730150939_LinkGoodsReceiptSlipsToNetsisOrders'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260730150939_LinkGoodsReceiptSlipsToNetsisOrders', N'10.0.10');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260730153748_LinkTransferShipmentAndWarehouseSlipsToNetsisOrders'
)
BEGIN
    IF OBJECT_ID(N'dbo.RII_FN_WT_LINE') IS NULL
    BEGIN
        EXEC sys.sp_executesql N'CREATE FUNCTION dbo.RII_FN_WT_LINE
    (
        @SiparisNoCsv NVARCHAR(MAX),
        @BranchCode VARCHAR(10) = NULL
    )
    RETURNS TABLE
    AS
    RETURN
    WITH OrderRows AS
    (
        SELECT
            S.FISNO AS FisNo,
            S.SUBE_KODU AS BranchCode,
            S.DEPO_KODU AS TargetWh,
            NULLIF(LTRIM(RTRIM(S.PROJE_KODU)), '''') AS ProjectCode,
            COALESCE(
                TRY_CONVERT(DATETIME, S.STHAR_TESTAR, 104),
                TRY_CONVERT(DATETIME, M.SIPARIS_TEST, 104)) AS DeliveryDate,
            CAST(ISNULL(S.STHAR_NF, 0) AS DECIMAL(28, 8)) AS NetUnitPrice,
            CAST(ISNULL(S.STHAR_BF, 0) AS DECIMAL(28, 8)) AS GrossUnitPrice,
            S.STHAR_TARIH AS OrderDate,
            S.INCKEYNO AS OrderID,
            S.SIRA AS OrderLineSequence,
            S.STOK_KODU AS StockCode,
            ST.STOK_ADI AS StockName,
            ST.OLCU_BR1 AS UnitCode,
            COALESCE(M.CARI_KODU, S.STHAR_ACIKLAMA) AS CustomerCode,
            C.CARI_ISIM AS CustomerName,
            CAST(CASE WHEN ISNULL(S.L_YEDEK9, 0) = -1 THEN S.STHAR_GCMIK2 ELSE S.STHAR_GCMIK END AS DECIMAL(18, 4)) AS OrderedQty,
            CAST(ISNULL(S.FIRMA_DOVTUT, 0) AS DECIMAL(18, 4)) AS DeliveredQty
        FROM V3RIICO.dbo.TBLSIPATRA AS S WITH (NOLOCK)
        LEFT JOIN V3RIICO.dbo.TBLSIPAMAS AS M WITH (NOLOCK)
          ON M.FATIRS_NO = S.FISNO
         AND M.FTIRSIP = S.STHAR_FTIRSIP
         AND M.SUBE_KODU = S.SUBE_KODU
        LEFT JOIN V3RIICO.dbo.TBLCASABIT AS C WITH (NOLOCK)
          ON C.CARI_KOD = COALESCE(M.CARI_KODU, S.STHAR_ACIKLAMA)
        LEFT JOIN V3RIICO.dbo.TBLASORTIMAS AS A WITH (NOLOCK) ON A.ASORTIKOD = S.EKALAN1
        LEFT JOIN V3RIICO.dbo.TBLSTSABIT AS ST WITH (NOLOCK) ON ST.STOK_KODU = S.STOK_KODU
        WHERE S.STHAR_FTIRSIP = ''6''
          AND S.STHAR_GCKOD = ''C''
          AND S.STHAR_HTUR <> ''K''
          AND ISNULL(S.L_YEDEK9, 0) <= 0
          AND (A.ASORTIKOD IS NULL OR S.REDNEDEN <> 2)
          AND (CASE WHEN ISNULL(S.L_YEDEK9, 0) = -1 THEN S.STHAR_GCMIK2 ELSE S.STHAR_GCMIK END)
              - ISNULL(S.FIRMA_DOVTUT, 0) > 0
          AND (@BranchCode IS NULL OR @BranchCode = '''' OR CONVERT(VARCHAR(10), S.SUBE_KODU) = @BranchCode)
          AND (NULLIF(REPLACE(LTRIM(RTRIM(@SiparisNoCsv)), N'' '', N''''), N'''') IS NULL
               OR CHARINDEX(
                   N'','' + LTRIM(RTRIM(CONVERT(NVARCHAR(100), S.FISNO))) + N'','',
                   N'','' + REPLACE(@SiparisNoCsv, N'' '', N'''') + N'','') > 0)
    ),
    OrderLines AS
    (
        SELECT
            FisNo, BranchCode, TargetWh, MAX(ProjectCode) AS ProjectCode,
            MIN(OrderDate) AS OrderDate, MIN(DeliveryDate) AS DeliveryDate,
            MAX(NetUnitPrice) AS NetUnitPrice, MAX(GrossUnitPrice) AS GrossUnitPrice,
            OrderID, MAX(OrderLineSequence) AS OrderLineSequence,
            StockCode, MAX(StockName) AS StockName, MAX(UnitCode) AS UnitCode,
            MAX(CustomerCode) AS CustomerCode, MAX(CustomerName) AS CustomerName,
            SUM(OrderedQty) AS OrderedQty, SUM(DeliveredQty) AS DeliveredQty,
            CAST(SUM(OrderedQty - DeliveredQty) AS DECIMAL(18, 4)) AS RemainingHamax
        FROM OrderRows
        GROUP BY FisNo, BranchCode, TargetWh, OrderID, StockCode
    ),
    ActiveAllocations AS
    (
        SELECT
            LS.ExternalLineId,
            SUM(ISNULL(LS.AllocatedQuantity, 0)) AS PlannedQtyAllocated
        FROM dbo.RII_WT_LINE_SOURCE AS LS
        INNER JOIN dbo.RII_WT_SOURCE_DOCUMENT AS SD ON SD.Id = LS.WtSourceDocumentId
        INNER JOIN dbo.RII_WT_HEADER AS H ON H.Id = SD.WtHeaderId
        WHERE LS.IsDeleted = 0 AND SD.IsDeleted = 0 AND H.IsDeleted = 0
          AND H.Status <> 11 AND H.ErpIntegrationStatus <> 4
          AND (@BranchCode IS NULL OR @BranchCode = '''' OR H.BranchCode = @BranchCode)
        GROUP BY LS.ExternalLineId
    )
    SELECT
        ''L'' AS Mode,
        X.FisNo AS SiparisNo,
        X.OrderID,
        X.OrderLineSequence,
        X.StockCode,
        X.StockName,
        X.UnitCode,
        CAST('''' AS VARCHAR(100)) AS YapKod,
        CAST('''' AS VARCHAR(100)) AS YapAcik,
        X.CustomerCode,
        X.CustomerName,
        X.BranchCode,
        X.TargetWh,
        X.ProjectCode,
        X.OrderDate,
        X.DeliveryDate,
        X.NetUnitPrice,
        X.GrossUnitPrice,
        X.OrderedQty,
        X.DeliveredQty,
        X.RemainingHamax,
        CAST(ISNULL(A.PlannedQtyAllocated, 0) AS DECIMAL(18, 4)) AS PlannedQtyAllocated,
        CAST(X.RemainingHamax - ISNULL(A.PlannedQtyAllocated, 0) AS DECIMAL(18, 4)) AS RemainingForImport
    FROM OrderLines AS X
    LEFT JOIN ActiveAllocations AS A ON A.ExternalLineId = CONVERT(NVARCHAR(100), X.OrderID)
    WHERE X.RemainingHamax > 0;';
    END
    ELSE
    BEGIN
        EXEC sys.sp_executesql N'ALTER FUNCTION dbo.RII_FN_WT_LINE
    (
        @SiparisNoCsv NVARCHAR(MAX),
        @BranchCode VARCHAR(10) = NULL
    )
    RETURNS TABLE
    AS
    RETURN
    WITH OrderRows AS
    (
        SELECT
            S.FISNO AS FisNo,
            S.SUBE_KODU AS BranchCode,
            S.DEPO_KODU AS TargetWh,
            NULLIF(LTRIM(RTRIM(S.PROJE_KODU)), '''') AS ProjectCode,
            COALESCE(
                TRY_CONVERT(DATETIME, S.STHAR_TESTAR, 104),
                TRY_CONVERT(DATETIME, M.SIPARIS_TEST, 104)) AS DeliveryDate,
            CAST(ISNULL(S.STHAR_NF, 0) AS DECIMAL(28, 8)) AS NetUnitPrice,
            CAST(ISNULL(S.STHAR_BF, 0) AS DECIMAL(28, 8)) AS GrossUnitPrice,
            S.STHAR_TARIH AS OrderDate,
            S.INCKEYNO AS OrderID,
            S.SIRA AS OrderLineSequence,
            S.STOK_KODU AS StockCode,
            ST.STOK_ADI AS StockName,
            ST.OLCU_BR1 AS UnitCode,
            COALESCE(M.CARI_KODU, S.STHAR_ACIKLAMA) AS CustomerCode,
            C.CARI_ISIM AS CustomerName,
            CAST(CASE WHEN ISNULL(S.L_YEDEK9, 0) = -1 THEN S.STHAR_GCMIK2 ELSE S.STHAR_GCMIK END AS DECIMAL(18, 4)) AS OrderedQty,
            CAST(ISNULL(S.FIRMA_DOVTUT, 0) AS DECIMAL(18, 4)) AS DeliveredQty
        FROM V3RIICO.dbo.TBLSIPATRA AS S WITH (NOLOCK)
        LEFT JOIN V3RIICO.dbo.TBLSIPAMAS AS M WITH (NOLOCK)
          ON M.FATIRS_NO = S.FISNO
         AND M.FTIRSIP = S.STHAR_FTIRSIP
         AND M.SUBE_KODU = S.SUBE_KODU
        LEFT JOIN V3RIICO.dbo.TBLCASABIT AS C WITH (NOLOCK)
          ON C.CARI_KOD = COALESCE(M.CARI_KODU, S.STHAR_ACIKLAMA)
        LEFT JOIN V3RIICO.dbo.TBLASORTIMAS AS A WITH (NOLOCK) ON A.ASORTIKOD = S.EKALAN1
        LEFT JOIN V3RIICO.dbo.TBLSTSABIT AS ST WITH (NOLOCK) ON ST.STOK_KODU = S.STOK_KODU
        WHERE S.STHAR_FTIRSIP = ''6''
          AND S.STHAR_GCKOD = ''C''
          AND S.STHAR_HTUR <> ''K''
          AND ISNULL(S.L_YEDEK9, 0) <= 0
          AND (A.ASORTIKOD IS NULL OR S.REDNEDEN <> 2)
          AND (CASE WHEN ISNULL(S.L_YEDEK9, 0) = -1 THEN S.STHAR_GCMIK2 ELSE S.STHAR_GCMIK END)
              - ISNULL(S.FIRMA_DOVTUT, 0) > 0
          AND (@BranchCode IS NULL OR @BranchCode = '''' OR CONVERT(VARCHAR(10), S.SUBE_KODU) = @BranchCode)
          AND (NULLIF(REPLACE(LTRIM(RTRIM(@SiparisNoCsv)), N'' '', N''''), N'''') IS NULL
               OR CHARINDEX(
                   N'','' + LTRIM(RTRIM(CONVERT(NVARCHAR(100), S.FISNO))) + N'','',
                   N'','' + REPLACE(@SiparisNoCsv, N'' '', N'''') + N'','') > 0)
    ),
    OrderLines AS
    (
        SELECT
            FisNo, BranchCode, TargetWh, MAX(ProjectCode) AS ProjectCode,
            MIN(OrderDate) AS OrderDate, MIN(DeliveryDate) AS DeliveryDate,
            MAX(NetUnitPrice) AS NetUnitPrice, MAX(GrossUnitPrice) AS GrossUnitPrice,
            OrderID, MAX(OrderLineSequence) AS OrderLineSequence,
            StockCode, MAX(StockName) AS StockName, MAX(UnitCode) AS UnitCode,
            MAX(CustomerCode) AS CustomerCode, MAX(CustomerName) AS CustomerName,
            SUM(OrderedQty) AS OrderedQty, SUM(DeliveredQty) AS DeliveredQty,
            CAST(SUM(OrderedQty - DeliveredQty) AS DECIMAL(18, 4)) AS RemainingHamax
        FROM OrderRows
        GROUP BY FisNo, BranchCode, TargetWh, OrderID, StockCode
    ),
    ActiveAllocations AS
    (
        SELECT
            LS.ExternalLineId,
            SUM(ISNULL(LS.AllocatedQuantity, 0)) AS PlannedQtyAllocated
        FROM dbo.RII_WT_LINE_SOURCE AS LS
        INNER JOIN dbo.RII_WT_SOURCE_DOCUMENT AS SD ON SD.Id = LS.WtSourceDocumentId
        INNER JOIN dbo.RII_WT_HEADER AS H ON H.Id = SD.WtHeaderId
        WHERE LS.IsDeleted = 0 AND SD.IsDeleted = 0 AND H.IsDeleted = 0
          AND H.Status <> 11 AND H.ErpIntegrationStatus <> 4
          AND (@BranchCode IS NULL OR @BranchCode = '''' OR H.BranchCode = @BranchCode)
        GROUP BY LS.ExternalLineId
    )
    SELECT
        ''L'' AS Mode,
        X.FisNo AS SiparisNo,
        X.OrderID,
        X.OrderLineSequence,
        X.StockCode,
        X.StockName,
        X.UnitCode,
        CAST('''' AS VARCHAR(100)) AS YapKod,
        CAST('''' AS VARCHAR(100)) AS YapAcik,
        X.CustomerCode,
        X.CustomerName,
        X.BranchCode,
        X.TargetWh,
        X.ProjectCode,
        X.OrderDate,
        X.DeliveryDate,
        X.NetUnitPrice,
        X.GrossUnitPrice,
        X.OrderedQty,
        X.DeliveredQty,
        X.RemainingHamax,
        CAST(ISNULL(A.PlannedQtyAllocated, 0) AS DECIMAL(18, 4)) AS PlannedQtyAllocated,
        CAST(X.RemainingHamax - ISNULL(A.PlannedQtyAllocated, 0) AS DECIMAL(18, 4)) AS RemainingForImport
    FROM OrderLines AS X
    LEFT JOIN ActiveAllocations AS A ON A.ExternalLineId = CONVERT(NVARCHAR(100), X.OrderID)
    WHERE X.RemainingHamax > 0;';
    END
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260730153748_LinkTransferShipmentAndWarehouseSlipsToNetsisOrders'
)
BEGIN
    IF OBJECT_ID(N'dbo.RII_FN_SH_LINE') IS NULL
    BEGIN
        EXEC sys.sp_executesql N'CREATE FUNCTION dbo.RII_FN_SH_LINE
    (
        @SiparisNoCsv NVARCHAR(MAX),
        @BranchCode VARCHAR(10) = NULL
    )
    RETURNS TABLE
    AS
    RETURN
    WITH OrderRows AS
    (
        SELECT
            S.FISNO AS FisNo,
            S.SUBE_KODU AS BranchCode,
            S.DEPO_KODU AS TargetWh,
            NULLIF(LTRIM(RTRIM(S.PROJE_KODU)), '''') AS ProjectCode,
            COALESCE(
                TRY_CONVERT(DATETIME, S.STHAR_TESTAR, 104),
                TRY_CONVERT(DATETIME, M.SIPARIS_TEST, 104)) AS DeliveryDate,
            CAST(ISNULL(S.STHAR_NF, 0) AS DECIMAL(28, 8)) AS NetUnitPrice,
            CAST(ISNULL(S.STHAR_BF, 0) AS DECIMAL(28, 8)) AS GrossUnitPrice,
            S.STHAR_TARIH AS OrderDate,
            S.INCKEYNO AS OrderID,
            S.SIRA AS OrderLineSequence,
            S.STOK_KODU AS StockCode,
            ST.STOK_ADI AS StockName,
            ST.OLCU_BR1 AS UnitCode,
            COALESCE(M.CARI_KODU, S.STHAR_ACIKLAMA) AS CustomerCode,
            C.CARI_ISIM AS CustomerName,
            CAST(CASE WHEN ISNULL(S.L_YEDEK9, 0) = -1 THEN S.STHAR_GCMIK2 ELSE S.STHAR_GCMIK END AS DECIMAL(18, 4)) AS OrderedQty,
            CAST(ISNULL(S.FIRMA_DOVTUT, 0) AS DECIMAL(18, 4)) AS DeliveredQty
        FROM V3RIICO.dbo.TBLSIPATRA AS S WITH (NOLOCK)
        LEFT JOIN V3RIICO.dbo.TBLSIPAMAS AS M WITH (NOLOCK)
          ON M.FATIRS_NO = S.FISNO
         AND M.FTIRSIP = S.STHAR_FTIRSIP
         AND M.SUBE_KODU = S.SUBE_KODU
        LEFT JOIN V3RIICO.dbo.TBLCASABIT AS C WITH (NOLOCK)
          ON C.CARI_KOD = COALESCE(M.CARI_KODU, S.STHAR_ACIKLAMA)
        LEFT JOIN V3RIICO.dbo.TBLASORTIMAS AS A WITH (NOLOCK) ON A.ASORTIKOD = S.EKALAN1
        LEFT JOIN V3RIICO.dbo.TBLSTSABIT AS ST WITH (NOLOCK) ON ST.STOK_KODU = S.STOK_KODU
        WHERE S.STHAR_FTIRSIP = ''6''
          AND S.STHAR_GCKOD = ''C''
          AND S.STHAR_HTUR <> ''K''
          AND ISNULL(S.L_YEDEK9, 0) <= 0
          AND (A.ASORTIKOD IS NULL OR S.REDNEDEN <> 2)
          AND (CASE WHEN ISNULL(S.L_YEDEK9, 0) = -1 THEN S.STHAR_GCMIK2 ELSE S.STHAR_GCMIK END)
              - ISNULL(S.FIRMA_DOVTUT, 0) > 0
          AND (@BranchCode IS NULL OR @BranchCode = '''' OR CONVERT(VARCHAR(10), S.SUBE_KODU) = @BranchCode)
          AND (NULLIF(REPLACE(LTRIM(RTRIM(@SiparisNoCsv)), N'' '', N''''), N'''') IS NULL
               OR CHARINDEX(
                   N'','' + LTRIM(RTRIM(CONVERT(NVARCHAR(100), S.FISNO))) + N'','',
                   N'','' + REPLACE(@SiparisNoCsv, N'' '', N'''') + N'','') > 0)
    ),
    OrderLines AS
    (
        SELECT
            FisNo, BranchCode, TargetWh, MAX(ProjectCode) AS ProjectCode,
            MIN(OrderDate) AS OrderDate, MIN(DeliveryDate) AS DeliveryDate,
            MAX(NetUnitPrice) AS NetUnitPrice, MAX(GrossUnitPrice) AS GrossUnitPrice,
            OrderID, MAX(OrderLineSequence) AS OrderLineSequence,
            StockCode, MAX(StockName) AS StockName, MAX(UnitCode) AS UnitCode,
            MAX(CustomerCode) AS CustomerCode, MAX(CustomerName) AS CustomerName,
            SUM(OrderedQty) AS OrderedQty, SUM(DeliveredQty) AS DeliveredQty,
            CAST(SUM(OrderedQty - DeliveredQty) AS DECIMAL(18, 4)) AS RemainingHamax
        FROM OrderRows
        GROUP BY FisNo, BranchCode, TargetWh, OrderID, StockCode
    ),
    ActiveAllocations AS
    (
        SELECT
            LS.ExternalLineId,
            SUM(ISNULL(LS.AllocatedQuantity, 0)) AS PlannedQtyAllocated
        FROM dbo.RII_SH_LINE_SOURCE AS LS
        INNER JOIN dbo.RII_SH_SOURCE_DOCUMENT AS SD ON SD.Id = LS.ShipmentSourceDocumentId
        INNER JOIN dbo.RII_SH_HEADER AS H ON H.Id = SD.ShipmentHeaderId
        WHERE LS.IsDeleted = 0 AND SD.IsDeleted = 0 AND H.IsDeleted = 0
          AND H.Status <> 11 AND H.ErpIntegrationStatus <> 4
          AND (@BranchCode IS NULL OR @BranchCode = '''' OR H.BranchCode = @BranchCode)
        GROUP BY LS.ExternalLineId
    )
    SELECT
        ''L'' AS Mode,
        X.FisNo AS SiparisNo,
        X.OrderID,
        X.OrderLineSequence,
        X.StockCode,
        X.StockName,
        X.UnitCode,
        CAST('''' AS VARCHAR(100)) AS YapKod,
        CAST('''' AS VARCHAR(100)) AS YapAcik,
        X.CustomerCode,
        X.CustomerName,
        X.BranchCode,
        X.TargetWh,
        X.ProjectCode,
        X.OrderDate,
        X.DeliveryDate,
        X.NetUnitPrice,
        X.GrossUnitPrice,
        X.OrderedQty,
        X.DeliveredQty,
        X.RemainingHamax,
        CAST(ISNULL(A.PlannedQtyAllocated, 0) AS DECIMAL(18, 4)) AS PlannedQtyAllocated,
        CAST(X.RemainingHamax - ISNULL(A.PlannedQtyAllocated, 0) AS DECIMAL(18, 4)) AS RemainingForImport
    FROM OrderLines AS X
    LEFT JOIN ActiveAllocations AS A ON A.ExternalLineId = CONVERT(NVARCHAR(100), X.OrderID)
    WHERE X.RemainingHamax > 0;';
    END
    ELSE
    BEGIN
        EXEC sys.sp_executesql N'ALTER FUNCTION dbo.RII_FN_SH_LINE
    (
        @SiparisNoCsv NVARCHAR(MAX),
        @BranchCode VARCHAR(10) = NULL
    )
    RETURNS TABLE
    AS
    RETURN
    WITH OrderRows AS
    (
        SELECT
            S.FISNO AS FisNo,
            S.SUBE_KODU AS BranchCode,
            S.DEPO_KODU AS TargetWh,
            NULLIF(LTRIM(RTRIM(S.PROJE_KODU)), '''') AS ProjectCode,
            COALESCE(
                TRY_CONVERT(DATETIME, S.STHAR_TESTAR, 104),
                TRY_CONVERT(DATETIME, M.SIPARIS_TEST, 104)) AS DeliveryDate,
            CAST(ISNULL(S.STHAR_NF, 0) AS DECIMAL(28, 8)) AS NetUnitPrice,
            CAST(ISNULL(S.STHAR_BF, 0) AS DECIMAL(28, 8)) AS GrossUnitPrice,
            S.STHAR_TARIH AS OrderDate,
            S.INCKEYNO AS OrderID,
            S.SIRA AS OrderLineSequence,
            S.STOK_KODU AS StockCode,
            ST.STOK_ADI AS StockName,
            ST.OLCU_BR1 AS UnitCode,
            COALESCE(M.CARI_KODU, S.STHAR_ACIKLAMA) AS CustomerCode,
            C.CARI_ISIM AS CustomerName,
            CAST(CASE WHEN ISNULL(S.L_YEDEK9, 0) = -1 THEN S.STHAR_GCMIK2 ELSE S.STHAR_GCMIK END AS DECIMAL(18, 4)) AS OrderedQty,
            CAST(ISNULL(S.FIRMA_DOVTUT, 0) AS DECIMAL(18, 4)) AS DeliveredQty
        FROM V3RIICO.dbo.TBLSIPATRA AS S WITH (NOLOCK)
        LEFT JOIN V3RIICO.dbo.TBLSIPAMAS AS M WITH (NOLOCK)
          ON M.FATIRS_NO = S.FISNO
         AND M.FTIRSIP = S.STHAR_FTIRSIP
         AND M.SUBE_KODU = S.SUBE_KODU
        LEFT JOIN V3RIICO.dbo.TBLCASABIT AS C WITH (NOLOCK)
          ON C.CARI_KOD = COALESCE(M.CARI_KODU, S.STHAR_ACIKLAMA)
        LEFT JOIN V3RIICO.dbo.TBLASORTIMAS AS A WITH (NOLOCK) ON A.ASORTIKOD = S.EKALAN1
        LEFT JOIN V3RIICO.dbo.TBLSTSABIT AS ST WITH (NOLOCK) ON ST.STOK_KODU = S.STOK_KODU
        WHERE S.STHAR_FTIRSIP = ''6''
          AND S.STHAR_GCKOD = ''C''
          AND S.STHAR_HTUR <> ''K''
          AND ISNULL(S.L_YEDEK9, 0) <= 0
          AND (A.ASORTIKOD IS NULL OR S.REDNEDEN <> 2)
          AND (CASE WHEN ISNULL(S.L_YEDEK9, 0) = -1 THEN S.STHAR_GCMIK2 ELSE S.STHAR_GCMIK END)
              - ISNULL(S.FIRMA_DOVTUT, 0) > 0
          AND (@BranchCode IS NULL OR @BranchCode = '''' OR CONVERT(VARCHAR(10), S.SUBE_KODU) = @BranchCode)
          AND (NULLIF(REPLACE(LTRIM(RTRIM(@SiparisNoCsv)), N'' '', N''''), N'''') IS NULL
               OR CHARINDEX(
                   N'','' + LTRIM(RTRIM(CONVERT(NVARCHAR(100), S.FISNO))) + N'','',
                   N'','' + REPLACE(@SiparisNoCsv, N'' '', N'''') + N'','') > 0)
    ),
    OrderLines AS
    (
        SELECT
            FisNo, BranchCode, TargetWh, MAX(ProjectCode) AS ProjectCode,
            MIN(OrderDate) AS OrderDate, MIN(DeliveryDate) AS DeliveryDate,
            MAX(NetUnitPrice) AS NetUnitPrice, MAX(GrossUnitPrice) AS GrossUnitPrice,
            OrderID, MAX(OrderLineSequence) AS OrderLineSequence,
            StockCode, MAX(StockName) AS StockName, MAX(UnitCode) AS UnitCode,
            MAX(CustomerCode) AS CustomerCode, MAX(CustomerName) AS CustomerName,
            SUM(OrderedQty) AS OrderedQty, SUM(DeliveredQty) AS DeliveredQty,
            CAST(SUM(OrderedQty - DeliveredQty) AS DECIMAL(18, 4)) AS RemainingHamax
        FROM OrderRows
        GROUP BY FisNo, BranchCode, TargetWh, OrderID, StockCode
    ),
    ActiveAllocations AS
    (
        SELECT
            LS.ExternalLineId,
            SUM(ISNULL(LS.AllocatedQuantity, 0)) AS PlannedQtyAllocated
        FROM dbo.RII_SH_LINE_SOURCE AS LS
        INNER JOIN dbo.RII_SH_SOURCE_DOCUMENT AS SD ON SD.Id = LS.ShipmentSourceDocumentId
        INNER JOIN dbo.RII_SH_HEADER AS H ON H.Id = SD.ShipmentHeaderId
        WHERE LS.IsDeleted = 0 AND SD.IsDeleted = 0 AND H.IsDeleted = 0
          AND H.Status <> 11 AND H.ErpIntegrationStatus <> 4
          AND (@BranchCode IS NULL OR @BranchCode = '''' OR H.BranchCode = @BranchCode)
        GROUP BY LS.ExternalLineId
    )
    SELECT
        ''L'' AS Mode,
        X.FisNo AS SiparisNo,
        X.OrderID,
        X.OrderLineSequence,
        X.StockCode,
        X.StockName,
        X.UnitCode,
        CAST('''' AS VARCHAR(100)) AS YapKod,
        CAST('''' AS VARCHAR(100)) AS YapAcik,
        X.CustomerCode,
        X.CustomerName,
        X.BranchCode,
        X.TargetWh,
        X.ProjectCode,
        X.OrderDate,
        X.DeliveryDate,
        X.NetUnitPrice,
        X.GrossUnitPrice,
        X.OrderedQty,
        X.DeliveredQty,
        X.RemainingHamax,
        CAST(ISNULL(A.PlannedQtyAllocated, 0) AS DECIMAL(18, 4)) AS PlannedQtyAllocated,
        CAST(X.RemainingHamax - ISNULL(A.PlannedQtyAllocated, 0) AS DECIMAL(18, 4)) AS RemainingForImport
    FROM OrderLines AS X
    LEFT JOIN ActiveAllocations AS A ON A.ExternalLineId = CONVERT(NVARCHAR(100), X.OrderID)
    WHERE X.RemainingHamax > 0;';
    END
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260730153748_LinkTransferShipmentAndWarehouseSlipsToNetsisOrders'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260730153748_LinkTransferShipmentAndWarehouseSlipsToNetsisOrders', N'10.0.10');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260801043004_AddSteelVehicleAcceptedPlate'
)
BEGIN
    ALTER TABLE [RII_STEEL_VEHICLE_ACCEPTANCE] DROP CONSTRAINT [CK_RII_STEEL_VEHICLE_ACCEPTANCE_QTY];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260801043004_AddSteelVehicleAcceptedPlate'
)
BEGIN
    CREATE TABLE [RII_STEEL_VEHICLE_ACCEPTED_PLATE] (
        [Id] bigint NOT NULL IDENTITY,
        [VehicleCheckInId] bigint NOT NULL,
        [VehicleAcceptanceId] bigint NOT NULL,
        [SequenceNo] int NOT NULL,
        [IdentityStatus] nvarchar(30) NOT NULL,
        [PlanLineId] bigint NULL,
        [ResolvedAtUtc] datetimeoffset NULL,
        [ResolvedBy] bigint NULL,
        [RowVersion] rowversion NOT NULL,
        [BranchCode] nvarchar(10) NOT NULL DEFAULT N'0',
        [CreatedDate] datetime2 NULL,
        [UpdatedDate] datetime2 NULL,
        [DeletedDate] datetime2 NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [CreatedBy] bigint NULL,
        [UpdatedBy] bigint NULL,
        [DeletedBy] bigint NULL,
        CONSTRAINT [PK_RII_STEEL_VEHICLE_ACCEPTED_PLATE] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_RII_STEEL_ACCEPTED_PLATE_SEQUENCE] CHECK ([SequenceNo] > 0),
        CONSTRAINT [FK_RII_STEEL_VEHICLE_ACCEPTED_PLATE_RII_STEEL_RECEIPT_PLAN_LINE_PlanLineId] FOREIGN KEY ([PlanLineId]) REFERENCES [RII_STEEL_RECEIPT_PLAN_LINE] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_RII_STEEL_VEHICLE_ACCEPTED_PLATE_RII_STEEL_VEHICLE_ACCEPTANCE_VehicleAcceptanceId] FOREIGN KEY ([VehicleAcceptanceId]) REFERENCES [RII_STEEL_VEHICLE_ACCEPTANCE] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_RII_STEEL_VEHICLE_ACCEPTED_PLATE_RII_VEHICLE_CHECKIN_HEADER_VehicleCheckInId] FOREIGN KEY ([VehicleCheckInId]) REFERENCES [RII_VEHICLE_CHECKIN_HEADER] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260801043004_AddSteelVehicleAcceptedPlate'
)
BEGIN
    EXEC(N'ALTER TABLE [RII_STEEL_VEHICLE_ACCEPTANCE] ADD CONSTRAINT [CK_RII_STEEL_VEHICLE_ACCEPTANCE_QTY] CHECK ([TotalAcceptedQuantity] >= 0)');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260801043004_AddSteelVehicleAcceptedPlate'
)
BEGIN
    CREATE INDEX [IX_RII_STEEL_VEHICLE_ACCEPTED_PLATE_IsDeleted] ON [RII_STEEL_VEHICLE_ACCEPTED_PLATE] ([IsDeleted]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260801043004_AddSteelVehicleAcceptedPlate'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_RII_STEEL_VEHICLE_ACCEPTED_PLATE_PlanLineId] ON [RII_STEEL_VEHICLE_ACCEPTED_PLATE] ([PlanLineId]) WHERE [PlanLineId] IS NOT NULL AND [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260801043004_AddSteelVehicleAcceptedPlate'
)
BEGIN
    CREATE INDEX [IX_RII_STEEL_VEHICLE_ACCEPTED_PLATE_VehicleAcceptanceId] ON [RII_STEEL_VEHICLE_ACCEPTED_PLATE] ([VehicleAcceptanceId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260801043004_AddSteelVehicleAcceptedPlate'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_RII_STEEL_VEHICLE_ACCEPTED_PLATE_VehicleAcceptanceId_SequenceNo] ON [RII_STEEL_VEHICLE_ACCEPTED_PLATE] ([VehicleAcceptanceId], [SequenceNo]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260801043004_AddSteelVehicleAcceptedPlate'
)
BEGIN
    CREATE INDEX [IX_RII_STEEL_VEHICLE_ACCEPTED_PLATE_VehicleCheckInId] ON [RII_STEEL_VEHICLE_ACCEPTED_PLATE] ([VehicleCheckInId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260801043004_AddSteelVehicleAcceptedPlate'
)
BEGIN
    EXEC sys.sp_executesql N'INSERT INTO [RII_PERMISSION_DEFINITIONS]
    (
        [AvailableOnMobile],
        [AvailableOnWeb],
        [BranchCode],
        [Code],
        [CreatedDate],
        [Description],
        [IsActive],
        [Name],
        [IsDeleted]
    )
    SELECT
        CAST(0 AS bit),
        CAST(1 AS bit),
        N''0'',
        N''WMS.VEHICLECHECKIN.UNKNOWN_PLATE_RESOLVE'',
        SYSUTCDATETIME(),
        N''Created by migration 20260801043004_AddSteelVehicleAcceptedPlate'',
        CAST(1 AS bit),
        N''Bilinmeyen SAC levhalarını eşleştir'',
        CAST(0 AS bit)
    WHERE NOT EXISTS
    (
        SELECT 1
        FROM [RII_PERMISSION_DEFINITIONS]
        WHERE [Code] = N''WMS.VEHICLECHECKIN.UNKNOWN_PLATE_RESOLVE''
          AND [IsDeleted] = CAST(0 AS bit)
    );

    INSERT INTO [RII_PERMISSION_GROUP_PERMISSIONS]
        ([BranchCode], [CreatedDate], [IsDeleted], [PermissionDefinitionId], [PermissionGroupId])
    SELECT
        N''0'',
        SYSUTCDATETIME(),
        CAST(0 AS bit),
        permission.[Id],
        CAST(1001 AS bigint)
    FROM [RII_PERMISSION_DEFINITIONS] AS permission
    WHERE permission.[Code] = N''WMS.VEHICLECHECKIN.UNKNOWN_PLATE_RESOLVE''
      AND permission.[IsDeleted] = CAST(0 AS bit)
      AND NOT EXISTS
      (
          SELECT 1
          FROM [RII_PERMISSION_GROUP_PERMISSIONS] AS existing
          WHERE existing.[PermissionGroupId] = CAST(1001 AS bigint)
            AND existing.[PermissionDefinitionId] = permission.[Id]
            AND existing.[IsDeleted] = CAST(0 AS bit)
      );';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260801043004_AddSteelVehicleAcceptedPlate'
)
BEGIN
    EXEC sys.sp_executesql N';WITH LegacyAcceptedLines AS
    (
        SELECT
            planLine.[Id] AS [PlanLineId],
            planLine.[VehicleAcceptanceId],
            acceptance.[VehicleCheckInId],
            acceptance.[BranchCode],
            CAST(ROW_NUMBER() OVER
            (
                PARTITION BY planLine.[VehicleAcceptanceId]
                ORDER BY planLine.[Id]
            ) AS int) AS [SequenceNo],
            COALESCE(acceptance.[CreatedBy], acceptance.[AcceptedBy]) AS [CreatedBy],
            COALESCE(
                acceptance.[CreatedDate],
                CONVERT(datetime2, acceptance.[AcceptedAtUtc])
            ) AS [CreatedDate]
        FROM [RII_STEEL_RECEIPT_PLAN_LINE] AS planLine
        INNER JOIN [RII_STEEL_VEHICLE_ACCEPTANCE] AS acceptance
            ON acceptance.[Id] = planLine.[VehicleAcceptanceId]
        WHERE planLine.[VehicleAcceptanceId] IS NOT NULL
          AND planLine.[IsDeleted] = CAST(0 AS bit)
          AND acceptance.[IsDeleted] = CAST(0 AS bit)
    )
    INSERT INTO [RII_STEEL_VEHICLE_ACCEPTED_PLATE]
    (
        [VehicleCheckInId],
        [VehicleAcceptanceId],
        [SequenceNo],
        [IdentityStatus],
        [PlanLineId],
        [ResolvedAtUtc],
        [ResolvedBy],
        [BranchCode],
        [CreatedDate],
        [UpdatedDate],
        [DeletedDate],
        [IsDeleted],
        [CreatedBy],
        [UpdatedBy],
        [DeletedBy]
    )
    SELECT
        legacy.[VehicleCheckInId],
        legacy.[VehicleAcceptanceId],
        legacy.[SequenceNo],
        N''Known'',
        legacy.[PlanLineId],
        NULL,
        NULL,
        legacy.[BranchCode],
        legacy.[CreatedDate],
        NULL,
        NULL,
        CAST(0 AS bit),
        legacy.[CreatedBy],
        NULL,
        NULL
    FROM LegacyAcceptedLines AS legacy
    WHERE NOT EXISTS
    (
        SELECT 1
        FROM [RII_STEEL_VEHICLE_ACCEPTED_PLATE] AS existing
        WHERE existing.[PlanLineId] = legacy.[PlanLineId]
            AND existing.[IsDeleted] = CAST(0 AS bit)
    );';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260801043004_AddSteelVehicleAcceptedPlate'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260801043004_AddSteelVehicleAcceptedPlate', N'10.0.10');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260803093322_AddKkdEntitlementFoundation'
)
BEGIN
    CREATE TABLE [RII_KKD_DEPARTMENT] (
        [Id] bigint NOT NULL IDENTITY,
        [Code] nvarchar(50) NOT NULL,
        [Name] nvarchar(200) NOT NULL,
        [IsActive] bit NOT NULL,
        [RowVersion] rowversion NOT NULL,
        [BranchCode] nvarchar(10) NOT NULL DEFAULT N'0',
        [CreatedDate] datetime2 NULL,
        [UpdatedDate] datetime2 NULL,
        [DeletedDate] datetime2 NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [CreatedBy] bigint NULL,
        [UpdatedBy] bigint NULL,
        [DeletedBy] bigint NULL,
        CONSTRAINT [PK_RII_KKD_DEPARTMENT] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260803093322_AddKkdEntitlementFoundation'
)
BEGIN
    CREATE TABLE [RII_KKD_VALIDATION_LOG] (
        [Id] bigint NOT NULL IDENTITY,
        [CorrelationId] uniqueidentifier NOT NULL,
        [EmployeeId] bigint NULL,
        [StockId] bigint NULL,
        [GroupCode] nvarchar(80) NULL,
        [WarehouseId] bigint NULL,
        [AttemptedQuantity] decimal(20,6) NOT NULL,
        [ReasonCode] nvarchar(80) NOT NULL,
        [Message] nvarchar(2000) NULL,
        [DeviceInfo] nvarchar(1000) NULL,
        [BranchCode] nvarchar(10) NOT NULL DEFAULT N'0',
        [CreatedDate] datetime2 NULL,
        [UpdatedDate] datetime2 NULL,
        [DeletedDate] datetime2 NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [CreatedBy] bigint NULL,
        [UpdatedBy] bigint NULL,
        [DeletedBy] bigint NULL,
        CONSTRAINT [PK_RII_KKD_VALIDATION_LOG] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260803093322_AddKkdEntitlementFoundation'
)
BEGIN
    CREATE TABLE [RII_KKD_ROLE] (
        [Id] bigint NOT NULL IDENTITY,
        [DepartmentId] bigint NULL,
        [Code] nvarchar(50) NOT NULL,
        [Name] nvarchar(200) NOT NULL,
        [IsActive] bit NOT NULL,
        [RowVersion] rowversion NOT NULL,
        [BranchCode] nvarchar(10) NOT NULL DEFAULT N'0',
        [CreatedDate] datetime2 NULL,
        [UpdatedDate] datetime2 NULL,
        [DeletedDate] datetime2 NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [CreatedBy] bigint NULL,
        [UpdatedBy] bigint NULL,
        [DeletedBy] bigint NULL,
        CONSTRAINT [PK_RII_KKD_ROLE] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_RII_KKD_ROLE_RII_KKD_DEPARTMENT_DepartmentId] FOREIGN KEY ([DepartmentId]) REFERENCES [RII_KKD_DEPARTMENT] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260803093322_AddKkdEntitlementFoundation'
)
BEGIN
    CREATE TABLE [RII_KKD_EMPLOYEE] (
        [Id] bigint NOT NULL IDENTITY,
        [UserId] bigint NULL,
        [CustomerId] bigint NOT NULL,
        [EmployeeCode] nvarchar(80) NOT NULL,
        [FirstName] nvarchar(100) NOT NULL,
        [LastName] nvarchar(100) NOT NULL,
        [DepartmentId] bigint NOT NULL,
        [RoleId] bigint NOT NULL,
        [QrCode] nvarchar(200) NOT NULL,
        [EmploymentStartDate] date NOT NULL,
        [IsActive] bit NOT NULL,
        [LastSyncDate] datetime2 NULL,
        [RowVersion] rowversion NOT NULL,
        [BranchCode] nvarchar(10) NOT NULL DEFAULT N'0',
        [CreatedDate] datetime2 NULL,
        [UpdatedDate] datetime2 NULL,
        [DeletedDate] datetime2 NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [CreatedBy] bigint NULL,
        [UpdatedBy] bigint NULL,
        [DeletedBy] bigint NULL,
        CONSTRAINT [PK_RII_KKD_EMPLOYEE] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_RII_KKD_EMPLOYEE_RII_KKD_DEPARTMENT_DepartmentId] FOREIGN KEY ([DepartmentId]) REFERENCES [RII_KKD_DEPARTMENT] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_RII_KKD_EMPLOYEE_RII_KKD_ROLE_RoleId] FOREIGN KEY ([RoleId]) REFERENCES [RII_KKD_ROLE] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260803093322_AddKkdEntitlementFoundation'
)
BEGIN
    CREATE TABLE [RII_KKD_MATRIX] (
        [Id] bigint NOT NULL IDENTITY,
        [CustomerId] bigint NOT NULL,
        [DepartmentId] bigint NOT NULL,
        [RoleId] bigint NOT NULL,
        [Code] nvarchar(80) NOT NULL,
        [Name] nvarchar(200) NOT NULL,
        [EffectiveFrom] date NULL,
        [EffectiveTo] date NULL,
        [IsActive] bit NOT NULL,
        [Description] nvarchar(1000) NULL,
        [RowVersion] rowversion NOT NULL,
        [BranchCode] nvarchar(10) NOT NULL DEFAULT N'0',
        [CreatedDate] datetime2 NULL,
        [UpdatedDate] datetime2 NULL,
        [DeletedDate] datetime2 NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [CreatedBy] bigint NULL,
        [UpdatedBy] bigint NULL,
        [DeletedBy] bigint NULL,
        CONSTRAINT [PK_RII_KKD_MATRIX] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_RII_KKD_MATRIX_DATES] CHECK ([EffectiveTo] IS NULL OR [EffectiveFrom] IS NULL OR [EffectiveTo] >= [EffectiveFrom]),
        CONSTRAINT [FK_RII_KKD_MATRIX_RII_KKD_DEPARTMENT_DepartmentId] FOREIGN KEY ([DepartmentId]) REFERENCES [RII_KKD_DEPARTMENT] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_RII_KKD_MATRIX_RII_KKD_ROLE_RoleId] FOREIGN KEY ([RoleId]) REFERENCES [RII_KKD_ROLE] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260803093322_AddKkdEntitlementFoundation'
)
BEGIN
    CREATE TABLE [RII_KKD_DISTRIBUTION] (
        [Id] bigint NOT NULL IDENTITY,
        [CorrelationId] uniqueidentifier NOT NULL,
        [EmployeeId] bigint NOT NULL,
        [CustomerId] bigint NOT NULL,
        [WarehouseId] bigint NOT NULL,
        [DocumentSeriesId] bigint NOT NULL,
        [DocumentNo] nvarchar(50) NOT NULL,
        [Status] nvarchar(30) NOT NULL,
        [WarehouseOutboundId] bigint NULL,
        [FailureReason] nvarchar(2000) NULL,
        [CompletedAtUtc] datetimeoffset NULL,
        [RowVersion] rowversion NOT NULL,
        [BranchCode] nvarchar(10) NOT NULL DEFAULT N'0',
        [CreatedDate] datetime2 NULL,
        [UpdatedDate] datetime2 NULL,
        [DeletedDate] datetime2 NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [CreatedBy] bigint NULL,
        [UpdatedBy] bigint NULL,
        [DeletedBy] bigint NULL,
        CONSTRAINT [PK_RII_KKD_DISTRIBUTION] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_RII_KKD_DISTRIBUTION_RII_KKD_EMPLOYEE_EmployeeId] FOREIGN KEY ([EmployeeId]) REFERENCES [RII_KKD_EMPLOYEE] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260803093322_AddKkdEntitlementFoundation'
)
BEGIN
    CREATE TABLE [RII_KKD_RULE] (
        [Id] bigint NOT NULL IDENTITY,
        [MatrixId] bigint NOT NULL,
        [GroupCode] nvarchar(80) NOT NULL,
        [GroupName] nvarchar(200) NULL,
        [StockId] bigint NULL,
        [StockCodeSnapshot] nvarchar(100) NULL,
        [StockNameSnapshot] nvarchar(300) NULL,
        [StandardCode] nvarchar(80) NULL,
        [StandardName] nvarchar(200) NULL,
        [AnnualIssueCount] int NULL,
        [AnnualQuantity] decimal(20,6) NULL,
        [MaxCarryQuantity] decimal(20,6) NULL,
        [AllowBulkIssue] bit NOT NULL,
        [IsMandatory] bit NOT NULL,
        [SortOrder] int NOT NULL,
        [IsActive] bit NOT NULL,
        [Description] nvarchar(1000) NULL,
        [RowVersion] rowversion NOT NULL,
        [BranchCode] nvarchar(10) NOT NULL DEFAULT N'0',
        [CreatedDate] datetime2 NULL,
        [UpdatedDate] datetime2 NULL,
        [DeletedDate] datetime2 NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [CreatedBy] bigint NULL,
        [UpdatedBy] bigint NULL,
        [DeletedBy] bigint NULL,
        CONSTRAINT [PK_RII_KKD_RULE] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_RII_KKD_RULE_ANNUAL_COUNT] CHECK ([AnnualIssueCount] IS NULL OR [AnnualIssueCount] > 0),
        CONSTRAINT [CK_RII_KKD_RULE_QUANTITY] CHECK (([AnnualQuantity] IS NULL OR [AnnualQuantity] >= 0) AND ([MaxCarryQuantity] IS NULL OR [MaxCarryQuantity] >= 0)),
        CONSTRAINT [FK_RII_KKD_RULE_RII_KKD_MATRIX_MatrixId] FOREIGN KEY ([MatrixId]) REFERENCES [RII_KKD_MATRIX] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260803093322_AddKkdEntitlementFoundation'
)
BEGIN
    CREATE TABLE [RII_KKD_DISTRIBUTION_LINE] (
        [Id] bigint NOT NULL IDENTITY,
        [DistributionId] bigint NOT NULL,
        [LineNo] int NOT NULL,
        [StockId] bigint NOT NULL,
        [StockCodeSnapshot] nvarchar(100) NOT NULL,
        [StockNameSnapshot] nvarchar(300) NULL,
        [GroupCode] nvarchar(80) NOT NULL,
        [Quantity] decimal(20,6) NOT NULL,
        [EntitledQuantity] decimal(20,6) NOT NULL,
        [ExcessQuantity] decimal(20,6) NOT NULL,
        [SourceLocationId] bigint NOT NULL,
        [LotNo] nvarchar(100) NULL,
        [SerialNo] nvarchar(200) NULL,
        [OpenOrderNo] nvarchar(100) NULL,
        [OpenOrderLineId] nvarchar(100) NULL,
        [RowVersion] rowversion NOT NULL,
        [BranchCode] nvarchar(10) NOT NULL DEFAULT N'0',
        [CreatedDate] datetime2 NULL,
        [UpdatedDate] datetime2 NULL,
        [DeletedDate] datetime2 NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [CreatedBy] bigint NULL,
        [UpdatedBy] bigint NULL,
        [DeletedBy] bigint NULL,
        CONSTRAINT [PK_RII_KKD_DISTRIBUTION_LINE] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_RII_KKD_DISTRIBUTION_LINE_QTY] CHECK ([Quantity] > 0 AND [EntitledQuantity] >= 0 AND [ExcessQuantity] >= 0 AND [EntitledQuantity] + [ExcessQuantity] = [Quantity]),
        CONSTRAINT [FK_RII_KKD_DISTRIBUTION_LINE_RII_KKD_DISTRIBUTION_DistributionId] FOREIGN KEY ([DistributionId]) REFERENCES [RII_KKD_DISTRIBUTION] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260803093322_AddKkdEntitlementFoundation'
)
BEGIN
    CREATE TABLE [RII_KKD_OVERRIDE] (
        [Id] bigint NOT NULL IDENTITY,
        [EmployeeId] bigint NOT NULL,
        [RuleId] bigint NULL,
        [GroupCode] nvarchar(80) NOT NULL,
        [Quantity] decimal(20,6) NOT NULL,
        [ConsumedQuantity] decimal(20,6) NOT NULL,
        [ValidFrom] date NOT NULL,
        [ValidTo] date NULL,
        [Reason] nvarchar(1000) NOT NULL,
        [ApprovedByUserId] bigint NOT NULL,
        [IsActive] bit NOT NULL,
        [RowVersion] rowversion NOT NULL,
        [BranchCode] nvarchar(10) NOT NULL DEFAULT N'0',
        [CreatedDate] datetime2 NULL,
        [UpdatedDate] datetime2 NULL,
        [DeletedDate] datetime2 NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [CreatedBy] bigint NULL,
        [UpdatedBy] bigint NULL,
        [DeletedBy] bigint NULL,
        CONSTRAINT [PK_RII_KKD_OVERRIDE] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_RII_KKD_OVERRIDE_QTY] CHECK ([Quantity] > 0 AND [ConsumedQuantity] >= 0 AND [ConsumedQuantity] <= [Quantity] AND ([ValidTo] IS NULL OR [ValidTo] >= [ValidFrom])),
        CONSTRAINT [FK_RII_KKD_OVERRIDE_RII_KKD_EMPLOYEE_EmployeeId] FOREIGN KEY ([EmployeeId]) REFERENCES [RII_KKD_EMPLOYEE] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_RII_KKD_OVERRIDE_RII_KKD_RULE_RuleId] FOREIGN KEY ([RuleId]) REFERENCES [RII_KKD_RULE] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260803093322_AddKkdEntitlementFoundation'
)
BEGIN
    CREATE TABLE [RII_KKD_PHASE] (
        [Id] bigint NOT NULL IDENTITY,
        [RuleId] bigint NOT NULL,
        [PhaseType] nvarchar(30) NOT NULL,
        [OffsetMonths] int NOT NULL,
        [Quantity] decimal(20,6) NOT NULL,
        [AllowBulkIssue] bit NOT NULL,
        [FrequencyDays] int NULL,
        [QuantityPerFrequency] decimal(20,6) NULL,
        [PeriodType] nvarchar(20) NULL,
        [PeriodInterval] int NULL,
        [SortOrder] int NOT NULL,
        [IsActive] bit NOT NULL,
        [Description] nvarchar(1000) NULL,
        [BranchCode] nvarchar(10) NOT NULL DEFAULT N'0',
        [CreatedDate] datetime2 NULL,
        [UpdatedDate] datetime2 NULL,
        [DeletedDate] datetime2 NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [CreatedBy] bigint NULL,
        [UpdatedBy] bigint NULL,
        [DeletedBy] bigint NULL,
        CONSTRAINT [PK_RII_KKD_PHASE] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_RII_KKD_PHASE_VALUES] CHECK ([Quantity] >= 0 AND [OffsetMonths] >= 0 AND ([FrequencyDays] IS NULL OR [FrequencyDays] > 0) AND ([PeriodInterval] IS NULL OR [PeriodInterval] > 0)),
        CONSTRAINT [FK_RII_KKD_PHASE_RII_KKD_RULE_RuleId] FOREIGN KEY ([RuleId]) REFERENCES [RII_KKD_RULE] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260803093322_AddKkdEntitlementFoundation'
)
BEGIN
    CREATE TABLE [RII_KKD_CONSUMPTION] (
        [Id] bigint NOT NULL IDENTITY,
        [EmployeeId] bigint NOT NULL,
        [DistributionId] bigint NOT NULL,
        [DistributionLineId] bigint NOT NULL,
        [StockId] bigint NOT NULL,
        [GroupCode] nvarchar(80) NOT NULL,
        [SourceType] nvarchar(30) NOT NULL,
        [MatrixId] bigint NULL,
        [RuleId] bigint NULL,
        [PhaseId] bigint NULL,
        [OverrideId] bigint NULL,
        [Quantity] decimal(20,6) NOT NULL,
        [ConsumedAtUtc] datetimeoffset NOT NULL,
        [IsReversal] bit NOT NULL,
        [ReversesConsumptionId] bigint NULL,
        [BranchCode] nvarchar(10) NOT NULL DEFAULT N'0',
        [CreatedDate] datetime2 NULL,
        [UpdatedDate] datetime2 NULL,
        [DeletedDate] datetime2 NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [CreatedBy] bigint NULL,
        [UpdatedBy] bigint NULL,
        [DeletedBy] bigint NULL,
        CONSTRAINT [PK_RII_KKD_CONSUMPTION] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_RII_KKD_CONSUMPTION_QTY] CHECK ([Quantity] > 0),
        CONSTRAINT [FK_RII_KKD_CONSUMPTION_RII_KKD_DISTRIBUTION_LINE_DistributionLineId] FOREIGN KEY ([DistributionLineId]) REFERENCES [RII_KKD_DISTRIBUTION_LINE] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260803093322_AddKkdEntitlementFoundation'
)
BEGIN
    CREATE TABLE [RII_KKD_DISTRIBUTION_ALLOCATION] (
        [Id] bigint NOT NULL IDENTITY,
        [DistributionLineId] bigint NOT NULL,
        [SourceType] nvarchar(30) NOT NULL,
        [SourceId] bigint NOT NULL,
        [Quantity] decimal(20,6) NOT NULL,
        [PeriodStart] date NOT NULL,
        [PeriodEnd] date NULL,
        [BranchCode] nvarchar(10) NOT NULL DEFAULT N'0',
        [CreatedDate] datetime2 NULL,
        [UpdatedDate] datetime2 NULL,
        [DeletedDate] datetime2 NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [CreatedBy] bigint NULL,
        [UpdatedBy] bigint NULL,
        [DeletedBy] bigint NULL,
        CONSTRAINT [PK_RII_KKD_DISTRIBUTION_ALLOCATION] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_RII_KKD_DISTRIBUTION_ALLOCATION_DATES] CHECK ([PeriodEnd] IS NULL OR [PeriodEnd] >= [PeriodStart]),
        CONSTRAINT [CK_RII_KKD_DISTRIBUTION_ALLOCATION_QTY] CHECK ([Quantity] > 0),
        CONSTRAINT [FK_RII_KKD_DISTRIBUTION_ALLOCATION_RII_KKD_DISTRIBUTION_LINE_DistributionLineId] FOREIGN KEY ([DistributionLineId]) REFERENCES [RII_KKD_DISTRIBUTION_LINE] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260803093322_AddKkdEntitlementFoundation'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'AvailableOnMobile', N'AvailableOnWeb', N'BranchCode', N'Code', N'CreatedBy', N'CreatedDate', N'DeletedBy', N'DeletedDate', N'Description', N'IsActive', N'Name', N'UpdatedBy', N'UpdatedDate') AND [object_id] = OBJECT_ID(N'[RII_PERMISSION_DEFINITIONS]'))
        SET IDENTITY_INSERT [RII_PERMISSION_DEFINITIONS] ON;
    EXEC(N'INSERT INTO [RII_PERMISSION_DEFINITIONS] ([Id], [AvailableOnMobile], [AvailableOnWeb], [BranchCode], [Code], [CreatedBy], [CreatedDate], [DeletedBy], [DeletedDate], [Description], [IsActive], [Name], [UpdatedBy], [UpdatedDate])
    VALUES (CAST(2500 AS bigint), CAST(0 AS bit), CAST(1 AS bit), N''0'', N''WMS.KKD.DEFINITIONS.VIEW'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, NULL, CAST(1 AS bit), N''KKD departman ve rol tanımlarını görüntüle'', NULL, NULL),
    (CAST(2501 AS bigint), CAST(0 AS bit), CAST(1 AS bit), N''0'', N''WMS.KKD.DEFINITIONS.MANAGE'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, NULL, CAST(1 AS bit), N''KKD departman ve rol tanımlarını yönet'', NULL, NULL),
    (CAST(2502 AS bigint), CAST(0 AS bit), CAST(1 AS bit), N''0'', N''WMS.KKD.EMPLOYEES.VIEW'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, NULL, CAST(1 AS bit), N''KKD personellerini görüntüle'', NULL, NULL),
    (CAST(2503 AS bigint), CAST(0 AS bit), CAST(1 AS bit), N''0'', N''WMS.KKD.EMPLOYEES.MANAGE'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, NULL, CAST(1 AS bit), N''KKD personellerini yönet'', NULL, NULL),
    (CAST(2504 AS bigint), CAST(0 AS bit), CAST(1 AS bit), N''0'', N''WMS.KKD.MATRICES.VIEW'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, NULL, CAST(1 AS bit), N''KKD hak matrislerini görüntüle'', NULL, NULL),
    (CAST(2505 AS bigint), CAST(0 AS bit), CAST(1 AS bit), N''0'', N''WMS.KKD.MATRICES.MANAGE'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, NULL, CAST(1 AS bit), N''KKD hak matrislerini yönet'', NULL, NULL),
    (CAST(2506 AS bigint), CAST(0 AS bit), CAST(1 AS bit), N''0'', N''WMS.KKD.OVERRIDES.MANAGE'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, NULL, CAST(1 AS bit), N''KKD personel ek haklarını yönet'', NULL, NULL),
    (CAST(2507 AS bigint), CAST(0 AS bit), CAST(1 AS bit), N''0'', N''WMS.KKD.ENTITLEMENT.CHECK'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, NULL, CAST(1 AS bit), N''KKD hak sorgulaması yap'', NULL, NULL),
    (CAST(2508 AS bigint), CAST(0 AS bit), CAST(1 AS bit), N''0'', N''WMS.KKD.DISTRIBUTION.OPERATE'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, NULL, CAST(1 AS bit), N''KKD dağıtım ve ambar çıkış işlemini yürüt'', NULL, NULL),
    (CAST(2509 AS bigint), CAST(0 AS bit), CAST(1 AS bit), N''0'', N''WMS.KKD.REPORTS.VIEW'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, NULL, CAST(1 AS bit), N''KKD raporlarını görüntüle'', NULL, NULL)');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'AvailableOnMobile', N'AvailableOnWeb', N'BranchCode', N'Code', N'CreatedBy', N'CreatedDate', N'DeletedBy', N'DeletedDate', N'Description', N'IsActive', N'Name', N'UpdatedBy', N'UpdatedDate') AND [object_id] = OBJECT_ID(N'[RII_PERMISSION_DEFINITIONS]'))
        SET IDENTITY_INSERT [RII_PERMISSION_DEFINITIONS] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260803093322_AddKkdEntitlementFoundation'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'BranchCode', N'CreatedBy', N'CreatedDate', N'DeletedBy', N'DeletedDate', N'PermissionDefinitionId', N'PermissionGroupId', N'UpdatedBy', N'UpdatedDate') AND [object_id] = OBJECT_ID(N'[RII_PERMISSION_GROUP_PERMISSIONS]'))
        SET IDENTITY_INSERT [RII_PERMISSION_GROUP_PERMISSIONS] ON;
    EXEC(N'INSERT INTO [RII_PERMISSION_GROUP_PERMISSIONS] ([Id], [BranchCode], [CreatedBy], [CreatedDate], [DeletedBy], [DeletedDate], [PermissionDefinitionId], [PermissionGroupId], [UpdatedBy], [UpdatedDate])
    VALUES (CAST(2500 AS bigint), N''0'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, CAST(2500 AS bigint), CAST(1001 AS bigint), NULL, NULL),
    (CAST(2501 AS bigint), N''0'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, CAST(2501 AS bigint), CAST(1001 AS bigint), NULL, NULL),
    (CAST(2502 AS bigint), N''0'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, CAST(2502 AS bigint), CAST(1001 AS bigint), NULL, NULL),
    (CAST(2503 AS bigint), N''0'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, CAST(2503 AS bigint), CAST(1001 AS bigint), NULL, NULL),
    (CAST(2504 AS bigint), N''0'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, CAST(2504 AS bigint), CAST(1001 AS bigint), NULL, NULL),
    (CAST(2505 AS bigint), N''0'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, CAST(2505 AS bigint), CAST(1001 AS bigint), NULL, NULL),
    (CAST(2506 AS bigint), N''0'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, CAST(2506 AS bigint), CAST(1001 AS bigint), NULL, NULL),
    (CAST(2507 AS bigint), N''0'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, CAST(2507 AS bigint), CAST(1001 AS bigint), NULL, NULL),
    (CAST(2508 AS bigint), N''0'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, CAST(2508 AS bigint), CAST(1001 AS bigint), NULL, NULL),
    (CAST(2509 AS bigint), N''0'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, CAST(2509 AS bigint), CAST(1001 AS bigint), NULL, NULL)');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'BranchCode', N'CreatedBy', N'CreatedDate', N'DeletedBy', N'DeletedDate', N'PermissionDefinitionId', N'PermissionGroupId', N'UpdatedBy', N'UpdatedDate') AND [object_id] = OBJECT_ID(N'[RII_PERMISSION_GROUP_PERMISSIONS]'))
        SET IDENTITY_INSERT [RII_PERMISSION_GROUP_PERMISSIONS] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260803093322_AddKkdEntitlementFoundation'
)
BEGIN
    CREATE INDEX [IX_RII_KKD_CONSUMPTION_BranchCode_EmployeeId_GroupCode_ConsumedAtUtc] ON [RII_KKD_CONSUMPTION] ([BranchCode], [EmployeeId], [GroupCode], [ConsumedAtUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260803093322_AddKkdEntitlementFoundation'
)
BEGIN
    CREATE INDEX [IX_RII_KKD_CONSUMPTION_DistributionLineId] ON [RII_KKD_CONSUMPTION] ([DistributionLineId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260803093322_AddKkdEntitlementFoundation'
)
BEGIN
    CREATE INDEX [IX_RII_KKD_CONSUMPTION_IsDeleted] ON [RII_KKD_CONSUMPTION] ([IsDeleted]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260803093322_AddKkdEntitlementFoundation'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_RII_KKD_CONSUMPTION_ReversesConsumptionId] ON [RII_KKD_CONSUMPTION] ([ReversesConsumptionId]) WHERE [ReversesConsumptionId] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260803093322_AddKkdEntitlementFoundation'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_RII_KKD_DEPARTMENT_BranchCode_Code] ON [RII_KKD_DEPARTMENT] ([BranchCode], [Code]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260803093322_AddKkdEntitlementFoundation'
)
BEGIN
    CREATE INDEX [IX_RII_KKD_DEPARTMENT_IsDeleted] ON [RII_KKD_DEPARTMENT] ([IsDeleted]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260803093322_AddKkdEntitlementFoundation'
)
BEGIN
    CREATE UNIQUE INDEX [IX_RII_KKD_DISTRIBUTION_BranchCode_DocumentNo] ON [RII_KKD_DISTRIBUTION] ([BranchCode], [DocumentNo]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260803093322_AddKkdEntitlementFoundation'
)
BEGIN
    CREATE UNIQUE INDEX [IX_RII_KKD_DISTRIBUTION_CorrelationId] ON [RII_KKD_DISTRIBUTION] ([CorrelationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260803093322_AddKkdEntitlementFoundation'
)
BEGIN
    CREATE INDEX [IX_RII_KKD_DISTRIBUTION_EmployeeId] ON [RII_KKD_DISTRIBUTION] ([EmployeeId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260803093322_AddKkdEntitlementFoundation'
)
BEGIN
    CREATE INDEX [IX_RII_KKD_DISTRIBUTION_IsDeleted] ON [RII_KKD_DISTRIBUTION] ([IsDeleted]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260803093322_AddKkdEntitlementFoundation'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_RII_KKD_DISTRIBUTION_WarehouseOutboundId] ON [RII_KKD_DISTRIBUTION] ([WarehouseOutboundId]) WHERE [WarehouseOutboundId] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260803093322_AddKkdEntitlementFoundation'
)
BEGIN
    CREATE INDEX [IX_RII_KKD_DISTRIBUTION_ALLOCATION_BranchCode_SourceType_SourceId_PeriodStart_PeriodEnd] ON [RII_KKD_DISTRIBUTION_ALLOCATION] ([BranchCode], [SourceType], [SourceId], [PeriodStart], [PeriodEnd]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260803093322_AddKkdEntitlementFoundation'
)
BEGIN
    CREATE UNIQUE INDEX [IX_RII_KKD_DISTRIBUTION_ALLOCATION_DistributionLineId_SourceType_SourceId_PeriodStart] ON [RII_KKD_DISTRIBUTION_ALLOCATION] ([DistributionLineId], [SourceType], [SourceId], [PeriodStart]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260803093322_AddKkdEntitlementFoundation'
)
BEGIN
    CREATE INDEX [IX_RII_KKD_DISTRIBUTION_ALLOCATION_IsDeleted] ON [RII_KKD_DISTRIBUTION_ALLOCATION] ([IsDeleted]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260803093322_AddKkdEntitlementFoundation'
)
BEGIN
    CREATE UNIQUE INDEX [IX_RII_KKD_DISTRIBUTION_LINE_DistributionId_LineNo] ON [RII_KKD_DISTRIBUTION_LINE] ([DistributionId], [LineNo]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260803093322_AddKkdEntitlementFoundation'
)
BEGIN
    CREATE INDEX [IX_RII_KKD_DISTRIBUTION_LINE_IsDeleted] ON [RII_KKD_DISTRIBUTION_LINE] ([IsDeleted]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260803093322_AddKkdEntitlementFoundation'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_RII_KKD_EMPLOYEE_BranchCode_EmployeeCode] ON [RII_KKD_EMPLOYEE] ([BranchCode], [EmployeeCode]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260803093322_AddKkdEntitlementFoundation'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_RII_KKD_EMPLOYEE_BranchCode_QrCode] ON [RII_KKD_EMPLOYEE] ([BranchCode], [QrCode]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260803093322_AddKkdEntitlementFoundation'
)
BEGIN
    CREATE INDEX [IX_RII_KKD_EMPLOYEE_DepartmentId] ON [RII_KKD_EMPLOYEE] ([DepartmentId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260803093322_AddKkdEntitlementFoundation'
)
BEGIN
    CREATE INDEX [IX_RII_KKD_EMPLOYEE_IsDeleted] ON [RII_KKD_EMPLOYEE] ([IsDeleted]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260803093322_AddKkdEntitlementFoundation'
)
BEGIN
    CREATE INDEX [IX_RII_KKD_EMPLOYEE_RoleId] ON [RII_KKD_EMPLOYEE] ([RoleId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260803093322_AddKkdEntitlementFoundation'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_RII_KKD_MATRIX_BranchCode_Code] ON [RII_KKD_MATRIX] ([BranchCode], [Code]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260803093322_AddKkdEntitlementFoundation'
)
BEGIN
    CREATE INDEX [IX_RII_KKD_MATRIX_BranchCode_CustomerId_DepartmentId_RoleId_IsActive] ON [RII_KKD_MATRIX] ([BranchCode], [CustomerId], [DepartmentId], [RoleId], [IsActive]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260803093322_AddKkdEntitlementFoundation'
)
BEGIN
    CREATE INDEX [IX_RII_KKD_MATRIX_DepartmentId] ON [RII_KKD_MATRIX] ([DepartmentId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260803093322_AddKkdEntitlementFoundation'
)
BEGIN
    CREATE INDEX [IX_RII_KKD_MATRIX_IsDeleted] ON [RII_KKD_MATRIX] ([IsDeleted]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260803093322_AddKkdEntitlementFoundation'
)
BEGIN
    CREATE INDEX [IX_RII_KKD_MATRIX_RoleId] ON [RII_KKD_MATRIX] ([RoleId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260803093322_AddKkdEntitlementFoundation'
)
BEGIN
    CREATE INDEX [IX_RII_KKD_OVERRIDE_BranchCode_EmployeeId_GroupCode_IsActive] ON [RII_KKD_OVERRIDE] ([BranchCode], [EmployeeId], [GroupCode], [IsActive]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260803093322_AddKkdEntitlementFoundation'
)
BEGIN
    CREATE INDEX [IX_RII_KKD_OVERRIDE_EmployeeId] ON [RII_KKD_OVERRIDE] ([EmployeeId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260803093322_AddKkdEntitlementFoundation'
)
BEGIN
    CREATE INDEX [IX_RII_KKD_OVERRIDE_IsDeleted] ON [RII_KKD_OVERRIDE] ([IsDeleted]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260803093322_AddKkdEntitlementFoundation'
)
BEGIN
    CREATE INDEX [IX_RII_KKD_OVERRIDE_RuleId] ON [RII_KKD_OVERRIDE] ([RuleId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260803093322_AddKkdEntitlementFoundation'
)
BEGIN
    CREATE INDEX [IX_RII_KKD_PHASE_IsDeleted] ON [RII_KKD_PHASE] ([IsDeleted]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260803093322_AddKkdEntitlementFoundation'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_RII_KKD_PHASE_RuleId_PhaseType_OffsetMonths] ON [RII_KKD_PHASE] ([RuleId], [PhaseType], [OffsetMonths]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260803093322_AddKkdEntitlementFoundation'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_RII_KKD_ROLE_BranchCode_DepartmentId_Code] ON [RII_KKD_ROLE] ([BranchCode], [DepartmentId], [Code]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260803093322_AddKkdEntitlementFoundation'
)
BEGIN
    CREATE INDEX [IX_RII_KKD_ROLE_DepartmentId] ON [RII_KKD_ROLE] ([DepartmentId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260803093322_AddKkdEntitlementFoundation'
)
BEGIN
    CREATE INDEX [IX_RII_KKD_ROLE_IsDeleted] ON [RII_KKD_ROLE] ([IsDeleted]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260803093322_AddKkdEntitlementFoundation'
)
BEGIN
    CREATE INDEX [IX_RII_KKD_RULE_BranchCode_StockId_GroupCode_IsActive] ON [RII_KKD_RULE] ([BranchCode], [StockId], [GroupCode], [IsActive]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260803093322_AddKkdEntitlementFoundation'
)
BEGIN
    CREATE INDEX [IX_RII_KKD_RULE_IsDeleted] ON [RII_KKD_RULE] ([IsDeleted]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260803093322_AddKkdEntitlementFoundation'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_RII_KKD_RULE_MatrixId_StockId_GroupCode] ON [RII_KKD_RULE] ([MatrixId], [StockId], [GroupCode]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260803093322_AddKkdEntitlementFoundation'
)
BEGIN
    CREATE INDEX [IX_RII_KKD_VALIDATION_LOG_BranchCode_CorrelationId] ON [RII_KKD_VALIDATION_LOG] ([BranchCode], [CorrelationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260803093322_AddKkdEntitlementFoundation'
)
BEGIN
    CREATE INDEX [IX_RII_KKD_VALIDATION_LOG_BranchCode_EmployeeId_CreatedDate] ON [RII_KKD_VALIDATION_LOG] ([BranchCode], [EmployeeId], [CreatedDate]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260803093322_AddKkdEntitlementFoundation'
)
BEGIN
    CREATE INDEX [IX_RII_KKD_VALIDATION_LOG_IsDeleted] ON [RII_KKD_VALIDATION_LOG] ([IsDeleted]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260803093322_AddKkdEntitlementFoundation'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260803093322_AddKkdEntitlementFoundation', N'10.0.10');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260803111314_AddKkdBranchProcessPolicy'
)
BEGIN
    CREATE TABLE [RII_KKD_POLICY] (
        [Id] bigint NOT NULL IDENTITY,
        [PolicyKey] nvarchar(30) NOT NULL,
        [RequireOpenOrder] bit NOT NULL,
        [AllowOpenOrderExcess] bit NOT NULL,
        [AllowMultipleOrdersPerDistribution] bit NOT NULL,
        [RequireEmployeeUserLink] bit NOT NULL,
        [AllowFutureDatedDistribution] bit NOT NULL,
        [RowVersion] rowversion NOT NULL,
        [BranchCode] nvarchar(10) NOT NULL DEFAULT N'0',
        [CreatedDate] datetime2 NULL,
        [UpdatedDate] datetime2 NULL,
        [DeletedDate] datetime2 NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [CreatedBy] bigint NULL,
        [UpdatedBy] bigint NULL,
        [DeletedBy] bigint NULL,
        CONSTRAINT [PK_RII_KKD_POLICY] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260803111314_AddKkdBranchProcessPolicy'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'AvailableOnMobile', N'AvailableOnWeb', N'BranchCode', N'Code', N'CreatedBy', N'CreatedDate', N'DeletedBy', N'DeletedDate', N'Description', N'IsActive', N'Name', N'UpdatedBy', N'UpdatedDate') AND [object_id] = OBJECT_ID(N'[RII_PERMISSION_DEFINITIONS]'))
        SET IDENTITY_INSERT [RII_PERMISSION_DEFINITIONS] ON;
    EXEC(N'INSERT INTO [RII_PERMISSION_DEFINITIONS] ([Id], [AvailableOnMobile], [AvailableOnWeb], [BranchCode], [Code], [CreatedBy], [CreatedDate], [DeletedBy], [DeletedDate], [Description], [IsActive], [Name], [UpdatedBy], [UpdatedDate])
    VALUES (CAST(2510 AS bigint), CAST(0 AS bit), CAST(1 AS bit), N''0'', N''WMS.KKD.POLICY.VIEW'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, NULL, CAST(1 AS bit), N''KKD süreç politikasını görüntüle'', NULL, NULL),
    (CAST(2511 AS bigint), CAST(0 AS bit), CAST(1 AS bit), N''0'', N''WMS.KKD.POLICY.MANAGE'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, NULL, CAST(1 AS bit), N''KKD süreç politikasını yönet'', NULL, NULL)');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'AvailableOnMobile', N'AvailableOnWeb', N'BranchCode', N'Code', N'CreatedBy', N'CreatedDate', N'DeletedBy', N'DeletedDate', N'Description', N'IsActive', N'Name', N'UpdatedBy', N'UpdatedDate') AND [object_id] = OBJECT_ID(N'[RII_PERMISSION_DEFINITIONS]'))
        SET IDENTITY_INSERT [RII_PERMISSION_DEFINITIONS] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260803111314_AddKkdBranchProcessPolicy'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'BranchCode', N'CreatedBy', N'CreatedDate', N'DeletedBy', N'DeletedDate', N'PermissionDefinitionId', N'PermissionGroupId', N'UpdatedBy', N'UpdatedDate') AND [object_id] = OBJECT_ID(N'[RII_PERMISSION_GROUP_PERMISSIONS]'))
        SET IDENTITY_INSERT [RII_PERMISSION_GROUP_PERMISSIONS] ON;
    EXEC(N'INSERT INTO [RII_PERMISSION_GROUP_PERMISSIONS] ([Id], [BranchCode], [CreatedBy], [CreatedDate], [DeletedBy], [DeletedDate], [PermissionDefinitionId], [PermissionGroupId], [UpdatedBy], [UpdatedDate])
    VALUES (CAST(2510 AS bigint), N''0'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, CAST(2510 AS bigint), CAST(1001 AS bigint), NULL, NULL),
    (CAST(2511 AS bigint), N''0'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, CAST(2511 AS bigint), CAST(1001 AS bigint), NULL, NULL)');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'BranchCode', N'CreatedBy', N'CreatedDate', N'DeletedBy', N'DeletedDate', N'PermissionDefinitionId', N'PermissionGroupId', N'UpdatedBy', N'UpdatedDate') AND [object_id] = OBJECT_ID(N'[RII_PERMISSION_GROUP_PERMISSIONS]'))
        SET IDENTITY_INSERT [RII_PERMISSION_GROUP_PERMISSIONS] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260803111314_AddKkdBranchProcessPolicy'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_RII_KKD_POLICY_BranchCode_PolicyKey] ON [RII_KKD_POLICY] ([BranchCode], [PolicyKey]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260803111314_AddKkdBranchProcessPolicy'
)
BEGIN
    CREATE INDEX [IX_RII_KKD_POLICY_IsDeleted] ON [RII_KKD_POLICY] ([IsDeleted]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260803111314_AddKkdBranchProcessPolicy'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260803111314_AddKkdBranchProcessPolicy', N'10.0.10');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260803150000_PrepareWarehouseTransferReturnLocation'
)
BEGIN
    ALTER TABLE [RII_WAREHOUSE] DROP CONSTRAINT [FK_RII_WAREHOUSE_RII_LOCATION_DefaultGoodsReceiptLocationId];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260803150000_PrepareWarehouseTransferReturnLocation'
)
BEGIN
    ALTER TABLE [RII_WAREHOUSE] ADD CONSTRAINT [FK_RII_WAREHOUSE_RII_LOCATION_DefaultGoodsReceiptLocationId] FOREIGN KEY ([DefaultGoodsReceiptLocationId]) REFERENCES [RII_LOCATION] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260803150000_PrepareWarehouseTransferReturnLocation'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260803150000_PrepareWarehouseTransferReturnLocation', N'10.0.10');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260803154454_AddProductionTransferCancellationAndTaskManagement'
)
BEGIN
    ALTER TABLE [RII_WT_POLICIES] ADD [CancellationReturnPolicy] nvarchar(40) NOT NULL DEFAULT N'OriginalSourceLocation';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260803154454_AddProductionTransferCancellationAndTaskManagement'
)
BEGIN
    ALTER TABLE [RII_WT_HEADER] ADD [CancellationReturnLocationId] bigint NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260803154454_AddProductionTransferCancellationAndTaskManagement'
)
BEGIN
    ALTER TABLE [RII_WT_HEADER] ADD [CancellationReturnPolicy] nvarchar(40) NOT NULL DEFAULT N'OriginalSourceLocation';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260803154454_AddProductionTransferCancellationAndTaskManagement'
)
BEGIN
    ALTER TABLE [RII_WAREHOUSE] ADD [DefaultTransferReturnLocationId] bigint NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260803154454_AddProductionTransferCancellationAndTaskManagement'
)
BEGIN
    ALTER TABLE [RII_PT_POLICIES] ADD [CancellationReturnPolicy] nvarchar(40) NOT NULL DEFAULT N'OriginalSourceLocation';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260803154454_AddProductionTransferCancellationAndTaskManagement'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'AvailableOnMobile', N'AvailableOnWeb', N'BranchCode', N'Code', N'CreatedBy', N'CreatedDate', N'DeletedBy', N'DeletedDate', N'Description', N'IsActive', N'Name', N'UpdatedBy', N'UpdatedDate') AND [object_id] = OBJECT_ID(N'[RII_PERMISSION_DEFINITIONS]'))
        SET IDENTITY_INSERT [RII_PERMISSION_DEFINITIONS] ON;
    EXEC(N'INSERT INTO [RII_PERMISSION_DEFINITIONS] ([Id], [AvailableOnMobile], [AvailableOnWeb], [BranchCode], [Code], [CreatedBy], [CreatedDate], [DeletedBy], [DeletedDate], [Description], [IsActive], [Name], [UpdatedBy], [UpdatedDate])
    VALUES (CAST(2409 AS bigint), CAST(0 AS bit), CAST(1 AS bit), N''0'', N''WMS.PRODUCTION_TRANSFER.ASSIGN'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, NULL, CAST(1 AS bit), N''Üretim transfer görevlerini ata ve kaldır'', NULL, NULL)');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'AvailableOnMobile', N'AvailableOnWeb', N'BranchCode', N'Code', N'CreatedBy', N'CreatedDate', N'DeletedBy', N'DeletedDate', N'Description', N'IsActive', N'Name', N'UpdatedBy', N'UpdatedDate') AND [object_id] = OBJECT_ID(N'[RII_PERMISSION_DEFINITIONS]'))
        SET IDENTITY_INSERT [RII_PERMISSION_DEFINITIONS] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260803154454_AddProductionTransferCancellationAndTaskManagement'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'BranchCode', N'CreatedBy', N'CreatedDate', N'DeletedBy', N'DeletedDate', N'PermissionDefinitionId', N'PermissionGroupId', N'UpdatedBy', N'UpdatedDate') AND [object_id] = OBJECT_ID(N'[RII_PERMISSION_GROUP_PERMISSIONS]'))
        SET IDENTITY_INSERT [RII_PERMISSION_GROUP_PERMISSIONS] ON;
    EXEC(N'INSERT INTO [RII_PERMISSION_GROUP_PERMISSIONS] ([Id], [BranchCode], [CreatedBy], [CreatedDate], [DeletedBy], [DeletedDate], [PermissionDefinitionId], [PermissionGroupId], [UpdatedBy], [UpdatedDate])
    VALUES (CAST(2409 AS bigint), N''0'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, CAST(2409 AS bigint), CAST(1001 AS bigint), NULL, NULL)');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'BranchCode', N'CreatedBy', N'CreatedDate', N'DeletedBy', N'DeletedDate', N'PermissionDefinitionId', N'PermissionGroupId', N'UpdatedBy', N'UpdatedDate') AND [object_id] = OBJECT_ID(N'[RII_PERMISSION_GROUP_PERMISSIONS]'))
        SET IDENTITY_INSERT [RII_PERMISSION_GROUP_PERMISSIONS] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260803154454_AddProductionTransferCancellationAndTaskManagement'
)
BEGIN
    CREATE INDEX [IX_RII_WAREHOUSE_DEFAULT_TRANSFER_RETURN_LOCATION] ON [RII_WAREHOUSE] ([DefaultTransferReturnLocationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260803154454_AddProductionTransferCancellationAndTaskManagement'
)
BEGIN
    ALTER TABLE [RII_WAREHOUSE] ADD CONSTRAINT [FK_RII_WAREHOUSE_RII_LOCATION_DefaultTransferReturnLocationId] FOREIGN KEY ([DefaultTransferReturnLocationId]) REFERENCES [RII_LOCATION] ([Id]) ON DELETE SET NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260803154454_AddProductionTransferCancellationAndTaskManagement'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260803154454_AddProductionTransferCancellationAndTaskManagement', N'10.0.10');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260803205213_AddKkdExcessApprovalAndEmployeeStockPreference'
)
BEGIN
    ALTER TABLE [RII_WT_HEADER] ADD [ProjectCode] nvarchar(50) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260803205213_AddKkdExcessApprovalAndEmployeeStockPreference'
)
BEGIN
    ALTER TABLE [RII_KKD_POLICY] ADD [RequireManagerApprovalForExcess] bit NOT NULL DEFAULT CAST(1 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260803205213_AddKkdExcessApprovalAndEmployeeStockPreference'
)
BEGIN
    ALTER TABLE [RII_KKD_DISTRIBUTION] ADD [ExcessApprovalReason] nvarchar(1000) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260803205213_AddKkdExcessApprovalAndEmployeeStockPreference'
)
BEGIN
    ALTER TABLE [RII_KKD_DISTRIBUTION] ADD [ExcessApprovalStatus] nvarchar(30) NOT NULL DEFAULT N'NotRequired';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260803205213_AddKkdExcessApprovalAndEmployeeStockPreference'
)
BEGIN
    ALTER TABLE [RII_KKD_DISTRIBUTION] ADD [ExcessApprovedAtUtc] datetimeoffset NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260803205213_AddKkdExcessApprovalAndEmployeeStockPreference'
)
BEGIN
    ALTER TABLE [RII_KKD_DISTRIBUTION] ADD [ExcessApprovedBy] bigint NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260803205213_AddKkdExcessApprovalAndEmployeeStockPreference'
)
BEGIN
    CREATE TABLE [RII_KKD_EMPLOYEE_STOCK_PREFERENCE] (
        [Id] bigint NOT NULL IDENTITY,
        [EmployeeId] bigint NOT NULL,
        [GroupCode] nvarchar(80) NOT NULL,
        [StockId] bigint NOT NULL,
        [LastSelectedAtUtc] datetimeoffset NOT NULL,
        [RowVersion] rowversion NOT NULL,
        [BranchCode] nvarchar(10) NOT NULL DEFAULT N'0',
        [CreatedDate] datetime2 NULL,
        [UpdatedDate] datetime2 NULL,
        [DeletedDate] datetime2 NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [CreatedBy] bigint NULL,
        [UpdatedBy] bigint NULL,
        [DeletedBy] bigint NULL,
        CONSTRAINT [PK_RII_KKD_EMPLOYEE_STOCK_PREFERENCE] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_RII_KKD_EMPLOYEE_STOCK_PREFERENCE_RII_KKD_EMPLOYEE_EmployeeId] FOREIGN KEY ([EmployeeId]) REFERENCES [RII_KKD_EMPLOYEE] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260803205213_AddKkdExcessApprovalAndEmployeeStockPreference'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_RII_KKD_EMPLOYEE_STOCK_PREFERENCE_BranchCode_EmployeeId_GroupCode] ON [RII_KKD_EMPLOYEE_STOCK_PREFERENCE] ([BranchCode], [EmployeeId], [GroupCode]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260803205213_AddKkdExcessApprovalAndEmployeeStockPreference'
)
BEGIN
    CREATE INDEX [IX_RII_KKD_EMPLOYEE_STOCK_PREFERENCE_BranchCode_StockId] ON [RII_KKD_EMPLOYEE_STOCK_PREFERENCE] ([BranchCode], [StockId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260803205213_AddKkdExcessApprovalAndEmployeeStockPreference'
)
BEGIN
    CREATE INDEX [IX_RII_KKD_EMPLOYEE_STOCK_PREFERENCE_EmployeeId] ON [RII_KKD_EMPLOYEE_STOCK_PREFERENCE] ([EmployeeId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260803205213_AddKkdExcessApprovalAndEmployeeStockPreference'
)
BEGIN
    CREATE INDEX [IX_RII_KKD_EMPLOYEE_STOCK_PREFERENCE_IsDeleted] ON [RII_KKD_EMPLOYEE_STOCK_PREFERENCE] ([IsDeleted]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260803205213_AddKkdExcessApprovalAndEmployeeStockPreference'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260803205213_AddKkdExcessApprovalAndEmployeeStockPreference', N'10.0.10');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260803224536_AddStockMediaLibrary'
)
BEGIN
    CREATE TABLE [RII_STOCK_IMAGE] (
        [Id] bigint NOT NULL IDENTITY,
        [StockId] bigint NOT NULL,
        [FileUrl] nvarchar(500) NOT NULL,
        [OriginalFileName] nvarchar(240) NOT NULL,
        [ContentType] nvarchar(100) NOT NULL,
        [FileLength] bigint NOT NULL,
        [AltText] nvarchar(200) NULL,
        [SortOrder] int NOT NULL,
        [IsPrimary] bit NOT NULL DEFAULT CAST(0 AS bit),
        [BranchCode] nvarchar(10) NOT NULL DEFAULT N'0',
        [CreatedDate] datetime2 NULL,
        [UpdatedDate] datetime2 NULL,
        [DeletedDate] datetime2 NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [CreatedBy] bigint NULL,
        [UpdatedBy] bigint NULL,
        [DeletedBy] bigint NULL,
        CONSTRAINT [PK_RII_STOCK_IMAGE] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_RII_STOCK_IMAGE_RII_STOCK_StockId] FOREIGN KEY ([StockId]) REFERENCES [RII_STOCK] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260803224536_AddStockMediaLibrary'
)
BEGIN
    CREATE INDEX [IX_RII_STOCK_IMAGE_IsDeleted] ON [RII_STOCK_IMAGE] ([IsDeleted]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260803224536_AddStockMediaLibrary'
)
BEGIN
    CREATE INDEX [IX_RII_STOCK_IMAGE_StockId] ON [RII_STOCK_IMAGE] ([StockId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260803224536_AddStockMediaLibrary'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_StockImage_Branch_Stock_SortOrder] ON [RII_STOCK_IMAGE] ([BranchCode], [StockId], [SortOrder]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260803224536_AddStockMediaLibrary'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_StockImage_OnePrimary] ON [RII_STOCK_IMAGE] ([BranchCode], [StockId]) WHERE [IsDeleted] = 0 AND [IsPrimary] = 1');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260803224536_AddStockMediaLibrary'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260803224536_AddStockMediaLibrary', N'10.0.10');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804095239_AddNetsisProductionReadFunctions'
)
BEGIN
    IF OBJECT_ID(N'dbo.RII_FN_ISEMRI') IS NULL
    BEGIN
        EXEC sys.sp_executesql N'CREATE FUNCTION [dbo].[RII_FN_ISEMRI]
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
            CONVERT(NVARCHAR(50), NULLIF(LTRIM(RTRIM(I.YAPKOD)), '''')) AS YapilandirmaKodu,
            CONVERT(DECIMAL(28, 8), ISNULL(I.MIKTAR, 0)) AS IsEmriMiktari,
            CONVERT(INT, 1) AS BirimSirasi,
            CONVERT(NVARCHAR(20), NULLIF(LTRIM(RTRIM(S.OLCU_BR1)), '''')) AS BirimKodu,
            CONVERT(DECIMAL(28, 8), CASE WHEN ISNULL(S.FORMUL_TOPLAMI, 0) = 0 THEN 1 ELSE S.FORMUL_TOPLAMI END) AS ReceteToplami,
            CONVERT(DATETIME2, I.TARIH) AS Tarih,
            CONVERT(DATETIME2, I.TESLIM_TARIHI) AS TeslimTarihi,
            CONVERT(NVARCHAR(50), NULLIF(LTRIM(RTRIM(I.SIPARIS_NO)), '''')) AS SiparisNo,
            CONVERT(INT, ISNULL(I.SIPKONT, 0)) AS SiparisSatirNo,
            CONVERT(NVARCHAR(50), NULLIF(LTRIM(RTRIM(I.PROJE_KODU)), '''')) AS ProjeKodu,
            CONVERT(INT, ISNULL(I.DEPO_KODU, 0)) AS DepoKodu,
            CONVERT(INT, ISNULL(I.CIKIS_DEPO_KODU, 0)) AS CikisDepoKodu,
            CONVERT(BIT, CASE WHEN UPPER(LTRIM(RTRIM(ISNULL(I.KAPALI, '''')))) IN (''1'', ''E'', ''EVET'', ''TRUE'') THEN 1 ELSE 0 END) AS Kapali
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
          AND (@KapaliDahil = 1 OR UPPER(LTRIM(RTRIM(ISNULL(I.KAPALI, '''')))) NOT IN (''1'', ''E'', ''EVET'', ''TRUE''))
    );';
    END
    ELSE
    BEGIN
        EXEC sys.sp_executesql N'ALTER FUNCTION [dbo].[RII_FN_ISEMRI]
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
            CONVERT(NVARCHAR(50), NULLIF(LTRIM(RTRIM(I.YAPKOD)), '''')) AS YapilandirmaKodu,
            CONVERT(DECIMAL(28, 8), ISNULL(I.MIKTAR, 0)) AS IsEmriMiktari,
            CONVERT(INT, 1) AS BirimSirasi,
            CONVERT(NVARCHAR(20), NULLIF(LTRIM(RTRIM(S.OLCU_BR1)), '''')) AS BirimKodu,
            CONVERT(DECIMAL(28, 8), CASE WHEN ISNULL(S.FORMUL_TOPLAMI, 0) = 0 THEN 1 ELSE S.FORMUL_TOPLAMI END) AS ReceteToplami,
            CONVERT(DATETIME2, I.TARIH) AS Tarih,
            CONVERT(DATETIME2, I.TESLIM_TARIHI) AS TeslimTarihi,
            CONVERT(NVARCHAR(50), NULLIF(LTRIM(RTRIM(I.SIPARIS_NO)), '''')) AS SiparisNo,
            CONVERT(INT, ISNULL(I.SIPKONT, 0)) AS SiparisSatirNo,
            CONVERT(NVARCHAR(50), NULLIF(LTRIM(RTRIM(I.PROJE_KODU)), '''')) AS ProjeKodu,
            CONVERT(INT, ISNULL(I.DEPO_KODU, 0)) AS DepoKodu,
            CONVERT(INT, ISNULL(I.CIKIS_DEPO_KODU, 0)) AS CikisDepoKodu,
            CONVERT(BIT, CASE WHEN UPPER(LTRIM(RTRIM(ISNULL(I.KAPALI, '''')))) IN (''1'', ''E'', ''EVET'', ''TRUE'') THEN 1 ELSE 0 END) AS Kapali
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
          AND (@KapaliDahil = 1 OR UPPER(LTRIM(RTRIM(ISNULL(I.KAPALI, '''')))) NOT IN (''1'', ''E'', ''EVET'', ''TRUE''))
    );';
    END
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804095239_AddNetsisProductionReadFunctions'
)
BEGIN
    IF OBJECT_ID(N'dbo.RII_FN_STOK_RECETE') IS NULL
    BEGIN
        EXEC sys.sp_executesql N'CREATE FUNCTION [dbo].[RII_FN_STOK_RECETE]
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
            CONVERT(NVARCHAR(20), NULLIF(LTRIM(RTRIM(M.OLCU_BR1)), '''')) AS MamulBirimKodu,
            CONVERT(NVARCHAR(50), NULLIF(LTRIM(RTRIM(R.MAMYAPKOD)), '''')) AS MamulYapilandirmaKodu,
            CONVERT(NVARCHAR(50), R.HAM_KODU) AS BilesenStokKodu,
            CONVERT(NVARCHAR(200), H.STOK_ADI) AS BilesenStokAdi,
            CONVERT(NVARCHAR(20), NULLIF(LTRIM(RTRIM(H.OLCU_BR1)), '''')) AS BilesenBirimKodu,
            CONVERT(NVARCHAR(50), NULLIF(LTRIM(RTRIM(R.HAMYAPKOD)), '''')) AS BilesenYapilandirmaKodu,
            CONVERT(INT, ISNULL(TRY_CONVERT(INT, NULLIF(LTRIM(RTRIM(R.OPNO)), '''')), 0)) AS OperasyonNo,
            CONVERT(DECIMAL(28, 8), CASE WHEN ISNULL(M.FORMUL_TOPLAMI, 0) = 0 THEN 1 ELSE M.FORMUL_TOPLAMI END) AS ReceteToplami,
            CONVERT(DECIMAL(28, 8), ISNULL(R.MIKTAR, 0)) AS ReceteMiktari,
            CONVERT(DECIMAL(28, 8), ISNULL(R.MIKTAR, 0) / CASE WHEN ISNULL(M.FORMUL_TOPLAMI, 0) = 0 THEN 1 ELSE M.FORMUL_TOPLAMI END) AS BirMamulIcinMiktar,
            CONVERT(DECIMAL(28, 8), ISNULL(R.F_YEDEK1, 0)) AS FireDegeri,
            CONVERT(DECIMAL(28, 8), ISNULL(R.F_YEDEK2, 0)) AS SabitFireMiktari,
            CONVERT(BIT, CASE WHEN UPPER(LTRIM(RTRIM(ISNULL(R.MIKTARSABITLE, '''')))) IN (''1'', ''E'', ''EVET'', ''TRUE'') THEN 1 ELSE 0 END) AS MiktarSabit
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
              ISNULL(NULLIF(LTRIM(RTRIM(R.MAMYAPKOD)), ''''), '''') = ISNULL(NULLIF(LTRIM(RTRIM(@YapilandirmaKodu)), ''''), '''')
              OR
              (
                  ISNULL(NULLIF(LTRIM(RTRIM(@YapilandirmaKodu)), ''''), '''') <> ''''
                  AND ISNULL(NULLIF(LTRIM(RTRIM(R.MAMYAPKOD)), ''''), '''') = ''''
                  AND NOT EXISTS
                  (
                      SELECT 1 FROM V3RIICO.dbo.TBLSTOKURM AS RX
                      WHERE RX.MAMUL_KODU = @StokKodu
                        AND ISNULL(NULLIF(LTRIM(RTRIM(RX.MAMYAPKOD)), ''''), '''') = ISNULL(NULLIF(LTRIM(RTRIM(@YapilandirmaKodu)), ''''), '''')
                        AND UPPER(ISNULL(NULLIF(LTRIM(RTRIM(RX.OPR_BIL)), ''''), ''B'')) <> ''O''
                  )
              )
          )
          AND UPPER(ISNULL(NULLIF(LTRIM(RTRIM(R.OPR_BIL)), ''''), ''B'')) <> ''O''
    );';
    END
    ELSE
    BEGIN
        EXEC sys.sp_executesql N'ALTER FUNCTION [dbo].[RII_FN_STOK_RECETE]
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
            CONVERT(NVARCHAR(20), NULLIF(LTRIM(RTRIM(M.OLCU_BR1)), '''')) AS MamulBirimKodu,
            CONVERT(NVARCHAR(50), NULLIF(LTRIM(RTRIM(R.MAMYAPKOD)), '''')) AS MamulYapilandirmaKodu,
            CONVERT(NVARCHAR(50), R.HAM_KODU) AS BilesenStokKodu,
            CONVERT(NVARCHAR(200), H.STOK_ADI) AS BilesenStokAdi,
            CONVERT(NVARCHAR(20), NULLIF(LTRIM(RTRIM(H.OLCU_BR1)), '''')) AS BilesenBirimKodu,
            CONVERT(NVARCHAR(50), NULLIF(LTRIM(RTRIM(R.HAMYAPKOD)), '''')) AS BilesenYapilandirmaKodu,
            CONVERT(INT, ISNULL(TRY_CONVERT(INT, NULLIF(LTRIM(RTRIM(R.OPNO)), '''')), 0)) AS OperasyonNo,
            CONVERT(DECIMAL(28, 8), CASE WHEN ISNULL(M.FORMUL_TOPLAMI, 0) = 0 THEN 1 ELSE M.FORMUL_TOPLAMI END) AS ReceteToplami,
            CONVERT(DECIMAL(28, 8), ISNULL(R.MIKTAR, 0)) AS ReceteMiktari,
            CONVERT(DECIMAL(28, 8), ISNULL(R.MIKTAR, 0) / CASE WHEN ISNULL(M.FORMUL_TOPLAMI, 0) = 0 THEN 1 ELSE M.FORMUL_TOPLAMI END) AS BirMamulIcinMiktar,
            CONVERT(DECIMAL(28, 8), ISNULL(R.F_YEDEK1, 0)) AS FireDegeri,
            CONVERT(DECIMAL(28, 8), ISNULL(R.F_YEDEK2, 0)) AS SabitFireMiktari,
            CONVERT(BIT, CASE WHEN UPPER(LTRIM(RTRIM(ISNULL(R.MIKTARSABITLE, '''')))) IN (''1'', ''E'', ''EVET'', ''TRUE'') THEN 1 ELSE 0 END) AS MiktarSabit
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
              ISNULL(NULLIF(LTRIM(RTRIM(R.MAMYAPKOD)), ''''), '''') = ISNULL(NULLIF(LTRIM(RTRIM(@YapilandirmaKodu)), ''''), '''')
              OR
              (
                  ISNULL(NULLIF(LTRIM(RTRIM(@YapilandirmaKodu)), ''''), '''') <> ''''
                  AND ISNULL(NULLIF(LTRIM(RTRIM(R.MAMYAPKOD)), ''''), '''') = ''''
                  AND NOT EXISTS
                  (
                      SELECT 1 FROM V3RIICO.dbo.TBLSTOKURM AS RX
                      WHERE RX.MAMUL_KODU = @StokKodu
                        AND ISNULL(NULLIF(LTRIM(RTRIM(RX.MAMYAPKOD)), ''''), '''') = ISNULL(NULLIF(LTRIM(RTRIM(@YapilandirmaKodu)), ''''), '''')
                        AND UPPER(ISNULL(NULLIF(LTRIM(RTRIM(RX.OPR_BIL)), ''''), ''B'')) <> ''O''
                  )
              )
          )
          AND UPPER(ISNULL(NULLIF(LTRIM(RTRIM(R.OPR_BIL)), ''''), ''B'')) <> ''O''
    );';
    END
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804095239_AddNetsisProductionReadFunctions'
)
BEGIN
    IF OBJECT_ID(N'dbo.RII_FN_ISEMRI_RECETE') IS NULL
    BEGIN
        EXEC sys.sp_executesql N'CREATE FUNCTION [dbo].[RII_FN_ISEMRI_RECETE]
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
    );';
    END
    ELSE
    BEGIN
        EXEC sys.sp_executesql N'ALTER FUNCTION [dbo].[RII_FN_ISEMRI_RECETE]
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
    );';
    END
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804095239_AddNetsisProductionReadFunctions'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260804095239_AddNetsisProductionReadFunctions', N'10.0.10');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804121412_AddCompatibleNetsisProductionReadFunctions'
)
BEGIN
    IF OBJECT_ID(N'dbo.RII_FN_ISEMRI') IS NULL
    BEGIN
        EXEC sys.sp_executesql N'CREATE FUNCTION [dbo].[RII_FN_ISEMRI]
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
            CONVERT(NVARCHAR(50), NULLIF(LTRIM(RTRIM(I.YAPKOD)), '''')) AS YapilandirmaKodu,
            CONVERT(DECIMAL(28, 8), ISNULL(I.MIKTAR, 0)) AS IsEmriMiktari,
            CONVERT(INT, 1) AS BirimSirasi,
            CONVERT(NVARCHAR(20), NULLIF(LTRIM(RTRIM(S.OLCU_BR1)), '''')) AS BirimKodu,
            CONVERT(DECIMAL(28, 8), CASE WHEN ISNULL(S.FORMUL_TOPLAMI, 0) = 0 THEN 1 ELSE S.FORMUL_TOPLAMI END) AS ReceteToplami,
            CONVERT(DATETIME2, I.TARIH) AS Tarih,
            CONVERT(DATETIME2, I.TESLIM_TARIHI) AS TeslimTarihi,
            CONVERT(NVARCHAR(50), NULLIF(LTRIM(RTRIM(I.SIPARIS_NO)), '''')) AS SiparisNo,
            CONVERT(INT, ISNULL(I.SIPKONT, 0)) AS SiparisSatirNo,
            CONVERT(NVARCHAR(50), NULLIF(LTRIM(RTRIM(I.PROJE_KODU)), '''')) AS ProjeKodu,
            CONVERT(INT, ISNULL(I.DEPO_KODU, 0)) AS DepoKodu,
            CONVERT(INT, ISNULL(I.CIKIS_DEPO_KODU, 0)) AS CikisDepoKodu,
            CONVERT(BIT, CASE WHEN UPPER(LTRIM(RTRIM(ISNULL(I.KAPALI, '''')))) IN (''1'', ''E'', ''EVET'', ''TRUE'') THEN 1 ELSE 0 END) AS Kapali
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
          AND (@KapaliDahil = 1 OR UPPER(LTRIM(RTRIM(ISNULL(I.KAPALI, '''')))) NOT IN (''1'', ''E'', ''EVET'', ''TRUE''))
    );';
    END
    ELSE
    BEGIN
        EXEC sys.sp_executesql N'ALTER FUNCTION [dbo].[RII_FN_ISEMRI]
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
            CONVERT(NVARCHAR(50), NULLIF(LTRIM(RTRIM(I.YAPKOD)), '''')) AS YapilandirmaKodu,
            CONVERT(DECIMAL(28, 8), ISNULL(I.MIKTAR, 0)) AS IsEmriMiktari,
            CONVERT(INT, 1) AS BirimSirasi,
            CONVERT(NVARCHAR(20), NULLIF(LTRIM(RTRIM(S.OLCU_BR1)), '''')) AS BirimKodu,
            CONVERT(DECIMAL(28, 8), CASE WHEN ISNULL(S.FORMUL_TOPLAMI, 0) = 0 THEN 1 ELSE S.FORMUL_TOPLAMI END) AS ReceteToplami,
            CONVERT(DATETIME2, I.TARIH) AS Tarih,
            CONVERT(DATETIME2, I.TESLIM_TARIHI) AS TeslimTarihi,
            CONVERT(NVARCHAR(50), NULLIF(LTRIM(RTRIM(I.SIPARIS_NO)), '''')) AS SiparisNo,
            CONVERT(INT, ISNULL(I.SIPKONT, 0)) AS SiparisSatirNo,
            CONVERT(NVARCHAR(50), NULLIF(LTRIM(RTRIM(I.PROJE_KODU)), '''')) AS ProjeKodu,
            CONVERT(INT, ISNULL(I.DEPO_KODU, 0)) AS DepoKodu,
            CONVERT(INT, ISNULL(I.CIKIS_DEPO_KODU, 0)) AS CikisDepoKodu,
            CONVERT(BIT, CASE WHEN UPPER(LTRIM(RTRIM(ISNULL(I.KAPALI, '''')))) IN (''1'', ''E'', ''EVET'', ''TRUE'') THEN 1 ELSE 0 END) AS Kapali
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
          AND (@KapaliDahil = 1 OR UPPER(LTRIM(RTRIM(ISNULL(I.KAPALI, '''')))) NOT IN (''1'', ''E'', ''EVET'', ''TRUE''))
    );';
    END
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804121412_AddCompatibleNetsisProductionReadFunctions'
)
BEGIN
    IF OBJECT_ID(N'dbo.RII_FN_STOK_RECETE') IS NULL
    BEGIN
        EXEC sys.sp_executesql N'CREATE FUNCTION [dbo].[RII_FN_STOK_RECETE]
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
            CONVERT(NVARCHAR(20), NULLIF(LTRIM(RTRIM(M.OLCU_BR1)), '''')) AS MamulBirimKodu,
            CONVERT(NVARCHAR(50), NULLIF(LTRIM(RTRIM(R.MAMYAPKOD)), '''')) AS MamulYapilandirmaKodu,
            CONVERT(NVARCHAR(50), R.HAM_KODU) AS BilesenStokKodu,
            CONVERT(NVARCHAR(200), H.STOK_ADI) AS BilesenStokAdi,
            CONVERT(NVARCHAR(20), NULLIF(LTRIM(RTRIM(H.OLCU_BR1)), '''')) AS BilesenBirimKodu,
            CONVERT(NVARCHAR(50), NULLIF(LTRIM(RTRIM(R.HAMYAPKOD)), '''')) AS BilesenYapilandirmaKodu,
            CONVERT(INT, ISNULL(TRY_CONVERT(INT, NULLIF(LTRIM(RTRIM(R.OPNO)), '''')), 0)) AS OperasyonNo,
            CONVERT(DECIMAL(28, 8), CASE WHEN ISNULL(M.FORMUL_TOPLAMI, 0) = 0 THEN 1 ELSE M.FORMUL_TOPLAMI END) AS ReceteToplami,
            CONVERT(DECIMAL(28, 8), ISNULL(R.MIKTAR, 0)) AS ReceteMiktari,
            CONVERT(DECIMAL(28, 8), ISNULL(R.MIKTAR, 0) / CASE WHEN ISNULL(M.FORMUL_TOPLAMI, 0) = 0 THEN 1 ELSE M.FORMUL_TOPLAMI END) AS BirMamulIcinMiktar,
            CONVERT(DECIMAL(28, 8), ISNULL(R.F_YEDEK1, 0)) AS FireDegeri,
            CONVERT(DECIMAL(28, 8), ISNULL(R.F_YEDEK2, 0)) AS SabitFireMiktari,
            CONVERT(BIT, CASE WHEN UPPER(LTRIM(RTRIM(ISNULL(R.MIKTARSABITLE, '''')))) IN (''1'', ''E'', ''EVET'', ''TRUE'') THEN 1 ELSE 0 END) AS MiktarSabit
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
              ISNULL(NULLIF(LTRIM(RTRIM(R.MAMYAPKOD)), ''''), '''') = ISNULL(NULLIF(LTRIM(RTRIM(@YapilandirmaKodu)), ''''), '''')
              OR
              (
                  ISNULL(NULLIF(LTRIM(RTRIM(@YapilandirmaKodu)), ''''), '''') <> ''''
                  AND ISNULL(NULLIF(LTRIM(RTRIM(R.MAMYAPKOD)), ''''), '''') = ''''
                  AND NOT EXISTS
                  (
                      SELECT 1 FROM V3RIICO.dbo.TBLSTOKURM AS RX
                      WHERE RX.MAMUL_KODU = @StokKodu
                        AND ISNULL(NULLIF(LTRIM(RTRIM(RX.MAMYAPKOD)), ''''), '''') = ISNULL(NULLIF(LTRIM(RTRIM(@YapilandirmaKodu)), ''''), '''')
                        AND UPPER(ISNULL(NULLIF(LTRIM(RTRIM(RX.OPR_BIL)), ''''), ''B'')) <> ''O''
                  )
              )
          )
          AND UPPER(ISNULL(NULLIF(LTRIM(RTRIM(R.OPR_BIL)), ''''), ''B'')) <> ''O''
    );';
    END
    ELSE
    BEGIN
        EXEC sys.sp_executesql N'ALTER FUNCTION [dbo].[RII_FN_STOK_RECETE]
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
            CONVERT(NVARCHAR(20), NULLIF(LTRIM(RTRIM(M.OLCU_BR1)), '''')) AS MamulBirimKodu,
            CONVERT(NVARCHAR(50), NULLIF(LTRIM(RTRIM(R.MAMYAPKOD)), '''')) AS MamulYapilandirmaKodu,
            CONVERT(NVARCHAR(50), R.HAM_KODU) AS BilesenStokKodu,
            CONVERT(NVARCHAR(200), H.STOK_ADI) AS BilesenStokAdi,
            CONVERT(NVARCHAR(20), NULLIF(LTRIM(RTRIM(H.OLCU_BR1)), '''')) AS BilesenBirimKodu,
            CONVERT(NVARCHAR(50), NULLIF(LTRIM(RTRIM(R.HAMYAPKOD)), '''')) AS BilesenYapilandirmaKodu,
            CONVERT(INT, ISNULL(TRY_CONVERT(INT, NULLIF(LTRIM(RTRIM(R.OPNO)), '''')), 0)) AS OperasyonNo,
            CONVERT(DECIMAL(28, 8), CASE WHEN ISNULL(M.FORMUL_TOPLAMI, 0) = 0 THEN 1 ELSE M.FORMUL_TOPLAMI END) AS ReceteToplami,
            CONVERT(DECIMAL(28, 8), ISNULL(R.MIKTAR, 0)) AS ReceteMiktari,
            CONVERT(DECIMAL(28, 8), ISNULL(R.MIKTAR, 0) / CASE WHEN ISNULL(M.FORMUL_TOPLAMI, 0) = 0 THEN 1 ELSE M.FORMUL_TOPLAMI END) AS BirMamulIcinMiktar,
            CONVERT(DECIMAL(28, 8), ISNULL(R.F_YEDEK1, 0)) AS FireDegeri,
            CONVERT(DECIMAL(28, 8), ISNULL(R.F_YEDEK2, 0)) AS SabitFireMiktari,
            CONVERT(BIT, CASE WHEN UPPER(LTRIM(RTRIM(ISNULL(R.MIKTARSABITLE, '''')))) IN (''1'', ''E'', ''EVET'', ''TRUE'') THEN 1 ELSE 0 END) AS MiktarSabit
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
              ISNULL(NULLIF(LTRIM(RTRIM(R.MAMYAPKOD)), ''''), '''') = ISNULL(NULLIF(LTRIM(RTRIM(@YapilandirmaKodu)), ''''), '''')
              OR
              (
                  ISNULL(NULLIF(LTRIM(RTRIM(@YapilandirmaKodu)), ''''), '''') <> ''''
                  AND ISNULL(NULLIF(LTRIM(RTRIM(R.MAMYAPKOD)), ''''), '''') = ''''
                  AND NOT EXISTS
                  (
                      SELECT 1 FROM V3RIICO.dbo.TBLSTOKURM AS RX
                      WHERE RX.MAMUL_KODU = @StokKodu
                        AND ISNULL(NULLIF(LTRIM(RTRIM(RX.MAMYAPKOD)), ''''), '''') = ISNULL(NULLIF(LTRIM(RTRIM(@YapilandirmaKodu)), ''''), '''')
                        AND UPPER(ISNULL(NULLIF(LTRIM(RTRIM(RX.OPR_BIL)), ''''), ''B'')) <> ''O''
                  )
              )
          )
          AND UPPER(ISNULL(NULLIF(LTRIM(RTRIM(R.OPR_BIL)), ''''), ''B'')) <> ''O''
    );';
    END
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804121412_AddCompatibleNetsisProductionReadFunctions'
)
BEGIN
    IF OBJECT_ID(N'dbo.RII_FN_ISEMRI_RECETE') IS NULL
    BEGIN
        EXEC sys.sp_executesql N'CREATE FUNCTION [dbo].[RII_FN_ISEMRI_RECETE]
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
    );';
    END
    ELSE
    BEGIN
        EXEC sys.sp_executesql N'ALTER FUNCTION [dbo].[RII_FN_ISEMRI_RECETE]
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
    );';
    END
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804121412_AddCompatibleNetsisProductionReadFunctions'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260804121412_AddCompatibleNetsisProductionReadFunctions', N'10.0.10');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804133017_AddKkdMaterialRequestOrderFlowPolicy'
)
BEGIN
    ALTER TABLE [RII_KKD_POLICY] ADD [EnableMaterialRequestOrderFlow] bit NOT NULL DEFAULT CAST(1 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804133017_AddKkdMaterialRequestOrderFlowPolicy'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260804133017_AddKkdMaterialRequestOrderFlowPolicy', N'10.0.10');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804144902_AddWarehouseOutboundOperationMetadata'
)
BEGIN
    ALTER TABLE [RII_WO_LINE] ADD [ProjectCode] nvarchar(50) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804144902_AddWarehouseOutboundOperationMetadata'
)
BEGIN
    ALTER TABLE [RII_WO_HEADER] ADD [CostCenterCode] nvarchar(100) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804144902_AddWarehouseOutboundOperationMetadata'
)
BEGIN
    ALTER TABLE [RII_WO_HEADER] ADD [ExitLocationCode] nvarchar(100) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804144902_AddWarehouseOutboundOperationMetadata'
)
BEGIN
    ALTER TABLE [RII_WO_HEADER] ADD [MovementTypeCode] nvarchar(50) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804144902_AddWarehouseOutboundOperationMetadata'
)
BEGIN
    ALTER TABLE [RII_WO_HEADER] ADD [ProjectCode] nvarchar(50) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804144902_AddWarehouseOutboundOperationMetadata'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260804144902_AddWarehouseOutboundOperationMetadata', N'10.0.10');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804152439_AddProcurementModule'
)
BEGIN
    CREATE TABLE [RII_PC_ORDER] (
        [Id] bigint NOT NULL IDENTITY,
        [OrderNo] nvarchar(50) NOT NULL,
        [OrderDate] date NOT NULL,
        [DeliveryDate] date NULL,
        [SupplierId] bigint NOT NULL,
        [SupplierCodeSnapshot] nvarchar(100) NOT NULL,
        [SupplierNameSnapshot] nvarchar(300) NOT NULL,
        [SourceQuoteId] bigint NULL,
        [CurrencyCode] nvarchar(10) NOT NULL,
        [ExchangeRate] decimal(20,8) NOT NULL,
        [ProjectCode] nvarchar(100) NULL,
        [Description] nvarchar(2000) NULL,
        [Status] nvarchar(30) NOT NULL,
        [ApprovedAtUtc] datetimeoffset NULL,
        [ApprovedBy] bigint NULL,
        [ErpOrderNo] nvarchar(100) NULL,
        [ErpPostedAtUtc] datetimeoffset NULL,
        [RowVersion] rowversion NOT NULL,
        [BranchCode] nvarchar(10) NOT NULL DEFAULT N'0',
        [CreatedDate] datetime2 NULL,
        [UpdatedDate] datetime2 NULL,
        [DeletedDate] datetime2 NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [CreatedBy] bigint NULL,
        [UpdatedBy] bigint NULL,
        [DeletedBy] bigint NULL,
        CONSTRAINT [PK_RII_PC_ORDER] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804152439_AddProcurementModule'
)
BEGIN
    CREATE TABLE [RII_PC_REQUEST] (
        [Id] bigint NOT NULL IDENTITY,
        [RequestNo] nvarchar(50) NOT NULL,
        [RequestDate] date NOT NULL,
        [RequiredDate] date NULL,
        [DepartmentCode] nvarchar(80) NULL,
        [ProjectCode] nvarchar(100) NULL,
        [Subject] nvarchar(250) NOT NULL,
        [Description] nvarchar(2000) NULL,
        [Status] nvarchar(30) NOT NULL,
        [SubmittedAtUtc] datetimeoffset NULL,
        [DecidedAtUtc] datetimeoffset NULL,
        [DecidedBy] bigint NULL,
        [DecisionNote] nvarchar(1000) NULL,
        [RowVersion] rowversion NOT NULL,
        [BranchCode] nvarchar(10) NOT NULL DEFAULT N'0',
        [CreatedDate] datetime2 NULL,
        [UpdatedDate] datetime2 NULL,
        [DeletedDate] datetime2 NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [CreatedBy] bigint NULL,
        [UpdatedBy] bigint NULL,
        [DeletedBy] bigint NULL,
        CONSTRAINT [PK_RII_PC_REQUEST] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804152439_AddProcurementModule'
)
BEGIN
    CREATE TABLE [RII_PC_STATUS_HISTORY] (
        [Id] bigint NOT NULL IDENTITY,
        [DocumentType] nvarchar(30) NOT NULL,
        [DocumentId] bigint NOT NULL,
        [FromStatus] nvarchar(30) NOT NULL,
        [ToStatus] nvarchar(30) NOT NULL,
        [ActorUserId] bigint NOT NULL,
        [Note] nvarchar(1000) NULL,
        [ChangedAtUtc] datetimeoffset NOT NULL,
        [BranchCode] nvarchar(10) NOT NULL DEFAULT N'0',
        [CreatedDate] datetime2 NULL,
        [UpdatedDate] datetime2 NULL,
        [DeletedDate] datetime2 NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [CreatedBy] bigint NULL,
        [UpdatedBy] bigint NULL,
        [DeletedBy] bigint NULL,
        CONSTRAINT [PK_RII_PC_STATUS_HISTORY] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804152439_AddProcurementModule'
)
BEGIN
    CREATE TABLE [RII_PC_ORDER_LINE] (
        [Id] bigint NOT NULL IDENTITY,
        [ProcurementPurchaseOrderId] bigint NOT NULL,
        [SourceQuoteLineId] bigint NULL,
        [LineNo] int NOT NULL,
        [StockId] bigint NULL,
        [StockCodeSnapshot] nvarchar(100) NULL,
        [StockNameSnapshot] nvarchar(300) NOT NULL,
        [UnitCode] nvarchar(20) NOT NULL,
        [OrderedQuantity] decimal(20,6) NOT NULL,
        [ReceivedQuantity] decimal(20,6) NOT NULL,
        [CancelledQuantity] decimal(20,6) NOT NULL,
        [UnitPrice] decimal(20,6) NOT NULL,
        [DiscountRate] decimal(9,4) NOT NULL,
        [VatRate] decimal(9,4) NOT NULL,
        [DeliveryDate] date NULL,
        [ProjectCode] nvarchar(100) NULL,
        [BranchCode] nvarchar(10) NOT NULL DEFAULT N'0',
        [CreatedDate] datetime2 NULL,
        [UpdatedDate] datetime2 NULL,
        [DeletedDate] datetime2 NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [CreatedBy] bigint NULL,
        [UpdatedBy] bigint NULL,
        [DeletedBy] bigint NULL,
        CONSTRAINT [PK_RII_PC_ORDER_LINE] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_RII_PC_ORDER_LINE_AMOUNTS] CHECK ([OrderedQuantity] > 0 AND [ReceivedQuantity] >= 0 AND [CancelledQuantity] >= 0 AND [ReceivedQuantity] + [CancelledQuantity] <= [OrderedQuantity] AND [UnitPrice] >= 0),
        CONSTRAINT [FK_RII_PC_ORDER_LINE_RII_PC_ORDER_ProcurementPurchaseOrderId] FOREIGN KEY ([ProcurementPurchaseOrderId]) REFERENCES [RII_PC_ORDER] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804152439_AddProcurementModule'
)
BEGIN
    CREATE TABLE [RII_PC_REQUEST_LINE] (
        [Id] bigint NOT NULL IDENTITY,
        [ProcurementRequestId] bigint NOT NULL,
        [LineNo] int NOT NULL,
        [StockId] bigint NULL,
        [StockCodeSnapshot] nvarchar(100) NULL,
        [StockNameSnapshot] nvarchar(300) NOT NULL,
        [UnitCode] nvarchar(20) NOT NULL,
        [RequestedQuantity] decimal(20,6) NOT NULL,
        [ConvertedQuantity] decimal(20,6) NOT NULL,
        [RequiredDate] date NULL,
        [ProjectCode] nvarchar(100) NULL,
        [Description] nvarchar(1000) NULL,
        [BranchCode] nvarchar(10) NOT NULL DEFAULT N'0',
        [CreatedDate] datetime2 NULL,
        [UpdatedDate] datetime2 NULL,
        [DeletedDate] datetime2 NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [CreatedBy] bigint NULL,
        [UpdatedBy] bigint NULL,
        [DeletedBy] bigint NULL,
        CONSTRAINT [PK_RII_PC_REQUEST_LINE] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_RII_PC_REQUEST_LINE_QTY] CHECK ([RequestedQuantity] > 0 AND [ConvertedQuantity] >= 0 AND [ConvertedQuantity] <= [RequestedQuantity]),
        CONSTRAINT [FK_RII_PC_REQUEST_LINE_RII_PC_REQUEST_ProcurementRequestId] FOREIGN KEY ([ProcurementRequestId]) REFERENCES [RII_PC_REQUEST] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804152439_AddProcurementModule'
)
BEGIN
    CREATE TABLE [RII_PC_RFQ] (
        [Id] bigint NOT NULL IDENTITY,
        [RfqNo] nvarchar(50) NOT NULL,
        [RfqDate] date NOT NULL,
        [ResponseDueDate] date NOT NULL,
        [ProcurementRequestId] bigint NULL,
        [Subject] nvarchar(250) NOT NULL,
        [BuyerMessage] nvarchar(2000) NULL,
        [Status] nvarchar(30) NOT NULL,
        [SentAtUtc] datetimeoffset NULL,
        [RowVersion] rowversion NOT NULL,
        [BranchCode] nvarchar(10) NOT NULL DEFAULT N'0',
        [CreatedDate] datetime2 NULL,
        [UpdatedDate] datetime2 NULL,
        [DeletedDate] datetime2 NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [CreatedBy] bigint NULL,
        [UpdatedBy] bigint NULL,
        [DeletedBy] bigint NULL,
        CONSTRAINT [PK_RII_PC_RFQ] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_RII_PC_RFQ_RII_PC_REQUEST_ProcurementRequestId] FOREIGN KEY ([ProcurementRequestId]) REFERENCES [RII_PC_REQUEST] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804152439_AddProcurementModule'
)
BEGIN
    CREATE TABLE [RII_PC_QUOTE] (
        [Id] bigint NOT NULL IDENTITY,
        [ProcurementRfqId] bigint NOT NULL,
        [SupplierId] bigint NOT NULL,
        [SupplierCodeSnapshot] nvarchar(100) NOT NULL,
        [SupplierNameSnapshot] nvarchar(300) NOT NULL,
        [QuoteNo] nvarchar(100) NOT NULL,
        [QuoteDate] date NOT NULL,
        [ValidUntil] date NULL,
        [CurrencyCode] nvarchar(10) NOT NULL,
        [ExchangeRate] decimal(20,8) NOT NULL,
        [Status] nvarchar(30) NOT NULL,
        [Note] nvarchar(2000) NULL,
        [RowVersion] rowversion NOT NULL,
        [BranchCode] nvarchar(10) NOT NULL DEFAULT N'0',
        [CreatedDate] datetime2 NULL,
        [UpdatedDate] datetime2 NULL,
        [DeletedDate] datetime2 NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [CreatedBy] bigint NULL,
        [UpdatedBy] bigint NULL,
        [DeletedBy] bigint NULL,
        CONSTRAINT [PK_RII_PC_QUOTE] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_RII_PC_QUOTE_RII_PC_RFQ_ProcurementRfqId] FOREIGN KEY ([ProcurementRfqId]) REFERENCES [RII_PC_RFQ] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804152439_AddProcurementModule'
)
BEGIN
    CREATE TABLE [RII_PC_RFQ_LINE] (
        [Id] bigint NOT NULL IDENTITY,
        [ProcurementRfqId] bigint NOT NULL,
        [ProcurementRequestLineId] bigint NULL,
        [LineNo] int NOT NULL,
        [StockId] bigint NULL,
        [StockCodeSnapshot] nvarchar(100) NULL,
        [StockNameSnapshot] nvarchar(300) NOT NULL,
        [UnitCode] nvarchar(20) NOT NULL,
        [RequestedQuantity] decimal(20,6) NOT NULL,
        [RequiredDate] date NULL,
        [ProjectCode] nvarchar(100) NULL,
        [BranchCode] nvarchar(10) NOT NULL DEFAULT N'0',
        [CreatedDate] datetime2 NULL,
        [UpdatedDate] datetime2 NULL,
        [DeletedDate] datetime2 NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [CreatedBy] bigint NULL,
        [UpdatedBy] bigint NULL,
        [DeletedBy] bigint NULL,
        CONSTRAINT [PK_RII_PC_RFQ_LINE] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_RII_PC_RFQ_LINE_QTY] CHECK ([RequestedQuantity] > 0),
        CONSTRAINT [FK_RII_PC_RFQ_LINE_RII_PC_RFQ_ProcurementRfqId] FOREIGN KEY ([ProcurementRfqId]) REFERENCES [RII_PC_RFQ] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804152439_AddProcurementModule'
)
BEGIN
    CREATE TABLE [RII_PC_RFQ_SUPPLIER] (
        [Id] bigint NOT NULL IDENTITY,
        [ProcurementRfqId] bigint NOT NULL,
        [SupplierId] bigint NOT NULL,
        [SupplierCodeSnapshot] nvarchar(100) NOT NULL,
        [SupplierNameSnapshot] nvarchar(300) NOT NULL,
        [BranchCode] nvarchar(10) NOT NULL DEFAULT N'0',
        [CreatedDate] datetime2 NULL,
        [UpdatedDate] datetime2 NULL,
        [DeletedDate] datetime2 NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [CreatedBy] bigint NULL,
        [UpdatedBy] bigint NULL,
        [DeletedBy] bigint NULL,
        CONSTRAINT [PK_RII_PC_RFQ_SUPPLIER] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_RII_PC_RFQ_SUPPLIER_RII_PC_RFQ_ProcurementRfqId] FOREIGN KEY ([ProcurementRfqId]) REFERENCES [RII_PC_RFQ] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804152439_AddProcurementModule'
)
BEGIN
    CREATE TABLE [RII_PC_QUOTE_LINE] (
        [Id] bigint NOT NULL IDENTITY,
        [ProcurementSupplierQuoteId] bigint NOT NULL,
        [ProcurementRfqLineId] bigint NOT NULL,
        [LineNo] int NOT NULL,
        [QuotedQuantity] decimal(20,6) NOT NULL,
        [UnitPrice] decimal(20,6) NOT NULL,
        [DiscountRate] decimal(9,4) NOT NULL,
        [VatRate] decimal(9,4) NOT NULL,
        [DeliveryDate] date NULL,
        [BranchCode] nvarchar(10) NOT NULL DEFAULT N'0',
        [CreatedDate] datetime2 NULL,
        [UpdatedDate] datetime2 NULL,
        [DeletedDate] datetime2 NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [CreatedBy] bigint NULL,
        [UpdatedBy] bigint NULL,
        [DeletedBy] bigint NULL,
        CONSTRAINT [PK_RII_PC_QUOTE_LINE] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_RII_PC_QUOTE_LINE_AMOUNTS] CHECK ([QuotedQuantity] > 0 AND [UnitPrice] >= 0 AND [DiscountRate] >= 0 AND [DiscountRate] <= 100 AND [VatRate] >= 0),
        CONSTRAINT [FK_RII_PC_QUOTE_LINE_RII_PC_QUOTE_ProcurementSupplierQuoteId] FOREIGN KEY ([ProcurementSupplierQuoteId]) REFERENCES [RII_PC_QUOTE] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804152439_AddProcurementModule'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'AvailableOnMobile', N'AvailableOnWeb', N'BranchCode', N'Code', N'CreatedBy', N'CreatedDate', N'DeletedBy', N'DeletedDate', N'Description', N'IsActive', N'Name', N'UpdatedBy', N'UpdatedDate') AND [object_id] = OBJECT_ID(N'[RII_PERMISSION_DEFINITIONS]'))
        SET IDENTITY_INSERT [RII_PERMISSION_DEFINITIONS] ON;
    EXEC(N'INSERT INTO [RII_PERMISSION_DEFINITIONS] ([Id], [AvailableOnMobile], [AvailableOnWeb], [BranchCode], [Code], [CreatedBy], [CreatedDate], [DeletedBy], [DeletedDate], [Description], [IsActive], [Name], [UpdatedBy], [UpdatedDate])
    VALUES (CAST(2600 AS bigint), CAST(0 AS bit), CAST(1 AS bit), N''0'', N''WMS.PROCUREMENT.VIEW'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, NULL, CAST(1 AS bit), N''Satınalma belgelerini görüntüle'', NULL, NULL),
    (CAST(2601 AS bigint), CAST(0 AS bit), CAST(1 AS bit), N''0'', N''WMS.PROCUREMENT.REQUEST.MANAGE'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, NULL, CAST(1 AS bit), N''Satınalma taleplerini yönet'', NULL, NULL),
    (CAST(2602 AS bigint), CAST(0 AS bit), CAST(1 AS bit), N''0'', N''WMS.PROCUREMENT.RFQ.MANAGE'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, NULL, CAST(1 AS bit), N''Teklif taleplerini yönet'', NULL, NULL),
    (CAST(2603 AS bigint), CAST(0 AS bit), CAST(1 AS bit), N''0'', N''WMS.PROCUREMENT.QUOTE.MANAGE'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, NULL, CAST(1 AS bit), N''Tedarikçi tekliflerini yönet'', NULL, NULL),
    (CAST(2604 AS bigint), CAST(0 AS bit), CAST(1 AS bit), N''0'', N''WMS.PROCUREMENT.ORDER.MANAGE'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, NULL, CAST(1 AS bit), N''Satınalma siparişlerini yönet'', NULL, NULL),
    (CAST(2605 AS bigint), CAST(0 AS bit), CAST(1 AS bit), N''0'', N''WMS.PROCUREMENT.APPROVE'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, NULL, CAST(1 AS bit), N''Satınalma belgelerini onayla'', NULL, NULL)');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'AvailableOnMobile', N'AvailableOnWeb', N'BranchCode', N'Code', N'CreatedBy', N'CreatedDate', N'DeletedBy', N'DeletedDate', N'Description', N'IsActive', N'Name', N'UpdatedBy', N'UpdatedDate') AND [object_id] = OBJECT_ID(N'[RII_PERMISSION_DEFINITIONS]'))
        SET IDENTITY_INSERT [RII_PERMISSION_DEFINITIONS] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804152439_AddProcurementModule'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'BranchCode', N'CreatedBy', N'CreatedDate', N'DeletedBy', N'DeletedDate', N'PermissionDefinitionId', N'PermissionGroupId', N'UpdatedBy', N'UpdatedDate') AND [object_id] = OBJECT_ID(N'[RII_PERMISSION_GROUP_PERMISSIONS]'))
        SET IDENTITY_INSERT [RII_PERMISSION_GROUP_PERMISSIONS] ON;
    EXEC(N'INSERT INTO [RII_PERMISSION_GROUP_PERMISSIONS] ([Id], [BranchCode], [CreatedBy], [CreatedDate], [DeletedBy], [DeletedDate], [PermissionDefinitionId], [PermissionGroupId], [UpdatedBy], [UpdatedDate])
    VALUES (CAST(2600 AS bigint), N''0'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, CAST(2600 AS bigint), CAST(1001 AS bigint), NULL, NULL),
    (CAST(2601 AS bigint), N''0'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, CAST(2601 AS bigint), CAST(1001 AS bigint), NULL, NULL),
    (CAST(2602 AS bigint), N''0'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, CAST(2602 AS bigint), CAST(1001 AS bigint), NULL, NULL),
    (CAST(2603 AS bigint), N''0'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, CAST(2603 AS bigint), CAST(1001 AS bigint), NULL, NULL),
    (CAST(2604 AS bigint), N''0'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, CAST(2604 AS bigint), CAST(1001 AS bigint), NULL, NULL),
    (CAST(2605 AS bigint), N''0'', NULL, ''2026-07-21T00:00:00.0000000Z'', NULL, NULL, CAST(2605 AS bigint), CAST(1001 AS bigint), NULL, NULL)');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'BranchCode', N'CreatedBy', N'CreatedDate', N'DeletedBy', N'DeletedDate', N'PermissionDefinitionId', N'PermissionGroupId', N'UpdatedBy', N'UpdatedDate') AND [object_id] = OBJECT_ID(N'[RII_PERMISSION_GROUP_PERMISSIONS]'))
        SET IDENTITY_INSERT [RII_PERMISSION_GROUP_PERMISSIONS] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804152439_AddProcurementModule'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_RII_PC_ORDER_BranchCode_OrderNo] ON [RII_PC_ORDER] ([BranchCode], [OrderNo]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804152439_AddProcurementModule'
)
BEGIN
    CREATE INDEX [IX_RII_PC_ORDER_BranchCode_Status_SupplierId] ON [RII_PC_ORDER] ([BranchCode], [Status], [SupplierId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804152439_AddProcurementModule'
)
BEGIN
    CREATE INDEX [IX_RII_PC_ORDER_IsDeleted] ON [RII_PC_ORDER] ([IsDeleted]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804152439_AddProcurementModule'
)
BEGIN
    CREATE INDEX [IX_RII_PC_ORDER_LINE_IsDeleted] ON [RII_PC_ORDER_LINE] ([IsDeleted]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804152439_AddProcurementModule'
)
BEGIN
    CREATE UNIQUE INDEX [IX_RII_PC_ORDER_LINE_ProcurementPurchaseOrderId_LineNo] ON [RII_PC_ORDER_LINE] ([ProcurementPurchaseOrderId], [LineNo]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804152439_AddProcurementModule'
)
BEGIN
    CREATE INDEX [IX_RII_PC_ORDER_LINE_StockId_DeliveryDate] ON [RII_PC_ORDER_LINE] ([StockId], [DeliveryDate]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804152439_AddProcurementModule'
)
BEGIN
    CREATE INDEX [IX_RII_PC_QUOTE_IsDeleted] ON [RII_PC_QUOTE] ([IsDeleted]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804152439_AddProcurementModule'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_RII_PC_QUOTE_ProcurementRfqId_SupplierId_QuoteNo] ON [RII_PC_QUOTE] ([ProcurementRfqId], [SupplierId], [QuoteNo]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804152439_AddProcurementModule'
)
BEGIN
    CREATE INDEX [IX_RII_PC_QUOTE_LINE_IsDeleted] ON [RII_PC_QUOTE_LINE] ([IsDeleted]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804152439_AddProcurementModule'
)
BEGIN
    CREATE UNIQUE INDEX [IX_RII_PC_QUOTE_LINE_ProcurementSupplierQuoteId_LineNo] ON [RII_PC_QUOTE_LINE] ([ProcurementSupplierQuoteId], [LineNo]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804152439_AddProcurementModule'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_RII_PC_REQUEST_BranchCode_RequestNo] ON [RII_PC_REQUEST] ([BranchCode], [RequestNo]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804152439_AddProcurementModule'
)
BEGIN
    CREATE INDEX [IX_RII_PC_REQUEST_BranchCode_Status_RequestDate] ON [RII_PC_REQUEST] ([BranchCode], [Status], [RequestDate]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804152439_AddProcurementModule'
)
BEGIN
    CREATE INDEX [IX_RII_PC_REQUEST_IsDeleted] ON [RII_PC_REQUEST] ([IsDeleted]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804152439_AddProcurementModule'
)
BEGIN
    CREATE INDEX [IX_RII_PC_REQUEST_LINE_IsDeleted] ON [RII_PC_REQUEST_LINE] ([IsDeleted]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804152439_AddProcurementModule'
)
BEGIN
    CREATE UNIQUE INDEX [IX_RII_PC_REQUEST_LINE_ProcurementRequestId_LineNo] ON [RII_PC_REQUEST_LINE] ([ProcurementRequestId], [LineNo]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804152439_AddProcurementModule'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_RII_PC_RFQ_BranchCode_RfqNo] ON [RII_PC_RFQ] ([BranchCode], [RfqNo]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804152439_AddProcurementModule'
)
BEGIN
    CREATE INDEX [IX_RII_PC_RFQ_IsDeleted] ON [RII_PC_RFQ] ([IsDeleted]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804152439_AddProcurementModule'
)
BEGIN
    CREATE INDEX [IX_RII_PC_RFQ_ProcurementRequestId] ON [RII_PC_RFQ] ([ProcurementRequestId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804152439_AddProcurementModule'
)
BEGIN
    CREATE INDEX [IX_RII_PC_RFQ_LINE_IsDeleted] ON [RII_PC_RFQ_LINE] ([IsDeleted]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804152439_AddProcurementModule'
)
BEGIN
    CREATE UNIQUE INDEX [IX_RII_PC_RFQ_LINE_ProcurementRfqId_LineNo] ON [RII_PC_RFQ_LINE] ([ProcurementRfqId], [LineNo]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804152439_AddProcurementModule'
)
BEGIN
    CREATE INDEX [IX_RII_PC_RFQ_SUPPLIER_IsDeleted] ON [RII_PC_RFQ_SUPPLIER] ([IsDeleted]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804152439_AddProcurementModule'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_RII_PC_RFQ_SUPPLIER_ProcurementRfqId_SupplierId] ON [RII_PC_RFQ_SUPPLIER] ([ProcurementRfqId], [SupplierId]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804152439_AddProcurementModule'
)
BEGIN
    CREATE INDEX [IX_RII_PC_STATUS_HISTORY_DocumentType_DocumentId_ChangedAtUtc] ON [RII_PC_STATUS_HISTORY] ([DocumentType], [DocumentId], [ChangedAtUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804152439_AddProcurementModule'
)
BEGIN
    CREATE INDEX [IX_RII_PC_STATUS_HISTORY_IsDeleted] ON [RII_PC_STATUS_HISTORY] ([IsDeleted]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804152439_AddProcurementModule'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260804152439_AddProcurementModule', N'10.0.10');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804164101_AddProcurementSplitAwards'
)
BEGIN
    ALTER TABLE [RII_PC_QUOTE_LINE] DROP CONSTRAINT [CK_RII_PC_QUOTE_LINE_AMOUNTS];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804164101_AddProcurementSplitAwards'
)
BEGIN
    ALTER TABLE [RII_PC_REQUEST_LINE] ADD [RowVersion] rowversion NOT NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804164101_AddProcurementSplitAwards'
)
BEGIN
    ALTER TABLE [RII_PC_QUOTE_LINE] ADD [ConvertedQuantity] decimal(20,6) NOT NULL DEFAULT 0.0;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804164101_AddProcurementSplitAwards'
)
BEGIN
    ALTER TABLE [RII_PC_QUOTE_LINE] ADD [RowVersion] rowversion NOT NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804164101_AddProcurementSplitAwards'
)
BEGIN
    CREATE TABLE [RII_PC_POLICY] (
        [Id] bigint NOT NULL IDENTITY,
        [PolicyKey] nvarchar(30) NOT NULL,
        [AllowMultipleRfqsPerRequest] bit NOT NULL,
        [AllowPartialRfqLines] bit NOT NULL,
        [AllowMultipleQuotesPerSupplier] bit NOT NULL,
        [AllowMultipleOrdersPerQuote] bit NOT NULL,
        [AllowPartialOrderLines] bit NOT NULL,
        [AllowSplitAwardsAcrossSuppliers] bit NOT NULL,
        [RowVersion] rowversion NOT NULL,
        [BranchCode] nvarchar(10) NOT NULL DEFAULT N'0',
        [CreatedDate] datetime2 NULL,
        [UpdatedDate] datetime2 NULL,
        [DeletedDate] datetime2 NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [CreatedBy] bigint NULL,
        [UpdatedBy] bigint NULL,
        [DeletedBy] bigint NULL,
        CONSTRAINT [PK_RII_PC_POLICY] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804164101_AddProcurementSplitAwards'
)
BEGIN
    EXEC(N'ALTER TABLE [RII_PC_QUOTE_LINE] ADD CONSTRAINT [CK_RII_PC_QUOTE_LINE_AMOUNTS] CHECK ([QuotedQuantity] > 0 AND [ConvertedQuantity] >= 0 AND [ConvertedQuantity] <= [QuotedQuantity] AND [UnitPrice] >= 0 AND [DiscountRate] >= 0 AND [DiscountRate] <= 100 AND [VatRate] >= 0)');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804164101_AddProcurementSplitAwards'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_RII_PC_POLICY_BranchCode_PolicyKey] ON [RII_PC_POLICY] ([BranchCode], [PolicyKey]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804164101_AddProcurementSplitAwards'
)
BEGIN
    CREATE INDEX [IX_RII_PC_POLICY_IsDeleted] ON [RII_PC_POLICY] ([IsDeleted]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260804164101_AddProcurementSplitAwards'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260804164101_AddProcurementSplitAwards', N'10.0.10');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260805092111_AddConfigurableProductionOrderSources'
)
BEGIN
    ALTER TABLE [RII_PT_POLICIES] ADD [ProductionOrderSource] nvarchar(40) NOT NULL DEFAULT N'NetsisErpFunctions';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260805092111_AddConfigurableProductionOrderSources'
)
BEGIN
    ALTER TABLE [RII_PT_POLICIES] ADD [WmsSourceSystemCode] nvarchar(50) NOT NULL DEFAULT N'WINDBOX';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260805092111_AddConfigurableProductionOrderSources'
)
BEGIN
    CREATE TABLE [RII_PR_SOURCE_ORDER] (
        [Id] bigint NOT NULL IDENTITY,
        [SourceSystemCode] nvarchar(50) NOT NULL,
        [ExternalKey] nvarchar(150) NOT NULL,
        [WorkOrderNumber] nvarchar(100) NOT NULL,
        [RevisionNumber] int NOT NULL,
        [Status] nvarchar(30) NOT NULL,
        [ProductCode] nvarchar(100) NOT NULL,
        [ProductName] nvarchar(300) NULL,
        [ConfigurationCode] nvarchar(100) NULL,
        [PlannedQuantity] decimal(20,6) NOT NULL,
        [UnitCode] nvarchar(20) NOT NULL,
        [SourceWarehouseCode] int NOT NULL,
        [TargetWarehouseCode] int NOT NULL,
        [WorkOrderDate] datetime2 NULL,
        [DeliveryDate] datetime2 NULL,
        [ProjectCode] nvarchar(100) NULL,
        [SourceUpdatedAtUtc] datetimeoffset NOT NULL,
        [PayloadHash] nvarchar(128) NULL,
        [RowVersion] rowversion NOT NULL,
        [BranchCode] nvarchar(10) NOT NULL DEFAULT N'0',
        [CreatedDate] datetime2 NULL,
        [UpdatedDate] datetime2 NULL,
        [DeletedDate] datetime2 NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [CreatedBy] bigint NULL,
        [UpdatedBy] bigint NULL,
        [DeletedBy] bigint NULL,
        CONSTRAINT [PK_RII_PR_SOURCE_ORDER] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_RII_PR_SOURCE_ORDER_QTY_REV] CHECK ([PlannedQuantity] > 0 AND [RevisionNumber] > 0)
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260805092111_AddConfigurableProductionOrderSources'
)
BEGIN
    CREATE TABLE [RII_PR_SOURCE_RECIPE] (
        [Id] bigint NOT NULL IDENTITY,
        [ProductionSourceWorkOrderId] bigint NOT NULL,
        [LineNumber] int NOT NULL,
        [OperationNumber] int NOT NULL,
        [ComponentStockCode] nvarchar(100) NOT NULL,
        [ComponentStockName] nvarchar(300) NULL,
        [ComponentConfigurationCode] nvarchar(100) NULL,
        [UnitCode] nvarchar(20) NOT NULL,
        [RecipeQuantity] decimal(20,6) NOT NULL,
        [VariableWasteQuantity] decimal(20,6) NOT NULL,
        [FixedWasteQuantity] decimal(20,6) NOT NULL,
        [TotalRequiredQuantity] decimal(20,6) NOT NULL,
        [IsMandatory] bit NOT NULL,
        [RowVersion] rowversion NOT NULL,
        [BranchCode] nvarchar(10) NOT NULL DEFAULT N'0',
        [CreatedDate] datetime2 NULL,
        [UpdatedDate] datetime2 NULL,
        [DeletedDate] datetime2 NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [CreatedBy] bigint NULL,
        [UpdatedBy] bigint NULL,
        [DeletedBy] bigint NULL,
        CONSTRAINT [PK_RII_PR_SOURCE_RECIPE] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_RII_PR_SOURCE_RECIPE_QTY] CHECK ([LineNumber] > 0 AND [OperationNumber] >= 0 AND [RecipeQuantity] > 0 AND [TotalRequiredQuantity] > 0 AND [VariableWasteQuantity] >= 0 AND [FixedWasteQuantity] >= 0),
        CONSTRAINT [FK_RII_PR_SOURCE_RECIPE_RII_PR_SOURCE_ORDER_ProductionSourceWorkOrderId] FOREIGN KEY ([ProductionSourceWorkOrderId]) REFERENCES [RII_PR_SOURCE_ORDER] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260805092111_AddConfigurableProductionOrderSources'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_RII_PR_SOURCE_ORDER_BranchCode_SourceSystemCode_ExternalKey] ON [RII_PR_SOURCE_ORDER] ([BranchCode], [SourceSystemCode], [ExternalKey]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260805092111_AddConfigurableProductionOrderSources'
)
BEGIN
    CREATE INDEX [IX_RII_PR_SOURCE_ORDER_BranchCode_SourceSystemCode_Status_SourceUpdatedAtUtc] ON [RII_PR_SOURCE_ORDER] ([BranchCode], [SourceSystemCode], [Status], [SourceUpdatedAtUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260805092111_AddConfigurableProductionOrderSources'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_RII_PR_SOURCE_ORDER_BranchCode_SourceSystemCode_WorkOrderNumber_RevisionNumber] ON [RII_PR_SOURCE_ORDER] ([BranchCode], [SourceSystemCode], [WorkOrderNumber], [RevisionNumber]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260805092111_AddConfigurableProductionOrderSources'
)
BEGIN
    CREATE INDEX [IX_RII_PR_SOURCE_ORDER_IsDeleted] ON [RII_PR_SOURCE_ORDER] ([IsDeleted]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260805092111_AddConfigurableProductionOrderSources'
)
BEGIN
    CREATE INDEX [IX_RII_PR_SOURCE_RECIPE_ComponentStockCode_ComponentConfigurationCode] ON [RII_PR_SOURCE_RECIPE] ([ComponentStockCode], [ComponentConfigurationCode]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260805092111_AddConfigurableProductionOrderSources'
)
BEGIN
    CREATE INDEX [IX_RII_PR_SOURCE_RECIPE_IsDeleted] ON [RII_PR_SOURCE_RECIPE] ([IsDeleted]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260805092111_AddConfigurableProductionOrderSources'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_RII_PR_SOURCE_RECIPE_ProductionSourceWorkOrderId_LineNumber] ON [RII_PR_SOURCE_RECIPE] ([ProductionSourceWorkOrderId], [LineNumber]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260805092111_AddConfigurableProductionOrderSources'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260805092111_AddConfigurableProductionOrderSources', N'10.0.10');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260805094423_AddCombinedProductionSourcesAndManualErpPolicy'
)
BEGIN
    ALTER TABLE [RII_PT_POLICIES] ADD [RequireErpMasterDataForManualTransfer] bit NOT NULL DEFAULT CAST(1 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260805094423_AddCombinedProductionSourcesAndManualErpPolicy'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260805094423_AddCombinedProductionSourcesAndManualErpPolicy', N'10.0.10');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260805095114_AddProductionExternalSourceIdentity'
)
BEGIN
    ALTER TABLE [RII_PR_ORDER] ADD [ExternalSourceSystemCode] nvarchar(50) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260805095114_AddProductionExternalSourceIdentity'
)
BEGIN
    EXEC(N'CREATE INDEX [IX_RII_PR_ORDER_BranchCode_ExternalSourceSystemCode_ExternalOrderNo] ON [RII_PR_ORDER] ([BranchCode], [ExternalSourceSystemCode], [ExternalOrderNo]) WHERE [IsDeleted] = 0 AND [ExternalOrderNo] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260805095114_AddProductionExternalSourceIdentity'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260805095114_AddProductionExternalSourceIdentity', N'10.0.10');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260805101019_AddDefaultProductionTransferLocation'
)
BEGIN
    ALTER TABLE [RII_WAREHOUSE] ADD [DefaultProductionTransferLocationId] bigint NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260805101019_AddDefaultProductionTransferLocation'
)
BEGIN
    CREATE INDEX [IX_RII_WAREHOUSE_DEFAULT_PRODUCTION_TRANSFER_LOCATION] ON [RII_WAREHOUSE] ([DefaultProductionTransferLocationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260805101019_AddDefaultProductionTransferLocation'
)
BEGIN
    ALTER TABLE [RII_WAREHOUSE] ADD CONSTRAINT [FK_RII_WAREHOUSE_RII_LOCATION_DefaultProductionTransferLocationId] FOREIGN KEY ([DefaultProductionTransferLocationId]) REFERENCES [RII_LOCATION] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260805101019_AddDefaultProductionTransferLocation'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260805101019_AddDefaultProductionTransferLocation', N'10.0.10');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260805110139_AddProcurementSupplierPortal'
)
BEGIN
    ALTER TABLE [RII_PC_QUOTE] ADD [PreviousQuoteId] bigint NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260805110139_AddProcurementSupplierPortal'
)
BEGIN
    ALTER TABLE [RII_PC_QUOTE] ADD [RevisionNo] int NOT NULL DEFAULT 1;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260805110139_AddProcurementSupplierPortal'
)
BEGIN
    ALTER TABLE [RII_PC_QUOTE] ADD [SubmittedAtUtc] datetimeoffset NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260805110139_AddProcurementSupplierPortal'
)
BEGIN
    CREATE TABLE [RII_PC_QUOTE_INVITATION] (
        [Id] bigint NOT NULL IDENTITY,
        [ProcurementRfqId] bigint NOT NULL,
        [ProcurementRfqSupplierId] bigint NOT NULL,
        [SupplierId] bigint NOT NULL,
        [RecipientEmail] nvarchar(320) NOT NULL,
        [TokenHash] nchar(64) NOT NULL,
        [Status] nvarchar(30) NOT NULL,
        [ExpiresAtUtc] datetimeoffset NOT NULL,
        [FirstOpenedAtUtc] datetimeoffset NULL,
        [LastOpenedAtUtc] datetimeoffset NULL,
        [LastSentAtUtc] datetimeoffset NULL,
        [SubmittedAtUtc] datetimeoffset NULL,
        [RevokedAtUtc] datetimeoffset NULL,
        [CurrentQuoteId] bigint NULL,
        [RowVersion] rowversion NOT NULL,
        [BranchCode] nvarchar(10) NOT NULL DEFAULT N'0',
        [CreatedDate] datetime2 NULL,
        [UpdatedDate] datetime2 NULL,
        [DeletedDate] datetime2 NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [CreatedBy] bigint NULL,
        [UpdatedBy] bigint NULL,
        [DeletedBy] bigint NULL,
        CONSTRAINT [PK_RII_PC_QUOTE_INVITATION] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_RII_PC_QUOTE_INVITATION_RII_PC_QUOTE_CurrentQuoteId] FOREIGN KEY ([CurrentQuoteId]) REFERENCES [RII_PC_QUOTE] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_RII_PC_QUOTE_INVITATION_RII_PC_RFQ_ProcurementRfqId] FOREIGN KEY ([ProcurementRfqId]) REFERENCES [RII_PC_RFQ] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_RII_PC_QUOTE_INVITATION_RII_PC_RFQ_SUPPLIER_ProcurementRfqSupplierId] FOREIGN KEY ([ProcurementRfqSupplierId]) REFERENCES [RII_PC_RFQ_SUPPLIER] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260805110139_AddProcurementSupplierPortal'
)
BEGIN
    CREATE INDEX [IX_RII_PC_QUOTE_INVITATION_CurrentQuoteId] ON [RII_PC_QUOTE_INVITATION] ([CurrentQuoteId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260805110139_AddProcurementSupplierPortal'
)
BEGIN
    CREATE INDEX [IX_RII_PC_QUOTE_INVITATION_IsDeleted] ON [RII_PC_QUOTE_INVITATION] ([IsDeleted]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260805110139_AddProcurementSupplierPortal'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_RII_PC_QUOTE_INVITATION_ProcurementRfqId_SupplierId] ON [RII_PC_QUOTE_INVITATION] ([ProcurementRfqId], [SupplierId]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260805110139_AddProcurementSupplierPortal'
)
BEGIN
    CREATE INDEX [IX_RII_PC_QUOTE_INVITATION_ProcurementRfqSupplierId] ON [RII_PC_QUOTE_INVITATION] ([ProcurementRfqSupplierId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260805110139_AddProcurementSupplierPortal'
)
BEGIN
    CREATE INDEX [IX_RII_PC_QUOTE_INVITATION_Status_ExpiresAtUtc] ON [RII_PC_QUOTE_INVITATION] ([Status], [ExpiresAtUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260805110139_AddProcurementSupplierPortal'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_RII_PC_QUOTE_INVITATION_TokenHash] ON [RII_PC_QUOTE_INVITATION] ([TokenHash]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260805110139_AddProcurementSupplierPortal'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260805110139_AddProcurementSupplierPortal', N'10.0.10');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260805112517_ParameterizeSupplierQuotePortal'
)
BEGIN
    ALTER TABLE [RII_PC_POLICY] ADD [AllowSupplierDraftSave] bit NOT NULL DEFAULT CAST(1 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260805112517_ParameterizeSupplierQuotePortal'
)
BEGIN
    ALTER TABLE [RII_PC_POLICY] ADD [AllowSupplierQuantityChange] bit NOT NULL DEFAULT CAST(1 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260805112517_ParameterizeSupplierQuotePortal'
)
BEGIN
    ALTER TABLE [RII_PC_POLICY] ADD [AllowSupplierRevisions] bit NOT NULL DEFAULT CAST(1 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260805112517_ParameterizeSupplierQuotePortal'
)
BEGIN
    ALTER TABLE [RII_PC_POLICY] ADD [AllowZeroUnitPrice] bit NOT NULL DEFAULT CAST(0 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260805112517_ParameterizeSupplierQuotePortal'
)
BEGIN
    ALTER TABLE [RII_PC_POLICY] ADD [InvitationValidityDays] int NOT NULL DEFAULT 7;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260805112517_ParameterizeSupplierQuotePortal'
)
BEGIN
    ALTER TABLE [RII_PC_POLICY] ADD [MaximumSupplierRevisionCount] int NOT NULL DEFAULT 3;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260805112517_ParameterizeSupplierQuotePortal'
)
BEGIN
    ALTER TABLE [RII_PC_POLICY] ADD [RequireSupplierDeliveryDate] bit NOT NULL DEFAULT CAST(0 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260805112517_ParameterizeSupplierQuotePortal'
)
BEGIN
    ALTER TABLE [RII_PC_POLICY] ADD [SupplierQuoteChannelMode] int NOT NULL DEFAULT 2;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260805112517_ParameterizeSupplierQuotePortal'
)
BEGIN
    EXEC(N'ALTER TABLE [RII_PC_POLICY] ADD CONSTRAINT [CK_RII_PC_POLICY_SUPPLIER_PORTAL] CHECK ([SupplierQuoteChannelMode] IN (1, 2, 3) AND [InvitationValidityDays] BETWEEN 1 AND 30 AND [MaximumSupplierRevisionCount] BETWEEN 0 AND 20)');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260805112517_ParameterizeSupplierQuotePortal'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260805112517_ParameterizeSupplierQuotePortal', N'10.0.10');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260805131731_AddProtectedDefaultPermissionGroups'
)
BEGIN
    ALTER TABLE [RII_PERMISSION_GROUPS] ADD [IsProtected] bit NOT NULL DEFAULT CAST(0 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260805131731_AddProtectedDefaultPermissionGroups'
)
BEGIN
    ALTER TABLE [RII_PERMISSION_GROUPS] ADD [TemplateKey] nvarchar(80) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260805131731_AddProtectedDefaultPermissionGroups'
)
BEGIN
    EXEC(N'UPDATE [RII_PERMISSION_GROUPS] SET [IsProtected] = CAST(1 AS bit), [TemplateKey] = N''SYSTEM_ADMINISTRATORS''
    WHERE [Id] = CAST(1001 AS bigint);
    SELECT @@ROWCOUNT');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260805131731_AddProtectedDefaultPermissionGroups'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_RII_PERMISSION_GROUPS_TemplateKey] ON [RII_PERMISSION_GROUPS] ([TemplateKey]) WHERE [TemplateKey] IS NOT NULL AND [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260805131731_AddProtectedDefaultPermissionGroups'
)
BEGIN
    EXEC sys.sp_executesql N'INSERT INTO RII_PERMISSION_GROUPS
        (BranchCode, CreatedDate, IsDeleted, Name, Description, IsSystemAdmin, IsProtected, TemplateKey, IsActive)
    SELECT ''0'', SYSUTCDATETIME(), 0, seed.Name, seed.Description, 0, 1, seed.TemplateKey, 1
    FROM (VALUES
        (N''DEPO_YONETICILERI'', N''Depo Yöneticileri'', N''Depo operasyonlarını ve operasyon ayarlarını yöneten kurumsal varsayılan şablon.''),
        (N''DEPO_OPERATORLERI'', N''Depo Operatörleri'', N''Atanmış fiziksel depo işlerini yürüten kullanıcılar için güvenli varsayılan şablon.''),
        (N''KALITE_UZMANLARI'', N''Kalite Uzmanları'', N''Kalite kontrol, inceleme ve serbest bırakma işlemleri için varsayılan şablon.''),
        (N''SALT_OKUNUR_RAPORLAMA'', N''Salt Okunur ve Raporlama'', N''Operasyon verilerini değiştirmeden görüntüleme ve raporlama için varsayılan şablon.'')
    ) seed(TemplateKey, Name, Description)
    WHERE NOT EXISTS (
        SELECT 1 FROM RII_PERMISSION_GROUPS existing
        WHERE existing.TemplateKey = seed.TemplateKey AND existing.IsDeleted = 0
    );

    INSERT INTO RII_PERMISSION_GROUP_PERMISSIONS
        (BranchCode, CreatedDate, IsDeleted, PermissionGroupId, PermissionDefinitionId)
    SELECT ''0'', SYSUTCDATETIME(), 0, groups.Id, permissions.Id
    FROM RII_PERMISSION_GROUPS groups
    JOIN RII_PERMISSION_DEFINITIONS permissions ON permissions.IsDeleted = 0 AND permissions.IsActive = 1
    WHERE groups.IsDeleted = 0
      AND (
        (groups.TemplateKey = N''DEPO_YONETICILERI'' AND (permissions.Code LIKE N''WMS.%'' OR permissions.Code LIKE N''ERP.%''))
        OR
        (groups.TemplateKey = N''DEPO_OPERATORLERI''
            AND permissions.Code LIKE N''WMS.%''
            AND (
                permissions.Code LIKE N''%.VIEW''
                OR permissions.Code LIKE N''%.CREATE''
                OR permissions.Code LIKE N''%.OPERATE''
                OR permissions.Code LIKE N''%.RECEIVE''
                OR permissions.Code LIKE N''%.COMPLETE''
                OR permissions.Code LIKE N''%.PRINT''
                OR permissions.Code LIKE N''%.CHECK''
                OR permissions.Code LIKE N''%.POST''
            )
            AND permissions.Code NOT LIKE N''%.SETTINGS.%''
            AND permissions.Code NOT LIKE N''%.POLICY.%''
            AND permissions.Code NOT LIKE N''%.RULES.%'')
        OR
        (groups.TemplateKey = N''KALITE_UZMANLARI''
            AND (
                permissions.Code LIKE N''WMS.QUALITY.%''
                OR permissions.Code IN (
                    N''WMS.GOODS_RECEIPT.VIEW'',
                    N''WMS.STOCK_BALANCES.VIEW'',
                    N''WMS.STOCK_MOVEMENTS.VIEW''
                )
            ))
        OR
        (groups.TemplateKey = N''SALT_OKUNUR_RAPORLAMA''
            AND (permissions.Code LIKE N''%.VIEW'' OR permissions.Code LIKE N''%.REPORTS.VIEW''))
      )
      AND NOT EXISTS (
        SELECT 1 FROM RII_PERMISSION_GROUP_PERMISSIONS link
        WHERE link.PermissionGroupId = groups.Id
          AND link.PermissionDefinitionId = permissions.Id
          AND link.IsDeleted = 0
      );

    UPDATE users
    SET users.Role = N''Admin''
    FROM RII_USERS users
    WHERE LOWER(users.Role) <> N''superadmin''
      AND EXISTS (
        SELECT 1
        FROM RII_USER_PERMISSION_GROUPS userGroups
        JOIN RII_PERMISSION_GROUPS groups ON groups.Id = userGroups.PermissionGroupId
        WHERE userGroups.UserId = users.Id
          AND userGroups.IsDeleted = 0
          AND groups.IsDeleted = 0
          AND groups.IsActive = 1
          AND groups.IsSystemAdmin = 1
      );

    UPDATE users
    SET users.Role = N''User''
    FROM RII_USERS users
    WHERE LOWER(users.Role) = N''admin''
      AND NOT EXISTS (
        SELECT 1
        FROM RII_USER_PERMISSION_GROUPS userGroups
        JOIN RII_PERMISSION_GROUPS groups ON groups.Id = userGroups.PermissionGroupId
        WHERE userGroups.UserId = users.Id
          AND userGroups.IsDeleted = 0
          AND groups.IsDeleted = 0
          AND groups.IsActive = 1
          AND groups.IsSystemAdmin = 1
      );';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260805131731_AddProtectedDefaultPermissionGroups'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260805131731_AddProtectedDefaultPermissionGroups', N'10.0.10');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260805140037_AddOperationalPermissionGroupTemplates'
)
BEGIN
    EXEC sys.sp_executesql N'INSERT INTO RII_PERMISSION_GROUPS
        (BranchCode, CreatedDate, IsDeleted, Name, Description, IsSystemAdmin, IsProtected, TemplateKey, IsActive)
    SELECT ''0'', SYSUTCDATETIME(), 0, seed.Name, seed.Description, 0, 1, seed.TemplateKey, 1
    FROM (VALUES
        (N''VARDIYA_AMIRLERI'', N''Vardiya Amirleri'', N''Operasyonları izler; emir atama, onay, serbest bırakma, kısmi/tam tamamlama ve kontrollü iptal işlemlerini yürütür.''),
        (N''MAL_KABUL_OPERATORLERI'', N''Mal Kabul Operatörleri'', N''Siparişli, siparişsiz, doğrudan ve sac mal kabulünün fiziksel kabul adımlarını yürütür.''),
        (N''YERLESTIRME_TRANSFER_OPERATORLERI'', N''Yerleştirme ve Transfer Operatörleri'', N''Raf yerleştirme, ambar giriş ve depolar arası/üretim/fason transfer görevlerini yürütür.''),
        (N''TOPLAMA_SEVK_PAKETLEME_OPERATORLERI'', N''Toplama, Sevk ve Paketleme Operatörleri'', N''Toplama, ambar çıkış, paketleme, etiketleme ve sevk operasyonlarını yürütür.''),
        (N''KALITE_YONETICILERI'', N''Kalite Yöneticileri'', N''Kalite kurallarını, ayarlarını, inceleme kararlarını ve stok serbest bırakma yetkisini yönetir.''),
        (N''STOK_KONTROL_UZMANLARI'', N''Stok Kontrol Uzmanları'', N''Raf ve stok tanımları, stok hareketi ters kayıtları ve bakiye uzlaştırma işlemlerini yürütür.''),
        (N''URETIM_LOJISTIK_OPERATORLERI'', N''Üretim Lojistik Operatörleri'', N''Üretim emirleri ile üretime ve fasona transfer operasyonlarını yürütür.''),
        (N''SATINALMA_UZMANLARI'', N''Satınalma Uzmanları'', N''Satınalma talebi, teklif talebi, tedarikçi teklifi ve sipariş kayıtlarını onay yetkisi olmadan yönetir.''),
        (N''SATINALMA_ONAYLAYICILARI'', N''Satınalma Onaylayıcıları'', N''Satınalma belgelerini görüntüler ve görevler ayrılığına uygun olarak onaylar.''),
        (N''KKD_OPERATORLERI'', N''KKD Operatörleri'', N''KKD personel, hak sorgulama, dağıtım ve raporlama operasyonlarını yürütür.''),
        (N''KKD_YONETICILERI'', N''KKD Yöneticileri'', N''KKD tanım, matris, politika, kota aşımı ve operasyon kurallarını yönetir.''),
        (N''ERP_ENTEGRASYON_UZMANLARI'', N''ERP Entegrasyon Uzmanları'', N''ERP/Netsis veri okuma, eşleme, yeniden deneme, entegrasyon bağlantıları ve iş tetikleme işlemlerini yürütür.''),
        (N''SISTEM_DENETCILERI'', N''Sistem Denetçileri'', N''Operasyon ve ERP kayıtlarını değiştirmeden görüntüler; denetim kayıtlarına erişir.'')
    ) seed(TemplateKey, Name, Description)
    WHERE NOT EXISTS (
        SELECT 1 FROM RII_PERMISSION_GROUPS existing
        WHERE existing.TemplateKey = seed.TemplateKey AND existing.IsDeleted = 0
    );

    DELETE links
    FROM RII_PERMISSION_GROUP_PERMISSIONS links
    JOIN RII_PERMISSION_GROUPS groups ON groups.Id = links.PermissionGroupId
    WHERE groups.TemplateKey IN (
        N''DEPO_YONETICILERI'', N''KALITE_UZMANLARI'', N''SALT_OKUNUR_RAPORLAMA'',
        N''VARDIYA_AMIRLERI'', N''MAL_KABUL_OPERATORLERI'', N''YERLESTIRME_TRANSFER_OPERATORLERI'',
        N''TOPLAMA_SEVK_PAKETLEME_OPERATORLERI'', N''KALITE_YONETICILERI'', N''STOK_KONTROL_UZMANLARI'',
        N''URETIM_LOJISTIK_OPERATORLERI'', N''SATINALMA_UZMANLARI'', N''SATINALMA_ONAYLAYICILARI'',
        N''KKD_OPERATORLERI'', N''KKD_YONETICILERI'', N''ERP_ENTEGRASYON_UZMANLARI'', N''SISTEM_DENETCILERI''
    );

    INSERT INTO RII_PERMISSION_GROUP_PERMISSIONS
        (BranchCode, CreatedDate, IsDeleted, PermissionGroupId, PermissionDefinitionId)
    SELECT ''0'', SYSUTCDATETIME(), 0, groups.Id, permissions.Id
    FROM RII_PERMISSION_GROUPS groups
    CROSS JOIN RII_PERMISSION_DEFINITIONS permissions
    WHERE groups.IsDeleted = 0
      AND permissions.IsDeleted = 0
      AND permissions.IsActive = 1
      AND (
        (groups.TemplateKey = N''DEPO_YONETICILERI''
            AND permissions.Code LIKE N''WMS.%''
            AND permissions.Code NOT LIKE N''WMS.PROCUREMENT.%''
            AND permissions.Code NOT LIKE N''WMS.KKD.%'')
        OR
        (groups.TemplateKey = N''KALITE_UZMANLARI'' AND permissions.Code IN (
            N''WMS.QUALITY.INSPECTIONS.VIEW'', N''WMS.QUALITY.INSPECTIONS.DECIDE'',
            N''WMS.QUALITY.RULES.VIEW'', N''WMS.QUALITY.SETTINGS.VIEW'',
            N''WMS.GOODS_RECEIPT.VIEW'', N''WMS.STOCK_BALANCES.VIEW'', N''WMS.STOCK_MOVEMENTS.VIEW''))
        OR
        (groups.TemplateKey = N''SALT_OKUNUR_RAPORLAMA''
            AND (permissions.Code LIKE N''WMS.%.VIEW'' OR permissions.Code = N''ERP.MIRROR.VIEW''))
        OR
        (groups.TemplateKey = N''VARDIYA_AMIRLERI''
            AND permissions.Code LIKE N''WMS.%''
            AND (permissions.Code LIKE N''%.VIEW'' OR permissions.Code LIKE N''%.APPROVE''
                OR permissions.Code LIKE N''%.ASSIGN'' OR permissions.Code LIKE N''%.RELEASE''
                OR permissions.Code LIKE N''%.COMPLETE'' OR permissions.Code LIKE N''%.CANCEL''
                OR permissions.Code LIKE N''%.REOPEN'' OR permissions.Code LIKE N''%.DECIDE''))
        OR
        (groups.TemplateKey = N''MAL_KABUL_OPERATORLERI'' AND (
            permissions.Code IN (
                N''WMS.GOODS_RECEIPT.VIEW'', N''WMS.GOODS_RECEIPT.CREATE'', N''WMS.GOODS_RECEIPT.UPDATE'',
                N''WMS.GOODS_RECEIPT.RECEIVE'', N''WMS.GOODS_RECEIPT.COMPLETE'',
                N''WMS.GOODS_RECEIPT.SETTINGS.VIEW'', N''WMS.LOCATIONS.VIEW'', N''WMS.STOCK_BALANCES.VIEW'',
                N''WMS.BARCODE_DESIGNER.VIEW'', N''WMS.BARCODE_DESIGNER.PRINT'',
                N''WMS.BARCODE_POLICY.VIEW'', N''WMS.BARCODE_POLICY.GENERATE'', N''ERP.NETSIS_READ.VIEW'')
            OR permissions.Code LIKE N''WMS.STEEL_RECEIPT.%''))
        OR
        (groups.TemplateKey = N''YERLESTIRME_TRANSFER_OPERATORLERI'' AND (
            permissions.Code IN (N''WMS.LOCATIONS.VIEW'', N''WMS.STOCK_BALANCES.VIEW'', N''WMS.STOCK_MOVEMENTS.VIEW'')
            OR (permissions.Code LIKE N''WMS.WAREHOUSE_TRANSFER.%'' AND permissions.Code NOT LIKE N''%.APPROVE'')
            OR (permissions.Code LIKE N''WMS.WAREHOUSE_INBOUND.%'' AND permissions.Code NOT LIKE N''%.SETTINGS.MANAGE'')
            OR permissions.Code IN (N''WMS.PRODUCTION_TRANSFER.VIEW'', N''WMS.PRODUCTION_TRANSFER.OPERATE'',
                N''WMS.SUBCONTRACTING_TRANSFER.VIEW'', N''WMS.SUBCONTRACTING_TRANSFER.OPERATE'')))
        OR
        (groups.TemplateKey = N''TOPLAMA_SEVK_PAKETLEME_OPERATORLERI'' AND (
            permissions.Code IN (N''WMS.LOCATIONS.VIEW'', N''WMS.STOCK_BALANCES.VIEW'',
                N''WMS.BARCODE_DESIGNER.VIEW'', N''WMS.BARCODE_DESIGNER.PRINT'')
            OR (permissions.Code LIKE N''WMS.SHIPPING.%'' AND permissions.Code NOT LIKE N''%.APPROVE'' AND permissions.Code NOT LIKE N''%.SETTINGS.MANAGE'')
            OR (permissions.Code LIKE N''WMS.WAREHOUSE_OUTBOUND.%'' AND permissions.Code NOT LIKE N''%.APPROVE'' AND permissions.Code NOT LIKE N''%.SETTINGS.MANAGE'')
            OR (permissions.Code LIKE N''WMS.PACKING.%'' AND permissions.Code NOT LIKE N''%.DEFINITIONS.MANAGE'' AND permissions.Code NOT LIKE N''%.SETTINGS.MANAGE'')))
        OR
        (groups.TemplateKey = N''KALITE_YONETICILERI'' AND (
            permissions.Code LIKE N''WMS.QUALITY.%''
            OR permissions.Code IN (N''WMS.GOODS_RECEIPT.VIEW'', N''WMS.STOCK_BALANCES.VIEW'', N''WMS.STOCK_MOVEMENTS.VIEW'')))
        OR
        (groups.TemplateKey = N''STOK_KONTROL_UZMANLARI'' AND (
            permissions.Code LIKE N''WMS.LOCATIONS.%''
            OR permissions.Code LIKE N''WMS.STOCK_BALANCES.%''
            OR permissions.Code LIKE N''WMS.STOCK_MOVEMENTS.%''
            OR permissions.Code IN (N''WMS.DOCUMENT_SERIES.VIEW'', N''ERP.MIRROR.VIEW'')))
        OR
        (groups.TemplateKey = N''URETIM_LOJISTIK_OPERATORLERI'' AND (
            permissions.Code IN (N''WMS.LOCATIONS.VIEW'', N''WMS.STOCK_BALANCES.VIEW'', N''ERP.NETSIS_READ.VIEW'')
            OR permissions.Code LIKE N''WMS.PRODUCTION.%''
            OR (permissions.Code LIKE N''WMS.PRODUCTION_TRANSFER.%'' AND permissions.Code NOT LIKE N''%.SETTINGS.MANAGE'' AND permissions.Code NOT LIKE N''%.APPROVE'')
            OR (permissions.Code LIKE N''WMS.SUBCONTRACTING_TRANSFER.%'' AND permissions.Code NOT LIKE N''%.SETTINGS.MANAGE'' AND permissions.Code NOT LIKE N''%.APPROVE'')))
        OR
        (groups.TemplateKey = N''SATINALMA_UZMANLARI''
            AND permissions.Code LIKE N''WMS.PROCUREMENT.%'' AND permissions.Code <> N''WMS.PROCUREMENT.APPROVE'')
        OR
        (groups.TemplateKey = N''SATINALMA_ONAYLAYICILARI''
            AND permissions.Code IN (N''WMS.PROCUREMENT.VIEW'', N''WMS.PROCUREMENT.APPROVE''))
        OR
        (groups.TemplateKey = N''KKD_OPERATORLERI'' AND permissions.Code IN (
            N''WMS.KKD.DEFINITIONS.VIEW'', N''WMS.KKD.EMPLOYEES.VIEW'', N''WMS.KKD.EMPLOYEES.MANAGE'',
            N''WMS.KKD.MATRICES.VIEW'', N''WMS.KKD.ENTITLEMENT.CHECK'', N''WMS.KKD.DISTRIBUTION.OPERATE'',
            N''WMS.KKD.REPORTS.VIEW''))
        OR
        (groups.TemplateKey = N''KKD_YONETICILERI'' AND permissions.Code LIKE N''WMS.KKD.%'')
        OR
        (groups.TemplateKey = N''ERP_ENTEGRASYON_UZMANLARI'' AND (
            permissions.Code LIKE N''ERP.%''
            OR permissions.Code IN (N''SYSTEM.HANGFIRE.VIEW'', N''SYSTEM.HANGFIRE.TRIGGER'',
                N''WMS.GOODS_RECEIPT.ERP_RETRY'', N''WMS.INCOMING_INVOICE.VIEW'', N''WMS.INCOMING_INVOICE.IMPORT'',
                N''WMS.INCOMING_INVOICE.OCR_IMPORT'', N''WMS.INCOMING_INVOICE.CONNECTIONS.MANAGE'')))
        OR
        (groups.TemplateKey = N''SISTEM_DENETCILERI'' AND (
            permissions.Code = N''SYSTEM.AUDIT.VIEW''
            OR permissions.Code LIKE N''WMS.%.VIEW''
            OR permissions.Code IN (N''ERP.MIRROR.VIEW'', N''ERP.NETSIS_READ.VIEW'')))
      );';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260805140037_AddOperationalPermissionGroupTemplates'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260805140037_AddOperationalPermissionGroupTemplates', N'10.0.10');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260805192720_AddProductionTaskAssignmentReturn'
)
BEGIN
    ALTER TABLE [RII_WT_TASK] ADD [OriginTaskId] bigint NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260805192720_AddProductionTaskAssignmentReturn'
)
BEGIN
    ALTER TABLE [RII_WT_TASK] ADD [OriginUserId] bigint NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260805192720_AddProductionTaskAssignmentReturn'
)
BEGIN
    ALTER TABLE [RII_WT_TASK] ADD [PreviousTaskId] bigint NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260805192720_AddProductionTaskAssignmentReturn'
)
BEGIN
    CREATE INDEX [IX_RII_WT_TASK_OriginTaskId] ON [RII_WT_TASK] ([OriginTaskId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260805192720_AddProductionTaskAssignmentReturn'
)
BEGIN
    CREATE INDEX [IX_RII_WT_TASK_PreviousTaskId] ON [RII_WT_TASK] ([PreviousTaskId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260805192720_AddProductionTaskAssignmentReturn'
)
BEGIN
    ALTER TABLE [RII_WT_TASK] ADD CONSTRAINT [FK_RII_WT_TASK_RII_WT_TASK_OriginTaskId] FOREIGN KEY ([OriginTaskId]) REFERENCES [RII_WT_TASK] ([Id]) ON DELETE NO ACTION;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260805192720_AddProductionTaskAssignmentReturn'
)
BEGIN
    ALTER TABLE [RII_WT_TASK] ADD CONSTRAINT [FK_RII_WT_TASK_RII_WT_TASK_PreviousTaskId] FOREIGN KEY ([PreviousTaskId]) REFERENCES [RII_WT_TASK] ([Id]) ON DELETE NO ACTION;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260805192720_AddProductionTaskAssignmentReturn'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260805192720_AddProductionTaskAssignmentReturn', N'10.0.10');
END;

COMMIT;
GO

/* Calistirma sonu sema dogrulamasi. */
SELECT
    COL_LENGTH(N'dbo.RII_WAREHOUSE', N'DefaultGoodsReceiptLocationId') AS DefaultLocationColumnByteLength,
    (SELECT COUNT_BIG(*) FROM dbo.__EFMigrationsHistory) AS AppliedMigrationCount,
    CASE WHEN EXISTS
    (
        SELECT 1 FROM dbo.__EFMigrationsHistory
        WHERE MigrationId = N'20260805192720_AddProductionTaskAssignmentReturn'
    ) THEN 1 ELSE 0 END AS LatestMigrationApplied;
GO

