using verii_wms_api_v2.Shared.Domain;

namespace verii_wms_api_v2.Modules.ErpBalanceSync.Domain;

public sealed class ErpStockBalanceSyncRun : BaseEntity
{
    public Guid RunKey { get; set; } = Guid.NewGuid();
    public string Mode { get; set; } = ErpStockBalanceSyncModes.Full;
    public string TriggerSource { get; set; } = ErpStockBalanceSyncTriggerSources.Hangfire;
    public string Status { get; set; } = ErpStockBalanceSyncStatuses.Running;
    public int? WarehouseCode { get; set; }
    public string? StockCode { get; set; }
    public string? TriggerReference { get; set; }
    public DateTime StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public long? DurationMs { get; set; }
    public int SourceCount { get; set; }
    public int InsertedCount { get; set; }
    public int UpdatedCount { get; set; }
    public int UnchangedCount { get; set; }
    public int MissingCount { get; set; }
    public int DifferenceCount { get; set; }
    public int UnmappedCount { get; set; }
    public string? ErrorType { get; set; }
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// ERP/Netsis warehouse balance authority copied into WMS. This is deliberately
/// separate from the movement-ledger projection because ERP has no rack/lot/serial dimension.
/// </summary>
public sealed class ErpWarehouseStockBalance : BaseEntity
{
    public int WarehouseCode { get; set; }
    public string StockCode { get; set; } = string.Empty;
    public long? WarehouseId { get; set; }
    public long? StockId { get; set; }
    public string? UnitCode { get; set; }
    public decimal ErpQuantity { get; set; }
    public decimal WmsQuantityAtSync { get; set; }
    public decimal Difference { get; set; }
    public string MappingStatus { get; set; } = ErpStockBalanceMappingStatuses.Unmapped;
    public bool IsMissingInErp { get; set; }
    public DateTime FirstObservedAtUtc { get; set; }
    public DateTime LastChangedAtUtc { get; set; }
    public long LastSyncRunId { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

public sealed class ErpStockBalanceChangeLog : BaseEntity
{
    public long SyncRunId { get; set; }
    public int WarehouseCode { get; set; }
    public string StockCode { get; set; } = string.Empty;
    public long? WarehouseId { get; set; }
    public long? StockId { get; set; }
    public decimal? PreviousErpQuantity { get; set; }
    public decimal CurrentErpQuantity { get; set; }
    public decimal PreviousWmsQuantity { get; set; }
    public decimal CurrentWmsQuantity { get; set; }
    public decimal Difference { get; set; }
    public string ChangeType { get; set; } = ErpStockBalanceChangeTypes.NewBalance;
    public string ReasonCode { get; set; } = ErpStockBalanceReasonCodes.ErpSnapshotChanged;
    public DateTime ObservedAtUtc { get; set; }
}

public static class ErpStockBalanceSyncModes
{
    public const string Full = "Full";
    public const string Targeted = "Targeted";
}

public static class ErpStockBalanceSyncTriggerSources
{
    public const string Hangfire = "Hangfire";
    public const string ErpPosting = "ErpPosting";
    public const string Manual = "Manual";
}

public static class ErpStockBalanceSyncStatuses
{
    public const string Running = "Running";
    public const string Succeeded = "Succeeded";
    public const string Failed = "Failed";
}

public static class ErpStockBalanceMappingStatuses
{
    public const string Mapped = "Mapped";
    public const string Unmapped = "Unmapped";
    public const string Ambiguous = "Ambiguous";
}

public static class ErpStockBalanceChangeTypes
{
    public const string NewBalance = "NewBalance";
    public const string ErpQuantityChanged = "ErpQuantityChanged";
    public const string WmsQuantityChanged = "WmsQuantityChanged";
    public const string MappingChanged = "MappingChanged";
    public const string MissingInErp = "MissingInErp";
    public const string RestoredInErp = "RestoredInErp";
}

public static class ErpStockBalanceReasonCodes
{
    public const string ErpSnapshotChanged = "ERP_SNAPSHOT_CHANGED";
    public const string WmsProjectionChanged = "WMS_PROJECTION_CHANGED";
    public const string SourceRowMissing = "ERP_SOURCE_ROW_MISSING";
    public const string SourceRowRestored = "ERP_SOURCE_ROW_RESTORED";
    public const string MasterDataMappingChanged = "MASTER_DATA_MAPPING_CHANGED";
}
