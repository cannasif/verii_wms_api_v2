using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using verii_wms_api_v2.Modules.Audit.Application;
using verii_wms_api_v2.Modules.Customer.Domain;
using verii_wms_api_v2.Modules.DocumentSeries.Application;
using verii_wms_api_v2.Modules.DocumentSeries.Domain;
using verii_wms_api_v2.Modules.ErpIntegration.Application;
using verii_wms_api_v2.Modules.GoodsReceipt.Domain;
using verii_wms_api_v2.Modules.Identity.Domain;
using verii_wms_api_v2.Modules.Identity.Application;
using verii_wms_api_v2.Modules.Location.Domain;
using verii_wms_api_v2.Modules.NetsisRead.Application;
using verii_wms_api_v2.Modules.NetsisRead.Application.Dtos;
using verii_wms_api_v2.Modules.Quality.Application;
using verii_wms_api_v2.Modules.Quality.Domain;
using verii_wms_api_v2.Modules.Stock.Application;
using verii_wms_api_v2.Modules.SerialNumberPolicy.Application;
using verii_wms_api_v2.Modules.StockBalance.Domain;
using verii_wms_api_v2.Modules.StockTracking.Application;
using verii_wms_api_v2.Modules.StockMovement.Application;
using verii_wms_api_v2.Modules.StockMovement.Domain;
using verii_wms_api_v2.Shared.Application.Validation;
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
    IGoodsReceiptErpPostingCoordinator erpPosting,
    IGoodsReceiptOnReceiptLabelService onReceiptLabels,
    IGoodsReceiptOrderSource orderSource,
    INetsisImportOpenFileReader importOpenFileReader,
    IQualityWarehouseRoutingResolver? qualityWarehouseRoutingResolver = null) : IGoodsReceiptOperationsService
{
    private static readonly IReadOnlyDictionary<string, string> GridSearchColumnMapping =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["id"] = nameof(GoodsReceiptGridRow.Id),
            ["documentNo"] = nameof(GoodsReceiptGridRow.DocumentNo),
            ["waybillNo"] = nameof(GoodsReceiptGridRow.WaybillSearchText),
            ["supplierName"] = nameof(GoodsReceiptGridRow.SupplierSearchText),
            ["warehouseName"] = nameof(GoodsReceiptGridRow.WarehouseSearchText),
            ["createdBy"] = nameof(GoodsReceiptGridRow.CreatedBySearchText),
            ["updatedBy"] = nameof(GoodsReceiptGridRow.UpdatedBySearchText)
        };
    private static readonly string[] DefaultGridSearchColumns =
        ["documentNo", "waybillNo", "supplierName", "warehouseName"];

    private IGenericRepository<GoodsReceiptHeader> Headers => unitOfWork.Repository<GoodsReceiptHeader>();
    private IGenericRepository<GoodsReceiptExecution> Executions => unitOfWork.Repository<GoodsReceiptExecution>();

    public async Task<GoodsReceiptQualityRequirementResult> ResolveQualityRequirementsAsync(
        ResolveGoodsReceiptQualityRequest request,
        CancellationToken cancellationToken = default)
    {
        var branch = string.IsNullOrWhiteSpace(request.BranchCode) ? "0" : request.BranchCode.Trim();
        var stockIds = request.StockIds?.Where(x => x > 0).Distinct().ToArray() ?? [];
        if (stockIds.Length == 0 || stockIds.Length > 200)
            throw AppException.BadRequest("Kalite kontrolü için 1-200 arası geçerli stok seçilmelidir.");

        var stocks = await unitOfWork.Repository<StockEntity>().Query()
            .Where(x => x.BranchCode == branch && stockIds.Contains(x.Id))
            .Select(x => new { x.Id, x.GroupCode })
            .ToListAsync(cancellationToken);
        if (stocks.Count != stockIds.Length)
            throw AppException.BadRequest("Seçilen stoklardan biri bulunamadı veya farklı şubeye ait.");

        var requirements = new List<GoodsReceiptStockQualityRequirement>(stocks.Count);
        foreach (var stock in stocks)
        {
            var policy = await qualityPolicyResolver.ResolveAsync(
                branch, stock.Id, stock.GroupCode, cancellationToken);
            requirements.Add(new(
                stock.Id,
                RequiresQualityForLine(false, policy),
                policy.Source,
                policy.RuleId,
                policy.InspectionMode));
        }

        var requiresQuality = requirements.Any(x => x.RequiresQualityControl);
        return new(
            requiresQuality,
            ResolveNextAction(requiresQuality),
            requirements.OrderBy(x => x.StockId).ToArray());
    }

    public async Task<ManualGoodsReceiptResult> CreateOrderlessTaskAsync(CreateManualGoodsReceiptRequest request, long actorUserId, CancellationToken cancellationToken = default)
    {
        var importFile = await ResolveOpenImportFileForNewReceiptAsync(request, cancellationToken);
        return await CreateAsync(
            request,
            actorUserId,
            direct: false,
            qualityAlreadyApproved: false,
            importFile,
            cancellationToken);
    }

    public async Task<ManualGoodsReceiptResult> CreateDirectReceiptAsync(
        CreateManualGoodsReceiptRequest request,
        long actorUserId,
        CancellationToken cancellationToken = default)
    {
        var importFile = await ResolveOpenImportFileForNewReceiptAsync(request, cancellationToken);
        var result = await CreateAsync(
            request,
            actorUserId,
            direct: true,
            qualityAlreadyApproved: false,
            importFile,
            cancellationToken);
        await erpPosting.PostIfEligibleAsync(result.Id, actorUserId, cancellationToken);
        return result;
    }

    public Task<ManualGoodsReceiptResult> CreateImportDirectReceiptAsync(
        CreateManualGoodsReceiptRequest request,
        long actorUserId,
        CancellationToken cancellationToken = default) =>
        CreateDirectReceiptAsync(ApplyImportDefaults(request), actorUserId, cancellationToken);

    public async Task<ManualGoodsReceiptResult> CreateDirectReceiptDeferredErpAsync(
        CreateManualGoodsReceiptRequest request,
        long actorUserId,
        bool qualityAlreadyApproved,
        CancellationToken cancellationToken = default)
    {
        var importFile = await ResolveOpenImportFileForNewReceiptAsync(request, cancellationToken);
        return await CreateAsync(
            request,
            actorUserId,
            direct: true,
            qualityAlreadyApproved,
            importFile,
            cancellationToken);
    }

    public async Task<PagedResponse<GoodsReceiptGridRow>> GetPagedAsync(PagedRequest request, CancellationToken cancellationToken = default)
    {
        var includeLineSummary = RequiresLineSummaryInMainQuery(request);
        var headers = Headers.Query();
        var warehouses = unitOfWork.Repository<WarehouseEntity>().Query(ignoreQueryFilters: true);
        var lines = unitOfWork.Repository<GoodsReceiptLine>().Query();
        var users = unitOfWork.Repository<User>().Query();
        var userDetails = unitOfWork.Repository<UserDetail>().Query();
        var query = BuildPagedQuery(request, headers, warehouses, lines, users, userDetails);
        var countQuery = BuildCountQuery(request, headers, warehouses, lines, users, userDetails);
        var page = await query.ToPagedResponseAsync(countQuery, request, cancellationToken);
        return new PagedResponse<GoodsReceiptGridRow>
        {
            Items = await EnrichGridRowsAsync(page.Items, loadLineSummary: !includeLineSummary, cancellationToken),
            TotalCount = page.TotalCount,
            PageNumber = page.PageNumber,
            PageSize = page.PageSize,
        };
    }

    internal static IQueryable<GoodsReceiptGridRow> BuildPagedQuery(
        PagedRequest request,
        IQueryable<GoodsReceiptHeader> headers,
        IQueryable<WarehouseEntity> warehouses,
        IQueryable<GoodsReceiptLine> lines,
        IQueryable<User> users,
        IQueryable<UserDetail> userDetails)
    {
        // IgnoreQueryFilters on the warehouse root applies to the whole composed query.
        // Keep business-owned roots explicitly active while retaining historical warehouse names.
        headers = headers.Where(x => !x.IsDeleted);
        lines = lines.Where(x => !x.IsDeleted);

        var sources = BuildGridSourceQuery(request, headers, warehouses, lines);
        IQueryable<GoodsReceiptActorGridSource> actorSources;
        if (RequiresActorSearch(request))
        {
            actorSources = from source in sources
                      join createdUser in users on source.Base.CreatedBy equals (long?)createdUser.Id into createdUsers
                      from createdUser in createdUsers.DefaultIfEmpty()
                      join createdDetail in userDetails on source.Base.CreatedBy equals (long?)createdDetail.UserId into createdDetails
                      from createdDetail in createdDetails.DefaultIfEmpty()
                      join updatedUser in users on source.Base.UpdatedBy equals (long?)updatedUser.Id into updatedUsers
                      from updatedUser in updatedUsers.DefaultIfEmpty()
                      join updatedDetail in userDetails on source.Base.UpdatedBy equals (long?)updatedDetail.UserId into updatedDetails
                      from updatedDetail in updatedDetails.DefaultIfEmpty()
                      select new GoodsReceiptActorGridSource
                      {
                          Source = source,
                          CreatedBySearchText = (source.Base.CreatedBy == null
                                  ? "Sistem System"
                                  : source.Base.CreatedBy.GetValueOrDefault().ToString()) + " "
                              + (createdUser == null ? "" : createdUser.Username + " " + createdUser.Email) + " "
                              + (createdDetail == null ? "" : createdDetail.FirstName + " " + createdDetail.LastName),
                          UpdatedBySearchText = (source.Base.UpdatedBy == null
                                  ? "Sistem System"
                                  : source.Base.UpdatedBy.GetValueOrDefault().ToString()) + " "
                              + (updatedUser == null ? "" : updatedUser.Username + " " + updatedUser.Email) + " "
                              + (updatedDetail == null ? "" : updatedDetail.FirstName + " " + updatedDetail.LastName)
                      };
        }
        else
        {
            actorSources = sources.Select(source => new GoodsReceiptActorGridSource { Source = source });
        }

        return actorSources
            .Select(x => new GoodsReceiptGridRow(
                x.Source.Base.Id, x.Source.Base.BranchCode, x.Source.Base.DocumentNo, x.Source.Base.DocumentDate,
                x.Source.Base.ReceiptType, x.Source.Base.InitiationMode, x.Source.Base.ProcessType,
                x.Source.Base.Status, x.Source.Base.ApprovalStatus, x.Source.Base.QualityStatus,
                x.Source.Base.PutawayStatus, x.Source.Base.ErpIntegrationStatus,
                x.Source.Base.SupplierId, x.Source.Base.SupplierCode, x.Source.Base.SupplierName,
                x.Source.Base.TargetWarehouseId, x.Source.Base.WarehouseCode, x.Source.Base.WarehouseName,
                x.Source.Base.WaybillNo, x.Source.Base.ElectronicWaybillNo, x.Source.Base.WaybillDate,
                x.Source.LineCount, x.Source.ExpectedQuantity, x.Source.ReceivedQuantity,
                x.Source.Base.Priority, x.Source.Base.PlannedArrivalAtUtc, x.Source.Base.ReceivedAtUtc,
                x.Source.Base.CreatedBy, x.Source.Base.CreatedDate, x.Source.Base.UpdatedBy, x.Source.Base.UpdatedDate,
                null, null, x.Source.Base.RowVersion,
                (x.Source.Base.WaybillNo ?? "") + " " + (x.Source.Base.ElectronicWaybillNo ?? ""),
                (x.Source.Base.SupplierName ?? "") + " " + (x.Source.Base.SupplierCode ?? ""),
                x.Source.Base.WarehouseName + " " + x.Source.Base.WarehouseCode,
                x.CreatedBySearchText, x.UpdatedBySearchText))
            .ApplySearch(request, GridSearchColumnMapping, DefaultGridSearchColumns)
            .ApplyAdvancedFilters(request)
            .ApplySort(request, nameof(GoodsReceiptGridRow.CreatedDate));
    }

    internal static IQueryable<GoodsReceiptGridRow> BuildCountQuery(
        PagedRequest request,
        IQueryable<GoodsReceiptHeader> headers,
        IQueryable<WarehouseEntity> warehouses,
        IQueryable<GoodsReceiptLine> lines,
        IQueryable<User> users,
        IQueryable<UserDetail> userDetails) =>
        BuildPagedQuery(
            new PagedRequest
            {
                PageNumber = request.PageNumber,
                Page = request.Page,
                PageSize = request.PageSize,
                Search = request.EffectiveSearch,
                SearchFields = request.SearchFields,
                FilterLogic = request.FilterLogic,
                Filters = request.Filters
            },
            headers,
            warehouses,
            lines,
            users,
            userDetails);

    private static IQueryable<GoodsReceiptGridSource> BuildGridSourceQuery(
        PagedRequest request,
        IQueryable<GoodsReceiptHeader> headers,
        IQueryable<WarehouseEntity> warehouses,
        IQueryable<GoodsReceiptLine> lines)
    {
        var baseSources = from header in headers
                          join warehouse in warehouses on header.TargetWarehouseId equals warehouse.Id
                          select new GoodsReceiptGridBaseSource
                          {
                              Id = header.Id,
                              BranchCode = header.BranchCode,
                              DocumentNo = header.DocumentNo,
                              DocumentDate = header.DocumentDate,
                              ReceiptType = header.ReceiptType,
                              InitiationMode = header.InitiationMode,
                              ProcessType = header.ProcessType,
                              Status = header.Status,
                              ApprovalStatus = header.ApprovalStatus,
                              QualityStatus = header.QualityStatus,
                              PutawayStatus = header.PutawayStatus,
                              ErpIntegrationStatus = header.ErpIntegrationStatus,
                              SupplierId = header.SupplierId,
                              SupplierCode = header.SupplierCodeSnapshot,
                              SupplierName = header.SupplierNameSnapshot,
                              TargetWarehouseId = header.TargetWarehouseId,
                              WarehouseCode = warehouse.WarehouseCode,
                              WarehouseName = warehouse.WarehouseName,
                              WaybillNo = header.WaybillNo,
                              ElectronicWaybillNo = header.ElectronicWaybillNo,
                              WaybillDate = header.WaybillDate,
                              Priority = header.Priority,
                              PlannedArrivalAtUtc = header.PlannedArrivalAtUtc,
                              ReceivedAtUtc = header.ReceivedAtUtc,
                              CreatedBy = header.CreatedBy,
                              CreatedDate = header.CreatedDate,
                              UpdatedBy = header.UpdatedBy,
                              UpdatedDate = header.UpdatedDate,
                              RowVersion = header.RowVersion
                          };
        if (!RequiresLineSummaryInMainQuery(request))
            return baseSources.Select(source => new GoodsReceiptGridSource { Base = source });

        var lineSummaries = lines
            .GroupBy(x => x.GrHeaderId)
            .Select(group => new
            {
                HeaderId = group.Key,
                LineCount = group.Count(),
                ExpectedQuantity = group.Sum(x => x.ExpectedQuantity),
                ReceivedQuantity = group.Sum(x => x.ReceivedQuantity)
            });
        return from source in baseSources
               join summary in lineSummaries on source.Id equals summary.HeaderId into summaries
               from summary in summaries.DefaultIfEmpty()
               select new GoodsReceiptGridSource
               {
                   Base = source,
                   LineCount = (int?)summary.LineCount ?? 0,
                   ExpectedQuantity = (decimal?)summary.ExpectedQuantity ?? 0,
                   ReceivedQuantity = (decimal?)summary.ReceivedQuantity ?? 0
               };
    }

    private static bool RequiresActorSearch(PagedRequest request) =>
        !string.IsNullOrWhiteSpace(request.EffectiveSearch)
        && request.SearchFields.Any(x => IsColumn(x, "createdBy") || IsColumn(x, "updatedBy"));

    private static bool RequiresLineSummaryInMainQuery(PagedRequest request) =>
        IsLineSummaryColumn(request.SortBy)
        || request.Filters.Any(x => IsLineSummaryColumn(x.Column));

    private static bool IsLineSummaryColumn(string? column) =>
        IsColumn(column, "lineCount")
        || IsColumn(column, "expectedQuantity")
        || IsColumn(column, "receivedQuantity");

    private static bool IsColumn(string? value, string expected) =>
        string.Equals(value?.Trim(), expected, StringComparison.OrdinalIgnoreCase);

    private async Task<IReadOnlyList<GoodsReceiptGridRow>> EnrichGridRowsAsync(
        IReadOnlyList<GoodsReceiptGridRow> rows,
        bool loadLineSummary,
        CancellationToken cancellationToken)
    {
        if (rows.Count == 0) return rows;

        var headerIds = rows.Select(x => x.Id).ToArray();
        Dictionary<long, GoodsReceiptLineSummary> lineSummariesByHeader = [];
        if (loadLineSummary)
        {
            lineSummariesByHeader = await unitOfWork.Repository<GoodsReceiptLine>().Query()
                .Where(x => headerIds.Contains(x.GrHeaderId))
                .GroupBy(x => x.GrHeaderId)
                .Select(group => new GoodsReceiptLineSummary
                {
                    HeaderId = group.Key,
                    LineCount = group.Count(),
                    ExpectedQuantity = group.Sum(x => x.ExpectedQuantity),
                    ReceivedQuantity = group.Sum(x => x.ReceivedQuantity)
                })
                .ToDictionaryAsync(x => x.HeaderId, cancellationToken);
        }
        var orderRows = await unitOfWork.Repository<GoodsReceiptSourceDocument>().Query()
            .Where(x => headerIds.Contains(x.GrHeaderId)
                && x.SourceDocumentType == GoodsReceiptSourceDocumentType.PurchaseOrder)
            .Select(x => new { x.GrHeaderId, x.ExternalDocumentNo })
            .ToListAsync(cancellationToken);
        var projectRows = await (
            from line in unitOfWork.Repository<GoodsReceiptLine>().Query()
            join source in unitOfWork.Repository<GoodsReceiptLineSource>().Query() on line.Id equals source.GrLineId
            where headerIds.Contains(line.GrHeaderId)
                && source.ProjectCodeSnapshot != null
                && source.ProjectCodeSnapshot != ""
            select new { line.GrHeaderId, source.ProjectCodeSnapshot })
            .ToListAsync(cancellationToken);

        var ordersByHeader = orderRows
            .GroupBy(x => x.GrHeaderId)
            .ToDictionary(x => x.Key, x => JoinDistinctValues(x.Select(row => row.ExternalDocumentNo)));
        var projectsByHeader = projectRows
            .GroupBy(x => x.GrHeaderId)
            .ToDictionary(x => x.Key, x => JoinDistinctValues(x.Select(row => row.ProjectCodeSnapshot)));

        return rows
            .Select(row =>
            {
                lineSummariesByHeader.TryGetValue(row.Id, out var lineSummary);
                return row with
                {
                    LineCount = loadLineSummary ? lineSummary?.LineCount ?? 0 : row.LineCount,
                    ExpectedQuantity = loadLineSummary ? lineSummary?.ExpectedQuantity ?? 0 : row.ExpectedQuantity,
                    ReceivedQuantity = loadLineSummary ? lineSummary?.ReceivedQuantity ?? 0 : row.ReceivedQuantity,
                    OrderNumbers = ordersByHeader.GetValueOrDefault(row.Id),
                    ProjectCodes = projectsByHeader.GetValueOrDefault(row.Id),
                };
            })
            .ToArray();
    }

    private sealed class GoodsReceiptGridBaseSource
    {
        public long Id { get; init; }
        public required string BranchCode { get; init; }
        public required string DocumentNo { get; init; }
        public DateOnly DocumentDate { get; init; }
        public GoodsReceiptType ReceiptType { get; init; }
        public GoodsReceiptInitiationMode InitiationMode { get; init; }
        public GoodsReceiptProcessType ProcessType { get; init; }
        public WarehouseOperationStatus Status { get; init; }
        public OperationApprovalStatus ApprovalStatus { get; init; }
        public OperationQualityStatus QualityStatus { get; init; }
        public OperationPutawayStatus PutawayStatus { get; init; }
        public ErpIntegrationStatus ErpIntegrationStatus { get; init; }
        public long? SupplierId { get; init; }
        public string? SupplierCode { get; init; }
        public string? SupplierName { get; init; }
        public long TargetWarehouseId { get; init; }
        public int WarehouseCode { get; init; }
        public required string WarehouseName { get; init; }
        public string? WaybillNo { get; init; }
        public string? ElectronicWaybillNo { get; init; }
        public DateOnly? WaybillDate { get; init; }
        public byte Priority { get; init; }
        public DateTimeOffset? PlannedArrivalAtUtc { get; init; }
        public DateTimeOffset? ReceivedAtUtc { get; init; }
        public long? CreatedBy { get; init; }
        public DateTime? CreatedDate { get; init; }
        public long? UpdatedBy { get; init; }
        public DateTime? UpdatedDate { get; init; }
        public required byte[] RowVersion { get; init; }
    }

    private sealed class GoodsReceiptGridSource
    {
        public required GoodsReceiptGridBaseSource Base { get; init; }
        public int LineCount { get; init; }
        public decimal ExpectedQuantity { get; init; }
        public decimal ReceivedQuantity { get; init; }
    }

    private sealed class GoodsReceiptActorGridSource
    {
        public required GoodsReceiptGridSource Source { get; init; }
        public string? CreatedBySearchText { get; init; }
        public string? UpdatedBySearchText { get; init; }
    }

    private sealed class GoodsReceiptLineSummary
    {
        public long HeaderId { get; init; }
        public int LineCount { get; init; }
        public decimal ExpectedQuantity { get; init; }
        public decimal ReceivedQuantity { get; init; }
    }

    private static string? JoinDistinctValues(IEnumerable<string?> values)
    {
        var normalized = values
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return normalized.Length == 0 ? null : string.Join(", ", normalized);
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
        var sourceDocumentRows = await unitOfWork.Repository<GoodsReceiptSourceDocument>().Query().Where(x => x.GrHeaderId == id)
            .OrderBy(x => x.Id).Select(x => new { x.SourceDocumentType, x.ExternalDocumentNo }).ToListAsync(cancellationToken);
        var documents = sourceDocumentRows.Select(x => $"{x.SourceDocumentType}:{x.ExternalDocumentNo}").ToList();
        var orderNumbersSummary = JoinDistinctValues(sourceDocumentRows
            .Where(x => x.SourceDocumentType == GoodsReceiptSourceDocumentType.PurchaseOrder)
            .Select(x => x.ExternalDocumentNo));
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
        var lineIds = lineEntities.Select(x => x.Id).ToArray();
        var projectCodes = lineIds.Length == 0
            ? []
            : await unitOfWork.Repository<GoodsReceiptLineSource>().Query()
                .Where(x => lineIds.Contains(x.GrLineId)
                    && x.ProjectCodeSnapshot != null
                    && x.ProjectCodeSnapshot != "")
                .Select(x => x.ProjectCodeSnapshot!)
                .Distinct()
                .OrderBy(x => x)
                .ToListAsync(cancellationToken);
        var projectCodesSummary = JoinDistinctValues(projectCodes);
        var grid = new GoodsReceiptGridRow(header.Id, header.BranchCode, header.DocumentNo, header.DocumentDate,
            header.ReceiptType, header.InitiationMode, header.ProcessType, header.Status, header.ApprovalStatus,
            header.QualityStatus, header.PutawayStatus, header.ErpIntegrationStatus,
            header.SupplierId, header.SupplierCodeSnapshot, header.SupplierNameSnapshot, header.TargetWarehouseId,
            warehouse.WarehouseCode, warehouse.WarehouseName, header.WaybillNo, header.ElectronicWaybillNo, header.WaybillDate, detailLines.Count,
            detailLines.Sum(x => x.ExpectedQuantity), detailLines.Sum(x => x.ReceivedQuantity), header.Priority,
            header.PlannedArrivalAtUtc, header.ReceivedAtUtc, header.CreatedBy, header.CreatedDate,
            header.UpdatedBy, header.UpdatedDate, orderNumbersSummary, projectCodesSummary, header.RowVersion);
        return new GoodsReceiptDetail(grid, detailLines, putawayCandidates, documents, taskNumbers, executionCount, projectCodes);
    }

    private Task<ManualGoodsReceiptResult> CreateAsync(
        CreateManualGoodsReceiptRequest request,
        long actor,
        bool direct,
        bool qualityAlreadyApproved,
        NetsisImportOpenFileDto? importFile,
        CancellationToken ct)
    {
        // Orderless/direct receipts are operational captures, not planned work.
        // Keep them at the lowest queue priority regardless of client payload.
        request = ApplyUnplannedDefaults(request);
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
            ValidateImportFileCustomer(request.TradeType, importFile, supplier.CustomerCode);
            var waybillNo = NormalizeDocumentNumber(request.WaybillNo);
            var electronicWaybillNo = NormalizeDocumentNumber(request.ElectronicWaybillNo);
            var duplicateDocument = await Headers.Query().AnyAsync(x => x.BranchCode == branch && x.SupplierId == supplier.Id
                && ((waybillNo != null && x.WaybillNo == waybillNo)
                    || (electronicWaybillNo != null && x.ElectronicWaybillNo == electronicWaybillNo)), token);
            if (duplicateDocument)
                throw AppException.Conflict("Bu tedarikçi için aynı mal kabul numarası daha önce kullanılmış.");
            var warehouse = await unitOfWork.Repository<WarehouseEntity>().FirstOrDefaultAsync(x => x.Id == request.TargetWarehouseId && x.BranchCode == branch, false, token)
                ?? throw AppException.BadRequest("Hedef depo bulunamadı.");
            var hasOrderSources = request.Lines.Any(x => !string.IsNullOrWhiteSpace(x.SourceOrderNumber) || x.SourceOrderId.HasValue);
            if (hasOrderSources && !direct)
                throw AppException.BadRequest("Sipariş kaynaklı satırlar yalnızca doğrudan mal kabul işleminde kullanılabilir.");
            if (hasOrderSources && request.Lines.Any(x => string.IsNullOrWhiteSpace(x.SourceOrderNumber) || !x.SourceOrderId.HasValue))
                throw AppException.BadRequest("Siparişten doğrudan kabulde tüm kalemlerin sipariş numarası ve satır kimliği bulunmalıdır.");
            var policy = await receiptPolicyService.GetAsync(branch, token);
            ValidateManualQualityPolicy(
                policy.ErpQualityGatePolicy,
                request.ForceQualityControl || request.Lines.Any(x => x.ForceQualityControl));
            var orderSourceByKey = new Dictionary<(string OrderNumber, int OrderId), GoodsReceiptOrderSourceLine>();
            if (hasOrderSources)
            {
                var orderNumbers = request.Lines.Select(x => x.SourceOrderNumber!.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                var sourceRows = await orderSource.GetOpenLinesAsync(
                    string.Join(',', orderNumbers), supplier.CustomerCode, branch, token);
                orderSourceByKey = sourceRows.ToDictionary(
                    x => (x.OrderNumber.ToUpperInvariant(), x.OrderId), x => x);
                foreach (var group in request.Lines.GroupBy(x =>
                             (OrderNumber: x.SourceOrderNumber!.Trim().ToUpperInvariant(), OrderId: x.SourceOrderId!.Value)))
                {
                    if (!orderSourceByKey.TryGetValue(group.Key, out var source))
                        throw AppException.Conflict("Seçilen sipariş satırlarından biri artık açık değildir. Siparişleri yenileyip tekrar deneyiniz.");
                    if (!string.Equals(source.CustomerCode, supplier.CustomerCode, StringComparison.OrdinalIgnoreCase)
                        || source.BranchCode?.ToString() != branch)
                        throw AppException.BadRequest("Sipariş satırı seçilen tedarikçi veya şube ile uyuşmuyor.");
                    if (source.TargetWarehouseCode.HasValue && source.TargetWarehouseCode.Value != warehouse.WarehouseCode)
                        throw AppException.Forbidden($"Sipariş satırı depo {source.TargetWarehouseCode} içindir; depo {warehouse.WarehouseCode} üzerinden kabul edilemez.");
                    var requestedQuantity = group.Sum(x => x.Quantity);
                    if (requestedQuantity <= 0 || requestedQuantity > source.AvailableQuantity)
                        throw AppException.Conflict($"Sipariş {source.OrderNumber}/{source.OrderId} için kabul miktarı açık miktarı aşıyor.");
                }
            }
            var location = await unitOfWork.Repository<WarehouseLocation>().FindByIdAsync(request.ReceivingLocationId, false, token)
                ?? throw AppException.BadRequest("Mal kabul alanı bulunamadı.");
            if (!location.IsActive || location.WarehouseId != warehouse.Id)
                throw AppException.BadRequest(LocationPolicyError(
                    GoodsReceiptLocationSelectionPolicy.AnyActiveWarehouseLocation));

            var requestedLineWarehouseIds = request.Lines
                .Select(x => x.TargetWarehouseId ?? request.TargetWarehouseId)
                .Distinct()
                .ToArray();
            if (requestedLineWarehouseIds.Any(x => x != warehouse.Id))
                throw AppException.BadRequest("Siparişsiz ve direkt kabulde kalem hedef deposu header deposuyla aynı olmalıdır.");
            await UserWarehouseAccessService.EnsureAsync(
                unitOfWork, actor, branch, requestedLineWarehouseIds.Append(warehouse.Id), token);
            var requestedLineLocationIds = request.Lines
                .Select(x => x.ReceivingLocationId ?? request.ReceivingLocationId)
                .Distinct()
                .ToArray();
            var lineLocations = await unitOfWork.Repository<WarehouseLocation>().Query()
                .Where(x => requestedLineLocationIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, token);
            if (lineLocations.Count != requestedLineLocationIds.Length
                || lineLocations.Values.Any(x => !x.IsActive || x.WarehouseId != warehouse.Id))
                throw AppException.BadRequest(LocationPolicyError(
                    GoodsReceiptLocationSelectionPolicy.AnyActiveWarehouseLocation));

            var stockIds = request.Lines.Select(x => x.StockId).Distinct().ToList();
            var stocks = await unitOfWork.Repository<StockEntity>().Query().Where(x => x.BranchCode == branch && stockIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, token);
            if (stocks.Count != stockIds.Count) throw AppException.BadRequest("Geçersiz veya farklı şubeye ait stok seçildi.");
            if (hasOrderSources && request.Lines.Any(input =>
                    !orderSourceByKey.TryGetValue((input.SourceOrderNumber!.Trim().ToUpperInvariant(), input.SourceOrderId!.Value), out var source)
                    || !string.Equals(source.StockCode, stocks[input.StockId].ErpStockCode, StringComparison.OrdinalIgnoreCase)))
                throw AppException.BadRequest("Sipariş satırındaki stok ile kabul kalemindeki stok uyuşmuyor.");
            var yapIds = request.Lines.Where(x => x.YapCodeId.HasValue).Select(x => x.YapCodeId!.Value).Distinct().ToList();
            var yaps = await unitOfWork.Repository<Modules.YapCode.Domain.YapCode>().Query().Where(x => x.BranchCode == branch && yapIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, token);
            if (yaps.Count != yapIds.Count) throw AppException.BadRequest("Geçersiz veya farklı şubeye ait YAP kodu seçildi.");
            if (hasOrderSources && request.Lines.Any(input =>
                    orderSourceByKey.TryGetValue((input.SourceOrderNumber!.Trim().ToUpperInvariant(), input.SourceOrderId!.Value), out var source)
                    && ((!string.IsNullOrWhiteSpace(source.YapCode)
                         && (!input.YapCodeId.HasValue
                             || !string.Equals(source.YapCode, yaps[input.YapCodeId.Value].ConfigurationCode, StringComparison.OrdinalIgnoreCase)))
                        || (string.IsNullOrWhiteSpace(source.YapCode) && input.YapCodeId.HasValue))))
                throw AppException.BadRequest("Sipariş satırındaki YAP kodu ile kabul kalemindeki YAP kodu uyuşmuyor.");

            if (!direct && !policy.AllowOrderlessReceipt) throw AppException.Forbidden("Siparişsiz mal kabul emri politika gereği kapalıdır.");
            if (RequiresUnplannedReceiptPermission(direct, hasOrderSources) && !policy.AllowUnplannedReceipt)
                throw AppException.Forbidden("Emirsiz direkt mal kabul politika gereği kapalıdır.");
            var resolved = new Dictionary<long, ResolvedQualityPolicy>();
            foreach (var stock in stocks.Values) resolved[stock.Id] = await qualityPolicyResolver.ResolveAsync(branch, stock.Id, stock.GroupCode, token);
            var trackingPolicies = new Dictionary<long, EffectiveStockTrackingPolicy>();
            foreach (var stock in stocks.Values) trackingPolicies[stock.Id] = await trackingPolicyResolver.ResolveAsync(branch, stock.Id, token);
            var requiresQuality = RequiresQuality(
                qualityAlreadyApproved,
                resolved.Values.Any(x => x.InspectionMode != QualityInspectionMode.NoCheck),
                request.ForceQualityControl || request.Lines.Any(x => x.ForceQualityControl));
            foreach (var input in request.Lines)
            {
                var forceQuality = request.ForceQualityControl || input.ForceQualityControl;
                var lineRequiresQuality = RequiresQualityForLine(
                    qualityAlreadyApproved, resolved[input.StockId], forceQuality);
                var holdsInventory = ShouldHoldInventoryForQuality(
                    lineRequiresQuality,
                    policy.HoldInventoryUntilQualityDecision,
                    ResolveQualityRoutingSource(resolved[input.StockId], forceQuality, qualityAlreadyApproved));
                var lineLocationId = input.ReceivingLocationId ?? request.ReceivingLocationId;
                var locationPolicy = GoodsReceiptLocationPolicy.ResolveSelectionPolicy(
                    policy.BlockPutawayUntilQualityDecision);
                if (!GoodsReceiptLocationPolicy.IsAllowedForReceiptLine(
                        locationPolicy,
                        lineLocations[lineLocationId],
                        warehouse.Id,
                        lineRequiresQuality,
                        policy.BlockPutawayUntilQualityDecision,
                        holdsInventory))
                    throw AppException.BadRequest(
                        $"{stocks[input.StockId].ErpStockCode}: {LocationPolicyError(
                            holdsInventory
                                ? GoodsReceiptLocationSelectionPolicy.AnyActiveWarehouseLocation
                                : locationPolicy)}");
            }
            ValidateQualityReceivingLocations(
                requiresQuality,
                policy.BlockPutawayUntilQualityDecision,
                request.Lines
                    .Where(input =>
                    {
                        var forceQuality = request.ForceQualityControl || input.ForceQualityControl;
                        var lineRequiresQuality = RequiresQualityForLine(
                            qualityAlreadyApproved, resolved[input.StockId], forceQuality);
                        return lineRequiresQuality
                            && !ShouldHoldInventoryForQuality(
                                lineRequiresQuality,
                                policy.HoldInventoryUntilQualityDecision,
                                ResolveQualityRoutingSource(resolved[input.StockId], forceQuality, qualityAlreadyApproved));
                    })
                    .Select(input => lineLocations[
                        input.ReceivingLocationId ?? request.ReceivingLocationId]));

            ValidateTrackedLines(
                request, stocks, resolved, trackingPolicies,
                requireCompleteCapture: direct,
                includeQualityRequirements: !qualityAlreadyApproved);
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
                DocumentDate = request.DocumentDate, ReceiptType = hasOrderSources ? GoodsReceiptType.PurchaseOrder : GoodsReceiptType.Direct,
                InitiationMode = direct ? GoodsReceiptInitiationMode.DirectReceipt : GoodsReceiptInitiationMode.UnplannedTask,
                ProcessType = direct
                    ? hasOrderSources ? GoodsReceiptProcessType.OrderBasedDirectReceipt : GoodsReceiptProcessType.OrderlessDirectReceipt
                    : GoodsReceiptProcessType.OrderlessTask,
                LabelStrategy = request.LabelStrategy,
                SourceSystem = hasOrderSources ? WarehouseOperationSourceSystem.Netsis : WarehouseOperationSourceSystem.Manual,
                CorrelationId = request.IdempotencyKey, SupplierId = supplier.Id, SupplierCodeSnapshot = supplier.CustomerCode,
                SupplierNameSnapshot = supplier.CustomerName, TargetWarehouseId = warehouse.Id, ReceivingLocationId = location.Id,
                Status = direct ? WarehouseOperationStatus.Processed : WarehouseOperationStatus.Draft,
                ApprovalStatus = policy.RequireReceiptApproval ? OperationApprovalStatus.Pending : OperationApprovalStatus.NotRequired,
                QualityStatus = qualityAlreadyApproved
                    ? OperationQualityStatus.Passed
                    : requiresQuality ? OperationQualityStatus.Pending : OperationQualityStatus.NotRequired,
                PutawayStatus = OperationPutawayStatus.Pending, ErpIntegrationStatus = ErpIntegrationStatus.Pending,
                PlannedArrivalAtUtc = request.PlannedArrivalAtUtc?.ToUniversalTime(), ActualArrivalAtUtc = direct ? now : null,
                ReceivedAtUtc = direct ? now : null, ReceivedBy = direct ? actor : null,
                WaybillNo = waybillNo, WaybillDate = request.WaybillDate,
                ElectronicWaybillNo = electronicWaybillNo, ShipmentReferenceNo = Clean(request.ShipmentReferenceNo, 100),
                TradeType = request.TradeType, ImportFileNumber = Clean(request.ImportFileNumber, 20),
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
                ErpQualityGatePolicy = policy.ErpQualityGatePolicy,
                RequireQualityControl = requiresQuality, RequirePutaway = true, Priority = request.Priority,
                Description = Clean(request.Description, 1000)
            }, actor);
            await Headers.AddAsync(header, token);
            await unitOfWork.SaveChangesAsync(token);

            var orderDocuments = new Dictionary<string, GoodsReceiptSourceDocument>(StringComparer.OrdinalIgnoreCase);
            if (hasOrderSources)
            {
                foreach (var group in orderSourceByKey.Values
                             .Where(x => request.Lines.Any(input => string.Equals(input.SourceOrderNumber?.Trim(), x.OrderNumber, StringComparison.OrdinalIgnoreCase)))
                             .GroupBy(x => x.OrderNumber, StringComparer.OrdinalIgnoreCase))
                {
                    var source = group.First();
                    var document = Stamp(new GoodsReceiptSourceDocument
                    {
                        BranchCode = branch,
                        Header = header,
                        SourceDocumentType = GoodsReceiptSourceDocumentType.PurchaseOrder,
                        SourceSystem = WarehouseOperationSourceSystem.Netsis,
                        ExternalDocumentId = source.OrderNumber,
                        ExternalDocumentNo = source.OrderNumber,
                        ExternalDocumentDate = source.OrderDate.HasValue ? DateOnly.FromDateTime(source.OrderDate.Value) : null,
                        SupplierCodeSnapshot = supplier.CustomerCode,
                        SupplierNameSnapshot = supplier.CustomerName,
                        ExternalStatus = "Open"
                    }, actor);
                    orderDocuments[source.OrderNumber] = document;
                    header.SourceDocuments.Add(document);
                }
            }
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

            var qualityWarehouseRoute = qualityWarehouseRoutingResolver is null
                ? null
                : await qualityWarehouseRoutingResolver.ResolveWarehouseRouteAsync(branch, warehouse.Id, token);
            header.QualityLocationId ??= qualityWarehouseRoute?.QualityLocationId;
            var grLines = new List<GoodsReceiptLine>();
            for (var index = 0; index < request.Lines.Count; index++)
            {
                var input = request.Lines[index]; var stock = stocks[input.StockId];
                var lineLocationId = input.ReceivingLocationId ?? request.ReceivingLocationId;
                yaps.TryGetValue(input.YapCodeId ?? 0, out var yap); var qp = resolved[stock.Id];
                var trackingPolicy = trackingPolicies[stock.Id];
                var unit = StockUnitPolicy.Resolve(stock, input.UnitCode);
                var qualityRequired = RequiresQualityForLine(
                    qualityAlreadyApproved, qp, request.ForceQualityControl || input.ForceQualityControl);
                var line = Stamp(new GoodsReceiptLine
                {
                    BranchCode = branch, Header = header, LineNo = index + 1, StockId = stock.Id,
                    StockCodeSnapshot = stock.ErpStockCode, StockNameSnapshot = stock.StockName,
                    YapCodeId = yap?.Id, YapCodeSnapshot = yap?.ConfigurationCode, UnitCode = unit, BaseUnitCode = unit,
                    ExpectedQuantity = input.Quantity, ReceivedQuantity = direct ? input.Quantity : 0,
                    AcceptedQuantity = direct && !qualityRequired ? input.Quantity : 0,
                    QuarantineQuantity = direct && qualityRequired ? input.Quantity : 0,
                    PutawayQuantity = direct && !qualityRequired && lineLocations[lineLocationId].IsPutaway
                        ? input.Quantity
                        : 0,
                    TrackingType = trackingPolicy.TrackingType,
                    RequireLot = trackingPolicy.RequireLot, RequireSerial = trackingPolicy.RequireSerial,
                    RequireExpirationDate = trackingPolicy.RequireExpirationDate,
                    MinimumShelfLifeDays = qp.MinimumRemainingShelfLifeDays, RequireQualityControl = qualityRequired,
                    QualityRoutingSource = ResolveQualityRoutingSource(
                        qp, request.ForceQualityControl || input.ForceQualityControl, qualityAlreadyApproved),
                    Status = direct ? GoodsReceiptLineStatus.Received : GoodsReceiptLineStatus.Open,
                    AllowOverReceipt = policy.OverReceiptPolicy != OverReceiptPolicy.NotAllowed,
                    OverReceiptTolerancePercent = policy.OverReceiptTolerancePercent, AllowUnderReceipt = policy.AllowUnderReceipt,
                    TargetWarehouseId = warehouse.Id, DefaultReceivingLocationId = lineLocationId,
                    DefaultPutawayLocationId = lineLocations[lineLocationId].IsPutaway ? lineLocationId : null,
                    Description = Clean(input.Description, 500)
                }, actor);
                if (hasOrderSources)
                {
                    var source = orderSourceByKey[(input.SourceOrderNumber!.Trim().ToUpperInvariant(), input.SourceOrderId!.Value)];
                    line.Sources.Add(Stamp(new GoodsReceiptLineSource
                    {
                        BranchCode = branch,
                        Line = line,
                        SourceDocument = orderDocuments[source.OrderNumber],
                        ExternalLineId = source.OrderId.ToString(),
                        ExternalStockCode = source.StockCode ?? stock.ErpStockCode,
                        ExternalYapCode = source.YapCode,
                        OrderedQuantity = source.OrderedQuantity,
                        PreviouslyReceivedQuantity = source.DeliveredQuantity,
                        AllocatedQuantity = input.Quantity,
                        ReceivedQuantity = direct ? input.Quantity : 0,
                        UnitCode = unit,
                        ExternalStatus = "Open",
                        ProjectCodeSnapshot = Clean(source.ProjectCode, 50)
                    }, actor));
                }
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

            if (direct)
                GoodsReceiptExecutionService.RefreshHeaderStatus(header, actor);
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
                return new(header.Id, header.DocumentNo, header.InitiationMode, header.Status, task!.Id, task.TaskNo,
                    null, null, null, grLines.Count, grLines.Sum(x => x.ExpectedQuantity), false, []);
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
            var qualityHold = ShouldHoldInventoryForQuality(line, header);
            execution.Lines.Add(Stamp(new GoodsReceiptExecutionLine { BranchCode = header.BranchCode, Execution = execution,
                Line = line, LineNo = index + 1, StockId = line.StockId, YapCodeId = line.YapCodeId,
                Quantity = input.Quantity, UnitCode = line.UnitCode, LotNo = Clean(input.LotNo, 100), SerialNo = Clean(input.SerialNo, 100),
                ManufacturingDate = input.ManufacturingDate, ExpirationDate = input.ExpirationDate,
                ScannedBarcode = Clean(input.ScannedBarcode, 250), WarehouseId = line.TargetWarehouseId,
                LocationId = ResolveQualityInventoryLocationId(
                    line, header, line.DefaultReceivingLocationId ?? header.ReceivingLocationId),
                StockStatus = qualityHold ? "QualityHold" : "Available",
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
        var generatedLabelIds = await onReceiptLabels.GenerateForExecutionAsync(
            header, execution, execution.Lines.ToArray(), actor, ct);
        await unitOfWork.SaveChangesAsync(ct);
        await audit.WriteAsync(new("goods-receipt.direct", nameof(GoodsReceiptHeader), header.Id.ToString(), "Succeeded", "goods-receipt",
            NewValues: new { header.DocumentNo, execution.ExecutionNo, movement.OperationId, QualityInspectionId = inspection?.Id,
                LineCount = lines.Count, Quantity = lines.Sum(x => x.ReceivedQuantity) },
            ChangedFields: ["Header", "Lines", "Execution", "StockMovement", "Quality"]), ct);
        return new(header.Id, header.DocumentNo, header.InitiationMode, header.Status, null, null, execution.Id,
            movement.OperationId, inspection?.Id, lines.Count, lines.Sum(x => x.ReceivedQuantity), false,
            generatedLabelIds);
    }

    private async Task<ManualGoodsReceiptResult> ExistingResult(GoodsReceiptHeader header, GoodsReceiptExecution? execution, CancellationToken ct)
    {
        var task = await unitOfWork.Repository<GoodsReceiptTask>().Query().FirstOrDefaultAsync(x => x.GrHeaderId == header.Id, ct);
        var lines = unitOfWork.Repository<GoodsReceiptLine>().Query().Where(x => x.GrHeaderId == header.Id);
        var inspection = await unitOfWork.Repository<QualityInspection>().Query().FirstOrDefaultAsync(x => x.SourceDocumentType == "GoodsReceipt" && x.SourceDocumentId == header.Id, ct);
        return new(header.Id, header.DocumentNo, header.InitiationMode, header.Status, task?.Id, task?.TaskNo,
            execution?.Id, execution?.StockMovementOperationId, inspection?.Id, await lines.CountAsync(ct),
            await lines.SumAsync(x => header.InitiationMode == GoodsReceiptInitiationMode.DirectReceipt ? x.ReceivedQuantity : x.ExpectedQuantity, ct), true,
            execution is null
                ? []
                : await unitOfWork.Repository<GoodsReceiptLabelBatch>().Query()
                    .Where(x => x.CorrelationId == execution.IdempotencyKey)
                    .SelectMany(x => x.Labels)
                    .Select(x => x.Id)
                    .ToArrayAsync(ct));
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
        if (direct) ValidateDirectLabelMode(request.LabelStrategy, request.ExecutionMode);
        var waybillNo = NormalizeDocumentNumber(request.WaybillNo);
        var electronicWaybillNo = NormalizeDocumentNumber(request.ElectronicWaybillNo);
        ValidateDocumentReference(waybillNo, electronicWaybillNo, request.WaybillDate, request.ExecutionMode);
        if (waybillNo is not null && !PurchaseWaybillNumberPolicy.IsValid(waybillNo))
            throw AppException.BadRequest("Normal irsaliye numarası semboller dahil tam 15 karakter olmalıdır.");
        if (electronicWaybillNo is not null && !PurchaseWaybillNumberPolicy.IsValid(electronicWaybillNo))
            throw AppException.BadRequest("E-irsaliye / GİB numarası semboller dahil tam 15 karakter olmalıdır.");
        ValidateTradeClassification(request.TradeType, request.ImportFileNumber);
    }

    internal static string? ValidateTradeClassification(
        GoodsReceiptTradeType tradeType,
        string? importFileNumber)
    {
        if (!Enum.IsDefined(tradeType))
            throw AppException.BadRequest("Geçersiz ERP ticaret tipi.");

        var normalizedFileNumber = Clean(importFileNumber, 20);
        if (tradeType == GoodsReceiptTradeType.Foreign && normalizedFileNumber is null)
            throw AppException.BadRequest("Yurt dışı mal kabulünde açık ithalat dosyası seçimi zorunludur.");
        if (tradeType == GoodsReceiptTradeType.Domestic && normalizedFileNumber is not null)
            throw AppException.BadRequest("Yurt içi mal kabulünde ithalat dosyası gönderilemez.");

        return normalizedFileNumber;
    }

    internal static CreateManualGoodsReceiptRequest ApplyUnplannedDefaults(
        CreateManualGoodsReceiptRequest request) =>
        request with { Priority = 1 };

    internal static CreateManualGoodsReceiptRequest ApplyImportDefaults(
        CreateManualGoodsReceiptRequest request) =>
        request with
        {
            TradeType = GoodsReceiptTradeType.Foreign,
            ExecutionMode = request.LabelStrategy == GoodsReceiptLabelStrategy.SupplierLabel
                ? GoodsReceiptExecutionMode.SupplierLabel
                : GoodsReceiptExecutionMode.Import
        };

    internal static void ValidateImportFileCustomer(
        GoodsReceiptTradeType tradeType,
        NetsisImportOpenFileDto? importFile,
        string supplierCode)
    {
        if (tradeType != GoodsReceiptTradeType.Foreign) return;
        if (importFile is null)
            throw AppException.Conflict(
                "Seçilen ithalat dosyası artık açık değildir. Listeyi yenileyip tekrar seçiniz.");
        if (!string.Equals(
                importFile.CustomerCode.Trim(),
                supplierCode.Trim(),
                StringComparison.OrdinalIgnoreCase))
            throw AppException.BadRequest(
                "Seçilen cari, ithalat dosyasındaki cari koduyla uyuşmuyor.");
    }

    private async Task<NetsisImportOpenFileDto?> ResolveOpenImportFileForNewReceiptAsync(
        CreateManualGoodsReceiptRequest request,
        CancellationToken cancellationToken)
    {
        if (request.TradeType != GoodsReceiptTradeType.Foreign) return null;

        var importFileNumber = ValidateTradeClassification(
            request.TradeType,
            request.ImportFileNumber);

        // Idempotent replays must remain replayable after the ERP file is closed.
        if (await Headers.Query().AnyAsync(
                header => header.CorrelationId == request.IdempotencyKey,
                cancellationToken))
            return null;

        var openFiles = await importOpenFileReader.GetImportOpenFilesAsync(cancellationToken);
        return NetsisImportOpenFilePolicy.FindOpenFile(importFileNumber, openFiles)
            ?? throw AppException.Conflict(
                "Seçilen ithalat dosyası artık açık değildir. Listeyi yenileyip tekrar seçiniz.");
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

    internal static void ValidateDirectLabelMode(
        GoodsReceiptLabelStrategy labelStrategy,
        GoodsReceiptExecutionMode executionMode)
    {
        if (labelStrategy == GoodsReceiptLabelStrategy.PreGenerate
            || executionMode == GoodsReceiptExecutionMode.PreGeneratedLabel)
            throw AppException.BadRequest(
                "Direkt kabulde iç ön etiket kullanılamaz. Ön etiketli işlem için mal kabul emri; dış etiket için tedarikçi etiketi seçilmelidir.");
        if (labelStrategy == GoodsReceiptLabelStrategy.SupplierLabel
            && executionMode != GoodsReceiptExecutionMode.SupplierLabel)
            throw AppException.BadRequest("Tedarikçi etiketi stratejisinde giriş yöntemi de tedarikçi etiketi olmalıdır.");
    }

    internal static bool RequiresUnplannedReceiptPermission(bool direct, bool hasOrderSources)
        => direct && !hasOrderSources;

    internal static string LocationPolicyError(GoodsReceiptLocationSelectionPolicy policy) =>
        policy == GoodsReceiptLocationSelectionPolicy.AnyActiveWarehouseLocation
            ? "Seçilen raf aktif ve hedef depoya ait olmalıdır."
            : "Seçilen raf aktif, hedef depoya ait bir kabul veya staging alanı olmalıdır.";

    internal static void ValidateQualityReceivingLocations(
        bool requiresQuality,
        bool blockPutawayUntilQualityDecision,
        IEnumerable<WarehouseLocation> selectedLocations,
        bool holdsInventoryUntilQualityDecision = false)
    {
        if (requiresQuality
            && blockPutawayUntilQualityDecision
            && !holdsInventoryUntilQualityDecision
            && selectedLocations.Any(location =>
                location.LocationType is not (LocationTypes.Receiving or LocationTypes.Staging)))
            throw AppException.BadRequest(
                "Kalite kararı verilene kadar rafa kaldırma kapalıdır. Kalite kontrollü ürün için kabul veya staging alanı seçiniz.");
    }

    internal static bool RequiresQuality(
        bool qualityAlreadyApproved,
        bool anyStockPolicyRequiresQuality,
        bool forceQualityControl = false) =>
        !qualityAlreadyApproved && (anyStockPolicyRequiresQuality || forceQualityControl);

    internal static bool RequiresQualityForLine(
        bool qualityAlreadyApproved,
        ResolvedQualityPolicy qualityPolicy,
        bool forceQualityControl = false) =>
        !qualityAlreadyApproved
        && (qualityPolicy.InspectionMode != QualityInspectionMode.NoCheck || forceQualityControl);

    internal static GoodsReceiptQualityRoutingSource ResolveQualityRoutingSource(
        ResolvedQualityPolicy qualityPolicy,
        bool forceQualityControl,
        bool qualityAlreadyApproved = false)
    {
        if (qualityAlreadyApproved) return GoodsReceiptQualityRoutingSource.None;
        if (qualityPolicy.InspectionMode != QualityInspectionMode.NoCheck)
            return qualityPolicy.Source switch
            {
                "StockRule" => GoodsReceiptQualityRoutingSource.StockRule,
                "StockGroupRule" => GoodsReceiptQualityRoutingSource.StockGroupRule,
                "GlobalDefault" => GoodsReceiptQualityRoutingSource.GlobalDefault,
                _ => GoodsReceiptQualityRoutingSource.GlobalDefault
            };
        return forceQualityControl
            ? GoodsReceiptQualityRoutingSource.ManualReceipt
            : GoodsReceiptQualityRoutingSource.None;
    }

    internal static bool ShouldHoldInventoryForQuality(
        bool requireQualityControl,
        bool holdInventoryUntilQualityDecision,
        GoodsReceiptQualityRoutingSource routingSource) =>
        requireQualityControl
        && (holdInventoryUntilQualityDecision
            || routingSource == GoodsReceiptQualityRoutingSource.ManualReceipt);

    internal static bool ShouldHoldInventoryForQuality(
        GoodsReceiptLine line,
        GoodsReceiptHeader header) =>
        ShouldHoldInventoryForQuality(
            line.RequireQualityControl,
            header.HoldInventoryUntilQualityDecision,
            line.QualityRoutingSource);

    internal static long ResolveQualityInventoryLocationId(
        GoodsReceiptLine line,
        GoodsReceiptHeader header,
        long requestedLocationId) =>
        ShouldHoldInventoryForQuality(line, header)
            ? header.QualityLocationId ?? requestedLocationId
            : requestedLocationId;

    internal static void ValidateManualQualityPolicy(
        GoodsReceiptErpQualityGatePolicy qualityGatePolicy,
        bool manualQualityRequested)
    {
        if (manualQualityRequested
            && qualityGatePolicy != GoodsReceiptErpQualityGatePolicy.AnyQualityPlan)
            throw AppException.Conflict(
                "Manuel kalite yönlendirmesi için mal kabul ERP kalite bekleme politikası 'Kural veya manuel tüm kalite planlarını bekle' olmalıdır.");
    }

    internal static string ResolveNextAction(bool requiresQualityControl) =>
        requiresQualityControl ? "SendToQuality" : "CreateWaybill";

    private static void ValidateTrackedLines(
        CreateManualGoodsReceiptRequest request,
        IReadOnlyDictionary<long, StockEntity> stocks,
        IReadOnlyDictionary<long, ResolvedQualityPolicy> qualityPolicies,
        IReadOnlyDictionary<long, EffectiveStockTrackingPolicy> trackingPolicies,
        bool requireCompleteCapture,
        bool includeQualityRequirements)
    {
        foreach (var line in request.Lines)
        {
            var qualityPolicy = qualityPolicies[line.StockId];
            var policy = trackingPolicies[line.StockId];
            if (includeQualityRequirements
                && policy.TrackingType == StockTrackingType.None
                && (qualityPolicy.RequireLot || qualityPolicy.RequireSerial || qualityPolicy.RequireExpiryDate))
                throw AppException.BadRequest(
                    $"{stocks[line.StockId].ErpStockCode}: kalite kuralı lot/seri/SKT isterken merkezî stok takip politikası Takipsiz olamaz.");
            var effectivePolicy = policy with
            {
                RequireLot = policy.RequireLot || includeQualityRequirements && qualityPolicy.RequireLot,
                RequireSerial = policy.RequireSerial || includeQualityRequirements && qualityPolicy.RequireSerial,
                RequireExpirationDate = policy.RequireExpirationDate
                    || includeQualityRequirements && qualityPolicy.RequireExpiryDate
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

    private static decimal Sample(decimal quantity, ResolvedQualityPolicy policy) =>
        QualitySamplingCalculator.Calculate(quantity, policy.SamplingMode, policy.SamplingValue);
    private static T Stamp<T>(T entity, long actor) where T : verii_wms_api_v2.Shared.Domain.BaseEntity { entity.CreatedBy = actor; entity.CreatedDate = DateTime.UtcNow; return entity; }
    private static string TaskNo(string documentNo)
    {
        var value = $"{documentNo}-RCV-01";
        return value.Length <= 50 ? value : value[..50];
    }
    private static string? Clean(string? value, int max) { var text = string.IsNullOrWhiteSpace(value) ? null : value.Trim(); return text?.Length > max ? text[..max] : text; }
    private static string? NormalizeDocumentNumber(string? value) =>
        PurchaseWaybillNumberPolicy.Normalize(value);
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
