using Microsoft.EntityFrameworkCore;
using verii_wms_api_v2.Modules.GoodsReceipt.Domain;
using verii_wms_api_v2.Modules.Identity.Application;
using verii_wms_api_v2.Modules.Location.Domain;
using verii_wms_api_v2.Modules.Packing.Domain;
using verii_wms_api_v2.Modules.Quality.Domain;
using verii_wms_api_v2.Modules.Shipping.Domain;
using verii_wms_api_v2.Modules.StockBalance.Domain;
using verii_wms_api_v2.Modules.StockMovement.Domain;
using verii_wms_api_v2.Modules.WarehouseOperations.Domain;
using verii_wms_api_v2.Modules.WarehouseTransfer.Domain;
using WarehouseEntity = verii_wms_api_v2.Modules.Warehouse.Domain.Warehouse;
using StockEntity = verii_wms_api_v2.Modules.Stock.Domain.Stock;
using static verii_wms_api_v2.Modules.WarehouseAssistant.Localization.WarehouseAssistantMessageKeys;

namespace verii_wms_api_v2.Modules.WarehouseAssistant.Application;

public sealed partial class WarehouseAssistantService
{
    private async Task<ExecutionResult> ExecuteShiftBriefAsync(
        WarehouseAssistantIntentResolution resolution,
        string originalMessage,
        long actorUserId,
        string branchCode,
        WarehouseAssistantAccess access,
        CancellationToken ct)
    {
        var taskResult = CanQueryAnyTasks(access)
            ? await ExecuteAssignedTasksAsync(
                resolution with { Intent = WarehouseAssistantIntent.AssignedTasks, TargetUserQuery = null, RequestsAllUsers = false },
                originalMessage,
                actorUserId,
                branchCode,
                access,
                ct)
            : null;
        var exceptions = CanQueryOperationalExceptions(access)
            ? await QueryOperationalExceptionsAsync(actorUserId, branchCode, access, ct)
            : [];

        var tasks = taskResult?.Tasks ?? [];
        var metrics = new List<WarehouseAssistantSummaryMetricRow>
        {
            new("openTasks", M(MetricOpenTasks), tasks.Count, M(UnitTask), "Info", "Tasks", "/warehouse/goods-receipts/assigned"),
            new("remainingQuantity", M(MetricRemainingQuantity), tasks.Sum(x => x.RemainingQuantity), M(UnitQuantity), "Info", "Tasks", "/warehouse/goods-receipts/assigned"),
            new("criticalExceptions", M(MetricCriticalExceptions), exceptions.Count(x => x.Severity == "Critical"), M(UnitRecord), "Critical", "Operations"),
            new("highExceptions", M(MetricHighExceptions), exceptions.Count(x => x.Severity == "High"), M(UnitRecord), "High", "Operations"),
            new("qualityWaiting", M(MetricQualityWaiting), exceptions.Count(x => x.Code == "QUALITY_WAITING"), M(UnitRecord), "High", "Quality", "/warehouse/quality/inspections"),
            new("erpFailures", M(MetricErpFailures), exceptions.Count(x => x.Code.EndsWith("ERP_FAILED", StringComparison.Ordinal)), M(UnitRecord), "Critical", "ERP")
        };

        var answer = M(ShiftBriefAnswer, tasks.Count, exceptions.Count, metrics[2].Value + metrics[3].Value);
        return new ExecutionResult(
            WarehouseAssistantIntent.ShiftBrief,
            "self-and-authorized-warehouses",
            "query-shift-brief",
            answer,
            [], [], [], [], null, [], tasks,
            new WarehouseAssistantContext(null, null, null),
            [M(CapabilityExampleOperationalExceptions), M(CapabilityExampleTasks)],
            SummaryMetrics: metrics,
            Exceptions: exceptions);
    }

    private async Task<ExecutionResult> ExecuteOperationalExceptionsAsync(
        long actorUserId,
        string branchCode,
        WarehouseAssistantAccess access,
        CancellationToken ct)
    {
        if (!CanQueryOperationalExceptions(access))
            return Denied(WarehouseAssistantIntent.OperationalExceptions, M(OperationalExceptionsDenied));

        var rows = await QueryOperationalExceptionsAsync(actorUserId, branchCode, access, ct);
        var answer = rows.Count == 0
            ? M(OperationalExceptionsNone)
            : M(OperationalExceptionsFound, rows.Count, rows.Count(x => x.Severity is "Critical" or "High"));
        return new ExecutionResult(
            WarehouseAssistantIntent.OperationalExceptions,
            "authorized-warehouses",
            "query-operational-exceptions",
            answer,
            [], [], [], [], null, [], [],
            new WarehouseAssistantContext(null, null, null),
            [M(CapabilityExampleShiftBrief), M(CapabilityExampleProcessBlockers)],
            Exceptions: rows);
    }

    private async Task<IReadOnlyList<WarehouseAssistantExceptionRow>> QueryOperationalExceptionsAsync(
        long actorUserId,
        string branchCode,
        WarehouseAssistantAccess access,
        CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow();
        var warehouseAccess = await UserWarehouseAccessService.ResolveAsync(unitOfWork, actorUserId, branchCode, ct);
        var rows = new List<WarehouseAssistantExceptionRow>();

        if (access.CanViewStockBalances)
        {
            var balanceQuery = unitOfWork.Repository<LocationStockBalance>().Query()
                .Where(x => x.BranchCode == branchCode
                    && (x.Quantity < 0 || x.AvailableQuantity < 0 || x.ReservedQuantity > x.Quantity));
            if (warehouseAccess.IsRestricted)
                balanceQuery = balanceQuery.Where(x => warehouseAccess.WarehouseIds.Contains(x.WarehouseId));
            var balances = await balanceQuery.OrderBy(x => x.AvailableQuantity).Take(MaximumResultCount)
                .Select(x => new { x.Id, x.WarehouseId, x.LocationId, x.StockId, x.Quantity, x.ReservedQuantity, x.AvailableQuantity, x.LastTransactionDate })
                .ToListAsync(ct);
            foreach (var x in balances)
                rows.Add(Exception("BALANCE_INTEGRITY", "Critical", "StockBalance", M(ExceptionBalanceTitle),
                    M(ExceptionBalanceDescription, x.Quantity, x.ReservedQuantity, x.AvailableQuantity), "LocationStockBalance", x.Id, null,
                    "Invalid", Utc(x.LastTransactionDate), M(ExceptionBalanceAction), "/warehouse/location-balances", now));
        }

        if (access.CanViewGoodsReceipts)
        {
            var query = unitOfWork.Repository<GoodsReceiptHeader>().Query()
                .Where(x => x.BranchCode == branchCode && x.Status != WarehouseOperationStatus.Cancelled);
            if (warehouseAccess.IsRestricted)
                query = query.Where(x => warehouseAccess.WarehouseIds.Contains(x.TargetWarehouseId));
            var items = await query
                .Where(x => x.ErpIntegrationStatus == ErpIntegrationStatus.Failed
                    || x.ErpIntegrationStatus == ErpIntegrationStatus.CommitUncertain
                    || (x.RequireQualityControl && x.QualityStatus != OperationQualityStatus.Passed && x.QualityStatus != OperationQualityStatus.NotRequired)
                    || (x.RequirePutaway && x.Status == WarehouseOperationStatus.Completed && x.PutawayStatus != OperationPutawayStatus.Completed))
                .OrderByDescending(x => x.CreatedDate).Take(MaximumResultCount)
                .Select(x => new { x.Id, x.DocumentNo, x.Status, x.ErpIntegrationStatus, x.RequireQualityControl, x.QualityStatus, x.RequirePutaway, x.PutawayStatus, x.CreatedDate })
                .ToListAsync(ct);
            foreach (var x in items)
            {
                var detected = Utc(x.CreatedDate ?? now.UtcDateTime);
                if (x.ErpIntegrationStatus is ErpIntegrationStatus.Failed or ErpIntegrationStatus.CommitUncertain)
                    rows.Add(Exception("GR_ERP_FAILED", "Critical", "GoodsReceipt", M(ExceptionGrErpTitle, x.DocumentNo),
                        M(ExceptionGrErpDescription, x.ErpIntegrationStatus), "GoodsReceipt", x.Id, x.DocumentNo, x.ErpIntegrationStatus.ToString(), detected,
                        M(ExceptionGrErpAction), "/warehouse/goods-receipts/list", now));
                if (x.RequireQualityControl && x.QualityStatus is not (OperationQualityStatus.Passed or OperationQualityStatus.NotRequired))
                    rows.Add(Exception("QUALITY_WAITING", "High", "Quality", M(ExceptionQualityTitle, x.DocumentNo),
                        M(ExceptionQualityDescription, x.QualityStatus), "GoodsReceipt", x.Id, x.DocumentNo, x.QualityStatus.ToString(), detected,
                        M(ExceptionQualityAction), "/warehouse/quality/inspections", now));
                if (x.RequirePutaway && x.Status == WarehouseOperationStatus.Completed && x.PutawayStatus != OperationPutawayStatus.Completed)
                    rows.Add(Exception("GR_PUTAWAY_WAITING", "High", "GoodsReceipt", M(ExceptionPutawayTitle, x.DocumentNo),
                        M(ExceptionPutawayDescription, x.PutawayStatus), "GoodsReceipt", x.Id, x.DocumentNo, x.PutawayStatus.ToString(), detected,
                        M(ExceptionPutawayAction), "/warehouse/goods-receipts/list", now));
            }
        }

        if (access.CanViewWarehouseTransfers || access.CanViewProductionTransfers)
        {
            var query = unitOfWork.Repository<WarehouseTransferHeader>().Query()
                .Where(x => x.BranchCode == branchCode && x.Status != WarehouseTransferStatus.Cancelled && x.Status != WarehouseTransferStatus.Completed);
            if (!access.CanViewWarehouseTransfers)
                query = query.Where(x => x.BusinessContext == WarehouseTransferBusinessContext.ProductionMaterialSupply);
            if (!access.CanViewProductionTransfers)
                query = query.Where(x => x.BusinessContext != WarehouseTransferBusinessContext.ProductionMaterialSupply);
            if (warehouseAccess.IsRestricted)
                query = query.Where(x => warehouseAccess.WarehouseIds.Contains(x.SourceWarehouseId) || warehouseAccess.WarehouseIds.Contains(x.TargetWarehouseId));
            var items = await query
                .Where(x => x.ErpIntegrationStatus == ErpIntegrationStatus.Failed
                    || x.ErpIntegrationStatus == ErpIntegrationStatus.CommitUncertain
                    || (x.PlannedDispatchAtUtc != null && x.PlannedDispatchAtUtc < now && x.ShippedAtUtc == null)
                    || (x.PlannedArrivalAtUtc != null && x.PlannedArrivalAtUtc < now && x.ReceivedAtUtc == null))
                .OrderBy(x => x.PlannedDispatchAtUtc).Take(MaximumResultCount)
                .Select(x => new { x.Id, x.DocumentNo, x.Status, x.ErpIntegrationStatus, x.PlannedDispatchAtUtc, x.PlannedArrivalAtUtc, x.ShippedAtUtc, x.ReceivedAtUtc, x.CreatedDate })
                .ToListAsync(ct);
            foreach (var x in items)
            {
                var detected = x.PlannedDispatchAtUtc ?? x.PlannedArrivalAtUtc ?? Utc(x.CreatedDate ?? now.UtcDateTime);
                if (x.ErpIntegrationStatus is ErpIntegrationStatus.Failed or ErpIntegrationStatus.CommitUncertain)
                    rows.Add(Exception("TRANSFER_ERP_FAILED", "Critical", "Transfer", M(ExceptionTransferErpTitle, x.DocumentNo),
                        M(ExceptionTransferErpDescription, x.ErpIntegrationStatus), "WarehouseTransfer", x.Id, x.DocumentNo, x.ErpIntegrationStatus.ToString(), detected,
                        M(ExceptionTransferErpAction), "/warehouse/transfers/list", now));
                if ((x.PlannedDispatchAtUtc < now && x.ShippedAtUtc == null) || (x.PlannedArrivalAtUtc < now && x.ReceivedAtUtc == null))
                    rows.Add(Exception("TRANSFER_OVERDUE", "High", "Transfer", M(ExceptionTransferOverdueTitle, x.DocumentNo),
                        M(ExceptionTransferOverdueDescription, x.Status), "WarehouseTransfer", x.Id, x.DocumentNo, x.Status.ToString(), detected,
                        M(ExceptionTransferOverdueAction), "/warehouse/transfers/list", now));
            }
        }

        if (access.CanViewShipping)
        {
            var query = unitOfWork.Repository<ShipmentHeader>().Query()
                .Where(x => x.BranchCode == branchCode && x.Status != ShipmentStatus.Cancelled && x.Status != ShipmentStatus.Shipped);
            if (warehouseAccess.IsRestricted)
                query = query.Where(x => warehouseAccess.WarehouseIds.Contains(x.SourceWarehouseId));
            var items = await query
                .Where(x => x.ErpIntegrationStatus == ErpIntegrationStatus.Failed
                    || x.ErpIntegrationStatus == ErpIntegrationStatus.CommitUncertain
                    || (x.PlannedShipmentAtUtc != null && x.PlannedShipmentAtUtc < now))
                .OrderBy(x => x.PlannedShipmentAtUtc).Take(MaximumResultCount)
                .Select(x => new { x.Id, x.DocumentNo, x.Status, x.ErpIntegrationStatus, x.PlannedShipmentAtUtc, x.CreatedDate })
                .ToListAsync(ct);
            foreach (var x in items)
            {
                var detected = x.PlannedShipmentAtUtc ?? Utc(x.CreatedDate ?? now.UtcDateTime);
                if (x.ErpIntegrationStatus is ErpIntegrationStatus.Failed or ErpIntegrationStatus.CommitUncertain)
                    rows.Add(Exception("SHIPMENT_ERP_FAILED", "Critical", "Shipping", M(ExceptionShipmentErpTitle, x.DocumentNo),
                        M(ExceptionShipmentErpDescription, x.ErpIntegrationStatus), "Shipment", x.Id, x.DocumentNo, x.ErpIntegrationStatus.ToString(), detected,
                        M(ExceptionShipmentErpAction), "/warehouse/shipments/list", now));
                if (x.PlannedShipmentAtUtc < now)
                    rows.Add(Exception("SHIPMENT_OVERDUE", "High", "Shipping", M(ExceptionShipmentOverdueTitle, x.DocumentNo),
                        M(ExceptionShipmentOverdueDescription, x.Status), "Shipment", x.Id, x.DocumentNo, x.Status.ToString(), detected,
                        M(ExceptionShipmentOverdueAction), "/warehouse/shipments/list", now));
            }
        }

        if (access.CanViewQuality)
        {
            var query = unitOfWork.Repository<QualityInspection>().Query()
                .Where(x => x.BranchCode == branchCode && (x.Status == QualityInspectionStatus.Pending
                    || x.Status == QualityInspectionStatus.InProgress
                    || x.Status == QualityInspectionStatus.PartiallyDecided
                    || x.Status == QualityInspectionStatus.Quarantined));
            if (warehouseAccess.IsRestricted)
                query = query.Where(x => warehouseAccess.WarehouseIds.Contains(x.WarehouseId));
            var items = await query.OrderBy(x => x.CreatedAtUtc).Take(MaximumResultCount)
                .Select(x => new { x.Id, x.InspectionNo, x.SourceDocumentNo, x.Status, x.CreatedAtUtc })
                .ToListAsync(ct);
            foreach (var x in items.Where(x => x.Status == QualityInspectionStatus.Quarantined || now - x.CreatedAtUtc >= TimeSpan.FromHours(4)))
                rows.Add(Exception("QUALITY_WAITING", x.Status == QualityInspectionStatus.Quarantined ? "Critical" : "High", "Quality",
                    M(ExceptionInspectionTitle, x.InspectionNo), M(ExceptionInspectionDescription, x.SourceDocumentNo, x.Status),
                    "QualityInspection", x.Id, x.SourceDocumentNo, x.Status.ToString(), x.CreatedAtUtc,
                    M(ExceptionQualityAction), "/warehouse/quality/inspections", now));
        }

        if (access.CanViewPacking)
        {
            var query =
                from job in unitOfWork.Repository<PackingPrintJob>().Query()
                join handlingUnit in unitOfWork.Repository<HandlingUnit>().Query() on job.HandlingUnitId equals handlingUnit.Id
                join session in unitOfWork.Repository<PackingSession>().Query() on handlingUnit.PackingSessionId equals session.Id
                where session.BranchCode == branchCode && job.Status == PackingPrintJobStatus.Failed
                select new
                {
                    job.Id,
                    job.Status,
                    job.AttemptCount,
                    job.RequestedAtUtc,
                    job.LastError,
                    session.WarehouseId,
                    session.PackingNo,
                    handlingUnit.HandlingUnitNo
                };
            if (warehouseAccess.IsRestricted)
                query = query.Where(x => warehouseAccess.WarehouseIds.Contains(x.WarehouseId));
            var jobs = await query.OrderByDescending(x => x.RequestedAtUtc).Take(MaximumResultCount).ToListAsync(ct);
            foreach (var x in jobs)
                rows.Add(Exception("PACKING_PRINT_FAILED", "High", "Packing",
                    M(ExceptionPackingPrintTitle, x.HandlingUnitNo),
                    M(ExceptionPackingPrintDescription, x.PackingNo, x.AttemptCount, x.LastError ?? M(ExceptionPackingPrintUnknownError)),
                    "PackingPrintJob", x.Id, x.PackingNo, x.Status.ToString(), x.RequestedAtUtc,
                    M(ExceptionPackingPrintAction), "/warehouse/packing", now));
        }

        return rows
            .OrderBy(x => SeverityRank(x.Severity))
            .ThenByDescending(x => x.AgeHours)
            .Take(MaximumResultCount)
            .ToArray();
    }

    private async Task<ExecutionResult> ExecuteProcessBlockersAsync(
        WarehouseAssistantIntentResolution resolution,
        long actorUserId,
        string branchCode,
        WarehouseAssistantAccess access,
        CancellationToken ct)
    {
        if (!CanQueryProcessBlockers(access))
            return Denied(resolution.Intent, M(ProcessBlockersDenied));
        var documentNo = resolution.DocumentQuery?.Trim();
        if (string.IsNullOrWhiteSpace(documentNo))
            return MissingEntity(resolution.Intent, M(ProcessDocumentRequired));

        var now = timeProvider.GetUtcNow();
        var warehouseAccess = await UserWarehouseAccessService.ResolveAsync(unitOfWork, actorUserId, branchCode, ct);
        var rows = new List<WarehouseAssistantExceptionRow>();
        var matched = 0;

        if (access.CanViewGoodsReceipts)
        {
            var query = unitOfWork.Repository<GoodsReceiptHeader>().Query()
                .Where(x => x.BranchCode == branchCode && x.DocumentNo.Contains(documentNo));
            if (warehouseAccess.IsRestricted) query = query.Where(x => warehouseAccess.WarehouseIds.Contains(x.TargetWarehouseId));
            var items = await query.OrderByDescending(x => x.DocumentNo == documentNo).Take(5)
                .Select(x => new { x.Id, x.DocumentNo, x.Status, x.ApprovalStatus, x.RequireQualityControl, x.QualityStatus, x.RequirePutaway, x.PutawayStatus, x.ErpIntegrationStatus, x.CreatedDate })
                .ToListAsync(ct);
            matched += items.Count;
            foreach (var x in items)
            {
                var detected = Utc(x.CreatedDate ?? now.UtcDateTime);
                if (x.ApprovalStatus == OperationApprovalStatus.Pending)
                    rows.Add(ProcessBlocker("GR_APPROVAL", "High", "GoodsReceipt", x.Id, x.DocumentNo, x.ApprovalStatus.ToString(), detected, M(BlockerApprovalTitle), M(BlockerApprovalDescription), M(BlockerApprovalAction), "/warehouse/goods-receipts/list", now));
                if (x.RequireQualityControl && x.QualityStatus is not (OperationQualityStatus.Passed or OperationQualityStatus.NotRequired))
                    rows.Add(ProcessBlocker("GR_QUALITY", "High", "GoodsReceipt", x.Id, x.DocumentNo, x.QualityStatus.ToString(), detected, M(BlockerQualityTitle), M(BlockerQualityDescription, x.QualityStatus), M(BlockerQualityAction), "/warehouse/quality/inspections", now));
                if (x.RequirePutaway && x.PutawayStatus != OperationPutawayStatus.Completed)
                    rows.Add(ProcessBlocker("GR_PUTAWAY", "Medium", "GoodsReceipt", x.Id, x.DocumentNo, x.PutawayStatus.ToString(), detected, M(BlockerPutawayTitle), M(BlockerPutawayDescription, x.PutawayStatus), M(BlockerPutawayAction), "/warehouse/goods-receipts/list", now));
                if (x.ErpIntegrationStatus is ErpIntegrationStatus.Failed or ErpIntegrationStatus.CommitUncertain)
                    rows.Add(ProcessBlocker("GR_ERP", "Critical", "GoodsReceipt", x.Id, x.DocumentNo, x.ErpIntegrationStatus.ToString(), detected, M(BlockerErpTitle), M(BlockerErpDescription, x.ErpIntegrationStatus), M(BlockerErpAction), "/warehouse/goods-receipts/list", now));
            }
        }

        if (access.CanViewWarehouseTransfers || access.CanViewProductionTransfers)
        {
            var query = unitOfWork.Repository<WarehouseTransferHeader>().Query()
                .Where(x => x.BranchCode == branchCode && x.DocumentNo.Contains(documentNo));
            if (warehouseAccess.IsRestricted) query = query.Where(x => warehouseAccess.WarehouseIds.Contains(x.SourceWarehouseId) || warehouseAccess.WarehouseIds.Contains(x.TargetWarehouseId));
            var items = await query.OrderByDescending(x => x.DocumentNo == documentNo).Take(5)
                .Select(x => new { x.Id, x.DocumentNo, x.Status, x.ApprovalStatus, x.ErpIntegrationStatus, x.CreatedDate })
                .ToListAsync(ct);
            matched += items.Count;
            foreach (var x in items)
            {
                var detected = Utc(x.CreatedDate ?? now.UtcDateTime);
                if (x.ApprovalStatus == OperationApprovalStatus.Pending)
                    rows.Add(ProcessBlocker("TRANSFER_APPROVAL", "High", "Transfer", x.Id, x.DocumentNo, x.ApprovalStatus.ToString(), detected, M(BlockerApprovalTitle), M(BlockerApprovalDescription), M(BlockerApprovalAction), "/warehouse/transfers/list", now));
                if (x.Status is not (WarehouseTransferStatus.Completed or WarehouseTransferStatus.CompletedWithShortage or WarehouseTransferStatus.Cancelled))
                    rows.Add(ProcessBlocker("TRANSFER_PROGRESS", "Medium", "Transfer", x.Id, x.DocumentNo, x.Status.ToString(), detected, M(BlockerTransferTitle), M(BlockerTransferDescription, x.Status), M(BlockerTransferAction), "/warehouse/transfers/list", now));
                if (x.ErpIntegrationStatus is ErpIntegrationStatus.Failed or ErpIntegrationStatus.CommitUncertain)
                    rows.Add(ProcessBlocker("TRANSFER_ERP", "Critical", "Transfer", x.Id, x.DocumentNo, x.ErpIntegrationStatus.ToString(), detected, M(BlockerErpTitle), M(BlockerErpDescription, x.ErpIntegrationStatus), M(BlockerErpAction), "/warehouse/transfers/list", now));
            }
        }

        if (access.CanViewShipping)
        {
            var query = unitOfWork.Repository<ShipmentHeader>().Query()
                .Where(x => x.BranchCode == branchCode && x.DocumentNo.Contains(documentNo));
            if (warehouseAccess.IsRestricted) query = query.Where(x => warehouseAccess.WarehouseIds.Contains(x.SourceWarehouseId));
            var items = await query.OrderByDescending(x => x.DocumentNo == documentNo).Take(5)
                .Select(x => new { x.Id, x.DocumentNo, x.Status, x.ApprovalStatus, x.ErpIntegrationStatus, x.CreatedDate })
                .ToListAsync(ct);
            matched += items.Count;
            foreach (var x in items)
            {
                var detected = Utc(x.CreatedDate ?? now.UtcDateTime);
                if (x.ApprovalStatus == OperationApprovalStatus.Pending)
                    rows.Add(ProcessBlocker("SHIPMENT_APPROVAL", "High", "Shipping", x.Id, x.DocumentNo, x.ApprovalStatus.ToString(), detected, M(BlockerApprovalTitle), M(BlockerApprovalDescription), M(BlockerApprovalAction), "/warehouse/shipments/list", now));
                if (x.Status is not (ShipmentStatus.Shipped or ShipmentStatus.Cancelled))
                    rows.Add(ProcessBlocker("SHIPMENT_PROGRESS", "Medium", "Shipping", x.Id, x.DocumentNo, x.Status.ToString(), detected, M(BlockerShipmentTitle), M(BlockerShipmentDescription, x.Status), M(BlockerShipmentAction), "/warehouse/shipments/list", now));
                if (x.ErpIntegrationStatus is ErpIntegrationStatus.Failed or ErpIntegrationStatus.CommitUncertain)
                    rows.Add(ProcessBlocker("SHIPMENT_ERP", "Critical", "Shipping", x.Id, x.DocumentNo, x.ErpIntegrationStatus.ToString(), detected, M(BlockerErpTitle), M(BlockerErpDescription, x.ErpIntegrationStatus), M(BlockerErpAction), "/warehouse/shipments/list", now));
            }
        }

        var answer = matched == 0
            ? M(ProcessDocumentNotFound, documentNo)
            : rows.Count == 0 ? M(ProcessBlockersNone, documentNo) : M(ProcessBlockersFound, documentNo, rows.Count);
        return new ExecutionResult(
            resolution.Intent, "authorized-warehouses", "query-process-blockers", answer,
            [], [], [], [], null, [], [], new WarehouseAssistantContext(null, null, null, DocumentNo: documentNo),
            [M(CapabilityExampleOperationalExceptions)], Exceptions: rows);
    }

    private async Task<ExecutionResult> ExecuteTraceabilityAsync(
        WarehouseAssistantIntentResolution resolution,
        long actorUserId,
        string branchCode,
        WarehouseAssistantAccess access,
        CancellationToken ct)
    {
        if (!access.CanViewStockMovements || !access.CanViewStockBalances)
            return Denied(resolution.Intent, M(TraceabilityDenied));
        var serialNo = (resolution.SerialNo ?? resolution.Barcode)?.Trim();
        if (string.IsNullOrWhiteSpace(serialNo))
            return MissingEntity(resolution.Intent, M(TraceabilitySubjectRequired));

        var warehouseAccess = await UserWarehouseAccessService.ResolveAsync(unitOfWork, actorUserId, branchCode, ct);
        var query = from entry in unitOfWork.Repository<StockMovementEntry>().Query()
                    join operation in unitOfWork.Repository<StockMovementOperation>().Query() on entry.OperationId equals operation.Id
                    join stock in unitOfWork.Repository<StockEntity>().Query() on entry.StockId equals stock.Id
                    join warehouse in unitOfWork.Repository<WarehouseEntity>().Query() on entry.WarehouseId equals warehouse.Id
                    join location in unitOfWork.Repository<WarehouseLocation>().Query() on entry.LocationId equals location.Id
                    where entry.BranchCode == branchCode && entry.SerialNo != null && entry.SerialNo!.ToUpper() == serialNo.ToUpper()
                    select new
                    {
                        entry.Id, entry.WarehouseId, entry.OccurredAt, operation.OperationType, operation.ReferenceType, operation.ReferenceId,
                        operation.ReferenceNo, operation.Status, operation.ReversalOfOperationId, operation.CreatedBy,
                        entry.StockId, stock.ErpStockCode, stock.StockName, entry.SerialNo, entry.LotNo, entry.QuantityDelta,
                        entry.UnitCode, warehouse.WarehouseCode, warehouse.WarehouseName, location.Code, location.Name
                    };
        if (warehouseAccess.IsRestricted)
            query = query.Where(x => warehouseAccess.WarehouseIds.Contains(x.WarehouseId));

        var raw = await query.OrderBy(x => x.OccurredAt).Take(200).ToListAsync(ct);
        var names = await ResolveUserNamesAsync(raw.Select(x => x.CreatedBy), ct);
        var events = raw.Select(x => new WarehouseAssistantTraceabilityEventRow(
            $"movement:{x.Id}", Utc(x.OccurredAt), M(TraceStageMovement), x.OperationType,
            x.ReferenceType ?? M(TraceDocumentUnknown), x.ReferenceId, x.ReferenceNo,
            x.StockId, x.ErpStockCode, x.StockName, x.SerialNo, x.LotNo, x.QuantityDelta, x.UnitCode,
            x.WarehouseCode, x.WarehouseName, x.Code, x.Name, x.Status,
            DisplayUser(x.CreatedBy, null, names), x.ReversalOfOperationId.HasValue,
            "/warehouse/stock-movements")).ToList();

        var answer = events.Count == 0
            ? M(TraceabilityNone, serialNo)
            : M(TraceabilityFound, serialNo, events.Count, events.First().OccurredAtUtc, events.Last().OccurredAtUtc);
        return new ExecutionResult(
            resolution.Intent, "authorized-warehouses", "query-serial-traceability", answer,
            [], [], [], [], null, [], [], new WarehouseAssistantContext(serialNo, events.FirstOrDefault()?.StockId, events.FirstOrDefault()?.StockCode),
            [M(SuggestionSerialBalance, serialNo), M(SuggestionStockMovement, serialNo)], TraceabilityEvents: events);
    }

    private static WarehouseAssistantExceptionRow Exception(
        string code, string severity, string module, string title, string description, string entityType,
        long? entityId, string? documentNo, string status, DateTimeOffset? detectedAtUtc, string action,
        string? route, DateTimeOffset now) =>
        new(code, severity, module, title, description, entityType, entityId, documentNo, status, detectedAtUtc,
            detectedAtUtc.HasValue ? Math.Max(0, Math.Round((decimal)(now - detectedAtUtc.Value).TotalHours, 1)) : null,
            action, route);

    private static WarehouseAssistantExceptionRow ProcessBlocker(
        string code, string severity, string module, long entityId, string documentNo, string status,
        DateTimeOffset detectedAtUtc, string title, string description, string action, string route, DateTimeOffset now) =>
        Exception(code, severity, module, title, description, module, entityId, documentNo, status, detectedAtUtc, action, route, now);

    private static DateTimeOffset Utc(DateTime value) =>
        new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private static int SeverityRank(string severity) => severity switch
    {
        "Critical" => 0,
        "High" => 1,
        "Medium" => 2,
        _ => 3
    };

    private IReadOnlyList<WarehouseAssistantEvidenceRow> BuildEvidence(ExecutionResult result)
    {
        var count = result.Activities.Count + result.SerialBalances.Count + result.SerialReceipts.Count
            + result.StockLocations.Count + result.Movements.Count + result.Tasks.Count
            + (result.GoodsReceipts?.Count ?? 0) + (result.SteelVehicles?.Count ?? 0)
            + (result.Transfers?.Count ?? 0) + (result.Exceptions?.Count ?? 0)
            + (result.TraceabilityEvents?.Count ?? 0) + (result.Barcode is null ? 0 : 1);
        var dataAsOf = result.TraceabilityEvents?.Select(x => (DateTimeOffset?)x.OccurredAtUtc).Max()
            ?? result.Activities.Select(x => (DateTimeOffset?)x.OccurredAtUtc).Max()
            ?? result.SerialBalances.Select(x => (DateTimeOffset?)Utc(x.LastTransactionAtUtc)).Max()
            ?? result.Movements.Select(x => (DateTimeOffset?)x.OccurredAtUtc).Max();
        return
        [
            new WarehouseAssistantEvidenceRow(
                M(EvidenceOperationalDatabase),
                result.ToolName,
                count,
                timeProvider.GetUtcNow(),
                dataAsOf,
                result.Scope,
                M(EvidenceAuthorizedScope),
                count >= MaximumResultCount,
                null)
        ];
    }
}
