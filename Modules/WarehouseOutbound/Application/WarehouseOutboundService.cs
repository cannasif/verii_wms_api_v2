using System.Data;
using Microsoft.EntityFrameworkCore;
using verii_wms_api_v2.Modules.Audit.Application;
using verii_wms_api_v2.Modules.DocumentSeries.Application;
using verii_wms_api_v2.Modules.DocumentSeries.Domain;
using verii_wms_api_v2.Modules.Identity.Domain;
using verii_wms_api_v2.Modules.Location.Domain;
using verii_wms_api_v2.Modules.NetsisRead.Application;
using verii_wms_api_v2.Modules.NetsisRead.Application.Dtos;
using verii_wms_api_v2.Modules.Stock.Application;
using verii_wms_api_v2.Modules.StockTracking.Application;
using verii_wms_api_v2.Modules.WarehouseOutbound.Domain;
using verii_wms_api_v2.Modules.WarehouseOperations.Domain;
using verii_wms_api_v2.Shared;
using verii_wms_api_v2.Shared.Application.Abstractions.Persistence;
using verii_wms_api_v2.Shared.Application.Exceptions;
using CustomerEntity = verii_wms_api_v2.Modules.Customer.Domain.Customer;
using StockEntity = verii_wms_api_v2.Modules.Stock.Domain.Stock;
using WarehouseEntity = verii_wms_api_v2.Modules.Warehouse.Domain.Warehouse;
using YapEntity = verii_wms_api_v2.Modules.YapCode.Domain.YapCode;

namespace verii_wms_api_v2.Modules.WarehouseOutbound.Application;

public sealed class WarehouseOutboundService(
    IUnitOfWork uow,
    IWarehouseOutboundPolicyService policies,
    IDocumentNumberAllocator allocator,
    IAuditLogWriter audit,
    INetsisReadService netsis,
    IWarehouseOutboundReservationService reservations,
    IStockTrackingPolicyResolver trackingPolicyResolver) : IWarehouseOutboundService
{
    private IGenericRepository<WarehouseOutboundHeader> Headers => uow.Repository<WarehouseOutboundHeader>();
    private static readonly HashSet<string> LineSummaryColumns = new(StringComparer.OrdinalIgnoreCase)
    {
        nameof(WarehouseOutboundGridRow.LineCount),
        nameof(WarehouseOutboundGridRow.RequestedQuantity),
        nameof(WarehouseOutboundGridRow.PickedQuantity),
        nameof(WarehouseOutboundGridRow.PackedQuantity),
        nameof(WarehouseOutboundGridRow.LoadedQuantity),
        nameof(WarehouseOutboundGridRow.ShippedQuantity)
    };

    public Task<CreateWarehouseOutboundDraftResult> CreateDraftAsync(
        CreateWarehouseOutboundDraftRequest request,
        long actor,
        CancellationToken ct = default)
    {
        ValidateEnvelope(request);

        // Sipariş açığı ile yerel tahsis aynı kritik bölümde okunur. Böylece iki
        // eşzamanlı istek aynı açık miktarı birlikte tüketemez.
        return uow.ExecuteInTransactionAsync(
            token => CreateDraftCoreAsync(request, actor, token),
            ct,
            IsolationLevel.Serializable);
    }

    private async Task<CreateWarehouseOutboundDraftResult> CreateDraftCoreAsync(
        CreateWarehouseOutboundDraftRequest request,
        long actor,
        CancellationToken token)
    {
        var existing = await Headers.Query()
            .Include(x => x.Lines)
            .FirstOrDefaultAsync(x => x.CorrelationId == request.IdempotencyKey, token);

        if (existing is not null)
        {
            var replayTask = await uow.Repository<WarehouseOutboundTask>().Query()
                .OrderBy(x => x.Id)
                .FirstOrDefaultAsync(
                    x => x.WarehouseOutboundHeaderId == existing.Id && x.TaskType == WarehouseOutboundTaskType.Pick,
                    token);
            return new(
                existing.Id,
                existing.DocumentNo,
                existing.Lines.Count,
                existing.Lines.Sum(x => x.RequestedQuantity),
                true,
                replayTask?.Id,
                replayTask?.TaskNo);
        }

        var branch = request.BranchCode.Trim();
        var policy = await policies.GetAsync(branch, token);
        var taskBased = request.InitiationMode is
            WarehouseOutboundInitiationMode.OrderBasedTask or WarehouseOutboundInitiationMode.StockBasedTask;
        var orderBased = request.InitiationMode is
            WarehouseOutboundInitiationMode.OrderBasedTask or WarehouseOutboundInitiationMode.OrderBasedDirect;

        EnsureMode(request, policy, taskBased, orderBased);

        var assigneeIds = (request.AssignedUserIds ?? []).Distinct().ToArray();
        await ValidateAssigneesAsync(assigneeIds, taskBased, policy, token);

        var customer = await uow.Repository<CustomerEntity>().FirstOrDefaultAsync(
            x => x.Id == request.CustomerId && x.BranchCode == branch,
            false,
            token) ?? throw AppException.BadRequest("Cari bulunamadı.");

        var warehouse = await uow.Repository<WarehouseEntity>().FirstOrDefaultAsync(
            x => x.Id == request.SourceWarehouseId && x.BranchCode == branch,
            false,
            token) ?? throw AppException.BadRequest("Kaynak depo bulunamadı.");

        await ValidateLocationsAsync(request, warehouse.Id, policy, token);

        var stockIds = request.Lines.Select(x => x.StockId).Distinct().ToArray();
        var stocks = await uow.Repository<StockEntity>().Query()
            .Where(x => stockIds.Contains(x.Id) && x.BranchCode == branch)
            .ToDictionaryAsync(x => x.Id, token);
        if (stocks.Count != stockIds.Length)
            throw AppException.BadRequest("Seçilen stoklardan biri ERP mirror tablosunda bulunamadı.");
        var trackingPolicies = new Dictionary<long, EffectiveStockTrackingPolicy>();
        foreach (var stockId in stockIds)
            trackingPolicies[stockId] = await trackingPolicyResolver.ResolveAsync(branch, stockId, token);

        var yapIds = request.Lines
            .Where(x => x.YapCodeId.HasValue)
            .Select(x => x.YapCodeId!.Value)
            .Distinct()
            .ToArray();
        var yaps = await uow.Repository<YapEntity>().Query()
            .Where(x => yapIds.Contains(x.Id) && x.BranchCode == branch)
            .ToDictionaryAsync(x => x.Id, token);
        if (yaps.Count != yapIds.Length)
            throw AppException.BadRequest("Seçilen yapı kodlarından biri ERP mirror tablosunda bulunamadı.");

        ValidateTrackingPlans(request, trackingPolicies);

        if (orderBased)
            await ValidateOrderSourcesAsync(request, customer.CustomerCode, stocks, yaps, token);

        var number = await allocator.AllocateAsync(
            request.DocumentSeriesId,
            WmsDocumentType.WarehouseIssue,
            DateTime.UtcNow,
            token);

        var now = DateTime.UtcNow;
        var header = CreateHeader(request, actor, branch, customer, warehouse, policy, number, now, orderBased, taskBased);
        var sourceDocuments = CreateSourceDocuments(request, header, actor, branch, now, orderBased);
        var task = CreatePickTask(request, header, assigneeIds, actor, branch, now, number.DocumentNumber, taskBased);

        var lineNo = 0;
        foreach (var item in request.Lines)
        {
            var stock = stocks[item.StockId];
            var unit = StockUnitPolicy.Resolve(stock, item.UnitCode);
            var yap = item.YapCodeId.HasValue ? yaps[item.YapCodeId.Value] : null;
            var trackingPolicy = trackingPolicies[item.StockId];
            var line = new WarehouseOutboundLine
            {
                BranchCode = branch,
                CreatedBy = actor,
                CreatedDate = now,
                LineNo = ++lineNo,
                StockId = stock.Id,
                StockCodeSnapshot = stock.ErpStockCode,
                StockNameSnapshot = stock.StockName,
                YapCodeId = yap?.Id,
                YapCodeSnapshot = yap?.ConfigurationCode,
                UnitCode = unit,
                RequestedQuantity = item.Quantity,
                TrackingType = trackingPolicy.TrackingType,
                RequireHandlingUnit = item.RequireHandlingUnit,
                DefaultSourceLocationId = item.SourceLocationId,
                Description = Clean(item.Description, 1000),
                ProjectCode = Clean(item.ProjectCode, 50) ?? Clean(request.ProjectCode, 50)
            };

            foreach (var tracking in item.Trackings ?? [])
            {
                line.Trackings.Add(new WarehouseOutboundTracking
                {
                    BranchCode = branch,
                    CreatedBy = actor,
                    CreatedDate = now,
                    HandlingUnitNo = Clean(tracking.HandlingUnitNo, 100),
                    ContainerNo = Clean(tracking.ContainerNo, 100),
                    LotNo = Clean(tracking.LotNo, 100),
                    SerialNo = Clean(tracking.SerialNo, 200),
                    ManufacturingDate = tracking.ManufacturingDate,
                    ExpirationDate = tracking.ExpirationDate,
                    PlannedQuantity = tracking.Quantity,
                    SourceLocationId = tracking.SourceLocationId ?? item.SourceLocationId
                });
            }

            if (item.Source is not null)
            {
                var source = item.Source;
                line.Sources.Add(new WarehouseOutboundLineSource
                {
                    BranchCode = branch,
                    CreatedBy = actor,
                    CreatedDate = now,
                    Line = line,
                    SourceDocument = sourceDocuments[source.OrderNumber.Trim()],
                    ExternalLineId = source.ExternalLineId.Trim(),
                    ExternalLineNo = source.ExternalLineNo,
                    ExternalStockCode = source.ExternalStockCode.Trim(),
                    ExternalYapCode = Clean(source.ExternalYapCode, 100),
                    OrderedQuantity = source.OrderedQuantity,
                    PreviouslyShippedQuantity = source.PreviouslyShippedQuantity,
                    AllocatedQuantity = item.Quantity,
                    UnitCode = unit
                });
            }

            header.Lines.Add(line);
            if (task is not null)
            {
                task.Lines.Add(new WarehouseOutboundTaskLine
                {
                    BranchCode = branch,
                    CreatedBy = actor,
                    CreatedDate = now,
                    Task = task,
                    Line = line,
                    PlannedQuantity = item.Quantity,
                    SourceLocationId = item.SourceLocationId
                });
            }
        }

        header.StatusHistory.Add(new WarehouseOutboundStatusHistory
        {
            BranchCode = branch,
            CreatedBy = actor,
            CreatedDate = now,
            Header = header,
            ToStatus = WarehouseOutboundStatus.Draft.ToString(),
            Description = "Sevk taslağı oluşturuldu.",
            ChangedAtUtc = DateTimeOffset.UtcNow,
            ChangedBy = actor,
            CorrelationId = request.IdempotencyKey
        });

        await Headers.AddAsync(header, token);
        await uow.SaveChangesAsync(token);
        if (header.ReservationPolicy == WarehouseOutboundReservationPolicy.OnCreate)
        {
            await reservations.ReserveAsync(header, $"WO:{header.Id}:RESERVE:CREATE", actor, token);
            await uow.SaveChangesAsync(token);
        }

        var result = new CreateWarehouseOutboundDraftResult(
            header.Id,
            header.DocumentNo,
            header.Lines.Count,
            header.Lines.Sum(x => x.RequestedQuantity),
            false,
            task?.Id,
            task?.TaskNo);

        await audit.WriteAsync(new(
            "warehouse-outbound.draft.create",
            nameof(WarehouseOutboundHeader),
            header.Id.ToString(),
            "Succeeded",
            "warehouse-outbound",
            NewValues: result,
            ChangedFields: ["Header", "Sources", "Lines", "Trackings", "Task", "Assignments"]), token);

        return result;
    }

    public async Task<PagedResponse<WarehouseOutboundGridRow>> GetPagedAsync(
        PagedRequest request,
        CancellationToken ct = default)
    {
        var warehouses = uow.Repository<WarehouseEntity>().Query(ignoreQueryFilters: true);
        var lines = uow.Repository<WarehouseOutboundLine>().Query();
        var headers = Headers.Query();
        var query = BuildPagedQuery(request, headers, warehouses, lines);
        var countQuery = BuildCountQuery(request, headers, warehouses, lines);
        var page = await query.ToPagedResponseAsync(countQuery, request, ct);
        if (RequiresLineSummaryInMainQuery(request) || page.Items.Count == 0) return page;

        return new PagedResponse<WarehouseOutboundGridRow>
        {
            Items = await EnrichLineSummaryAsync(page.Items, lines, ct),
            TotalCount = page.TotalCount,
            PageNumber = page.PageNumber,
            PageSize = page.PageSize
        };
    }

    internal static IQueryable<WarehouseOutboundGridRow> BuildPagedQuery(
        PagedRequest request,
        IQueryable<WarehouseOutboundHeader> headers,
        IQueryable<WarehouseEntity> warehouses,
        IQueryable<WarehouseOutboundLine> lines)
    {
        var query = BuildGridRows(
            request,
            headers,
            warehouses,
            lines,
            RequiresLineSummaryInMainQuery(request));
        if (request.HasExplicitSearchFields) query = query.ApplySearch(request);
        return query.ApplyAdvancedFilters(request)
            .ApplySort(request, nameof(WarehouseOutboundGridRow.CreatedDate))
            .Select(x => new WarehouseOutboundGridRow(
                x.Id, x.BranchCode, x.DocumentNo, x.DocumentDate, x.InitiationMode, x.Status,
                x.ApprovalStatus, x.ErpIntegrationStatus, x.CustomerId, x.CustomerCode, x.CustomerName,
                x.SourceWarehouseId, x.SourceWarehouseCode, x.SourceWarehouseName, x.LineCount,
                x.RequestedQuantity, x.PickedQuantity, x.PackedQuantity, x.LoadedQuantity,
                x.ShippedQuantity, x.Priority, x.PlannedWarehouseOutboundAtUtc, x.CreatedBy,
                x.CreatedDate, x.UpdatedBy, x.UpdatedDate));
    }

    internal static IQueryable<long> BuildCountQuery(
        PagedRequest request,
        IQueryable<WarehouseOutboundHeader> headers,
        IQueryable<WarehouseEntity> warehouses,
        IQueryable<WarehouseOutboundLine> lines)
    {
        var includeLineSummary = RequiresLineSummaryForCount(request);
        var query = BuildGridRows(request, headers, warehouses, lines, includeLineSummary);
        if (request.HasExplicitSearchFields) query = query.ApplySearch(request);
        return query.ApplyAdvancedFilters(request).Select(x => x.Id);
    }

    private static IQueryable<WarehouseOutboundGridProjection> BuildGridRows(
        PagedRequest request,
        IQueryable<WarehouseOutboundHeader> headers,
        IQueryable<WarehouseEntity> warehouses,
        IQueryable<WarehouseOutboundLine> lines,
        bool includeLineSummary)
    {
        var search = request.LegacySearch?.Trim();
        var baseQuery =
            from header in headers
            join warehouse in warehouses on header.SourceWarehouseId equals warehouse.Id
            where string.IsNullOrWhiteSpace(search)
                  || header.DocumentNo.Contains(search)
                  || header.CustomerCodeSnapshot.Contains(search)
                  || (header.CustomerNameSnapshot != null && header.CustomerNameSnapshot.Contains(search))
                  || warehouse.WarehouseName.Contains(search)
                  || (header.ExternalReferenceNo != null && header.ExternalReferenceNo.Contains(search))
            select new { Header = header, Warehouse = warehouse };

        if (!includeLineSummary)
            return baseQuery.Select(item => new WarehouseOutboundGridProjection
            {
                Id = item.Header.Id,
                BranchCode = item.Header.BranchCode,
                DocumentNo = item.Header.DocumentNo,
                DocumentDate = item.Header.DocumentDate,
                InitiationMode = item.Header.InitiationMode,
                Status = item.Header.Status,
                ApprovalStatus = item.Header.ApprovalStatus,
                ErpIntegrationStatus = item.Header.ErpIntegrationStatus,
                CustomerId = item.Header.CustomerId,
                CustomerCode = item.Header.CustomerCodeSnapshot,
                CustomerName = item.Header.CustomerNameSnapshot,
                SourceWarehouseId = item.Header.SourceWarehouseId,
                SourceWarehouseCode = item.Warehouse.WarehouseCode,
                SourceWarehouseName = item.Warehouse.WarehouseName,
                Priority = item.Header.Priority,
                PlannedWarehouseOutboundAtUtc = item.Header.PlannedWarehouseOutboundAtUtc,
                CreatedBy = item.Header.CreatedBy,
                CreatedDate = item.Header.CreatedDate,
                UpdatedBy = item.Header.UpdatedBy,
                UpdatedDate = item.Header.UpdatedDate
            });

        var totals =
            from line in lines
            group line by line.WarehouseOutboundHeaderId into groupRows
            select new
            {
                HeaderId = groupRows.Key,
                LineCount = groupRows.Count(),
                RequestedQuantity = groupRows.Sum(x => x.RequestedQuantity),
                PickedQuantity = groupRows.Sum(x => x.PickedQuantity),
                PackedQuantity = groupRows.Sum(x => x.PackedQuantity),
                LoadedQuantity = groupRows.Sum(x => x.LoadedQuantity),
                ShippedQuantity = groupRows.Sum(x => x.ShippedQuantity)
            };
        return
            from item in baseQuery
            join total in totals on item.Header.Id equals total.HeaderId into totalRows
            from total in totalRows.DefaultIfEmpty()
            select new WarehouseOutboundGridProjection
            {
                Id = item.Header.Id,
                BranchCode = item.Header.BranchCode,
                DocumentNo = item.Header.DocumentNo,
                DocumentDate = item.Header.DocumentDate,
                InitiationMode = item.Header.InitiationMode,
                Status = item.Header.Status,
                ApprovalStatus = item.Header.ApprovalStatus,
                ErpIntegrationStatus = item.Header.ErpIntegrationStatus,
                CustomerId = item.Header.CustomerId,
                CustomerCode = item.Header.CustomerCodeSnapshot,
                CustomerName = item.Header.CustomerNameSnapshot,
                SourceWarehouseId = item.Header.SourceWarehouseId,
                SourceWarehouseCode = item.Warehouse.WarehouseCode,
                SourceWarehouseName = item.Warehouse.WarehouseName,
                LineCount = (int?)total.LineCount ?? 0,
                RequestedQuantity = (decimal?)total.RequestedQuantity ?? 0,
                PickedQuantity = (decimal?)total.PickedQuantity ?? 0,
                PackedQuantity = (decimal?)total.PackedQuantity ?? 0,
                LoadedQuantity = (decimal?)total.LoadedQuantity ?? 0,
                ShippedQuantity = (decimal?)total.ShippedQuantity ?? 0,
                Priority = item.Header.Priority,
                PlannedWarehouseOutboundAtUtc = item.Header.PlannedWarehouseOutboundAtUtc,
                CreatedBy = item.Header.CreatedBy,
                CreatedDate = item.Header.CreatedDate,
                UpdatedBy = item.Header.UpdatedBy,
                UpdatedDate = item.Header.UpdatedDate
            };
    }

    private static bool RequiresLineSummaryForCount(PagedRequest request) =>
        (!string.IsNullOrWhiteSpace(request.EffectiveSearch)
         && request.SearchFields.Any(LineSummaryColumns.Contains))
        || request.Filters.Any(filter => LineSummaryColumns.Contains(filter.Column));

    private static bool RequiresLineSummaryInMainQuery(PagedRequest request) =>
        RequiresLineSummaryForCount(request)
        || LineSummaryColumns.Contains(request.SortBy ?? string.Empty);

    private static async Task<IReadOnlyList<WarehouseOutboundGridRow>> EnrichLineSummaryAsync(
        IReadOnlyList<WarehouseOutboundGridRow> rows,
        IQueryable<WarehouseOutboundLine> lines,
        CancellationToken cancellationToken)
    {
        var headerIds = rows.Select(x => x.Id).ToArray();
        var totals = await lines
            .Where(x => headerIds.Contains(x.WarehouseOutboundHeaderId))
            .GroupBy(x => x.WarehouseOutboundHeaderId)
            .Select(groupRows => new
            {
                HeaderId = groupRows.Key,
                LineCount = groupRows.Count(),
                RequestedQuantity = groupRows.Sum(x => x.RequestedQuantity),
                PickedQuantity = groupRows.Sum(x => x.PickedQuantity),
                PackedQuantity = groupRows.Sum(x => x.PackedQuantity),
                LoadedQuantity = groupRows.Sum(x => x.LoadedQuantity),
                ShippedQuantity = groupRows.Sum(x => x.ShippedQuantity)
            })
            .ToDictionaryAsync(x => x.HeaderId, cancellationToken);

        return rows.Select(row => totals.TryGetValue(row.Id, out var total)
            ? row with
            {
                LineCount = total.LineCount,
                RequestedQuantity = total.RequestedQuantity,
                PickedQuantity = total.PickedQuantity,
                PackedQuantity = total.PackedQuantity,
                LoadedQuantity = total.LoadedQuantity,
                ShippedQuantity = total.ShippedQuantity
            }
            : row).ToArray();
    }

    private sealed class WarehouseOutboundGridProjection
    {
        public long Id { get; init; }
        public required string BranchCode { get; init; }
        public required string DocumentNo { get; init; }
        public DateOnly DocumentDate { get; init; }
        public WarehouseOutboundInitiationMode InitiationMode { get; init; }
        public WarehouseOutboundStatus Status { get; init; }
        public OperationApprovalStatus ApprovalStatus { get; init; }
        public ErpIntegrationStatus ErpIntegrationStatus { get; init; }
        public long CustomerId { get; init; }
        public required string CustomerCode { get; init; }
        public string? CustomerName { get; init; }
        public long SourceWarehouseId { get; init; }
        public int SourceWarehouseCode { get; init; }
        public required string SourceWarehouseName { get; init; }
        public int LineCount { get; init; }
        public decimal RequestedQuantity { get; init; }
        public decimal PickedQuantity { get; init; }
        public decimal PackedQuantity { get; init; }
        public decimal LoadedQuantity { get; init; }
        public decimal ShippedQuantity { get; init; }
        public byte Priority { get; init; }
        public DateTimeOffset? PlannedWarehouseOutboundAtUtc { get; init; }
        public long? CreatedBy { get; init; }
        public DateTime? CreatedDate { get; init; }
        public long? UpdatedBy { get; init; }
        public DateTime? UpdatedDate { get; init; }
    }

    public async Task<WarehouseOutboundDetail> GetDetailAsync(long id, CancellationToken ct = default)
    {
        var warehouses = uow.Repository<WarehouseEntity>().Query(ignoreQueryFilters: true);
        var warehouseOutboundLines = uow.Repository<WarehouseOutboundLine>().Query();
        var header = await (
            from entity in Headers.Query()
            join warehouse in warehouses on entity.SourceWarehouseId equals warehouse.Id
            where entity.Id == id
            select new WarehouseOutboundGridRow(
                entity.Id,
                entity.BranchCode,
                entity.DocumentNo,
                entity.DocumentDate,
                entity.InitiationMode,
                entity.Status,
                entity.ApprovalStatus,
                entity.ErpIntegrationStatus,
                entity.CustomerId,
                entity.CustomerCodeSnapshot,
                entity.CustomerNameSnapshot,
                entity.SourceWarehouseId,
                warehouse.WarehouseCode,
                warehouse.WarehouseName,
                warehouseOutboundLines.Count(x => x.WarehouseOutboundHeaderId == entity.Id),
                warehouseOutboundLines.Where(x => x.WarehouseOutboundHeaderId == entity.Id).Sum(x => (decimal?)x.RequestedQuantity) ?? 0,
                warehouseOutboundLines.Where(x => x.WarehouseOutboundHeaderId == entity.Id).Sum(x => (decimal?)x.PickedQuantity) ?? 0,
                warehouseOutboundLines.Where(x => x.WarehouseOutboundHeaderId == entity.Id).Sum(x => (decimal?)x.PackedQuantity) ?? 0,
                warehouseOutboundLines.Where(x => x.WarehouseOutboundHeaderId == entity.Id).Sum(x => (decimal?)x.LoadedQuantity) ?? 0,
                warehouseOutboundLines.Where(x => x.WarehouseOutboundHeaderId == entity.Id).Sum(x => (decimal?)x.ShippedQuantity) ?? 0,
                entity.Priority,
                entity.PlannedWarehouseOutboundAtUtc,
                entity.CreatedBy,
                entity.CreatedDate,
                entity.UpdatedBy,
                entity.UpdatedDate))
            .SingleOrDefaultAsync(ct) ?? throw AppException.NotFound("Sevk kaydı bulunamadı.");
        var tracking = uow.Repository<WarehouseOutboundTracking>().Query();
        var lines = await uow.Repository<WarehouseOutboundLine>().Query()
            .Where(x => x.WarehouseOutboundHeaderId == id)
            .OrderBy(x => x.LineNo)
            .Select(x => new WarehouseOutboundDetailLine(
                x.Id,
                x.LineNo,
                x.StockId,
                x.StockCodeSnapshot,
                x.StockNameSnapshot,
                x.YapCodeSnapshot,
                x.UnitCode,
                x.RequestedQuantity,
                x.ReservedQuantity,
                x.PickedQuantity,
                x.PackedQuantity,
                x.LoadedQuantity,
                x.ShippedQuantity,
                x.Status,
                tracking.Count(t => t.WarehouseOutboundLineId == x.Id),
                x.TrackingType,
                x.RequireHandlingUnit,
                x.ProjectCode))
            .ToListAsync(ct);
        var draft = await Headers.Query().Where(x => x.Id == id).Select(x => new
        {
            x.RowVersion, x.StagingLocationId, x.LoadingLocationId, x.ExternalReferenceNo, x.IsEDispatch,
            x.CarrierCode, x.CarrierName, x.VehiclePlate, x.TrailerPlate, x.DriverName, x.SealNo, x.Description,
            x.ProjectCode, x.CostCenterCode, x.MovementTypeCode, x.ExitLocationCode
        }).SingleAsync(ct);
        return new(header, lines, Convert.ToBase64String(draft.RowVersion), new(draft.StagingLocationId, draft.LoadingLocationId,
            draft.ExternalReferenceNo, draft.IsEDispatch, draft.CarrierCode, draft.CarrierName, draft.VehiclePlate,
            draft.TrailerPlate, draft.DriverName, draft.SealNo, draft.Description, draft.ProjectCode,
            draft.CostCenterCode, draft.MovementTypeCode, draft.ExitLocationCode));
    }

    public Task<WarehouseOutboundDetail> UpdateDraftAsync(long id, UpdateWarehouseOutboundDraftRequest request, long actor, CancellationToken ct = default) =>
        uow.ExecuteInTransactionAsync(async token =>
        {
            if (id <= 0 || request.Priority is < 1 or > 5) throw AppException.BadRequest("Sevk ve öncelik bilgisi geçersiz.");
            var header = await Headers.Query().FirstOrDefaultAsync(x => x.Id == id, token)
                ?? throw AppException.NotFound("Sevk kaydı bulunamadı.");
            if (header.Status != WarehouseOutboundStatus.Draft) throw AppException.Conflict("Yalnızca taslak sevk bilgileri güncellenebilir.");
            EnsureRowVersion(header.RowVersion, request.RowVersion);
            var locationIds = new long?[] { request.StagingLocationId, request.LoadingLocationId }.Where(x => x.HasValue).Select(x => x!.Value).Distinct().ToArray();
            var locations = await uow.Repository<WarehouseLocation>().Query().Where(x => locationIds.Contains(x.Id) && x.IsActive).ToDictionaryAsync(x => x.Id, token);
            if (locations.Count != locationIds.Length || locations.Values.Any(x => x.WarehouseId != header.SourceWarehouseId))
                throw AppException.BadRequest("Hazırlık ve yükleme rafları kaynak depoya ait ve aktif olmalıdır.");
            var old = new { header.DocumentDate, header.StagingLocationId, header.LoadingLocationId, header.PlannedWarehouseOutboundAtUtc, header.Priority,
                header.ExternalReferenceNo, header.IsEDispatch, header.CarrierCode, header.CarrierName, header.VehiclePlate, header.TrailerPlate,
                header.DriverName, header.SealNo, header.Description, header.ProjectCode, header.CostCenterCode,
                header.MovementTypeCode, header.ExitLocationCode };
            header.DocumentDate = request.DocumentDate;
            header.StagingLocationId = request.StagingLocationId;
            header.LoadingLocationId = request.LoadingLocationId;
            header.PlannedWarehouseOutboundAtUtc = request.PlannedWarehouseOutboundAtUtc?.ToUniversalTime();
            header.Priority = request.Priority;
            header.ExternalReferenceNo = Clean(request.ExternalReferenceNo, 100);
            header.IsEDispatch = request.IsEDispatch;
            header.CarrierCode = Clean(request.CarrierCode, 50);
            header.CarrierName = Clean(request.CarrierName, 200);
            header.VehiclePlate = Clean(request.VehiclePlate, 20);
            header.TrailerPlate = Clean(request.TrailerPlate, 20);
            header.DriverName = Clean(request.DriverName, 200);
            header.SealNo = Clean(request.SealNo, 100);
            header.Description = Clean(request.Description, 2000);
            header.ProjectCode = Clean(request.ProjectCode, 50);
            header.CostCenterCode = Clean(request.CostCenterCode, 100);
            header.MovementTypeCode = Clean(request.MovementTypeCode, 50);
            header.ExitLocationCode = Clean(request.ExitLocationCode, 100);
            header.UpdatedBy = actor;
            header.UpdatedDate = DateTime.UtcNow;
            try { await uow.SaveChangesAsync(token); }
            catch (DbUpdateConcurrencyException) { throw AppException.Conflict("Sevk başka bir kullanıcı tarafından değiştirildi. Listeyi yenileyip tekrar deneyin."); }
            await audit.WriteAsync(new("warehouse-outbound.draft.update", nameof(WarehouseOutboundHeader), id.ToString(), "Succeeded", "warehouse-outbound", OldValues: old,
                NewValues: new { header.DocumentDate, header.StagingLocationId, header.LoadingLocationId, header.PlannedWarehouseOutboundAtUtc, header.Priority,
                    header.ExternalReferenceNo, header.IsEDispatch, header.CarrierCode, header.CarrierName, header.VehiclePlate, header.TrailerPlate,
                    header.DriverName, header.SealNo, header.Description, header.ProjectCode, header.CostCenterCode,
                    header.MovementTypeCode, header.ExitLocationCode }, ChangedFields: ["Header"]), token);
            return await GetDetailAsync(id, token);
        }, ct);

    public Task DeleteDraftAsync(long id, long actor, CancellationToken ct = default) =>
        uow.ExecuteInTransactionAsync(async token =>
        {
            var header = await Headers.Query().Include(x => x.Lines).ThenInclude(x => x.Trackings).FirstOrDefaultAsync(x => x.Id == id, token)
                ?? throw AppException.NotFound("Sevk kaydı bulunamadı.");
            if (header.Status != WarehouseOutboundStatus.Draft)
                throw AppException.Conflict("Yalnızca taslak sevk silinebilir. Başlatılmış sevk için iptal işlemini kullanın.");
            if (await uow.Repository<Modules.StockMovement.Domain.StockMovementOperation>().Query()
                .AnyAsync(x => x.ReferenceType == "WarehouseOutbound" && x.ReferenceId == id, token))
                throw AppException.Conflict("Stok hareketi bulunan sevk silinemez; iptal ve ters hareket kullanılmalıdır.");
            await reservations.ReleaseAllAsync(header, $"WO:{id}:RESERVE:DELETE", "Taslak sevk silindi.", actor, token);
            var now = DateTime.UtcNow;
            var lineIds = header.Lines.Select(x => x.Id).ToArray();
            var taskIds = await uow.Repository<WarehouseOutboundTask>().Query().Where(x => x.WarehouseOutboundHeaderId == id).Select(x => x.Id).ToArrayAsync(token);
            await SoftDelete(uow.Repository<WarehouseOutboundLineSource>().Query(), x => lineIds.Contains(x.WarehouseOutboundLineId), actor, now, token);
            await SoftDelete(uow.Repository<WarehouseOutboundTracking>().Query(), x => lineIds.Contains(x.WarehouseOutboundLineId), actor, now, token);
            await SoftDelete(uow.Repository<WarehouseOutboundTaskAssignment>().Query(), x => taskIds.Contains(x.WarehouseOutboundTaskId), actor, now, token);
            await SoftDelete(uow.Repository<WarehouseOutboundTaskLine>().Query(), x => taskIds.Contains(x.WarehouseOutboundTaskId), actor, now, token);
            await SoftDelete(uow.Repository<WarehouseOutboundStatusHistory>().Query(), x => x.WarehouseOutboundHeaderId == id, actor, now, token);
            await SoftDelete(uow.Repository<WarehouseOutboundTask>().Query(), x => x.WarehouseOutboundHeaderId == id, actor, now, token);
            await SoftDelete(uow.Repository<WarehouseOutboundLine>().Query(), x => x.WarehouseOutboundHeaderId == id, actor, now, token);
            await SoftDelete(uow.Repository<WarehouseOutboundSourceDocument>().Query(), x => x.WarehouseOutboundHeaderId == id, actor, now, token);
            header.IsDeleted = true; header.DeletedBy = actor; header.DeletedDate = now;
            await uow.SaveChangesAsync(token);
            await audit.WriteAsync(new("warehouse-outbound.draft.delete", nameof(WarehouseOutboundHeader), id.ToString(), "Succeeded", "warehouse-outbound",
                OldValues: new { header.DocumentNo, header.Status }, ChangedFields: ["IsDeleted"]), token);
            return true;
        }, ct);

    private async Task ValidateAssigneesAsync(
        long[] assigneeIds,
        bool taskBased,
        WarehouseOutboundPolicyDto policy,
        CancellationToken token)
    {
        if (taskBased && policy.RequireAssigneeForTask && assigneeIds.Length == 0)
            throw AppException.BadRequest("Emirli sevkte kullanıcı ataması zorunludur.");
        if (!policy.AllowMultipleAssignees && assigneeIds.Length > 1)
            throw AppException.BadRequest("Birden fazla kullanıcı atamasına izin verilmiyor.");
        if (assigneeIds.Length == 0) return;

        var activeCount = await uow.Repository<User>().Query()
            .CountAsync(x => assigneeIds.Contains(x.Id) && x.IsActive, token);
        if (activeCount != assigneeIds.Length)
            throw AppException.BadRequest("Atanan kullanıcılardan biri bulunamadı veya aktif değil.");
    }

    private async Task ValidateLocationsAsync(
        CreateWarehouseOutboundDraftRequest request,
        long warehouseId,
        WarehouseOutboundPolicyDto policy,
        CancellationToken token)
    {
        var locationIds = request.Lines.Select(x => x.SourceLocationId)
            .Concat([request.StagingLocationId, request.LoadingLocationId])
            .Where(x => x.HasValue)
            .Select(x => x!.Value)
            .Distinct()
            .ToArray();
        var validCount = await uow.Repository<WarehouseLocation>().Query()
            .CountAsync(x => locationIds.Contains(x.Id) && x.IsActive && x.WarehouseId == warehouseId, token);
        if (validCount != locationIds.Length)
            throw AppException.BadRequest("Seçilen raflardan biri kaynak depoya ait değil veya aktif değil.");
        if (policy.RequireSourceLocation && request.Lines.Any(x => !x.SourceLocationId.HasValue))
            throw AppException.BadRequest("Sevk politikası kaynak rafı kalem bazında zorunlu tutuyor.");
    }

    private async Task ValidateOrderSourcesAsync(
        CreateWarehouseOutboundDraftRequest request,
        string customerCode,
        IReadOnlyDictionary<long, StockEntity> stocks,
        IReadOnlyDictionary<long, YapEntity> yaps,
        CancellationToken token)
    {
        var orderNumbers = request.Lines
            .Select(x => x.Source!.OrderNumber.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var liveRows = await netsis.GetShipmentOpenOrderLinesAsync(
            string.Join(',', orderNumbers),
            request.BranchCode,
            token);
        var liveByKey = liveRows.ToDictionary(
            x => SourceKey(x.OrderNumber, x.OrderId.ToString()),
            StringComparer.OrdinalIgnoreCase);

        var externalIds = request.Lines.Select(x => x.Source!.ExternalLineId.Trim()).Distinct().ToArray();
        var localAllocations = await uow.Repository<WarehouseOutboundLineSource>().Query()
            .Where(x =>
                orderNumbers.Contains(x.SourceDocument.ExternalDocumentNo)
                && externalIds.Contains(x.ExternalLineId)
                && x.Line.Header.Status != WarehouseOutboundStatus.Cancelled)
            .GroupBy(x => new { x.SourceDocument.ExternalDocumentNo, x.ExternalLineId })
            .Select(x => new
            {
                Key = x.Key.ExternalDocumentNo + "|" + x.Key.ExternalLineId,
                Quantity = x.Sum(y => y.AllocatedQuantity)
            })
            .ToDictionaryAsync(x => x.Key, x => x.Quantity, StringComparer.OrdinalIgnoreCase, token);

        foreach (var line in request.Lines)
        {
            var source = line.Source!;
            var key = SourceKey(source.OrderNumber, source.ExternalLineId);
            if (!liveByKey.TryGetValue(key, out ShipmentOpenOrderLineDto? live))
                throw AppException.BadRequest($"{source.OrderNumber}/{source.ExternalLineId} Netsis açık siparişlerinde bulunamadı.");
            if (!string.Equals(live.CustomerCode, customerCode, StringComparison.OrdinalIgnoreCase))
                throw AppException.BadRequest($"{source.OrderNumber} seçilen cariye ait değil.");
            if (!string.Equals(live.StockCode, stocks[line.StockId].ErpStockCode, StringComparison.OrdinalIgnoreCase))
                throw AppException.BadRequest($"{source.OrderNumber}/{source.ExternalLineId} stok eşleşmesi geçersiz.");

            var selectedYap = line.YapCodeId.HasValue ? yaps[line.YapCodeId.Value].ConfigurationCode : null;
            if (!string.Equals(Normalize(live.YapCode), Normalize(selectedYap), StringComparison.OrdinalIgnoreCase))
                throw AppException.BadRequest($"{source.OrderNumber}/{source.ExternalLineId} yapı kodu eşleşmesi geçersiz.");

            var alreadyAllocated = localAllocations.GetValueOrDefault(key);
            var available = Math.Max(0, (live.RemainingQuantity ?? 0) - alreadyAllocated);
            if (line.Quantity > available)
                throw AppException.BadRequest(
                    $"{source.OrderNumber}/{source.ExternalLineId} için istenen {line.Quantity} miktarı güncel {available} açık miktarı aşıyor.");
        }
    }

    private static void ValidateEnvelope(CreateWarehouseOutboundDraftRequest request)
    {
        if (request.IdempotencyKey == Guid.Empty)
            throw AppException.BadRequest("Idempotency anahtarı zorunludur.");
        if (string.IsNullOrWhiteSpace(request.BranchCode))
            throw AppException.BadRequest("Şube kodu zorunludur.");
        if (request.DocumentSeriesId <= 0 || request.CustomerId <= 0 || request.SourceWarehouseId <= 0)
            throw AppException.BadRequest("Cari, kaynak depo ve belge serisi zorunludur.");
        if (request.Priority is < 1 or > 9)
            throw AppException.BadRequest("Öncelik 1-9 arasında olmalıdır.");
        if (request.Lines.Count == 0)
            throw AppException.BadRequest("En az bir sevk kalemi zorunludur.");
        if (request.Lines.Any(x => x.StockId <= 0 || x.Quantity <= 0))
            throw AppException.BadRequest("Sevk kalemlerinde stok ve pozitif miktar zorunludur.");
    }

    private static void EnsureMode(
        CreateWarehouseOutboundDraftRequest request,
        WarehouseOutboundPolicyDto policy,
        bool taskBased,
        bool orderBased)
    {
        var allowed = request.InitiationMode switch
        {
            WarehouseOutboundInitiationMode.OrderBasedTask => policy.AllowOrderBasedTask,
            WarehouseOutboundInitiationMode.StockBasedTask => policy.AllowStockBasedTask,
            WarehouseOutboundInitiationMode.OrderBasedDirect => policy.AllowOrderBasedDirect,
            WarehouseOutboundInitiationMode.StockBasedDirect => policy.AllowStockBasedDirect,
            _ => false
        };
        if (!allowed)
            throw AppException.BadRequest("Seçilen sevk türü politikada kapalıdır.");
        if (orderBased && request.Lines.Any(x => x.Source is null))
            throw AppException.BadRequest("Siparişli sevkte her kalemin Netsis sipariş kaynağı olmalıdır.");
        if (!orderBased && request.Lines.Any(x => x.Source is not null))
            throw AppException.BadRequest("Siparişsiz sevkte sipariş kaynağı gönderilemez.");
        if (!taskBased && (request.AssignedUserIds?.Count ?? 0) > 0)
            throw AppException.BadRequest("Doğrudan sevkte kullanıcı görevi atanamaz.");
        if (policy.RequireWarehouseOutboundInformation
            && string.IsNullOrWhiteSpace(request.VehiclePlate)
            && string.IsNullOrWhiteSpace(request.CarrierCode))
            throw AppException.BadRequest("Araç plakası veya taşıyıcı bilgisi zorunludur.");
    }

    private static void ValidateTrackingPlans(
        CreateWarehouseOutboundDraftRequest request,
        IReadOnlyDictionary<long, EffectiveStockTrackingPolicy> policies)
    {
        var serials = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in request.Lines)
        {
            var trackings = line.Trackings ?? [];
            var policy = policies[line.StockId];
            try
            {
                StockTrackingPolicyGuard.Validate(
                    policy,
                    line.Quantity,
                    line.TrackingType,
                    trackings.Select(x => new StockTrackingCapture(
                        x.Quantity, x.LotNo, x.SerialNo, x.ManufacturingDate, x.ExpirationDate)).ToArray(),
                    requireCompleteCapture: policy.TrackingType != StockTrackingType.None);
            }
            catch (StockTrackingPolicyViolationException exception)
            {
                throw AppException.BadRequest(exception.Message);
            }
            if (line.RequireHandlingUnit && trackings.Count > 0
                && trackings.Any(x => string.IsNullOrWhiteSpace(x.HandlingUnitNo)))
                throw AppException.BadRequest("Palet/kasa zorunlu kalemde her takip satırının taşıma birimi olmalıdır.");
            foreach (var serial in trackings.Select(x => x.SerialNo).Where(x => !string.IsNullOrWhiteSpace(x)))
                if (!serials.Add(serial!.Trim()))
                    throw AppException.BadRequest($"Aynı seri numarası sevk içinde tekrar edemez: {serial}");
        }
    }

    private static WarehouseOutboundHeader CreateHeader(
        CreateWarehouseOutboundDraftRequest request,
        long actor,
        string branch,
        CustomerEntity customer,
        WarehouseEntity warehouse,
        WarehouseOutboundPolicyDto policy,
        AllocatedDocumentNumber number,
        DateTime now,
        bool orderBased,
        bool taskBased) =>
        new()
        {
            BranchCode = branch,
            CreatedBy = actor,
            CreatedDate = now,
            DocumentSeriesId = number.DocumentSeriesId,
            DocumentNo = number.DocumentNumber,
            DocumentDate = request.DocumentDate,
            InitiationMode = request.InitiationMode,
            SourceSystem = orderBased ? WarehouseOperationSourceSystem.Netsis : WarehouseOperationSourceSystem.Manual,
            CorrelationId = request.IdempotencyKey,
            CustomerId = customer.Id,
            CustomerCodeSnapshot = customer.CustomerCode,
            CustomerNameSnapshot = customer.CustomerName,
            SourceWarehouseId = warehouse.Id,
            StagingLocationId = request.StagingLocationId,
            LoadingLocationId = request.LoadingLocationId,
            PlannedWarehouseOutboundAtUtc = request.PlannedWarehouseOutboundAtUtc?.ToUniversalTime(),
            Priority = request.Priority,
            ExternalReferenceNo = Clean(request.ExternalReferenceNo, 100),
            IsEDispatch = request.IsEDispatch,
            CarrierCode = Clean(request.CarrierCode, 50),
            CarrierName = Clean(request.CarrierName, 200),
            VehiclePlate = Clean(request.VehiclePlate, 20),
            TrailerPlate = Clean(request.TrailerPlate, 20),
            DriverName = Clean(request.DriverName, 200),
            SealNo = Clean(request.SealNo, 50),
            Description = Clean(request.Description, 2000),
            ProjectCode = Clean(request.ProjectCode, 50),
            CostCenterCode = Clean(request.CostCenterCode, 100),
            MovementTypeCode = Clean(request.MovementTypeCode, 50),
            ExitLocationCode = Clean(request.ExitLocationCode, 100),
            ApprovalStatus = policy.RequireApproval ? OperationApprovalStatus.Pending : OperationApprovalStatus.NotRequired,
            RequireApproval = policy.RequireApproval,
            RequireAssignee = taskBased && policy.RequireAssigneeForTask,
            AllowPartialPicking = policy.AllowPartialPicking,
            AllowPartialWarehouseOutbound = policy.AllowPartialWarehouseOutbound,
            RequireSourceLocation = policy.RequireSourceLocation,
            RequireWarehouseOutboundInformation = policy.RequireWarehouseOutboundInformation,
            RequireLoadingConfirmation = policy.RequireLoadingConfirmation,
            AutoReleaseTaskBased = policy.AutoReleaseTaskBased,
            AutoPostErpAfterApproval = policy.AutoPostErpAfterApproval,
            MinimumFulfillmentPercent = policy.MinimumFulfillmentPercent,
            OverPickTolerancePercent = policy.OverPickTolerancePercent,
            ReservationPolicy = policy.ReservationPolicy,
            PackingPolicy = policy.PackingPolicy,
            ShortagePolicy = policy.ShortagePolicy,
            OverPickPolicy = policy.OverPickPolicy
        };

    private static Dictionary<string, WarehouseOutboundSourceDocument> CreateSourceDocuments(
        CreateWarehouseOutboundDraftRequest request,
        WarehouseOutboundHeader header,
        long actor,
        string branch,
        DateTime now,
        bool orderBased)
    {
        var documents = new Dictionary<string, WarehouseOutboundSourceDocument>(StringComparer.OrdinalIgnoreCase);
        if (!orderBased) return documents;
        foreach (var group in request.Lines.Select(x => x.Source!).GroupBy(x => x.OrderNumber, StringComparer.OrdinalIgnoreCase))
        {
            var first = group.First();
            var document = new WarehouseOutboundSourceDocument
            {
                BranchCode = branch,
                CreatedBy = actor,
                CreatedDate = now,
                Header = header,
                ExternalDocumentNo = first.OrderNumber.Trim(),
                ExternalDocumentId = first.OrderNumber.Trim(),
                ExternalDocumentDate = first.OrderDate
            };
            header.SourceDocuments.Add(document);
            documents[first.OrderNumber.Trim()] = document;
        }
        return documents;
    }

    private static WarehouseOutboundTask? CreatePickTask(
        CreateWarehouseOutboundDraftRequest request,
        WarehouseOutboundHeader header,
        IReadOnlyList<long> assigneeIds,
        long actor,
        string branch,
        DateTime now,
        string documentNumber,
        bool taskBased)
    {
        if (!taskBased) return null;
        var task = new WarehouseOutboundTask
        {
            BranchCode = branch,
            CreatedBy = actor,
            CreatedDate = now,
            Header = header,
            TaskNo = $"{documentNumber}-P01",
            TaskType = WarehouseOutboundTaskType.Pick,
            WarehouseId = request.SourceWarehouseId,
            Status = assigneeIds.Count > 0 ? WarehouseOutboundTaskStatus.Assigned : WarehouseOutboundTaskStatus.Open,
            Priority = request.Priority,
            PlannedAtUtc = request.PlannedWarehouseOutboundAtUtc?.ToUniversalTime()
        };
        foreach (var userId in assigneeIds)
        {
            task.Assignments.Add(new WarehouseOutboundTaskAssignment
            {
                BranchCode = branch,
                CreatedBy = actor,
                CreatedDate = now,
                Task = task,
                UserId = userId,
                IsPrimary = userId == assigneeIds[0],
                AssignedAtUtc = DateTimeOffset.UtcNow,
                AssignedBy = actor
            });
        }
        header.Tasks.Add(task);
        return task;
    }

    private static string SourceKey(string orderNumber, string externalLineId) =>
        $"{orderNumber.Trim()}|{externalLineId.Trim()}";

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? Clean(string? value, int max)
    {
        var normalized = Normalize(value);
        return normalized is null || normalized.Length <= max ? normalized : normalized[..max];
    }

    private static void EnsureRowVersion(byte[] current, string supplied)
    {
        byte[] expected;
        try { expected = Convert.FromBase64String(supplied ?? string.Empty); }
        catch (FormatException) { throw AppException.BadRequest("Geçersiz eşzamanlılık anahtarı."); }
        if (!System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(current, expected))
            throw AppException.Conflict("Sevk başka bir kullanıcı tarafından değiştirildi. Listeyi yenileyip tekrar deneyin.");
    }

    private static Task SoftDelete<TEntity>(IQueryable<TEntity> query, System.Linq.Expressions.Expression<Func<TEntity, bool>> predicate,
        long actor, DateTime now, CancellationToken ct) where TEntity : verii_wms_api_v2.Shared.Domain.BaseEntity =>
        query.Where(predicate).ExecuteUpdateAsync(x => x.SetProperty(v => v.IsDeleted, true)
            .SetProperty(v => v.DeletedBy, actor).SetProperty(v => v.DeletedDate, now), ct);
}
