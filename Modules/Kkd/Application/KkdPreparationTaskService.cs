using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using verii_wms_api_v2.Modules.Audit.Application;
using verii_wms_api_v2.Modules.BarcodeDesigner.Application;
using verii_wms_api_v2.Modules.Identity.Domain;
using verii_wms_api_v2.Modules.Kkd.Domain;
using verii_wms_api_v2.Modules.Kkd.Localization;
using verii_wms_api_v2.Modules.Location.Domain;
using verii_wms_api_v2.Modules.StockBalance.Application;
using verii_wms_api_v2.Modules.StockBalance.Domain;
using verii_wms_api_v2.Shared.Application.Abstractions.Persistence;
using verii_wms_api_v2.Shared.Application.Exceptions;
using WarehouseEntity = verii_wms_api_v2.Modules.Warehouse.Domain.Warehouse;

namespace verii_wms_api_v2.Modules.Kkd.Application;

/// <summary>
/// KKD hazırlama görevleri: üretim transfer görev modelinin KKD karşılığı.
/// Kalem bazlı atama, üzerine alma, kalan işi devretme, işi havuza iade ve fiziksel toplama
/// (başlatma/raf rezervasyonu/rota güncelleme) akışlarını yönetir.
/// </summary>
public sealed class KkdPreparationTaskService(
    IUnitOfWork uow,
    IAuditLogWriter audit,
    IKkdEntitlementService entitlements,
    IStockBalanceService balances,
    IWarehouseBarcodeResolver barcodeResolver,
    IStringLocalizer<KkdRequestResource> localizer) : IKkdPreparationTaskService
{
    private IGenericRepository<KkdPreparationTask> Tasks => uow.Repository<KkdPreparationTask>();
    private IGenericRepository<KkdPreparationTaskLine> TaskLines => uow.Repository<KkdPreparationTaskLine>();
    private IGenericRepository<KkdPreparationTaskLineLocation> TaskLineLocations => uow.Repository<KkdPreparationTaskLineLocation>();
    private IGenericRepository<KkdRequest> Requests => uow.Repository<KkdRequest>();
    private IGenericRepository<User> Users => uow.Repository<User>();
    private IGenericRepository<WarehouseEntity> Warehouses => uow.Repository<WarehouseEntity>();
    private IGenericRepository<WarehouseLocation> Locations => uow.Repository<WarehouseLocation>();
    private IGenericRepository<UserWarehouseAssignment> WarehouseAssignments => uow.Repository<UserWarehouseAssignment>();

    private static readonly KkdPreparationTaskStatus[] ActiveStatuses =
        [KkdPreparationTaskStatus.Assigned, KkdPreparationTaskStatus.InPreparation];

    public async Task<IReadOnlyList<KkdPreparationTaskRow>> GetByRequestAsync(long requestId, long actor, CancellationToken ct = default)
    {
        var request = await Requests.Query().Where(x => x.Id == requestId)
            .Select(x => new { x.WarehouseId }).SingleOrDefaultAsync(ct);
        if (request?.WarehouseId is { } warehouseId)
            await EnsureWarehouseAccessAsync(actor, warehouseId, ct);

        var tasks = await Tasks.Query()
            .Include(x => x.Request)
            .Include(x => x.Lines).ThenInclude(x => x.RequestLine)
            .Include(x => x.Lines).ThenInclude(x => x.Locations)
            .Include(x => x.Distribution)
            .Where(x => x.RequestId == requestId)
            .OrderBy(x => x.Id)
            .ToListAsync(ct);
        return await MapRowsAsync(tasks, ct);
    }

    public async Task<IReadOnlyList<KkdPreparationTaskRow>> AssignAsync(long requestId, KkdPreparationAssignRequest request, long actor, CancellationToken ct = default)
    {
        ValidateKey(request.IdempotencyKey);
        if (request.Groups is null || request.Groups.Count == 0 || request.Groups.Any(x => x.LineIds is null || x.LineIds.Count == 0))
            throw AppException.BadRequest(Message(KkdRequestMessageKeys.TaskGroupsRequired));

        return await uow.ExecuteInTransactionAsync(async token =>
        {
            if (await Tasks.AnyAsync(x => x.CorrelationId == request.IdempotencyKey, token))
                return await GetByRequestAsync(requestId, actor, token);

            var entity = await Requests.Query(true).Include(x => x.Lines)
                .SingleOrDefaultAsync(x => x.Id == requestId, token)
                ?? throw AppException.NotFound(Message(KkdRequestMessageKeys.NotFound));
            EnsureMutable(entity);
            CheckVersion(entity.RowVersion, request.ExpectedRowVersion);
            if (!await Warehouses.AnyAsync(x => x.Id == request.WarehouseId, token))
                throw AppException.NotFound(Message(KkdRequestMessageKeys.NotFound));

            // Kişiye atanmış gruplarda tekrar kontrolü; havuza bırakılan (kullanıcısız) grup birden fazla olamaz.
            var namedUserIds = request.Groups.Where(x => x.AssignedUserId.HasValue).Select(x => x.AssignedUserId!.Value).ToArray();
            if (namedUserIds.Distinct().Count() != namedUserIds.Length)
                throw AppException.BadRequest(Message(KkdRequestMessageKeys.DuplicateAssignee));
            if (request.Groups.Count(x => !x.AssignedUserId.HasValue) > 1)
                throw AppException.BadRequest(Message(KkdRequestMessageKeys.DuplicatePoolGroup));
            // Tek bir çağrı içindeki kontrol yeterli değil: talebe ayrı ayrı çağrılarla art arda "havuza ata"
            // dendiğinde de en fazla bir aktif (sahipsiz) havuz görevi olmalı.
            if (request.Groups.Any(x => !x.AssignedUserId.HasValue) && await Tasks.AnyAsync(x =>
                x.RequestId == requestId && x.AssignedUserId == null
                && (x.Status == KkdPreparationTaskStatus.Assigned || x.Status == KkdPreparationTaskStatus.InPreparation), token))
                throw AppException.BadRequest(Message(KkdRequestMessageKeys.DuplicatePoolGroup));
            var activeUserCount = await Users.CountAsync(x => namedUserIds.Contains(x.Id) && x.IsActive, token);
            if (activeUserCount != namedUserIds.Length)
                throw AppException.NotFound(Message(KkdRequestMessageKeys.UserNotFound));
            foreach (var userId in namedUserIds)
                await EnsureWarehouseAccessAsync(userId, request.WarehouseId, token);

            var unassigned = await UnassignedOpenLinesAsync(entity, token);
            var requested = request.Groups.SelectMany(x => x.LineIds).ToArray();
            if (requested.Distinct().Count() != requested.Length)
                throw AppException.BadRequest(Message(KkdRequestMessageKeys.DuplicateLineAssignment));
            if (requested.Any(id => !unassigned.ContainsKey(id)))
                throw AppException.Conflict(Message(KkdRequestMessageKeys.LineAlreadyAssigned));
            // Tüm açık kalemlerin atanması artık zorunlu değil: müdür kota aşımlı bir kalemi
            // bu turda hariç tutabilir (o kalem atanmamış kalır, sonra tekrar denenebilir).

            // Kota aşan ve henüz karara bağlanmamış (Approved değil) bir kalem seçildiyse atama tamamen
            // durur — müdür önce ya bu kalemi seçimden çıkarmalı ya da kota kararını (Onayla/Reddet) vermeli.
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            foreach (var lineId in requested)
            {
                var line = entity.Lines.Single(x => x.Id == lineId);
                if (line.StockId is not { } stockId || line.QuotaDecision == KkdRequestLineQuotaDecision.Approved) continue;
                var check = await entitlements.CheckAsync(new(entity.EmployeeId, stockId, unassigned[lineId], today), token);
                if (!check.IsAllowed)
                    throw AppException.Conflict(Message(KkdRequestMessageKeys.QuotaDecisionRequired));
            }

            var now = DateTimeOffset.UtcNow;
            var sequence = await Tasks.CountAsync(x => x.RequestId == entity.Id, token);
            var created = new List<KkdPreparationTask>();
            foreach (var group in request.Groups)
            {
                sequence += 1;
                var task = new KkdPreparationTask
                {
                    // Idempotency: ilk görev istek anahtarını taşır; sonrakiler türetilmiş anahtar alır.
                    CorrelationId = created.Count == 0 ? request.IdempotencyKey : Guid.NewGuid(),
                    RequestId = entity.Id,
                    TaskNo = $"{entity.RequestNo}-H{sequence}",
                    AssignedUserId = group.AssignedUserId,
                    WarehouseId = request.WarehouseId,
                    Status = KkdPreparationTaskStatus.Assigned,
                    AssignedAtUtc = now,
                    CreatedBy = actor,
                    Lines = group.LineIds.Select(lineId => new KkdPreparationTaskLine
                    {
                        RequestLineId = lineId,
                        Quantity = unassigned[lineId],
                        CreatedBy = actor,
                    }).ToList(),
                };
                created.Add(task);
                await Tasks.AddAsync(task, token);
            }

            entity.WarehouseId = request.WarehouseId;
            entity.StartedAtUtc ??= now;
            entity.UpdatedBy = actor;
            entity.UpdatedDate = now.UtcDateTime;
            await SaveAsync(token);
            await audit.WriteAsync(new AuditLogWriteEntry(
                "kkd.preparation-task.assign", nameof(KkdRequest), entity.Id.ToString(), "Succeeded", "kkd-request",
                NewValues: new
                {
                    entity.WarehouseId,
                    Tasks = created.Select(x => new { x.TaskNo, x.AssignedUserId, LineIds = x.Lines.Select(l => l.RequestLineId) }),
                },
                ChangedFields: ["WarehouseId", "PreparationTasks"]), token);
            return await GetByRequestAsync(entity.Id, actor, token);
        }, ct, IsolationLevel.Serializable);
    }

    public async Task<KkdPreparationTaskRow> ClaimAsync(long requestId, KkdPreparationClaimRequest request, long actor, CancellationToken ct = default)
    {
        ValidateKey(request.IdempotencyKey);
        return await uow.ExecuteInTransactionAsync(async token =>
        {
            var existing = await Tasks.Query().Include(x => x.Request)
                .Include(x => x.Lines).ThenInclude(x => x.RequestLine)
                .Include(x => x.Lines).ThenInclude(x => x.Locations)
                .SingleOrDefaultAsync(x => x.CorrelationId == request.IdempotencyKey, token);
            if (existing is not null) return (await MapRowsAsync([existing], token))[0];

            var entity = await Requests.Query(true).Include(x => x.Lines)
                .SingleOrDefaultAsync(x => x.Id == requestId, token)
                ?? throw AppException.NotFound(Message(KkdRequestMessageKeys.NotFound));
            EnsureMutable(entity);
            CheckVersion(entity.RowVersion, request.ExpectedRowVersion);
            if (!await Warehouses.AnyAsync(x => x.Id == request.WarehouseId, token))
                throw AppException.NotFound(Message(KkdRequestMessageKeys.NotFound));

            await EnsureWarehouseAccessAsync(actor, request.WarehouseId, token);

            var unassigned = await UnassignedOpenLinesAsync(entity, token);
            if (unassigned.Count == 0)
                throw AppException.Conflict(Message(KkdRequestMessageKeys.NothingToAssign));

            // Kota aşan kalemler artık dışlanmıyor — iş kişinin üzerinde kalır, tüm kalemler göreve girer.
            // Ama karar verilmemiş (Pending) kalemi varsa StartAsync toplamayı başlatmaz; müdür karara
            // bağlayana kadar iş bu kişinin üzerinde "kilitli" durur.
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var claimable = new Dictionary<long, decimal>(unassigned);
            var now = DateTimeOffset.UtcNow;
            foreach (var (lineId, quantity) in unassigned)
            {
                var line = entity.Lines.Single(x => x.Id == lineId);
                if (line.StockId is not { } stockId || line.QuotaDecision != KkdRequestLineQuotaDecision.None) continue;
                var check = await entitlements.CheckAsync(new(entity.EmployeeId, stockId, quantity, today), token);
                if (check.IsAllowed) continue;
                line.QuotaDecision = KkdRequestLineQuotaDecision.Pending;
                line.UpdatedBy = actor;
                line.UpdatedDate = now.UtcDateTime;
            }
            var sequence = await Tasks.CountAsync(x => x.RequestId == entity.Id, token) + 1;
            var task = new KkdPreparationTask
            {
                CorrelationId = request.IdempotencyKey,
                RequestId = entity.Id,
                TaskNo = $"{entity.RequestNo}-H{sequence}",
                AssignedUserId = actor,
                WarehouseId = request.WarehouseId,
                Status = KkdPreparationTaskStatus.Assigned,
                AssignedAtUtc = now,
                CreatedBy = actor,
                Lines = claimable.Select(pair => new KkdPreparationTaskLine
                {
                    RequestLineId = pair.Key,
                    Quantity = pair.Value,
                    CreatedBy = actor,
                }).ToList(),
            };
            await Tasks.AddAsync(task, token);
            entity.WarehouseId = request.WarehouseId;
            entity.StartedAtUtc ??= now;
            entity.UpdatedBy = actor;
            entity.UpdatedDate = now.UtcDateTime;
            await SaveAsync(token);
            await audit.WriteAsync(new AuditLogWriteEntry(
                "kkd.preparation-task.claim", nameof(KkdPreparationTask), task.Id.ToString(), "Succeeded", "kkd-request",
                NewValues: new { task.TaskNo, task.AssignedUserId, LineIds = task.Lines.Select(l => l.RequestLineId) },
                ChangedFields: ["PreparationTasks"]), token);
            var loaded = await LoadTaskAsync(task.Id, token);
            return (await MapRowsAsync([loaded], token))[0];
        }, ct, IsolationLevel.Serializable);
    }

    public async Task<KkdPreparationTaskRow> ClaimTaskAsync(long taskId, KkdPreparationClaimTaskRequest request, long actor, CancellationToken ct = default)
    {
        ValidateKey(request.IdempotencyKey);
        return await uow.ExecuteInTransactionAsync(async token =>
        {
            var task = await Tasks.Query(true)
                .Include(x => x.Request)
                .Include(x => x.Lines).ThenInclude(x => x.RequestLine)
                .Include(x => x.Lines).ThenInclude(x => x.Locations)
                .SingleOrDefaultAsync(x => x.Id == taskId, token)
                ?? throw AppException.NotFound(Message(KkdRequestMessageKeys.TaskNotFound));
            EnsureActive(task);
            if (task.AssignedUserId == actor) return (await MapRowsAsync([task], token))[0];
            if (task.AssignedUserId.HasValue) throw AppException.Conflict(Message(KkdRequestMessageKeys.TaskAlreadyClaimed));
            CheckVersion(task.RowVersion, request.ExpectedRowVersion);
            await EnsureWarehouseAccessAsync(actor, task.WarehouseId, token);

            var now = DateTimeOffset.UtcNow;
            task.AssignedUserId = actor;
            task.AssignedAtUtc = now;
            task.UpdatedBy = actor;
            task.UpdatedDate = now.UtcDateTime;
            await SaveAsync(token);
            await audit.WriteAsync(new AuditLogWriteEntry(
                "kkd.preparation-task.claim-pool", nameof(KkdPreparationTask), task.Id.ToString(), "Succeeded", "kkd-request",
                NewValues: new { task.AssignedUserId }, ChangedFields: ["AssignedUserId"]), token);
            return (await MapRowsAsync([task], token))[0];
        }, ct, IsolationLevel.Serializable);
    }

    public async Task<KkdPreparationTaskRow> HandoffAsync(long taskId, KkdPreparationHandoffRequest request, long actor, CancellationToken ct = default)
    {
        ValidateKey(request.IdempotencyKey);
        ValidateReason(request.Reason);
        return await uow.ExecuteInTransactionAsync(async token =>
        {
            var existing = await Tasks.Query().Include(x => x.Request)
                .Include(x => x.Lines).ThenInclude(x => x.RequestLine)
                .Include(x => x.Lines).ThenInclude(x => x.Locations)
                .SingleOrDefaultAsync(x => x.CorrelationId == request.IdempotencyKey, token);
            if (existing is not null) return (await MapRowsAsync([existing], token))[0];

            var task = await Tasks.Query(true)
                .Include(x => x.Request)
                .Include(x => x.Lines).ThenInclude(x => x.RequestLine)
                .Include(x => x.Lines).ThenInclude(x => x.Locations)
                .SingleOrDefaultAsync(x => x.Id == taskId, token)
                ?? throw AppException.NotFound(Message(KkdRequestMessageKeys.TaskNotFound));
            EnsureActive(task);
            if (!task.AssignedUserId.HasValue) throw AppException.Conflict(Message(KkdRequestMessageKeys.TaskNotPooled));
            CheckVersion(task.RowVersion, request.ExpectedRowVersion);
            if (request.ToUserId == task.AssignedUserId)
                throw AppException.BadRequest(Message(KkdRequestMessageKeys.HandoffSameUser));
            if (!await Users.AnyAsync(x => x.Id == request.ToUserId && x.IsActive, token))
                throw AppException.NotFound(Message(KkdRequestMessageKeys.UserNotFound));
            await EnsureWarehouseAccessAsync(request.ToUserId, task.WarehouseId, token);

            var now = DateTimeOffset.UtcNow;
            var hasProgress = task.Lines.Any(x => x.PreparedQuantity > 0 || x.DeliveredQuantity > 0);
            if (!hasProgress)
            {
                // Üretimdeki kural: ilerleme yoksa devir aynı görev üzerinde kullanıcı değiştirir.
                var oldUser = task.AssignedUserId;
                task.AssignedUserId = request.ToUserId;
                task.OriginUserId = oldUser;
                task.CorrelationId = request.IdempotencyKey;
                task.UpdatedBy = actor;
                task.UpdatedDate = now.UtcDateTime;
                await SaveAsync(token);
                await audit.WriteAsync(new AuditLogWriteEntry(
                    "kkd.preparation-task.handoff", nameof(KkdPreparationTask), task.Id.ToString(), "Succeeded", "kkd-request",
                    Reason: request.Reason.Trim(),
                    OldValues: new { AssignedUserId = oldUser },
                    NewValues: new { task.AssignedUserId },
                    ChangedFields: ["AssignedUserId"]), token);
                return (await MapRowsAsync([task], token))[0];
            }

            // Kalan (hazırlanmamış) miktar yeni göreve taşınır; hazırlanan kısım eski görevde kalır.
            var movable = task.Lines
                .Select(line => new { Line = line, Remaining = line.Quantity - line.PreparedQuantity })
                .Where(x => x.Remaining > 0)
                .ToArray();
            if (movable.Length == 0)
                throw AppException.Conflict(Message(KkdRequestMessageKeys.NothingToHandoff));

            var sequence = await Tasks.CountAsync(x => x.RequestId == task.RequestId, token) + 1;
            var next = new KkdPreparationTask
            {
                CorrelationId = request.IdempotencyKey,
                RequestId = task.RequestId,
                TaskNo = $"{task.Request.RequestNo}-H{sequence}",
                AssignedUserId = request.ToUserId,
                WarehouseId = task.WarehouseId,
                Status = KkdPreparationTaskStatus.Assigned,
                PreviousTaskId = task.Id,
                OriginUserId = task.AssignedUserId,
                AssignedAtUtc = now,
                CreatedBy = actor,
                Lines = movable.Select(x => new KkdPreparationTaskLine
                {
                    RequestLineId = x.Line.RequestLineId,
                    Quantity = x.Remaining,
                    CreatedBy = actor,
                }).ToList(),
            };
            await Tasks.AddAsync(next, token);
            foreach (var item in movable)
            {
                if (item.Line.PreparedQuantity <= 0) TaskLines.Remove(item.Line);
                else item.Line.Quantity = item.Line.PreparedQuantity;
            }
            task.UpdatedBy = actor;
            task.UpdatedDate = now.UtcDateTime;
            await SaveAsync(token);
            await audit.WriteAsync(new AuditLogWriteEntry(
                "kkd.preparation-task.handoff", nameof(KkdPreparationTask), task.Id.ToString(), "Succeeded", "kkd-request",
                Reason: request.Reason.Trim(),
                NewValues: new { NextTaskNo = next.TaskNo, next.AssignedUserId, LineIds = next.Lines.Select(l => l.RequestLineId) },
                ChangedFields: ["PreparationTasks"]), token);
            var loaded = await LoadTaskAsync(next.Id, token);
            return (await MapRowsAsync([loaded], token))[0];
        }, ct, IsolationLevel.Serializable);
    }

    /// <summary>
    /// "Bu işi yapıyorum": havuz görevinde ilk basan üzerine alır (atomik), sonra stoğu bilinen
    /// satırlara raf ataması + gerçek rezervasyon yapılır. Stoğu henüz bilinmeyen satırlar
    /// atlanır — iş sekteye uğramaz, o satır okutulup çözülünce kendi rafı ayrıca oluşur
    /// (bkz. KkdPreparationScanPickService). İdempotent: zaten başlamışsa mevcut durumu döner.
    /// </summary>
    public async Task<KkdPreparationTaskRow> StartAsync(long taskId, KkdPreparationStartRequest request, long actor, CancellationToken ct = default)
    {
        ValidateKey(request.IdempotencyKey);
        // Claim/Assign anında kota uygundu, ama aradan geçen sürede personelin hakkı BAŞKA bir talepte
        // tükenmiş olabilir. Bunu, stok fiziksel olarak raftan çıkmadan (rezervasyondan) önce yakalamak için
        // ayrı bir ön-işlemde kontrol edip kalıcı olarak Pending işaretliyoruz — aşağıdaki mevcut
        // "Pending/Rejected kalemi varsa başlatma" kontrolü bunu doğal olarak yakalar.
        await RefreshQuotaDecisionsBeforeStartAsync(taskId, actor, ct);
        return await uow.ExecuteInTransactionAsync(async token =>
        {
            var task = await Tasks.Query(true)
                .Include(x => x.Request)
                .Include(x => x.Lines).ThenInclude(x => x.RequestLine)
                .Include(x => x.Lines).ThenInclude(x => x.Locations)
                .SingleOrDefaultAsync(x => x.Id == taskId, token)
                ?? throw AppException.NotFound(Message(KkdRequestMessageKeys.TaskNotFound));
            EnsureActive(task);

            // İş kişinin üzerinde durur ama kota kararı bekleyen (veya reddedilmiş) bir kalem varsa
            // toplama başlayamaz — müdür "Kota Onayı" ekranından karar verene kadar.
            if (task.Lines.Any(x => x.RequestLine.QuotaDecision is KkdRequestLineQuotaDecision.Pending or KkdRequestLineQuotaDecision.Rejected))
                throw AppException.Conflict(Message(KkdRequestMessageKeys.QuotaDecisionPending));

            if (!task.AssignedUserId.HasValue)
            {
                // Havuz görevi: "Bu işi yapıyorum" aynı zamanda üzerine almadır, ilk basan kazanır.
                await EnsureWarehouseAccessAsync(actor, task.WarehouseId, token);
                task.AssignedUserId = actor;
                task.AssignedAtUtc = DateTimeOffset.UtcNow;
            }
            else if (task.AssignedUserId != actor)
            {
                throw AppException.Conflict(Message(KkdRequestMessageKeys.TaskAlreadyClaimed));
            }

            if (task.StartedAtUtc.HasValue)
                return (await MapRowsAsync([task], token))[0];

            var now = DateTimeOffset.UtcNow;
            foreach (var line in task.Lines.Where(x => x.RequestLine.StockId.HasValue))
            {
                var remaining = line.Quantity - line.PreparedQuantity;
                if (remaining <= 0) continue;
                await ReserveLineAsync(
                    task, line, line.RequestLine.StockId!.Value, line.RequestLine.StockCodeSnapshot ?? string.Empty,
                    remaining, request.IdempotencyKey, actor, now, token);
            }

            task.StartedAtUtc = now;
            if (task.Status == KkdPreparationTaskStatus.Assigned)
                task.Status = KkdPreparationTaskStatus.InPreparation;
            task.UpdatedBy = actor;
            task.UpdatedDate = now.UtcDateTime;
            await SaveAsync(token);
            await audit.WriteAsync(new AuditLogWriteEntry(
                "kkd.preparation-task.start", nameof(KkdPreparationTask), task.Id.ToString(), "Succeeded", "kkd-request",
                NewValues: new { task.StartedAtUtc, task.AssignedUserId },
                ChangedFields: ["StartedAtUtc", "AssignedUserId"]), token);
            return (await MapRowsAsync([task], token))[0];
        }, ct, IsolationLevel.Serializable);
    }

    /// <summary>StartAsync'in asıl rezervasyon işleminden önce çalışır: kota kararı henüz verilmemiş (None)
    /// ve stoğu bilinen satırların hakkını canlı olarak tekrar kontrol eder, aşımı varsa Pending işaretleyip
    /// kalıcı olarak kaydeder (kendi ayrı işlemi — StartAsync'in ana işlemi içinde yapılsaydı, o işlem
    /// Pending bulununca hata fırlatıp geri alındığında bu işaretleme de kaybolurdu).</summary>
    private async Task RefreshQuotaDecisionsBeforeStartAsync(long taskId, long actor, CancellationToken ct)
    {
        await uow.ExecuteInTransactionAsync<object?>(async token =>
        {
            var task = await Tasks.Query(true)
                .Include(x => x.Request)
                .Include(x => x.Lines).ThenInclude(x => x.RequestLine)
                .SingleOrDefaultAsync(x => x.Id == taskId, token);
            // Görev zaten başlamışsa (StartAsync'e idempotent bir yeniden çağrı) stok çoktan rezerve
            // edilmiş olur; bu noktada satırı sonradan Pending işaretlemek hiçbir şeyi geri almaz,
            // sadece daha önce sorunsuz tamamlanmış bir işi görünüşte bloklar. Bu yüzden kontrol sadece
            // henüz hiç başlamamış görevlerde, gerçek rezervasyondan önce çalışmalı.
            if (task is null || task.StartedAtUtc.HasValue) return null;

            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var now = DateTimeOffset.UtcNow;
            var changed = false;
            foreach (var line in task.Lines)
            {
                var requestLine = line.RequestLine;
                if (requestLine.StockId is not { } stockId || requestLine.QuotaDecision != KkdRequestLineQuotaDecision.None)
                    continue;
                var remaining = line.Quantity - line.PreparedQuantity;
                if (remaining <= 0) continue;
                var check = await entitlements.CheckAsync(new(task.Request.EmployeeId, stockId, remaining, today), token);
                if (check.IsAllowed) continue;
                requestLine.QuotaDecision = KkdRequestLineQuotaDecision.Pending;
                requestLine.UpdatedBy = actor;
                requestLine.UpdatedDate = now.UtcDateTime;
                changed = true;
            }
            if (changed) await SaveAsync(token);
            return null;
        }, ct, IsolationLevel.Serializable);
    }

    /// <summary>Bir görev satırı için hâlâ boş kalan raf/seri adaylarını listeler ("Rotayı güncelle").</summary>
    public async Task<KkdRouteCandidatesResult> GetRouteCandidatesAsync(long taskLineId, long actor, CancellationToken ct = default)
    {
        var line = await TaskLines.Query()
            .Include(x => x.Task)
            .Include(x => x.RequestLine)
            .Include(x => x.Locations)
            .SingleOrDefaultAsync(x => x.Id == taskLineId, ct)
            ?? throw AppException.NotFound(Message(KkdRequestMessageKeys.TaskNotFound));
        await EnsureWarehouseAccessAsync(actor, line.Task.WarehouseId, ct);
        if (!line.RequestLine.StockId.HasValue)
            throw AppException.Conflict(Message(KkdRequestMessageKeys.StockNotResolved));

        var resolved = await ResolveLineBalancesAsync(line.Task, line.RequestLine.StockId.Value, line.RequestLine.StockCodeSnapshot, ct);
        var excludeIds = line.Locations.Select(x => x.LocationId).ToHashSet();
        var isSerial = resolved.RequireSerial || resolved.BalanceCandidates.Any(x => !string.IsNullOrWhiteSpace(x.SerialNo));
        var candidates = resolved.BalanceCandidates
            .Where(x => !excludeIds.Contains(x.LocationId))
            .Select(x => new KkdRouteCandidateRow(x.LocationId, x.LocationCode, x.LocationName, x.AvailableQuantity, x.SerialNo, x.LotNo))
            .ToArray();
        return new(isSerial, candidates);
    }

    /// <summary>Checkbox seçimiyle raf/seri revizesi: eski rezervasyonu bırakır, seçileni yazar, yeniden rezerve eder.</summary>
    public async Task<KkdPreparationTaskRow> ApplyRouteSplitAsync(long taskLineId, KkdRouteSplitRequest request, long actor, CancellationToken ct = default)
    {
        ValidateKey(request.IdempotencyKey);
        if (request.Selections is null || request.Selections.Count == 0)
            throw AppException.BadRequest(Message(KkdRequestMessageKeys.NothingToAssign));

        return await uow.ExecuteInTransactionAsync(async token =>
        {
            var line = await TaskLines.Query(true)
                .Include(x => x.Task)
                .Include(x => x.RequestLine)
                .Include(x => x.Locations)
                .SingleOrDefaultAsync(x => x.Id == taskLineId, token)
                ?? throw AppException.NotFound(Message(KkdRequestMessageKeys.TaskNotFound));
            await EnsureWarehouseAccessAsync(actor, line.Task.WarehouseId, token);
            if (line.Task.AssignedUserId != actor)
                throw AppException.Forbidden(Message(KkdRequestMessageKeys.WarehouseAccessDenied));
            if (!line.RequestLine.StockId.HasValue)
                throw AppException.Conflict(Message(KkdRequestMessageKeys.StockNotResolved));
            CheckVersion(line.RowVersion, request.ExpectedTaskLineRowVersion);

            var stockId = line.RequestLine.StockId.Value;
            var now = DateTimeOffset.UtcNow;
            await ReleaseLineReservationsAsync(
                line.Task, line, stockId, line.RequestLine.UnitCode, $"{request.IdempotencyKey}:release", actor, now, token);
            // Henüz toplanmamış (PickedQuantity==0) raf satırlarını yeni seçimle değiştir; toplanmış olanlar kalır.
            foreach (var removable in line.Locations.Where(x => x.PickedQuantity <= 0).ToArray())
                TaskLineLocations.Remove(removable);

            var resolved = await ResolveLineBalancesAsync(line.Task, stockId, line.RequestLine.StockCodeSnapshot, token);
            var candidateByKey = resolved.BalanceCandidates
                .ToDictionary(x => (x.LocationId, SerialNo: x.SerialNo ?? string.Empty));

            var reservationLines = new List<StockReservationLineRequest>();
            foreach (var selection in request.Selections.Where(x => x.Quantity > 0))
            {
                var candidate = candidateByKey.GetValueOrDefault((selection.LocationId, selection.SerialNo ?? string.Empty))
                    ?? throw AppException.Conflict(Message(KkdRequestMessageKeys.InsufficientRouteBalance));
                if (candidate.AvailableQuantity < selection.Quantity)
                    throw AppException.Conflict(Message(KkdRequestMessageKeys.InsufficientRouteBalance));

                await TaskLineLocations.AddAsync(new KkdPreparationTaskLineLocation
                {
                    TaskLineId = line.Id,
                    LocationId = selection.LocationId,
                    ReservedQuantity = selection.Quantity,
                    SerialNo = selection.SerialNo,
                    LotNo = candidate.LotNo,
                    CreatedBy = actor,
                    CreatedDate = now.UtcDateTime,
                }, token);
                reservationLines.Add(new(
                    line.Id, line.Task.WarehouseId, selection.LocationId, stockId, null,
                    resolved.UnitCode, candidate.LotNo, selection.SerialNo, "Available", selection.Quantity));
            }
            if (reservationLines.Count > 0)
            {
                await balances.PostReservationAsync(new(
                    $"{request.IdempotencyKey}:reserve", "KkdPreparationTaskLine", line.Id, line.Task.TaskNo,
                    StockReservationOperationTypes.Reserve, "Rotayı güncelleme: yeni rezervasyon", reservationLines), token);
            }

            line.UpdatedBy = actor;
            line.UpdatedDate = now.UtcDateTime;
            await SaveAsync(token);
            await audit.WriteAsync(new AuditLogWriteEntry(
                "kkd.preparation-task.route-split", nameof(KkdPreparationTaskLine), line.Id.ToString(), "Succeeded", "kkd-request",
                NewValues: new { Selections = request.Selections }, ChangedFields: ["Locations"]), token);
            var loaded = await LoadTaskAsync(line.TaskId, token);
            return (await MapRowsAsync([loaded], token))[0];
        }, ct, IsolationLevel.Serializable);
    }

    /// <summary>Paylaşılan barkod çözücüyü StockId üzerinden çağırır — o stoğun/deponun o anki raf/seri bakiyelerini döner.</summary>
    private Task<ResolvedWarehouseBarcode> ResolveLineBalancesAsync(
        KkdPreparationTask task, long stockId, string? stockCodeSnapshot, CancellationToken ct) =>
        barcodeResolver.ResolveAsync(new(
            string.IsNullOrWhiteSpace(stockCodeSnapshot) ? $"#{stockId}" : stockCodeSnapshot,
            task.BranchCode, WarehouseBarcodePurpose.Outbound, task.WarehouseId, ExpectedStockId: stockId), ct);

    /// <summary>Bir görev satırı için raf(lar) üzerinden greedy dağıtım yapıp gerçek rezervasyon postalar.</summary>
    private async Task ReserveLineAsync(
        KkdPreparationTask task, KkdPreparationTaskLine line, long stockId, string stockCodeSnapshot,
        decimal remaining, Guid idempotencyKey, long actor, DateTimeOffset now, CancellationToken ct)
    {
        var resolved = await ResolveLineBalancesAsync(task, stockId, stockCodeSnapshot, ct);
        var isSerial = resolved.RequireSerial || resolved.BalanceCandidates.Any(x => !string.IsNullOrWhiteSpace(x.SerialNo));
        var chunks = isSerial
            ? KkdRouteAllocation.AllocateSerial((int)Math.Ceiling(remaining), resolved.BalanceCandidates)
            : KkdRouteAllocation.AllocateGreedy(remaining, resolved.BalanceCandidates);

        var reservationLines = new List<StockReservationLineRequest>();
        foreach (var chunk in chunks.Where(x => x.LocationId.HasValue))
        {
            await TaskLineLocations.AddAsync(new KkdPreparationTaskLineLocation
            {
                TaskLineId = line.Id,
                LocationId = chunk.LocationId!.Value,
                ReservedQuantity = chunk.Quantity,
                SerialNo = chunk.SerialNo,
                LotNo = chunk.LotNo,
                CreatedBy = actor,
                CreatedDate = now.UtcDateTime,
            }, ct);
            reservationLines.Add(new(
                line.Id, task.WarehouseId, chunk.LocationId.Value, stockId, null, resolved.UnitCode,
                chunk.LotNo, chunk.SerialNo, "Available", chunk.Quantity));
        }
        if (reservationLines.Count == 0) return;

        await balances.PostReservationAsync(new(
            $"{idempotencyKey}:line-{line.Id}", "KkdPreparationTaskLine", line.Id, task.TaskNo,
            StockReservationOperationTypes.Reserve, "KKD toplama rezervasyonu", reservationLines), ct);
    }

    /// <summary>Bir görev satırının hâlâ açık (tüketilmemiş) rezervasyonlarını serbest bırakır.</summary>
    private async Task ReleaseLineReservationsAsync(
        KkdPreparationTask task, KkdPreparationTaskLine line, long stockId, string unitCode,
        string idempotencyKey, long actor, DateTimeOffset now, CancellationToken ct)
    {
        var existing = line.Locations.Where(x => x.ReservedQuantity > 0).ToArray();
        if (existing.Length == 0) return;

        await balances.PostReservationAsync(new(
            idempotencyKey, "KkdPreparationTaskLine", line.Id, task.TaskNo,
            StockReservationOperationTypes.Release, "KKD rezervasyonu serbest bırakıldı",
            existing.Select(x => new StockReservationLineRequest(
                line.Id, task.WarehouseId, x.LocationId, stockId, null,
                unitCode, x.LotNo, x.SerialNo, "Available", -x.ReservedQuantity)).ToList()), ct);

        foreach (var loc in existing)
        {
            loc.ReservedQuantity = 0;
            loc.UpdatedBy = actor;
            loc.UpdatedDate = now.UtcDateTime;
        }
    }

    /// <summary>Açık olup aktif bir göreve bağlı olmayan kalemler ve kalan miktarları.</summary>
    private async Task<Dictionary<long, decimal>> UnassignedOpenLinesAsync(KkdRequest entity, CancellationToken ct)
    {
        var lineIds = entity.Lines.Select(x => x.Id).ToArray();
        var covered = await TaskLines.Query()
            .Where(x => lineIds.Contains(x.RequestLineId) && ActiveStatuses.Contains(x.Task.Status))
            .Select(x => x.RequestLineId)
            .Distinct()
            .ToHashSetAsync(ct);
        return entity.Lines
            .Where(x => x.Status is not (KkdRequestLineStatus.Cancelled or KkdRequestLineStatus.Completed) && !covered.Contains(x.Id))
            .Select(x => new { x.Id, Remaining = x.RequestedQuantity - x.DeliveredQuantity - x.CancelledQuantity })
            .Where(x => x.Remaining > 0)
            .ToDictionary(x => x.Id, x => x.Remaining);
    }

    private async Task<KkdPreparationTask> LoadTaskAsync(long id, CancellationToken ct) =>
        await Tasks.Query()
            .Include(x => x.Request)
            .Include(x => x.Lines).ThenInclude(x => x.RequestLine)
            .Include(x => x.Lines).ThenInclude(x => x.Locations)
            .Include(x => x.Distribution)
            .SingleAsync(x => x.Id == id, ct);

    private async Task<IReadOnlyList<KkdPreparationTaskRow>> MapRowsAsync(IReadOnlyList<KkdPreparationTask> tasks, CancellationToken ct)
    {
        if (tasks.Count == 0) return [];
        var userIds = tasks.Where(x => x.AssignedUserId.HasValue).Select(x => x.AssignedUserId!.Value)
            .Concat(tasks.Where(x => x.OriginUserId.HasValue).Select(x => x.OriginUserId!.Value))
            .Distinct().ToArray();
        var locationIds = tasks.SelectMany(x => x.Lines).SelectMany(x => x.Locations)
            .Select(x => x.LocationId).Distinct().ToArray();
        var locationsById = locationIds.Length == 0
            ? new Dictionary<long, (string Code, string Name)>()
            : await Locations.Query().Where(x => locationIds.Contains(x.Id))
                .Select(x => new { x.Id, x.Code, x.Name })
                .ToDictionaryAsync(x => x.Id, x => (x.Code, x.Name), ct);
        var usernames = await Users.Query().Where(x => userIds.Contains(x.Id))
            .Select(x => new { x.Id, DisplayName = x.Detail == null || (x.Detail.FirstName == "" && x.Detail.LastName == "")
                ? x.Username : (x.Detail.FirstName + " " + x.Detail.LastName).Trim() })
            .ToDictionaryAsync(x => x.Id, x => x.DisplayName, ct);
        var previousIds = tasks.Where(x => x.PreviousTaskId.HasValue).Select(x => x.PreviousTaskId!.Value).Distinct().ToArray();
        var previousNos = previousIds.Length == 0
            ? new Dictionary<long, string>()
            : await Tasks.Query().Where(x => previousIds.Contains(x.Id))
                .Select(x => new { x.Id, x.TaskNo })
                .ToDictionaryAsync(x => x.Id, x => x.TaskNo, ct);

        return tasks.Select(task => new KkdPreparationTaskRow(
            task.Id,
            task.TaskNo,
            task.RequestId,
            task.Request.RequestNo,
            task.Status.ToString(),
            task.AssignedUserId,
            task.AssignedUserId.HasValue
                ? usernames.GetValueOrDefault(task.AssignedUserId.Value, $"#{task.AssignedUserId}")
                : Message(KkdRequestMessageKeys.PoolLabel),
            task.WarehouseId,
            task.PreviousTaskId,
            task.PreviousTaskId.HasValue ? previousNos.GetValueOrDefault(task.PreviousTaskId.Value) : null,
            task.OriginUserId,
            task.OriginUserId.HasValue ? usernames.GetValueOrDefault(task.OriginUserId.Value) : null,
            task.DistributionId,
            task.Distribution?.WarehouseOutboundId,
            task.AssignedAtUtc,
            task.StartedAtUtc,
            task.CompletedAtUtc,
            task.ClosureReason,
            Convert.ToBase64String(task.RowVersion),
            task.Lines.OrderBy(x => x.RequestLine.LineNo).Select(line => new KkdPreparationTaskLineRow(
                line.Id,
                line.RequestLineId,
                line.RequestLine.LineNo,
                line.RequestLine.GroupCode,
                line.RequestLine.GroupName,
                line.RequestLine.StockId,
                line.RequestLine.StockCodeSnapshot,
                line.RequestLine.StockNameSnapshot,
                line.RequestLine.UnitCode,
                line.Quantity,
                line.PreparedQuantity,
                line.DeliveredQuantity,
                line.RequestLine.Status.ToString(),
                Convert.ToBase64String(line.RequestLine.RowVersion),
                line.RequestLine.QuotaDecision.ToString(),
                line.Locations.Where(x => !x.IsDeleted).Select(loc =>
                {
                    var (code, name) = locationsById.GetValueOrDefault(loc.LocationId, (string.Empty, string.Empty));
                    return new KkdPreparationTaskLineLocationRow(
                        loc.LocationId, code, name, loc.ReservedQuantity, loc.PickedQuantity, loc.SerialNo, loc.LotNo);
                }).ToArray())).ToArray())).ToArray();
    }

    private void EnsureActive(KkdPreparationTask task)
    {
        if (!ActiveStatuses.Contains(task.Status))
            throw AppException.Conflict(Message(KkdRequestMessageKeys.TaskNotActive));
    }

    /// <summary>Depo kısıtı olan (müdür olmayan) kullanıcılar yalnızca kendi depolarındaki havuz görevlerini üzerine alabilir.</summary>
    private async Task EnsureWarehouseAccessAsync(long actor, long warehouseId, CancellationToken ct)
    {
        var warehouseIds = await WarehouseAssignments.Query().Where(x => x.UserId == actor)
            .Select(x => x.WarehouseId).ToArrayAsync(ct);
        if (warehouseIds.Length > 0 && !warehouseIds.Contains(warehouseId))
            throw AppException.Forbidden(Message(KkdRequestMessageKeys.WarehouseAccessDenied));
    }

    private void ValidateKey(Guid key)
    {
        if (key == Guid.Empty) throw AppException.BadRequest(Message(KkdRequestMessageKeys.InvalidIdempotencyKey));
    }

    private void ValidateReason(string? reason)
    {
        if (reason?.Trim().Length is not (>= 3 and <= 1000))
            throw AppException.BadRequest(Message(KkdRequestMessageKeys.InvalidReason));
    }

    private void EnsureMutable(KkdRequest entity)
    {
        if (entity.Status is KkdRequestStatus.Completed or KkdRequestStatus.Cancelled)
            throw AppException.Conflict(Message(KkdRequestMessageKeys.ClosedRequestCannotChange));
    }

    private void CheckVersion(byte[] current, string? expected)
    {
        if (string.IsNullOrWhiteSpace(expected)) return;
        byte[] decoded;
        try { decoded = Convert.FromBase64String(expected); }
        catch { throw AppException.Conflict(Message(KkdRequestMessageKeys.ConcurrencyConflict)); }
        if (!current.SequenceEqual(decoded))
            throw AppException.Conflict(Message(KkdRequestMessageKeys.ConcurrencyConflict));
    }

    private async Task SaveAsync(CancellationToken ct)
    {
        try { await uow.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { throw AppException.Conflict(Message(KkdRequestMessageKeys.ConcurrencyConflict)); }
    }

    private string Message(string key, params object[] args) => localizer[key, args].Value;
}
