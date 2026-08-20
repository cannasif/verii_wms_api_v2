using Microsoft.EntityFrameworkCore;
using verii_wms_api_v2.Modules.DocumentSeries.Application;
using verii_wms_api_v2.Modules.DocumentSeries.Domain;
using verii_wms_api_v2.Modules.ErpIntegration.Application;
using verii_wms_api_v2.Modules.ErpIntegration.Domain;
using verii_wms_api_v2.Modules.Identity.Domain;
using verii_wms_api_v2.Modules.Kkd.Domain;
using verii_wms_api_v2.Modules.WarehouseOperations.Domain;
using verii_wms_api_v2.Modules.WarehouseOutbound.Application;
using verii_wms_api_v2.Modules.WarehouseOutbound.Domain;
using verii_wms_api_v2.Shared.Application.Abstractions.Persistence;
using verii_wms_api_v2.Shared.Application.Exceptions;
using StockEntity = verii_wms_api_v2.Modules.Stock.Domain.Stock;
using WarehouseEntity = verii_wms_api_v2.Modules.Warehouse.Domain.Warehouse;

namespace verii_wms_api_v2.Modules.Kkd.Application;

/// <summary>
/// Fiziksel teslim onayını tek sunucu işlemi olarak yürütür. Toplama sırasında stok zaten kaynak raftan
/// KKD bekleme rafına taşındığı için ambar çıkışı stoğu yeniden toplamaz: belge bekleme rafı üzerinden
/// açılır, "Topla" adımı bakiyeyi değiştirmeyen bir durum geçişine indirgenir ve sevk bekleme rafından
/// yapılır. Tarayıcıdan sırayla çağrılan pick/pack/load/ship zinciri yerine tek çağrı olması, yarım kalan
/// teslimlerin talep satırında ayrılmış (AllocatedQuantity) miktarı kilitli bırakmasını engeller.
/// </summary>
public sealed class KkdPhysicalDeliveryService(
    IUnitOfWork uow,
    IKkdDistributionService distributions,
    IWarehouseOutboundOperationService outboundOperations,
    IDocumentSeriesService documentSeries,
    IErpPostingService erp,
    IKkdPreparationTaskService preparationTasks,
    ILogger<KkdPhysicalDeliveryService> logger) : IKkdPhysicalDeliveryService
{
    private IGenericRepository<KkdPreparationTask> Tasks => uow.Repository<KkdPreparationTask>();
    private IGenericRepository<KkdPreparationBarcodeScan> Scans => uow.Repository<KkdPreparationBarcodeScan>();
    private IGenericRepository<WarehouseEntity> Warehouses => uow.Repository<WarehouseEntity>();
    private IGenericRepository<WarehouseOutboundHeader> Outbounds => uow.Repository<WarehouseOutboundHeader>();

    public async Task<KkdPhysicalDeliveryResult> DeliverAsync(
        long taskId,
        KkdPhysicalDeliveryRequest request,
        long actor,
        CancellationToken ct = default)
    {
        if (taskId <= 0 || request.IdempotencyKey == Guid.Empty)
            throw AppException.BadRequest("Hazırlama görevi ve idempotency anahtarı zorunludur.");

        var existing = await uow.Repository<KkdDistribution>().Query()
            .SingleOrDefaultAsync(x => x.CorrelationId == request.IdempotencyKey, ct);
        if (existing is not null)
            return await BuildResultAsync(existing.Id, actor, replayed: true, ct);

        var task = await LoadTaskAsync(taskId, actor, ct);
        var stagingLocationId = await Warehouses.Query()
            .Where(x => x.Id == task.WarehouseId)
            .Select(x => x.KkdPickingStagingLocationId)
            .SingleOrDefaultAsync(ct)
            ?? throw AppException.Conflict(
                "Bu depo için KKD toplama sanal rafı tanımlanmamış; teslim yapılamaz. Depo ayarlarından bekleme rafını seçin.");

        var lines = await BuildLinesAsync(task, stagingLocationId, ct);
        if (lines.Count == 0)
            throw AppException.Conflict("Bu görevde teslim edilecek toplanmış kalem yok. Önce barkod okutarak toplama yapın.");

        var seriesId = request.DocumentSeriesId ?? await DefaultSeriesIdAsync(task.Request.BranchCode, ct);

        var created = await distributions.CreateAsync(new KkdDistributionCreateRequest(
            request.IdempotencyKey,
            task.Request.EmployeeId,
            task.WarehouseId,
            seriesId,
            DateOnly.FromDateTime(DateTime.UtcNow),
            stagingLocationId,
            LoadingLocationId: null,
            request.Description,
            lines.Select(x => x.Request).ToArray(),
            CreateWarehouseTask: false,
            AssignedUserIds: null,
            KkdRequestId: task.RequestId,
            StockAlreadyStaged: true), actor, ct);

        if (string.Equals(created.ExcessApprovalStatus, KkdExcessApprovalStatus.Pending.ToString(), StringComparison.Ordinal))
            return await BuildResultAsync(created.Id, actor, replayed: created.Replayed, ct);

        try
        {
            await ShipAsync(created.WarehouseOutboundId, request.IdempotencyKey, lines, stagingLocationId, actor, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await CompensateAsync(created, ex, actor, ct);
            throw;
        }

        if (request.CloseTaskAfterDelivery)
            await preparationTasks.CloseAfterDeliveryAsync(taskId, request.IdempotencyKey, actor, ct);

        return await BuildResultAsync(created.Id, actor, replayed: false, ct);
    }

    /// <summary>
    /// Sevk zinciri: serbest bırak → topla → sevk et. Paketleme ve yükleme adımları, stok zaten bekleme
    /// rafında olduğu için belge oluşturulurken kapatılmıştır (bkz. StockAlreadyStaged).
    /// </summary>
    private async Task ShipAsync(
        long outboundId,
        Guid idempotencyKey,
        IReadOnlyList<PreparedDeliveryLine> lines,
        long stagingLocationId,
        long actor,
        CancellationToken ct)
    {
        var status = await Outbounds.Query().Where(x => x.Id == outboundId).Select(x => x.Status).SingleAsync(ct);
        if (status == WarehouseOutboundStatus.Draft)
            await ReleaseAsync(outboundId, idempotencyKey, actor, ct);

        var outboundLineIds = await Outbounds.Query()
            .Where(x => x.Id == outboundId)
            .SelectMany(x => x.Lines)
            .OrderBy(x => x.LineNo)
            .Select(x => x.Id)
            .ToArrayAsync(ct);
        if (outboundLineIds.Length != lines.Count)
            throw AppException.Conflict("Ambar çıkış kalemleri teslim kalemleriyle eşleşmiyor.");

        var batches = BuildOperationBatches(lines, outboundLineIds, stagingLocationId);
        for (var index = 0; index < batches.Count; index++)
        {
            await outboundOperations.PickAsync(
                outboundId, Operation(idempotencyKey, "pick", index, batches[index]), actor, ct);
        }
        for (var index = 0; index < batches.Count; index++)
        {
            await outboundOperations.ShipAsync(
                outboundId, Operation(idempotencyKey, "ship", index, batches[index]), actor, ct);
        }
    }

    private async Task ReleaseAsync(long outboundId, Guid idempotencyKey, long actor, CancellationToken ct)
    {
        var release = new WarehouseOutboundTransitionRequest(
            Derive(idempotencyKey, "release"), "KKD fiziksel teslim onayı");
        var requiresApproval = await Outbounds.Query()
            .Where(x => x.Id == outboundId)
            .Select(x => x.RequireApproval && x.ApprovalStatus == OperationApprovalStatus.Pending)
            .SingleAsync(ct);
        if (requiresApproval)
            await outboundOperations.ApproveAsync(
                outboundId,
                new(Derive(idempotencyKey, "approve"), "KKD fiziksel teslim onayı"),
                actor,
                ct);
        await outboundOperations.ReleaseAsync(outboundId, release, actor, ct);
    }

    /// <summary>
    /// WMS tarafı sevki tamamlanamadıysa ayrılan miktarın talep satırında kilitli kalmaması için dağıtım
    /// geri alınır. Sevk commit olduktan sonraki hatalar (ERP kilidi, tamamlama) geri alınmaz; belge
    /// gerçekten çıkmıştır ve ERP gönderimi ayrıca tekrar denenir.
    /// </summary>
    private async Task CompensateAsync(KkdDistributionCreateResult created, Exception failure, long actor, CancellationToken ct)
    {
        var shipped = await Outbounds.Query()
            .AnyAsync(x => x.Id == created.WarehouseOutboundId && x.Status == WarehouseOutboundStatus.Shipped, ct);
        if (shipped) return;
        try
        {
            await distributions.CancelAsync(
                created.Id,
                Derive(created.DocumentNo, "compensate"),
                "Fiziksel teslim tamamlanamadı; ayrılan miktar serbest bırakıldı.",
                null,
                actor,
                ct);
        }
        catch (Exception cancelFailure)
        {
            logger.LogError(
                cancelFailure,
                "KKD fiziksel teslimi geri alınamadı DistributionId={DistributionId} Hata={Failure}",
                created.Id,
                failure.Message);
        }
    }

    private async Task<KkdPreparationTask> LoadTaskAsync(long taskId, long actor, CancellationToken ct)
    {
            var task = await Tasks.Query()
                .Include(x => x.Request).ThenInclude(x => x.Lines)
                .Include(x => x.Lines)
            .SingleOrDefaultAsync(x => x.Id == taskId, ct)
            ?? throw AppException.NotFound("Hazırlama görevi bulunamadı.");
        if (task.Status is not (KkdPreparationTaskStatus.Assigned or KkdPreparationTaskStatus.InPreparation))
            throw AppException.Conflict("Görev teslim için aktif değil.");

        var warehouseIds = await uow.Repository<UserWarehouseAssignment>().Query()
            .Where(x => x.UserId == actor).Select(x => x.WarehouseId).ToArrayAsync(ct);
        if (warehouseIds.Length > 0 && !warehouseIds.Contains(task.WarehouseId))
            throw AppException.Forbidden("Bu depodaki göreve erişim yetkiniz yok.");
        return task;
    }

    /// <summary>Teslim edilecek miktar okutma journal'ının kendisidir; kullanıcıya miktar sorulmaz.</summary>
    private async Task<IReadOnlyList<PreparedDeliveryLine>> BuildLinesAsync(
        KkdPreparationTask task,
        long stagingLocationId,
        CancellationToken ct)
    {
        var scans = await Scans.Query()
            .Where(x => x.TaskId == task.Id && x.DistributionId == null && !x.IsReversed)
            .OrderBy(x => x.ScannedAtUtc)
            .ToListAsync(ct);
        if (scans.Count == 0) return [];

        var stockIds = scans.Select(x => x.StockId).Distinct().ToArray();
        var trackingTypes = await uow.Repository<StockEntity>().Query()
            .Where(x => stockIds.Contains(x.Id))
            .Select(x => new { x.Id, x.ErpStockCode })
            .ToDictionaryAsync(x => x.Id, x => x.ErpStockCode, ct);
        if (trackingTypes.Count != stockIds.Length)
            throw AppException.Conflict("Okutulan stoklardan biri bulunamadı.");

        var taskLines = task.Lines.Where(x => !x.IsDeleted).ToDictionary(x => x.Id);
        // Tezgâh kanalında talep açık siparişten üretilir; teslimin siparişi kapatabilmesi için referans
        // kalemden alınıp dağıtıma taşınır. Talepler kanalında bu alanlar boştur ve teslim siparişsizdir.
        var orderRefs = task.Request.Lines
            .Where(x => !string.IsNullOrWhiteSpace(x.ExternalOrderNo) && long.TryParse(x.ExternalOrderLineId, out _))
            .ToDictionary(x => x.Id, x => ((string? Number, long LineId))(x.ExternalOrderNo, long.Parse(x.ExternalOrderLineId!)));

        var results = new List<PreparedDeliveryLine>();
        foreach (var group in scans.GroupBy(x => x.RequestLineId))
        {
            var first = group.First();
            if (group.Select(x => x.StockId).Distinct().Skip(1).Any())
                throw AppException.Conflict("Aynı talep kaleminde farklı stoklar okutulmuş; teslim yapılamaz.");
            if (!taskLines.Values.Any(x => x.RequestLineId == group.Key))
                throw AppException.Conflict("Okutulan kalem bu görevde bulunamadı.");
            var order = orderRefs.TryGetValue(group.Key, out var reference) ? reference : default;

            var trackings = group
                .Select(x => new KkdDistributionTrackingRequest(
                    x.Quantity, x.LotNo, x.SerialNo, null, null, null, stagingLocationId))
                .ToArray();
            results.Add(new PreparedDeliveryLine(
                new KkdDistributionLineCreateRequest(
                    first.StockId,
                    YapCodeId: null,
                    group.Sum(x => x.Quantity),
                    first.UnitCode,
                    stagingLocationId,
                    order.Number,
                    order.Number is null ? null : order.LineId,
                    RequireHandlingUnit: false,
                    Description: null,
                    trackings,
                    group.Key),
                trackings));
        }
        return results;
    }

    /// <summary>
    /// Serili stokta operasyon satırı başına tek seri ve miktar 1 zorunludur; ayrıca bir istekte aynı belge
    /// kalemi iki kez yer alamaz. Bu yüzden takip kayıtları, her partide her kalemden en fazla bir satır
    /// olacak şekilde dilimlenir.
    /// </summary>
    internal static List<List<WarehouseOutboundOperationLineRequest>> BuildOperationBatches(
        IReadOnlyList<PreparedDeliveryLine> lines,
        IReadOnlyList<long> outboundLineIds,
        long stagingLocationId)
    {
        var depth = lines.Max(x => Math.Max(1, x.Trackings.Length));
        var batches = new List<List<WarehouseOutboundOperationLineRequest>>(depth);
        for (var slot = 0; slot < depth; slot++)
        {
            var batch = new List<WarehouseOutboundOperationLineRequest>();
            for (var index = 0; index < lines.Count; index++)
            {
                var trackings = lines[index].Trackings;
                if (trackings.Length == 0)
                {
                    if (slot > 0) continue;
                    batch.Add(new(outboundLineIds[index], lines[index].Request.Quantity, stagingLocationId, null, null, null, null));
                    continue;
                }
                if (slot >= trackings.Length) continue;
                var tracking = trackings[slot];
                batch.Add(new(
                    outboundLineIds[index], tracking.Quantity, stagingLocationId, null,
                    tracking.LotNo, tracking.SerialNo, null));
            }
            if (batch.Count > 0) batches.Add(batch);
        }
        return batches;
    }

    private async Task<long> DefaultSeriesIdAsync(string branchCode, CancellationToken ct)
    {
        var rows = await documentSeries.GetLookupAsync(WmsDocumentType.WarehouseIssue, branchCode, ct);
        var series = rows.FirstOrDefault(x => x.IsDefault) ?? rows.FirstOrDefault();
        return series?.Id
            ?? throw AppException.Conflict("Ambar çıkışı için belge serisi tanımlanmamış.");
    }

    private async Task<KkdPhysicalDeliveryResult> BuildResultAsync(
        long distributionId,
        long actor,
        bool replayed,
        CancellationToken ct)
    {
        var distribution = await uow.Repository<KkdDistribution>().Query()
            .Include(x => x.Employee)
            .Include(x => x.Lines)
            .SingleAsync(x => x.Id == distributionId, ct);
        var outbound = await Outbounds.Query()
            .Where(x => x.Id == distribution.WarehouseOutboundId)
            .Select(x => new { x.Id, x.DocumentNo, x.Status })
            .SingleAsync(ct);
        var deliveredBy = await uow.Repository<User>().Query()
            .Where(x => x.Id == (distribution.CreatedBy ?? actor))
            .Select(x => x.Detail == null ? x.Username : x.Detail.FirstName + " " + x.Detail.LastName)
            .SingleOrDefaultAsync(ct) ?? string.Empty;
        var units = await uow.Repository<WarehouseOutboundLine>().Query()
            .Where(x => x.WarehouseOutboundHeaderId == outbound.Id)
            .Select(x => new { x.LineNo, x.UnitCode })
            .ToDictionaryAsync(x => x.LineNo, x => x.UnitCode, ct);

        var posting = await ErpStateAsync(outbound.Id, ct);
        return new KkdPhysicalDeliveryResult(
            distribution.Id,
            distribution.DocumentNo,
            distribution.Status.ToString(),
            outbound.Id,
            outbound.DocumentNo,
            outbound.Status.ToString(),
            distribution.ExcessApprovalStatus.ToString(),
            posting.Status,
            posting.DocumentNo,
            posting.ErrorMessage,
            distribution.DocumentNo,
            distribution.CompletedAtUtc,
            distribution.Employee.EmployeeCode,
            $"{distribution.Employee.FirstName} {distribution.Employee.LastName}".Trim(),
            deliveredBy,
            distribution.Lines.OrderBy(x => x.LineNo).Select(x => new KkdPhysicalDeliveryLine(
                x.StockCodeSnapshot,
                x.StockNameSnapshot ?? string.Empty,
                x.Quantity,
                units.GetValueOrDefault(x.LineNo) ?? string.Empty,
                x.LotNo,
                x.SerialNo)).ToArray(),
            replayed);
    }

    private async Task<(string Status, string? DocumentNo, string? ErrorMessage)> ErpStateAsync(long outboundId, CancellationToken ct)
    {
        try
        {
            var result = await erp.GetAsync(ErpPostingSourceType.WarehouseOutbound, outboundId, ct);
            return (result.Status.ToString(), result.ErpDocumentNo, result.ErrorMessage);
        }
        catch (AppException)
        {
            var status = await Outbounds.Query().Where(x => x.Id == outboundId)
                .Select(x => x.ErpIntegrationStatus).SingleAsync(ct);
            return (status.ToString(), null, null);
        }
    }

    private static WarehouseOutboundOperationRequest Operation(
        Guid idempotencyKey,
        string phase,
        int batchIndex,
        IReadOnlyList<WarehouseOutboundOperationLineRequest> lines) =>
        new(Derive(idempotencyKey, $"{phase}:{batchIndex}"), lines, null, "KKD fiziksel teslim onayı", null, null, null, null);

    /// <summary>Tek teslim anahtarından adım bazlı, tekrar oynatmada aynı sonucu veren anahtarlar türetir.</summary>
    private static Guid Derive(Guid idempotencyKey, string step) => Derive(idempotencyKey.ToString("N"), step);

    private static Guid Derive(string seed, string step)
    {
        var bytes = System.Security.Cryptography.MD5.HashData(
            System.Text.Encoding.UTF8.GetBytes($"kkd-delivery|{seed}|{step}"));
        return new Guid(bytes);
    }

    internal sealed record PreparedDeliveryLine(
        KkdDistributionLineCreateRequest Request,
        KkdDistributionTrackingRequest[] Trackings);
}
