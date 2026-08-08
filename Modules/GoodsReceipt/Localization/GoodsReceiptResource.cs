namespace verii_wms_api_v2.Modules.GoodsReceipt.Localization;

public sealed class GoodsReceiptResource;

public static class GoodsReceiptMessageKeys
{
    public const string Created = nameof(Created);
    public const string InvalidRequest = nameof(InvalidRequest);
    public const string SupplierNotFound = nameof(SupplierNotFound);
    public const string WarehouseNotFound = nameof(WarehouseNotFound);
    public const string ReceivingLocationNotFound = nameof(ReceivingLocationNotFound);
    public const string InvalidReceivingLocation = nameof(InvalidReceivingLocation);
    public const string SourceOrderChanged = nameof(SourceOrderChanged);
    public const string StockMirrorMissing = nameof(StockMirrorMissing);
    public const string YapMirrorMissing = nameof(YapMirrorMissing);
    public const string InvalidQuantity = nameof(InvalidQuantity);
    public const string QuantityExceedsAvailable = nameof(QuantityExceedsAvailable);
    public const string InvalidAssignee = nameof(InvalidAssignee);
    public const string IdempotencyConflict = nameof(IdempotencyConflict);
    public const string ConcurrencyConflict = nameof(ConcurrencyConflict);
    public const string LabelSplitInvalidRequest = nameof(LabelSplitInvalidRequest);
    public const string LabelSplitNotFound = nameof(LabelSplitNotFound);
    public const string LabelSplitAlreadyCompleted = nameof(LabelSplitAlreadyCompleted);
    public const string LabelSplitUnavailable = nameof(LabelSplitUnavailable);
    public const string LabelSplitSerialPerUnitBlocked = nameof(LabelSplitSerialPerUnitBlocked);
    public const string LabelSplitQuantityMismatch = nameof(LabelSplitQuantityMismatch);
    public const string LabelSplitCompleted = nameof(LabelSplitCompleted);
    public const string LabelVoidUnavailable = nameof(LabelVoidUnavailable);
}
