using System.Data;
using Microsoft.EntityFrameworkCore;
using verii_wms_api_v2.Modules.Audit.Application;
using verii_wms_api_v2.Modules.StockMovement.Application;
using verii_wms_api_v2.Modules.StockMovement.Domain;
using verii_wms_api_v2.Modules.WarehouseOperations.Domain;
using verii_wms_api_v2.Modules.WarehouseTransfer.Domain;
using verii_wms_api_v2.Shared.Application.Abstractions.Persistence;
using verii_wms_api_v2.Shared.Application.Exceptions;

namespace verii_wms_api_v2.Modules.WarehouseTransfer.Application;

public sealed class WarehouseTransferOperationService(
    IUnitOfWork uow,
    IStockMovementService movements,
    IWarehouseTransferReservationService reservations,
    IAuditLogWriter audit) : IWarehouseTransferOperationService
{
    public Task<WarehouseTransferOperationResult> ApproveAsync(
        long id, WarehouseTransferTransitionRequest request, long actor, CancellationToken ct = default) =>
        TransitionAsync(id, request, actor, "Approval", async (header, token) =>
        {
            if (!header.RequireApproval)
                throw AppException.Conflict("Bu transfer için onay gerekmiyor.");
            if (header.ApprovalStatus == OperationApprovalStatus.Rejected)
                throw AppException.Conflict("Reddedilmiş transfer onaylanamaz.");
            header.ApprovalStatus = OperationApprovalStatus.Approved;
            await Task.CompletedTask;
        }, ct);

    public Task<WarehouseTransferOperationResult> ReleaseAsync(
        long id, WarehouseTransferTransitionRequest request, long actor, CancellationToken ct = default) =>
        TransitionAsync(id, request, actor, "Release", async (header, token) =>
        {
            if (header.RequireApproval && header.ApprovalStatus != OperationApprovalStatus.Approved)
                throw AppException.Conflict("Transfer serbest bırakılmadan önce onaylanmalıdır.");
            if (header.Status != WarehouseTransferStatus.Draft)
                throw AppException.Conflict("Yalnızca taslak transfer serbest bırakılabilir.");
            header.Status = WarehouseTransferStatus.Released;
            header.ReleasedAtUtc = DateTimeOffset.UtcNow;
            header.ReleasedBy = actor;
            foreach (var task in header.Tasks.Where(x => x.Status == WarehouseTransferTaskStatus.Open && x.Assignments.Count > 0))
                task.Status = WarehouseTransferTaskStatus.Assigned;
            if (header.ReservationPolicy == WarehouseTransferReservationPolicy.OnRelease)
                await reservations.ReserveAsync(header, $"WT:{header.Id}:RESERVE:RELEASE", actor, token);
        }, ct);

    public Task<WarehouseTransferOperationResult> PickAsync(
        long id, WarehouseTransferOperationRequest request, long actor, CancellationToken ct = default) =>
        ExecuteMovementAsync(id, request, actor, TransferPhase.Pick, ct);

    public Task<WarehouseTransferOperationResult> DispatchAsync(
        long id, WarehouseTransferOperationRequest request, long actor, CancellationToken ct = default) =>
        ExecuteMovementAsync(id, request, actor, TransferPhase.Dispatch, ct);

    public Task<WarehouseTransferOperationResult> ReceiveAsync(
        long id, WarehouseTransferOperationRequest request, long actor, CancellationToken ct = default) =>
        ExecuteMovementAsync(id, request, actor, TransferPhase.Receive, ct);

    public Task<WarehouseTransferOperationResult> PutawayAsync(
        long id, WarehouseTransferOperationRequest request, long actor, CancellationToken ct = default) =>
        ExecuteMovementAsync(id, request, actor, TransferPhase.Putaway, ct);

    public Task<WarehouseTransferOperationResult> CancelAsync(
        long id, WarehouseTransferTransitionRequest request, long actor, CancellationToken ct = default) =>
        CancelCoreAsync(id, request, actor, false, ct);

    public Task<WarehouseTransferOperationResult> CancelAfterErpDeletionAsync(
        long id, WarehouseTransferTransitionRequest request, long actor, CancellationToken ct = default) =>
        CancelCoreAsync(id, request, actor, true, ct);

    private Task<WarehouseTransferOperationResult> CancelCoreAsync(
        long id,
        WarehouseTransferTransitionRequest request,
        long actor,
        bool erpDeletionConfirmed,
        CancellationToken ct)
    {
        if (id <= 0 || request.IdempotencyKey == Guid.Empty || string.IsNullOrWhiteSpace(request.Reason))
            throw AppException.BadRequest("Transfer, idempotency anahtarı ve iptal nedeni zorunludur.");
        return uow.ExecuteInTransactionAsync(async token =>
        {
            var header = await LoadAsync(id, token);
            var replay = await uow.Repository<WarehouseTransferStatusHistory>().Query()
                .AnyAsync(x => x.WtHeaderId == id && x.CorrelationId == request.IdempotencyKey, token);
            if (replay) return Result(header, null, true);
            if (header.Status == WarehouseTransferStatus.Cancelled) throw AppException.Conflict("Transfer zaten iptal edilmiş.");
            if (!erpDeletionConfirmed
                && header.ErpIntegrationStatus is ErpIntegrationStatus.Processing
                    or ErpIntegrationStatus.Succeeded
                    or ErpIntegrationStatus.CommitUncertain
                    or ErpIntegrationStatus.Cancelled)
                throw AppException.Conflict("ERP aktarımı başlamış veya tamamlanmış transfer WMS üzerinden iptal edilemez.");
            if (erpDeletionConfirmed
                && header.ErpIntegrationStatus is not (ErpIntegrationStatus.Succeeded or ErpIntegrationStatus.Cancelled))
                throw AppException.Conflict("Transfer ERP silme doğrulamasıyla uyumlu durumda değil.");

            var operationRepo = uow.Repository<StockMovementOperation>();
            var operations = await operationRepo.Query()
                .Where(x => x.ReferenceType == "WarehouseTransfer" && x.ReferenceId == id
                    && x.OperationType != StockMovementTypes.Reversal
                    && !operationRepo.Query().Any(r => r.ReversalOfOperationId == x.Id))
                .OrderByDescending(x => x.Id).Select(x => x.Id).ToListAsync(token);
            long? lastReversalId = null;
            foreach (var operationId in operations)
            {
                var reversal = await movements.ReverseAsync(operationId,
                    new($"WT:{id}:CANCEL:{request.IdempotencyKey:N}:{operationId}", request.Reason!.Trim(), DateTime.UtcNow), token);
                lastReversalId = reversal.OperationId;
            }
            await reservations.ReleaseAllAsync(header, $"WT:{id}:RESERVE:CANCEL:{request.IdempotencyKey:N}", request.Reason!.Trim(), actor, token);
            foreach (var line in header.Lines) line.Status = WarehouseTransferLineStatus.Cancelled;
            foreach (var task in header.Tasks) task.Status = WarehouseTransferTaskStatus.Cancelled;
            foreach (var tracking in header.Lines.SelectMany(x => x.Trackings)) tracking.Status = WarehouseTransferTrackingStatus.Cancelled;
            header.Status = WarehouseTransferStatus.Cancelled;
            header.CancelledAtUtc = DateTimeOffset.UtcNow;
            header.CancelledBy = actor;
            header.CancellationReason = Clean(request.Reason, 1000);
            if (erpDeletionConfirmed) header.ErpIntegrationStatus = ErpIntegrationStatus.Cancelled;
            header.UpdatedBy = actor;
            header.UpdatedDate = DateTime.UtcNow;
            AddHistory(header, "Cancel", request.IdempotencyKey, request.Reason, actor);
            await uow.SaveChangesAsync(token);
            await audit.WriteAsync(new("warehouse-transfer.cancel", nameof(WarehouseTransferHeader), id.ToString(), "Succeeded", "warehouse-transfer",
                NewValues: new { header.DocumentNo, ReversedOperationCount = operations.Count, LastReversalId = lastReversalId, header.CancellationReason },
                ChangedFields: ["Status", "Reservations", "StockMovement"]), token);
            return Result(header, lastReversalId, false);
        }, ct, IsolationLevel.Serializable);
    }

    private Task<WarehouseTransferOperationResult> ExecuteMovementAsync(
        long id,
        WarehouseTransferOperationRequest request,
        long actor,
        TransferPhase phase,
        CancellationToken ct)
    {
        ValidateRequest(id, request);
        return uow.ExecuteInTransactionAsync(async token =>
        {
            var header = await LoadAsync(id, token);
            var requestLines = request.Lines.ToDictionary(x => x.LineId);
            var lines = header.Lines.Where(x => requestLines.ContainsKey(x.Id)).ToList();
            if (lines.Count != requestLines.Count)
                throw AppException.BadRequest("Operasyon satırlarından biri bu transfere ait değil.");

            var movementRequest = BuildMovementRequest(header, lines, requestLines, request, phase);
            if (await uow.Repository<StockMovementOperation>().AnyAsync(
                    x => x.IdempotencyKey == movementRequest.IdempotencyKey, token))
            {
                var replay = await movements.PostAsync(movementRequest, token);
                return Result(header, replay.OperationId, true);
            }

            EnsurePhaseState(header, phase);
            if (phase == TransferPhase.Pick) EnsurePickerAssignment(header, actor);
            ValidateQuantities(header, lines, requestLines, phase);
            ApplyShipmentInfo(header, request, phase);
            if (phase == TransferPhase.Pick)
                await reservations.ConsumeAsync(header, requestLines, $"WT:{header.Id}:RESERVE:PICK:{request.IdempotencyKey:N}", actor, token);

            var movement = await movements.PostAsync(movementRequest, token);

            foreach (var line in lines)
            {
                var quantity = requestLines[line.Id].Quantity;
                switch (phase)
                {
                    case TransferPhase.Pick:
                        line.PickedQuantity += quantity;
                        line.Status = line.PickedQuantity >= line.RequestedQuantity
                            ? WarehouseTransferLineStatus.Picked : WarehouseTransferLineStatus.PartiallyPicked;
                        UpdatePickTask(header, line, quantity, actor);
                        break;
                    case TransferPhase.Dispatch:
                        line.ShippedQuantity += quantity;
                        if (header.CreateTransitInventory)
                        {
                            line.Status = WarehouseTransferLineStatus.Shipped;
                        }
                        else
                        {
                            line.ReceivedQuantity += quantity;
                            line.Status = line.ReceivedQuantity >= line.RequestedQuantity
                                ? WarehouseTransferLineStatus.Received
                                : WarehouseTransferLineStatus.PartiallyReceived;
                        }
                        break;
                    case TransferPhase.Receive:
                        line.ReceivedQuantity += quantity;
                        line.Status = line.ReceivedQuantity >= line.ShippedQuantity
                            ? WarehouseTransferLineStatus.Received : WarehouseTransferLineStatus.PartiallyReceived;
                        break;
                    case TransferPhase.Putaway:
                        line.PutawayQuantity += quantity;
                        line.Status = WarehouseTransferLineStatus.Putaway;
                        break;
                }
                ApplyTracking(line, requestLines[line.Id], phase, actor);
                line.UpdatedBy = actor;
                line.UpdatedDate = DateTime.UtcNow;
            }

            UpdateHeaderStatus(header, phase, actor);
            AddHistory(header, phase.ToString(), request.IdempotencyKey, request.Reason, actor);
            await uow.SaveChangesAsync(token);
            await audit.WriteAsync(new(
                $"warehouse-transfer.{phase.ToString().ToLowerInvariant()}",
                nameof(WarehouseTransferHeader),
                header.Id.ToString(),
                "Succeeded",
                "warehouse-transfer",
                NewValues: new { header.DocumentNo, Phase = phase.ToString(), movement.OperationId, Lines = request.Lines.Count },
                ChangedFields: ["Status", "Quantities", "StockMovement"]), token);
            return Result(header, movement.OperationId, false);
        }, ct, IsolationLevel.Serializable);
    }

    private Task<WarehouseTransferOperationResult> TransitionAsync(
        long id,
        WarehouseTransferTransitionRequest request,
        long actor,
        string transition,
        Func<WarehouseTransferHeader, CancellationToken, Task> mutate,
        CancellationToken ct)
    {
        if (id <= 0 || request.IdempotencyKey == Guid.Empty)
            throw AppException.BadRequest("Geçerli transfer ve idempotency anahtarı zorunludur.");
        return uow.ExecuteInTransactionAsync(async token =>
        {
            var header = await LoadAsync(id, token);
            var replay = await uow.Repository<WarehouseTransferStatusHistory>().Query()
                .AnyAsync(x => x.WtHeaderId == id && x.CorrelationId == request.IdempotencyKey, token);
            if (replay) return Result(header, null, true);
            if (header.Status == WarehouseTransferStatus.Cancelled)
                throw AppException.Conflict("İptal edilmiş transfer üzerinde işlem yapılamaz.");

            await mutate(header, token);
            AddHistory(header, transition, request.IdempotencyKey, request.Reason, actor);
            header.UpdatedBy = actor;
            header.UpdatedDate = DateTime.UtcNow;
            await uow.SaveChangesAsync(token);
            await audit.WriteAsync(new(
                $"warehouse-transfer.{transition.ToLowerInvariant()}",
                nameof(WarehouseTransferHeader),
                id.ToString(),
                "Succeeded",
                "warehouse-transfer",
                NewValues: new { header.DocumentNo, Transition = transition, header.Status, header.ApprovalStatus },
                ChangedFields: ["Status", "ApprovalStatus"]), token);
            return Result(header, null, false);
        }, ct, IsolationLevel.Serializable);
    }

    private async Task<WarehouseTransferHeader> LoadAsync(long id, CancellationToken ct) =>
        await uow.Repository<WarehouseTransferHeader>().Query(tracking: true)
            .Include(x => x.Lines).ThenInclude(x => x.Trackings)
            .Include(x => x.Tasks).ThenInclude(x => x.Lines)
            .Include(x => x.Tasks).ThenInclude(x => x.Assignments)
            .FirstOrDefaultAsync(x => x.Id == id, ct)
        ?? throw AppException.NotFound("Transfer kaydı bulunamadı.");

    private static void EnsurePhaseState(WarehouseTransferHeader header, TransferPhase phase)
    {
        if (header.Status == WarehouseTransferStatus.Cancelled)
            throw AppException.Conflict("İptal edilmiş transfer üzerinde işlem yapılamaz.");
        var allowed = phase switch
        {
            TransferPhase.Pick => header.Status is WarehouseTransferStatus.Released
                or WarehouseTransferStatus.Picking or WarehouseTransferStatus.PartiallyPicked,
            TransferPhase.Dispatch => header.Status is WarehouseTransferStatus.Picked
                or WarehouseTransferStatus.PartiallyPicked or WarehouseTransferStatus.Shipped
                || (!header.CreateTransitInventory && header.Status is
                    WarehouseTransferStatus.PartiallyReceived or WarehouseTransferStatus.Received),
            TransferPhase.Receive => header.Status is WarehouseTransferStatus.Shipped
                or WarehouseTransferStatus.PartiallyReceived,
            TransferPhase.Putaway => header.Status is WarehouseTransferStatus.Received
                or WarehouseTransferStatus.PartiallyReceived or WarehouseTransferStatus.PartiallyPutaway,
            _ => false
        };
        if (!allowed) throw AppException.Conflict($"{phase} işlemi mevcut {header.Status} durumunda yapılamaz.");
    }

    private static void EnsurePickerAssignment(WarehouseTransferHeader header, long actor)
    {
        if (!header.RequireAssignee) return;
        var task = header.Tasks.FirstOrDefault(x => x.TaskType == WarehouseTransferTaskType.Pick)
            ?? throw AppException.Conflict("Transfer toplama emri bulunamadı.");
        var assignment = task.Assignments.FirstOrDefault(x => x.UserId == actor)
            ?? throw AppException.Forbidden("Bu transfer toplama emri size atanmamış.");
        assignment.AcceptedAtUtc ??= DateTimeOffset.UtcNow;
        task.AcceptedAtUtc ??= DateTimeOffset.UtcNow;
        task.AcceptedBy ??= actor;
        task.StartedAtUtc ??= DateTimeOffset.UtcNow;
        task.StartedBy ??= actor;
        task.Status = WarehouseTransferTaskStatus.InProgress;
    }

    private static void ValidateQuantities(
        WarehouseTransferHeader header,
        IReadOnlyCollection<WarehouseTransferLine> lines,
        IReadOnlyDictionary<long, WarehouseTransferOperationLineRequest> requests,
        TransferPhase phase)
    {
        foreach (var line in lines)
        {
            var quantity = requests[line.Id].Quantity;
            if (quantity <= 0) throw AppException.BadRequest("Operasyon miktarı sıfırdan büyük olmalıdır.");
            var available = phase switch
            {
                TransferPhase.Pick => line.RequestedQuantity - line.PickedQuantity,
                TransferPhase.Dispatch => line.PickedQuantity - line.ShippedQuantity,
                TransferPhase.Receive => line.ShippedQuantity - line.ReceivedQuantity,
                TransferPhase.Putaway => line.ReceivedQuantity - line.PutawayQuantity,
                _ => 0
            };
            if (quantity > available)
                throw AppException.Conflict($"{line.LineNo}. satırda kullanılabilir miktar {available}, istenen {quantity}.");
            ValidateTrackingDimension(line, requests[line.Id], phase);
        }

        var phaseTotal = lines.Sum(x => phase switch
        {
            TransferPhase.Pick => x.PickedQuantity,
            TransferPhase.Dispatch => x.ShippedQuantity,
            TransferPhase.Receive => x.ReceivedQuantity,
            TransferPhase.Putaway => x.PutawayQuantity,
            _ => 0
        }) + requests.Values.Sum(x => x.Quantity);
        var requestedTotal = header.Lines.Sum(x => x.RequestedQuantity);
        if (phase == TransferPhase.Dispatch)
        {
            var percent = requestedTotal == 0 ? 0 : phaseTotal * 100m / requestedTotal;
            if (percent < header.MinimumFulfillmentPercent)
                throw AppException.Conflict($"Minimum karşılama oranı %{header.MinimumFulfillmentPercent}; mevcut oran %{percent:N2}.");
            if (!header.AllowPartialShipment && phaseTotal < requestedTotal)
                throw AppException.Conflict("Transfer politikası kısmi sevke izin vermiyor.");
        }
        if (phase == TransferPhase.Receive && !header.AllowPartialReceipt
            && phaseTotal < header.Lines.Sum(x => x.ShippedQuantity))
            throw AppException.Conflict("Transfer politikası kısmi kabule izin vermiyor.");
    }

    private static PostStockMovementRequest BuildMovementRequest(
        WarehouseTransferHeader header,
        IReadOnlyCollection<WarehouseTransferLine> lines,
        IReadOnlyDictionary<long, WarehouseTransferOperationLineRequest> requests,
        WarehouseTransferOperationRequest request,
        TransferPhase phase)
    {
        var rows = lines.Select(line =>
        {
            var item = requests[line.Id];
            var sourceLocation = phase switch
            {
                TransferPhase.Pick => item.SourceLocationId ?? line.DefaultSourceLocationId,
                TransferPhase.Dispatch => item.SourceLocationId ?? header.SourceStagingLocationId,
                TransferPhase.Receive => item.SourceLocationId ?? header.TargetReceivingLocationId,
                TransferPhase.Putaway => item.SourceLocationId ?? header.TargetReceivingLocationId,
                _ => null
            };
            var targetLocation = phase switch
            {
                TransferPhase.Pick => item.TargetLocationId ?? header.SourceStagingLocationId,
                TransferPhase.Dispatch => item.TargetLocationId ?? header.TargetReceivingLocationId,
                TransferPhase.Receive => item.TargetLocationId ?? header.TargetReceivingLocationId,
                TransferPhase.Putaway => item.TargetLocationId ?? line.DefaultTargetLocationId ?? header.TargetPutawayLocationId,
                _ => null
            };
            var sourceWarehouse = phase is TransferPhase.Pick or TransferPhase.Dispatch
                ? header.SourceWarehouseId : header.TargetWarehouseId;
            var targetWarehouse = phase == TransferPhase.Pick
                ? header.SourceWarehouseId : header.TargetWarehouseId;
            var sourceStatus = phase == TransferPhase.Receive && header.CreateTransitInventory ? "InTransit" : "Available";
            var targetStatus = phase == TransferPhase.Dispatch && header.CreateTransitInventory ? "InTransit" : "Available";
            return new StockMovementLineRequest(
                line.StockId, line.YapCodeId, item.Quantity,
                sourceWarehouse, sourceLocation, targetWarehouse, targetLocation,
                line.UnitCode, item.LotNo, item.SerialNo, null, sourceStatus, targetStatus);
        }).ToList();

        return new(
            $"WT:{header.Id}:{phase}:{request.IdempotencyKey:N}",
            StockMovementTypes.Transfer,
            "WarehouseTransfer",
            header.DocumentNo,
            header.Id,
            request.OccurredAtUtc?.UtcDateTime,
            Clean(request.Reason, 500),
            $"{phase} operation for {header.DocumentNo}",
            rows);
    }

    private static void ApplyShipmentInfo(
        WarehouseTransferHeader header,
        WarehouseTransferOperationRequest request,
        TransferPhase phase)
    {
        if (phase != TransferPhase.Dispatch) return;
        header.VehiclePlate = Clean(request.VehiclePlate, 20) ?? header.VehiclePlate;
        header.DriverName = Clean(request.DriverName, 200) ?? header.DriverName;
        header.WaybillNo = Clean(request.WaybillNo, 50) ?? header.WaybillNo;
        if (header.RequireShipmentInformation
            && string.IsNullOrWhiteSpace(header.VehiclePlate)
            && string.IsNullOrWhiteSpace(header.CarrierCode))
            throw AppException.BadRequest("Sevk için araç plakası veya taşıyıcı bilgisi zorunludur.");
    }

    private static void UpdatePickTask(WarehouseTransferHeader header, WarehouseTransferLine line, decimal quantity, long actor)
    {
        var task = header.Tasks.FirstOrDefault(x => x.TaskType == WarehouseTransferTaskType.Pick);
        var taskLine = task?.Lines.FirstOrDefault(x => x.WtLineId == line.Id);
        if (taskLine is null) return;
        taskLine.ProcessedQuantity += quantity;
        taskLine.UpdatedBy = actor;
        taskLine.UpdatedDate = DateTime.UtcNow;
        if (task!.Lines.All(x => x.ProcessedQuantity >= x.PlannedQuantity))
        {
            task.Status = WarehouseTransferTaskStatus.Completed;
            task.CompletedAtUtc = DateTimeOffset.UtcNow;
            task.CompletedBy = actor;
        }
        else task.Status = WarehouseTransferTaskStatus.PartiallyCompleted;
    }

    private static void ValidateTrackingDimension(
        WarehouseTransferLine line,
        WarehouseTransferOperationLineRequest request,
        TransferPhase phase)
    {
        var lotNo = Clean(request.LotNo, 100);
        var serialNo = Clean(request.SerialNo, 200);
        if (line.RequireSerial && serialNo is null)
            throw AppException.BadRequest($"{line.LineNo}. satır için seri numarası zorunludur.");
        if (line.RequireLot && lotNo is null)
            throw AppException.BadRequest($"{line.LineNo}. satır için lot numarası zorunludur.");
        if (line.TrackingType is StockTrackingType.Serial or StockTrackingType.LotAndSerial
            && request.Quantity != 1)
            throw AppException.BadRequest($"{line.LineNo}. satırda serili stok miktarı 1 olmalıdır.");

        var tracking = line.Trackings.FirstOrDefault(x =>
            Equal(x.LotNo, lotNo) && Equal(x.SerialNo, serialNo));
        var hasTrackingDimension = lotNo is not null || serialNo is not null
            || line.RequireLot || line.RequireSerial;

        if (phase == TransferPhase.Pick && line.Trackings.Count > 0 && tracking is null)
            throw AppException.Conflict($"{line.LineNo}. satırın seri/lot bilgisi planlanan takip kaydıyla eşleşmiyor.");
        if (phase != TransferPhase.Pick && hasTrackingDimension && tracking is null)
            throw AppException.Conflict($"{line.LineNo}. satırın seri/lot bilgisi önceki transfer adımıyla eşleşmiyor.");
        if (tracking is null) return;

        var available = phase switch
        {
            TransferPhase.Pick => tracking.PlannedQuantity - tracking.PickedQuantity,
            TransferPhase.Dispatch => tracking.PickedQuantity - tracking.ShippedQuantity,
            TransferPhase.Receive => tracking.ShippedQuantity - tracking.ReceivedQuantity,
            TransferPhase.Putaway => tracking.ReceivedQuantity - tracking.PutawayQuantity,
            _ => 0
        };
        if (request.Quantity > available)
            throw AppException.Conflict(
                $"{line.LineNo}. satırın seçilen seri/lot boyutunda kullanılabilir miktarı {available}, istenen {request.Quantity}.");
    }

    private static void ApplyTracking(
        WarehouseTransferLine line,
        WarehouseTransferOperationLineRequest request,
        TransferPhase phase,
        long actor)
    {
        var lotNo = Clean(request.LotNo, 100);
        var serialNo = Clean(request.SerialNo, 200);
        if (lotNo is null && serialNo is null && line.TrackingType == StockTrackingType.None) return;

        var tracking = line.Trackings.FirstOrDefault(x =>
            Equal(x.LotNo, lotNo) && Equal(x.SerialNo, serialNo));
        if (tracking is null)
        {
            if (phase != TransferPhase.Pick)
                throw AppException.Conflict($"{line.LineNo}. satırın takip boyutu önceki transfer adımında bulunamadı.");
            tracking = new WarehouseTransferTracking
            {
                BranchCode = line.BranchCode,
                Line = line,
                LotNo = lotNo,
                SerialNo = serialNo,
                PlannedQuantity = request.Quantity,
                SourceLocationId = request.SourceLocationId,
                TargetLocationId = request.TargetLocationId,
                CreatedBy = actor,
                CreatedDate = DateTime.UtcNow
            };
            line.Trackings.Add(tracking);
        }

        tracking.PlannedQuantity = Math.Max(tracking.PlannedQuantity, request.Quantity);
        switch (phase)
        {
            case TransferPhase.Pick:
                tracking.PickedQuantity += request.Quantity;
                tracking.Status = WarehouseTransferTrackingStatus.Picked;
                break;
            case TransferPhase.Dispatch:
                tracking.ShippedQuantity += request.Quantity;
                tracking.Status = WarehouseTransferTrackingStatus.Shipped;
                break;
            case TransferPhase.Receive:
                tracking.ReceivedQuantity += request.Quantity;
                tracking.Status = WarehouseTransferTrackingStatus.Received;
                break;
            case TransferPhase.Putaway:
                tracking.PutawayQuantity += request.Quantity;
                tracking.Status = WarehouseTransferTrackingStatus.Putaway;
                break;
        }
        tracking.SourceLocationId ??= request.SourceLocationId;
        tracking.TargetLocationId ??= request.TargetLocationId;
        tracking.UpdatedBy = actor;
        tracking.UpdatedDate = DateTime.UtcNow;
    }

    private static void UpdateHeaderStatus(WarehouseTransferHeader header, TransferPhase phase, long actor)
    {
        var all = header.Lines;
        header.Status = phase switch
        {
            TransferPhase.Pick when all.All(x => x.PickedQuantity >= x.RequestedQuantity) => WarehouseTransferStatus.Picked,
            TransferPhase.Pick when all.Sum(x => x.PickedQuantity) > 0 => WarehouseTransferStatus.PartiallyPicked,
            TransferPhase.Pick => WarehouseTransferStatus.Picking,
            TransferPhase.Dispatch when header.CreateTransitInventory => WarehouseTransferStatus.Shipped,
            TransferPhase.Dispatch when all.All(x => x.ReceivedQuantity >= x.RequestedQuantity) => WarehouseTransferStatus.Received,
            TransferPhase.Dispatch => WarehouseTransferStatus.PartiallyReceived,
            TransferPhase.Receive when all.All(x => x.ReceivedQuantity >= x.ShippedQuantity) => WarehouseTransferStatus.Received,
            TransferPhase.Receive => WarehouseTransferStatus.PartiallyReceived,
            TransferPhase.Putaway when all.All(x => x.PutawayQuantity >= x.ReceivedQuantity) => WarehouseTransferStatus.Completed,
            TransferPhase.Putaway => WarehouseTransferStatus.PartiallyPutaway,
            _ => header.Status
        };
        if (phase == TransferPhase.Dispatch)
        {
            header.ShippedAtUtc ??= DateTimeOffset.UtcNow;
            header.ShippedBy ??= actor;
            if (!header.CreateTransitInventory && header.Status == WarehouseTransferStatus.Received)
            {
                header.ReceivedAtUtc ??= DateTimeOffset.UtcNow;
                header.ReceivedBy ??= actor;
            }
        }
        if (phase == TransferPhase.Receive && header.Status == WarehouseTransferStatus.Received)
        {
            header.ReceivedAtUtc ??= DateTimeOffset.UtcNow;
            header.ReceivedBy ??= actor;
        }
        if (header.Status == WarehouseTransferStatus.Completed)
        {
            header.CompletedAtUtc ??= DateTimeOffset.UtcNow;
            header.CompletedBy ??= actor;
        }
        header.UpdatedBy = actor;
        header.UpdatedDate = DateTime.UtcNow;
    }

    private static void AddHistory(
        WarehouseTransferHeader header, string status, Guid correlationId, string? reason, long actor) =>
        header.StatusHistory.Add(new()
        {
            BranchCode = header.BranchCode,
            CreatedBy = actor,
            CreatedDate = DateTime.UtcNow,
            Header = header,
            StatusArea = status switch
            {
                "Approval" => WarehouseTransferStatusArea.Approval,
                "Release" => WarehouseTransferStatusArea.Operation,
                "Pick" => WarehouseTransferStatusArea.Picking,
                "Dispatch" => WarehouseTransferStatusArea.Dispatch,
                "Receive" => WarehouseTransferStatusArea.Receiving,
                "Putaway" => WarehouseTransferStatusArea.Putaway,
                _ => WarehouseTransferStatusArea.Operation
            },
            ToStatus = status,
            ChangedAtUtc = DateTimeOffset.UtcNow,
            ChangedBy = actor,
            Description = Clean(reason, 1000),
            CorrelationId = correlationId
        });

    private static WarehouseTransferOperationResult Result(
        WarehouseTransferHeader header, long? movementId, bool replayed) =>
        new(
            header.Id,
            header.DocumentNo,
            header.Status.ToString(),
            movementId,
            header.Lines.Sum(x => x.PickedQuantity),
            header.Lines.Sum(x => x.ShippedQuantity),
            header.Lines.Sum(x => x.ReceivedQuantity),
            header.Lines.Sum(x => x.PutawayQuantity),
            replayed);

    private static void ValidateRequest(long id, WarehouseTransferOperationRequest request)
    {
        if (id <= 0 || request.IdempotencyKey == Guid.Empty || request.Lines.Count == 0)
            throw AppException.BadRequest("Transfer, idempotency anahtarı ve operasyon satırları zorunludur.");
        if (request.Lines.GroupBy(x => x.LineId).Any(x => x.Count() > 1))
            throw AppException.BadRequest("Aynı transfer satırı bir istekte tekrar edemez.");
    }

    private static string? Clean(string? value, int max)
    {
        var text = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        return text?.Length > max ? text[..max] : text;
    }

    private static bool Equal(string? left, string? right) =>
        string.Equals(Clean(left, 200), Clean(right, 200), StringComparison.OrdinalIgnoreCase);

    private enum TransferPhase { Pick, Dispatch, Receive, Putaway }
}
