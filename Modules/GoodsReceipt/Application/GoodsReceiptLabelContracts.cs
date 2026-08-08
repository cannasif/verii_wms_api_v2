using verii_wms_api_v2.Modules.GoodsReceipt.Domain;
using verii_wms_api_v2.Shared;

namespace verii_wms_api_v2.Modules.GoodsReceipt.Application;

public sealed record GenerateGoodsReceiptLabelLineRequest(long TaskLineId, int LabelCount = 1, decimal? QuantityPerLabel = null);
public sealed record GenerateGoodsReceiptLabelBatchRequest(Guid IdempotencyKey, long TaskId,
    IReadOnlyList<GenerateGoodsReceiptLabelLineRequest> Lines, string? Description);
public sealed record GoodsReceiptLabelBatchRow(long Id, long GoodsReceiptId, string DocumentNo, long? TaskId,
    string? TaskNo, string BatchNo, GoodsReceiptLabelBatchStatus Status, int TotalLabelCount,
    int PrintedLabelCount, int ConsumedLabelCount, int VoidLabelCount, DateTimeOffset? LastPrintedAtUtc,
    long? CreatedBy, DateTime? CreatedDate, byte[] RowVersion);
public sealed record GoodsReceiptLabelRow(long Id, long BatchId, long GoodsReceiptId, long? GoodsReceiptLineId,
    long? TaskLineId, long? StockId, string StockCode, string? StockName, string? YapCode,
    decimal Quantity, string UnitCode, string? LotNo, string? SerialNo, DateOnly? ManufacturingDate,
    DateOnly? ExpirationDate, string BarcodeValue, GoodsReceiptLabelStatus Status, int PrintCount,
    DateTimeOffset? LastPrintedAtUtc, DateTimeOffset? ConsumedAtUtc, string? VoidReason,
    long? ParentLabelId, long? RootLabelId, DateTimeOffset? SplitAtUtc, long? SplitBy,
    string? SplitReason, bool CanSplit, string? SplitBlockReason, byte[] RowVersion);
public sealed record GoodsReceiptLabelBatchDetail(GoodsReceiptLabelBatchRow Batch, IReadOnlyList<GoodsReceiptLabelRow> Labels);
public sealed record MarkGoodsReceiptLabelsPrintedRequest(IReadOnlyList<long> LabelIds);
public sealed record VoidGoodsReceiptLabelRequest(string Reason, string RowVersion);
public sealed record SplitGoodsReceiptLabelRequest(Guid IdempotencyKey, decimal SplitQuantity,
    string Reason, string RowVersion);
public sealed record SplitGoodsReceiptLabelResult(GoodsReceiptLabelRow Source,
    IReadOnlyList<GoodsReceiptLabelRow> ChildLabels, bool Replayed);

public interface IGoodsReceiptLabelService
{
    Task<GoodsReceiptLabelBatchDetail> GenerateAsync(long goodsReceiptId, GenerateGoodsReceiptLabelBatchRequest request,
        long actor, bool restrictToActorAssignment, CancellationToken ct = default);
    Task<PagedResponse<GoodsReceiptLabelBatchRow>> GetPagedAsync(PagedRequest request, CancellationToken ct = default);
    Task<GoodsReceiptLabelBatchDetail> GetAsync(long batchId, CancellationToken ct = default);
    Task<IReadOnlyList<GoodsReceiptLabelRow>> GetForReceiptAsync(long goodsReceiptId, long? lineId = null, CancellationToken ct = default);
    Task MarkPrintedAsync(MarkGoodsReceiptLabelsPrintedRequest request, long actor,
        bool restrictToActorAssignment, CancellationToken ct = default);
    Task VoidAsync(long labelId, VoidGoodsReceiptLabelRequest request, long actor, CancellationToken ct = default);
    Task<SplitGoodsReceiptLabelResult> SplitAsync(long labelId, SplitGoodsReceiptLabelRequest request,
        long actor, CancellationToken ct = default);
}
