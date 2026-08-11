using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using verii_wms_api_v2.Modules.BarcodeDesigner.Application;
using verii_wms_api_v2.Modules.Location.Domain;
using verii_wms_api_v2.Modules.Production.Application;
using verii_wms_api_v2.Modules.ProductionTransfer.Domain;
using verii_wms_api_v2.Modules.StockTracking.Application;
using verii_wms_api_v2.Modules.WarehouseOperations.Domain;
using verii_wms_api_v2.Modules.WarehouseTransfer.Application;
using verii_wms_api_v2.Modules.WarehouseTransfer.Domain;
using verii_wms_api_v2.Shared.Application.Abstractions.Persistence;
using verii_wms_api_v2.Shared.Application.Exceptions;
using WarehouseEntity = verii_wms_api_v2.Modules.Warehouse.Domain.Warehouse;

namespace verii_wms_api_v2.Modules.ProductionTransfer.Application;

internal sealed class ProductionTransferScanPickExecutor(
    IUnitOfWork uow,
    IMemoryCache memoryCache,
    IWarehouseBarcodeResolver barcodeResolver,
    IWarehouseTransferOperationService operations,
    IWarehouseTransferReservationService reservations,
    IStockTrackingPolicyResolver trackingPolicies,
    Func<long, bool, CancellationToken, Task<(WarehouseTransferHeader Header, ProductionTransferHeaderLink Link)>> loadAsync,
    Func<long, long, long, decimal, string?, string?, long, CancellationToken, Task> ensureOverPickPlannedQuantitiesAsync)
{
    internal const string PickAboveThresholdConfirmMessage =
        "Bu miktar onay eşiğini aşıyor. Devam etmek için onaylayın.";
    private static readonly TimeSpan PendingTtl = TimeSpan.FromMinutes(15);

    private sealed record PendingScanPickState(
        long WarehouseTransferHeaderId,
        long ProductionTransferHeaderLinkId,
        long ProductionTransferLineLinkId,
        long ExpectedTaskLineId,
        long WtLineId,
        string NormalizedBarcode,
        string BarcodeValue,
        string BarcodeSource,
        long StockId,
        long? YapCodeId,
        string UnitCode,
        string? LotNo,
        string? SerialNo,
        decimal Quantity,
        decimal MaxPickQuantity,
        long SourceLocationId,
        long TargetLocationId);

    private static string PendingCacheKey(Guid idempotencyKey) => $"pt:scan-pending:{idempotencyKey:N}";

    internal async Task<ProductionTransferScanPickResult> ExecuteAsync(
        long transferId,
        ProductionTransferScanPickRequest request,
        long actor,
        CancellationToken ct)
    {
        if (request.IdempotencyKey == Guid.Empty) throw AppException.BadRequest("İşlem anahtarı zorunludur.");
        if (string.IsNullOrWhiteSpace(request.Barcode)) throw AppException.BadRequest("Barkod zorunludur.");
        var normalizedBarcode = request.Barcode.Trim().ToUpperInvariant();

        var replay = await uow.Repository<ProductionTransferBarcodeScan>().Query()
            .FirstOrDefaultAsync(x => x.IdempotencyKey == request.IdempotencyKey, ct);
        if (replay is not null)
            return await BuildReplayResultAsync(transferId, request, replay, ct);

        var pending = memoryCache.Get<PendingScanPickState>(PendingCacheKey(request.IdempotencyKey));
        if (pending is not null)
        {
            if (!request.ConfirmAboveThreshold)
                throw AppException.Conflict(PickAboveThresholdConfirmMessage);

            EnsurePendingMatchesRequest(transferId, request, normalizedBarcode, pending);
            return await ExecutePendingAsync(transferId, request, pending, actor, ct);
        }

        if (request.ConfirmAboveThreshold)
            throw AppException.Conflict("Onay bekleyen barkod toplama bulunamadı. Barkodu yeniden okutun.");

        var resolved = await ResolveAsync(transferId, request, normalizedBarcode, actor, ct);
        if (resolved.ThresholdExceeded)
        {
            SavePending(request.IdempotencyKey, resolved.Pending!);
            throw AppException.Conflict(PickAboveThresholdConfirmMessage);
        }

        return await CommitAsync(transferId, request.IdempotencyKey, resolved, actor, ct);
    }

    private sealed record ResolvedScanPick(
        (WarehouseTransferHeader Header, ProductionTransferHeaderLink Link) Aggregate,
        WarehouseTransferTaskLine TaskLine,
        WarehouseTransferLine Line,
        ProductionTransferLineLink LineLink,
        ResolvedWarehouseBarcode Barcode,
        decimal Quantity,
        decimal MaxPickQuantity,
        long SourceLocationId,
        WarehouseBarcodeBalanceCandidate SourceBalance,
        string NormalizedBarcode,
        decimal? RemainingBarcodeQuantity,
        bool ThresholdExceeded,
        PendingScanPickState? Pending);

    private async Task<ResolvedScanPick> ResolveAsync(
        long transferId,
        ProductionTransferScanPickRequest request,
        string normalizedBarcode,
        long actor,
        CancellationToken ct)
    {
        var aggregate = await loadAsync(transferId, false, ct);
        EnsurePickingAllowed(aggregate.Link);
        ProductionTransferPickingSupport.ResolveAssignedPickTaskForLine(
            aggregate.Header, request.ExpectedTaskLineId, actor);
        var taskLine = aggregate.Header.Tasks
            .SelectMany(x => x.Lines)
            .SingleOrDefault(x => x.Id == request.ExpectedTaskLineId && !x.IsDeleted)
            ?? throw AppException.BadRequest("Beklenen toplama satırı bu üretim transferine ait değil.");
        var line = ProductionTransferPickingSupport.ResolveTaskLine(aggregate.Header, taskLine);
        if (line.WtHeaderId != transferId)
            throw AppException.BadRequest("Beklenen toplama satırı bu üretim transferine ait değil.");
        var lineLink = aggregate.Link.Lines.Single(x => x.WarehouseTransferLineId == line.Id);
        var task = aggregate.Header.Tasks.Single(x => x.Lines.Any(l => l.Id == taskLine.Id));
        var pickingTable = await ProductionTransferPickingSupport.BuildInlinePickingTableAsync(
            uow, aggregate.Header, aggregate.Link, task, ct);
        var transferPolicy = await ProductionTransferOverIssueSupport.LoadPolicyAsync(
            uow, aggregate.Header.BranchCode, ct);
        var linePickCapacity = ProductionTransferOverIssueSupport.GetRemainingPickCapacity(line, transferPolicy);
        var maxPickQuantity = ProductionTransferOverIssueSupport.GetMaxPickQuantity(line, transferPolicy);
        var remaining = taskLine.PlannedQuantity - taskLine.ProcessedQuantity;
        if (remaining <= 0 && linePickCapacity <= 0)
            throw ProductionTransferBarcodeInput.AlreadyPicked(line.StockCodeSnapshot);
        if (remaining <= 0 && !transferPolicy.AllowOverIssue)
            throw ProductionTransferBarcodeInput.AlreadyPicked(line.StockCodeSnapshot);
        var waitingLocationId = aggregate.Header.SourceStagingLocationId
            ?? throw AppException.Conflict("Kaynak depo için üretim transfer bekleme rafı tanımlanmamış.");

        var input = ProductionTransferBarcodeInput.Parse(request.Barcode);
        if (input.StockCode is not null
            && !ProductionTransferBarcodeInput.SameStockCode(line.StockCodeSnapshot, input.StockCode))
            throw AppException.Conflict(
                $"Okutulan stok kodu beklenen stokla uyuşmuyor. Beklenen: {line.StockCodeSnapshot}.");

        var openRows = pickingTable.Rows
            .Where(row => IsRowOpenForPicking(pickingTable, aggregate.Header, row))
            .ToArray();
        if (line.Trackings.Count > 0
            && input.StockCode is null
            && input.SerialNo is null
            && !string.IsNullOrWhiteSpace(input.Raw))
        {
            var matchedRow = ProductionTransferBarcodeInput.FindMatchingOpenRow(input, openRows);
            input = ProductionTransferBarcodeInput.EnrichFromMatchedRow(input, matchedRow);
            var unavailableRow = ProductionTransferBarcodeInput.FindUnavailableRow(input, openRows);
            if (unavailableRow is not null)
                throw ProductionTransferBarcodeInput.UnavailableBalance(unavailableRow);
            if (matchedRow is null)
                throw AppException.BadRequest(ProductionTransferBarcodeInput.SerialCompositeFormatMessage);
        }

        if (line.Trackings.Count == 0 && input.StockCode is null && !string.IsNullOrWhiteSpace(input.Raw))
        {
            var unavailableRow = ProductionTransferBarcodeInput.FindUnavailableNonSerialRow(input, openRows);
            if (unavailableRow is not null && ProductionTransferBarcodeInput.FindMatchingOpenRow(input, openRows) is null)
                throw ProductionTransferBarcodeInput.UnavailableBalance(unavailableRow);
        }

        var expectedSourceLocationId = request.SourceLocationId ?? taskLine.SourceLocationId ?? line.DefaultSourceLocationId;
        var pickRows = pickingTable.Rows
            .Where(x => x.TaskLineId == taskLine.Id && IsRowOpenForPicking(pickingTable, aggregate.Header, x))
            .ToArray();
        var matchedPickRow = ProductionTransferBarcodeInput.FindMatchingOpenRow(input, pickRows)
            ?? pickRows.FirstOrDefault();
        input = ProductionTransferBarcodeInput.EnrichFromMatchedRow(input, matchedPickRow);
        var resolved = await ResolveTransferPickBarcodeAsync(
            aggregate.Header,
            line,
            matchedPickRow ?? new ProductionTransferPickingRowDto(
                taskLine.Id,
                line.Id,
                line.LineNo,
                expectedSourceLocationId,
                null,
                line.StockId,
                line.StockCodeSnapshot,
                line.StockNameSnapshot,
                null,
                taskLine.PlannedQuantity,
                remaining,
                taskLine.ProcessedQuantity,
                expectedSourceLocationId.HasValue && remaining > 0),
            input,
            new ProductionTransferBarcodeInput.ResolveContext(
                line.StockId,
                expectedSourceLocationId,
                line.YapCodeId,
                line.UnitCode),
            ct);
        if (!resolved.CanExecute || resolved.MissingFields.Count > 0)
            throw AppException.Conflict($"Barkod toplama için uygun değil: {string.Join(", ", resolved.MissingFields)}.");
        ValidateDimensions(line, resolved);

        var sourceLocationId = ResolveSourceLocation(request.SourceLocationId, line.DefaultSourceLocationId, resolved);
        var sourceBalance = resolved.BalanceCandidates.FirstOrDefault(x => x.LocationId == sourceLocationId)
            ?? throw AppException.Conflict("Seçilen kaynak rafta okutulan barkoda ait kullanılabilir stok bulunamadı.");
        ValidateSourceBalance(line, resolved, sourceBalance);

        var quantityBound = ProductionTransferBarcodePickPolicy.IsQuantityBoundSource(resolved.Source);
        var alreadyAccepted = quantityBound
            ? Math.Max(0, ProductionTransferUnpickMovement.NetBarcodeAcceptedQuantity(
                await uow.Repository<ProductionTransferBarcodeScan>().Query()
                    .Where(x => x.ProductionTransferHeaderLinkId == aggregate.Link.Id
                        && x.NormalizedBarcode == normalizedBarcode)
                    .ToListAsync(ct),
                normalizedBarcode))
            : 0;
        decimal? plannedTrackingRemaining = null;
        if (line.Trackings.Count > 0)
        {
            var plannedTracking = line.Trackings.FirstOrDefault(x =>
                SameTrackingValue(x.LotNo, resolved.LotNo)
                && SameTrackingValue(x.SerialNo, resolved.SerialNo));
            if (plannedTracking is null)
                throw AppException.Conflict(
                    $"{line.LineNo}. satırın seri/lot bilgisi planlanan takip kaydıyla eşleşmiyor.");
            plannedTrackingRemaining = plannedTracking.PlannedQuantity - plannedTracking.PickedQuantity;
        }
        var policy = await trackingPolicies.ResolveAsync(aggregate.Header.BranchCode, line.StockId, ct);
        var requestedQuantity = request.Quantity ?? Math.Max(remaining, 0);
        if (requestedQuantity <= 0) throw AppException.BadRequest("Toplanacak miktar geçersiz.");
        if (transferPolicy.AllowOverIssue)
        {
            if (requestedQuantity > linePickCapacity + 0.000001m)
                throw AppException.BadRequest("Toplanacak miktar fazla sarf toleransını aşıyor.");
        }
        else if (requestedQuantity > remaining + 0.000001m)
        {
            throw AppException.BadRequest("Toplanacak miktar kalan miktardan fazla olamaz.");
        }
        var quantity = ProductionTransferBarcodePickPolicy.CalculateQuantity(
            policy, resolved.Quantity, alreadyAccepted, requestedQuantity, sourceBalance.AvailableQuantity,
            quantityBound, plannedTrackingRemaining);
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

        decimal? remainingBarcodeQuantity = quantityBound
            ? Math.Max(0, (resolved.Quantity ?? 0) - alreadyAccepted - quantity)
            : null;

        var autoPickThreshold = await uow.Repository<WarehouseEntity>().Query()
            .Where(x => x.Id == aggregate.Header.SourceWarehouseId)
            .Select(x => x.AutoPickWithoutConfirmMaxQuantity)
            .SingleAsync(ct);
        var thresholdExceeded = autoPickThreshold is > 0 && quantity > autoPickThreshold.Value;
        PendingScanPickState? pending = null;
        if (thresholdExceeded)
        {
            pending = new PendingScanPickState(
                transferId,
                aggregate.Link.Id,
                lineLink.Id,
                request.ExpectedTaskLineId,
                line.Id,
                normalizedBarcode,
                resolved.RawBarcode,
                resolved.Source,
                resolved.StockId,
                resolved.YapCodeId,
                sourceBalance.UnitCode,
                resolved.LotNo,
                resolved.SerialNo,
                quantity,
                maxPickQuantity,
                sourceLocationId,
                waitingLocationId);
        }

        return new(
            aggregate,
            taskLine,
            line,
            lineLink,
            resolved,
            quantity,
            maxPickQuantity,
            sourceLocationId,
            sourceBalance,
            normalizedBarcode,
            remainingBarcodeQuantity,
            thresholdExceeded,
            pending);
    }

    private Task<ProductionTransferScanPickResult> CommitAsync(
        long transferId,
        Guid idempotencyKey,
        ResolvedScanPick resolved,
        long actor,
        CancellationToken ct,
        Guid? pendingCacheKeyToClear = null) =>
        CommitAsync(
            transferId,
            idempotencyKey,
            resolved.Aggregate,
            resolved.TaskLine,
            resolved.Line,
            resolved.LineLink,
            resolved.Barcode,
            resolved.Quantity,
            resolved.MaxPickQuantity,
            resolved.SourceLocationId,
            resolved.SourceBalance,
            resolved.NormalizedBarcode,
            resolved.RemainingBarcodeQuantity,
            actor,
            ct,
            pendingCacheKeyToClear);

    private async Task<ProductionTransferScanPickResult> ExecutePendingAsync(
        long transferId,
        ProductionTransferScanPickRequest request,
        PendingScanPickState pending,
        long actor,
        CancellationToken ct)
    {
        var aggregate = await loadAsync(transferId, false, ct);
        EnsurePickingAllowed(aggregate.Link);
        ProductionTransferPickingSupport.ResolveAssignedPickTaskForLine(
            aggregate.Header, pending.ExpectedTaskLineId, actor);
        var taskLine = aggregate.Header.Tasks
            .SelectMany(x => x.Lines)
            .Single(x => x.Id == pending.ExpectedTaskLineId && !x.IsDeleted);
        var line = aggregate.Header.Lines.Single(x => x.Id == pending.WtLineId);
        var lineLink = aggregate.Link.Lines.Single(x => x.Id == pending.ProductionTransferLineLinkId);
        var sourceBalance = new WarehouseBarcodeBalanceCandidate(
            0,
            aggregate.Header.SourceWarehouseId,
            pending.SourceLocationId,
            string.Empty,
            string.Empty,
            pending.StockId,
            pending.YapCodeId,
            pending.UnitCode,
            pending.LotNo,
            pending.SerialNo,
            "Available",
            pending.Quantity);
        var resolvedBarcode = new ResolvedWarehouseBarcode(
            pending.BarcodeValue,
            pending.BarcodeSource,
            pending.StockId,
            line.StockCodeSnapshot,
            line.StockNameSnapshot ?? string.Empty,
            pending.YapCodeId,
            null,
            pending.Quantity,
            pending.UnitCode,
            pending.LotNo,
            pending.SerialNo,
            null,
            null,
            false,
            false,
            false,
            false,
            [],
            [sourceBalance],
            pending.SourceLocationId,
            true);
        return await CommitAsync(
            transferId,
            request.IdempotencyKey,
            aggregate,
            taskLine,
            line,
            lineLink,
            resolvedBarcode,
            pending.Quantity,
            pending.MaxPickQuantity,
            pending.SourceLocationId,
            sourceBalance,
            pending.NormalizedBarcode,
            null,
            actor,
            ct,
            request.IdempotencyKey);
    }

    private async Task<ProductionTransferScanPickResult> CommitAsync(
        long transferId,
        Guid idempotencyKey,
        (WarehouseTransferHeader Header, ProductionTransferHeaderLink Link) aggregate,
        WarehouseTransferTaskLine taskLine,
        WarehouseTransferLine line,
        ProductionTransferLineLink lineLink,
        ResolvedWarehouseBarcode resolved,
        decimal quantity,
        decimal maxPickQuantity,
        long sourceLocationId,
        WarehouseBarcodeBalanceCandidate sourceBalance,
        string normalizedBarcode,
        decimal? remainingBarcodeQuantity,
        long actor,
        CancellationToken ct,
        Guid? pendingCacheKeyToClear = null)
    {
        var waitingLocationId = aggregate.Header.SourceStagingLocationId
            ?? throw AppException.Conflict("Kaynak depo için üretim transfer bekleme rafı tanımlanmamış.");

        await uow.ExecuteInTransactionAsync(async token =>
        {
            var header = await uow.Repository<WarehouseTransferHeader>().Query(true)
                .Include(x => x.Tasks).ThenInclude(x => x.Assignments)
                .SingleAsync(x => x.Id == transferId, token);
            ProductionTransferPickingSupport.EnsureHeaderReleasedForPicking(
                header, actor, DateTimeOffset.UtcNow);
            await uow.SaveChangesAsync(token);
            return true;
        }, ct, IsolationLevel.Serializable);

        await ensureOverPickPlannedQuantitiesAsync(
            transferId, line.Id, taskLine.Id, quantity, resolved.LotNo, resolved.SerialNo, actor, ct);

        await uow.ExecuteInTransactionAsync(async token =>
        {
            var header = await uow.Repository<WarehouseTransferHeader>().Query(true)
                .Include(x => x.Lines).ThenInclude(x => x.Trackings)
                .SingleAsync(x => x.Id == transferId, token);
            var pickLine = new WarehouseTransferOperationLineRequest(
                line.Id,
                quantity,
                sourceLocationId,
                waitingLocationId,
                resolved.LotNo,
                resolved.SerialNo,
                maxPickQuantity);
            await reservations.EnsurePickCoverageAsync(
                header,
                line.Id,
                pickLine,
                $"WT:{transferId}:RESERVE:OVER-PICK:{idempotencyKey:N}",
                actor,
                token);
            await uow.SaveChangesAsync(token);
            return true;
        }, ct);

        await operations.PickAsync(transferId, new(
            idempotencyKey,
            [new(line.Id, quantity, sourceLocationId, waitingLocationId, resolved.LotNo, resolved.SerialNo, maxPickQuantity)],
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
                IdempotencyKey = idempotencyKey,
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
            if (pendingCacheKeyToClear.HasValue)
                memoryCache.Remove(PendingCacheKey(pendingCacheKeyToClear.Value));
            await uow.SaveChangesAsync(token);
            return true;
        }, ct);

        return await ReadDeltaAsync(
            transferId,
            taskLine.Id,
            line.Id,
            sourceLocationId,
            sourceBalance.LocationCode,
            sourceBalance.LocationName,
            quantity,
            resolved.SerialNo,
            resolved.LotNo,
            resolved.Source,
            remainingBarcodeQuantity,
            ct);
    }

    private void SavePending(Guid idempotencyKey, PendingScanPickState pending)
    {
        if (memoryCache.TryGetValue(PendingCacheKey(idempotencyKey), out PendingScanPickState? existing)
            && existing is not null)
        {
            if (existing.WarehouseTransferHeaderId != pending.WarehouseTransferHeaderId
                || existing.ExpectedTaskLineId != pending.ExpectedTaskLineId
                || !string.Equals(existing.NormalizedBarcode, pending.NormalizedBarcode, StringComparison.OrdinalIgnoreCase))
                throw AppException.Conflict("Aynı işlem anahtarı farklı bir barkod toplama isteğinde kullanılamaz.");
            return;
        }

        memoryCache.Set(PendingCacheKey(idempotencyKey), pending, PendingTtl);
    }

    private static void EnsurePendingMatchesRequest(
        long transferId,
        ProductionTransferScanPickRequest request,
        string normalizedBarcode,
        PendingScanPickState pending)
    {
        if (pending.WarehouseTransferHeaderId != transferId
            || pending.ExpectedTaskLineId != request.ExpectedTaskLineId
            || !string.Equals(pending.NormalizedBarcode, normalizedBarcode, StringComparison.OrdinalIgnoreCase))
            throw AppException.Conflict("Aynı işlem anahtarı farklı bir barkod toplama isteğinde kullanılamaz.");
        if (request.Quantity.HasValue && Math.Abs(request.Quantity.Value - pending.Quantity) > 0.000001m)
            throw AppException.Conflict("Onay bekleyen toplama miktarı istekle uyuşmuyor.");
        if (request.SourceLocationId.HasValue && request.SourceLocationId.Value != pending.SourceLocationId)
            throw AppException.Conflict("Onay bekleyen toplama kaynak rafı istekle uyuşmuyor.");
    }

    private async Task<ProductionTransferScanPickResult> BuildReplayResultAsync(
        long transferId,
        ProductionTransferScanPickRequest request,
        ProductionTransferBarcodeScan replay,
        CancellationToken ct)
    {
        var replayHeaderLink = await uow.Repository<ProductionTransferHeaderLink>().Query()
            .SingleAsync(x => x.Id == replay.ProductionTransferHeaderLinkId, ct);
        var replayLineLink = await uow.Repository<ProductionTransferLineLink>().Query()
            .SingleAsync(x => x.Id == replay.ProductionTransferLineLinkId, ct);
        var replayTaskLine = await uow.Repository<WarehouseTransferTaskLine>().Query()
            .SingleAsync(x => x.Id == request.ExpectedTaskLineId, ct);
        if (replayHeaderLink.WarehouseTransferHeaderId != transferId
            || replayLineLink.WarehouseTransferLineId != replayTaskLine.WtLineId
            || replay.NormalizedBarcode != request.Barcode.Trim().ToUpperInvariant())
            throw AppException.Conflict("Aynı işlem anahtarı farklı bir barkod toplama isteğinde kullanılamaz.");
        var replayLocation = await uow.Repository<WarehouseLocation>().Query()
            .SingleAsync(x => x.Id == replay.SourceLocationId, ct);
        return await ReadDeltaAsync(
            transferId,
            replayTaskLine.Id,
            replayTaskLine.WtLineId,
            replay.SourceLocationId,
            replayLocation.Code,
            replayLocation.Name,
            replay.Quantity,
            replay.SerialNo,
            replay.LotNo,
            replay.BarcodeSource,
            null,
            ct);
    }

    private async Task<ProductionTransferScanPickResult> ReadDeltaAsync(
        long transferId,
        long taskLineId,
        long lineId,
        long sourceLocationId,
        string sourceLocationCode,
        string sourceLocationName,
        decimal acceptedQuantity,
        string? serialNo,
        string? lotNo,
        string barcodeSource,
        decimal? remainingBarcodeQuantity,
        CancellationToken ct)
    {
        var aggregate = await loadAsync(transferId, false, ct);
        var line = aggregate.Header.Lines.Single(x => x.Id == lineId);
        var task = aggregate.Header.Tasks.Single(x => x.Lines.Any(l => l.Id == taskLineId));
        var pickingTable = await ProductionTransferPickingSupport.BuildInlinePickingTableAsync(
            uow, aggregate.Header, aggregate.Link, task, ct);
        var row = pickingTable.Rows.FirstOrDefault(x => x.TaskLineId == taskLineId && SameTrackingValue(x.SerialNo, serialNo))
            ?? pickingTable.Rows.First(x => x.TaskLineId == taskLineId);
        var effectivePicked = ProductionWorkOrderMaterialAssignment.ResolveEffectivePickedQuantity(line);
        return new(
            row,
            new(
                aggregate.Link.WorkflowStatus,
                pickingTable.PickedQuantity,
                pickingTable.ShortageQuantity,
                pickingTable.OverIssueQuantity,
                pickingTable.CanCompletePicking),
            new(
                line.Id,
                effectivePicked,
                Math.Max(0, line.RequestedQuantity - effectivePicked),
                ProductionTransferOverIssueSupport.GetOverIssueQuantity(line)),
            line.Id,
            line.StockCodeSnapshot,
            acceptedQuantity,
            serialNo,
            lotNo,
            barcodeSource,
            sourceLocationId,
            sourceLocationCode,
            sourceLocationName,
            remainingBarcodeQuantity);
    }

    private async Task<ResolvedWarehouseBarcode> ResolveTransferPickBarcodeAsync(
        WarehouseTransferHeader header,
        WarehouseTransferLine matchedLine,
        ProductionTransferPickingRowDto matchedRow,
        ProductionTransferBarcodeInput.Parsed input,
        ProductionTransferBarcodeInput.ResolveContext resolveContext,
        CancellationToken ct)
    {
        var resolved = await barcodeResolver.ResolveAsync(new(
            input.ResolutionBarcode,
            header.BranchCode,
            WarehouseBarcodePurpose.Outbound,
            header.SourceWarehouseId,
            resolveContext.StockId,
            resolveContext.LocationId,
            resolveContext.YapCodeId,
            resolveContext.UnitCode), ct);

        var expectedLocationId = matchedRow.SourceLocationId ?? matchedLine.DefaultSourceLocationId;
        if (!expectedLocationId.HasValue)
            return resolved;

        var pickBalances = await ProductionTransferPickingBalanceSupport.FindPickBalanceCandidatesAsync(
            uow,
            header,
            matchedLine,
            expectedLocationId.Value,
            resolved.LotNo,
            matchedRow.SerialNo ?? resolved.SerialNo,
            ct);
        if (pickBalances.Count == 0)
            return resolved;

        var missing = resolved.MissingFields
            .Where(x => !string.Equals(x, "Kullanılabilir raf bakiyesi", StringComparison.Ordinal))
            .ToArray();
        return resolved with
        {
            BalanceCandidates = pickBalances,
            MissingFields = missing,
            SuggestedLocationId = expectedLocationId,
            CanExecute = missing.Length == 0,
        };
    }

    private static void EnsurePickingAllowed(ProductionTransferHeaderLink link)
    {
        if (link.WorkflowStatus is not (ProductionTransferWorkflowStatus.Planned or ProductionTransferWorkflowStatus.Picking))
            throw AppException.Conflict("Bu üretim transferi toplama aşamasında değil.");
    }

    private static bool IsRowOpenForPicking(
        ProductionTransferPickingTableDto table,
        WarehouseTransferHeader header,
        ProductionTransferPickingRowDto row)
    {
        if (row.RemainingQuantity > 0) return true;
        if (!table.AllowOverIssue || row.ProcessedQuantity <= 0) return false;
        var line = header.Lines.Single(x => x.Id == row.WtLineId);
        var policy = new ProductionTransferPolicy
        {
            AllowOverIssue = true,
            OverIssueTolerancePercent = table.OverIssueTolerancePercent,
        };
        return ProductionTransferOverIssueSupport.GetRemainingPickCapacity(line, policy) > 0;
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

    private static bool SameTrackingValue(string? left, string? right) =>
        string.Equals(
            string.IsNullOrWhiteSpace(left) ? null : left.Trim(),
            string.IsNullOrWhiteSpace(right) ? null : right.Trim(),
            StringComparison.OrdinalIgnoreCase);
}
