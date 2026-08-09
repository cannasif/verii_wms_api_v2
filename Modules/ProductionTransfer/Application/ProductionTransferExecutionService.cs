using System.Data;
using Microsoft.EntityFrameworkCore;
using verii_wms_api_v2.Modules.Audit.Application;
using verii_wms_api_v2.Modules.BarcodeDesigner.Application;
using verii_wms_api_v2.Modules.ErpIntegration.Domain;
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

    public async Task<ProductionTransferPickingTableDto> GetPickingTableAsync(long transferId, long actor, CancellationToken ct = default)
    {
        var aggregate = await LoadAsync(transferId, false, ct);
        EnsurePickingAllowed(aggregate.Link);
        var task = ProductionTransferPickingSupport.ResolveWorkerPickTask(aggregate.Header, actor);
        if (task.Status is WarehouseTransferTaskStatus.InProgress or WarehouseTransferTaskStatus.PartiallyCompleted)
        {
            await uow.ExecuteInTransactionAsync(async token =>
            {
                var header = await uow.Repository<WarehouseTransferHeader>().Query(true)
                    .Include(x => x.Lines)
                    .Include(x => x.Tasks).ThenInclude(x => x.Lines)
                    .SingleAsync(x => x.Id == transferId, token);
                var link = await uow.Repository<ProductionTransferHeaderLink>().Query(true)
                    .Include(x => x.Lines)
                    .SingleAsync(x => x.WarehouseTransferHeaderId == transferId, token);
                var activeTask = header.Tasks.Single(x => x.Id == task.Id);
                ProductionTransferLineSplitHelper.RemoveRedundantShortageSiblings(header, activeTask, link);
                await uow.SaveChangesAsync(token);
                return true;
            }, ct);
            aggregate = await LoadAsync(transferId, false, ct);
            task = ProductionTransferPickingSupport.ResolveWorkerPickTask(aggregate.Header, actor);
        }

        var isLocked = task.Status is not WarehouseTransferTaskStatus.InProgress
            and not WarehouseTransferTaskStatus.PartiallyCompleted;
        IReadOnlyList<ProductionTransferPickingRowDto> rows;
        if (isLocked)
            rows = ProductionTransferPickingSupport.BuildRecipeRows(aggregate.Header, task);
        else
        {
            var locationIds = task.Lines.SelectMany(x =>
            {
                var line = ProductionTransferPickingSupport.ResolveTaskLine(aggregate.Header, x);
                return new long?[] { x.SourceLocationId, line.DefaultSourceLocationId }
                    .Concat(line.Trackings.Select(t => t.SourceLocationId));
            });
            var locationCodes = await ProductionTransferPickingSupport.LoadLocationCodesAsync(uow, locationIds, ct);
            rows = ProductionTransferPickingSupport.BuildPersistedRows(aggregate.Header, task, locationCodes);
        }

        return ProductionTransferPickingSupport.MapTable(
            aggregate.Header, aggregate.Link, task, isLocked,
            ProductionTransferPickingSupport.SortDisplayRows(rows, aggregate.Header, aggregate.Link));
    }

    public async Task<ResolveProductionTransferBarcodeResult> ResolveBarcodeAsync(
        long transferId,
        ResolveProductionTransferBarcodeRequest request,
        long actor,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Barcode))
            throw AppException.BadRequest("Barkod zorunludur.");
        var table = await GetPickingTableAsync(transferId, actor, ct);
        if (table.IsLocked)
            throw AppException.Conflict("Toplama başlatılmadan barkod doğrulanamaz.");
        var aggregate = await LoadAsync(transferId, false, ct);
        var allRows = table.Rows.ToArray();
        var openRows = allRows.Where(x => x.RemainingQuantity > 0).OrderBy(x => x.LineNo).ToArray();

        var input = ProductionTransferBarcodeInput.Parse(request.Barcode);
        ProductionTransferBarcodeInput.EnsureResolvableBarcode(input, openRows, allRows);

        if (openRows.Length == 0)
            throw AppException.Conflict("Toplanacak açık satır bulunmuyor.");

        var matchedRow = ProductionTransferBarcodeInput.FindMatchingOpenRow(input, openRows);
        if (matchedRow is null)
            throw AppException.Conflict("Okutulan barkod tablodaki açık satırlardan biriyle eşleşmedi.");

        WarehouseTransferLine? matchedLine = matchedRow is null
            ? null
            : aggregate.Header.Lines.Single(x => x.Id == matchedRow.WtLineId);
        var resolveContext = ProductionTransferBarcodeInput.BuildResolveContext(
            input, openRows, aggregate.Header, matchedLine, matchedRow);
        if (input.StockCode is not null && resolveContext.StockId is null)
            throw AppException.Conflict("Okutulan stok kodu tablodaki açık satırlarla eşleşmedi.");

        var resolved = await barcodeResolver.ResolveAsync(new(
            input.ResolutionBarcode,
            aggregate.Header.BranchCode,
            WarehouseBarcodePurpose.Outbound,
            aggregate.Header.SourceWarehouseId,
            resolveContext.StockId,
            resolveContext.LocationId,
            resolveContext.YapCodeId,
            resolveContext.UnitCode), ct);
        if (!resolved.CanExecute || resolved.MissingFields.Count > 0)
            throw AppException.Conflict("Okutulan barkod tablodaki açık satırlardan biriyle eşleşmedi.");

        foreach (var row in openRows.Where(x => x.CanPick))
        {
            var line = aggregate.Header.Lines.Single(x => x.Id == row.WtLineId);
            if (line.StockId != resolved.StockId) continue;
            if (input.StockCode is not null
                && !ProductionTransferBarcodeInput.SameStockCode(line.StockCodeSnapshot, input.StockCode))
                continue;
            if (line.YapCodeId != resolved.YapCodeId) continue;
            if (!string.Equals(line.UnitCode.Trim(), resolved.UnitCode.Trim(), StringComparison.OrdinalIgnoreCase)) continue;
            if (input.SerialNo is not null
                && !SameTrackingValue(row.SerialNo ?? resolved.SerialNo, input.SerialNo))
                continue;
            if (row.SerialNo is not null
                && !SameTrackingValue(row.SerialNo, resolved.SerialNo))
                continue;

            var expectedSourceLocationId = row.SourceLocationId ?? line.DefaultSourceLocationId;
            if (expectedSourceLocationId.HasValue
                && !resolved.BalanceCandidates.Any(x => x.LocationId == expectedSourceLocationId.Value))
                continue;

            return new(
                row.TaskLineId, row.WtLineId, row.SourceLocationId, row.SourceLocationCode,
                line.StockId, line.StockCodeSnapshot, line.StockNameSnapshot, row.SerialNo ?? resolved.SerialNo,
                resolved.LotNo, row.RemainingQuantity, row.RemainingQuantity,
                !string.IsNullOrWhiteSpace(row.SerialNo), row.CanPick);
        }

        throw AppException.Conflict("Okutulan barkod tablodaki açık satırlardan biriyle eşleşmedi.");
    }

    public Task<ProductionTransferRouteRefreshCandidatesDto> GetRouteRefreshCandidatesAsync(
        long transferId,
        long taskLineId,
        string? currentSerialNo,
        long actor,
        CancellationToken ct = default) =>
        uow.ExecuteInTransactionAsync(async token =>
        {
            var aggregate = await LoadAsync(transferId, false, token);
            EnsurePickingAllowed(aggregate.Link);
            var task = ProductionTransferPickingSupport.ResolveWorkerPickTask(aggregate.Header, actor);
            if (task.Status is not WarehouseTransferTaskStatus.InProgress)
                throw AppException.Conflict("Rota güncelleme yalnızca başlatılmış toplama görevlerinde kullanılabilir.");
            var taskLine = task.Lines.SingleOrDefault(x => x.Id == taskLineId && !x.IsDeleted)
                ?? throw AppException.NotFound("Toplama satırı bulunamadı.");
            var line = ProductionTransferPickingSupport.ResolveTaskLine(aggregate.Header, taskLine);

            if (line.Trackings.Count > 0)
            {
                if (string.IsNullOrWhiteSpace(currentSerialNo))
                    throw AppException.BadRequest("Serili satır için seri numarası zorunludur.");

                var tracking = line.Trackings.SingleOrDefault(x =>
                        SameTrackingValue(x.SerialNo, currentSerialNo))
                    ?? throw AppException.NotFound("Seçilen seri bulunamadı.");
                var trackingRemaining = tracking.PlannedQuantity - tracking.PickedQuantity;
                if (trackingRemaining <= 0)
                    throw AppException.Conflict("Seçilen serinin güncellenecek kalan miktarı yok.");

                var context = await ProductionTransferPickingSupport.LoadBalanceContextAsync(
                    uow, aggregate.Header, [line], token);
                var serialCurrentSourceLocationId = tracking.SourceLocationId ?? taskLine.SourceLocationId ?? line.DefaultSourceLocationId;
                var serialExcludedLocations = ProductionTransferRouteAllocation.GetRouteRefreshExcludedSourceLocationIds(
                    serialCurrentSourceLocationId);
                var serialEligibleBalances = ProductionTransferRouteAllocation.ExcludeLocations(
                    context.Balances, serialExcludedLocations);
                var assignedSerials = ProductionTransferRouteAllocation.GetAssignedSerialNumbersInGroup(
                    task, line, aggregate.Link, currentSerialNo);
                var candidates = ProductionTransferRouteAllocation.ListSerialRouteRefreshCandidates(
                    line.StockId,
                    line.YapCodeId,
                    line.UnitCode,
                    currentSerialNo,
                    assignedSerials,
                    serialEligibleBalances,
                    context.Locations);

                return new ProductionTransferRouteRefreshCandidatesDto(
                    taskLineId,
                    1,
                    true,
                    currentSerialNo.Trim(),
                    candidates.Select(x => new ProductionTransferRouteRefreshCandidateDto(
                        x.LocationId,
                        context.Locations[x.LocationId].Code,
                        x.AvailableQuantity,
                        1,
                        x.SerialNo)).ToArray());
            }

            var remaining = taskLine.PlannedQuantity - taskLine.ProcessedQuantity;
            if (remaining <= 0) throw AppException.Conflict("Seçilen satırın güncellenecek kalan miktarı yok.");

            var currentSourceLocationId = taskLine.SourceLocationId ?? line.DefaultSourceLocationId;
            var excludedLocations = ProductionTransferRouteAllocation.GetRouteRefreshExcludedSourceLocationIds(
                currentSourceLocationId);
            var nonSerialContext = await ProductionTransferPickingSupport.LoadBalanceContextAsync(
                uow, aggregate.Header, [line], token);
            var eligibleBalances = ProductionTransferRouteAllocation.ExcludeLocations(
                nonSerialContext.Balances, excludedLocations);
            var nonSerialCandidates = ProductionTransferRouteAllocation.ListNonSerialCandidates(
                line.StockId, line.YapCodeId, line.UnitCode, eligibleBalances, nonSerialContext.Locations);
            var greedy = ProductionTransferRouteAllocation.AllocateGreedyNonSerial(
                remaining, line.StockId, line.YapCodeId, line.UnitCode, eligibleBalances, nonSerialContext.Locations)
                .Where(x => x.LocationId.HasValue)
                .GroupBy(x => x.LocationId!.Value)
                .ToDictionary(g => g.Key, g => g.Sum(x => x.Quantity));
            var subtractSiblingCommitments = aggregate.Header.ReservationPolicy == WarehouseTransferReservationPolicy.None
                && aggregate.Header.Lines.All(x => x.ReservedQuantity <= 0);
            return new ProductionTransferRouteRefreshCandidatesDto(
                taskLineId,
                remaining,
                false,
                null,
                nonSerialCandidates
                    .Select(x =>
                    {
                        var available = ProductionTransferRouteAllocation.GetRouteRefreshAvailableAtLocation(
                            x.LocationId,
                            line.StockId,
                            line.YapCodeId,
                            line.UnitCode,
                            eligibleBalances,
                            task,
                            taskLine,
                            line,
                            aggregate.Link,
                            subtractSiblingCommitments);
                        return new ProductionTransferRouteRefreshCandidateDto(
                            x.LocationId,
                            nonSerialContext.Locations[x.LocationId].Code,
                            available,
                            Math.Min(greedy.GetValueOrDefault(x.LocationId), available));
                    })
                    .Where(x => x.AvailableQuantity > 0)
                    .ToArray());
        }, ct);

    public Task<ProductionTransferPickingTableDto> ApplyRouteRefreshSplitAsync(
        long transferId,
        long taskLineId,
        ApplyProductionTransferRouteRefreshSplitRequest request,
        long actor,
        CancellationToken ct = default) =>
        uow.ExecuteInTransactionAsync(async token =>
        {
            if (request.IdempotencyKey == Guid.Empty) throw AppException.BadRequest("İşlem anahtarı zorunludur.");
            var aggregate = await LoadAsync(transferId, true, token);
            EnsurePickingAllowed(aggregate.Link);
            var task = ProductionTransferPickingSupport.ResolveWorkerPickTask(aggregate.Header, actor);
            if (task.Status is not WarehouseTransferTaskStatus.InProgress)
                throw AppException.Conflict("Rota güncelleme yalnızca başlatılmış toplama görevlerinde kullanılabilir.");
            var taskLine = task.Lines.SingleOrDefault(x => x.Id == taskLineId && !x.IsDeleted)
                ?? throw AppException.NotFound("Toplama satırı bulunamadı.");
            var line = ProductionTransferPickingSupport.ResolveTaskLine(aggregate.Header, taskLine);
            var reservationPrefix = $"WT:{transferId}:ROUTE-SPLIT:{taskLineId}:{request.IdempotencyKey:N}";

            if (line.Trackings.Count > 0)
            {
                if (string.IsNullOrWhiteSpace(request.CurrentSerialNo))
                    throw AppException.BadRequest("Serili satır için seri numarası zorunludur.");

                var tracking = line.Trackings.SingleOrDefault(x =>
                        SameTrackingValue(x.SerialNo, request.CurrentSerialNo))
                    ?? throw AppException.NotFound("Seçilen seri bulunamadı.");
                var trackingRemaining = tracking.PlannedQuantity - tracking.PickedQuantity;
                if (trackingRemaining <= 0)
                    throw AppException.Conflict("Seçilen serinin güncellenecek kalan miktarı yok.");
                if (tracking.PickedQuantity > 0)
                    throw AppException.Conflict("Toplanmış serinin rotası güncellenemez.");

                var split = request.Splits.SingleOrDefault(x => x.Quantity > 0)
                    ?? throw AppException.BadRequest("Yeni seri seçilmelidir.");
                if (split.Quantity != 1)
                    throw AppException.BadRequest("Serili rota güncellemede miktar 1 olmalıdır.");
                if (string.IsNullOrWhiteSpace(split.SerialNo))
                    throw AppException.BadRequest("Yeni seri numarası zorunludur.");
                if (SameTrackingValue(split.SerialNo, request.CurrentSerialNo))
                    throw AppException.BadRequest("Mevcut seri ile aynı seri seçilemez.");

                var serialCurrentSourceLocationId = tracking.SourceLocationId ?? taskLine.SourceLocationId ?? line.DefaultSourceLocationId;
                if (serialCurrentSourceLocationId == split.LocationId)
                    throw AppException.Conflict("Kaynak rafa rotalama yapılamaz.");

                var serialContext = await ProductionTransferPickingSupport.LoadBalanceContextAsync(
                    uow, aggregate.Header, [line], token);
                if (!serialContext.Locations.ContainsKey(split.LocationId))
                    throw AppException.BadRequest("Seçilen kaynak raf geçersiz.");

                var assignedSerials = ProductionTransferRouteAllocation.GetAssignedSerialNumbersInGroup(
                    task, line, aggregate.Link, request.CurrentSerialNo);
                if (assignedSerials.Contains(ProductionTransferRouteAllocation.NormalizeSerial(split.SerialNo)))
                    throw AppException.Conflict("Seçilen seri zaten toplama listesinde.");

                var balance = serialContext.Balances
                    .Where(x => x.LocationId == split.LocationId
                        && x.StockId == line.StockId
                        && x.YapCodeId == line.YapCodeId
                        && string.Equals(x.UnitCode, line.UnitCode, StringComparison.OrdinalIgnoreCase)
                        && SameTrackingValue(x.SerialNo, split.SerialNo))
                    .OrderByDescending(x => x.AvailableQuantity)
                    .FirstOrDefault();
                if (balance is null || balance.AvailableQuantity + 0.000001m < 1)
                    throw AppException.Conflict($"{serialContext.Locations[split.LocationId].Code} rafında seçilen seri için yeterli stok yok.");

                await ReleaseTransferReservationsAsync(
                    aggregate.Header,
                    $"{reservationPrefix}:release",
                    "Rota güncelleme öncesi rezervasyon salımı",
                    actor,
                    token);

                var utcNow = DateTime.UtcNow;
                ProductionTransferLineSplitHelper.ApplySerialRouteReplacement(
                    tracking,
                    taskLine,
                    line,
                    split.LocationId,
                    split.SerialNo,
                    balance.LotNo,
                    actor,
                    utcNow);

                await uow.SaveChangesAsync(token);

                await ReserveTransferReservationsAsync(
                    aggregate.Header,
                    $"{reservationPrefix}:reserve",
                    actor,
                    token);
                await uow.SaveChangesAsync(token);
                return await GetPickingTableAsync(transferId, actor, token);
            }

            var remaining = taskLine.PlannedQuantity - taskLine.ProcessedQuantity;
            if (remaining <= 0) throw AppException.Conflict("Seçilen satırın güncellenecek kalan miktarı yok.");

            var splits = request.Splits.Where(x => x.Quantity > 0).ToArray();
            var total = splits.Sum(x => x.Quantity);
            if (total <= 0) throw AppException.BadRequest("En az bir raftan miktar girilmelidir.");
            if (total > remaining + 0.000001m)
                throw AppException.BadRequest("Girilen miktarlar kalan ihtiyaçtan fazla olamaz.");

            var link = aggregate.Link;
            var sourceLineLink = link.Lines.Single(x => x.WarehouseTransferLineId == line.Id);
            var currentSourceLocationId = taskLine.SourceLocationId ?? line.DefaultSourceLocationId;
            var excludedLocations = ProductionTransferRouteAllocation.GetRouteRefreshExcludedSourceLocationIds(
                currentSourceLocationId);
            var context = await ProductionTransferPickingSupport.LoadBalanceContextAsync(uow, aggregate.Header, [line], token);
            var subtractSiblingCommitments = aggregate.Header.ReservationPolicy == WarehouseTransferReservationPolicy.None
                && aggregate.Header.Lines.All(x => x.ReservedQuantity <= 0);
            foreach (var split in splits)
            {
                if (excludedLocations.Contains(split.LocationId))
                {
                    var blockedCode = context.Locations.GetValueOrDefault(split.LocationId)?.Code ?? split.LocationId.ToString();
                    throw AppException.Conflict($"{blockedCode} kaynak rafından rotalama yapılamaz.");
                }
                if (!context.Locations.ContainsKey(split.LocationId))
                    throw AppException.BadRequest("Seçilen kaynak raf geçersiz.");
                var available = ProductionTransferRouteAllocation.GetRouteRefreshAvailableAtLocation(
                    split.LocationId,
                    line.StockId,
                    line.YapCodeId,
                    line.UnitCode,
                    context.Balances,
                    task,
                    taskLine,
                    line,
                    link,
                    subtractSiblingCommitments);
                if (available + 0.000001m < split.Quantity)
                    throw AppException.Conflict($"{context.Locations[split.LocationId].Code} rafında yeterli stok yok.");
            }

            var chunks = ProductionTransferRouteAllocation.BuildRouteRefreshSplitChunks(
                remaining,
                currentSourceLocationId,
                splits.Select(x => new RouteAllocationChunk(x.LocationId, x.Quantity, null, null)));
            if (remaining - total > 0.000001m && !currentSourceLocationId.HasValue)
                throw AppException.Conflict("Kalan miktar için mevcut kaynak rafı bulunamadı.");
            var nextLineNo = aggregate.Header.Lines.Max(x => x.LineNo);
            await ReleaseTransferReservationsAsync(
                aggregate.Header,
                $"{reservationPrefix}:release",
                "Rota güncelleme öncesi rezervasyon salımı",
                actor,
                token);

            ProductionTransferLineSplitHelper.ApplyNonSerialRouteChunks(
                aggregate.Header, link, task, taskLine, line, sourceLineLink, chunks, ref nextLineNo, actor,
                DateTime.UtcNow, allowShortageWithoutLocation: false);
            ProductionTransferLineSplitHelper.ConsolidateSameLocationOpenTaskLines(
                aggregate.Header, link, task, actor, DateTime.UtcNow);

            await uow.SaveChangesAsync(token);

            await ReserveTransferReservationsAsync(
                aggregate.Header,
                $"{reservationPrefix}:reserve",
                actor,
                token);

            await uow.SaveChangesAsync(token);
            return await GetPickingTableAsync(transferId, actor, token);
        }, ct, IsolationLevel.Serializable);

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
            var replayTaskLine = await uow.Repository<WarehouseTransferTaskLine>().Query()
                .SingleAsync(x => x.Id == request.ExpectedTaskLineId, ct);
            if (replayHeaderLink.WarehouseTransferHeaderId != transferId
                || replayLineLink.WarehouseTransferLineId != replayTaskLine.WtLineId
                || replay.NormalizedBarcode != normalizedBarcode)
                throw AppException.Conflict("Aynı işlem anahtarı farklı bir barkod toplama isteğinde kullanılamaz.");
            var replayLocation = await uow.Repository<WarehouseLocation>().Query()
                .SingleAsync(x => x.Id == replay.SourceLocationId, ct);
            var replayExecution = await GetAsync(transferId, ct);
            var replayLine = replayExecution.Lines.Single(x => x.LineId == replayTaskLine.WtLineId);
            return new(replayExecution, replayTaskLine.WtLineId, replayLine.StockCode, replay.Quantity,
                replay.SerialNo, replay.LotNo, replay.BarcodeSource, replay.SourceLocationId,
                replayLocation.Code, replayLocation.Name, null);
        }

        var aggregate = await LoadAsync(transferId, false, ct);
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
        var remaining = taskLine.PlannedQuantity - taskLine.ProcessedQuantity;
        if (remaining <= 0)
            throw ProductionTransferBarcodeInput.AlreadyPicked(line.StockCodeSnapshot);
        var waitingLocationId = aggregate.Header.SourceStagingLocationId
            ?? throw AppException.Conflict("Kaynak depo için üretim transfer bekleme rafı tanımlanmamış.");

        var input = ProductionTransferBarcodeInput.Parse(request.Barcode);
        if (input.StockCode is not null
            && !ProductionTransferBarcodeInput.SameStockCode(line.StockCodeSnapshot, input.StockCode))
            throw AppException.Conflict(
                $"Okutulan stok kodu beklenen stokla uyuşmuyor. Beklenen: {line.StockCodeSnapshot}.");
        if (line.Trackings.Count > 0
            && input.StockCode is null
            && input.SerialNo is null
            && !string.IsNullOrWhiteSpace(input.Raw))
        {
            var openRows = (await GetPickingTableAsync(transferId, actor, ct)).Rows
                .Where(x => x.RemainingQuantity > 0)
                .ToArray();
            var unavailableRow = ProductionTransferBarcodeInput.FindUnavailableRow(input, openRows);
            if (unavailableRow is not null)
                throw ProductionTransferBarcodeInput.UnavailableBalance(unavailableRow);
            throw AppException.BadRequest(ProductionTransferBarcodeInput.SerialCompositeFormatMessage);
        }

        if (line.Trackings.Count == 0 && input.StockCode is null && !string.IsNullOrWhiteSpace(input.Raw))
        {
            var openRows = (await GetPickingTableAsync(transferId, actor, ct)).Rows
                .Where(x => x.RemainingQuantity > 0)
                .ToArray();
            var unavailableRow = ProductionTransferBarcodeInput.FindUnavailableNonSerialRow(input, openRows);
            if (unavailableRow is not null && ProductionTransferBarcodeInput.FindMatchingOpenRow(input, openRows) is null)
                throw ProductionTransferBarcodeInput.UnavailableBalance(unavailableRow);
        }

        var expectedSourceLocationId = request.SourceLocationId ?? taskLine.SourceLocationId ?? line.DefaultSourceLocationId;
        var resolved = await barcodeResolver.ResolveAsync(new(
            input.ResolutionBarcode,
            aggregate.Header.BranchCode,
            WarehouseBarcodePurpose.Outbound,
            aggregate.Header.SourceWarehouseId,
            line.StockId,
            expectedSourceLocationId,
            line.YapCodeId,
            line.UnitCode), ct);
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
        var requestedQuantity = request.Quantity ?? remaining;
        if (requestedQuantity <= 0) throw AppException.BadRequest("Toplanacak miktar geçersiz.");
        if (requestedQuantity > remaining) throw AppException.BadRequest("Toplanacak miktar kalan miktardan fazla olamaz.");
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

    private static bool SameTrackingValue(string? left, string? right) =>
        string.Equals(
            string.IsNullOrWhiteSpace(left) ? null : left.Trim(),
            string.IsNullOrWhiteSpace(right) ? null : right.Trim(),
            StringComparison.OrdinalIgnoreCase);

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
            if (picked < requested && !request.ConfirmPartialPicking)
                throw AppException.Conflict("Toplama eksik. Eksik toplamayı bilinçli olarak onaylamadan devam edemezsiniz.");

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

    public Task<ProductionTransferExecutionDto> ResumePickingAsync(
        long transferId,
        ResumeProductionPickingRequest request,
        long actor,
        CancellationToken ct = default) =>
        uow.ExecuteInTransactionAsync(async token =>
        {
            if (request.IdempotencyKey == Guid.Empty) throw AppException.BadRequest("İşlem anahtarı zorunludur.");
            var aggregate = await LoadAsync(transferId, true, token);
            if (aggregate.Link.WorkflowStatus == ProductionTransferWorkflowStatus.Picking)
                return await MapAsync(aggregate.Header, aggregate.Link, token);
            if (aggregate.Link.WorkflowStatus != ProductionTransferWorkflowStatus.AwaitingHandover)
                throw AppException.Conflict("Transfer toplama aşamasına geri alınamaz.");

            _ = ProductionTransferPickingSupport.ResolveActivePickTaskForResume(aggregate.Header, actor);

            var picked = aggregate.Header.Lines.Where(x => !x.IsDeleted).Sum(x => x.PickedQuantity);
            if (picked <= 0) throw AppException.Conflict("Toplanmış stok olmadan toplamaya dönülemez.");

            aggregate.Link.WorkflowStatus = ProductionTransferWorkflowStatus.Picking;
            aggregate.Link.LastPickingCompletionIdempotencyKey = null;
            aggregate.Link.UpdatedBy = actor;
            aggregate.Link.UpdatedDate = DateTime.UtcNow;

            var lines = aggregate.Header.Lines.Where(x => !x.IsDeleted).ToArray();
            aggregate.Header.Status = lines.All(x => x.PickedQuantity >= x.RequestedQuantity)
                ? WarehouseTransferStatus.Picked
                : lines.Sum(x => x.PickedQuantity) > 0
                    ? WarehouseTransferStatus.PartiallyPicked
                    : WarehouseTransferStatus.Picking;
            aggregate.Header.UpdatedBy = actor;
            aggregate.Header.UpdatedDate = DateTime.UtcNow;
            AddHistory(aggregate.Header, aggregate.Header.Status, request.IdempotencyKey, "Toplamaya geri dönüldü", actor);
            await uow.SaveChangesAsync(token);
            await audit.WriteAsync(new("production-transfer.picking.resume", nameof(ProductionTransferHeaderLink), aggregate.Link.Id.ToString(),
                "Succeeded", "production-transfer", NewValues: new { aggregate.Header.DocumentNo, aggregate.Header.Status },
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
            ErpPostingPolicy = originalLink.ErpPostingPolicy,
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

    private static bool UsesTransferReservations(WarehouseTransferHeader header) =>
        header.ReservationPolicy != WarehouseTransferReservationPolicy.None
        || Contexts.Contains(header.BusinessContext);

    private async Task ReleaseTransferReservationsAsync(
        WarehouseTransferHeader header,
        string idempotencyKey,
        string reason,
        long actor,
        CancellationToken token)
    {
        if (!UsesTransferReservations(header)) return;
        if (header.Status is WarehouseTransferStatus.Cancelled or WarehouseTransferStatus.Completed) return;
        await reservations.ReleaseAllAsync(header, idempotencyKey, reason, actor, token);
    }

    private async Task ReserveTransferReservationsAsync(
        WarehouseTransferHeader header,
        string idempotencyKey,
        long actor,
        CancellationToken token)
    {
        if (!UsesTransferReservations(header)) return;
        if (header.Status is WarehouseTransferStatus.Cancelled or WarehouseTransferStatus.Completed) return;
        await reservations.ReserveAsync(header, idempotencyKey, actor, token);
    }

    private async Task<(WarehouseTransferHeader Header, ProductionTransferHeaderLink Link)> LoadAsync(
        long transferId, bool tracking, CancellationToken ct)
    {
        if (transferId <= 0) throw AppException.BadRequest("Transfer kimliği geçersiz.");
        var header = await uow.Repository<WarehouseTransferHeader>().Query(tracking)
            .Include(x => x.Lines).ThenInclude(x => x.Trackings)
            .Include(x => x.Tasks).ThenInclude(x => x.Lines).ThenInclude(x => x.Line).ThenInclude(x => x.Trackings)
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
        var erpPosting = await uow.Repository<ErpPostingRecord>().Query()
            .Where(x => x.SourceType == ErpPostingSourceType.WarehouseTransfer && x.SourceEntityId == header.Id)
            .Select(x => new
            {
                x.Status,
                x.ErpDocumentNo,
                x.LastErrorCode,
                x.LastErrorMessage
            })
            .SingleOrDefaultAsync(ct);

        var lineLinks = link.Lines.ToDictionary(x => x.WarehouseTransferLineId);
        var excludedSourceLocationIds = await ProductionTransferSourceLocationExclusions.FromHeaderAsync(
            uow, header, header.Lines, ct);
        var lines = header.Lines.OrderBy(x => x.LineNo).Select(x =>
        {
            lineLinks.TryGetValue(x.Id, out var lineLink);
            var suggestedLocationId = x.DefaultSourceLocationId;
            if (suggestedLocationId.HasValue && excludedSourceLocationIds.Contains(suggestedLocationId.Value))
                suggestedLocationId = null;
            var routedLocation = suggestedLocationId.HasValue
                && routedLocations.TryGetValue(suggestedLocationId.Value, out var location)
                    ? location
                    : null;
            return new ProductionTransferExecutionLineDto(
                x.Id, x.LineNo, x.StockId, x.StockCodeSnapshot, x.StockNameSnapshot, x.UnitCode,
                x.RequestedQuantity, x.PickedQuantity, lineLink?.HandedOverQuantity ?? 0,
                Math.Max(0, x.RequestedQuantity - x.PickedQuantity),
                Math.Max(0, x.RequestedQuantity - x.PickedQuantity),
                x.TrackingType.ToString(), suggestedLocationId, routedLocation?.Code, routedLocation?.Name);
        }).ToArray();
        var requested = lines.Sum(x => x.RequestedQuantity);
        var picked = lines.Sum(x => x.PickedQuantity);
        var handedOver = lines.Sum(x => x.HandedOverQuantity);
        return new(
            header.Id, header.DocumentNo, link.WorkflowStatus, header.Status.ToString(),
            link.ErpPostingPolicy, header.ErpIntegrationStatus, erpPosting?.Status,
            erpPosting?.ErpDocumentNo, erpPosting?.LastErrorCode, erpPosting?.LastErrorMessage,
            source.Id, source.WarehouseCode, source.WarehouseName,
            target.Id, target.WarehouseCode, target.WarehouseName,
            waiting?.Id, waiting?.Code, waiting?.Name,
            link.RequestedByUserId, link.RequestedByNameSnapshot,
            link.HandoverConfirmedBy, link.HandoverConfirmedAtUtc, link.HandoverShortageReason,
            link.ParentWarehouseTransferHeaderId, link.ResidualWarehouseTransferHeaderId, residualDocumentNo,
            requested, picked, handedOver, Math.Max(0, requested - picked),
            picked > 0 && link.WorkflowStatus is ProductionTransferWorkflowStatus.Planned or ProductionTransferWorkflowStatus.Picking,
            link.WorkflowStatus == ProductionTransferWorkflowStatus.AwaitingHandover,
            excludedSourceLocationIds.ToArray(),
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
