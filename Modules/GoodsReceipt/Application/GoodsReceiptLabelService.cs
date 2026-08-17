using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using verii_wms_api_v2.Modules.Audit.Application;
using verii_wms_api_v2.Modules.BarcodeDesigner.Application;
using verii_wms_api_v2.Modules.BarcodeDesigner.Domain;
using verii_wms_api_v2.Modules.GoodsReceipt.Domain;
using verii_wms_api_v2.Modules.GoodsReceipt.Localization;
using verii_wms_api_v2.Modules.StockTracking.Application;
using verii_wms_api_v2.Modules.StockTracking.Domain;
using verii_wms_api_v2.Shared;
using verii_wms_api_v2.Shared.Application.Abstractions.Persistence;
using verii_wms_api_v2.Shared.Application.Exceptions;

namespace verii_wms_api_v2.Modules.GoodsReceipt.Application;

public sealed class GoodsReceiptLabelService(
    IUnitOfWork uow,
    IBarcodePolicyService barcodePolicy,
    IAuditLogWriter audit,
    IStockTrackingPolicyResolver trackingPolicies,
    IStringLocalizer<GoodsReceiptResource> localizer) : IGoodsReceiptLabelService
{
    private static readonly IReadOnlyDictionary<string, string> GridSearchColumns =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["id"] = nameof(GoodsReceiptLabelBatchRow.Id),
            ["batchNo"] = nameof(GoodsReceiptLabelBatchRow.BatchNo),
            ["waybillNo"] = nameof(GoodsReceiptLabelBatchRow.WaybillSearchText),
            ["taskNo"] = nameof(GoodsReceiptLabelBatchRow.TaskNo),
            ["totalLabelCount"] = nameof(GoodsReceiptLabelBatchRow.TotalLabelCount),
            ["printedLabelCount"] = nameof(GoodsReceiptLabelBatchRow.PrintedLabelCount),
            ["consumedLabelCount"] = nameof(GoodsReceiptLabelBatchRow.ConsumedLabelCount)
        };
    private static readonly string[] DefaultGridSearchColumns = ["batchNo", "waybillNo", "taskNo"];

    private IGenericRepository<GoodsReceiptLabelBatch> Batches => uow.Repository<GoodsReceiptLabelBatch>();
    private IGenericRepository<GoodsReceiptLabel> Labels => uow.Repository<GoodsReceiptLabel>();

    public Task<GoodsReceiptLabelBatchDetail> GenerateAsync(long goodsReceiptId,
        GenerateGoodsReceiptLabelBatchRequest request, long actor,
        bool restrictToActorAssignment, CancellationToken ct = default)
    {
        if (goodsReceiptId <= 0 || request.TaskId <= 0 || request.IdempotencyKey == Guid.Empty
            || request.Lines is not { Count: > 0 and <= 500 }
            || request.Lines.Any(x => x.TaskLineId <= 0 || x.LabelCount is < 1 or > 500 || x.QuantityPerLabel <= 0)
            || request.Lines.GroupBy(x => x.TaskLineId).Any(x => x.Count() > 1))
            throw AppException.BadRequest("Etiket üretim isteği geçersiz.");

        return uow.ExecuteInTransactionAsync(async token =>
        {
            var task = await uow.Repository<GoodsReceiptTask>().Query()
                .Include(x => x.Header)
                .Include(x => x.Lines).ThenInclude(x => x.Line)
                .Include(x => x.Lines).ThenInclude(x => x.Trackings)
                .Include(x => x.Assignments)
                .FirstOrDefaultAsync(x => x.Id == request.TaskId && x.GrHeaderId == goodsReceiptId, token)
                ?? throw AppException.NotFound("Mal kabul emri bulunamadı.");
            if (task.Status is GoodsReceiptTaskStatus.Completed or GoodsReceiptTaskStatus.Cancelled)
                throw AppException.Conflict("Tamamlanmış veya iptal edilmiş emir için etiket üretilemez.");
            if (task.Header.LabelStrategy != GoodsReceiptLabelStrategy.PreGenerate)
                throw AppException.Conflict("Manuel ön etiket yalnızca 'Önceden üret' etiket stratejisinde oluşturulabilir.");
            if (restrictToActorAssignment && !HasActiveAssignment(task.Assignments, actor))
                throw AppException.Forbidden("Yalnızca size atanmış aktif mal kabul emirleri için etiket üretebilirsiniz.");

            var replay = await Batches.Query().Include(x => x.Labels)
                .FirstOrDefaultAsync(x => x.CorrelationId == request.IdempotencyKey, token);
            if (replay is not null)
            {
                if (replay.GrHeaderId != goodsReceiptId)
                    throw AppException.Conflict("Aynı idempotency anahtarı farklı bir mal kabul için kullanılamaz.");
                return await MapDetail(replay, token, task);
            }

            var selectedIds = request.Lines.Select(x => x.TaskLineId).ToHashSet();
            var selected = task.Lines.Where(x => selectedIds.Contains(x.Id)).ToDictionary(x => x.Id);
            if (selected.Count != selectedIds.Count) throw AppException.BadRequest("Etiket satırlarından biri seçilen emre ait değil.");

            var warehouse = await uow.Repository<verii_wms_api_v2.Modules.Warehouse.Domain.Warehouse>().FindByIdAsync(task.WarehouseId, false, token)
                ?? throw AppException.NotFound("Emir deposu bulunamadı.");
            var batch = Stamp(new GoodsReceiptLabelBatch
            {
                BranchCode = task.BranchCode,
                GrHeaderId = task.GrHeaderId,
                CorrelationId = request.IdempotencyKey,
                BatchNo = BatchNo(task.Header.DocumentNo, request.IdempotencyKey),
                Status = GoodsReceiptLabelBatchStatus.Draft,
                Description = Clean(request.Description, 500)
            }, actor);

            foreach (var input in request.Lines)
            {
                var taskLine = selected[input.TaskLineId];
                var remaining = taskLine.PlannedQuantity - taskLine.ProcessedQuantity;
                if (remaining <= 0) throw AppException.Conflict($"{taskLine.Line.StockCodeSnapshot} satırı tamamen işlendi.");
                var seeds = BuildSeeds(taskLine, input, remaining);
                var sequence = 0;
                foreach (var seed in seeds)
                {
                    var scope = seed.SerialNo is not null ? BarcodePolicyScope.ProductSerial
                        : seed.LotNo is not null ? BarcodePolicyScope.ProductLot : BarcodePolicyScope.Logistics;
                    var generated = await barcodePolicy.GenerateAsync(scope, new BarcodeGenerateRequest(
                        $"GR-LABEL:{request.IdempotencyKey:N}:{taskLine.Id}:{++sequence}",
                        taskLine.Line.StockCodeSnapshot, seed.SerialNo, taskLine.Line.YapCodeSnapshot,
                        seed.LotNo, warehouse.WarehouseCode.ToString(), null, task.Header.DocumentNo), token);
                    batch.Labels.Add(Stamp(new GoodsReceiptLabel
                    {
                        BranchCode = task.BranchCode,
                        GrHeaderId = task.Header.Id,
                        GrLineId = taskLine.GrLineId,
                        GrTaskLineId = taskLine.Id,
                        StockId = taskLine.Line.StockId,
                        StockCodeSnapshot = taskLine.Line.StockCodeSnapshot,
                        StockNameSnapshot = taskLine.Line.StockNameSnapshot,
                        YapCodeId = taskLine.Line.YapCodeId,
                        YapCodeSnapshot = taskLine.Line.YapCodeSnapshot,
                        LabelQuantity = seed.Quantity,
                        UnitCode = taskLine.UnitCode,
                        LotNo = seed.LotNo,
                        SerialNo = seed.SerialNo,
                        ManufacturingDate = seed.ManufacturingDate,
                        ExpirationDate = seed.ExpirationDate,
                        BarcodeValue = generated.Value,
                        Status = GoodsReceiptLabelStatus.Generated,
                        Description = seed.Description
                    }, actor));
                }
            }

            batch.TotalLabelCount = batch.Labels.Count;
            batch.Status = GoodsReceiptLabelBatchStatus.Generated;
            await Batches.AddAsync(batch, token);
            await uow.SaveChangesAsync(token);
            await audit.WriteAsync(new("goods-receipt.labels.generate", nameof(GoodsReceiptLabelBatch), batch.Id.ToString(),
                "Succeeded", "goods-receipt", NewValues: new { batch.BatchNo, task.Id, batch.TotalLabelCount },
                ChangedFields: ["Batch", "Labels", "BarcodePolicy"]), token);
            return await MapDetail(batch, token, task);
        }, ct, IsolationLevel.Serializable);
    }

    public async Task<PagedResponse<GoodsReceiptLabelBatchRow>> GetPagedAsync(PagedRequest request, CancellationToken ct = default)
    {
        var headers = uow.Repository<GoodsReceiptHeader>().Query();
        var labels = uow.Repository<GoodsReceiptLabel>().Query();
        var taskLines = uow.Repository<GoodsReceiptTaskLine>().Query();
        var tasks = uow.Repository<GoodsReceiptTask>().Query();
        var joined = from batch in Batches.Query()
                     join header in headers on batch.GrHeaderId equals header.Id
                     select new { Batch = batch, Header = header };
        var query = joined.Select(x => new GoodsReceiptLabelBatchRow(x.Batch.Id, x.Header.Id, x.Header.DocumentNo,
            x.Header.WaybillNo, x.Header.ElectronicWaybillNo,
            (from label in labels
             join taskLine in taskLines on label.GrTaskLineId equals (long?)taskLine.Id
             where label.BatchId == x.Batch.Id
             select (long?)taskLine.GrTaskId).FirstOrDefault(),
            (from label in labels
             join taskLine in taskLines on label.GrTaskLineId equals (long?)taskLine.Id
             join task in tasks on taskLine.GrTaskId equals task.Id
             where label.BatchId == x.Batch.Id
             select task.TaskNo).FirstOrDefault(),
            x.Batch.BatchNo, x.Batch.Status, x.Batch.TotalLabelCount, x.Batch.PrintedLabelCount,
            x.Batch.ConsumedLabelCount, x.Batch.VoidLabelCount, x.Batch.LastPrintedAtUtc,
            x.Batch.CreatedBy, x.Batch.CreatedDate, x.Batch.RowVersion,
            (x.Header.WaybillNo ?? "") + " " + (x.Header.ElectronicWaybillNo ?? "")));
        return await query.ApplySearch(request, GridSearchColumns, DefaultGridSearchColumns)
            .ApplyAdvancedFilters(request).ApplySort(request, nameof(GoodsReceiptLabelBatchRow.CreatedDate))
            .ToPagedResponseAsync(request, ct);
    }

    public async Task<GoodsReceiptLabelBatchDetail> GetAsync(long batchId, CancellationToken ct = default)
    {
        var batch = await Batches.Query().Include(x => x.Labels).FirstOrDefaultAsync(x => x.Id == batchId, ct)
            ?? throw AppException.NotFound("Etiket paketi bulunamadı.");
        return await MapDetail(batch, ct);
    }

    public async Task<IReadOnlyList<GoodsReceiptLabelRow>> GetForReceiptAsync(
        long goodsReceiptId, long? lineId = null, CancellationToken ct = default)
    {
        if (goodsReceiptId <= 0 || (lineId.HasValue && lineId.Value <= 0))
            throw AppException.BadRequest("Mal kabul veya kalem bilgisi geçersiz.");

        if (!await uow.Repository<GoodsReceiptHeader>().AnyAsync(x => x.Id == goodsReceiptId, ct))
            throw AppException.NotFound("Mal kabul bulunamadı.");

        if (lineId.HasValue && !await uow.Repository<GoodsReceiptLine>()
                .AnyAsync(x => x.Id == lineId.Value && x.GrHeaderId == goodsReceiptId, ct))
            throw AppException.NotFound("Mal kabul kalemi bulunamadı.");

        var query = Labels.Query().Where(x => x.GrHeaderId == goodsReceiptId
            && x.Status != GoodsReceiptLabelStatus.Void);
        if (lineId.HasValue) query = query.Where(x => x.GrLineId == lineId.Value);

        var rows = await query.OrderBy(x => x.GrLineId).ThenBy(x => x.Id).ToListAsync(ct);
        return await MapRowsAsync(rows, ct);
    }

    public Task MarkPrintedAsync(MarkGoodsReceiptLabelsPrintedRequest request, long actor,
        bool restrictToActorAssignment, CancellationToken ct = default)
    {
        var ids = request.LabelIds?.Where(x => x > 0).Distinct().ToArray() ?? [];
        if (ids.Length == 0 || ids.Length > 1000) throw AppException.BadRequest("Yazdırılan etiketler belirtilmelidir.");
        return uow.ExecuteInTransactionAsync(async token =>
        {
            var labels = await Labels.Query(true).Where(x => ids.Contains(x.Id)).ToListAsync(token);
            if (labels.Count != ids.Length) throw AppException.NotFound("Etiketlerden biri bulunamadı.");
            if (labels.Any(x => x.Status is GoodsReceiptLabelStatus.Void or GoodsReceiptLabelStatus.Split
                    || x.Status == GoodsReceiptLabelStatus.Consumed && !x.ParentLabelId.HasValue))
                throw AppException.Conflict("İptal veya tüketilmiş etiket yazdırılamaz.");
            if (restrictToActorAssignment)
                await EnsureLabelsBelongToActorAssignments(labels, actor, token);
            var now = DateTimeOffset.UtcNow;
            foreach (var label in labels)
            {
                if (label.Status != GoodsReceiptLabelStatus.Consumed)
                    label.Status = GoodsReceiptLabelStatus.Printed;
                label.PrintCount++;
                label.LastPrintedAtUtc = now;
                label.UpdatedBy = actor;
                label.UpdatedDate = DateTime.UtcNow;
            }
            await uow.SaveChangesAsync(token);
            await RefreshBatches(labels.Select(x => x.BatchId), actor, token);
            await audit.WriteAsync(new("goods-receipt.labels.print", nameof(GoodsReceiptLabel), string.Join(',', ids),
                "Succeeded", "goods-receipt", NewValues: new { Count = ids.Length }, ChangedFields: ["Status", "PrintCount"]), token);
            return true;
        }, ct);
    }

    private async Task EnsureLabelsBelongToActorAssignments(
        IReadOnlyCollection<GoodsReceiptLabel> labels, long actor, CancellationToken ct)
    {
        var directHeaderIds = labels.Where(x => !x.GrTaskLineId.HasValue)
            .Select(x => x.GrHeaderId).Distinct().ToArray();
        if (directHeaderIds.Length > 0)
        {
            var authorizedDirectCount = await uow.Repository<GoodsReceiptHeader>().Query()
                .CountAsync(x => directHeaderIds.Contains(x.Id)
                    && x.InitiationMode == GoodsReceiptInitiationMode.DirectReceipt
                    && (x.ReceivedBy == actor || x.CreatedBy == actor), ct);
            if (authorizedDirectCount != directHeaderIds.Length)
                throw AppException.Forbidden("Yalnızca kendi tamamladığınız direkt mal kabul etiketlerini yazdırabilirsiniz.");
        }

        var taskLineIds = labels.Select(x => x.GrTaskLineId).Where(x => x.HasValue)
            .Select(x => x!.Value).Distinct().ToArray();
        if (taskLineIds.Length == 0) return;

        var taskIds = await uow.Repository<GoodsReceiptTaskLine>().Query()
            .Where(x => taskLineIds.Contains(x.Id))
            .Select(x => x.GrTaskId)
            .Distinct()
            .ToArrayAsync(ct);
        if (taskIds.Length == 0)
            throw AppException.Forbidden("Bu etiketler atanmış bir mal kabul emrine ait değil.");

        var authorizedTaskCount = await uow.Repository<GoodsReceiptTaskAssignment>().Query()
            .Where(x => taskIds.Contains(x.GrTaskId)
                && x.UserId == actor
                && (x.Status == GoodsReceiptAssignmentStatus.Assigned
                    || x.Status == GoodsReceiptAssignmentStatus.Accepted
                    || x.Status == GoodsReceiptAssignmentStatus.InProgress
                    || x.Status == GoodsReceiptAssignmentStatus.Completed))
            .Select(x => x.GrTaskId)
            .Distinct()
            .CountAsync(ct);
        if (authorizedTaskCount != taskIds.Length)
            throw AppException.Forbidden("Yalnızca size atanmış aktif mal kabul emirlerinin etiketlerini yazdırabilirsiniz.");
    }

    private static bool HasActiveAssignment(IEnumerable<GoodsReceiptTaskAssignment> assignments, long actor)
        => assignments.Any(x => x.UserId == actor
            && x.Status is GoodsReceiptAssignmentStatus.Assigned
                or GoodsReceiptAssignmentStatus.Accepted
                or GoodsReceiptAssignmentStatus.InProgress);

    public Task VoidAsync(long labelId, VoidGoodsReceiptLabelRequest request, long actor, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length > 500)
            throw AppException.BadRequest("Etiket iptal nedeni zorunludur.");
        return uow.ExecuteInTransactionAsync(async token =>
        {
            var label = await Labels.FindByIdAsync(labelId, true, token) ?? throw AppException.NotFound("Etiket bulunamadı.");
            ApplyVersion(label, request.RowVersion);
            if (label.Status == GoodsReceiptLabelStatus.Consumed) throw AppException.Conflict("Tüketilmiş etiket iptal edilemez.");
            if (label.Status == GoodsReceiptLabelStatus.Split)
                throw AppException.Conflict(Message(GoodsReceiptMessageKeys.LabelSplitAlreadyCompleted));
            label.Status = GoodsReceiptLabelStatus.Void;
            label.VoidReason = request.Reason.Trim();
            label.UpdatedBy = actor;
            label.UpdatedDate = DateTime.UtcNow;
            await uow.SaveChangesAsync(token);
            await RefreshBatches([label.BatchId], actor, token);
            await audit.WriteAsync(new("goods-receipt.label.void", nameof(GoodsReceiptLabel), label.Id.ToString(),
                "Succeeded", "goods-receipt", Reason: label.VoidReason, ChangedFields: ["Status", "VoidReason"]), token);
            return true;
        }, ct);
    }

    public Task<SplitGoodsReceiptLabelResult> SplitAsync(long labelId,
        SplitGoodsReceiptLabelRequest request, long actor, CancellationToken ct = default)
    {
        var reason = request.Reason?.Trim();
        if (labelId <= 0 || request.IdempotencyKey == Guid.Empty || request.SplitQuantity <= 0
            || string.IsNullOrWhiteSpace(reason) || reason.Length is < 3 or > 500)
            throw AppException.BadRequest(Message(GoodsReceiptMessageKeys.LabelSplitInvalidRequest));

        return uow.ExecuteInTransactionAsync(async token =>
        {
            var source = await Labels.Query(true).Include(x => x.ChildLabels)
                .FirstOrDefaultAsync(x => x.Id == labelId, token)
                ?? throw AppException.NotFound(Message(GoodsReceiptMessageKeys.LabelSplitNotFound));

            if (source.SplitCorrelationId == request.IdempotencyKey)
            {
                var replaySourceRow = (await MapRowsAsync([source], token)).Single();
                var replayRows = await MapRowsAsync(source.ChildLabels.OrderBy(x => x.Id).ToList(), token);
                return new SplitGoodsReceiptLabelResult(replaySourceRow, replayRows, true);
            }
            if (source.Status == GoodsReceiptLabelStatus.Split)
                throw AppException.Conflict(Message(GoodsReceiptMessageKeys.LabelSplitAlreadyCompleted));
            if (source.Status == GoodsReceiptLabelStatus.Void || !source.StockId.HasValue)
                throw AppException.Conflict(Message(GoodsReceiptMessageKeys.LabelSplitUnavailable));
            if (request.SplitQuantity >= source.LabelQuantity
                || !HasSupportedQuantityScale(request.SplitQuantity))
                throw AppException.BadRequest(Message(GoodsReceiptMessageKeys.LabelSplitInvalidRequest));

            ApplyVersion(source, request.RowVersion);
            var policy = await trackingPolicies.ResolveAsync(source.BranchCode, source.StockId.Value, token);
            if (policy.SerialQuantityRule == SerialQuantityRule.OneSerialPerBaseUnit)
                throw AppException.Conflict(Message(
                    GoodsReceiptMessageKeys.LabelSplitSerialPerUnitBlocked, source.StockCodeSnapshot));

            var quantities = new[] { request.SplitQuantity, source.LabelQuantity - request.SplitQuantity };
            if (quantities.Any(x => x <= 0 || !HasSupportedQuantityScale(x))
                || quantities.Sum() != source.LabelQuantity)
                throw AppException.BadRequest(Message(GoodsReceiptMessageKeys.LabelSplitQuantityMismatch));

            var documentNo = await uow.Repository<GoodsReceiptHeader>().Query()
                .Where(x => x.Id == source.GrHeaderId)
                .Select(x => x.DocumentNo)
                .SingleAsync(token);

            var now = DateTimeOffset.UtcNow;
            var sourceStatus = source.Status;
            source.Status = GoodsReceiptLabelStatus.Split;
            source.SplitCorrelationId = request.IdempotencyKey;
            source.SplitAtUtc = now;
            source.SplitBy = actor;
            source.SplitReason = reason;
            source.UpdatedBy = actor;
            source.UpdatedDate = DateTime.UtcNow;

            var childStatus = sourceStatus == GoodsReceiptLabelStatus.Consumed
                ? GoodsReceiptLabelStatus.Consumed
                : GoodsReceiptLabelStatus.Generated;
            var children = new List<GoodsReceiptLabel>(2);
            for (var index = 0; index < quantities.Length; index++)
            {
                var barcode = await barcodePolicy.GenerateAsync(BarcodeScope(source), new BarcodeGenerateRequest(
                    $"GR-LABEL-SPLIT:{request.IdempotencyKey:N}:{index + 1}",
                    source.StockCodeSnapshot, source.SerialNo, source.YapCodeSnapshot, source.LotNo,
                    null, null, documentNo), token);
                var child = Stamp(new GoodsReceiptLabel
                {
                    BranchCode = source.BranchCode,
                    BatchId = source.BatchId,
                    GrHeaderId = source.GrHeaderId,
                    GrLineId = source.GrLineId,
                    GrTaskLineId = source.GrTaskLineId,
                    StockId = source.StockId,
                    StockCodeSnapshot = source.StockCodeSnapshot,
                    StockNameSnapshot = source.StockNameSnapshot,
                    YapCodeId = source.YapCodeId,
                    YapCodeSnapshot = source.YapCodeSnapshot,
                    LabelQuantity = quantities[index],
                    UnitCode = source.UnitCode,
                    LotNo = source.LotNo,
                    SerialNo = source.SerialNo,
                    ManufacturingDate = source.ManufacturingDate,
                    ExpirationDate = source.ExpirationDate,
                    BarcodeValue = barcode.Value,
                    Status = childStatus,
                    ConsumedAtUtc = childStatus == GoodsReceiptLabelStatus.Consumed
                        ? source.ConsumedAtUtc
                        : null,
                    ParentLabelId = source.Id,
                    RootLabelId = source.RootLabelId ?? source.Id,
                    Description = reason
                }, actor);
                children.Add(child);
            }

            await Labels.AddRangeAsync(children, token);
            await uow.SaveChangesAsync(token);
            await RefreshBatches([source.BatchId], actor, token);
            await audit.WriteAsync(new("goods-receipt.label.split", nameof(GoodsReceiptLabel), source.Id.ToString(),
                "Succeeded", "goods-receipt", Reason: reason,
                OldValues: new { source.BarcodeValue, SourceQuantity = source.LabelQuantity },
                NewValues: new
                {
                    source.SplitCorrelationId,
                    ChildLabels = children.Select(x => new { x.Id, x.BarcodeValue, x.LabelQuantity })
                },
                ChangedFields: ["Status", "SplitCorrelationId", "SplitAtUtc", "ChildLabels"]), token);

            var sourceRow = (await MapRowsAsync([source], token)).Single();
            var childRows = await MapRowsAsync(children, token);
            return new SplitGoodsReceiptLabelResult(sourceRow, childRows, false);
        }, ct, IsolationLevel.Serializable);
    }

    private async Task RefreshBatches(IEnumerable<long> batchIds, long actor, CancellationToken ct)
    {
        foreach (var id in batchIds.Distinct())
        {
            var batch = await Batches.Query(true).Include(x => x.Labels).FirstAsync(x => x.Id == id, ct);
            var activeLabels = batch.Labels.Where(x => x.Status != GoodsReceiptLabelStatus.Split).ToList();
            batch.TotalLabelCount = activeLabels.Count;
            batch.PrintedLabelCount = activeLabels.Count(x => x.PrintCount > 0);
            batch.ConsumedLabelCount = activeLabels.Count(x => x.Status == GoodsReceiptLabelStatus.Consumed);
            batch.VoidLabelCount = activeLabels.Count(x => x.Status == GoodsReceiptLabelStatus.Void);
            batch.LastPrintedAtUtc = batch.Labels.Max(x => x.LastPrintedAtUtc);
            batch.Status = batch.ConsumedLabelCount + batch.VoidLabelCount == batch.TotalLabelCount
                ? batch.ConsumedLabelCount == 0 ? GoodsReceiptLabelBatchStatus.Cancelled : GoodsReceiptLabelBatchStatus.Consumed
                : batch.ConsumedLabelCount > 0 ? GoodsReceiptLabelBatchStatus.PartiallyConsumed
                : batch.PrintedLabelCount == batch.TotalLabelCount ? GoodsReceiptLabelBatchStatus.Printed
                : batch.PrintedLabelCount > 0 ? GoodsReceiptLabelBatchStatus.PartiallyPrinted
                : GoodsReceiptLabelBatchStatus.Generated;
            if (batch.Status == GoodsReceiptLabelBatchStatus.Consumed) batch.CompletedAtUtc ??= DateTimeOffset.UtcNow;
            batch.UpdatedBy = actor;
            batch.UpdatedDate = DateTime.UtcNow;
        }
        await uow.SaveChangesAsync(ct);
    }

    private async Task<GoodsReceiptLabelBatchDetail> MapDetail(GoodsReceiptLabelBatch batch, CancellationToken ct, GoodsReceiptTask? task = null)
    {
        if (batch.Labels.Count == 0) batch = await Batches.Query().Include(x => x.Labels).FirstAsync(x => x.Id == batch.Id, ct);
        var header = await uow.Repository<GoodsReceiptHeader>().FindByIdAsync(batch.GrHeaderId, false, ct)
            ?? throw AppException.NotFound("Mal kabul bulunamadı.");
        var taskLineId = batch.Labels.Select(x => x.GrTaskLineId).FirstOrDefault(x => x.HasValue);
        if (task is null && taskLineId.HasValue)
        {
            task = await (from line in uow.Repository<GoodsReceiptTaskLine>().Query()
                          join taskRow in uow.Repository<GoodsReceiptTask>().Query() on line.GrTaskId equals taskRow.Id
                          where line.Id == taskLineId.Value select taskRow).FirstOrDefaultAsync(ct);
        }
        var row = new GoodsReceiptLabelBatchRow(batch.Id, header.Id, header.DocumentNo,
            header.WaybillNo, header.ElectronicWaybillNo, task?.Id, task?.TaskNo,
            batch.BatchNo, batch.Status, batch.TotalLabelCount, batch.PrintedLabelCount, batch.ConsumedLabelCount,
            batch.VoidLabelCount, batch.LastPrintedAtUtc, batch.CreatedBy, batch.CreatedDate, batch.RowVersion);
        return new(row, await MapRowsAsync(batch.Labels.OrderBy(x => x.Id).ToList(), ct));
    }

    private async Task<IReadOnlyList<GoodsReceiptLabelRow>> MapRowsAsync(
        IReadOnlyCollection<GoodsReceiptLabel> labels, CancellationToken ct)
    {
        var policies = new Dictionary<(string BranchCode, long StockId), EffectiveStockTrackingPolicy>();
        foreach (var group in labels.Where(x => x.StockId.HasValue)
                     .GroupBy(x => (x.BranchCode, StockId: x.StockId!.Value)))
            policies[group.Key] = await trackingPolicies.ResolveAsync(
                group.Key.BranchCode, group.Key.StockId, ct);
        return labels.Select(x => Map(x, x.StockId.HasValue
            ? policies.GetValueOrDefault((x.BranchCode, x.StockId.Value)) : null)).ToList();
    }

    private string? SplitBlockReason(GoodsReceiptLabel label, EffectiveStockTrackingPolicy? policy)
    {
        if (label.Status == GoodsReceiptLabelStatus.Split)
            return Message(GoodsReceiptMessageKeys.LabelSplitAlreadyCompleted);
        if (label.Status == GoodsReceiptLabelStatus.Void || !label.StockId.HasValue)
            return Message(GoodsReceiptMessageKeys.LabelSplitUnavailable);
        if (label.LabelQuantity < 0.000002m)
            return Message(GoodsReceiptMessageKeys.LabelSplitInvalidRequest);
        return policy?.SerialQuantityRule == SerialQuantityRule.OneSerialPerBaseUnit
            ? Message(GoodsReceiptMessageKeys.LabelSplitSerialPerUnitBlocked, label.StockCodeSnapshot)
            : null;
    }

    private static bool HasSupportedQuantityScale(decimal quantity)
        => decimal.Round(quantity, 6, MidpointRounding.AwayFromZero) == quantity;

    private GoodsReceiptLabelRow Map(GoodsReceiptLabel x, EffectiveStockTrackingPolicy? policy)
    {
        var splitBlockReason = SplitBlockReason(x, policy);
        return new(x.Id, x.BatchId, x.GrHeaderId, x.GrLineId,
            x.GrTaskLineId, x.StockId, x.StockCodeSnapshot, x.StockNameSnapshot, x.YapCodeSnapshot, x.LabelQuantity,
            x.UnitCode, x.LotNo, x.SerialNo, x.ManufacturingDate, x.ExpirationDate, x.BarcodeValue, x.Status,
            x.PrintCount, x.LastPrintedAtUtc, x.ConsumedAtUtc, x.VoidReason, x.ParentLabelId, x.RootLabelId,
            x.SplitAtUtc, x.SplitBy, x.SplitReason, splitBlockReason is null, splitBlockReason, x.RowVersion);
    }

    private static IReadOnlyList<LabelSeed> BuildSeeds(GoodsReceiptTaskLine line, GenerateGoodsReceiptLabelLineRequest input, decimal remaining)
    {
        if (line.Trackings.Count > 0)
        {
            var seeds = line.Trackings.OrderBy(x => x.SequenceNo)
                .Select(x => new LabelSeed(x.PlannedQuantity, Clean(x.LotNo, 100), Clean(x.SerialNo, 100),
                    x.ManufacturingDate, x.ExpirationDate, Clean(x.Description, 500))).ToList();
            if (seeds.Sum(x => x.Quantity) != remaining)
                throw AppException.Conflict($"{line.Line.StockCodeSnapshot} takip planı kalan emir miktarıyla uyuşmuyor.");
            return seeds;
        }
        var quantity = input.QuantityPerLabel ?? remaining / input.LabelCount;
        if (quantity <= 0 || quantity * input.LabelCount != remaining)
            throw AppException.BadRequest($"{line.Line.StockCodeSnapshot} için etiket miktarları kalan emir miktarına eşit olmalıdır.");
        return Enumerable.Range(1, input.LabelCount).Select(_ => new LabelSeed(quantity, null, null, null, null, null)).ToList();
    }

    private static string BatchNo(string documentNo, Guid key)
    {
        var suffix = key.ToString("N")[..8].ToUpperInvariant();
        var prefix = documentNo.Length > 36 ? documentNo[..36] : documentNo;
        return $"{prefix}-LB-{suffix}";
    }
    private static void ApplyVersion(GoodsReceiptLabel entity, string supplied)
    {
        try { entity.RowVersion = Convert.FromBase64String(supplied); }
        catch { throw AppException.Conflict("Etiket güncellik bilgisi geçersiz. Listeyi yenileyin."); }
    }
    private static T Stamp<T>(T entity, long actor) where T : Shared.Domain.BaseEntity
    { entity.CreatedBy = actor; entity.CreatedDate = DateTime.UtcNow; return entity; }
    private static string? Clean(string? value, int max)
    { var text = string.IsNullOrWhiteSpace(value) ? null : value.Trim(); return text?.Length > max ? text[..max] : text; }
    private sealed record LabelSeed(decimal Quantity, string? LotNo, string? SerialNo,
        DateOnly? ManufacturingDate, DateOnly? ExpirationDate, string? Description);

    private static BarcodePolicyScope BarcodeScope(GoodsReceiptLabel label) =>
        !string.IsNullOrWhiteSpace(label.SerialNo) ? BarcodePolicyScope.ProductSerial
        : !string.IsNullOrWhiteSpace(label.LotNo) ? BarcodePolicyScope.ProductLot
        : BarcodePolicyScope.Logistics;

    private string Message(string key, params object[] arguments) => localizer[key, arguments].Value;
}
