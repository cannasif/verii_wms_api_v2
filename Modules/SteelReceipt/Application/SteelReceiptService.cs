using System.Data;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using verii_wms_api_v2.Modules.Audit.Application;
using verii_wms_api_v2.Modules.DocumentSeries.Domain;
using verii_wms_api_v2.Modules.ErpIntegration.Application;
using verii_wms_api_v2.Modules.GoodsReceipt.Application;
using verii_wms_api_v2.Modules.GoodsReceipt.Domain;
using verii_wms_api_v2.Modules.Location.Domain;
using verii_wms_api_v2.Modules.SteelReceipt.Domain;
using verii_wms_api_v2.Modules.VehicleCheckIn.Domain;
using verii_wms_api_v2.Modules.StockMovement.Application;
using verii_wms_api_v2.Modules.StockMovement.Domain;
using verii_wms_api_v2.Shared;
using verii_wms_api_v2.Shared.Application.Abstractions.Persistence;
using verii_wms_api_v2.Shared.Application.Exceptions;
using verii_wms_api_v2.Shared.Application.Validation;
using verii_wms_api_v2.Shared.Infrastructure.Files;
using CustomerEntity=verii_wms_api_v2.Modules.Customer.Domain.Customer;
using DocumentSeriesEntity=verii_wms_api_v2.Modules.DocumentSeries.Domain.DocumentSeries;
using StockEntity=verii_wms_api_v2.Modules.Stock.Domain.Stock;
using WarehouseEntity=verii_wms_api_v2.Modules.Warehouse.Domain.Warehouse;

namespace verii_wms_api_v2.Modules.SteelReceipt.Application;

public sealed class SteelReceiptService(IUnitOfWork uow,IGoodsReceiptOperationsService grOperations,
    IGoodsReceiptErpPostingCoordinator erpPosting,
    IStockMovementService stockMovement,IAuditLogWriter audit,ISteelReceiptAttachmentStorage attachmentStorage):ISteelReceiptService
{
    private IGenericRepository<SteelReceiptPlan> Plans=>uow.Repository<SteelReceiptPlan>();
    private IGenericRepository<SteelReceiptPlanLine> Lines=>uow.Repository<SteelReceiptPlanLine>();

    public async Task<SteelImportPreview> PreviewAsync(PreviewSteelReceiptImportRequest request,CancellationToken ct=default)
    {
        var normalized=await ValidateImportAsync(request,ct);var keys=normalized.Select(x=>x.Key).ToArray();
        var stockIds=normalized.Where(x=>x.Stock is not null).Select(x=>x.Stock!.Id).Distinct().ToArray();
        var serials=normalized.Select(x=>x.Serial).Distinct().ToArray();
        var existingRows=await Lines.Query().Where(x=>keys.Contains(x.ExternalLineKey)
                ||stockIds.Contains(x.StockId)&&serials.Contains(x.SupplierSerialNo))
            .Select(x=>new{x.ExternalLineKey,x.StockId,x.SupplierSerialNo,x.DCode}).ToListAsync(ct);
        var existingByKey=existingRows.GroupBy(x=>x.ExternalLineKey).ToDictionary(x=>x.Key,x=>x.First());
        var existingBySerial=existingRows.GroupBy(x=>SerialKey(x.StockId,x.SupplierSerialNo))
            .ToDictionary(x=>x.Key,x=>x.First());
        var rows=normalized.Select(x=>{existingByKey.TryGetValue(x.Key,out var found);
            if(found is null&&x.Stock is not null)existingBySerial.TryGetValue(SerialKey(x.Stock.Id,x.Serial),out found);
            var errors=x.Errors.ToList();
            if(found is not null)errors.Add($"Bu levha daha önce {found.DCode} olarak içe aktarılmış.");
            return new SteelImportPreviewLine(x.Input.RowNumber,x.Serial,x.Stock?.ErpStockCode,found is null?"New":"Existing",found?.DCode,errors);}).ToList();
        return new(rows.Count,rows.Count(x=>x.Action=="New"),rows.Count(x=>x.Action=="Existing"),rows.Count(x=>x.Errors.Count>0),
            request.Lines.Sum(x=>x.ExpectedQuantity),rows);
    }

    public Task<long> CommitAsync(CommitSteelReceiptImportRequest request,long actor,CancellationToken ct=default)
    {
        if(request.IdempotencyKey==Guid.Empty)throw AppException.BadRequest("Idempotency anahtarı zorunludur.");
        return uow.ExecuteInTransactionAsync<long>(async token=>{
            var replay=await Plans.Query().FirstOrDefaultAsync(
                x=>x.CorrelationId==request.IdempotencyKey,token);
            if(replay is not null)return replay.Id;
            var import=request.Import;var normalized=await ValidateImportAsync(import,token);
            var errors=normalized.OrderBy(x=>x.Input.RowNumber).SelectMany(x=>x.Errors.Select(e=>$"Satır {x.Input.RowNumber}: {e}")).ToList();
            if(errors.Count>0)throw AppException.BadRequest(string.Join(" ",errors.Take(10)));
            var keys=normalized.Select(x=>x.Key).ToArray();
            var stockIds=normalized.Select(x=>x.Stock!.Id).Distinct().ToArray();
            var serials=normalized.Select(x=>x.Serial).Distinct().ToArray();
            var existingRows=await Lines.Query().Where(x=>keys.Contains(x.ExternalLineKey)
                    ||stockIds.Contains(x.StockId)&&serials.Contains(x.SupplierSerialNo))
                .Select(x=>new{x.ExternalLineKey,x.StockId,x.SupplierSerialNo,x.DCode}).ToListAsync(token);
            var duplicate=existingRows.FirstOrDefault(x=>keys.Contains(x.ExternalLineKey)
                ||normalized.Any(row=>row.Stock!.Id==x.StockId
                    &&string.Equals(row.Serial,x.SupplierSerialNo,StringComparison.OrdinalIgnoreCase)));
            if(duplicate is not null)throw AppException.Conflict($"Levha daha önce {duplicate.DCode} olarak içe aktarılmış.");
            var branch=import.BranchCode.Trim();
            if(await Plans.AnyAsync(x=>x.BranchCode==branch&&x.ImportReferenceNo==import.ImportReferenceNo.Trim(),token))
                throw AppException.Conflict("Bu aktarım referansı daha önce kullanılmış.");
            var supplier=await uow.Repository<CustomerEntity>().FindByIdAsync(import.SupplierId,false,token)
                ??throw AppException.BadRequest("Tedarikçi bulunamadı.");
            var vehicle=import.VehicleCheckInId.HasValue
                ?await uow.Repository<VehicleCheckInHeader>().FindByIdAsync(import.VehicleCheckInId.Value,true,token):null;
            if(import.VehicleCheckInId.HasValue&&(vehicle is null||vehicle.BranchCode!=branch))
                throw AppException.BadRequest("Seçilen araç giriş kaydı bu şubede bulunamadı.");
            var sourceWaybillNo=PurchaseWaybillNumberPolicy.Normalize(import.WaybillNo);
            var receivingLocationId=await ResolveImportReceivingLocationAsync(
                import.TargetWarehouseId,import.ReceivingLocationId,
                GoodsReceiptLocationSelectionPolicy.AnyActiveWarehouseLocation,token);
            var plan=Stamp(new SteelReceiptPlan{BranchCode=branch,CorrelationId=request.IdempotencyKey,
                ImportReferenceNo=Clean(import.ImportReferenceNo,100,true)!,SourceFileName=Clean(import.SourceFileName,260,true)!,
                ExportReferenceNo=Clean(import.ExportReferenceNo,100),VehicleCheckInId=vehicle?.Id,SupplierId=supplier.Id,SupplierCodeSnapshot=supplier.CustomerCode,
                SupplierNameSnapshot=supplier.CustomerName,TargetWarehouseId=import.TargetWarehouseId,ReceivingLocationId=receivingLocationId,
                DocumentSeriesId=import.DocumentSeriesId,WaybillNo=sourceWaybillNo,WaybillDate=import.WaybillDate,
                PlannedArrivalAtUtc=import.PlannedArrivalAtUtc?.ToUniversalTime(),Status=SteelReceiptPlanStatus.Imported,
                TotalLineCount=normalized.Count,TotalExpectedQuantity=normalized.Sum(x=>x.Input.ExpectedQuantity),
                ImportedAtUtc=DateTimeOffset.UtcNow,ImportedBy=actor},actor);
            if(vehicle is not null){vehicle.Status=VehicleCheckInStatus.LinkedToReceipt;vehicle.UpdatedBy=actor;vehicle.UpdatedDate=DateTime.UtcNow;}
            await Plans.AddAsync(plan,token);await uow.SaveChangesAsync(token);
            var year=DateTime.UtcNow.Year;
            var last=await Lines.Query(ignoreQueryFilters:true).Where(x=>x.DCode.StartsWith($"SAC-{year}-"))
                .OrderByDescending(x=>x.DCode).Select(x=>x.DCode).FirstOrDefaultAsync(token);
            var seq=int.TryParse(last?.Split('-').Last(),out var parsed)?parsed:0;
            var lineNo=0;
            foreach(var row in normalized.OrderBy(x=>x.Input.RowNumber)){var i=row.Input;
                plan.Lines.Add(Stamp(new SteelReceiptPlanLine{BranchCode=branch,Plan=plan,LineNo=++lineNo,DCode=$"SAC-{year}-{++seq:000000}",
                    ExternalLineKey=row.Key,NetsisOrderNo=Clean(i.NetsisOrderNo,50),NetsisOrderLineNo=Clean(i.NetsisOrderLineNo,50),
                    StockId=row.Stock!.Id,StockCodeSnapshot=row.Stock.ErpStockCode,StockNameSnapshot=row.Stock.StockName,
                    YapCodeId=row.YapCodeId,YapCodeSnapshot=row.YapCode,UnitCode=Clean(i.UnitCode,20,true)!.ToUpperInvariant(),
                    SupplierSerialNo=row.Serial,SecondarySerialNo=Clean(i.SecondarySerialNo,100),CombinedSize=Clean(i.CombinedSize,100),
                    MaterialGrade=Clean(i.MaterialGrade,100),HeatNumber=Clean(i.HeatNumber,100),CertificateNumber=Clean(i.CertificateNumber,100),
                    ExpectedQuantity=i.ExpectedQuantity,TargetWarehouseId=i.TargetWarehouseId??import.TargetWarehouseId,
                    ReceivingLocationId=i.ReceivingLocationId??receivingLocationId},actor));}
            await uow.SaveChangesAsync(token);
            await audit.WriteAsync(new("steel-receipt.import",nameof(SteelReceiptPlan),plan.Id.ToString(),"Succeeded","steel-receipt",
                NewValues:new{plan.ImportReferenceNo,plan.TotalLineCount,plan.TotalExpectedQuantity},ChangedFields:["Plan","Lines"]),token);
            return plan.Id;
        },ct,IsolationLevel.Serializable);
    }

    public async Task<PagedResponse<SteelReceiptPlanGridRow>> GetPlansPagedAsync(PagedRequest request,CancellationToken ct=default)
    {
        var vehicleHeaders=uow.Repository<VehicleCheckInHeader>().Query();
        var acceptances=uow.Repository<SteelVehicleAcceptance>().Query();
        var planLines=Lines.Query();
        var lineAggregates=from line in planLines
            group line by line.PlanId into grouped
            select new
            {
                PlanId=grouped.Key,
                AllConverted=!grouped.Any(x=>x.ConversionStatus!=SteelReceiptConversionStatus.Created),
                AnyConverted=grouped.Any(x=>x.ConversionStatus==SteelReceiptConversionStatus.Created),
                HasApproved=grouped.Any(x=>x.InspectionStatus==SteelInspectionStatus.Approved
                    ||x.InspectionStatus==SteelInspectionStatus.PartiallyApproved),
                HasPending=grouped.Any(x=>x.InspectionStatus==SteelInspectionStatus.Pending),
                AnyNonPendingInspection=grouped.Any(x=>x.InspectionStatus!=SteelInspectionStatus.Pending)
            };
        var joined=from p in Plans.Query() join w in uow.Repository<WarehouseEntity>().Query() on p.TargetWarehouseId equals w.Id
                   join agg in lineAggregates on p.Id equals agg.PlanId into aggregates
                   from agg in aggregates.DefaultIfEmpty()
                   join v in vehicleHeaders on p.VehicleCheckInId equals v.Id into vehicles
                   from v in vehicles.DefaultIfEmpty()
                   let resolvedStatus=p.Status==SteelReceiptPlanStatus.Cancelled?SteelReceiptPlanStatus.Cancelled:
                       agg==null?SteelReceiptPlanStatus.Imported:
                       agg.AllConverted?SteelReceiptPlanStatus.Converted:
                       agg.AnyConverted?SteelReceiptPlanStatus.PartiallyConverted:
                       agg.HasApproved&&agg.HasPending?SteelReceiptPlanStatus.PartiallyReadyForReceipt:
                       agg.HasApproved?SteelReceiptPlanStatus.ReadyForReceipt:
                       agg.AnyNonPendingInspection?SteelReceiptPlanStatus.InspectionInProgress:
                       SteelReceiptPlanStatus.Imported
                   select new {Plan=p,Warehouse=w,Vehicle=v,Status=resolvedStatus};
        var q=joined.Select(x=>new SteelReceiptPlanGridRow(x.Plan.Id,x.Plan.BranchCode,x.Plan.ImportReferenceNo,x.Plan.SourceFileName,
            x.Plan.ExportReferenceNo,x.Vehicle==null?null:x.Vehicle.Id,x.Vehicle==null?null:x.Vehicle.PlateNo,
            x.Vehicle==null?null:((x.Vehicle.DriverFirstName??"")+" "+(x.Vehicle.DriverLastName??"")).Trim(),x.Plan.SupplierId,
            x.Plan.SupplierCodeSnapshot,x.Plan.SupplierNameSnapshot,x.Plan.TargetWarehouseId,x.Warehouse.WarehouseCode,
            x.Warehouse.WarehouseName,x.Status,x.Plan.TotalLineCount,x.Plan.TotalExpectedQuantity,x.Plan.ImportedAtUtc,
            x.Plan.CreatedBy,x.Plan.CreatedDate,x.Plan.UpdatedBy,x.Plan.UpdatedDate));
        var s=request.Search?.Trim();q=q.Where(x=>string.IsNullOrWhiteSpace(s)||x.ImportReferenceNo.Contains(s)||x.SupplierCode.Contains(s)
            ||x.SupplierName.Contains(s)||(x.ExportReferenceNo!=null&&x.ExportReferenceNo.Contains(s)));
        var response=await q.ApplyAdvancedFilters(request).ApplySort(request,nameof(SteelReceiptPlanGridRow.ImportedAtUtc)).ToPagedResponseAsync(request,ct);
        return await EnrichLinkedPlanVehiclesAsync(response,vehicleHeaders,acceptances,planLines,ct);
    }

    private async Task<PagedResponse<SteelReceiptPlanGridRow>> EnrichLinkedPlanVehiclesAsync(
        PagedResponse<SteelReceiptPlanGridRow> response,
        IQueryable<VehicleCheckInHeader> vehicleHeaders,
        IQueryable<SteelVehicleAcceptance> acceptances,
        IQueryable<SteelReceiptPlanLine> planLines,
        CancellationToken ct)
    {
        var planIds=response.Items
            .Where(x=>!x.VehicleCheckInId.HasValue)
            .Select(x=>x.Id)
            .ToArray();
        if(planIds.Length==0)
            return response;

        var links=await(from line in planLines
            where planIds.Contains(line.PlanId)&&line.VehicleAcceptanceId!=null
            join acc in acceptances on line.VehicleAcceptanceId equals acc.Id
            join vehicle in vehicleHeaders on acc.VehicleCheckInId equals vehicle.Id
            select new{line.PlanId,acc.AcceptedAtUtc,vehicle}).ToListAsync(ct);
        var vehiclesByPlan=links
            .GroupBy(x=>x.PlanId)
            .ToDictionary(
                x=>x.Key,
                x=>x.OrderByDescending(link=>link.AcceptedAtUtc).First().vehicle);
        var items=response.Items.Select(row=>{
            if(row.VehicleCheckInId.HasValue||!vehiclesByPlan.TryGetValue(row.Id,out var vehicle))
                return row;
            return row with
            {
                VehicleCheckInId=vehicle.Id,
                VehiclePlateNo=vehicle.PlateNo,
                DriverName=((vehicle.DriverFirstName??"")+" "+(vehicle.DriverLastName??"")).Trim()
            };
        }).ToList();
        return new PagedResponse<SteelReceiptPlanGridRow>
        {
            Items=items,
            TotalCount=response.TotalCount,
            PageNumber=response.PageNumber,
            PageSize=response.PageSize
        };
    }

    public async Task<PagedResponse<SteelReceiptLineGridRow>> GetLinesPagedAsync(PagedRequest request,CancellationToken ct=default)
    {
        var q=GridQuery();var s=request.Search?.Trim();q=q.Where(x=>string.IsNullOrWhiteSpace(s)||x.DCode.Contains(s)||x.StockCode.Contains(s)
            ||x.SupplierSerialNo.Contains(s)||(x.NetsisOrderNo!=null&&x.NetsisOrderNo.Contains(s))||x.ImportReferenceNo.Contains(s));
        return await q.ApplyAdvancedFilters(request).ApplySort(request,nameof(SteelReceiptLineGridRow.Id)).ToPagedResponseAsync(request,ct);
    }

    public Task<PagedResponse<SteelReceiptLineGridRow>> GetReceiptCandidatesPagedAsync(PagedRequest request,CancellationToken ct=default)=>
        PageLinesAsync(GridQuery(Lines.Query().Where(x=>(x.InspectionStatus==SteelInspectionStatus.Approved||x.InspectionStatus==SteelInspectionStatus.PartiallyApproved)
            &&x.ApprovedQuantity>0&&x.ConversionStatus==SteelReceiptConversionStatus.NotCreated)),request,ct);

    public async Task<PagedResponse<SteelReceiptPendingSourceRow>> GetPendingReceiptSourcesPagedAsync(
        PagedRequest request,
        CancellationToken ct=default)
    {
        var query=Plans.Query()
            .Where(plan=>plan.Status!=SteelReceiptPlanStatus.Cancelled
                &&plan.Lines.Any(line=>line.ConversionStatus==SteelReceiptConversionStatus.NotCreated))
            .Select(plan=>new SteelReceiptPendingSourceRow(
                plan.Id,plan.BranchCode,plan.ImportReferenceNo,plan.SourceFileName,plan.WaybillNo,plan.WaybillDate,
                plan.SupplierCodeSnapshot,plan.SupplierNameSnapshot,
                plan.Lines.Count(line=>line.ConversionStatus==SteelReceiptConversionStatus.NotCreated),
                plan.TotalLineCount,plan.ImportedAtUtc));
        query=query.ApplySearch(request,["ImportReferenceNo","WaybillNo","SupplierCode","SupplierName"]);
        return await query.ApplyAdvancedFilters(request)
            .ApplySort(request,nameof(SteelReceiptPendingSourceRow.ImportedAtUtc))
            .ToPagedResponseAsync(request,ct);
    }

    public async Task<SteelReceiptSourceRow> GetReceiptSourceAsync(string reference,CancellationToken ct=default)
    {
        var value=Clean(reference,100,true)!;
        var exactImport=await Plans.Query()
            .Where(x=>x.Status!=SteelReceiptPlanStatus.Cancelled&&x.ImportReferenceNo==value)
            .OrderByDescending(x=>x.Id).ToListAsync(ct);
        var plans=exactImport.Count>0
            ?exactImport
            :await Plans.Query().Where(x=>x.Status!=SteelReceiptPlanStatus.Cancelled&&x.WaybillNo==value)
                .OrderByDescending(x=>x.Id).ToListAsync(ct);
        if(plans.Count==0)throw AppException.NotFound("Excel aktarım referansı veya irsaliye numarasıyla eşleşen SAC planı bulunamadı.");
        if(plans.Count>1)throw AppException.Conflict("Bu irsaliye numarası birden fazla SAC planında bulunuyor. Excel aktarım referansını girin.");
        var plan=plans[0];
        var lines=await GridQuery(Lines.Query()
            .Where(x=>x.PlanId==plan.Id)
            .OrderBy(x=>x.LineNo))
            .ToListAsync(ct);
        return new(plan.Id,plan.ImportReferenceNo,plan.SourceFileName,plan.WaybillNo,plan.WaybillDate,
            plan.SupplierId,plan.SupplierCodeSnapshot,plan.SupplierNameSnapshot,plan.Status,
            plan.TotalLineCount,plan.TotalExpectedQuantity,lines);
    }

    public Task<PagedResponse<SteelReceiptLineGridRow>> GetPlacementCandidatesPagedAsync(PagedRequest request,CancellationToken ct=default)
    {
        var readyLineIds=uow.Repository<GoodsReceiptExecutionLine>().Query()
            .Where(x=>x.StockStatus=="Available").Select(x=>x.GrLineId);
        return PageLinesAsync(GridQuery(Lines.Query().Where(x=>x.ConversionStatus==SteelReceiptConversionStatus.Created&&x.PutawayStatus==SteelPutawayStatus.Pending
            &&x.GoodsReceiptLineId.HasValue&&readyLineIds.Contains(x.GoodsReceiptLineId.Value))),request,ct);
    }

    public async Task<SteelReceiptLineGridRow> GetLineAsync(long lineId,CancellationToken ct=default)=>
        await GridQuery().FirstOrDefaultAsync(x=>x.Id==lineId,ct)??throw AppException.NotFound("SAC levhası bulunamadı.");

    public async Task<IReadOnlyList<SteelPlacementOccupancyRow>> GetOccupancyAsync(long locationId,CancellationToken ct=default)=>
        await (from p in uow.Repository<SteelReceiptPlacement>().Query() join l in Lines.Query() on p.PlanLineId equals l.Id
            where p.LocationId==locationId
            orderby p.RowNo,p.PositionNo,p.StackOrderNo
            select new SteelPlacementOccupancyRow(p.Id,l.Id,l.DCode,l.StockCodeSnapshot,l.SupplierSerialNo,l.CombinedSize,l.MaterialGrade,
                l.ApprovedQuantity,l.UnitCode,p.WarehouseId,p.LocationId,p.PlacementType,p.RowNo!.Value,p.PositionNo!.Value,p.StackOrderNo,p.PlacedAtUtc))
            .ToListAsync(ct);

    public Task<SteelReceiptLineGridRow> InspectAsync(long lineId,InspectSteelReceiptLineRequest request,long actor,CancellationToken ct=default)=>
        uow.ExecuteInTransactionAsync<SteelReceiptLineGridRow>(async token=>{
            var line=await Lines.Query(true).Include(x=>x.Plan).FirstOrDefaultAsync(x=>x.Id==lineId,token)
                ??throw AppException.NotFound("SAC levhası bulunamadı.");
            ApplyVersion(line.RowVersion,request.RowVersion);
            if(line.ConversionStatus==SteelReceiptConversionStatus.Created)throw AppException.Conflict("Mal kabule aktarılmış levhanın kontrol kararı değiştirilemez.");
            if(!request.IsArrived){
                if(request.ArrivedQuantity!=0||request.ApprovedQuantity!=0||request.RejectedQuantity!=0)throw AppException.BadRequest("Gelmedi olarak işaretlenen levhanın miktarları sıfır olmalıdır.");
                line.ArrivalStatus=SteelArrivalStatus.Missing;line.InspectionStatus=SteelInspectionStatus.Pending;
            }else{
                if(request.ArrivedQuantity<=0||request.ArrivedQuantity>line.ExpectedQuantity)throw AppException.BadRequest("Gelen miktar beklenen miktarı aşamaz.");
                if(request.ApprovedQuantity<0||request.RejectedQuantity<0||request.ApprovedQuantity+request.RejectedQuantity>request.ArrivedQuantity)
                    throw AppException.BadRequest("Kabul ve ret toplamı gelen miktarı aşamaz.");
                if(request.RejectedQuantity>0&&string.IsNullOrWhiteSpace(request.RejectReason))throw AppException.BadRequest("Ret nedeni zorunludur.");
                line.ArrivalStatus=SteelArrivalStatus.Arrived;
                line.InspectionStatus=request.ApprovedQuantity>0&&request.RejectedQuantity>0?SteelInspectionStatus.PartiallyApproved:
                    request.ApprovedQuantity>0?SteelInspectionStatus.Approved:
                    request.RejectedQuantity>0?SteelInspectionStatus.Rejected:SteelInspectionStatus.Inspected;
            }
            line.ArrivedQuantity=request.ArrivedQuantity;line.ApprovedQuantity=request.ApprovedQuantity;line.RejectedQuantity=request.RejectedQuantity;
            line.RejectReason=Clean(request.RejectReason,500);line.InspectionNote=Clean(request.Note,1000);line.InspectedBy=actor;
            line.InspectedAtUtc=DateTimeOffset.UtcNow;line.UpdatedBy=actor;line.UpdatedDate=DateTime.UtcNow;await uow.SaveChangesAsync(token);
            await RefreshPlanAsync(line.Plan,token);
            await audit.WriteAsync(new("steel-receipt.inspect",nameof(SteelReceiptPlanLine),line.Id.ToString(),"Succeeded","steel-receipt",
                NewValues:new{line.ArrivalStatus,line.InspectionStatus,line.ApprovedQuantity,line.RejectedQuantity},ChangedFields:["Inspection"]),token);
            return await GridQuery().FirstAsync(x=>x.Id==line.Id,token);
        },ct);

    public async Task<ConvertSteelReceiptResult> ConvertAsync(long planId,ConvertSteelReceiptRequest request,long actor,CancellationToken ct=default)
    {
        if(request.IdempotencyKey==Guid.Empty)throw AppException.BadRequest("Idempotency anahtarı zorunludur.");
        if(request.LineIds is not{Count:>0})throw AppException.BadRequest("En az bir SAC levhası seçilmelidir.");
        var legacyTaskRequest=request.Mode==0;
        var mode=legacyTaskRequest?SteelReceiptConversionMode.Task:request.Mode;
        ValidateConversionMode(mode,request.AssignToAllActiveUsers,request.AssignedUserIds);

        var converted=await uow.ExecuteInTransactionAsync<ConvertSteelReceiptResult>(async token=>{
            var plan=await Plans.Query(true).Include(x=>x.Lines).FirstOrDefaultAsync(x=>x.Id==planId,token)??throw AppException.NotFound("SAC planı bulunamadı.");
            var ids=request.LineIds.Where(x=>x>0).Distinct().ToArray();var selected=plan.Lines.Where(x=>ids.Contains(x.Id)).OrderBy(x=>x.LineNo).ToList();
            if(selected.Count==0||selected.Count!=ids.Length)throw AppException.BadRequest("Seçilen SAC satırlarından biri bulunamadı.");
            var vehicleAcceptanceIds=selected.Where(x=>x.VehicleAcceptanceId.HasValue)
                .Select(x=>x.VehicleAcceptanceId!.Value).Distinct().ToArray();
            if(vehicleAcceptanceIds.Length>0)
            {
                var affectedVehicleIds=await uow.Repository<SteelVehicleAcceptance>().Query()
                    .Where(x=>vehicleAcceptanceIds.Contains(x.Id))
                    .Select(x=>x.VehicleCheckInId)
                    .Distinct()
                    .ToArrayAsync(token);
                var unknownCount=await uow.Repository<SteelVehicleAcceptedPlate>().Query()
                    .CountAsync(x=>affectedVehicleIds.Contains(x.VehicleCheckInId)
                        &&x.IdentityStatus==SteelPlateIdentityStatus.Unknown,token);
                EnsureVehicleHasNoUnknownPlates(unknownCount);
            }
            var (waybillNo,electronicWaybillNo)=ResolveConversionDocumentReference(
                request.WaybillNo,request.ElectronicWaybillNo,plan.WaybillNo);
            var waybillDate=request.WaybillDate??plan.WaybillDate;
            GoodsReceiptOperationsService.ValidateDocumentReference(
                waybillNo,electronicWaybillNo,waybillDate,
                legacyTaskRequest?GoodsReceiptExecutionMode.Import:GoodsReceiptExecutionMode.Manual);

            if(selected.All(x=>x.ConversionStatus==SteelReceiptConversionStatus.Created))
            {
                var receiptIds=selected.Select(x=>x.GoodsReceiptId).Distinct().ToArray();
                if(receiptIds.Length!=1||!receiptIds[0].HasValue)
                    throw AppException.Conflict("Seçilen levhaların mevcut mal kabul bağlantıları tutarsızdır.");
                var existingHeader=await uow.Repository<GoodsReceiptHeader>().Query()
                    .FirstOrDefaultAsync(x=>x.Id==receiptIds[0]!.Value,token)
                    ??throw AppException.Conflict("Bağlı mal kabul kaydı bulunamadı.");
                if(!IsCompatibleReplay(
                    existingHeader,request.IdempotencyKey,mode,
                    waybillNo,electronicWaybillNo,waybillDate))
                    throw AppException.Conflict("Levhalar daha önce farklı bir mal kabul isteğiyle aktarılmış.");
                var existingTask=await uow.Repository<GoodsReceiptTask>().Query()
                    .FirstOrDefaultAsync(x=>x.GrHeaderId==existingHeader.Id,token);
                var existingExecution=await uow.Repository<GoodsReceiptExecution>().Query()
                    .FirstOrDefaultAsync(x=>x.GrHeaderId==existingHeader.Id,token);
                var labelIds=existingExecution is null
                    ?[]
                    :await uow.Repository<GoodsReceiptLabelBatch>().Query()
                        .Where(x=>x.CorrelationId==existingExecution.IdempotencyKey)
                        .SelectMany(x=>x.Labels).Select(x=>x.Id).ToArrayAsync(token);
                return new(existingHeader.Id,existingHeader.DocumentNo,existingTask?.Id,existingTask?.TaskNo,
                    existingExecution?.Id,existingExecution?.StockMovementOperationId,labelIds,
                    selected.Count,selected.Sum(x=>x.ApprovedQuantity),mode,true);
            }
            if(selected.Any(x=>x.InspectionStatus is not(SteelInspectionStatus.Approved or SteelInspectionStatus.PartiallyApproved)
                ||x.ApprovedQuantity<=0||x.ConversionStatus==SteelReceiptConversionStatus.Created))throw AppException.Conflict("Yalnızca onaylı ve aktarılmamış levhalar seçilebilir.");
            List<long>? assignedUserIds=null;
            if(mode==SteelReceiptConversionMode.Task)
            {
                assignedUserIds=request.AssignToAllActiveUsers
                    ?await uow.Repository<verii_wms_api_v2.Modules.Identity.Domain.User>().Query()
                        .Where(x=>x.IsActive).Select(x=>x.Id).ToListAsync(token)
                    :request.AssignedUserIds?.Where(x=>x>0).Distinct().ToList();
                if(assignedUserIds is null||assignedUserIds.Count==0)
                    throw AppException.BadRequest("SAC mal kabul emri için en az bir aktif kullanıcı atanmalıdır.");
            }
            var manual=new CreateManualGoodsReceiptRequest(request.IdempotencyKey,plan.BranchCode,plan.DocumentSeriesId,plan.SupplierId,
                plan.TargetWarehouseId,plan.ReceivingLocationId,request.DocumentDate,waybillNo,waybillDate,electronicWaybillNo,plan.ExportReferenceNo,
                null,null,null,null,null,null,plan.PlannedArrivalAtUtc,null,
                mode==SteelReceiptConversionMode.Direct?GoodsReceiptLabelStrategy.GenerateOnReceipt:GoodsReceiptLabelStrategy.PreGenerate,
                legacyTaskRequest?GoodsReceiptExecutionMode.Import:GoodsReceiptExecutionMode.Manual,
                request.Priority,null,Clean(request.Description,1000),assignedUserIds,
                selected.Select(BuildManualGoodsReceiptLineForConvert).ToList());
            var result=mode==SteelReceiptConversionMode.Direct
                ?await grOperations.CreateDirectReceiptDeferredErpAsync(manual,actor,qualityAlreadyApproved:true,token)
                :await grOperations.CreateOrderlessTaskAsync(manual,actor,token);
            var header=await uow.Repository<GoodsReceiptHeader>().Query(true).FirstAsync(x=>x.Id==result.Id,token);
            header.ReceiptType=GoodsReceiptType.SteelPlate;header.SourceSystem=verii_wms_api_v2.Modules.WarehouseOperations.Domain.WarehouseOperationSourceSystem.Import;
            header.Description=Clean($"{header.Description} | SAC plan: {plan.ImportReferenceNo}",1000);
            var grLines=await uow.Repository<GoodsReceiptLine>().Query().Where(x=>x.GrHeaderId==result.Id).OrderBy(x=>x.LineNo).ToListAsync(token);
            if(grLines.Count!=selected.Count)throw AppException.Conflict("Mal kabul satır eşleştirmesi kurulamadı.");
            for(var i=0;i<selected.Count;i++){selected[i].GoodsReceiptId=result.Id;selected[i].GoodsReceiptLineId=grLines[i].Id;
                selected[i].ConversionStatus=SteelReceiptConversionStatus.Created;selected[i].UpdatedBy=actor;selected[i].UpdatedDate=DateTime.UtcNow;}
            await RefreshPlanAsync(plan,token);await uow.SaveChangesAsync(token);
            await audit.WriteAsync(new("steel-receipt.convert",nameof(SteelReceiptPlan),plan.Id.ToString(),"Succeeded","steel-receipt",
                NewValues:new{result.Id,result.DocumentNo,Mode=mode,LineIds=ids},ChangedFields:["GoodsReceipt","ConversionStatus"]),token);
            return new(result.Id,result.DocumentNo,result.TaskId,result.TaskNo,result.ExecutionId,result.StockMovementOperationId,
                result.GeneratedLabelIds,selected.Count,selected.Sum(x=>x.ApprovedQuantity),mode,result.Replayed);
        },ct,IsolationLevel.Serializable);
        if(mode==SteelReceiptConversionMode.Direct)
            await erpPosting.PostIfEligibleAsync(converted.GoodsReceiptId,actor,ct);
        return converted;
    }

    internal static void EnsureVehicleHasNoUnknownPlates(int unknownCount)
    {
        if(unknownCount>0)
            throw AppException.Conflict($"Bu araçta {unknownCount} adet bilinmeyen levha var; irsaliye oluşturmak için önce eşleştirin.");
    }

    public Task<PlaceSteelReceiptLineResult> PlaceAsync(long lineId,PlaceSteelReceiptLineRequest request,long actor,CancellationToken ct=default)
    {
        if(request.IdempotencyKey==Guid.Empty)throw AppException.BadRequest("Idempotency anahtarı zorunludur.");
        return uow.ExecuteInTransactionAsync<PlaceSteelReceiptLineResult>(async token=>{
            var line=await Lines.Query(true).Include(x=>x.Placement).FirstOrDefaultAsync(x=>x.Id==lineId,token)??throw AppException.NotFound("SAC levhası bulunamadı.");
            ApplyVersion(line.RowVersion,request.RowVersion);
            if(line.ConversionStatus!=SteelReceiptConversionStatus.Created||!line.GoodsReceiptLineId.HasValue)throw AppException.Conflict("Levha önce ortak mal kabule aktarılmalıdır.");
            if(line.PutawayStatus==SteelPutawayStatus.Placed&&line.Placement is not null)
                return new(line.Placement.Id,line.Placement.StockMovementOperationId,true,line.Placement.LocationId,
                    line.Placement.PlacementType,line.Placement.RowNo??1,line.Placement.PositionNo??1,line.Placement.StackOrderNo??1);
            var dest=await uow.Repository<WarehouseLocation>().FindByIdAsync(request.LocationId,false,token)??throw AppException.BadRequest("Hedef raf bulunamadı.");
            if(!dest.IsActive||!dest.IsPutaway||dest.IsQuarantine||dest.WarehouseId!=line.TargetWarehouseId)throw AppException.BadRequest("Hedef raf yerleştirmeye uygun değil.");
            var occupancy=uow.Repository<SteelReceiptPlacement>().Query().Where(x=>x.LocationId==request.LocationId);
            var occupiedCount=await occupancy.CountAsync(token);
            var maximumStack=await occupancy.MaxAsync(x=>(int?)x.StackOrderNo,token)??0;
            var stackOrder=Math.Max(occupiedCount,maximumStack)+1;
            const int rowNo=1;
            const int positionNo=1;
            if(await uow.Repository<SteelReceiptPlacement>().AnyAsync(x=>x.LocationId==request.LocationId&&x.RowNo==rowNo&&x.PositionNo==positionNo&&x.StackOrderNo==stackOrder,token))
                throw AppException.Conflict("Seçilen SAC yerleşim koordinatı dolu.");
            var status=await uow.Repository<GoodsReceiptExecutionLine>().Query().Where(x=>x.GrLineId==line.GoodsReceiptLineId)
                .OrderByDescending(x=>x.Id).Select(x=>x.StockStatus).FirstOrDefaultAsync(token);
            if(status is null)throw AppException.Conflict("Fiziksel mal kabul tamamlanmadan yerleştirme yapılamaz.");
            if(!string.Equals(status,"Available",StringComparison.OrdinalIgnoreCase))throw AppException.Conflict("Kalite bekleyen levha yerleştirilemez.");
            var movement=await stockMovement.PostAsync(new PostStockMovementRequest($"STEEL-PUTAWAY:{line.Id}:{request.IdempotencyKey:N}",
                StockMovementTypes.Transfer,"SteelReceipt",line.DCode,line.Id,DateTime.UtcNow,"SteelPutaway",null,
                [new StockMovementLineRequest(line.StockId,line.YapCodeId,line.ApprovedQuantity,line.TargetWarehouseId,line.ReceivingLocationId,
                    line.TargetWarehouseId,dest.Id,line.UnitCode,line.HeatNumber,line.SupplierSerialNo,
                    "Available","Available","Available")]),token);
            var placement=Stamp(new SteelReceiptPlacement{BranchCode=line.BranchCode,PlanLine=line,WarehouseId=line.TargetWarehouseId,
                LocationId=dest.Id,PlacementType=SteelPlacementType.Stacked,RowNo=rowNo,PositionNo=positionNo,StackOrderNo=stackOrder,
                StockMovementOperationId=movement.OperationId,PlacedAtUtc=DateTimeOffset.UtcNow,PlacedBy=actor},actor);
            await uow.Repository<SteelReceiptPlacement>().AddAsync(placement,token);line.PutawayStatus=SteelPutawayStatus.Placed;
            line.UpdatedBy=actor;line.UpdatedDate=DateTime.UtcNow;await uow.SaveChangesAsync(token);
            await audit.WriteAsync(new("steel-receipt.place",nameof(SteelReceiptPlanLine),line.Id.ToString(),"Succeeded","steel-receipt",
                NewValues:new{placement.LocationId,placement.PlacementType,placement.RowNo,placement.PositionNo,placement.StackOrderNo,movement.OperationId},
                ChangedFields:["Placement","StockMovement"]),token);
            return new(placement.Id,movement.OperationId,movement.IsReplay,placement.LocationId,placement.PlacementType,
                rowNo,positionNo,stackOrder);
        },ct,IsolationLevel.Serializable);
    }

    private IQueryable<SteelReceiptLineGridRow> GridQuery()=>GridQuery(Lines.Query());

    private IQueryable<SteelReceiptLineGridRow> GridQuery(IQueryable<SteelReceiptPlanLine> lines)
    {
        var vehicleHeaders=uow.Repository<VehicleCheckInHeader>().Query();
        var acceptances=uow.Repository<SteelVehicleAcceptance>().Query();
        var joined=from l in lines join p in Plans.Query() on l.PlanId equals p.Id
                   join g in uow.Repository<GoodsReceiptHeader>().Query() on l.GoodsReceiptId equals g.Id into gs
                   from g in gs.DefaultIfEmpty()
                   join acc in acceptances on l.VehicleAcceptanceId equals acc.Id into acceptanceRows
                   from acc in acceptanceRows.DefaultIfEmpty()
                   join v in vehicleHeaders on acc.VehicleCheckInId equals v.Id into vehicles
                   from v in vehicles.DefaultIfEmpty()
                   select new {Line=l,Plan=p,Receipt=g,Vehicle=v};
        return joined.Select(x=>new SteelReceiptLineGridRow(x.Line.Id,x.Line.PlanId,x.Plan.ImportReferenceNo,x.Line.LineNo,
            x.Line.DCode,x.Line.NetsisOrderNo,x.Line.StockCodeSnapshot,x.Line.StockNameSnapshot,x.Line.SupplierSerialNo,
            x.Line.SecondarySerialNo,x.Line.CombinedSize,x.Line.MaterialGrade,x.Line.HeatNumber,x.Line.CertificateNumber,
            x.Line.ExpectedQuantity,x.Line.ArrivedQuantity,x.Line.ApprovedQuantity,x.Line.RejectedQuantity,x.Line.UnitCode,
            x.Line.ArrivalStatus,x.Line.InspectionStatus,x.Line.ConversionStatus,x.Line.PutawayStatus,
            x.Receipt==null?null:x.Receipt.DocumentNo,x.Line.GoodsReceiptId,
            x.Receipt==null?null:x.Receipt.ErpIntegrationStatus.ToString(),x.Line.TargetWarehouseId,x.Line.ReceivingLocationId,
            x.Line.GoodsReceiptLineId,x.Line.CreatedBy,x.Line.CreatedDate,x.Line.UpdatedBy,x.Line.UpdatedDate,
            x.Vehicle==null?null:x.Vehicle.PlateNo,
            x.Vehicle==null?null:((x.Vehicle.DriverFirstName??"")+" "+(x.Vehicle.DriverLastName??"")).Trim(),
            x.Receipt==null?null:(x.Receipt.ElectronicWaybillNo??x.Receipt.WaybillNo),
            x.Receipt==null?null:(x.Receipt.ReceivedAtUtc??(x.Receipt.CreatedDate.HasValue?new DateTimeOffset(DateTime.SpecifyKind(x.Receipt.CreatedDate.Value,DateTimeKind.Utc)):(DateTimeOffset?)null)),
            Convert.ToBase64String(x.Line.RowVersion)));
    }

    public async Task<IReadOnlyList<SteelReceiptAttachmentRow>> GetAttachmentsAsync(long lineId,CancellationToken ct=default)
    {
        if(!await Lines.AnyAsync(x=>x.Id==lineId,ct))throw AppException.NotFound("SAC levhası bulunamadı.");
        return await uow.Repository<SteelReceiptInspectionAttachment>().Query()
            .Where(x=>x.PlanLineId==lineId).OrderByDescending(x=>x.CreatedDate)
            .Select(x=>new SteelReceiptAttachmentRow(x.Id,x.PlanLineId,x.FileName,x.ContentType,$"/api/steel-receipts/attachments/{x.Id}/file",x.Caption,x.FileSize,x.CreatedBy,x.CreatedDate))
            .ToListAsync(ct);
    }

    public async Task<SteelReceiptAttachmentRow> AddAttachmentAsync(long lineId,SteelReceiptAttachmentUpload upload,string? caption,long actor,CancellationToken ct=default)
    {
        var line=await Lines.FindByIdAsync(lineId,false,ct)??throw AppException.NotFound("SAC levhası bulunamadı.");
        if(line.ConversionStatus==SteelReceiptConversionStatus.Created)throw AppException.Conflict("Ortak mal kabule aktarılmış levhanın kontrol kanıtları değiştirilemez.");
        var path=await attachmentStorage.SaveAsync(lineId,upload,ct);
        try
        {
            var entity=new SteelReceiptInspectionAttachment{PlanLineId=lineId,FileName=PrivateUploadFileName.ForDisplay(upload.FileName),ContentType=upload.ContentType,
                StoragePath=path,Caption=Clean(caption,500),FileSize=upload.Length,CreatedBy=actor,CreatedDate=DateTime.UtcNow};
            await uow.Repository<SteelReceiptInspectionAttachment>().AddAsync(entity,ct);
            await uow.SaveChangesAsync(ct);
            await audit.WriteAsync(new("steel-receipt.attachment.add",nameof(SteelReceiptInspectionAttachment),entity.Id.ToString(),"Succeeded","steel-receipt",
                NewValues:new{entity.PlanLineId,entity.FileName,entity.ContentType,entity.FileSize},ChangedFields:["Attachment"]),ct);
            return new(entity.Id,lineId,entity.FileName,entity.ContentType,$"/api/steel-receipts/attachments/{entity.Id}/file",entity.Caption,entity.FileSize,entity.CreatedBy,entity.CreatedDate);
        }
        catch { attachmentStorage.Delete(path); throw; }
    }

    public async Task<SteelReceiptAttachmentDownload> DownloadAttachmentAsync(long attachmentId,CancellationToken ct=default)
    {
        var entity=await uow.Repository<SteelReceiptInspectionAttachment>().FindByIdAsync(attachmentId,false,ct)
            ??throw AppException.NotFound("Kontrol eki bulunamadı.");
        return new(await attachmentStorage.OpenReadAsync(entity.StoragePath,ct),entity.FileName,entity.ContentType);
    }

    public async Task RemoveAttachmentAsync(long attachmentId,long actor,CancellationToken ct=default)
    {
        var entity=await uow.Repository<SteelReceiptInspectionAttachment>().Query(true).Include(x=>x.PlanLine)
            .FirstOrDefaultAsync(x=>x.Id==attachmentId,ct)??throw AppException.NotFound("Kontrol eki bulunamadı.");
        if(entity.PlanLine.ConversionStatus==SteelReceiptConversionStatus.Created)throw AppException.Conflict("Ortak mal kabule aktarılmış levhanın kontrol kanıtları değiştirilemez.");
        entity.IsDeleted=true;entity.DeletedBy=actor;entity.DeletedDate=DateTime.UtcNow;
        await uow.SaveChangesAsync(ct);
        attachmentStorage.Delete(entity.StoragePath);
        await audit.WriteAsync(new("steel-receipt.attachment.remove",nameof(SteelReceiptInspectionAttachment),entity.Id.ToString(),"Succeeded","steel-receipt",
            OldValues:new{entity.PlanLineId,entity.FileName},ChangedFields:["Attachment"]),ct);
    }

    private static async Task<PagedResponse<SteelReceiptLineGridRow>> PageLinesAsync(IQueryable<SteelReceiptLineGridRow> query,PagedRequest request,CancellationToken ct)
    {
        var s=request.Search?.Trim();query=query.Where(x=>string.IsNullOrWhiteSpace(s)||x.DCode.Contains(s)||x.StockCode.Contains(s)
            ||x.SupplierSerialNo.Contains(s)||(x.NetsisOrderNo!=null&&x.NetsisOrderNo.Contains(s))||x.ImportReferenceNo.Contains(s));
        return await query.ApplyAdvancedFilters(request).ApplySort(request,nameof(SteelReceiptLineGridRow.Id)).ToPagedResponseAsync(request,ct);
    }

    private async Task<List<NormalizedLine>> ValidateImportAsync(PreviewSteelReceiptImportRequest request,CancellationToken ct)
    {
        if(string.IsNullOrWhiteSpace(request.BranchCode)||string.IsNullOrWhiteSpace(request.ImportReferenceNo)||string.IsNullOrWhiteSpace(request.SourceFileName)
            ||request.Lines.Count is<1 or>5000)throw AppException.BadRequest("Şube, aktarım referansı, dosya ve 1-5000 satır zorunludur.");
        var waybillNo=PurchaseWaybillNumberPolicy.Normalize(request.WaybillNo);
        if(!PurchaseWaybillNumberPolicy.IsValid(waybillNo))
            throw AppException.BadRequest("E-irsaliye / GİB numarası semboller dahil tam 15 karakter olmalıdır.");
        if(!request.WaybillDate.HasValue)
            throw AppException.BadRequest("İrsaliye tarihi zorunludur.");
        var branch=request.BranchCode.Trim();var supplier=await uow.Repository<CustomerEntity>().FindByIdAsync(request.SupplierId,false,ct)
            ??throw AppException.BadRequest("Tedarikçi bulunamadı.");if(supplier.BranchCode!=branch)throw AppException.BadRequest("Tedarikçi şubesi uyuşmuyor.");
        var warehouse=await uow.Repository<WarehouseEntity>().FindByIdAsync(request.TargetWarehouseId,false,ct)
            ??throw AppException.BadRequest("Depo bulunamadı.");if(warehouse.BranchCode!=branch)throw AppException.BadRequest("Depo şubesi uyuşmuyor.");
        var receivingLocationId=await ResolveImportReceivingLocationAsync(
            warehouse.Id,request.ReceivingLocationId,
            GoodsReceiptLocationSelectionPolicy.AnyActiveWarehouseLocation,ct);
        var series=await uow.Repository<DocumentSeriesEntity>().FindByIdAsync(request.DocumentSeriesId,false,ct);
        if(series is null||!series.IsActive||series.DocumentType!=WmsDocumentType.GoodsReceipt)
            throw AppException.BadRequest("Seçilen belge serisi aktif bir Mal Kabul serisi olmalıdır.");
        var locIds=request.Lines.Select(x=>x.ReceivingLocationId??receivingLocationId).Append(receivingLocationId).Distinct().ToArray();
        var locations=await uow.Repository<WarehouseLocation>().Query().Where(x=>locIds.Contains(x.Id)).ToDictionaryAsync(x=>x.Id,ct);
        if(locations.Count!=locIds.Length||locations.Values.Any(x=>
               !GoodsReceiptLocationPolicy.IsAllowed(
                   GoodsReceiptLocationSelectionPolicy.AnyActiveWarehouseLocation,x,warehouse.Id)))
            throw AppException.BadRequest(GoodsReceiptOperationsService.LocationPolicyError(
                GoodsReceiptLocationSelectionPolicy.AnyActiveWarehouseLocation));
        var stockIds=request.Lines.Where(x=>x.StockId.HasValue).Select(x=>x.StockId!.Value).Distinct().ToArray();
        var stockCodes=request.Lines.Select(x=>x.StockCode.Trim().ToUpperInvariant()).Where(x=>x.Length>0).Distinct().ToArray();
        var stockRows=await uow.Repository<StockEntity>().Query().Where(x=>stockIds.Contains(x.Id)||stockCodes.Contains(x.ErpStockCode)).ToListAsync(ct);
        var stocksById=stockRows.ToDictionary(x=>x.Id);var stocksByCode=stockRows.GroupBy(x=>x.ErpStockCode,StringComparer.OrdinalIgnoreCase).ToDictionary(x=>x.Key,x=>x.First(),StringComparer.OrdinalIgnoreCase);
        var yapIds=request.Lines.Where(x=>x.YapCodeId.HasValue).Select(x=>x.YapCodeId!.Value).Distinct().ToArray();
        var yapCodes=request.Lines.Select(x=>x.YapCode?.Trim().ToUpperInvariant()).Where(x=>!string.IsNullOrWhiteSpace(x)).Distinct().ToArray();
        var yapRows=await uow.Repository<verii_wms_api_v2.Modules.YapCode.Domain.YapCode>().Query()
            .Where(x=>yapIds.Contains(x.Id)||yapCodes.Contains(x.ConfigurationCode)).ToListAsync(ct);
        var yapsById=yapRows.ToDictionary(x=>x.Id);var yapsByCode=yapRows.GroupBy(x=>x.ConfigurationCode,StringComparer.OrdinalIgnoreCase).ToDictionary(x=>x.Key,x=>x.First(),StringComparer.OrdinalIgnoreCase);
        var duplicates=request.Lines.GroupBy(x=>Key(request.SupplierId,x)).Where(x=>x.Count()>1).Select(x=>x.Key).ToHashSet();
        var result=new List<NormalizedLine>();
        foreach(var input in request.Lines){var errors=new List<string>();StockEntity? stock=null;
            if(input.StockId.HasValue)stocksById.TryGetValue(input.StockId.Value,out stock);
            if(stock is null&&!string.IsNullOrWhiteSpace(input.StockCode))stocksByCode.TryGetValue(input.StockCode.Trim(),out stock);
            if(stock is null||stock.BranchCode!=branch)errors.Add("Stok bulunamadı veya şube uyuşmuyor.");
            verii_wms_api_v2.Modules.YapCode.Domain.YapCode? yap=null;
            if(input.YapCodeId.HasValue)yapsById.TryGetValue(input.YapCodeId.Value,out yap);
            if(yap is null&&!string.IsNullOrWhiteSpace(input.YapCode))yapsByCode.TryGetValue(input.YapCode.Trim(),out yap);
            if((input.YapCodeId.HasValue||!string.IsNullOrWhiteSpace(input.YapCode))&&yap is null)errors.Add("YAP kodu bulunamadı.");
            var serial=Clean(input.SupplierSerialNo,100)??string.Empty;if(serial.Length==0)errors.Add("Tedarikçi seri numarası zorunludur.");
            if(input.ExpectedQuantity<=0)errors.Add("Beklenen miktar sıfırdan büyük olmalıdır.");var key=Key(request.SupplierId,input);
            if(duplicates.Contains(key))errors.Add("Dosyada aynı levha birden fazla kez bulunuyor.");
            result.Add(new(input,key,serial,stock,yap?.ConfigurationCode,errors,yap?.Id));}
        var repeatedSerials=result.Where(x=>x.Stock is not null)
            .GroupBy(x=>SerialKey(x.Stock!.Id,x.Serial))
            .Where(x=>x.Count()>1).Select(x=>x.Key).ToHashSet();
        return result.Select(x=>repeatedSerials.Contains(SerialKey(x.Stock?.Id??0,x.Serial))
            ?x with{Errors=x.Errors.Append("Dosyada aynı stok ve levha seri numarası birden fazla kez bulunuyor.").ToList()}
            :x).ToList();
    }

    private async Task<long> ResolveImportReceivingLocationAsync(
        long warehouseId,
        long? requestedLocationId,
        GoodsReceiptLocationSelectionPolicy locationPolicy,
        CancellationToken ct)
    {
        if(requestedLocationId is>0)
        {
            var requested=await uow.Repository<WarehouseLocation>().FindByIdAsync(requestedLocationId.Value,false,ct);
            if(requested is not null
                &&GoodsReceiptLocationPolicy.IsAllowed(locationPolicy,requested,warehouseId))
                return requested.Id;
            throw AppException.BadRequest(GoodsReceiptOperationsService.LocationPolicyError(locationPolicy));
        }
        var defaultLocationId=await uow.Repository<WarehouseEntity>().Query()
            .Where(x=>x.Id==warehouseId)
            .Select(x=>x.DefaultGoodsReceiptLocationId)
            .FirstOrDefaultAsync(ct);
        if(defaultLocationId is>0)
        {
            var defaultLocation=await uow.Repository<WarehouseLocation>().FindByIdAsync(defaultLocationId.Value,false,ct);
            if(defaultLocation is not null
                &&GoodsReceiptLocationPolicy.IsAllowed(locationPolicy,defaultLocation,warehouseId))
                return defaultLocation.Id;
        }
        var locations=uow.Repository<WarehouseLocation>().Query()
            .Where(x=>x.WarehouseId==warehouseId&&x.IsActive);
        if(locationPolicy==GoodsReceiptLocationSelectionPolicy.ReceivingOrStagingOnly)
            locations=locations.Where(x=>
                x.LocationType==LocationTypes.Receiving||x.LocationType==LocationTypes.Staging);
        var resolved=await locations
            .OrderBy(x=>x.LocationType==LocationTypes.Receiving?0:1)
            .ThenBy(x=>x.LocationType==LocationTypes.Staging?0:1)
            .ThenBy(x=>x.Id)
            .Select(x=>(long?)x.Id)
            .FirstOrDefaultAsync(ct);
        return resolved??throw AppException.BadRequest(
            GoodsReceiptOperationsService.LocationPolicyError(locationPolicy));
    }

    private async Task RefreshPlanAsync(SteelReceiptPlan plan,CancellationToken ct)
    {
        var s=await Lines.Query().Where(x=>x.PlanId==plan.Id).Select(x=>new{x.InspectionStatus,x.ConversionStatus}).ToListAsync(ct);
        plan.Status=SteelReceiptPlanStatusRules.Resolve(s.Select(x=>new SteelReceiptPlanStatusRules.LineState(x.InspectionStatus,x.ConversionStatus)));
        plan.UpdatedDate=DateTime.UtcNow;await uow.SaveChangesAsync(ct);
    }
    internal static ManualGoodsReceiptLineRequest BuildManualGoodsReceiptLineForConvert(SteelReceiptPlanLine line)
    {
        var serialNo=string.IsNullOrWhiteSpace(line.SupplierSerialNo)?line.DCode:line.SupplierSerialNo.Trim();
        var description=string.IsNullOrWhiteSpace(line.HeatNumber)
            ?$"SAC {line.DCode} · Seri {serialNo}"
            :$"SAC {line.DCode} · Seri {serialNo} · Isı {line.HeatNumber.Trim()}";
        return new ManualGoodsReceiptLineRequest(line.StockId,line.YapCodeId,line.ApprovedQuantity,line.UnitCode,null,serialNo,
            null,null,null,null,Clean(description,1000),line.TargetWarehouseId,line.ReceivingLocationId);
    }

    internal static void ValidateConversionMode(
        SteelReceiptConversionMode mode,
        bool assignToAllActiveUsers,
        IReadOnlyCollection<long>? assignedUserIds)
    {
        if(mode is not(SteelReceiptConversionMode.Task or SteelReceiptConversionMode.Direct))
            throw AppException.BadRequest("Geçersiz SAC mal kabul işlem modu.");
        if(mode==SteelReceiptConversionMode.Direct
            &&(assignToAllActiveUsers||assignedUserIds is{Count:>0}))
            throw AppException.BadRequest("Doğrudan mal kabulde kullanıcı ataması yapılamaz.");
    }

    internal static bool IsCompatibleReplay(
        GoodsReceiptHeader existingHeader,
        Guid idempotencyKey,
        SteelReceiptConversionMode mode,
        string? waybillNo,
        string? electronicWaybillNo,
        DateOnly? waybillDate)
    {
        var expectedInitiation=mode==SteelReceiptConversionMode.Direct
            ?GoodsReceiptInitiationMode.DirectReceipt:GoodsReceiptInitiationMode.UnplannedTask;
        return existingHeader.CorrelationId==idempotencyKey
            &&existingHeader.InitiationMode==expectedInitiation
            &&existingHeader.WaybillNo==waybillNo
            &&existingHeader.ElectronicWaybillNo==electronicWaybillNo
            &&existingHeader.WaybillDate==waybillDate;
    }

    internal static (string? WaybillNo,string? ElectronicWaybillNo) ResolveConversionDocumentReference(
        string? requestedWaybillNo,
        string? requestedElectronicWaybillNo,
        string? sourceWaybillNo)
    {
        var waybillNo=Clean(requestedWaybillNo,50);
        var electronicWaybillNo=Clean(requestedElectronicWaybillNo,50);
        if(waybillNo is not null||electronicWaybillNo is not null)
            return(waybillNo,electronicWaybillNo);

        var source=PurchaseWaybillNumberPolicy.Normalize(sourceWaybillNo);
        return source is null?(null,null):(null,source);
    }

    private static string Key(long supplierId,SteelImportLineRequest x)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        $"{supplierId}|{x.NetsisOrderNo?.Trim().ToUpperInvariant()}|{x.NetsisOrderLineNo?.Trim().ToUpperInvariant()}|{x.StockId?.ToString()??x.StockCode.Trim().ToUpperInvariant()}|{x.SupplierSerialNo?.Trim().ToUpperInvariant()}|{x.SecondarySerialNo?.Trim().ToUpperInvariant()}")));
    private static string SerialKey(long stockId,string serial)=>$"{stockId}|{serial.Trim().ToUpperInvariant()}";
    private static T Stamp<T>(T v,long actor)where T:verii_wms_api_v2.Shared.Domain.BaseEntity{v.CreatedBy=actor;v.CreatedDate=DateTime.UtcNow;return v;}
    private static string? Clean(string? value,int max,bool required=false){var v=string.IsNullOrWhiteSpace(value)?null:value.Trim();
        if(required&&v is null)throw AppException.BadRequest("Zorunlu alan boş bırakılamaz.");if(v?.Length>max)throw AppException.BadRequest($"En fazla {max} karakter.");return v;}
    private static void ApplyVersion(byte[] current,string supplied){byte[] expected;try{expected=Convert.FromBase64String(supplied);}catch{throw AppException.BadRequest("Geçersiz eşzamanlılık bilgisi.");}
        if(!current.SequenceEqual(expected))throw AppException.Conflict("Kayıt başka bir kullanıcı tarafından değiştirildi. Listeyi yenileyin.");}
    private sealed record NormalizedLine(SteelImportLineRequest Input,string Key,string Serial,StockEntity? Stock,string? YapCode,IReadOnlyList<string> Errors,long? YapCodeId);
}
