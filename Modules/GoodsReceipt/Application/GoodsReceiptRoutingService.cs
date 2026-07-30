using System.Data;
using Microsoft.EntityFrameworkCore;
using verii_wms_api_v2.Modules.Audit.Application;
using verii_wms_api_v2.Modules.GoodsReceipt.Domain;
using verii_wms_api_v2.Modules.WarehouseOperations.Domain;
using verii_wms_api_v2.Modules.WarehouseOutbound.Application;
using verii_wms_api_v2.Modules.WarehouseOutbound.Domain;
using verii_wms_api_v2.Modules.WarehouseTransfer.Application;
using verii_wms_api_v2.Modules.WarehouseTransfer.Domain;
using verii_wms_api_v2.Shared.Application.Abstractions.Persistence;
using verii_wms_api_v2.Shared.Application.Exceptions;

namespace verii_wms_api_v2.Modules.GoodsReceipt.Application;

public sealed class GoodsReceiptRoutingService(
    IUnitOfWork uow,
    IWarehouseTransferService transfers,
    IWarehouseOutboundService outbounds,
    IAuditLogWriter audit) : IGoodsReceiptRoutingService
{
    private IGenericRepository<GoodsReceiptRoutingBatch> Batches => uow.Repository<GoodsReceiptRoutingBatch>();

    public async Task<IReadOnlyDictionary<long, decimal>> GetActiveAllocatedQuantitiesAsync(
        IReadOnlyCollection<long> goodsReceiptLineIds,
        CancellationToken cancellationToken = default) =>
        await GetActiveAllocatedQuantitiesCoreAsync(goodsReceiptLineIds.Distinct().ToArray(), cancellationToken);

    public Task<GoodsReceiptRoutingResult> CreateTransferAsync(
        long goodsReceiptId,
        CreateGoodsReceiptTransferRequest request,
        long actor,
        CancellationToken cancellationToken = default) =>
        uow.ExecuteInTransactionAsync(async ct =>
        {
            var replay = await ReplayAsync(request.IdempotencyKey, ct);
            if (replay is not null) return replay;

            var context = await PrepareAsync(goodsReceiptId, request.Lines, ct);
            if (request.TargetWarehouseId == context.SourceWarehouseId)
                throw AppException.BadRequest("Kaynak ve hedef depo aynı olamaz.");

            var result = await transfers.CreateDraftAsync(new(
                request.IdempotencyKey,
                context.Header.BranchCode,
                request.DocumentSeriesId,
                DateOnly.FromDateTime(DateTime.UtcNow),
                WarehouseTransferInitiationMode.DirectTransfer,
                WarehouseTransferProcessType.Direct,
                context.SourceWarehouseId,
                request.TargetWarehouseId,
                null,
                request.TargetReceivingLocationId,
                request.TargetPutawayLocationId,
                null,
                null,
                NormalizePriority(request.Priority),
                $"GR:{context.Header.DocumentNo}",
                Clean(request.Description),
                context.Lines.Select(x => new WarehouseTransferLineDraftRequest(
                    x.Line.StockId,
                    x.Line.YapCodeId,
                    x.Request.Quantity,
                    x.Line.UnitCode,
                    x.Line.TrackingType,
                    x.Line.RequireHandlingUnit,
                    ResolveSourceLocation(x.Line, x.Request),
                    request.TargetPutawayLocationId ?? request.TargetReceivingLocationId,
                    $"Mal kabul {context.Header.DocumentNo} / satır {x.Line.LineNo}",
                    null,
                    null)).ToArray(),
                null), actor, ct);

            var targetLines = await uow.Repository<WarehouseTransferLine>().Query()
                .Where(x => x.WtHeaderId == result.Id).OrderBy(x => x.LineNo).ToListAsync(ct);
            return await PersistAsync(context, request.IdempotencyKey, GoodsReceiptRouteType.WarehouseTransfer,
                result.Id, result.DocumentNo, targetLines.Select(x => x.Id).ToArray(), request.Description, actor, ct);
        }, cancellationToken, IsolationLevel.Serializable);

    public Task<GoodsReceiptRoutingResult> CreateOutboundAsync(
        long goodsReceiptId,
        CreateGoodsReceiptOutboundRequest request,
        long actor,
        CancellationToken cancellationToken = default) =>
        uow.ExecuteInTransactionAsync(async ct =>
        {
            var replay = await ReplayAsync(request.IdempotencyKey, ct);
            if (replay is not null) return replay;

            var context = await PrepareAsync(goodsReceiptId, request.Lines, ct);
            var result = await outbounds.CreateDraftAsync(new(
                request.IdempotencyKey,
                context.Header.BranchCode,
                request.DocumentSeriesId,
                DateOnly.FromDateTime(DateTime.UtcNow),
                WarehouseOutboundInitiationMode.StockBasedDirect,
                request.CustomerId,
                context.SourceWarehouseId,
                request.StagingLocationId,
                request.LoadingLocationId,
                null,
                NormalizePriority(request.Priority),
                $"GR:{context.Header.DocumentNo}",
                false,
                null, null, null, null, null, null,
                Clean(request.Description),
                context.Lines.Select(x => new WarehouseOutboundLineRequest(
                    x.Line.StockId,
                    x.Line.YapCodeId,
                    x.Request.Quantity,
                    x.Line.UnitCode,
                    x.Line.TrackingType,
                    x.Line.RequireHandlingUnit,
                    ResolveSourceLocation(x.Line, x.Request),
                    $"Mal kabul {context.Header.DocumentNo} / satır {x.Line.LineNo}",
                    null,
                    null)).ToArray(),
                null), actor, ct);

            var targetLines = await uow.Repository<WarehouseOutboundLine>().Query()
                .Where(x => x.WarehouseOutboundHeaderId == result.Id).OrderBy(x => x.LineNo).ToListAsync(ct);
            return await PersistAsync(context, request.IdempotencyKey, GoodsReceiptRouteType.WarehouseOutbound,
                result.Id, result.DocumentNo, targetLines.Select(x => x.Id).ToArray(), request.Description, actor, ct);
        }, cancellationToken, IsolationLevel.Serializable);

    public Task<GoodsReceiptSplitRoutingResult> CreateSplitAsync(
        long goodsReceiptId,
        CreateGoodsReceiptSplitRoutingRequest request,
        long actor,
        CancellationToken cancellationToken = default)
    {
        if (request.Transfer is null && request.Outbound is null)
            throw AppException.BadRequest("En az bir transfer veya ambar çıkış dağıtımı girilmelidir.");
        if (request.Transfer is not null && request.Outbound is not null
            && request.Transfer.IdempotencyKey == request.Outbound.IdempotencyKey)
            throw AppException.BadRequest("Transfer ve ambar çıkış işlemleri farklı idempotency anahtarları kullanmalıdır.");

        return uow.ExecuteInTransactionAsync(async ct =>
        {
            var results = new List<GoodsReceiptRoutingResult>(2);
            if (request.Transfer is not null)
                results.Add(await CreateTransferAsync(goodsReceiptId, request.Transfer, actor, ct));
            if (request.Outbound is not null)
                results.Add(await CreateOutboundAsync(goodsReceiptId, request.Outbound, actor, ct));
            return new GoodsReceiptSplitRoutingResult(results, results.Sum(x => x.RoutedQuantity));
        }, cancellationToken, IsolationLevel.Serializable);
    }

    private async Task<RoutingContext> PrepareAsync(
        long goodsReceiptId,
        IReadOnlyList<GoodsReceiptRoutingLineRequest> requests,
        CancellationToken ct)
    {
        if (requests.Count == 0) throw AppException.BadRequest("En az bir mal kabul kalemi seçilmelidir.");
        if (requests.Any(x => x.Quantity <= 0)) throw AppException.BadRequest("Yönlendirilecek miktarlar sıfırdan büyük olmalıdır.");
        if (requests.Select(x => x.GoodsReceiptLineId).Distinct().Count() != requests.Count)
            throw AppException.BadRequest("Aynı mal kabul kalemi bir istekte birden fazla gönderilemez.");

        var header = await uow.Repository<GoodsReceiptHeader>().Query()
            .FirstOrDefaultAsync(x => x.Id == goodsReceiptId, ct)
            ?? throw AppException.NotFound("Mal kabul kaydı bulunamadı.");
        if (!CanRouteAfterErpReceipt(
                header.Status,
                header.ApprovalStatus,
                header.QualityStatus,
                header.ErpIntegrationStatus))
        {
            if (header.Status != WarehouseOperationStatus.Completed)
                throw AppException.Conflict("Mal kabul tamamlanmadan DAT veya ambar çıkış oluşturulamaz.");
            if (header.ApprovalStatus is not (OperationApprovalStatus.NotRequired or OperationApprovalStatus.Approved))
                throw AppException.Conflict("Mal kabul onayı tamamlanmadan yönlendirme yapılamaz.");
            if (!CanRouteAfterQuality(header.QualityStatus))
                throw AppException.Conflict("Kalite/GKK kararı tamamlanmadan ürünler yönlendirilemez.");
            throw AppException.Conflict(
                "Mal kabul ERP irsaliyesi başarıyla oluşturulmadan DAT veya ambar çıkış oluşturulamaz.");
        }

        var ids = requests.Select(x => x.GoodsReceiptLineId).ToArray();
        var lines = await uow.Repository<GoodsReceiptLine>().Query()
            .Where(x => x.GrHeaderId == goodsReceiptId && ids.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, ct);
        if (lines.Count != ids.Length) throw AppException.BadRequest("Seçilen kalemlerden biri bu mal kabule ait değil.");

        var requestedLines = requests.Select(x => new RoutingLine(lines[x.GoodsReceiptLineId], x)).ToArray();
        var warehouses = requestedLines.Select(x => x.Line.TargetWarehouseId).Distinct().ToArray();
        if (warehouses.Length != 1)
            throw AppException.BadRequest("Tek hedef belgeye yönlendirilen kalemlerin kaynak deposu aynı olmalıdır.");

        var allocated = await GetActiveAllocatedQuantitiesCoreAsync(ids, ct);
        foreach (var item in requestedLines)
        {
            var used = allocated.GetValueOrDefault(item.Line.Id);
            var remaining = Math.Max(0, item.Line.AcceptedQuantity - used);
            if (item.Request.Quantity > remaining)
                throw AppException.Conflict(
                    $"{item.Line.StockCodeSnapshot} için kullanılabilir miktar {remaining:0.######}; istenen miktar {item.Request.Quantity:0.######}.");
        }

        return new RoutingContext(header, warehouses[0], requestedLines);
    }

    internal static bool CanRouteAfterQuality(OperationQualityStatus status) =>
        status is OperationQualityStatus.NotRequired
            or OperationQualityStatus.Passed
            or OperationQualityStatus.Failed;

    internal static bool CanRouteAfterErpReceipt(
        WarehouseOperationStatus operationStatus,
        OperationApprovalStatus approvalStatus,
        OperationQualityStatus qualityStatus,
        ErpIntegrationStatus erpStatus) =>
        operationStatus == WarehouseOperationStatus.Completed
        && approvalStatus is OperationApprovalStatus.NotRequired or OperationApprovalStatus.Approved
        && CanRouteAfterQuality(qualityStatus)
        && erpStatus == ErpIntegrationStatus.Succeeded;

    private async Task<Dictionary<long, decimal>> GetActiveAllocatedQuantitiesCoreAsync(long[] lineIds, CancellationToken ct)
    {
        var rows = await (from allocation in uow.Repository<GoodsReceiptRoutingAllocation>().Query()
                          join batch in Batches.Query() on allocation.RoutingBatchId equals batch.Id
                          where lineIds.Contains(allocation.GrLineId)
                          select new { allocation.GrLineId, allocation.Quantity, batch.RouteType, batch.TargetDocumentId })
            .ToListAsync(ct);
        if (rows.Count == 0) return [];

        var transferIds = rows.Where(x => x.RouteType == GoodsReceiptRouteType.WarehouseTransfer)
            .Select(x => x.TargetDocumentId).Distinct().ToArray();
        var outboundIds = rows.Where(x => x.RouteType == GoodsReceiptRouteType.WarehouseOutbound)
            .Select(x => x.TargetDocumentId).Distinct().ToArray();
        var liveTransfers = (await uow.Repository<WarehouseTransferHeader>().Query()
            .Where(x => transferIds.Contains(x.Id) && x.Status != WarehouseTransferStatus.Cancelled)
            .Select(x => x.Id).ToListAsync(ct)).ToHashSet();
        var liveOutbounds = (await uow.Repository<WarehouseOutboundHeader>().Query()
            .Where(x => outboundIds.Contains(x.Id) && x.Status != WarehouseOutboundStatus.Cancelled)
            .Select(x => x.Id).ToListAsync(ct)).ToHashSet();

        return rows.Where(x =>
                x.RouteType == GoodsReceiptRouteType.WarehouseTransfer
                    ? liveTransfers.Contains(x.TargetDocumentId)
                    : liveOutbounds.Contains(x.TargetDocumentId))
            .GroupBy(x => x.GrLineId)
            .ToDictionary(x => x.Key, x => x.Sum(y => y.Quantity));
    }

    private async Task<GoodsReceiptRoutingResult?> ReplayAsync(Guid correlationId, CancellationToken ct)
    {
        var batch = await Batches.Query().Include(x => x.Allocations)
            .FirstOrDefaultAsync(x => x.CorrelationId == correlationId, ct);
        return batch is null
            ? null
            : new(batch.Id, batch.RouteType, batch.TargetDocumentId, batch.TargetDocumentNo,
                batch.Allocations.Sum(x => x.Quantity), true);
    }

    private async Task<GoodsReceiptRoutingResult> PersistAsync(
        RoutingContext context,
        Guid correlationId,
        GoodsReceiptRouteType routeType,
        long targetId,
        string targetNo,
        long[] targetLineIds,
        string? description,
        long actor,
        CancellationToken ct)
    {
        if (targetLineIds.Length != context.Lines.Length)
            throw AppException.Conflict("Hedef belge kalemleri oluşturulamadı; işlem geri alındı.");
        var now = DateTime.UtcNow;
        var batch = new GoodsReceiptRoutingBatch
        {
            BranchCode = context.Header.BranchCode,
            CreatedBy = actor,
            CreatedDate = now,
            GrHeaderId = context.Header.Id,
            RouteType = routeType,
            CorrelationId = correlationId,
            TargetDocumentId = targetId,
            TargetDocumentNo = targetNo,
            RoutedAtUtc = DateTimeOffset.UtcNow,
            RoutedBy = actor,
            Description = Clean(description)
        };
        for (var i = 0; i < context.Lines.Length; i++)
            batch.Allocations.Add(new GoodsReceiptRoutingAllocation
            {
                BranchCode = context.Header.BranchCode,
                CreatedBy = actor,
                CreatedDate = now,
                GrLineId = context.Lines[i].Line.Id,
                TargetDocumentLineId = targetLineIds[i],
                Quantity = context.Lines[i].Request.Quantity
            });
        await Batches.AddAsync(batch, ct);
        await uow.SaveChangesAsync(ct);
        await audit.WriteAsync(new("goods-receipt.route", nameof(GoodsReceiptHeader), context.Header.Id.ToString(),
            "Succeeded", "goods-receipt", NewValues: new
            {
                routeType,
                targetId,
                targetNo,
                quantity = batch.Allocations.Sum(x => x.Quantity)
            }, ChangedFields: ["Routing"]), ct);
        return new(batch.Id, routeType, targetId, targetNo, batch.Allocations.Sum(x => x.Quantity), false);
    }

    private static long? ResolveSourceLocation(GoodsReceiptLine line, GoodsReceiptRoutingLineRequest request) =>
        request.SourceLocationId ?? line.DefaultPutawayLocationId ?? line.DefaultReceivingLocationId;
    private static byte NormalizePriority(byte value) => value is >= 1 and <= 5 ? value : (byte)3;
    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed record RoutingLine(GoodsReceiptLine Line, GoodsReceiptRoutingLineRequest Request);
    private sealed record RoutingContext(GoodsReceiptHeader Header, long SourceWarehouseId, RoutingLine[] Lines);
}
