using System.Data;
using Microsoft.EntityFrameworkCore;
using verii_wms_api_v2.Modules.Audit.Application;
using verii_wms_api_v2.Modules.Shipping.Domain;
using verii_wms_api_v2.Modules.StockMovement.Application;
using verii_wms_api_v2.Modules.StockMovement.Domain;
using verii_wms_api_v2.Modules.WarehouseOperations.Domain;
using verii_wms_api_v2.Shared.Application.Abstractions.Persistence;
using verii_wms_api_v2.Shared.Application.Exceptions;
using verii_wms_api_v2.Shared.Application.Validation;

namespace verii_wms_api_v2.Modules.Shipping.Application;

public sealed class ShippingOperationService(
    IUnitOfWork uow,
    IStockMovementService movements,
    IShipmentReservationService reservations,
    IAuditLogWriter audit) : IShippingOperationService
{
    public Task<ShipmentOperationResult> ApproveAsync(
        long id, ShipmentTransitionRequest request, long actor, CancellationToken ct = default) =>
        TransitionAsync(id, request, actor, "Approval", async (header, token) =>
        {
            if (!header.RequireApproval) throw AppException.Conflict("Bu sevk için onay gerekmiyor.");
            if (header.ApprovalStatus == OperationApprovalStatus.Rejected)
                throw AppException.Conflict("Reddedilmiş sevk onaylanamaz.");
            header.ApprovalStatus = OperationApprovalStatus.Approved;
            await Task.CompletedTask;
        }, ct);

    public Task<ShipmentOperationResult> ReleaseAsync(
        long id, ShipmentTransitionRequest request, long actor, CancellationToken ct = default) =>
        TransitionAsync(id, request, actor, "Release", async (header, token) =>
        {
            if (header.RequireApproval && header.ApprovalStatus != OperationApprovalStatus.Approved)
                throw AppException.Conflict("Sevk serbest bırakılmadan önce onaylanmalıdır.");
            if (header.Status != ShipmentStatus.Draft)
                throw AppException.Conflict("Yalnızca taslak sevk serbest bırakılabilir.");
            header.Status = ShipmentStatus.Released;
            if (header.ReservationPolicy == ShipmentReservationPolicy.OnRelease)
                await reservations.ReserveAsync(header, $"SH:{header.Id}:RESERVE:RELEASE", actor, token);
        }, ct);

    public Task<ShipmentOperationResult> PickAsync(
        long id, ShipmentOperationRequest request, long actor, CancellationToken ct = default) =>
        ExecuteMovementAsync(id, request, actor, ShipmentPhase.Pick, ct);

    public Task<ShipmentOperationResult> LoadAsync(
        long id, ShipmentOperationRequest request, long actor, CancellationToken ct = default) =>
        ExecuteMovementAsync(id, request, actor, ShipmentPhase.Load, ct);

    public Task<ShipmentOperationResult> ShipAsync(
        long id, ShipmentOperationRequest request, long actor, CancellationToken ct = default) =>
        ExecuteMovementAsync(id, request, actor, ShipmentPhase.Ship, ct);

    public Task<ShipmentOperationResult> CancelAsync(
        long id, ShipmentTransitionRequest request, long actor, CancellationToken ct = default) =>
        CancelCoreAsync(id, request, actor, false, ct);

    public Task<ShipmentOperationResult> CancelAfterErpDeletionAsync(
        long id, ShipmentTransitionRequest request, long actor, CancellationToken ct = default) =>
        CancelCoreAsync(id, request, actor, true, ct);

    private Task<ShipmentOperationResult> CancelCoreAsync(
        long id,
        ShipmentTransitionRequest request,
        long actor,
        bool erpDeletionConfirmed,
        CancellationToken ct)
    {
        if (id <= 0 || request.IdempotencyKey == Guid.Empty || string.IsNullOrWhiteSpace(request.Reason))
            throw AppException.BadRequest("Sevk, idempotency anahtarı ve iptal nedeni zorunludur.");
        return uow.ExecuteInTransactionAsync(async token =>
        {
            var header = await LoadAsync(id, token);
            if (await HasReplayAsync(id, request.IdempotencyKey, token)) return Result(header, null, true);
            if (header.Status == ShipmentStatus.Cancelled) throw AppException.Conflict("Sevk zaten iptal edilmiş.");
            if (!erpDeletionConfirmed
                && header.ErpIntegrationStatus is ErpIntegrationStatus.Processing
                    or ErpIntegrationStatus.Succeeded
                    or ErpIntegrationStatus.CommitUncertain
                    or ErpIntegrationStatus.Cancelled)
                throw AppException.Conflict("ERP aktarımı başlamış veya tamamlanmış sevk WMS üzerinden iptal edilemez.");
            if (erpDeletionConfirmed
                && header.ErpIntegrationStatus is not (ErpIntegrationStatus.Succeeded or ErpIntegrationStatus.Cancelled))
                throw AppException.Conflict("Sevk ERP silme doğrulamasıyla uyumlu durumda değil.");

            var operationRepo = uow.Repository<StockMovementOperation>();
            var operations = await operationRepo.Query()
                .Where(x => x.ReferenceType == "Shipment" && x.ReferenceId == id
                    && x.OperationType != StockMovementTypes.Reversal
                    && !operationRepo.Query().Any(r => r.ReversalOfOperationId == x.Id))
                .OrderByDescending(x => x.Id).Select(x => x.Id).ToListAsync(token);
            long? lastReversalId = null;
            foreach (var operationId in operations)
            {
                var reversal = await movements.ReverseAsync(operationId,
                    new($"SH:{id}:CANCEL:{request.IdempotencyKey:N}:{operationId}", request.Reason!.Trim(), DateTime.UtcNow), token);
                lastReversalId = reversal.OperationId;
            }
            await reservations.ReleaseAllAsync(header, $"SH:{id}:RESERVE:CANCEL:{request.IdempotencyKey:N}", request.Reason!.Trim(), actor, token);
            foreach (var line in header.Lines) line.Status = ShipmentLineStatus.Cancelled;
            foreach (var task in header.Tasks) task.Status = ShipmentTaskStatus.Cancelled;
            header.Status = ShipmentStatus.Cancelled;
            if (erpDeletionConfirmed) header.ErpIntegrationStatus = ErpIntegrationStatus.Cancelled;
            header.UpdatedBy = actor;
            header.UpdatedDate = DateTime.UtcNow;
            AddHistory(header, "Cancel", request.IdempotencyKey, request.Reason, actor);
            await uow.SaveChangesAsync(token);
            await WriteAudit(header, "cancel", lastReversalId, operations.Count, token);
            return Result(header, lastReversalId, false);
        }, ct, IsolationLevel.Serializable);
    }

    public Task<ShipmentOperationResult> PackAsync(
        long id, ShipmentOperationRequest request, long actor, CancellationToken ct = default)
    {
        ValidateRequest(id, request);
        return uow.ExecuteInTransactionAsync(async token =>
        {
            var header = await LoadAsync(id, token);
            var replay = await HasReplayAsync(id, request.IdempotencyKey, token);
            if (replay) return Result(header, null, true);
            if (header.Status is not (ShipmentStatus.Picked or ShipmentStatus.Packing or ShipmentStatus.Packed))
                throw AppException.Conflict($"Paketleme mevcut {header.Status} durumunda yapılamaz.");

            var map = request.Lines.ToDictionary(x => x.LineId);
            var lines = header.Lines.Where(x => map.ContainsKey(x.Id)).ToList();
            if (lines.Count != map.Count) throw AppException.BadRequest("Paketleme satırlarından biri bu sevke ait değil.");
            foreach (var line in lines)
            {
                var item = map[line.Id];
                if (item.Quantity <= 0 || item.Quantity > line.PickedQuantity - line.PackedQuantity)
                    throw AppException.Conflict($"{line.LineNo}. satır paketlenebilir miktarı aşıyor.");
                if (line.RequireHandlingUnit && string.IsNullOrWhiteSpace(item.HandlingUnitNo))
                    throw AppException.BadRequest($"{line.LineNo}. satırda palet/kasa numarası zorunludur.");
                ValidateTrackingDimension(header, line, item, ShipmentTrackingPhase.Pack);
                line.PackedQuantity += item.Quantity;
                line.Status = ShipmentLineStatus.Packed;
                ApplyTracking(line, item, ShipmentTrackingPhase.Pack, actor);
                line.UpdatedBy = actor;
                line.UpdatedDate = DateTime.UtcNow;
            }
            header.Status = header.Lines.All(x => x.PackedQuantity >= x.PickedQuantity)
                ? ShipmentStatus.Packed : ShipmentStatus.Packing;
            AddHistory(header, "Pack", request.IdempotencyKey, request.Reason, actor);
            header.UpdatedBy = actor;
            header.UpdatedDate = DateTime.UtcNow;
            await uow.SaveChangesAsync(token);
            await WriteAudit(header, "pack", null, request.Lines.Count, token);
            return Result(header, null, false);
        }, ct, IsolationLevel.Serializable);
    }

    private Task<ShipmentOperationResult> ExecuteMovementAsync(
        long id,
        ShipmentOperationRequest request,
        long actor,
        ShipmentPhase phase,
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
            if (await uow.Repository<StockMovementOperation>().AnyAsync(
                    x => x.IdempotencyKey == movementRequest.IdempotencyKey, token))
            {
                var replay = await movements.PostAsync(movementRequest, token);
                return Result(header, replay.OperationId, true);
            }

            EnsurePhaseState(header, phase);
            if (phase == ShipmentPhase.Pick) EnsurePickerAssignment(header, actor);
            ValidateQuantities(header, lines, map, phase);
            ApplyShipmentInfo(header, request, phase);
            if (phase == ShipmentPhase.Pick)
                await reservations.ConsumeAsync(header, map, $"SH:{header.Id}:RESERVE:PICK:{request.IdempotencyKey:N}", actor, token);

            var movement = await movements.PostAsync(movementRequest, token);

            foreach (var line in lines)
            {
                var quantity = map[line.Id].Quantity;
                switch (phase)
                {
                    case ShipmentPhase.Pick:
                        line.PickedQuantity += quantity;
                        line.Status = line.PickedQuantity >= line.RequestedQuantity
                            ? ShipmentLineStatus.Picked : ShipmentLineStatus.Picking;
                        UpdatePickTask(header, line, quantity);
                        break;
                    case ShipmentPhase.Load:
                        line.LoadedQuantity += quantity;
                        line.Status = ShipmentLineStatus.Loaded;
                        break;
                    case ShipmentPhase.Ship:
                        line.ShippedQuantity += quantity;
                        line.Status = ShipmentLineStatus.Shipped;
                        break;
                }
                ApplyTracking(line, map[line.Id], phase switch
                {
                    ShipmentPhase.Pick => ShipmentTrackingPhase.Pick,
                    ShipmentPhase.Load => ShipmentTrackingPhase.Load,
                    ShipmentPhase.Ship => ShipmentTrackingPhase.Ship,
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
            await WriteAudit(header, phase.ToString().ToLowerInvariant(), movement.OperationId, request.Lines.Count, token);
            return Result(header, movement.OperationId, false);
        }, ct, IsolationLevel.Serializable);
    }

    private Task<ShipmentOperationResult> TransitionAsync(
        long id,
        ShipmentTransitionRequest request,
        long actor,
        string transition,
        Func<ShipmentHeader, CancellationToken, Task> mutate,
        CancellationToken ct)
    {
        if (id <= 0 || request.IdempotencyKey == Guid.Empty)
            throw AppException.BadRequest("Geçerli sevk ve idempotency anahtarı zorunludur.");
        return uow.ExecuteInTransactionAsync(async token =>
        {
            var header = await LoadAsync(id, token);
            if (await HasReplayAsync(id, request.IdempotencyKey, token)) return Result(header, null, true);
            if (header.Status == ShipmentStatus.Cancelled)
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

    private async Task<ShipmentHeader> LoadAsync(long id, CancellationToken ct) =>
        await uow.Repository<ShipmentHeader>().Query(tracking: true)
            .Include(x => x.Lines).ThenInclude(x => x.Trackings)
            .Include(x => x.Tasks).ThenInclude(x => x.Lines)
            .Include(x => x.Tasks).ThenInclude(x => x.Assignments)
            .FirstOrDefaultAsync(x => x.Id == id, ct)
        ?? throw AppException.NotFound("Sevk kaydı bulunamadı.");

    private async Task<bool> HasReplayAsync(long id, Guid key, CancellationToken ct) =>
        await uow.Repository<ShipmentStatusHistory>().Query()
            .AnyAsync(x => x.ShipmentHeaderId == id && x.CorrelationId == key, ct);

    private static void EnsurePhaseState(ShipmentHeader header, ShipmentPhase phase)
    {
        if (header.Status == ShipmentStatus.Cancelled)
            throw AppException.Conflict("İptal edilmiş sevk üzerinde işlem yapılamaz.");
        var allowed = phase switch
        {
            ShipmentPhase.Pick => header.Status is ShipmentStatus.Released or ShipmentStatus.Picking,
            ShipmentPhase.Load => header.Status is ShipmentStatus.Picked or ShipmentStatus.Packed
                or ShipmentStatus.Loading or ShipmentStatus.Loaded,
            ShipmentPhase.Ship => header.Status is ShipmentStatus.Picked or ShipmentStatus.Packed
                or ShipmentStatus.Loaded or ShipmentStatus.AwaitingApproval,
            _ => false
        };
        if (!allowed) throw AppException.Conflict($"{phase} işlemi mevcut {header.Status} durumunda yapılamaz.");
    }

    private static void EnsurePickerAssignment(ShipmentHeader header, long actor)
    {
        if (!header.RequireAssignee) return;
        var task = header.Tasks.FirstOrDefault(x => x.TaskType == ShipmentTaskType.Pick)
            ?? throw AppException.Conflict("Sevk toplama emri bulunamadı.");
        var assignment = task.Assignments.FirstOrDefault(x => x.UserId == actor)
            ?? throw AppException.Forbidden("Bu sevk toplama emri size atanmamış.");
        assignment.AcceptedAtUtc ??= DateTimeOffset.UtcNow;
        task.Status = ShipmentTaskStatus.InProgress;
    }

    private static void ValidateQuantities(
        ShipmentHeader header,
        IReadOnlyCollection<ShipmentLine> lines,
        IReadOnlyDictionary<long, ShipmentOperationLineRequest> requests,
        ShipmentPhase phase)
    {
        foreach (var line in lines)
        {
            var item = requests[line.Id];
            if (item.Quantity <= 0) throw AppException.BadRequest("Operasyon miktarı sıfırdan büyük olmalıdır.");
            var available = phase switch
            {
                ShipmentPhase.Pick => line.RequestedQuantity - line.PickedQuantity,
                ShipmentPhase.Load => (header.PackingPolicy == ShipmentPackingPolicy.Required
                    ? line.PackedQuantity : line.PickedQuantity) - line.LoadedQuantity,
                ShipmentPhase.Ship => (header.RequireLoadingConfirmation
                    ? line.LoadedQuantity
                    : header.PackingPolicy == ShipmentPackingPolicy.Required ? line.PackedQuantity : line.PickedQuantity)
                    - line.ShippedQuantity,
                _ => 0
            };
            if (item.Quantity > available)
                throw AppException.Conflict($"{line.LineNo}. satırda kullanılabilir miktar {available}, istenen {item.Quantity}.");
            ValidateTrackingDimension(header, line, item, phase switch
            {
                ShipmentPhase.Pick => ShipmentTrackingPhase.Pick,
                ShipmentPhase.Load => ShipmentTrackingPhase.Load,
                ShipmentPhase.Ship => ShipmentTrackingPhase.Ship,
                _ => throw new ArgumentOutOfRangeException(nameof(phase))
            });
        }
        if (phase != ShipmentPhase.Ship) return;
        var after = header.Lines.Sum(x => x.ShippedQuantity) + requests.Values.Sum(x => x.Quantity);
        var requested = header.Lines.Sum(x => x.RequestedQuantity);
        var percent = requested == 0 ? 0 : after * 100m / requested;
        if (percent < header.MinimumFulfillmentPercent)
            throw AppException.Conflict($"Minimum sevk karşılama oranı %{header.MinimumFulfillmentPercent}; mevcut oran %{percent:N2}.");
        if (!header.AllowPartialShipment && after < requested)
            throw AppException.Conflict("Sevk politikası kısmi sevke izin vermiyor.");
    }

    private static PostStockMovementRequest BuildMovementRequest(
        ShipmentHeader header,
        IReadOnlyCollection<ShipmentLine> lines,
        IReadOnlyDictionary<long, ShipmentOperationLineRequest> requests,
        ShipmentOperationRequest request,
        ShipmentPhase phase)
    {
        var movementType = phase == ShipmentPhase.Ship ? StockMovementTypes.Shipment : StockMovementTypes.Transfer;
        var rows = lines.Select(line =>
        {
            var item = requests[line.Id];
            var source = phase switch
            {
                ShipmentPhase.Pick => item.SourceLocationId ?? line.DefaultSourceLocationId,
                ShipmentPhase.Load => item.SourceLocationId ?? header.StagingLocationId,
                ShipmentPhase.Ship => item.SourceLocationId ?? (header.RequireLoadingConfirmation
                    ? header.LoadingLocationId : header.StagingLocationId ?? line.DefaultSourceLocationId),
                _ => null
            };
            var target = phase switch
            {
                ShipmentPhase.Pick => item.TargetLocationId ?? header.StagingLocationId,
                ShipmentPhase.Load => item.TargetLocationId ?? header.LoadingLocationId,
                _ => null
            };
            return new StockMovementLineRequest(
                line.StockId, line.YapCodeId, item.Quantity,
                header.SourceWarehouseId, source,
                phase == ShipmentPhase.Ship ? null : header.SourceWarehouseId,
                target, line.UnitCode, item.LotNo, item.SerialNo, "Available");
        }).ToList();
        return new(
            $"SH:{header.Id}:{phase}:{request.IdempotencyKey:N}",
            movementType,
            "Shipment",
            header.DocumentNo,
            header.Id,
            request.OccurredAtUtc?.UtcDateTime,
            Clean(request.Reason, 500),
            $"{phase} operation for {header.DocumentNo}",
            rows);
    }

    private static void ApplyShipmentInfo(ShipmentHeader header, ShipmentOperationRequest request, ShipmentPhase phase)
    {
        if (phase != ShipmentPhase.Ship) return;
        header.VehiclePlate = Clean(request.VehiclePlate, 20) ?? header.VehiclePlate;
        header.DriverName = Clean(request.DriverName, 200) ?? header.DriverName;
        header.WaybillNo = PurchaseWaybillNumberPolicy.Normalize(request.WaybillNo) ?? header.WaybillNo;
        header.TrackingNo = Clean(request.TrackingNo, 100) ?? header.TrackingNo;
        if (header.RequireShipmentInformation
            && string.IsNullOrWhiteSpace(header.VehiclePlate)
            && string.IsNullOrWhiteSpace(header.CarrierCode))
            throw AppException.BadRequest("Sevk için araç plakası veya taşıyıcı bilgisi zorunludur.");
    }

    private static void UpdatePickTask(ShipmentHeader header, ShipmentLine line, decimal quantity)
    {
        var task = header.Tasks.FirstOrDefault(x => x.TaskType == ShipmentTaskType.Pick);
        var taskLine = task?.Lines.FirstOrDefault(x => x.ShipmentLineId == line.Id);
        if (taskLine is null) return;
        taskLine.ProcessedQuantity += quantity;
        task!.Status = task.Lines.All(x => x.ProcessedQuantity >= x.PlannedQuantity)
            ? ShipmentTaskStatus.Completed : ShipmentTaskStatus.InProgress;
    }

    private static void UpdateHeaderStatus(ShipmentHeader header, ShipmentPhase phase)
    {
        header.Status = phase switch
        {
            ShipmentPhase.Pick when header.Lines.All(x => x.PickedQuantity >= x.RequestedQuantity) => ShipmentStatus.Picked,
            ShipmentPhase.Pick => ShipmentStatus.Picking,
            ShipmentPhase.Load when header.Lines.All(x => x.LoadedQuantity >=
                (header.PackingPolicy == ShipmentPackingPolicy.Required ? x.PackedQuantity : x.PickedQuantity)) => ShipmentStatus.Loaded,
            ShipmentPhase.Load => ShipmentStatus.Loading,
            ShipmentPhase.Ship => ShipmentStatus.Shipped,
            _ => header.Status
        };
        if (phase == ShipmentPhase.Ship)
        {
            header.ShippedAtUtc ??= DateTimeOffset.UtcNow;
            header.ErpIntegrationStatus = header.AutoPostErpAfterApproval
                ? ErpIntegrationStatus.Processing : ErpIntegrationStatus.Pending;
        }
    }

    private static void AddHistory(
        ShipmentHeader header, string status, Guid key, string? reason, long actor) =>
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

    private Task WriteAudit(ShipmentHeader header, string operation, long? movementId, int lineCount, CancellationToken ct) =>
        audit.WriteAsync(new(
            $"shipping.{operation}",
            nameof(ShipmentHeader),
            header.Id.ToString(),
            "Succeeded",
            "shipping",
            NewValues: new { header.DocumentNo, Operation = operation, MovementId = movementId, LineCount = lineCount },
            ChangedFields: ["Status", "Quantities", "StockMovement"]), ct);

    private static ShipmentOperationResult Result(ShipmentHeader header, long? movementId, bool replayed) =>
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

    private static void ValidateRequest(long id, ShipmentOperationRequest request)
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
        ShipmentLine line,
        ShipmentOperationLineRequest request,
        ShipmentTrackingPhase phase,
        long actor)
    {
        var handlingUnitNo = Clean(request.HandlingUnitNo, 100);
        var lotNo = Clean(request.LotNo, 100);
        var serialNo = Clean(request.SerialNo, 200);
        if (handlingUnitNo is null && lotNo is null && serialNo is null) return;

        var tracking = line.Trackings.FirstOrDefault(x =>
            x.HandlingUnitNo == handlingUnitNo
            && x.LotNo == lotNo
            && x.SerialNo == serialNo);
        if (tracking is null)
        {
            tracking = new ShipmentTracking
            {
                ShipmentLineId = line.Id,
                HandlingUnitNo = handlingUnitNo,
                LotNo = lotNo,
                SerialNo = serialNo,
                PlannedQuantity = request.Quantity,
                PickedQuantity = phase is ShipmentTrackingPhase.Pack or ShipmentTrackingPhase.Load or ShipmentTrackingPhase.Ship
                    ? request.Quantity : 0,
                PackedQuantity = phase is ShipmentTrackingPhase.Load or ShipmentTrackingPhase.Ship
                    ? request.Quantity : 0,
                LoadedQuantity = phase is ShipmentTrackingPhase.Ship ? request.Quantity : 0,
                SourceLocationId = request.SourceLocationId,
                CreatedBy = actor,
                CreatedDate = DateTime.UtcNow
            };
            line.Trackings.Add(tracking);
        }
        tracking.PlannedQuantity = Math.Max(tracking.PlannedQuantity, request.Quantity);

        switch (phase)
        {
            case ShipmentTrackingPhase.Pick:
                tracking.PickedQuantity += request.Quantity;
                break;
            case ShipmentTrackingPhase.Pack:
                tracking.PackedQuantity += request.Quantity;
                break;
            case ShipmentTrackingPhase.Load:
                tracking.LoadedQuantity += request.Quantity;
                break;
            case ShipmentTrackingPhase.Ship:
                tracking.ShippedQuantity += request.Quantity;
                break;
        }
        tracking.SourceLocationId ??= request.SourceLocationId;
        tracking.UpdatedBy = actor;
        tracking.UpdatedDate = DateTime.UtcNow;
    }

    private static void ValidateTrackingDimension(
        ShipmentHeader header,
        ShipmentLine line,
        ShipmentOperationLineRequest request,
        ShipmentTrackingPhase phase)
    {
        var handlingUnitNo = Clean(request.HandlingUnitNo, 100);
        var lotNo = Clean(request.LotNo, 100);
        var serialNo = Clean(request.SerialNo, 200);
        var serialTracked = line.TrackingType is StockTrackingType.Serial or StockTrackingType.LotAndSerial;
        var lotTracked = line.TrackingType is StockTrackingType.Lot or StockTrackingType.LotAndSerial;

        if (serialTracked && serialNo is null)
            throw AppException.BadRequest($"{line.LineNo}. satır için seri numarası zorunludur.");
        if (lotTracked && lotNo is null)
            throw AppException.BadRequest($"{line.LineNo}. satır için lot numarası zorunludur.");
        if (serialTracked && request.Quantity != 1)
            throw AppException.BadRequest($"{line.LineNo}. satırda serili stok miktarı 1 olmalıdır.");
        if (line.RequireHandlingUnit && handlingUnitNo is null)
            throw AppException.BadRequest($"{line.LineNo}. satır için palet/kasa numarası zorunludur.");

        var tracking = line.Trackings.FirstOrDefault(x =>
            Equal(x.HandlingUnitNo, handlingUnitNo)
            && Equal(x.LotNo, lotNo)
            && Equal(x.SerialNo, serialNo));
        var hasDimension = serialTracked || lotTracked || line.RequireHandlingUnit
            || handlingUnitNo is not null || lotNo is not null || serialNo is not null;

        if (phase == ShipmentTrackingPhase.Pick && line.Trackings.Count > 0 && tracking is null)
            throw AppException.Conflict($"{line.LineNo}. satırın seri/lot/palet bilgisi planlanan takip kaydıyla eşleşmiyor.");
        if (phase != ShipmentTrackingPhase.Pick && hasDimension && tracking is null)
            throw AppException.Conflict($"{line.LineNo}. satırın seri/lot/palet bilgisi önceki operasyonla eşleşmiyor.");
        if (tracking is null) return;

        var available = phase switch
        {
            ShipmentTrackingPhase.Pick => tracking.PlannedQuantity - tracking.PickedQuantity,
            ShipmentTrackingPhase.Pack => tracking.PickedQuantity - tracking.PackedQuantity,
            ShipmentTrackingPhase.Load => (header.PackingPolicy == ShipmentPackingPolicy.Required
                ? tracking.PackedQuantity : tracking.PickedQuantity) - tracking.LoadedQuantity,
            ShipmentTrackingPhase.Ship => (header.RequireLoadingConfirmation
                ? tracking.LoadedQuantity
                : header.PackingPolicy == ShipmentPackingPolicy.Required
                    ? tracking.PackedQuantity
                    : tracking.PickedQuantity) - tracking.ShippedQuantity,
            _ => 0
        };
        if (request.Quantity > available)
            throw AppException.Conflict(
                $"{line.LineNo}. satırın seçilen seri/lot/palet boyutunda kullanılabilir miktarı {available}, istenen {request.Quantity}.");
    }

    private static bool Equal(string? left, string? right) =>
        string.Equals(Clean(left, 200), Clean(right, 200), StringComparison.OrdinalIgnoreCase);

    private enum ShipmentPhase { Pick, Load, Ship }
    private enum ShipmentTrackingPhase { Pick, Pack, Load, Ship }
}
