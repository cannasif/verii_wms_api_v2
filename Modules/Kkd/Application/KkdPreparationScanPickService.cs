using System.Data;
using Microsoft.EntityFrameworkCore;
using verii_wms_api_v2.Modules.Audit.Application;
using verii_wms_api_v2.Modules.BarcodeDesigner.Application;
using verii_wms_api_v2.Modules.Identity.Domain;
using verii_wms_api_v2.Modules.Kkd.Domain;
using verii_wms_api_v2.Modules.StockBalance.Application;
using verii_wms_api_v2.Modules.StockBalance.Domain;
using verii_wms_api_v2.Modules.StockMovement.Application;
using verii_wms_api_v2.Modules.StockMovement.Domain;
using verii_wms_api_v2.Shared.Application.Abstractions.Persistence;
using verii_wms_api_v2.Shared.Application.Exceptions;
using StockEntity = verii_wms_api_v2.Modules.Stock.Domain.Stock;
using WarehouseEntity = verii_wms_api_v2.Modules.Warehouse.Domain.Warehouse;

namespace verii_wms_api_v2.Modules.Kkd.Application;

/// <summary>
/// KKD hazırlama görevi için scan-pick: paylaşılan barkod çözümleyici + journal + PreparedQuantity,
/// üzerine üretimdeki gibi canlı rezervasyon-tüketimi + gerçek stok hareketi (kaynak raf → KKD toplama
/// sanal rafı). "Teslimi Tamamla" artık stok düşürmüyor; zaten postalanmış hareketleri dağıtım/ambar
/// çıkışına bağlayıp kapatan bir finalize adımı (bkz. KkdPreparationPickingPage "Fiziksel Teslim Onayı").
/// Otomatik toplama eşiği üretimle aynı: depo AutoPickWithoutConfirmMaxQuantity.
/// </summary>
public sealed class KkdPreparationScanPickService(
    IUnitOfWork uow,
    IAuditLogWriter audit,
    IWarehouseBarcodeResolver barcodeResolver,
    IStockBalanceService balances,
    IStockMovementService movements,
    IKkdRequestService requests,
    IKkdPreparationTaskService preparationTasks) : IKkdPreparationScanPickService
{
    internal const string PickAboveThresholdConfirmMessage =
        "Bu miktar onay eşiğini aşıyor. Devam etmek için onaylayın.";

    private IGenericRepository<KkdPreparationTask> Tasks => uow.Repository<KkdPreparationTask>();
    private IGenericRepository<KkdPreparationBarcodeScan> Scans => uow.Repository<KkdPreparationBarcodeScan>();
    private IGenericRepository<KkdPreparationTaskLineLocation> TaskLineLocations => uow.Repository<KkdPreparationTaskLineLocation>();
    private IGenericRepository<UserWarehouseAssignment> WarehouseAssignments => uow.Repository<UserWarehouseAssignment>();
    private IGenericRepository<StockEntity> Stocks => uow.Repository<StockEntity>();
    private IGenericRepository<WarehouseEntity> Warehouses => uow.Repository<WarehouseEntity>();

    public async Task<KkdPreparationResolveScanResult> ResolveScanAsync(
        long taskId,
        KkdPreparationResolveScanRequest request,
        long actor,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Barcode))
            throw AppException.BadRequest("Barkod zorunludur.");

        var task = await LoadActiveTaskAsync(taskId, actor, tracking: false, ct);
        var resolved = await ResolveBarcodeAsync(task, request.Barcode, ct);

        var stockGroup = await Stocks.Query().Where(x => x.Id == resolved.StockId)
            .Select(x => x.GroupCode).SingleOrDefaultAsync(ct);
        var match = FindTargetLine(task, resolved, stockGroup, request.ExpectedTaskLineId);
        var remaining = Remaining(match.Line);
        if (remaining <= 0)
            throw AppException.Conflict("Bu kalemde kalan miktar yok.");

        var isSerial = IsSerial(resolved);
        // KKD PPE: birim seri → 1; serisiz → barkod miktarı veya 1 (üretim defaultQuantity mantığı).
        var defaultQuantity = isSerial
            ? Math.Min(1m, remaining)
            : Math.Min(remaining, resolved.Quantity is > 0 ? resolved.Quantity.Value : 1m);
        var threshold = await Warehouses.Query()
            .Where(x => x.Id == task.WarehouseId)
            .Select(x => x.AutoPickWithoutConfirmMaxQuantity)
            .SingleAsync(ct);
        // Üretim: raf/seri belirsizken otomatik toplama yok. Seri veya (eşik tanımlı ve default ≤ eşik).
        var canAutoPick = HasUniquePickSource(resolved)
            && (isSerial
                || (threshold is > 0 && defaultQuantity > 0 && defaultQuantity <= threshold.Value));

        return new(
            match.Line.Id,
            match.Line.RequestLineId,
            match.NeedsGroupResolve,
            match.Line.RequestLine.GroupCode,
            resolved.StockId,
            resolved.StockCode,
            resolved.StockName,
            resolved.UnitCode,
            resolved.LotNo,
            resolved.SerialNo,
            resolved.SuggestedLocationId,
            resolved.RequireSerial,
            resolved.RequireLot,
            remaining,
            defaultQuantity,
            isSerial,
            canAutoPick,
            threshold,
            resolved.RawBarcode,
            resolved.Source,
            resolved.BalanceCandidates);
    }

    public async Task<KkdPreparationScanPickResult> ScanPickAsync(
        long taskId,
        KkdPreparationScanPickRequest request,
        long actor,
        CancellationToken ct = default)
    {
        if (request.IdempotencyKey == Guid.Empty)
            throw AppException.BadRequest("IdempotencyKey zorunludur.");
        if (string.IsNullOrWhiteSpace(request.Barcode))
            throw AppException.BadRequest("Barkod zorunludur.");

        var replay = await Scans.Query()
            .SingleOrDefaultAsync(x => x.IdempotencyKey == request.IdempotencyKey, ct);
        if (replay is not null)
            return await BuildReplayResultAsync(taskId, replay, actor, ct);

        // Grup çözümü ayrı transaction (kendi idempotency'si); scan journal ile iç içe girilmez.
        var prep = await PrepareMatchAsync(taskId, request, actor, ct);
        if (prep.NeedsGroupResolve)
        {
            await requests.ResolveLineAsync(prep.RequestId, prep.RequestLineId, new(
                Guid.NewGuid(),
                prep.Resolved.StockId,
                "Toplama sırasında barkod ile çözümlendi.",
                request.ExpectedRequestLineRowVersion), actor, ct);
        }

        return await uow.ExecuteInTransactionAsync(async token =>
        {
            replay = await Scans.Query()
                .SingleOrDefaultAsync(x => x.IdempotencyKey == request.IdempotencyKey, token);
            if (replay is not null)
                return await BuildReplayResultAsync(taskId, replay, actor, token);

            var task = await LoadActiveTaskAsync(taskId, actor, tracking: true, token);
            if (task.AssignedUserId is not null && task.AssignedUserId != actor)
                throw AppException.Forbidden("Bu hazırlama görevi başka bir kullanıcıya atanmış.");

            var line = task.Lines.SingleOrDefault(x => x.Id == prep.TaskLineId)
                ?? throw AppException.Conflict("Görev kalemi bulunamadı.");
            var requestLine = line.RequestLine;
            if (!requestLine.StockId.HasValue || requestLine.StockId.Value != prep.Resolved.StockId)
                throw AppException.Conflict("Okutulan stok görev kalemiyle eşleşmiyor.");
            // ResolveLineAsync stoğu az önce bağlamış olabilir ve kota aşımı bulmuş olabilir (QuotaDecision=Pending);
            // bu durumda barkod okutarak sessizce toplamaya devam edilmemeli, müdür kararına kadar durmalı.
            if (requestLine.QuotaDecision is KkdRequestLineQuotaDecision.Pending or KkdRequestLineQuotaDecision.Rejected)
                throw AppException.Conflict("Bu kalem için kota kararı bekleniyor; müdür onaylayana/reddedene kadar toplama yapılamaz.");

            var remaining = Remaining(line);
            if (remaining <= 0)
                throw AppException.Conflict("Bu kalemde kalan miktar yok.");

            var resolved = prep.Resolved;
            var selected = SelectBalanceCandidate(resolved, request.SourceLocationId, request.SerialNo, request.LotNo)
                ?? throw AppException.Conflict("Kaynak raf belirlenemedi; birden fazla raf/seri varsa birini seçmelisiniz.");
            var sourceLocationId = selected.LocationId;
            var serialNo = NullIfWhiteSpace(request.SerialNo)
                ?? NullIfWhiteSpace(selected.SerialNo)
                ?? NullIfWhiteSpace(resolved.SerialNo);
            var lotNo = NullIfWhiteSpace(request.LotNo)
                ?? NullIfWhiteSpace(selected.LotNo)
                ?? NullIfWhiteSpace(resolved.LotNo);
            var isSerial = !string.IsNullOrWhiteSpace(serialNo) || IsSerial(resolved);
            decimal quantity;
            if (isSerial)
            {
                if (string.IsNullOrWhiteSpace(serialNo))
                    throw AppException.Conflict("Seri numarası zorunlu; aday listesinden bir seri seçin.");
                quantity = 1m;
                var serial = serialNo.Trim();
                var duplicate = await Scans.Query().AnyAsync(x =>
                    x.TaskId == task.Id
                    && x.SerialNo != null
                    && x.SerialNo == serial, token);
                if (duplicate)
                    throw AppException.Conflict($"Seri {serial} bu görevde zaten okutulmuş.");
            }
            else
            {
                quantity = request.Quantity is > 0
                    ? request.Quantity.Value
                    : resolved.Quantity is > 0 ? resolved.Quantity.Value : 1m;
                if (quantity <= 0)
                    throw AppException.BadRequest("Geçerli bir miktar girin.");
                var maxOnShelf = Math.Min(remaining, selected.AvailableQuantity);
                if (quantity > maxOnShelf)
                    quantity = maxOnShelf;
                if (quantity <= 0)
                    throw AppException.Conflict("Seçilen rafta toplanabilir bakiye yok.");
            }

            if (quantity > remaining)
                throw AppException.Conflict($"Kalan miktar {remaining:0.######}; {quantity:0.######} toplanamaz.");

            var threshold = await Warehouses.Query()
                .Where(x => x.Id == task.WarehouseId)
                .Select(x => x.AutoPickWithoutConfirmMaxQuantity)
                .SingleAsync(token);
            if (threshold is > 0 && quantity > threshold.Value && !request.ConfirmAboveThreshold)
                throw AppException.Conflict(PickAboveThresholdConfirmMessage);

            var now = DateTimeOffset.UtcNow;
            var unitCode = string.IsNullOrWhiteSpace(resolved.UnitCode) ? requestLine.UnitCode : resolved.UnitCode;

            // Canlı stok düşümü: rezervasyonu tüket (yoksa/eksikse tamamla) ve kaynak raf → KKD toplama
            // sanal rafına gerçek stok hareketi postala — "ben bu işi yapıyorum" dediği an bakiye düşer.
            var movementOperationId = await ConsumeReservationAndMoveAsync(
                task, line, resolved.StockId, unitCode, sourceLocationId,
                serialNo, lotNo, quantity,
                actor, now, request.IdempotencyKey, token);

            await Scans.AddAsync(new KkdPreparationBarcodeScan
            {
                TaskId = task.Id,
                TaskLineId = line.Id,
                RequestLineId = line.RequestLineId,
                IdempotencyKey = request.IdempotencyKey,
                BarcodeValue = resolved.RawBarcode,
                NormalizedBarcode = resolved.RawBarcode.Trim().ToUpperInvariant(),
                BarcodeSource = resolved.Source,
                StockId = resolved.StockId,
                UnitCode = unitCode,
                LotNo = lotNo,
                SerialNo = serialNo,
                Quantity = quantity,
                SourceLocationId = sourceLocationId,
                ScannedAtUtc = now,
                BranchCode = task.BranchCode,
                CreatedBy = actor,
                StockMovementOperationId = movementOperationId,
            }, token);

            line.PreparedQuantity = Math.Min(line.Quantity, line.PreparedQuantity + quantity);
            line.UpdatedBy = actor;
            line.UpdatedDate = now.UtcDateTime;
            if (task.Status == KkdPreparationTaskStatus.Assigned)
                task.Status = KkdPreparationTaskStatus.InPreparation;
            task.StartedAtUtc ??= now;
            task.UpdatedBy = actor;
            task.UpdatedDate = now.UtcDateTime;

            await uow.SaveChangesAsync(token);

            var taskRow = (await preparationTasks.GetByRequestAsync(task.RequestId, actor, token))
                .Single(x => x.Id == task.Id);
            var lineRow = taskRow.Lines.Single(x => x.Id == line.Id);
            return new(
                false,
                line.Id,
                line.RequestLineId,
                quantity,
                lineRow.PreparedQuantity,
                lineRow.Quantity,
                resolved.StockId,
                resolved.StockCode,
                resolved.StockName,
                NullIfWhiteSpace(lotNo),
                serialNo,
                sourceLocationId,
                taskRow);
        }, ct, IsolationLevel.Serializable);
    }

    public async Task<IReadOnlyList<KkdPreparationScanPickTracking>> GetStagedTrackingsAsync(
        long taskId,
        long requestLineId,
        CancellationToken ct = default)
    {
        // Her okutma anında stok zaten kaynak raftan KKD toplama sanal rafına (KkdPickingStagingLocationId)
        // taşınmıştı (bkz. ConsumeReservationAndMoveAsync) — o yüzden burada dağıtım/ambar çıkışına
        // bildirilecek kaynak, okutulan ORİJİNAL raf değil, stoğun şu an fiziksel olarak durduğu sanal raf
        // olmalı. Aksi halde ambar çıkışının "Topla" adımı, zaten boşalmış orijinal raftan tekrar düşmeye
        // çalışır: raf boşsa "yetersiz bakiye" hatası, raf'ta tesadüfen başka bir parti varsa sessizce
        // yanlış stoğu düşürür.
        var warehouseId = await Tasks.Query()
            .Where(x => x.Id == taskId)
            .Select(x => x.WarehouseId)
            .SingleOrDefaultAsync(ct);
        var stagingLocationId = await Warehouses.Query()
            .Where(x => x.Id == warehouseId)
            .Select(x => x.KkdPickingStagingLocationId)
            .SingleOrDefaultAsync(ct)
            ?? throw AppException.Conflict("Bu depo için KKD toplama sanal rafı tanımlanmamış.");

        // Yalnızca henüz dağıtıma bağlanmamış (consume edilmemiş) okutmalar.
        return await Scans.Query()
            .Where(x => x.TaskId == taskId && x.RequestLineId == requestLineId && x.DistributionId == null)
            .OrderBy(x => x.ScannedAtUtc)
            .Select(x => new KkdPreparationScanPickTracking(x.Quantity, x.LotNo, x.SerialNo, stagingLocationId))
            .ToListAsync(ct);
    }

    /// <summary>"Son okutmalar" listesi — geri alma (Unpick) UI'sı için. En yeni önce.</summary>
    public async Task<IReadOnlyList<KkdPreparationScanRow>> GetRecentScansAsync(long taskId, long actor, CancellationToken ct = default)
    {
        await LoadActiveTaskAsync(taskId, actor, tracking: false, ct);
        var scans = await Scans.Query()
            .Where(x => x.TaskId == taskId)
            .OrderByDescending(x => x.ScannedAtUtc)
            .Take(100)
            .ToListAsync(ct);
        var stockIds = scans.Select(x => x.StockId).Distinct().ToArray();
        var stocks = stockIds.Length == 0
            ? new Dictionary<long, (string Code, string Name)>()
            : await Stocks.Query().Where(x => stockIds.Contains(x.Id))
                .Select(x => new { x.Id, x.ErpStockCode, x.StockName })
                .ToDictionaryAsync(x => x.Id, x => (x.ErpStockCode, x.StockName), ct);
        return scans.Select(x =>
        {
            var (code, name) = stocks.GetValueOrDefault(x.StockId, (string.Empty, string.Empty));
            return new KkdPreparationScanRow(
                x.Id, x.TaskLineId, x.StockId, code, name, x.Quantity, x.UnitCode, x.LotNo, x.SerialNo,
                x.SourceLocationId, x.ScannedAtUtc, x.IsReversed, !x.IsReversed && x.DistributionId is null);
        }).ToArray();
    }

    /// <summary>
    /// Yanlış okutulan bir taramayı geri alır: gerçek stok hareketinin tersini postalar
    /// (üretimdeki Unpick ile aynı mantık — silme değil, ters kayıt), rezervasyonu geri yükler,
    /// PreparedQuantity'yi düşürür.
    /// </summary>
    public async Task<KkdPreparationUnpickResult> UnpickAsync(
        long taskId, long scanId, KkdPreparationUnpickRequest request, long actor, CancellationToken ct = default)
    {
        if (request.IdempotencyKey == Guid.Empty)
            throw AppException.BadRequest("IdempotencyKey zorunludur.");

        return await uow.ExecuteInTransactionAsync(async token =>
        {
            var scan = await Scans.Query(true)
                .SingleOrDefaultAsync(x => x.Id == scanId && x.TaskId == taskId, token)
                ?? throw AppException.NotFound("Tarama bulunamadı.");
            if (scan.IsReversed)
                throw AppException.Conflict("Bu tarama zaten geri alınmış.");
            if (scan.DistributionId.HasValue)
                throw AppException.Conflict("Teslimi tamamlanmış bir tarama geri alınamaz.");

            var task = await LoadActiveTaskAsync(taskId, actor, tracking: true, token);
            if (task.AssignedUserId is not null && task.AssignedUserId != actor)
                throw AppException.Forbidden("Bu hazırlama görevi başka bir kullanıcıya atanmış.");
            var line = task.Lines.SingleOrDefault(x => x.Id == scan.TaskLineId)
                ?? throw AppException.Conflict("Görev kalemi bulunamadı.");

            var now = DateTimeOffset.UtcNow;
            if (scan.StockMovementOperationId.HasValue)
                await movements.ReverseAsync(
                    scan.StockMovementOperationId.Value,
                    new($"{request.IdempotencyKey}:reverse", "KKD toplama geri alma", now.UtcDateTime), token);

            if (scan.SourceLocationId.HasValue)
            {
                var locationRow = await TaskLineLocations.Query(true)
                    .SingleOrDefaultAsync(x => x.TaskLineId == line.Id
                        && x.LocationId == scan.SourceLocationId.Value && x.SerialNo == scan.SerialNo, token);
                if (locationRow is not null)
                {
                    await balances.PostReservationAsync(new(
                        $"{request.IdempotencyKey}:reserve", "KkdPreparationTaskLine", line.Id, task.TaskNo,
                        StockReservationOperationTypes.Reserve, "Geri alma: rezervasyon geri yüklendi",
                        [new(line.Id, task.WarehouseId, scan.SourceLocationId.Value, scan.StockId, null,
                            scan.UnitCode, scan.LotNo, scan.SerialNo, "Available", scan.Quantity)]), token);
                    locationRow.ReservedQuantity += scan.Quantity;
                    locationRow.PickedQuantity = Math.Max(0, locationRow.PickedQuantity - scan.Quantity);
                    locationRow.UpdatedBy = actor;
                    locationRow.UpdatedDate = now.UtcDateTime;
                }
            }

            scan.IsReversed = true;
            scan.UpdatedBy = actor;
            scan.UpdatedDate = now.UtcDateTime;
            line.PreparedQuantity = Math.Max(0, line.PreparedQuantity - scan.Quantity);
            line.UpdatedBy = actor;
            line.UpdatedDate = now.UtcDateTime;
            if (task.Status == KkdPreparationTaskStatus.InPreparation && task.Lines.All(x => x.PreparedQuantity <= 0))
                task.Status = KkdPreparationTaskStatus.Assigned;
            task.UpdatedBy = actor;
            task.UpdatedDate = now.UtcDateTime;

            await uow.SaveChangesAsync(token);
            await audit.WriteAsync(new AuditLogWriteEntry(
                "kkd.preparation-task.unpick", nameof(KkdPreparationBarcodeScan), scan.Id.ToString(), "Succeeded", "kkd-request",
                NewValues: new { scan.Id, scan.Quantity, scan.StockId }, ChangedFields: ["IsReversed", "PreparedQuantity"]), token);

            var taskRow = (await preparationTasks.GetByRequestAsync(task.RequestId, actor, token)).Single(x => x.Id == task.Id);
            return new KkdPreparationUnpickResult(scan.Id, scan.TaskLineId, scan.Quantity, taskRow);
        }, ct, IsolationLevel.Serializable);
    }

    private sealed record PreparedMatch(
        long RequestId,
        long TaskLineId,
        long RequestLineId,
        bool NeedsGroupResolve,
        ResolvedWarehouseBarcode Resolved);

    private async Task<PreparedMatch> PrepareMatchAsync(
        long taskId,
        KkdPreparationScanPickRequest request,
        long actor,
        CancellationToken ct)
    {
        var task = await LoadActiveTaskAsync(taskId, actor, tracking: false, ct);
        var resolved = await ResolveBarcodeAsync(task, request.Barcode, ct);

        var stockGroup = await Stocks.Query().Where(x => x.Id == resolved.StockId)
            .Select(x => x.GroupCode).SingleOrDefaultAsync(ct);
        var match = FindTargetLine(task, resolved, stockGroup, request.ExpectedTaskLineId);
        return new(task.RequestId, match.Line.Id, match.Line.RequestLineId, match.NeedsGroupResolve, resolved);
    }

    /// <summary>
    /// StokKodu**SeriNo yazılmışsa önce stok koduyla StockId'yi bulur, seriyi ExpectedStockId ile
    /// çözer; düz stok kodu bu görevin açık kalemine oturuyorsa aynı bağlamla çözülür (çıkış
    /// barkod politikası stok kodunu reddetmesin). Aksi halde ham metin paylaşılan çözücüye gider.
    /// </summary>
    private async Task<ResolvedWarehouseBarcode> ResolveBarcodeAsync(KkdPreparationTask task, string rawBarcode, CancellationToken ct)
    {
        var parsed = KkdBarcodeInput.Parse(rawBarcode);
        ResolvedWarehouseBarcode resolved;
        if (parsed.StockCode is not null)
        {
            var stockId = await Stocks.Query()
                .Where(x => x.BranchCode == task.BranchCode && x.ErpStockCode == parsed.StockCode)
                .Select(x => (long?)x.Id).SingleOrDefaultAsync(ct)
                ?? throw AppException.NotFound($"{parsed.StockCode} stok kartıyla eşleşmedi.");
            resolved = await barcodeResolver.ResolveAsync(new(
                parsed.SerialNo!, task.BranchCode, WarehouseBarcodePurpose.Outbound, task.WarehouseId,
                ExpectedStockId: stockId), ct);
        }
        else
        {
            var expectedStockId = await FindOpenTaskStockIdByCodeAsync(task, parsed.Raw, ct);
            resolved = await barcodeResolver.ResolveAsync(new(
                parsed.Raw, task.BranchCode, WarehouseBarcodePurpose.Outbound, task.WarehouseId,
                ExpectedStockId: expectedStockId), ct);
        }

        if (!resolved.CanExecute)
        {
            var onlyTrackingMissing = resolved.MissingFields.Count > 0
                && resolved.MissingFields.All(field => field is "Seri" or "Lot");
            if (resolved.BalanceCandidates.Count == 0 || !onlyTrackingMissing)
            {
                throw AppException.Conflict(resolved.MissingFields.Count > 0
                    ? $"Barkod toplama için uygun değil: {string.Join(", ", resolved.MissingFields)}."
                    : "Barkod çıkış için kullanılamıyor.");
            }
        }

        return resolved;
    }

    private async Task<long?> FindOpenTaskStockIdByCodeAsync(KkdPreparationTask task, string code, CancellationToken ct)
    {
        var normalized = code.Trim();
        if (string.IsNullOrWhiteSpace(normalized)) return null;
        var stockIds = task.Lines
            .Where(x => !x.IsDeleted && Remaining(x) > 0 && x.RequestLine.StockId.HasValue)
            .Select(x => x.RequestLine.StockId!.Value)
            .Distinct()
            .ToArray();
        if (stockIds.Length == 0) return null;
        var stocks = await Stocks.Query()
            .Where(x => stockIds.Contains(x.Id) && x.BranchCode == task.BranchCode)
            .Select(x => new
            {
                x.Id,
                x.ErpStockCode,
                x.ManufacturerCode,
                x.Code1,
                x.Code2,
                x.Code3,
                x.Code4,
                x.Code5,
            })
            .ToListAsync(ct);
        return stocks.FirstOrDefault(stock =>
            new[]
            {
                stock.ErpStockCode,
                stock.ManufacturerCode,
                stock.Code1,
                stock.Code2,
                stock.Code3,
                stock.Code4,
                stock.Code5,
            }.Any(alias => string.Equals(alias?.Trim(), normalized, StringComparison.OrdinalIgnoreCase)))?.Id;
    }

    internal static bool HasUniquePickSource(ResolvedWarehouseBarcode resolved)
    {
        if (resolved.BalanceCandidates.Count == 1) return true;
        if (resolved.SuggestedLocationId is not { } locationId) return false;
        return resolved.BalanceCandidates.Count(candidate => candidate.LocationId == locationId) == 1;
    }

    internal static WarehouseBarcodeBalanceCandidate? SelectBalanceCandidate(
        ResolvedWarehouseBarcode resolved,
        long? sourceLocationId,
        string? serialNo,
        string? lotNo)
    {
        var serial = NullIfWhiteSpace(serialNo);
        var lot = NullIfWhiteSpace(lotNo);
        var matches = resolved.BalanceCandidates.AsEnumerable();
        if (sourceLocationId is { } locationId)
            matches = matches.Where(candidate => candidate.LocationId == locationId);
        if (serial is not null)
            matches = matches.Where(candidate => string.Equals(candidate.SerialNo, serial, StringComparison.OrdinalIgnoreCase));
        if (lot is not null)
            matches = matches.Where(candidate => string.Equals(candidate.LotNo, lot, StringComparison.OrdinalIgnoreCase));

        var list = matches.ToArray();
        if (list.Length == 1) return list[0];
        if (list.Length == 0 && sourceLocationId is null && resolved.BalanceCandidates.Count == 1)
            return resolved.BalanceCandidates[0];
        return null;
    }

    private async Task<KkdPreparationScanPickResult> BuildReplayResultAsync(
        long taskId,
        KkdPreparationBarcodeScan replay,
        long actor,
        CancellationToken ct)
    {
        if (replay.TaskId != taskId)
            throw AppException.Conflict("IdempotencyKey başka bir göreve ait.");
        var requestId = await Tasks.Query().Where(x => x.Id == taskId).Select(x => x.RequestId).SingleAsync(ct);
        var taskRow = (await preparationTasks.GetByRequestAsync(requestId, actor, ct)).Single(x => x.Id == taskId);
        var lineRow = taskRow.Lines.Single(x => x.Id == replay.TaskLineId);
        var stock = await Stocks.Query().Where(x => x.Id == replay.StockId)
            .Select(x => new { x.ErpStockCode, x.StockName }).SingleAsync(ct);
        return new(
            true,
            replay.TaskLineId,
            replay.RequestLineId,
            replay.Quantity,
            lineRow.PreparedQuantity,
            lineRow.Quantity,
            replay.StockId,
            stock.ErpStockCode,
            stock.StockName,
            replay.LotNo,
            replay.SerialNo,
            replay.SourceLocationId,
            taskRow);
    }

    /// <summary>
    /// Rezervasyonu tüketir (eksikse önce tamamlar) ve kaynak raf → KKD toplama sanal rafına gerçek
    /// stok hareketi postalar. Bu, "toplama = anlık stok hareketi" davranışının kalbi.
    /// </summary>
    private async Task<long> ConsumeReservationAndMoveAsync(
        KkdPreparationTask task,
        KkdPreparationTaskLine line,
        long stockId,
        string unitCode,
        long? sourceLocationId,
        string? serialNo,
        string? lotNo,
        decimal quantity,
        long actor,
        DateTimeOffset now,
        Guid idempotencyKey,
        CancellationToken ct)
    {
        if (!sourceLocationId.HasValue)
            throw AppException.Conflict("Kaynak raf belirlenemedi; birden fazla raf/seri varsa birini seçmelisiniz.");

        var stagingLocationId = await Warehouses.Query()
            .Where(x => x.Id == task.WarehouseId)
            .Select(x => x.KkdPickingStagingLocationId)
            .SingleAsync(ct)
            ?? throw AppException.Conflict("Bu depo için KKD toplama sanal rafı tanımlanmamış.");

        var locationRow = await TaskLineLocations.Query(true)
            .SingleOrDefaultAsync(x => x.TaskLineId == line.Id && x.LocationId == sourceLocationId.Value
                && x.SerialNo == serialNo, ct);
        var shortfall = quantity - (locationRow?.ReservedQuantity ?? 0m);
        if (shortfall > 0)
        {
            await balances.PostReservationAsync(new(
                $"{idempotencyKey}:reserve", "KkdPreparationTaskLine", line.Id, task.TaskNo,
                StockReservationOperationTypes.Reserve, "Toplama sırasında ek rezervasyon",
                [new(line.Id, task.WarehouseId, sourceLocationId.Value, stockId, null, unitCode, lotNo, serialNo, "Available", shortfall)]), ct);
            if (locationRow is null)
            {
                locationRow = new KkdPreparationTaskLineLocation
                {
                    TaskLineId = line.Id,
                    LocationId = sourceLocationId.Value,
                    SerialNo = serialNo,
                    LotNo = lotNo,
                    ReservedQuantity = 0,
                    CreatedBy = actor,
                    CreatedDate = now.UtcDateTime,
                };
                await TaskLineLocations.AddAsync(locationRow, ct);
            }
            locationRow.ReservedQuantity += shortfall;
        }

        await balances.PostReservationAsync(new(
            $"{idempotencyKey}:consume", "KkdPreparationTaskLine", line.Id, task.TaskNo,
            StockReservationOperationTypes.Consume, "Toplama tüketimi",
            [new(line.Id, task.WarehouseId, sourceLocationId.Value, stockId, null, unitCode, lotNo, serialNo, "Available", quantity)]), ct);
        locationRow!.ReservedQuantity = Math.Max(0, locationRow.ReservedQuantity - quantity);
        locationRow.PickedQuantity += quantity;
        locationRow.UpdatedBy = actor;
        locationRow.UpdatedDate = now.UtcDateTime;

        var movement = await movements.PostAsync(new(
            $"{idempotencyKey}:movement", StockMovementTypes.Transfer, "KkdPreparationTaskLine", task.TaskNo, line.Id,
            now.UtcDateTime, "KKD toplama", null,
            [new(stockId, null, quantity, task.WarehouseId, sourceLocationId.Value, task.WarehouseId, stagingLocationId,
                unitCode, lotNo, serialNo, "Available", "Available", "Available")]), ct);
        return movement.OperationId;
    }

    private async Task<KkdPreparationTask> LoadActiveTaskAsync(long taskId, long actor, bool tracking, CancellationToken ct)
    {
        var task = await Tasks.Query(tracking)
            .Include(x => x.Lines).ThenInclude(x => x.RequestLine)
            .SingleOrDefaultAsync(x => x.Id == taskId, ct)
            ?? throw AppException.NotFound("Hazırlama görevi bulunamadı.");

        if (task.Status is not (KkdPreparationTaskStatus.Assigned or KkdPreparationTaskStatus.InPreparation))
            throw AppException.Conflict("Görev toplama için aktif değil.");

        var warehouseIds = await WarehouseAssignments.Query().Where(x => x.UserId == actor)
            .Select(x => x.WarehouseId).ToArrayAsync(ct);
        if (warehouseIds.Length > 0 && !warehouseIds.Contains(task.WarehouseId))
            throw AppException.Forbidden("Bu depodaki göreve erişim yetkiniz yok.");

        return task;
    }

    private static (KkdPreparationTaskLine Line, bool NeedsGroupResolve) FindTargetLine(
        KkdPreparationTask task,
        ResolvedWarehouseBarcode resolved,
        string? stockGroupCode,
        long? expectedTaskLineId)
    {
        var openLines = task.Lines.Where(x => !x.IsDeleted && Remaining(x) > 0).ToArray();
        if (expectedTaskLineId is { } expectedId)
        {
            var expected = openLines.SingleOrDefault(x => x.Id == expectedId)
                ?? throw AppException.Conflict("Beklenen görev kalemi açık değil.");
            return MatchLine(expected, resolved, stockGroupCode);
        }

        var known = openLines.FirstOrDefault(x => x.RequestLine.StockId == resolved.StockId);
        if (known is not null)
            return (known, false);

        var unresolved = openLines
            .Where(x => !x.RequestLine.StockId.HasValue
                && SameGroup(x.RequestLine.GroupCode, stockGroupCode))
            .ToArray();
        if (unresolved.Length > 0)
            return (unresolved[0], true);

        throw AppException.Conflict($"{resolved.StockCode} bu toplamada beklenen kalemlerden biri değil veya zaten tamamlandı.");
    }

    private static (KkdPreparationTaskLine Line, bool NeedsGroupResolve) MatchLine(
        KkdPreparationTaskLine line,
        ResolvedWarehouseBarcode resolved,
        string? stockGroupCode)
    {
        if (line.RequestLine.StockId is { } stockId)
        {
            if (stockId != resolved.StockId)
                throw AppException.Conflict("Okutulan stok seçilen kalemle eşleşmiyor.");
            return (line, false);
        }

        if (!SameGroup(line.RequestLine.GroupCode, stockGroupCode))
            throw AppException.Conflict("Okutulan stok seçilen kalemin grubuyla eşleşmiyor.");
        return (line, true);
    }

    private static bool SameGroup(string groupCode, string? stockGroupCode) =>
        !string.IsNullOrWhiteSpace(stockGroupCode)
        && string.Equals(groupCode.Trim(), stockGroupCode.Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Hazırlanacak kalan. Prepared ve Delivered aynı birimleri iki kez düşürmesin diye Max kullanılır
    /// (Teslim sonrası Prepared hâlâ dolu kalır; Delivered ayrıca düşülürse kalan şişer/küçülür).
    /// </summary>
    private static decimal Remaining(KkdPreparationTaskLine line) =>
        Math.Max(0m, line.Quantity - Math.Max(line.PreparedQuantity, line.DeliveredQuantity));

    private static bool IsSerial(ResolvedWarehouseBarcode resolved) =>
        !string.IsNullOrWhiteSpace(resolved.SerialNo) || resolved.RequireSerial;

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
