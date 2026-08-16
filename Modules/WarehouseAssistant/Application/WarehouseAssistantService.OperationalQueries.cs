using Microsoft.EntityFrameworkCore;
using verii_wms_api_v2.Modules.BarcodeDesigner.Application;
using verii_wms_api_v2.Modules.GoodsReceipt.Domain;
using verii_wms_api_v2.Modules.Identity.Application;
using verii_wms_api_v2.Modules.Location.Domain;
using verii_wms_api_v2.Modules.Shipping.Domain;
using verii_wms_api_v2.Modules.StockBalance.Domain;
using verii_wms_api_v2.Modules.StockMovement.Domain;
using verii_wms_api_v2.Modules.WarehouseInbound.Domain;
using verii_wms_api_v2.Modules.WarehouseOutbound.Domain;
using verii_wms_api_v2.Modules.WarehouseTransfer.Domain;
using verii_wms_api_v2.Shared.Application.Exceptions;
using StockEntity = verii_wms_api_v2.Modules.Stock.Domain.Stock;
using WarehouseEntity = verii_wms_api_v2.Modules.Warehouse.Domain.Warehouse;
using static verii_wms_api_v2.Modules.WarehouseAssistant.Localization.WarehouseAssistantMessageKeys;

namespace verii_wms_api_v2.Modules.WarehouseAssistant.Application;

public sealed partial class WarehouseAssistantService
{
    private async Task<ExecutionResult> ExecuteBarcodeLookupAsync(
        WarehouseAssistantIntentResolution resolution,
        long actorUserId,
        string branchCode,
        WarehouseAssistantAccess access,
        CancellationToken ct)
    {
        if (!access.CanViewStockBalances)
            return Denied(resolution.Intent, M(BarcodeBalanceDenied));
        if (string.IsNullOrWhiteSpace(resolution.Barcode))
            return MissingEntity(resolution.Intent, M(BarcodeRequired));
        if (barcodeResolver is null)
            return MissingEntity(resolution.Intent, M(BarcodeResolverUnavailable));

        var barcodeValue = resolution.Barcode.Trim();
        ResolvedWarehouseBarcode resolved;
        try
        {
            resolved = await barcodeResolver.ResolveAsync(new ResolveWarehouseBarcodeRequest(
                barcodeValue,
                branchCode,
                WarehouseBarcodePurpose.Lookup), ct);
        }
        catch (AppException exception) when (exception.StatusCode is 400 or 404 or 409)
        {
            return new ExecutionResult(
                resolution.Intent,
                "authorized-warehouses",
                "resolve-warehouse-barcode",
                exception.Message,
                [], [], [], [], null, [], [],
                new WarehouseAssistantContext(null, null, null, barcodeValue),
                [M(BarcodeRetrySuggestion)]);
        }

        var barcode = new WarehouseAssistantBarcodeRow(
            resolved.RawBarcode,
            resolved.Source,
            resolved.StockId,
            resolved.StockCode,
            resolved.StockName,
            resolved.YapCodeId,
            resolved.YapCode,
            resolved.Quantity,
            resolved.UnitCode,
            resolved.LotNo,
            resolved.SerialNo,
            resolved.ManufacturingDate,
            resolved.ExpirationDate,
            resolved.RequireSerial,
            resolved.RequireLot,
            resolved.RequireManufacturingDate,
            resolved.RequireExpirationDate,
            resolved.MissingFields);

        var warehouseAccess = await UserWarehouseAccessService.ResolveAsync(unitOfWork, actorUserId, branchCode, ct);
        var balances = unitOfWork.Repository<LocationStockBalance>().Query()
            .Where(x => x.BranchCode == branchCode && x.StockId == resolved.StockId && x.Quantity != 0);
        if (!string.IsNullOrWhiteSpace(resolved.SerialNo))
            balances = balances.Where(x => x.SerialNo == resolved.SerialNo);
        if (!string.IsNullOrWhiteSpace(resolved.LotNo))
            balances = balances.Where(x => x.LotNo == resolved.LotNo);
        if (warehouseAccess.IsRestricted)
            balances = balances.Where(x => warehouseAccess.WarehouseIds.Contains(x.WarehouseId));

        var stockLocations = await (from balance in balances
                                    join warehouse in unitOfWork.Repository<WarehouseEntity>().Query() on balance.WarehouseId equals warehouse.Id
                                    join location in unitOfWork.Repository<WarehouseLocation>().Query() on balance.LocationId equals location.Id
                                    orderby balance.AvailableQuantity descending, warehouse.WarehouseCode, location.Code
                                    select new WarehouseAssistantStockLocationRow(
                                        resolved.StockId,
                                        resolved.StockCode,
                                        resolved.StockName,
                                        warehouse.WarehouseCode,
                                        warehouse.WarehouseName,
                                        location.Code,
                                        location.Name,
                                        balance.UnitCode,
                                        balance.Quantity,
                                        balance.ReservedQuantity,
                                        balance.AvailableQuantity))
            .Take(MaximumResultCount)
            .ToListAsync(ct);

        var answer = stockLocations.Count == 0
            ? M(BarcodeBalanceNone, resolved.StockCode, resolved.StockName)
            : M(BarcodeBalanceFound, resolved.StockCode, resolved.StockName, stockLocations.Count, stockLocations.Sum(x => x.AvailableQuantity), resolved.UnitCode);
        return new ExecutionResult(
            resolution.Intent,
            "authorized-warehouses",
            "resolve-warehouse-barcode",
            answer,
            [], [], [], stockLocations, barcode, [], [],
            new WarehouseAssistantContext(resolved.SerialNo, resolved.StockId, resolved.StockCode, resolved.RawBarcode),
            [
                M(SuggestionStockMovement, resolved.StockCode),
                !string.IsNullOrWhiteSpace(resolved.SerialNo)
                    ? M(SuggestionSerialBalance, resolved.SerialNo)
                    : M(SuggestionStockLocation, resolved.StockCode)
            ]);
    }

    private async Task<ExecutionResult> ExecuteStockMovementHistoryAsync(
        WarehouseAssistantIntentResolution resolution,
        string message,
        long actorUserId,
        string branchCode,
        WarehouseAssistantAccess access,
        CancellationToken ct)
    {
        if (!access.CanViewStockMovements)
            return Denied(resolution.Intent, M(MovementDenied));

        StockEntity? stock = null;
        var serialNo = string.IsNullOrWhiteSpace(resolution.SerialNo) ? null : resolution.SerialNo.Trim();
        EntityLookupResult<StockEntity>? stockLookup = null;
        if (serialNo is null)
        {
            stockLookup = await ResolveStockAsync(resolution.StockQuery, message, branchCode, ct);
            stock = stockLookup.Entity;
        }
        if (serialNo is null && stock is null)
            return stockLookup is not null && !string.IsNullOrWhiteSpace(stockLookup.SearchTerm)
                ? EntityClarification(resolution.Intent, stockLookup.SearchTerm, stockLookup.Candidates)
                : MissingEntity(resolution.Intent, M(MovementSubjectRequired));

        var (startUtc, endUtc, periodLabel) = await ResolveDateRangeAsync(
            resolution.DatePreset, ct, resolution.DateFrom, resolution.DateTo);
        var authorizedWarehouses = await ResolveAuthorizedWarehousesAsync(actorUserId, branchCode, resolution.WarehouseQuery, ct);
        var warehouseIds = authorizedWarehouses.Select(x => x.Id).ToArray();
        var entries = unitOfWork.Repository<StockMovementEntry>().Query()
            .Where(x => x.BranchCode == branchCode && warehouseIds.Contains(x.WarehouseId)
                && x.OccurredAt >= startUtc && x.OccurredAt < endUtc);
        entries = serialNo is not null
            ? entries.Where(x => x.SerialNo != null && x.SerialNo == serialNo)
            : entries.Where(x => x.StockId == stock!.Id);
        if (resolution.StatusQuery == "Outbound") entries = entries.Where(x => x.QuantityDelta < 0);
        if (resolution.StatusQuery == "Inbound") entries = entries.Where(x => x.QuantityDelta > 0);

        var operations = unitOfWork.Repository<StockMovementOperation>().Query();
        if (resolution.ExcludeCancelled)
            operations = operations.Where(x => x.Status != StockMovementStatuses.Reversed
                && x.ReversalOfOperationId == null
                && !unitOfWork.Repository<StockMovementOperation>().Query().Any(reversal => reversal.ReversalOfOperationId == x.Id));
        var rows = await (from entry in entries
                          join operation in operations on entry.OperationId equals operation.Id
                          join stockRow in unitOfWork.Repository<StockEntity>().Query() on entry.StockId equals stockRow.Id
                          join warehouse in unitOfWork.Repository<WarehouseEntity>().Query() on entry.WarehouseId equals warehouse.Id
                          join location in unitOfWork.Repository<WarehouseLocation>().Query() on entry.LocationId equals location.Id
                          orderby entry.OccurredAt descending, entry.Id descending
                          select new WarehouseAssistantMovementRow(
                              entry.Id,
                              operation.Id,
                              operation.OperationType,
                              operation.Status,
                              operation.ReferenceType,
                              operation.ReferenceNo,
                              operation.ReferenceId,
                              stockRow.Id,
                              stockRow.ErpStockCode,
                              stockRow.StockName,
                              warehouse.WarehouseCode,
                              warehouse.WarehouseName,
                              location.Code,
                              location.Name,
                              entry.QuantityDelta,
                              entry.UnitCode,
                              entry.LotNo,
                              entry.SerialNo,
                              entry.StockStatus,
                              entry.OccurredAt,
                              operation.Status == StockMovementStatuses.Reversed || operation.ReversalOfOperationId != null))
            .Take(MaximumResultCount)
            .ToListAsync(ct);

        var entityLabel = serialNo is not null
            ? M(SerialSubject, serialNo)
            : $"{stock!.ErpStockCode} - {stock.StockName}";
        var answer = rows.Count == 0
            ? M(MovementNone, periodLabel, entityLabel)
            : M(MovementFound, periodLabel, entityLabel, rows.Count, rows.Where(x => x.QuantityDelta > 0).Sum(x => x.QuantityDelta), Math.Abs(rows.Where(x => x.QuantityDelta < 0).Sum(x => x.QuantityDelta)));
        var first = rows.FirstOrDefault();
        return new ExecutionResult(
            resolution.Intent,
            "authorized-warehouses",
            "query-stock-movement-history",
            answer,
            [], [], [], [], null, rows, [],
            new WarehouseAssistantContext(serialNo, first?.StockId ?? stock?.Id, first?.StockCode ?? stock?.ErpStockCode,
                DateFrom: resolution.DateFrom, DateTo: resolution.DateTo,
                WarehouseQuery: resolution.WarehouseQuery, QueryKind: resolution.QueryKind, StockMeasure: resolution.StockMeasure),
            [serialNo is not null ? M(SuggestionSerialBalance, serialNo) : M(SuggestionStockLocation, stock!.ErpStockCode)]);
    }

    private async Task<ExecutionResult> ExecuteAssignedTasksAsync(
        WarehouseAssistantIntentResolution resolution,
        string message,
        long actorUserId,
        string branchCode,
        WarehouseAssistantAccess access,
        CancellationToken ct)
    {
        if (!CanQueryAnyTasks(access))
            return Denied(resolution.Intent, M(TaskDenied));

        var target = await ResolveActivityTargetAsync(message, resolution, actorUserId, access.CanQueryAllUsers, ct);
        long? targetUserId = target.AllUsers ? null : target.UserId ?? actorUserId;
        var warehouseAccess = await UserWarehouseAccessService.ResolveAsync(unitOfWork, actorUserId, branchCode, ct);
        var candidates = new List<TaskCandidate>();

        if (access.CanViewGoodsReceipts)
            candidates.AddRange(await QueryGoodsReceiptTasksAsync(branchCode, targetUserId, warehouseAccess.IsRestricted, warehouseAccess.WarehouseIds, ct));
        if (access.CanViewWarehouseTransfers || access.CanViewProductionTransfers)
            candidates.AddRange(await QueryWarehouseTransferTasksAsync(branchCode, targetUserId, warehouseAccess.IsRestricted, warehouseAccess.WarehouseIds, access, ct));
        if (access.CanViewShipping)
            candidates.AddRange(await QueryShipmentTasksAsync(branchCode, targetUserId, warehouseAccess.IsRestricted, warehouseAccess.WarehouseIds, ct));
        if (access.CanViewWarehouseInbound)
            candidates.AddRange(await QueryWarehouseInboundTasksAsync(branchCode, targetUserId, warehouseAccess.IsRestricted, warehouseAccess.WarehouseIds, ct));
        if (access.CanViewWarehouseOutbound)
            candidates.AddRange(await QueryWarehouseOutboundTasksAsync(branchCode, targetUserId, warehouseAccess.IsRestricted, warehouseAccess.WarehouseIds, ct));

        var scopedCandidates = resolution.TransferScope switch
        {
            WarehouseAssistantTransferScope.Production => candidates.Where(x => x.Module == "ProductionTransfer"),
            WarehouseAssistantTransferScope.InterWarehouse => candidates.Where(x => x.Module == "WarehouseTransfer"),
            _ => candidates
        };
        var selected = scopedCandidates
            .OrderBy(x => x.Priority)
            .ThenBy(x => x.DueAtUtc ?? DateTimeOffset.MaxValue)
            .ThenBy(x => x.PlannedAtUtc ?? DateTimeOffset.MaxValue)
            .ThenBy(x => x.TaskId)
            .Take(MaximumResultCount)
            .ToArray();
        var names = await ResolveUserNamesAsync(selected.Select(x => x.AssigneeUserId), ct);
        var rows = selected.Select(x => new WarehouseAssistantTaskRow(
            x.Module,
            x.TaskId,
            x.TaskNo,
            x.TaskType,
            x.Status,
            x.Priority,
            x.DocumentId,
            x.DocumentNo,
            x.WarehouseId,
            x.WarehouseCode,
            x.WarehouseName,
            x.PlannedQuantity,
            x.ProcessedQuantity,
            Math.Max(0, x.PlannedQuantity - x.ProcessedQuantity),
            x.PlannedAtUtc,
            x.DueAtUtc,
            x.AssigneeUserId,
            x.AssigneeUserId.HasValue ? names.GetValueOrDefault(x.AssigneeUserId.Value, M(UserNumber, x.AssigneeUserId)) : M(Unassigned)))
            .ToArray();

        var forcedSelf = !access.CanQueryAllUsers && (resolution.RequestsAllUsers || target.RequestedAnotherUser);
        var answer = rows.Length == 0
            ? M(TaskNone, target.DisplayName)
            : M(TaskFound, target.DisplayName, rows.Length);
        if (forcedSelf)
            answer = M(TaskForcedSelf) + " " + answer;
        return new ExecutionResult(
            resolution.Intent,
            target.AllUsers ? "all-users" : targetUserId == actorUserId ? "self" : "selected-user",
            "query-assigned-operational-tasks",
            answer,
            [], [], [], [], null, [], rows,
            new WarehouseAssistantContext(null, null, null),
            [M(CapabilityExampleMyActivities), M(CapabilityExampleTasks)]);
    }

    private async Task<IReadOnlyList<TaskCandidate>> QueryGoodsReceiptTasksAsync(
        string branchCode, long? targetUserId, bool restricted, IReadOnlyCollection<long> warehouseIds, CancellationToken ct)
    {
        var query = from assignment in unitOfWork.Repository<GoodsReceiptTaskAssignment>().Query()
                    join task in unitOfWork.Repository<GoodsReceiptTask>().Query() on assignment.GrTaskId equals task.Id
                    join header in unitOfWork.Repository<GoodsReceiptHeader>().Query() on task.GrHeaderId equals header.Id
                    join warehouse in unitOfWork.Repository<WarehouseEntity>().Query() on task.WarehouseId equals warehouse.Id
                    where task.BranchCode == branchCode
                        && assignment.Status != GoodsReceiptAssignmentStatus.Unassigned
                        && assignment.Status != GoodsReceiptAssignmentStatus.Rejected
                        && task.Status != GoodsReceiptTaskStatus.Completed
                        && task.Status != GoodsReceiptTaskStatus.Cancelled
                        && (!targetUserId.HasValue || assignment.UserId == targetUserId.Value)
                        && (!restricted || warehouseIds.Contains(task.WarehouseId))
                    select new TaskCandidate(
                        "GoodsReceipt", task.Id, task.TaskNo, task.TaskType.ToString(), task.Status.ToString(), task.Priority,
                        header.Id, header.DocumentNo, task.WarehouseId, warehouse.WarehouseCode, warehouse.WarehouseName,
                        unitOfWork.Repository<GoodsReceiptTaskLine>().Query().Where(x => x.GrTaskId == task.Id).Sum(x => (decimal?)x.PlannedQuantity) ?? 0,
                        unitOfWork.Repository<GoodsReceiptTaskLine>().Query().Where(x => x.GrTaskId == task.Id).Sum(x => (decimal?)x.ProcessedQuantity) ?? 0,
                        task.PlannedStartAtUtc, task.DueAtUtc, assignment.UserId);
        return await query.Take(MaximumResultCount).ToListAsync(ct);
    }

    private async Task<IReadOnlyList<TaskCandidate>> QueryWarehouseTransferTasksAsync(
        string branchCode, long? targetUserId, bool restricted, IReadOnlyCollection<long> warehouseIds,
        WarehouseAssistantAccess access, CancellationToken ct)
    {
        var productionContexts = new[]
        {
            WarehouseTransferBusinessContext.ProductionMaterialSupply,
            WarehouseTransferBusinessContext.ProductionWipMove,
            WarehouseTransferBusinessContext.ProductionOutputMove
        };
        var query = from assignment in unitOfWork.Repository<WarehouseTransferTaskAssignment>().Query()
                    join task in unitOfWork.Repository<WarehouseTransferTask>().Query() on assignment.WtTaskId equals task.Id
                    join header in unitOfWork.Repository<WarehouseTransferHeader>().Query() on task.WtHeaderId equals header.Id
                    join warehouse in unitOfWork.Repository<WarehouseEntity>().Query() on task.WarehouseId equals warehouse.Id
                    where task.BranchCode == branchCode
                        && task.Status != WarehouseTransferTaskStatus.Completed
                        && task.Status != WarehouseTransferTaskStatus.Cancelled
                        && (!targetUserId.HasValue || assignment.UserId == targetUserId.Value)
                        && (!restricted || warehouseIds.Contains(task.WarehouseId))
                        && ((productionContexts.Contains(header.BusinessContext) && access.CanViewProductionTransfers)
                            || (!productionContexts.Contains(header.BusinessContext) && access.CanViewWarehouseTransfers))
                    select new TaskCandidate(
                        productionContexts.Contains(header.BusinessContext) ? "ProductionTransfer" : "WarehouseTransfer",
                        task.Id, task.TaskNo, task.TaskType.ToString(), task.Status.ToString(), task.Priority,
                        header.Id, header.DocumentNo, task.WarehouseId, warehouse.WarehouseCode, warehouse.WarehouseName,
                        unitOfWork.Repository<WarehouseTransferTaskLine>().Query().Where(x => x.WtTaskId == task.Id).Sum(x => (decimal?)x.PlannedQuantity) ?? 0,
                        unitOfWork.Repository<WarehouseTransferTaskLine>().Query().Where(x => x.WtTaskId == task.Id).Sum(x => (decimal?)x.ProcessedQuantity) ?? 0,
                        task.PlannedAtUtc, null, assignment.UserId);
        return await query.Take(MaximumResultCount).ToListAsync(ct);
    }

    private async Task<IReadOnlyList<TaskCandidate>> QueryShipmentTasksAsync(
        string branchCode, long? targetUserId, bool restricted, IReadOnlyCollection<long> warehouseIds, CancellationToken ct)
    {
        var query = from assignment in unitOfWork.Repository<ShipmentTaskAssignment>().Query()
                    join task in unitOfWork.Repository<ShipmentTask>().Query() on assignment.ShipmentTaskId equals task.Id
                    join header in unitOfWork.Repository<ShipmentHeader>().Query() on task.ShipmentHeaderId equals header.Id
                    join warehouse in unitOfWork.Repository<WarehouseEntity>().Query() on task.WarehouseId equals warehouse.Id
                    where task.BranchCode == branchCode
                        && task.Status != ShipmentTaskStatus.Completed
                        && task.Status != ShipmentTaskStatus.Cancelled
                        && (!targetUserId.HasValue || assignment.UserId == targetUserId.Value)
                        && (!restricted || warehouseIds.Contains(task.WarehouseId))
                    select new TaskCandidate(
                        "Shipping", task.Id, task.TaskNo, task.TaskType.ToString(), task.Status.ToString(), task.Priority,
                        header.Id, header.DocumentNo, task.WarehouseId, warehouse.WarehouseCode, warehouse.WarehouseName,
                        unitOfWork.Repository<ShipmentTaskLine>().Query().Where(x => x.ShipmentTaskId == task.Id).Sum(x => (decimal?)x.PlannedQuantity) ?? 0,
                        unitOfWork.Repository<ShipmentTaskLine>().Query().Where(x => x.ShipmentTaskId == task.Id).Sum(x => (decimal?)x.ProcessedQuantity) ?? 0,
                        task.PlannedAtUtc, null, assignment.UserId);
        return await query.Take(MaximumResultCount).ToListAsync(ct);
    }

    private async Task<IReadOnlyList<TaskCandidate>> QueryWarehouseInboundTasksAsync(
        string branchCode, long? targetUserId, bool restricted, IReadOnlyCollection<long> warehouseIds, CancellationToken ct)
    {
        var query = from assignment in unitOfWork.Repository<WarehouseInboundTaskAssignment>().Query()
                    join task in unitOfWork.Repository<WarehouseInboundTask>().Query() on assignment.GrTaskId equals task.Id
                    join header in unitOfWork.Repository<WarehouseInboundHeader>().Query() on task.GrHeaderId equals header.Id
                    join warehouse in unitOfWork.Repository<WarehouseEntity>().Query() on task.WarehouseId equals warehouse.Id
                    where task.BranchCode == branchCode
                        && assignment.Status != WarehouseInboundAssignmentStatus.Unassigned
                        && assignment.Status != WarehouseInboundAssignmentStatus.Rejected
                        && task.Status != WarehouseInboundTaskStatus.Completed
                        && task.Status != WarehouseInboundTaskStatus.Cancelled
                        && (!targetUserId.HasValue || assignment.UserId == targetUserId.Value)
                        && (!restricted || warehouseIds.Contains(task.WarehouseId))
                    select new TaskCandidate(
                        "WarehouseInbound", task.Id, task.TaskNo, task.TaskType.ToString(), task.Status.ToString(), task.Priority,
                        header.Id, header.DocumentNo, task.WarehouseId, warehouse.WarehouseCode, warehouse.WarehouseName,
                        unitOfWork.Repository<WarehouseInboundTaskLine>().Query().Where(x => x.GrTaskId == task.Id).Sum(x => (decimal?)x.PlannedQuantity) ?? 0,
                        unitOfWork.Repository<WarehouseInboundTaskLine>().Query().Where(x => x.GrTaskId == task.Id).Sum(x => (decimal?)x.ProcessedQuantity) ?? 0,
                        task.PlannedStartAtUtc, task.DueAtUtc, assignment.UserId);
        return await query.Take(MaximumResultCount).ToListAsync(ct);
    }

    private async Task<IReadOnlyList<TaskCandidate>> QueryWarehouseOutboundTasksAsync(
        string branchCode, long? targetUserId, bool restricted, IReadOnlyCollection<long> warehouseIds, CancellationToken ct)
    {
        var query = from assignment in unitOfWork.Repository<WarehouseOutboundTaskAssignment>().Query()
                    join task in unitOfWork.Repository<WarehouseOutboundTask>().Query() on assignment.WarehouseOutboundTaskId equals task.Id
                    join header in unitOfWork.Repository<WarehouseOutboundHeader>().Query() on task.WarehouseOutboundHeaderId equals header.Id
                    join warehouse in unitOfWork.Repository<WarehouseEntity>().Query() on task.WarehouseId equals warehouse.Id
                    where task.BranchCode == branchCode
                        && task.Status != WarehouseOutboundTaskStatus.Completed
                        && task.Status != WarehouseOutboundTaskStatus.Cancelled
                        && (!targetUserId.HasValue || assignment.UserId == targetUserId.Value)
                        && (!restricted || warehouseIds.Contains(task.WarehouseId))
                    select new TaskCandidate(
                        "WarehouseOutbound", task.Id, task.TaskNo, task.TaskType.ToString(), task.Status.ToString(), task.Priority,
                        header.Id, header.DocumentNo, task.WarehouseId, warehouse.WarehouseCode, warehouse.WarehouseName,
                        unitOfWork.Repository<WarehouseOutboundTaskLine>().Query().Where(x => x.WarehouseOutboundTaskId == task.Id).Sum(x => (decimal?)x.PlannedQuantity) ?? 0,
                        unitOfWork.Repository<WarehouseOutboundTaskLine>().Query().Where(x => x.WarehouseOutboundTaskId == task.Id).Sum(x => (decimal?)x.ProcessedQuantity) ?? 0,
                        task.PlannedAtUtc, null, assignment.UserId);
        return await query.Take(MaximumResultCount).ToListAsync(ct);
    }

    private sealed record TaskCandidate(
        string Module,
        long TaskId,
        string TaskNo,
        string TaskType,
        string Status,
        byte Priority,
        long DocumentId,
        string DocumentNo,
        long WarehouseId,
        int WarehouseCode,
        string WarehouseName,
        decimal PlannedQuantity,
        decimal ProcessedQuantity,
        DateTimeOffset? PlannedAtUtc,
        DateTimeOffset? DueAtUtc,
        long? AssigneeUserId);
}
