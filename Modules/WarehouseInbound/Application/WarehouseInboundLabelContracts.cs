using verii_wms_api_v2.Modules.WarehouseInbound.Domain;
using verii_wms_api_v2.Shared;

namespace verii_wms_api_v2.Modules.WarehouseInbound.Application;

public sealed record GenerateWarehouseInboundLabelLineRequest(long TaskLineId, int LabelCount = 1, decimal? QuantityPerLabel = null);
public sealed record GenerateWarehouseInboundLabelBatchRequest(Guid IdempotencyKey, long TaskId,
    IReadOnlyList<GenerateWarehouseInboundLabelLineRequest> Lines, string? Description);
public sealed record WarehouseInboundLabelBatchRow(long Id, long WarehouseInboundId, string DocumentNo, long? TaskId,
    string? TaskNo, string BatchNo, WarehouseInboundLabelBatchStatus Status, int TotalLabelCount,
    int PrintedLabelCount, int ConsumedLabelCount, int VoidLabelCount, DateTimeOffset? LastPrintedAtUtc,
    long? CreatedBy, DateTime? CreatedDate, byte[] RowVersion);
public sealed record WarehouseInboundLabelRow(long Id, long BatchId, long WarehouseInboundId, long? WarehouseInboundLineId,
    long? TaskLineId, long? StockId, string StockCode, string? StockName, string? YapCode,
    decimal Quantity, string UnitCode, string? LotNo, string? SerialNo, DateOnly? ManufacturingDate,
    DateOnly? ExpirationDate, string BarcodeValue, WarehouseInboundLabelStatus Status, int PrintCount,
    DateTimeOffset? LastPrintedAtUtc, DateTimeOffset? ConsumedAtUtc, string? VoidReason, byte[] RowVersion);
public sealed record WarehouseInboundLabelBatchDetail(WarehouseInboundLabelBatchRow Batch, IReadOnlyList<WarehouseInboundLabelRow> Labels);
public sealed record MarkWarehouseInboundLabelsPrintedRequest(IReadOnlyList<long> LabelIds);
public sealed record VoidWarehouseInboundLabelRequest(string Reason, string RowVersion);

public interface IWarehouseInboundLabelService
{
    Task<WarehouseInboundLabelBatchDetail> GenerateAsync(long goodsReceiptId, GenerateWarehouseInboundLabelBatchRequest request, long actor, CancellationToken ct = default);
    Task<PagedResponse<WarehouseInboundLabelBatchRow>> GetPagedAsync(PagedRequest request, CancellationToken ct = default);
    Task<WarehouseInboundLabelBatchDetail> GetAsync(long batchId, CancellationToken ct = default);
    Task<IReadOnlyList<WarehouseInboundLabelRow>> GetForReceiptAsync(long goodsReceiptId, long? lineId = null, CancellationToken ct = default);
    Task MarkPrintedAsync(MarkWarehouseInboundLabelsPrintedRequest request, long actor, CancellationToken ct = default);
    Task VoidAsync(long labelId, VoidWarehouseInboundLabelRequest request, long actor, CancellationToken ct = default);
}
