-- Kalem bazlı talep onayı: RII_PC_REQUEST_LINE.Status
-- PartiallyApproved (header) string olarak Status kolonunda saklanır (enum conversion).

IF COL_LENGTH('dbo.RII_PC_REQUEST_LINE', 'Status') IS NULL
BEGIN
    ALTER TABLE dbo.RII_PC_REQUEST_LINE
        ADD [Status] nvarchar(30) NOT NULL
            CONSTRAINT DF_RII_PC_REQUEST_LINE_Status DEFAULT (N'Draft');
END
GO

-- Mevcut satırları üst talep durumundan dönüştür
UPDATE L
SET L.[Status] = CASE R.[Status]
    WHEN N'Draft' THEN N'Draft'
    WHEN N'PendingApproval' THEN N'PendingApproval'
    WHEN N'Approved' THEN N'Approved'
    WHEN N'Rejected' THEN N'Rejected'
    WHEN N'Cancelled' THEN N'Cancelled'
    WHEN N'Converted' THEN N'Approved'
    WHEN N'PartiallyConverted' THEN N'Approved'
    WHEN N'PartiallyApproved' THEN N'PendingApproval'
    ELSE N'Draft'
END
FROM dbo.RII_PC_REQUEST_LINE L
INNER JOIN dbo.RII_PC_REQUEST R ON R.Id = L.ProcurementRequestId
WHERE L.IsDeleted = 0 AND R.IsDeleted = 0;
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = N'IX_RII_PC_REQUEST_LINE_RequestId_Status'
      AND object_id = OBJECT_ID(N'dbo.RII_PC_REQUEST_LINE')
)
BEGIN
    CREATE INDEX IX_RII_PC_REQUEST_LINE_RequestId_Status
        ON dbo.RII_PC_REQUEST_LINE (ProcurementRequestId, [Status]);
END
GO
