using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using verii_wms_api_v2.Modules.Audit.Application;
using verii_wms_api_v2.Modules.Identity.Domain;
using verii_wms_api_v2.Modules.Kkd.Domain;
using verii_wms_api_v2.Modules.Kkd.Localization;
using verii_wms_api_v2.Shared.Application.Abstractions.Persistence;
using verii_wms_api_v2.Shared.Application.Exceptions;
using WarehouseEntity = verii_wms_api_v2.Modules.Warehouse.Domain.Warehouse;

namespace verii_wms_api_v2.Modules.Kkd.Application;

/// <summary>
/// KKD hazırlama görevleri: üretim transfer görev modelinin KKD karşılığı.
/// Kalem bazlı atama, üzerine alma, kalan işi devretme ve işi havuza iade akışlarını yönetir.
/// </summary>
public sealed class KkdPreparationTaskService(
    IUnitOfWork uow,
    IAuditLogWriter audit,
    IStringLocalizer<KkdRequestResource> localizer) : IKkdPreparationTaskService
{
    private IGenericRepository<KkdPreparationTask> Tasks => uow.Repository<KkdPreparationTask>();
    private IGenericRepository<KkdPreparationTaskLine> TaskLines => uow.Repository<KkdPreparationTaskLine>();
    private IGenericRepository<KkdRequest> Requests => uow.Repository<KkdRequest>();
    private IGenericRepository<User> Users => uow.Repository<User>();
    private IGenericRepository<WarehouseEntity> Warehouses => uow.Repository<WarehouseEntity>();
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
            // Üretimdeki kural: kayıt için tüm açık kalemler atanmış olmalı (kişiye veya havuza).
            if (requested.Length != unassigned.Count)
                throw AppException.BadRequest(Message(KkdRequestMessageKeys.AllLinesMustBeAssigned));

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

            var now = DateTimeOffset.UtcNow;
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
                Lines = unassigned.Select(pair => new KkdPreparationTaskLine
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
                .SingleOrDefaultAsync(x => x.CorrelationId == request.IdempotencyKey, token);
            if (existing is not null) return (await MapRowsAsync([existing], token))[0];

            var task = await Tasks.Query(true)
                .Include(x => x.Request)
                .Include(x => x.Lines).ThenInclude(x => x.RequestLine)
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

    public async Task ReturnAsync(long taskId, KkdPreparationReturnRequest request, long actor, CancellationToken ct = default)
    {
        ValidateKey(request.IdempotencyKey);
        ValidateReason(request.Reason);
        await uow.ExecuteInTransactionAsync<object?>(async token =>
        {
            var task = await Tasks.Query(true)
                .Include(x => x.Request)
                .Include(x => x.Lines)
                .SingleOrDefaultAsync(x => x.Id == taskId, token)
                ?? throw AppException.NotFound(Message(KkdRequestMessageKeys.TaskNotFound));
            if (task.Status == KkdPreparationTaskStatus.Returned) return null;
            EnsureActive(task);
            if (!task.AssignedUserId.HasValue) throw AppException.Conflict(Message(KkdRequestMessageKeys.TaskNotPooled));
            CheckVersion(task.RowVersion, request.ExpectedRowVersion);
            if (task.Lines.Any(x => x.PreparedQuantity > 0 || x.DeliveredQuantity > 0))
                throw AppException.Conflict(Message(KkdRequestMessageKeys.TaskHasProgress));

            var now = DateTimeOffset.UtcNow;
            task.Status = KkdPreparationTaskStatus.Returned;
            task.ClosedAtUtc = now;
            task.ClosureReason = request.Reason.Trim();
            task.UpdatedBy = actor;
            task.UpdatedDate = now.UtcDateTime;
            await SaveAsync(token);
            await audit.WriteAsync(new AuditLogWriteEntry(
                "kkd.preparation-task.return", nameof(KkdPreparationTask), task.Id.ToString(), "Succeeded", "kkd-request",
                Reason: request.Reason.Trim(),
                NewValues: new { task.Status },
                ChangedFields: ["Status", "ClosedAtUtc", "ClosureReason"]), token);
            return null;
        }, ct, IsolationLevel.Serializable);
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
            .Include(x => x.Distribution)
            .SingleAsync(x => x.Id == id, ct);

    private async Task<IReadOnlyList<KkdPreparationTaskRow>> MapRowsAsync(IReadOnlyList<KkdPreparationTask> tasks, CancellationToken ct)
    {
        if (tasks.Count == 0) return [];
        var userIds = tasks.Where(x => x.AssignedUserId.HasValue).Select(x => x.AssignedUserId!.Value)
            .Concat(tasks.Where(x => x.OriginUserId.HasValue).Select(x => x.OriginUserId!.Value))
            .Distinct().ToArray();
        var usernames = await Users.Query().Where(x => userIds.Contains(x.Id))
            .Select(x => new { x.Id, x.Username })
            .ToDictionaryAsync(x => x.Id, x => x.Username, ct);
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
                Convert.ToBase64String(line.RequestLine.RowVersion))).ToArray())).ToArray();
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
