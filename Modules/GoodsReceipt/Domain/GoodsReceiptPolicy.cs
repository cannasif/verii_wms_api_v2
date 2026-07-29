using verii_wms_api_v2.Shared.Domain;

namespace verii_wms_api_v2.Modules.GoodsReceipt.Domain;

public enum OverReceiptPolicy { NotAllowed = 1, WithinTolerance = 2, ApprovalRequired = 3 }
public enum InventoryAvailabilityPolicy { Immediate = 1, AfterReceiptApproval = 2, AfterQualityApproval = 3, AfterAllApprovals = 4 }
public enum GoodsReceiptErpPostingPolicy { AfterReceipt = 1, AfterReceiptApproval = 2, AfterQualityApproval = 3, AfterAllApprovals = 4 }

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
    public bool AllowOrderlessReceipt { get; set; } = true;
    public bool AllowUnplannedReceipt { get; set; } = true;
    public bool ShowAllocatedOpenOrderLines { get; set; }
    public byte[] RowVersion { get; set; } = [];
}
