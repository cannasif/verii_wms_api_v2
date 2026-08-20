using Microsoft.EntityFrameworkCore;
using verii_wms_api_v2.Modules.Location.Domain;
using verii_wms_api_v2.Modules.ProductionTransfer.Domain;
using verii_wms_api_v2.Modules.StockMovement.Application;
using verii_wms_api_v2.Modules.WarehouseOperations.Domain;
using verii_wms_api_v2.Modules.WarehouseTransfer.Domain;
using verii_wms_api_v2.Shared.Application.Abstractions.Persistence;
using verii_wms_api_v2.Shared.Application.Exceptions;
using WarehouseEntity = verii_wms_api_v2.Modules.Warehouse.Domain.Warehouse;

namespace verii_wms_api_v2.Modules.ProductionTransfer.Application;

internal static class ProductionTransferUnpickMovement
{
    internal const string BarcodeSource = "PickUnpick";

    internal static async Task<WarehouseLocation> ValidateTargetLocationAsync(
        IUnitOfWork uow,
        WarehouseTransferHeader header,
        long targetLocationId,
        CancellationToken ct)
    {
        if (header.SourceStagingLocationId == targetLocationId)
            throw AppException.BadRequest("Hedef raf bekleme rafı olamaz.");

        var location = await uow.Repository<WarehouseLocation>().Query()
            .SingleOrDefaultAsync(x => x.Id == targetLocationId, ct)
            ?? throw AppException.BadRequest("Seçilen raf bulunamadı.");
        if (location.WarehouseId != header.SourceWarehouseId)
            throw AppException.BadRequest("Seçilen raf kaynak depoya ait olmalıdır.");
        if (!location.IsActive || !location.IsPickable)
            throw AppException.BadRequest("Seçilen raf aktif ve toplanabilir olmalıdır.");

        var warehouseDefaults = await uow.Repository<WarehouseEntity>().Query()
            .Where(x => x.Id == header.SourceWarehouseId)
            .Select(x => new
            {
                x.DefaultGoodsReceiptLocationId,
                x.DefaultProductionTransferLocationId,
                x.ProductionPickingStagingLocationId
            })
            .SingleOrDefaultAsync(ct);

        if (!IsAllowedUnpickTargetLocation(
            location,
            header.SourceStagingLocationId,
            warehouseDefaults?.ProductionPickingStagingLocationId,
            warehouseDefaults?.DefaultProductionTransferLocationId,
            warehouseDefaults?.DefaultGoodsReceiptLocationId))
            throw AppException.BadRequest("Hedef raf yalnızca depo rafı veya mal kabul rafı olabilir.");

        return location;
    }

    internal static bool IsAllowedUnpickTargetLocation(
        WarehouseLocation location,
        long? waitingLocationId,
        long? pickingStagingLocationId,
        long? defaultProductionTransferLocationId,
        long? defaultGoodsReceiptLocationId)
    {
        if (waitingLocationId == location.Id) return false;
        if (pickingStagingLocationId == location.Id) return false;
        if (!location.IsActive || !location.IsPickable || location.IsQuarantine) return false;
        if (defaultProductionTransferLocationId == location.Id) return true;
        if (defaultGoodsReceiptLocationId == location.Id) return true;
        return location.LocationType is LocationTypes.Shelf
            or LocationTypes.Cell
            or LocationTypes.Rack
            or LocationTypes.Receiving;
    }

    internal static long ResolveStagingLocationId(
        WarehouseTransferHeader header,
        WarehouseTransferLine line,
        WarehouseTransferTaskLine taskLine,
        WarehouseTransferTracking? tracking)
    {
        // Barkod toplama stoku her zaman bekleme rafına taşır. Tracking.TargetLocationId
        // taslakta planlanan hedef/üretim rafı olarak kalabilir; geri alma kaynağı olamaz.
        if (header.SourceStagingLocationId is long waitingLocationId)
            return waitingLocationId;

        if (tracking?.TargetLocationId is long trackingStaging) return trackingStaging;
        var fromTracking = line.Trackings
            .Where(x => x.PickedQuantity > 0 && x.TargetLocationId.HasValue)
            .Select(x => x.TargetLocationId!.Value)
            .Distinct()
            .ToArray();
        if (fromTracking.Length == 1) return fromTracking[0];
        if (taskLine.TargetLocationId is long taskTarget) return taskTarget;
        throw AppException.Conflict("Kaynak depo için üretim transfer bekleme rafı tanımlanmamış.");
    }

    internal static StockMovementLineRequest BuildMovementLine(
        WarehouseTransferHeader header,
        WarehouseTransferLine line,
        long stagingLocationId,
        long targetLocationId,
        decimal quantity,
        string? lotNo,
        string? serialNo) =>
        new(
            line.StockId,
            line.YapCodeId,
            quantity,
            header.SourceWarehouseId,
            stagingLocationId,
            header.SourceWarehouseId,
            targetLocationId,
            line.UnitCode,
            lotNo,
            serialNo,
            null,
            line.SourceStockStatus,
            line.SourceStockStatus);

    internal static void ApplyUnpickedQuantities(
        WarehouseTransferLine line,
        WarehouseTransferTaskLine taskLine,
        decimal quantity,
        string? serialNo,
        long actor,
        DateTime utcNow)
    {
        if (quantity <= 0)
            throw AppException.BadRequest("Geri alınacak miktar geçersiz.");

        if (taskLine.ProcessedQuantity + 0.000001m < quantity)
            throw AppException.Conflict("Geri alınacak miktar toplanan miktardan fazla olamaz.");

        taskLine.ProcessedQuantity -= quantity;
        taskLine.UpdatedBy = actor;
        taskLine.UpdatedDate = utcNow;

        line.PickedQuantity = Math.Max(0, line.PickedQuantity - quantity);
        line.Status = line.PickedQuantity <= 0
            ? WarehouseTransferLineStatus.Open
            : line.PickedQuantity >= line.RequestedQuantity
                ? WarehouseTransferLineStatus.Picked
                : WarehouseTransferLineStatus.PartiallyPicked;
        line.UpdatedBy = actor;
        line.UpdatedDate = utcNow;

        if (line.Trackings.Count == 0) return;

        var remaining = quantity;
        IEnumerable<WarehouseTransferTracking> candidates = string.IsNullOrWhiteSpace(serialNo)
            ? line.Trackings.Where(x => x.PickedQuantity > 0).OrderByDescending(x => x.PickedQuantity)
            : line.Trackings.Where(x => x.PickedQuantity > 0 && SameTrackingValue(x.SerialNo, serialNo));

        foreach (var tracking in candidates)
        {
            if (remaining <= 0) break;
            var delta = Math.Min(tracking.PickedQuantity, remaining);
            tracking.PickedQuantity -= delta;
            remaining -= delta;
            tracking.Status = tracking.PickedQuantity > 0
                ? WarehouseTransferTrackingStatus.Picked
                : tracking.ReservedQuantity > 0
                    ? WarehouseTransferTrackingStatus.Reserved
                    : WarehouseTransferTrackingStatus.Planned;
            tracking.UpdatedBy = actor;
            tracking.UpdatedDate = utcNow;
        }

        if (remaining > 0.000001m)
            throw AppException.Conflict("Toplanmış seri/lot kaydı geri alınamadı.");
    }

    internal static void ApplyUnpickedRouteLocations(
        WarehouseTransferLine line,
        WarehouseTransferTaskLine taskLine,
        long targetLocationId,
        string? serialNo,
        long actor,
        DateTime utcNow)
    {
        if (!string.IsNullOrWhiteSpace(serialNo))
        {
            foreach (var tracking in line.Trackings.Where(x => SameTrackingValue(x.SerialNo, serialNo)))
            {
                tracking.SourceLocationId = targetLocationId;
                tracking.TargetLocationId = null;
                tracking.UpdatedBy = actor;
                tracking.UpdatedDate = utcNow;
            }

            RefreshOpenSerialSourceLocations(taskLine, line, actor, utcNow);
            taskLine.TargetLocationId = null;
            return;
        }

        line.DefaultSourceLocationId = targetLocationId;
        line.UpdatedBy = actor;
        line.UpdatedDate = utcNow;

        taskLine.SourceLocationId = targetLocationId;
        taskLine.TargetLocationId = null;
        taskLine.UpdatedBy = actor;
        taskLine.UpdatedDate = utcNow;
    }

    internal static WarehouseTransferTaskLine ReopenTransferredQuantityInActiveTask(
        WarehouseTransferHeader header,
        ProductionTransferHeaderLink link,
        WarehouseTransferTask activeTask,
        WarehouseTransferTaskLine sourceTaskLine,
        WarehouseTransferLine line,
        ProductionTransferLineLink lineLink,
        decimal quantity,
        long sourceLocationId,
        long actor,
        DateTime utcNow)
    {
        if (quantity <= 0)
            throw AppException.BadRequest("Aktif göreve aktarılacak miktar geçersiz.");

        var hasSerialTrackings = line.TrackingType is StockTrackingType.Serial or StockTrackingType.LotAndSerial;
        if (!hasSerialTrackings
            && ProductionTransferLineSplitHelper.TryMergeUnpickedQuantityAtLocation(
                header,
                link,
                activeTask,
                lineLink,
                line,
                sourceLocationId,
                quantity,
                excludePickedWtLineId: line.Id,
                actor,
                utcNow,
                out var mergedTaskLine)
            && mergedTaskLine is not null)
        {
            activeTask.UpdatedBy = actor;
            activeTask.UpdatedDate = utcNow;
            return mergedTaskLine;
        }

        var targetTaskLine = activeTask.Lines
            .Where(x => !x.IsDeleted && x.WtLineId == sourceTaskLine.WtLineId)
            .Where(x => hasSerialTrackings
                || (x.SourceLocationId ?? line.DefaultSourceLocationId) == sourceLocationId)
            .OrderBy(x => x.Id)
            .FirstOrDefault();

        if (targetTaskLine is null)
        {
            targetTaskLine = new WarehouseTransferTaskLine
            {
                BranchCode = activeTask.BranchCode,
                Line = line,
                WtLineId = line.Id,
                PlannedQuantity = quantity,
                ProcessedQuantity = 0,
                SourceLocationId = sourceLocationId,
                TargetLocationId = null,
                CreatedBy = actor,
                CreatedDate = utcNow
            };
            activeTask.Lines.Add(targetTaskLine);
        }
        else
        {
            targetTaskLine.PlannedQuantity += quantity;
            if (!hasSerialTrackings)
                targetTaskLine.SourceLocationId = sourceLocationId;
            targetTaskLine.UpdatedBy = actor;
            targetTaskLine.UpdatedDate = utcNow;
        }

        activeTask.UpdatedBy = actor;
        activeTask.UpdatedDate = utcNow;
        return targetTaskLine;
    }

    internal static void RefreshOpenSerialSourceLocations(
        WarehouseTransferTaskLine taskLine,
        WarehouseTransferLine line,
        long actor,
        DateTime utcNow)
    {
        var openLocations = line.Trackings
            .Where(x => x.PickedQuantity <= 0 && x.PlannedQuantity - x.PickedQuantity > 0)
            .Select(x => x.SourceLocationId ?? taskLine.SourceLocationId ?? line.DefaultSourceLocationId)
            .Where(x => x.HasValue)
            .Select(x => x!.Value)
            .Distinct()
            .ToArray();
        var pickedLocations = line.Trackings
            .Where(x => x.PickedQuantity > 0)
            .Select(x => x.SourceLocationId ?? taskLine.SourceLocationId ?? line.DefaultSourceLocationId)
            .Where(x => x.HasValue)
            .Select(x => x!.Value)
            .Distinct()
            .ToArray();

        if (openLocations.Length == 1)
        {
            taskLine.SourceLocationId = openLocations[0];
            line.DefaultSourceLocationId = openLocations[0];
        }
        else if (openLocations.Length > 1)
        {
            taskLine.SourceLocationId = null;
            if (pickedLocations.Length == 0)
                line.DefaultSourceLocationId = null;
        }

        line.UpdatedBy = actor;
        line.UpdatedDate = utcNow;
        taskLine.UpdatedBy = actor;
        taskLine.UpdatedDate = utcNow;
    }

    internal static void UpdateHeaderStatusAfterUnpick(WarehouseTransferHeader header, long actor)
    {
        var all = header.Lines.Where(x => !x.IsDeleted).ToArray();
        header.Status = all.All(x => x.PickedQuantity >= x.RequestedQuantity)
            ? WarehouseTransferStatus.Picked
            : all.Sum(x => x.PickedQuantity) > 0
                ? WarehouseTransferStatus.PartiallyPicked
                : WarehouseTransferStatus.Picking;
        header.UpdatedBy = actor;
        header.UpdatedDate = DateTime.UtcNow;
    }

    internal static void UpdateTaskStatusAfterUnpick(WarehouseTransferTask task, long actor)
    {
        var activeLines = task.Lines.Where(x => !x.IsDeleted).ToArray();
        if (activeLines.Length == 0) return;

        if (activeLines.All(x => x.ProcessedQuantity >= x.PlannedQuantity))
            task.Status = WarehouseTransferTaskStatus.PartiallyCompleted;
        else if (activeLines.Any(x => x.ProcessedQuantity > 0))
            task.Status = WarehouseTransferTaskStatus.InProgress;
        else
            task.Status = WarehouseTransferTaskStatus.InProgress;

        task.UpdatedBy = actor;
        task.UpdatedDate = DateTime.UtcNow;
    }

    internal static decimal NetBarcodeAcceptedQuantity(
        IEnumerable<ProductionTransferBarcodeScan> scans,
        string normalizedBarcode) =>
        scans
            .Where(x => x.NormalizedBarcode == normalizedBarcode)
            .Sum(x => x.BarcodeSource == BarcodeSource ? -x.Quantity : x.Quantity);

    internal static async Task AppendBarcodeUnpickJournalAsync(
        IUnitOfWork uow,
        ProductionTransferHeaderLink link,
        ProductionTransferLineLink lineLink,
        WarehouseTransferLine line,
        long stagingLocationId,
        long targetLocationId,
        decimal quantity,
        string? lotNo,
        string? serialNo,
        Guid idempotencyKey,
        long actor,
        CancellationToken token)
    {
        await uow.Repository<ProductionTransferBarcodeScan>().AddAsync(new()
        {
            BranchCode = link.BranchCode,
            CreatedBy = actor,
            CreatedDate = DateTime.UtcNow,
            ProductionTransferHeaderLinkId = link.Id,
            ProductionTransferLineLinkId = lineLink.Id,
            IdempotencyKey = idempotencyKey,
            BarcodeValue = $"UNPICK:{line.StockCodeSnapshot}",
            NormalizedBarcode = $"UNPICK:{line.StockCodeSnapshot}".ToUpperInvariant(),
            BarcodeSource = BarcodeSource,
            StockId = line.StockId,
            YapCodeId = line.YapCodeId,
            UnitCode = line.UnitCode,
            LotNo = lotNo,
            SerialNo = serialNo,
            Quantity = quantity,
            SourceLocationId = stagingLocationId,
            TargetLocationId = targetLocationId,
            ScannedAtUtc = DateTimeOffset.UtcNow
        }, token);
    }

    private static bool SameTrackingValue(string? left, string? right) =>
        string.Equals(
            string.IsNullOrWhiteSpace(left) ? null : left.Trim(),
            string.IsNullOrWhiteSpace(right) ? null : right.Trim(),
            StringComparison.OrdinalIgnoreCase);
}
