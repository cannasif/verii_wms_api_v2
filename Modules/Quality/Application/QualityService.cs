using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using System.Data;
using System.Security.Cryptography;
using verii_wms_api_v2.Modules.Audit.Application;
using verii_wms_api_v2.Modules.DocumentSeries.Application;
using verii_wms_api_v2.Modules.DocumentSeries.Domain;
using verii_wms_api_v2.Modules.ErpIntegration.Application;
using verii_wms_api_v2.Modules.GoodsReceipt.Application;
using verii_wms_api_v2.Modules.GoodsReceipt.Domain;
using verii_wms_api_v2.Modules.Identity.Domain;
using verii_wms_api_v2.Modules.Location.Domain;
using verii_wms_api_v2.Modules.Quality.Domain;
using verii_wms_api_v2.Modules.Quality.Localization;
using verii_wms_api_v2.Modules.Stock.Domain;
using verii_wms_api_v2.Modules.StockBalance.Domain;
using verii_wms_api_v2.Modules.StockMovement.Application;
using verii_wms_api_v2.Modules.StockMovement.Domain;
using verii_wms_api_v2.Modules.StockTracking.Application;
using verii_wms_api_v2.Modules.StockTracking.Domain;
using verii_wms_api_v2.Modules.Warehouse.Domain;
using verii_wms_api_v2.Modules.WarehouseOperations.Domain;
using verii_wms_api_v2.Modules.WarehouseTransfer.Application;
using verii_wms_api_v2.Modules.WarehouseTransfer.Domain;
using verii_wms_api_v2.Shared;
using verii_wms_api_v2.Shared.Application.Abstractions.Persistence;
using verii_wms_api_v2.Shared.Application.Exceptions;

namespace verii_wms_api_v2.Modules.Quality.Application;

using StockEntity = verii_wms_api_v2.Modules.Stock.Domain.Stock;
using WarehouseEntity = verii_wms_api_v2.Modules.Warehouse.Domain.Warehouse;

public sealed class QualityService(
    IUnitOfWork uow,
    IAuditLogWriter audit,
    IStockMovementService stockMovement,
    IWarehouseTransferService warehouseTransfer,
    IDocumentSeriesService documentSeries,
    IGoodsReceiptErpPostingCoordinator erpPosting,
    IStringLocalizer<QualityResource> localizer,
    IStockTrackingPolicyResolver? stockTrackingPolicyResolver = null) : IQualityService, IQualityPolicyResolver, IQualityWarehouseRoutingResolver
{
    private IGenericRepository<QualityParameter> Parameters => uow.Repository<QualityParameter>();
    private IGenericRepository<QualityQuarantineDestination> QuarantineDestinations =>
        uow.Repository<QualityQuarantineDestination>();
    private IGenericRepository<QualityWarehouseRoute> WarehouseRoutes =>
        uow.Repository<QualityWarehouseRoute>();
    private IGenericRepository<QualityRule> Rules => uow.Repository<QualityRule>();
    private IGenericRepository<QualityDecisionCode> DecisionCodes => uow.Repository<QualityDecisionCode>();
    private IGenericRepository<QualityInspection> Inspections => uow.Repository<QualityInspection>();
    private IGenericRepository<QualityInspectionDisposition> Dispositions =>
        uow.Repository<QualityInspectionDisposition>();
    private IGenericRepository<QualityInspectionControl> Controls =>
        uow.Repository<QualityInspectionControl>();
    private IGenericRepository<QualityInspectionWorkSession> WorkSessions =>
        uow.Repository<QualityInspectionWorkSession>();

    public async Task<QualityParameterDto> GetParametersAsync(string branchCode, CancellationToken ct = default)
    {
        var branch = NormalizeBranch(branchCode);
        var value = await Parameters.FirstOrDefaultAsync(x => x.BranchCode == branch && x.ParameterKey == "DEFAULT", false, ct) ?? Default(branch);
        return await MapParameterAsync(value, ct);
    }

    public async Task<ResolvedQualityWarehouseRoute> ResolveWarehouseRouteAsync(
        string branchCode,
        long sourceWarehouseId,
        CancellationToken ct = default)
    {
        var branch = NormalizeBranch(branchCode);
        var parameter = await Parameters.FirstOrDefaultAsync(
            value => value.BranchCode == branch && value.ParameterKey == "DEFAULT", false, ct) ?? Default(branch);
        var defaults = ResolveWarehouseRouteDefaults(
            parameter,
            await GetActiveWarehouseRoutesAsync(parameter, ct),
            sourceWarehouseId);
        return new ResolvedQualityWarehouseRoute(
            defaults.QualityLocationId,
            defaults.AcceptedLocationId,
            defaults.QuarantineLocationId,
            defaults.RejectLocationId);
    }

    public async Task<QualityParameterDto> UpdateParametersAsync(UpdateQualityParameterRequest request, long actor, CancellationToken ct = default)
    {
        var branch = NormalizeBranch(request.BranchCode);
        var destinations = NormalizeQuarantineDestinationRequests(request);
        var warehouseRoutes = request.WarehouseRoutes is null
            ? null
            : NormalizeWarehouseRouteRequests(request.WarehouseRoutes);
        var defaultQuarantineLocationId = ResolveDefaultQuarantineLocationId(request, destinations);
        await ValidateLocations(request, destinations, warehouseRoutes ?? [], defaultQuarantineLocationId, branch, ct);

        return await uow.ExecuteInTransactionAsync(async token =>
        {
            var entity = await Parameters.FirstOrDefaultAsync(
                x => x.BranchCode == branch && x.ParameterKey == "DEFAULT", true, token);
            var before = entity is null ? null : await MapParameterAsync(entity, token);
            if (entity is null)
            {
                entity = Default(branch);
                entity.CreatedBy = actor;
                entity.CreatedDate = DateTime.UtcNow;
                await Parameters.AddAsync(entity, token);
                await uow.SaveChangesAsync(token);
            }

            entity.AutoCreateInspectionOnReceipt = request.AutoCreateInspectionOnReceipt;
            entity.DefaultInspectionMode = request.DefaultInspectionMode;
            entity.DefaultFailAction = request.DefaultFailAction;
            entity.HoldInventoryUntilDecision = request.HoldInventoryUntilDecision;
            entity.BlockPutawayUntilDecision = request.BlockPutawayUntilDecision;
            entity.BlockErpPostingUntilDecision = request.BlockErpPostingUntilDecision;
            entity.RequireManagerApprovalForRelease = request.RequireManagerApprovalForRelease;
            entity.AllowPartialDecision = request.AllowPartialDecision;
            entity.AllowDirectReceiptWhenNoRule = request.AllowDirectReceiptWhenNoRule;
            entity.BlockReceiptWhenLotMissing = request.BlockReceiptWhenLotMissing;
            entity.BlockReceiptWhenSerialMissing = request.BlockReceiptWhenSerialMissing;
            entity.BlockReceiptWhenExpiryMissing = request.BlockReceiptWhenExpiryMissing;
            entity.DefaultQualityLocationId = request.DefaultQualityLocationId;
            entity.DefaultAcceptedLocationId = request.DefaultAcceptedLocationId;
            entity.DefaultQuarantineLocationId = defaultQuarantineLocationId;
            entity.DefaultRejectLocationId = request.DefaultRejectLocationId;
            entity.UpdatedBy = actor;
            entity.UpdatedDate = DateTime.UtcNow;

            await SynchronizeQuarantineDestinationsAsync(entity, destinations, actor, token);
            if (warehouseRoutes is not null)
                await SynchronizeWarehouseRoutesAsync(entity, warehouseRoutes, actor, token);
            await uow.SaveChangesAsync(token);
            var result = await MapParameterAsync(entity, token);
            await audit.WriteAsync(new(
                "quality.parameters.update",
                nameof(QualityParameter),
                entity.Id.ToString(),
                "Succeeded",
                "quality",
                OldValues: before,
                NewValues: result,
                ChangedFields: ["Parameters", "QuarantineDestinations", "WarehouseRoutes"]), token);
            return result;
        }, ct, IsolationLevel.Serializable);
    }

    public async Task<PagedResponse<QualityRuleGridRow>> GetRulesPagedAsync(PagedRequest request, CancellationToken ct = default)
    {
        var joined = from rule in Rules.Query()
                     join stock in uow.Repository<StockEntity>().Query() on rule.StockId equals stock.Id into stocks
                     from stock in stocks.DefaultIfEmpty()
                     select new { Rule=rule, Stock=stock };
        var q = joined.Select(x => new QualityRuleGridRow { Id=x.Rule.Id, BranchCode=x.Rule.BranchCode, ScopeType=x.Rule.ScopeType, StockId=x.Rule.StockId,
            StockCode=x.Stock==null?null:x.Stock.ErpStockCode, StockName=x.Stock==null?null:x.Stock.StockName, StockGroupCode=x.Rule.StockGroupCode,
            InspectionMode=x.Rule.InspectionMode.ToString(), SamplingMode=x.Rule.SamplingMode.ToString(), SamplingValue=x.Rule.SamplingValue,
            FailAction=x.Rule.FailAction.ToString(), AutoQuarantine=x.Rule.AutoQuarantine, RequireLot=x.Rule.RequireLot, RequireSerial=x.Rule.RequireSerial,
            RequireExpiryDate=x.Rule.RequireExpiryDate, MinimumRemainingShelfLifeDays=x.Rule.MinimumRemainingShelfLifeDays, IsActive=x.Rule.IsActive,
            Description=x.Rule.Description, CreatedBy=x.Rule.CreatedBy, CreatedDate=x.Rule.CreatedDate, UpdatedBy=x.Rule.UpdatedBy, UpdatedDate=x.Rule.UpdatedDate });
        var search=request.Search?.Trim(); q=q.Where(x=>string.IsNullOrWhiteSpace(search)||(x.StockCode!=null&&x.StockCode.Contains(search))||(x.StockName!=null&&x.StockName.Contains(search))||(x.StockGroupCode!=null&&x.StockGroupCode.Contains(search)));
        return await q.ApplyAdvancedFilters(request).ApplySort(request,nameof(QualityRuleGridRow.Id)).ToPagedResponseAsync(request,ct);
    }

    public async Task<PagedResponse<QualityStockGroupOption>> GetStockGroupsPagedAsync(
        string branchCode, PagedRequest request, CancellationToken ct = default)
    {
        var branch = NormalizeBranch(branchCode);
        var groups = uow.Repository<StockEntity>().Query()
            .Where(x => x.BranchCode == branch && x.GroupCode != null && x.GroupCode != "")
            .GroupBy(x => x.GroupCode!.Trim().ToUpper())
            .Select(x => new QualityStockGroupOption(x.Key, x.Count()));
        groups = groups.ApplySearch(request, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["code"] = nameof(QualityStockGroupOption.Code),
            ["stockCount"] = nameof(QualityStockGroupOption.StockCount)
        }, ["code"]);
        groups = request.SortDirection.Equals("desc", StringComparison.OrdinalIgnoreCase)
            ? groups.OrderByDescending(x => x.Code)
            : groups.OrderBy(x => x.Code);
        return await groups.ToPagedResponseAsync(request, ct);
    }

    public async Task<PagedResponse<QualityDecisionCodeGridRow>> GetDecisionCodesPagedAsync(
        PagedRequest request, CancellationToken ct = default)
    {
        var query = DecisionCodes.Query().Select(entity => new QualityDecisionCodeGridRow
        {
            Id = entity.Id,
            BranchCode = entity.BranchCode,
            Code = entity.Code,
            Name = entity.Name,
            ApplicableDecision = entity.ApplicableDecision,
            Description = entity.Description,
            RequiresNote = entity.RequiresNote,
            SortOrder = entity.SortOrder,
            IsActive = entity.IsActive,
            CreatedBy = entity.CreatedBy,
            CreatedDate = entity.CreatedDate,
            UpdatedBy = entity.UpdatedBy,
            UpdatedDate = entity.UpdatedDate,
            RowVersion = entity.RowVersion
        });
        query = query.ApplySearch(request, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["code"] = nameof(QualityDecisionCodeGridRow.Code),
            ["name"] = nameof(QualityDecisionCodeGridRow.Name),
            ["description"] = nameof(QualityDecisionCodeGridRow.Description)
        }, ["code", "name"]);
        return await query.ApplyAdvancedFilters(request)
            .ApplySort(request, nameof(QualityDecisionCodeGridRow.SortOrder))
            .ToPagedResponseAsync(request, ct);
    }

    public async Task<PagedResponse<QualityDecisionCodeOption>> GetDecisionCodeOptionsPagedAsync(
        string branchCode, QualityDecision decision, PagedRequest request, CancellationToken ct = default)
    {
        var branch = NormalizeBranch(branchCode);
        var query = DecisionCodes.Query()
            .Where(entity => entity.BranchCode == branch
                && entity.IsActive
                && (!entity.ApplicableDecision.HasValue || entity.ApplicableDecision == decision))
            .OrderBy(entity => entity.SortOrder)
            .ThenBy(entity => entity.Code)
            .Select(entity => new QualityDecisionCodeOption(
                entity.Id,
                entity.Code,
                entity.Name,
                entity.ApplicableDecision,
                entity.RequiresNote));
        query = query.ApplySearch(request, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["code"] = nameof(QualityDecisionCodeOption.Code),
            ["name"] = nameof(QualityDecisionCodeOption.Name)
        }, ["code", "name"]);
        return await query.ToPagedResponseAsync(request, ct);
    }

    public async Task<long> CreateDecisionCodeAsync(
        QualityDecisionCodeUpsertRequest request, long actor, CancellationToken ct = default)
    {
        var entity = new QualityDecisionCode();
        await ApplyDecisionCodeAsync(entity, request, null, ct);
        entity.CreatedBy = actor;
        entity.CreatedDate = DateTime.UtcNow;
        await DecisionCodes.AddAsync(entity, ct);
        await uow.SaveChangesAsync(ct);
        await audit.WriteAsync(new("quality.decision-code.create", nameof(QualityDecisionCode),
            entity.Id.ToString(), "Succeeded", "quality", NewValues: DecisionCodeSnapshot(entity),
            ChangedFields: ["DecisionCode"]), ct);
        return entity.Id;
    }

    public async Task UpdateDecisionCodeAsync(
        long id, QualityDecisionCodeUpsertRequest request, long actor, CancellationToken ct = default)
    {
        var entity = await DecisionCodes.FindByIdAsync(id, true, ct)
            ?? throw AppException.NotFound("Kalite karar kodu tanımı bulunamadı.");
        var before = DecisionCodeSnapshot(entity);
        ApplyDecisionCodeVersion(entity, request.RowVersion);
        await ApplyDecisionCodeAsync(entity, request, id, ct);
        entity.UpdatedBy = actor;
        entity.UpdatedDate = DateTime.UtcNow;
        await uow.SaveChangesAsync(ct);
        await audit.WriteAsync(new("quality.decision-code.update", nameof(QualityDecisionCode),
            entity.Id.ToString(), "Succeeded", "quality", OldValues: before,
            NewValues: DecisionCodeSnapshot(entity), ChangedFields: ["DecisionCode"]), ct);
    }

    public async Task DeleteDecisionCodeAsync(long id, long actor, CancellationToken ct = default)
    {
        var entity = await DecisionCodes.FindByIdAsync(id, true, ct)
            ?? throw AppException.NotFound("Kalite karar kodu tanımı bulunamadı.");
        var before = DecisionCodeSnapshot(entity);
        entity.IsActive = false;
        entity.DeletedBy = actor;
        entity.DeletedDate = DateTime.UtcNow;
        entity.IsDeleted = true;
        await uow.SaveChangesAsync(ct);
        await audit.WriteAsync(new("quality.decision-code.delete", nameof(QualityDecisionCode),
            entity.Id.ToString(), "Succeeded", "quality", OldValues: before,
            NewValues: new { entity.Id, entity.IsDeleted }, ChangedFields: ["IsDeleted", "IsActive"]), ct);
    }

    public QualityInspectionStatusCatalogDto GetInspectionStatusCatalog() => BuildInspectionStatusCatalog();

    internal static QualityInspectionStatusCatalogDto BuildInspectionStatusCatalog()
    {
        const QualityInspectionStatus defaultStatus = QualityInspectionStatus.Pending;
        var items = Enum.GetValues<QualityInspectionStatus>()
            .Select(status => new QualityInspectionStatusOptionDto(
                status.ToString(),
                status == defaultStatus,
                IsTerminalStatus(status),
                CanPrioritize(status)))
            .ToArray();
        return new(defaultStatus.ToString(), items);
    }

    public async Task<long> CreateRuleAsync(QualityRuleUpsertRequest request, long actor, CancellationToken ct = default)
    {
        var entity=new QualityRule(); await ApplyRule(entity,request,null,ct); entity.CreatedBy=actor; entity.CreatedDate=DateTime.UtcNow; await Rules.AddAsync(entity,ct); await uow.SaveChangesAsync(ct);
        await audit.WriteAsync(new("quality.rule.create",nameof(QualityRule),entity.Id.ToString(),"Succeeded","quality",NewValues:Snapshot(entity),ChangedFields:["Rule"]),ct); return entity.Id;
    }

    public async Task UpdateRuleAsync(long id, QualityRuleUpsertRequest request, long actor, CancellationToken ct = default)
    {
        var entity=await Rules.FindByIdAsync(id,true,ct)??throw AppException.NotFound("Kalite kuralı bulunamadı."); var before=Snapshot(entity); await ApplyRule(entity,request,id,ct);
        entity.UpdatedBy=actor; entity.UpdatedDate=DateTime.UtcNow; await uow.SaveChangesAsync(ct); await audit.WriteAsync(new("quality.rule.update",nameof(QualityRule),id.ToString(),"Succeeded","quality",OldValues:before,NewValues:Snapshot(entity),ChangedFields:["Rule"]),ct);
    }

    public async Task DeleteRuleAsync(long id,long actor,CancellationToken ct=default)
    {
        var entity=await Rules.FindByIdAsync(id,true,ct)??throw AppException.NotFound("Kalite kuralı bulunamadı."); entity.IsActive=false; entity.DeletedBy=actor; await Rules.SoftDeleteAsync(id,ct); await uow.SaveChangesAsync(ct);
    }

    public async Task<PagedResponse<QualityInspectionGridRow>> GetInspectionsPagedAsync(PagedRequest request,CancellationToken ct=default)
    {
        var joined=from i in Inspections.Query()
                   join w in uow.Repository<WarehouseEntity>().Query() on i.WarehouseId equals w.Id into ws
                   from w in ws.DefaultIfEmpty()
                   join g in uow.Repository<GoodsReceiptHeader>().Query() on new { Type=i.SourceDocumentType, Id=i.SourceDocumentId }
                       equals new { Type="GoodsReceipt", Id=g.Id } into gs
                   from g in gs.DefaultIfEmpty()
                   join u in uow.Repository<User>().Query() on i.CreatedBy equals (long?)u.Id into users
                   from u in users.DefaultIfEmpty()
                   join d in uow.Repository<UserDetail>().Query() on u.Id equals d.UserId into details
                   from d in details.DefaultIfEmpty()
                   where i.QueuedAtUtc != null
                   select new { Inspection=i, Warehouse=w, Receipt=g, User=u, Detail=d };
        var q=joined.Select(x=>new QualityInspectionGridRow { Id=x.Inspection.Id,BranchCode=x.Inspection.BranchCode,InspectionNo=x.Inspection.InspectionNo,
            SourceDocumentType=x.Inspection.SourceDocumentType,SourceDocumentId=x.Inspection.SourceDocumentId,SourceDocumentNo=x.Inspection.SourceDocumentNo,
            WarehouseId=x.Inspection.WarehouseId,WarehouseCode=x.Warehouse==null?null:x.Warehouse.WarehouseCode,
            WarehouseName=x.Warehouse==null?null:x.Warehouse.WarehouseName,SupplierId=x.Inspection.SupplierId,
            SourceWaybillNo=x.Receipt==null?null:(x.Receipt.ElectronicWaybillNo??x.Receipt.WaybillNo),
            CreatedByName=x.User==null?null:(x.Detail==null?x.User.Username:(x.Detail.FirstName+" "+x.Detail.LastName)),
            IsPriority=x.Inspection.IsPriority,PriorityAssignedAtUtc=x.Inspection.PriorityAssignedAtUtc,
            Status=x.Inspection.Status.ToString(),LineCount=x.Inspection.Lines.Count,TotalQuantity=x.Inspection.Lines.Sum(line=>line.Quantity),
            RequiredInspectionQuantity=x.Inspection.Lines.Sum(line=>line.SampleQuantity),InspectedQuantity=x.Inspection.Lines.Sum(line=>line.InspectedQuantity),
            CreatedAtUtc=x.Inspection.CreatedAtUtc,QueuedAtUtc=x.Inspection.QueuedAtUtc,DecidedAtUtc=x.Inspection.DecidedAtUtc,InspectorUserId=x.Inspection.InspectorUserId,
            WorkState=x.Inspection.Status==QualityInspectionStatus.Passed||x.Inspection.Status==QualityInspectionStatus.Failed
                ||x.Inspection.Status==QualityInspectionStatus.Released||x.Inspection.Status==QualityInspectionStatus.Cancelled
                ? QualityInspectionWorkState.Completed.ToString()
                : x.Inspection.WorkSessions.Any(session=>session.EndedAtUtc==null)
                    ? QualityInspectionWorkState.Running.ToString()
                    : x.Inspection.WorkSessions.Any()
                        ? QualityInspectionWorkState.Paused.ToString()
                        : QualityInspectionWorkState.NotStarted.ToString(),
            RecordedWorkSeconds=x.Inspection.WorkSessions.Sum(session=>(long?)session.DurationSeconds)??0,
            WorkSessionCount=x.Inspection.WorkSessions.Count,
            ParticipantCount=x.Inspection.WorkSessions.Select(session=>session.WorkerUserId).Distinct().Count(),
            ActiveWorkerUserId=x.Inspection.WorkSessions.Where(session=>session.EndedAtUtc==null).Select(session=>(long?)session.WorkerUserId).FirstOrDefault(),
            ActiveWorkerName=x.Inspection.WorkSessions.Where(session=>session.EndedAtUtc==null).Select(session=>session.WorkerNameSnapshot).FirstOrDefault(),
            ActiveWorkStartedAtUtc=x.Inspection.WorkSessions.Where(session=>session.EndedAtUtc==null).Select(session=>(DateTimeOffset?)session.StartedAtUtc).FirstOrDefault(),
            WorkStartedByName=x.Inspection.WorkSessions.OrderByDescending(session=>session.SequenceNo).Select(session=>session.WorkerNameSnapshot).FirstOrDefault(),
            WorkStoppedByUserId=x.Inspection.WorkSessions.OrderByDescending(session=>session.SequenceNo).Select(session=>session.EndedAtUtc==null?null:session.EndedByUserId).FirstOrDefault(),
            CreatedBy=x.Inspection.CreatedBy,CreatedDate=x.Inspection.CreatedDate,UpdatedBy=x.Inspection.UpdatedBy,UpdatedDate=x.Inspection.UpdatedDate });
        var search=request.Search?.Trim(); q=q.Where(x=>string.IsNullOrWhiteSpace(search)||x.InspectionNo.Contains(search)||x.SourceDocumentNo.Contains(search)
            ||(x.SourceWaybillNo!=null&&x.SourceWaybillNo.Contains(search))||(x.CreatedByName!=null&&x.CreatedByName.Contains(search))
            ||(x.WorkStartedByName!=null&&x.WorkStartedByName.Contains(search))
            ||(x.WarehouseName!=null&&x.WarehouseName.Contains(search))
            ||(x.SourceDocumentType=="GoodsReceipt" && (
                from line in uow.Repository<GoodsReceiptLine>().Query()
                join source in uow.Repository<GoodsReceiptLineSource>().Query() on line.Id equals source.GrLineId
                where line.GrHeaderId==x.SourceDocumentId
                    && source.ProjectCodeSnapshot!=null
                    && source.ProjectCodeSnapshot.Contains(search)
                select source).Any()));
        var filtered = q.ApplyAdvancedFilters(request);
        var page = await ApplyInspectionListSort(filtered, request).ToPagedResponseAsync(request, ct);
        return new PagedResponse<QualityInspectionGridRow>
        {
            Items = await AttachPriorityRanksAsync(
                await AttachWorkStoppedByNamesAsync(await AttachProjectCodesAsync(page.Items, ct), ct), ct),
            TotalCount = page.TotalCount,
            PageNumber = page.PageNumber,
            PageSize = page.PageSize,
        };
    }

    public async Task<QualityInspectionDetail> GetInspectionAsync(long id, long actor, bool canExecute,
        bool canSupervise, bool canDecide, CancellationToken ct = default)
    {
        var inspection = await Inspections.Query().Include(x => x.Lines).Include(x => x.WorkSessions)
            .FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw AppException.NotFound("Kalite kontrolü bulunamadı.");
        var warehouse = await uow.Repository<WarehouseEntity>().Query().Where(x => x.Id == inspection.WarehouseId)
            .Select(x => new { x.WarehouseCode, x.WarehouseName }).FirstOrDefaultAsync(ct);
        var receipt = inspection.SourceDocumentType == "GoodsReceipt"
            ? await uow.Repository<GoodsReceiptHeader>().Query().Where(x => x.Id == inspection.SourceDocumentId)
                .Select(x => new { x.WaybillNo, x.ElectronicWaybillNo, x.Status, x.ReceivingLocationId }).FirstOrDefaultAsync(ct)
            : null;
        var creator = inspection.CreatedBy.HasValue
            ? await (from user in uow.Repository<User>().Query()
                     join detail in uow.Repository<UserDetail>().Query() on user.Id equals detail.UserId into details
                     from detail in details.DefaultIfEmpty()
                     where user.Id == inspection.CreatedBy.Value
                     select detail == null ? user.Username : detail.FirstName + " " + detail.LastName).FirstOrDefaultAsync(ct)
            : null;
        var lastWork = ResolveLastWorkActors(inspection);
        var header = new QualityInspectionGridRow { Id = inspection.Id, BranchCode = inspection.BranchCode,
            InspectionNo = inspection.InspectionNo, SourceDocumentType = inspection.SourceDocumentType,
            SourceDocumentId = inspection.SourceDocumentId, SourceDocumentNo = inspection.SourceDocumentNo,
            WarehouseId = inspection.WarehouseId, WarehouseCode = warehouse?.WarehouseCode, WarehouseName = warehouse?.WarehouseName,
            SupplierId = inspection.SupplierId, SourceWaybillNo = receipt == null ? null : receipt.ElectronicWaybillNo ?? receipt.WaybillNo,
            CreatedByName = creator, IsPriority = inspection.IsPriority, Status = inspection.Status.ToString(), LineCount = inspection.Lines.Count,
            TotalQuantity = inspection.Lines.Sum(x => x.Quantity),
            RequiredInspectionQuantity = inspection.Lines.Sum(x => x.SampleQuantity),
            InspectedQuantity = inspection.Lines.Sum(x => x.InspectedQuantity), CreatedAtUtc = inspection.CreatedAtUtc,
            QueuedAtUtc = inspection.QueuedAtUtc, DecidedAtUtc = inspection.DecidedAtUtc, InspectorUserId = inspection.InspectorUserId,
            WorkState = ResolveWorkState(inspection).ToString(),
            RecordedWorkSeconds = inspection.WorkSessions.Sum(x => x.DurationSeconds),
            WorkSessionCount = inspection.WorkSessions.Count,
            ParticipantCount = inspection.WorkSessions.Select(x => x.WorkerUserId).Distinct().Count(),
            ActiveWorkerUserId = inspection.WorkSessions.FirstOrDefault(x => x.EndedAtUtc == null)?.WorkerUserId,
            ActiveWorkerName = inspection.WorkSessions.FirstOrDefault(x => x.EndedAtUtc == null)?.WorkerNameSnapshot,
            ActiveWorkStartedAtUtc = inspection.WorkSessions.FirstOrDefault(x => x.EndedAtUtc == null)?.StartedAtUtc,
            WorkStartedByName = lastWork.StartedByName,
            WorkStoppedByUserId = lastWork.StoppedByUserId,
            WorkStoppedByName = await ResolveWorkStoppedByNameAsync(lastWork, ct),
            CreatedBy = inspection.CreatedBy, CreatedDate = inspection.CreatedDate, UpdatedBy = inspection.UpdatedBy, UpdatedDate = inspection.UpdatedDate,
            ProjectCodes = await ResolveProjectCodesAsync(inspection.SourceDocumentType, inspection.SourceDocumentId, ct) };
        var parameter = await Parameters.FirstOrDefaultAsync(x => x.BranchCode == inspection.BranchCode && x.ParameterKey == "DEFAULT", false, ct)
            ?? Default(inspection.BranchCode);
        var warehouseRoutes = await GetActiveWarehouseRoutesAsync(parameter, ct);
        var routeDefaults = ResolveInspectionWarehouseRoute(warehouseRoutes, inspection.WarehouseId);
        var goodsReceiptLineIds = inspection.Lines
            .Where(line => line.GoodsReceiptLineId.HasValue)
            .Select(line => line.GoodsReceiptLineId!.Value)
            .Distinct()
            .ToArray();
        var receiptLineDefaults = goodsReceiptLineIds.Length == 0
            ? new Dictionary<long, QualityReceiptLineAcceptedTarget>()
            : await uow.Repository<GoodsReceiptLine>().Query()
                .Where(line => goodsReceiptLineIds.Contains(line.Id))
                .Select(line => new QualityReceiptLineAcceptedTarget(
                    line.Id,
                    line.TargetWarehouseId,
                    line.DefaultPutawayLocationId,
                    line.DefaultReceivingLocationId))
                .ToDictionaryAsync(line => line.LineId, ct);
        var acceptedLocationIdByInspectionLineId = inspection.Lines.ToDictionary(
            line => line.Id,
            line =>
            {
                if (!line.GoodsReceiptLineId.HasValue
                    || !receiptLineDefaults.TryGetValue(line.GoodsReceiptLineId.Value, out var receiptLine))
                    return ResolveAcceptedLocationId(null, null, receipt?.ReceivingLocationId);
                return ResolveAcceptedLocationId(
                    receiptLine.DefaultReceivingLocationId,
                    receiptLine.DefaultPutawayLocationId,
                    receipt?.ReceivingLocationId);
            });
        var acceptedLocationIds = acceptedLocationIdByInspectionLineId.Values
            .Where(locationId => locationId.HasValue)
            .Select(locationId => locationId!.Value)
            .Distinct()
            .ToArray();
        var acceptedDestinationByLocationId = acceptedLocationIds.Length == 0
            ? new Dictionary<long, QualityDecisionDestinationDto>()
            : await (from location in uow.Repository<WarehouseLocation>().Query()
                     join targetWarehouse in uow.Repository<WarehouseEntity>().Query()
                         on location.WarehouseId equals targetWarehouse.Id
                     where acceptedLocationIds.Contains(location.Id)
                         && location.IsActive
                         && !location.IsQuarantine
                     select new QualityDecisionDestinationDto(
                         location.Id,
                         targetWarehouse.Id,
                         targetWarehouse.WarehouseCode,
                         targetWarehouse.WarehouseName,
                         location.Code,
                         location.Name))
                .ToDictionaryAsync(destination => destination.LocationId, ct);
        var defaultAcceptedDestinationByInspectionLineId = acceptedLocationIdByInspectionLineId
            .Where(pair => pair.Value.HasValue && acceptedDestinationByLocationId.ContainsKey(pair.Value.Value))
            .ToDictionary(pair => pair.Key, pair => acceptedDestinationByLocationId[pair.Value!.Value]);
        var lineSourceSummaries = await ResolveLineSourceSummariesAsync(goodsReceiptLineIds, ct);
        var lines = inspection.Lines.OrderBy(x => x.Id).Select(x =>
        {
            var summary = x.GoodsReceiptLineId is long goodsReceiptLineId
                && lineSourceSummaries.TryGetValue(goodsReceiptLineId, out var found)
                ? found
                : (ProjectCodes: (string?)null, OrderNumbers: (string?)null);
            return new QualityInspectionLineDto(x.Id, x.GoodsReceiptLineId,
            x.StockId, x.StockCodeSnapshot, x.StockNameSnapshot, x.YapCodeSnapshot, x.LotNo, x.SerialNo, x.ExpiryDate,
            x.Quantity, x.SampleQuantity, x.InspectedQuantity, x.AcceptedQuantity, x.RejectedQuantity, x.QuarantineQuantity,
            x.QuarantineLocationId, x.Decision,
            x.DecisionCodeId, x.ReasonCode, x.ReasonNote, x.DecisionBy, x.DecisionAtUtc,
            defaultAcceptedDestinationByInspectionLineId.GetValueOrDefault(x.Id),
            summary.ProjectCodes,
            summary.OrderNumbers);
        }).ToList();
        var quarantineSourceWarehouseIds = receiptLineDefaults.Values
            .Select(line => line.WarehouseId)
            .Append(inspection.WarehouseId)
            .Distinct()
            .ToArray();
        var quarantineDestinations = MarkInspectionQuarantineDefaults(
            await BuildInspectionQuarantineDestinationsAsync(
                parameter, warehouseRoutes, quarantineSourceWarehouseIds, ct),
            inspection.WarehouseId,
            routeDefaults.QuarantineLocationId);
        QualityDecisionDestinationDto? defaultAcceptedDestination = null;
        var lineDefaults = defaultAcceptedDestinationByInspectionLineId.Values
            .DistinctBy(destination => destination.LocationId)
            .Take(2)
            .ToArray();
        if (lineDefaults.Length == 1)
            defaultAcceptedDestination = lineDefaults[0];
        var defaultRejectedDestination = await GetDecisionDestinationAsync(routeDefaults.RejectLocationId, ct);
        var warehouseTransferDocumentSeries = (await documentSeries.GetLookupAsync(
                WmsDocumentType.InterWarehouseTransfer, inspection.BranchCode, ct))
            .Select(series => new QualityDatDocumentSeriesDto(
                series.Id,
                series.Code,
                series.Name,
                series.PreviewDocumentNumber,
                series.IsDefault))
            .ToArray();
        var dispositionHistory = await Dispositions.Query()
            .Where(x => x.QualityInspectionId == inspection.Id)
            .OrderBy(x => x.DecisionAtUtc)
            .ThenBy(x => x.SequenceNo)
            .Select(x => new QualityInspectionDispositionDto(
                x.Id,
                x.QualityInspectionLineId,
                x.IdempotencyKey,
                x.SequenceNo,
                x.Decision,
                x.Quantity,
                x.SourceWarehouseId,
                x.SourceLocationId,
                x.TargetWarehouseId,
                x.TargetLocationId,
                x.SourceWarehouseCodeSnapshot,
                x.SourceLocationCodeSnapshot,
                x.TargetWarehouseCodeSnapshot,
                x.TargetLocationCodeSnapshot,
                x.SourceStockStatus,
                x.TargetStockStatus,
                x.StockMovementOperationId,
                x.WarehouseTransferId,
                x.DecisionCodeId,
                x.ReasonCode,
                x.ReasonNote,
                x.DecisionBy,
                x.DecisionAtUtc))
            .ToListAsync(ct);
        var controlHistory = await Controls.Query()
            .Where(x => x.QualityInspectionId == inspection.Id)
            .OrderBy(x => x.InspectedAtUtc)
            .ThenBy(x => x.Id)
            .Select(x => new QualityInspectionControlDto(
                x.Id,
                x.QualityInspectionLineId,
                x.IdempotencyKey,
                x.LotQuantitySnapshot,
                x.RequiredQuantitySnapshot,
                x.InspectedQuantity,
                x.OutcomeSummary,
                x.Note,
                x.InspectedBy,
                x.InspectedAtUtc))
            .ToListAsync(ct);
        var workNow = DateTimeOffset.UtcNow;
        var workSummary = BuildWorkSummary(
            inspection,
            actor,
            canExecute,
            canSupervise,
            canDecide,
            receipt is not null && IsReceiptReadyForQualityDisposition(receipt.Status),
            workNow);
        var workHistory = inspection.WorkSessions
            .OrderByDescending(x => x.SequenceNo)
            .Select(MapWorkSession)
            .ToArray();
        return new QualityInspectionDetail(header, lines, inspection.Note, inspection.RowVersion,
            parameter.AllowPartialDecision, parameter.RequireManagerApprovalForRelease,
            receipt?.Status,
            receipt is not null && IsReceiptReadyForQualityDisposition(receipt.Status),
            quarantineDestinations, defaultAcceptedDestination, defaultRejectedDestination,
            warehouseTransferDocumentSeries, dispositionHistory, controlHistory, workSummary, workHistory);
    }

    public Task<QualityInspectionWorkSummaryDto> StartInspectionWorkAsync(
        long id,
        StartQualityInspectionWorkRequest request,
        long actor,
        bool canExecute,
        bool canSupervise,
        bool canDecide,
        CancellationToken ct = default)
    {
        if (!canExecute) throw AppException.Forbidden();
        if (request.IdempotencyKey == Guid.Empty)
            throw AppException.BadRequest(Message(QualityMessageKeys.WorkIdempotencyKeyRequired));

        return uow.ExecuteInTransactionAsync(async token =>
        {
            var inspection = await Inspections.Query(true).Include(x => x.WorkSessions)
                .FirstOrDefaultAsync(x => x.Id == id, token)
                ?? throw AppException.NotFound(Message(QualityMessageKeys.InspectionNotFound));
            var now = DateTimeOffset.UtcNow;
            var receiptReady = await IsSourceReceiptReadyAsync(inspection, token);
            var repeated = inspection.WorkSessions.FirstOrDefault(x => x.StartIdempotencyKey == request.IdempotencyKey);
            if (repeated is not null)
                return BuildWorkSummary(inspection, actor, canExecute, canSupervise, canDecide, receiptReady, now);

            ApplyVersion(inspection, request.RowVersion);
            if (IsTerminalStatus(inspection.Status))
                throw AppException.Conflict(Message(QualityMessageKeys.WorkCannotStartForClosedInspection));
            if (!receiptReady)
                throw AppException.Conflict(Message(QualityMessageKeys.ReceiptMustBeCompletedBeforeWork));

            var active = inspection.WorkSessions.FirstOrDefault(x => x.EndedAtUtc == null);
            if (active is not null)
            {
                if (active.WorkerUserId == actor)
                    return BuildWorkSummary(inspection, actor, canExecute, canSupervise, canDecide, receiptReady, now);
                throw AppException.Conflict(Message(
                    QualityMessageKeys.WorkAlreadyActiveByAnotherUser,
                    active.WorkerNameSnapshot));
            }

            var session = new QualityInspectionWorkSession
            {
                BranchCode = inspection.BranchCode,
                QualityInspectionId = inspection.Id,
                QualityInspection = inspection,
                SequenceNo = inspection.WorkSessions.Select(x => x.SequenceNo).DefaultIfEmpty().Max() + 1,
                WorkerUserId = actor,
                WorkerNameSnapshot = await GetActorDisplayNameAsync(actor, token),
                StartedAtUtc = now,
                StartIdempotencyKey = request.IdempotencyKey,
                CreatedBy = actor,
                CreatedDate = DateTime.UtcNow
            };
            await WorkSessions.AddAsync(session, token);
            inspection.StartedAtUtc ??= now;
            if (inspection.Status == QualityInspectionStatus.Pending)
                inspection.Status = QualityInspectionStatus.InProgress;
            inspection.InspectorUserId = actor;
            inspection.UpdatedBy = actor;
            inspection.UpdatedDate = DateTime.UtcNow;
            await uow.SaveChangesAsync(token);
            await audit.WriteAsync(new(
                "quality.inspection.work.start",
                nameof(QualityInspection),
                id.ToString(),
                "Succeeded",
                "quality",
                NewValues: new { session.Id, session.SequenceNo, session.WorkerUserId, session.StartedAtUtc },
                ChangedFields: ["WorkSession", nameof(QualityInspection.Status)]), token);
            return BuildWorkSummary(inspection, actor, canExecute, canSupervise, canDecide, receiptReady, now);
        }, ct, IsolationLevel.Serializable);
    }

    public Task<QualityInspectionWorkSummaryDto> PauseInspectionWorkAsync(
        long id,
        PauseQualityInspectionWorkRequest request,
        long actor,
        bool canExecute,
        bool canSupervise,
        bool canDecide,
        CancellationToken ct = default)
    {
        if (!canExecute) throw AppException.Forbidden();
        if (request.IdempotencyKey == Guid.Empty)
            throw AppException.BadRequest(Message(QualityMessageKeys.WorkIdempotencyKeyRequired));
        if (!Enum.IsDefined(request.Reason)
            || request.Reason is QualityInspectionWorkStopReason.DecisionApplied
                or QualityInspectionWorkStopReason.InspectionCancelled)
            throw AppException.BadRequest(Message(QualityMessageKeys.WorkStopReasonRequired));
        var note = Clean(request.Note, 1000);
        if (request.Reason == QualityInspectionWorkStopReason.Other && string.IsNullOrWhiteSpace(note))
            throw AppException.BadRequest(Message(QualityMessageKeys.WorkOtherStopNoteRequired));

        return uow.ExecuteInTransactionAsync(async token =>
        {
            var inspection = await Inspections.Query(true).Include(x => x.WorkSessions).Include(x => x.Lines)
                .FirstOrDefaultAsync(x => x.Id == id, token)
                ?? throw AppException.NotFound(Message(QualityMessageKeys.InspectionNotFound));
            var now = DateTimeOffset.UtcNow;
            var receiptReady = await IsSourceReceiptReadyAsync(inspection, token);
            if (inspection.WorkSessions.Any(x => x.EndIdempotencyKey == request.IdempotencyKey))
                return BuildWorkSummary(inspection, actor, canExecute, canSupervise, canDecide, receiptReady, now);

            ApplyVersion(inspection, request.RowVersion);
            var active = inspection.WorkSessions.FirstOrDefault(x => x.EndedAtUtc == null)
                ?? throw AppException.Conflict(Message(QualityMessageKeys.WorkHasNoActiveSession));
            if (active.WorkerUserId != actor && !canSupervise)
                throw AppException.Forbidden(Message(QualityMessageKeys.WorkPauseRequiresOwnerOrSupervisor));

            await ApplyProgressControlsAsync(inspection, request.ControlQuantities, request.IdempotencyKey, actor, now, token);
            CloseWorkSession(active, now, request.Reason, note, request.IdempotencyKey, actor);
            var revertedToPending = TryRevertIdleInProgress(inspection);
            inspection.UpdatedBy = actor;
            inspection.UpdatedDate = DateTime.UtcNow;
            await uow.SaveChangesAsync(token);
            await audit.WriteAsync(new(
                "quality.inspection.work.pause",
                nameof(QualityInspection),
                id.ToString(),
                "Succeeded",
                "quality",
                NewValues: new { active.Id, active.SequenceNo, active.WorkerUserId, active.EndedAtUtc, active.DurationSeconds, active.StopReason, active.StopNote, active.EndedByUserId, inspection.Status },
                ChangedFields: revertedToPending
                    ? ["WorkSession", nameof(QualityInspection.Status)]
                    : ["WorkSession"]), token);
            return BuildWorkSummary(inspection, actor, canExecute, canSupervise, canDecide, receiptReady, now);
        }, ct, IsolationLevel.Serializable);
    }

    public Task<QualityInspectionPriorityResult> ToggleInspectionPriorityAsync(
        long id,
        long actor,
        CancellationToken ct = default) =>
        uow.ExecuteInTransactionAsync(async token =>
        {
            var inspection = await Inspections.Query(true)
                .FirstOrDefaultAsync(value => value.Id == id, token)
                ?? throw AppException.NotFound(Message(QualityMessageKeys.InspectionNotFound));
            if (!CanPrioritize(inspection.Status))
                throw AppException.Conflict(Message(QualityMessageKeys.PriorityOnlyForOpenInspection));

            var previous = inspection.IsPriority;
            var current = TogglePriority(inspection, actor);
            await uow.SaveChangesAsync(token);
            await audit.WriteAsync(new(
                "quality.inspection.priority.toggle",
                nameof(QualityInspection),
                id.ToString(),
                "Succeeded",
                "quality",
                OldValues: new { IsPriority = previous },
                NewValues: new { IsPriority = current },
                ChangedFields: [nameof(QualityInspection.IsPriority)]), token);
            return new QualityInspectionPriorityResult(id, current);
        }, ct, IsolationLevel.Serializable);

    public async Task<QualityDecisionResult> DecideInspectionAsync(long id, DecideQualityInspectionRequest request, long actor,
        bool canReleaseQuarantine, CancellationToken ct = default)
    {
        long goodsReceiptId;
        try
        {
            var hasDispositionDecisions = request.Dispositions is { Count: > 0 };
            var hasQuantityDecisions = request.QuantityDecisions is { Count: > 0 };
            if (request.IdempotencyKey == Guid.Empty
                || !hasDispositionDecisions && !hasQuantityDecisions
                    && request.Decision is QualityDecision.Pending or QualityDecision.Hold)
                throw AppException.BadRequest("Nihai karar kabul, ret, karantina veya tedarikçiye iade olmalıdır.");
            goodsReceiptId = await uow.ExecuteInTransactionAsync(async token =>
            {
            var inspection = await Inspections.Query(true).Include(x => x.Lines).Include(x => x.WorkSessions)
                .FirstOrDefaultAsync(x => x.Id == id, token)
                ?? throw AppException.NotFound("Kalite kontrolü bulunamadı.");
            if (await Dispositions.AnyAsync(x => x.QualityInspectionId == id
                    && x.IdempotencyKey == request.IdempotencyKey, token)
                || await Controls.AnyAsync(x => x.QualityInspectionId == id
                    && x.IdempotencyKey == request.IdempotencyKey, token))
                return inspection.SourceDocumentId;
            var activeWorkSession = inspection.WorkSessions.FirstOrDefault(x => x.EndedAtUtc == null);
            if (activeWorkSession is null || activeWorkSession.WorkerUserId != actor)
                throw AppException.Conflict(Message(QualityMessageKeys.WorkMustBeActiveForCurrentUser));
            ApplyVersion(inspection, request.RowVersion);
            if (inspection.Status == QualityInspectionStatus.Cancelled) throw AppException.Conflict("İptal edilmiş kalite kontrolü sonuçlandırılamaz.");
            if (!string.Equals(inspection.SourceDocumentType, "GoodsReceipt", StringComparison.OrdinalIgnoreCase))
                throw AppException.Conflict("Bu kaynak türü için fiziksel kalite kararı henüz desteklenmiyor.");

            var gr = await uow.Repository<GoodsReceiptHeader>().Query(true)
                .Include(x => x.Lines)
                .Include(x => x.Tasks)
                .FirstOrDefaultAsync(x => x.Id == inspection.SourceDocumentId, token)
                ?? throw AppException.NotFound("Mal kabul kaydı bulunamadı.");
            if (!IsReceiptReadyForQualityDisposition(gr.Status))
                throw AppException.Conflict(Message(QualityMessageKeys.ReceiptMustBeCompletedBeforeRouting));
            var parameter = await Parameters.FirstOrDefaultAsync(x => x.BranchCode == inspection.BranchCode && x.ParameterKey == "DEFAULT", false, token)
                ?? Default(inspection.BranchCode);
            var warehouseRoutes = await GetActiveWarehouseRoutesAsync(parameter, token);
            var sectionQuarantineDestinations = await GetQuarantineDestinationsAsync(parameter, token);
            IReadOnlyList<QualityQuarantineDestinationDto> configuredQuarantineDestinations = sectionQuarantineDestinations;
            var quantityDecisionGroups = request.QuantityDecisions?.GroupBy(x => x.LineId).ToList();
            if (quantityDecisionGroups?.Any(x => x.Count() != 1) == true)
                throw AppException.BadRequest("Aynı kalite satırı için birden fazla miktar dağılımı gönderilemez.");
            var quantityDecisions = quantityDecisionGroups?.ToDictionary(x => x.Key, x => x.Single());
            var requestedIds = hasDispositionDecisions
                ? request.Dispositions!.Select(x => x.LineId).Where(x => x > 0).ToHashSet()
                : hasQuantityDecisions
                    ? quantityDecisions!.Keys.Where(x => x > 0).ToHashSet()
                    : request.LineIds?.Where(x => x > 0).Distinct().ToHashSet();
            var eligible = inspection.Lines.Where(x => x.Decision is QualityDecision.Pending or QualityDecision.Hold or QualityDecision.Quarantined).ToList();
            var selected = requestedIds is { Count: > 0 } ? eligible.Where(x => requestedIds.Contains(x.Id)).ToList() : eligible;
            if (selected.Count == 0 || requestedIds is { Count: > 0 } && selected.Count != requestedIds.Count)
                throw AppException.BadRequest("Seçilen kalite satırlarından biri bulunamadı veya daha önce sonuçlandırılmış.");
            if (selected.Count != eligible.Count && !parameter.AllowPartialDecision)
                throw AppException.Conflict("Kalite ayarlarında kısmi karar kapalı; bekleyen satırların tamamı seçilmelidir.");

            var decisionParts = BuildDecisionParts(
                selected,
                request.Dispositions,
                quantityDecisions,
                request.Decision,
                request.QuarantineLocationId,
                request.DecisionCodeId);
            decisionParts = await ResolveDecisionCodesAsync(
                inspection.BranchCode,
                decisionParts,
                request.DecisionCodeId,
                request.Note,
                token);
            var controlQuantities = ValidateControlQuantities(selected, request.ControlQuantities);
            var releasesQuarantine = decisionParts.Any(x =>
                x.Decision == QualityDecision.Accepted
                && x.Line.Decision == QualityDecision.Quarantined);
            if (releasesQuarantine && parameter.RequireManagerApprovalForRelease && !canReleaseQuarantine)
                throw AppException.Forbidden("Karantinadan serbest bırakma için yönetici izni gerekir.");
            var grLineIds = selected.Where(x => x.GoodsReceiptLineId.HasValue).Select(x => x.GoodsReceiptLineId!.Value).Distinct().ToArray();
            var grLines = gr.Lines.Where(x => grLineIds.Contains(x.Id)).ToDictionary(x => x.Id);
            if (grLines.Count != grLineIds.Length) throw AppException.Conflict("Kalite satırının mal kabul bağlantısı eksik.");
            configuredQuarantineDestinations = await BuildInspectionQuarantineDestinationsAsync(
                parameter,
                warehouseRoutes,
                grLines.Values.Select(line => line.TargetWarehouseId).Append(inspection.WarehouseId),
                token);
            EnsureInspectionDecisionDestinations(
                decisionParts,
                grLines,
                warehouseRoutes,
                configuredQuarantineDestinations,
                gr.ReceivingLocationId);
            foreach (var line in selected.Where(x =>
                         !string.IsNullOrWhiteSpace(x.SerialNo)
                         || grLines[x.GoodsReceiptLineId!.Value].TrackingType == StockTrackingType.Serial))
            {
                var outcomeCount = decisionParts.Count(x => x.Line.Id == line.Id && x.Quantity > 0);
                var serialQuantityRule = stockTrackingPolicyResolver is null
                    ? SerialQuantityRule.OneSerialPerBaseUnit
                    : (await stockTrackingPolicyResolver.ResolveAsync(
                        inspection.BranchCode,
                        line.StockId,
                        token)).SerialQuantityRule;
                if (outcomeCount > 1 && serialQuantityRule != SerialQuantityRule.OneSerialPerLine)
                    throw AppException.Conflict(
                        $"'{line.SerialNo ?? line.StockCodeSnapshot}' seri takipli satırı birden fazla kalite sonucuna bölünemez.");
            }

            var qualityLineIds = selected.Select(line => line.Id).ToArray();
            var executionSources = await uow.Repository<GoodsReceiptExecutionLine>().Query()
                .Where(line => line.QualityInspectionLineId.HasValue
                    && qualityLineIds.Contains(line.QualityInspectionLineId.Value))
                .OrderByDescending(line => line.Id)
                .Select(line => new QualityReceiptExecutionSource(
                    line.QualityInspectionLineId!.Value,
                    line.WarehouseId,
                    line.LocationId,
                    line.StockStatus))
                .ToListAsync(token);
            var executionSourceByQualityLine = executionSources
                .GroupBy(source => source.QualityInspectionLineId)
                .ToDictionary(group => group.Key, group => group.First());

            var stockIds = selected.Select(line => line.StockId).Distinct().ToArray();
            var sourceWarehouseIds = grLines.Values.Select(line => line.TargetWarehouseId)
                .Concat(executionSources.Select(source => source.WarehouseId))
                .Concat(configuredQuarantineDestinations.Select(destination => destination.WarehouseId))
                .Distinct()
                .ToArray();
            var balances = await uow.Repository<LocationStockBalance>().Query()
                .Where(balance => stockIds.Contains(balance.StockId)
                    && sourceWarehouseIds.Contains(balance.WarehouseId)
                    && balance.AvailableQuantity > 0)
                .ToListAsync(token);

            var involvedWarehouseIds = grLines.Values
                .Select(line => line.TargetWarehouseId)
                .Append(inspection.WarehouseId)
                .ToHashSet();
            var decisionTargetLocationIds = configuredQuarantineDestinations
                .Select(destination => destination.LocationId)
                .Concat(warehouseRoutes
                    .Where(route => involvedWarehouseIds.Contains(route.Key))
                    .SelectMany(route => new long?[]
                    {
                        route.Value.AcceptedLocationId,
                        route.Value.QuarantineLocationId,
                        route.Value.RejectLocationId
                    })
                    .Where(locationId => locationId.HasValue)
                    .Select(locationId => locationId!.Value))
                .Concat(decisionParts.Select(part => part.TargetLocationId ?? 0))
                .Where(locationId => locationId > 0)
                .Distinct()
                .ToArray();
            var acceptedFallbackLocationIds = grLines.Values
                .Select(line => ResolveAcceptedLocationId(
                    line.DefaultReceivingLocationId,
                    line.DefaultPutawayLocationId,
                    gr.ReceivingLocationId))
                .Where(locationId => locationId.HasValue)
                .Select(locationId => locationId!.Value)
                .Distinct()
                .ToArray();
            var receiptLocationIds = grLines.Values
                .SelectMany(line => new long?[]
                {
                    line.DefaultReceivingLocationId,
                    line.DefaultPutawayLocationId,
                    gr.ReceivingLocationId
                })
                .Where(locationId => locationId.HasValue)
                .Select(locationId => locationId!.Value)
                .Distinct()
                .ToArray();
            var movementLocationIds = balances.Select(balance => balance.LocationId)
                .Concat(receiptLocationIds)
                .Concat(acceptedFallbackLocationIds)
                .Concat(decisionTargetLocationIds)
                .Distinct()
                .ToArray();
            var movementLocations = await uow.Repository<WarehouseLocation>().Query()
                .Where(location => movementLocationIds.Contains(location.Id))
                .ToDictionaryAsync(location => location.Id, token);
            var requiredLocationIds = ResolveRequiredDecisionTargetLocationIds(
                decisionParts,
                grLines,
                parameter,
                warehouseRoutes,
                gr.ReceivingLocationId,
                configuredQuarantineDestinations);
            if (requiredLocationIds.Any(locationId => !movementLocations.ContainsKey(locationId)))
                throw AppException.Conflict("Kalite stok hareketi için hedef raf bulunamadı veya kullanıma kapatılmış.");

            foreach (var part in decisionParts)
            {
                var receiptLine = grLines[part.Line.GoodsReceiptLineId!.Value];
                var effectiveTargetLocationId = ResolveInspectionDecisionTargetLocationId(
                    part,
                    receiptLine.TargetWarehouseId,
                    warehouseRoutes,
                    configuredQuarantineDestinations,
                    receiptLine.DefaultReceivingLocationId,
                    receiptLine.DefaultPutawayLocationId,
                    gr.ReceivingLocationId);
                if (!effectiveTargetLocationId.HasValue)
                    continue;

                var target = movementLocations[effectiveTargetLocationId.Value];
                if (!target.IsActive || !string.Equals(target.BranchCode, inspection.BranchCode, StringComparison.OrdinalIgnoreCase))
                    throw AppException.BadRequest("Seçilen kalite hedefi aktif ve kalite kaydıyla aynı şubede olmalıdır.");
                if (part.Decision == QualityDecision.Accepted && target.IsQuarantine)
                    throw AppException.BadRequest("Onaylanan miktarın hedef rafı karantina dışı olmalıdır.");
                if (part.Decision is QualityDecision.Quarantined or QualityDecision.Rejected && !target.IsQuarantine)
                    throw AppException.BadRequest("Ret ve karantina miktarlarının hedefi karantina tipi raf olmalıdır.");
                if (part.Decision == QualityDecision.Returned)
                    throw AppException.BadRequest("Tedarikçiye iade kararında hedef raf seçilemez.");
            }

            var remainingByBalanceId = balances.ToDictionary(balance => balance.Id, balance => balance.AvailableQuantity);
            var dispositions = new List<QualityInventoryDisposition>();
            foreach (var part in decisionParts)
            {
                var line = part.Line;
                var receiptLine = grLines[line.GoodsReceiptLineId!.Value];
                var receiptLocationId = receiptLine.DefaultReceivingLocationId ?? gr.ReceivingLocationId;
                executionSourceByQualityLine.TryGetValue(line.Id, out var executionSource);
                var wasQuarantined = line.Decision == QualityDecision.Quarantined;
                var preferredSourceLocationId = wasQuarantined
                    ? line.QuarantineLocationId
                        ?? configuredQuarantineDestinations.FirstOrDefault(destination => destination.IsDefault)?.LocationId
                        ?? configuredQuarantineDestinations.OrderBy(destination => destination.Priority)
                            .Select(destination => (long?)destination.LocationId).FirstOrDefault()
                        ?? throw AppException.Conflict("Karantina rafı ayarlarda tanımlı değil.")
                    : executionSource?.LocationId ?? receiptLocationId;
                var preferredSourceStatus = wasQuarantined
                    ? "Quarantine"
                    : executionSource?.StockStatus
                        ?? (GoodsReceiptOperationsService.ShouldHoldInventoryForQuality(receiptLine, gr)
                            ? "QualityHold"
                            : "Available");
                var allowedSourceStatuses = wasQuarantined
                    ? QuarantinedSourceStatuses
                    : PendingQualitySourceStatuses;
                var allowedQuarantineLocationIds = wasQuarantined
                    ? line.QuarantineLocationId.HasValue
                        ? [line.QuarantineLocationId.Value]
                        : configuredQuarantineDestinations.Select(destination => destination.LocationId).ToArray()
                    : [];
                var candidates = balances
                    .Where(balance => (wasQuarantined || balance.WarehouseId == receiptLine.TargetWarehouseId)
                        && (!wasQuarantined || allowedQuarantineLocationIds.Contains(balance.LocationId))
                        && SameInventoryDimension(balance, line, receiptLine.UnitCode)
                        && allowedSourceStatuses.Contains(balance.StockStatus, StringComparer.OrdinalIgnoreCase)
                        && movementLocations.TryGetValue(balance.LocationId, out _))
                    .Select(balance => new QualityInventorySourceCandidate(
                        balance.Id,
                        balance.WarehouseId,
                        balance.LocationId,
                        movementLocations[balance.LocationId].Code,
                        balance.StockStatus,
                        balance.AvailableQuantity,
                        balance.LastTransactionDate))
                    .ToList();
                var allocations = AllocateInventorySources(
                    candidates,
                    remainingByBalanceId,
                    part.Quantity,
                    preferredSourceLocationId,
                    preferredSourceStatus,
                    line.StockCodeSnapshot,
                    gr.DocumentNo,
                    line.LotNo,
                    line.SerialNo);

                foreach (var allocation in allocations)
                {
                    var destinationLocationId = ResolveInspectionDecisionTargetLocationId(
                        part,
                        receiptLine.TargetWarehouseId,
                        warehouseRoutes,
                        configuredQuarantineDestinations,
                        receiptLine.DefaultReceivingLocationId,
                        receiptLine.DefaultPutawayLocationId,
                        gr.ReceivingLocationId)
                        ?? (part.Decision is QualityDecision.Accepted or QualityDecision.Quarantined or QualityDecision.Rejected
                            ? throw AppException.Conflict(Message(InspectionDestinationMessageKey(part.Decision)))
                            : receiptLocationId);
                    var destinationStatus = part.Decision switch
                    {
                        QualityDecision.Accepted => "Available",
                        QualityDecision.Quarantined => "Quarantine",
                        QualityDecision.Rejected => "Rejected",
                        _ => allocation.StockStatus
                    };
                    dispositions.Add(new QualityInventoryDisposition(
                        line,
                        receiptLine,
                        allocation.WarehouseId,
                        allocation.LocationId,
                        movementLocations[destinationLocationId].WarehouseId,
                        destinationLocationId,
                        allocation.StockStatus,
                        destinationStatus,
                        part.Decision,
                        allocation.Quantity,
                        part.DecisionCodeId,
                        part.ReasonCode,
                        part.Note));
                }
            }

            var datDispositions = dispositions
                .Where(x => x.Decision != QualityDecision.Returned && RequiresDat(x.SourceWarehouseId, x.TargetWarehouseId))
                .ToList();
            var movementLines = dispositions
                .Where(x => x.Decision == QualityDecision.Returned || !RequiresDat(x.SourceWarehouseId, x.TargetWarehouseId))
                .Where(x => x.Decision == QualityDecision.Returned
                    || x.SourceLocationId != x.TargetLocationId
                    || !string.Equals(x.SourceStockStatus, x.TargetStockStatus, StringComparison.OrdinalIgnoreCase))
                .Select(x => x.Decision == QualityDecision.Returned
                    ? new StockMovementLineRequest(x.InspectionLine.StockId, x.InspectionLine.YapCodeId, x.Quantity,
                        x.SourceWarehouseId, x.SourceLocationId,
                        null, null, x.ReceiptLine.UnitCode, x.InspectionLine.LotNo, x.InspectionLine.SerialNo,
                        x.SourceStockStatus, x.SourceStockStatus, null)
                    : new StockMovementLineRequest(x.InspectionLine.StockId, x.InspectionLine.YapCodeId, x.Quantity,
                        x.SourceWarehouseId, x.SourceLocationId, x.TargetWarehouseId, x.TargetLocationId,
                        x.ReceiptLine.UnitCode, x.InspectionLine.LotNo, x.InspectionLine.SerialNo,
                        x.SourceStockStatus, x.SourceStockStatus, x.TargetStockStatus))
                .ToList();
            StockMovementPostResult? movement = null;
            if (movementLines.Count > 0)
                movement = await stockMovement.PostAsync(new PostStockMovementRequest($"QUALITY:{inspection.Id}:{request.IdempotencyKey:N}",
                    dispositions.All(x => x.Decision == QualityDecision.Returned)
                        ? StockMovementTypes.SupplierReturn
                        : StockMovementTypes.Transfer,
                    "QualityInspection", inspection.InspectionNo, inspection.Id, DateTime.UtcNow, "QualityDisposition", request.Note, movementLines), token);

            var datIds = new List<long>();
            var datIdByRoute = new Dictionary<(long SourceWarehouseId, long TargetWarehouseId), long>();
            if (datDispositions.Count > 0)
            {
                if (!request.WarehouseTransferDocumentSeriesId.HasValue
                    || request.WarehouseTransferDocumentSeriesId.Value <= 0)
                    throw AppException.Conflict(Message(QualityMessageKeys.DatDocumentSeriesRequired));
                var series = (await documentSeries.GetLookupAsync(
                        WmsDocumentType.InterWarehouseTransfer, inspection.BranchCode, token))
                    .FirstOrDefault(x => x.Id == request.WarehouseTransferDocumentSeriesId.Value)
                    ?? throw AppException.Conflict(Message(QualityMessageKeys.DatDocumentSeriesInvalid));
                foreach (var group in datDispositions.GroupBy(x => new { x.SourceWarehouseId, x.TargetWarehouseId }))
                {
                    var dat = await warehouseTransfer.CreateDraftAsync(new CreateWarehouseTransferDraftRequest(
                        CreateDatIdempotencyKey(request.IdempotencyKey, group.Key.SourceWarehouseId, group.Key.TargetWarehouseId),
                        inspection.BranchCode,
                        series.Id,
                        DateOnly.FromDateTime(DateTime.UtcNow),
                        WarehouseTransferInitiationMode.DirectTransfer,
                        WarehouseTransferProcessType.Direct,
                        group.Key.SourceWarehouseId,
                        group.Key.TargetWarehouseId,
                        null,
                        group.Select(x => (long?)x.TargetLocationId).Distinct().Count() == 1 ? group.First().TargetLocationId : null,
                        null,
                        null,
                        null,
                        3,
                        inspection.InspectionNo,
                        $"Kalite kararı sonrası depo değişikliği. Mal kabul: {gr.DocumentNo}.",
                        group.Select(x => new WarehouseTransferLineDraftRequest(
                            x.InspectionLine.StockId,
                            x.InspectionLine.YapCodeId,
                            x.Quantity,
                            x.ReceiptLine.UnitCode,
                            x.ReceiptLine.TrackingType,
                            x.ReceiptLine.RequireHandlingUnit,
                            x.SourceLocationId,
                            x.TargetLocationId,
                            request.Note,
                            BuildDatTrackings(x),
                            null,
                            x.SourceStockStatus,
                            x.TargetStockStatus)).ToList(),
                        [],
                        WarehouseTransferBusinessContext.QualityDisposition), actor, token);
                    datIds.Add(dat.Id);
                    datIdByRoute[(group.Key.SourceWarehouseId, group.Key.TargetWarehouseId)] = dat.Id;
                }
            }

            var now = DateTimeOffset.UtcNow;
            var dispositionWarehouseIds = dispositions
                .SelectMany(disposition => new[] { disposition.SourceWarehouseId, disposition.TargetWarehouseId })
                .Distinct()
                .ToArray();
            var warehouseCodes = await uow.Repository<WarehouseEntity>().Query()
                .Where(warehouse => dispositionWarehouseIds.Contains(warehouse.Id))
                .ToDictionaryAsync(warehouse => warehouse.Id, warehouse => warehouse.WarehouseCode.ToString(), token);
            foreach (var line in selected)
            {
                var receiptLine = grLines[line.GoodsReceiptLineId!.Value];
                var parts = decisionParts.Where(x => x.Line.Id == line.Id).ToList();
                var control = controlQuantities[line.Id];
                var quarantineLocationIds = dispositions
                    .Where(x => x.InspectionLine.Id == line.Id && x.Decision == QualityDecision.Quarantined)
                    .Select(x => x.TargetLocationId)
                    .Distinct()
                    .ToArray();
                var quarantineLocationId = quarantineLocationIds.Length == 1
                    ? quarantineLocationIds[0]
                    : (long?)null;
                ApplyDecisionParts(
                    line,
                    receiptLine,
                    parts,
                    actor,
                    now,
                    request.Note,
                    quarantineLocationId);
                line.InspectedQuantity += control.InspectedQuantity;
                if (control.InspectedQuantity > 0)
                {
                    await Controls.AddAsync(new QualityInspectionControl
                    {
                        BranchCode = inspection.BranchCode,
                        QualityInspectionId = inspection.Id,
                        QualityInspectionLineId = line.Id,
                        IdempotencyKey = request.IdempotencyKey,
                        LotQuantitySnapshot = control.LotQuantity,
                        RequiredQuantitySnapshot = control.RequiredQuantity,
                        InspectedQuantity = control.InspectedQuantity,
                        OutcomeSummary = string.Join(" | ", parts.Select(part =>
                            $"{part.Decision}:{part.Quantity:0.######}")),
                        Note = Clean(request.Note, 1000),
                        InspectedBy = actor,
                        InspectedAtUtc = now,
                        CreatedBy = actor,
                        CreatedDate = DateTime.UtcNow
                    }, token);
                }
                var receiptAcceptLocationId = ResolveAcceptedLocationId(
                    receiptLine.DefaultReceivingLocationId,
                    receiptLine.DefaultPutawayLocationId,
                    gr.ReceivingLocationId);
                var acceptedIntoPutaway = dispositions
                    .Where(x => x.InspectionLine.Id == line.Id
                        && x.Decision == QualityDecision.Accepted
                        && (movementLocations[x.TargetLocationId].IsPutaway
                            || (receiptAcceptLocationId.HasValue && x.TargetLocationId == receiptAcceptLocationId.Value)))
                    .Sum(x => x.Quantity);
                if (acceptedIntoPutaway > 0)
                {
                    receiptLine.PutawayQuantity = Math.Min(
                        receiptLine.AcceptedQuantity,
                        receiptLine.PutawayQuantity + acceptedIntoPutaway);
                }
            }

            var dispositionSequence = 0;
            foreach (var disposition in dispositions)
            {
                var isDat = RequiresDat(disposition.SourceWarehouseId, disposition.TargetWarehouseId);
                datIdByRoute.TryGetValue(
                    (disposition.SourceWarehouseId, disposition.TargetWarehouseId),
                    out var warehouseTransferId);
                await Dispositions.AddAsync(new QualityInspectionDisposition
                {
                    BranchCode = inspection.BranchCode,
                    QualityInspectionId = inspection.Id,
                    QualityInspectionLineId = disposition.InspectionLine.Id,
                    IdempotencyKey = request.IdempotencyKey,
                    SequenceNo = ++dispositionSequence,
                    Decision = disposition.Decision,
                    Quantity = disposition.Quantity,
                    SourceWarehouseId = disposition.SourceWarehouseId,
                    SourceLocationId = disposition.SourceLocationId,
                    TargetWarehouseId = disposition.TargetWarehouseId,
                    TargetLocationId = disposition.TargetLocationId,
                    SourceWarehouseCodeSnapshot = warehouseCodes.GetValueOrDefault(
                        disposition.SourceWarehouseId,
                        disposition.SourceWarehouseId.ToString()),
                    SourceLocationCodeSnapshot = movementLocations[disposition.SourceLocationId].Code,
                    TargetWarehouseCodeSnapshot = warehouseCodes.GetValueOrDefault(
                        disposition.TargetWarehouseId,
                        disposition.TargetWarehouseId.ToString()),
                    TargetLocationCodeSnapshot = movementLocations[disposition.TargetLocationId].Code,
                    SourceStockStatus = disposition.SourceStockStatus,
                    TargetStockStatus = disposition.TargetStockStatus,
                    StockMovementOperationId = isDat ? null : movement?.OperationId,
                    WarehouseTransferId = isDat && warehouseTransferId > 0 ? warehouseTransferId : null,
                    DecisionCodeId = disposition.DecisionCodeId,
                    ReasonCode = disposition.ReasonCode,
                    ReasonNote = disposition.Note ?? Clean(request.Note, 1000),
                    DecisionBy = actor,
                    DecisionAtUtc = now,
                    CreatedBy = actor,
                    CreatedDate = DateTime.UtcNow
                }, token);
            }
            var decisionState = ResolveDecisionState(
                inspection.Lines,
                releasesQuarantine);
            inspection.Status = decisionState.InspectionStatus;
            inspection.DecidedAtUtc = decisionState.IsTerminal ? now : null;
            if (decisionState.IsTerminal)
            {
                inspection.IsPriority = false;
                inspection.PriorityAssignedAtUtc = null;
                CloseWorkSession(
                    activeWorkSession,
                    now,
                    QualityInspectionWorkStopReason.DecisionApplied,
                    request.Note,
                    request.IdempotencyKey,
                    actor);
            }
            inspection.InspectorUserId = actor; inspection.Note = Clean(request.Note, 1000);
            inspection.UpdatedBy = actor; inspection.UpdatedDate = DateTime.UtcNow;
            gr.QualityStatus = decisionState.ReceiptStatus;
            SynchronizeGoodsReceiptStatus(gr, actor);
            await uow.SaveChangesAsync(token);
            await audit.WriteAsync(new("quality.inspection.decide", nameof(QualityInspection), id.ToString(), "Succeeded", "quality",
                NewValues: new { request.IdempotencyKey, request.Decision, request.LineIds, request.DecisionCodeId,
                    request.QuantityDecisions, request.Dispositions, request.ControlQuantities, request.QuarantineLocationId,
                    request.WarehouseTransferDocumentSeriesId,
                    MovementId = movement?.OperationId, WarehouseTransferIds = datIds },
                ChangedFields: ["Status", "Lines", "InventoryStatus"]), token);
                return gr.Id;
            }, ct, IsolationLevel.Serializable);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await audit.WriteAsync(new(
                "quality.inspection.decide",
                nameof(QualityInspection),
                id.ToString(),
                "Failed",
                "quality",
                FailureReason: Clean(exception.Message, 2000),
                NewValues: new
                {
                    request.IdempotencyKey,
                    request.Decision,
                    request.LineIds,
                    request.ReasonCode,
                    request.QuantityDecisions,
                    request.Dispositions,
                    request.ControlQuantities,
                    request.QuarantineLocationId,
                    request.WarehouseTransferDocumentSeriesId
                }), ct);
            throw;
        }
        ErpPostingResult? posting = null;
        string? erpFailureMessage = null;
        try
        {
            posting = await erpPosting.PostIfEligibleAsync(goodsReceiptId, actor, ct);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The quality decision already committed. A Netsis/token failure must not
            // make the decide HTTP call look like the inspection is still open.
            erpFailureMessage = Clean(exception.Message, 1000);
        }
        var receipt = await uow.Repository<GoodsReceiptHeader>().Query()
            .AsNoTracking()
            .SingleAsync(x => x.Id == goodsReceiptId, ct);
        return BuildDecisionResult(receipt, posting, erpFailureMessage);
    }

    public async Task<ResolvedQualityPolicy> ResolveAsync(string branchCode,long stockId,string? stockGroupCode,CancellationToken ct=default)
    {
        var branch=NormalizeBranch(branchCode);
        var parameter=await Parameters.FirstOrDefaultAsync(x=>x.BranchCode==branch&&x.ParameterKey=="DEFAULT",false,ct)??Default(branch);
        var rule=await Rules.Query()
            .Where(x=>x.BranchCode==branch&&x.IsActive&&x.ScopeType==QualityRuleScopeTypes.Stock&&x.StockId==stockId)
            .OrderByDescending(x=>x.Id)
            .FirstOrDefaultAsync(ct);
        var normalizedGroup=Clean(stockGroupCode,50)?.ToUpperInvariant();
        if(rule is null&&!string.IsNullOrWhiteSpace(normalizedGroup))
            rule=await Rules.Query()
                .Where(x=>x.BranchCode==branch&&x.IsActive&&x.ScopeType==QualityRuleScopeTypes.StockGroup
                    &&x.StockGroupCode!=null&&x.StockGroupCode.Trim().ToUpper()==normalizedGroup)
                .OrderByDescending(x=>x.Id)
                .FirstOrDefaultAsync(ct);
        return rule is null ? new("NoRule",null,QualityInspectionMode.NoCheck,QualitySamplingMode.All,100,parameter.DefaultFailAction,false,false,false,false,null,parameter.HoldInventoryUntilDecision,parameter.BlockPutawayUntilDecision,parameter.BlockErpPostingUntilDecision)
            : new(rule.StockId.HasValue?"StockRule":"StockGroupRule",rule.Id,rule.InspectionMode,rule.SamplingMode,rule.SamplingValue,rule.FailAction,rule.AutoQuarantine,rule.RequireLot,rule.RequireSerial,rule.RequireExpiryDate,rule.MinimumRemainingShelfLifeDays,parameter.HoldInventoryUntilDecision,parameter.BlockPutawayUntilDecision,parameter.BlockErpPostingUntilDecision);
    }

    private async Task ApplyRule(QualityRule entity,QualityRuleUpsertRequest r,long? currentId,CancellationToken ct)
    {
        var branch=NormalizeBranch(r.BranchCode);
        var scope=QualityRuleScopeTypes.All.FirstOrDefault(x=>string.Equals(x,r.ScopeType?.Trim(),StringComparison.OrdinalIgnoreCase))
            ?? throw AppException.BadRequest("Geçersiz kalite kapsamı.");
        long? stockId=null;
        string? stockGroupCode=null;
        if(r.SamplingValue<=0||(r.SamplingMode==QualitySamplingMode.Percentage&&r.SamplingValue>100)||r.MinimumRemainingShelfLifeDays<0) throw AppException.BadRequest("Geçersiz örnekleme veya raf ömrü değeri.");
        if(scope==QualityRuleScopeTypes.Stock)
        {
            if(!r.StockId.HasValue||!await uow.Repository<StockEntity>().AnyAsync(x=>x.Id==r.StockId&&x.BranchCode==branch,ct))
                throw AppException.BadRequest("Stok bulunamadı.");
            stockId=r.StockId;
        }
        else
        {
            stockGroupCode=Clean(r.StockGroupCode,50)?.ToUpperInvariant();
            if(string.IsNullOrWhiteSpace(stockGroupCode))
                throw AppException.BadRequest("Stok grup kodu zorunludur.");
            if(!await uow.Repository<StockEntity>().AnyAsync(x=>x.BranchCode==branch&&x.GroupCode!=null&&x.GroupCode.Trim().ToUpper()==stockGroupCode,ct))
                throw AppException.BadRequest($"'{stockGroupCode}' stok grubu bulunamadı.");
        }
        if(await Rules.AnyAsync(x=>x.Id!=currentId&&x.BranchCode==branch&&x.ScopeType==scope&&x.StockId==stockId
            &&(stockGroupCode==null?x.StockGroupCode==null:x.StockGroupCode!=null&&x.StockGroupCode.Trim().ToUpper()==stockGroupCode)&&x.IsActive,ct))
            throw AppException.Conflict("Bu kapsam için aktif kalite kuralı zaten var.");
        entity.BranchCode=branch; entity.ScopeType=scope; entity.StockId=stockId; entity.StockGroupCode=stockGroupCode;
        entity.InspectionMode=r.InspectionMode; entity.SamplingMode=r.SamplingMode; entity.SamplingValue=r.SamplingValue; entity.FailAction=r.FailAction; entity.AutoQuarantine=r.AutoQuarantine; entity.RequireLot=r.RequireLot; entity.RequireSerial=r.RequireSerial; entity.RequireExpiryDate=r.RequireExpiryDate; entity.MinimumRemainingShelfLifeDays=r.MinimumRemainingShelfLifeDays; entity.IsActive=r.IsActive; entity.Description=Clean(r.Description,500);
    }
    private async Task ValidateLocations(
        UpdateQualityParameterRequest request,
        IReadOnlyCollection<QualityQuarantineDestinationRequest> destinations,
        IReadOnlyCollection<QualityWarehouseRouteRequest> warehouseRoutes,
        long? defaultQuarantineLocationId,
        string branch,
        CancellationToken ct)
    {
        var ids = new long?[]
            {
                request.DefaultQualityLocationId,
                request.DefaultAcceptedLocationId,
                defaultQuarantineLocationId,
                request.DefaultRejectLocationId
            }
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .Concat(destinations.Select(destination => destination.LocationId))
            .Concat(warehouseRoutes.SelectMany(RouteLocationIds))
            .Distinct()
            .ToArray();
        var locations = await uow.Repository<WarehouseLocation>().Query()
            .Where(location => ids.Contains(location.Id))
            .ToDictionaryAsync(location => location.Id, ct);
        if (locations.Count != ids.Length
            || locations.Values.Any(location => !location.IsActive || location.BranchCode != branch))
            throw AppException.BadRequest("Kalite lokasyonları aktif ve aynı şubede olmalıdır.");

        await ValidateWarehouseRoutesAsync(warehouseRoutes, locations, branch, ct);

        var quarantineIds = destinations.Select(destination => destination.LocationId)
            .Append(request.DefaultRejectLocationId ?? 0)
            .Where(id => id > 0)
            .Distinct();
        if (quarantineIds.Any(id => !locations[id].IsQuarantine))
            throw AppException.BadRequest("Karantina ve ret hedefleri karantina tipi raf olmalıdır.");
        if (request.DefaultQualityLocationId.HasValue
            && locations[request.DefaultQualityLocationId.Value].IsPickable)
            throw AppException.BadRequest("Kalite bekleme rafı toplama işlemine açık olamaz.");
        if (request.DefaultAcceptedLocationId.HasValue
            && (!locations[request.DefaultAcceptedLocationId.Value].IsPutaway
                || locations[request.DefaultAcceptedLocationId.Value].IsQuarantine))
            throw AppException.BadRequest("Varsayılan kabul hedefi yerleştirmeye açık ve karantina dışı bir raf olmalıdır.");
    }

    private async Task ValidateWarehouseRoutesAsync(
        IReadOnlyCollection<QualityWarehouseRouteRequest> warehouseRoutes,
        IReadOnlyDictionary<long, WarehouseLocation> locations,
        string branch,
        CancellationToken ct)
    {
        if (warehouseRoutes.Count == 0)
            return;
        var sourceWarehouseIds = warehouseRoutes.Select(route => route.SourceWarehouseId).Distinct().ToArray();
        var sourceWarehouses = await uow.Repository<WarehouseEntity>().Query()
            .Where(warehouse => sourceWarehouseIds.Contains(warehouse.Id))
            .ToDictionaryAsync(warehouse => warehouse.Id, ct);
        if (sourceWarehouses.Count != sourceWarehouseIds.Length
            || sourceWarehouses.Values.Any(warehouse => !string.Equals(warehouse.BranchCode, branch, StringComparison.OrdinalIgnoreCase)))
            throw AppException.BadRequest("Kaynak kalite depoları aktif ve aynı şubede olmalıdır.");

        foreach (var route in warehouseRoutes)
        {
            if (route.QualityLocationId.HasValue)
            {
                var waiting = locations[route.QualityLocationId.Value];
                if (waiting.WarehouseId != route.SourceWarehouseId)
                    throw AppException.BadRequest("Kalite bekleme rafı kaynak deponun içinde olmalıdır.");
                if (waiting.IsPickable)
                    throw AppException.BadRequest("Depo bazlı kalite bekleme rafı toplama işlemine açık olamaz.");
            }
            if (route.AcceptedLocationId.HasValue)
            {
                var accepted = locations[route.AcceptedLocationId.Value];
                if (!accepted.IsPutaway || accepted.IsQuarantine)
                    throw AppException.BadRequest("Depo bazlı kabul hedefi yerleştirmeye açık ve karantina dışı olmalıdır.");
            }
            foreach (var quarantineId in new[] { route.QuarantineLocationId, route.RejectLocationId }.Where(id => id.HasValue))
                if (!locations[quarantineId!.Value].IsQuarantine)
                    throw AppException.BadRequest("Depo bazlı karantina ve ret hedefleri karantina tipi raf olmalıdır.");
        }
    }

    private static IReadOnlyList<QualityWarehouseRouteRequest> NormalizeWarehouseRouteRequests(
        IReadOnlyCollection<QualityWarehouseRouteRequest> requested)
    {
        if (requested.Count > 250)
            throw AppException.BadRequest("En fazla 250 depo bazlı kalite rotası tanımlanabilir.");
        if (requested.Any(route => route.SourceWarehouseId <= 0))
            throw AppException.BadRequest("Her kalite rotası için kaynak depo zorunludur.");
        if (requested.GroupBy(route => route.SourceWarehouseId).Any(group => group.Count() > 1))
            throw AppException.BadRequest("Aynı kaynak depo için birden fazla kalite rotası tanımlanamaz.");
        if (requested.Any(route => RouteLocationIds(route).Count == 0))
            throw AppException.BadRequest("Kalite rotasında en az bir bekleme, kabul, karantina veya ret rafı seçilmelidir.");
        return requested.OrderBy(route => route.SourceWarehouseId).ToArray();
    }

    private static IReadOnlyList<long> RouteLocationIds(QualityWarehouseRouteRequest route) =>
        new long?[] { route.QualityLocationId, route.AcceptedLocationId, route.QuarantineLocationId, route.RejectLocationId }
            .Where(id => id.HasValue).Select(id => id!.Value).Distinct().ToArray();

    private static IReadOnlyList<QualityQuarantineDestinationRequest> NormalizeQuarantineDestinationRequests(
        UpdateQualityParameterRequest request)
    {
        var requested = request.QuarantineDestinations;
        if (requested is null)
            return request.DefaultQuarantineLocationId.HasValue
                ? [new(request.DefaultQuarantineLocationId.Value, 100)]
                : [];
        if (requested.Count > 50)
            throw AppException.BadRequest("En fazla 50 aktif karantina rafı tanımlanabilir.");
        if (requested.Any(destination => destination.LocationId <= 0
            || destination.Priority is < 1 or > 9999))
            throw AppException.BadRequest("Karantina rafı ve 1-9999 arasındaki öncelik değeri zorunludur.");
        if (requested.GroupBy(destination => destination.LocationId).Any(group => group.Count() > 1))
            throw AppException.BadRequest("Aynı karantina rafı birden fazla kez tanımlanamaz.");
        return requested.OrderBy(destination => destination.Priority)
            .ThenBy(destination => destination.LocationId)
            .ToArray();
    }

    private static long? ResolveDefaultQuarantineLocationId(
        UpdateQualityParameterRequest request,
        IReadOnlyCollection<QualityQuarantineDestinationRequest> destinations)
    {
        var active = destinations.Where(destination => destination.IsActive).ToArray();
        if (active.Length == 0)
        {
            if (destinations.Count > 0)
                throw AppException.BadRequest("En az bir karantina rafı aktif olmalıdır.");
            return null;
        }
        if (request.DefaultQuarantineLocationId.HasValue)
        {
            if (!active.Any(destination => destination.LocationId == request.DefaultQuarantineLocationId.Value))
                throw AppException.BadRequest("Varsayılan karantina rafı aktif karantina hedefleri arasında olmalıdır.");
            return request.DefaultQuarantineLocationId;
        }
        return active.OrderBy(destination => destination.Priority)
            .ThenBy(destination => destination.LocationId)
            .First().LocationId;
    }

    private async Task SynchronizeQuarantineDestinationsAsync(
        QualityParameter parameter,
        IReadOnlyCollection<QualityQuarantineDestinationRequest> requested,
        long actor,
        CancellationToken ct)
    {
        var existing = await QuarantineDestinations.Query(true)
            .Where(destination => destination.QualityParameterId == parameter.Id)
            .ToListAsync(ct);
        var requestedByLocation = requested.ToDictionary(destination => destination.LocationId);
        var now = DateTime.UtcNow;
        foreach (var destination in existing)
        {
            if (!requestedByLocation.TryGetValue(destination.LocationId, out var update))
            {
                destination.IsActive = false;
                destination.IsDeleted = true;
                destination.DeletedBy = actor;
                destination.DeletedDate = now;
                continue;
            }
            destination.Priority = update.Priority;
            destination.IsActive = update.IsActive;
            destination.UpdatedBy = actor;
            destination.UpdatedDate = now;
            requestedByLocation.Remove(destination.LocationId);
        }
        foreach (var destination in requestedByLocation.Values)
        {
            await QuarantineDestinations.AddAsync(new QualityQuarantineDestination
            {
                BranchCode = parameter.BranchCode,
                QualityParameterId = parameter.Id,
                LocationId = destination.LocationId,
                Priority = destination.Priority,
                IsActive = destination.IsActive,
                CreatedBy = actor,
                CreatedDate = now
            }, ct);
        }
    }

    private async Task<IReadOnlyList<QualityQuarantineDestinationDto>> GetQuarantineDestinationsAsync(
        QualityParameter parameter,
        CancellationToken ct)
    {
        var configured = parameter.Id <= 0
            ? []
            : await (from destination in QuarantineDestinations.Query()
                     join location in uow.Repository<WarehouseLocation>().Query()
                         on destination.LocationId equals location.Id
                     join warehouse in uow.Repository<WarehouseEntity>().Query()
                         on location.WarehouseId equals warehouse.Id
                     where destination.QualityParameterId == parameter.Id
                         && destination.IsActive
                         && location.IsActive
                         && location.IsQuarantine
                     orderby destination.Priority, warehouse.WarehouseCode, location.Code
                     select new QualityQuarantineDestinationDto(
                         destination.Id,
                         location.Id,
                         warehouse.Id,
                         warehouse.WarehouseCode,
                         warehouse.WarehouseName,
                         location.Code,
                         location.Name,
                         destination.Priority,
                         location.Id == parameter.DefaultQuarantineLocationId,
                         destination.IsActive)).ToListAsync(ct);
        if (configured.Count > 0 || !parameter.DefaultQuarantineLocationId.HasValue)
            return configured;

        var legacy = await (from location in uow.Repository<WarehouseLocation>().Query()
                            join warehouse in uow.Repository<WarehouseEntity>().Query()
                                on location.WarehouseId equals warehouse.Id
                            where location.Id == parameter.DefaultQuarantineLocationId.Value
                                && location.IsActive
                                && location.IsQuarantine
                            select new QualityQuarantineDestinationDto(
                                0,
                                location.Id,
                                warehouse.Id,
                                warehouse.WarehouseCode,
                                warehouse.WarehouseName,
                                location.Code,
                                location.Name,
                                100,
                                true,
                                location.IsActive)).ToListAsync(ct);
        return legacy;
    }

    private async Task SynchronizeWarehouseRoutesAsync(
        QualityParameter parameter,
        IReadOnlyCollection<QualityWarehouseRouteRequest> requested,
        long actor,
        CancellationToken ct)
    {
        var existing = await WarehouseRoutes.Query(true)
            .Where(route => route.QualityParameterId == parameter.Id)
            .ToListAsync(ct);
        var requestedByWarehouse = requested.ToDictionary(route => route.SourceWarehouseId);
        var now = DateTime.UtcNow;
        foreach (var route in existing)
        {
            if (!requestedByWarehouse.TryGetValue(route.SourceWarehouseId, out var update))
            {
                route.IsActive = false;
                route.IsDeleted = true;
                route.DeletedBy = actor;
                route.DeletedDate = now;
                continue;
            }
            route.QualityLocationId = update.QualityLocationId;
            route.AcceptedLocationId = update.AcceptedLocationId;
            route.QuarantineLocationId = update.QuarantineLocationId;
            route.RejectLocationId = update.RejectLocationId;
            route.IsActive = update.IsActive;
            route.UpdatedBy = actor;
            route.UpdatedDate = now;
            requestedByWarehouse.Remove(route.SourceWarehouseId);
        }
        foreach (var route in requestedByWarehouse.Values)
        {
            await WarehouseRoutes.AddAsync(new QualityWarehouseRoute
            {
                BranchCode = parameter.BranchCode,
                QualityParameterId = parameter.Id,
                SourceWarehouseId = route.SourceWarehouseId,
                QualityLocationId = route.QualityLocationId,
                AcceptedLocationId = route.AcceptedLocationId,
                QuarantineLocationId = route.QuarantineLocationId,
                RejectLocationId = route.RejectLocationId,
                IsActive = route.IsActive,
                CreatedBy = actor,
                CreatedDate = now
            }, ct);
        }
    }

    private async Task<IReadOnlyList<QualityWarehouseRouteDto>> GetWarehouseRoutesAsync(
        QualityParameter parameter,
        CancellationToken ct)
    {
        if (parameter.Id <= 0)
            return [];
        var routes = await WarehouseRoutes.Query()
            .Where(route => route.QualityParameterId == parameter.Id)
            .OrderBy(route => route.SourceWarehouseId)
            .ToListAsync(ct);
        if (routes.Count == 0)
            return [];
        var warehouseIds = routes.Select(route => route.SourceWarehouseId).Distinct().ToArray();
        var warehouses = await uow.Repository<WarehouseEntity>().Query()
            .Where(warehouse => warehouseIds.Contains(warehouse.Id))
            .ToDictionaryAsync(warehouse => warehouse.Id, ct);
        var locationIds = routes.SelectMany(route => new long?[]
            {
                route.QualityLocationId,
                route.AcceptedLocationId,
                route.QuarantineLocationId,
                route.RejectLocationId
            })
            .Where(id => id.HasValue).Select(id => id!.Value).Distinct().ToArray();
        var destinationRows = await (from location in uow.Repository<WarehouseLocation>().Query()
                                     join warehouse in uow.Repository<WarehouseEntity>().Query()
                                         on location.WarehouseId equals warehouse.Id
                                     where locationIds.Contains(location.Id)
                                     select new QualityDecisionDestinationDto(
                                         location.Id,
                                         warehouse.Id,
                                         warehouse.WarehouseCode,
                                         warehouse.WarehouseName,
                                         location.Code,
                                         location.Name)).ToListAsync(ct);
        var destinations = destinationRows.ToDictionary(destination => destination.LocationId);
        QualityDecisionDestinationDto? Destination(long? id) =>
            id.HasValue && destinations.TryGetValue(id.Value, out var destination) ? destination : null;
        return routes.Where(route => warehouses.ContainsKey(route.SourceWarehouseId))
            .Select(route => new QualityWarehouseRouteDto(
                route.Id,
                route.SourceWarehouseId,
                warehouses[route.SourceWarehouseId].WarehouseCode,
                warehouses[route.SourceWarehouseId].WarehouseName,
                route.QualityLocationId,
                route.AcceptedLocationId,
                route.QuarantineLocationId,
                route.RejectLocationId,
                Destination(route.QualityLocationId),
                Destination(route.AcceptedLocationId),
                Destination(route.QuarantineLocationId),
                Destination(route.RejectLocationId),
                route.IsActive))
            .OrderBy(route => route.SourceWarehouseCode)
            .ToArray();
    }

    private async Task<QualityDecisionDestinationDto?> GetDecisionDestinationAsync(
        long? locationId,
        CancellationToken ct)
    {
        if (!locationId.HasValue)
            return null;
        return await (from location in uow.Repository<WarehouseLocation>().Query()
                      join warehouse in uow.Repository<WarehouseEntity>().Query()
                          on location.WarehouseId equals warehouse.Id
                      where location.Id == locationId.Value && location.IsActive
                      select new QualityDecisionDestinationDto(
                          location.Id,
                          warehouse.Id,
                          warehouse.WarehouseCode,
                          warehouse.WarehouseName,
                          location.Code,
                          location.Name)).FirstOrDefaultAsync(ct);
    }

    private async Task<IReadOnlyDictionary<long, QualityWarehouseRoute>> GetActiveWarehouseRoutesAsync(
        QualityParameter parameter,
        CancellationToken ct)
    {
        if (parameter.Id <= 0)
            return new Dictionary<long, QualityWarehouseRoute>();
        return await WarehouseRoutes.Query()
            .Where(route => route.QualityParameterId == parameter.Id && route.IsActive)
            .ToDictionaryAsync(route => route.SourceWarehouseId, ct);
    }

    private async Task<IReadOnlyList<QualityQuarantineDestinationDto>> MergeRouteQuarantineDestinationsAsync(
        IReadOnlyList<QualityQuarantineDestinationDto> configured,
        IReadOnlyDictionary<long, QualityWarehouseRoute> routes,
        CancellationToken ct)
    {
        var existingIds = configured.Select(destination => destination.LocationId).ToHashSet();
        var routeLocationIds = routes.Values.Select(route => route.QuarantineLocationId)
            .Where(id => id.HasValue && !existingIds.Contains(id.Value))
            .Select(id => id!.Value).Distinct().ToArray();
        if (routeLocationIds.Length == 0)
            return configured;
        var additional = await (from location in uow.Repository<WarehouseLocation>().Query()
                                join warehouse in uow.Repository<WarehouseEntity>().Query()
                                    on location.WarehouseId equals warehouse.Id
                                where routeLocationIds.Contains(location.Id) && location.IsActive && location.IsQuarantine
                                select new QualityQuarantineDestinationDto(
                                    0,
                                    location.Id,
                                    warehouse.Id,
                                    warehouse.WarehouseCode,
                                    warehouse.WarehouseName,
                                    location.Code,
                                    location.Name,
                                    0,
                                    false,
                                    true)).ToListAsync(ct);
        return configured.Concat(additional)
            .GroupBy(destination => destination.LocationId)
            .Select(group => group.First())
            .OrderBy(destination => destination.Priority)
            .ThenBy(destination => destination.WarehouseCode)
            .ThenBy(destination => destination.LocationCode)
            .ToArray();
    }

    internal static QualityWarehouseRouteDefaults ResolveWarehouseRouteDefaults(
        QualityParameter parameter,
        IReadOnlyDictionary<long, QualityWarehouseRoute> routes,
        long sourceWarehouseId)
    {
        routes.TryGetValue(sourceWarehouseId, out var route);
        return new QualityWarehouseRouteDefaults(
            route?.QualityLocationId ?? parameter.DefaultQualityLocationId,
            route?.AcceptedLocationId ?? parameter.DefaultAcceptedLocationId,
            route?.QuarantineLocationId ?? parameter.DefaultQuarantineLocationId,
            route?.RejectLocationId ?? parameter.DefaultRejectLocationId);
    }

    internal static QualityWarehouseRouteDefaults ResolveInspectionWarehouseRoute(
        IReadOnlyDictionary<long, QualityWarehouseRoute> routes,
        long sourceWarehouseId)
    {
        routes.TryGetValue(sourceWarehouseId, out var route);
        return new QualityWarehouseRouteDefaults(
            route?.QualityLocationId,
            route?.AcceptedLocationId,
            route?.QuarantineLocationId,
            route?.RejectLocationId);
    }

    internal static long? ResolveInspectionQuarantineLocationId(
        IReadOnlyDictionary<long, QualityWarehouseRoute> routes,
        IReadOnlyCollection<QualityQuarantineDestinationDto> sectionDestinations,
        long sourceWarehouseId)
    {
        var matrixLocationId = ResolveInspectionWarehouseRoute(routes, sourceWarehouseId).QuarantineLocationId;
        if (matrixLocationId.HasValue)
            return matrixLocationId;

        var active = sectionDestinations.Where(destination => destination.IsActive).ToArray();
        if (active.Length == 0)
            return null;
        return active
            .OrderByDescending(destination => destination.WarehouseId == sourceWarehouseId)
            .ThenByDescending(destination => destination.IsDefault)
            .ThenBy(destination => destination.Priority)
            .ThenBy(destination => destination.WarehouseCode)
            .ThenBy(destination => destination.LocationCode)
            .First().LocationId;
    }

    internal static long? ResolveInspectionDecisionTargetLocationId(
        QualityDecisionPart part,
        long sourceWarehouseId,
        IReadOnlyDictionary<long, QualityWarehouseRoute> warehouseRoutes,
        IReadOnlyCollection<QualityQuarantineDestinationDto> sectionQuarantineDestinations,
        long? defaultReceivingLocationId,
        long? defaultPutawayLocationId,
        long? headerReceivingLocationId)
    {
        if (part.TargetLocationId.HasValue)
        {
            if (part.Decision == QualityDecision.Quarantined)
                return ResolveQuarantineDestination(
                    sectionQuarantineDestinations,
                    part.TargetLocationId,
                    sourceWarehouseId).LocationId;
            return part.TargetLocationId;
        }

        var route = ResolveInspectionWarehouseRoute(warehouseRoutes, sourceWarehouseId);
        return part.Decision switch
        {
            QualityDecision.Accepted => ResolveAcceptedLocationId(
                defaultReceivingLocationId,
                defaultPutawayLocationId,
                headerReceivingLocationId),
            QualityDecision.Rejected => route.RejectLocationId,
            QualityDecision.Quarantined => ResolveInspectionQuarantineLocationId(
                warehouseRoutes,
                sectionQuarantineDestinations,
                sourceWarehouseId),
            _ => null
        };
    }

    internal static IReadOnlyList<QualityQuarantineDestinationDto> MarkInspectionQuarantineDefaults(
        IReadOnlyList<QualityQuarantineDestinationDto> destinations,
        long sourceWarehouseId,
        long? matrixQuarantineLocationId)
    {
        if (destinations.Count == 0)
            return destinations;

        var defaultLocationId = matrixQuarantineLocationId
            ?? destinations
                .Where(destination => destination.IsActive)
                .OrderByDescending(destination => destination.WarehouseId == sourceWarehouseId)
                .ThenByDescending(destination => destination.IsDefault)
                .ThenBy(destination => destination.Priority)
                .ThenBy(destination => destination.WarehouseCode)
                .ThenBy(destination => destination.LocationCode)
                .Select(destination => (long?)destination.LocationId)
                .FirstOrDefault();

        return destinations
            .Select(destination => destination with
            {
                IsDefault = defaultLocationId.HasValue && destination.LocationId == defaultLocationId.Value
            })
            .OrderByDescending(destination => destination.IsDefault)
            .ThenByDescending(destination => destination.WarehouseId == sourceWarehouseId)
            .ThenBy(destination => destination.Priority)
            .ThenBy(destination => destination.WarehouseCode)
            .ThenBy(destination => destination.LocationCode)
            .ToArray();
    }

    private async Task<IReadOnlyList<QualityQuarantineDestinationDto>> BuildInspectionQuarantineDestinationsAsync(
        QualityParameter parameter,
        IReadOnlyDictionary<long, QualityWarehouseRoute> routes,
        IEnumerable<long> sourceWarehouseIds,
        CancellationToken ct)
    {
        var section = (await GetQuarantineDestinationsAsync(parameter, ct)).ToList();
        var existingIds = section.Select(destination => destination.LocationId).ToHashSet();
        var extraLocationIds = sourceWarehouseIds
            .Select(warehouseId => ResolveInspectionWarehouseRoute(routes, warehouseId).QuarantineLocationId)
            .Where(locationId => locationId.HasValue && !existingIds.Contains(locationId.Value))
            .Select(locationId => locationId!.Value)
            .Distinct()
            .ToArray();
        if (extraLocationIds.Length == 0)
            return section;

        var additional = await (from location in uow.Repository<WarehouseLocation>().Query()
                                join warehouse in uow.Repository<WarehouseEntity>().Query()
                                    on location.WarehouseId equals warehouse.Id
                                where extraLocationIds.Contains(location.Id)
                                    && location.IsActive
                                    && location.IsQuarantine
                                select new QualityQuarantineDestinationDto(
                                    0,
                                    location.Id,
                                    warehouse.Id,
                                    warehouse.WarehouseCode,
                                    warehouse.WarehouseName,
                                    location.Code,
                                    location.Name,
                                    0,
                                    false,
                                    true)).ToListAsync(ct);
        return section.Concat(additional)
            .GroupBy(destination => destination.LocationId)
            .Select(group => group.First())
            .ToArray();
    }

    private void EnsureInspectionDecisionDestinations(
        IReadOnlyList<QualityDecisionPart> decisionParts,
        IReadOnlyDictionary<long, GoodsReceiptLine> receiptLines,
        IReadOnlyDictionary<long, QualityWarehouseRoute> warehouseRoutes,
        IReadOnlyCollection<QualityQuarantineDestinationDto> sectionQuarantineDestinations,
        long headerReceivingLocationId)
    {
        foreach (var part in decisionParts)
        {
            if (part.TargetLocationId.HasValue
                || !part.Line.GoodsReceiptLineId.HasValue
                || !receiptLines.TryGetValue(part.Line.GoodsReceiptLineId.Value, out var receiptLine))
                continue;

            var warehouseId = receiptLine.TargetWarehouseId;
            var route = ResolveInspectionWarehouseRoute(warehouseRoutes, warehouseId);
            switch (part.Decision)
            {
                case QualityDecision.Accepted when !ResolveAcceptedLocationId(
                    receiptLine.DefaultReceivingLocationId,
                    receiptLine.DefaultPutawayLocationId,
                    headerReceivingLocationId).HasValue:
                    throw AppException.Conflict(Message(QualityMessageKeys.InspectionWarehouseAcceptedLocationMissing));
                case QualityDecision.Rejected when !route.RejectLocationId.HasValue:
                    throw AppException.Conflict(Message(QualityMessageKeys.InspectionWarehouseRejectLocationMissing));
                case QualityDecision.Quarantined when !ResolveInspectionQuarantineLocationId(
                    warehouseRoutes,
                    sectionQuarantineDestinations,
                    warehouseId).HasValue:
                    throw AppException.Conflict(Message(QualityMessageKeys.InspectionWarehouseQuarantineLocationMissing));
            }
        }
    }

    private static string InspectionDestinationMessageKey(QualityDecision decision) =>
        decision switch
        {
            QualityDecision.Accepted => QualityMessageKeys.InspectionWarehouseAcceptedLocationMissing,
            QualityDecision.Rejected => QualityMessageKeys.InspectionWarehouseRejectLocationMissing,
            QualityDecision.Quarantined => QualityMessageKeys.InspectionWarehouseQuarantineLocationMissing,
            _ => QualityMessageKeys.InspectionWarehouseQualityHoldLocationMissing
        };

    private static QualityParameter Default(string branch)=>new(){BranchCode=branch,ParameterKey="DEFAULT"};
    private async Task<QualityParameterDto> MapParameterAsync(QualityParameter x, CancellationToken ct) => new(
        x.Id, x.BranchCode, x.AutoCreateInspectionOnReceipt, x.DefaultInspectionMode, x.DefaultFailAction,
        x.HoldInventoryUntilDecision, x.BlockPutawayUntilDecision, x.BlockErpPostingUntilDecision,
        x.RequireManagerApprovalForRelease, x.AllowPartialDecision, x.AllowDirectReceiptWhenNoRule,
        x.BlockReceiptWhenLotMissing, x.BlockReceiptWhenSerialMissing, x.BlockReceiptWhenExpiryMissing,
        x.DefaultQualityLocationId, x.DefaultAcceptedLocationId, x.DefaultQuarantineLocationId,
        x.DefaultRejectLocationId, await GetQuarantineDestinationsAsync(x, ct),
        await GetWarehouseRoutesAsync(x, ct), x.UpdatedBy, x.UpdatedDate);
    private static object Snapshot(QualityRule x)=>new{x.Id,x.BranchCode,x.ScopeType,x.StockId,x.StockGroupCode,x.InspectionMode,x.SamplingMode,x.SamplingValue,x.FailAction,x.AutoQuarantine,x.RequireLot,x.RequireSerial,x.RequireExpiryDate,x.MinimumRemainingShelfLifeDays,x.IsActive,x.Description};
    internal static QualityDecisionState ResolveDecisionState(
        IEnumerable<QualityDecision> decisions,
        bool releasesQuarantine)
    {
        var values = decisions.ToArray();
        var pending = values.Count(x => x is QualityDecision.Pending or QualityDecision.Hold);
        var accepted = values.Count(x => x == QualityDecision.Accepted);
        var quarantined = values.Count(x => x == QualityDecision.Quarantined);
        var failed = values.Count(x => x is QualityDecision.Rejected or QualityDecision.Returned);
        var inspectionStatus = pending > 0 || accepted > 0 && quarantined > 0
            ? QualityInspectionStatus.PartiallyDecided
            : failed > 0
                ? QualityInspectionStatus.Failed
                : quarantined > 0
                    ? QualityInspectionStatus.Quarantined
                    : releasesQuarantine
                        ? QualityInspectionStatus.Released
                        : QualityInspectionStatus.Passed;
        var receiptStatus = pending > 0
            ? OperationQualityStatus.PartiallyCompleted
            : failed > 0
                ? OperationQualityStatus.Failed
                : quarantined > 0
                    ? OperationQualityStatus.InProgress
                    : OperationQualityStatus.Passed;
        return new(inspectionStatus, receiptStatus, pending == 0);
    }
    internal static QualityDecisionState ResolveDecisionState(
        IEnumerable<QualityInspectionLine> lines,
        bool releasesQuarantine)
    {
        var values = lines.ToArray();
        var pending = values.Any(x => DecidedQuantity(x) < x.Quantity);
        var failed = values.Any(x => x.RejectedQuantity > 0);
        var quarantined = values.Any(x => x.QuarantineQuantity > 0);
        var inspectionStatus = pending
            ? QualityInspectionStatus.PartiallyDecided
            : quarantined
                ? QualityInspectionStatus.Quarantined
                : failed
                    ? QualityInspectionStatus.Failed
                    : releasesQuarantine
                        ? QualityInspectionStatus.Released
                        : QualityInspectionStatus.Passed;
        var receiptStatus = pending
            ? OperationQualityStatus.PartiallyCompleted
            : quarantined
                ? OperationQualityStatus.InProgress
                : failed
                    ? OperationQualityStatus.Failed
                    : OperationQualityStatus.Passed;
        return new(inspectionStatus, receiptStatus, !pending);
    }
    internal static IReadOnlyList<QualityDecisionPart> BuildDecisionParts(
        IReadOnlyList<QualityInspectionLine> selected,
        IReadOnlyList<QualityInspectionDispositionRequest>? dispositions,
        IReadOnlyDictionary<long, QualityInspectionQuantityDecisionRequest>? quantityDecisions,
        QualityDecision fallbackDecision,
        long? fallbackQuarantineLocationId = null,
        long? fallbackDecisionCodeId = null)
    {
        if (dispositions is not { Count: > 0 })
            return BuildDecisionParts(
                selected,
                quantityDecisions,
                fallbackDecision,
                fallbackQuarantineLocationId,
                fallbackDecisionCodeId);

        var selectedIds = selected.Select(line => line.Id).ToHashSet();
        if (dispositions.Any(part => !selectedIds.Contains(part.LineId)))
            throw AppException.BadRequest("Kalite dağıtım planında seçili olmayan bir satır bulunuyor.");

        var result = new List<QualityDecisionPart>();
        var sequence = 0;
        foreach (var line in selected)
        {
            var actionable = ActionableQuantity(line);
            if (actionable <= 0)
                throw AppException.Conflict($"'{line.StockCodeSnapshot}' kalite satırında karar bekleyen miktar bulunmuyor.");

            var lineParts = dispositions.Where(part => part.LineId == line.Id).ToArray();
            if (lineParts.Length == 0)
                throw AppException.BadRequest($"'{line.StockCodeSnapshot}' için en az bir kalite dağıtım satırı girilmelidir.");
            if (lineParts.Any(part => part.Quantity <= 0))
                throw AppException.BadRequest("Kalite dağıtım miktarı sıfırdan büyük olmalıdır.");
            if (lineParts.Any(part => part.Decision is QualityDecision.Pending or QualityDecision.Hold))
                throw AppException.BadRequest("Dağıtım kararı kabul, ret, karantina veya tedarikçiye iade olmalıdır.");

            var returned = lineParts.Where(part => part.Decision == QualityDecision.Returned).ToArray();
            if (returned.Length > 0 && (lineParts.Length != 1 || Math.Abs(returned[0].Quantity - actionable) > 0.000001m))
                throw AppException.BadRequest("Tedarikçiye iade kararı başka kararlarla bölünemez ve bekleyen miktarın tamamını kapsamalıdır.");

            var allocated = lineParts.Sum(part => part.Quantity);
            if (Math.Abs(allocated - actionable) > 0.000001m)
                throw AppException.BadRequest(
                    $"'{line.StockCodeSnapshot}' için dağıtılan toplam {allocated:0.######}, karar bekleyen {actionable:0.######} miktara eşit olmalıdır.");

            foreach (var part in lineParts)
            {
                result.Add(new QualityDecisionPart(
                    line,
                    part.Decision,
                    part.Quantity,
                    part.TargetLocationId,
                    part.DecisionCodeId,
                    Clean(part.ReasonCode, 100),
                    Clean(part.Note, 1000),
                    ++sequence));
            }
        }
        return result;
    }

    internal static IReadOnlyList<QualityDecisionPart> BuildDecisionParts(
        IReadOnlyList<QualityInspectionLine> selected,
        IReadOnlyDictionary<long, QualityInspectionQuantityDecisionRequest>? quantityDecisions,
        QualityDecision fallbackDecision,
        long? fallbackQuarantineLocationId = null,
        long? fallbackDecisionCodeId = null)
    {
        var result = new List<QualityDecisionPart>();
        foreach (var line in selected)
        {
            var actionable = ActionableQuantity(line);
            if (actionable <= 0)
                throw AppException.Conflict($"'{line.StockCodeSnapshot}' kalite satırında karar bekleyen miktar bulunmuyor.");
            if (quantityDecisions is null)
            {
                result.Add(new(
                    line,
                    fallbackDecision,
                    actionable,
                    fallbackDecision == QualityDecision.Quarantined ? fallbackQuarantineLocationId : null,
                    fallbackDecisionCodeId));
                continue;
            }

            var allocation = quantityDecisions[line.Id];
            if (allocation.AcceptedQuantity < 0 || allocation.RejectedQuantity < 0 || allocation.QuarantineQuantity < 0)
                throw AppException.BadRequest("Kalite karar miktarları negatif olamaz.");
            var allocated = allocation.AcceptedQuantity + allocation.RejectedQuantity + allocation.QuarantineQuantity;
            if (Math.Abs(allocated - actionable) > 0.000001m)
                throw AppException.BadRequest(
                    $"'{line.StockCodeSnapshot}' için onay, ret ve karantina toplamı karar bekleyen {actionable:0.######} miktara eşit olmalıdır.");
            if (allocation.AcceptedQuantity > 0) result.Add(new(line, QualityDecision.Accepted, allocation.AcceptedQuantity, null, fallbackDecisionCodeId));
            if (allocation.RejectedQuantity > 0) result.Add(new(line, QualityDecision.Rejected, allocation.RejectedQuantity, null, fallbackDecisionCodeId));
            if (allocation.QuarantineQuantity > 0) result.Add(new(
                line,
                QualityDecision.Quarantined,
                allocation.QuarantineQuantity,
                allocation.QuarantineLocationId ?? fallbackQuarantineLocationId,
                fallbackDecisionCodeId));
        }
        return result;
    }
    internal static void ApplyDecisionParts(
        QualityInspectionLine line,
        GoodsReceiptLine receiptLine,
        IReadOnlyList<QualityDecisionPart> parts,
        long actor,
        DateTimeOffset decidedAt,
        string? note,
        long? quarantineLocationId = null)
    {
        var sourceQuantity = ActionableQuantity(line);
        var accepted = parts.Where(x => x.Decision == QualityDecision.Accepted).Sum(x => x.Quantity);
        var rejected = parts.Where(x => x.Decision is QualityDecision.Rejected or QualityDecision.Returned).Sum(x => x.Quantity);
        var quarantined = parts.Where(x => x.Decision == QualityDecision.Quarantined).Sum(x => x.Quantity);
        var heldSource = Math.Min(receiptLine.QuarantineQuantity, sourceQuantity);
        receiptLine.QuarantineQuantity -= heldSource;
        var availableSource = sourceQuantity - heldSource;
        if (availableSource > 0)
            receiptLine.AcceptedQuantity = Math.Max(0, receiptLine.AcceptedQuantity - availableSource);
        receiptLine.AcceptedQuantity += accepted;
        receiptLine.RejectedQuantity += rejected;
        receiptLine.QuarantineQuantity += quarantined;
        line.AcceptedQuantity += accepted;
        line.RejectedQuantity += rejected;
        line.QuarantineQuantity = quarantined;
        line.QuarantineLocationId = quarantined > 0 ? quarantineLocationId : null;
        line.Decision = ResolveLineDecision(line);
        line.DecisionBy = actor;
        line.DecisionAtUtc = decidedAt;
        var decisionCodeIds = parts.Where(part => part.DecisionCodeId.HasValue)
            .Select(part => part.DecisionCodeId!.Value).Distinct().Take(2).ToArray();
        var reasonCodes = parts.Where(part => !string.IsNullOrWhiteSpace(part.ReasonCode))
            .Select(part => part.ReasonCode!).Distinct(StringComparer.OrdinalIgnoreCase).Take(2).ToArray();
        line.DecisionCodeId = decisionCodeIds.Length == 1 ? decisionCodeIds[0] : null;
        line.ReasonCode = reasonCodes.Length == 1 ? Clean(reasonCodes[0], 100) : null;
        line.ReasonNote = Clean(note, 1000);
    }

    private async Task<IReadOnlyList<QualityDecisionPart>> ResolveDecisionCodesAsync(
        string branchCode,
        IReadOnlyList<QualityDecisionPart> parts,
        long? fallbackDecisionCodeId,
        string? fallbackNote,
        CancellationToken ct)
    {
        var requestedIds = parts
            .Select(part => part.DecisionCodeId ?? fallbackDecisionCodeId)
            .Where(id => id.HasValue && id.Value > 0)
            .Select(id => id!.Value)
            .Distinct()
            .ToArray();
        var definitions = requestedIds.Length == 0
            ? new Dictionary<long, QualityDecisionCode>()
            : await DecisionCodes.Query()
                .Where(code => requestedIds.Contains(code.Id)
                    && code.BranchCode == branchCode
                    && code.IsActive)
                .ToDictionaryAsync(code => code.Id, ct);
        if (definitions.Count != requestedIds.Length)
            throw AppException.BadRequest("Seçilen kalite karar kodu aktif değil veya bu şubeye ait değil.");

        var result = new List<QualityDecisionPart>(parts.Count);
        foreach (var part in parts)
        {
            var decisionCodeId = part.DecisionCodeId ?? fallbackDecisionCodeId;
            if (!decisionCodeId.HasValue || decisionCodeId.Value <= 0)
            {
                if (RequiresDecisionCode(part.Decision))
                    throw AppException.BadRequest("Ret, karantina ve tedarikçiye iade kararlarında tanımlı karar kodu seçilmelidir.");
                result.Add(part with { DecisionCodeId = null, ReasonCode = null, Note = part.Note ?? Clean(fallbackNote, 1000) });
                continue;
            }

            var definition = definitions[decisionCodeId.Value];
            if (definition.ApplicableDecision.HasValue && definition.ApplicableDecision != part.Decision)
                throw AppException.BadRequest(
                    $"'{definition.Code}' karar kodu {part.Decision} kararı için kullanılamaz.");
            var note = Clean(part.Note ?? fallbackNote, 1000);
            if (definition.RequiresNote && string.IsNullOrWhiteSpace(note))
                throw AppException.BadRequest($"'{definition.Code}' karar kodu için açıklama zorunludur.");
            result.Add(part with
            {
                DecisionCodeId = definition.Id,
                ReasonCode = definition.Code,
                Note = note
            });
        }
        return result;
    }

    private static bool RequiresDecisionCode(QualityDecision decision) =>
        decision is QualityDecision.Rejected or QualityDecision.Quarantined or QualityDecision.Returned;
    private IReadOnlyDictionary<long, QualityInspectionControlSnapshot> ValidateControlQuantities(
        IReadOnlyCollection<QualityInspectionLine> selected,
        IReadOnlyList<QualityInspectionControlQuantityRequest>? requests)
    {
        var groups = requests?.GroupBy(request => request.LineId).ToArray() ?? [];
        if (groups.Length != selected.Count
            || groups.Any(group => group.Count() != 1)
            || groups.Any(group => selected.All(line => line.Id != group.Key)))
            throw AppException.BadRequest(Message(QualityMessageKeys.ControlQuantityRequired));

        var result = new Dictionary<long, QualityInspectionControlSnapshot>(selected.Count);
        foreach (var line in selected)
        {
            var inspected = groups.Single(group => group.Key == line.Id).Single().InspectedQuantity;
            var lotQuantity = line.Quantity;
            var required = RequiredControlQuantityForDecision(line);
            if (inspected < 0)
                throw AppException.BadRequest(Message(
                    QualityMessageKeys.ControlQuantityMustBePositive,
                    line.StockCodeSnapshot));
            var remainingInspectable = RemainingInspectableQuantity(line);
            inspected = NormalizeAdditionalControlQuantity(inspected, remainingInspectable);
            if (inspected - remainingInspectable > 0.000001m)
                throw AppException.BadRequest(Message(
                    QualityMessageKeys.ControlQuantityExceedsLot,
                    line.StockCodeSnapshot,
                    inspected,
                    remainingInspectable));
            if (required - inspected > 0.000001m)
                throw AppException.Conflict(Message(
                    QualityMessageKeys.ControlQuantityBelowMinimum,
                    line.StockCodeSnapshot,
                    inspected,
                    required));
            result[line.Id] = new QualityInspectionControlSnapshot(lotQuantity, required, inspected);
        }
        return result;
    }
    private static decimal ActionableQuantity(QualityInspectionLine line) =>
        line.Decision == QualityDecision.Quarantined
            ? line.QuarantineQuantity
            : Math.Max(0, line.Quantity - DecidedQuantity(line));
    internal static decimal RequiredControlQuantityForDecision(QualityInspectionLine line) =>
        Math.Max(0, Math.Min(line.SampleQuantity, line.Quantity) - line.InspectedQuantity);
    internal static decimal RemainingInspectableQuantity(QualityInspectionLine line) =>
        Math.Max(0, line.Quantity - line.InspectedQuantity);

    internal static decimal NormalizeAdditionalControlQuantity(decimal requested, decimal remainingInspectable) =>
        remainingInspectable <= 0.000001m ? 0 : requested;

    private IReadOnlyList<(QualityInspectionLine Line, decimal InspectedQuantity)> ResolveProgressControls(
        IEnumerable<QualityInspectionLine> lines,
        IReadOnlyList<QualityInspectionControlQuantityRequest>? requests)
    {
        if (requests is null || requests.Count == 0) return [];
        var byId = lines.ToDictionary(line => line.Id);
        var result = new List<(QualityInspectionLine Line, decimal InspectedQuantity)>();
        foreach (var request in requests)
        {
            if (!byId.TryGetValue(request.LineId, out var line))
                throw AppException.BadRequest(Message(QualityMessageKeys.InspectionLineNotFound));
            if (request.InspectedQuantity <= 0) continue;
            var remaining = RemainingInspectableQuantity(line);
            if (request.InspectedQuantity - remaining > 0.000001m)
                throw AppException.BadRequest(Message(
                    QualityMessageKeys.ControlQuantityExceedsLot,
                    line.StockCodeSnapshot,
                    request.InspectedQuantity,
                    remaining));
            result.Add((line, request.InspectedQuantity));
        }
        return result;
    }

    private async Task ApplyProgressControlsAsync(
        QualityInspection inspection,
        IReadOnlyList<QualityInspectionControlQuantityRequest>? requests,
        Guid idempotencyKey,
        long actor,
        DateTimeOffset now,
        CancellationToken token)
    {
        foreach (var (line, inspected) in ResolveProgressControls(inspection.Lines, requests))
        {
            var required = RequiredControlQuantityForDecision(line);
            line.InspectedQuantity += inspected;
            await Controls.AddAsync(new QualityInspectionControl
            {
                BranchCode = inspection.BranchCode,
                QualityInspectionId = inspection.Id,
                QualityInspectionLineId = line.Id,
                IdempotencyKey = idempotencyKey,
                LotQuantitySnapshot = line.Quantity,
                RequiredQuantitySnapshot = required,
                InspectedQuantity = inspected,
                OutcomeSummary = "Progress",
                InspectedBy = actor,
                InspectedAtUtc = now,
                CreatedBy = actor,
                CreatedDate = DateTime.UtcNow
            }, token);
        }
    }
    private static decimal DecidedQuantity(QualityInspectionLine line) =>
        line.AcceptedQuantity + line.RejectedQuantity + line.QuarantineQuantity;
    private static QualityDecision ResolveLineDecision(QualityInspectionLine line) =>
        DecidedQuantity(line) < line.Quantity
            ? QualityDecision.Hold
            : line.QuarantineQuantity > 0
                ? QualityDecision.Quarantined
                : line.RejectedQuantity > 0
                    ? QualityDecision.Rejected
                    : QualityDecision.Accepted;
    internal static bool RequiresDat(long sourceWarehouseId,long targetWarehouseId)=>sourceWarehouseId!=targetWarehouseId;
    internal static bool IsReceiptReadyForQualityDisposition(WarehouseOperationStatus status) =>
        status is WarehouseOperationStatus.Processed or WarehouseOperationStatus.Completed;
    internal static long? ResolveAcceptedLocationId(
        long? defaultReceivingLocationId,
        long? defaultPutawayLocationId,
        long? headerReceivingLocationId) =>
        defaultReceivingLocationId
        ?? defaultPutawayLocationId
        ?? headerReceivingLocationId;
    internal static IReadOnlySet<long> ResolveRequiredDecisionTargetLocationIds(
        IReadOnlyList<QualityDecisionPart> decisionParts,
        IReadOnlyDictionary<long, GoodsReceiptLine> receiptLines,
        QualityParameter parameter,
        IReadOnlyDictionary<long, QualityWarehouseRoute> warehouseRoutes,
        long headerReceivingLocationId,
        IReadOnlyCollection<QualityQuarantineDestinationDto> globalQuarantineDestinations)
    {
        _ = parameter;
        var result = new HashSet<long>();
        foreach (var part in decisionParts)
        {
            if (!part.Line.GoodsReceiptLineId.HasValue
                || !receiptLines.TryGetValue(part.Line.GoodsReceiptLineId.Value, out var receiptLine))
                continue;

            var targetLocationId = ResolveInspectionDecisionTargetLocationId(
                part,
                receiptLine.TargetWarehouseId,
                warehouseRoutes,
                globalQuarantineDestinations,
                receiptLine.DefaultReceivingLocationId,
                receiptLine.DefaultPutawayLocationId,
                headerReceivingLocationId);
            if (targetLocationId.HasValue)
                result.Add(targetLocationId.Value);
        }

        return result;
    }
    private string Message(string key, params object[] arguments) =>
        arguments.Length == 0 ? localizer[key].Value : localizer[key, arguments].Value;

    private async Task<bool> IsSourceReceiptReadyAsync(QualityInspection inspection, CancellationToken ct)
    {
        if (!string.Equals(inspection.SourceDocumentType, "GoodsReceipt", StringComparison.OrdinalIgnoreCase))
            return false;
        var status = await uow.Repository<GoodsReceiptHeader>().Query()
            .Where(x => x.Id == inspection.SourceDocumentId)
            .Select(x => (WarehouseOperationStatus?)x.Status)
            .FirstOrDefaultAsync(ct);
        return status.HasValue && IsReceiptReadyForQualityDisposition(status.Value);
    }

    private async Task<string> GetActorDisplayNameAsync(long actor, CancellationToken ct)
    {
        var value = await (from user in uow.Repository<User>().Query()
                           join detail in uow.Repository<UserDetail>().Query() on user.Id equals detail.UserId into details
                           from detail in details.DefaultIfEmpty()
                           where user.Id == actor
                           select new
                           {
                               user.Username,
                               FirstName = detail == null ? null : detail.FirstName,
                               LastName = detail == null ? null : detail.LastName
                           }).FirstOrDefaultAsync(ct);
        if (value is null) return $"#{actor}";
        var fullName = $"{value.FirstName} {value.LastName}".Trim();
        return Clean(string.IsNullOrWhiteSpace(fullName) ? value.Username : fullName, 200) ?? $"#{actor}";
    }

    internal static QualityInspectionWorkState ResolveWorkState(QualityInspection inspection)
    {
        if (IsTerminalStatus(inspection.Status)) return QualityInspectionWorkState.Completed;
        if (inspection.WorkSessions.Any(x => x.EndedAtUtc == null)) return QualityInspectionWorkState.Running;
        return inspection.WorkSessions.Count > 0
            ? QualityInspectionWorkState.Paused
            : QualityInspectionWorkState.NotStarted;
    }

    internal static bool TryRevertIdleInProgress(QualityInspection inspection)
    {
        if (inspection.Status != QualityInspectionStatus.InProgress) return false;
        if (inspection.WorkSessions.Any(session => session.EndedAtUtc == null)) return false;
        inspection.Status = QualityInspectionStatus.Pending;
        return true;
    }

    internal readonly record struct QualityWorkActors(string? StartedByName, long? WorkerUserId, long? StoppedByUserId);

    internal static QualityWorkActors ResolveLastWorkActors(QualityInspection inspection)
    {
        var last = inspection.WorkSessions
            .Where(session => !session.IsDeleted)
            .OrderByDescending(session => session.SequenceNo)
            .ThenByDescending(session => session.StartedAtUtc)
            .FirstOrDefault();
        if (last is null) return default;
        return new(last.WorkerNameSnapshot, last.WorkerUserId, last.EndedAtUtc is null ? null : last.EndedByUserId);
    }

    internal static QualityInspectionWorkSummaryDto BuildWorkSummary(
        QualityInspection inspection,
        long actor,
        bool canExecute,
        bool canSupervise,
        bool canDecide,
        bool receiptReady,
        DateTimeOffset now)
    {
        var sessions = inspection.WorkSessions.Where(x => !x.IsDeleted).ToArray();
        var active = sessions.FirstOrDefault(x => x.EndedAtUtc == null);
        var total = sessions.Sum(x => EffectiveWorkSeconds(x, now));
        var currentUserTotal = sessions.Where(x => x.WorkerUserId == actor).Sum(x => EffectiveWorkSeconds(x, now));
        var terminal = IsTerminalStatus(inspection.Status);
        return new QualityInspectionWorkSummaryDto(
            terminal
                ? QualityInspectionWorkState.Completed
                : active is not null
                    ? QualityInspectionWorkState.Running
                    : sessions.Length > 0
                        ? QualityInspectionWorkState.Paused
                        : QualityInspectionWorkState.NotStarted,
            now,
            total,
            currentUserTotal,
            sessions.Length,
            sessions.Select(x => x.WorkerUserId).Distinct().Count(),
            active?.WorkerUserId,
            active?.WorkerNameSnapshot,
            active?.StartedAtUtc,
            canExecute && receiptReady && !terminal && active is null,
            canExecute && active is not null && (active.WorkerUserId == actor || canSupervise),
            canDecide && receiptReady && active?.WorkerUserId == actor);
    }

    internal static QualityInspectionWorkSessionDto MapWorkSession(QualityInspectionWorkSession session) => new(
        session.Id,
        session.SequenceNo,
        session.WorkerUserId,
        session.WorkerNameSnapshot,
        session.StartedAtUtc,
        session.EndedAtUtc,
        session.DurationSeconds,
        session.StopReason,
        session.StopNote,
        session.EndedByUserId);

    internal static void CloseWorkSession(
        QualityInspectionWorkSession session,
        DateTimeOffset endedAtUtc,
        QualityInspectionWorkStopReason reason,
        string? note,
        Guid idempotencyKey,
        long actor)
    {
        if (session.EndedAtUtc.HasValue) return;
        session.EndedAtUtc = endedAtUtc < session.StartedAtUtc ? session.StartedAtUtc : endedAtUtc;
        session.DurationSeconds = Math.Max(0, (long)Math.Floor((session.EndedAtUtc.Value - session.StartedAtUtc).TotalSeconds));
        session.StopReason = reason;
        session.StopNote = Clean(note, 1000);
        session.EndIdempotencyKey = idempotencyKey;
        session.EndedByUserId = actor;
        session.UpdatedBy = actor;
        session.UpdatedDate = DateTime.UtcNow;
    }

    internal static long EffectiveWorkSeconds(QualityInspectionWorkSession session, DateTimeOffset now) =>
        session.EndedAtUtc.HasValue
            ? session.DurationSeconds
            : Math.Max(0, (long)Math.Floor((now - session.StartedAtUtc).TotalSeconds));

    internal static bool IsTerminalStatus(QualityInspectionStatus status) => status is
        QualityInspectionStatus.Passed or
        QualityInspectionStatus.Failed or
        QualityInspectionStatus.Released or
        QualityInspectionStatus.Cancelled;

    internal static bool CanPrioritize(QualityInspectionStatus status) => status is
        QualityInspectionStatus.Pending or
        QualityInspectionStatus.InProgress or
        QualityInspectionStatus.PartiallyDecided or
        QualityInspectionStatus.Quarantined;

    internal static IQueryable<QualityInspectionGridRow> ApplyInspectionListSort(
        IQueryable<QualityInspectionGridRow> query,
        PagedRequest request) =>
        query.OrderByDescending(row => row.IsPriority)
            .ThenBy(row => row.PriorityAssignedAtUtc)
            .ApplyThenSort(request, nameof(QualityInspectionGridRow.QueuedAtUtc));

    internal static IReadOnlyDictionary<long, int> BuildPriorityRanks(
        IEnumerable<(long Id, string BranchCode, DateTimeOffset? PriorityAssignedAtUtc, DateTimeOffset? QueuedAtUtc)> rows) =>
        rows.GroupBy(row => row.BranchCode, StringComparer.Ordinal)
            .SelectMany(group => group
                .OrderBy(row => row.PriorityAssignedAtUtc ?? DateTimeOffset.MinValue)
                .ThenBy(row => row.QueuedAtUtc ?? DateTimeOffset.MinValue)
                .ThenBy(row => row.Id)
                .Select((row, index) => (row.Id, Rank: index + 1)))
            .ToDictionary(row => row.Id, row => row.Rank);

    internal static bool TogglePriority(QualityInspection inspection, long actor, DateTimeOffset? now = null)
    {
        var assignedAt = now ?? DateTimeOffset.UtcNow;
        inspection.IsPriority = !inspection.IsPriority;
        inspection.PriorityAssignedAtUtc = inspection.IsPriority ? assignedAt : null;
        inspection.UpdatedBy = actor;
        inspection.UpdatedDate = assignedAt.UtcDateTime;
        return inspection.IsPriority;
    }
    internal static void SynchronizeGoodsReceiptStatus(GoodsReceiptHeader receipt, long actor) =>
        GoodsReceiptExecutionService.RefreshHeaderStatus(receipt, actor);

    internal static QualityDecisionResult BuildDecisionResult(
        GoodsReceiptHeader receipt,
        ErpPostingResult? posting,
        string? erpFailureMessage = null)
    {
        var createdNow = posting?.Status == Modules.ErpIntegration.Domain.ErpPostingStatus.Succeeded;
        var message = !string.IsNullOrWhiteSpace(erpFailureMessage)
            ? $"Kalite kararı uygulandı ancak Netsis irsaliyesi oluşturulamadı: {erpFailureMessage}"
            : createdNow
            ? "Kalite kararı uygulandı ve Netsis alış irsaliyesi oluşturuldu."
            : receipt.ErpIntegrationStatus == ErpIntegrationStatus.Succeeded
                ? "Kalite kararı uygulandı. Bu mal kabulün Netsis alış irsaliyesi daha önce oluşturulmuş."
                : receipt.QualityStatus is OperationQualityStatus.Pending
                    or OperationQualityStatus.InProgress
                    or OperationQualityStatus.PartiallyCompleted
                    ? "Kalite kararı kısmen uygulandı. Kalan kalite kararları tamamlandığında ERP gönderimi yeniden değerlendirilecek."
                    : receipt.ApprovalStatus == OperationApprovalStatus.Pending
                      && receipt.ErpPostingPolicy is GoodsReceiptErpPostingPolicy.AfterReceiptApproval
                          or GoodsReceiptErpPostingPolicy.AfterAllApprovals
                        ? "Kalite kararı uygulandı. Netsis alış irsaliyesi için mal kabul onayı bekleniyor."
                        : receipt.Status is not (WarehouseOperationStatus.Processed or WarehouseOperationStatus.Completed)
                            ? "Kalite kararı uygulandı. Netsis alış irsaliyesi için mal kabul operasyonunun tamamlanması bekleniyor."
                            : receipt.ErpIntegrationStatus == ErpIntegrationStatus.Cancelled
                                ? "Kalite kararı uygulandı; ancak bu mal kabulün ERP kaydı iptal durumunda."
                                : "Kalite kararı uygulandı. Seçili ERP gönderim politikası nedeniyle Netsis alış irsaliyesi henüz oluşturulmadı.";
        return new(
            receipt.Id,
            receipt.DocumentNo,
            receipt.Status,
            receipt.QualityStatus,
            receipt.ApprovalStatus,
            receipt.ErpIntegrationStatus,
            createdNow,
            message);
    }
    private static Guid CreateDatIdempotencyKey(Guid decisionKey,long sourceWarehouseId,long targetWarehouseId)
    {
        var input=$"{decisionKey:N}:{sourceWarehouseId}:{targetWarehouseId}";
        var hash=SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(input));
        return new Guid(hash[..16]);
    }
    private static IReadOnlyList<WarehouseTransferTrackingDraftRequest>? BuildDatTrackings(QualityInventoryDisposition disposition)
    {
        if (disposition.ReceiptLine.TrackingType == StockTrackingType.None
            && string.IsNullOrWhiteSpace(disposition.InspectionLine.LotNo)
            && string.IsNullOrWhiteSpace(disposition.InspectionLine.SerialNo))
            return null;
        return [new WarehouseTransferTrackingDraftRequest(
            disposition.Quantity,
            null,
            disposition.InspectionLine.LotNo,
            disposition.InspectionLine.SerialNo,
            null,
            disposition.InspectionLine.ExpiryDate,
            disposition.SourceLocationId,
            disposition.TargetLocationId)];
    }
    private static readonly string[] PendingQualitySourceStatuses = ["QualityHold", "Available"];
    private static readonly string[] QuarantinedSourceStatuses = ["Quarantine"];

    internal static QualityQuarantineDestinationDto ResolveQuarantineDestination(
        IReadOnlyCollection<QualityQuarantineDestinationDto> configured,
        long? requestedLocationId,
        long sourceWarehouseId)
    {
        var active = configured.Where(destination => destination.IsActive).ToArray();
        if (requestedLocationId.HasValue)
            return active.FirstOrDefault(destination => destination.LocationId == requestedLocationId.Value)
                ?? throw AppException.BadRequest("Seçilen karantina rafı aktif kalite hedefleri arasında bulunmuyor.");
        return active
            .OrderByDescending(destination => destination.WarehouseId == sourceWarehouseId)
            .ThenByDescending(destination => destination.IsDefault)
            .ThenBy(destination => destination.Priority)
            .ThenBy(destination => destination.WarehouseCode)
            .ThenBy(destination => destination.LocationCode)
            .FirstOrDefault()
            ?? throw AppException.Conflict("Karantina kararı için aktif hedef depo/raf bulunamadı.");
    }

    private static bool SameInventoryDimension(
        LocationStockBalance balance,
        QualityInspectionLine inspectionLine,
        string unitCode) =>
        balance.StockId == inspectionLine.StockId
        && balance.YapCodeId == inspectionLine.YapCodeId
        && string.Equals(NormalizeDimension(balance.UnitCode), NormalizeDimension(unitCode), StringComparison.OrdinalIgnoreCase)
        && string.Equals(NormalizeDimension(balance.LotNo), NormalizeDimension(inspectionLine.LotNo), StringComparison.OrdinalIgnoreCase)
        && string.Equals(NormalizeDimension(balance.SerialNo), NormalizeDimension(inspectionLine.SerialNo), StringComparison.OrdinalIgnoreCase);

    internal static IReadOnlyList<QualityInventorySourceAllocation> AllocateInventorySources(
        IReadOnlyCollection<QualityInventorySourceCandidate> candidates,
        IDictionary<long, decimal> remainingByBalanceId,
        decimal requiredQuantity,
        long preferredLocationId,
        string preferredStatus,
        string stockCode,
        string receiptDocumentNo,
        string? lotNo,
        string? serialNo)
    {
        decimal Remaining(long balanceId) =>
            remainingByBalanceId.TryGetValue(balanceId, out var quantity) ? quantity : 0;

        var ordered = candidates
            .Where(candidate => Remaining(candidate.BalanceId) > 0)
            .OrderByDescending(candidate => candidate.LocationId == preferredLocationId
                && string.Equals(candidate.StockStatus, preferredStatus, StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(candidate => string.Equals(
                candidate.StockStatus, preferredStatus, StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(candidate => candidate.LocationId == preferredLocationId)
            .ThenByDescending(candidate => candidate.LastTransactionDate)
            .ThenBy(candidate => candidate.LocationId)
            .ThenBy(candidate => candidate.BalanceId)
            .ToList();
        var availableQuantity = ordered.Sum(candidate => Remaining(candidate.BalanceId));
        if (availableQuantity + 0.000001m < requiredQuantity)
        {
            var balanceSummary = ordered.Count == 0
                ? "uygun raf/statü bulunamadı"
                : string.Join(" | ", ordered.Take(6).Select(candidate =>
                    $"{candidate.LocationCode}/{candidate.StockStatus}: {Remaining(candidate.BalanceId):0.######}"));
            throw AppException.Conflict(
                $"'{stockCode}' kalite satırı için gerçek raf bakiyesi yetersiz. "
                + $"Gereken: {requiredQuantity:0.######}, kullanılabilir: {availableQuantity:0.######}. "
                + $"Mal kabul: {receiptDocumentNo}; lot: {DisplayDimension(lotNo)}; seri: {DisplayDimension(serialNo)}. "
                + $"Bulunan bakiyeler: {balanceSummary}. Stok hareketi ile kalite kaydının mutabakatını kontrol edin.");
        }

        var allocations = new List<QualityInventorySourceAllocation>();
        var remainingQuantity = requiredQuantity;
        foreach (var candidate in ordered)
        {
            if (remainingQuantity <= 0.000001m) break;
            var candidateRemaining = Remaining(candidate.BalanceId);
            if (candidateRemaining <= 0) continue;
            var quantity = Math.Min(candidateRemaining, remainingQuantity);
            allocations.Add(new QualityInventorySourceAllocation(
                candidate.BalanceId,
                candidate.WarehouseId,
                candidate.LocationId,
                candidate.LocationCode,
                candidate.StockStatus,
                quantity));
            remainingByBalanceId[candidate.BalanceId] = candidateRemaining - quantity;
            remainingQuantity -= quantity;
        }
        return allocations;
    }

    private static string NormalizeDimension(string? value) => value?.Trim() ?? string.Empty;
    private static string DisplayDimension(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "yok" : value.Trim();

    private sealed record QualityInventoryDisposition(
        QualityInspectionLine InspectionLine,
        GoodsReceiptLine ReceiptLine,
        long SourceWarehouseId,
        long SourceLocationId,
        long TargetWarehouseId,
        long TargetLocationId,
        string SourceStockStatus,
        string TargetStockStatus,
        QualityDecision Decision,
        decimal Quantity,
        long? DecisionCodeId,
        string? ReasonCode,
        string? Note);
    private sealed record QualityReceiptExecutionSource(
        long QualityInspectionLineId,
        long WarehouseId,
        long LocationId,
        string StockStatus);
    private sealed record QualityReceiptLineAcceptedTarget(
        long LineId,
        long WarehouseId,
        long? DefaultPutawayLocationId,
        long? DefaultReceivingLocationId);
    internal sealed record QualityInventorySourceCandidate(
        long BalanceId,
        long WarehouseId,
        long LocationId,
        string LocationCode,
        string StockStatus,
        decimal AvailableQuantity,
        DateTime LastTransactionDate);
    internal sealed record QualityInventorySourceAllocation(
        long BalanceId,
        long WarehouseId,
        long LocationId,
        string LocationCode,
        string StockStatus,
        decimal Quantity);
    internal sealed record QualityDecisionPart(
        QualityInspectionLine Line,
        QualityDecision Decision,
        decimal Quantity,
        long? TargetLocationId = null,
        long? DecisionCodeId = null,
        string? ReasonCode = null,
        string? Note = null,
        int SequenceNo = 0);
    internal sealed record QualityDecisionState(
        QualityInspectionStatus InspectionStatus,
        OperationQualityStatus ReceiptStatus,
        bool IsTerminal);
    private sealed record QualityInspectionControlSnapshot(
        decimal LotQuantity,
        decimal RequiredQuantity,
        decimal InspectedQuantity);
    internal sealed record QualityWarehouseRouteDefaults(
        long? QualityLocationId,
        long? AcceptedLocationId,
        long? QuarantineLocationId,
        long? RejectLocationId);
    private static string NormalizeBranch(string? x)=>string.IsNullOrWhiteSpace(x)?"0":x.Trim(); private static string? Clean(string? x,int max){var v=string.IsNullOrWhiteSpace(x)?null:x.Trim();return v?.Length>max?v[..max]:v;}
    private async Task ApplyDecisionCodeAsync(
        QualityDecisionCode entity,
        QualityDecisionCodeUpsertRequest request,
        long? currentId,
        CancellationToken ct)
    {
        var branch = NormalizeBranch(request.BranchCode);
        var code = NormalizeDecisionCode(request.Code);
        var name = Clean(request.Name, 150)
            ?? throw AppException.BadRequest("Kalite karar kodu adı zorunludur.");
        if (code.Length == 0)
            throw AppException.BadRequest("Kalite karar kodu zorunludur.");
        if (request.ApplicableDecision is QualityDecision.Pending)
            throw AppException.BadRequest("Bekliyor durumu için karar kodu tanımlanamaz.");
        if (request.SortOrder < 0)
            throw AppException.BadRequest("Karar kodu sırası negatif olamaz.");
        if (await DecisionCodes.AnyAsync(value => value.BranchCode == branch
                && value.Code == code
                && (!currentId.HasValue || value.Id != currentId.Value), ct))
            throw AppException.Conflict($"'{code}' kalite karar kodu bu şubede zaten tanımlı.");

        entity.BranchCode = branch;
        entity.Code = code;
        entity.Name = name;
        entity.Description = Clean(request.Description, 500);
        entity.ApplicableDecision = request.ApplicableDecision;
        entity.RequiresNote = request.RequiresNote;
        entity.SortOrder = request.SortOrder;
        entity.IsActive = request.IsActive;
    }
    private static string NormalizeDecisionCode(string? value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToUpperInvariant();
    private static object DecisionCodeSnapshot(QualityDecisionCode entity) => new
    {
        entity.Id, entity.BranchCode, entity.Code, entity.Name, entity.ApplicableDecision,
        entity.Description, entity.RequiresNote, entity.SortOrder, entity.IsActive
    };
    private static void ApplyDecisionCodeVersion(QualityDecisionCode entity, string? supplied)
    {
        if (string.IsNullOrWhiteSpace(supplied)) return;
        try { entity.RowVersion = Convert.FromBase64String(supplied); }
        catch { throw AppException.Conflict("Kalite karar kodu güncellik bilgisi geçersiz. Sayfayı yenileyin."); }
    }
    private static void ApplyVersion(QualityInspection entity,string? supplied){if(string.IsNullOrWhiteSpace(supplied))return;try{entity.RowVersion=Convert.FromBase64String(supplied);}catch{throw AppException.Conflict("Kalite kaydı güncellik bilgisi geçersiz. Sayfayı yenileyin.");}}

    private async Task<IReadOnlyList<QualityInspectionGridRow>> AttachPriorityRanksAsync(
        IReadOnlyList<QualityInspectionGridRow> rows, CancellationToken ct)
    {
        var prioritized = rows.Where(row => row.IsPriority).ToList();
        if (prioritized.Count == 0) return rows;

        var branchCodes = prioritized.Select(row => row.BranchCode).Distinct(StringComparer.Ordinal).ToArray();
        var keys = await Inspections.Query()
            .Where(inspection => inspection.IsPriority && branchCodes.Contains(inspection.BranchCode))
            .Select(inspection => new
            {
                inspection.Id,
                inspection.BranchCode,
                inspection.PriorityAssignedAtUtc,
                inspection.QueuedAtUtc,
            })
            .ToListAsync(ct);

        var ranks = BuildPriorityRanks(keys.Select(key =>
            (key.Id, key.BranchCode, key.PriorityAssignedAtUtc, key.QueuedAtUtc)));
        foreach (var row in rows)
        {
            if (row.IsPriority && ranks.TryGetValue(row.Id, out var rank))
                row.PriorityRank = rank;
        }

        return rows;
    }

    private async Task<IReadOnlyList<QualityInspectionGridRow>> AttachWorkStoppedByNamesAsync(
        IReadOnlyList<QualityInspectionGridRow> rows, CancellationToken ct)
    {
        var userIds = rows
            .Select(row => row.WorkStoppedByUserId)
            .Where(id => id is > 0)
            .Select(id => id!.Value)
            .Distinct()
            .ToArray();
        if (userIds.Length == 0) return rows;

        var names = await (
            from user in uow.Repository<User>().Query()
            join detail in uow.Repository<UserDetail>().Query() on user.Id equals detail.UserId into details
            from detail in details.DefaultIfEmpty()
            where userIds.Contains(user.Id)
            select new
            {
                user.Id,
                user.Username,
                FirstName = detail == null ? null : detail.FirstName,
                LastName = detail == null ? null : detail.LastName
            }).ToListAsync(ct);

        var byId = names.ToDictionary(
            value => value.Id,
            value =>
            {
                var fullName = $"{value.FirstName} {value.LastName}".Trim();
                return string.IsNullOrWhiteSpace(fullName) ? value.Username : fullName;
            });

        foreach (var row in rows)
        {
            if (row.WorkStoppedByUserId is not long userId) continue;
            row.WorkStoppedByName = byId.TryGetValue(userId, out var name) ? name : $"#{userId}";
        }

        return rows;
    }

    private async Task<string?> ResolveWorkStoppedByNameAsync(QualityWorkActors actors, CancellationToken ct)
    {
        if (actors.StoppedByUserId is not long userId) return null;
        if (actors.WorkerUserId == userId) return actors.StartedByName;
        return await GetActorDisplayNameAsync(userId, ct);
    }

    private async Task<IReadOnlyList<QualityInspectionGridRow>> AttachProjectCodesAsync(
        IReadOnlyList<QualityInspectionGridRow> rows, CancellationToken ct)
    {
        if (rows.Count == 0) return rows;
        var receiptIds = rows
            .Where(x => string.Equals(x.SourceDocumentType, "GoodsReceipt", StringComparison.OrdinalIgnoreCase))
            .Select(x => x.SourceDocumentId)
            .Distinct()
            .ToArray();
        if (receiptIds.Length == 0) return rows;

        var projectRows = await (
            from line in uow.Repository<GoodsReceiptLine>().Query()
            join source in uow.Repository<GoodsReceiptLineSource>().Query() on line.Id equals source.GrLineId
            where receiptIds.Contains(line.GrHeaderId)
                && source.ProjectCodeSnapshot != null
                && source.ProjectCodeSnapshot != ""
            select new { line.GrHeaderId, source.ProjectCodeSnapshot })
            .ToListAsync(ct);

        var byReceipt = projectRows
            .GroupBy(x => x.GrHeaderId)
            .ToDictionary(g => g.Key, g => JoinDistinctProjectCodes(g.Select(x => x.ProjectCodeSnapshot)));

        foreach (var row in rows)
        {
            if (string.Equals(row.SourceDocumentType, "GoodsReceipt", StringComparison.OrdinalIgnoreCase)
                && byReceipt.TryGetValue(row.SourceDocumentId, out var codes))
                row.ProjectCodes = codes;
        }

        return rows;
    }

    private async Task<string?> ResolveProjectCodesAsync(string sourceDocumentType, long sourceDocumentId, CancellationToken ct)
    {
        if (!string.Equals(sourceDocumentType, "GoodsReceipt", StringComparison.OrdinalIgnoreCase))
            return null;
        var codes = await (
            from line in uow.Repository<GoodsReceiptLine>().Query()
            join source in uow.Repository<GoodsReceiptLineSource>().Query() on line.Id equals source.GrLineId
            where line.GrHeaderId == sourceDocumentId
                && source.ProjectCodeSnapshot != null
                && source.ProjectCodeSnapshot != ""
            select source.ProjectCodeSnapshot)
            .ToListAsync(ct);
        return JoinDistinctProjectCodes(codes);
    }

    private async Task<IReadOnlyDictionary<long, (string? ProjectCodes, string? OrderNumbers)>> ResolveLineSourceSummariesAsync(
        long[] goodsReceiptLineIds, CancellationToken ct)
    {
        if (goodsReceiptLineIds.Length == 0)
            return new Dictionary<long, (string? ProjectCodes, string? OrderNumbers)>();

        var rows = await (
            from source in uow.Repository<GoodsReceiptLineSource>().Query()
            join document in uow.Repository<GoodsReceiptSourceDocument>().Query()
                on source.GrSourceDocumentId equals document.Id
            where goodsReceiptLineIds.Contains(source.GrLineId)
            select new
            {
                source.GrLineId,
                source.ProjectCodeSnapshot,
                document.ExternalDocumentNo
            }).ToListAsync(ct);

        return rows
            .GroupBy(row => row.GrLineId)
            .ToDictionary(
                group => group.Key,
                group => (
                    ProjectCodes: JoinDistinctProjectCodes(group.Select(x => x.ProjectCodeSnapshot)),
                    OrderNumbers: JoinDistinctProjectCodes(group.Select(x => x.ExternalDocumentNo))));
    }

    private static string? JoinDistinctProjectCodes(IEnumerable<string?> values)
    {
        var normalized = values
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return normalized.Length == 0 ? null : string.Join(", ", normalized);
    }
}
