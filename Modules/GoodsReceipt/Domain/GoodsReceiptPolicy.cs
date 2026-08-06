using verii_wms_api_v2.Shared.Domain;

namespace verii_wms_api_v2.Modules.GoodsReceipt.Domain;

public enum OverReceiptPolicy { NotAllowed = 1, WithinTolerance = 2, ApprovalRequired = 3 }
public enum InventoryAvailabilityPolicy { Immediate = 1, AfterReceiptApproval = 2, AfterQualityApproval = 3, AfterAllApprovals = 4 }
public enum GoodsReceiptErpPostingPolicy { AfterReceipt = 1, AfterReceiptApproval = 2, AfterQualityApproval = 3, AfterAllApprovals = 4 }
public enum GoodsReceiptErpQualityGatePolicy { None = 1, RuleBasedOnly = 2, AnyQualityPlan = 3 }
public enum GoodsReceiptQualityRoutingSource { None = 1, StockRule = 2, StockGroupRule = 3, GlobalDefault = 4, ManualReceipt = 5 }
public enum GoodsReceiptLocationSelectionPolicy
{
    ReceivingOrStagingOnly = 1,
    AnyActiveWarehouseLocation = 2
}

public sealed class GoodsReceiptPolicy : BaseEntity
{
    public string PolicyKey { get; set; } = "DEFAULT";
    public OverReceiptPolicy OverReceiptPolicy { get; set; } = OverReceiptPolicy.NotAllowed;
    public decimal OverReceiptTolerancePercent { get; set; }
    public bool AllowUnderReceipt { get; set; } = true;
    public bool RequireShortCloseApproval { get; set; } = true;
    public bool RequireReceiptApproval { get; set; }
    public bool RequireQualityApproval { get; set; }
    public bool RequireErpApproval { get; set; }
    public bool HoldInventoryUntilQualityDecision { get; set; } = true;
    public bool BlockPutawayUntilQualityDecision { get; set; } = true;
    public InventoryAvailabilityPolicy InventoryAvailabilityPolicy { get; set; } = InventoryAvailabilityPolicy.AfterQualityApproval;
    public GoodsReceiptErpPostingPolicy ErpPostingPolicy { get; set; } = GoodsReceiptErpPostingPolicy.AfterAllApprovals;
    public GoodsReceiptErpQualityGatePolicy ErpQualityGatePolicy { get; set; } = GoodsReceiptErpQualityGatePolicy.AnyQualityPlan;
    public bool AllowOrderlessReceipt { get; set; } = true;
    public bool AllowUnplannedReceipt { get; set; } = true;
    public bool ShowAllocatedOpenOrderLines { get; set; }
    public GoodsReceiptLocationSelectionPolicy LocationSelectionPolicy { get; set; } =
        GoodsReceiptLocationSelectionPolicy.ReceivingOrStagingOnly;
    public byte[] RowVersion { get; set; } = [];
}
