using Microsoft.EntityFrameworkCore;
using verii_wms_api_v2.Modules.BarcodeDesigner.Application;
using verii_wms_api_v2.Modules.BarcodeDesigner.Domain;
using verii_wms_api_v2.Modules.GoodsReceipt.Domain;
using verii_wms_api_v2.Shared.Application.Abstractions.Persistence;
using WarehouseEntity = verii_wms_api_v2.Modules.Warehouse.Domain.Warehouse;

namespace verii_wms_api_v2.Modules.GoodsReceipt.Application;

public interface IGoodsReceiptOnReceiptLabelService
{
    Task<IReadOnlyList<long>> GenerateForExecutionAsync(
        GoodsReceiptHeader header,
        GoodsReceiptExecution execution,
        IReadOnlyCollection<GoodsReceiptExecutionLine> lines,
        long actor,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Generates labels whose quantity and tracking values become final during receipt.
/// The caller owns the transaction, so acceptance and its labels commit atomically.
/// </summary>
public sealed class GoodsReceiptOnReceiptLabelService(
    IUnitOfWork unitOfWork,
    IBarcodePolicyService barcodePolicy) : IGoodsReceiptOnReceiptLabelService
{
    public async Task<IReadOnlyList<long>> GenerateForExecutionAsync(
        GoodsReceiptHeader header,
        GoodsReceiptExecution execution,
        IReadOnlyCollection<GoodsReceiptExecutionLine> lines,
        long actor,
        CancellationToken cancellationToken = default)
    {
        if (header.LabelStrategy != GoodsReceiptLabelStrategy.GenerateOnReceipt)
            return [];

        var existing = await unitOfWork.Repository<GoodsReceiptLabelBatch>().Query()
            .Include(x => x.Labels)
            .FirstOrDefaultAsync(x => x.CorrelationId == execution.IdempotencyKey, cancellationToken);
        if (existing is not null)
            return existing.Labels.OrderBy(x => x.Id).Select(x => x.Id).ToArray();

        var warehouseIds = lines.Select(x => x.WarehouseId).Distinct().ToArray();
        var warehouseCodes = await unitOfWork.Repository<WarehouseEntity>().Query()
            .Where(x => warehouseIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.WarehouseCode.ToString(), cancellationToken);
        var taskLineByReceiptLine = execution.GrTaskId.HasValue
            ? await unitOfWork.Repository<GoodsReceiptTaskLine>().Query()
                .Where(x => x.GrTaskId == execution.GrTaskId.Value)
                .ToDictionaryAsync(x => x.GrLineId, x => x.Id, cancellationToken)
            : [];

        var batch = Stamp(new GoodsReceiptLabelBatch
        {
            BranchCode = header.BranchCode,
            GrHeaderId = header.Id,
            CorrelationId = execution.IdempotencyKey,
            BatchNo = BatchNo(header.DocumentNo, execution.IdempotencyKey),
            Status = GoodsReceiptLabelBatchStatus.Generated,
            Description = "Mal kabul sırasında otomatik oluşturulan etiketler"
        }, actor);

        var sequence = 0;
        foreach (var line in lines.OrderBy(x => x.LineNo))
        {
            var sourceLine = line.Line;
            var taskLineId = taskLineByReceiptLine.TryGetValue(line.GrLineId, out var linkedTaskLineId)
                ? linkedTaskLineId
                : (long?)null;
            var scope = !string.IsNullOrWhiteSpace(line.SerialNo)
                ? BarcodePolicyScope.ProductSerial
                : !string.IsNullOrWhiteSpace(line.LotNo)
                    ? BarcodePolicyScope.ProductLot
                    : BarcodePolicyScope.Logistics;
            var barcode = await barcodePolicy.GenerateAsync(scope, new BarcodeGenerateRequest(
                $"GR-ON-RECEIPT:{execution.IdempotencyKey:N}:{++sequence}",
                sourceLine.StockCodeSnapshot,
                line.SerialNo,
                sourceLine.YapCodeSnapshot,
                line.LotNo,
                warehouseCodes.GetValueOrDefault(line.WarehouseId),
                null,
                header.DocumentNo), cancellationToken);

            batch.Labels.Add(Stamp(new GoodsReceiptLabel
            {
                BranchCode = header.BranchCode,
                GrHeaderId = header.Id,
                GrLineId = line.GrLineId,
                GrTaskLineId = taskLineId,
                StockId = line.StockId,
                StockCodeSnapshot = sourceLine.StockCodeSnapshot,
                StockNameSnapshot = sourceLine.StockNameSnapshot,
                YapCodeId = line.YapCodeId,
                YapCodeSnapshot = sourceLine.YapCodeSnapshot,
                LabelQuantity = line.Quantity,
                UnitCode = line.UnitCode,
                LotNo = line.LotNo,
                SerialNo = line.SerialNo,
                ManufacturingDate = line.ManufacturingDate,
                ExpirationDate = line.ExpirationDate,
                BarcodeValue = barcode.Value,
                Status = GoodsReceiptLabelStatus.Generated,
                Description = "Mal kabul sonrası ürün etiketi"
            }, actor));
        }

        batch.TotalLabelCount = batch.Labels.Count;
        await unitOfWork.Repository<GoodsReceiptLabelBatch>().AddAsync(batch, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return batch.Labels.OrderBy(x => x.Id).Select(x => x.Id).ToArray();
    }

    private static string BatchNo(string documentNo, Guid key)
    {
        var suffix = key.ToString("N")[..8].ToUpperInvariant();
        var prefix = documentNo.Length > 36 ? documentNo[..36] : documentNo;
        return $"{prefix}-AR-{suffix}";
    }

    private static T Stamp<T>(T entity, long actor) where T : Shared.Domain.BaseEntity
    {
        entity.CreatedBy = actor;
        entity.CreatedDate = DateTime.UtcNow;
        return entity;
    }
}
