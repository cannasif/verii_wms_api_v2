using System.Data;
using Microsoft.EntityFrameworkCore;
using verii_wms_api_v2.Modules.Audit.Application;
using verii_wms_api_v2.Modules.ErpIntegration.Application;
using verii_wms_api_v2.Modules.Location.Domain;
using verii_wms_api_v2.Modules.Production.Application;
using verii_wms_api_v2.Modules.Production.Domain;
using verii_wms_api_v2.Modules.ProductionTransfer.Domain;
using verii_wms_api_v2.Modules.StockBalance.Domain;
using verii_wms_api_v2.Modules.StockMovement.Domain;
using verii_wms_api_v2.Modules.StockTracking.Application;
using verii_wms_api_v2.Modules.WarehouseOperations.Domain;
using verii_wms_api_v2.Modules.WarehouseTransfer.Application;
using verii_wms_api_v2.Modules.WarehouseTransfer.Domain;
using verii_wms_api_v2.Shared;
using verii_wms_api_v2.Shared.Application.Abstractions.Persistence;
using verii_wms_api_v2.Shared.Application.Exceptions;
using StockEntity=verii_wms_api_v2.Modules.Stock.Domain.Stock;
using WarehouseEntity=verii_wms_api_v2.Modules.Warehouse.Domain.Warehouse;
using YapCodeEntity=verii_wms_api_v2.Modules.YapCode.Domain.YapCode;

namespace verii_wms_api_v2.Modules.ProductionTransfer.Application;

public sealed class ProductionTransferService(
    IUnitOfWork uow,
    IWarehouseTransferService transfers,
    IWarehouseTransferReservationService reservations,
    IAuditLogWriter audit,
    IStockTrackingPolicyResolver trackingPolicyResolver,
    IOperationCancellationCoordinator cancellationCoordinator) : IProductionTransferService
{
    private static readonly WarehouseTransferBusinessContext[] Contexts =
    [
        WarehouseTransferBusinessContext.ProductionMaterialSupply,
        WarehouseTransferBusinessContext.ProductionWipMove,
        WarehouseTransferBusinessContext.ProductionOutputMove
    ];

    private IGenericRepository<ProductionTransferHeaderLink> Links => uow.Repository<ProductionTransferHeaderLink>();
    private IGenericRepository<ProductionTransferPolicy> Policies => uow.Repository<ProductionTransferPolicy>();

    public Task<CreateWarehouseTransferDraftResult> CreateDraftAsync(
        CreateProductionTransferDraftRequest request,long actor,CancellationToken ct=default)
    {
        Validate(request);
        return uow.ExecuteInTransactionAsync(async token =>
        {
            var existing=await uow.Repository<WarehouseTransferHeader>().Query()
                .Where(x=>x.CorrelationId==request.Transfer.IdempotencyKey)
                .Select(x=>new{x.Id,x.BusinessContext}).SingleOrDefaultAsync(token);
            if(existing is not null)
            {
                if(!Contexts.Contains(existing.BusinessContext))
                    throw AppException.Conflict("Aynı idempotency anahtarı başka bir transfer bağlamında kullanılmış.");
                var replay=await transfers.CreateDraftAsync(request.Transfer with{BusinessContext=existing.BusinessContext},actor,token);
                if(!await Links.AnyAsync(x=>x.WarehouseTransferHeaderId==existing.Id,token))
                    throw AppException.Conflict("Üretim transferi önceki istekte eksik bağlamla oluşmuş; teknik inceleme gereklidir.");
                return replay;
            }

            var policy=await GetPolicyEntityAsync(request.Transfer.BranchCode,token);
            request=await ApplyDefaultProductionTransferLocationAsync(
                request,policy.RequireTargetProductionLocation,token);
            if(IsAutoAssignSources(request))
                request=await AutoAssignSourceLocationsAndSerialsAsync(request,token);
            ValidatePolicy(request,policy);
            await ValidateProductionReferencesAsync(request,token);
            if(!request.TriggeredByProduction&&policy.RequireErpMasterDataForManualTransfer)
                await ValidateManualErpMasterDataAsync(request,token);
            var context=Context(request.Purpose);
            var availability=policy.CheckMaterialAvailability
                ? await AvailabilityAsync(request.Transfer,token)
                : ProductionMaterialAvailabilityStatus.NotChecked;
            if(policy.BlockOnShortage&&!IsAutoAssignSources(request)&&availability==ProductionMaterialAvailabilityStatus.Shortage)
                throw AppException.Conflict("Üretim besleme transferi için kaynak rafta yeterli kullanılabilir stok yok.");

            var result=await transfers.CreateDraftWithPolicyContextAsync(
                request.Transfer with{
                    BusinessContext=context,
                    AutoAssignSources=IsAutoAssignSources(request)},
                ProductionTransferWarehousePolicyAdapter.FromProductionPolicy(policy),
                actor,
                token);
            var header=await uow.Repository<WarehouseTransferHeader>().Query(true)
                .Include(x=>x.Lines).SingleAsync(x=>x.Id==result.Id,token);
            header.RequireApproval|=policy.RequireApproval;
            header.CancellationReturnPolicy=policy.CancellationReturnPolicy;
            if(IsAutoAssignSources(request))
                header.ReservationPolicy=WarehouseTransferReservationPolicy.None;
            if(header.RequireApproval&&header.ApprovalStatus==Modules.WarehouseOperations.Domain.OperationApprovalStatus.NotRequired)
                header.ApprovalStatus=Modules.WarehouseOperations.Domain.OperationApprovalStatus.Pending;

            var now=DateTime.UtcNow;
            var link=new ProductionTransferHeaderLink{
                BranchCode=header.BranchCode,CreatedBy=actor,CreatedDate=now,WarehouseTransferHeader=header,
                Purpose=request.Purpose,ProductionHeaderId=request.ProductionHeaderId,ProductionOrderId=request.ProductionOrderId,
                ProductionOperationId=request.ProductionOperationId,ProductionPlanNo=Clean(request.ProductionPlanNo,100),
                ProductionOrderNo=Clean(request.ProductionOrderNo,100),ProductionOperationCode=Clean(request.ProductionOperationCode,100),
                SourceWorkCenterCode=Clean(request.SourceWorkCenterCode,100),TargetWorkCenterCode=Clean(request.TargetWorkCenterCode,100),
                TriggeredByProduction=request.TriggeredByProduction,AutoGenerated=request.AutoGenerated,
                RequiredForOrderStart=request.RequiredForOrderStart,RequiredForOrderCompletion=request.RequiredForOrderCompletion,
                MaterialAvailabilityStatus=availability,RequirementCalculatedAtUtc=policy.CheckMaterialAvailability?DateTimeOffset.UtcNow:null,
                ErpPostingPolicy=policy.ErpPostingPolicy,
                RequestedByUserId=request.RequestedByUserId??actor,
                RequestedByNameSnapshot=Clean(request.RequestedByName,200)
            };
            var contexts=(request.LineContexts??[]).ToDictionary(x=>x.LineIndex);
            foreach(var line in header.Lines.OrderBy(x=>x.LineNo))
            {
                contexts.TryGetValue(line.LineNo-1,out var lineContext);
                link.Lines.Add(new ProductionTransferLineLink{
                    BranchCode=header.BranchCode,CreatedBy=actor,CreatedDate=now,WarehouseTransferLine=line,
                    LineRole=lineContext?.LineRole??DefaultRole(request.Purpose),
                    ProductionConsumptionId=lineContext?.ProductionConsumptionId,
                    ProductionOutputId=lineContext?.ProductionOutputId,
                    RequirementReference=Clean(lineContext?.RequirementReference,150),
                    RequiredQuantity=lineContext?.RequiredQuantity??line.RequestedQuantity
                });
            }
            await Links.AddAsync(link,token);
            await uow.SaveChangesAsync(token);
            await audit.WriteAsync(new("production-transfer.draft.create",nameof(ProductionTransferHeaderLink),link.Id.ToString(),
                "Succeeded","production-transfer",NewValues:new{result.Id,result.DocumentNo,request.Purpose,request.ProductionOrderNo,availability},
                ChangedFields:["Transfer","ProductionContext","LineLinks"]),token);
            return result;
        },ct,IsolationLevel.Serializable);
    }

    public Task<PagedResponse<WarehouseTransferGridRow>> GetPagedAsync(PagedRequest request,CancellationToken ct=default) =>
        transfers.GetPagedByContextAsync(request,Contexts,ct);

    public async Task<ProductionTransferDetail> GetDetailAsync(long id,CancellationToken ct=default)
    {
        var transfer=await transfers.GetDetailForContextAsync(id,Contexts,ct);
        var racklessFlags=await ProductionTransferWarehouseRacklessSupport.GetRacklessFlagsAsync(
            uow,[transfer.Header.SourceWarehouseId,transfer.Header.TargetWarehouseId],ct);
        transfer=transfer with
        {
            SourceIsRackless=racklessFlags.GetValueOrDefault(transfer.Header.SourceWarehouseId),
            TargetIsRackless=racklessFlags.GetValueOrDefault(transfer.Header.TargetWarehouseId),
        };
        var context=await Links.Query().Where(x=>x.WarehouseTransferHeaderId==id)
            .Select(x=>new ProductionTransferContextDto(x.Id,x.Purpose,x.ProductionHeaderId,x.ProductionOrderId,
                x.ProductionOperationId,x.ProductionPlanNo,x.ProductionOrderNo,x.ProductionOperationCode,
                x.SourceWorkCenterCode,x.TargetWorkCenterCode,x.TriggeredByProduction,x.AutoGenerated,
                x.RequiredForOrderStart,x.RequiredForOrderCompletion,x.MaterialAvailabilityStatus,x.ErpPostingPolicy))
            .SingleOrDefaultAsync(ct)??throw AppException.NotFound("Üretim transfer bağlamı bulunamadı.");
        return new(transfer,context);
    }

    public async Task<ProductionTransferDetail> UpdateDraftAsync(
        long id,UpdateWarehouseTransferDraftRequest request,long actor,CancellationToken ct=default)
    {
        await transfers.EnsureContextAsync(id,Contexts,ct);
        await transfers.UpdateDraftAsync(id,request,actor,ct);
        return await GetDetailAsync(id,ct);
    }

    public Task DeleteDraftAsync(long id,long actor,CancellationToken ct=default)=>
        uow.ExecuteInTransactionAsync(async token=>{
            await transfers.EnsureContextAsync(id,Contexts,token);
            var link=await Links.Query(true).SingleOrDefaultAsync(x=>x.WarehouseTransferHeaderId==id,token)
                ??throw AppException.NotFound("Üretim transfer bağlamı bulunamadı.");
            var now=DateTime.UtcNow;
            await uow.Repository<ProductionTransferLineLink>().Query(true).Where(x=>x.ProductionTransferHeaderLinkId==link.Id)
                .ExecuteUpdateAsync(x=>x.SetProperty(v=>v.IsDeleted,true).SetProperty(v=>v.DeletedBy,actor).SetProperty(v=>v.DeletedDate,now),token);
            link.IsDeleted=true;link.DeletedBy=actor;link.DeletedDate=now;
            await uow.SaveChangesAsync(token);
            await transfers.DeleteDraftAsync(id,actor,token);
            return true;
        },ct);

    public async Task<OperationCancellationResult> CancelAsync(
        long id,
        WarehouseTransferTransitionRequest request,
        long actor,
        CancellationToken ct = default)
    {
        if (id <= 0 || request.IdempotencyKey == Guid.Empty)
            throw AppException.BadRequest("Transfer ve idempotency anahtarı zorunludur.");
        if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length is < 5 or > 1000)
            throw AppException.BadRequest("İptal nedeni 5-1000 karakter arasında olmalıdır.");

        await transfers.EnsureContextAsync(id, Contexts, ct);

        var header = await uow.Repository<WarehouseTransferHeader>().Query()
            .Include(x => x.Lines.Where(line => !line.IsDeleted))
            .SingleOrDefaultAsync(x => x.Id == id, ct)
            ?? throw AppException.NotFound("Transfer kaydı bulunamadı.");

        if (header.IsDeleted)
        {
            return new OperationCancellationResult(
                "WarehouseTransfer",
                header.Id,
                header.DocumentNo,
                OperationCancellationRoute.AlreadyCancelled,
                "Deleted",
                header.ErpIntegrationStatus.ToString(),
                false,
                true,
                true);
        }

        var link = await Links.Query()
            .SingleOrDefaultAsync(x => x.WarehouseTransferHeaderId == id, ct)
            ?? throw AppException.NotFound("Üretim transfer bağlamı bulunamadı.");

        if (await ShouldDeleteDraftInsteadOfCancelAsync(header, link, ct))
        {
            var documentNo = header.DocumentNo;
            var erpStatus = header.ErpIntegrationStatus;
            await DeleteDraftAsync(id, actor, ct);
            await audit.WriteAsync(new(
                "production-transfer.draft.cancel",
                nameof(WarehouseTransferHeader),
                id.ToString(),
                "Succeeded",
                "production-transfer",
                NewValues: new { documentNo, Reason = request.Reason.Trim() },
                ChangedFields: ["IsDeleted"]), ct);

            return new OperationCancellationResult(
                "WarehouseTransfer",
                id,
                documentNo,
                OperationCancellationRoute.LocalCompensation,
                "Deleted",
                erpStatus.ToString(),
                false,
                true,
                false);
        }

        if (await ShouldReleaseUnlinkedDraftToAtanmayanlarAsync(header, link, ct))
        {
            return await uow.ExecuteInTransactionAsync(async token =>
            {
                var trackedHeader = await uow.Repository<WarehouseTransferHeader>().Query(true)
                    .Include(x => x.Lines.Where(line => !line.IsDeleted))
                    .Include(x => x.Tasks.Where(task => !task.IsDeleted))
                        .ThenInclude(task => task.Lines.Where(line => !line.IsDeleted))
                    .Include(x => x.Tasks.Where(task => !task.IsDeleted))
                        .ThenInclude(task => task.Assignments)
                    .Include(x => x.StatusHistory)
                    .SingleAsync(x => x.Id == id, token);
                var trackedLink = await Links.Query(true)
                    .SingleAsync(x => x.WarehouseTransferHeaderId == id, token);

                await ProductionTransferCancellationReturnRemainderSupport.ReleaseUnlinkedDraftToAtanmayanlarAsync(
                    uow,
                    reservations,
                    trackedHeader,
                    trackedLink,
                    request.Reason.Trim(),
                    request.IdempotencyKey,
                    actor,
                    token);

                await uow.SaveChangesAsync(token);
                await audit.WriteAsync(new(
                    "production-transfer.unlinked-draft.release-to-pending",
                    nameof(WarehouseTransferHeader),
                    id.ToString(),
                    "Succeeded",
                    "production-transfer",
                    NewValues: new { trackedHeader.DocumentNo, Reason = request.Reason.Trim() },
                    ChangedFields: ["WorkflowStatus", "TaskAssignments"]), token);

                return new OperationCancellationResult(
                    "WarehouseTransfer",
                    trackedHeader.Id,
                    trackedHeader.DocumentNo,
                    OperationCancellationRoute.LocalCompensation,
                    trackedHeader.Status.ToString(),
                    trackedHeader.ErpIntegrationStatus.ToString(),
                    false,
                    true,
                    false);
            }, ct);
        }

        return await cancellationCoordinator.CancelWarehouseTransferAsync(id, request, actor, ct);
    }

    public Task<WithdrawProductionTransferDraftLinesResult> WithdrawDraftLinesAsync(
        long id,
        WithdrawProductionTransferDraftLinesRequest request,
        long actor,
        CancellationToken ct = default)
    {
        var lineIds = (request.TransferLineIds ?? []).Distinct().ToArray();
        if (lineIds.Length == 0)
            throw AppException.BadRequest("Geri alınacak en az bir satır seçilmelidir.");

        return uow.ExecuteInTransactionAsync(async token =>
        {
            await transfers.EnsureContextAsync(id, Contexts, token);

            var header = await uow.Repository<WarehouseTransferHeader>().Query(true)
                .Include(x => x.Lines.Where(line => !line.IsDeleted))
                    .ThenInclude(line => line.Trackings.Where(tracking => !tracking.IsDeleted))
                .Include(x => x.Tasks.Where(task => !task.IsDeleted))
                    .ThenInclude(task => task.Lines.Where(line => !line.IsDeleted))
                .SingleAsync(x => x.Id == id, token);

            if (header.Status != WarehouseTransferStatus.Draft)
                throw AppException.Conflict("Yalnızca taslak transferden stok geri alınabilir.");

            var activeLines = header.Lines.Where(line => !line.IsDeleted).ToArray();
            var selectedLines = activeLines.Where(line => lineIds.Contains(line.Id)).ToArray();
            if (selectedLines.Length != lineIds.Length)
                throw AppException.BadRequest("Seçilen satırlardan biri transferde bulunamadı.");

            foreach (var line in selectedLines)
            {
                if (ProductionWorkOrderMaterialAssignment.ResolveEffectivePickedQuantity(line) > 0)
                    throw AppException.Conflict($"{line.LineNo}. satırda toplanmış miktar olduğu için geri alınamaz.");
            }

            var link = await Links.Query(true)
                .Include(x => x.Lines.Where(line => !line.IsDeleted))
                .SingleOrDefaultAsync(x => x.WarehouseTransferHeaderId == id, token)
                ?? throw AppException.NotFound("Üretim transfer bağlamı bulunamadı.");

            if (selectedLines.Length == activeLines.Length)
            {
                var withdrawnAll = selectedLines.Select(line =>
                {
                    var productionLineLink = link.Lines.FirstOrDefault(x => x.WarehouseTransferLineId == line.Id);
                    return new WithdrawnProductionTransferDraftLineDto(
                        line.Id,
                        line.StockId,
                        line.StockCodeSnapshot,
                        line.StockNameSnapshot,
                        line.RequestedQuantity,
                        productionLineLink?.RequirementReference);
                }).ToArray();

                await DeleteDraftAsync(id, actor, token);
                await audit.WriteAsync(new(
                    "production-transfer.draft.withdraw-lines",
                    nameof(WarehouseTransferHeader),
                    id.ToString(),
                    "Succeeded",
                    "production-transfer",
                    NewValues: new
                    {
                        TransferDeleted = true,
                        link.ProductionOrderNo,
                        WithdrawnLineCount = withdrawnAll.Length,
                        WithdrawnQuantity = withdrawnAll.Sum(x => x.Quantity),
                    },
                    ChangedFields: ["Lines", "TransferDeleted"]), token);

                return new WithdrawProductionTransferDraftLinesResult(
                    true,
                    null,
                    header.DocumentNo,
                    link.ProductionOrderNo,
                    withdrawnAll.Length,
                    withdrawnAll.Sum(x => x.Quantity),
                    0,
                    withdrawnAll);
            }

            var now = DateTime.UtcNow;
            var withdrawn = new List<WithdrawnProductionTransferDraftLineDto>(selectedLines.Length);
            foreach (var line in selectedLines)
            {
                var productionLineLink = link.Lines.FirstOrDefault(x => x.WarehouseTransferLineId == line.Id);
                withdrawn.Add(new WithdrawnProductionTransferDraftLineDto(
                    line.Id,
                    line.StockId,
                    line.StockCodeSnapshot,
                    line.StockNameSnapshot,
                    line.RequestedQuantity,
                    productionLineLink?.RequirementReference));

                line.IsDeleted = true;
                line.DeletedBy = actor;
                line.DeletedDate = now;
                foreach (var tracking in line.Trackings.Where(tracking => !tracking.IsDeleted))
                {
                    tracking.IsDeleted = true;
                    tracking.DeletedBy = actor;
                    tracking.DeletedDate = now;
                }

                foreach (var task in header.Tasks.Where(task => !task.IsDeleted))
                {
                    foreach (var taskLine in task.Lines.Where(taskLine => !taskLine.IsDeleted && taskLine.WtLineId == line.Id))
                    {
                        taskLine.IsDeleted = true;
                        taskLine.DeletedBy = actor;
                        taskLine.DeletedDate = now;
                    }
                }

                if (productionLineLink is not null)
                {
                    productionLineLink.IsDeleted = true;
                    productionLineLink.DeletedBy = actor;
                    productionLineLink.DeletedDate = now;
                }
            }

            if (WarehouseTransferReservationService.UsesTransferReservations(header))
            {
                var reason = Clean(request.Reason, 500) ?? "Taslak transfer satırı iş emrine geri alındı.";
                await reservations.ReleaseAllAsync(
                    header,
                    $"WT:{id}:RESERVE:WITHDRAW:{Guid.NewGuid():N}",
                    reason,
                    actor,
                    token);
                await reservations.ReserveAsync(
                    header,
                    $"WT:{id}:RESERVE:WITHDRAW-REBOOK:{Guid.NewGuid():N}",
                    actor,
                    token);
            }

            header.UpdatedBy = actor;
            header.UpdatedDate = now;
            await uow.SaveChangesAsync(token);

            var remainingCount = header.Lines.Count(line => !line.IsDeleted);
            await audit.WriteAsync(new(
                "production-transfer.draft.withdraw-lines",
                nameof(WarehouseTransferHeader),
                id.ToString(),
                "Succeeded",
                "production-transfer",
                NewValues: new
                {
                    TransferDeleted = false,
                    link.ProductionOrderNo,
                    WithdrawnLineCount = withdrawn.Count,
                    WithdrawnQuantity = withdrawn.Sum(x => x.Quantity),
                    RemainingLineCount = remainingCount,
                },
                ChangedFields: ["Lines"]), token);

            return new WithdrawProductionTransferDraftLinesResult(
                false,
                id,
                header.DocumentNo,
                link.ProductionOrderNo,
                withdrawn.Count,
                withdrawn.Sum(x => x.Quantity),
                remainingCount,
                withdrawn);
        }, ct, IsolationLevel.Serializable);
    }

    public async Task<ProductionTransferPolicyDto> GetPolicyAsync(string branchCode,CancellationToken ct=default) =>
        Map(await GetPolicyEntityAsync(branchCode,ct));

    public async Task<ProductionTransferPolicyDto> UpdatePolicyAsync(
        UpdateProductionTransferPolicyRequest request,long actor,CancellationToken ct=default)
    {
        if(request.OverIssueTolerancePercent is <0 or >100)
            throw AppException.BadRequest("Fazla sarf toleransı 0-100 arasında olmalıdır.");
        if(!request.AllowOverIssue&&request.OverIssueTolerancePercent!=0)
            throw AppException.BadRequest("Fazla sarf kapalıyken tolerans sıfır olmalıdır.");
        var sourceSystemCode=Clean(request.WmsSourceSystemCode,50)?.ToUpperInvariant();
        if((request.ProductionOrderSource is ProductionOrderSourceType.WmsIntegrationTables or ProductionOrderSourceType.ErpAndWms)&&
           string.IsNullOrWhiteSpace(sourceSystemCode))
            throw AppException.BadRequest("WMS entegrasyon tablosu seçildiğinde kaynak sistem kodu zorunludur.");
        var branch=Branch(request.BranchCode);
        var entity=await Policies.FirstOrDefaultAsync(x=>x.BranchCode==branch&&x.PolicyKey=="DEFAULT",true,ct);
        var before=entity is null?null:Map(entity);
        if(entity is null){entity=Default(branch);entity.CreatedBy=actor;await Policies.AddAsync(entity,ct);}
        else EnsureRowVersion(entity.RowVersion,request.RowVersion);
        entity.ProductionOrderSource=request.ProductionOrderSource;
        entity.WmsSourceSystemCode=sourceSystemCode??"WINDBOX";
        entity.RequireProductionOrderReference=request.RequireProductionOrderReference;
        entity.AllowManualTransfer=request.AllowManualTransfer;
        entity.RequireErpMasterDataForManualTransfer=request.RequireErpMasterDataForManualTransfer;
        entity.AllowAutomaticGeneration=request.AllowAutomaticGeneration;
        entity.CheckMaterialAvailability=request.CheckMaterialAvailability;entity.BlockOnShortage=request.BlockOnShortage;
        entity.RequireTaskAssignment=request.RequireTaskAssignment;
        entity.RequireSourceProductionLocation=request.RequireSourceProductionLocation;
        entity.RequireTargetProductionLocation=request.RequireTargetProductionLocation;
        entity.AllowPartialSupply=request.AllowPartialSupply;entity.AllowOverIssue=request.AllowOverIssue;
        entity.OverIssueTolerancePercent=request.OverIssueTolerancePercent;entity.RequireApproval=request.RequireApproval;
        entity.ErpPostingPolicy=request.ErpPostingPolicy;
        entity.CancellationReturnPolicy=request.CancellationReturnPolicy;
        entity.UpdatedBy=actor;entity.UpdatedDate=DateTime.UtcNow;
        try{await uow.SaveChangesAsync(ct);}
        catch(DbUpdateConcurrencyException){throw AppException.Conflict("Üretim transfer politikası başka bir kullanıcı tarafından değiştirildi. Sayfayı yenileyin.");}
        var result=Map(entity);
        await audit.WriteAsync(new("production-transfer.policy.update",nameof(ProductionTransferPolicy),entity.Id.ToString(),
            "Succeeded","production-transfer",OldValues:before,NewValues:result,ChangedFields:["Policy"]),ct);
        return result;
    }

    private async Task<ProductionMaterialAvailabilityStatus> AvailabilityAsync(
        CreateWarehouseTransferDraftRequest request,CancellationToken ct)
    {
        var balances=uow.Repository<LocationStockBalance>().Query();
        var availableCount=0;
        foreach(var line in request.Lines)
        {
            var available=await balances.Where(x=>x.WarehouseId==request.SourceWarehouseId
                    && x.LocationId==line.DefaultSourceLocationId && x.StockId==line.StockId
                    && x.YapCodeId==line.YapCodeId && x.StockStatus=="Available")
                .SumAsync(x=>(decimal?)x.AvailableQuantity,ct)??0;
            if(available>=line.Quantity)availableCount++;
        }
        if(availableCount==request.Lines.Count)return ProductionMaterialAvailabilityStatus.Available;
        return availableCount==0?ProductionMaterialAvailabilityStatus.Shortage:ProductionMaterialAvailabilityStatus.PartiallyAvailable;
    }

    private async Task<ProductionTransferPolicy> GetPolicyEntityAsync(string branchCode,CancellationToken ct)
    {
        var branch=Branch(branchCode);
        return await Policies.FirstOrDefaultAsync(x=>x.BranchCode==branch&&x.PolicyKey=="DEFAULT",false,ct)??Default(branch);
    }

    private async Task ValidateProductionReferencesAsync(
        CreateProductionTransferDraftRequest request,
        CancellationToken ct)
    {
        var branch=Branch(request.Transfer.BranchCode);
        if(request.ProductionHeaderId.HasValue)
        {
            var exists=await uow.Repository<ProductionHeader>().Query()
                .AnyAsync(x=>x.Id==request.ProductionHeaderId&&x.BranchCode==branch,ct);
            if(!exists)throw AppException.BadRequest("Bağlı üretim planı bulunamadı.");
        }
        if(request.ProductionOrderId.HasValue)
        {
            var order=await uow.Repository<ProductionOrder>().Query()
                .Where(x=>x.Id==request.ProductionOrderId&&x.BranchCode==branch)
                .Select(x=>new{x.Id,x.ProductionHeaderId}).SingleOrDefaultAsync(ct)
                ??throw AppException.BadRequest("Bağlı üretim emri bulunamadı.");
            if(request.ProductionHeaderId.HasValue&&order.ProductionHeaderId!=request.ProductionHeaderId)
                throw AppException.BadRequest("Üretim emri seçilen üretim planına ait değil.");
        }
        var consumptionIds=(request.LineContexts??[]).Where(x=>x.ProductionConsumptionId.HasValue)
            .Select(x=>x.ProductionConsumptionId!.Value).Distinct().ToArray();
        if(consumptionIds.Length>0)
        {
            var count=await uow.Repository<ProductionMaterialRequirement>().Query()
                .CountAsync(x=>consumptionIds.Contains(x.Id)&&
                    (!request.ProductionOrderId.HasValue||x.ProductionOrderId==request.ProductionOrderId),ct);
            if(count!=consumptionIds.Length)
                throw AppException.BadRequest("Üretim transferindeki malzeme ihtiyaç bağlantılarından biri geçersiz.");
        }
        var outputIds=(request.LineContexts??[]).Where(x=>x.ProductionOutputId.HasValue)
            .Select(x=>x.ProductionOutputId!.Value).Distinct().ToArray();
        if(outputIds.Length>0)
        {
            var count=await uow.Repository<ProductionOutputExpectation>().Query()
                .CountAsync(x=>outputIds.Contains(x.Id)&&
                    (!request.ProductionOrderId.HasValue||x.ProductionOrderId==request.ProductionOrderId),ct);
            if(count!=outputIds.Length)
                throw AppException.BadRequest("Üretim transferindeki çıktı bağlantılarından biri geçersiz.");
        }
    }

    private async Task ValidateManualErpMasterDataAsync(
        CreateProductionTransferDraftRequest request,CancellationToken ct)
    {
        var branch=Branch(request.Transfer.BranchCode);
        var warehouseIds=new[]{request.Transfer.SourceWarehouseId,request.Transfer.TargetWarehouseId}.Distinct().ToArray();
        var warehouseCount=await uow.Repository<WarehouseEntity>().Query()
            .CountAsync(x=>x.BranchCode==branch&&warehouseIds.Contains(x.Id),ct);
        if(warehouseCount!=warehouseIds.Length)
            throw AppException.BadRequest("Plansız üretim transferindeki kaynak veya hedef depo ERP aynasında bulunamadı.");

        var stockIds=request.Transfer.Lines.Select(x=>x.StockId).Distinct().ToArray();
        var stocks=await uow.Repository<StockEntity>().Query()
            .Where(x=>x.BranchCode==branch&&stockIds.Contains(x.Id))
            .Select(x=>new{x.Id,x.BaseUnitCode}).ToListAsync(ct);
        if(stocks.Count!=stockIds.Length)
            throw AppException.BadRequest("Plansız üretim transferindeki stoklardan biri ERP aynasında bulunamadı.");
        var units=stocks.ToDictionary(x=>x.Id,x=>x.BaseUnitCode);
        foreach(var line in request.Transfer.Lines)
            if(string.IsNullOrWhiteSpace(line.UnitCode)||
               !string.Equals(units[line.StockId].Trim(),line.UnitCode.Trim(),StringComparison.OrdinalIgnoreCase))
                throw AppException.BadRequest($"Stok {line.StockId} için ölçü birimi ERP ana birimiyle uyuşmuyor. Beklenen: {units[line.StockId]}.");

        var configurationIds=request.Transfer.Lines.Where(x=>x.YapCodeId.HasValue)
            .Select(x=>x.YapCodeId!.Value).Distinct().ToArray();
        if(configurationIds.Length==0)return;
        var configurations=await uow.Repository<YapCodeEntity>().Query()
            .Where(x=>x.BranchCode==branch&&configurationIds.Contains(x.Id))
            .Select(x=>new{x.Id,x.StockId}).ToListAsync(ct);
        if(configurations.Count!=configurationIds.Length)
            throw AppException.BadRequest("Plansız üretim transferindeki yapılandırma kodlarından biri ERP aynasında bulunamadı.");
        var configurationMap=configurations.ToDictionary(x=>x.Id);
        if(request.Transfer.Lines.Any(x=>x.YapCodeId.HasValue&&configurationMap[x.YapCodeId.Value].StockId.HasValue&&
               configurationMap[x.YapCodeId.Value].StockId!=x.StockId))
            throw AppException.BadRequest("Seçilen yapılandırma kodu transfer satırındaki stoğa ait değil.");
    }

    private async Task<CreateProductionTransferDraftRequest> ApplyDefaultProductionTransferLocationAsync(
        CreateProductionTransferDraftRequest request,bool required,CancellationToken ct)
    {
        var branch=Branch(request.Transfer.BranchCode);
        var sourceSetting=await uow.Repository<WarehouseEntity>().Query()
            .Where(x=>x.Id==request.Transfer.SourceWarehouseId&&x.BranchCode==branch)
            .Select(x=>new{x.Id,x.WarehouseCode,x.ProductionPickingStagingLocationId,x.DefaultProductionTransferLocationId})
            .SingleOrDefaultAsync(ct)
            ??throw AppException.BadRequest("Üretime transfer kaynak deposu bulunamadı.");
        if(!sourceSetting.ProductionPickingStagingLocationId.HasValue)
            throw AppException.Conflict($"{sourceSetting.WarehouseCode} kaynak deposu için toplama sanal rafı tanımlanmamış.");
        var sourceStagingLocationId=sourceSetting.ProductionPickingStagingLocationId.Value;
        var validSourceStaging=await uow.Repository<WarehouseLocation>().Query().AnyAsync(x=>
            x.Id==sourceStagingLocationId&&x.WarehouseId==sourceSetting.Id&&x.IsActive&&x.IsPutaway,ct);
        if(!validSourceStaging)
            throw AppException.Conflict("Kaynak deponun toplama sanal rafı aktif ve yerleştirmeye uygun değil.");

        // Rafsız kaynak: tüm satırların DefaultSourceLocationId'si kaynak deponun
        // DefaultProductionTransferLocationId'sine sabitlenir (arama/rota yok).
        long? racklessSourceLocationId=null;
        if(await ProductionTransferWarehouseRacklessSupport.IsRacklessAsync(uow,sourceSetting.Id,ct))
        {
            if(!sourceSetting.DefaultProductionTransferLocationId.HasValue)
                throw AppException.Conflict($"{sourceSetting.WarehouseCode} kaynak deposu rafsız; varsayılan üretim transfer rafı tanımlanmamış.");
            var sourceDefaultLocationId=sourceSetting.DefaultProductionTransferLocationId.Value;
            var validSourceDefault=await uow.Repository<WarehouseLocation>().Query().AnyAsync(x=>
                x.Id==sourceDefaultLocationId&&x.WarehouseId==sourceSetting.Id&&x.IsActive&&x.IsPutaway,ct);
            if(!validSourceDefault)
                throw AppException.Conflict("Kaynak deponun varsayılan üretim transfer rafı aktif ve yerleştirmeye uygun değil.");
            racklessSourceLocationId=sourceDefaultLocationId;
        }

        var setting=await uow.Repository<WarehouseEntity>().Query()
            .Where(x=>x.Id==request.Transfer.TargetWarehouseId&&x.BranchCode==branch)
            .Select(x=>new{x.Id,x.WarehouseCode,x.DefaultProductionTransferLocationId})
            .SingleOrDefaultAsync(ct)
            ??throw AppException.BadRequest("Üretime transfer hedef deposu bulunamadı.");
        if(!setting.DefaultProductionTransferLocationId.HasValue)
        {
            if(!required)
            {
                // Hedef varsayılanı opsiyonel ve yok: hedef tarafına dokunma; kaynak tarafını (staging + rafsız kaynak) yine uygula.
                var sourceOnly=request.Transfer with
                {
                    SourceStagingLocationId=request.Transfer.SourceStagingLocationId??sourceStagingLocationId,
                    Lines=ApplySourceLineDefaults(request.Transfer.Lines,sourceStagingLocationId,racklessSourceLocationId)
                };
                return request with{Transfer=sourceOnly};
            }
            throw AppException.Conflict($"{setting.WarehouseCode} hedef deposu için varsayılan üretim transfer rafı tanımlanmamış.");
        }
        var locationId=setting.DefaultProductionTransferLocationId.Value;
        var valid=await uow.Repository<WarehouseLocation>().Query().AnyAsync(x=>
            x.Id==locationId&&x.WarehouseId==setting.Id&&x.IsActive&&x.IsPutaway,ct);
        if(!valid)
            throw AppException.Conflict("Hedef deponun varsayılan üretim transfer rafı aktif ve yerleştirmeye uygun değil.");

        var transfer=request.Transfer with
        {
            SourceStagingLocationId=request.Transfer.SourceStagingLocationId??sourceStagingLocationId,
            TargetPutawayLocationId=request.Transfer.TargetPutawayLocationId??locationId,
            Lines=ApplySourceLineDefaults(request.Transfer.Lines,sourceStagingLocationId,racklessSourceLocationId)
        };
        return request with{Transfer=transfer};
    }

    private static IReadOnlyList<WarehouseTransferLineDraftRequest> ApplySourceLineDefaults(
        IReadOnlyList<WarehouseTransferLineDraftRequest> lines,
        long sourceStagingLocationId,
        long? racklessSourceLocationId)
    {
        return lines.Select(x=>{
            var line=x.DefaultTargetLocationId.HasValue
                ?x
                :x with{DefaultTargetLocationId=sourceStagingLocationId};
            if(racklessSourceLocationId.HasValue)
                line=line with{DefaultSourceLocationId=racklessSourceLocationId};
            return line;
        }).ToArray();
    }

    public async Task<DefaultProductionTargetLocationDto> GetDefaultTargetLocationAsync(
        long warehouseId,string branchCode,CancellationToken ct=default)
    {
        var branch=Branch(branchCode);
        var warehouse=await uow.Repository<WarehouseEntity>().Query()
            .Where(x=>x.Id==warehouseId&&x.BranchCode==branch)
            .Select(x=>new{x.Id,x.DefaultProductionTransferLocationId})
            .SingleOrDefaultAsync(ct);
        if(warehouse?.DefaultProductionTransferLocationId is not long locationId)
            return new(null,null,null);
        var location=await uow.Repository<WarehouseLocation>().Query()
            .Where(x=>x.Id==locationId&&x.WarehouseId==warehouse.Id&&x.IsActive&&x.IsPutaway)
            .Select(x=>new{x.Code,x.Name}).SingleOrDefaultAsync(ct);
        return location is null?new(null,null,null):new(locationId,location.Code,location.Name);
    }

    private static void Validate(CreateProductionTransferDraftRequest request)
    {
        if(request.Transfer is null)throw AppException.BadRequest("Transfer gövdesi zorunludur.");
        if(request.LineContexts?.Any(x=>x.LineIndex<0||x.LineIndex>=request.Transfer.Lines.Count||x.RequiredQuantity<=0)==true)
            throw AppException.BadRequest("Üretim transfer kalem bağlamı geçersiz.");
        if(request.LineContexts?.GroupBy(x=>x.LineIndex).Any(x=>x.Count()>1)==true)
            throw AppException.BadRequest("Aynı transfer kalemine birden fazla üretim bağlamı atanamaz.");
    }

    private static void ValidatePolicy(CreateProductionTransferDraftRequest request,ProductionTransferPolicy policy)
    {
        if(request.TriggeredByProduction&&policy.RequireProductionOrderReference&&
           string.IsNullOrWhiteSpace(request.ProductionOrderNo)&&!request.ProductionOrderId.HasValue)
            throw AppException.BadRequest("Üretim emri referansı zorunludur.");
        if(!policy.AllowManualTransfer&&!request.TriggeredByProduction)
            throw AppException.BadRequest("Manuel üretim transferi politikada kapalıdır.");
        if(!policy.AllowAutomaticGeneration&&request.AutoGenerated)
            throw AppException.BadRequest("Otomatik üretim transferi politikada kapalıdır.");
        var taskBased=request.Transfer.InitiationMode is WarehouseTransferInitiationMode.OrderBasedTask or WarehouseTransferInitiationMode.StockBasedTask;
        if(policy.RequireTaskAssignment&&!taskBased)
            throw AppException.BadRequest("Üretime transferde emirli yürütme zorunludur. Kullanıcı transfer oluşturulduktan sonra atanabilir.");
        if(policy.RequireSourceProductionLocation&&!IsAutoAssignSources(request)&&request.Transfer.Lines.Any(x=>!x.DefaultSourceLocationId.HasValue))
            throw AppException.BadRequest("Üretime transferde kaynak raf zorunludur.");
        if(policy.RequireTargetProductionLocation&&request.Transfer.Lines.Any(x=>!x.DefaultTargetLocationId.HasValue))
            throw AppException.BadRequest("Üretime transferde üretim besleme/hedef rafı zorunludur.");
        if(!policy.AllowPartialSupply&&request.LineContexts?.Any(x=>
               request.Transfer.Lines[x.LineIndex].Quantity<x.RequiredQuantity)==true)
            throw AppException.BadRequest("Kısmi üretim beslemesi politikada kapalıdır.");
        if(!policy.AllowOverIssue&&request.LineContexts?.Any(x=>x.RequiredQuantity>0&&request.Transfer.Lines[x.LineIndex].Quantity>x.RequiredQuantity)==true)
            throw AppException.BadRequest("Talep miktarından fazla üretim transferi politikada kapalıdır.");
        if(policy.AllowOverIssue&&request.LineContexts?.Any(x=>x.RequiredQuantity>0
               && request.Transfer.Lines[x.LineIndex].Quantity>x.RequiredQuantity*(1+policy.OverIssueTolerancePercent/100m))==true)
            throw AppException.BadRequest("Üretim transfer miktarı fazla sarf toleransını aşıyor.");
    }

    private static bool IsAutoAssignSources(CreateProductionTransferDraftRequest request)=>
        request.AutoAssignSources||request.Transfer.AutoAssignSources;

    private async Task<CreateProductionTransferDraftRequest> AutoAssignSourceLocationsAndSerialsAsync(
        CreateProductionTransferDraftRequest request,CancellationToken ct)
    {
        var transfer=request.Transfer;
        var branch=transfer.BranchCode.Trim();
        var sourceWarehouseId=transfer.SourceWarehouseId;
        // Rafsız kaynakta DefaultSourceLocationId ApplyDefault ile sabitlenir; otomatik atama ezmesin.
        if(await ProductionTransferWarehouseRacklessSupport.IsRacklessAsync(uow,sourceWarehouseId,ct))
            return request;
        var excludedLocationIds=await ProductionTransferSourceLocationExclusions.FromTransferAsync(uow,transfer,ct);
        var locations=await uow.Repository<WarehouseLocation>().Query()
            .Where(x=>x.WarehouseId==sourceWarehouseId&&x.IsActive&&x.IsPickable&&!x.IsQuarantine)
            .ToDictionaryAsync(x=>x.Id,ct);
        var locationIds=locations.Keys.ToArray();
        var stockIds=transfer.Lines.Select(x=>x.StockId).Distinct().ToArray();
        var trackingPolicies=new Dictionary<long,EffectiveStockTrackingPolicy>();
        foreach(var stockId in stockIds)
            trackingPolicies[stockId]=await trackingPolicyResolver.ResolveAsync(branch,stockId,ct);
        var balances=(await uow.Repository<LocationStockBalance>().Query()
            .Where(x=>x.WarehouseId==sourceWarehouseId&&stockIds.Contains(x.StockId)
                &&locationIds.Contains(x.LocationId)&&x.StockStatus=="Available"&&x.AvailableQuantity>0)
            .ToListAsync(ct))
            .Where(x=>!excludedLocationIds.Contains(x.LocationId))
            .ToList();
        var movementEntries=await uow.Repository<StockMovementEntry>().Query()
            .Where(x=>x.WarehouseId==sourceWarehouseId&&stockIds.Contains(x.StockId)
                &&x.QuantityDelta>0&&x.SerialNo!=null)
            .ToListAsync(ct);
        var entryLookup=movementEntries
            .GroupBy(x=>(x.StockId,x.YapCodeId,SerialNo:x.SerialNo!.Trim().ToUpperInvariant()))
            .ToDictionary(g=>g.Key,g=>g.OrderBy(e=>e.OccurredAt).ThenBy(e=>e.Id).First());

        var filledLines=transfer.Lines.Select(line=>{
            var policy=trackingPolicies[line.StockId];
            return policy.TrackingType is StockTrackingType.Serial or StockTrackingType.LotAndSerial
                ?AssignSerialLine(line,balances,locations,entryLookup)
                :AssignNonSerialLine(line,balances,locations);
        }).ToArray();

        return request with{
            AutoAssignSources=true,
            Transfer=transfer with{AutoAssignSources=true,Lines=filledLines}
        };
    }

    private static WarehouseTransferLineDraftRequest AssignNonSerialLine(
        WarehouseTransferLineDraftRequest line,
        IReadOnlyCollection<LocationStockBalance> balances,
        IReadOnlyDictionary<long,WarehouseLocation> locations)
    {
        var best=balances.Where(x=>x.StockId==line.StockId&&x.YapCodeId==line.YapCodeId
                &&string.Equals(x.UnitCode,line.UnitCode,StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x=>x.AvailableQuantity)
            .ThenBy(x=>locations[x.LocationId].Code,StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        return line with{DefaultSourceLocationId=best?.LocationId};
    }

    private static WarehouseTransferLineDraftRequest AssignSerialLine(
        WarehouseTransferLineDraftRequest line,
        IReadOnlyCollection<LocationStockBalance> balances,
        IReadOnlyDictionary<long,WarehouseLocation> locations,
        IReadOnlyDictionary<(long StockId,long? YapCodeId,string SerialNo),StockMovementEntry> entryLookup)
    {
        var serialCandidates=balances.Where(x=>x.StockId==line.StockId&&x.YapCodeId==line.YapCodeId
                &&string.Equals(x.UnitCode,line.UnitCode,StringComparison.OrdinalIgnoreCase)
                &&!string.IsNullOrWhiteSpace(x.SerialNo))
            .GroupBy(x=>x.SerialNo!.Trim(),StringComparer.OrdinalIgnoreCase)
            .Select(g=>g.OrderByDescending(x=>x.AvailableQuantity)
                .ThenBy(x=>locations[x.LocationId].Code,StringComparer.OrdinalIgnoreCase)
                .First())
            .Select(balance=>{
                var serial=balance.SerialNo!.Trim().ToUpperInvariant();
                entryLookup.TryGetValue((balance.StockId,balance.YapCodeId,serial),out var entry);
                return new{Balance=balance,Entry=entry};
            })
            .OrderBy(x=>x.Entry?.OccurredAt??DateTime.MaxValue)
            .ThenBy(x=>x.Entry?.Id??long.MaxValue)
            .ToArray();

        if(serialCandidates.Length==0)
            return line with{
                DefaultSourceLocationId=null,
                Trackings=[new WarehouseTransferTrackingDraftRequest(line.Quantity,null,null,null,null,null,null,null)]
            };

        var wholeUnits=(int)Math.Floor(line.Quantity);
        var trackings=new List<WarehouseTransferTrackingDraftRequest>();
        foreach(var candidate in serialCandidates.Take(wholeUnits))
            trackings.Add(new WarehouseTransferTrackingDraftRequest(1,null,null,candidate.Balance.SerialNo!.Trim(),null,null,
                candidate.Balance.LocationId,null));
        var remaining=line.Quantity-trackings.Sum(x=>x.Quantity);
        if(remaining>0)
            trackings.Add(new WarehouseTransferTrackingDraftRequest(remaining,null,null,null,null,null,null,null));

        long? defaultSource=trackings.Count(x=>x.SourceLocationId.HasValue)==1
            ?trackings.First(x=>x.SourceLocationId.HasValue).SourceLocationId
            :null;
        return line with{DefaultSourceLocationId=defaultSource,Trackings=trackings};
    }

    private static WarehouseTransferBusinessContext Context(ProductionTransferPurpose purpose)=>purpose switch{
        ProductionTransferPurpose.MaterialSupply=>WarehouseTransferBusinessContext.ProductionMaterialSupply,
        ProductionTransferPurpose.WorkInProgressMove=>WarehouseTransferBusinessContext.ProductionWipMove,
        ProductionTransferPurpose.OutputMove=>WarehouseTransferBusinessContext.ProductionOutputMove,
        _=>throw AppException.BadRequest("Üretim transfer amacı geçersiz.")
    };
    private static ProductionTransferLineRole DefaultRole(ProductionTransferPurpose purpose)=>purpose switch{
        ProductionTransferPurpose.MaterialSupply=>ProductionTransferLineRole.ConsumptionSupply,
        ProductionTransferPurpose.WorkInProgressMove=>ProductionTransferLineRole.WorkInProgress,
        _=>ProductionTransferLineRole.ProductionOutput
    };
    private static ProductionTransferPolicy Default(string branch)=>new(){BranchCode=branch,PolicyKey="DEFAULT",CreatedDate=DateTime.UtcNow};
    private static ProductionTransferPolicyDto Map(ProductionTransferPolicy x)=>new(x.Id,x.BranchCode,Convert.ToBase64String(x.RowVersion),
        x.ProductionOrderSource,x.WmsSourceSystemCode,x.RequireProductionOrderReference,
        x.AllowManualTransfer,x.RequireErpMasterDataForManualTransfer,x.AllowAutomaticGeneration,
        x.CheckMaterialAvailability,x.BlockOnShortage,x.RequireTaskAssignment,
        x.RequireSourceProductionLocation,x.RequireTargetProductionLocation,x.AllowPartialSupply,x.AllowOverIssue,
        x.OverIssueTolerancePercent,x.RequireApproval,x.ErpPostingPolicy,x.CancellationReturnPolicy,x.UpdatedBy,x.UpdatedDate);
    private static void EnsureRowVersion(byte[] current,string? supplied){
        byte[] expected;try{expected=Convert.FromBase64String(supplied??string.Empty);}
        catch(FormatException){throw AppException.BadRequest("Geçersiz eşzamanlılık anahtarı.");}
        if(!System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(current,expected))
            throw AppException.Conflict("Üretim transfer politikası başka bir kullanıcı tarafından değiştirildi. Sayfayı yenileyin.");
    }
    private static string Branch(string? value)=>string.IsNullOrWhiteSpace(value)?"0":value.Trim();
    private static string? Clean(string? value,int max){var result=value?.Trim();return string.IsNullOrEmpty(result)?null:result.Length<=max?result:result[..max];}

    private async Task<bool> ShouldDeleteDraftInsteadOfCancelAsync(
        WarehouseTransferHeader header,
        ProductionTransferHeaderLink link,
        CancellationToken ct)
    {
        if (ProductionWorkOrderTransferGrouping.IsUnlinkedProductionTransfer(link))
            return false;

        if (header.Status != WarehouseTransferStatus.Draft)
            return false;

        if (header.Lines.Any(line => !line.IsDeleted
                && ProductionWorkOrderMaterialAssignment.ResolveEffectivePickedQuantity(line) > 0))
            return false;

        return !await uow.Repository<StockMovementOperation>().Query()
            .AnyAsync(x => x.ReferenceType == "WarehouseTransfer" && x.ReferenceId == header.Id, ct);
    }

    private async Task<bool> ShouldReleaseUnlinkedDraftToAtanmayanlarAsync(
        WarehouseTransferHeader header,
        ProductionTransferHeaderLink link,
        CancellationToken ct)
    {
        if (!ProductionWorkOrderTransferGrouping.IsUnlinkedProductionTransfer(link))
            return false;

        if (header.Status != WarehouseTransferStatus.Draft)
            return false;

        if (header.Lines.Any(line => !line.IsDeleted
                && ProductionWorkOrderMaterialAssignment.ResolveEffectivePickedQuantity(line) > 0))
            return false;

        return !await uow.Repository<StockMovementOperation>().Query()
            .AnyAsync(x => x.ReferenceType == "WarehouseTransfer" && x.ReferenceId == header.Id, ct);
    }
}
