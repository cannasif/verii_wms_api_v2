using Microsoft.EntityFrameworkCore;
using verii_wms_api_v2.Modules.GoodsReceipt.Domain;
using verii_wms_api_v2.Modules.Quality.Domain;
using verii_wms_api_v2.Shared;
using verii_wms_api_v2.Shared.Application.Abstractions.Persistence;
using verii_wms_api_v2.Shared.Application.Exceptions;
using WarehouseEntity = verii_wms_api_v2.Modules.Warehouse.Domain.Warehouse;

namespace verii_wms_api_v2.Modules.Quality.Application;

public sealed class QualityReportService(IUnitOfWork uow) : IQualityReportService
{
    private static readonly IReadOnlyDictionary<string, string> InspectionColumns =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["id"] = nameof(QualityInspectionReportRow.Id),
            ["inspectionNo"] = nameof(QualityInspectionReportRow.InspectionNo),
            ["sourceDocumentNo"] = nameof(QualityInspectionReportRow.SourceDocumentNo),
            ["waybillNo"] = nameof(QualityInspectionReportRow.WaybillNo),
            ["supplierCode"] = nameof(QualityInspectionReportRow.SupplierCode),
            ["supplierName"] = nameof(QualityInspectionReportRow.SupplierName),
            ["warehouseCode"] = nameof(QualityInspectionReportRow.WarehouseCode),
            ["warehouseName"] = nameof(QualityInspectionReportRow.WarehouseName),
            ["status"] = nameof(QualityInspectionReportRow.Status),
            ["lineCount"] = nameof(QualityInspectionReportRow.LineCount),
            ["totalQuantity"] = nameof(QualityInspectionReportRow.TotalQuantity),
            ["requiredInspectionQuantity"] = nameof(QualityInspectionReportRow.RequiredInspectionQuantity),
            ["inspectedQuantity"] = nameof(QualityInspectionReportRow.InspectedQuantity),
            ["acceptedQuantity"] = nameof(QualityInspectionReportRow.AcceptedQuantity),
            ["rejectedQuantity"] = nameof(QualityInspectionReportRow.RejectedQuantity),
            ["quarantineQuantity"] = nameof(QualityInspectionReportRow.QuarantineQuantity),
            ["controlCount"] = nameof(QualityInspectionReportRow.ControlCount),
            ["imageCount"] = nameof(QualityInspectionReportRow.ImageCount),
            ["createdAtUtc"] = nameof(QualityInspectionReportRow.CreatedAtUtc),
            ["decidedAtUtc"] = nameof(QualityInspectionReportRow.DecidedAtUtc)
        };

    private static readonly IReadOnlyDictionary<string, string> StockColumns =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["id"] = nameof(QualityStockReportRow.Id),
            ["stockId"] = nameof(QualityStockReportRow.StockId),
            ["stockCode"] = nameof(QualityStockReportRow.StockCode),
            ["stockName"] = nameof(QualityStockReportRow.StockName),
            ["inspectionCount"] = nameof(QualityStockReportRow.InspectionCount),
            ["receiptCount"] = nameof(QualityStockReportRow.ReceiptCount),
            ["totalQuantity"] = nameof(QualityStockReportRow.TotalQuantity),
            ["requiredInspectionQuantity"] = nameof(QualityStockReportRow.RequiredInspectionQuantity),
            ["inspectedQuantity"] = nameof(QualityStockReportRow.InspectedQuantity),
            ["acceptedQuantity"] = nameof(QualityStockReportRow.AcceptedQuantity),
            ["rejectedQuantity"] = nameof(QualityStockReportRow.RejectedQuantity),
            ["quarantineQuantity"] = nameof(QualityStockReportRow.QuarantineQuantity),
            ["firstInspectionAtUtc"] = nameof(QualityStockReportRow.FirstInspectionAtUtc),
            ["lastInspectionAtUtc"] = nameof(QualityStockReportRow.LastInspectionAtUtc)
        };

    public async Task<PagedResponse<QualityInspectionReportRow>> GetInspectionsPagedAsync(
        PagedRequest request,
        CancellationToken ct = default)
    {
        var query =
            from inspection in uow.Repository<QualityInspection>().Query()
            join receipt in uow.Repository<GoodsReceiptHeader>().Query()
                on new { Type = inspection.SourceDocumentType, Id = inspection.SourceDocumentId }
                equals new { Type = "GoodsReceipt", Id = receipt.Id } into receipts
            from receipt in receipts.DefaultIfEmpty()
            join warehouse in uow.Repository<WarehouseEntity>().Query()
                on inspection.WarehouseId equals warehouse.Id into warehouses
            from warehouse in warehouses.DefaultIfEmpty()
            where inspection.QueuedAtUtc != null
            select new QualityInspectionReportRow
            {
                Id = inspection.Id,
                InspectionNo = inspection.InspectionNo,
                SourceDocumentNo = inspection.SourceDocumentNo,
                WaybillNo = receipt == null ? null : receipt.ElectronicWaybillNo ?? receipt.WaybillNo,
                SupplierCode = receipt == null ? null : receipt.SupplierCodeSnapshot,
                SupplierName = receipt == null ? null : receipt.SupplierNameSnapshot,
                WarehouseCode = warehouse == null ? null : warehouse.WarehouseCode,
                WarehouseName = warehouse == null ? null : warehouse.WarehouseName,
                Status = inspection.Status,
                LineCount = inspection.Lines.Count,
                TotalQuantity = inspection.Lines.Sum(line => line.Quantity),
                RequiredInspectionQuantity = inspection.Lines.Sum(line => line.SampleQuantity),
                InspectedQuantity = inspection.Lines.Sum(line => line.InspectedQuantity),
                AcceptedQuantity = inspection.Lines.Sum(line => line.AcceptedQuantity),
                RejectedQuantity = inspection.Lines.Sum(line => line.RejectedQuantity),
                QuarantineQuantity = inspection.Lines.Sum(line => line.QuarantineQuantity),
                ControlCount = inspection.Controls.Count,
                ImageCount = inspection.Images.Count,
                CreatedAtUtc = inspection.CreatedAtUtc,
                DecidedAtUtc = inspection.DecidedAtUtc
            };

        query = query.ApplySearch(
            request,
            InspectionColumns,
            ["inspectionNo", "sourceDocumentNo", "waybillNo", "supplierCode", "supplierName"],
            AdvancedQueryExtensions.TurkishCaseInsensitiveSearchCollation);
        query = query.ApplyAdvancedFilters(request, InspectionColumns);

        var page = string.IsNullOrWhiteSpace(request.SortBy)
            ? await query.OrderByDescending(row => row.CreatedAtUtc)
                .ThenByDescending(row => row.Id)
                .ToPagedResponseAsync(request, ct)
            : await query.ApplySort(request, nameof(QualityInspectionReportRow.CreatedAtUtc), InspectionColumns)
                .ToPagedResponseAsync(request, ct);

        await EnrichInspectionWorkMetricsAsync(page.Items, ct);
        return page;
    }

    public async Task<QualityInspectionReportDetailDto> GetInspectionDetailAsync(
        long inspectionId,
        CancellationToken ct = default)
    {
        var inspection = await uow.Repository<QualityInspection>().Query()
            .AsSplitQuery()
            .Include(entity => entity.Lines)
                .ThenInclude(line => line.DecisionCode)
            .Include(entity => entity.Controls)
            .Include(entity => entity.WorkSessions)
            .SingleOrDefaultAsync(entity => entity.Id == inspectionId, ct)
            ?? throw AppException.NotFound("Kalite raporu bulunamadı.");

        var receipt = inspection.SourceDocumentType == "GoodsReceipt"
            ? await uow.Repository<GoodsReceiptHeader>().Query()
                .Where(entity => entity.Id == inspection.SourceDocumentId)
                .Select(entity => new
                {
                    WaybillNo = entity.ElectronicWaybillNo ?? entity.WaybillNo,
                    SupplierCode = entity.SupplierCodeSnapshot,
                    SupplierName = entity.SupplierNameSnapshot
                })
                .SingleOrDefaultAsync(ct)
            : null;
        var warehouse = await uow.Repository<WarehouseEntity>().Query()
            .Where(entity => entity.Id == inspection.WarehouseId)
            .Select(entity => new { entity.WarehouseCode, entity.WarehouseName })
            .SingleOrDefaultAsync(ct);
        var imageCounts = await uow.Repository<QualityInspectionImage>().Query()
            .Where(image => image.QualityInspectionId == inspectionId)
            .GroupBy(image => image.QualityInspectionLineId)
            .Select(group => new { LineId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(item => item.LineId, item => item.Count, ct);

        var header = new QualityInspectionReportRow
        {
            Id = inspection.Id,
            InspectionNo = inspection.InspectionNo,
            SourceDocumentNo = inspection.SourceDocumentNo,
            WaybillNo = receipt?.WaybillNo,
            SupplierCode = receipt?.SupplierCode,
            SupplierName = receipt?.SupplierName,
            WarehouseCode = warehouse?.WarehouseCode,
            WarehouseName = warehouse?.WarehouseName,
            Status = inspection.Status,
            LineCount = inspection.Lines.Count,
            TotalQuantity = inspection.Lines.Sum(line => line.Quantity),
            RequiredInspectionQuantity = inspection.Lines.Sum(line => line.SampleQuantity),
            InspectedQuantity = inspection.Lines.Sum(line => line.InspectedQuantity),
            AcceptedQuantity = inspection.Lines.Sum(line => line.AcceptedQuantity),
            RejectedQuantity = inspection.Lines.Sum(line => line.RejectedQuantity),
            QuarantineQuantity = inspection.Lines.Sum(line => line.QuarantineQuantity),
            ControlCount = inspection.Controls.Count,
            ImageCount = imageCounts.Values.Sum(),
            CreatedAtUtc = inspection.CreatedAtUtc,
            DecidedAtUtc = inspection.DecidedAtUtc
        };
        ApplyWorkMetrics(header, inspection.WorkSessions, DateTimeOffset.UtcNow);

        var controlsByLine = inspection.Controls
            .GroupBy(control => control.QualityInspectionLineId)
            .ToDictionary(group => group.Key, group => group.Count());
        var lines = inspection.Lines
            .OrderBy(line => line.Id)
            .Select(line => new QualityInspectionReportLineDto(
                line.Id,
                line.StockId,
                line.StockCodeSnapshot,
                line.StockNameSnapshot,
                line.YapCodeSnapshot,
                line.LotNo,
                line.SerialNo,
                line.Quantity,
                line.SampleQuantity,
                line.InspectedQuantity,
                line.AcceptedQuantity,
                line.RejectedQuantity,
                line.QuarantineQuantity,
                line.Decision,
                controlsByLine.GetValueOrDefault(line.Id),
                imageCounts.GetValueOrDefault(line.Id),
                line.DecisionCode == null ? line.ReasonCode : $"{line.DecisionCode.Code} · {line.DecisionCode.Name}",
                line.ReasonNote,
                line.DecisionAtUtc))
            .ToArray();
        var workers = BuildWorkerMetrics(inspection.WorkSessions);
        var pauses = BuildPauseMetrics(inspection.WorkSessions, inspection.DecidedAtUtc);
        return new QualityInspectionReportDetailDto(header, lines, workers, pauses);
    }

    public async Task<PagedResponse<QualityStockReportRow>> GetStocksPagedAsync(
        PagedRequest request,
        CancellationToken ct = default)
    {
        var query = uow.Repository<QualityInspectionLine>().Query()
            .Where(line => line.Inspection.QueuedAtUtc != null)
            .GroupBy(line => new { line.StockId, line.StockCodeSnapshot, line.StockNameSnapshot })
            .Select(group => new QualityStockReportRow
            {
                Id = group.Key.StockId,
                StockId = group.Key.StockId,
                StockCode = group.Key.StockCodeSnapshot,
                StockName = group.Key.StockNameSnapshot,
                InspectionCount = group.Select(line => line.QualityInspectionId).Distinct().Count(),
                ReceiptCount = group.Where(line => line.Inspection.SourceDocumentType == "GoodsReceipt")
                    .Select(line => line.Inspection.SourceDocumentId).Distinct().Count(),
                TotalQuantity = group.Sum(line => line.Quantity),
                RequiredInspectionQuantity = group.Sum(line => line.SampleQuantity),
                InspectedQuantity = group.Sum(line => line.InspectedQuantity),
                AcceptedQuantity = group.Sum(line => line.AcceptedQuantity),
                RejectedQuantity = group.Sum(line => line.RejectedQuantity),
                QuarantineQuantity = group.Sum(line => line.QuarantineQuantity),
                FirstInspectionAtUtc = group.Min(line => line.Inspection.CreatedAtUtc),
                LastInspectionAtUtc = group.Max(line => line.Inspection.CreatedAtUtc)
            });

        query = query.ApplySearch(
            request,
            StockColumns,
            ["stockCode", "stockName"],
            AdvancedQueryExtensions.TurkishCaseInsensitiveSearchCollation);
        query = query.ApplyAdvancedFilters(request, StockColumns);
        var page = string.IsNullOrWhiteSpace(request.SortBy)
            ? await query.OrderByDescending(row => row.LastInspectionAtUtc)
                .ThenBy(row => row.StockCode)
                .ToPagedResponseAsync(request, ct)
            : await query.ApplySort(request, nameof(QualityStockReportRow.LastInspectionAtUtc), StockColumns)
                .ToPagedResponseAsync(request, ct);

        await EnrichStockWorkMetricsAsync(page.Items, ct);
        return page;
    }

    private async Task EnrichInspectionWorkMetricsAsync(
        IReadOnlyList<QualityInspectionReportRow> rows,
        CancellationToken ct)
    {
        var ids = rows.Select(row => row.Id).ToArray();
        if (ids.Length == 0) return;
        var sessions = await uow.Repository<QualityInspectionWorkSession>().Query()
            .Where(session => ids.Contains(session.QualityInspectionId))
            .OrderBy(session => session.QualityInspectionId)
            .ThenBy(session => session.SequenceNo)
            .ToListAsync(ct);
        var byInspection = sessions.GroupBy(session => session.QualityInspectionId)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<QualityInspectionWorkSession>)group.ToArray());
        var now = DateTimeOffset.UtcNow;
        foreach (var row in rows)
            ApplyWorkMetrics(row, byInspection.GetValueOrDefault(row.Id) ?? [], now);
    }

    private async Task EnrichStockWorkMetricsAsync(
        IReadOnlyList<QualityStockReportRow> rows,
        CancellationToken ct)
    {
        var stockIds = rows.Select(row => row.StockId).ToArray();
        if (stockIds.Length == 0) return;
        var links = await uow.Repository<QualityInspectionLine>().Query()
            .Where(line => stockIds.Contains(line.StockId))
            .Select(line => new { line.StockId, line.QualityInspectionId })
            .Distinct()
            .ToListAsync(ct);
        var inspectionIds = links.Select(link => link.QualityInspectionId).Distinct().ToArray();
        var sessions = await uow.Repository<QualityInspectionWorkSession>().Query()
            .Where(session => inspectionIds.Contains(session.QualityInspectionId))
            .ToListAsync(ct);
        var now = DateTimeOffset.UtcNow;
        var secondsByInspection = sessions.GroupBy(session => session.QualityInspectionId)
            .ToDictionary(
                group => group.Key,
                group => group.Sum(session => EffectiveWorkSeconds(session, now)));
        var participantsByInspection = sessions.GroupBy(session => session.QualityInspectionId)
            .ToDictionary(
                group => group.Key,
                group => group.Select(session => session.WorkerUserId).Distinct().ToArray());

        foreach (var row in rows)
        {
            var related = links.Where(link => link.StockId == row.StockId)
                .Select(link => link.QualityInspectionId)
                .Distinct()
                .ToArray();
            row.ActiveWorkSeconds = related.Sum(id => secondsByInspection.GetValueOrDefault(id));
            row.AverageWorkSeconds = related.Length == 0 ? 0 : row.ActiveWorkSeconds / related.Length;
            row.ParticipantCount = related
                .SelectMany(id => participantsByInspection.GetValueOrDefault(id) ?? [])
                .Distinct()
                .Count();
        }
    }

    internal static void ApplyWorkMetrics(
        QualityInspectionReportRow row,
        IEnumerable<QualityInspectionWorkSession> sessions,
        DateTimeOffset now)
    {
        var ordered = sessions.OrderBy(session => session.SequenceNo).ToArray();
        if (ordered.Length == 0) return;
        row.StartedAtUtc = ordered[0].StartedAtUtc;
        row.ActiveWorkSeconds = ordered.Sum(session => EffectiveWorkSeconds(session, now));
        var end = row.DecidedAtUtc ?? now;
        row.ElapsedSeconds = Math.Max(0, (long)Math.Floor((end - ordered[0].StartedAtUtc).TotalSeconds));
        row.PauseSeconds = Math.Max(0, row.ElapsedSeconds - row.ActiveWorkSeconds);
        row.PauseCount = ordered.Count(IsOperationalPause);
        row.BreakCount = ordered.Count(session => session.StopReason == QualityInspectionWorkStopReason.Break);
        row.ParticipantCount = ordered.Select(session => session.WorkerUserId).Distinct().Count();
        row.Participants = string.Join(" · ", ordered.Select(session => session.WorkerNameSnapshot)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase));
    }

    internal static IReadOnlyList<QualityReportWorkerDto> BuildWorkerMetrics(
        IEnumerable<QualityInspectionWorkSession> sessions)
    {
        var now = DateTimeOffset.UtcNow;
        return sessions.GroupBy(session => new { session.WorkerUserId, session.WorkerNameSnapshot })
            .Select(group => new QualityReportWorkerDto(
                group.Key.WorkerUserId,
                group.Key.WorkerNameSnapshot,
                group.Sum(session => EffectiveWorkSeconds(session, now)),
                group.Count(),
                group.Min(session => session.StartedAtUtc),
                group.Max(session => session.EndedAtUtc)))
            .OrderByDescending(worker => worker.ActiveWorkSeconds)
            .ToArray();
    }

    internal static IReadOnlyList<QualityReportPauseDto> BuildPauseMetrics(
        IEnumerable<QualityInspectionWorkSession> sessions,
        DateTimeOffset? inspectionEndedAtUtc)
    {
        var ordered = sessions.OrderBy(session => session.SequenceNo).ToArray();
        var pauses = new List<QualityReportPauseDto>();
        for (var index = 0; index < ordered.Length; index++)
        {
            var session = ordered[index];
            if (!session.EndedAtUtc.HasValue || !IsOperationalPause(session)) continue;
            var nextStarted = index + 1 < ordered.Length ? ordered[index + 1].StartedAtUtc : inspectionEndedAtUtc;
            long? pauseSeconds = nextStarted.HasValue
                ? Math.Max(0, (long)Math.Floor((nextStarted.Value - session.EndedAtUtc.Value).TotalSeconds))
                : null;
            pauses.Add(new QualityReportPauseDto(
                session.SequenceNo,
                session.WorkerUserId,
                session.WorkerNameSnapshot,
                session.StopReason!.Value,
                session.StopNote,
                session.EndedAtUtc.Value,
                nextStarted ?? session.EndedAtUtc.Value,
                session.DurationSeconds,
                pauseSeconds));
        }
        return pauses;
    }

    private static bool IsOperationalPause(QualityInspectionWorkSession session) =>
        session.StopReason.HasValue
        && session.StopReason is not QualityInspectionWorkStopReason.DecisionApplied
        && session.StopReason is not QualityInspectionWorkStopReason.InspectionCancelled;

    private static long EffectiveWorkSeconds(QualityInspectionWorkSession session, DateTimeOffset now) =>
        session.EndedAtUtc.HasValue
            ? session.DurationSeconds
            : Math.Max(0, (long)Math.Floor((now - session.StartedAtUtc).TotalSeconds));
}
