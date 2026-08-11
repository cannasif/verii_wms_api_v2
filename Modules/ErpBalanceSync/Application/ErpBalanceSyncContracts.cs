using Hangfire;

using verii_wms_api_v2.Shared;

namespace verii_wms_api_v2.Modules.ErpBalanceSync.Application;

public sealed record ErpStockBalanceTarget(int WarehouseCode, string StockCode);

public sealed record ErpStockBalanceSyncJobRequest(
    string Mode,
    string TriggerSource,
    IReadOnlyList<ErpStockBalanceTarget> Targets,
    string? TriggerReference = null)
{
    public static ErpStockBalanceSyncJobRequest Full() =>
        new(Domain.ErpStockBalanceSyncModes.Full, Domain.ErpStockBalanceSyncTriggerSources.Hangfire, []);
}

public sealed record ErpStockBalanceSyncResult(
    long RunId,
    int SourceCount,
    int InsertedCount,
    int UpdatedCount,
    int UnchangedCount,
    int MissingCount,
    int DifferenceCount,
    int UnmappedCount);

public sealed record ErpWarehouseStockBalanceRow(
    long Id,
    int WarehouseCode,
    string? WarehouseName,
    string StockCode,
    string? StockName,
    string? UnitCode,
    decimal ErpQuantity,
    decimal WmsQuantity,
    decimal Difference,
    string MappingStatus,
    bool IsMissingInErp,
    DateTime FirstObservedAtUtc,
    DateTime LastChangedAtUtc,
    long LastSyncRunId);

public sealed record ErpStockBalanceChangeRow(
    long Id,
    long SyncRunId,
    int WarehouseCode,
    string StockCode,
    decimal? PreviousErpQuantity,
    decimal CurrentErpQuantity,
    decimal PreviousWmsQuantity,
    decimal CurrentWmsQuantity,
    decimal Difference,
    string ChangeType,
    string ReasonCode,
    DateTime ObservedAtUtc);

public sealed record ErpStockBalanceSyncRunRow(
    long Id,
    Guid RunKey,
    string Mode,
    string TriggerSource,
    string Status,
    int? WarehouseCode,
    string? StockCode,
    string? TriggerReference,
    DateTime StartedAtUtc,
    DateTime? CompletedAtUtc,
    long? DurationMs,
    int SourceCount,
    int InsertedCount,
    int UpdatedCount,
    int UnchangedCount,
    int MissingCount,
    int DifferenceCount,
    int UnmappedCount,
    string? ErrorMessage);

public sealed class ErpStockBalanceSyncOptions
{
    public const string SectionName = "ErpBalanceSync";
    public bool Enabled { get; set; } = true;
    public string Cron { get; set; } = "*/5 * * * *";
    public int CommandTimeoutSeconds { get; set; } = 180;
    public int BatchSize { get; set; } = 500;
    public int MinimumFullSourceRows { get; set; } = 1;
    public decimal MinimumPreviousSourceRatio { get; set; } = 0.50m;
    public int MaximumTargetCount { get; set; } = 500;
}

public interface IErpStockBalanceSyncStore
{
    Task<long> StartRunAsync(ErpStockBalanceSyncJobRequest request, CancellationToken cancellationToken);
    Task<ErpStockBalanceSyncResult> SynchronizeAsync(long runId, ErpStockBalanceSyncJobRequest request, CancellationToken cancellationToken);
    Task CompleteRunAsync(ErpStockBalanceSyncResult result, CancellationToken cancellationToken);
    Task FailRunAsync(long runId, Exception exception, CancellationToken cancellationToken);
}

public interface IErpStockBalanceSyncJobRunner
{
    [AutomaticRetry(Attempts = 0)]
    [DisableConcurrentExecution(600)]
    Task RunAsync(ErpStockBalanceSyncJobRequest request, CancellationToken cancellationToken = default);
}

public interface IErpStockBalanceQueryService
{
    Task<PagedResponse<ErpWarehouseStockBalanceRow>> GetBalancesAsync(PagedRequest request, CancellationToken cancellationToken);
    Task<PagedResponse<ErpStockBalanceChangeRow>> GetChangesAsync(PagedRequest request, CancellationToken cancellationToken);
    Task<PagedResponse<ErpStockBalanceSyncRunRow>> GetRunsAsync(PagedRequest request, CancellationToken cancellationToken);
}
