using System.Data;
using Microsoft.EntityFrameworkCore;
using verii_wms_api_v2.Modules.Audit.Application;
using verii_wms_api_v2.Modules.BarcodeDesigner.Application;
using verii_wms_api_v2.Modules.BarcodeDesigner.Domain;
using verii_wms_api_v2.Modules.WarehouseInbound.Domain;
using verii_wms_api_v2.Shared;
using verii_wms_api_v2.Shared.Application.Abstractions.Persistence;
using verii_wms_api_v2.Shared.Application.Exceptions;

namespace verii_wms_api_v2.Modules.WarehouseInbound.Application;

public sealed class WarehouseInboundLabelService(
    IUnitOfWork uow,
    IBarcodePolicyService barcodePolicy,
    IAuditLogWriter audit) : IWarehouseInboundLabelService
{
    private IGenericRepository<WarehouseInboundLabelBatch> Batches => uow.Repository<WarehouseInboundLabelBatch>();
    private IGenericRepository<WarehouseInboundLabel> Labels => uow.Repository<WarehouseInboundLabel>();

    public Task<WarehouseInboundLabelBatchDetail> GenerateAsync(long goodsReceiptId,
        GenerateWarehouseInboundLabelBatchRequest request, long actor, CancellationToken ct = default)
    {
        if (goodsReceiptId <= 0 || request.TaskId <= 0 || request.IdempotencyKey == Guid.Empty
            || request.Lines is not { Count: > 0 and <= 500 }
            || request.Lines.Any(x => x.TaskLineId <= 0 || x.LabelCount is < 1 or > 500 || x.QuantityPerLabel <= 0)
            || request.Lines.GroupBy(x => x.TaskLineId).Any(x => x.Count() > 1))
            throw AppException.BadRequest("Etiket üretim isteği geçersiz.");

        return uow.ExecuteInTransactionAsync(async token =>
        {
            var replay = await Batches.Query().Include(x => x.Labels)
                .FirstOrDefaultAsync(x => x.CorrelationId == request.IdempotencyKey, token);
            if (replay is not null)
            {
                if (replay.GrHeaderId != goodsReceiptId) throw AppException.Conflict("Aynı idempotency anahtarı farklı bir mal kabul için kullanılamaz.");
                return await MapDetail(replay, token);
            }

            var task = await uow.Repository<WarehouseInboundTask>().Query()
                .Include(x => x.Header)
                .Include(x => x.Lines).ThenInclude(x => x.Line)
                .Include(x => x.Lines).ThenInclude(x => x.Trackings)
                .FirstOrDefaultAsync(x => x.Id == request.TaskId && x.GrHeaderId == goodsReceiptId, token)
                ?? throw AppException.NotFound("Mal kabul emri bulunamadı.");
            if (task.Status is WarehouseInboundTaskStatus.Completed or WarehouseInboundTaskStatus.Cancelled)
                throw AppException.Conflict("Tamamlanmış veya iptal edilmiş emir için etiket üretilemez.");

            var selectedIds = request.Lines.Select(x => x.TaskLineId).ToHashSet();
            var selected = task.Lines.Where(x => selectedIds.Contains(x.Id)).ToDictionary(x => x.Id);
            if (selected.Count != selectedIds.Count) throw AppException.BadRequest("Etiket satırlarından biri seçilen emre ait değil.");

            var warehouse = await uow.Repository<verii_wms_api_v2.Modules.Warehouse.Domain.Warehouse>().FindByIdAsync(task.WarehouseId, false, token)
                ?? throw AppException.NotFound("Emir deposu bulunamadı.");
            var batch = Stamp(new WarehouseInboundLabelBatch
            {
                BranchCode = task.BranchCode,
                GrHeaderId = task.GrHeaderId,
                CorrelationId = request.IdempotencyKey,
                BatchNo = BatchNo(task.Header.DocumentNo, request.IdempotencyKey),
                Status = WarehouseInboundLabelBatchStatus.Draft,
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
                    batch.Labels.Add(Stamp(new WarehouseInboundLabel
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
                        Status = WarehouseInboundLabelStatus.Generated,
                        Description = seed.Description
                    }, actor));
                }
            }

            batch.TotalLabelCount = batch.Labels.Count;
            batch.Status = WarehouseInboundLabelBatchStatus.Generated;
            await Batches.AddAsync(batch, token);
            await uow.SaveChangesAsync(token);
            await audit.WriteAsync(new("warehouse-inbound.labels.generate", nameof(WarehouseInboundLabelBatch), batch.Id.ToString(),
                "Succeeded", "warehouse-inbound", NewValues: new { batch.BatchNo, task.Id, batch.TotalLabelCount },
                ChangedFields: ["Batch", "Labels", "BarcodePolicy"]), token);
            return await MapDetail(batch, token, task);
        }, ct, IsolationLevel.Serializable);
    }

    public async Task<PagedResponse<WarehouseInboundLabelBatchRow>> GetPagedAsync(PagedRequest request, CancellationToken ct = default)
    {
        var headers = uow.Repository<WarehouseInboundHeader>().Query();
        var labels = Labels.Query();
        var taskLines = uow.Repository<WarehouseInboundTaskLine>().Query();
        var tasks = uow.Repository<WarehouseInboundTask>().Query();
        var query = BuildPagedQuery(request, Batches.Query(), headers, labels, taskLines, tasks);
        var countQuery = BuildCountQuery(request, Batches.Query(), headers, labels, taskLines, tasks);
        var page = await query.ToPagedResponseAsync(countQuery, request, ct);
        if (page.Items.Count == 0) return page;

        return new PagedResponse<WarehouseInboundLabelBatchRow>
        {
            Items = await EnrichTaskReferencesAsync(page.Items, labels, taskLines, tasks, ct),
            TotalCount = page.TotalCount,
            PageNumber = page.PageNumber,
            PageSize = page.PageSize
        };
    }

    internal static IQueryable<WarehouseInboundLabelBatchRow> BuildPagedQuery(
        PagedRequest request,
        IQueryable<WarehouseInboundLabelBatch> batches,
        IQueryable<WarehouseInboundHeader> headers,
        IQueryable<WarehouseInboundLabel> labels,
        IQueryable<WarehouseInboundTaskLine> taskLines,
        IQueryable<WarehouseInboundTask> tasks)
    {
        var query = BuildGridRows(request, batches, headers, labels, taskLines, tasks, RequiresTaskReferenceInMainQuery(request));
        if (request.HasExplicitSearchFields) query = query.ApplySearch(request);
        return query.ApplyAdvancedFilters(request).ApplySort(request, nameof(WarehouseInboundLabelBatchRow.CreatedDate))
            .Select(x => new WarehouseInboundLabelBatchRow(x.Id, x.WarehouseInboundId, x.DocumentNo,
                x.TaskId, x.TaskNo, x.BatchNo, x.Status, x.TotalLabelCount, x.PrintedLabelCount,
                x.ConsumedLabelCount, x.VoidLabelCount, x.LastPrintedAtUtc, x.CreatedBy, x.CreatedDate,
                x.RowVersion));
    }

    internal static IQueryable<long> BuildCountQuery(
        PagedRequest request,
        IQueryable<WarehouseInboundLabelBatch> batches,
        IQueryable<WarehouseInboundHeader> headers,
        IQueryable<WarehouseInboundLabel> labels,
        IQueryable<WarehouseInboundTaskLine> taskLines,
        IQueryable<WarehouseInboundTask> tasks)
    {
        var query = BuildGridRows(request, batches, headers, labels, taskLines, tasks, RequiresTaskReferenceForCount(request));
        if (request.HasExplicitSearchFields) query = query.ApplySearch(request);
        return query.ApplyAdvancedFilters(request).Select(x => x.Id);
    }

    private static IQueryable<WarehouseInboundLabelGridProjection> BuildGridRows(
        PagedRequest request,
        IQueryable<WarehouseInboundLabelBatch> batches,
        IQueryable<WarehouseInboundHeader> headers,
        IQueryable<WarehouseInboundLabel> labels,
        IQueryable<WarehouseInboundTaskLine> taskLines,
        IQueryable<WarehouseInboundTask> tasks,
        bool includeTaskReference)
    {
        var search = request.Search?.Trim();
        var joined = from batch in batches
                     join header in headers on batch.GrHeaderId equals header.Id
                     where string.IsNullOrWhiteSpace(search)
                           || batch.BatchNo.Contains(search)
                           || header.DocumentNo.Contains(search)
                     select new { Batch = batch, Header = header };
        if (!includeTaskReference)
            return joined.Select(x => new WarehouseInboundLabelGridProjection
            {
                Id = x.Batch.Id, WarehouseInboundId = x.Header.Id, DocumentNo = x.Header.DocumentNo,
                BatchNo = x.Batch.BatchNo, Status = x.Batch.Status, TotalLabelCount = x.Batch.TotalLabelCount,
                PrintedLabelCount = x.Batch.PrintedLabelCount, ConsumedLabelCount = x.Batch.ConsumedLabelCount,
                VoidLabelCount = x.Batch.VoidLabelCount, LastPrintedAtUtc = x.Batch.LastPrintedAtUtc,
                CreatedBy = x.Batch.CreatedBy, CreatedDate = x.Batch.CreatedDate, RowVersion = x.Batch.RowVersion
            });

        var taskReferences = from label in labels
                             join taskLine in taskLines on label.GrTaskLineId equals (long?)taskLine.Id
                             join task in tasks on taskLine.GrTaskId equals task.Id
                             group label by new { label.BatchId, TaskId = task.Id, task.TaskNo } into grouped
                             select grouped.Key;
        return from x in joined
               join taskReference in taskReferences on x.Batch.Id equals taskReference.BatchId into taskReferenceRows
               from taskReference in taskReferenceRows.DefaultIfEmpty()
               select new WarehouseInboundLabelGridProjection
               {
                   Id = x.Batch.Id, WarehouseInboundId = x.Header.Id, DocumentNo = x.Header.DocumentNo,
                   TaskId = taskReference == null ? null : taskReference.TaskId,
                   TaskNo = taskReference == null ? null : taskReference.TaskNo,
                   BatchNo = x.Batch.BatchNo, Status = x.Batch.Status, TotalLabelCount = x.Batch.TotalLabelCount,
                   PrintedLabelCount = x.Batch.PrintedLabelCount, ConsumedLabelCount = x.Batch.ConsumedLabelCount,
                   VoidLabelCount = x.Batch.VoidLabelCount, LastPrintedAtUtc = x.Batch.LastPrintedAtUtc,
                   CreatedBy = x.Batch.CreatedBy, CreatedDate = x.Batch.CreatedDate, RowVersion = x.Batch.RowVersion
               };
    }

    private static bool RequiresTaskReferenceForCount(PagedRequest request) =>
        (!string.IsNullOrWhiteSpace(request.EffectiveSearch)
         && request.SearchFields.Any(field => field.Equals("taskId", StringComparison.OrdinalIgnoreCase)
                                               || field.Equals("taskNo", StringComparison.OrdinalIgnoreCase)))
        || request.Filters.Any(filter => filter.Column.Equals("taskId", StringComparison.OrdinalIgnoreCase)
                                         || filter.Column.Equals("taskNo", StringComparison.OrdinalIgnoreCase));

    private static bool RequiresTaskReferenceInMainQuery(PagedRequest request) =>
        RequiresTaskReferenceForCount(request)
        || string.Equals(request.SortBy, "taskId", StringComparison.OrdinalIgnoreCase)
        || string.Equals(request.SortBy, "taskNo", StringComparison.OrdinalIgnoreCase);

    private static async Task<IReadOnlyList<WarehouseInboundLabelBatchRow>> EnrichTaskReferencesAsync(
        IReadOnlyList<WarehouseInboundLabelBatchRow> rows,
        IQueryable<WarehouseInboundLabel> labels,
        IQueryable<WarehouseInboundTaskLine> taskLines,
        IQueryable<WarehouseInboundTask> tasks,
        CancellationToken cancellationToken)
    {
        var batchIds = rows.Select(x => x.Id).ToArray();
        var references = await (from label in labels
                                join taskLine in taskLines on label.GrTaskLineId equals (long?)taskLine.Id
                                join task in tasks on taskLine.GrTaskId equals task.Id
                                where batchIds.Contains(label.BatchId)
                                select new { label.BatchId, LabelId = label.Id, TaskId = task.Id, task.TaskNo })
            .ToListAsync(cancellationToken);
        var referenceByBatch = references.OrderBy(x => x.LabelId).GroupBy(x => x.BatchId)
            .ToDictionary(x => x.Key, x => x.First());
        return rows.Select(row => referenceByBatch.TryGetValue(row.Id, out var reference)
            ? row with { TaskId = reference.TaskId, TaskNo = reference.TaskNo }
            : row).ToArray();
    }

    private sealed class WarehouseInboundLabelGridProjection
    {
        public long Id { get; init; }
        public long WarehouseInboundId { get; init; }
        public required string DocumentNo { get; init; }
        public long? TaskId { get; init; }
        public string? TaskNo { get; init; }
        public required string BatchNo { get; init; }
        public WarehouseInboundLabelBatchStatus Status { get; init; }
        public int TotalLabelCount { get; init; }
        public int PrintedLabelCount { get; init; }
        public int ConsumedLabelCount { get; init; }
        public int VoidLabelCount { get; init; }
        public DateTimeOffset? LastPrintedAtUtc { get; init; }
        public long? CreatedBy { get; init; }
        public DateTime? CreatedDate { get; init; }
        public required byte[] RowVersion { get; init; }
    }

    public async Task<WarehouseInboundLabelBatchDetail> GetAsync(long batchId, CancellationToken ct = default)
    {
        var batch = await Batches.Query().Include(x => x.Labels).FirstOrDefaultAsync(x => x.Id == batchId, ct)
            ?? throw AppException.NotFound("Etiket paketi bulunamadı.");
        return await MapDetail(batch, ct);
    }

    public async Task<IReadOnlyList<WarehouseInboundLabelRow>> GetForReceiptAsync(
        long goodsReceiptId, long? lineId = null, CancellationToken ct = default)
    {
        if (goodsReceiptId <= 0 || (lineId.HasValue && lineId.Value <= 0))
            throw AppException.BadRequest("Mal kabul veya kalem bilgisi geçersiz.");

        if (!await uow.Repository<WarehouseInboundHeader>().AnyAsync(x => x.Id == goodsReceiptId, ct))
            throw AppException.NotFound("Mal kabul bulunamadı.");

        if (lineId.HasValue && !await uow.Repository<WarehouseInboundLine>()
                .AnyAsync(x => x.Id == lineId.Value && x.GrHeaderId == goodsReceiptId, ct))
            throw AppException.NotFound("Mal kabul kalemi bulunamadı.");

        var query = Labels.Query().Where(x => x.GrHeaderId == goodsReceiptId
            && x.Status != WarehouseInboundLabelStatus.Void);
        if (lineId.HasValue) query = query.Where(x => x.GrLineId == lineId.Value);

        var rows = await query.OrderBy(x => x.GrLineId).ThenBy(x => x.Id).ToListAsync(ct);
        return rows.Select(Map).ToList();
    }

    public Task MarkPrintedAsync(MarkWarehouseInboundLabelsPrintedRequest request, long actor, CancellationToken ct = default)
    {
        var ids = request.LabelIds?.Where(x => x > 0).Distinct().ToArray() ?? [];
        if (ids.Length == 0 || ids.Length > 1000) throw AppException.BadRequest("Yazdırılan etiketler belirtilmelidir.");
        return uow.ExecuteInTransactionAsync(async token =>
        {
            var labels = await Labels.Query(true).Where(x => ids.Contains(x.Id)).ToListAsync(token);
            if (labels.Count != ids.Length) throw AppException.NotFound("Etiketlerden biri bulunamadı.");
            if (labels.Any(x => x.Status is WarehouseInboundLabelStatus.Void or WarehouseInboundLabelStatus.Consumed))
                throw AppException.Conflict("İptal veya tüketilmiş etiket yazdırılamaz.");
            var now = DateTimeOffset.UtcNow;
            foreach (var label in labels)
            {
                label.Status = WarehouseInboundLabelStatus.Printed;
                label.PrintCount++;
                label.LastPrintedAtUtc = now;
                label.UpdatedBy = actor;
                label.UpdatedDate = DateTime.UtcNow;
            }
            await uow.SaveChangesAsync(token);
            await RefreshBatches(labels.Select(x => x.BatchId), actor, token);
            await audit.WriteAsync(new("warehouse-inbound.labels.print", nameof(WarehouseInboundLabel), string.Join(',', ids),
                "Succeeded", "warehouse-inbound", NewValues: new { Count = ids.Length }, ChangedFields: ["Status", "PrintCount"]), token);
            return true;
        }, ct);
    }

    public Task VoidAsync(long labelId, VoidWarehouseInboundLabelRequest request, long actor, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length > 500)
            throw AppException.BadRequest("Etiket iptal nedeni zorunludur.");
        return uow.ExecuteInTransactionAsync(async token =>
        {
            var label = await Labels.FindByIdAsync(labelId, true, token) ?? throw AppException.NotFound("Etiket bulunamadı.");
            ApplyVersion(label, request.RowVersion);
            if (label.Status == WarehouseInboundLabelStatus.Consumed) throw AppException.Conflict("Tüketilmiş etiket iptal edilemez.");
            label.Status = WarehouseInboundLabelStatus.Void;
            label.VoidReason = request.Reason.Trim();
            label.UpdatedBy = actor;
            label.UpdatedDate = DateTime.UtcNow;
            await uow.SaveChangesAsync(token);
            await RefreshBatches([label.BatchId], actor, token);
            await audit.WriteAsync(new("warehouse-inbound.label.void", nameof(WarehouseInboundLabel), label.Id.ToString(),
                "Succeeded", "warehouse-inbound", Reason: label.VoidReason, ChangedFields: ["Status", "VoidReason"]), token);
            return true;
        }, ct);
    }

    private async Task RefreshBatches(IEnumerable<long> batchIds, long actor, CancellationToken ct)
    {
        foreach (var id in batchIds.Distinct())
        {
            var batch = await Batches.Query(true).Include(x => x.Labels).FirstAsync(x => x.Id == id, ct);
            batch.PrintedLabelCount = batch.Labels.Count(x => x.PrintCount > 0);
            batch.ConsumedLabelCount = batch.Labels.Count(x => x.Status == WarehouseInboundLabelStatus.Consumed);
            batch.VoidLabelCount = batch.Labels.Count(x => x.Status == WarehouseInboundLabelStatus.Void);
            batch.LastPrintedAtUtc = batch.Labels.Max(x => x.LastPrintedAtUtc);
            batch.Status = batch.ConsumedLabelCount + batch.VoidLabelCount == batch.TotalLabelCount
                ? batch.ConsumedLabelCount == 0 ? WarehouseInboundLabelBatchStatus.Cancelled : WarehouseInboundLabelBatchStatus.Consumed
                : batch.ConsumedLabelCount > 0 ? WarehouseInboundLabelBatchStatus.PartiallyConsumed
                : batch.PrintedLabelCount == batch.TotalLabelCount ? WarehouseInboundLabelBatchStatus.Printed
                : batch.PrintedLabelCount > 0 ? WarehouseInboundLabelBatchStatus.PartiallyPrinted
                : WarehouseInboundLabelBatchStatus.Generated;
            if (batch.Status == WarehouseInboundLabelBatchStatus.Consumed) batch.CompletedAtUtc ??= DateTimeOffset.UtcNow;
            batch.UpdatedBy = actor;
            batch.UpdatedDate = DateTime.UtcNow;
        }
        await uow.SaveChangesAsync(ct);
    }

    private async Task<WarehouseInboundLabelBatchDetail> MapDetail(WarehouseInboundLabelBatch batch, CancellationToken ct, WarehouseInboundTask? task = null)
    {
        if (batch.Labels.Count == 0) batch = await Batches.Query().Include(x => x.Labels).FirstAsync(x => x.Id == batch.Id, ct);
        var header = await uow.Repository<WarehouseInboundHeader>().FindByIdAsync(batch.GrHeaderId, false, ct)
            ?? throw AppException.NotFound("Mal kabul bulunamadı.");
        var taskLineId = batch.Labels.Select(x => x.GrTaskLineId).FirstOrDefault(x => x.HasValue);
        if (task is null && taskLineId.HasValue)
        {
            task = await (from line in uow.Repository<WarehouseInboundTaskLine>().Query()
                          join taskRow in uow.Repository<WarehouseInboundTask>().Query() on line.GrTaskId equals taskRow.Id
                          where line.Id == taskLineId.Value select taskRow).FirstOrDefaultAsync(ct);
        }
        var row = new WarehouseInboundLabelBatchRow(batch.Id, header.Id, header.DocumentNo, task?.Id, task?.TaskNo,
            batch.BatchNo, batch.Status, batch.TotalLabelCount, batch.PrintedLabelCount, batch.ConsumedLabelCount,
            batch.VoidLabelCount, batch.LastPrintedAtUtc, batch.CreatedBy, batch.CreatedDate, batch.RowVersion);
        return new(row, batch.Labels.OrderBy(x => x.Id).Select(Map).ToList());
    }

    private static WarehouseInboundLabelRow Map(WarehouseInboundLabel x) => new(x.Id, x.BatchId, x.GrHeaderId, x.GrLineId,
        x.GrTaskLineId, x.StockId, x.StockCodeSnapshot, x.StockNameSnapshot, x.YapCodeSnapshot, x.LabelQuantity,
        x.UnitCode, x.LotNo, x.SerialNo, x.ManufacturingDate, x.ExpirationDate, x.BarcodeValue, x.Status,
        x.PrintCount, x.LastPrintedAtUtc, x.ConsumedAtUtc, x.VoidReason, x.RowVersion);

    private static IReadOnlyList<LabelSeed> BuildSeeds(WarehouseInboundTaskLine line, GenerateWarehouseInboundLabelLineRequest input, decimal remaining)
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
    private static void ApplyVersion(WarehouseInboundLabel entity, string supplied)
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
}
