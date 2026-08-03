/*
  NETSIS -> WMS V2 / DEPO + SERI ACILIS BAKIYESI

  Calistirma veritabani : WMS veritabani
  Kaynak veritabani     : [TESTV3RII]
  Kaynak                 : TBLSTOKPH + TBLSERITRA (+ TBLSTHAR baglantisi)
  Hedef                  : RII_STOCK_MOVEMENT_OPERATION + RII_STOCK_MOVEMENT

  NOTLAR
  - Bu script ilk acilis icindir. Once test/yedek veritabaninda calistirin.
  - Depo toplamlarini Netsis'in guncel bakiye tablosu TBLSTOKPH'den okur.
  - Seri kirilimini TBLSERITRA.DEPOKOD/GCKOD/MIKTAR alanlarindan okur.
  - Seri disinda kalan miktari seri bos olacak sekilde yazar.
  - Tum bakiye once deponun DefaultGoodsReceiptLocationId rafina yazilir.
  - Ardindan 02_netsis_rack_serial_distribution.sql ile gercek raflara dagitilir.
  - Ayni IdempotencyKey ikinci kez calistirilirsa veri eklemez.
*/

SET NOCOUNT ON;
SET XACT_ABORT ON;

-- SSMS ayni oturumda RETURN/THROW sonrasinda local temp tablolari yasatir.
-- Scriptin ayni sorgu penceresinde guvenle tekrar calistirilabilmesi icin
-- yalnizca bu scripte ait gecici tablolar temizlenir.
IF OBJECT_ID(N'tempdb..#Resolved', N'U') IS NOT NULL DROP TABLE #Resolved;
IF OBJECT_ID(N'tempdb..#Opening', N'U') IS NOT NULL DROP TABLE #Opening;
IF OBJECT_ID(N'tempdb..#SerialMismatch', N'U') IS NOT NULL DROP TABLE #SerialMismatch;
IF OBJECT_ID(N'tempdb..#SerialTotal', N'U') IS NOT NULL DROP TABLE #SerialTotal;
IF OBJECT_ID(N'tempdb..#DepotTotal', N'U') IS NOT NULL DROP TABLE #DepotTotal;

DECLARE @WmsBranchCode nvarchar(10) = N'0';
DECLARE @NetsisBranchCode smallint = 0;
DECLARE @AsOf datetime2(7) = SYSUTCDATETIME();
DECLARE @AllowExistingMovements bit = 0; -- sadece bilincli yeniden kurulumda 1
DECLARE @Apply bit = 0; -- once 0 ile onizleyin; kontrol sonrasi 1 yapin
DECLARE @SerialMismatchMode nvarchar(20) = N'STRICT'; -- STRICT | DEPOT_TRUTH
DECLARE @AllowUnresolvedRequiredSerial bit = 0; -- seri zorunlu stokta DEPOT_TRUTH icin bilincli olarak 1
DECLARE @IncludeNetsisVirtualWarehouseZero bit = 0; -- Netsis depo 0 genellikle tum depolar toplami
DECLARE @AutoCreateMissingDefaultLocation bit = 1; -- eksik varsayilan kabul rafini @Apply=1 iken YER1 olarak acar
DECLARE @DefaultLocationCode nvarchar(50) = N'YER1';
DECLARE @IdempotencyKey nvarchar(100) = CONCAT(N'NETSIS-OPENING-DEPOT-SERIAL-V1-', @WmsBranchCode);

IF DB_ID(N'TESTV3RII') IS NULL
    THROW 51000, N'TESTV3RII bulunamadi. Kaynak ERP veritabani adini duzeltin.', 1;

IF OBJECT_ID(N'TESTV3RII.dbo.TBLSTHAR', N'U') IS NULL
    THROW 51000, N'TESTV3RII.dbo.TBLSTHAR bulunamadi.', 1;

IF OBJECT_ID(N'TESTV3RII.dbo.TBLSTOKPH', N'U') IS NULL
    THROW 51000, N'TESTV3RII.dbo.TBLSTOKPH bulunamadi.', 1;

IF OBJECT_ID(N'TESTV3RII.dbo.TBLSERITRA', N'U') IS NULL
    THROW 51000, N'TESTV3RII.dbo.TBLSERITRA bulunamadi.', 1;

IF @SerialMismatchMode NOT IN (N'STRICT', N'DEPOT_TRUTH')
    THROW 51000, N'@SerialMismatchMode yalnizca STRICT veya DEPOT_TRUTH olabilir.', 1;

IF EXISTS (
    SELECT 1 FROM dbo.RII_STOCK_MOVEMENT_OPERATION
    WHERE IdempotencyKey = @IdempotencyKey AND IsDeleted = 0
)
BEGIN
    SELECT N'Bu sube icin depo/seri acilis kaydi daha once uygulanmis; yeni kayit eklenmedi.' AS Message;
    RETURN;
END;

CREATE TABLE #DepotTotal
(
    WarehouseCode int NOT NULL,
    StockCode nvarchar(100) NOT NULL,
    YapCode nvarchar(100) NOT NULL,
    Quantity decimal(18,6) NOT NULL,
    PRIMARY KEY (WarehouseCode, StockCode, YapCode)
);

DECLARE @StockPhYapColumn sysname = CASE
    WHEN COL_LENGTH(N'TESTV3RII.dbo.TBLSTOKPH', N'YAPKOD') IS NOT NULL THEN N'YAPKOD'
    WHEN COL_LENGTH(N'TESTV3RII.dbo.TBLSTOKPH', N'YAP_KOD') IS NOT NULL THEN N'YAP_KOD'
END;
DECLARE @UseYapDimension bit = CASE
    WHEN @StockPhYapColumn IS NOT NULL
     AND COL_LENGTH(N'TESTV3RII.dbo.TBLSTHAR', N'YAPKOD') IS NOT NULL THEN 1 ELSE 0 END;

-- Dinamik SQL yalnizca Netsis surumleri arasindaki opsiyonel YAP kolonunu
-- guvenli ele almak icindir. Depo bakiyesinin otoritesi TBLSTOKPH'dir.
DECLARE @SourceSql nvarchar(max) = N'
INSERT #DepotTotal (WarehouseCode, StockCode, YapCode, Quantity)
SELECT CONVERT(int, p.DEPO_KODU),
       LTRIM(RTRIM(p.STOK_KODU)),
       ' + CASE WHEN @UseYapDimension = 1
                THEN N'LTRIM(RTRIM(ISNULL(p.' + QUOTENAME(@StockPhYapColumn) + N',N'''')))'
                ELSE N'N''''' END + N',
       CONVERT(decimal(18,6), SUM(ISNULL(p.TOP_GIRIS_MIK,0)-ISNULL(p.TOP_CIKIS_MIK,0)))
FROM TESTV3RII.dbo.TBLSTOKPH p
WHERE p.SUBE_KODU=@pBranch
  AND (@pIncludeZero=1 OR CONVERT(int,p.DEPO_KODU)<>0)
GROUP BY p.DEPO_KODU,p.STOK_KODU'
    + CASE WHEN @UseYapDimension = 1 THEN N',p.' + QUOTENAME(@StockPhYapColumn) ELSE N'' END + N'
HAVING SUM(ISNULL(p.TOP_GIRIS_MIK,0)-ISNULL(p.TOP_CIKIS_MIK,0))>0.000001;';

EXEC sys.sp_executesql @SourceSql,
    N'@pBranch smallint,@pIncludeZero bit',
    @pBranch=@NetsisBranchCode,@pIncludeZero=@IncludeNetsisVirtualWarehouseZero;

CREATE TABLE #SerialTotal
(
    WarehouseCode int NOT NULL,
    StockCode nvarchar(100) NOT NULL,
    YapCode nvarchar(100) NOT NULL,
    SerialNo nvarchar(100) NOT NULL,
    Quantity decimal(18,6) NOT NULL,
    PRIMARY KEY (WarehouseCode, StockCode, YapCode, SerialNo)
);

SET @SourceSql = N'
INSERT #SerialTotal (WarehouseCode, StockCode, YapCode, SerialNo, Quantity)
SELECT CONVERT(int,s.DEPOKOD),
       LTRIM(RTRIM(s.STOK_KODU)),
       ' + CASE WHEN @UseYapDimension = 1
                THEN N'LTRIM(RTRIM(ISNULL(h.YAPKOD,N'''')))'
                ELSE N'N''''' END + N',
       LTRIM(RTRIM(s.SERI_NO)),
       CONVERT(decimal(18,6),SUM(CASE WHEN s.GCKOD=''G'' THEN s.MIKTAR ELSE -s.MIKTAR END))
FROM TESTV3RII.dbo.TBLSERITRA s
INNER JOIN TESTV3RII.dbo.TBLSTHAR h ON h.INCKEYNO=s.STRA_INC
WHERE h.SUBE_KODU=@pBranch
  AND (@pIncludeZero=1 OR CONVERT(int,s.DEPOKOD)<>0)
  AND NULLIF(LTRIM(RTRIM(s.SERI_NO)),N'''') IS NOT NULL
GROUP BY s.DEPOKOD,s.STOK_KODU,'
    + CASE WHEN @UseYapDimension = 1 THEN N'ISNULL(h.YAPKOD,N''''),' ELSE N'' END + N's.SERI_NO
HAVING SUM(CASE WHEN s.GCKOD=''G'' THEN s.MIKTAR ELSE -s.MIKTAR END)>0.000001;';

EXEC sys.sp_executesql @SourceSql,
    N'@pBranch smallint,@pIncludeZero bit',
    @pBranch=@NetsisBranchCode,@pIncludeZero=@IncludeNetsisVirtualWarehouseZero;

CREATE TABLE #SerialMismatch
(
    WarehouseCode int NOT NULL,
    StockCode nvarchar(100) NOT NULL,
    YapCode nvarchar(100) NOT NULL,
    DepotQuantity decimal(18,6) NOT NULL,
    SerialQuantity decimal(18,6) NOT NULL,
    ExcessQuantity decimal(18,6) NOT NULL,
    PRIMARY KEY (WarehouseCode,StockCode,YapCode)
);

INSERT #SerialMismatch
SELECT s.WarehouseCode,s.StockCode,s.YapCode,
       ISNULL(d.Quantity,0),s.Quantity,s.Quantity-ISNULL(d.Quantity,0)
FROM (
    SELECT WarehouseCode,StockCode,YapCode,SUM(Quantity) Quantity
    FROM #SerialTotal GROUP BY WarehouseCode,StockCode,YapCode
) s
LEFT JOIN #DepotTotal d ON d.WarehouseCode=s.WarehouseCode
    AND d.StockCode=s.StockCode AND d.YapCode=s.YapCode
WHERE d.WarehouseCode IS NULL OR s.Quantity>d.Quantity+0.000001;

IF EXISTS (SELECT 1 FROM #SerialMismatch)
BEGIN
    SELECT N'SERIAL_DEPOT_MISMATCH' AS DiagnosticType,
           WarehouseCode,StockCode,YapCode,DepotQuantity,SerialQuantity,ExcessQuantity
    FROM #SerialMismatch
    ORDER BY ExcessQuantity DESC,WarehouseCode,StockCode,YapCode;

    -- Bu tani Netsis'in kendi seri yonu/deposu ile stok hareketindeki degerleri
    -- yan yana verir; otomatik duzeltme veya rastgele seri kirpma yapmaz.
    SELECT TOP (200) s.DEPOKOD AS SerialWarehouse,h.DEPO_KODU AS MovementWarehouse,
           s.STOK_KODU,
           CASE WHEN @UseYapDimension=1 THEN ISNULL(h.YAPKOD,N'') ELSE N'' END AS YapCode,
           s.GCKOD AS SerialDirection,h.STHAR_GCKOD AS MovementDirection,
           COUNT_BIG(*) AS RecordCount,SUM(s.MIKTAR) AS RawSerialQuantity,
           SUM(CASE WHEN s.GCKOD='G' THEN s.MIKTAR ELSE -s.MIKTAR END) AS SignedSerialQuantity
    FROM TESTV3RII.dbo.TBLSERITRA s
    INNER JOIN TESTV3RII.dbo.TBLSTHAR h ON h.INCKEYNO=s.STRA_INC
    INNER JOIN #SerialMismatch x ON x.WarehouseCode=CONVERT(int,s.DEPOKOD)
        AND x.StockCode=LTRIM(RTRIM(s.STOK_KODU))
        AND x.YapCode=CASE WHEN @UseYapDimension=1 THEN LTRIM(RTRIM(ISNULL(h.YAPKOD,N''))) ELSE N'' END
    WHERE h.SUBE_KODU=@NetsisBranchCode
    GROUP BY s.DEPOKOD,h.DEPO_KODU,s.STOK_KODU,
             CASE WHEN @UseYapDimension=1 THEN ISNULL(h.YAPKOD,N'') ELSE N'' END,
             s.GCKOD,h.STHAR_GCKOD
    ORDER BY s.DEPOKOD,s.STOK_KODU,s.GCKOD,h.STHAR_GCKOD;

    IF @SerialMismatchMode=N'STRICT' AND @Apply=0
    BEGIN
        SELECT N'ONIZLEME BLOKAJI: Seri ve depo bakiyesi uyusmuyor. Yukaridaki SERIAL_DEPOT_MISMATCH satirlari duzeltilmeden STRICT modda veri yazilamaz.' AS Message;
        RETURN;
    END;

    IF @SerialMismatchMode=N'STRICT'
        THROW 51000, N'Netsis seri bakiyesi guncel depo bakiyesinden buyuk. Tani tablosu gercek kaynak tutarsizligini gosteriyor; veri yazilmadi.', 1;

    -- DEPOT_TRUTH secildiginde miktar otoritesi TBLSTOKPH olur. Problemli
    -- stoklarda hangi serinin hatali oldugu bilinmedigi icin seri kirpmak yerine
    -- tum ilgili kirilim serisiz acilir. Seri zorunlu stoklar ayrica korunur.
    IF @AllowUnresolvedRequiredSerial=0 AND EXISTS
    (
        SELECT 1
        FROM #SerialMismatch x
        INNER JOIN dbo.RII_STOCK st ON st.BranchCode=@WmsBranchCode
            AND st.ErpStockCode=x.StockCode AND st.IsDeleted=0
        OUTER APPLY
        (
            SELECT TOP (1) p.RequireSerial
            FROM dbo.RII_STOCK_TRACKING_POLICIES p
            WHERE p.BranchCode=@WmsBranchCode AND p.IsDeleted=0 AND p.IsActive=1
              AND p.EffectiveFromUtc<=SYSUTCDATETIME()
              AND (p.EffectiveToUtc IS NULL OR p.EffectiveToUtc>SYSUTCDATETIME())
              AND (p.Scope=N'BranchDefault'
                   OR (p.Scope=N'StockGroup' AND p.StockGroupCode=st.GroupCode)
                   OR (p.Scope=N'Stock' AND p.StockId=st.Id))
            ORDER BY CASE p.Scope WHEN N'Stock' THEN 3 WHEN N'StockGroup' THEN 2 ELSE 1 END DESC,
                     p.Priority DESC,p.Version DESC
        ) effective
        WHERE ISNULL(effective.RequireSerial,0)=1
    )
        THROW 51000, N'Problemli serilerden en az biri WMS politikasinda seri zorunlu. Netsis verisini duzeltin veya riski kabul ederek @AllowUnresolvedRequiredSerial=1 yapin.', 1;

    DELETE s
    FROM #SerialTotal s
    INNER JOIN #SerialMismatch x ON x.WarehouseCode=s.WarehouseCode
        AND x.StockCode=s.StockCode AND x.YapCode=s.YapCode;

    SELECT N'UYARI: Problemli seri kirilimlari aktarilmadi; TBLSTOKPH depo miktari serisiz acilis olarak kullanilacak.' AS Message;
END;

CREATE TABLE #Opening
(
    WarehouseCode int NOT NULL,
    StockCode nvarchar(100) NOT NULL,
    YapCode nvarchar(100) NOT NULL,
    SerialNo nvarchar(100) NULL,
    Quantity decimal(18,6) NOT NULL
);

INSERT #Opening SELECT WarehouseCode, StockCode, YapCode, SerialNo, Quantity FROM #SerialTotal;

INSERT #Opening (WarehouseCode, StockCode, YapCode, SerialNo, Quantity)
SELECT d.WarehouseCode, d.StockCode, d.YapCode, NULL,
       CONVERT(decimal(18,6), d.Quantity - ISNULL(s.Quantity, 0))
FROM #DepotTotal d
LEFT JOIN (
    SELECT WarehouseCode, StockCode, YapCode, SUM(Quantity) Quantity
    FROM #SerialTotal GROUP BY WarehouseCode, StockCode, YapCode
) s ON s.WarehouseCode=d.WarehouseCode AND s.StockCode=d.StockCode AND s.YapCode=d.YapCode
WHERE d.Quantity - ISNULL(s.Quantity, 0) > 0.000001;

-- WMS depo master kaydi ERP kod eslemesidir; sessizce uretilmez.
IF EXISTS (
    SELECT 1 FROM #Opening x
    LEFT JOIN dbo.RII_WAREHOUSE w ON w.BranchCode=@WmsBranchCode AND w.WarehouseCode=x.WarehouseCode AND w.IsDeleted=0
    WHERE w.Id IS NULL
)
BEGIN
    SELECT DISTINCT x.WarehouseCode AS MissingWmsWarehouseCode
    FROM #Opening x
    LEFT JOIN dbo.RII_WAREHOUSE w ON w.BranchCode=@WmsBranchCode
        AND w.WarehouseCode=x.WarehouseCode AND w.IsDeleted=0
    WHERE w.Id IS NULL
    ORDER BY x.WarehouseCode;

    THROW 51000, N'Netsis depolarindan en az birinin WMS RII_WAREHOUSE karsiligi bulunamadi.', 1;
END;

IF EXISTS (
    SELECT 1 FROM #Opening x
    LEFT JOIN dbo.RII_STOCK st ON st.BranchCode=@WmsBranchCode AND st.ErpStockCode=x.StockCode AND st.IsDeleted=0
    WHERE st.Id IS NULL
)
    THROW 51000, N'Netsis stok kodlarindan en az biri WMS RII_STOCK tablosunda bulunamadi.', 1;

/*
  Ilk acilis iki asamalidir: once tum bakiye deponun varsayilan kabul alanina,
  sonra 02 numarali script ile gercek raflara tasinir. Varsayilan alan yoksa
  YER1 idempotent bicimde hazirlanir. Mevcut ve gecerli varsayilan raf korunur.
*/
IF EXISTS
(
    SELECT 1
    FROM (SELECT DISTINCT WarehouseCode FROM #Opening) x
    INNER JOIN dbo.RII_WAREHOUSE w ON w.BranchCode=@WmsBranchCode
        AND w.WarehouseCode=x.WarehouseCode AND w.IsDeleted=0
    LEFT JOIN dbo.RII_LOCATION currentLocation ON currentLocation.Id=w.DefaultGoodsReceiptLocationId
        AND currentLocation.WarehouseId=w.Id AND currentLocation.BranchCode=w.BranchCode
        AND currentLocation.IsDeleted=0 AND currentLocation.IsActive=1
    WHERE currentLocation.Id IS NULL
)
BEGIN
    SELECT DISTINCT w.WarehouseCode, w.WarehouseName,
           @DefaultLocationCode AS LocationCode,
           CASE WHEN @Apply=1 AND @AutoCreateMissingDefaultLocation=1
                THEN N'OLUSTURULACAK/BAGLANACAK'
                ELSE N'EKSIK' END AS PlannedAction
    FROM (SELECT DISTINCT WarehouseCode FROM #Opening) x
    INNER JOIN dbo.RII_WAREHOUSE w ON w.BranchCode=@WmsBranchCode
        AND w.WarehouseCode=x.WarehouseCode AND w.IsDeleted=0
    LEFT JOIN dbo.RII_LOCATION currentLocation ON currentLocation.Id=w.DefaultGoodsReceiptLocationId
        AND currentLocation.WarehouseId=w.Id AND currentLocation.BranchCode=w.BranchCode
        AND currentLocation.IsDeleted=0 AND currentLocation.IsActive=1
    WHERE currentLocation.Id IS NULL
    ORDER BY w.WarehouseCode;

    IF @AutoCreateMissingDefaultLocation=0
        THROW 51000, N'Deponun varsayilan mal kabul rafi eksik. Otomatik olusturma kapali.', 1;

    IF @Apply=0
    BEGIN
        SELECT N'ONIZLEME: Yukaridaki depolara YER1 kabul alani @Apply=1 calistirmasinda otomatik hazirlanacak; henuz kayit yazilmadi.' AS Message;
        RETURN;
    END;

    BEGIN TRY
        BEGIN TRANSACTION;

        -- Ayni kodla daha once pasife alinmis raf varsa yeni kopya acmak yerine geri etkinlestir.
        UPDATE location
        SET location.IsActive=1,
            location.UpdatedDate=@AsOf
        FROM dbo.RII_LOCATION location WITH (UPDLOCK, HOLDLOCK)
        INNER JOIN dbo.RII_WAREHOUSE w ON w.Id=location.WarehouseId
        INNER JOIN (SELECT DISTINCT WarehouseCode FROM #Opening) x
            ON x.WarehouseCode=w.WarehouseCode
        LEFT JOIN dbo.RII_LOCATION currentLocation ON currentLocation.Id=w.DefaultGoodsReceiptLocationId
            AND currentLocation.WarehouseId=w.Id AND currentLocation.BranchCode=w.BranchCode
            AND currentLocation.IsDeleted=0 AND currentLocation.IsActive=1
        WHERE w.BranchCode=@WmsBranchCode AND w.IsDeleted=0
          AND currentLocation.Id IS NULL
          AND location.BranchCode=w.BranchCode AND location.IsDeleted=0
          AND UPPER(LTRIM(RTRIM(location.Code)))=UPPER(@DefaultLocationCode)
          AND location.IsActive=0;

        INSERT dbo.RII_LOCATION
        (
            WarehouseId, ParentLocationId, Code, Name, LocationType,
            BarcodeEntryMode, Barcode, ZoneCode, AisleNo, RackNo, LevelNo, BinNo,
            CapacityQuantity, CapacityWeight, CapacityVolume, CapacityUnit,
            AllowMixedStock, AllowMixedLot, AllowMixedStatus, AllowCycleCount,
            IsPickable, IsPutaway, IsQuarantine, IsActive, Description,
            BranchCode, CreatedDate, UpdatedDate, DeletedDate, IsDeleted,
            CreatedBy, UpdatedBy, DeletedBy
        )
        SELECT
            w.Id, NULL, @DefaultLocationCode, N'Varsayilan Mal Kabul Alani', N'Receiving',
            N'Auto', NULL, NULL, NULL, NULL, NULL, NULL,
            NULL, NULL, NULL, NULL,
            1, 1, 1, 0,
            0, 1, 0, 1, N'Netsis ilk acilis aktarimi tarafindan otomatik olusturuldu.',
            w.BranchCode, @AsOf, NULL, NULL, 0,
            NULL, NULL, NULL
        FROM dbo.RII_WAREHOUSE w WITH (UPDLOCK, HOLDLOCK)
        INNER JOIN (SELECT DISTINCT WarehouseCode FROM #Opening) x
            ON x.WarehouseCode=w.WarehouseCode
        LEFT JOIN dbo.RII_LOCATION currentLocation ON currentLocation.Id=w.DefaultGoodsReceiptLocationId
            AND currentLocation.WarehouseId=w.Id AND currentLocation.BranchCode=w.BranchCode
            AND currentLocation.IsDeleted=0 AND currentLocation.IsActive=1
        WHERE w.BranchCode=@WmsBranchCode AND w.IsDeleted=0
          AND currentLocation.Id IS NULL
          AND NOT EXISTS
          (
              SELECT 1 FROM dbo.RII_LOCATION location WITH (UPDLOCK, HOLDLOCK)
              WHERE location.WarehouseId=w.Id AND location.BranchCode=w.BranchCode
                AND location.IsDeleted=0
                AND UPPER(LTRIM(RTRIM(location.Code)))=UPPER(@DefaultLocationCode)
          );

        UPDATE w
        SET w.DefaultGoodsReceiptLocationId=defaultLocation.Id,
            w.UpdatedDate=@AsOf
        FROM dbo.RII_WAREHOUSE w WITH (UPDLOCK, HOLDLOCK)
        INNER JOIN (SELECT DISTINCT WarehouseCode FROM #Opening) x
            ON x.WarehouseCode=w.WarehouseCode
        OUTER APPLY
        (
            SELECT TOP (1) location.Id
            FROM dbo.RII_LOCATION location
            WHERE location.WarehouseId=w.Id AND location.BranchCode=w.BranchCode
              AND location.IsDeleted=0 AND location.IsActive=1
              AND UPPER(LTRIM(RTRIM(location.Code)))=UPPER(@DefaultLocationCode)
            ORDER BY location.Id
        ) defaultLocation
        LEFT JOIN dbo.RII_LOCATION currentLocation ON currentLocation.Id=w.DefaultGoodsReceiptLocationId
            AND currentLocation.WarehouseId=w.Id AND currentLocation.BranchCode=w.BranchCode
            AND currentLocation.IsDeleted=0 AND currentLocation.IsActive=1
        WHERE w.BranchCode=@WmsBranchCode AND w.IsDeleted=0
          AND currentLocation.Id IS NULL
          AND defaultLocation.Id IS NOT NULL;

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF XACT_STATE()<>0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH;
END;

IF EXISTS (
    SELECT 1 FROM #Opening x
    JOIN dbo.RII_WAREHOUSE w ON w.BranchCode=@WmsBranchCode AND w.WarehouseCode=x.WarehouseCode AND w.IsDeleted=0
    LEFT JOIN dbo.RII_LOCATION l ON l.Id=w.DefaultGoodsReceiptLocationId AND l.WarehouseId=w.Id
        AND l.BranchCode=@WmsBranchCode AND l.IsDeleted=0 AND l.IsActive=1
    WHERE l.Id IS NULL
)
    THROW 51000, N'Varsayilan mal kabul rafi aktif degil veya ilgili depoya ait degil.', 1;

IF EXISTS (
    SELECT 1 FROM #Opening x
    WHERE x.YapCode <> N'' AND NOT EXISTS (
        SELECT 1 FROM dbo.RII_YAP_CODE y
        INNER JOIN dbo.RII_STOCK st ON st.Id=y.StockId
        WHERE y.BranchCode=@WmsBranchCode AND y.IsDeleted=0
          AND y.ConfigurationCode=x.YapCode AND st.ErpStockCode=x.StockCode
    )
)
    THROW 51000, N'Netsis YAPKOD degerlerinden en az biri WMS konfigurasyon kodunda bulunamadi.', 1;

CREATE TABLE #Resolved
(
    RowNo int NOT NULL PRIMARY KEY,
    StockId bigint NOT NULL,
    YapCodeId bigint NULL,
    WarehouseId bigint NOT NULL,
    LocationId bigint NOT NULL,
    UnitCode nvarchar(20) NOT NULL,
    SerialNo nvarchar(100) NULL,
    Quantity decimal(18,6) NOT NULL
);

INSERT #Resolved
SELECT
    CONVERT(int, ROW_NUMBER() OVER (ORDER BY w.WarehouseCode, st.ErpStockCode, x.YapCode, ISNULL(x.SerialNo,N''))),
    st.Id, y.Id, w.Id, w.DefaultGoodsReceiptLocationId, st.BaseUnitCode, x.SerialNo, x.Quantity
FROM #Opening x
INNER JOIN dbo.RII_WAREHOUSE w ON w.BranchCode=@WmsBranchCode AND w.WarehouseCode=x.WarehouseCode AND w.IsDeleted=0
INNER JOIN dbo.RII_STOCK st ON st.BranchCode=@WmsBranchCode AND st.ErpStockCode=x.StockCode AND st.IsDeleted=0
OUTER APPLY (
    SELECT TOP (1) yc.Id FROM dbo.RII_YAP_CODE yc
    WHERE x.YapCode<>N'' AND yc.BranchCode=@WmsBranchCode AND yc.StockId=st.Id
      AND yc.ConfigurationCode=x.YapCode AND yc.IsDeleted=0
    ORDER BY yc.Id
) y;

-- Sube genelindeki ilgisiz hareketler aktarimi engellemez. Ancak Netsis'ten
-- acilisi yapilacak ayni depo/stok/YAP/birim boyutunda mevcut net bakiye varsa
-- tam bakiye tekrar eklenerek mukerrerlik olusmamasi icin durulur.
IF @AllowExistingMovements = 0 AND EXISTS
(
    SELECT 1
    FROM
    (
        SELECT m.WarehouseId, m.StockId, m.YapCodeId, m.UnitCode,
               SUM(m.QuantityDelta) AS CurrentQuantity
        FROM dbo.RII_STOCK_MOVEMENT m
        INNER JOIN dbo.RII_STOCK_MOVEMENT_OPERATION o
            ON o.Id=m.OperationId AND o.IsDeleted=0 AND o.Status=N'Posted'
        WHERE m.BranchCode=@WmsBranchCode AND m.IsDeleted=0
        GROUP BY m.WarehouseId, m.StockId, m.YapCodeId, m.UnitCode
        HAVING ABS(SUM(m.QuantityDelta))>0.000001
    ) currentBalance
    INNER JOIN
    (
        SELECT DISTINCT WarehouseId, StockId, YapCodeId, UnitCode
        FROM #Resolved
    ) incoming
      ON incoming.WarehouseId=currentBalance.WarehouseId
     AND incoming.StockId=currentBalance.StockId
     AND ISNULL(incoming.YapCodeId,0)=ISNULL(currentBalance.YapCodeId,0)
     AND incoming.UnitCode=currentBalance.UnitCode
)
BEGIN
    SELECT w.WarehouseCode, st.ErpStockCode, y.ConfigurationCode,
           currentBalance.UnitCode, currentBalance.CurrentQuantity
    FROM
    (
        SELECT m.WarehouseId, m.StockId, m.YapCodeId, m.UnitCode,
               SUM(m.QuantityDelta) AS CurrentQuantity
        FROM dbo.RII_STOCK_MOVEMENT m
        INNER JOIN dbo.RII_STOCK_MOVEMENT_OPERATION o
            ON o.Id=m.OperationId AND o.IsDeleted=0 AND o.Status=N'Posted'
        WHERE m.BranchCode=@WmsBranchCode AND m.IsDeleted=0
        GROUP BY m.WarehouseId, m.StockId, m.YapCodeId, m.UnitCode
        HAVING ABS(SUM(m.QuantityDelta))>0.000001
    ) currentBalance
    INNER JOIN (SELECT DISTINCT WarehouseId,StockId,YapCodeId,UnitCode FROM #Resolved) incoming
      ON incoming.WarehouseId=currentBalance.WarehouseId
     AND incoming.StockId=currentBalance.StockId
     AND ISNULL(incoming.YapCodeId,0)=ISNULL(currentBalance.YapCodeId,0)
     AND incoming.UnitCode=currentBalance.UnitCode
    INNER JOIN dbo.RII_WAREHOUSE w ON w.Id=currentBalance.WarehouseId
    INNER JOIN dbo.RII_STOCK st ON st.Id=currentBalance.StockId
    LEFT JOIN dbo.RII_YAP_CODE y ON y.Id=currentBalance.YapCodeId
    ORDER BY w.WarehouseCode,st.ErpStockCode,y.ConfigurationCode;

    THROW 51000, N'Netsis acilisi yapilacak ayni depo/stok boyutunda WMS bakiyesi var. Listelenen cakismalar cozulmeden tam bakiye eklenemez.', 1;
END;

SELECT COUNT_BIG(*) AS DimensionCount, SUM(r.Quantity) AS TotalQuantity,
       COUNT_BIG(DISTINCT r.StockId) AS StockCount, COUNT_BIG(DISTINCT r.WarehouseId) AS WarehouseCount,
       SUM(CASE WHEN r.SerialNo IS NULL THEN 0 ELSE 1 END) AS SerialDimensionCount
FROM #Resolved r;

SELECT TOP (200) w.WarehouseCode, l.Code AS DefaultLocationCode, st.ErpStockCode, y.ConfigurationCode,
       r.UnitCode, r.SerialNo, r.Quantity
FROM #Resolved r
INNER JOIN dbo.RII_WAREHOUSE w ON w.Id=r.WarehouseId
INNER JOIN dbo.RII_LOCATION l ON l.Id=r.LocationId
INNER JOIN dbo.RII_STOCK st ON st.Id=r.StockId
LEFT JOIN dbo.RII_YAP_CODE y ON y.Id=r.YapCodeId
ORDER BY w.WarehouseCode, st.ErpStockCode, y.ConfigurationCode, r.SerialNo;

IF @Apply = 0
BEGIN
    SELECT N'ONIZLEME: Kayit yazilmadi. Sonuclari kontrol edip @Apply=1 ile yeniden calistirin.' AS Message;
    RETURN;
END;

BEGIN TRANSACTION;

INSERT dbo.RII_STOCK_MOVEMENT_OPERATION
(
    BranchCode, CreatedDate, IsDeleted, OperationCode, IdempotencyKey, RequestHash,
    OperationType, Status, ReferenceType, ReferenceNo, OccurredAt, Reason, Description
)
VALUES
(
    @WmsBranchCode, @AsOf, 0, NEWID(), @IdempotencyKey,
    CONVERT(varchar(64), HASHBYTES('SHA2_256', CONVERT(varchar(8000), @IdempotencyKey)), 2),
    N'AdjustmentIncrease', N'Posted', N'NetsisOpeningSql', N'DEPOT-SERIAL', @AsOf,
    N'Netsis depo ve seri acilis bakiyesi', N'TBLSTOKPH ve TBLSERITRA kaynakli idempotent ilk bakiye'
);

DECLARE @OperationId bigint = SCOPE_IDENTITY();

INSERT dbo.RII_STOCK_MOVEMENT
(
    BranchCode, CreatedDate, IsDeleted, OperationId, [LineNo], StockId, YapCodeId,
    WarehouseId, LocationId, QuantityDelta, UnitCode, LotNo, SerialNo, StockStatus, OccurredAt
)
SELECT @WmsBranchCode, @AsOf, 0, @OperationId, RowNo, StockId, YapCodeId,
       WarehouseId, LocationId, Quantity, UnitCode, NULL, SerialNo, N'Available', @AsOf
FROM #Resolved;

COMMIT TRANSACTION;

SELECT @OperationId AS OperationId, COUNT(*) AS InsertedMovementCount, SUM(Quantity) AS InsertedQuantity
FROM #Resolved;

-- Dogrudan SQL, uygulamanin projection servisini calistirmaz.
-- 02 numarali dagitimdan sonra yetkili oturumla bir kez POST /api/stock-balances/rebuild cagrilmalidir.
