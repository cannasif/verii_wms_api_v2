using System.Data;
using Microsoft.EntityFrameworkCore;
using verii_wms_api_v2.Modules.Audit.Application;
using verii_wms_api_v2.Modules.BarcodeDesigner.Application;
using verii_wms_api_v2.Modules.Location.Domain;
using verii_wms_api_v2.Modules.ProductionTransfer.Domain;
using verii_wms_api_v2.Modules.StockMovement.Application;
using verii_wms_api_v2.Modules.StockMovement.Domain;
using verii_wms_api_v2.Modules.StockTracking.Application;
using verii_wms_api_v2.Modules.WarehouseTransfer.Application;
using verii_wms_api_v2.Modules.WarehouseTransfer.Domain;
using verii_wms_api_v2.Shared.Application.Abstractions.Persistence;
using verii_wms_api_v2.Shared.Application.Exceptions;
using WarehouseEntity = verii_wms_api_v2.Modules.Warehouse.Domain.Warehouse;

namespace verii_wms_api_v2.Modules.ProductionTransfer.Application;

public sealed class ProductionTransferExecutionService(
    IUnitOfWork uow,
    IWarehouseBarcodeResolver barcodeResolver,
    IWarehouseTransferOperationService operations,
    IWarehouseTransferService transfers,
    IWarehouseTransferReservationService reservations,
    IStockMovementService stockMovements,
    IStockTrackingPolicyResolver trackingPolicies,
    IAuditLogWriter audit) : IProductionTransferExecutionService
{
    private static readonly WarehouseTransferBusinessContext[] Contexts =
    [
        WarehouseTransferBusinessContext.ProductionMaterialSupply,
        WarehouseTransferBusinessContext.ProductionWipMove,
        WarehouseTransferBusinessContext.ProductionOutputMove
    ];

    public async Task<ProductionTransferExecutionDto> GetAsync(long transferId, CancellationToken ct = default)
    {
        var aggregate = await LoadAsync(transferId, false, ct);
        return await MapAsync(aggregate.Header, aggregate.Link, ct);
    }

    public Task<ProductionTransferScanPickResult> ScanPickAsync(
        long transferId,
        ProductionTransferScanPickRequest request,
        long actor,
        CancellationToken ct = default) =>
        uow.ExecuteInTransactionAsync(
            token => ScanPickCoreAsync(transferId, request, actor, token),
            ct,
            IsolationLevel.Serializable);

    private async Task<ProductionTransferScanPickResult> ScanPickCoreAsync(
        long transferId,
        ProductionTransferScanPickRequest request,
        long actor,
        CancellationToken ct = default)
    {
        if (request.IdempotencyKey == Guid.Empty) throw AppException.BadRequest("İşlem anahtarı zorunludur.");
        if (string.IsNullOrWhiteSpace(request.Barcode)) throw AppException.BadRequest("Barkod zorunludur.");
        var normalizedBarcode = request.Barcode.Trim().ToUpperInvariant();

        var replay = await uow.Repository<ProductionTransferBarcodeScan>().Query()
            .FirstOrDefaultAsync(x => x.IdempotencyKey == request.IdempotencyKey, ct);
        if (replay is not null)
        {
            var replayHeaderLink = await uow.Repository<ProductionTransferHeaderLink>().Query()
                .SingleAsync(x => x.Id == replay.ProductionTransferHeaderLinkId, ct);
            var replayLineLink = await uow.Repository<ProductionTransferLineLink>().Query()
                .SingleAsync(x => x.Id == replay.ProductionTransferLineLinkId, ct);
            if (replayHeaderLink.WarehouseTransferHeaderId != transferId
                || replayLineLink.WarehouseTransferLineId != request.ExpectedLineId
                || replay.NormalizedBarcode != normalizedBarcode)
                throw AppException.Conflict("Aynı işlem anahtarı farklı bir barkod toplama isteğinde kullanılamaz.");
            var replayLocation = await uow.Repository<WarehouseLocation>().Query()
                .SingleAsync(x => x.Id == replay.SourceLocationId, ct);
            var replayExecution = await GetAsync(transferId, ct);
            var replayLine = replayExecution.Lines.Single(x => x.LineId == request.ExpectedLineId);
            return new(replayExecution, replayLine.LineId, replayLine.StockCode, replay.Quantity,
                replay.SerialNo, replay.LotNo, replay.BarcodeSource, replay.SourceLocationId,
                replayLocation.Code, replayLocation.Name, null);
        }

        var aggregate = await LoadAsync(transferId, false, ct);
        EnsurePickingAllowed(aggregate.Link);
        var line = aggregate.Header.Lines.SingleOrDefault(x => x.Id == request.ExpectedLineId)
            ?? throw AppException.BadRequest("Beklenen toplama kalemi bu üretim transferine ait değil.");
        var lineLink = aggregate.Link.Lines.Single(x => x.WarehouseTransferLineId == line.Id);
        var remaining = line.RequestedQuantity - line.PickedQuantity;
        if (remaining <= 0) throw AppException.Conflict("Seçilen stok kalemi daha önce tamamen toplandı.");
        var waitingLocationId = aggregate.Header.SourceStagingLocationId
            ?? throw AppException.Conflict("Kaynak depo için üretim transfer bekleme rafı tanımlanmamış.");

        var resolved = await barcodeResolver.ResolveAsync(new(
            request.Barcode.Trim(),
            aggregate.Header.BranchCode,
            WarehouseBarcodePurpose.Outbound,
            aggregate.Header.SourceWarehouseId,
            line.StockId), ct);
        if (!resolved.CanExecute || resolved.MissingFields.Count > 0)
            throw AppException.Conflict($"Barkod toplama için uygun değil: {string.Join(", ", resolved.MissingFields)}.");
        ValidateDimensions(line, resolved);

        var sourceLocationId = ResolveSourceLocation(request.SourceLocationId, line.DefaultSourceLocationId, resolved);
        var sourceBalance = resolved.BalanceCandidates.FirstOrDefault(x => x.LocationId == sourceLocationId)
            ?? throw AppException.Conflict("Seçilen kaynak rafta okutulan barkoda ait kullanılabilir stok bulunamadı.");
        ValidateSourceBalance(line, resolved, sourceBalance);

        var quantityBound = ProductionTransferBarcodePickPolicy.IsQuantityBoundSource(resolved.Source);
        var alreadyAccepted = quantityBound
            ? await uow.Repository<ProductionTransferBarcodeScan>().Query()
                .Where(x => x.ProductionTransferHeaderLinkId == aggregate.Link.Id
                    && x.NormalizedBarcode == normalizedBarcode)
                .SumAsync(x => (decimal?)x.Quantity, ct) ?? 0
            : 0;
        var policy = await trackingPolicies.ResolveAsync(aggregate.Header.BranchCode, line.StockId, ct);
        var quantity = ProductionTransferBarcodePickPolicy.CalculateQuantity(
            policy, resolved.Quantity, alreadyAccepted, remaining, sourceBalance.AvailableQuantity, quantityBound);
        if (quantity <= 0) throw AppException.Conflict("Okutulan barkod için toplanabilir miktar bulunamadı.");
        try
        {
            StockTrackingPolicyGuard.ValidateSerialMovementQuantity(
                policy, quantity, sourceBalance.AvailableQuantity, resolved.SerialNo);
        }
        catch (StockTrackingPolicyViolationException exception)
        {
            throw AppException.Conflict(exception.Message);
        }

        await operations.PickAsync(transferId, new(
            request.IdempotencyKey,
            [new(line.Id, quantity, sourceLocationId, waitingLocationId, resolved.LotNo, resolved.SerialNo)],
            DateTimeOffset.UtcNow,
            $"Barkodlu üretim toplama: {resolved.RawBarcode}",
            null, null, null), actor, ct);

        await uow.ExecuteInTransactionAsync(async token =>
        {
            var link = await uow.Repository<ProductionTransferHeaderLink>().Query(true)
                .SingleAsync(x => x.WarehouseTransferHeaderId == transferId, token);
            if (link.WorkflowStatus == ProductionTransferWorkflowStatus.Planned)
                link.WorkflowStatus = ProductionTransferWorkflowStatus.Picking;
            link.UpdatedBy = actor;
            link.UpdatedDate = DateTime.UtcNow;
            await uow.Repository<ProductionTransferBarcodeScan>().AddAsync(new()
            {
                BranchCode = aggregate.Header.BranchCode,
                CreatedBy = actor,
                CreatedDate = DateTime.UtcNow,
                ProductionTransferHeaderLinkId = aggregate.Link.Id,
                ProductionTransferLineLinkId = lineLink.Id,
                IdempotencyKey = request.IdempotencyKey,
                BarcodeValue = resolved.RawBarcode,
                NormalizedBarcode = normalizedBarcode,
                BarcodeSource = resolved.Source,
                StockId = resolved.StockId,
                YapCodeId = resolved.YapCodeId,
                UnitCode = sourceBalance.UnitCode,
                LotNo = resolved.LotNo,
                SerialNo = resolved.SerialNo,
                Quantity = quantity,
                SourceLocationId = sourceLocationId,
                TargetLocationId = waitingLocationId,
                ScannedAtUtc = DateTimeOffset.UtcNow
            }, token);
            await uow.SaveChangesAsync(token);
            return true;
        }, ct);

        var execution = await GetAsync(transferId, ct);
        decimal? remainingBarcodeQuantity = quantityBound
            ? Math.Max(0, (resolved.Quantity ?? 0) - alreadyAccepted - quantity)
            : null;
        return new(execution, line.Id, line.StockCodeSnapshot, quantity, resolved.SerialNo, resolved.LotNo,
            resolved.Source, sourceLocationId, sourceBalance.LocationCode, sourceBalance.LocationName,
            remainingBarcodeQuantity);
    }

    public Task<ProductionTransferExecutionDto> CompletePickingAsync(
        long transferId,
        CompleteProductionPickingRequest request,
        long actor,
        CancellationToken ct = default) =>
        uow.ExecuteInTransactionAsync(async token =>
        {
            if (request.IdempotencyKey == Guid.Empty) throw AppException.BadRequest("İşlem anahtarı zorunludur.");
            var aggregate = await LoadAsync(transferId, true, token);
            if (aggregate.Link.LastPickingCompletionIdempotencyKey == request.IdempotencyKey
                || aggregate.Link.WorkflowStatus == ProductionTransferWorkflowStatus.AwaitingHandover)
                return await MapAsync(aggregate.Header, aggregate.Link, token);
            EnsurePickingAllowed(aggregate.Link);

            var picked = aggregate.Header.Lines.Sum(x => x.PickedQuantity);
            var requested = aggregate.Header.Lines.Sum(x => x.RequestedQuantity);
            if (picked <= 0) throw AppException.Conflict("Teslim beklemeye alınacak toplanmış stok bulunmuyor.");
            if (picked < requested)
            {
                if (!request.ConfirmPartialPicking)
                    throw AppException.Conflict("Toplama eksik. Eksik toplamayı bilinçli olarak onaylamadan devam edemezsiniz.");
                if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length < 5)
                    throw AppException.BadRequest("Eksik toplama nedeni en az 5 karakter olmalıdır.");
            }

            aggregate.Link.WorkflowStatus = ProductionTransferWorkflowStatus.AwaitingHandover;
            aggregate.Link.LastPickingCompletionIdempotencyKey = request.IdempotencyKey;
            aggregate.Link.UpdatedBy = actor;
            aggregate.Link.UpdatedDate = DateTime.UtcNow;
            aggregate.Header.Status = WarehouseTransferStatus.AwaitingHandover;
            aggregate.Header.UpdatedBy = actor;
            aggregate.Header.UpdatedDate = DateTime.UtcNow;
            AddHistory(aggregate.Header, WarehouseTransferStatus.AwaitingHandover, request.IdempotencyKey, request.Reason, actor);
            await uow.SaveChangesAsync(token);
            await audit.WriteAsync(new("production-transfer.picking.complete", nameof(ProductionTransferHeaderLink), aggregate.Link.Id.ToString(),
                "Succeeded", "production-transfer", NewValues: new { aggregate.Header.DocumentNo, picked, requested },
                ChangedFields: ["WorkflowStatus", "TransferStatus"]), token);
            return await MapAsync(aggregate.Header, aggregate.Link, token);
        }, ct, IsolationLevel.Serializable);

    public Task<ProductionTransferExecutionDto> ConfirmHandoverAsync(
        long transferId,
        ConfirmProductionHandoverRequest request,
        long actor,
        bool canOverrideRequester,
        CancellationToken ct = default) =>
        uow.ExecuteInTransactionAsync(async token =>
        {
            if (request.IdempotencyKey == Guid.Empty) throw AppException.BadRequest("İşlem anahtarı zorunludur.");
            var aggregate = await LoadAsync(transferId, true, token);
            if (aggregate.Link.LastHandoverIdempotencyKey == request.IdempotencyKey)
                return await MapAsync(aggregate.Header, aggregate.Link, token);
            if (aggregate.Link.WorkflowStatus != ProductionTransferWorkflowStatus.AwaitingHandover)
                throw AppException.Conflict("Transfer teslim onayı beklemiyor.");
            if (aggregate.Link.RequestedByUserId.HasValue
                && aggregate.Link.RequestedByUserId.Value != actor
                && !canOverrideRequester)
                throw AppException.Forbidden("Fiziksel teslimi yalnızca emri isteyen kişi veya üretim transferi onay yetkisine sahip bir yönetici onaylayabilir.");

            var picked = aggregate.Header.Lines.Sum(x => x.PickedQuantity);
            var requested = aggregate.Header.Lines.Sum(x => x.RequestedQuantity);
            var shortage = Math.Max(0, requested - picked);
            if (picked <= 0) throw AppException.Conflict("Teslim edilecek toplanmış stok bulunmuyor.");
            if (shortage > 0)
            {
                if (!request.ConfirmShortage)
                    throw AppException.Conflict("Transfer eksik. Eksik teslim uyarısını onaylamadan işlem tamamlanamaz.");
                if (string.IsNullOrWhiteSpace(request.ShortageReason) || request.ShortageReason.Trim().Length < 5)
                    throw AppException.BadRequest("Eksik teslim nedeni en az 5 karakter olmalıdır.");
            }

            var movementRows = BuildHandoverMovementRows(aggregate.Header);
            if (movementRows.Count > 0)
                await stockMovements.PostAsync(new(
                    $"PT:{transferId}:HANDOVER:{request.IdempotencyKey:N}",
                    StockMovementTypes.Transfer,
                    "ProductionTransferHandover",
                    aggregate.Header.DocumentNo,
                    aggregate.Header.Id,
                    DateTime.UtcNow,
                    Clean(request.ShortageReason, 1000),
                    $"Üretim transferi fiziksel teslim onayı: {aggregate.Header.DocumentNo}",
                    movementRows), token);

            var now = DateTimeOffset.UtcNow;
            foreach (var line in aggregate.Header.Lines)
            {
                var delivered = line.PickedQuantity;
                var lineShortage = Math.Max(0, line.RequestedQuantity - delivered);
                line.ShippedQuantity = delivered;
                line.ReceivedQuantity = delivered;
                line.PutawayQuantity = delivered;
                line.ShortClosedQuantity = lineShortage;
                line.Status = lineShortage > 0 ? WarehouseTransferLineStatus.ShortClosed : WarehouseTransferLineStatus.Putaway;
                line.UpdatedBy = actor;
                line.UpdatedDate = DateTime.UtcNow;
                foreach (var tracking in line.Trackings)
                {
                    tracking.ShippedQuantity = tracking.PickedQuantity;
                    tracking.ReceivedQuantity = tracking.PickedQuantity;
                    tracking.PutawayQuantity = tracking.PickedQuantity;
                    tracking.Status = WarehouseTransferTrackingStatus.Putaway;
                    tracking.UpdatedBy = actor;
                    tracking.UpdatedDate = DateTime.UtcNow;
                }
                var lineLink = aggregate.Link.Lines.Single(x => x.WarehouseTransferLineId == line.Id);
                lineLink.HandedOverQuantity = delivered;
                lineLink.ShortClosedQuantity = lineShortage;
                lineLink.UpdatedBy = actor;
                lineLink.UpdatedDate = DateTime.UtcNow;
            }

            await reservations.ReleaseAllAsync(aggregate.Header,
                $"PT:{transferId}:RESERVE:HANDOVER:{request.IdempotencyKey:N}",
                shortage > 0 ? "Eksik üretim teslimi sonrası kalan rezervasyonlar çözüldü." : "Üretim teslimi tamamlandı.",
                actor, token);

            foreach (var task in aggregate.Header.Tasks.Where(x => x.TaskType == WarehouseTransferTaskType.Pick
                         && x.Status is not (WarehouseTransferTaskStatus.Completed or WarehouseTransferTaskStatus.Cancelled)))
            {
                foreach (var taskLine in task.Lines) taskLine.PlannedQuantity = taskLine.ProcessedQuantity;
                task.Status = WarehouseTransferTaskStatus.Completed;
                task.CompletedAtUtc = now;
                task.CompletedBy = actor;
                task.UpdatedBy = actor;
                task.UpdatedDate = DateTime.UtcNow;
            }

            if (shortage > 0)
                aggregate.Link.ResidualWarehouseTransferHeaderId = await CreateResidualTransferAsync(aggregate.Header, aggregate.Link, request, actor, token);

            aggregate.Link.WorkflowStatus = shortage > 0
                ? ProductionTransferWorkflowStatus.CompletedWithShortage
                : ProductionTransferWorkflowStatus.Completed;
            aggregate.Link.HandoverConfirmedBy = actor;
            aggregate.Link.HandoverConfirmedAtUtc = now;
            aggregate.Link.HandoverShortageReason = shortage > 0 ? Clean(request.ShortageReason, 1000) : null;
            aggregate.Link.LastHandoverIdempotencyKey = request.IdempotencyKey;
            aggregate.Link.UpdatedBy = actor;
            aggregate.Link.UpdatedDate = DateTime.UtcNow;

            aggregate.Header.Status = shortage > 0 ? WarehouseTransferStatus.CompletedWithShortage : WarehouseTransferStatus.Completed;
            aggregate.Header.ShippedAtUtc = now;
            aggregate.Header.ShippedBy = actor;
            aggregate.Header.ReceivedAtUtc = now;
            aggregate.Header.ReceivedBy = actor;
            aggregate.Header.CompletedAtUtc = now;
            aggregate.Header.CompletedBy = actor;
            aggregate.Header.UpdatedBy = actor;
            aggregate.Header.UpdatedDate = DateTime.UtcNow;
            AddHistory(aggregate.Header, aggregate.Header.Status, request.IdempotencyKey, request.ShortageReason, actor);
            await uow.SaveChangesAsync(token);
            await audit.WriteAsync(new("production-transfer.handover.confirm", nameof(ProductionTransferHeaderLink), aggregate.Link.Id.ToString(),
                "Succeeded", "production-transfer", NewValues: new
                {
                    aggregate.Header.DocumentNo, requested, delivered = picked, shortage,
                    aggregate.Link.ResidualWarehouseTransferHeaderId
                }, ChangedFields: ["WorkflowStatus", "Handover", "LineQuantities", "ResidualTransfer"]), token);
            return await MapAsync(aggregate.Header, aggregate.Link, token);
        }, ct, IsolationLevel.Serializable);

    private async Task<long> CreateResidualTransferAsync(
        WarehouseTransferHeader original,
        ProductionTransferHeaderLink originalLink,
        ConfirmProductionHandoverRequest handover,
        long actor,
        CancellationToken ct)
    {
        var residualLines = original.Lines
            .Where(x => x.RequestedQuantity > x.PickedQuantity)
            .OrderBy(x => x.LineNo)
            .ToArray();
        var draftLines = residualLines.Select(line => new WarehouseTransferLineDraftRequest(
            line.StockId,
            line.YapCodeId,
            line.RequestedQuantity - line.PickedQuantity,
            line.UnitCode,
            line.TrackingType,
            line.RequireHandlingUnit,
            line.DefaultSourceLocationId,
            line.DefaultTargetLocationId,
            $"{original.DocumentNo} eksik tesliminden kalan miktar",
            BuildResidualTrackings(line),
            null,
            line.SourceStockStatus,
            line.TargetStockStatus)).ToArray();

        var result = await transfers.CreateDraftAsync(new(
            Guid.NewGuid(), original.BranchCode, original.DocumentSeriesId, DateOnly.FromDateTime(DateTime.UtcNow),
            original.InitiationMode, original.ProcessType, original.SourceWarehouseId, original.TargetWarehouseId,
            original.SourceStagingLocationId, original.TargetReceivingLocationId, original.TargetPutawayLocationId,
            original.PlannedDispatchAtUtc, original.PlannedArrivalAtUtc, original.Priority,
            $"KALAN:{original.DocumentNo}",
            $"{original.DocumentNo} eksik tesliminden otomatik oluşturulan kalan iş emri. {Clean(handover.ShortageReason, 500)}",
            draftLines, null, original.BusinessContext, original.ProjectCode), actor, ct);

        var residualHeader = await uow.Repository<WarehouseTransferHeader>().Query(true)
            .Include(x => x.Lines).SingleAsync(x => x.Id == result.Id, ct);
        var childLink = new ProductionTransferHeaderLink
        {
            BranchCode = original.BranchCode,
            CreatedBy = actor,
            CreatedDate = DateTime.UtcNow,
            WarehouseTransferHeader = residualHeader,
            Purpose = originalLink.Purpose,
            ProductionHeaderId = originalLink.ProductionHeaderId,
            ProductionOrderId = originalLink.ProductionOrderId,
            ProductionOperationId = originalLink.ProductionOperationId,
            ProductionPlanNo = originalLink.ProductionPlanNo,
            ProductionOrderNo = originalLink.ProductionOrderNo,
            ProductionOperationCode = originalLink.ProductionOperationCode,
            SourceWorkCenterCode = originalLink.SourceWorkCenterCode,
            TargetWorkCenterCode = originalLink.TargetWorkCenterCode,
            TriggeredByProduction = originalLink.TriggeredByProduction,
            AutoGenerated = true,
            RequiredForOrderStart = originalLink.RequiredForOrderStart,
            RequiredForOrderCompletion = originalLink.RequiredForOrderCompletion,
            MaterialAvailabilityStatus = originalLink.MaterialAvailabilityStatus,
            RequirementCalculatedAtUtc = DateTimeOffset.UtcNow,
            WorkflowStatus = ProductionTransferWorkflowStatus.Planned,
            RequestedByUserId = originalLink.RequestedByUserId,
            RequestedByNameSnapshot = originalLink.RequestedByNameSnapshot,
            ParentWarehouseTransferHeaderId = original.Id
        };
        var sourceLinks = originalLink.Lines.ToDictionary(x => x.WarehouseTransferLineId);
        foreach (var pair in residualLines.Zip(residualHeader.Lines.OrderBy(x => x.LineNo)))
        {
            var sourceLink = sourceLinks[pair.First.Id];
            childLink.Lines.Add(new()
            {
                BranchCode = original.BranchCode,
                CreatedBy = actor,
                CreatedDate = DateTime.UtcNow,
                WarehouseTransferLine = pair.Second,
                LineRole = sourceLink.LineRole,
                ProductionConsumptionId = sourceLink.ProductionConsumptionId,
                ProductionOutputId = sourceLink.ProductionOutputId,
                RequirementReference = sourceLink.RequirementReference,
                RequiredQuantity = pair.First.RequestedQuantity - pair.First.PickedQuantity
            });
        }
        await uow.Repository<ProductionTransferHeaderLink>().AddAsync(childLink, ct);
        await uow.SaveChangesAsync(ct);
        return residualHeader.Id;
    }

    private static IReadOnlyList<WarehouseTransferTrackingDraftRequest>? BuildResidualTrackings(WarehouseTransferLine line)
    {
        if (line.Trackings.Count == 0) return null;
        return line.Trackings
            .Where(x => x.PlannedQuantity > x.PickedQuantity)
            .Select(x => new WarehouseTransferTrackingDraftRequest(
                x.PlannedQuantity - x.PickedQuantity,
                x.HandlingUnitNo, x.LotNo, x.SerialNo, x.ManufacturingDate, x.ExpirationDate,
                x.SourceLocationId ?? line.DefaultSourceLocationId,
                x.TargetLocationId ?? line.DefaultTargetLocationId))
            .ToArray();
    }

    private static List<StockMovementLineRequest> BuildHandoverMovementRows(WarehouseTransferHeader header)
    {
        var sourceLocationId = header.SourceStagingLocationId
            ?? throw AppException.Conflict("Üretim transfer bekleme rafı bulunamadı.");
        var rows = new List<StockMovementLineRequest>();
        foreach (var line in header.Lines.Where(x => x.PickedQuantity > 0))
        {
            var targetLocationId = line.DefaultTargetLocationId ?? header.TargetPutawayLocationId
                ?? throw AppException.Conflict($"{line.LineNo}. kalem için üretim hedef rafı bulunamadı.");
            if (header.SourceWarehouseId == header.TargetWarehouseId && sourceLocationId == targetLocationId) continue;
            if (line.Trackings.Count > 0)
            {
                rows.AddRange(line.Trackings.Where(x => x.PickedQuantity > 0).Select(x => new StockMovementLineRequest(
                    line.StockId, line.YapCodeId, x.PickedQuantity,
                    header.SourceWarehouseId, sourceLocationId,
                    header.TargetWarehouseId, targetLocationId,
                    line.UnitCode, x.LotNo, x.SerialNo, null, line.SourceStockStatus, line.TargetStockStatus)));
            }
            else
            {
                rows.Add(new(line.StockId, line.YapCodeId, line.PickedQuantity,
                    header.SourceWarehouseId, sourceLocationId,
                    header.TargetWarehouseId, targetLocationId,
                    line.UnitCode, null, null, null, line.SourceStockStatus, line.TargetStockStatus));
            }
        }
        return rows;
    }

    private async Task<(WarehouseTransferHeader Header, ProductionTransferHeaderLink Link)> LoadAsync(
        long transferId, bool tracking, CancellationToken ct)
    {
        if (transferId <= 0) throw AppException.BadRequest("Transfer kimliği geçersiz.");
        var header = await uow.Repository<WarehouseTransferHeader>().Query(tracking)
            .Include(x => x.Lines).ThenInclude(x => x.Trackings)
            .Include(x => x.Tasks).ThenInclude(x => x.Lines)
            .Include(x => x.Tasks).ThenInclude(x => x.Assignments)
            .SingleOrDefaultAsync(x => x.Id == transferId && Contexts.Contains(x.BusinessContext), ct)
            ?? throw AppException.NotFound("Üretim transferi bulunamadı.");
        var link = await uow.Repository<ProductionTransferHeaderLink>().Query(tracking)
            .Include(x => x.Lines)
            .SingleOrDefaultAsync(x => x.WarehouseTransferHeaderId == transferId, ct)
            ?? throw AppException.NotFound("Üretim transfer bağlamı bulunamadı.");
        return (header, link);
    }

    private async Task<ProductionTransferExecutionDto> MapAsync(
        WarehouseTransferHeader header,
        ProductionTransferHeaderLink link,
        CancellationToken ct)
    {
        var warehouseIds = new[] { header.SourceWarehouseId, header.TargetWarehouseId };
        var warehouses = await uow.Repository<WarehouseEntity>().Query()
            .Where(x => warehouseIds.Contains(x.Id))
            .Select(x => new { x.Id, x.WarehouseCode, x.WarehouseName }).ToListAsync(ct);
        var source = warehouses.Single(x => x.Id == header.SourceWarehouseId);
        var target = warehouses.Single(x => x.Id == header.TargetWarehouseId);
        var waiting = header.SourceStagingLocationId.HasValue
            ? await uow.Repository<WarehouseLocation>().Query()
                .Where(x => x.Id == header.SourceStagingLocationId.Value)
                .Select(x => new { x.Id, x.Code, x.Name }).SingleOrDefaultAsync(ct)
            : null;
        var routedLocationIds = header.Lines.Where(x => x.DefaultSourceLocationId.HasValue)
            .Select(x => x.DefaultSourceLocationId!.Value).Distinct().ToArray();
        var routedLocations = routedLocationIds.Length == 0
            ? new Dictionary<long, WarehouseLocation>()
            : await uow.Repository<WarehouseLocation>().Query()
                .Where(x => routedLocationIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, ct);
        string? residualDocumentNo = null;
        if (link.ResidualWarehouseTransferHeaderId.HasValue)
            residualDocumentNo = await uow.Repository<WarehouseTransferHeader>().Query()
                .Where(x => x.Id == link.ResidualWarehouseTransferHeaderId.Value)
                .Select(x => x.DocumentNo).SingleOrDefaultAsync(ct);

        var lineLinks = link.Lines.ToDictionary(x => x.WarehouseTransferLineId);
        var lines = header.Lines.OrderBy(x => x.LineNo).Select(x =>
        {
            lineLinks.TryGetValue(x.Id, out var lineLink);
            var routedLocation = x.DefaultSourceLocationId.HasValue
                && routedLocations.TryGetValue(x.DefaultSourceLocationId.Value, out var location)
                    ? location
                    : null;
            return new ProductionTransferExecutionLineDto(
                x.Id, x.LineNo, x.StockId, x.StockCodeSnapshot, x.StockNameSnapshot, x.UnitCode,
                x.RequestedQuantity, x.PickedQuantity, lineLink?.HandedOverQuantity ?? 0,
                Math.Max(0, x.RequestedQuantity - x.PickedQuantity),
                Math.Max(0, x.RequestedQuantity - x.PickedQuantity),
                x.TrackingType.ToString(), x.DefaultSourceLocationId, routedLocation?.Code, routedLocation?.Name);
        }).ToArray();
        var requested = lines.Sum(x => x.RequestedQuantity);
        var picked = lines.Sum(x => x.PickedQuantity);
        var handedOver = lines.Sum(x => x.HandedOverQuantity);
        return new(
            header.Id, header.DocumentNo, link.WorkflowStatus, header.Status.ToString(),
            source.Id, source.WarehouseCode, source.WarehouseName,
            target.Id, target.WarehouseCode, target.WarehouseName,
            waiting?.Id, waiting?.Code, waiting?.Name,
            link.RequestedByUserId, link.RequestedByNameSnapshot,
            link.HandoverConfirmedBy, link.HandoverConfirmedAtUtc, link.HandoverShortageReason,
            link.ParentWarehouseTransferHeaderId, link.ResidualWarehouseTransferHeaderId, residualDocumentNo,
            requested, picked, handedOver, Math.Max(0, requested - picked),
            picked > 0 && link.WorkflowStatus is ProductionTransferWorkflowStatus.Planned or ProductionTransferWorkflowStatus.Picking,
            link.WorkflowStatus == ProductionTransferWorkflowStatus.AwaitingHandover,
            lines);
    }

    private static void EnsurePickingAllowed(ProductionTransferHeaderLink link)
    {
        if (link.WorkflowStatus is not (ProductionTransferWorkflowStatus.Planned or ProductionTransferWorkflowStatus.Picking))
            throw AppException.Conflict("Bu üretim transferi toplama aşamasında değil.");
    }

    private static long ResolveSourceLocation(
        long? requestedLocationId,
        long? routedLocationId,
        ResolvedWarehouseBarcode resolved)
    {
        if (requestedLocationId.HasValue) return requestedLocationId.Value;
        if (routedLocationId.HasValue
            && resolved.BalanceCandidates.Any(x => x.LocationId == routedLocationId.Value))
            return routedLocationId.Value;
        if (resolved.SuggestedLocationId.HasValue) return resolved.SuggestedLocationId.Value;
        if (resolved.BalanceCandidates.Count == 1) return resolved.BalanceCandidates[0].LocationId;
        throw AppException.Conflict(
            "Barkod birden fazla kaynak rafla eşleşiyor. Fiziksel olarak topladığınız kaynak rafı seçip tekrar okutun.");
    }

    private static void ValidateDimensions(WarehouseTransferLine line, ResolvedWarehouseBarcode resolved)
    {
        if (!string.Equals(line.UnitCode.Trim(), resolved.UnitCode.Trim(), StringComparison.OrdinalIgnoreCase))
            throw AppException.Conflict(
                $"Okutulan etiketin birimi ({resolved.UnitCode}) emir kalemi birimiyle ({line.UnitCode}) uyuşmuyor.");
        if (line.YapCodeId != resolved.YapCodeId)
            throw AppException.Conflict(
                "Okutulan barkodun Yapılandırma Kodu beklenen emir kalemiyle uyuşmuyor. " +
                "Yapılandırma Kodu olmayan ve olan stok boyutları birbirinin yerine kullanılamaz.");
    }

    private static void ValidateSourceBalance(
        WarehouseTransferLine line,
        ResolvedWarehouseBarcode resolved,
        WarehouseBarcodeBalanceCandidate balance)
    {
        if (balance.StockId != line.StockId
            || balance.YapCodeId != line.YapCodeId
            || !string.Equals(balance.UnitCode, line.UnitCode, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(balance.LotNo ?? string.Empty, resolved.LotNo ?? string.Empty, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(balance.SerialNo ?? string.Empty, resolved.SerialNo ?? string.Empty, StringComparison.OrdinalIgnoreCase))
            throw AppException.Conflict(
                "Seçilen kaynak raf bakiyesi emir kaleminin stok/birim/Yapılandırma Kodu/lot/seri boyutlarıyla uyuşmuyor.");
    }

    private static void AddHistory(WarehouseTransferHeader header, WarehouseTransferStatus status, Guid correlationId, string? reason, long actor)
    {
        header.StatusHistory.Add(new()
        {
            BranchCode = header.BranchCode,
            CreatedBy = actor,
            CreatedDate = DateTime.UtcNow,
            StatusArea = WarehouseTransferStatusArea.Operation,
            ToStatus = status.ToString(),
            ChangedAtUtc = DateTimeOffset.UtcNow,
            ChangedBy = actor,
            Description = Clean(reason, 1000),
            CorrelationId = correlationId
        });
    }

    private static string? Clean(string? value, int maxLength)
    {
        var cleaned = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        return cleaned is { Length: > 0 } && cleaned.Length > maxLength ? cleaned[..maxLength] : cleaned;
    }
}
