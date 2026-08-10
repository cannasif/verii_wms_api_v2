using System.Data;
using Microsoft.EntityFrameworkCore;
using verii_wms_api_v2.Modules.Production.Domain;
using verii_wms_api_v2.Modules.ProductionTransfer.Application;
using verii_wms_api_v2.Modules.ProductionTransfer.Domain;
using verii_wms_api_v2.Modules.WarehouseTransfer.Application;
using verii_wms_api_v2.Modules.WarehouseTransfer.Domain;
using verii_wms_api_v2.Shared.Application.Exceptions;

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

            var cancellable = await BuildCancellableRemainingQuantitiesAsync(branch, templateRow, token);
            if (cancellable.Count == 0)
                throw AppException.Conflict("Bu iş emri için iptal edilebilir atanmamış malzeme kalmadı.");

            var requested = ResolveRequestedCancellationQuantities(request.Lines, cancellable);
            await CancelOpenTransfersForMaterialsAsync(
                branch,
                workOrderNumber,
                request.TransferId,
                requested,
                request.Reason.Trim(),
                request.IdempotencyKey,
                actor,
                token);

            var cancellation = await UpsertActiveCancellationAsync(
                branch,
                workOrderNumber,
                sourceType,
                sourceSystemCode,
                request.Reason.Trim(),
                request.IdempotencyKey,
                requested,
                request.TransferId,
                actor,
                token);

            await uow.SaveChangesAsync(token);
            await audit.WriteAsync(new(
                "production.work-order-assignment.cancel",
                nameof(ProductionWorkOrderAssignmentCancellation),
                cancellation.Id.ToString(),
                "Succeeded",
                "production",
                NewValues: new { workOrderNumber, cancellation.Status, lines = ToAuditMaterialQuantities(requested) }),
                token);

            return MapCancellationResult(
                cancellation,
                cancellation.Lines.Where(x => !x.IsDeleted).Sum(x => x.CancelledQuantity),
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

            var remainingCancelled = cancellation.Lines
                .Where(x => !x.IsDeleted && x.CancelledQuantity > 0)
                .Sum(x => x.CancelledQuantity);
            if (remainingCancelled <= 0.0001m)
            {
                cancellation.Status = ProductionWorkOrderAssignmentCancellationStatus.Restored;
                cancellation.RestoredAtUtc = DateTimeOffset.UtcNow;
                cancellation.RestoredBy = actor;
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
        CancellationToken ct)
    {
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

        var totals = new Dictionary<ProductionRecipeMaterialKey, decimal>();
        foreach (var material in reclassified.Remaining)
        {
            var key = ProductionWorkOrderMaterialAssignment.CreateKey(
                material.StockId,
                material.YapCodeId,
                material.OperationNumber);
            totals[key] = totals.GetValueOrDefault(key) + material.RequiredQuantity;
        }

        foreach (var (key, cancelledQuantity) in cancelledMaterials)
            totals[key] = Math.Max(0, totals.GetValueOrDefault(key) - cancelledQuantity);

        return totals
            .Where(x => x.Value > 0.0001m)
            .ToDictionary(x => x.Key, x => x.Value);
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

    private async Task CancelOpenTransfersForMaterialsAsync(
        string branch,
        string workOrderNumber,
        long? scopedTransferId,
        IReadOnlyDictionary<ProductionRecipeMaterialKey, decimal> cancelledQuantities,
        string reason,
        Guid idempotencyKey,
        long actor,
        CancellationToken ct)
    {
        var contexts = ProductionSourceWorkOrderAssignmentFilter.ProductionContexts;
        var links = await uow.Repository<ProductionTransferHeaderLink>().Query()
            .Where(x => x.BranchCode == branch
                && contexts.Contains(x.WarehouseTransferHeader.BusinessContext)
                && x.WarehouseTransferHeader.Status != WarehouseTransferStatus.Cancelled
                && x.WorkflowStatus != ProductionTransferWorkflowStatus.Cancelled
                && (x.ProductionOrderNo == workOrderNumber
                    || x.WarehouseTransferHeader.ExternalReferenceNo == workOrderNumber)
                && (!scopedTransferId.HasValue || x.WarehouseTransferHeaderId == scopedTransferId.Value))
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

            var affectedLineIds = link.Lines
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
                .Select(x => x.WarehouseTransferLineId)
                .Distinct()
                .ToArray();

            if (affectedLineIds.Length == 0) continue;

            if (header.Status == WarehouseTransferStatus.Draft)
            {
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
                netsis.IsClosed);
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

        var cancellable = await BuildCancellableRemainingQuantitiesAsync(branch, row, ct);
        return cancellable.Count == 0;
    }

    private static IReadOnlyList<ProductionSourceWorkOrderRow> MergeUnassignedWithCancellationRemaindersAsync(
        IReadOnlyList<ProductionSourceWorkOrderRow> unassigned,
        IReadOnlyList<ProductionSourceWorkOrderRow> cancellationRemainders,
        int take,
        WorkOrderAssignmentSnapshot assignmentSnapshot)
    {
        IEnumerable<ProductionSourceWorkOrderRow> combined = unassigned;
        if (cancellationRemainders.Count > 0)
        {
            var unassignedWorkOrders = new HashSet<string>(
                unassigned.Select(x => x.WorkOrderNumber.Trim()),
                StringComparer.OrdinalIgnoreCase);
            combined = unassigned.Concat(
                cancellationRemainders.Where(x => !unassignedWorkOrders.Contains(x.WorkOrderNumber.Trim())));
        }

        var filtered = new List<ProductionSourceWorkOrderRow>();
        foreach (var row in combined)
        {
            if (row.ListingKind == ProductionSourceWorkOrderListingKind.CancellationReturnRemainder)
            {
                filtered.Add(row);
                continue;
            }

            if (assignmentSnapshot.IsRemainingFullyCancelled(row))
                continue;

            filtered.Add(row);
        }

        return filtered
            .OrderByDescending(x => x.WorkOrderDate)
            .ThenBy(x => x.WorkOrderNumber, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.SourceSystemCode, StringComparer.OrdinalIgnoreCase)
            .Take(take)
            .ToArray();
    }
}
