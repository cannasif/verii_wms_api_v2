-- ShowAllocatedOpenOrderLinesGoodsReceiptPolicy
-- MigrationId: 20260729123000_ShowAllocatedOpenOrderLinesGoodsReceiptPolicy
-- Açıklama: Mal kabul sürecinde ayrılmış/gönderilmiş açık sipariş kalemlerini
--           gösterme ayarı (RII_GR_POLICIES.ShowAllocatedOpenOrderLines). Varsayılan: kapalı (0).

IF COL_LENGTH(N'dbo.RII_GR_POLICIES', N'ShowAllocatedOpenOrderLines') IS NULL
BEGIN
    ALTER TABLE [dbo].[RII_GR_POLICIES]
    ADD [ShowAllocatedOpenOrderLines] bit NOT NULL
        CONSTRAINT [DF_RII_GR_POLICIES_ShowAllocatedOpenOrderLines] DEFAULT (0);
END
GO

-- EF migrations history (API sonradan migrate ederse tekrar uygulamaması için)
IF OBJECT_ID(N'dbo.__EFMigrationsHistory') IS NOT NULL
   AND NOT EXISTS (
        SELECT 1
        FROM [dbo].[__EFMigrationsHistory]
        WHERE [MigrationId] = N'20260729123000_ShowAllocatedOpenOrderLinesGoodsReceiptPolicy'
   )
BEGIN
    INSERT INTO [dbo].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260729123000_ShowAllocatedOpenOrderLinesGoodsReceiptPolicy', N'9.0.0');
END
GO
