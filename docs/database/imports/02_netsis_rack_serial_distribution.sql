/*
  NETSIS -> WMS V2 / RAF + SERI DAGITIMI

  ON KOSUL: 01_netsis_depot_serial_opening.sql basariyla calismis olmali.

  Bu sorgu yeni stok uretmez. Ilk sorgunun deponun varsayilan rafina yazdigi
  bakiyeyi, Netsis dinamik depo bakiyesine gore gercek raflara TRANSFER eder.
  Boylece depo ve raf sorgulari birlikte calistirildiginda miktar iki kez sayilmaz.

  Netsis surumlerinde dinamik depo kolon adlari degisebildigi icin yaygin
  DEPOKODU/DEPO_KODU, HUCREKODU/HUCRE_KODU, STOKKODU/STOK_KODU ve
  NETBAKIYE/BAKIYE adlari calisma aninda kesfedilir.
*/

SET NOCOUNT ON;
SET XACT_ABORT ON;

-- SSMS ayni oturumda RETURN/THROW sonrasinda local temp tablolari yasatir.
-- Scriptin ayni sorgu penceresinde guvenle tekrar calistirilabilmesi icin
-- yalnizca bu scripte ait gecici tablolar temizlenir.
IF OBJECT_ID(N'tempdb..#Transfer', N'U') IS NOT NULL DROP TABLE #Transfer;
IF OBJECT_ID(N'tempdb..#Desired', N'U') IS NOT NULL DROP TABLE #Desired;
IF OBJECT_ID(N'tempdb..#RackSerialMismatch', N'U') IS NOT NULL DROP TABLE #RackSerialMismatch;
IF OBJECT_ID(N'tempdb..#RackSerial', N'U') IS NOT NULL DROP TABLE #RackSerial;
IF OBJECT_ID(N'tempdb..#RackTotal', N'U') IS NOT NULL DROP TABLE #RackTotal;

DECLARE @WmsBranchCode nvarchar(10) = N'0';
DECLARE @NetsisBranchCode smallint = 0;
DECLARE @AsOf datetime2(7) = SYSUTCDATETIME();
DECLARE @Apply bit = 0; -- once 0 ile onizleyin; kontrol sonrasi 1 yapin
DECLARE @SerialMismatchMode nvarchar(20) = N'STRICT'; -- 01 ile ayni deger: STRICT | DEPOT_TRUTH
DECLARE @IdempotencyKey nvarchar(100) = CONCAT(N'NETSIS-OPENING-RACK-SERIAL-V1-', @WmsBranchCode);
DECLARE @OpeningKey nvarchar(100) = CONCAT(N'NETSIS-OPENING-DEPOT-SERIAL-V1-', @WmsBranchCode);

IF DB_ID(N'TESTV3RII') IS NULL
    THROW 51000, N'TESTV3RII bulunamadi. Kaynak ERP veritabani adini duzeltin.', 1;

IF @SerialMismatchMode NOT IN (N'STRICT',N'DEPOT_TRUTH')
    THROW 51000, N'@SerialMismatchMode yalnizca STRICT veya DEPOT_TRUTH olabilir.', 1;

IF OBJECT_ID(N'TESTV3RII.dbo.TBLDEPDURUM', N'U') IS NULL OR OBJECT_ID(N'TESTV3RII.dbo.TBLDEPMAS', N'U') IS NULL
    THROW 51000, N'Netsis dinamik depo tablolari TBLDEPDURUM/TBLDEPMAS bulunamadi.', 1;

IF NOT EXISTS (
    SELECT 1 FROM dbo.RII_STOCK_MOVEMENT_OPERATION
    WHERE IdempotencyKey=@OpeningKey AND Status=N'Posted' AND IsDeleted=0
)
    THROW 51000, N'Once 01_netsis_depot_serial_opening.sql calistirilmalidir.', 1;

IF EXISTS (
    SELECT 1 FROM dbo.RII_STOCK_MOVEMENT_OPERATION
    WHERE IdempotencyKey=@IdempotencyKey AND IsDeleted=0
)
BEGIN
    SELECT N'Bu sube icin raf/seri dagitimi daha once uygulanmis; yeni kayit eklenmedi.' AS Message;
    RETURN;
END;

DECLARE @MasCell sysname = CASE
    WHEN COL_LENGTH(N'TESTV3RII.dbo.TBLDEPMAS', N'HUCREKODU') IS NOT NULL THEN N'HUCREKODU'
    WHEN COL_LENGTH(N'TESTV3RII.dbo.TBLDEPMAS', N'HUCRE_KODU') IS NOT NULL THEN N'HUCRE_KODU' END;
DECLARE @MasWarehouse sysname = CASE
    WHEN COL_LENGTH(N'TESTV3RII.dbo.TBLDEPMAS', N'DEPOKODU') IS NOT NULL THEN N'DEPOKODU'
    WHEN COL_LENGTH(N'TESTV3RII.dbo.TBLDEPMAS', N'DEPO_KODU') IS NOT NULL THEN N'DEPO_KODU'
    WHEN COL_LENGTH(N'TESTV3RII.dbo.TBLDEPMAS', N'DEPOKOD') IS NOT NULL THEN N'DEPOKOD' END;
DECLARE @DurumCell sysname = CASE
    WHEN COL_LENGTH(N'TESTV3RII.dbo.TBLDEPDURUM', N'HUCREKODU') IS NOT NULL THEN N'HUCREKODU'
    WHEN COL_LENGTH(N'TESTV3RII.dbo.TBLDEPDURUM', N'HUCRE_KODU') IS NOT NULL THEN N'HUCRE_KODU' END;
DECLARE @DurumStock sysname = CASE
    WHEN COL_LENGTH(N'TESTV3RII.dbo.TBLDEPDURUM', N'STOKKODU') IS NOT NULL THEN N'STOKKODU'
    WHEN COL_LENGTH(N'TESTV3RII.dbo.TBLDEPDURUM', N'STOK_KODU') IS NOT NULL THEN N'STOK_KODU' END;
DECLARE @DurumBalance sysname = CASE
    WHEN COL_LENGTH(N'TESTV3RII.dbo.TBLDEPDURUM', N'NETBAKIYE') IS NOT NULL THEN N'NETBAKIYE'
    WHEN COL_LENGTH(N'TESTV3RII.dbo.TBLDEPDURUM', N'BAKIYE') IS NOT NULL THEN N'BAKIYE' END;
DECLARE @DurumYap sysname = CASE
    WHEN COL_LENGTH(N'TESTV3RII.dbo.TBLDEPDURUM', N'YAPKOD') IS NOT NULL THEN N'YAPKOD'
    WHEN COL_LENGTH(N'TESTV3RII.dbo.TBLDEPDURUM', N'YAP_KOD') IS NOT NULL THEN N'YAP_KOD' END;
DECLARE @DurumWarehouse sysname = CASE
    WHEN COL_LENGTH(N'TESTV3RII.dbo.TBLDEPDURUM', N'DEPOKODU') IS NOT NULL THEN N'DEPOKODU'
    WHEN COL_LENGTH(N'TESTV3RII.dbo.TBLDEPDURUM', N'DEPO_KODU') IS NOT NULL THEN N'DEPO_KODU'
    WHEN COL_LENGTH(N'TESTV3RII.dbo.TBLDEPDURUM', N'DEPOKOD') IS NOT NULL THEN N'DEPOKOD' END;
DECLARE @MasBranch sysname = CASE
    WHEN COL_LENGTH(N'TESTV3RII.dbo.TBLDEPMAS', N'SUBE_KODU') IS NOT NULL THEN N'SUBE_KODU'
    WHEN COL_LENGTH(N'TESTV3RII.dbo.TBLDEPMAS', N'SUBEKODU') IS NOT NULL THEN N'SUBEKODU' END;
DECLARE @DurumBranch sysname = CASE
    WHEN COL_LENGTH(N'TESTV3RII.dbo.TBLDEPDURUM', N'SUBE_KODU') IS NOT NULL THEN N'SUBE_KODU'
    WHEN COL_LENGTH(N'TESTV3RII.dbo.TBLDEPDURUM', N'SUBEKODU') IS NOT NULL THEN N'SUBEKODU' END;
DECLARE @StockPhYap sysname = CASE
    WHEN COL_LENGTH(N'TESTV3RII.dbo.TBLSTOKPH', N'YAPKOD') IS NOT NULL THEN N'YAPKOD'
    WHEN COL_LENGTH(N'TESTV3RII.dbo.TBLSTOKPH', N'YAP_KOD') IS NOT NULL THEN N'YAP_KOD' END;
DECLARE @UseYapDimension bit = CASE
    WHEN @DurumYap IS NOT NULL AND @StockPhYap IS NOT NULL
     AND COL_LENGTH(N'TESTV3RII.dbo.TBLSTHAR',N'YAPKOD') IS NOT NULL THEN 1 ELSE 0 END;

IF @MasCell IS NULL OR @MasWarehouse IS NULL OR @DurumCell IS NULL OR @DurumStock IS NULL OR @DurumBalance IS NULL
    THROW 51000, N'Netsis dinamik depo kolonlari taninamadi. TBLDEPMAS/TBLDEPDURUM semasini kontrol edin.', 1;

CREATE TABLE #RackTotal
(
    WarehouseCode int NOT NULL,
    LocationCode nvarchar(100) NOT NULL,
    StockCode nvarchar(100) NOT NULL,
    YapCode nvarchar(100) NOT NULL,
    Quantity decimal(18,6) NOT NULL,
    PRIMARY KEY (WarehouseCode, LocationCode, StockCode, YapCode)
);

DECLARE @sql nvarchar(max) = N'
INSERT #RackTotal (WarehouseCode, LocationCode, StockCode, YapCode, Quantity)
SELECT CONVERT(int,m.' + QUOTENAME(@MasWarehouse) + N'),
       LTRIM(RTRIM(d.' + QUOTENAME(@DurumCell) + N')),
       LTRIM(RTRIM(d.' + QUOTENAME(@DurumStock) + N')),
       ' + CASE WHEN @UseYapDimension = 0 THEN N'N'''''
              ELSE N'LTRIM(RTRIM(ISNULL(d.' + QUOTENAME(@DurumYap) + N',N'''')))' END + N',
       CONVERT(decimal(18,6),SUM(d.' + QUOTENAME(@DurumBalance) + N'))
FROM TESTV3RII.dbo.TBLDEPDURUM d
INNER JOIN TESTV3RII.dbo.TBLDEPMAS m
  ON m.' + QUOTENAME(@MasCell) + N'=d.' + QUOTENAME(@DurumCell)
    + CASE WHEN @DurumWarehouse IS NULL THEN N''
           ELSE N' AND m.' + QUOTENAME(@MasWarehouse) + N'=d.' + QUOTENAME(@DurumWarehouse) END
    + CASE WHEN @MasBranch IS NULL OR @DurumBranch IS NULL THEN N''
           ELSE N' AND m.' + QUOTENAME(@MasBranch) + N'=d.' + QUOTENAME(@DurumBranch) END + N'
WHERE 1=1'
    + CASE WHEN @MasBranch IS NOT NULL THEN N' AND m.' + QUOTENAME(@MasBranch) + N'=@pBranch'
           WHEN @DurumBranch IS NOT NULL THEN N' AND d.' + QUOTENAME(@DurumBranch) + N'=@pBranch'
           ELSE N'' END + N'
GROUP BY m.' + QUOTENAME(@MasWarehouse) + N',d.' + QUOTENAME(@DurumCell) + N',d.' + QUOTENAME(@DurumStock) + N''
    + CASE WHEN @UseYapDimension = 0 THEN N'' ELSE N',d.' + QUOTENAME(@DurumYap) END + N'
HAVING SUM(d.' + QUOTENAME(@DurumBalance) + N')>0;';

EXEC sys.sp_executesql @sql, N'@pBranch smallint', @pBranch=@NetsisBranchCode;

CREATE TABLE #RackSerial
(
    WarehouseCode int NOT NULL,
    LocationCode nvarchar(100) NOT NULL,
    StockCode nvarchar(100) NOT NULL,
    YapCode nvarchar(100) NOT NULL,
    SerialNo nvarchar(100) NOT NULL,
    Quantity decimal(18,6) NOT NULL,
    PRIMARY KEY (WarehouseCode, LocationCode, StockCode, YapCode, SerialNo)
);

IF COL_LENGTH(N'TESTV3RII.dbo.TBLSERITRA', N'YEDEK1') IS NULL
    THROW 51000, N'TBLSERITRA.YEDEK1 bulunamadi; seri-raf eslesmesi bu Netsis surumu icin uyarlanmalidir.', 1;

SET @sql=N'
INSERT #RackSerial (WarehouseCode, LocationCode, StockCode, YapCode, SerialNo, Quantity)
SELECT CONVERT(int,s.DEPOKOD),LTRIM(RTRIM(s.YEDEK1)),LTRIM(RTRIM(s.STOK_KODU)),
       ' + CASE WHEN @UseYapDimension=1 THEN N'LTRIM(RTRIM(ISNULL(h.YAPKOD,N'''')))' ELSE N'N''''' END + N',
       LTRIM(RTRIM(s.SERI_NO)),
       CONVERT(decimal(18,6),SUM(CASE WHEN s.GCKOD=''G'' THEN s.MIKTAR ELSE -s.MIKTAR END))
FROM TESTV3RII.dbo.TBLSERITRA s
INNER JOIN TESTV3RII.dbo.TBLSTHAR h ON h.INCKEYNO=s.STRA_INC
WHERE h.SUBE_KODU=@pBranch
  AND NULLIF(LTRIM(RTRIM(s.YEDEK1)),N'''') IS NOT NULL
  AND NULLIF(LTRIM(RTRIM(s.SERI_NO)),N'''') IS NOT NULL
GROUP BY s.DEPOKOD,s.YEDEK1,s.STOK_KODU,'
    + CASE WHEN @UseYapDimension=1 THEN N'ISNULL(h.YAPKOD,N''''),' ELSE N'' END + N's.SERI_NO
HAVING SUM(CASE WHEN s.GCKOD=''G'' THEN s.MIKTAR ELSE -s.MIKTAR END)>0.000001;';

EXEC sys.sp_executesql @sql,N'@pBranch smallint',@pBranch=@NetsisBranchCode;

-- 01 DEPOT_TRUTH kullanildiginda problemli depo/stok kirilimlari serisiz
-- acilmistir. Raf kaynaginda gorunen fakat acilis ledger'inda bulunmayan bir
-- seri yeniden uretilmez; ilgili raf miktari asagida serisiz dagitilir.
IF @SerialMismatchMode=N'DEPOT_TRUTH'
BEGIN
    DELETE rs
    FROM #RackSerial rs
    INNER JOIN dbo.RII_WAREHOUSE w ON w.BranchCode=@WmsBranchCode
        AND w.WarehouseCode=rs.WarehouseCode AND w.IsDeleted=0
    INNER JOIN dbo.RII_STOCK st ON st.BranchCode=@WmsBranchCode
        AND st.ErpStockCode=rs.StockCode AND st.IsDeleted=0
    OUTER APPLY
    (
        SELECT TOP (1) y.Id
        FROM dbo.RII_YAP_CODE y
        WHERE rs.YapCode<>N'' AND y.BranchCode=@WmsBranchCode
          AND y.StockId=st.Id AND y.ConfigurationCode=rs.YapCode AND y.IsDeleted=0
        ORDER BY y.Id
    ) yap
    WHERE NOT EXISTS
    (
        SELECT 1
        FROM dbo.RII_STOCK_MOVEMENT m
        INNER JOIN dbo.RII_STOCK_MOVEMENT_OPERATION o ON o.Id=m.OperationId
        WHERE o.IdempotencyKey=@OpeningKey AND o.Status=N'Posted' AND o.IsDeleted=0
          AND m.IsDeleted=0 AND m.BranchCode=@WmsBranchCode
          AND m.WarehouseId=w.Id AND m.LocationId=w.DefaultGoodsReceiptLocationId
          AND m.StockId=st.Id AND ISNULL(m.YapCodeId,0)=ISNULL(yap.Id,0)
          AND ISNULL(m.SerialNo,N'')=rs.SerialNo
        GROUP BY m.StockId
        HAVING SUM(m.QuantityDelta)>0.000001
    );

    IF @@ROWCOUNT>0
        SELECT N'UYARI: Acilis ledgerinda bulunmayan Netsis raf serileri atlandi; ilgili raf miktarlari serisiz dagitilacak.' AS Message;
END;

CREATE TABLE #RackSerialMismatch
(
    WarehouseCode int NOT NULL,LocationCode nvarchar(100) NOT NULL,
    StockCode nvarchar(100) NOT NULL,YapCode nvarchar(100) NOT NULL,
    RackQuantity decimal(18,6) NOT NULL,SerialQuantity decimal(18,6) NOT NULL,
    PRIMARY KEY(WarehouseCode,LocationCode,StockCode,YapCode)
);

INSERT #RackSerialMismatch
SELECT s.WarehouseCode,s.LocationCode,s.StockCode,s.YapCode,
       ISNULL(r.Quantity,0),s.Quantity
FROM (
    SELECT WarehouseCode,LocationCode,StockCode,YapCode,SUM(Quantity) Quantity
    FROM #RackSerial GROUP BY WarehouseCode,LocationCode,StockCode,YapCode
) s
LEFT JOIN #RackTotal r ON r.WarehouseCode=s.WarehouseCode AND r.LocationCode=s.LocationCode
    AND r.StockCode=s.StockCode AND r.YapCode=s.YapCode
WHERE r.WarehouseCode IS NULL OR s.Quantity>r.Quantity+0.000001;

IF EXISTS(SELECT 1 FROM #RackSerialMismatch)
BEGIN
    SELECT N'RACK_SERIAL_MISMATCH' AS DiagnosticType,WarehouseCode,LocationCode,
           StockCode,YapCode,RackQuantity,SerialQuantity,SerialQuantity-RackQuantity AS ExcessQuantity
    FROM #RackSerialMismatch
    ORDER BY SerialQuantity-RackQuantity DESC,WarehouseCode,LocationCode,StockCode;

    IF @SerialMismatchMode=N'STRICT' AND @Apply=0
    BEGIN
        SELECT N'ONIZLEME BLOKAJI: Raf ve seri bakiyesi uyusmuyor. Yukaridaki RACK_SERIAL_MISMATCH satirlari duzeltilmeden STRICT modda veri yazilamaz.' AS Message;
        RETURN;
    END;

    IF @SerialMismatchMode=N'STRICT'
        THROW 51000, N'Netsis raf seri bakiyesi raf toplam bakiyesinden buyuk. Veri yazilmadi.', 1;

    -- 01 DEPOT_TRUTH problemli depo/stok kirilimini tamamen serisiz actigi icin
    -- 02 de ayni kirilimin butun raf seri satirlarini serisiz dagitmalidir.
    DELETE rs
    FROM #RackSerial rs
    WHERE EXISTS
    (
        SELECT 1 FROM #RackSerialMismatch x
        WHERE x.WarehouseCode=rs.WarehouseCode AND x.StockCode=rs.StockCode
          AND x.YapCode=rs.YapCode
    );

    SELECT N'UYARI: Problemli depo/stok kirilimlarinda raf dagitimi serisiz yapilacak.' AS Message;
END;

CREATE TABLE #Desired
(
    WarehouseCode int NOT NULL, LocationCode nvarchar(100) NOT NULL,
    StockCode nvarchar(100) NOT NULL, YapCode nvarchar(100) NOT NULL,
    SerialNo nvarchar(100) NULL, Quantity decimal(18,6) NOT NULL
);

INSERT #Desired SELECT WarehouseCode,LocationCode,StockCode,YapCode,SerialNo,Quantity FROM #RackSerial;
INSERT #Desired
SELECT r.WarehouseCode,r.LocationCode,r.StockCode,r.YapCode,NULL,
       CONVERT(decimal(18,6),r.Quantity-ISNULL(s.Quantity,0))
FROM #RackTotal r
LEFT JOIN (
    SELECT WarehouseCode,LocationCode,StockCode,YapCode,SUM(Quantity) Quantity
    FROM #RackSerial GROUP BY WarehouseCode,LocationCode,StockCode,YapCode
) s ON s.WarehouseCode=r.WarehouseCode AND s.LocationCode=r.LocationCode
   AND s.StockCode=r.StockCode AND s.YapCode=r.YapCode
WHERE r.Quantity-ISNULL(s.Quantity,0)>0.000001;

-- Tum kaynak kodlar WMS'te birebir bulunmali; aksi halde aktarim tamamen durur.
IF EXISTS (
    SELECT 1 FROM #Desired x
    LEFT JOIN dbo.RII_WAREHOUSE w ON w.BranchCode=@WmsBranchCode AND w.WarehouseCode=x.WarehouseCode AND w.IsDeleted=0
    LEFT JOIN dbo.RII_LOCATION l ON l.BranchCode=@WmsBranchCode AND l.WarehouseId=w.Id
        AND l.Code=x.LocationCode AND l.IsDeleted=0 AND l.IsActive=1
    LEFT JOIN dbo.RII_STOCK st ON st.BranchCode=@WmsBranchCode AND st.ErpStockCode=x.StockCode AND st.IsDeleted=0
    WHERE w.Id IS NULL OR l.Id IS NULL OR st.Id IS NULL
)
    THROW 51000, N'Netsis depo/raf/stok kodlarindan en az biri WMS master verisinde bulunamadi.', 1;

IF EXISTS (
    SELECT 1 FROM #Desired x WHERE x.YapCode<>N'' AND NOT EXISTS (
        SELECT 1 FROM dbo.RII_STOCK st
        INNER JOIN dbo.RII_YAP_CODE y ON y.StockId=st.Id AND y.BranchCode=@WmsBranchCode AND y.IsDeleted=0
        WHERE st.BranchCode=@WmsBranchCode AND st.IsDeleted=0 AND st.ErpStockCode=x.StockCode
          AND y.ConfigurationCode=x.YapCode
    )
)
    THROW 51000, N'Raf bakiyesindeki bir YAPKOD WMS konfigurasyon kodunda bulunamadi.', 1;

CREATE TABLE #Transfer
(
    RowNo int NOT NULL PRIMARY KEY, StockId bigint NOT NULL, YapCodeId bigint NULL,
    WarehouseId bigint NOT NULL, SourceLocationId bigint NOT NULL, TargetLocationId bigint NOT NULL,
    UnitCode nvarchar(20) NOT NULL, SerialNo nvarchar(100) NULL, Quantity decimal(18,6) NOT NULL
);

INSERT #Transfer
SELECT CONVERT(int,ROW_NUMBER() OVER(ORDER BY w.WarehouseCode,l.Code,st.ErpStockCode,x.YapCode,ISNULL(x.SerialNo,N''))),
       st.Id,y.Id,w.Id,w.DefaultGoodsReceiptLocationId,l.Id,st.BaseUnitCode,x.SerialNo,x.Quantity
FROM #Desired x
INNER JOIN dbo.RII_WAREHOUSE w ON w.BranchCode=@WmsBranchCode AND w.WarehouseCode=x.WarehouseCode AND w.IsDeleted=0
INNER JOIN dbo.RII_LOCATION l ON l.BranchCode=@WmsBranchCode AND l.WarehouseId=w.Id
    AND l.Code=x.LocationCode AND l.IsDeleted=0 AND l.IsActive=1
INNER JOIN dbo.RII_STOCK st ON st.BranchCode=@WmsBranchCode AND st.ErpStockCode=x.StockCode AND st.IsDeleted=0
OUTER APPLY (
    SELECT TOP(1) yc.Id FROM dbo.RII_YAP_CODE yc
    WHERE x.YapCode<>N'' AND yc.BranchCode=@WmsBranchCode AND yc.StockId=st.Id
      AND yc.ConfigurationCode=x.YapCode AND yc.IsDeleted=0 ORDER BY yc.Id
) y
WHERE l.Id<>w.DefaultGoodsReceiptLocationId;

SELECT COUNT_BIG(*) AS DimensionCount,SUM(t.Quantity) AS TotalQuantity,
       COUNT_BIG(DISTINCT t.StockId) AS StockCount,COUNT_BIG(DISTINCT t.TargetLocationId) AS TargetLocationCount,
       SUM(CASE WHEN t.SerialNo IS NULL THEN 0 ELSE 1 END) AS SerialDimensionCount
FROM #Transfer t;

SELECT TOP (200) w.WarehouseCode, sl.Code AS SourceLocationCode, tl.Code AS TargetLocationCode,
       st.ErpStockCode, y.ConfigurationCode, t.UnitCode, t.SerialNo, t.Quantity
FROM #Transfer t
INNER JOIN dbo.RII_WAREHOUSE w ON w.Id=t.WarehouseId
INNER JOIN dbo.RII_LOCATION sl ON sl.Id=t.SourceLocationId
INNER JOIN dbo.RII_LOCATION tl ON tl.Id=t.TargetLocationId
INNER JOIN dbo.RII_STOCK st ON st.Id=t.StockId
LEFT JOIN dbo.RII_YAP_CODE y ON y.Id=t.YapCodeId
ORDER BY w.WarehouseCode, tl.Code, st.ErpStockCode, y.ConfigurationCode, t.SerialNo;

-- Varsayilan raftaki seri/serisiz miktar dagitimi karsilamiyorsa negatif stok olusturma.
IF EXISTS (
    SELECT 1
    FROM (
        SELECT StockId,YapCodeId,WarehouseId,SourceLocationId,UnitCode,ISNULL(SerialNo,N'') SerialNo,SUM(Quantity) Need
        FROM #Transfer GROUP BY StockId,YapCodeId,WarehouseId,SourceLocationId,UnitCode,ISNULL(SerialNo,N'')
    ) n
    OUTER APPLY (
        SELECT SUM(m.QuantityDelta) Have
        FROM dbo.RII_STOCK_MOVEMENT m
        INNER JOIN dbo.RII_STOCK_MOVEMENT_OPERATION o ON o.Id=m.OperationId AND o.Status=N'Posted' AND o.IsDeleted=0
        WHERE m.IsDeleted=0 AND m.BranchCode=@WmsBranchCode
          AND m.StockId=n.StockId AND ISNULL(m.YapCodeId,0)=ISNULL(n.YapCodeId,0)
          AND m.WarehouseId=n.WarehouseId AND m.LocationId=n.SourceLocationId
          AND m.UnitCode=n.UnitCode AND ISNULL(m.SerialNo,N'')=n.SerialNo
          AND ISNULL(m.LotNo,N'')=N'' AND m.StockStatus=N'Available'
    ) b
    WHERE ISNULL(b.Have,0)+0.000001<n.Need
)
    THROW 51000, N'Raf dagitimi varsayilan raftaki acilis bakiyesini asiyor. Netsis depo/raf/seri tutarliligini kontrol edin.', 1;

IF @Apply = 0
BEGIN
    SELECT N'ONIZLEME: Transfer yazilmadi. Sonuclari kontrol edip @Apply=1 ile yeniden calistirin.' AS Message;
    RETURN;
END;

BEGIN TRANSACTION;

INSERT dbo.RII_STOCK_MOVEMENT_OPERATION
(
    BranchCode,CreatedDate,IsDeleted,OperationCode,IdempotencyKey,RequestHash,
    OperationType,Status,ReferenceType,ReferenceNo,OccurredAt,Reason,Description
)
VALUES
(
    @WmsBranchCode,@AsOf,0,NEWID(),@IdempotencyKey,
    CONVERT(varchar(64),HASHBYTES('SHA2_256',CONVERT(varchar(8000),@IdempotencyKey)),2),
    N'Transfer',N'Posted',N'NetsisOpeningSql',N'RACK-SERIAL',@AsOf,
    N'Netsis dinamik depo raf dagitimi',N'Varsayilan mal kabul rafindan gercek raflara miktar ve seri transferi'
);

DECLARE @OperationId bigint=SCOPE_IDENTITY();

INSERT dbo.RII_STOCK_MOVEMENT
(
    BranchCode,CreatedDate,IsDeleted,OperationId,[LineNo],StockId,YapCodeId,WarehouseId,
    LocationId,QuantityDelta,UnitCode,LotNo,SerialNo,StockStatus,OccurredAt
)
SELECT @WmsBranchCode,@AsOf,0,@OperationId,CONVERT(int,(RowNo-1)*2+1),StockId,YapCodeId,WarehouseId,
       SourceLocationId,-Quantity,UnitCode,NULL,SerialNo,N'Available',@AsOf
FROM #Transfer
UNION ALL
SELECT @WmsBranchCode,@AsOf,0,@OperationId,CONVERT(int,(RowNo-1)*2+2),StockId,YapCodeId,WarehouseId,
       TargetLocationId,Quantity,UnitCode,NULL,SerialNo,N'Available',@AsOf
FROM #Transfer;

COMMIT TRANSACTION;

SELECT @OperationId AS OperationId,COUNT(*) AS DistributedDimensionCount,SUM(Quantity) AS DistributedQuantity
FROM #Transfer;

-- ZORUNLU SON ADIM:
-- Yetkili WMS oturumuyla POST /api/stock-balances/rebuild cagrilarak
-- RII_LOCATION_STOCK_BALANCE ve RII_WAREHOUSE_STOCK_BALANCE ledger'dan yeniden uretilmelidir.
