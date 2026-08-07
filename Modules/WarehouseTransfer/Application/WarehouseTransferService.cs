using Microsoft.EntityFrameworkCore;
using verii_wms_api_v2.Modules.Audit.Application;
using verii_wms_api_v2.Modules.DocumentSeries.Application;
using verii_wms_api_v2.Modules.DocumentSeries.Domain;
using verii_wms_api_v2.Modules.Location.Domain;
using verii_wms_api_v2.Modules.Identity.Domain;
using verii_wms_api_v2.Modules.Identity.Application;
using verii_wms_api_v2.Modules.Stock.Application;
using verii_wms_api_v2.Modules.StockBalance.Domain;
using verii_wms_api_v2.Modules.StockTracking.Application;
using verii_wms_api_v2.Modules.WarehouseOperations.Domain;
using verii_wms_api_v2.Modules.WarehouseTransfer.Domain;
using verii_wms_api_v2.Shared;
using verii_wms_api_v2.Shared.Application.Abstractions.Persistence;
using verii_wms_api_v2.Shared.Application.Exceptions;
using StockEntity=verii_wms_api_v2.Modules.Stock.Domain.Stock;
using WarehouseEntity=verii_wms_api_v2.Modules.Warehouse.Domain.Warehouse;
using YapCodeEntity=verii_wms_api_v2.Modules.YapCode.Domain.YapCode;

namespace verii_wms_api_v2.Modules.WarehouseTransfer.Application;

public sealed class WarehouseTransferService(IUnitOfWork uow,IWarehouseTransferPolicyService policyService,IDocumentNumberAllocator numberAllocator,IAuditLogWriter audit,IWarehouseTransferReservationService reservations,IStockTrackingPolicyResolver trackingPolicyResolver):IWarehouseTransferService
{
    private IGenericRepository<WarehouseTransferHeader> Headers=>uow.Repository<WarehouseTransferHeader>();

    public Task<CreateWarehouseTransferDraftResult> CreateDraftAsync(CreateWarehouseTransferDraftRequest request,long actor,CancellationToken ct=default)
    {
        ValidateEnvelope(request);
        return uow.ExecuteInTransactionAsync(async token=>{
            var existing=await Headers.Query().Include(x=>x.Lines).FirstOrDefaultAsync(x=>x.CorrelationId==request.IdempotencyKey,token);
            if(existing is not null){var replayTask=await uow.Repository<WarehouseTransferTask>().Query().Where(x=>x.WtHeaderId==existing.Id&&x.TaskType==WarehouseTransferTaskType.Pick).OrderBy(x=>x.Id).FirstOrDefaultAsync(token);return new(existing.Id,existing.DocumentNo,existing.Lines.Count,existing.Lines.Sum(x=>x.RequestedQuantity),true,replayTask?.Id,replayTask?.TaskNo);}

            var branch=request.BranchCode.Trim();
            await UserWarehouseAccessService.EnsureAsync(
                uow, actor, branch, [request.SourceWarehouseId, request.TargetWarehouseId], token);
            var policy=await policyService.GetAsync(branch,token);
            ValidateMode(request,policy);
            var taskBased=request.InitiationMode is WarehouseTransferInitiationMode.OrderBasedTask or WarehouseTransferInitiationMode.StockBasedTask;
            var orderBased=request.InitiationMode is WarehouseTransferInitiationMode.OrderBasedTask or WarehouseTransferInitiationMode.OrderBasedDirectTransfer;
            var assigneeIds=(request.AssignedUserIds??[]).Distinct().ToArray();
            var productionContext=request.BusinessContext is WarehouseTransferBusinessContext.ProductionMaterialSupply
                or WarehouseTransferBusinessContext.ProductionWipMove
                or WarehouseTransferBusinessContext.ProductionOutputMove;
            if(taskBased&&policy.RequireAssigneeForTask&&assigneeIds.Length==0&&!productionContext)
                throw AppException.BadRequest("Emirli transferde en az bir kullanıcı atanmalıdır.");
            if(!policy.AllowMultipleAssignees&&assigneeIds.Length>1)throw AppException.BadRequest("Transfer politikası birden fazla kullanıcı atamasına izin vermiyor.");
            if(assigneeIds.Length>0){
                var activeUsers=await uow.Repository<User>().Query().CountAsync(x=>assigneeIds.Contains(x.Id)&&x.IsActive,token);
                if(activeUsers!=assigneeIds.Length)throw AppException.BadRequest("Atanan kullanıcılardan biri bulunamadı veya aktif değil.");
            }
            if(orderBased&&request.Lines.Any(x=>x.Source is null))throw AppException.BadRequest("Siparişli transferde her kalemin Netsis kaynak satırı olmalıdır.");
            if(!orderBased&&request.Lines.Any(x=>x.Source is not null))throw AppException.BadRequest("Siparişsiz transferde Netsis kaynak satırı gönderilemez.");
            var warehouseIds=new[]{request.SourceWarehouseId,request.TargetWarehouseId}.Distinct().ToArray();
            var warehouses=await uow.Repository<WarehouseEntity>().Query().Where(x=>warehouseIds.Contains(x.Id)&&x.BranchCode==branch).ToDictionaryAsync(x=>x.Id,token);
            if(warehouses.Count!=warehouseIds.Length)throw AppException.BadRequest("Kaynak veya hedef depo bulunamadı.");

            var locationIds=request.Lines.SelectMany(x=>new long?[]{x.DefaultSourceLocationId,x.DefaultTargetLocationId})
                .Concat([request.SourceStagingLocationId,request.TargetReceivingLocationId,request.TargetPutawayLocationId])
                .Where(x=>x.HasValue).Select(x=>x!.Value).Distinct().ToArray();
            var locations=await uow.Repository<WarehouseLocation>().Query().Where(x=>locationIds.Contains(x.Id)&&x.IsActive).ToDictionaryAsync(x=>x.Id,token);
            if(locations.Count!=locationIds.Length)throw AppException.BadRequest("Seçilen raflardan biri bulunamadı veya aktif değil.");
            ValidateLocations(request,locations);

            var stockIds=request.Lines.Select(x=>x.StockId).Distinct().ToArray();
            var stocks=await uow.Repository<StockEntity>().Query().Where(x=>stockIds.Contains(x.Id)&&x.BranchCode==branch).ToDictionaryAsync(x=>x.Id,token);
            if(orderBased&&request.Lines.Where(x=>x.Source is not null).Any(x=>stocks.TryGetValue(x.StockId,out var stock)&&!string.Equals(stock.ErpStockCode,x.Source!.ExternalStockCode,StringComparison.OrdinalIgnoreCase)))
                throw AppException.BadRequest("Sipariş kaynak satırındaki stok ile seçilen ERP mirror stoku eşleşmiyor.");
            if(stocks.Count!=stockIds.Length)throw AppException.BadRequest("Seçilen stoklardan biri ERP mirror tablosunda bulunamadı.");
            var trackingPolicies=new Dictionary<long,EffectiveStockTrackingPolicy>();
            foreach(var stockId in stockIds)
                trackingPolicies[stockId]=await trackingPolicyResolver.ResolveAsync(branch,stockId,token);
            var yapIds=request.Lines.Where(x=>x.YapCodeId.HasValue).Select(x=>x.YapCodeId!.Value).Distinct().ToArray();
            var yaps=await uow.Repository<YapCodeEntity>().Query().Where(x=>yapIds.Contains(x.Id)&&x.BranchCode==branch).ToDictionaryAsync(x=>x.Id,token);
            if(yaps.Count!=yapIds.Length)throw AppException.BadRequest("Seçilen yapı kodlarından biri ERP mirror tablosunda bulunamadı.");
            ValidateTrackings(request,trackingPolicies,request.AutoAssignSources);
            await ValidateSerialSourceBalancesAsync(request,trackingPolicies,stocks,token);

            var allocated=await numberAllocator.AllocateAsync(request.DocumentSeriesId,DocumentType(request.BusinessContext),DateTime.UtcNow,token);
            var now=DateTime.UtcNow;
            var header=new WarehouseTransferHeader{
                BranchCode=branch,CreatedBy=actor,CreatedDate=now,DocumentSeriesId=allocated.DocumentSeriesId,DocumentNo=allocated.DocumentNumber,
                DocumentDate=request.DocumentDate,BusinessContext=request.BusinessContext,InitiationMode=request.InitiationMode,ProcessType=request.ProcessType,SourceSystem=orderBased?WarehouseOperationSourceSystem.Netsis:WarehouseOperationSourceSystem.Manual,
                CorrelationId=request.IdempotencyKey,ExternalReferenceNo=Clean(request.ExternalReferenceNo,100),ProjectCode=Clean(request.ProjectCode,50),
                SourceWarehouseId=request.SourceWarehouseId,TargetWarehouseId=request.TargetWarehouseId,
                SourceStagingLocationId=request.SourceStagingLocationId,TargetReceivingLocationId=request.TargetReceivingLocationId,TargetPutawayLocationId=request.TargetPutawayLocationId,
                Status=WarehouseTransferStatus.Draft,ErpIntegrationStatus=ErpIntegrationStatus.Pending,
                PlannedDispatchAtUtc=request.PlannedDispatchAtUtc?.ToUniversalTime(),PlannedArrivalAtUtc=request.PlannedArrivalAtUtc?.ToUniversalTime(),
                RequireApproval=policy.RequireApproval,ApprovalStatus=policy.RequireApproval?OperationApprovalStatus.Pending:OperationApprovalStatus.NotRequired,
                AllowPartialPicking=policy.AllowPartialPicking,AllowPartialShipment=policy.AllowPartialShipment,AllowPartialReceipt=policy.AllowPartialReceipt,
                RequireDestinationAcceptance=policy.RequireDestinationAcceptance,RequirePutaway=policy.RequirePutaway,
                CreateTransitInventory=policy.CreateTransitInventory,DiscrepancyPolicy=policy.DiscrepancyPolicy,
                CancellationReturnPolicy=policy.CancellationReturnPolicy,
                ReservationPolicy=policy.ReservationPolicy,DirectPostingPolicy=policy.DirectPostingPolicy,
                RequireAssignee=policy.RequireAssigneeForTask,RequireSourceLocation=policy.RequireSourceLocation,
                RequireTargetLocation=policy.RequireTargetLocation,RequireShipmentInformation=policy.RequireShipmentInformation,
                AutoRelease=policy.AutoReleaseTaskBased,MinimumFulfillmentPercent=policy.MinimumFulfillmentPercent,
                Priority=request.Priority,Description=Clean(request.Description,2000)
            };
            var documents=new Dictionary<string,WarehouseTransferSourceDocument>(StringComparer.OrdinalIgnoreCase);
            if(orderBased)foreach(var source in request.Lines.Select(x=>x.Source!).GroupBy(x=>x.OrderNumber,StringComparer.OrdinalIgnoreCase)){
                var first=source.First();
                var document=new WarehouseTransferSourceDocument{BranchCode=branch,CreatedBy=actor,CreatedDate=now,Header=header,
                    SourceSystem=WarehouseOperationSourceSystem.Netsis,SourceDocumentType="TransferOrder",
                    ExternalDocumentNo=first.OrderNumber.Trim(),ExternalDocumentId=first.OrderNumber.Trim(),
                    ExternalDocumentDate=first.OrderDate,ExternalStatus=first.ExternalStatus,LastSynchronizedAtUtc=DateTimeOffset.UtcNow};
                header.SourceDocuments.Add(document);documents[first.OrderNumber.Trim()]=document;
            }
            WarehouseTransferTask? pickTask=null;
            if(taskBased){
                pickTask=new WarehouseTransferTask{BranchCode=branch,CreatedBy=actor,CreatedDate=now,Header=header,
                    TaskNo=$"{allocated.DocumentNumber}-P01",TaskType=WarehouseTransferTaskType.Pick,WarehouseId=request.SourceWarehouseId,
                    Status=assigneeIds.Length>0?WarehouseTransferTaskStatus.Assigned:WarehouseTransferTaskStatus.Open,
                    Priority=request.Priority,PlannedAtUtc=request.PlannedDispatchAtUtc?.ToUniversalTime(),Description="Transfer toplama emri"};
                foreach(var userId in assigneeIds)pickTask.Assignments.Add(new WarehouseTransferTaskAssignment{BranchCode=branch,CreatedBy=actor,CreatedDate=now,
                    Task=pickTask,UserId=userId,IsPrimary=userId==assigneeIds[0],AssignedAtUtc=DateTimeOffset.UtcNow,AssignedBy=actor});
                header.Tasks.Add(pickTask);
            }
            var lineNo=0;
            foreach(var item in request.Lines){
                var stock=stocks[item.StockId];var yap=item.YapCodeId.HasValue?yaps[item.YapCodeId.Value]:null;
                var unit=StockUnitPolicy.Resolve(stock,item.UnitCode);
                var trackingPolicy=trackingPolicies[item.StockId];
                var effectiveTrackingType=trackingPolicy.TrackingType;
                var line=new WarehouseTransferLine{
                    BranchCode=branch,CreatedBy=actor,CreatedDate=now,LineNo=++lineNo,StockId=stock.Id,StockCodeSnapshot=stock.ErpStockCode,
                    StockNameSnapshot=stock.StockName,YapCodeId=yap?.Id,YapCodeSnapshot=yap?.ConfigurationCode,UnitCode=unit,
                    BaseUnitCode=unit,RequestedQuantity=item.Quantity,TrackingType=effectiveTrackingType,
                    RequireLot=trackingPolicy.RequireLot,
                    RequireSerial=trackingPolicy.RequireSerial,RequireHandlingUnit=item.RequireHandlingUnit,
                    SourceWarehouseId=request.SourceWarehouseId,TargetWarehouseId=request.TargetWarehouseId,
                    DefaultSourceLocationId=item.DefaultSourceLocationId,DefaultTargetLocationId=item.DefaultTargetLocationId,
                    SourceStockStatus=NormalizeStockStatus(item.SourceStockStatus),
                    TargetStockStatus=NormalizeStockStatus(item.TargetStockStatus),
                    Description=Clean(item.Description,1000),Status=WarehouseTransferLineStatus.Open
                };
                foreach(var tracking in item.Trackings??[]){
                    line.Trackings.Add(new WarehouseTransferTracking{
                        BranchCode=branch,CreatedBy=actor,CreatedDate=now,HandlingUnitNo=Clean(tracking.HandlingUnitNo,100),
                        LotNo=Clean(tracking.LotNo,100),SerialNo=Clean(tracking.SerialNo,200),ManufacturingDate=tracking.ManufacturingDate,
                        ExpirationDate=tracking.ExpirationDate,PlannedQuantity=tracking.Quantity,SourceLocationId=tracking.SourceLocationId??item.DefaultSourceLocationId,
                        TargetLocationId=tracking.TargetLocationId??item.DefaultTargetLocationId,Status=WarehouseTransferTrackingStatus.Planned
                    });
                }
                header.Lines.Add(line);
                if(item.Source is not null){
                    var source=item.Source;var document=documents[source.OrderNumber.Trim()];
                    line.Sources.Add(new WarehouseTransferLineSource{BranchCode=branch,CreatedBy=actor,CreatedDate=now,Line=line,SourceDocument=document,
                        ExternalLineId=source.ExternalLineId.Trim(),ExternalLineNo=source.ExternalLineNo,ExternalStockCode=source.ExternalStockCode.Trim(),
                        ExternalYapCode=Clean(source.ExternalYapCode,100),OrderedQuantity=source.OrderedQuantity,
                        PreviouslyTransferredQuantity=source.PreviouslyTransferredQuantity,AllocatedQuantity=item.Quantity,
                        UnitCode=unit,ExternalStatus=Clean(source.ExternalStatus,50)});
                }
                if(pickTask is not null)pickTask.Lines.Add(new WarehouseTransferTaskLine{BranchCode=branch,CreatedBy=actor,CreatedDate=now,
                    Task=pickTask,Line=line,PlannedQuantity=item.Quantity,SourceLocationId=item.DefaultSourceLocationId,
                    TargetLocationId=request.SourceStagingLocationId});
            }
            header.StatusHistory.Add(new WarehouseTransferStatusHistory{
                BranchCode=branch,CreatedBy=actor,CreatedDate=now,StatusArea=WarehouseTransferStatusArea.Operation,
                ToStatus=WarehouseTransferStatus.Draft.ToString(),ChangedAtUtc=DateTimeOffset.UtcNow,ChangedBy=actor,
                Description="Transfer taslağı oluşturuldu.",CorrelationId=request.IdempotencyKey
            });
            await Headers.AddAsync(header,token);await uow.SaveChangesAsync(token);
            if(header.ReservationPolicy==WarehouseTransferReservationPolicy.OnCreate)
            {
                await reservations.ReserveAsync(header,$"WT:{header.Id}:RESERVE:CREATE",actor,token);
                await uow.SaveChangesAsync(token);
            }
            var result=new CreateWarehouseTransferDraftResult(header.Id,header.DocumentNo,header.Lines.Count,header.Lines.Sum(x=>x.RequestedQuantity),false,pickTask?.Id,pickTask?.TaskNo);
            await audit.WriteAsync(new("warehouse-transfer.draft.create",nameof(WarehouseTransferHeader),header.Id.ToString(),"Succeeded","warehouse-transfer",NewValues:result,ChangedFields:["Header","SourceDocuments","Lines","Trackings","Task","Assignments"]),token);
            return result;
        },ct);
    }

    public Task<PagedResponse<WarehouseTransferGridRow>> GetPagedAsync(PagedRequest request,CancellationToken ct=default) =>
        GetPagedByContextAsync(request,[WarehouseTransferBusinessContext.InterWarehouse],ct);

    public async Task<PagedResponse<WarehouseTransferGridRow>> GetPagedByContextAsync(
        PagedRequest request,
        IReadOnlyCollection<WarehouseTransferBusinessContext> contexts,
        CancellationToken ct=default)
    {
        if(contexts.Count==0)throw AppException.BadRequest("En az bir transfer bağlamı seçilmelidir.");
        var search=request.Search?.Trim();var warehouses=uow.Repository<WarehouseEntity>().Query(ignoreQueryFilters:true);var lines=uow.Repository<WarehouseTransferLine>().Query();
        var baseQuery=from h in Headers.Query()
            join sw in warehouses on h.SourceWarehouseId equals sw.Id
            join tw in warehouses on h.TargetWarehouseId equals tw.Id
            where contexts.Contains(h.BusinessContext)
                && (string.IsNullOrWhiteSpace(search)||h.DocumentNo.Contains(search)||(h.ExternalReferenceNo!=null&&h.ExternalReferenceNo.Contains(search))
                ||sw.WarehouseName.Contains(search)||tw.WarehouseName.Contains(search)||h.BranchCode.Contains(search))
            select new {Header=h,Source=sw,Target=tw};
        var desc=string.Equals(request.SortDirection,"desc",StringComparison.OrdinalIgnoreCase);
        var sortBy=request.SortBy?.Trim();
        var sorted=sortBy?.ToLowerInvariant() switch{
            "id"=>desc?baseQuery.OrderByDescending(x=>x.Header.Id):baseQuery.OrderBy(x=>x.Header.Id),
            "documentno"=>desc?baseQuery.OrderByDescending(x=>x.Header.DocumentNo):baseQuery.OrderBy(x=>x.Header.DocumentNo),
            "documentdate"=>desc?baseQuery.OrderByDescending(x=>x.Header.DocumentDate):baseQuery.OrderBy(x=>x.Header.DocumentDate),
            "sourcewarehousecode"=>desc?baseQuery.OrderByDescending(x=>x.Source.WarehouseCode):baseQuery.OrderBy(x=>x.Source.WarehouseCode),
            "targetwarehousecode"=>desc?baseQuery.OrderByDescending(x=>x.Target.WarehouseCode):baseQuery.OrderBy(x=>x.Target.WarehouseCode),
            "linecount"=>desc?baseQuery.OrderByDescending(x=>lines.Count(l=>l.WtHeaderId==x.Header.Id)):baseQuery.OrderBy(x=>lines.Count(l=>l.WtHeaderId==x.Header.Id)),
            "requestedquantity"=>desc?baseQuery.OrderByDescending(x=>lines.Where(l=>l.WtHeaderId==x.Header.Id).Sum(l=>(decimal?)l.RequestedQuantity)??0):baseQuery.OrderBy(x=>lines.Where(l=>l.WtHeaderId==x.Header.Id).Sum(l=>(decimal?)l.RequestedQuantity)??0),
            "pickedquantity"=>desc?baseQuery.OrderByDescending(x=>lines.Where(l=>l.WtHeaderId==x.Header.Id).Sum(l=>(decimal?)l.PickedQuantity)??0):baseQuery.OrderBy(x=>lines.Where(l=>l.WtHeaderId==x.Header.Id).Sum(l=>(decimal?)l.PickedQuantity)??0),
            "shippedquantity"=>desc?baseQuery.OrderByDescending(x=>lines.Where(l=>l.WtHeaderId==x.Header.Id).Sum(l=>(decimal?)l.ShippedQuantity)??0):baseQuery.OrderBy(x=>lines.Where(l=>l.WtHeaderId==x.Header.Id).Sum(l=>(decimal?)l.ShippedQuantity)??0),
            "receivedquantity"=>desc?baseQuery.OrderByDescending(x=>lines.Where(l=>l.WtHeaderId==x.Header.Id).Sum(l=>(decimal?)l.ReceivedQuantity)??0):baseQuery.OrderBy(x=>lines.Where(l=>l.WtHeaderId==x.Header.Id).Sum(l=>(decimal?)l.ReceivedQuantity)??0),
            "putawayquantity"=>desc?baseQuery.OrderByDescending(x=>lines.Where(l=>l.WtHeaderId==x.Header.Id).Sum(l=>(decimal?)l.PutawayQuantity)??0):baseQuery.OrderBy(x=>lines.Where(l=>l.WtHeaderId==x.Header.Id).Sum(l=>(decimal?)l.PutawayQuantity)??0),
            "status"=>desc?baseQuery.OrderByDescending(x=>x.Header.Status):baseQuery.OrderBy(x=>x.Header.Status),
            "priority"=>desc?baseQuery.OrderByDescending(x=>x.Header.Priority):baseQuery.OrderBy(x=>x.Header.Priority),
            "planneddispatchatutc"=>desc?baseQuery.OrderByDescending(x=>x.Header.PlannedDispatchAtUtc):baseQuery.OrderBy(x=>x.Header.PlannedDispatchAtUtc),
            "plannedarrivalatutc"=>desc?baseQuery.OrderByDescending(x=>x.Header.PlannedArrivalAtUtc):baseQuery.OrderBy(x=>x.Header.PlannedArrivalAtUtc),
            "createdby"=>desc?baseQuery.OrderByDescending(x=>x.Header.CreatedBy):baseQuery.OrderBy(x=>x.Header.CreatedBy),
            "updatedby"=>desc?baseQuery.OrderByDescending(x=>x.Header.UpdatedBy):baseQuery.OrderBy(x=>x.Header.UpdatedBy),
            "updateddate"=>desc?baseQuery.OrderByDescending(x=>x.Header.UpdatedDate):baseQuery.OrderBy(x=>x.Header.UpdatedDate),
            _=>desc?baseQuery.OrderByDescending(x=>x.Header.CreatedDate):baseQuery.OrderBy(x=>x.Header.CreatedDate)
        };
        var stableSorted=desc?sorted.ThenByDescending(x=>x.Header.Id):sorted.ThenBy(x=>x.Header.Id);
        var query=from item in stableSorted
            let h=item.Header
            let sw=item.Source
            let tw=item.Target
            select new WarehouseTransferGridRow(h.Id,h.BranchCode,h.DocumentNo,h.DocumentDate,h.BusinessContext,h.InitiationMode,h.ProcessType,h.Status,h.ApprovalStatus,h.ErpIntegrationStatus,
                h.SourceWarehouseId,sw.WarehouseCode,sw.WarehouseName,h.TargetWarehouseId,tw.WarehouseCode,tw.WarehouseName,
                lines.Count(x=>x.WtHeaderId==h.Id),lines.Where(x=>x.WtHeaderId==h.Id).Sum(x=>(decimal?)x.RequestedQuantity)??0,
                lines.Where(x=>x.WtHeaderId==h.Id).Sum(x=>(decimal?)x.PickedQuantity)??0,lines.Where(x=>x.WtHeaderId==h.Id).Sum(x=>(decimal?)x.ShippedQuantity)??0,
                lines.Where(x=>x.WtHeaderId==h.Id).Sum(x=>(decimal?)x.ReceivedQuantity)??0,lines.Where(x=>x.WtHeaderId==h.Id).Sum(x=>(decimal?)x.PutawayQuantity)??0,
                h.Priority,h.PlannedDispatchAtUtc,h.PlannedArrivalAtUtc,h.CreatedBy,h.CreatedDate,h.UpdatedBy,h.UpdatedDate);
        return await query.ApplyAdvancedFilters(request).ToPagedResponseAsync(request,ct);
    }

    public async Task<WarehouseTransferDetail> GetDetailAsync(long id,CancellationToken ct=default)
    {
        var warehouses=uow.Repository<WarehouseEntity>().Query(ignoreQueryFilters:true);
        var transferLines=uow.Repository<WarehouseTransferLine>().Query();
        var header=await (
            from h in Headers.Query()
            join sw in warehouses on h.SourceWarehouseId equals sw.Id
            join tw in warehouses on h.TargetWarehouseId equals tw.Id
            where h.Id==id
            select new WarehouseTransferGridRow(h.Id,h.BranchCode,h.DocumentNo,h.DocumentDate,h.BusinessContext,h.InitiationMode,h.ProcessType,h.Status,h.ApprovalStatus,h.ErpIntegrationStatus,
                h.SourceWarehouseId,sw.WarehouseCode,sw.WarehouseName,h.TargetWarehouseId,tw.WarehouseCode,tw.WarehouseName,
                transferLines.Count(x=>x.WtHeaderId==h.Id),transferLines.Where(x=>x.WtHeaderId==h.Id).Sum(x=>(decimal?)x.RequestedQuantity)??0,
                transferLines.Where(x=>x.WtHeaderId==h.Id).Sum(x=>(decimal?)x.PickedQuantity)??0,transferLines.Where(x=>x.WtHeaderId==h.Id).Sum(x=>(decimal?)x.ShippedQuantity)??0,
                transferLines.Where(x=>x.WtHeaderId==h.Id).Sum(x=>(decimal?)x.ReceivedQuantity)??0,transferLines.Where(x=>x.WtHeaderId==h.Id).Sum(x=>(decimal?)x.PutawayQuantity)??0,
                h.Priority,h.PlannedDispatchAtUtc,h.PlannedArrivalAtUtc,h.CreatedBy,h.CreatedDate,h.UpdatedBy,h.UpdatedDate))
            .SingleOrDefaultAsync(ct)??throw AppException.NotFound("Transfer kaydı bulunamadı.");
        var lineRows=await uow.Repository<WarehouseTransferLine>().Query().Where(x=>x.WtHeaderId==id).OrderBy(x=>x.LineNo)
            .Select(x=>new{
                x.Id,x.LineNo,x.StockId,x.StockCodeSnapshot,x.StockNameSnapshot,x.YapCodeId,x.YapCodeSnapshot,
                x.UnitCode,x.RequestedQuantity,x.ReservedQuantity,x.PickedQuantity,x.ShippedQuantity,x.ReceivedQuantity,x.PutawayQuantity,x.DamagedQuantity,x.LostQuantity,
                x.TrackingType,x.Status,TrackingCount=x.Trackings.Count,x.DefaultSourceLocationId,x.DefaultTargetLocationId,
                Trackings=x.Trackings.Where(t=>t.Status!=WarehouseTransferTrackingStatus.Cancelled).Select(t=>new WarehouseTransferTrackingLineDto(
                    t.Id,t.HandlingUnitNo,t.LotNo,t.SerialNo,t.ManufacturingDate,t.ExpirationDate,
                    t.PlannedQuantity,t.PickedQuantity,t.ShippedQuantity,t.ReceivedQuantity,t.PutawayQuantity,t.Status)).ToList()})
            .ToListAsync(ct);
        var locationIds=lineRows.SelectMany(x=>new[]{x.DefaultSourceLocationId,x.DefaultTargetLocationId}).Where(x=>x.HasValue).Select(x=>x!.Value).Distinct().ToList();
        var locationLookup=locationIds.Count==0?new Dictionary<long,WarehouseLocation>():await uow.Repository<WarehouseLocation>()
            .Query(ignoreQueryFilters:true).Where(x=>locationIds.Contains(x.Id)).ToDictionaryAsync(x=>x.Id,ct);
        (string? Code,string? Name) LocationLabel(long? locationId)=>locationId.HasValue&&locationLookup.TryGetValue(locationId.Value,out var loc)?(loc.Code,loc.Name):(null,null);
        var lines=lineRows.Select(x=>{
            var source=LocationLabel(x.DefaultSourceLocationId);
            var target=LocationLabel(x.DefaultTargetLocationId);
            return new WarehouseTransferDetailLine(x.Id,x.LineNo,x.StockId,x.StockCodeSnapshot,x.StockNameSnapshot,x.YapCodeId,x.YapCodeSnapshot,
                x.UnitCode,x.RequestedQuantity,x.ReservedQuantity,x.PickedQuantity,x.ShippedQuantity,x.ReceivedQuantity,x.PutawayQuantity,x.DamagedQuantity,x.LostQuantity,
                x.TrackingType,x.Status,x.TrackingCount,x.Trackings,
                x.DefaultSourceLocationId,source.Code,source.Name,x.DefaultTargetLocationId,target.Code,target.Name);
        }).ToList();
        var draft=await Headers.Query().Where(x=>x.Id==id).Select(x=>new{x.RowVersion,x.SourceStagingLocationId,x.TargetReceivingLocationId,x.TargetPutawayLocationId,x.ExternalReferenceNo,x.Description,x.ProjectCode}).SingleAsync(ct);
        return new(header,lines,Convert.ToBase64String(draft.RowVersion),new(draft.SourceStagingLocationId,draft.TargetReceivingLocationId,draft.TargetPutawayLocationId,draft.ExternalReferenceNo,draft.Description,draft.ProjectCode));
    }

    public async Task<WarehouseTransferDetail> GetDetailForContextAsync(
        long id,
        IReadOnlyCollection<WarehouseTransferBusinessContext> contexts,
        CancellationToken ct=default)
    {
        await EnsureContextAsync(id,contexts,ct);
        return await GetDetailAsync(id,ct);
    }

    public async Task EnsureContextAsync(
        long id,
        IReadOnlyCollection<WarehouseTransferBusinessContext> contexts,
        CancellationToken ct=default)
    {
        if(id<=0||contexts.Count==0)throw AppException.BadRequest("Transfer bağlamı geçersiz.");
        var context=await Headers.Query().Where(x=>x.Id==id)
            .Select(x=>(WarehouseTransferBusinessContext?)x.BusinessContext).SingleOrDefaultAsync(ct);
        if(!context.HasValue)throw AppException.NotFound("Transfer kaydı bulunamadı.");
        if(!contexts.Contains(context.Value))throw AppException.NotFound("Transfer kaydı bu operasyon modülüne ait değil.");
    }

    public Task<WarehouseTransferDetail> UpdateDraftAsync(long id,UpdateWarehouseTransferDraftRequest request,long actor,CancellationToken ct=default)=>
        uow.ExecuteInTransactionAsync(async token=>{
            if(id<=0||request.Priority is <1 or >5)throw AppException.BadRequest("Transfer ve öncelik bilgisi geçersiz.");
            var header=await Headers.Query().FirstOrDefaultAsync(x=>x.Id==id,token)??throw AppException.NotFound("Transfer kaydı bulunamadı.");
            if(header.Status!=WarehouseTransferStatus.Draft)throw AppException.Conflict("Yalnızca taslak transfer bilgileri güncellenebilir.");
            EnsureRowVersion(header.RowVersion,request.RowVersion);
            var locationIds=new long?[]{request.SourceStagingLocationId,request.TargetReceivingLocationId,request.TargetPutawayLocationId}
                .Where(x=>x.HasValue).Select(x=>x!.Value).Distinct().ToArray();
            var locations=await uow.Repository<WarehouseLocation>().Query().Where(x=>locationIds.Contains(x.Id)&&x.IsActive).ToDictionaryAsync(x=>x.Id,token);
            if(locations.Count!=locationIds.Length)throw AppException.BadRequest("Seçilen raflardan biri bulunamadı veya aktif değil.");
            if(request.SourceStagingLocationId.HasValue&&locations[request.SourceStagingLocationId.Value].WarehouseId!=header.SourceWarehouseId)
                throw AppException.BadRequest("Hazırlık rafı kaynak depoya ait olmalıdır.");
            foreach(var targetId in new[]{request.TargetReceivingLocationId,request.TargetPutawayLocationId}.Where(x=>x.HasValue).Select(x=>x!.Value))
                if(locations[targetId].WarehouseId!=header.TargetWarehouseId)throw AppException.BadRequest("Kabul ve yerleştirme rafları hedef depoya ait olmalıdır.");
            if(request.PlannedDispatchAtUtc.HasValue&&request.PlannedArrivalAtUtc.HasValue&&request.PlannedArrivalAtUtc<request.PlannedDispatchAtUtc)
                throw AppException.BadRequest("Planlanan varış zamanı sevk zamanından önce olamaz.");
            var old=new{header.DocumentDate,header.SourceStagingLocationId,header.TargetReceivingLocationId,header.TargetPutawayLocationId,header.PlannedDispatchAtUtc,header.PlannedArrivalAtUtc,header.Priority,header.ExternalReferenceNo,header.Description,header.ProjectCode};
            header.DocumentDate=request.DocumentDate;header.SourceStagingLocationId=request.SourceStagingLocationId;
            header.TargetReceivingLocationId=request.TargetReceivingLocationId;header.TargetPutawayLocationId=request.TargetPutawayLocationId;
            header.PlannedDispatchAtUtc=request.PlannedDispatchAtUtc?.ToUniversalTime();header.PlannedArrivalAtUtc=request.PlannedArrivalAtUtc?.ToUniversalTime();
            header.Priority=request.Priority;header.ExternalReferenceNo=Clean(request.ExternalReferenceNo,100);header.Description=Clean(request.Description,2000);header.ProjectCode=Clean(request.ProjectCode,50);
            header.UpdatedBy=actor;header.UpdatedDate=DateTime.UtcNow;
            try{await uow.SaveChangesAsync(token);}catch(DbUpdateConcurrencyException){throw AppException.Conflict("Transfer başka bir kullanıcı tarafından değiştirildi. Listeyi yenileyip tekrar deneyin.");}
            await audit.WriteAsync(new("warehouse-transfer.draft.update",nameof(WarehouseTransferHeader),id.ToString(),"Succeeded","warehouse-transfer",OldValues:old,
                NewValues:new{header.DocumentDate,header.SourceStagingLocationId,header.TargetReceivingLocationId,header.TargetPutawayLocationId,header.PlannedDispatchAtUtc,header.PlannedArrivalAtUtc,header.Priority,header.ExternalReferenceNo,header.Description,header.ProjectCode},
                ChangedFields:["Header"]),token);
            return await GetDetailAsync(id,token);
        },ct);

    public Task DeleteDraftAsync(long id,long actor,CancellationToken ct=default)=>
        uow.ExecuteInTransactionAsync(async token=>{
            var header=await Headers.Query().Include(x=>x.Lines).ThenInclude(x=>x.Trackings).FirstOrDefaultAsync(x=>x.Id==id,token)
                ??throw AppException.NotFound("Transfer kaydı bulunamadı.");
            if(header.Status!=WarehouseTransferStatus.Draft)throw AppException.Conflict("Yalnızca taslak transfer silinebilir. Başlatılmış transfer için iptal işlemini kullanın.");
            if(await uow.Repository<Modules.StockMovement.Domain.StockMovementOperation>().Query()
                .AnyAsync(x=>x.ReferenceType=="WarehouseTransfer"&&x.ReferenceId==id,token))
                throw AppException.Conflict("Stok hareketi bulunan transfer silinemez; iptal ve ters hareket kullanılmalıdır.");
            await reservations.ReleaseAllAsync(header,$"WT:{id}:RESERVE:DELETE","Taslak transfer silindi.",actor,token);
            var now=DateTime.UtcNow;var lineIds=header.Lines.Select(x=>x.Id).ToArray();
            var sourceIds=await uow.Repository<WarehouseTransferSourceDocument>().Query().Where(x=>x.WtHeaderId==id).Select(x=>x.Id).ToArrayAsync(token);
            var taskIds=await uow.Repository<WarehouseTransferTask>().Query().Where(x=>x.WtHeaderId==id).Select(x=>x.Id).ToArrayAsync(token);
            await SoftDelete(uow.Repository<WarehouseTransferLineSource>().Query(),x=>lineIds.Contains(x.WtLineId),actor,now,token);
            await SoftDelete(uow.Repository<WarehouseTransferTracking>().Query(),x=>lineIds.Contains(x.WtLineId),actor,now,token);
            await SoftDelete(uow.Repository<WarehouseTransferTaskAssignment>().Query(),x=>taskIds.Contains(x.WtTaskId),actor,now,token);
            await SoftDelete(uow.Repository<WarehouseTransferTaskLine>().Query(),x=>taskIds.Contains(x.WtTaskId),actor,now,token);
            await SoftDelete(uow.Repository<WarehouseTransferStatusHistory>().Query(),x=>x.WtHeaderId==id,actor,now,token);
            await SoftDelete(uow.Repository<WarehouseTransferTask>().Query(),x=>x.WtHeaderId==id,actor,now,token);
            await SoftDelete(uow.Repository<WarehouseTransferLine>().Query(),x=>x.WtHeaderId==id,actor,now,token);
            await SoftDelete(uow.Repository<WarehouseTransferSourceDocument>().Query(),x=>x.WtHeaderId==id,actor,now,token);
            header.IsDeleted=true;header.DeletedBy=actor;header.DeletedDate=now;await uow.SaveChangesAsync(token);
            await audit.WriteAsync(new("warehouse-transfer.draft.delete",nameof(WarehouseTransferHeader),id.ToString(),"Succeeded","warehouse-transfer",
                OldValues:new{header.DocumentNo,header.Status},ChangedFields:["IsDeleted"]),token);
            return true;
        },ct);

    private static void ValidateEnvelope(CreateWarehouseTransferDraftRequest r){
        if(r.IdempotencyKey==Guid.Empty)throw AppException.BadRequest("Idempotency anahtarı zorunludur.");
        if(string.IsNullOrWhiteSpace(r.BranchCode))throw AppException.BadRequest("Şube kodu zorunludur.");
        if(r.SourceWarehouseId<=0||r.TargetWarehouseId<=0)throw AppException.BadRequest("Kaynak ve hedef depo zorunludur.");
        if(r.BusinessContext==WarehouseTransferBusinessContext.InterWarehouse&&r.SourceWarehouseId==r.TargetWarehouseId)
            throw AppException.BadRequest("Depolar arası transferde kaynak ve hedef depo farklı olmalıdır.");
        if(r.SourceWarehouseId==r.TargetWarehouseId&&!r.AutoAssignSources){
            if(r.Lines.Any(x=>!x.DefaultSourceLocationId.HasValue))
                throw AppException.BadRequest("Aynı depo içindeki üretim/fason transferinde kaynak raf seçimi zorunludur.");
            if(r.Lines.Any(x=>!x.DefaultTargetLocationId.HasValue))
                throw AppException.BadRequest("Aynı depo içindeki üretim/fason transferinde hedef raf seçimi zorunludur.");
            if(r.Lines.Any(x=>x.DefaultSourceLocationId==x.DefaultTargetLocationId))
                throw AppException.BadRequest("Aynı depo içindeki üretim/fason transferinde kaynak ve hedef raf farklı olmalıdır.");
        }
        if(r.DocumentSeriesId<=0)throw AppException.BadRequest("Transfer belge serisi zorunludur.");
        if(r.Priority is <1 or >9)throw AppException.BadRequest("Öncelik 1-9 arasında olmalıdır.");
        if(r.Lines.Count==0)throw AppException.BadRequest("En az bir transfer satırı zorunludur.");
        if(r.Lines.Any(x=>x.StockId<=0||x.Quantity<=0))throw AppException.BadRequest("Stok ve pozitif miktar zorunludur.");
        if(r.PlannedDispatchAtUtc.HasValue&&r.PlannedArrivalAtUtc.HasValue&&r.PlannedArrivalAtUtc<r.PlannedDispatchAtUtc)throw AppException.BadRequest("Planlanan varış sevk zamanından önce olamaz.");
    }
    private static void ValidateMode(CreateWarehouseTransferDraftRequest r,WarehouseTransferPolicyDto p){
        var allowed=r.InitiationMode switch{
            WarehouseTransferInitiationMode.OrderBasedTask=>p.AllowOrderBasedTask,
            WarehouseTransferInitiationMode.StockBasedTask=>p.AllowStockBasedTask,
            WarehouseTransferInitiationMode.DirectTransfer=>p.AllowStockBasedDirect,
            WarehouseTransferInitiationMode.OrderBasedDirectTransfer=>p.AllowOrderBasedDirect,
            _=>false};
        if(!allowed)throw AppException.BadRequest("Seçilen sipariş/emir kombinasyonu transfer politikasında kapalıdır.");
        if(p.RequireSourceLocation&&!r.AutoAssignSources&&r.Lines.Any(x=>!x.DefaultSourceLocationId.HasValue))
            throw AppException.BadRequest("Transfer politikası kaynak rafı kalem bazında zorunlu tutuyor.");
        if(p.RequireTargetLocation&&r.Lines.Any(x=>!x.DefaultTargetLocationId.HasValue))
            throw AppException.BadRequest("Transfer politikası hedef rafı kalem bazında zorunlu tutuyor.");
        foreach(var line in r.Lines.Where(x=>x.Source is not null)){
            var source=line.Source!;
            if(string.IsNullOrWhiteSpace(source.OrderNumber)||string.IsNullOrWhiteSpace(source.ExternalLineId)||string.IsNullOrWhiteSpace(source.ExternalStockCode))
                throw AppException.BadRequest("Sipariş kaynak belge, satır ve stok bilgisi zorunludur.");
            if(line.Quantity>source.AvailableQuantity)throw AppException.BadRequest($"{source.OrderNumber}/{source.ExternalLineId} için miktar açık miktarı aşamaz.");
        }
    }
    private static void ValidateLocations(CreateWarehouseTransferDraftRequest r,IReadOnlyDictionary<long,WarehouseLocation> locations){
        bool Belongs(long? id,long warehouse)=>!id.HasValue||locations[id.Value].WarehouseId==warehouse;
        if(!Belongs(r.SourceStagingLocationId,r.SourceWarehouseId)||!Belongs(r.TargetReceivingLocationId,r.TargetWarehouseId)||!Belongs(r.TargetPutawayLocationId,r.TargetWarehouseId)
            ||r.Lines.Any(x=>!Belongs(x.DefaultSourceLocationId,r.SourceWarehouseId)||!Belongs(x.DefaultTargetLocationId,r.TargetWarehouseId)))
            throw AppException.BadRequest("Kaynak veya hedef raf seçilen depoyla eşleşmiyor.");
    }
    private static void ValidateTrackings(CreateWarehouseTransferDraftRequest r,IReadOnlyDictionary<long,EffectiveStockTrackingPolicy> policies,bool autoAssignSources){
        if(autoAssignSources)return;
        foreach(var line in r.Lines){
            var trackings=line.Trackings??[];
            var policy=policies[line.StockId];
            try{
                StockTrackingPolicyGuard.Validate(policy,line.Quantity,line.TrackingType,
                    trackings.Select(x=>new StockTrackingCapture(x.Quantity,x.LotNo,x.SerialNo,x.ManufacturingDate,x.ExpirationDate)).ToArray(),
                    requireCompleteCapture:policy.TrackingType!=StockTrackingType.None);
            }catch(StockTrackingPolicyViolationException ex){throw AppException.BadRequest(ex.Message);}
        }
    }
    private async Task ValidateSerialSourceBalancesAsync(
        CreateWarehouseTransferDraftRequest request,
        IReadOnlyDictionary<long,EffectiveStockTrackingPolicy> policies,
        IReadOnlyDictionary<long,StockEntity> stocks,
        CancellationToken ct)
    {
        var selections=request.Lines
            .SelectMany(line=>(line.Trackings??[])
                .Where(tracking=>!string.IsNullOrWhiteSpace(tracking.SerialNo))
                .Select(tracking=>new{Line=line,Tracking=tracking,LocationId=tracking.SourceLocationId??line.DefaultSourceLocationId}))
            .Where(x=>x.LocationId.HasValue)
            .ToArray();
        if(selections.Length==0)return;
        var stockIds=selections.Select(x=>x.Line.StockId).Distinct().ToArray();
        var locationIds=selections.Select(x=>x.LocationId!.Value).Distinct().ToArray();
        var serials=selections.Select(x=>x.Tracking.SerialNo!.Trim().ToUpperInvariant()).Distinct().ToArray();
        var rows=await uow.Repository<LocationStockBalance>().Query()
            .Where(x=>x.WarehouseId==request.SourceWarehouseId
                && stockIds.Contains(x.StockId)
                && locationIds.Contains(x.LocationId)
                && x.SerialNo!=null
                && serials.Contains(x.SerialNo))
            .Select(x=>new{x.StockId,x.YapCodeId,x.WarehouseId,x.LocationId,x.UnitCode,x.LotNo,x.SerialNo,x.StockStatus,x.AvailableQuantity})
            .ToListAsync(ct);
        var balances=rows
            .GroupBy(x=>WarehouseTransferSerialBalanceKey.Create(x.StockId,x.YapCodeId,x.WarehouseId,x.LocationId,x.UnitCode,x.LotNo,x.SerialNo!,x.StockStatus))
            .ToDictionary(x=>x.Key,x=>x.Sum(row=>row.AvailableQuantity));

        foreach(var selection in selections)
        {
            var line=selection.Line;
            var tracking=selection.Tracking;
            var policy=policies[line.StockId];
            var serial=tracking.SerialNo!.Trim().ToUpperInvariant();
            var unit=StockUnitPolicy.Resolve(stocks[line.StockId],line.UnitCode);
            var sourceStatus=NormalizeStockStatus(line.SourceStockStatus);
            var key=WarehouseTransferSerialBalanceKey.Create(line.StockId,line.YapCodeId,request.SourceWarehouseId,
                selection.LocationId!.Value,unit,tracking.LotNo,serial,sourceStatus);
            try
            {
                StockTrackingPolicyGuard.ValidateSerialMovementQuantity(policy,tracking.Quantity,balances.GetValueOrDefault(key),serial);
            }
            catch(StockTrackingPolicyViolationException ex)
            {
                throw AppException.Conflict(ex.Message);
            }
        }
    }
    private static WmsDocumentType DocumentType(WarehouseTransferBusinessContext context)=>context switch{
        WarehouseTransferBusinessContext.InterWarehouse=>WmsDocumentType.InterWarehouseTransfer,
        WarehouseTransferBusinessContext.ProductionMaterialSupply or WarehouseTransferBusinessContext.ProductionWipMove or WarehouseTransferBusinessContext.ProductionOutputMove=>WmsDocumentType.ProductionTransfer,
        WarehouseTransferBusinessContext.SubcontractingIssue or WarehouseTransferBusinessContext.SubcontractorToSubcontractor=>WmsDocumentType.SubcontractingIssue,
        WarehouseTransferBusinessContext.SubcontractingReceipt=>WmsDocumentType.SubcontractingReceipt,
        _=>throw AppException.BadRequest("Desteklenmeyen transfer bağlamı.")
    };
    private static string? Clean(string? value,int max){var v=value?.Trim();if(string.IsNullOrEmpty(v))return null;return v.Length<=max?v:v[..max];}
    private static string NormalizeStockStatus(string? value)
    {
        var status=Clean(value,40);
        return string.IsNullOrWhiteSpace(status)?"Available":status;
    }
    private static void EnsureRowVersion(byte[] current,string supplied){
        byte[] expected;try{expected=Convert.FromBase64String(supplied??string.Empty);}catch(FormatException){throw AppException.BadRequest("Geçersiz eşzamanlılık anahtarı.");}
        if(!System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(current,expected))throw AppException.Conflict("Transfer başka bir kullanıcı tarafından değiştirildi. Listeyi yenileyip tekrar deneyin.");
    }
    private static Task SoftDelete<TEntity>(IQueryable<TEntity> query,System.Linq.Expressions.Expression<Func<TEntity,bool>> predicate,long actor,DateTime now,CancellationToken ct)
        where TEntity:verii_wms_api_v2.Shared.Domain.BaseEntity=>query.Where(predicate).ExecuteUpdateAsync(x=>x.SetProperty(v=>v.IsDeleted,true).SetProperty(v=>v.DeletedBy,actor).SetProperty(v=>v.DeletedDate,now),ct);
}
