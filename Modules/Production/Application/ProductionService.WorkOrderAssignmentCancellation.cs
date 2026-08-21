using System.Data;
using Microsoft.EntityFrameworkCore;
using verii_wms_api_v2.Modules.Production.Domain;
using verii_wms_api_v2.Modules.ProductionTransfer.Application;
using verii_wms_api_v2.Modules.ProductionTransfer.Domain;
using verii_wms_api_v2.Modules.WarehouseTransfer.Application;
using verii_wms_api_v2.Modules.WarehouseTransfer.Domain;
using verii_wms_api_v2.Shared.Application.Exceptions;
using StockEntity = verii_wms_api_v2.Modules.Stock.Domain.Stock;
using WarehouseEntity = verii_wms_api_v2.Modules.Warehouse.Domain.Warehouse;
using YapCodeEntity = verii_wms_api_v2.Modules.YapCode.Domain.YapCode;

namespace verii_wms_api_v2.Modules.Production.Application;

public sealed partial class ProductionService
{
    public async Task<IReadOnlyList<ProductionSourceWorkOrderRow>> GetCancelledWorkOrderAssignmentsAsync(
        string? search,
        string branchCode,
        int take = 200,
        CancellationToken ct = default)
    {
        var branch = branchCode.Trim();
        var boundedTake = Math.Clamp(take, 1, 500);
        if (!int.TryParse(branch, out var branchNumber))
            throw AppException.BadRequest("Oturum şube kodu sayısal değildir.");

        var cancellations = await uow.Repository<ProductionWorkOrderAssignmentCancellation>().Query()
            .AsNoTracking()
            .Where(x => x.BranchCode == branch
                && x.Status == ProductionWorkOrderAssignmentCancellationStatus.Active
                && !x.IsDeleted)
            .Include(x => x.Lines.Where(line => !line.IsDeleted))
            .OrderByDescending(x => x.CancelledAtUtc)
            .Take(1000)
            .ToListAsync(ct);

        if (cancellations.Count == 0) return [];

        var rows = new List<ProductionSourceWorkOrderRow>(boundedTake);
        foreach (var cancellation in cancellations)
        {
            if (!string.IsNullOrWhiteSpace(search)
                && !cancellation.WorkOrderNumber.Contains(search.Trim(), StringComparison.OrdinalIgnoreCase))
                continue;

            var template = await ResolveSourceWorkOrderTemplateAsync(
                cancellation.WorkOrderNumber,
                branch,
                branchNumber,
                (cancellation.SourceType, cancellation.SourceSystemCode),
                ct);

            if (template is not null)
            {
                rows.Add(template with
                {
                    ListingKind = ProductionSourceWorkOrderListingKind.ManagerCancelledAssignment,
                    CancellationId = cancellation.Id,
                });
                continue;
            }

            rows.Add(new ProductionSourceWorkOrderRow(
                cancellation.SourceType,
                cancellation.SourceSystemCode,
                1,
                cancellation.WorkOrderNumber,
                branchNumber,
                string.Empty,
                string.Empty,
                null,
                0,
                null,
                0,
                cancellation.CancelledAtUtc.UtcDateTime,
                null,
                null,
                0,
                0,
                false,
                ProductionSourceWorkOrderListingKind.ManagerCancelledAssignment,
                null,
                null,
                cancellation.Id));
        }

        return rows
            .OrderByDescending(x => x.WorkOrderDate)
            .ThenBy(x => x.WorkOrderNumber, StringComparer.OrdinalIgnoreCase)
            .Take(boundedTake)
            .ToArray();
    }

    public async Task<PreparedNetsisProductionWorkOrder> GetCancelledWorkOrderAssignmentDetailAsync(
        long cancellationId,
        string branchCode,
        CancellationToken ct = default)
    {
        var branch = branchCode.Trim();
        if (!int.TryParse(branch, out var branchNumber))
            throw AppException.BadRequest("Oturum şube kodu sayısal değildir.");
        if (cancellationId <= 0)
            throw AppException.BadRequest("İptal kaydı numarası zorunludur.");

        var cancellation = await uow.Repository<ProductionWorkOrderAssignmentCancellation>().Query()
            .AsNoTracking()
            .Include(x => x.Lines.Where(line => !line.IsDeleted))
            .FirstOrDefaultAsync(x => x.Id == cancellationId
                && x.BranchCode == branch
                && x.Status == ProductionWorkOrderAssignmentCancellationStatus.Active
                && !x.IsDeleted, ct)
            ?? throw AppException.NotFound("İptal edilen iş emri ataması bulunamadı.");

        var template = await ResolveSourceWorkOrderTemplateAsync(
            cancellation.WorkOrderNumber,
            branch,
            branchNumber,
            (cancellation.SourceType, cancellation.SourceSystemCode),
            ct);

        var lines = cancellation.Lines
            .Where(line => !line.IsDeleted)
            .OrderBy(line => line.OperationNumber)
            .ThenBy(line => line.Id)
            .ToArray();
        var stockIds = lines.Where(line => line.StockId.HasValue).Select(line => line.StockId!.Value).Distinct().ToArray();
        var yapIds = lines.Where(line => line.YapCodeId.HasValue).Select(line => line.YapCodeId!.Value).Distinct().ToArray();
        var stocks = stockIds.Length == 0
            ? []
            : await uow.Repository<StockEntity>().Query()
                .AsNoTracking()
                .Where(stock => stock.BranchCode == branch && stockIds.Contains(stock.Id))
                .ToListAsync(ct);
        var stockMap = stocks.ToDictionary(stock => stock.Id);
        var yapCodes = yapIds.Length == 0
            ? []
            : await uow.Repository<YapCodeEntity>().Query()
                .AsNoTracking()
                .Where(yap => yap.BranchCode == branch && yapIds.Contains(yap.Id))
                .ToListAsync(ct);
        var yapMap = yapCodes.ToDictionary(yap => yap.Id);

        var warehouseCodes = new[] { template?.IssueWarehouseCode ?? 0, template?.WarehouseCode ?? 0 }
            .Where(code => code > 0)
            .Distinct()
            .ToArray();
        var warehouses = warehouseCodes.Length == 0
            ? []
            : await uow.Repository<WarehouseEntity>().Query()
                .AsNoTracking()
                .Where(warehouse => warehouse.BranchCode == branch && warehouseCodes.Contains(warehouse.WarehouseCode))
                .ToListAsync(ct);
        var warehouseMap = warehouses.ToDictionary(warehouse => warehouse.WarehouseCode);
        warehouseMap.TryGetValue(template?.IssueWarehouseCode ?? 0, out var sourceWarehouse);
        warehouseMap.TryGetValue(template?.WarehouseCode ?? 0, out var targetWarehouse);

        var materials = lines.Select(line =>
        {
            stockMap.TryGetValue(line.StockId ?? 0, out var stock);
            yapMap.TryGetValue(line.YapCodeId ?? 0, out var yap);
            return new PreparedNetsisProductionMaterial(
                line.StockId,
                stock?.ErpStockCode ?? (line.StockId.HasValue ? $"#{line.StockId}" : "—"),
                stock?.StockName,
                stock?.BaseUnitCode ?? "ADET",
                line.YapCodeId,
                yap?.ConfigurationCode,
                line.OperationNumber,
                line.CancelledQuantity,
                0,
                line.CancelledQuantity,
                null);
        }).ToArray();

        return new PreparedNetsisProductionWorkOrder(
            cancellation.SourceType,
            cancellation.SourceSystemCode,
            cancellation.WorkOrderNumber,
            branchNumber,
            template?.StockCode ?? string.Empty,
            template?.StockName ?? string.Empty,
            template?.UnitCode ?? "ADET",
            template?.WorkOrderQuantity ?? materials.Sum(material => material.RequiredQuantity),
            null,
            null,
            template?.ConfigurationCode,
            sourceWarehouse?.Id,
            template?.IssueWarehouseCode ?? 0,
            sourceWarehouse?.WarehouseName,
            targetWarehouse?.Id,
            template?.WarehouseCode ?? 0,
            targetWarehouse?.WarehouseName,
            template?.WorkOrderDate,
            template?.DeliveryDate,
            template?.ProjectCode,
            template?.IsClosed ?? false,
            null,
            null,
            null,
            [],
            materials,
            [],
            ProductionSourceWorkOrderListingKind.ManagerCancelledAssignment,
            Description: string.IsNullOrWhiteSpace(cancellation.Reason) ? template?.Description : cancellation.Reason.Trim());
    }

    public Task<ProductionWorkOrderAssignmentCancellationResult> CancelWorkOrderAssignmentAsync(
        CancelProductionWorkOrderAssignmentRequest request,
        string branchCode,
        long actor,
        CancellationToken ct = default) =>
        uow.ExecuteInTransactionAsync(async token =>
        {
            ValidateCancellationReason(request.Reason);
            var workOrderNumber = request.WorkOrderNumber?.Trim()
                ?? throw AppException.BadRequest("İş emri numarası zorunludur.");
            var branch = branchCode.Trim();
            var setting = await GetSourceSettingAsync(branch, token);
            var sourceType = request.SourceType ?? setting.Source;
            var sourceSystemCode = request.SourceSystemCode?.Trim()
                ?? (sourceType == ProductionOrderSourceType.WmsIntegrationTables ? setting.SourceSystemCode : "NETSIS");

            var replay = await uow.Repository<ProductionWorkOrderAssignmentCancellation>().Query()
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.BranchCode == branch && x.CorrelationId == request.IdempotencyKey, token);
            if (replay is not null)
                return MapCancellationResult(replay, replay.Lines.Where(x => !x.IsDeleted).Sum(x => x.CancelledQuantity), true);

            var templateRow = new ProductionSourceWorkOrderRow(
                sourceType,
                sourceSystemCode,
                1,
                workOrderNumber,
                int.TryParse(branch, out var branchNumber) ? branchNumber : 0,
                string.Empty,
                string.Empty,
                null,
                0,
                null,
                0,
                null,
                null,
                null,
                0,
                0,
                false);

            var scopedTransferId = request.TransferId;
            var scopedKalanTaskId = request.KalanTaskId;
            if (scopedTransferId is long transferId && scopedKalanTaskId is null)
            {
                scopedKalanTaskId = await TryResolveCancellationReturnKalanTaskIdAsync(branch, transferId, token);
                if (scopedKalanTaskId is null)
                    throw AppException.Conflict("Transfer iadesi kalan görevi bulunamadı.");
            }

            var isCancellationReturnScope = scopedTransferId.HasValue && scopedKalanTaskId.HasValue;

            Dictionary<ProductionRecipeMaterialKey, decimal> cancellable;
            if (isCancellationReturnScope)
            {
                cancellable = await BuildCancellableRemainingQuantitiesAsync(
                    branch,
                    templateRow,
                    scopedTransferId!.Value,
                    scopedKalanTaskId!.Value,
                    token);
            }
            else
            {
                cancellable = await BuildCancellableRemainingQuantitiesAsync(
                    branch,
                    templateRow,
                    null,
                    null,
                    token);
            }
            if (cancellable.Count == 0)
                throw AppException.Conflict("Bu iş emri için iptal edilebilir atanmamış malzeme kalmadı.");

            var requested = ResolveRequestedCancellationQuantities(request.Lines, cancellable);
            IReadOnlySet<ProductionRecipeMaterialKey> draftRevertedKeys;
            if (isCancellationReturnScope)
            {
                draftRevertedKeys = await CancelOpenTransfersForMaterialsAsync(
                    branch,
                    workOrderNumber,
                    scopedTransferId: null,
                    excludeTransferHeaderId: scopedTransferId!.Value,
                    cancelledQuantities: requested,
                    reason: request.Reason.Trim(),
                    idempotencyKey: request.IdempotencyKey,
                    actor,
                    token);
                await CancelCancellationReturnKalanTaskAsync(
                    branch,
                    scopedTransferId!.Value,
                    scopedKalanTaskId!.Value,
                    actor,
                    token);
            }
            else
            {
                draftRevertedKeys = await CancelOpenTransfersForMaterialsAsync(
                    branch,
                    workOrderNumber,
                    request.TransferId,
                    excludeTransferHeaderId: null,
                    cancelledQuantities: requested,
                    reason: request.Reason.Trim(),
                    idempotencyKey: request.IdempotencyKey,
                    actor,
                    token);
            }
            var managerCancelled = ResolveManagerCancellationQuantities(requested, draftRevertedKeys);
            if (managerCancelled.Count == 0 && draftRevertedKeys.Count == 0)
                throw AppException.Conflict("Bu iş emri için iptal edilebilir malzeme bulunamadı.");

            ProductionWorkOrderAssignmentCancellation? cancellation = null;
            if (managerCancelled.Count > 0)
            {
                cancellation = await UpsertActiveCancellationAsync(
                    branch,
                    workOrderNumber,
                    sourceType,
                    sourceSystemCode,
                    request.Reason.Trim(),
                    request.IdempotencyKey,
                    managerCancelled,
                    request.TransferId,
                    actor,
                    token);
            }

            await uow.SaveChangesAsync(token);
            if (cancellation is not null)
            {
                await audit.WriteAsync(new(
                    "production.work-order-assignment.cancel",
                    nameof(ProductionWorkOrderAssignmentCancellation),
                    cancellation.Id.ToString(),
                    "Succeeded",
                    "production",
                    NewValues: new { workOrderNumber, cancellation.Status, lines = ToAuditMaterialQuantities(managerCancelled) }),
                    token);
                return MapCancellationResult(
                    cancellation,
                    cancellation.Lines.Where(x => !x.IsDeleted).Sum(x => x.CancelledQuantity),
                    false);
            }

            await audit.WriteAsync(new(
                "production.work-order-assignment.draft-revert",
                nameof(WarehouseTransferHeader),
                request.TransferId?.ToString() ?? workOrderNumber,
                "Succeeded",
                "production",
                NewValues: new { workOrderNumber, lines = ToAuditMaterialQuantities(
                    requested.Where(x => draftRevertedKeys.Contains(x.Key)).ToDictionary(x => x.Key, x => x.Value)) }),
                token);

            return new ProductionWorkOrderAssignmentCancellationResult(
                0,
                workOrderNumber,
                ProductionWorkOrderAssignmentCancellationStatus.Active,
                0,
                false);
        }, ct, IsolationLevel.Serializable);

    public Task<ProductionWorkOrderAssignmentCancellationResult> RestoreWorkOrderAssignmentAsync(
        RestoreProductionWorkOrderAssignmentRequest request,
        string branchCode,
        long actor,
        CancellationToken ct = default) =>
        uow.ExecuteInTransactionAsync(async token =>
        {
            var workOrderNumber = request.WorkOrderNumber?.Trim()
                ?? throw AppException.BadRequest("İş emri numarası zorunludur.");
            var branch = branchCode.Trim();

            var replay = await uow.Repository<ProductionWorkOrderAssignmentCancellation>().Query(true)
                .Include(x => x.Lines.Where(line => !line.IsDeleted))
                .FirstOrDefaultAsync(x => x.BranchCode == branch
                    && x.WorkOrderNumber == workOrderNumber
                    && x.Status == ProductionWorkOrderAssignmentCancellationStatus.Restored
                    && x.CorrelationId == request.IdempotencyKey, token);
            if (replay is not null)
                return MapCancellationResult(replay, 0, true);

            var cancellation = await uow.Repository<ProductionWorkOrderAssignmentCancellation>().Query(true)
                .Include(x => x.Lines.Where(line => !line.IsDeleted))
                .FirstOrDefaultAsync(x => x.BranchCode == branch
                    && x.WorkOrderNumber == workOrderNumber
                    && x.Status == ProductionWorkOrderAssignmentCancellationStatus.Active, token)
                ?? throw AppException.NotFound("Aktif iptal edilmiş iş emri ataması bulunamadı.");

            var activeLines = cancellation.Lines.Where(x => !x.IsDeleted && x.CancelledQuantity > 0).ToArray();
            if (activeLines.Length == 0)
                throw AppException.Conflict("Geri getirilecek iptal miktarı bulunamadı.");

            var activeTotals = AggregateCancellationLines(activeLines);
            var restoreTotals = request.Lines is { Count: > 0 }
                ? ResolveRequestedCancellationQuantities(request.Lines, activeTotals)
                : activeTotals;

            foreach (var (key, quantity) in restoreTotals)
            {
                if (quantity <= 0) continue;
                var line = activeLines.FirstOrDefault(x =>
                    ProductionWorkOrderMaterialAssignment.CreateKey(x.StockId, x.YapCodeId, x.OperationNumber).Equals(key));
                if (line is null || line.CancelledQuantity + 0.0001m < quantity)
                    throw AppException.BadRequest("Geri getirme miktarı iptal edilmiş miktarı aşıyor.");

                var fullyRestoringLine = quantity + 0.0001m >= line.CancelledQuantity;
                line.UpdatedBy = actor;
                line.UpdatedDate = DateTime.UtcNow;
                if (fullyRestoringLine)
                {
                    // CHECK constraint requires CancelledQuantity > 0; soft-delete without zeroing.
                    line.IsDeleted = true;
                    line.DeletedBy = actor;
                    line.DeletedDate = DateTime.UtcNow;
                    continue;
                }

                line.CancelledQuantity -= quantity;
            }

            var restoredTransferIds = activeLines
                .Where(line => line.SourceTransferHeaderId.HasValue
                    && restoreTotals.ContainsKey(ProductionWorkOrderMaterialAssignment.CreateKey(
                        line.StockId,
                        line.YapCodeId,
                        line.OperationNumber)))
                .Select(line => line.SourceTransferHeaderId!.Value)
                .Distinct()
                .ToArray();
            foreach (var transferId in restoredTransferIds)
                await TryRestoreAtanmayanlarPickTaskAsync(branch, transferId, actor, token);

            var remainingCancelled = cancellation.Lines
                .Where(x => !x.IsDeleted && x.CancelledQuantity > 0)
                .Sum(x => x.CancelledQuantity);
            cancellation.RestoredAtUtc = DateTimeOffset.UtcNow;
            cancellation.RestoredBy = actor;
            if (remainingCancelled <= 0.0001m)
            {
                cancellation.Status = ProductionWorkOrderAssignmentCancellationStatus.Restored;
                cancellation.CorrelationId = request.IdempotencyKey;
            }

            cancellation.UpdatedBy = actor;
            cancellation.UpdatedDate = DateTime.UtcNow;
            await uow.SaveChangesAsync(token);
            await audit.WriteAsync(new(
                "production.work-order-assignment.restore",
                nameof(ProductionWorkOrderAssignmentCancellation),
                cancellation.Id.ToString(),
                "Succeeded",
                "production",
                NewValues: new { workOrderNumber, lines = ToAuditMaterialQuantities(restoreTotals), cancellation.Status }),
                token);

            return MapCancellationResult(cancellation, remainingCancelled, false);
        }, ct, IsolationLevel.Serializable);

    private async Task<Dictionary<ProductionRecipeMaterialKey, decimal>> BuildCancellableRemainingQuantitiesAsync(
        string branch,
        ProductionSourceWorkOrderRow templateRow,
        long? cancellationReturnTransferId,
        long? cancellationReturnKalanTaskId,
        CancellationToken ct)
    {
        if (cancellationReturnTransferId is long transferId && cancellationReturnKalanTaskId is long kalanTaskId)
        {
            var split = await ResolveCancellationReturnRemainderMaterialSplitAsync(
                branch,
                templateRow,
                transferId,
                kalanTaskId,
                ct);
            return ToCancellableQuantityMap(split.Remaining);
        }

        var recipeMaterials = await LoadFullRecipeMaterialsAsync(templateRow, branch, ct);
        if (recipeMaterials.Count == 0) return [];

        var assignedMaterials = await LoadAssignedMaterialQuantitiesAsync(branch, templateRow.WorkOrderNumber, ct);
        var partialTransferRemainders = await LoadPartialTransferRemainderMaterialQuantitiesAsync(branch, templateRow.WorkOrderNumber, ct);
        var cancelledMaterials = await LoadCancelledMaterialQuantitiesAsync(branch, templateRow.WorkOrderNumber, ct);

        var splitMaterials = ProductionWorkOrderMaterialAssignment.SplitByAssignedCoverage(recipeMaterials, assignedMaterials);
        var reclassified = ApplyPartialTransferRemainderReclassification(
            recipeMaterials,
            splitMaterials,
            partialTransferRemainders);

        var totals = ToCancellableQuantityMap(reclassified.Remaining);

        foreach (var (key, cancelledQuantity) in cancelledMaterials)
            totals[key] = Math.Max(0, totals.GetValueOrDefault(key) - cancelledQuantity);

        return totals
            .Where(x => x.Value > 0.0001m)
            .ToDictionary(x => x.Key, x => x.Value);
    }

    private static Dictionary<ProductionRecipeMaterialKey, decimal> ToCancellableQuantityMap(
        IEnumerable<PreparedNetsisProductionMaterial> materials)
    {
        var totals = new Dictionary<ProductionRecipeMaterialKey, decimal>();
        foreach (var material in materials)
        {
            var key = ProductionWorkOrderMaterialAssignment.CreateKey(
                material.StockId,
                material.YapCodeId,
                material.OperationNumber);
            totals[key] = totals.GetValueOrDefault(key) + material.RequiredQuantity;
        }

        return totals;
    }

    private async Task<long?> TryResolveCancellationReturnKalanTaskIdAsync(
        string branch,
        long transferId,
        CancellationToken ct)
    {
        var contexts = ProductionSourceWorkOrderAssignmentFilter.ProductionContexts;
        var link = await uow.Repository<ProductionTransferHeaderLink>().Query()
            .AsNoTracking()
            .Where(x => x.BranchCode == branch
                && x.WarehouseTransferHeaderId == transferId
                && contexts.Contains(x.WarehouseTransferHeader.BusinessContext))
            .Include(x => x.WarehouseTransferHeader)
                .ThenInclude(h => h.Tasks.Where(task => !task.IsDeleted))
            .SingleOrDefaultAsync(ct);
        if (link is null) return null;

        var tasks = link.WarehouseTransferHeader.Tasks.Where(x => !x.IsDeleted).ToArray();
        var kalanTasks = tasks
            .Where(task => ProductionWorkOrderTransferGrouping.IsPostCancellationReturnUnassignedPickTask(task, tasks))
            .ToArray();
        return kalanTasks.Length == 1 ? kalanTasks[0].Id : null;
    }

    private async Task CancelCancellationReturnKalanTaskAsync(
        string branch,
        long transferId,
        long kalanTaskId,
        long actor,
        CancellationToken ct)
    {
        var contexts = ProductionSourceWorkOrderAssignmentFilter.ProductionContexts;
        var link = await uow.Repository<ProductionTransferHeaderLink>().Query(true)
            .Where(x => x.BranchCode == branch
                && x.WarehouseTransferHeaderId == transferId
                && contexts.Contains(x.WarehouseTransferHeader.BusinessContext))
            .Include(x => x.WarehouseTransferHeader)
                .ThenInclude(h => h.Tasks.Where(task => !task.IsDeleted))
                    .ThenInclude(task => task.Assignments)
            .SingleOrDefaultAsync(ct)
            ?? throw AppException.NotFound("İptal kalanı transferi bulunamadı.");

        var tasks = link.WarehouseTransferHeader.Tasks.Where(x => !x.IsDeleted).ToArray();
        var kalanTask = tasks.SingleOrDefault(x => x.Id == kalanTaskId)
            ?? throw AppException.NotFound("İptal kalanı görevi bulunamadı.");
        if (!ProductionWorkOrderTransferGrouping.IsCancellableAtanmayanlarPickTask(kalanTask, link, tasks))
            throw AppException.Conflict("Seçilen görev Atanmayanlar kuyruğunda iptal edilebilir bir toplama görevi değildir.");
        if (kalanTask.Status is WarehouseTransferTaskStatus.Completed or WarehouseTransferTaskStatus.Cancelled)
            return;

        var now = DateTime.UtcNow;
        kalanTask.Status = WarehouseTransferTaskStatus.Cancelled;
        kalanTask.UpdatedBy = actor;
        kalanTask.UpdatedDate = now;
    }

    private async Task TryRestoreAtanmayanlarPickTaskAsync(
        string branch,
        long transferId,
        long actor,
        CancellationToken ct)
    {
        var contexts = ProductionSourceWorkOrderAssignmentFilter.ProductionContexts;
        var link = await uow.Repository<ProductionTransferHeaderLink>().Query(true)
            .Where(x => x.BranchCode == branch
                && x.WarehouseTransferHeaderId == transferId
                && contexts.Contains(x.WarehouseTransferHeader.BusinessContext))
            .Include(x => x.WarehouseTransferHeader)
                .ThenInclude(h => h.Tasks.Where(task => !task.IsDeleted))
                    .ThenInclude(task => task.Assignments)
            .SingleOrDefaultAsync(ct);
        if (link is null) return;

        var header = link.WarehouseTransferHeader;
        var tasks = header.Tasks.Where(x => !x.IsDeleted).ToArray();
        var kalanTask = tasks
            .Where(task => ProductionWorkOrderTransferGrouping.IsRestorableAtanmayanlarPickTask(task, link, tasks))
            .OrderByDescending(task => task.UpdatedDate ?? task.CreatedDate)
            .ThenByDescending(task => task.Id)
            .FirstOrDefault();
        if (kalanTask is null) return;

        var now = DateTime.UtcNow;
        kalanTask.Status = WarehouseTransferTaskStatus.Open;
        kalanTask.UpdatedBy = actor;
        kalanTask.UpdatedDate = now;

        if (link.WorkflowStatus == ProductionTransferWorkflowStatus.Cancelled)
        {
            link.WorkflowStatus = ProductionTransferWorkflowStatus.Planned;
            link.UpdatedBy = actor;
            link.UpdatedDate = now;
        }

        if (header.Status == WarehouseTransferStatus.Cancelled)
        {
            header.Status = tasks.Any(task =>
                    task.Id != kalanTask.Id
                    && task.Status == WarehouseTransferTaskStatus.Completed)
                ? WarehouseTransferStatus.Released
                : WarehouseTransferStatus.Draft;
            header.UpdatedBy = actor;
            header.UpdatedDate = now;
        }
    }

    private async Task<Dictionary<ProductionRecipeMaterialKey, decimal>> LoadCancelledMaterialQuantitiesAsync(
        string branch,
        string workOrderNumber,
        CancellationToken ct)
    {
        var normalized = workOrderNumber.Trim();
        var cancellation = await uow.Repository<ProductionWorkOrderAssignmentCancellation>().Query()
            .AsNoTracking()
            .Where(x => x.BranchCode == branch
                && x.WorkOrderNumber == normalized
                && x.Status == ProductionWorkOrderAssignmentCancellationStatus.Active
                && !x.IsDeleted)
            .Include(x => x.Lines.Where(line => !line.IsDeleted))
            .FirstOrDefaultAsync(ct);

        if (cancellation is null) return [];

        return AggregateCancellationLines(cancellation.Lines.Where(x => !x.IsDeleted));
    }

    private static Dictionary<ProductionRecipeMaterialKey, decimal> AggregateCancellationLines(
        IEnumerable<ProductionWorkOrderAssignmentCancellationLine> lines)
    {
        var totals = new Dictionary<ProductionRecipeMaterialKey, decimal>();
        foreach (var line in lines)
        {
            if (line.CancelledQuantity <= 0) continue;
            var key = ProductionWorkOrderMaterialAssignment.CreateKey(line.StockId, line.YapCodeId, line.OperationNumber);
            totals[key] = totals.GetValueOrDefault(key) + line.CancelledQuantity;
        }

        return totals;
    }

    private static Dictionary<ProductionRecipeMaterialKey, decimal> ResolveRequestedCancellationQuantities(
        IReadOnlyList<CancelProductionWorkOrderAssignmentLineRequest>? requestedLines,
        IReadOnlyDictionary<ProductionRecipeMaterialKey, decimal> cancellable)
    {
        if (requestedLines is null || requestedLines.Count == 0)
            return cancellable.ToDictionary(x => x.Key, x => x.Value);

        var result = new Dictionary<ProductionRecipeMaterialKey, decimal>();
        foreach (var line in requestedLines)
        {
            if (line.Quantity <= 0) continue;
            var key = ProductionWorkOrderMaterialAssignment.CreateKey(line.StockId, line.YapCodeId, line.OperationNumber);
            if (!cancellable.TryGetValue(key, out var available) || available + 0.0001m < line.Quantity)
                throw AppException.BadRequest("İptal miktarı atanmamış/kalan miktarı aşıyor.");
            result[key] = result.GetValueOrDefault(key) + line.Quantity;
        }

        if (result.Count == 0)
            throw AppException.BadRequest("İptal edilecek en az bir malzeme satırı seçilmelidir.");

        return result;
    }

    private static Dictionary<ProductionRecipeMaterialKey, decimal> ResolveManagerCancellationQuantities(
        IReadOnlyDictionary<ProductionRecipeMaterialKey, decimal> requested,
        IReadOnlySet<ProductionRecipeMaterialKey> draftRevertedKeys) =>
        ProductionWorkOrderMaterialAssignment.ResolveManagerCancellationQuantities(requested, draftRevertedKeys);

    private async Task<IReadOnlySet<ProductionRecipeMaterialKey>> CancelOpenTransfersForMaterialsAsync(
        string branch,
        string workOrderNumber,
        long? scopedTransferId,
        long? excludeTransferHeaderId,
        IReadOnlyDictionary<ProductionRecipeMaterialKey, decimal> cancelledQuantities,
        string reason,
        Guid idempotencyKey,
        long actor,
        CancellationToken ct)
    {
        var draftRevertedKeys = new HashSet<ProductionRecipeMaterialKey>();
        var contexts = ProductionSourceWorkOrderAssignmentFilter.ProductionContexts;
        var links = await uow.Repository<ProductionTransferHeaderLink>().Query()
            .Where(x => x.BranchCode == branch
                && contexts.Contains(x.WarehouseTransferHeader.BusinessContext)
                && x.WarehouseTransferHeader.Status != WarehouseTransferStatus.Cancelled
                && x.WorkflowStatus != ProductionTransferWorkflowStatus.Cancelled
                && (x.ProductionOrderNo == workOrderNumber
                    || x.WarehouseTransferHeader.ExternalReferenceNo == workOrderNumber)
                && (!scopedTransferId.HasValue || x.WarehouseTransferHeaderId == scopedTransferId.Value)
                && (!excludeTransferHeaderId.HasValue || x.WarehouseTransferHeaderId != excludeTransferHeaderId.Value))
            .Include(x => x.WarehouseTransferHeader)
                .ThenInclude(h => h.Lines.Where(line => !line.IsDeleted))
                    .ThenInclude(line => line.Trackings.Where(tracking => !tracking.IsDeleted))
            .Include(x => x.Lines.Where(line => !line.IsDeleted))
            .ToListAsync(ct);

        foreach (var link in links)
        {
            if (ProductionWorkOrderTransferGrouping.IsOpenPartialTransferRemainderLink(link))
                continue;

            var header = link.WarehouseTransferHeader;
            if (header.Status is WarehouseTransferStatus.Completed or WarehouseTransferStatus.CompletedWithShortage)
                continue;

            var hasPicked = header.Lines.Any(x =>
                !x.IsDeleted && ProductionWorkOrderMaterialAssignment.ResolveEffectivePickedQuantity(x) > 0);
            if (hasPicked)
                throw AppException.Conflict(
                    $"{header.DocumentNo} transferinde toplanmış stok olduğu için iş emri iptali yapılamaz. Önce iptal iadesini tamamlayın.");

            var affectedLinkLines = link.Lines
                .Where(linkLine => !linkLine.IsDeleted)
                .Where(linkLine =>
                {
                    var transferLine = linkLine.WarehouseTransferLine;
                    if (transferLine is null || transferLine.IsDeleted) return false;
                    var operationNumber = ProductionWorkOrderMaterialAssignment.TryParseOperationNumber(
                        linkLine.RequirementReference,
                        out var parsedOperation)
                        ? parsedOperation
                        : 0;
                    var key = ProductionWorkOrderMaterialAssignment.CreateKey(
                        transferLine.StockId,
                        transferLine.YapCodeId,
                        operationNumber);
                    return cancelledQuantities.ContainsKey(key);
                })
                .ToArray();

            if (affectedLinkLines.Length == 0) continue;

            if (header.Status == WarehouseTransferStatus.Draft)
            {
                foreach (var linkLine in affectedLinkLines)
                {
                    var transferLine = linkLine.WarehouseTransferLine;
                    if (transferLine is null || transferLine.IsDeleted) continue;
                    var operationNumber = ProductionWorkOrderMaterialAssignment.TryParseOperationNumber(
                        linkLine.RequirementReference,
                        out var parsedOperation)
                        ? parsedOperation
                        : 0;
                    draftRevertedKeys.Add(ProductionWorkOrderMaterialAssignment.CreateKey(
                        transferLine.StockId,
                        transferLine.YapCodeId,
                        operationNumber));
                }

                var affectedLineIds = affectedLinkLines
                    .Select(x => x.WarehouseTransferLineId)
                    .Distinct()
                    .ToArray();
                var activeLineCount = header.Lines.Count(x => !x.IsDeleted);
                if (affectedLineIds.Length >= activeLineCount)
                {
                    await productionTransfers.DeleteDraftAsync(header.Id, actor, ct);
                    continue;
                }

                await productionTransfers.WithdrawDraftLinesAsync(
                    header.Id,
                    new WithdrawProductionTransferDraftLinesRequest(affectedLineIds, reason),
                    actor,
                    ct);
                continue;
            }

            await cancellationCoordinator.CancelWarehouseTransferAsync(
                header.Id,
                new WarehouseTransferTransitionRequest(idempotencyKey, reason),
                actor,
                ct);
        }

        return draftRevertedKeys;
    }

    private async Task<ProductionWorkOrderAssignmentCancellation> UpsertActiveCancellationAsync(
        string branch,
        string workOrderNumber,
        ProductionOrderSourceType sourceType,
        string sourceSystemCode,
        string reason,
        Guid correlationId,
        IReadOnlyDictionary<ProductionRecipeMaterialKey, decimal> cancelledQuantities,
        long? sourceTransferHeaderId,
        long actor,
        CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var cancellation = await uow.Repository<ProductionWorkOrderAssignmentCancellation>().Query(true)
            .Include(x => x.Lines.Where(line => !line.IsDeleted))
            .FirstOrDefaultAsync(x => x.BranchCode == branch
                && x.WorkOrderNumber == workOrderNumber
                && x.Status == ProductionWorkOrderAssignmentCancellationStatus.Active, ct);

        if (cancellation is null)
        {
            cancellation = new ProductionWorkOrderAssignmentCancellation
            {
                BranchCode = branch,
                WorkOrderNumber = workOrderNumber,
                SourceType = sourceType,
                SourceSystemCode = sourceSystemCode,
                Status = ProductionWorkOrderAssignmentCancellationStatus.Active,
                Reason = reason,
                CorrelationId = correlationId,
                CancelledAtUtc = DateTimeOffset.UtcNow,
                CancelledBy = actor,
                CreatedBy = actor,
                CreatedDate = now,
            };
            await uow.Repository<ProductionWorkOrderAssignmentCancellation>().AddAsync(cancellation, ct);
        }
        else
        {
            cancellation.Reason = reason;
            cancellation.RestoredAtUtc = null;
            cancellation.RestoredBy = null;
            cancellation.UpdatedBy = actor;
            cancellation.UpdatedDate = now;
        }

        foreach (var (key, quantity) in cancelledQuantities)
        {
            var existingLine = cancellation.Lines.FirstOrDefault(x =>
                !x.IsDeleted
                && x.StockId == key.StockId
                && x.YapCodeId == key.YapCodeId
                && x.OperationNumber == key.OperationNumber);
            if (existingLine is null)
            {
                cancellation.Lines.Add(new ProductionWorkOrderAssignmentCancellationLine
                {
                    BranchCode = branch,
                    Cancellation = cancellation,
                    StockId = key.StockId,
                    YapCodeId = key.YapCodeId,
                    OperationNumber = key.OperationNumber,
                    CancelledQuantity = quantity,
                    SourceTransferHeaderId = sourceTransferHeaderId,
                    CreatedBy = actor,
                    CreatedDate = now,
                });
                continue;
            }

            existingLine.CancelledQuantity += quantity;
            existingLine.UpdatedBy = actor;
            existingLine.UpdatedDate = now;
        }

        return cancellation;
    }

    private async Task<HashSet<string>> LoadRestoredCancelledWorkOrderNumbersAsync(
        string branch,
        CancellationToken ct)
    {
        var workOrderNumbers = await uow.Repository<ProductionWorkOrderAssignmentCancellation>().Query()
            .AsNoTracking()
            .Where(x => x.BranchCode == branch
                && !x.IsDeleted
                && x.RestoredAtUtc.HasValue)
            .Select(x => x.WorkOrderNumber)
            .Distinct()
            .ToListAsync(ct);

        return new HashSet<string>(workOrderNumbers, StringComparer.OrdinalIgnoreCase);
    }

    private static ProductionSourceWorkOrderRow ApplyRestoredCancelledListingKind(
        ProductionSourceWorkOrderRow row,
        IReadOnlySet<string> restoredWorkOrderNumbers)
    {
        if (row.ListingKind != ProductionSourceWorkOrderListingKind.Standard)
            return row;
        if (!restoredWorkOrderNumbers.Contains(row.WorkOrderNumber.Trim()))
            return row;

        return row with { ListingKind = ProductionSourceWorkOrderListingKind.RestoredCancelledAssignment };
    }

    private async Task<ProductionSourceWorkOrderRow?> ResolveSourceWorkOrderTemplateAsync(
        string workOrderNumber,
        string branch,
        int branchNumber,
        (ProductionOrderSourceType Source, string SourceSystemCode) setting,
        CancellationToken ct)
    {
        if (setting.Source is ProductionOrderSourceType.NetsisErpFunctions or ProductionOrderSourceType.ErpAndWms)
        {
            var netsis = (await netsisRead.GetProductionWorkOrdersAsync(workOrderNumber, branchNumber, true, 1, ct)).FirstOrDefault();
            if (netsis is null) return null;
            return new ProductionSourceWorkOrderRow(
                ProductionOrderSourceType.NetsisErpFunctions,
                "NETSIS",
                1,
                netsis.WorkOrderNumber,
                netsis.BranchCode ?? branchNumber,
                netsis.StockCode,
                netsis.StockName,
                netsis.ConfigurationCode,
                netsis.WorkOrderQuantity,
                netsis.UnitCode,
                netsis.RecipeTotal,
                netsis.WorkOrderDate,
                netsis.DeliveryDate,
                netsis.ProjectCode,
                netsis.WarehouseCode,
                netsis.IssueWarehouseCode,
                netsis.IsClosed,
                Description: netsis.Description);
        }

        if (setting.Source == ProductionOrderSourceType.WmsIntegrationTables)
        {
            var source = await uow.Repository<ProductionSourceWorkOrder>().Query()
                .AsNoTracking()
                .Where(x => x.BranchCode == branch
                    && x.SourceSystemCode == setting.SourceSystemCode
                    && x.WorkOrderNumber == workOrderNumber
                    && (x.Status == ProductionSourceOrderStatus.Ready || x.Status == ProductionSourceOrderStatus.Released))
                .OrderByDescending(x => x.RevisionNumber)
                .ThenByDescending(x => x.SourceUpdatedAtUtc)
                .Select(x => new
                {
                    x.SourceSystemCode,
                    x.RevisionNumber,
                    x.WorkOrderNumber,
                    x.ProductCode,
                    x.ProductName,
                    x.ConfigurationCode,
                    x.PlannedQuantity,
                    x.UnitCode,
                    RecipeLineCount = x.RecipeLines.Count,
                    x.WorkOrderDate,
                    x.DeliveryDate,
                    x.ProjectCode,
                    x.TargetWarehouseCode,
                    x.SourceWarehouseCode,
                    x.Description,
                })
                .FirstOrDefaultAsync(ct);
            if (source is null) return null;

            return new ProductionSourceWorkOrderRow(
                ProductionOrderSourceType.WmsIntegrationTables,
                source.SourceSystemCode,
                source.RevisionNumber,
                source.WorkOrderNumber,
                branchNumber,
                source.ProductCode,
                source.ProductName ?? source.ProductCode,
                source.ConfigurationCode,
                source.PlannedQuantity,
                source.UnitCode,
                source.RecipeLineCount,
                source.WorkOrderDate,
                source.DeliveryDate,
                source.ProjectCode,
                source.TargetWarehouseCode,
                source.SourceWarehouseCode,
                false,
                RecipeLineCount: source.RecipeLineCount,
                Description: source.Description);
        }

        return null;
    }

    private static void ValidateCancellationReason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length < 5)
            throw AppException.BadRequest("İptal nedeni en az 5 karakter olmalıdır.");
    }

    private static object[] ToAuditMaterialQuantities(IReadOnlyDictionary<ProductionRecipeMaterialKey, decimal> quantities) =>
        quantities.Select(x => new
        {
            stockId = x.Key.StockId,
            yapCodeId = x.Key.YapCodeId,
            operationNumber = x.Key.OperationNumber,
            quantity = x.Value,
        }).ToArray<object>();

    private static ProductionWorkOrderAssignmentCancellationResult MapCancellationResult(
        ProductionWorkOrderAssignmentCancellation cancellation,
        decimal cancelledQuantityTotal,
        bool replayed) =>
        new(
            cancellation.Id,
            cancellation.WorkOrderNumber,
            cancellation.Status,
            cancelledQuantityTotal,
            replayed);

    private async Task<bool> IsRemainingFullyCancelledAsync(
        string branch,
        ProductionSourceWorkOrderRow row,
        CancellationToken ct)
    {
        var cancelledMaterials = await LoadCancelledMaterialQuantitiesAsync(branch, row.WorkOrderNumber, ct);
        if (cancelledMaterials.Count == 0)
            return false;

        var cancellable = await BuildCancellableRemainingQuantitiesAsync(branch, row, null, null, ct);
        return cancellable.Count == 0;
    }

    private static IReadOnlyList<ProductionSourceWorkOrderRow> MergeUnassignedWithCancellationRemaindersAsync(
        IReadOnlyList<ProductionSourceWorkOrderRow> unassigned,
        IReadOnlyList<ProductionSourceWorkOrderRow> cancellationRemainders,
        int? take,
        WorkOrderAssignmentSnapshot assignmentSnapshot)
    {
        IEnumerable<ProductionSourceWorkOrderRow> combined = unassigned;
        if (cancellationRemainders.Count > 0)
        {
            var unassignedWorkOrders = new HashSet<string>(
                unassigned.Select(x => x.WorkOrderNumber.Trim()),
                StringComparer.OrdinalIgnoreCase);
            combined = unassigned.Concat(
                cancellationRemainders.Where(x =>
                    x.ListingKind == ProductionSourceWorkOrderListingKind.UnassignedCreatedTransfer
                    || !unassignedWorkOrders.Contains(x.WorkOrderNumber.Trim())));
        }

        var filtered = new List<ProductionSourceWorkOrderRow>();
        foreach (var row in combined)
        {
            if (row.ListingKind is ProductionSourceWorkOrderListingKind.CancellationReturnRemainder
                    or ProductionSourceWorkOrderListingKind.PartialTransferRemainder
                    or ProductionSourceWorkOrderListingKind.UnassignedCreatedTransfer)
            {
                filtered.Add(row);
                continue;
            }

            if (assignmentSnapshot.IsRemainingFullyCancelled(row))
                continue;

            filtered.Add(row);
        }

        var ordered = filtered
            .OrderByDescending(x => x.WorkOrderDate)
            .ThenBy(x => x.WorkOrderNumber, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.SourceSystemCode, StringComparer.OrdinalIgnoreCase);
        return (take is int boundedTake ? ordered.Take(boundedTake) : ordered).ToArray();
    }
}
