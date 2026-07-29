-- AddGoodsReceiptLocationSelectionPolicy
-- Varsayılan davranış mevcut kurulumlarla uyumludur: yalnızca Receiving / Staging.

IF COL_LENGTH(N'dbo.RII_GR_POLICIES', N'LocationSelectionPolicy') IS NULL
BEGIN
    ALTER TABLE [dbo].[RII_GR_POLICIES]
    ADD [LocationSelectionPolicy] nvarchar(50) NOT NULL
        CONSTRAINT [DF_RII_GR_POLICIES_LocationSelectionPolicy]
        DEFAULT (N'ReceivingOrStagingOnly');
END
GO

IF OBJECT_ID(N'dbo.__EFMigrationsHistory') IS NOT NULL
   AND NOT EXISTS (
       SELECT 1
       FROM [dbo].[__EFMigrationsHistory]
       WHERE [MigrationId] = N'20260729210236_AddGoodsReceiptLocationSelectionPolicy'
   )
BEGIN
    INSERT INTO [dbo].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260729210236_AddGoodsReceiptLocationSelectionPolicy', N'10.0.10');
END
GO
