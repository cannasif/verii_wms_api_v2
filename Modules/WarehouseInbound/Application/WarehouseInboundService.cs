using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using verii_wms_api_v2.Modules.Audit.Application;
using verii_wms_api_v2.Modules.DocumentSeries.Application;
using verii_wms_api_v2.Modules.DocumentSeries.Domain;
using verii_wms_api_v2.Modules.WarehouseInbound.Domain;
using verii_wms_api_v2.Modules.WarehouseInbound.Localization;
using verii_wms_api_v2.Modules.Identity.Domain;
using verii_wms_api_v2.Modules.Location.Domain;
using verii_wms_api_v2.Modules.Quality.Application;
using verii_wms_api_v2.Modules.Quality.Domain;
using verii_wms_api_v2.Modules.StockTracking.Application;
using verii_wms_api_v2.Modules.WarehouseOperations.Domain;
using verii_wms_api_v2.Shared.Application.Abstractions.Persistence;
using verii_wms_api_v2.Shared.Application.Exceptions;
using CustomerEntity = verii_wms_api_v2.Modules.Customer.Domain.Customer;
using StockEntity = verii_wms_api_v2.Modules.Stock.Domain.Stock;
using WarehouseEntity = verii_wms_api_v2.Modules.Warehouse.Domain.Warehouse;
using YapCodeEntity = verii_wms_api_v2.Modules.YapCode.Domain.YapCode;

namespace verii_wms_api_v2.Modules.WarehouseInbound.Application;

public sealed class WarehouseInboundService(
    IUnitOfWork unitOfWork,
    IWarehouseInboundOrderSource orderSource,
    IWarehouseInboundPolicyService receiptPolicyService,
    IQualityPolicyResolver qualityPolicyResolver,
    IStockTrackingPolicyResolver trackingPolicyResolver,
    IDocumentNumberAllocator numberAllocator,
    IAuditLogWriter audit,
    IStringLocalizer<WarehouseInboundResource> localizer) : IWarehouseInboundService
{
    private IGenericRepository<WarehouseInboundHeader> Headers => unitOfWork.Repository<WarehouseInboundHeader>();

    public Task<CreateWarehouseInboundResult> CreateFromOrdersAsync(CreateOrderBasedWarehouseInboundRequest request, long actorUserId, CancellationToken cancellationToken = default)
    {
        ValidateEnvelope(request);
        return unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            var existing = await Headers.Query().Include(x => x.Lines).ThenInclude(x => x.Sources).ThenInclude(x => x.SourceDocument)
                .Include(x => x.Tasks).ThenInclude(x => x.Lines)
                .FirstOrDefaultAsync(x => x.CorrelationId == request.IdempotencyKey, ct);
            if (existing is not null) return Replay(existing, request);

            var branch = request.BranchCode.Trim();
            var supplier = await unitOfWork.Repository<CustomerEntity>().FirstOrDefaultAsync(x => x.Id == request.SupplierId && x.BranchCode == branch, false, ct)
                ?? throw AppException.BadRequest(Message(WarehouseInboundMessageKeys.SupplierNotFound));
            var warehouse = await unitOfWork.Repository<WarehouseEntity>().FirstOrDefaultAsync(x => x.Id == request.TargetWarehouseId && x.BranchCode == branch, false, ct)
                ?? throw AppException.BadRequest(Message(WarehouseInboundMessageKeys.WarehouseNotFound));
            var location = await unitOfWork.Repository<WarehouseLocation>().FindByIdAsync(request.ReceivingLocationId, false, ct)
                ?? throw AppException.BadRequest(Message(WarehouseInboundMessageKeys.ReceivingLocationNotFound));
            if (!location.IsActive || location.WarehouseId != warehouse.Id || location.LocationType is not (LocationTypes.Receiving or LocationTypes.Staging))
                throw AppException.BadRequest(Message(WarehouseInboundMessageKeys.InvalidReceivingLocation));

            var targetWarehouseIds = request.Lines.Select(x => x.TargetWarehouseId).Append(request.TargetWarehouseId).Distinct().ToArray();
            var targetWarehouses = await unitOfWork.Repository<WarehouseEntity>().Query()
                .Where(x => targetWarehouseIds.Contains(x.Id) && x.BranchCode == branch).ToDictionaryAsync(x => x.Id, ct);
            if (targetWarehouses.Count != targetWarehouseIds.Length) throw AppException.BadRequest(Message(WarehouseInboundMessageKeys.WarehouseNotFound));
            var receivingLocationIds = request.Lines.Select(x => x.ReceivingLocationId).Append(request.ReceivingLocationId).Distinct().ToArray();
            var receivingLocations = await unitOfWork.Repository<WarehouseLocation>().Query()
                .Where(x => receivingLocationIds.Contains(x.Id) && x.IsActive).ToDictionaryAsync(x => x.Id, ct);
            if (receivingLocations.Count != receivingLocationIds.Length
                || request.Lines.Any(x => !receivingLocations.TryGetValue(x.ReceivingLocationId, out var targetLocation)
                    || targetLocation.WarehouseId != x.TargetWarehouseId
                    || targetLocation.LocationType is not (LocationTypes.Receiving or LocationTypes.Staging)))
                throw AppException.BadRequest(Message(WarehouseInboundMessageKeys.InvalidReceivingLocation));

            var selected = request.Lines.GroupBy(x => (x.OrderNumber.Trim(), x.OrderId)).Select(x => x.Single()).ToList();
            var orderCsv = string.Join(',', selected.Select(x => x.OrderNumber).Distinct(StringComparer.OrdinalIgnoreCase));
            var sourceRows = await orderSource.GetOpenLinesAsync(orderCsv, supplier.CustomerCode, branch, ct);
            var sourceByKey = sourceRows.ToDictionary(x => (x.OrderNumber, x.OrderId), x => x);
            if (selected.Any(x => !sourceByKey.ContainsKey((x.OrderNumber, x.OrderId))))
                throw AppException.Conflict(Message(WarehouseInboundMessageKeys.SourceOrderChanged));

            var sourceSelected = selected.Select(x => (Request: x, Source: sourceByKey[(x.OrderNumber, x.OrderId)])).ToList();
            if (sourceSelected.Any(x => x.Source.BranchCode?.ToString() != branch || !string.Equals(x.Source.CustomerCode, supplier.CustomerCode, StringComparison.OrdinalIgnoreCase)))
                throw AppException.BadRequest(Message(WarehouseInboundMessageKeys.SourceOrderChanged));
            foreach (var item in sourceSelected)
            {
                if (item.Request.Quantity <= 0) throw AppException.BadRequest(Message(WarehouseInboundMessageKeys.InvalidQuantity));
                if (item.Request.Quantity > item.Source.AvailableQuantity) throw AppException.Conflict(Message(WarehouseInboundMessageKeys.QuantityExceedsAvailable));
            }

            var stockCodes = sourceSelected.Select(x => x.Source.StockCode ?? string.Empty).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var stocks = await unitOfWork.Repository<StockEntity>().Query().Where(x => x.BranchCode == branch && stockCodes.Contains(x.ErpStockCode)).ToListAsync(ct);
            var stockByCode = stocks.ToDictionary(x => x.ErpStockCode, StringComparer.OrdinalIgnoreCase);
            if (stockCodes.Any(x => string.IsNullOrWhiteSpace(x) || !stockByCode.ContainsKey(x))) throw AppException.BadRequest(Message(WarehouseInboundMessageKeys.StockMirrorMissing));

            var receiptPolicy = await receiptPolicyService.GetAsync(branch, ct);
            var qualityPolicies = new Dictionary<long, ResolvedQualityPolicy>();
            foreach (var stock in stocks)
                qualityPolicies[stock.Id] = await qualityPolicyResolver.ResolveAsync(branch, stock.Id, stock.GroupCode, ct);
            var trackingPolicies = new Dictionary<long, EffectiveStockTrackingPolicy>();
            foreach (var stock in stocks)
                trackingPolicies[stock.Id] = await trackingPolicyResolver.ResolveAsync(branch, stock.Id, ct);
            var requiresQuality = receiptPolicy.RequireQualityApproval || qualityPolicies.Values.Any(x => x.InspectionMode != QualityInspectionMode.NoCheck);

            var yapCodes = sourceSelected.Select(x => x.Source.YapCode).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var yaps = await unitOfWork.Repository<YapCodeEntity>().Query().Where(x => x.BranchCode == branch && yapCodes.Contains(x.ConfigurationCode)).ToListAsync(ct);
            var yapByCode = yaps.ToDictionary(x => x.ConfigurationCode, StringComparer.OrdinalIgnoreCase);
            if (yapCodes.Any(x => !yapByCode.ContainsKey(x))) throw AppException.BadRequest(Message(WarehouseInboundMessageKeys.YapMirrorMissing));

            var assigneeIds = (request.AssignedUserIds is { Count: > 0 } ? request.AssignedUserIds : [actorUserId]).Distinct().ToList();
            var assignees = await unitOfWork.Repository<User>().Query().CountAsync(x => assigneeIds.Contains(x.Id) && x.IsActive, ct);
            if (assignees != assigneeIds.Count) throw AppException.BadRequest(Message(WarehouseInboundMessageKeys.InvalidAssignee));

            ValidateTrackingPlans(sourceSelected, stockByCode, trackingPolicies, qualityPolicies);
            var plannedSerials = sourceSelected.SelectMany(x => (x.Request.Trackings ?? []).Where(t => !string.IsNullOrWhiteSpace(t.SerialNo))
                .Select(t => new { StockId = stockByCode[x.Source.StockCode!].Id, SerialNo = t.SerialNo!.Trim() })).ToList();
            foreach (var serialGroup in plannedSerials.GroupBy(x => x.StockId))
            {
                var values = serialGroup.Select(x => x.SerialNo).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                if (await unitOfWork.Repository<WarehouseInboundTaskLineTracking>().AnyAsync(x => x.StockId == serialGroup.Key && x.SerialNo != null && values.Contains(x.SerialNo), ct))
                    throw AppException.Conflict("Girilen seri numaralarından biri başka bir açık ambar giriş emrinde planlanmış.");
            }

            var allocated = await numberAllocator.AllocateAsync(request.DocumentSeriesId, WmsDocumentType.WarehouseReceipt, DateTime.UtcNow, ct);
            var now = DateTime.UtcNow;
            var header = new WarehouseInboundHeader
            {
                BranchCode = branch, CreatedBy = actorUserId, CreatedDate = now, DocumentSeriesId = allocated.DocumentSeriesId,
                DocumentNo = allocated.DocumentNumber, DocumentDate = request.DocumentDate, ReceiptType = WarehouseInboundType.PurchaseOrder,
                InitiationMode = WarehouseInboundInitiationMode.OrderBasedTask, LabelStrategy = request.LabelStrategy, SourceSystem = WarehouseOperationSourceSystem.Netsis,
                ProcessType = WarehouseInboundProcessType.OrderBasedTask,
                CorrelationId = request.IdempotencyKey, SupplierId = supplier.Id, SupplierCodeSnapshot = supplier.CustomerCode,
                SupplierNameSnapshot = supplier.CustomerName, TargetWarehouseId = warehouse.Id, ReceivingLocationId = location.Id,
                Status = WarehouseOperationStatus.Draft, AllowOverReceipt = receiptPolicy.OverReceiptPolicy != OverReceiptPolicy.NotAllowed,
                OverReceiptPolicy = receiptPolicy.OverReceiptPolicy, OverReceiptTolerancePercent = receiptPolicy.OverReceiptTolerancePercent,
                AllowUnderReceipt = receiptPolicy.AllowUnderReceipt, RequireShortCloseApproval = receiptPolicy.RequireShortCloseApproval,
                RequireReceiptApproval = receiptPolicy.RequireReceiptApproval, RequireQualityApproval = receiptPolicy.RequireQualityApproval,
                RequireErpApproval = receiptPolicy.RequireErpApproval, HoldInventoryUntilQualityDecision = receiptPolicy.HoldInventoryUntilQualityDecision,
                BlockPutawayUntilQualityDecision = receiptPolicy.BlockPutawayUntilQualityDecision,
                InventoryAvailabilityPolicy = receiptPolicy.InventoryAvailabilityPolicy, ErpPostingPolicy = receiptPolicy.ErpPostingPolicy,
                ApprovalStatus = receiptPolicy.RequireReceiptApproval ? OperationApprovalStatus.Pending : OperationApprovalStatus.NotRequired,
                QualityStatus = requiresQuality ? OperationQualityStatus.Pending : OperationQualityStatus.NotRequired,
                RequireQualityControl = requiresQuality, RequirePutaway = request.RequirePutaway,
                Priority = request.Priority, PlannedArrivalAtUtc = request.PlannedArrivalAtUtc?.ToUniversalTime(), Description = Normalize(request.Description, 1000)
            };

            var documents = sourceSelected.GroupBy(x => x.Source.OrderNumber, StringComparer.OrdinalIgnoreCase).ToDictionary(x => x.Key, group =>
            {
                var first = group.First().Source;
                var document = Stamp(new WarehouseInboundSourceDocument { BranchCode = branch, Header = header, SourceDocumentType = WarehouseInboundSourceDocumentType.PurchaseOrder,
                    SourceSystem = WarehouseOperationSourceSystem.Netsis, ExternalDocumentId = first.OrderNumber, ExternalDocumentNo = first.OrderNumber,
                    ExternalDocumentDate = first.OrderDate.HasValue ? DateOnly.FromDateTime(first.OrderDate.Value) : null,
                    SupplierCodeSnapshot = supplier.CustomerCode, SupplierNameSnapshot = supplier.CustomerName }, actorUserId, now);
                header.SourceDocuments.Add(document); return document;
            }, StringComparer.OrdinalIgnoreCase);

            var tasksByWarehouse = sourceSelected.Select(x => x.Request.TargetWarehouseId).Distinct().OrderBy(x => x).Select((warehouseId, index) =>
            {
                var task = Stamp(new WarehouseInboundTask { BranchCode = branch, Header = header, TaskNo = TaskNumber(allocated.DocumentNumber, index + 1), TaskType = WarehouseInboundTaskType.Receive,
                    Status = WarehouseInboundTaskStatus.Assigned, Priority = request.Priority, WarehouseId = warehouseId, PlannedStartAtUtc = request.PlannedArrivalAtUtc?.ToUniversalTime() }, actorUserId, now);
                foreach (var userId in assigneeIds) task.Assignments.Add(Stamp(new WarehouseInboundTaskAssignment { BranchCode = branch, Task = task, UserId = userId,
                    AssignmentRole = userId == actorUserId ? WarehouseInboundAssignmentRole.Owner : WarehouseInboundAssignmentRole.Worker,
                    Status = WarehouseInboundAssignmentStatus.Assigned, AssignedAtUtc = DateTimeOffset.UtcNow, AssignedBy = actorUserId }, actorUserId, now));
                header.Tasks.Add(task);
                return (warehouseId, task);
            }).ToDictionary(x => x.warehouseId, x => x.task);
            var lineNo = 0;
            foreach (var item in sourceSelected.OrderBy(x => x.Source.OrderNumber).ThenBy(x => x.Source.OrderId))
            {
                var source = item.Source; var stock = stockByCode[source.StockCode!]; YapCodeEntity? yap = null;
                var trackingPolicy = trackingPolicies[stock.Id];
                if (!string.IsNullOrWhiteSpace(source.YapCode)) yap = yapByCode[source.YapCode];
                var unit = string.IsNullOrWhiteSpace(source.UnitCode) ? "ADET" : source.UnitCode.Trim().ToUpperInvariant();
                var line = Stamp(new WarehouseInboundLine { BranchCode = branch, Header = header, LineNo = ++lineNo, StockId = stock.Id,
                    StockCodeSnapshot = stock.ErpStockCode, StockNameSnapshot = stock.StockName, YapCodeId = yap?.Id, YapCodeSnapshot = yap?.ConfigurationCode,
                    UnitCode = unit, BaseUnitCode = unit, ExpectedQuantity = item.Request.Quantity, TrackingType = trackingPolicy.TrackingType,
                    TargetWarehouseId = item.Request.TargetWarehouseId,
                    RequireLot = trackingPolicy.RequireLot, RequireSerial = trackingPolicy.RequireSerial,
                    RequireExpirationDate = trackingPolicy.RequireExpirationDate,
                    AllowOverReceipt = request.AllowOverReceipt, OverReceiptTolerancePercent = request.OverReceiptTolerancePercent,
                    AllowUnderReceipt = receiptPolicy.AllowUnderReceipt, RequireQualityControl = receiptPolicy.RequireQualityApproval || qualityPolicies[stock.Id].InspectionMode != QualityInspectionMode.NoCheck,
                    DefaultReceivingLocationId = item.Request.ReceivingLocationId, Status = WarehouseInboundLineStatus.Open }, actorUserId, now);
                header.Lines.Add(line);
                line.Sources.Add(Stamp(new WarehouseInboundLineSource { BranchCode = branch, Line = line, SourceDocument = documents[source.OrderNumber],
                    ExternalLineId = source.OrderId.ToString(), ExternalStockCode = source.StockCode!, ExternalYapCode = source.YapCode,
                    OrderedQuantity = source.OrderedQuantity, PreviouslyReceivedQuantity = source.DeliveredQuantity,
                    AllocatedQuantity = item.Request.Quantity, ReceivedQuantity = 0, UnitCode = unit, ExternalStatus = "Open" }, actorUserId, now));
                var task = tasksByWarehouse[item.Request.TargetWarehouseId];
                var taskLine = Stamp(new WarehouseInboundTaskLine { BranchCode = branch, Task = task, Line = line, SequenceNo = task.Lines.Count + 1,
                    ToLocationId = item.Request.ReceivingLocationId, PlannedQuantity = item.Request.Quantity, UnitCode = unit, Status = WarehouseInboundTaskStatus.Assigned }, actorUserId, now);
                var trackingSequence = 0;
                foreach (var tracking in item.Request.Trackings ?? []) taskLine.Trackings.Add(Stamp(new WarehouseInboundTaskLineTracking
                {
                    BranchCode = branch, TaskLine = taskLine, SequenceNo = ++trackingSequence, StockId = stock.Id,
                    PlannedQuantity = tracking.Quantity, LotNo = Normalize(tracking.LotNo, 100), SerialNo = Normalize(tracking.SerialNo, 100),
                    ManufacturingDate = tracking.ManufacturingDate, ExpirationDate = tracking.ExpirationDate,
                    TargetWarehouseId = item.Request.TargetWarehouseId, ToLocationId = item.Request.ReceivingLocationId,
                    Description = Normalize(tracking.Description, 500)
                }, actorUserId, now));
                task.Lines.Add(taskLine);
            }
            header.StatusHistory.Add(Stamp(new WarehouseInboundStatusHistory { BranchCode = branch, Header = header, StatusArea = WarehouseInboundStatusArea.Operation,
                ToStatus = WarehouseOperationStatus.Draft.ToString(), ChangedAtUtc = DateTimeOffset.UtcNow, ChangedBy = actorUserId,
                Description = "Order-based warehouse inbound task created", CorrelationId = request.IdempotencyKey }, actorUserId, now));

            await Headers.AddAsync(header, ct);
            try { await unitOfWork.SaveChangesAsync(ct); }
            catch (DbUpdateException) { throw AppException.Conflict(Message(WarehouseInboundMessageKeys.ConcurrencyConflict)); }
            var result = Result(header, false);
            await audit.WriteAsync(new AuditLogWriteEntry("goods-receipt.create-from-orders", "WarehouseInboundHeader", header.Id.ToString(), "Succeeded", "goods-receipt",
                NewValues: new { header.DocumentNo, header.SupplierCodeSnapshot, header.TargetWarehouseId, result.LineCount, result.ReservedQuantity },
                ChangedFields: ["Header", "SourceDocuments", "Lines", "Task", "Assignments"]), ct);
            return result;
        }, cancellationToken, IsolationLevel.Serializable);
    }

    private CreateWarehouseInboundResult Replay(WarehouseInboundHeader header, CreateOrderBasedWarehouseInboundRequest request)
    {
        var current = header.Lines.SelectMany(x => x.Sources).ToDictionary(x => (x.SourceDocument.ExternalDocumentNo, int.Parse(x.ExternalLineId)), x => x.AllocatedQuantity);
        if (header.SupplierId != request.SupplierId || header.TargetWarehouseId != request.TargetWarehouseId || request.Lines.Count != current.Count
            || request.Lines.Any(x => !current.TryGetValue((x.OrderNumber.Trim(), x.OrderId), out var quantity) || quantity != x.Quantity))
            throw AppException.Conflict(Message(WarehouseInboundMessageKeys.IdempotencyConflict));
        return Result(header, true);
    }
    private static CreateWarehouseInboundResult Result(WarehouseInboundHeader header, bool replayed)
    {
        var tasks = header.Tasks.OrderBy(x => x.Id).Select(x => new CreatedWarehouseInboundTaskResult(x.Id, x.TaskNo, x.WarehouseId,
            x.Lines.Count, x.Lines.Sum(line => line.PlannedQuantity))).ToList();
        var first = tasks.First();
        return new(header.Id, header.DocumentNo, first.Id, first.TaskNo, header.Lines.Count, header.Lines.Sum(x => x.ExpectedQuantity), replayed, tasks);
    }
    private void ValidateEnvelope(CreateOrderBasedWarehouseInboundRequest request)
    {
        if (request.IdempotencyKey == Guid.Empty || string.IsNullOrWhiteSpace(request.BranchCode) || request.DocumentSeriesId <= 0 || request.SupplierId <= 0
            || request.TargetWarehouseId <= 0 || request.ReceivingLocationId <= 0 || request.Lines is not { Count: > 0 and <= 200 }
            || request.Priority is < 1 or > 5 || request.OverReceiptTolerancePercent is < 0 or > 100 || request.Description?.Length > 1000
            || request.Lines.Any(x => string.IsNullOrWhiteSpace(x.OrderNumber) || x.OrderId <= 0 || x.Quantity <= 0 || x.TargetWarehouseId <= 0 || x.ReceivingLocationId <= 0)
            || request.Lines.GroupBy(x => (x.OrderNumber.Trim(), x.OrderId)).Any(x => x.Count() > 1))
            throw AppException.BadRequest(Message(WarehouseInboundMessageKeys.InvalidRequest));
    }
    private static void ValidateTrackingPlans(
        IReadOnlyList<(ReserveWarehouseInboundOrderLineRequest Request, WarehouseInboundOrderSourceLine Source)> items,
        IReadOnlyDictionary<string, StockEntity> stocks,
        IReadOnlyDictionary<long, EffectiveStockTrackingPolicy> trackingPolicies,
        IReadOnlyDictionary<long, ResolvedQualityPolicy> qualityPolicies)
    {
        if (items.Sum(x => x.Request.Trackings?.Count ?? 0) > 5_000) throw AppException.BadRequest("Tek emirde en fazla 5.000 lot/seri planı oluşturulabilir.");
        var serialKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items)
        {
            var trackings = item.Request.Trackings ?? [];
            var stock = stocks[item.Source.StockCode!];
            var policy = trackingPolicies[stock.Id];
            var qualityPolicy = qualityPolicies[stock.Id];
            if (policy.TrackingType == StockTrackingType.None
                && (qualityPolicy.RequireLot || qualityPolicy.RequireSerial || qualityPolicy.RequireExpiryDate))
                throw AppException.BadRequest(
                    $"{item.Source.StockCode}: kalite kuralı lot/seri/SKT isterken merkezî stok takip politikası Takipsiz olamaz.");
            var effectivePolicy = policy with
            {
                RequireLot = policy.RequireLot || qualityPolicy.RequireLot,
                RequireSerial = policy.RequireSerial || qualityPolicy.RequireSerial,
                RequireExpirationDate = policy.RequireExpirationDate || qualityPolicy.RequireExpiryDate
            };
            try
            {
                StockTrackingPolicyGuard.Validate(
                    effectivePolicy,
                    item.Request.Quantity,
                    item.Request.TrackingType,
                    trackings.Select(x => new StockTrackingCapture(
                        x.Quantity, x.LotNo, x.SerialNo, x.ManufacturingDate, x.ExpirationDate)).ToArray(),
                    requireCompleteCapture: effectivePolicy.TrackingType != StockTrackingType.None);
            }
            catch (StockTrackingPolicyViolationException exception)
            {
                throw AppException.BadRequest(exception.Message);
            }
            foreach (var tracking in trackings)
            {
                var serial = Normalize(tracking.SerialNo, 100);
                if (serial is not null && !serialKeys.Add($"{stock.Id}|{serial}"))
                    throw AppException.Conflict($"{item.Source.StockCode}: {serial} seri numarası birden fazla girilemez.");
                if (tracking.ManufacturingDate.HasValue && tracking.ExpirationDate.HasValue && tracking.ExpirationDate < tracking.ManufacturingDate)
                    throw AppException.BadRequest($"{item.Source.StockCode}: Son kullanma tarihi üretim tarihinden önce olamaz.");
            }
        }
    }
    private static T Stamp<T>(T entity, long actor, DateTime now) where T : verii_wms_api_v2.Shared.Domain.BaseEntity { entity.CreatedBy = actor; entity.CreatedDate = now; return entity; }
    private static string? Normalize(string? value, int max) { var text = string.IsNullOrWhiteSpace(value) ? null : value.Trim(); return text?.Length > max ? text[..max] : text; }
    private static string TaskNumber(string documentNo, int sequence) { var value = $"{documentNo}-RCV-{sequence:00}"; return value.Length <= 50 ? value : value[..50]; }
    private string Message(string key) => localizer[key].Value;
}
