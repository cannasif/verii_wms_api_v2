using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using verii_wms_api_v2.Modules.Audit.Application;
using verii_wms_api_v2.Modules.Customer.Domain;
using verii_wms_api_v2.Modules.DocumentSeries.Application;
using verii_wms_api_v2.Modules.DocumentSeries.Domain;
using verii_wms_api_v2.Modules.ErpIntegration.Application;
using verii_wms_api_v2.Modules.GoodsReceipt.Domain;
using verii_wms_api_v2.Modules.Identity.Domain;
using verii_wms_api_v2.Modules.Location.Domain;
using verii_wms_api_v2.Modules.Quality.Application;
using verii_wms_api_v2.Modules.Quality.Domain;
using verii_wms_api_v2.Modules.Stock.Application;
using verii_wms_api_v2.Modules.SerialNumberPolicy.Application;
using verii_wms_api_v2.Modules.StockBalance.Domain;
using verii_wms_api_v2.Modules.StockTracking.Application;
using verii_wms_api_v2.Modules.StockMovement.Application;
using verii_wms_api_v2.Modules.StockMovement.Domain;
using verii_wms_api_v2.Modules.Warehouse.Domain;
using verii_wms_api_v2.Modules.WarehouseOperations.Domain;
using verii_wms_api_v2.Shared;
using verii_wms_api_v2.Shared.Application.Abstractions.Persistence;
using verii_wms_api_v2.Shared.Application.Exceptions;
using StockEntity = verii_wms_api_v2.Modules.Stock.Domain.Stock;
using WarehouseEntity = verii_wms_api_v2.Modules.Warehouse.Domain.Warehouse;
using CustomerEntity = verii_wms_api_v2.Modules.Customer.Domain.Customer;

namespace verii_wms_api_v2.Modules.GoodsReceipt.Application;

public sealed class GoodsReceiptOperationsService(
    IUnitOfWork unitOfWork,
    IGoodsReceiptPolicyService receiptPolicyService,
    IQualityPolicyResolver qualityPolicyResolver,
    IStockTrackingPolicyResolver trackingPolicyResolver,
    ISerialNumberPolicyResolver serialPolicyResolver,
    IDocumentNumberAllocator numberAllocator,
    IStockMovementService stockMovementService,
    IGoodsReceiptRoutingService routing,
    IAuditLogWriter audit,
    IGoodsReceiptErpAutomation erpAutomation) : IGoodsReceiptOperationsService
{
    private IGenericRepository<GoodsReceiptHeader> Headers => unitOfWork.Repository<GoodsReceiptHeader>();
    private IGenericRepository<GoodsReceiptExecution> Executions => unitOfWork.Repository<GoodsReceiptExecution>();

    public Task<ManualGoodsReceiptResult> CreateOrderlessTaskAsync(CreateManualGoodsReceiptRequest request, long actorUserId, CancellationToken cancellationToken = default) =>
        CreateAsync(request, actorUserId, direct: false, cancellationToken);

    public async Task<ManualGoodsReceiptResult> CreateDirectReceiptAsync(
        CreateManualGoodsReceiptRequest request,
        long actorUserId,
        CancellationToken cancellationToken = default)
    {
        var result = await CreateAsync(request, actorUserId, direct: true, cancellationToken);
        erpAutomation.Enqueue(result.Id, actorUserId);
        return result;
    }

    public async Task<PagedResponse<GoodsReceiptGridRow>> GetPagedAsync(PagedRequest request, CancellationToken cancellationToken = default)
    {
        var search = request.Search?.Trim();
        var headers = Headers.Query();
        var warehouses = unitOfWork.Repository<WarehouseEntity>().Query(ignoreQueryFilters: true);
        var lines = unitOfWork.Repository<GoodsReceiptLine>().Query();
        var joined = from h in headers
                     join w in warehouses on h.TargetWarehouseId equals w.Id
                     where string.IsNullOrWhiteSpace(search) || h.DocumentNo.Contains(search)
                         || (h.SupplierCodeSnapshot != null && h.SupplierCodeSnapshot.Contains(search))
                         || (h.SupplierNameSnapshot != null && h.SupplierNameSnapshot.Contains(search))
                         || (h.WaybillNo != null && h.WaybillNo.Contains(search))
                     select new { Header = h, Warehouse = w };
        var query = joined.Select(x => new GoodsReceiptGridRow(x.Header.Id, x.Header.BranchCode, x.Header.DocumentNo, x.Header.DocumentDate,
            x.Header.ReceiptType, x.Header.InitiationMode, x.Header.ProcessType, x.Header.Status, x.Header.ApprovalStatus,
            x.Header.QualityStatus, x.Header.PutawayStatus, x.Header.ErpIntegrationStatus, x.Header.SupplierId, x.Header.SupplierCodeSnapshot,
            x.Header.SupplierNameSnapshot, x.Header.TargetWarehouseId, x.Warehouse.WarehouseCode, x.Warehouse.WarehouseName,
            x.Header.WaybillNo, x.Header.WaybillDate, lines.Count(line => line.GrHeaderId == x.Header.Id),
            lines.Where(line => line.GrHeaderId == x.Header.Id).Sum(line => (decimal?)line.ExpectedQuantity) ?? 0,
            lines.Where(line => line.GrHeaderId == x.Header.Id).Sum(line => (decimal?)line.ReceivedQuantity) ?? 0,
            x.Header.Priority, x.Header.PlannedArrivalAtUtc, x.Header.ReceivedAtUtc,
            x.Header.CreatedBy, x.Header.CreatedDate, x.Header.UpdatedBy, x.Header.UpdatedDate,
            x.Header.RowVersion));
        return await query
            .ApplyAdvancedFilters(request)
            .ApplySort(request, nameof(GoodsReceiptGridRow.CreatedDate))
            .ToPagedResponseAsync(request, cancellationToken);
    }

    public async Task<GoodsReceiptDetail> GetDetailAsync(long id, CancellationToken cancellationToken = default)
    {
        var header = await Headers.Query().FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw AppException.NotFound("Mal kabul kaydı bulunamadı.");
        var warehouse = await unitOfWork.Repository<WarehouseEntity>().FirstOrDefaultAsync(x => x.Id == header.TargetWarehouseId, false, cancellationToken)
            ?? throw AppException.NotFound("Mal kabul deposu bulunamadı.");
        var lineQuery = unitOfWork.Repository<GoodsReceiptLine>().Query().Where(x => x.GrHeaderId == id).OrderBy(x => x.LineNo);
        var lineEntities = await lineQuery.ToListAsync(cancellationToken);
        var routedQuantities = await routing.GetActiveAllocatedQuantitiesAsync(lineEntities.Select(x => x.Id).ToArray(), cancellationToken);
        var detailLines = lineEntities.Select(x =>
        {
            var routed = routedQuantities.GetValueOrDefault(x.Id);
            return new GoodsReceiptDetailLine(x.Id, x.LineNo, x.StockId, x.StockCodeSnapshot,
                x.StockNameSnapshot, x.YapCodeId, x.YapCodeSnapshot, x.UnitCode, x.ExpectedQuantity, x.ReceivedQuantity,
                x.AcceptedQuantity, x.RejectedQuantity, x.QuarantineQuantity, x.ShortClosedQuantity, x.PutawayQuantity, x.Status,
                x.RequireQualityControl, x.TargetWarehouseId, x.DefaultReceivingLocationId,
                x.DefaultPutawayLocationId, routed, Math.Max(0, x.AcceptedQuantity - routed));
        }).ToList();
        var sourceDocuments = await unitOfWork.Repository<GoodsReceiptSourceDocument>().Query().Where(x => x.GrHeaderId == id)
            .OrderBy(x => x.Id).Select(x => new { x.SourceDocumentType, x.ExternalDocumentNo }).ToListAsync(cancellationToken);
        var documents = sourceDocuments.Select(x => $"{x.SourceDocumentType}:{x.ExternalDocumentNo}").ToList();
        var taskNumbers = await unitOfWork.Repository<GoodsReceiptTask>().Query().Where(x => x.GrHeaderId == id)
            .OrderBy(x => x.Id).Select(x => x.TaskNo).ToListAsync(cancellationToken);
        var executionCount = await Executions.Query().CountAsync(x => x.GrHeaderId == id, cancellationToken);
        var executionDimensions = await (from execution in Executions.Query()
            join executionLine in unitOfWork.Repository<GoodsReceiptExecutionLine>().Query()
                on execution.Id equals executionLine.GrExecutionId
            where execution.GrHeaderId == id && execution.Status == GoodsReceiptExecutionStatus.Posted
            group executionLine by new
            {
                executionLine.GrLineId,
                executionLine.StockId,
                executionLine.YapCodeId,
                executionLine.UnitCode,
                executionLine.WarehouseId,
                executionLine.LocationId,
                LotNo = executionLine.LotNo ?? "",
                SerialNo = executionLine.SerialNo ?? ""
            }
            into grouped
            select new { grouped.Key, Quantity = grouped.Sum(x => x.Quantity) })
            .ToListAsync(cancellationToken);
        var balanceStockIds = executionDimensions.Select(x => x.Key.StockId).Distinct().ToArray();
        var balanceLocationIds = executionDimensions.Select(x => x.Key.LocationId).Distinct().ToArray();
        var balances = await unitOfWork.Repository<LocationStockBalance>().Query()
            .Where(x => balanceStockIds.Contains(x.StockId) && balanceLocationIds.Contains(x.LocationId)
                && x.StockStatus == "Available" && x.AvailableQuantity > 0)
            .ToListAsync(cancellationToken);
        var detailLineMap = detailLines.ToDictionary(x => x.Id);
        var remainingByLine = detailLines.ToDictionary(x => x.Id,
            x => Math.Max(0, x.AcceptedQuantity - x.PutawayQuantity));
        var putawayCandidates = new List<GoodsReceiptPutawayCandidate>();
        foreach (var dimension in executionDimensions.OrderBy(x => x.Key.GrLineId).ThenBy(x => x.Key.SerialNo).ThenBy(x => x.Key.LotNo))
        {
            if (!detailLineMap.TryGetValue(dimension.Key.GrLineId, out var line)
                || remainingByLine[line.Id] <= 0) continue;
            var balance = balances.FirstOrDefault(x =>
                x.StockId == dimension.Key.StockId
                && x.YapCodeId == dimension.Key.YapCodeId
                && x.WarehouseId == dimension.Key.WarehouseId
                && x.LocationId == dimension.Key.LocationId
                && x.UnitCode == dimension.Key.UnitCode
                && x.LotNo == dimension.Key.LotNo
                && x.SerialNo == dimension.Key.SerialNo);
            if (balance is null) continue;
            var quantity = Math.Min(remainingByLine[line.Id],
                Math.Min(dimension.Quantity, balance.AvailableQuantity));
            if (quantity <= 0) continue;
            putawayCandidates.Add(new GoodsReceiptPutawayCandidate(
                line.Id, line.LineNo, line.StockId, line.StockCode, line.StockName,
                line.YapCodeId, line.YapCode, line.UnitCode, quantity,
                dimension.Key.WarehouseId, dimension.Key.LocationId,
                dimension.Key.LotNo == "" ? null : dimension.Key.LotNo,
                dimension.Key.SerialNo == "" ? null : dimension.Key.SerialNo,
                "Available", line.DefaultPutawayLocationId));
            remainingByLine[line.Id] -= quantity;
        }
        var grid = new GoodsReceiptGridRow(header.Id, header.BranchCode, header.DocumentNo, header.DocumentDate,
            header.ReceiptType, header.InitiationMode, header.ProcessType, header.Status, header.ApprovalStatus,
            header.QualityStatus, header.PutawayStatus, header.ErpIntegrationStatus,
            header.SupplierId, header.SupplierCodeSnapshot, header.SupplierNameSnapshot, header.TargetWarehouseId,
            warehouse.WarehouseCode, warehouse.WarehouseName, header.WaybillNo, header.WaybillDate, detailLines.Count,
            detailLines.Sum(x => x.ExpectedQuantity), detailLines.Sum(x => x.ReceivedQuantity), header.Priority,
            header.PlannedArrivalAtUtc, header.ReceivedAtUtc, header.CreatedBy, header.CreatedDate,
            header.UpdatedBy, header.UpdatedDate, header.RowVersion);
        return new GoodsReceiptDetail(grid, detailLines, putawayCandidates, documents, taskNumbers, executionCount);
    }

    private Task<ManualGoodsReceiptResult> CreateAsync(CreateManualGoodsReceiptRequest request, long actor, bool direct, CancellationToken ct)
    {
        Validate(request, direct);
        return unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            var requestHash = Hash(request);
            var existingHeader = await Headers.Query().FirstOrDefaultAsync(x => x.CorrelationId == request.IdempotencyKey, token);
            if (existingHeader is not null)
            {
                var existingExecution = await Executions.Query().FirstOrDefaultAsync(x => x.GrHeaderId == existingHeader.Id, token);
                if (direct && existingExecution is not null && !HashesMatch(existingExecution.RequestHash, requestHash))
                    throw AppException.Conflict("Aynı idempotency anahtarı farklı bir direkt mal kabul isteğiyle kullanılmış.");
                return await ExistingResult(existingHeader, existingExecution, token);
            }

            var branch = request.BranchCode.Trim();
            var supplier = await unitOfWork.Repository<CustomerEntity>().FirstOrDefaultAsync(x => x.Id == request.SupplierId && x.BranchCode == branch, false, token)
                ?? throw AppException.BadRequest("Cari bulunamadı veya şube ile uyuşmuyor.");
            var waybillNo = NormalizeDocumentNumber(request.WaybillNo);
            var electronicWaybillNo = NormalizeDocumentNumber(request.ElectronicWaybillNo);
            var duplicateDocument = await Headers.Query().AnyAsync(x => x.BranchCode == branch && x.SupplierId == supplier.Id
                && ((waybillNo != null && x.WaybillNo == waybillNo)
                    || (electronicWaybillNo != null && x.ElectronicWaybillNo == electronicWaybillNo)), token);
            if (duplicateDocument)
                throw AppException.Conflict("Bu tedarikçi için aynı mal kabul numarası daha önce kullanılmış.");
            var warehouse = await unitOfWork.Repository<WarehouseEntity>().FirstOrDefaultAsync(x => x.Id == request.TargetWarehouseId && x.BranchCode == branch, false, token)
                ?? throw AppException.BadRequest("Hedef depo bulunamadı.");
            var location = await unitOfWork.Repository<WarehouseLocation>().FindByIdAsync(request.ReceivingLocationId, false, token)
                ?? throw AppException.BadRequest("Mal kabul alanı bulunamadı.");
            if (!location.IsActive || location.WarehouseId != warehouse.Id || location.LocationType is not (LocationTypes.Receiving or LocationTypes.Staging))
                throw AppException.BadRequest("Seçilen lokasyon aktif bir kabul veya staging alanı olmalıdır.");

            var requestedLineWarehouseIds = request.Lines
                .Select(x => x.TargetWarehouseId ?? request.TargetWarehouseId)
                .Distinct()
                .ToArray();
            if (requestedLineWarehouseIds.Any(x => x != warehouse.Id))
                throw AppException.BadRequest("Siparişsiz ve direkt kabulde kalem hedef deposu header deposuyla aynı olmalıdır.");
            var requestedLineLocationIds = request.Lines
                .Select(x => x.ReceivingLocationId ?? request.ReceivingLocationId)
                .Distinct()
                .ToArray();
            var lineLocations = await unitOfWork.Repository<WarehouseLocation>().Query()
                .Where(x => requestedLineLocationIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, token);
            if (lineLocations.Count != requestedLineLocationIds.Length
                || lineLocations.Values.Any(x => !x.IsActive || x.WarehouseId != warehouse.Id
                    || (x.LocationType is not (LocationTypes.Receiving or LocationTypes.Staging) && !x.IsPutaway)
                    || x.IsQuarantine))
                throw AppException.BadRequest("Kalem hedef rafı aktif, aynı depoda ve kabul/putaway kullanımına uygun olmalıdır.");

            var stockIds = request.Lines.Select(x => x.StockId).Distinct().ToList();
            var stocks = await unitOfWork.Repository<StockEntity>().Query().Where(x => x.BranchCode == branch && stockIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, token);
            if (stocks.Count != stockIds.Count) throw AppException.BadRequest("Geçersiz veya farklı şubeye ait stok seçildi.");
            var yapIds = request.Lines.Where(x => x.YapCodeId.HasValue).Select(x => x.YapCodeId!.Value).Distinct().ToList();
            var yaps = await unitOfWork.Repository<Modules.YapCode.Domain.YapCode>().Query().Where(x => x.BranchCode == branch && yapIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, token);
            if (yaps.Count != yapIds.Count) throw AppException.BadRequest("Geçersiz veya farklı şubeye ait YAP kodu seçildi.");

            var policy = await receiptPolicyService.GetAsync(branch, token);
            if (!direct && !policy.AllowOrderlessReceipt) throw AppException.Forbidden("Siparişsiz mal kabul emri politika gereği kapalıdır.");
            if (direct && !policy.AllowUnplannedReceipt) throw AppException.Forbidden("Emirsiz direkt mal kabul politika gereği kapalıdır.");
            var resolved = new Dictionary<long, ResolvedQualityPolicy>();
            foreach (var stock in stocks.Values) resolved[stock.Id] = await qualityPolicyResolver.ResolveAsync(branch, stock.Id, stock.GroupCode, token);
            var trackingPolicies = new Dictionary<long, EffectiveStockTrackingPolicy>();
            foreach (var stock in stocks.Values) trackingPolicies[stock.Id] = await trackingPolicyResolver.ResolveAsync(branch, stock.Id, token);
            var requiresQuality = policy.RequireQualityApproval || resolved.Values.Any(x => x.InspectionMode != QualityInspectionMode.NoCheck);

            ValidateTrackedLines(request, stocks, resolved, trackingPolicies, requireCompleteCapture: direct);
            foreach (var input in request.Lines)
            {
                var validation = await serialPolicyResolver.ValidateAsync(branch, input.StockId, input.YapCodeId, input.SerialNo, token);
                if (!validation.IsValid) throw AppException.BadRequest(validation.Error ?? "Seri numarası geçersiz.");
            }
            var allocated = await numberAllocator.AllocateAsync(request.DocumentSeriesId, WmsDocumentType.GoodsReceipt, DateTime.UtcNow, token);
            var now = DateTimeOffset.UtcNow;
            var header = Stamp(new GoodsReceiptHeader
            {
                BranchCode = branch, DocumentSeriesId = allocated.DocumentSeriesId, DocumentNo = allocated.DocumentNumber,
                DocumentDate = request.DocumentDate, ReceiptType = GoodsReceiptType.Direct,
                InitiationMode = direct ? GoodsReceiptInitiationMode.DirectReceipt : GoodsReceiptInitiationMode.UnplannedTask,
                ProcessType = direct ? GoodsReceiptProcessType.OrderlessDirectReceipt : GoodsReceiptProcessType.OrderlessTask,
                LabelStrategy = request.LabelStrategy, SourceSystem = WarehouseOperationSourceSystem.Manual,
                CorrelationId = request.IdempotencyKey, SupplierId = supplier.Id, SupplierCodeSnapshot = supplier.CustomerCode,
                SupplierNameSnapshot = supplier.CustomerName, TargetWarehouseId = warehouse.Id, ReceivingLocationId = location.Id,
                Status = direct ? WarehouseOperationStatus.Processed : WarehouseOperationStatus.Draft,
                ApprovalStatus = policy.RequireReceiptApproval ? OperationApprovalStatus.Pending : OperationApprovalStatus.NotRequired,
                QualityStatus = requiresQuality ? OperationQualityStatus.Pending : OperationQualityStatus.NotRequired,
                PutawayStatus = OperationPutawayStatus.Pending, ErpIntegrationStatus = ErpIntegrationStatus.Pending,
                PlannedArrivalAtUtc = request.PlannedArrivalAtUtc?.ToUniversalTime(), ActualArrivalAtUtc = direct ? now : null,
                ReceivedAtUtc = direct ? now : null, ReceivedBy = direct ? actor : null,
                WaybillNo = waybillNo, WaybillDate = request.WaybillDate,
                ElectronicWaybillNo = electronicWaybillNo, ShipmentReferenceNo = Clean(request.ShipmentReferenceNo, 100),
                CarrierCode = Clean(request.CarrierCode, 50), CarrierName = Clean(request.CarrierName, 200),
                VehiclePlate = Clean(request.VehiclePlate, 20), TrailerPlate = Clean(request.TrailerPlate, 20),
                DriverName = Clean(request.DriverName, 150), SealNo = Clean(request.SealNo, 50),
                AllowOverReceipt = policy.OverReceiptPolicy != OverReceiptPolicy.NotAllowed, OverReceiptPolicy = policy.OverReceiptPolicy,
                OverReceiptTolerancePercent = policy.OverReceiptTolerancePercent, AllowUnderReceipt = policy.AllowUnderReceipt,
                RequireShortCloseApproval = policy.RequireShortCloseApproval, RequireReceiptApproval = policy.RequireReceiptApproval,
                RequireQualityApproval = policy.RequireQualityApproval, RequireErpApproval = policy.RequireErpApproval,
                HoldInventoryUntilQualityDecision = policy.HoldInventoryUntilQualityDecision,
                BlockPutawayUntilQualityDecision = policy.BlockPutawayUntilQualityDecision,
                InventoryAvailabilityPolicy = policy.InventoryAvailabilityPolicy, ErpPostingPolicy = policy.ErpPostingPolicy,
                RequireQualityControl = requiresQuality, RequirePutaway = true, Priority = request.Priority,
                Description = Clean(request.Description, 1000)
            }, actor);
            await Headers.AddAsync(header, token);
            await unitOfWork.SaveChangesAsync(token);

            if (!string.IsNullOrWhiteSpace(header.WaybillNo))
                await unitOfWork.Repository<GoodsReceiptSourceDocument>().AddAsync(Stamp(new GoodsReceiptSourceDocument
                {
                    BranchCode = branch, Header = header, SourceDocumentType = GoodsReceiptSourceDocumentType.SupplierWaybill,
                    SourceSystem = WarehouseOperationSourceSystem.Manual, ExternalDocumentId = header.WaybillNo,
                    ExternalDocumentNo = header.WaybillNo, ExternalDocumentDate = header.WaybillDate,
                    SupplierCodeSnapshot = supplier.CustomerCode, SupplierNameSnapshot = supplier.CustomerName
                }, actor), token);
            if (!string.IsNullOrWhiteSpace(header.ElectronicWaybillNo) && !string.Equals(header.ElectronicWaybillNo, header.WaybillNo, StringComparison.OrdinalIgnoreCase))
                await unitOfWork.Repository<GoodsReceiptSourceDocument>().AddAsync(Stamp(new GoodsReceiptSourceDocument
                {
                    BranchCode = branch, Header = header, SourceDocumentType = GoodsReceiptSourceDocumentType.ElectronicWaybill,
                    SourceSystem = WarehouseOperationSourceSystem.Netsis, ExternalDocumentId = header.ElectronicWaybillNo,
                    ExternalDocumentNo = header.ElectronicWaybillNo, ExternalDocumentDate = header.WaybillDate,
                    SupplierCodeSnapshot = supplier.CustomerCode, SupplierNameSnapshot = supplier.CustomerName
                }, actor), token);

            var grLines = new List<GoodsReceiptLine>();
            for (var index = 0; index < request.Lines.Count; index++)
            {
                var input = request.Lines[index]; var stock = stocks[input.StockId];
                var lineLocationId = input.ReceivingLocationId ?? request.ReceivingLocationId;
                yaps.TryGetValue(input.YapCodeId ?? 0, out var yap); var qp = resolved[stock.Id];
                var trackingPolicy = trackingPolicies[stock.Id];
                var unit = StockUnitPolicy.Resolve(stock, input.UnitCode);
                var qualityRequired = policy.RequireQualityApproval || qp.InspectionMode != QualityInspectionMode.NoCheck;
                var line = Stamp(new GoodsReceiptLine
                {
                    BranchCode = branch, Header = header, LineNo = index + 1, StockId = stock.Id,
                    StockCodeSnapshot = stock.ErpStockCode, StockNameSnapshot = stock.StockName,
                    YapCodeId = yap?.Id, YapCodeSnapshot = yap?.ConfigurationCode, UnitCode = unit, BaseUnitCode = unit,
                    ExpectedQuantity = input.Quantity, ReceivedQuantity = direct ? input.Quantity : 0,
                    AcceptedQuantity = direct && !qualityRequired ? input.Quantity : 0,
                    QuarantineQuantity = direct && qualityRequired ? input.Quantity : 0,
                    TrackingType = trackingPolicy.TrackingType,
                    RequireLot = trackingPolicy.RequireLot, RequireSerial = trackingPolicy.RequireSerial,
                    RequireExpirationDate = trackingPolicy.RequireExpirationDate,
                    MinimumShelfLifeDays = qp.MinimumRemainingShelfLifeDays, RequireQualityControl = qualityRequired,
                    Status = direct ? GoodsReceiptLineStatus.Received : GoodsReceiptLineStatus.Open,
                    AllowOverReceipt = policy.OverReceiptPolicy != OverReceiptPolicy.NotAllowed,
                    OverReceiptTolerancePercent = policy.OverReceiptTolerancePercent, AllowUnderReceipt = policy.AllowUnderReceipt,
                    TargetWarehouseId = warehouse.Id, DefaultReceivingLocationId = lineLocationId,
                    DefaultPutawayLocationId = lineLocations[lineLocationId].IsPutaway ? lineLocationId : null,
                    Description = Clean(input.Description, 500)
                }, actor);
                grLines.Add(line); header.Lines.Add(line);
            }

            GoodsReceiptTask? task = null;
            if (!direct)
            {
                task = Stamp(new GoodsReceiptTask { BranchCode = branch, Header = header, TaskNo = TaskNo(header.DocumentNo),
                    TaskType = GoodsReceiptTaskType.Receive, Status = GoodsReceiptTaskStatus.Assigned, Priority = request.Priority,
                    WarehouseId = warehouse.Id, PlannedStartAtUtc = request.PlannedArrivalAtUtc?.ToUniversalTime() }, actor);
                header.Tasks.Add(task);
                for (var index = 0; index < grLines.Count; index++)
                {
                    var input = request.Lines[index];
                    var taskLine = Stamp(new GoodsReceiptTaskLine
                    {
                        BranchCode = branch, Task = task, Line = grLines[index], SequenceNo = index + 1,
                        ToLocationId = grLines[index].DefaultReceivingLocationId, PlannedQuantity = grLines[index].ExpectedQuantity,
                        UnitCode = grLines[index].UnitCode, Status = GoodsReceiptTaskStatus.Assigned
                    }, actor);
                    if (!string.IsNullOrWhiteSpace(input.LotNo) || !string.IsNullOrWhiteSpace(input.SerialNo)
                        || input.ManufacturingDate.HasValue || input.ExpirationDate.HasValue)
                        taskLine.Trackings.Add(Stamp(new GoodsReceiptTaskLineTracking
                        {
                            BranchCode = branch,
                            TaskLine = taskLine,
                            SequenceNo = 1,
                            StockId = grLines[index].StockId,
                            PlannedQuantity = grLines[index].ExpectedQuantity,
                            LotNo = Clean(input.LotNo, 100),
                            SerialNo = Clean(input.SerialNo, 100),
                            ManufacturingDate = input.ManufacturingDate,
                            ExpirationDate = input.ExpirationDate,
                            TargetWarehouseId = grLines[index].TargetWarehouseId,
                            ToLocationId = grLines[index].DefaultReceivingLocationId ?? request.ReceivingLocationId,
                            Description = Clean(input.Description, 500)
                        }, actor));
                    task.Lines.Add(taskLine);
                }
                var users = (request.AssignedUserIds is { Count: > 0 } ? request.AssignedUserIds : [actor]).Distinct().ToList();
                if (await unitOfWork.Repository<User>().Query().CountAsync(x => users.Contains(x.Id) && x.IsActive, token) != users.Count)
                    throw AppException.BadRequest("Atanan kullanıcılardan biri geçersiz veya pasiftir.");
                foreach (var userId in users) task.Assignments.Add(Stamp(new GoodsReceiptTaskAssignment
                {
                    BranchCode = branch, Task = task, UserId = userId, AssignmentRole = userId == actor ? GoodsReceiptAssignmentRole.Owner : GoodsReceiptAssignmentRole.Worker,
                    Status = GoodsReceiptAssignmentStatus.Assigned, AssignedAtUtc = now, AssignedBy = actor
                }, actor));
            }

            header.StatusHistory.Add(Stamp(new GoodsReceiptStatusHistory { BranchCode = branch, Header = header,
                StatusArea = GoodsReceiptStatusArea.Operation, ToStatus = header.Status.ToString(), ChangedAtUtc = now,
                ChangedBy = actor, Description = direct ? "Direct receipt posted" : "Orderless receipt task created",
                CorrelationId = request.IdempotencyKey }, actor));
            await unitOfWork.SaveChangesAsync(token);

            if (!direct)
            {
                await audit.WriteAsync(new("goods-receipt.create-orderless", nameof(GoodsReceiptHeader), header.Id.ToString(), "Succeeded", "goods-receipt",
                    NewValues: new { header.DocumentNo, header.WaybillNo, LineCount = grLines.Count, Quantity = grLines.Sum(x => x.ExpectedQuantity) },
                    ChangedFields: ["Header", "Lines", "Task", "Assignments"]), token);
                return new(header.Id, header.DocumentNo, header.InitiationMode, header.Status, task!.Id, task.TaskNo, null, null, null, grLines.Count, grLines.Sum(x => x.ExpectedQuantity), false);
            }

            return await PostDirectAsync(request, requestHash, header, grLines, warehouse, resolved, actor, now, token);
        }, ct, IsolationLevel.Serializable);
    }

    private async Task<ManualGoodsReceiptResult> PostDirectAsync(CreateManualGoodsReceiptRequest request, string requestHash, GoodsReceiptHeader header,
        IReadOnlyList<GoodsReceiptLine> lines, WarehouseEntity warehouse,
        IReadOnlyDictionary<long, ResolvedQualityPolicy> qualityPolicies, long actor, DateTimeOffset now, CancellationToken ct)
    {
        QualityInspection? inspection = null;
        var inspectionLineByGrLine = new Dictionary<long, QualityInspectionLine>();
        if (lines.Any(x => x.RequireQualityControl))
        {
            inspection = Stamp(new QualityInspection { BranchCode = header.BranchCode, CorrelationId = request.IdempotencyKey,
                InspectionNo = $"QC-{header.DocumentNo}", SourceDocumentType = "GoodsReceipt", SourceDocumentId = header.Id,
                SourceDocumentNo = header.DocumentNo, WarehouseId = warehouse.Id, SupplierId = header.SupplierId,
                Status = QualityInspectionStatus.Pending, CreatedAtUtc = now, QueuedAtUtc = now, QueuedBy = actor }, actor);
            await unitOfWork.Repository<QualityInspection>().AddAsync(inspection, ct);
            foreach (var line in lines.Where(x => x.RequireQualityControl))
            {
                var input = request.Lines[line.LineNo - 1]; var qp = qualityPolicies[line.StockId];
                var qline = Stamp(new QualityInspectionLine { BranchCode = header.BranchCode, Inspection = inspection,
                    GoodsReceiptLineId = line.Id, StockId = line.StockId, StockCodeSnapshot = line.StockCodeSnapshot,
                    StockNameSnapshot = line.StockNameSnapshot, YapCodeId = line.YapCodeId, YapCodeSnapshot = line.YapCodeSnapshot,
                    LotNo = Clean(input.LotNo, 100), SerialNo = Clean(input.SerialNo, 100), ExpiryDate = input.ExpirationDate,
                    Quantity = input.Quantity, SampleQuantity = Sample(input.Quantity, qp), Decision = QualityDecision.Pending }, actor);
                inspection.Lines.Add(qline); inspectionLineByGrLine[line.Id] = qline;
            }
            await unitOfWork.SaveChangesAsync(ct);
        }

        var execution = Stamp(new GoodsReceiptExecution { BranchCode = header.BranchCode, Header = header,
            IdempotencyKey = request.IdempotencyKey, RequestHash = requestHash, ExecutionNo = $"{header.DocumentNo}-EX-01",
            Mode = request.ExecutionMode, Status = GoodsReceiptExecutionStatus.Posted,
            OccurredAtUtc = request.OccurredAtUtc?.ToUniversalTime() ?? now,
            DeviceId = Clean(request.DeviceId, 100), Description = Clean(request.Description, 500) }, actor);
        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index]; var input = request.Lines[index];
            execution.Lines.Add(Stamp(new GoodsReceiptExecutionLine { BranchCode = header.BranchCode, Execution = execution,
                Line = line, LineNo = index + 1, StockId = line.StockId, YapCodeId = line.YapCodeId,
                Quantity = input.Quantity, UnitCode = line.UnitCode, LotNo = Clean(input.LotNo, 100), SerialNo = Clean(input.SerialNo, 100),
                ManufacturingDate = input.ManufacturingDate, ExpirationDate = input.ExpirationDate,
                ScannedBarcode = Clean(input.ScannedBarcode, 250), WarehouseId = line.TargetWarehouseId,
                LocationId = line.DefaultReceivingLocationId ?? header.ReceivingLocationId,
                StockStatus = line.RequireQualityControl && header.HoldInventoryUntilQualityDecision ? "QualityHold" : "Available",
                GoodsReceiptLabelId = input.GoodsReceiptLabelId,
                QualityInspectionLineId = inspectionLineByGrLine.GetValueOrDefault(line.Id)?.Id }, actor));
        }
        await Executions.AddAsync(execution, ct); await unitOfWork.SaveChangesAsync(ct);

        var movement = await stockMovementService.PostAsync(new PostStockMovementRequest(
            $"GR:{request.IdempotencyKey:N}", StockMovementTypes.Receipt, "GoodsReceipt", header.DocumentNo, header.Id,
            execution.OccurredAtUtc.UtcDateTime, "GoodsReceipt", request.Description,
            execution.Lines.Select(x => new StockMovementLineRequest(x.StockId, x.YapCodeId, x.Quantity, null, null,
                x.WarehouseId, x.LocationId, x.UnitCode, x.LotNo, x.SerialNo, x.StockStatus)).ToList()), ct);
        execution.StockMovementOperationId = movement.OperationId;
        await unitOfWork.SaveChangesAsync(ct);
        await audit.WriteAsync(new("goods-receipt.direct", nameof(GoodsReceiptHeader), header.Id.ToString(), "Succeeded", "goods-receipt",
            NewValues: new { header.DocumentNo, execution.ExecutionNo, movement.OperationId, QualityInspectionId = inspection?.Id,
                LineCount = lines.Count, Quantity = lines.Sum(x => x.ReceivedQuantity) },
            ChangedFields: ["Header", "Lines", "Execution", "StockMovement", "Quality"]), ct);
        return new(header.Id, header.DocumentNo, header.InitiationMode, header.Status, null, null, execution.Id,
            movement.OperationId, inspection?.Id, lines.Count, lines.Sum(x => x.ReceivedQuantity), false);
    }

    private async Task<ManualGoodsReceiptResult> ExistingResult(GoodsReceiptHeader header, GoodsReceiptExecution? execution, CancellationToken ct)
    {
        var task = await unitOfWork.Repository<GoodsReceiptTask>().Query().FirstOrDefaultAsync(x => x.GrHeaderId == header.Id, ct);
        var lines = unitOfWork.Repository<GoodsReceiptLine>().Query().Where(x => x.GrHeaderId == header.Id);
        var inspection = await unitOfWork.Repository<QualityInspection>().Query().FirstOrDefaultAsync(x => x.SourceDocumentType == "GoodsReceipt" && x.SourceDocumentId == header.Id, ct);
        return new(header.Id, header.DocumentNo, header.InitiationMode, header.Status, task?.Id, task?.TaskNo,
            execution?.Id, execution?.StockMovementOperationId, inspection?.Id, await lines.CountAsync(ct),
            await lines.SumAsync(x => header.InitiationMode == GoodsReceiptInitiationMode.DirectReceipt ? x.ReceivedQuantity : x.ExpectedQuantity, ct), true);
    }

    private static void Validate(CreateManualGoodsReceiptRequest request, bool direct)
    {
        if (request.IdempotencyKey == Guid.Empty || string.IsNullOrWhiteSpace(request.BranchCode) || request.DocumentSeriesId <= 0
            || request.SupplierId <= 0 || request.TargetWarehouseId <= 0 || request.ReceivingLocationId <= 0
            || request.Priority is < 1 or > 5 || request.Lines is not { Count: > 0 and <= 200 }
            || request.Lines.Any(x => x.StockId <= 0 || x.Quantity <= 0 || x.Quantity > 999_999_999_999m)
            || request.Lines.Where(x => !string.IsNullOrWhiteSpace(x.SerialNo)).GroupBy(x => new { x.StockId, Serial = x.SerialNo!.Trim() }).Any(x => x.Count() > 1))
            throw AppException.BadRequest("Mal kabul isteği veya satırları geçersizdir.");
        if (direct && request.ExecutionMode == 0) throw AppException.BadRequest("Direkt kabul giriş yöntemi zorunludur.");
        var waybillNo = NormalizeDocumentNumber(request.WaybillNo);
        var electronicWaybillNo = NormalizeDocumentNumber(request.ElectronicWaybillNo);
        ValidateDocumentReference(waybillNo, electronicWaybillNo, request.WaybillDate, request.ExecutionMode);
        if (waybillNo is not null && !Regex.IsMatch(waybillNo, "^[0-9]{15}$", RegexOptions.CultureInvariant))
            throw AppException.BadRequest("Normal irsaliye mal kabul numarası tam 15 rakam olmalıdır.");
        if (electronicWaybillNo is not null && !Regex.IsMatch(electronicWaybillNo, "^[A-Z0-9]{3}[0-9]{13}$", RegexOptions.CultureInvariant))
            throw AppException.BadRequest("E-irsaliye numarası 3 karakter birim kodu, 4 karakter yıl ve 9 karakter sıra numarasından oluşmalıdır.");
    }

    internal static void ValidateDocumentReference(
        string? waybillNo,
        string? electronicWaybillNo,
        DateOnly? waybillDate,
        GoodsReceiptExecutionMode executionMode)
    {
        var hasWaybill = !string.IsNullOrWhiteSpace(waybillNo);
        var hasElectronicWaybill = !string.IsNullOrWhiteSpace(electronicWaybillNo);
        if (hasWaybill && hasElectronicWaybill)
            throw AppException.BadRequest("Normal irsaliye ve e-irsaliye numarası birlikte girilemez; yalnızca birini giriniz.");
        if (!hasWaybill && !hasElectronicWaybill && executionMode != GoodsReceiptExecutionMode.Import)
            throw AppException.BadRequest("Normal irsaliye numarası veya e-irsaliye numarasından biri zorunludur.");
        if ((hasWaybill || hasElectronicWaybill) && !waybillDate.HasValue)
            throw AppException.BadRequest("İrsaliye numarası girildiğinde irsaliye tarihi zorunludur.");
    }

    private static void ValidateTrackedLines(
        CreateManualGoodsReceiptRequest request,
        IReadOnlyDictionary<long, StockEntity> stocks,
        IReadOnlyDictionary<long, ResolvedQualityPolicy> qualityPolicies,
        IReadOnlyDictionary<long, EffectiveStockTrackingPolicy> trackingPolicies,
        bool requireCompleteCapture)
    {
        foreach (var line in request.Lines)
        {
            var qualityPolicy = qualityPolicies[line.StockId];
            var policy = trackingPolicies[line.StockId];
            if (policy.TrackingType == StockTrackingType.None
                && (qualityPolicy.RequireLot || qualityPolicy.RequireSerial || qualityPolicy.RequireExpiryDate))
                throw AppException.BadRequest(
                    $"{stocks[line.StockId].ErpStockCode}: kalite kuralı lot/seri/SKT isterken merkezî stok takip politikası Takipsiz olamaz.");
            var effectivePolicy = policy with
            {
                RequireLot = policy.RequireLot || qualityPolicy.RequireLot,
                RequireSerial = policy.RequireSerial || qualityPolicy.RequireSerial,
                RequireExpirationDate = policy.RequireExpirationDate || qualityPolicy.RequireExpiryDate
            };
            var submittedType = !string.IsNullOrWhiteSpace(line.SerialNo) && !string.IsNullOrWhiteSpace(line.LotNo)
                ? StockTrackingType.LotAndSerial
                : !string.IsNullOrWhiteSpace(line.SerialNo) ? StockTrackingType.Serial
                : !string.IsNullOrWhiteSpace(line.LotNo) ? StockTrackingType.Lot
                : StockTrackingType.None;
            try
            {
                StockTrackingPolicyGuard.Validate(
                    effectivePolicy,
                    line.Quantity,
                    submittedType,
                    submittedType == StockTrackingType.None
                        ? []
                        : [new StockTrackingCapture(line.Quantity, line.LotNo, line.SerialNo, line.ManufacturingDate, line.ExpirationDate)],
                    requireCompleteCapture: requireCompleteCapture
                        && effectivePolicy.TrackingType != StockTrackingType.None);
            }
            catch (StockTrackingPolicyViolationException exception)
            {
                throw AppException.BadRequest(exception.Message);
            }
            if (line.ManufacturingDate.HasValue && line.ExpirationDate.HasValue && line.ExpirationDate < line.ManufacturingDate)
                throw AppException.BadRequest("Son kullanma tarihi üretim tarihinden önce olamaz.");
        }
    }

    private static decimal Sample(decimal quantity, ResolvedQualityPolicy policy) => policy.SamplingMode switch
    {
        QualitySamplingMode.Percentage => Math.Min(quantity, Math.Ceiling(quantity * policy.SamplingValue / 100m)),
        QualitySamplingMode.FixedQuantity => Math.Min(quantity, policy.SamplingValue),
        _ => quantity
    };
    private static T Stamp<T>(T entity, long actor) where T : verii_wms_api_v2.Shared.Domain.BaseEntity { entity.CreatedBy = actor; entity.CreatedDate = DateTime.UtcNow; return entity; }
    private static string TaskNo(string documentNo)
    {
        var value = $"{documentNo}-RCV-01";
        return value.Length <= 50 ? value : value[..50];
    }
    private static string? Clean(string? value, int max) { var text = string.IsNullOrWhiteSpace(value) ? null : value.Trim(); return text?.Length > max ? text[..max] : text; }
    private static string? NormalizeDocumentNumber(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();
    private static string Hash(object value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value))));
    private static bool HashesMatch(string left, string right)
    {
        try
        {
            return CryptographicOperations.FixedTimeEquals(Convert.FromHexString(left), Convert.FromHexString(right));
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
