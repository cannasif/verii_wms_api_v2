using System.Data;
using Microsoft.EntityFrameworkCore;
using verii_wms_api_v2.Modules.Audit.Application;
using verii_wms_api_v2.Modules.WarehouseOutbound.Domain;
using verii_wms_api_v2.Modules.StockMovement.Application;
using verii_wms_api_v2.Modules.StockMovement.Domain;
using verii_wms_api_v2.Modules.WarehouseOperations.Domain;
using verii_wms_api_v2.Shared.Application.Abstractions.Persistence;
using verii_wms_api_v2.Shared.Application.Exceptions;
using verii_wms_api_v2.Shared.Application.Validation;

namespace verii_wms_api_v2.Modules.WarehouseOutbound.Application;

public sealed class WarehouseOutboundOperationService(
    IUnitOfWork uow,
    IStockMovementService movements,
    IWarehouseOutboundReservationService reservations,
    IAuditLogWriter audit,
    IEnumerable<IWarehouseOutboundShipmentFinalizationHandler> shipmentFinalizers) : IWarehouseOutboundOperationService
{
    public Task<WarehouseOutboundOperationResult> ApproveAsync(
        long id, WarehouseOutboundTransitionRequest request, long actor, CancellationToken ct = default) =>
        TransitionAsync(id, request, actor, "Approval", async (header, token) =>
        {
            if (!header.RequireApproval) throw AppException.Conflict("Bu sevk için onay gerekmiyor.");
            if (header.ApprovalStatus == OperationApprovalStatus.Rejected)
                throw AppException.Conflict("Reddedilmiş sevk onaylanamaz.");
            header.ApprovalStatus = OperationApprovalStatus.Approved;
            await Task.CompletedTask;
        }, ct);

    public Task<WarehouseOutboundOperationResult> ReleaseAsync(
        long id, WarehouseOutboundTransitionRequest request, long actor, CancellationToken ct = default) =>
        TransitionAsync(id, request, actor, "Release", async (header, token) =>
        {
            if (header.RequireApproval && header.ApprovalStatus != OperationApprovalStatus.Approved)
                throw AppException.Conflict("Sevk serbest bırakılmadan önce onaylanmalıdır.");
            if (header.Status != WarehouseOutboundStatus.Draft)
                throw AppException.Conflict("Yalnızca taslak sevk serbest bırakılabilir.");
            if (header.ReservationPolicy == WarehouseOutboundReservationPolicy.OnRelease)
                await reservations.ReserveAsync(header, $"WO:{header.Id}:RESERVE:RELEASE", actor, token);
            // Reservation posting performs intermediate saves inside the ambient transaction.
            // Change the aggregate state afterwards so the outer orchestration persists the
            // transition atomically with its history instead of accepting it too early.
            header.Status = WarehouseOutboundStatus.Released;
        }, ct);

    public Task<WarehouseOutboundOperationResult> PickAsync(
        long id, WarehouseOutboundOperationRequest request, long actor, CancellationToken ct = default) =>
        ExecuteMovementAsync(id, request, actor, WarehouseOutboundPhase.Pick, ct);

    public Task<WarehouseOutboundOperationResult> LoadAsync(
        long id, WarehouseOutboundOperationRequest request, long actor, CancellationToken ct = default) =>
        ExecuteMovementAsync(id, request, actor, WarehouseOutboundPhase.Load, ct);

    public async Task<WarehouseOutboundOperationResult> ShipAsync(
        long id, WarehouseOutboundOperationRequest request, long actor, CancellationToken ct = default)
    {
        var result = await ExecuteMovementAsync(id, request, actor, WarehouseOutboundPhase.Ship, ct);
        if (string.Equals(result.Status, WarehouseOutboundStatus.Shipped.ToString(), StringComparison.Ordinal))
        {
            foreach (var finalizer in shipmentFinalizers)
                await finalizer.OnShippedAsync(id, request.IdempotencyKey, actor, ct);
        }
        return result;
    }

    public Task<WarehouseOutboundOperationResult> CancelAsync(
        long id, WarehouseOutboundTransitionRequest request, long actor, CancellationToken ct = default)
    {
        if (id <= 0 || request.IdempotencyKey == Guid.Empty || string.IsNullOrWhiteSpace(request.Reason))
            throw AppException.BadRequest("Sevk, idempotency anahtarı ve iptal nedeni zorunludur.");
        return uow.ExecuteInTransactionAsync(async token =>
        {
            var header = await LoadAsync(id, token);
            if (await HasReplayAsync(id, request.IdempotencyKey, token)) return Result(header, null, true);
            if (header.Status == WarehouseOutboundStatus.Cancelled) throw AppException.Conflict("Sevk zaten iptal edilmiş.");
            if (header.ErpIntegrationStatus is ErpIntegrationStatus.Processing or ErpIntegrationStatus.Succeeded or ErpIntegrationStatus.CommitUncertain)
                throw AppException.Conflict("ERP aktarımı başlamış veya tamamlanmış sevk WMS üzerinden iptal edilemez.");

            var operationRepo = uow.Repository<StockMovementOperation>();
            var operations = await operationRepo.Query()
                .Where(x => x.ReferenceType == "WarehouseOutbound" && x.ReferenceId == id
                    && x.OperationType != StockMovementTypes.Reversal
                    && !operationRepo.Query().Any(r => r.ReversalOfOperationId == x.Id))
                .OrderByDescending(x => x.Id).Select(x => x.Id).ToListAsync(token);
            long? lastReversalId = null;
            foreach (var operationId in operations)
            {
                var reversal = await movements.ReverseAsync(operationId,
                    new($"WO:{id}:CANCEL:{request.IdempotencyKey:N}:{operationId}", request.Reason!.Trim(), DateTime.UtcNow), token);
                lastReversalId = reversal.OperationId;
            }
            await reservations.ReleaseAllAsync(header, $"WO:{id}:RESERVE:CANCEL:{request.IdempotencyKey:N}", request.Reason!.Trim(), actor, token);
            foreach (var line in header.Lines) line.Status = WarehouseOutboundLineStatus.Cancelled;
            foreach (var task in header.Tasks) task.Status = WarehouseOutboundTaskStatus.Cancelled;
            header.Status = WarehouseOutboundStatus.Cancelled;
            header.UpdatedBy = actor;
            header.UpdatedDate = DateTime.UtcNow;
            AddHistory(header, "Cancel", request.IdempotencyKey, request.Reason, actor);
            await uow.SaveChangesAsync(token);
            await WriteAudit(header, "cancel", lastReversalId, operations.Count, token);
            return Result(header, lastReversalId, false);
        }, ct, IsolationLevel.Serializable);
    }

    public Task<WarehouseOutboundOperationResult> PackAsync(
        long id, WarehouseOutboundOperationRequest request, long actor, CancellationToken ct = default)
    {
        ValidateRequest(id, request);
        return uow.ExecuteInTransactionAsync(async token =>
        {
            var header = await LoadAsync(id, token);
            var replay = await HasReplayAsync(id, request.IdempotencyKey, token);
            if (replay) return Result(header, null, true);
            if (header.Status is not (WarehouseOutboundStatus.Picked or WarehouseOutboundStatus.Packing or WarehouseOutboundStatus.Packed))
                throw AppException.Conflict($"Paketleme mevcut {header.Status} durumunda yapılamaz.");

            var map = request.Lines.ToDictionary(x => x.LineId);
            var lines = header.Lines.Where(x => map.ContainsKey(x.Id)).ToList();
            if (lines.Count != map.Count) throw AppException.BadRequest("Paketleme satırlarından biri bu sevke ait değil.");
            foreach (var line in lines)
            {
                var item = map[line.Id];
                if (item.Quantity <= 0 || item.Quantity > line.PickedQuantity - line.PackedQuantity)
                    throw AppException.Conflict($"{line.LineNo}. satır paketlenebilir miktarı aşıyor.");
                WarehouseOutboundOperationGuard.ValidateTrackingDimension(
                    header, line, item, WarehouseOutboundOperationPhase.Pack);
                if (line.RequireHandlingUnit && string.IsNullOrWhiteSpace(item.HandlingUnitNo))
                    throw AppException.BadRequest($"{line.LineNo}. satırda palet/kasa numarası zorunludur.");
                line.PackedQuantity += item.Quantity;
                line.Status = WarehouseOutboundLineStatus.Packed;
                ApplyTracking(line, item, WarehouseOutboundTrackingPhase.Pack, actor);
                line.UpdatedBy = actor;
                line.UpdatedDate = DateTime.UtcNow;
            }
            header.Status = header.Lines.All(x => x.PackedQuantity >= x.PickedQuantity)
                ? WarehouseOutboundStatus.Packed : WarehouseOutboundStatus.Packing;
            AddHistory(header, "Pack", request.IdempotencyKey, request.Reason, actor);
            header.UpdatedBy = actor;
            header.UpdatedDate = DateTime.UtcNow;
            await uow.SaveChangesAsync(token);
            await WriteAudit(header, "pack", null, request.Lines.Count, token);
            return Result(header, null, false);
        }, ct, IsolationLevel.Serializable);
    }

    private Task<WarehouseOutboundOperationResult> ExecuteMovementAsync(
        long id,
        WarehouseOutboundOperationRequest request,
        long actor,
        WarehouseOutboundPhase phase,
        CancellationToken ct)
    {
        ValidateRequest(id, request);
        return uow.ExecuteInTransactionAsync(async token =>
        {
            var header = await LoadAsync(id, token);
            var map = request.Lines.ToDictionary(x => x.LineId);
            var lines = header.Lines.Where(x => map.ContainsKey(x.Id)).ToList();
            if (lines.Count != map.Count) throw AppException.BadRequest("Operasyon satırlarından biri bu sevke ait değil.");

            var movementRequest = BuildMovementRequest(header, lines, map, request, phase);
            // Stok hazırlık rafına önceden taşınmışsa (KKD toplaması) kaynak ve hedef aynı raftır; bu adım
            // bakiyeyi değiştirmez. Sıfır etkili hareket postalamak yerine yalnızca belge durumu ilerletilir.
            var hasMovement = movementRequest.Lines.Count > 0;
            if (hasMovement)
            {
                if (await uow.Repository<StockMovementOperation>().AnyAsync(
                        x => x.IdempotencyKey == movementRequest.IdempotencyKey, token))
                {
                    var replay = await movements.PostAsync(movementRequest, token);
                    return Result(header, replay.OperationId, true);
                }
            }
            else if (await HasReplayAsync(id, request.IdempotencyKey, token))
            {
                return Result(header, null, true);
            }

            EnsurePhaseState(header, phase);
            if (phase == WarehouseOutboundPhase.Pick) EnsurePickerAssignment(header, actor);
            ValidateQuantities(header, lines, map, phase);
            ApplyWarehouseOutboundInfo(header, request, phase);
            if (phase == WarehouseOutboundPhase.Pick)
                await reservations.ConsumeAsync(header, map, $"WO:{header.Id}:RESERVE:PICK:{request.IdempotencyKey:N}", actor, token);

            long? movementId = hasMovement ? (await movements.PostAsync(movementRequest, token)).OperationId : null;

            foreach (var line in lines)
            {
                var quantity = map[line.Id].Quantity;
                switch (phase)
                {
                    case WarehouseOutboundPhase.Pick:
                        line.PickedQuantity += quantity;
                        line.Status = line.PickedQuantity >= line.RequestedQuantity
                            ? WarehouseOutboundLineStatus.Picked : WarehouseOutboundLineStatus.Picking;
                        UpdatePickTask(header, line, quantity);
                        break;
                    case WarehouseOutboundPhase.Load:
                        line.LoadedQuantity += quantity;
                        line.Status = WarehouseOutboundLineStatus.Loaded;
                        break;
                    case WarehouseOutboundPhase.Ship:
                        line.ShippedQuantity += quantity;
                        line.Status = WarehouseOutboundLineStatus.Shipped;
                        break;
                }
                ApplyTracking(line, map[line.Id], phase switch
                {
                    WarehouseOutboundPhase.Pick => WarehouseOutboundTrackingPhase.Pick,
                    WarehouseOutboundPhase.Load => WarehouseOutboundTrackingPhase.Load,
                    WarehouseOutboundPhase.Ship => WarehouseOutboundTrackingPhase.Ship,
                    _ => throw new ArgumentOutOfRangeException(nameof(phase))
                }, actor);
                line.UpdatedBy = actor;
                line.UpdatedDate = DateTime.UtcNow;
            }

            UpdateHeaderStatus(header, phase);
            AddHistory(header, phase.ToString(), request.IdempotencyKey, request.Reason, actor);
            header.UpdatedBy = actor;
            header.UpdatedDate = DateTime.UtcNow;
            await uow.SaveChangesAsync(token);
            await WriteAudit(header, phase.ToString().ToLowerInvariant(), movementId, request.Lines.Count, token);
            return Result(header, movementId, false);
        }, ct, IsolationLevel.Serializable);
    }

    private Task<WarehouseOutboundOperationResult> TransitionAsync(
        long id,
        WarehouseOutboundTransitionRequest request,
        long actor,
        string transition,
        Func<WarehouseOutboundHeader, CancellationToken, Task> mutate,
        CancellationToken ct)
    {
        if (id <= 0 || request.IdempotencyKey == Guid.Empty)
            throw AppException.BadRequest("Geçerli sevk ve idempotency anahtarı zorunludur.");
        return uow.ExecuteInTransactionAsync(async token =>
        {
            var header = await LoadAsync(id, token);
            if (await HasReplayAsync(id, request.IdempotencyKey, token)) return Result(header, null, true);
            if (header.Status == WarehouseOutboundStatus.Cancelled)
                throw AppException.Conflict("İptal edilmiş sevk üzerinde işlem yapılamaz.");
            await mutate(header, token);
            AddHistory(header, transition, request.IdempotencyKey, request.Reason, actor);
            header.UpdatedBy = actor;
            header.UpdatedDate = DateTime.UtcNow;
            await uow.SaveChangesAsync(token);
            await WriteAudit(header, transition.ToLowerInvariant(), null, 0, token);
            return Result(header, null, false);
        }, ct, IsolationLevel.Serializable);
    }

    private async Task<WarehouseOutboundHeader> LoadAsync(long id, CancellationToken ct) =>
        await uow.Repository<WarehouseOutboundHeader>().Query(tracking: true)
            .Include(x => x.Lines).ThenInclude(x => x.Trackings)
            .Include(x => x.Tasks).ThenInclude(x => x.Lines)
            .Include(x => x.Tasks).ThenInclude(x => x.Assignments)
            .FirstOrDefaultAsync(x => x.Id == id, ct)
        ?? throw AppException.NotFound("Sevk kaydı bulunamadı.");

    private async Task<bool> HasReplayAsync(long id, Guid key, CancellationToken ct) =>
        await uow.Repository<WarehouseOutboundStatusHistory>().Query()
            .AnyAsync(x => x.WarehouseOutboundHeaderId == id && x.CorrelationId == key, ct);

    private static void EnsurePhaseState(WarehouseOutboundHeader header, WarehouseOutboundPhase phase)
    {
        if (header.Status == WarehouseOutboundStatus.Cancelled)
            throw AppException.Conflict("İptal edilmiş sevk üzerinde işlem yapılamaz.");
        var allowed = phase switch
        {
            WarehouseOutboundPhase.Pick => header.Status is WarehouseOutboundStatus.Released or WarehouseOutboundStatus.Picking,
            WarehouseOutboundPhase.Load => header.Status is WarehouseOutboundStatus.Picked or WarehouseOutboundStatus.Packed
                or WarehouseOutboundStatus.Loading or WarehouseOutboundStatus.Loaded,
            WarehouseOutboundPhase.Ship => header.Status is WarehouseOutboundStatus.Picked or WarehouseOutboundStatus.Packed
                or WarehouseOutboundStatus.Loaded or WarehouseOutboundStatus.AwaitingApproval,
            _ => false
        };
        if (!allowed) throw AppException.Conflict($"{phase} işlemi mevcut {header.Status} durumunda yapılamaz.");
    }

    private static void EnsurePickerAssignment(WarehouseOutboundHeader header, long actor)
    {
        if (!header.RequireAssignee) return;
        var task = header.Tasks.FirstOrDefault(x => x.TaskType == WarehouseOutboundTaskType.Pick)
            ?? throw AppException.Conflict("Sevk toplama emri bulunamadı.");
        var assignment = task.Assignments.FirstOrDefault(x => x.UserId == actor)
            ?? throw AppException.Forbidden("Bu sevk toplama emri size atanmamış.");
        assignment.AcceptedAtUtc ??= DateTimeOffset.UtcNow;
        task.Status = WarehouseOutboundTaskStatus.InProgress;
    }

    private static void ValidateQuantities(
        WarehouseOutboundHeader header,
        IReadOnlyCollection<WarehouseOutboundLine> lines,
        IReadOnlyDictionary<long, WarehouseOutboundOperationLineRequest> requests,
        WarehouseOutboundPhase phase)
    {
        foreach (var line in lines)
        {
            var item = requests[line.Id];
            if (item.Quantity <= 0) throw AppException.BadRequest("Operasyon miktarı sıfırdan büyük olmalıdır.");
            var available = phase switch
            {
                WarehouseOutboundPhase.Pick => line.RequestedQuantity - line.PickedQuantity,
                WarehouseOutboundPhase.Load => (header.PackingPolicy == WarehouseOutboundPackingPolicy.Required
                    ? line.PackedQuantity : line.PickedQuantity) - line.LoadedQuantity,
                WarehouseOutboundPhase.Ship => (header.RequireLoadingConfirmation
                    ? line.LoadedQuantity
                    : header.PackingPolicy == WarehouseOutboundPackingPolicy.Required ? line.PackedQuantity : line.PickedQuantity)
                    - line.ShippedQuantity,
                _ => 0
            };
            if (item.Quantity > available)
                throw AppException.Conflict($"{line.LineNo}. satırda kullanılabilir miktar {available}, istenen {item.Quantity}.");
            WarehouseOutboundOperationGuard.ValidateTrackingDimension(
                header,
                line,
                item,
                phase switch
                {
                    WarehouseOutboundPhase.Pick => WarehouseOutboundOperationPhase.Pick,
                    WarehouseOutboundPhase.Load => WarehouseOutboundOperationPhase.Load,
                    WarehouseOutboundPhase.Ship => WarehouseOutboundOperationPhase.Ship,
                    _ => throw new ArgumentOutOfRangeException(nameof(phase))
                });
            if (line.TrackingType is StockTrackingType.Serial or StockTrackingType.LotAndSerial
                && !string.IsNullOrWhiteSpace(item.SerialNo) && item.Quantity != 1)
                throw AppException.BadRequest("Serili stok operasyonunda miktar 1 olmalıdır.");
        }
        if (phase != WarehouseOutboundPhase.Ship) return;
        var after = header.Lines.Sum(x => x.ShippedQuantity) + requests.Values.Sum(x => x.Quantity);
        var requested = header.Lines.Sum(x => x.RequestedQuantity);
        var percent = requested == 0 ? 0 : after * 100m / requested;
        if (percent < header.MinimumFulfillmentPercent)
            throw AppException.Conflict($"Minimum sevk karşılama oranı %{header.MinimumFulfillmentPercent}; mevcut oran %{percent:N2}.");
        if (!header.AllowPartialWarehouseOutbound && after < requested)
            throw AppException.Conflict("Sevk politikası kısmi sevke izin vermiyor.");
    }

    private static PostStockMovementRequest BuildMovementRequest(
        WarehouseOutboundHeader header,
        IReadOnlyCollection<WarehouseOutboundLine> lines,
        IReadOnlyDictionary<long, WarehouseOutboundOperationLineRequest> requests,
        WarehouseOutboundOperationRequest request,
        WarehouseOutboundPhase phase)
    {
        var movementType = phase == WarehouseOutboundPhase.Ship ? StockMovementTypes.Shipment : StockMovementTypes.Transfer;
        var rows = lines.Select(line =>
        {
            var item = requests[line.Id];
            var source = phase switch
            {
                WarehouseOutboundPhase.Pick => item.SourceLocationId ?? line.DefaultSourceLocationId,
                WarehouseOutboundPhase.Load => item.SourceLocationId ?? header.StagingLocationId,
                WarehouseOutboundPhase.Ship => item.SourceLocationId ?? (header.RequireLoadingConfirmation
                    ? header.LoadingLocationId : header.StagingLocationId ?? line.DefaultSourceLocationId),
                _ => null
            };
            var target = phase switch
            {
                WarehouseOutboundPhase.Pick => item.TargetLocationId ?? header.StagingLocationId,
                WarehouseOutboundPhase.Load => item.TargetLocationId ?? header.LoadingLocationId,
                _ => null
            };
            return new StockMovementLineRequest(
                line.StockId, line.YapCodeId, item.Quantity,
                header.SourceWarehouseId, source,
                phase == WarehouseOutboundPhase.Ship ? null : header.SourceWarehouseId,
                target, line.UnitCode, item.LotNo, item.SerialNo, "Available");
        }).Where(row => !IsSameLocationTransfer(row)).ToList();
        return new(
            $"WO:{header.Id}:{phase}:{request.IdempotencyKey:N}",
            movementType,
            "WarehouseOutbound",
            header.DocumentNo,
            header.Id,
            request.OccurredAtUtc?.UtcDateTime,
            Clean(request.Reason, 500),
            $"{phase} operation for {header.DocumentNo}",
            rows);
    }

    /// <summary>Aynı depo ve aynı raf arasındaki transfer bakiyeyi değiştirmez; hareket kaydı üretilmez.</summary>
    internal static bool IsSameLocationTransfer(StockMovementLineRequest row) =>
        row.SourceLocationId.HasValue
        && row.TargetLocationId.HasValue
        && row.SourceLocationId == row.TargetLocationId
        && row.SourceWarehouseId == row.TargetWarehouseId;

    private static void ApplyWarehouseOutboundInfo(WarehouseOutboundHeader header, WarehouseOutboundOperationRequest request, WarehouseOutboundPhase phase)
    {
        if (phase != WarehouseOutboundPhase.Ship) return;
        header.VehiclePlate = Clean(request.VehiclePlate, 20) ?? header.VehiclePlate;
        header.DriverName = Clean(request.DriverName, 200) ?? header.DriverName;
        header.WaybillNo = PurchaseWaybillNumberPolicy.Normalize(request.WaybillNo) ?? header.WaybillNo;
        header.TrackingNo = Clean(request.TrackingNo, 100) ?? header.TrackingNo;
        if (header.RequireWarehouseOutboundInformation
            && string.IsNullOrWhiteSpace(header.VehiclePlate)
            && string.IsNullOrWhiteSpace(header.CarrierCode))
            throw AppException.BadRequest("Sevk için araç plakası veya taşıyıcı bilgisi zorunludur.");
    }

    private static void UpdatePickTask(WarehouseOutboundHeader header, WarehouseOutboundLine line, decimal quantity)
    {
        var task = header.Tasks.FirstOrDefault(x => x.TaskType == WarehouseOutboundTaskType.Pick);
        var taskLine = task?.Lines.FirstOrDefault(x => x.WarehouseOutboundLineId == line.Id);
        if (taskLine is null) return;
        taskLine.ProcessedQuantity += quantity;
        task!.Status = task.Lines.All(x => x.ProcessedQuantity >= x.PlannedQuantity)
            ? WarehouseOutboundTaskStatus.Completed : WarehouseOutboundTaskStatus.InProgress;
    }

    private static void UpdateHeaderStatus(WarehouseOutboundHeader header, WarehouseOutboundPhase phase)
    {
        header.Status = phase switch
        {
            WarehouseOutboundPhase.Pick when header.Lines.All(x => x.PickedQuantity >= x.RequestedQuantity) => WarehouseOutboundStatus.Picked,
            WarehouseOutboundPhase.Pick => WarehouseOutboundStatus.Picking,
            WarehouseOutboundPhase.Load when header.Lines.All(x => x.LoadedQuantity >=
                (header.PackingPolicy == WarehouseOutboundPackingPolicy.Required ? x.PackedQuantity : x.PickedQuantity)) => WarehouseOutboundStatus.Loaded,
            WarehouseOutboundPhase.Load => WarehouseOutboundStatus.Loading,
            WarehouseOutboundPhase.Ship => WarehouseOutboundStatus.Shipped,
            _ => header.Status
        };
        if (phase == WarehouseOutboundPhase.Ship)
        {
            header.ShippedAtUtc ??= DateTimeOffset.UtcNow;
            header.ErpIntegrationStatus = header.AutoPostErpAfterApproval
                ? ErpIntegrationStatus.Processing : ErpIntegrationStatus.Pending;
        }
    }

    private static void AddHistory(
        WarehouseOutboundHeader header, string status, Guid key, string? reason, long actor) =>
        header.StatusHistory.Add(new()
        {
            BranchCode = header.BranchCode,
            CreatedBy = actor,
            CreatedDate = DateTime.UtcNow,
            Header = header,
            FromStatus = header.Status.ToString(),
            ToStatus = status,
            Description = Clean(reason, 1000),
            ChangedAtUtc = DateTimeOffset.UtcNow,
            ChangedBy = actor,
            CorrelationId = key
        });

    private Task WriteAudit(WarehouseOutboundHeader header, string operation, long? movementId, int lineCount, CancellationToken ct) =>
        audit.WriteAsync(new(
            $"warehouse-outbound.{operation}",
            nameof(WarehouseOutboundHeader),
            header.Id.ToString(),
            "Succeeded",
            "warehouse-outbound",
            NewValues: new { header.DocumentNo, Operation = operation, MovementId = movementId, LineCount = lineCount },
            ChangedFields: ["Status", "Quantities", "StockMovement"]), ct);

    private static WarehouseOutboundOperationResult Result(WarehouseOutboundHeader header, long? movementId, bool replayed) =>
        new(
            header.Id,
            header.DocumentNo,
            header.Status.ToString(),
            movementId,
            header.Lines.Sum(x => x.PickedQuantity),
            header.Lines.Sum(x => x.PackedQuantity),
            header.Lines.Sum(x => x.LoadedQuantity),
            header.Lines.Sum(x => x.ShippedQuantity),
            replayed);

    private static void ValidateRequest(long id, WarehouseOutboundOperationRequest request)
    {
        if (id <= 0 || request.IdempotencyKey == Guid.Empty || request.Lines.Count == 0)
            throw AppException.BadRequest("Sevk, idempotency anahtarı ve operasyon satırları zorunludur.");
        if (request.Lines.GroupBy(x => x.LineId).Any(x => x.Count() > 1))
            throw AppException.BadRequest("Aynı sevk satırı bir istekte tekrar edemez.");
    }

    private static string? Clean(string? value, int max)
    {
        var text = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        return text?.Length > max ? text[..max] : text;
    }

    private static void ApplyTracking(
        WarehouseOutboundLine line,
        WarehouseOutboundOperationLineRequest request,
        WarehouseOutboundTrackingPhase phase,
        long actor)
    {
        var handlingUnitNo = Clean(request.HandlingUnitNo, 100);
        var lotNo = Clean(request.LotNo, 100);
        var serialNo = Clean(request.SerialNo, 200);

        // Lot/seri/palet boş olsa bile, taslakta planlanmış “boş boyut” takip kaydı (ör. KKD
        // StockAlreadyStaged) güncellenmelidir; aksi halde Pick satır miktarını artırır, Ship ise
        // takip.PickedQuantity=0 görüp “kullanılabilir miktar 0” hatası verir.
        var tracking = line.Trackings.FirstOrDefault(x =>
            Equal(x.HandlingUnitNo, handlingUnitNo)
            && Equal(x.LotNo, lotNo)
            && Equal(x.SerialNo, serialNo));
        if (tracking is null)
        {
            if (handlingUnitNo is null && lotNo is null && serialNo is null)
                return;

            tracking = new WarehouseOutboundTracking
            {
                WarehouseOutboundLineId = line.Id,
                HandlingUnitNo = handlingUnitNo,
                LotNo = lotNo,
                SerialNo = serialNo,
                PlannedQuantity = request.Quantity,
                PickedQuantity = phase is WarehouseOutboundTrackingPhase.Pack or WarehouseOutboundTrackingPhase.Load or WarehouseOutboundTrackingPhase.Ship
                    ? request.Quantity : 0,
                PackedQuantity = phase is WarehouseOutboundTrackingPhase.Load or WarehouseOutboundTrackingPhase.Ship
                    ? request.Quantity : 0,
                LoadedQuantity = phase is WarehouseOutboundTrackingPhase.Ship ? request.Quantity : 0,
                SourceLocationId = request.SourceLocationId,
                CreatedBy = actor,
                CreatedDate = DateTime.UtcNow
            };
            line.Trackings.Add(tracking);
        }
        tracking.PlannedQuantity = Math.Max(tracking.PlannedQuantity, request.Quantity);

        switch (phase)
        {
            case WarehouseOutboundTrackingPhase.Pick:
                tracking.PickedQuantity += request.Quantity;
                break;
            case WarehouseOutboundTrackingPhase.Pack:
                tracking.PackedQuantity += request.Quantity;
                break;
            case WarehouseOutboundTrackingPhase.Load:
                tracking.LoadedQuantity += request.Quantity;
                break;
            case WarehouseOutboundTrackingPhase.Ship:
                tracking.ShippedQuantity += request.Quantity;
                break;
        }
        tracking.SourceLocationId ??= request.SourceLocationId;
        tracking.UpdatedBy = actor;
        tracking.UpdatedDate = DateTime.UtcNow;
    }

    private static bool Equal(string? left, string? right) =>
        string.Equals(left?.Trim(), right?.Trim(), StringComparison.OrdinalIgnoreCase);

    private enum WarehouseOutboundPhase { Pick, Load, Ship }
    private enum WarehouseOutboundTrackingPhase { Pick, Pack, Load, Ship }
}
