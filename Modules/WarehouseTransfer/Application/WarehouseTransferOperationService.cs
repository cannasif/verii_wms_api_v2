using System.Data;
using Microsoft.EntityFrameworkCore;
using verii_wms_api_v2.Modules.Audit.Application;
using verii_wms_api_v2.Modules.StockMovement.Application;
using verii_wms_api_v2.Modules.StockMovement.Domain;
using verii_wms_api_v2.Modules.StockBalance.Domain;
using verii_wms_api_v2.Modules.StockTracking.Application;
using verii_wms_api_v2.Modules.Location.Domain;
using verii_wms_api_v2.Modules.WarehouseOperations.Domain;
using verii_wms_api_v2.Modules.WarehouseTransfer.Domain;
using verii_wms_api_v2.Shared.Application.Abstractions.Persistence;
using verii_wms_api_v2.Shared.Application.Exceptions;
using verii_wms_api_v2.Shared.Application.Validation;
using WarehouseEntity = verii_wms_api_v2.Modules.Warehouse.Domain.Warehouse;

namespace verii_wms_api_v2.Modules.WarehouseTransfer.Application;

public sealed class WarehouseTransferOperationService(
    IUnitOfWork uow,
    IStockMovementService movements,
    IWarehouseTransferReservationService reservations,
    IStockTrackingPolicyResolver trackingPolicyResolver,
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
            var returnLocationId = await ResolveCancellationReturnLocationAsync(header, request, operations.Count > 0, token);
            long? lastReversalId = null;
            foreach (var operationId in operations)
            {
                var reversal = await movements.ReverseAsync(operationId,
                    new($"WT:{id}:CANCEL:{request.IdempotencyKey:N}:{operationId}", request.Reason!.Trim(), DateTime.UtcNow), token);
                lastReversalId = reversal.OperationId;
            }
            var returnTask = CreateCancellationReturnTask(header, returnLocationId, request.Reason!, actor);
            if (returnLocationId.HasValue) header.CancellationReturnLocationId = returnLocationId;
            await reservations.ReleaseAllAsync(header, $"WT:{id}:RESERVE:CANCEL:{request.IdempotencyKey:N}", request.Reason!.Trim(), actor, token);
            foreach (var line in header.Lines) line.Status = WarehouseTransferLineStatus.Cancelled;
            foreach (var task in header.Tasks.Where(x => x != returnTask)) task.Status = WarehouseTransferTaskStatus.Cancelled;
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
                NewValues: new { header.DocumentNo, ReversedOperationCount = operations.Count, LastReversalId = lastReversalId, ReturnTaskId = returnTask?.Id, header.CancellationReturnLocationId, header.CancellationReason },
                ChangedFields: ["Status", "Reservations", "StockMovement"]), token);
            return Result(header, lastReversalId, false);
        }, ct, IsolationLevel.Serializable);
    }

    private async Task<long?> ResolveCancellationReturnLocationAsync(
        WarehouseTransferHeader header,
        WarehouseTransferTransitionRequest request,
        bool hasPhysicalMovement,
        CancellationToken ct)
    {
        if (!hasPhysicalMovement) return null;

        var sourceLocationIds = header.Lines
            .SelectMany(x => x.Trackings.Where(t => t.PickedQuantity > 0).Select(t => t.SourceLocationId)
                .Append(x.DefaultSourceLocationId))
            .Where(x => x.HasValue).Select(x => x!.Value).Distinct().ToArray();
        var activeSourceIds = await uow.Repository<WarehouseLocation>().Query()
            .Where(x => sourceLocationIds.Contains(x.Id) && x.WarehouseId == header.SourceWarehouseId && x.IsActive)
            .Select(x => x.Id).ToListAsync(ct);
        var originalLocationsUsable = sourceLocationIds.Length > 0 && sourceLocationIds.All(activeSourceIds.Contains);

        long? selected = header.CancellationReturnPolicy switch
        {
            WarehouseTransferCancellationReturnPolicy.OriginalSourceLocation when originalLocationsUsable => null,
            WarehouseTransferCancellationReturnPolicy.ManagerSelectionRequired => request.ReturnLocationId ?? header.CancellationReturnLocationId
                ?? throw AppException.BadRequest("İptal politikası gereği kaynak depodan bir iade rafı seçilmelidir."),
            _ => (await uow.Repository<WarehouseEntity>().Query()
                    .Where(x => x.Id == header.SourceWarehouseId)
                    .Select(x => x.DefaultTransferReturnLocationId)
                    .SingleOrDefaultAsync(ct))
                ?? throw AppException.Conflict("Kaynak depoda varsayılan transfer iade rafı tanımlı değil. İptal tamamlanmadı."),
        };

        if (!selected.HasValue) return null;
        var valid = await uow.Repository<WarehouseLocation>().Query()
            .AnyAsync(x => x.Id == selected && x.WarehouseId == header.SourceWarehouseId && x.IsActive && x.IsPutaway, ct);
        if (!valid)
            throw AppException.BadRequest("Seçilen iade rafı kaynak depoya ait, aktif ve yerleştirmeye uygun olmalıdır.");
        return selected;
    }

    private static WarehouseTransferTask? CreateCancellationReturnTask(
        WarehouseTransferHeader header,
        long? returnLocationId,
        string reason,
        long actor)
    {
        var pickedLines = header.Lines.Where(x => x.PickedQuantity > 0).ToList();
        if (pickedLines.Count == 0) return null;
        var leftSourceWarehouse = header.Status is WarehouseTransferStatus.Shipped
            or WarehouseTransferStatus.PartiallyShipped
            or WarehouseTransferStatus.PartiallyReceived or WarehouseTransferStatus.Received
            or WarehouseTransferStatus.PartiallyPutaway or WarehouseTransferStatus.Completed
            || header.Lines.Any(x => x.ShippedQuantity > 0 || x.ReceivedQuantity > 0 || x.PutawayQuantity > 0);
        var now = DateTime.UtcNow;
        var task = new WarehouseTransferTask
        {
            BranchCode = header.BranchCode, CreatedBy = actor, CreatedDate = now, Header = header,
            TaskNo = $"{header.DocumentNo}-C01", TaskType = WarehouseTransferTaskType.CancellationReturn,
            WarehouseId = leftSourceWarehouse ? header.TargetWarehouseId : header.SourceWarehouseId,
            Status = WarehouseTransferTaskStatus.Open, Priority = 1,
            Description = leftSourceWarehouse
                ? $"Hedef depoya ulaşan transferi kaynak depoya geri gönderin. İptal nedeni: {Clean(reason, 400)}"
                : $"Toplanan stokları kaynak depodaki iade raflarına geri koyun. İptal nedeni: {Clean(reason, 400)}"
        };
        // Fiziksel stoğu aynı depoda en son yöneten kullanıcı, iade görevinin doğal sahibidir.
        // Depo değişmişse kullanıcıyı varsaymak yerine görev yönetici havuzunda açık kalır.
        var lastAssignee = header.Tasks
            .Where(x => x.TaskType != WarehouseTransferTaskType.CancellationReturn && x.WarehouseId == task.WarehouseId)
            .SelectMany(x => x.Assignments.Where(a => !a.IsDeleted))
            .OrderByDescending(x => x.AcceptedAtUtc ?? x.AssignedAtUtc)
            .ThenByDescending(x => x.Id)
            .FirstOrDefault();
        if (lastAssignee is not null)
        {
            task.Assignments.Add(new WarehouseTransferTaskAssignment
            {
                BranchCode = header.BranchCode,
                CreatedBy = actor,
                CreatedDate = now,
                Task = task,
                UserId = lastAssignee.UserId,
                IsPrimary = true,
                AssignedAtUtc = DateTimeOffset.UtcNow,
                AssignedBy = actor
            });
            task.Status = WarehouseTransferTaskStatus.Assigned;
        }
        foreach (var line in pickedLines)
        {
            var originalSources = line.Trackings.Where(x => x.PickedQuantity > 0 && x.SourceLocationId.HasValue)
                .Select(x => x.SourceLocationId!.Value).Append(line.DefaultSourceLocationId ?? 0).Where(x => x > 0).Distinct().ToArray();
            var physicalSources = leftSourceWarehouse
                ? line.Trackings.Where(x => x.PickedQuantity > 0 && x.TargetLocationId.HasValue)
                    .Select(x => x.TargetLocationId!.Value)
                    .Append(line.DefaultTargetLocationId ?? header.TargetPutawayLocationId ?? header.TargetReceivingLocationId ?? 0)
                    .Where(x => x > 0).Distinct().ToArray()
                : originalSources;
            task.Lines.Add(new WarehouseTransferTaskLine
            {
                BranchCode = header.BranchCode, CreatedBy = actor, CreatedDate = now, Task = task, Line = line,
                PlannedQuantity = line.PickedQuantity, ProcessedQuantity = 0,
                SourceLocationId = physicalSources.Length == 1 ? physicalSources[0] : null,
                TargetLocationId = returnLocationId ?? (originalSources.Length == 1 ? originalSources[0] : null)
            });
        }
        header.Tasks.Add(task);
        return task;
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
            if (phase == TransferPhase.Pick) EnsurePickerAssignment(header, actor, requestLines.Keys);
            ValidateQuantities(header, lines, requestLines, phase);
            await ValidateSerialSourceBalancesAsync(header, lines, requestLines, phase, token);
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

            if (phase == TransferPhase.Dispatch)
                SplitResidualProductionPickTask(header, actor);

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
                or WarehouseTransferStatus.Picking or WarehouseTransferStatus.PartiallyPicked
                or WarehouseTransferStatus.PartiallyShipped,
            TransferPhase.Dispatch => header.Status is WarehouseTransferStatus.Picked
                or WarehouseTransferStatus.PartiallyPicked or WarehouseTransferStatus.PartiallyShipped
                or WarehouseTransferStatus.Shipped
                || (!header.CreateTransitInventory && header.Status is
                    WarehouseTransferStatus.PartiallyReceived or WarehouseTransferStatus.Received),
            TransferPhase.Receive => header.Status is WarehouseTransferStatus.Shipped
                or WarehouseTransferStatus.PartiallyShipped or WarehouseTransferStatus.PartiallyReceived,
            TransferPhase.Putaway => header.Status is WarehouseTransferStatus.Received
                or WarehouseTransferStatus.PartiallyShipped or WarehouseTransferStatus.PartiallyReceived
                or WarehouseTransferStatus.PartiallyPutaway,
            _ => false
        };
        if (!allowed) throw AppException.Conflict($"{phase} işlemi mevcut {header.Status} durumunda yapılamaz.");
    }

    private static void EnsurePickerAssignment(
        WarehouseTransferHeader header,
        long actor,
        IEnumerable<long> requestedLineIds)
    {
        if (!header.RequireAssignee) return;
        var lineIds = requestedLineIds.ToHashSet();
        var task = header.Tasks
            .Where(x => x.TaskType == WarehouseTransferTaskType.Pick
                && x.Assignments.Any(a => !a.IsDeleted && a.UserId == actor)
                && x.Lines.Any(line => lineIds.Contains(line.WtLineId)
                    && line.ProcessedQuantity < line.PlannedQuantity))
            .OrderByDescending(x => x.StartedBy == actor && x.StartedAtUtc.HasValue)
            .ThenByDescending(x => x.Id)
            .FirstOrDefault()
            ?? throw AppException.Conflict("Transfer toplama emri bulunamadı.");
        var assignment = task.Assignments.FirstOrDefault(x => !x.IsDeleted && x.UserId == actor)
            ?? throw AppException.Forbidden("Bu transfer toplama emri size atanmamış.");
        var productionContext = header.BusinessContext is WarehouseTransferBusinessContext.ProductionMaterialSupply
            or WarehouseTransferBusinessContext.ProductionWipMove
            or WarehouseTransferBusinessContext.ProductionOutputMove;
        if (!productionContext)
        {
            var now = DateTimeOffset.UtcNow;
            assignment.AcceptedAtUtc ??= now;
            task.AcceptedAtUtc ??= now;
            task.AcceptedBy ??= actor;
            task.StartedAtUtc ??= now;
            task.StartedBy ??= actor;
            if (task.Status is WarehouseTransferTaskStatus.Open or WarehouseTransferTaskStatus.Assigned)
                task.Status = WarehouseTransferTaskStatus.InProgress;
            return;
        }
        if (!assignment.AcceptedAtUtc.HasValue || task.StartedBy != actor
            || task.Status is not (WarehouseTransferTaskStatus.InProgress or WarehouseTransferTaskStatus.PartiallyCompleted))
            throw AppException.Conflict("Toplamaya başlamadan önce görev ekranından 'Bu işi yapıyorum' işlemini kullanın.");
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
            var pickCeiling = phase == TransferPhase.Pick
                ? requests[line.Id].MaxPickQuantity ?? line.RequestedQuantity
                : line.RequestedQuantity;
            var available = phase switch
            {
                TransferPhase.Pick => pickCeiling - line.PickedQuantity,
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

    private async Task ValidateSerialSourceBalancesAsync(
        WarehouseTransferHeader header,
        IReadOnlyCollection<WarehouseTransferLine> lines,
        IReadOnlyDictionary<long,WarehouseTransferOperationLineRequest> requests,
        TransferPhase phase,
        CancellationToken ct)
    {
        var selections=lines
            .Select(line=>new{Line=line,Request=requests[line.Id]})
            .Where(x=>!string.IsNullOrWhiteSpace(x.Request.SerialNo))
            .Select(x=>new
            {
                x.Line,
                x.Request,
                SourceLocationId=phase switch
                {
                    TransferPhase.Pick=>x.Request.SourceLocationId??x.Line.DefaultSourceLocationId,
                    TransferPhase.Dispatch=>x.Request.SourceLocationId??header.SourceStagingLocationId,
                    TransferPhase.Receive=>x.Request.SourceLocationId??header.TargetReceivingLocationId,
                    TransferPhase.Putaway=>x.Request.SourceLocationId??header.TargetReceivingLocationId,
                    _=>null
                },
                SourceWarehouseId=phase is TransferPhase.Pick or TransferPhase.Dispatch
                    ?header.SourceWarehouseId:header.TargetWarehouseId,
                SourceStatus=phase==TransferPhase.Receive&&header.CreateTransitInventory
                    ?"InTransit":phase is TransferPhase.Pick or TransferPhase.Dispatch
                        ?x.Line.SourceStockStatus:x.Line.TargetStockStatus
            })
            .Where(x=>x.SourceLocationId.HasValue)
            .ToArray();
        if(selections.Length==0)return;

        var stockIds=selections.Select(x=>x.Line.StockId).Distinct().ToArray();
        var warehouseIds=selections.Select(x=>x.SourceWarehouseId).Distinct().ToArray();
        var locationIds=selections.Select(x=>x.SourceLocationId!.Value).Distinct().ToArray();
        var serials=selections.Select(x=>x.Request.SerialNo!.Trim().ToUpperInvariant()).Distinct().ToArray();
        var rows=await uow.Repository<LocationStockBalance>().Query()
            .Where(x=>stockIds.Contains(x.StockId)
                && warehouseIds.Contains(x.WarehouseId)
                && locationIds.Contains(x.LocationId)
                && x.SerialNo!=null
                && serials.Contains(x.SerialNo))
            .Select(x=>new{x.StockId,x.YapCodeId,x.WarehouseId,x.LocationId,x.UnitCode,x.LotNo,x.SerialNo,x.StockStatus,x.AvailableQuantity})
            .ToListAsync(ct);
        var balances=rows
            .GroupBy(x=>WarehouseTransferSerialBalanceKey.Create(x.StockId,x.YapCodeId,x.WarehouseId,x.LocationId,x.UnitCode,x.LotNo,x.SerialNo!,x.StockStatus))
            .ToDictionary(x=>x.Key,x=>x.Sum(row=>row.AvailableQuantity));
        var policies=new Dictionary<long,EffectiveStockTrackingPolicy>();
        foreach(var stockId in stockIds)
            policies[stockId]=await trackingPolicyResolver.ResolveAsync(header.BranchCode,stockId,ct);

        foreach(var selection in selections)
        {
            var line=selection.Line;
            var request=selection.Request;
            var serial=request.SerialNo!.Trim().ToUpperInvariant();
            var key=WarehouseTransferSerialBalanceKey.Create(line.StockId,line.YapCodeId,selection.SourceWarehouseId,
                selection.SourceLocationId!.Value,line.UnitCode,request.LotNo,serial,selection.SourceStatus);
            try
            {
                StockTrackingPolicyGuard.ValidateSerialMovementQuantity(
                    policies[line.StockId],request.Quantity,balances.GetValueOrDefault(key),serial);
            }
            catch(StockTrackingPolicyViolationException ex)
            {
                throw AppException.Conflict(ex.Message);
            }
        }
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
            var sourceStatus = phase == TransferPhase.Receive && header.CreateTransitInventory
                ? "InTransit"
                : phase is TransferPhase.Pick or TransferPhase.Dispatch
                    ? line.SourceStockStatus
                    : line.TargetStockStatus;
            var targetStatus = phase == TransferPhase.Dispatch && header.CreateTransitInventory
                ? "InTransit"
                : phase == TransferPhase.Pick ? line.SourceStockStatus : line.TargetStockStatus;
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
        header.WaybillNo = PurchaseWaybillNumberPolicy.Normalize(request.WaybillNo) ?? header.WaybillNo;
        if (header.RequireShipmentInformation
            && string.IsNullOrWhiteSpace(header.VehiclePlate)
            && string.IsNullOrWhiteSpace(header.CarrierCode))
            throw AppException.BadRequest("Sevk için araç plakası veya taşıyıcı bilgisi zorunludur.");
    }

    private static void UpdatePickTask(WarehouseTransferHeader header, WarehouseTransferLine line, decimal quantity, long actor)
    {
        var task = header.Tasks
            .Where(x => x.TaskType == WarehouseTransferTaskType.Pick
                && x.StartedBy == actor
                && x.Status is WarehouseTransferTaskStatus.InProgress or WarehouseTransferTaskStatus.PartiallyCompleted)
            .OrderByDescending(x => x.Id)
            .FirstOrDefault(x => x.Lines.Any(taskLine => taskLine.WtLineId == line.Id
                && taskLine.ProcessedQuantity < taskLine.PlannedQuantity));
        var taskLine = task?.Lines.FirstOrDefault(x => x.WtLineId == line.Id);
        if (taskLine is null) return;
        taskLine.ProcessedQuantity += quantity;
        taskLine.UpdatedBy = actor;
        taskLine.UpdatedDate = DateTime.UtcNow;

        var isProduction = header.BusinessContext is WarehouseTransferBusinessContext.ProductionMaterialSupply
            or WarehouseTransferBusinessContext.ProductionWipMove
            or WarehouseTransferBusinessContext.ProductionOutputMove;
        if (isProduction)
        {
            if (task!.Status is WarehouseTransferTaskStatus.Open or WarehouseTransferTaskStatus.Assigned)
                task.Status = WarehouseTransferTaskStatus.InProgress;
            return;
        }

        if (task!.Lines.All(x => x.ProcessedQuantity >= x.PlannedQuantity))
        {
            task.Status = WarehouseTransferTaskStatus.Completed;
            task.CompletedAtUtc = DateTimeOffset.UtcNow;
            task.CompletedBy = actor;
        }
        else task.Status = WarehouseTransferTaskStatus.PartiallyCompleted;
    }

    private static void SplitResidualProductionPickTask(WarehouseTransferHeader header, long actor)
    {
        if (header.BusinessContext is not (WarehouseTransferBusinessContext.ProductionMaterialSupply
            or WarehouseTransferBusinessContext.ProductionWipMove
            or WarehouseTransferBusinessContext.ProductionOutputMove)) return;

        var residualLines = header.Lines
            .Select(line => new { Line = line, Quantity = Math.Max(0, line.RequestedQuantity - line.PickedQuantity) })
            .Where(x => x.Quantity > 0)
            .ToArray();
        if (residualLines.Length == 0) return;
        var current = header.Tasks
            .Where(x => x.TaskType == WarehouseTransferTaskType.Pick
                && x.StartedBy == actor
                && x.Status == WarehouseTransferTaskStatus.PartiallyCompleted)
            .OrderByDescending(x => x.Id)
            .FirstOrDefault();
        if (current is null) return;

        foreach (var line in current.Lines)
            line.PlannedQuantity = line.ProcessedQuantity;
        current.Status = WarehouseTransferTaskStatus.Completed;
        current.CompletedAtUtc = DateTimeOffset.UtcNow;
        current.CompletedBy = actor;
        current.UpdatedBy = actor;
        current.UpdatedDate = DateTime.UtcNow;

        var sequence = header.Tasks.Count(x => x.TaskType == WarehouseTransferTaskType.Pick);
        var next = new WarehouseTransferTask
        {
            BranchCode = header.BranchCode,
            Header = header,
            TaskNo = $"{header.DocumentNo}-P01-{sequence}",
            TaskType = WarehouseTransferTaskType.Pick,
            WarehouseId = header.SourceWarehouseId,
            Status = WarehouseTransferTaskStatus.Assigned,
            Priority = current.Priority,
            PlannedAtUtc = DateTimeOffset.UtcNow,
            Description = $"{header.DocumentNo} kısmi sevkinden kalan toplama işi ({sequence}).",
            CreatedBy = actor,
            CreatedDate = DateTime.UtcNow
        };
        // Kısmi sevk sonrası kalan işi, önceki işi yapan kullanıcıya otomatik atar — aksi halde görev
        // kimseye atanmamış kalır ve kullanıcı devam etmeye çalıştığında "toplama emri bulunamadı" hatası alır.
        next.Assignments.Add(new WarehouseTransferTaskAssignment
        {
            BranchCode = header.BranchCode,
            CreatedBy = actor,
            CreatedDate = DateTime.UtcNow,
            Task = next,
            UserId = actor,
            IsPrimary = true,
            AssignedAtUtc = DateTimeOffset.UtcNow,
            AssignedBy = actor
        });
        foreach (var residual in residualLines)
            next.Lines.Add(new WarehouseTransferTaskLine
            {
                BranchCode = header.BranchCode,
                Task = next,
                Line = residual.Line,
                PlannedQuantity = residual.Quantity,
                ProcessedQuantity = 0,
                SourceLocationId = residual.Line.DefaultSourceLocationId,
                CreatedBy = actor,
                CreatedDate = DateTime.UtcNow
            });
        header.Tasks.Add(next);
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
        {
            if (phase == TransferPhase.Pick
                && request.MaxPickQuantity.HasValue
                && line.PickedQuantity + request.Quantity <= request.MaxPickQuantity.Value + 0.000001m
                && tracking.PickedQuantity + request.Quantity
                    <= Math.Max(tracking.PlannedQuantity, tracking.PickedQuantity + request.Quantity) + 0.000001m)
                return;
            throw AppException.Conflict(
                $"{line.LineNo}. satırın seçilen seri/lot boyutunda kullanılabilir miktarı {available}, istenen {request.Quantity}.");
        }
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

        tracking.PlannedQuantity = Math.Max(tracking.PlannedQuantity, tracking.PickedQuantity + request.Quantity);
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
            TransferPhase.Dispatch when header.CreateTransitInventory
                && all.All(x => x.ShippedQuantity >= x.RequestedQuantity) => WarehouseTransferStatus.Shipped,
            TransferPhase.Dispatch when header.CreateTransitInventory => WarehouseTransferStatus.PartiallyShipped,
            TransferPhase.Dispatch when all.All(x => x.ReceivedQuantity >= x.RequestedQuantity) => WarehouseTransferStatus.Received,
            TransferPhase.Dispatch => WarehouseTransferStatus.PartiallyReceived,
            TransferPhase.Receive when all.All(x => x.ReceivedQuantity >= x.ShippedQuantity)
                && all.All(x => x.ShippedQuantity >= x.RequestedQuantity) => WarehouseTransferStatus.Received,
            TransferPhase.Receive when all.All(x => x.ReceivedQuantity >= x.ShippedQuantity) => WarehouseTransferStatus.PartiallyShipped,
            TransferPhase.Receive => WarehouseTransferStatus.PartiallyReceived,
            TransferPhase.Putaway when all.All(x => x.PutawayQuantity >= x.ReceivedQuantity)
                && all.All(x => x.ShippedQuantity >= x.RequestedQuantity) => WarehouseTransferStatus.Completed,
            TransferPhase.Putaway when all.All(x => x.PutawayQuantity >= x.ReceivedQuantity) => WarehouseTransferStatus.PartiallyShipped,
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
