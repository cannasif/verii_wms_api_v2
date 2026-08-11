using System.Data;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using verii_wms_api_v2.Modules.ErpBalanceSync.Application;
using verii_wms_api_v2.Modules.ErpBalanceSync.Domain;
using verii_wms_api_v2.Modules.Identity.Infrastructure;

namespace verii_wms_api_v2.Modules.ErpBalanceSync.Infrastructure;

public sealed class SqlServerErpStockBalanceSyncStore(
    WmsDbContext dbContext,
    IOptions<ErpStockBalanceSyncOptions> optionsAccessor) : IErpStockBalanceSyncStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ErpStockBalanceSyncOptions _options = optionsAccessor.Value;

    public async Task<long> StartRunAsync(ErpStockBalanceSyncJobRequest request, CancellationToken cancellationToken)
    {
        var targets = NormalizeAndValidate(request);
        var singleTarget = targets.Count == 1 ? targets[0] : null;
        var now = DateTime.UtcNow;
        var run = new ErpStockBalanceSyncRun
        {
            BranchCode = "0",
            Mode = request.Mode,
            TriggerSource = request.TriggerSource,
            Status = ErpStockBalanceSyncStatuses.Running,
            WarehouseCode = singleTarget?.WarehouseCode,
            StockCode = singleTarget?.StockCode,
            TriggerReference = Limit(request.TriggerReference, 150),
            StartedAtUtc = now,
            CreatedDate = now
        };
        dbContext.ErpStockBalanceSyncRuns.Add(run);
        await dbContext.SaveChangesAsync(cancellationToken);
        return run.Id;
    }

    public async Task<ErpStockBalanceSyncResult> SynchronizeAsync(
        long runId,
        ErpStockBalanceSyncJobRequest request,
        CancellationToken cancellationToken)
    {
        var targets = NormalizeAndValidate(request);
        var wasOpen = dbContext.Database.GetDbConnection().State == ConnectionState.Open;
        if (!wasOpen)
            await dbContext.Database.OpenConnectionAsync(cancellationToken);

        try
        {
            var connection = (SqlConnection)dbContext.Database.GetDbConnection();
            await using var command = connection.CreateCommand();
            command.CommandText = SyncSql;
            command.CommandType = CommandType.Text;
            command.CommandTimeout = _options.CommandTimeoutSeconds;
            command.Parameters.Add(new SqlParameter("@RunId", SqlDbType.BigInt) { Value = runId });
            command.Parameters.Add(new SqlParameter("@IsFull", SqlDbType.Bit)
            {
                Value = string.Equals(request.Mode, ErpStockBalanceSyncModes.Full, StringComparison.OrdinalIgnoreCase)
            });
            command.Parameters.Add(new SqlParameter("@TargetsJson", SqlDbType.NVarChar, -1)
            {
                Value = JsonSerializer.Serialize(targets, JsonOptions)
            });
            command.Parameters.Add(new SqlParameter("@Now", SqlDbType.DateTime2) { Value = DateTime.UtcNow });
            command.Parameters.Add(new SqlParameter("@BatchSize", SqlDbType.Int) { Value = _options.BatchSize });
            command.Parameters.Add(new SqlParameter("@MinimumFullSourceRows", SqlDbType.Int) { Value = _options.MinimumFullSourceRows });
            command.Parameters.Add(new SqlParameter("@MinimumPreviousSourceRatio", SqlDbType.Decimal)
            {
                Precision = 5,
                Scale = 4,
                Value = _options.MinimumPreviousSourceRatio
            });

            await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
                throw new InvalidOperationException("ERP stock balance synchronization did not return a result.");

            return new ErpStockBalanceSyncResult(
                runId,
                reader.GetInt32(0),
                reader.GetInt32(1),
                reader.GetInt32(2),
                reader.GetInt32(3),
                reader.GetInt32(4),
                reader.GetInt32(5),
                reader.GetInt32(6));
        }
        finally
        {
            if (!wasOpen)
                await dbContext.Database.CloseConnectionAsync();
        }
    }

    public async Task CompleteRunAsync(ErpStockBalanceSyncResult result, CancellationToken cancellationToken)
    {
        var run = await dbContext.ErpStockBalanceSyncRuns.SingleAsync(x => x.Id == result.RunId, cancellationToken);
        run.Status = ErpStockBalanceSyncStatuses.Succeeded;
        run.CompletedAtUtc = DateTime.UtcNow;
        run.DurationMs = Math.Max(0, (long)(run.CompletedAtUtc.Value - run.StartedAtUtc).TotalMilliseconds);
        run.SourceCount = result.SourceCount;
        run.InsertedCount = result.InsertedCount;
        run.UpdatedCount = result.UpdatedCount;
        run.UnchangedCount = result.UnchangedCount;
        run.MissingCount = result.MissingCount;
        run.DifferenceCount = result.DifferenceCount;
        run.UnmappedCount = result.UnmappedCount;
        run.UpdatedDate = run.CompletedAtUtc;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task FailRunAsync(long runId, Exception exception, CancellationToken cancellationToken)
    {
        dbContext.ChangeTracker.Clear();
        var run = await dbContext.ErpStockBalanceSyncRuns.SingleOrDefaultAsync(x => x.Id == runId, cancellationToken);
        if (run is null)
            return;
        run.Status = ErpStockBalanceSyncStatuses.Failed;
        run.CompletedAtUtc = DateTime.UtcNow;
        run.DurationMs = Math.Max(0, (long)(run.CompletedAtUtc.Value - run.StartedAtUtc).TotalMilliseconds);
        run.ErrorType = Limit(exception.GetType().FullName, 500);
        run.ErrorMessage = Limit(exception.GetBaseException().Message, 4000);
        run.UpdatedDate = run.CompletedAtUtc;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private IReadOnlyList<ErpStockBalanceTarget> NormalizeAndValidate(ErpStockBalanceSyncJobRequest request)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));
        if (!string.Equals(request.Mode, ErpStockBalanceSyncModes.Full, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(request.Mode, ErpStockBalanceSyncModes.Targeted, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Unsupported ERP stock balance synchronization mode: {request.Mode}");

        var targets = (request.Targets ?? [])
            .Where(x => x.WarehouseCode >= 0 && !string.IsNullOrWhiteSpace(x.StockCode))
            .Select(x => new ErpStockBalanceTarget(x.WarehouseCode, x.StockCode.Trim().ToUpperInvariant()))
            .Distinct()
            .ToList();
        if (string.Equals(request.Mode, ErpStockBalanceSyncModes.Targeted, StringComparison.OrdinalIgnoreCase)
            && targets.Count == 0)
            throw new InvalidOperationException("Targeted ERP stock balance synchronization requires at least one warehouse/stock target.");
        if (targets.Count > _options.MaximumTargetCount)
            throw new InvalidOperationException($"ERP stock balance target count exceeds the configured maximum of {_options.MaximumTargetCount}.");
        return targets;
    }

    private static string? Limit(string? value, int maxLength) =>
        string.IsNullOrEmpty(value) || value.Length <= maxLength ? value : value[..maxLength];

    internal const string SyncSql = """
        SET NOCOUNT ON;
        SET XACT_ABORT ON;

        CREATE TABLE #ERP_BALANCE_STAGE
        (
            WarehouseCode int NOT NULL,
            StockCode varchar(50) COLLATE DATABASE_DEFAULT NOT NULL,
            ErpQuantity decimal(38,8) NOT NULL,
            BranchCode nvarchar(10) NULL,
            WarehouseId bigint NULL,
            StockId bigint NULL,
            UnitCode nvarchar(20) NULL,
            WmsQuantity decimal(38,8) NOT NULL DEFAULT (0),
            MappingCount int NOT NULL DEFAULT (0),
            PRIMARY KEY (WarehouseCode, StockCode)
        );
        CREATE TABLE #ERP_MISSING_STAGE
        (
            SnapshotId bigint NOT NULL PRIMARY KEY,
            WmsQuantity decimal(38,8) NOT NULL
        );

        IF @IsFull = 1
        BEGIN
            INSERT INTO #ERP_BALANCE_STAGE (WarehouseCode, StockCode, ErpQuantity)
            SELECT CONVERT(int, DEPO_KODU),
                   CONVERT(varchar(50), UPPER(LTRIM(RTRIM(STOK_KODU)))),
                   CONVERT(decimal(38,8), SUM(BAKIYE))
            FROM dbo.RII_FN_STOCK_BALANCE(NULL, NULL)
            WHERE DEPO_KODU IS NOT NULL AND NULLIF(LTRIM(RTRIM(STOK_KODU)), '') IS NOT NULL
            GROUP BY DEPO_KODU, UPPER(LTRIM(RTRIM(STOK_KODU)));
        END
        ELSE
        BEGIN
            CREATE TABLE #TARGETS (WarehouseCode int NOT NULL, StockCode varchar(50) COLLATE DATABASE_DEFAULT NOT NULL,
                PRIMARY KEY (WarehouseCode, StockCode));
            INSERT INTO #TARGETS (WarehouseCode, StockCode)
            SELECT DISTINCT WarehouseCode, UPPER(LTRIM(RTRIM(StockCode)))
            FROM OPENJSON(@TargetsJson)
            WITH (WarehouseCode int '$.warehouseCode', StockCode varchar(50) '$.stockCode')
            WHERE WarehouseCode >= 0 AND NULLIF(LTRIM(RTRIM(StockCode)), '') IS NOT NULL;

            INSERT INTO #ERP_BALANCE_STAGE (WarehouseCode, StockCode, ErpQuantity)
            SELECT target.WarehouseCode,
                   target.StockCode,
                   CONVERT(decimal(38,8), COALESCE(SUM(source.BAKIYE), 0))
            FROM #TARGETS target
            OUTER APPLY dbo.RII_FN_STOCK_BALANCE(target.WarehouseCode, target.StockCode) source
            GROUP BY target.WarehouseCode, target.StockCode;
        END;

        DECLARE @SourceCount int = (SELECT COUNT(*) FROM #ERP_BALANCE_STAGE);
        DECLARE @PreviousSourceCount int =
        (
            SELECT TOP (1) SourceCount
            FROM RII_ERP_STOCK_BALANCE_SYNC_RUN
            WHERE Id <> @RunId AND IsDeleted = 0 AND Mode = 'Full' AND Status = 'Succeeded'
            ORDER BY StartedAtUtc DESC
        );
        IF @IsFull = 1 AND
           (@SourceCount < @MinimumFullSourceRows OR
            (@PreviousSourceCount > 0 AND @SourceCount < CEILING(@PreviousSourceCount * @MinimumPreviousSourceRatio)))
            THROW 51020, 'ERP balance full snapshot failed its source row-count safety threshold.', 1;

        UPDATE stage
        SET MappingCount = CONVERT(int, candidate.MappingCount),
            BranchCode = CASE WHEN candidate.MappingCount = 1 THEN candidate.BranchCode END,
            WarehouseId = CASE WHEN candidate.MappingCount = 1 THEN candidate.WarehouseId END,
            StockId = CASE WHEN candidate.MappingCount = 1 THEN candidate.StockId END,
            UnitCode = CASE WHEN candidate.MappingCount = 1 THEN candidate.UnitCode END
        FROM #ERP_BALANCE_STAGE stage
        OUTER APPLY
        (
            SELECT COUNT_BIG(*) MappingCount,
                   MIN(warehouse.BranchCode) BranchCode,
                   MIN(warehouse.Id) WarehouseId,
                   MIN(stock.Id) StockId,
                   MIN(stock.BaseUnitCode) UnitCode
            FROM RII_WAREHOUSE warehouse
            INNER JOIN RII_STOCK stock ON stock.BranchCode = warehouse.BranchCode AND stock.IsDeleted = 0
            WHERE warehouse.IsDeleted = 0
              AND warehouse.WarehouseCode = stage.WarehouseCode
              AND stock.ErpStockCode = stage.StockCode
        ) candidate;

        UPDATE stage
        SET WmsQuantity = COALESCE(balance.Quantity, 0)
        FROM #ERP_BALANCE_STAGE stage
        OUTER APPLY
        (
            SELECT CONVERT(decimal(38,8), SUM(currentBalance.Quantity)) Quantity
            FROM RII_WAREHOUSE_STOCK_BALANCE currentBalance
            WHERE currentBalance.IsDeleted = 0
              AND currentBalance.WarehouseId = stage.WarehouseId
              AND currentBalance.StockId = stage.StockId
        ) balance;

        INSERT INTO RII_ERP_STOCK_BALANCE_CHANGE_LOG
        (BranchCode, SyncRunId, WarehouseCode, StockCode, WarehouseId, StockId,
         PreviousErpQuantity, CurrentErpQuantity, PreviousWmsQuantity, CurrentWmsQuantity,
         Difference, ChangeType, ReasonCode, ObservedAtUtc, CreatedDate, IsDeleted)
        SELECT COALESCE(stage.BranchCode, '0'), @RunId, stage.WarehouseCode, stage.StockCode,
               stage.WarehouseId, stage.StockId, currentSnapshot.ErpQuantity, stage.ErpQuantity,
               COALESCE(currentSnapshot.WmsQuantityAtSync, 0), stage.WmsQuantity,
               stage.ErpQuantity - stage.WmsQuantity,
               CASE
                   WHEN currentSnapshot.Id IS NULL THEN 'NewBalance'
                   WHEN currentSnapshot.IsMissingInErp = 1 THEN 'RestoredInErp'
                   WHEN currentSnapshot.MappingStatus <> CASE WHEN stage.MappingCount = 1 THEN 'Mapped' WHEN stage.MappingCount = 0 THEN 'Unmapped' ELSE 'Ambiguous' END THEN 'MappingChanged'
                   WHEN currentSnapshot.ErpQuantity <> stage.ErpQuantity THEN 'ErpQuantityChanged'
                   ELSE 'WmsQuantityChanged'
               END,
               CASE
                   WHEN currentSnapshot.IsMissingInErp = 1 THEN 'ERP_SOURCE_ROW_RESTORED'
                   WHEN currentSnapshot.MappingStatus <> CASE WHEN stage.MappingCount = 1 THEN 'Mapped' WHEN stage.MappingCount = 0 THEN 'Unmapped' ELSE 'Ambiguous' END THEN 'MASTER_DATA_MAPPING_CHANGED'
                   WHEN currentSnapshot.Id IS NULL OR currentSnapshot.ErpQuantity <> stage.ErpQuantity THEN 'ERP_SNAPSHOT_CHANGED'
                   ELSE 'WMS_PROJECTION_CHANGED'
               END,
               @Now, @Now, 0
        FROM #ERP_BALANCE_STAGE stage
        LEFT JOIN RII_ERP_WAREHOUSE_STOCK_BALANCE currentSnapshot
          ON currentSnapshot.IsDeleted = 0
         AND currentSnapshot.WarehouseCode = stage.WarehouseCode
         AND currentSnapshot.StockCode = stage.StockCode
        WHERE currentSnapshot.Id IS NULL
           OR currentSnapshot.IsMissingInErp = 1
           OR currentSnapshot.ErpQuantity <> stage.ErpQuantity
           OR currentSnapshot.WmsQuantityAtSync <> stage.WmsQuantity
           OR ISNULL(currentSnapshot.WarehouseId, 0) <> ISNULL(stage.WarehouseId, 0)
           OR ISNULL(currentSnapshot.StockId, 0) <> ISNULL(stage.StockId, 0)
           OR ISNULL(currentSnapshot.UnitCode, '') <> ISNULL(stage.UnitCode, '')
           OR currentSnapshot.MappingStatus <> CASE WHEN stage.MappingCount = 1 THEN 'Mapped' WHEN stage.MappingCount = 0 THEN 'Unmapped' ELSE 'Ambiguous' END;

        IF @IsFull = 1
        BEGIN
            INSERT INTO #ERP_MISSING_STAGE (SnapshotId, WmsQuantity)
            SELECT currentSnapshot.Id, COALESCE(balance.Quantity, 0)
            FROM RII_ERP_WAREHOUSE_STOCK_BALANCE currentSnapshot
            OUTER APPLY
            (
                SELECT CONVERT(decimal(38,8), SUM(currentBalance.Quantity)) Quantity
                FROM RII_WAREHOUSE_STOCK_BALANCE currentBalance
                WHERE currentBalance.IsDeleted = 0
                  AND currentBalance.WarehouseId = currentSnapshot.WarehouseId
                  AND currentBalance.StockId = currentSnapshot.StockId
            ) balance
            WHERE currentSnapshot.IsDeleted = 0
              AND NOT EXISTS
              (
                  SELECT 1 FROM #ERP_BALANCE_STAGE stage
                  WHERE stage.WarehouseCode = currentSnapshot.WarehouseCode
                    AND stage.StockCode = currentSnapshot.StockCode
              )
              AND (currentSnapshot.IsMissingInErp = 0 OR currentSnapshot.WmsQuantityAtSync <> COALESCE(balance.Quantity, 0));

            INSERT INTO RII_ERP_STOCK_BALANCE_CHANGE_LOG
            (BranchCode, SyncRunId, WarehouseCode, StockCode, WarehouseId, StockId,
             PreviousErpQuantity, CurrentErpQuantity, PreviousWmsQuantity, CurrentWmsQuantity,
             Difference, ChangeType, ReasonCode, ObservedAtUtc, CreatedDate, IsDeleted)
            SELECT currentSnapshot.BranchCode, @RunId, currentSnapshot.WarehouseCode, currentSnapshot.StockCode,
                   currentSnapshot.WarehouseId, currentSnapshot.StockId,
                   currentSnapshot.ErpQuantity, 0, currentSnapshot.WmsQuantityAtSync,
                   missing.WmsQuantity, -missing.WmsQuantity,
                   CASE WHEN currentSnapshot.IsMissingInErp = 0 THEN 'MissingInErp' ELSE 'WmsQuantityChanged' END,
                   CASE WHEN currentSnapshot.IsMissingInErp = 0 THEN 'ERP_SOURCE_ROW_MISSING' ELSE 'WMS_PROJECTION_CHANGED' END,
                   @Now, @Now, 0
            FROM #ERP_MISSING_STAGE missing
            INNER JOIN RII_ERP_WAREHOUSE_STOCK_BALANCE currentSnapshot ON currentSnapshot.Id = missing.SnapshotId;
        END;

        DECLARE @InsertedCount int = 0, @UpdatedCount int = 0, @MissingCount int = 0, @Affected int = 1;
        WHILE @Affected > 0
        BEGIN
            INSERT TOP (@BatchSize) INTO RII_ERP_WAREHOUSE_STOCK_BALANCE
            (BranchCode, WarehouseCode, StockCode, WarehouseId, StockId, UnitCode,
             ErpQuantity, WmsQuantityAtSync, Difference, MappingStatus, IsMissingInErp,
             FirstObservedAtUtc, LastChangedAtUtc, LastSyncRunId, CreatedDate, IsDeleted)
            SELECT COALESCE(stage.BranchCode, '0'), stage.WarehouseCode, stage.StockCode,
                   stage.WarehouseId, stage.StockId, stage.UnitCode, stage.ErpQuantity,
                   stage.WmsQuantity, stage.ErpQuantity - stage.WmsQuantity,
                   CASE WHEN stage.MappingCount = 1 THEN 'Mapped' WHEN stage.MappingCount = 0 THEN 'Unmapped' ELSE 'Ambiguous' END,
                   0, @Now, @Now, @RunId, @Now, 0
            FROM #ERP_BALANCE_STAGE stage
            WHERE NOT EXISTS
            (
                SELECT 1 FROM RII_ERP_WAREHOUSE_STOCK_BALANCE currentSnapshot
                WHERE currentSnapshot.IsDeleted = 0
                  AND currentSnapshot.WarehouseCode = stage.WarehouseCode
                  AND currentSnapshot.StockCode = stage.StockCode
            );
            SET @Affected = @@ROWCOUNT;
            SET @InsertedCount += @Affected;
        END;

        SET @Affected = 1;
        WHILE @Affected > 0
        BEGIN
            UPDATE TOP (@BatchSize) currentSnapshot WITH (ROWLOCK, UPDLOCK)
            SET BranchCode = COALESCE(stage.BranchCode, '0'),
                WarehouseId = stage.WarehouseId,
                StockId = stage.StockId,
                UnitCode = stage.UnitCode,
                ErpQuantity = stage.ErpQuantity,
                WmsQuantityAtSync = stage.WmsQuantity,
                Difference = stage.ErpQuantity - stage.WmsQuantity,
                MappingStatus = CASE WHEN stage.MappingCount = 1 THEN 'Mapped' WHEN stage.MappingCount = 0 THEN 'Unmapped' ELSE 'Ambiguous' END,
                IsMissingInErp = 0,
                LastChangedAtUtc = @Now,
                LastSyncRunId = @RunId,
                UpdatedDate = @Now
            FROM RII_ERP_WAREHOUSE_STOCK_BALANCE currentSnapshot
            INNER JOIN #ERP_BALANCE_STAGE stage
              ON stage.WarehouseCode = currentSnapshot.WarehouseCode
             AND stage.StockCode = currentSnapshot.StockCode
            WHERE currentSnapshot.IsDeleted = 0
              AND currentSnapshot.LastSyncRunId <> @RunId
              AND
              (
                  currentSnapshot.IsMissingInErp = 1
                  OR currentSnapshot.ErpQuantity <> stage.ErpQuantity
                  OR currentSnapshot.WmsQuantityAtSync <> stage.WmsQuantity
                  OR ISNULL(currentSnapshot.WarehouseId, 0) <> ISNULL(stage.WarehouseId, 0)
                  OR ISNULL(currentSnapshot.StockId, 0) <> ISNULL(stage.StockId, 0)
                  OR ISNULL(currentSnapshot.UnitCode, '') <> ISNULL(stage.UnitCode, '')
                  OR currentSnapshot.MappingStatus <> CASE WHEN stage.MappingCount = 1 THEN 'Mapped' WHEN stage.MappingCount = 0 THEN 'Unmapped' ELSE 'Ambiguous' END
              );
            SET @Affected = @@ROWCOUNT;
            SET @UpdatedCount += @Affected;
        END;

        IF @IsFull = 1
        BEGIN
            SET @Affected = 1;
            WHILE @Affected > 0
            BEGIN
                UPDATE TOP (@BatchSize) currentSnapshot WITH (ROWLOCK, UPDLOCK)
                SET ErpQuantity = 0,
                    WmsQuantityAtSync = missing.WmsQuantity,
                    Difference = -missing.WmsQuantity,
                    IsMissingInErp = 1,
                    LastChangedAtUtc = @Now,
                    LastSyncRunId = @RunId,
                    UpdatedDate = @Now
                FROM RII_ERP_WAREHOUSE_STOCK_BALANCE currentSnapshot
                INNER JOIN #ERP_MISSING_STAGE missing ON missing.SnapshotId = currentSnapshot.Id
                WHERE currentSnapshot.IsDeleted = 0
                  AND currentSnapshot.LastSyncRunId <> @RunId
                  AND (currentSnapshot.IsMissingInErp = 0 OR currentSnapshot.WmsQuantityAtSync <> missing.WmsQuantity);
                SET @Affected = @@ROWCOUNT;
                SET @MissingCount += @Affected;
            END;
        END;

        DECLARE @UnchangedCount int = @SourceCount - @InsertedCount - @UpdatedCount;
        DECLARE @DifferenceCount int =
        (
            SELECT COUNT(*)
            FROM RII_ERP_WAREHOUSE_STOCK_BALANCE currentSnapshot
            WHERE currentSnapshot.IsDeleted = 0
              AND currentSnapshot.MappingStatus = 'Mapped'
              AND currentSnapshot.Difference <> 0
              AND
              (
                  @IsFull = 1 OR EXISTS
                  (
                      SELECT 1 FROM #ERP_BALANCE_STAGE stage
                      WHERE stage.WarehouseCode = currentSnapshot.WarehouseCode
                        AND stage.StockCode = currentSnapshot.StockCode
                  )
              )
        );
        DECLARE @UnmappedCount int =
        (
            SELECT COUNT(*) FROM #ERP_BALANCE_STAGE
            WHERE MappingCount <> 1
        );

        SELECT @SourceCount SourceCount,
               @InsertedCount InsertedCount,
               @UpdatedCount UpdatedCount,
               CASE WHEN @UnchangedCount < 0 THEN 0 ELSE @UnchangedCount END UnchangedCount,
               @MissingCount MissingCount,
               @DifferenceCount DifferenceCount,
               @UnmappedCount UnmappedCount;
        """;
}
