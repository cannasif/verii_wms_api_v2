/*
  WMS v1 -> WMS v2 KKD hakediş matrisi aktarımı
  - Aynı SQL Server örneğinde V3RIIWMS_NEW ve V3RIIWMSV2 veritabanlarını bekler.
  - Tekrar çalıştırılabilir; var olan kodları çoğaltmaz.
  - Cari ve stokları teknik Id ile değil ERP koduyla eşler.
  - Kaynağı çözülemeyen kayıtları sonuç setinde raporlar.
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRANSACTION;

MERGE dbo.RII_KKD_DEPARTMENT AS target
USING (
    SELECT UPPER(LTRIM(RTRIM(DepartmentCode))) Code,
           MAX(LTRIM(RTRIM(DepartmentName))) Name,
           CONVERT(bit, MAX(CONVERT(int, IsActive))) IsActive,
           MAX(COALESCE(NULLIF(LTRIM(RTRIM(BranchCode)), ''), '0')) BranchCode
    FROM V3RIIWMS_NEW.dbo.RII_KKD_EMPLOYEE_DEPARTMENT
    WHERE IsDeleted = 0
    GROUP BY UPPER(LTRIM(RTRIM(DepartmentCode)))
) AS source ON target.Code = source.Code AND target.IsDeleted = 0
WHEN MATCHED THEN UPDATE SET target.Name = source.Name, target.IsActive = source.IsActive,
    target.UpdatedDate = SYSUTCDATETIME()
WHEN NOT MATCHED THEN INSERT (Code, Name, IsActive, BranchCode, CreatedDate, IsDeleted)
    VALUES (source.Code, source.Name, source.IsActive, source.BranchCode, SYSUTCDATETIME(), 0);

MERGE dbo.RII_KKD_ROLE AS target
USING (
    SELECT UPPER(LTRIM(RTRIM(r.RoleCode))) Code, LTRIM(RTRIM(r.RoleName)) Name,
           nd.Id DepartmentId, r.IsActive,
           COALESCE(NULLIF(LTRIM(RTRIM(r.BranchCode)), ''), '0') BranchCode
    FROM V3RIIWMS_NEW.dbo.RII_KKD_EMPLOYEE_ROLE r
    LEFT JOIN V3RIIWMS_NEW.dbo.RII_KKD_EMPLOYEE_DEPARTMENT od ON od.Id = r.DepartmentId AND od.IsDeleted = 0
    LEFT JOIN dbo.RII_KKD_DEPARTMENT nd ON nd.Code = UPPER(LTRIM(RTRIM(od.DepartmentCode))) AND nd.IsDeleted = 0
    WHERE r.IsDeleted = 0
) AS source ON target.Code = source.Code
    AND ISNULL(target.DepartmentId, -1) = ISNULL(source.DepartmentId, -1) AND target.IsDeleted = 0
WHEN MATCHED THEN UPDATE SET target.Name = source.Name, target.IsActive = source.IsActive,
    target.UpdatedDate = SYSUTCDATETIME()
WHEN NOT MATCHED THEN INSERT (DepartmentId, Code, Name, IsActive, BranchCode, CreatedDate, IsDeleted)
    VALUES (source.DepartmentId, source.Code, source.Name, source.IsActive, source.BranchCode, SYSUTCDATETIME(), 0);

;WITH ResolvedMatrices AS (
    SELECT h.Id SourceId, nc.Id CustomerId, nd.Id DepartmentId, nr.Id RoleId,
           UPPER(LTRIM(RTRIM(h.MatrixCode))) Code, LTRIM(RTRIM(h.MatrixName)) Name,
           CONVERT(date, h.EffectiveFrom) EffectiveFrom, CONVERT(date, h.EffectiveTo) EffectiveTo,
           h.IsActive, h.Description, COALESCE(NULLIF(LTRIM(RTRIM(h.BranchCode)), ''), '0') BranchCode
    FROM V3RIIWMS_NEW.dbo.RII_KKD_ENTITLEMENT_MATRIX_HEADER h
    LEFT JOIN V3RIIWMS_NEW.dbo.RII_WMS_CUSTOMER oc ON oc.Id = h.CustomerId AND oc.IsDeleted = 0
    LEFT JOIN dbo.RII_CUSTOMER nc ON nc.CustomerCode = oc.CustomerCode AND nc.IsDeleted = 0
    JOIN V3RIIWMS_NEW.dbo.RII_KKD_EMPLOYEE_DEPARTMENT od ON od.Id = h.DepartmentId AND od.IsDeleted = 0
    JOIN dbo.RII_KKD_DEPARTMENT nd ON nd.Code = UPPER(LTRIM(RTRIM(od.DepartmentCode))) AND nd.IsDeleted = 0
    JOIN V3RIIWMS_NEW.dbo.RII_KKD_EMPLOYEE_ROLE orole ON orole.Id = h.RoleId AND orole.IsDeleted = 0
    JOIN dbo.RII_KKD_ROLE nr ON nr.Code = UPPER(LTRIM(RTRIM(orole.RoleCode)))
        AND (nr.DepartmentId = nd.Id OR nr.DepartmentId IS NULL) AND nr.IsDeleted = 0
    WHERE h.IsDeleted = 0 AND nc.Id IS NOT NULL
)
MERGE dbo.RII_KKD_MATRIX AS target
USING ResolvedMatrices AS source ON target.Code = source.Code AND target.IsDeleted = 0
WHEN MATCHED THEN UPDATE SET target.CustomerId = source.CustomerId, target.DepartmentId = source.DepartmentId,
    target.RoleId = source.RoleId, target.Name = source.Name, target.EffectiveFrom = source.EffectiveFrom,
    target.EffectiveTo = source.EffectiveTo, target.IsActive = source.IsActive, target.Description = source.Description,
    target.UpdatedDate = SYSUTCDATETIME()
WHEN NOT MATCHED THEN INSERT (CustomerId, DepartmentId, RoleId, Code, Name, EffectiveFrom, EffectiveTo,
    IsActive, Description, BranchCode, CreatedDate, IsDeleted)
    VALUES (source.CustomerId, source.DepartmentId, source.RoleId, source.Code, source.Name, source.EffectiveFrom,
    source.EffectiveTo, source.IsActive, source.Description, source.BranchCode, SYSUTCDATETIME(), 0);

;WITH RankedLines AS (
    SELECT l.*, ROW_NUMBER() OVER (
        PARTITION BY l.HeaderId, ISNULL(l.StockId, -1), UPPER(LTRIM(RTRIM(l.GroupCode)))
        ORDER BY l.Id DESC) DuplicateRank
    FROM V3RIIWMS_NEW.dbo.RII_KKD_ENTITLEMENT_MATRIX_LINE l
    WHERE l.IsDeleted = 0
), ResolvedRules AS (
    SELECT l.Id SourceLineId, nm.Id MatrixId, UPPER(LTRIM(RTRIM(l.GroupCode))) GroupCode,
           NULLIF(LTRIM(RTRIM(l.GroupName)), '') GroupName, ns.Id StockId,
           ns.ErpStockCode StockCodeSnapshot, ns.StockName StockNameSnapshot,
           l.StandardCode, l.StandardName, NULLIF(l.AnnualIssueCount, 0) AnnualIssueCount,
           l.AnnualQuantity, l.MaxCarryQuantity, l.AllowBulkIssue, l.IsMandatory,
           l.SortOrder, l.IsActive, l.Description,
           COALESCE(NULLIF(LTRIM(RTRIM(l.BranchCode)), ''), '0') BranchCode
    FROM RankedLines l
    JOIN V3RIIWMS_NEW.dbo.RII_KKD_ENTITLEMENT_MATRIX_HEADER oh ON oh.Id = l.HeaderId AND oh.IsDeleted = 0
    JOIN dbo.RII_KKD_MATRIX nm ON nm.Code = UPPER(LTRIM(RTRIM(oh.MatrixCode))) AND nm.IsDeleted = 0
    LEFT JOIN V3RIIWMS_NEW.dbo.RII_WMS_STOCK os ON os.Id = l.StockId AND os.IsDeleted = 0
    LEFT JOIN dbo.RII_STOCK ns ON ns.ErpStockCode = os.ErpStockCode AND ns.IsDeleted = 0
    WHERE l.DuplicateRank = 1 AND (l.StockId IS NULL OR ns.Id IS NOT NULL)
)
MERGE dbo.RII_KKD_RULE AS target
USING ResolvedRules AS source ON target.MatrixId = source.MatrixId
    AND ISNULL(target.StockId, -1) = ISNULL(source.StockId, -1)
    AND target.GroupCode = source.GroupCode AND target.IsDeleted = 0
WHEN MATCHED THEN UPDATE SET target.GroupName = source.GroupName, target.StockCodeSnapshot = source.StockCodeSnapshot,
    target.StockNameSnapshot = source.StockNameSnapshot, target.StandardCode = source.StandardCode,
    target.StandardName = source.StandardName, target.AnnualIssueCount = source.AnnualIssueCount,
    target.AnnualQuantity = source.AnnualQuantity, target.MaxCarryQuantity = source.MaxCarryQuantity,
    target.AllowBulkIssue = source.AllowBulkIssue, target.IsMandatory = source.IsMandatory,
    target.SortOrder = source.SortOrder, target.IsActive = source.IsActive, target.Description = source.Description,
    target.UpdatedDate = SYSUTCDATETIME()
WHEN NOT MATCHED THEN INSERT (MatrixId, GroupCode, GroupName, StockId, StockCodeSnapshot, StockNameSnapshot,
    StandardCode, StandardName, AnnualIssueCount, AnnualQuantity, MaxCarryQuantity, AllowBulkIssue,
    IsMandatory, SortOrder, IsActive, Description, BranchCode, CreatedDate, IsDeleted)
    VALUES (source.MatrixId, source.GroupCode, source.GroupName, source.StockId, source.StockCodeSnapshot,
    source.StockNameSnapshot, source.StandardCode, source.StandardName, source.AnnualIssueCount,
    source.AnnualQuantity, source.MaxCarryQuantity, source.AllowBulkIssue, source.IsMandatory,
    source.SortOrder, source.IsActive, source.Description, source.BranchCode, SYSUTCDATETIME(), 0);

/* Eski satır özet alanları güvenilir ana kaynak kabul edilir; eksik phase satırları buradan tamamlanır. */
;WITH RankedLines AS (
    SELECT l.*, ROW_NUMBER() OVER (
        PARTITION BY l.HeaderId, ISNULL(l.StockId, -1), UPPER(LTRIM(RTRIM(l.GroupCode)))
        ORDER BY l.Id DESC) DuplicateRank
    FROM V3RIIWMS_NEW.dbo.RII_KKD_ENTITLEMENT_MATRIX_LINE l WHERE l.IsDeleted = 0
), RuleSource AS (
    SELECT l.*, r.Id RuleId
    FROM RankedLines l
    JOIN V3RIIWMS_NEW.dbo.RII_KKD_ENTITLEMENT_MATRIX_HEADER h ON h.Id = l.HeaderId AND h.IsDeleted = 0
    JOIN dbo.RII_KKD_MATRIX m ON m.Code = UPPER(LTRIM(RTRIM(h.MatrixCode))) AND m.IsDeleted = 0
    LEFT JOIN V3RIIWMS_NEW.dbo.RII_WMS_STOCK os ON os.Id = l.StockId AND os.IsDeleted = 0
    LEFT JOIN dbo.RII_STOCK ns ON ns.ErpStockCode = os.ErpStockCode AND ns.IsDeleted = 0
    JOIN dbo.RII_KKD_RULE r ON r.MatrixId = m.Id AND r.GroupCode = UPPER(LTRIM(RTRIM(l.GroupCode)))
        AND ISNULL(r.StockId, -1) = ISNULL(ns.Id, -1) AND r.IsDeleted = 0
    WHERE l.DuplicateRank = 1
), PhaseSource AS (
    SELECT RuleId, 'Initial' PhaseType, 0 OffsetMonths, InitialIssueQuantity Quantity,
           AllowBulkIssue, NULL FrequencyDays, NULL QuantityPerFrequency, NULL PeriodType,
           NULL PeriodInterval, 10 SortOrder, IsActive, BranchCode
    FROM RuleSource
    UNION ALL
    SELECT RuleId, 'AfterMonths', COALESCE(NULLIF(AdditionalAfterMonths, 0), 3), AdditionalAfterMonthsQuantity,
           AllowBulkIssue, NULL, NULL, NULL, NULL, 20, IsActive, BranchCode
    FROM RuleSource WHERE COALESCE(AdditionalAfterMonthsQuantity, 0) > 0
    UNION ALL
    SELECT RuleId, 'Recurring', COALESCE(NULLIF(AdditionalAfterMonths, 0), 3), RoutineQuantity,
           AllowBulkIssue, NULL, NULL,
           CASE UPPER(LTRIM(RTRIM(RoutinePeriodType))) WHEN 'DAY' THEN 'Day' WHEN 'MONTH' THEN 'Month' ELSE 'Year' END,
           CASE WHEN RoutinePeriodInterval > 0 THEN RoutinePeriodInterval ELSE 1 END,
           30, IsActive, BranchCode
    FROM RuleSource WHERE COALESCE(RoutineQuantity, 0) > 0
)
MERGE dbo.RII_KKD_PHASE AS target
USING PhaseSource AS source ON target.RuleId = source.RuleId AND target.PhaseType = source.PhaseType
    AND target.OffsetMonths = source.OffsetMonths AND target.IsDeleted = 0
WHEN MATCHED THEN UPDATE SET target.Quantity = source.Quantity, target.AllowBulkIssue = source.AllowBulkIssue,
    target.FrequencyDays = source.FrequencyDays, target.QuantityPerFrequency = source.QuantityPerFrequency,
    target.PeriodType = source.PeriodType, target.PeriodInterval = source.PeriodInterval,
    target.SortOrder = source.SortOrder, target.IsActive = source.IsActive, target.UpdatedDate = SYSUTCDATETIME()
WHEN NOT MATCHED THEN INSERT (RuleId, PhaseType, OffsetMonths, Quantity, AllowBulkIssue, FrequencyDays,
    QuantityPerFrequency, PeriodType, PeriodInterval, SortOrder, IsActive, BranchCode, CreatedDate, IsDeleted)
    VALUES (source.RuleId, source.PhaseType, source.OffsetMonths, source.Quantity, source.AllowBulkIssue,
    source.FrequencyDays, source.QuantityPerFrequency, source.PeriodType, source.PeriodInterval,
    source.SortOrder, source.IsActive, COALESCE(NULLIF(source.BranchCode, ''), '0'), SYSUTCDATETIME(), 0);

COMMIT TRANSACTION;

SELECT 'Department' Entity, COUNT_BIG(*) RecordCount FROM dbo.RII_KKD_DEPARTMENT WHERE IsDeleted = 0
UNION ALL SELECT 'Role', COUNT_BIG(*) FROM dbo.RII_KKD_ROLE WHERE IsDeleted = 0
UNION ALL SELECT 'Matrix', COUNT_BIG(*) FROM dbo.RII_KKD_MATRIX WHERE IsDeleted = 0
UNION ALL SELECT 'Rule', COUNT_BIG(*) FROM dbo.RII_KKD_RULE WHERE IsDeleted = 0
UNION ALL SELECT 'Phase', COUNT_BIG(*) FROM dbo.RII_KKD_PHASE WHERE IsDeleted = 0;

SELECT h.Id SourceMatrixId, h.MatrixCode, h.CustomerId, 'Cari koduyla eşleşen V2 müşteri bulunamadı' Reason
FROM V3RIIWMS_NEW.dbo.RII_KKD_ENTITLEMENT_MATRIX_HEADER h
LEFT JOIN V3RIIWMS_NEW.dbo.RII_WMS_CUSTOMER oc ON oc.Id = h.CustomerId AND oc.IsDeleted = 0
LEFT JOIN dbo.RII_CUSTOMER nc ON nc.CustomerCode = oc.CustomerCode AND nc.IsDeleted = 0
WHERE h.IsDeleted = 0 AND nc.Id IS NULL;
