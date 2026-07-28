using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using verii_wms_api_v2.Modules.Audit.Application;
using verii_wms_api_v2.Modules.DocumentSeries.Application;
using verii_wms_api_v2.Modules.DocumentSeries.Domain;
using verii_wms_api_v2.Modules.GoodsReceipt.Domain;
using verii_wms_api_v2.Modules.GoodsReceipt.Localization;
using verii_wms_api_v2.Modules.Identity.Domain;
using verii_wms_api_v2.Modules.Identity.Application;
using verii_wms_api_v2.Modules.Location.Domain;
using verii_wms_api_v2.Modules.Quality.Application;
using verii_wms_api_v2.Modules.Quality.Domain;
using verii_wms_api_v2.Modules.SerialNumberPolicy.Application;
using verii_wms_api_v2.Modules.Stock.Application;
using verii_wms_api_v2.Modules.StockTracking.Application;
using verii_wms_api_v2.Modules.WarehouseOperations.Domain;
using verii_wms_api_v2.Shared.Application.Abstractions.Persistence;
using verii_wms_api_v2.Shared.Application.Exceptions;
using CustomerEntity = verii_wms_api_v2.Modules.Customer.Domain.Customer;
using StockEntity = verii_wms_api_v2.Modules.Stock.Domain.Stock;
using WarehouseEntity = verii_wms_api_v2.Modules.Warehouse.Domain.Warehouse;
using YapCodeEntity = verii_wms_api_v2.Modules.YapCode.Domain.YapCode;

namespace verii_wms_api_v2.Modules.GoodsReceipt.Application;

public sealed class GoodsReceiptService(
    IUnitOfWork unitOfWork,
    IGoodsReceiptOrderSource orderSource,
    IGoodsReceiptPolicyService receiptPolicyService,
    IQualityPolicyResolver qualityPolicyResolver,
    IStockTrackingPolicyResolver trackingPolicyResolver,
    ISerialNumberPolicyService serialNumberPolicyService,
    IDocumentNumberAllocator numberAllocator,
    IAuditLogWriter audit,
    IStringLocalizer<GoodsReceiptResource> localizer) : IGoodsReceiptService
{
    private IGenericRepository<GoodsReceiptHeader> Headers => unitOfWork.Repository<GoodsReceiptHeader>();

    public Task<CreateGoodsReceiptResult> CreateFromOrdersAsync(CreateOrderBasedGoodsReceiptRequest request, long actorUserId, CancellationToken cancellationToken = default)
    {
        ValidateEnvelope(request);
        return unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            var existing = await Headers.Query().Include(x => x.Lines).ThenInclude(x => x.Sources).ThenInclude(x => x.SourceDocument)
                .Include(x => x.Tasks).ThenInclude(x => x.Lines)
                .FirstOrDefaultAsync(x => x.CorrelationId == request.IdempotencyKey, ct);
            if (existing is not null) return Replay(existing, request);

            var branch = request.BranchCode.Trim();
            var (waybillNo, electronicWaybillNo) = NormalizeDocumentReference(
                request.WaybillNo, request.ElectronicWaybillNo, request.WaybillDate);
            var supplier = await unitOfWork.Repository<CustomerEntity>().FirstOrDefaultAsync(x => x.Id == request.SupplierId && x.BranchCode == branch, false, ct)
                ?? throw AppException.BadRequest(Message(GoodsReceiptMessageKeys.SupplierNotFound));
            if (await Headers.AnyAsync(x => x.BranchCode == branch
                    && x.SupplierId == request.SupplierId
                    && ((waybillNo != null && x.WaybillNo == waybillNo)
                        || (electronicWaybillNo != null && x.ElectronicWaybillNo == electronicWaybillNo)), ct))
                throw AppException.Conflict("Bu tedarikçi ve irsaliye numarasıyla daha önce mal kabul oluşturulmuş.");
            var warehouse = await unitOfWork.Repository<WarehouseEntity>().FirstOrDefaultAsync(x => x.Id == request.TargetWarehouseId && x.BranchCode == branch, false, ct)
                ?? throw AppException.BadRequest(Message(GoodsReceiptMessageKeys.WarehouseNotFound));
            var location = await unitOfWork.Repository<WarehouseLocation>().FindByIdAsync(request.ReceivingLocationId, false, ct)
                ?? throw AppException.BadRequest(Message(GoodsReceiptMessageKeys.ReceivingLocationNotFound));
            if (!location.IsActive || location.WarehouseId != warehouse.Id || location.LocationType is not (LocationTypes.Receiving or LocationTypes.Staging))
                throw AppException.BadRequest(Message(GoodsReceiptMessageKeys.InvalidReceivingLocation));

            var targetWarehouseIds = request.Lines.Select(x => x.TargetWarehouseId).Append(request.TargetWarehouseId).Distinct().ToArray();
            if (targetWarehouseIds.Length != 1)
                throw AppException.BadRequest("Bir mal kabul emrinde yalnızca tek depo seçilebilir. Farklı depoya ait siparişleri ayrı emirlerde oluşturunuz.");
            await UserWarehouseAccessService.EnsureAsync(unitOfWork, actorUserId, branch, targetWarehouseIds, ct);
            var targetWarehouses = await unitOfWork.Repository<WarehouseEntity>().Query()
                .Where(x => targetWarehouseIds.Contains(x.Id) && x.BranchCode == branch).ToDictionaryAsync(x => x.Id, ct);
            if (targetWarehouses.Count != targetWarehouseIds.Length) throw AppException.BadRequest(Message(GoodsReceiptMessageKeys.WarehouseNotFound));
            var receivingLocationIds = request.Lines.Select(x => x.ReceivingLocationId).Append(request.ReceivingLocationId).Distinct().ToArray();
            var receivingLocations = await unitOfWork.Repository<WarehouseLocation>().Query()
                .Where(x => receivingLocationIds.Contains(x.Id) && x.IsActive).ToDictionaryAsync(x => x.Id, ct);
            if (receivingLocations.Count != receivingLocationIds.Length
                || request.Lines.Any(x => !receivingLocations.TryGetValue(x.ReceivingLocationId, out var targetLocation)
                    || targetLocation.WarehouseId != x.TargetWarehouseId
                    || targetLocation.LocationType is not (LocationTypes.Receiving or LocationTypes.Staging)))
                throw AppException.BadRequest(Message(GoodsReceiptMessageKeys.InvalidReceivingLocation));

            var selected = request.Lines.GroupBy(x => (x.OrderNumber.Trim(), x.OrderId)).Select(x => x.Single()).ToList();
            var orderCsv = string.Join(',', selected.Select(x => x.OrderNumber).Distinct(StringComparer.OrdinalIgnoreCase));
            var sourceRows = await orderSource.GetOpenLinesAsync(orderCsv, supplier.CustomerCode, branch, ct);
            var sourceByKey = sourceRows.ToDictionary(x => (x.OrderNumber, x.OrderId), x => x);
            if (selected.Any(x => !sourceByKey.ContainsKey((x.OrderNumber, x.OrderId))))
                throw AppException.Conflict(Message(GoodsReceiptMessageKeys.SourceOrderChanged));

            var sourceSelected = selected.Select(x => (Request: x, Source: sourceByKey[(x.OrderNumber, x.OrderId)])).ToList();
            if (sourceSelected.Any(x => x.Source.BranchCode?.ToString() != branch || !string.Equals(x.Source.CustomerCode, supplier.CustomerCode, StringComparison.OrdinalIgnoreCase)))
                throw AppException.BadRequest(Message(GoodsReceiptMessageKeys.SourceOrderChanged));
            foreach (var item in sourceSelected)
            {
                if (item.Request.Quantity <= 0) throw AppException.BadRequest(Message(GoodsReceiptMessageKeys.InvalidQuantity));
                if (item.Request.Quantity > item.Source.AvailableQuantity) throw AppException.Conflict(Message(GoodsReceiptMessageKeys.QuantityExceedsAvailable));
            }

            var stockCodes = sourceSelected.Select(x => x.Source.StockCode ?? string.Empty).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var stocks = await unitOfWork.Repository<StockEntity>().Query().Where(x => x.BranchCode == branch && stockCodes.Contains(x.ErpStockCode)).ToListAsync(ct);
            var stockByCode = stocks.ToDictionary(x => x.ErpStockCode, StringComparer.OrdinalIgnoreCase);
            if (stockCodes.Any(x => string.IsNullOrWhiteSpace(x) || !stockByCode.ContainsKey(x))) throw AppException.BadRequest(Message(GoodsReceiptMessageKeys.StockMirrorMissing));

            var receiptPolicy = await receiptPolicyService.GetAsync(branch, ct);
            var qualityPolicies = new Dictionary<long, ResolvedQualityPolicy>();
            foreach (var stock in stocks)
                qualityPolicies[stock.Id] = await qualityPolicyResolver.ResolveAsync(branch, stock.Id, stock.GroupCode, ct);
            var trackingPolicies = new Dictionary<long, EffectiveStockTrackingPolicy>();
            foreach (var stock in stocks)
                trackingPolicies[stock.Id] = await trackingPolicyResolver.ResolveAsync(branch, stock.Id, ct);
            sourceSelected = await ApplyAutomaticSerialsAsync(
                sourceSelected, stockByCode, trackingPolicies, branch, request.IdempotencyKey, actorUserId, ct);
            var requiresQuality = receiptPolicy.RequireQualityApproval || qualityPolicies.Values.Any(x => x.InspectionMode != QualityInspectionMode.NoCheck);

            var yapCodes = sourceSelected.Select(x => x.Source.YapCode).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var yaps = await unitOfWork.Repository<YapCodeEntity>().Query().Where(x => x.BranchCode == branch && yapCodes.Contains(x.ConfigurationCode)).ToListAsync(ct);
            var yapByCode = yaps.ToDictionary(x => x.ConfigurationCode, StringComparer.OrdinalIgnoreCase);
            if (yapCodes.Any(x => !yapByCode.ContainsKey(x))) throw AppException.BadRequest(Message(GoodsReceiptMessageKeys.YapMirrorMissing));

            var assigneeIds = (request.AssignedUserIds is { Count: > 0 } ? request.AssignedUserIds : [actorUserId]).Distinct().ToList();
            var assignees = await unitOfWork.Repository<User>().Query().CountAsync(x => assigneeIds.Contains(x.Id) && x.IsActive, ct);
            if (assignees != assigneeIds.Count) throw AppException.BadRequest(Message(GoodsReceiptMessageKeys.InvalidAssignee));

            ValidateTrackingPlans(sourceSelected, stockByCode, trackingPolicies, qualityPolicies);
            var plannedSerials = sourceSelected.SelectMany(x => (x.Request.Trackings ?? []).Where(t => !string.IsNullOrWhiteSpace(t.SerialNo))
                .Select(t => new { StockId = stockByCode[x.Source.StockCode!].Id, SerialNo = t.SerialNo!.Trim() })).ToList();
            foreach (var serialGroup in plannedSerials.GroupBy(x => x.StockId))
            {
                var values = serialGroup.Select(x => x.SerialNo).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                if (await unitOfWork.Repository<GoodsReceiptTaskLineTracking>().AnyAsync(x => x.StockId == serialGroup.Key && x.SerialNo != null && values.Contains(x.SerialNo), ct))
                    throw AppException.Conflict("Girilen seri numaralarından biri başka bir açık mal kabul emrinde planlanmış.");
            }

            var allocated = await numberAllocator.AllocateAsync(request.DocumentSeriesId, WmsDocumentType.GoodsReceipt, DateTime.UtcNow, ct);
            var now = DateTime.UtcNow;
            var header = new GoodsReceiptHeader
            {
                BranchCode = branch, CreatedBy = actorUserId, CreatedDate = now, DocumentSeriesId = allocated.DocumentSeriesId,
                DocumentNo = allocated.DocumentNumber, DocumentDate = request.DocumentDate, ReceiptType = GoodsReceiptType.PurchaseOrder,
                InitiationMode = GoodsReceiptInitiationMode.OrderBasedTask, LabelStrategy = request.LabelStrategy, SourceSystem = WarehouseOperationSourceSystem.Netsis,
                ProcessType = GoodsReceiptProcessType.OrderBasedTask,
                CorrelationId = request.IdempotencyKey, SupplierId = supplier.Id, SupplierCodeSnapshot = supplier.CustomerCode,
                SupplierNameSnapshot = supplier.CustomerName, TargetWarehouseId = warehouse.Id, ReceivingLocationId = location.Id,
                WaybillNo = waybillNo, WaybillDate = request.WaybillDate, ElectronicWaybillNo = electronicWaybillNo,
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
                var document = Stamp(new GoodsReceiptSourceDocument { BranchCode = branch, Header = header, SourceDocumentType = GoodsReceiptSourceDocumentType.PurchaseOrder,
                    SourceSystem = WarehouseOperationSourceSystem.Netsis, ExternalDocumentId = first.OrderNumber, ExternalDocumentNo = first.OrderNumber,
                    ExternalDocumentDate = first.OrderDate.HasValue ? DateOnly.FromDateTime(first.OrderDate.Value) : null,
                    SupplierCodeSnapshot = supplier.CustomerCode, SupplierNameSnapshot = supplier.CustomerName }, actorUserId, now);
                header.SourceDocuments.Add(document); return document;
            }, StringComparer.OrdinalIgnoreCase);
            header.SourceDocuments.Add(Stamp(new GoodsReceiptSourceDocument
            {
                BranchCode = branch,
                Header = header,
                SourceDocumentType = waybillNo is not null
                    ? GoodsReceiptSourceDocumentType.SupplierWaybill
                    : GoodsReceiptSourceDocumentType.ElectronicWaybill,
                SourceSystem = waybillNo is not null
                    ? WarehouseOperationSourceSystem.Manual
                    : WarehouseOperationSourceSystem.Netsis,
                ExternalDocumentId = waybillNo ?? electronicWaybillNo!,
                ExternalDocumentNo = waybillNo ?? electronicWaybillNo!,
                ExternalDocumentDate = request.WaybillDate,
                SupplierCodeSnapshot = supplier.CustomerCode,
                SupplierNameSnapshot = supplier.CustomerName
            }, actorUserId, now));

            var tasksByWarehouse = sourceSelected.Select(x => x.Request.TargetWarehouseId).Distinct().OrderBy(x => x).Select((warehouseId, index) =>
            {
                var task = Stamp(new GoodsReceiptTask { BranchCode = branch, Header = header, TaskNo = TaskNumber(allocated.DocumentNumber, index + 1), TaskType = GoodsReceiptTaskType.Receive,
                    Status = GoodsReceiptTaskStatus.Assigned, Priority = request.Priority, WarehouseId = warehouseId, PlannedStartAtUtc = request.PlannedArrivalAtUtc?.ToUniversalTime() }, actorUserId, now);
                foreach (var userId in assigneeIds) task.Assignments.Add(Stamp(new GoodsReceiptTaskAssignment { BranchCode = branch, Task = task, UserId = userId,
                    AssignmentRole = userId == actorUserId ? GoodsReceiptAssignmentRole.Owner : GoodsReceiptAssignmentRole.Worker,
                    Status = GoodsReceiptAssignmentStatus.Assigned, AssignedAtUtc = DateTimeOffset.UtcNow, AssignedBy = actorUserId }, actorUserId, now));
                header.Tasks.Add(task);
                return (warehouseId, task);
            }).ToDictionary(x => x.warehouseId, x => x.task);
            var lineNo = 0;
            foreach (var item in sourceSelected.OrderBy(x => x.Source.OrderNumber).ThenBy(x => x.Source.OrderId))
            {
                var source = item.Source; var stock = stockByCode[source.StockCode!]; YapCodeEntity? yap = null;
                var trackingPolicy = trackingPolicies[stock.Id];
                if (!string.IsNullOrWhiteSpace(source.YapCode)) yap = yapByCode[source.YapCode];
                var unit = StockUnitPolicy.Resolve(stock, source.UnitCode);
                var line = Stamp(new GoodsReceiptLine { BranchCode = branch, Header = header, LineNo = ++lineNo, StockId = stock.Id,
                    StockCodeSnapshot = stock.ErpStockCode, StockNameSnapshot = stock.StockName, YapCodeId = yap?.Id, YapCodeSnapshot = yap?.ConfigurationCode,
                    UnitCode = unit, BaseUnitCode = unit, ExpectedQuantity = item.Request.Quantity, TrackingType = trackingPolicy.TrackingType,
                    TargetWarehouseId = item.Request.TargetWarehouseId,
                    RequireLot = trackingPolicy.RequireLot, RequireSerial = trackingPolicy.RequireSerial,
                    RequireExpirationDate = trackingPolicy.RequireExpirationDate,
                    AllowOverReceipt = request.AllowOverReceipt, OverReceiptTolerancePercent = request.OverReceiptTolerancePercent,
                    AllowUnderReceipt = receiptPolicy.AllowUnderReceipt, RequireQualityControl = receiptPolicy.RequireQualityApproval || qualityPolicies[stock.Id].InspectionMode != QualityInspectionMode.NoCheck,
                    DefaultReceivingLocationId = item.Request.ReceivingLocationId, Status = GoodsReceiptLineStatus.Open }, actorUserId, now);
                header.Lines.Add(line);
                line.Sources.Add(Stamp(new GoodsReceiptLineSource { BranchCode = branch, Line = line, SourceDocument = documents[source.OrderNumber],
                    ExternalLineId = source.OrderId.ToString(), ExternalStockCode = source.StockCode!, ExternalYapCode = source.YapCode,
                    OrderedQuantity = source.OrderedQuantity, PreviouslyReceivedQuantity = source.DeliveredQuantity,
                    AllocatedQuantity = item.Request.Quantity, ReceivedQuantity = 0, UnitCode = unit, ExternalStatus = "Open" }, actorUserId, now));
                var task = tasksByWarehouse[item.Request.TargetWarehouseId];
                var taskLine = Stamp(new GoodsReceiptTaskLine { BranchCode = branch, Task = task, Line = line, SequenceNo = task.Lines.Count + 1,
                    ToLocationId = item.Request.ReceivingLocationId, PlannedQuantity = item.Request.Quantity, UnitCode = unit, Status = GoodsReceiptTaskStatus.Assigned }, actorUserId, now);
                var trackingSequence = 0;
                foreach (var tracking in item.Request.Trackings ?? []) taskLine.Trackings.Add(Stamp(new GoodsReceiptTaskLineTracking
                {
                    BranchCode = branch, TaskLine = taskLine, SequenceNo = ++trackingSequence, StockId = stock.Id,
                    PlannedQuantity = tracking.Quantity, LotNo = Normalize(tracking.LotNo, 100), SerialNo = Normalize(tracking.SerialNo, 100),
                    ManufacturingDate = tracking.ManufacturingDate, ExpirationDate = tracking.ExpirationDate,
                    TargetWarehouseId = item.Request.TargetWarehouseId, ToLocationId = item.Request.ReceivingLocationId,
                    Description = Normalize(tracking.Description, 500)
                }, actorUserId, now));
                task.Lines.Add(taskLine);
            }
            header.StatusHistory.Add(Stamp(new GoodsReceiptStatusHistory { BranchCode = branch, Header = header, StatusArea = GoodsReceiptStatusArea.Operation,
                ToStatus = WarehouseOperationStatus.Draft.ToString(), ChangedAtUtc = DateTimeOffset.UtcNow, ChangedBy = actorUserId,
                Description = "Order-based goods receipt task created", CorrelationId = request.IdempotencyKey }, actorUserId, now));

            await Headers.AddAsync(header, ct);
            try { await unitOfWork.SaveChangesAsync(ct); }
            catch (DbUpdateException) { throw AppException.Conflict(Message(GoodsReceiptMessageKeys.ConcurrencyConflict)); }
            var result = Result(header, false);
            await audit.WriteAsync(new AuditLogWriteEntry("goods-receipt.create-from-orders", "GoodsReceiptHeader", header.Id.ToString(), "Succeeded", "goods-receipt",
                NewValues: new { header.DocumentNo, header.SupplierCodeSnapshot, header.TargetWarehouseId, result.LineCount, result.ReservedQuantity },
                ChangedFields: ["Header", "SourceDocuments", "Lines", "Task", "Assignments"]), ct);
            return result;
        }, cancellationToken, IsolationLevel.Serializable);
    }

    private CreateGoodsReceiptResult Replay(GoodsReceiptHeader header, CreateOrderBasedGoodsReceiptRequest request)
    {
        var (waybillNo, electronicWaybillNo) = NormalizeDocumentReference(
            request.WaybillNo, request.ElectronicWaybillNo, request.WaybillDate);
        var current = header.Lines.SelectMany(x => x.Sources).ToDictionary(x => (x.SourceDocument.ExternalDocumentNo, int.Parse(x.ExternalLineId)), x => x.AllocatedQuantity);
        if (header.SupplierId != request.SupplierId || header.TargetWarehouseId != request.TargetWarehouseId || request.Lines.Count != current.Count
            || header.WaybillNo != waybillNo || header.ElectronicWaybillNo != electronicWaybillNo || header.WaybillDate != request.WaybillDate
            || request.Lines.Any(x => !current.TryGetValue((x.OrderNumber.Trim(), x.OrderId), out var quantity) || quantity != x.Quantity))
            throw AppException.Conflict(Message(GoodsReceiptMessageKeys.IdempotencyConflict));
        return Result(header, true);
    }
    private static CreateGoodsReceiptResult Result(GoodsReceiptHeader header, bool replayed)
    {
        var tasks = header.Tasks.OrderBy(x => x.Id).Select(x => new CreatedGoodsReceiptTaskResult(x.Id, x.TaskNo, x.WarehouseId,
            x.Lines.Count, x.Lines.Sum(line => line.PlannedQuantity))).ToList();
        var first = tasks.First();
        return new(header.Id, header.DocumentNo, first.Id, first.TaskNo, header.Lines.Count, header.Lines.Sum(x => x.ExpectedQuantity), replayed, tasks);
    }
    private void ValidateEnvelope(CreateOrderBasedGoodsReceiptRequest request)
    {
        if (request.IdempotencyKey == Guid.Empty || string.IsNullOrWhiteSpace(request.BranchCode) || request.DocumentSeriesId <= 0 || request.SupplierId <= 0
            || request.TargetWarehouseId <= 0 || request.ReceivingLocationId <= 0 || request.Lines is not { Count: > 0 and <= 200 }
            || request.Priority is < 1 or > 5 || request.OverReceiptTolerancePercent is < 0 or > 100 || request.Description?.Length > 1000
            || request.Lines.Any(x => string.IsNullOrWhiteSpace(x.OrderNumber) || x.OrderId <= 0 || x.Quantity <= 0 || x.TargetWarehouseId <= 0 || x.ReceivingLocationId <= 0)
            || request.Lines.GroupBy(x => (x.OrderNumber.Trim(), x.OrderId)).Any(x => x.Count() > 1))
            throw AppException.BadRequest(Message(GoodsReceiptMessageKeys.InvalidRequest));
    }

    internal static (string? WaybillNo, string? ElectronicWaybillNo) NormalizeDocumentReference(
        string? waybillNo,
        string? electronicWaybillNo,
        DateOnly? waybillDate)
    {
        var normal = string.IsNullOrWhiteSpace(waybillNo) ? null : waybillNo.Trim();
        var electronic = string.IsNullOrWhiteSpace(electronicWaybillNo)
            ? null
            : electronicWaybillNo.Trim().ToUpperInvariant();
        if ((normal is null) == (electronic is null))
            throw AppException.BadRequest(
                "Normal irsaliye veya e-irsaliye türlerinden yalnızca biri seçilmeli ve numarası girilmelidir.");
        if (!waybillDate.HasValue)
            throw AppException.BadRequest("İrsaliye tarihi zorunludur.");
        if (normal is not null && !Regex.IsMatch(normal, "^[0-9]{15}$", RegexOptions.CultureInvariant))
            throw AppException.BadRequest("Normal irsaliye numarası tam 15 rakam olmalıdır.");
        if (electronic is not null
            && !Regex.IsMatch(electronic, "^[A-Z0-9]{3}[0-9]{13}$", RegexOptions.CultureInvariant))
            throw AppException.BadRequest(
                "E-irsaliye numarası 3 karakter birim kodu, 4 karakter yıl ve 9 karakter sıra numarasından oluşmalıdır.");
        return (normal, electronic);
    }

    private async Task<List<(ReserveGoodsReceiptOrderLineRequest Request, GoodsReceiptOrderSourceLine Source)>> ApplyAutomaticSerialsAsync(
        IReadOnlyList<(ReserveGoodsReceiptOrderLineRequest Request, GoodsReceiptOrderSourceLine Source)> items,
        IReadOnlyDictionary<string, StockEntity> stocks,
        IReadOnlyDictionary<long, EffectiveStockTrackingPolicy> policies,
        string branch,
        Guid operationKey,
        long actor,
        CancellationToken ct)
    {
        var result = new List<(ReserveGoodsReceiptOrderLineRequest, GoodsReceiptOrderSourceLine)>(items.Count);
        foreach (var item in items)
        {
            var stock = stocks[item.Source.StockCode!];
            var policy = policies[stock.Id];
            var trackings = item.Request.Trackings ?? [];
            if (!policy.AutoGenerateSerials)
            {
                result.Add(item);
                continue;
            }

            var serialCount = trackings.Count(x => !string.IsNullOrWhiteSpace(x.SerialNo));
            if (serialCount == trackings.Count && serialCount > 0)
            {
                result.Add(item);
                continue;
            }
            if (serialCount > 0)
                throw AppException.BadRequest("Otomatik seri kullanılan kalemde manuel ve otomatik seri birlikte kullanılamaz.");
            if (item.Request.Quantity != decimal.Truncate(item.Request.Quantity)
                || item.Request.Quantity is < 1 or > 10_000)
                throw AppException.BadRequest("Otomatik seri üretilecek miktar 1-10.000 arasında tam sayı olmalıdır.");

            var quantity = decimal.ToInt32(item.Request.Quantity);
            if (trackings.Count > 0
                && (trackings.Count != quantity || trackings.Any(x => x.Quantity != 1)))
                throw AppException.BadRequest("Otomatik seride her stok birimi için miktarı 1 olan ayrı takip satırı bulunmalıdır.");

            var generated = await serialNumberPolicyService.GenerateAsync(
                new(branch, stock.Id, quantity,
                    BuildSerialGenerationKey("GR", operationKey, item.Request.OrderNumber, item.Request.OrderId),
                    "GoodsReceiptOrderDraft", null),
                actor,
                ct);
            var planned = trackings.Count == 0
                ? generated.Serials.Select(x => new PlanGoodsReceiptTrackingRequest(
                    1, null, x.SerialNo, null, null, "Stok kuralına göre otomatik üretildi.")).ToArray()
                : trackings.Zip(generated.Serials, (tracking, serial) => tracking with
                    { SerialNo = serial.SerialNo }).ToArray();
            result.Add((item.Request with
            {
                TrackingType = policy.TrackingType,
                Trackings = planned
            }, item.Source));
        }
        return result;
    }

    private static string BuildSerialGenerationKey(string prefix, Guid operationKey, string orderNumber, int orderId)
    {
        var input = $"{operationKey:N}|{orderNumber.Trim().ToUpperInvariant()}|{orderId}";
        return $"{prefix}-{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input)))}";
    }

    private static void ValidateTrackingPlans(
        IReadOnlyList<(ReserveGoodsReceiptOrderLineRequest Request, GoodsReceiptOrderSourceLine Source)> items,
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
