using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using verii_wms_api_v2.Modules.Audit.Application;
using verii_wms_api_v2.Modules.GoodsReceipt.Domain;
using verii_wms_api_v2.Modules.Location.Domain;
using verii_wms_api_v2.Modules.Quality.Domain;
using verii_wms_api_v2.Modules.StockMovement.Application;
using verii_wms_api_v2.Modules.StockMovement.Domain;
using verii_wms_api_v2.Modules.WarehouseOperations.Domain;
using verii_wms_api_v2.Shared.Application.Abstractions.Persistence;
using verii_wms_api_v2.Shared.Application.Exceptions;

namespace verii_wms_api_v2.Modules.GoodsReceipt.Application;

public sealed class GoodsReceiptLifecycleService(
    IUnitOfWork uow,
    IStockMovementService movements,
    IAuditLogWriter audit) : IGoodsReceiptLifecycleService
{
    private IGenericRepository<GoodsReceiptHeader> Headers => uow.Repository<GoodsReceiptHeader>();
    private IGenericRepository<GoodsReceiptStatusHistory> Histories => uow.Repository<GoodsReceiptStatusHistory>();
    private IGenericRepository<StockMovementOperation> MovementOperations => uow.Repository<StockMovementOperation>();

    public Task<GoodsReceiptLifecycleResult> ApproveAsync(
        long id,
        GoodsReceiptTransitionRequest request,
        long actor,
        CancellationToken cancellationToken = default)
    {
        ValidateTransition(id, request, requireReason: false);
        var normalizedReason = Clean(request.Reason, 500);
        var hash = Hash(new { Operation = "Approve", Id = id, request.IdempotencyKey, Reason = normalizedReason });
        return uow.ExecuteInTransactionAsync(async ct =>
        {
            var header = await LoadAsync(id, ct);
            if (await IsReplayAsync(header, request.IdempotencyKey, hash, ct))
                return Result(header, null, 0, true);

            ApplyVersion(header, request.RowVersion);
            if (!header.RequireReceiptApproval)
                throw AppException.Conflict("Bu mal kabul için operasyon onayı gerekmiyor.");
            if (header.ApprovalStatus == OperationApprovalStatus.Rejected)
                throw AppException.Conflict("Reddedilmiş mal kabul onaylanamaz.");
            if (header.Status == WarehouseOperationStatus.Cancelled)
                throw AppException.Conflict("İptal edilmiş mal kabul onaylanamaz.");

            var from = header.ApprovalStatus.ToString();
            header.ApprovalStatus = OperationApprovalStatus.Approved;
            Touch(header, actor);
            AddHistory(header, GoodsReceiptStatusArea.Approval, from, header.ApprovalStatus.ToString(),
                request.IdempotencyKey, hash, normalizedReason, actor);
            await SaveAsync(ct);
            await WriteAudit("approve", header, null, 0, actor, ct);
            return Result(header, null, 0, false);
        }, cancellationToken, IsolationLevel.Serializable);
    }

    public Task<GoodsReceiptLifecycleResult> ShortCloseAsync(
        long id,
        ShortCloseGoodsReceiptRequest request,
        long actor,
        CancellationToken cancellationToken = default)
    {
        if (id <= 0 || request.IdempotencyKey == Guid.Empty || string.IsNullOrWhiteSpace(request.Reason)
            || request.Lines is not { Count: > 0 and <= 200 }
            || request.Lines.Any(x => x.LineId <= 0 || x.Quantity <= 0)
            || request.Lines.GroupBy(x => x.LineId).Any(x => x.Count() > 1))
            throw AppException.BadRequest("Kısa kapama isteği ve satırları geçersizdir.");

        var normalizedLines = request.Lines.OrderBy(x => x.LineId).ToArray();
        var reason = Clean(request.Reason, 500)!;
        var hash = Hash(new { Operation = "ShortClose", Id = id, request.IdempotencyKey, Reason = reason, Lines = normalizedLines });
        return uow.ExecuteInTransactionAsync(async ct =>
        {
            var header = await LoadAsync(id, ct);
            if (await IsReplayAsync(header, request.IdempotencyKey, hash, ct))
                return Result(header, null, normalizedLines.Sum(x => x.Quantity), true);

            ApplyVersion(header, request.RowVersion);
            if (!header.AllowUnderReceipt)
                throw AppException.Conflict("Mal kabul politikası eksik kabul/kısa kapamaya izin vermiyor.");
            if (header.RequireShortCloseApproval && header.ApprovalStatus != OperationApprovalStatus.Approved)
                throw AppException.Conflict("Kısa kapama yapılmadan önce mal kabul onaylanmalıdır.");
            if (header.Status is WarehouseOperationStatus.Cancelled or WarehouseOperationStatus.Completed)
                throw AppException.Conflict("İptal edilmiş veya tamamlanmış mal kabul kısa kapatılamaz.");

            var requested = normalizedLines.ToDictionary(x => x.LineId);
            var lines = header.Lines.Where(x => requested.ContainsKey(x.Id)).ToList();
            if (lines.Count != requested.Count)
                throw AppException.BadRequest("Kısa kapama satırlarından biri bu mal kabule ait değil.");

            foreach (var line in lines)
            {
                var quantity = requested[line.Id].Quantity;
                var remaining = Math.Max(0, line.ExpectedQuantity - line.ReceivedQuantity - line.ShortClosedQuantity);
                if (quantity > remaining)
                    throw AppException.Conflict($"{line.LineNo}. satırda kısa kapatılabilir miktar {remaining}, istenen {quantity}.");
                line.ShortClosedQuantity += quantity;
                if (line.ReceivedQuantity + line.ShortClosedQuantity >= line.ExpectedQuantity)
                    line.Status = GoodsReceiptLineStatus.ShortClosed;
                line.UpdatedBy = actor;
                line.UpdatedDate = DateTime.UtcNow;
            }

            RefreshCompletion(header, actor);
            Touch(header, actor);
            AddHistory(header, GoodsReceiptStatusArea.Operation, null, "ShortClosed",
                request.IdempotencyKey, hash, reason, actor);
            await SaveAsync(ct);
            var affected = normalizedLines.Sum(x => x.Quantity);
            await WriteAudit("short-close", header, null, affected, actor, ct);
            return Result(header, null, affected, false);
        }, cancellationToken, IsolationLevel.Serializable);
    }

    public Task<GoodsReceiptLifecycleResult> PutawayAsync(
        long id,
        PutawayGoodsReceiptRequest request,
        long actor,
        CancellationToken cancellationToken = default)
    {
        if (id <= 0 || request.IdempotencyKey == Guid.Empty || request.Lines is not { Count: > 0 and <= 200 }
            || request.Lines.Any(x => x.LineId <= 0 || x.Quantity <= 0 || x.TargetLocationId <= 0)
            || request.Lines.Any(x => !string.IsNullOrWhiteSpace(x.SerialNo) && x.Quantity != 1))
            throw AppException.BadRequest("Yerleştirme isteği veya satırları geçersizdir.");

        var normalizedLines = request.Lines
            .Select(x => x with { LotNo = Clean(x.LotNo, 100), SerialNo = Clean(x.SerialNo, 100) })
            .OrderBy(x => x.LineId).ThenBy(x => x.SerialNo).ThenBy(x => x.LotNo).ToArray();
        var reason = Clean(request.Reason, 500);
        var occurredAt = request.OccurredAtUtc?.ToUniversalTime();
        var hash = Hash(new { Operation = "Putaway", Id = id, request.IdempotencyKey, Reason = reason, OccurredAtUtc = occurredAt, Lines = normalizedLines });
        var movementKey = $"GR:{id}:PUTAWAY:{request.IdempotencyKey:N}";

        return uow.ExecuteInTransactionAsync(async ct =>
        {
            var header = await LoadAsync(id, ct);
            if (await IsReplayAsync(header, request.IdempotencyKey, hash, ct))
            {
                var replayMovement = await MovementOperations.Query()
                    .FirstOrDefaultAsync(x => x.IdempotencyKey == movementKey, ct);
                return Result(header, replayMovement?.Id, normalizedLines.Sum(x => x.Quantity), true);
            }

            ApplyVersion(header, request.RowVersion);
            if (!header.RequirePutaway)
                throw AppException.Conflict("Bu mal kabul için raf yerleştirme gerekmiyor.");
            if (header.Status == WarehouseOperationStatus.Cancelled)
                throw AppException.Conflict("İptal edilmiş mal kabul yerleştirilemez.");
            if (header.BlockPutawayUntilQualityDecision
                && header.QualityStatus is OperationQualityStatus.Pending or OperationQualityStatus.InProgress or OperationQualityStatus.PartiallyCompleted)
                throw AppException.Conflict("Kalite kararı tamamlanmadan raf yerleştirme yapılamaz.");

            var requestedByLine = normalizedLines.GroupBy(x => x.LineId)
                .ToDictionary(x => x.Key, x => x.Sum(v => v.Quantity));
            var lines = header.Lines.Where(x => requestedByLine.ContainsKey(x.Id)).ToDictionary(x => x.Id);
            if (lines.Count != requestedByLine.Count)
                throw AppException.BadRequest("Yerleştirme satırlarından biri bu mal kabule ait değil.");
            foreach (var pair in requestedByLine)
            {
                var line = lines[pair.Key];
                var available = Math.Max(0, line.AcceptedQuantity - line.PutawayQuantity);
                if (pair.Value > available)
                    throw AppException.Conflict($"{line.LineNo}. satırda yerleştirilebilir miktar {available}, istenen {pair.Value}.");
            }

            var locationIds = normalizedLines.SelectMany(x => new long?[]
                { x.SourceLocationId, x.TargetLocationId }).Where(x => x.HasValue).Select(x => x!.Value).Distinct().ToArray();
            var locations = await uow.Repository<WarehouseLocation>().Query()
                .Where(x => locationIds.Contains(x.Id) && x.IsActive).ToDictionaryAsync(x => x.Id, ct);
            if (locations.Count != locationIds.Length)
                throw AppException.BadRequest("Yerleştirme için seçilen raflardan biri pasif veya geçersiz.");

            var movementLines = normalizedLines.Select(item =>
            {
                var line = lines[item.LineId];
                var sourceId = item.SourceLocationId ?? line.DefaultReceivingLocationId ?? header.ReceivingLocationId;
                if (!locations.TryGetValue(sourceId, out var source)
                    || source.WarehouseId != line.TargetWarehouseId)
                    throw AppException.BadRequest($"{line.LineNo}. satırın kaynak rafı mal kabul deposuna ait değil.");
                var target = locations[item.TargetLocationId];
                if (target.WarehouseId != line.TargetWarehouseId || !target.IsPutaway)
                    throw AppException.BadRequest($"{line.LineNo}. satırın hedef rafı aktif bir yerleştirme rafı olmalıdır.");
                if (source.Id == target.Id)
                    throw AppException.BadRequest("Kaynak ve hedef raf aynı olamaz.");

                return new StockMovementLineRequest(
                    line.StockId, line.YapCodeId, item.Quantity,
                    line.TargetWarehouseId, source.Id,
                    line.TargetWarehouseId, target.Id,
                    line.UnitCode, item.LotNo, item.SerialNo, "Available");
            }).ToList();

            var movement = await movements.PostAsync(new PostStockMovementRequest(
                movementKey,
                StockMovementTypes.Transfer,
                "GoodsReceipt",
                header.DocumentNo,
                header.Id,
                occurredAt?.UtcDateTime,
                "GoodsReceiptPutaway",
                reason,
                movementLines), ct);

            foreach (var pair in requestedByLine)
            {
                var line = lines[pair.Key];
                line.PutawayQuantity += pair.Value;
                line.UpdatedBy = actor;
                line.UpdatedDate = DateTime.UtcNow;
            }
            header.PutawayStatus = header.Lines.All(x => x.PutawayQuantity >= x.AcceptedQuantity)
                ? OperationPutawayStatus.Completed
                : header.Lines.Any(x => x.PutawayQuantity > 0)
                    ? OperationPutawayStatus.PartiallyCompleted
                    : OperationPutawayStatus.InProgress;
            RefreshCompletion(header, actor);
            Touch(header, actor);
            AddHistory(header, GoodsReceiptStatusArea.Putaway, null, header.PutawayStatus.ToString(),
                request.IdempotencyKey, hash, reason, actor);
            await SaveAsync(ct);
            var affected = normalizedLines.Sum(x => x.Quantity);
            await WriteAudit("putaway", header, movement.OperationId, affected, actor, ct);
            return Result(header, movement.OperationId, affected, false);
        }, cancellationToken, IsolationLevel.Serializable);
    }

    public Task<GoodsReceiptLifecycleResult> CancelAsync(
        long id,
        GoodsReceiptTransitionRequest request,
        long actor,
        CancellationToken cancellationToken = default)
    {
        ValidateTransition(id, request, requireReason: true);
        var reason = Clean(request.Reason, 500)!;
        var hash = Hash(new { Operation = "Cancel", Id = id, request.IdempotencyKey, Reason = reason });
        return uow.ExecuteInTransactionAsync(async ct =>
        {
            var header = await LoadAsync(id, ct);
            if (await IsReplayAsync(header, request.IdempotencyKey, hash, ct))
            {
                var lastReplay = await MovementOperations.Query()
                    .Where(x => x.IdempotencyKey.StartsWith($"GR:{id}:CANCEL:{request.IdempotencyKey:N}:"))
                    .OrderByDescending(x => x.Id).FirstOrDefaultAsync(ct);
                return Result(header, lastReplay?.Id, header.Lines.Sum(x => x.ReceivedQuantity), true);
            }

            ApplyVersion(header, request.RowVersion);
            if (header.Status == WarehouseOperationStatus.Cancelled)
                throw AppException.Conflict("Mal kabul zaten iptal edilmiş.");
            if (header.ErpIntegrationStatus is ErpIntegrationStatus.Processing or ErpIntegrationStatus.Succeeded or ErpIntegrationStatus.CommitUncertain)
                throw AppException.Conflict("ERP aktarımı başlamış veya tamamlanmış mal kabul WMS üzerinden iptal edilemez.");

            var qualityIds = await uow.Repository<QualityInspection>().Query()
                .Where(x => x.SourceDocumentType == "GoodsReceipt" && x.SourceDocumentId == id)
                .Select(x => x.Id).ToListAsync(ct);
            var operationIds = await MovementOperations.Query()
                .Where(x => x.OperationType != StockMovementTypes.Reversal
                    && ((x.ReferenceType == "GoodsReceipt" && x.ReferenceId == id)
                        || (x.ReferenceType == "QualityInspection" && x.ReferenceId.HasValue
                            && qualityIds.Contains(x.ReferenceId.Value)))
                    && !MovementOperations.Query().Any(r => r.ReversalOfOperationId == x.Id))
                .OrderByDescending(x => x.Id).Select(x => x.Id).ToListAsync(ct);

            long? lastReversalId = null;
            foreach (var operationId in operationIds)
            {
                var reversal = await movements.ReverseAsync(operationId,
                    new ReverseStockMovementRequest(
                        $"GR:{id}:CANCEL:{request.IdempotencyKey:N}:{operationId}",
                        reason,
                        DateTime.UtcNow), ct);
                lastReversalId = reversal.OperationId;
            }

            var now = DateTimeOffset.UtcNow;
            foreach (var line in header.Lines) line.Status = GoodsReceiptLineStatus.Cancelled;
            foreach (var task in header.Tasks)
            {
                task.Status = GoodsReceiptTaskStatus.Cancelled;
                task.CancelledAtUtc = now;
                task.CancellationReason = reason;
                foreach (var taskLine in task.Lines) taskLine.Status = GoodsReceiptTaskStatus.Cancelled;
            }
            var executions = await uow.Repository<GoodsReceiptExecution>().Query(true)
                .Where(x => x.GrHeaderId == id && x.Status == GoodsReceiptExecutionStatus.Posted).ToListAsync(ct);
            foreach (var execution in executions) execution.Status = GoodsReceiptExecutionStatus.Reversed;

            var labels = await uow.Repository<GoodsReceiptLabel>().Query(true)
                .Where(x => x.GrHeaderId == id && x.Status != GoodsReceiptLabelStatus.Consumed
                    && x.Status != GoodsReceiptLabelStatus.Void).ToListAsync(ct);
            foreach (var label in labels)
            {
                label.Status = GoodsReceiptLabelStatus.Void;
                label.VoidReason = reason;
                label.UpdatedBy = actor;
                label.UpdatedDate = DateTime.UtcNow;
            }

            var from = header.Status.ToString();
            header.Status = WarehouseOperationStatus.Cancelled;
            header.CancelledAtUtc = now;
            header.CancelledBy = actor;
            header.CancellationReason = reason;
            Touch(header, actor);
            AddHistory(header, GoodsReceiptStatusArea.Operation, from, header.Status.ToString(),
                request.IdempotencyKey, hash, reason, actor);
            await SaveAsync(ct);
            var affected = header.Lines.Sum(x => x.ReceivedQuantity);
            await WriteAudit("cancel", header, lastReversalId, affected, actor, ct);
            return Result(header, lastReversalId, affected, false);
        }, cancellationToken, IsolationLevel.Serializable);
    }

    private async Task<GoodsReceiptHeader> LoadAsync(long id, CancellationToken ct) =>
        await Headers.Query(true)
            .Include(x => x.Lines)
            .Include(x => x.Tasks).ThenInclude(x => x.Lines)
            .Include(x => x.StatusHistory)
            .FirstOrDefaultAsync(x => x.Id == id, ct)
        ?? throw AppException.NotFound("Mal kabul kaydı bulunamadı.");

    private async Task<bool> IsReplayAsync(
        GoodsReceiptHeader header,
        Guid idempotencyKey,
        string requestHash,
        CancellationToken ct)
    {
        var history = await Histories.Query()
            .FirstOrDefaultAsync(x => x.GrHeaderId == header.Id && x.CorrelationId == idempotencyKey, ct);
        if (history is null) return false;
        if (string.IsNullOrWhiteSpace(history.RequestHash)
            || !CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(history.RequestHash),
                Convert.FromHexString(requestHash)))
            throw AppException.Conflict("Aynı idempotency anahtarı farklı bir mal kabul operasyonunda kullanılamaz.");
        return true;
    }

    private static void AddHistory(
        GoodsReceiptHeader header,
        GoodsReceiptStatusArea area,
        string? from,
        string to,
        Guid key,
        string requestHash,
        string? reason,
        long actor) =>
        header.StatusHistory.Add(new GoodsReceiptStatusHistory
        {
            BranchCode = header.BranchCode,
            StatusArea = area,
            FromStatus = from,
            ToStatus = to,
            ChangedAtUtc = DateTimeOffset.UtcNow,
            ChangedBy = actor,
            Description = reason,
            CorrelationId = key,
            RequestHash = requestHash,
            CreatedBy = actor,
            CreatedDate = DateTime.UtcNow
        });

    private static void RefreshCompletion(GoodsReceiptHeader header, long actor)
    {
        var receiptTerminal = header.Lines.All(x =>
            x.ReceivedQuantity + x.ShortClosedQuantity >= x.ExpectedQuantity);
        var qualityTerminal = header.QualityStatus is OperationQualityStatus.NotRequired
            or OperationQualityStatus.Passed or OperationQualityStatus.Failed;
        var putawayTerminal = !header.RequirePutaway
            || header.Lines.All(x => x.PutawayQuantity >= x.AcceptedQuantity);
        if (!receiptTerminal || !qualityTerminal || !putawayTerminal) return;
        header.Status = WarehouseOperationStatus.Completed;
        header.CompletedAtUtc ??= DateTimeOffset.UtcNow;
        header.CompletedBy ??= actor;
        if (!header.RequirePutaway) header.PutawayStatus = OperationPutawayStatus.NotRequired;
    }

    private static void ValidateTransition(long id, GoodsReceiptTransitionRequest request, bool requireReason)
    {
        if (id <= 0 || request.IdempotencyKey == Guid.Empty || string.IsNullOrWhiteSpace(request.RowVersion)
            || requireReason && string.IsNullOrWhiteSpace(request.Reason))
            throw AppException.BadRequest("Mal kabul, idempotency anahtarı ve güncel satır versiyonu zorunludur.");
    }

    private static void ApplyVersion(GoodsReceiptHeader header, string supplied)
    {
        byte[] expected;
        try { expected = Convert.FromBase64String(supplied); }
        catch { throw AppException.BadRequest("Mal kabul satır versiyonu geçersiz."); }
        if (expected.Length == 0 || header.RowVersion.Length != expected.Length
            || !CryptographicOperations.FixedTimeEquals(header.RowVersion, expected))
            throw AppException.Conflict("Mal kabul başka bir kullanıcı veya işlem tarafından güncellendi. Kaydı yenileyip tekrar deneyin.");
    }

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        try { await uow.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException)
        {
            throw AppException.Conflict("Mal kabul başka bir kullanıcı veya işlem tarafından güncellendi. Kaydı yenileyip tekrar deneyin.");
        }
    }

    private static void Touch(GoodsReceiptHeader header, long actor)
    {
        header.UpdatedBy = actor;
        header.UpdatedDate = DateTime.UtcNow;
    }

    private static string Hash(object value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value))));

    private static string? Clean(string? value, int max)
    {
        var text = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        if (text?.Length > max) throw AppException.BadRequest($"Alan uzunluğu en fazla {max} olabilir.");
        return text;
    }

    private Task WriteAudit(
        string operation,
        GoodsReceiptHeader header,
        long? movementId,
        decimal quantity,
        long actor,
        CancellationToken ct) =>
        audit.WriteAsync(new AuditLogWriteEntry(
            $"goods-receipt.{operation}",
            nameof(GoodsReceiptHeader),
            header.Id.ToString(),
            "Succeeded",
            "goods-receipt",
            NewValues: new
            {
                header.DocumentNo,
                header.Status,
                header.ApprovalStatus,
                header.QualityStatus,
                header.PutawayStatus,
                MovementId = movementId,
                Quantity = quantity,
                Actor = actor
            },
            ChangedFields: ["Status", "Quantities", "StockMovement"]), ct);

    private static GoodsReceiptLifecycleResult Result(
        GoodsReceiptHeader header,
        long? movementId,
        decimal quantity,
        bool replayed) =>
        new(
            header.Id,
            header.DocumentNo,
            header.Status,
            header.ApprovalStatus,
            header.QualityStatus,
            header.PutawayStatus,
            movementId,
            quantity,
            replayed,
            Convert.ToBase64String(header.RowVersion));
}
