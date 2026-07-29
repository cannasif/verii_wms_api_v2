using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using verii_wms_api_v2.Modules.Audit.Application;
using verii_wms_api_v2.Modules.BarcodeDesigner.Application;
using verii_wms_api_v2.Modules.ErpIntegration.Application;
using verii_wms_api_v2.Modules.GoodsReceipt.Domain;
using verii_wms_api_v2.Modules.Location.Domain;
using verii_wms_api_v2.Modules.Quality.Application;
using verii_wms_api_v2.Modules.Quality.Domain;
using verii_wms_api_v2.Modules.SerialNumberPolicy.Application;
using verii_wms_api_v2.Modules.StockMovement.Application;
using verii_wms_api_v2.Modules.StockMovement.Domain;
using verii_wms_api_v2.Modules.StockTracking.Application;
using verii_wms_api_v2.Modules.WarehouseOperations.Domain;
using verii_wms_api_v2.Shared.Application.Abstractions.Persistence;
using verii_wms_api_v2.Shared.Application.Exceptions;

namespace verii_wms_api_v2.Modules.GoodsReceipt.Application;

/// <summary>
/// Posts one physical scan as immutable execution evidence. Projection updates, inventory movement,
/// quality hold and label consumption are committed in the same transaction.
/// </summary>
public sealed class GoodsReceiptExecutionService(
    IUnitOfWork uow,
    IStockMovementService stockMovement,
    IQualityPolicyResolver qualityPolicy,
    IStockTrackingPolicyResolver trackingPolicy,
    ISerialNumberPolicyResolver serialPolicy,
    IWarehouseBarcodeResolver barcodeResolver,
    IAuditLogWriter audit,
    IGoodsReceiptErpPostingCoordinator erpPosting,
    IGoodsReceiptOnReceiptLabelService onReceiptLabels) : IGoodsReceiptExecutionService
{
    public async Task<ReceiveGoodsReceiptTaskResult> ReceiveAsync(long taskId, ReceiveGoodsReceiptTaskRequest request,
        long actor, CancellationToken ct = default)
    {
        ValidateRequest(taskId, request);
        var requestHash = Hash(request);
        var result = await uow.ExecuteInTransactionAsync(async token =>
        {
            var replay = await uow.Repository<GoodsReceiptExecution>().Query()
                .Include(x => x.Lines).FirstOrDefaultAsync(x => x.IdempotencyKey == request.IdempotencyKey, token);
            if (replay is not null)
            {
                if (!FixedEquals(replay.RequestHash, requestHash) || replay.GrTaskId != taskId)
                    throw AppException.Conflict("Aynı idempotency anahtarı farklı bir okutma isteğinde kullanılamaz.");
                return await ReplayResult(replay, token);
            }

            var task = await uow.Repository<GoodsReceiptTask>().Query(true)
                .Include(x => x.Header).ThenInclude(x => x.Lines)
                .Include(x => x.Header).ThenInclude(x => x.Tasks)
                .Include(x => x.Assignments)
                .Include(x => x.Lines).ThenInclude(x => x.Line).ThenInclude(x => x.Sources)
                .Include(x => x.Lines).ThenInclude(x => x.Trackings)
                .FirstOrDefaultAsync(x => x.Id == taskId, token)
                ?? throw AppException.NotFound("Mal kabul emri bulunamadı.");
            if (task.Status != GoodsReceiptTaskStatus.InProgress)
                throw AppException.Conflict("Barkod okutabilmek için emir kabul edilip başlatılmalıdır.");
            var assignment = task.Assignments.FirstOrDefault(x => x.UserId == actor
                && x.Status == GoodsReceiptAssignmentStatus.InProgress)
                ?? throw AppException.Forbidden("Bu emir başlatılmış olarak size atanmamış.");

            GoodsReceiptLabel? label = null;
            if (!string.IsNullOrWhiteSpace(request.Barcode))
                label = await uow.Repository<GoodsReceiptLabel>().Query(true)
                    .FirstOrDefaultAsync(x => x.BarcodeValue == request.Barcode.Trim(), token);

            var requestedTaskLine = request.TaskLineId > 0
                ? task.Lines.FirstOrDefault(x => x.Id == request.TaskLineId)
                : null;
            ResolvedWarehouseBarcode? resolved = null;
            if (label is null && !string.IsNullOrWhiteSpace(request.Barcode))
                resolved = await barcodeResolver.ResolveAsync(new(
                    request.Barcode,
                    task.BranchCode,
                    WarehouseBarcodePurpose.Inbound,
                    task.WarehouseId,
                    requestedTaskLine?.Line.StockId), token);

            var resolvedTaskLine = resolved is null
                ? null
                : ResolveTaskLine(task.Lines, resolved);
            var taskLineId = request.TaskLineId > 0
                ? request.TaskLineId
                : label?.GrTaskLineId ?? resolvedTaskLine?.Id ?? 0;
            var taskLine = task.Lines.FirstOrDefault(x => x.Id == taskLineId)
                ?? throw AppException.BadRequest("Okutulan barkod bu emrin bir satırıyla eşleşmiyor.");
            if (resolved is not null && taskLine.Line.StockId != resolved.StockId)
                throw AppException.Conflict("Okutulan barkod seçilen emir satırındaki stokla uyuşmuyor.");
            ValidateLabel(label, task, taskLine);

            var quantity = label?.LabelQuantity ?? resolved?.Quantity ?? request.Quantity
                ?? throw AppException.BadRequest("Tedarikçi barkodunda kabul miktarı zorunludur.");
            var lot = Clean(label?.LotNo ?? resolved?.LotNo ?? request.LotNo, 100);
            var serial = Clean(label?.SerialNo ?? resolved?.SerialNo ?? request.SerialNo, 100);
            var manufacturingDate = label?.ManufacturingDate ?? resolved?.ManufacturingDate ?? request.ManufacturingDate;
            var expirationDate = label?.ExpirationDate ?? resolved?.ExpirationDate ?? request.ExpirationDate;
            var effectiveTrackingPolicy = await trackingPolicy.ResolveAsync(
                task.BranchCode, taskLine.Line.StockId, token);
            ValidateTracking(taskLine, effectiveTrackingPolicy, quantity, lot, serial, manufacturingDate, expirationDate);
            var serialValidation = await serialPolicy.ValidateAsync(task.BranchCode, taskLine.Line.StockId,
                taskLine.Line.YapCodeId, serial, token);
            if (!serialValidation.IsValid) throw AppException.BadRequest(serialValidation.Error ?? "Seri numarası geçersiz.");
            serial = serialValidation.NormalizedSerial;

            var locationId = request.ToLocationId ?? taskLine.ToLocationId
                ?? taskLine.Line.DefaultReceivingLocationId ?? task.Header.ReceivingLocationId;
            var location = await uow.Repository<WarehouseLocation>().FindByIdAsync(locationId, false, token)
                ?? throw AppException.BadRequest("Kabul rafı bulunamadı.");
            if (!location.IsActive || location.WarehouseId != task.WarehouseId)
                throw AppException.BadRequest("Kabul rafı aktif ve emir deposuna bağlı olmalıdır.");

            ValidateReceiptQuantity(
                quantity,
                taskLine.ProcessedQuantity,
                taskLine.PlannedQuantity,
                taskLine.Line.ReceivedQuantity,
                taskLine.Line.ExpectedQuantity,
                taskLine.Line.AllowOverReceipt,
                task.Header.AllowOverReceipt,
                taskLine.Line.OverReceiptTolerancePercent,
                task.Header.OverReceiptTolerancePercent);

            var stockGroupCode = await uow.Repository<Modules.Stock.Domain.Stock>().Query()
                .Where(x => x.Id == taskLine.Line.StockId && x.BranchCode == task.BranchCode)
                .Select(x => x.GroupCode)
                .FirstOrDefaultAsync(token);
            var policy = await qualityPolicy.ResolveAsync(
                task.BranchCode, taskLine.Line.StockId, stockGroupCode, token);
            var requiresQuality = GoodsReceiptOperationsService.RequiresQualityForLine(
                false,
                policy);
            taskLine.Line.RequireQualityControl = requiresQuality;
            var now = DateTimeOffset.UtcNow;
            QualityInspection? inspection = null;
            QualityInspectionLine? inspectionLine = null;
            if (requiresQuality)
            {
                inspection = await uow.Repository<QualityInspection>().Query(true)
                    .FirstOrDefaultAsync(x => x.SourceDocumentType == "GoodsReceipt"
                        && x.SourceDocumentId == task.Header.Id
                        && x.Status != QualityInspectionStatus.Cancelled, token);
                var isNewInspection = inspection is null;
                inspection ??= Stamp(new QualityInspection
                {
                    BranchCode = task.BranchCode,
                    CorrelationId = Guid.NewGuid(),
                    InspectionNo = $"QC-{task.Header.DocumentNo}",
                    SourceDocumentType = "GoodsReceipt",
                    SourceDocumentId = task.Header.Id,
                    SourceDocumentNo = task.Header.DocumentNo,
                    WarehouseId = task.WarehouseId,
                    SupplierId = task.Header.SupplierId,
                    Status = QualityInspectionStatus.Pending,
                    CreatedAtUtc = now
                }, actor);
                inspectionLine = Stamp(new QualityInspectionLine
                {
                    BranchCode = task.BranchCode,
                    Inspection = inspection,
                    GoodsReceiptLineId = taskLine.GrLineId,
                    StockId = taskLine.Line.StockId,
                    StockCodeSnapshot = taskLine.Line.StockCodeSnapshot,
                    StockNameSnapshot = taskLine.Line.StockNameSnapshot,
                    YapCodeId = taskLine.Line.YapCodeId,
                    YapCodeSnapshot = taskLine.Line.YapCodeSnapshot,
                    LotNo = lot,
                    SerialNo = serial,
                    ExpiryDate = expirationDate,
                    Quantity = quantity,
                    SampleQuantity = Sample(quantity, policy),
                    Decision = QualityDecision.Pending
                }, actor);
                inspection.Lines.Add(inspectionLine);
                if (isNewInspection)
                    await uow.Repository<QualityInspection>().AddAsync(inspection, token);
                await uow.SaveChangesAsync(token);
            }

            var executionCount = await uow.Repository<GoodsReceiptExecution>().Query()
                .CountAsync(x => x.GrHeaderId == task.GrHeaderId, token);
            var execution = Stamp(new GoodsReceiptExecution
            {
                BranchCode = task.BranchCode,
                Header = task.Header,
                Task = task,
                IdempotencyKey = request.IdempotencyKey,
                RequestHash = requestHash,
                ExecutionNo = ExecutionNo(task.Header.DocumentNo, executionCount + 1),
                Mode = label is not null ? GoodsReceiptExecutionMode.PreGeneratedLabel
                    : task.Header.LabelStrategy == GoodsReceiptLabelStrategy.SupplierLabel
                        ? GoodsReceiptExecutionMode.SupplierLabel : GoodsReceiptExecutionMode.BarcodeScan,
                Status = GoodsReceiptExecutionStatus.Posted,
                OccurredAtUtc = request.OccurredAtUtc?.ToUniversalTime() ?? now,
                DeviceId = Clean(request.DeviceId, 100)
            }, actor);
            execution.Lines.Add(Stamp(new GoodsReceiptExecutionLine
            {
                BranchCode = task.BranchCode,
                Line = taskLine.Line,
                LineNo = 1,
                StockId = taskLine.Line.StockId,
                YapCodeId = taskLine.Line.YapCodeId,
                Quantity = quantity,
                UnitCode = taskLine.Line.UnitCode,
                LotNo = lot,
                SerialNo = serial,
                SerialNumberRuleId = serialValidation.RuleId,
                SerialNumberRuleVersion = serialValidation.RuleVersion,
                SerialNumberRuleCodeSnapshot = serialValidation.RuleCode,
                SerialMaskSnapshot = serialValidation.MaskTemplate,
                ManufacturingDate = manufacturingDate,
                ExpirationDate = expirationDate,
                ScannedBarcode = Clean(request.Barcode, 250),
                WarehouseId = task.WarehouseId,
                LocationId = locationId,
                StockStatus = requiresQuality && task.Header.HoldInventoryUntilQualityDecision ? "QualityHold" : "Available",
                GoodsReceiptLabelId = label?.Id,
                QualityInspectionLineId = inspectionLine?.Id
            }, actor));
            await uow.Repository<GoodsReceiptExecution>().AddAsync(execution, token);
            await uow.SaveChangesAsync(token);

            var movement = await stockMovement.PostAsync(new PostStockMovementRequest(
                $"GR:{request.IdempotencyKey:N}", StockMovementTypes.Receipt, "GoodsReceipt",
                task.Header.DocumentNo, task.Header.Id, execution.OccurredAtUtc.UtcDateTime,
                "GoodsReceiptTaskScan", null,
                [new StockMovementLineRequest(taskLine.Line.StockId, taskLine.Line.YapCodeId, quantity,
                    null, null, task.WarehouseId, locationId, taskLine.Line.UnitCode, lot, serial,
                    execution.Lines.Single().StockStatus)]), token);
            execution.StockMovementOperationId = movement.OperationId;

            taskLine.ProcessedQuantity += quantity;
            taskLine.Status = taskLine.ProcessedQuantity >= taskLine.PlannedQuantity
                ? GoodsReceiptTaskStatus.Completed : GoodsReceiptTaskStatus.PartiallyCompleted;
            taskLine.Line.ReceivedQuantity += quantity;
            if (requiresQuality && task.Header.HoldInventoryUntilQualityDecision)
                taskLine.Line.QuarantineQuantity += quantity;
            else
                taskLine.Line.AcceptedQuantity += quantity;
            if (!requiresQuality && location.IsPutaway)
                taskLine.Line.PutawayQuantity += quantity;
            taskLine.Line.Status = taskLine.Line.ReceivedQuantity >= taskLine.Line.ExpectedQuantity
                ? GoodsReceiptLineStatus.Received : GoodsReceiptLineStatus.PartiallyReceived;
            AllocateSources(taskLine.Line, quantity);

            if (label is not null)
            {
                label.Status = GoodsReceiptLabelStatus.Consumed;
                label.ConsumedAtUtc = now;
                label.UpdatedBy = actor;
                label.UpdatedDate = DateTime.UtcNow;
                await RefreshBatch(label.BatchId, actor, token);
            }

            if (task.Lines.All(x => x.ProcessedQuantity >= x.PlannedQuantity))
            {
                task.Status = GoodsReceiptTaskStatus.Completed;
                task.CompletedAtUtc = now;
                foreach (var activeAssignment in task.Assignments.Where(x =>
                             x.Status is not (GoodsReceiptAssignmentStatus.Unassigned
                                 or GoodsReceiptAssignmentStatus.Rejected
                                 or GoodsReceiptAssignmentStatus.Completed)))
                {
                    activeAssignment.Status = GoodsReceiptAssignmentStatus.Completed;
                    activeAssignment.CompletedAtUtc ??= now;
                    activeAssignment.UpdatedBy = actor;
                    activeAssignment.UpdatedDate = DateTime.UtcNow;
                }
                var queuedInspection = inspection ?? await uow.Repository<QualityInspection>().Query(true)
                    .FirstOrDefaultAsync(x => x.SourceDocumentType == "GoodsReceipt"
                        && x.SourceDocumentId == task.Header.Id
                        && x.Status != QualityInspectionStatus.Cancelled, token);
                if (queuedInspection is not null && queuedInspection.QueuedAtUtc is null)
                {
                    queuedInspection.QueuedAtUtc = now;
                    queuedInspection.QueuedBy = actor;
                    queuedInspection.UpdatedBy = actor;
                    queuedInspection.UpdatedDate = DateTime.UtcNow;
                }
            }
            else task.Status = GoodsReceiptTaskStatus.InProgress;
            task.Header.ReceivedAtUtc ??= now;
            task.Header.ReceivedBy ??= actor;
            if (requiresQuality) task.Header.QualityStatus = OperationQualityStatus.InProgress;
            RefreshHeaderStatus(task.Header, actor);
            var generatedLabelIds = await onReceiptLabels.GenerateForExecutionAsync(
                task.Header, execution, execution.Lines.ToArray(), actor, token);
            await uow.SaveChangesAsync(token);
            await audit.WriteAsync(new("goods-receipt.task.scan", nameof(GoodsReceiptExecution), execution.Id.ToString(),
                "Succeeded", "goods-receipt", NewValues: new { TaskId = task.Id, TaskLineId = taskLine.Id, quantity, labelId = label?.Id,
                    movement.OperationId, inspectionId = inspection?.Id }, ChangedFields: ["Execution", "Inventory", "Task", "Quality"]), token);

            return Result(execution, task, taskLine, movement.OperationId, inspection?.Id, label?.Id,
                generatedLabelIds.SingleOrDefault(), false);
        }, ct, IsolationLevel.Serializable);
        await erpPosting.PostIfEligibleAsync(result.GoodsReceiptId, actor, ct);
        return result;
    }

    private async Task<ReceiveGoodsReceiptTaskResult> ReplayResult(GoodsReceiptExecution execution, CancellationToken ct)
    {
        var task = await uow.Repository<GoodsReceiptTask>().Query().Include(x => x.Lines)
            .FirstAsync(x => x.Id == execution.GrTaskId, ct);
        var line = execution.Lines.Single();
        var taskLine = task.Lines.Single(x => x.GrLineId == line.GrLineId);
        var inspectionId = line.QualityInspectionLineId.HasValue
            ? await uow.Repository<QualityInspectionLine>().Query().Where(x => x.Id == line.QualityInspectionLineId)
                .Select(x => (long?)x.QualityInspectionId).FirstOrDefaultAsync(ct) : null;
        var generatedLabelId = await uow.Repository<GoodsReceiptLabelBatch>().Query()
            .Where(x => x.CorrelationId == execution.IdempotencyKey)
            .SelectMany(x => x.Labels)
            .Select(x => (long?)x.Id)
            .FirstOrDefaultAsync(ct);
        return Result(execution, task, taskLine, execution.StockMovementOperationId ?? 0,
            inspectionId, line.GoodsReceiptLabelId, generatedLabelId, true);
    }

    private async Task RefreshBatch(long batchId, long actor, CancellationToken ct)
    {
        var batch = await uow.Repository<GoodsReceiptLabelBatch>().Query(true).Include(x => x.Labels)
            .FirstAsync(x => x.Id == batchId, ct);
        batch.PrintedLabelCount = batch.Labels.Count(x => x.PrintCount > 0);
        batch.ConsumedLabelCount = batch.Labels.Count(x => x.Status == GoodsReceiptLabelStatus.Consumed);
        batch.VoidLabelCount = batch.Labels.Count(x => x.Status == GoodsReceiptLabelStatus.Void);
        var finished = batch.ConsumedLabelCount + batch.VoidLabelCount == batch.TotalLabelCount;
        batch.Status = finished
            ? batch.ConsumedLabelCount == 0 ? GoodsReceiptLabelBatchStatus.Cancelled : GoodsReceiptLabelBatchStatus.Consumed
            : batch.ConsumedLabelCount > 0 ? GoodsReceiptLabelBatchStatus.PartiallyConsumed
            : batch.PrintedLabelCount == batch.TotalLabelCount ? GoodsReceiptLabelBatchStatus.Printed
            : batch.PrintedLabelCount > 0 ? GoodsReceiptLabelBatchStatus.PartiallyPrinted
            : GoodsReceiptLabelBatchStatus.Generated;
        if (finished) batch.CompletedAtUtc ??= DateTimeOffset.UtcNow;
        batch.UpdatedBy = actor;
        batch.UpdatedDate = DateTime.UtcNow;
    }

    internal static void ValidateLabel(GoodsReceiptLabel? label, GoodsReceiptTask task, GoodsReceiptTaskLine line)
    {
        if (label is null)
        {
            if (task.Header.LabelStrategy == GoodsReceiptLabelStrategy.PreGenerate)
                throw AppException.BadRequest(
                    "Bu emir ön etiketlidir. Ürün/tedarikçi barkodu kullanılamaz; " +
                    "oluşturup yazdırdığınız ön etiketin üzerindeki barkodu okutun.");
            return;
        }
        if (label.GrHeaderId != task.GrHeaderId || label.GrTaskLineId != line.Id || label.GrLineId != line.GrLineId)
            throw AppException.Conflict("Etiket başka bir mal kabul veya emir satırına ait.");
        if (label.Status == GoodsReceiptLabelStatus.Generated)
            throw AppException.Conflict("Etiket önce yazdırılmalı, ürüne yapıştırılmalı ve sonra okutulmalıdır.");
        if (label.Status == GoodsReceiptLabelStatus.Consumed)
            throw AppException.Conflict("Etiket daha önce kullanılmış.");
        if (label.Status == GoodsReceiptLabelStatus.Void)
            throw AppException.Conflict("İptal edilmiş etiket kullanılamaz.");
    }

    private static void ValidateTracking(
        GoodsReceiptTaskLine line,
        EffectiveStockTrackingPolicy policy,
        decimal quantity,
        string? lot,
        string? serial,
        DateOnly? manufacturingDate, DateOnly? expirationDate)
    {
        if (line.Line.RequireSerial && string.IsNullOrWhiteSpace(serial)) throw AppException.BadRequest("Seri numarası zorunludur.");
        if (line.Line.RequireLot && string.IsNullOrWhiteSpace(lot)) throw AppException.BadRequest("Lot numarası zorunludur.");
        if (line.Line.RequireManufacturingDate && !manufacturingDate.HasValue) throw AppException.BadRequest("Üretim tarihi zorunludur.");
        if (line.Line.RequireExpirationDate && !expirationDate.HasValue) throw AppException.BadRequest("Son kullanma tarihi zorunludur.");
        try
        {
            StockTrackingPolicyGuard.ValidateSerialQuantity(policy, quantity, serial);
        }
        catch (StockTrackingPolicyViolationException exception)
        {
            throw AppException.BadRequest(exception.Message);
        }
        if (manufacturingDate.HasValue && expirationDate.HasValue && expirationDate < manufacturingDate)
            throw AppException.BadRequest("Son kullanma tarihi üretim tarihinden önce olamaz.");
        if (line.Line.MinimumShelfLifeDays.HasValue && expirationDate.HasValue
            && expirationDate < DateOnly.FromDateTime(DateTime.UtcNow).AddDays(line.Line.MinimumShelfLifeDays.Value))
            throw AppException.BadRequest("Ürün minimum kalan raf ömrü koşulunu karşılamıyor.");
        if (line.Trackings.Count > 0 && !line.Trackings.Any(x => x.LotNo == lot && x.SerialNo == serial
                && x.ManufacturingDate == manufacturingDate && x.ExpirationDate == expirationDate))
            throw AppException.Conflict("Okutulan seri/lot emirde planlanan takip kaydıyla eşleşmiyor.");
    }

    private static void AllocateSources(GoodsReceiptLine line, decimal quantity)
    {
        var remaining = quantity;
        foreach (var source in line.Sources.OrderBy(x => x.Id))
        {
            var capacity = Math.Max(0, source.AllocatedQuantity - source.ReceivedQuantity);
            var allocated = Math.Min(capacity, remaining);
            source.ReceivedQuantity += allocated;
            remaining -= allocated;
            if (remaining <= 0) break;
        }
    }

    private static GoodsReceiptTaskLine? ResolveTaskLine(
        IEnumerable<GoodsReceiptTaskLine> lines,
        ResolvedWarehouseBarcode resolved)
    {
        var candidates = lines.Where(x =>
                x.Line.StockId == resolved.StockId
                && (!resolved.YapCodeId.HasValue || x.Line.YapCodeId == resolved.YapCodeId)
                && x.ProcessedQuantity < x.PlannedQuantity)
            .ToList();
        if (candidates.Count > 1)
            throw AppException.Conflict("Barkod birden fazla açık emir satırıyla eşleşiyor; emir satırını seçin.");
        return candidates.SingleOrDefault();
    }

    private static ReceiveGoodsReceiptTaskResult Result(GoodsReceiptExecution execution, GoodsReceiptTask task,
        GoodsReceiptTaskLine line, long movementId, long? inspectionId, long? labelId,
        long? generatedLabelId, bool replayed) =>
        new(execution.Id, movementId, task.GrHeaderId, task.Id, line.Id, line.ProcessedQuantity,
            Math.Max(0, line.PlannedQuantity - line.ProcessedQuantity), task.Status.ToString(), line.Status.ToString(),
            inspectionId, labelId, generatedLabelId, replayed);

    private static void ValidateRequest(long taskId, ReceiveGoodsReceiptTaskRequest request)
    {
        if (taskId <= 0 || request.IdempotencyKey == Guid.Empty || string.IsNullOrWhiteSpace(request.Barcode)
            || request.Barcode.Trim().Length > 250 || request.Quantity is <= 0)
            throw AppException.BadRequest("Barkod okutma isteği geçersiz.");
    }
    internal static void ValidateReceiptQuantity(
        decimal quantity,
        decimal processedQuantity,
        decimal plannedQuantity,
        decimal receivedQuantity,
        decimal expectedQuantity,
        bool lineAllowsOverReceipt,
        bool headerAllowsOverReceipt,
        decimal lineTolerancePercent,
        decimal headerTolerancePercent)
    {
        var allowsOverReceipt = lineAllowsOverReceipt || headerAllowsOverReceipt;
        var tolerancePercent = allowsOverReceipt
            ? Math.Max(lineTolerancePercent, headerTolerancePercent)
            : 0m;
        var maximumQuantity = expectedQuantity + expectedQuantity * tolerancePercent / 100m;
        var taskMaximumQuantity = allowsOverReceipt
            ? maximumQuantity
            : plannedQuantity;
        if (quantity <= 0
            || processedQuantity + quantity > taskMaximumQuantity
            || receivedQuantity + quantity > maximumQuantity)
            throw AppException.Conflict("Okutulan miktar emir veya fazla kabul toleransını aşıyor.");
    }
    internal static void RefreshHeaderStatus(GoodsReceiptHeader header, long actor)
    {
        var receiptTerminal = header.Tasks.Count > 0
            ? header.Tasks.All(x => x.Status is GoodsReceiptTaskStatus.Completed or GoodsReceiptTaskStatus.Cancelled)
            : header.Lines.All(x => x.ReceivedQuantity + x.ShortClosedQuantity >= x.ExpectedQuantity);
        if (!receiptTerminal)
        {
            header.Status = WarehouseOperationStatus.InProgress;
            return;
        }

        header.PutawayStatus = !header.RequirePutaway
            ? OperationPutawayStatus.NotRequired
            : header.Lines.All(x => x.PutawayQuantity >= x.AcceptedQuantity)
                ? OperationPutawayStatus.Completed
                : header.Lines.Any(x => x.PutawayQuantity > 0)
                    ? OperationPutawayStatus.PartiallyCompleted
                    : OperationPutawayStatus.Pending;
        var qualityTerminal = header.QualityStatus is OperationQualityStatus.NotRequired
            or OperationQualityStatus.Passed
            or OperationQualityStatus.Failed;
        var putawayTerminal = !header.RequirePutaway
            || header.PutawayStatus == OperationPutawayStatus.Completed;
        header.Status = qualityTerminal && putawayTerminal
            ? WarehouseOperationStatus.Completed
            : WarehouseOperationStatus.Processed;
        if (header.Status == WarehouseOperationStatus.Completed)
        {
            header.CompletedAtUtc ??= DateTimeOffset.UtcNow;
            header.CompletedBy ??= actor;
        }
    }
    private static decimal Sample(decimal quantity, ResolvedQualityPolicy policy) => policy.SamplingMode switch
    {
        QualitySamplingMode.Percentage => Math.Min(quantity, Math.Ceiling(quantity * policy.SamplingValue / 100m)),
        QualitySamplingMode.FixedQuantity => Math.Min(quantity, policy.SamplingValue),
        _ => quantity
    };
    private static string ExecutionNo(string documentNo, int sequence)
    { var value = $"{documentNo}-EX-{sequence:0000}"; return value.Length <= 60 ? value : value[^60..]; }
    private static string InspectionNo(string documentNo, Guid key)
    { var value = $"QC-{documentNo}-{key:N}"; return value.Length <= 60 ? value : value[..60]; }
    private static T Stamp<T>(T entity, long actor) where T : Shared.Domain.BaseEntity
    { entity.CreatedBy = actor; entity.CreatedDate = DateTime.UtcNow; return entity; }
    private static string? Clean(string? value, int max)
    { var text = string.IsNullOrWhiteSpace(value) ? null : value.Trim(); return text?.Length > max ? text[..max] : text; }
    private static string Hash(object value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value))));
    private static bool FixedEquals(string left, string right)
    {
        try { return CryptographicOperations.FixedTimeEquals(Convert.FromHexString(left), Convert.FromHexString(right)); }
        catch { return false; }
    }
}
