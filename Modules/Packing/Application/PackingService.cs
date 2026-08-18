using System.Data;
using Microsoft.EntityFrameworkCore;
using verii_wms_api_v2.Modules.Audit.Application;
using verii_wms_api_v2.Modules.Packing.Domain;
using verii_wms_api_v2.Modules.WarehouseOperations.Domain;
using verii_wms_api_v2.Shared;
using verii_wms_api_v2.Shared.Application.Abstractions.Persistence;
using verii_wms_api_v2.Shared.Application.Exceptions;
using CustomerEntity = verii_wms_api_v2.Modules.Customer.Domain.Customer;
using StockEntity = verii_wms_api_v2.Modules.Stock.Domain.Stock;

namespace verii_wms_api_v2.Modules.Packing.Application;

public sealed class PackingService(IUnitOfWork uow, IAuditLogWriter audit, PackingSourceAdapterResolver sourceAdapters, IPackingDeviceService devices) : IPackingService
{
    private IGenericRepository<PackagingMaterial> Materials => uow.Repository<PackagingMaterial>();
    private IGenericRepository<PackingStation> Stations => uow.Repository<PackingStation>();
    private IGenericRepository<PackagingSpecification> Specifications => uow.Repository<PackagingSpecification>();
    private IGenericRepository<PackingPolicy> Policies => uow.Repository<PackingPolicy>();
    private IGenericRepository<PackingSession> Sessions => uow.Repository<PackingSession>();
    private IGenericRepository<HandlingUnit> Units => uow.Repository<HandlingUnit>();
    private IGenericRepository<HandlingUnitLine> UnitLines => uow.Repository<HandlingUnitLine>();
    private IGenericRepository<PackingEvent> Events => uow.Repository<PackingEvent>();

    public Task<PagedResponse<PackagingMaterialRow>> GetMaterialsAsync(PagedRequest request,CancellationToken ct=default)
    {
        var search=request.LegacySearch?.Trim();
        var q=Materials.Query().Where(x=>string.IsNullOrWhiteSpace(search)||x.Code.Contains(search)||x.Name.Contains(search)||(x.Description!=null&&x.Description.Contains(search)))
            .Select(x=>new PackagingMaterialRow(x.Id,x.BranchCode,x.Code,x.Name,x.Type,x.TareWeight,x.MaxNetWeight,x.MaxGrossWeight,x.InnerLength,x.InnerWidth,x.InnerHeight,x.MaxVolume,x.IsReturnable,x.IsActive,x.Description,x.CreatedBy,x.CreatedDate,x.UpdatedBy,x.UpdatedDate))
            .ApplyAdvancedFilters(request).ApplySort(request,nameof(PackagingMaterialRow.Code));
        return q.ToPagedResponseAsync(request,ct);
    }
    public async Task<long> CreateMaterialAsync(PackagingMaterialRequest r,long actor,CancellationToken ct=default)
    {
        ValidateMaterial(r); var code=Code(r.Code);var branch=Branch(r.BranchCode);
        if(await Materials.AnyAsync(x=>x.BranchCode==branch&&x.Code==code,ct))throw AppException.Conflict("Ambalaj malzemesi kodu zaten kullanılıyor.");
        var e=new PackagingMaterial{BranchCode=Branch(r.BranchCode),CreatedBy=actor}; Apply(e,r,code); await Materials.AddAsync(e,ct);await uow.SaveChangesAsync(ct);
        await Audit("packing.material.create",e.Id,e,ct);return e.Id;
    }
    public async Task UpdateMaterialAsync(long id,PackagingMaterialRequest r,long actor,CancellationToken ct=default)
    {
        ValidateMaterial(r);var e=await Materials.FindByIdAsync(id,true,ct)??throw AppException.NotFound("Ambalaj malzemesi bulunamadı.");var code=Code(r.Code);var branch=Branch(r.BranchCode);
        if(await Materials.AnyAsync(x=>x.Id!=id&&x.BranchCode==branch&&x.Code==code,ct))throw AppException.Conflict("Ambalaj malzemesi kodu zaten kullanılıyor.");
        Apply(e,r,code);e.UpdatedBy=actor;e.UpdatedDate=DateTime.UtcNow;await uow.SaveChangesAsync(ct);await Audit("packing.material.update",id,e,ct);
    }
    public async Task DeleteMaterialAsync(long id,long actor,CancellationToken ct=default)
    {
        var e=await Materials.FindByIdAsync(id,true,ct)??throw AppException.NotFound("Ambalaj malzemesi bulunamadı.");
        if(await Units.AnyAsync(x=>x.PackagingMaterialId==id,ct)||await Specifications.AnyAsync(x=>x.PackagingMaterialId==id,ct))throw AppException.Conflict("Kullanılmış ambalaj malzemesi silinemez; pasife alın.");
        e.IsActive=false;e.DeletedBy=actor;await Materials.SoftDeleteAsync(id,ct);await uow.SaveChangesAsync(ct);await Audit("packing.material.delete",id,e,ct);
    }
    public Task<PagedResponse<PackingStationRow>> GetStationsAsync(PagedRequest r,CancellationToken ct=default)
    {
        var s=r.Search?.Trim();var q=Stations.Query().Where(x=>string.IsNullOrWhiteSpace(s)||x.Code.Contains(s)||x.Name.Contains(s))
            .Select(x=>new PackingStationRow(x.Id,x.BranchCode,x.WarehouseId,x.LocationId,x.Code,x.Name,x.ScaleDeviceCode,x.PrinterDefinitionId,x.IsActive,x.Description,x.CreatedBy,x.CreatedDate,x.UpdatedBy,x.UpdatedDate))
            .ApplyAdvancedFilters(r).ApplySort(r,nameof(PackingStationRow.Code));return q.ToPagedResponseAsync(r,ct);
    }
    public async Task<long> CreateStationAsync(PackingStationRequest r,long actor,CancellationToken ct=default)
    {
        await ValidateStation(r,null,ct);var e=new PackingStation{CreatedBy=actor};Apply(e,r);await Stations.AddAsync(e,ct);await uow.SaveChangesAsync(ct);await Audit("packing.station.create",e.Id,e,ct);return e.Id;
    }
    public async Task UpdateStationAsync(long id,PackingStationRequest r,long actor,CancellationToken ct=default)
    {
        await ValidateStation(r,id,ct);var e=await Stations.FindByIdAsync(id,true,ct)??throw AppException.NotFound("Paketleme istasyonu bulunamadı.");Apply(e,r);e.UpdatedBy=actor;e.UpdatedDate=DateTime.UtcNow;await uow.SaveChangesAsync(ct);await Audit("packing.station.update",id,e,ct);
    }
    public async Task DeleteStationAsync(long id,long actor,CancellationToken ct=default)
    {
        var e=await Stations.FindByIdAsync(id,true,ct)??throw AppException.NotFound("Paketleme istasyonu bulunamadı.");
        if(await Sessions.AnyAsync(x=>x.PackingStationId==id,ct))throw AppException.Conflict("Kullanılmış istasyon silinemez; pasife alın.");
        e.IsActive=false;e.DeletedBy=actor;await Stations.SoftDeleteAsync(id,ct);await uow.SaveChangesAsync(ct);await Audit("packing.station.delete",id,e,ct);
    }
    public Task<PagedResponse<PackagingSpecificationRow>> GetSpecificationsAsync(PagedRequest r,CancellationToken ct=default)
    {
        var search=r.Search?.Trim()??string.Empty;var materials=Materials.Query();var stocks=uow.Repository<StockEntity>().Query();var customers=uow.Repository<CustomerEntity>().Query();
        var q=from e in Specifications.Query()
              join material in materials on e.PackagingMaterialId equals material.Id
              join stockValue in stocks on e.StockId equals (long?)stockValue.Id into stockJoin
              from stock in stockJoin.DefaultIfEmpty()
              join customerValue in customers on e.CustomerId equals (long?)customerValue.Id into customerJoin
              from customer in customerJoin.DefaultIfEmpty()
              where search.Length==0
                    || material.Code.Contains(search)||material.Name.Contains(search)
                    || (e.StockGroupCode!=null&&e.StockGroupCode!.Contains(search))
                    || (stock!=null&&(stock.ErpStockCode.Contains(search)||stock.StockName.Contains(search)))
                    || (customer!=null&&(customer.CustomerCode.Contains(search)||customer.CustomerName.Contains(search)))
              select new PackagingSpecificationRow(e.Id,e.BranchCode,e.StockId,stock==null?null:stock.ErpStockCode,stock==null?null:stock.StockName,e.StockGroupCode,e.CustomerId,customer==null?null:customer.CustomerCode,customer==null?null:customer.CustomerName,e.PackagingMaterialId,material.Code,material.Name,e.UnitsPerHandlingUnit,e.MaxNetWeight,e.MaxVolume,e.Priority,e.IsActive,e.Notes,e.CreatedBy,e.CreatedDate,e.UpdatedBy,e.UpdatedDate);
        return q.ApplyAdvancedFilters(r).ApplySort(r,nameof(PackagingSpecificationRow.Priority)).ToPagedResponseAsync(r,ct);
    }
    public async Task<long> CreateSpecificationAsync(PackagingSpecificationRequest r,long actor,CancellationToken ct=default)
    {
        var normalized=await ValidateSpecification(r,null,ct);var e=new PackagingSpecification{CreatedBy=actor};Apply(e,r,normalized.Branch,normalized.Group);await Specifications.AddAsync(e,ct);await uow.SaveChangesAsync(ct);await Audit("packing.specification.create",e.Id,e,ct);return e.Id;
    }
    public async Task UpdateSpecificationAsync(long id,PackagingSpecificationRequest r,long actor,CancellationToken ct=default)
    {
        var e=await Specifications.FindByIdAsync(id,true,ct)??throw AppException.NotFound("Paketleme spesifikasyonu bulunamadı.");var normalized=await ValidateSpecification(r,id,ct);Apply(e,r,normalized.Branch,normalized.Group);e.UpdatedBy=actor;e.UpdatedDate=DateTime.UtcNow;await uow.SaveChangesAsync(ct);await Audit("packing.specification.update",id,e,ct);
    }
    public async Task DeleteSpecificationAsync(long id,long actor,CancellationToken ct=default)
    {
        var e=await Specifications.FindByIdAsync(id,true,ct)??throw AppException.NotFound("Paketleme spesifikasyonu bulunamadı.");e.IsActive=false;e.DeletedBy=actor;await Specifications.SoftDeleteAsync(id,ct);await uow.SaveChangesAsync(ct);await Audit("packing.specification.delete",id,e,ct);
    }
    public async Task<PackingPolicyDto> GetPolicyAsync(string branchCode,CancellationToken ct=default)
    {
        var branch=Branch(branchCode);var e=await Policies.FirstOrDefaultAsync(x=>x.BranchCode==branch&&x.PolicyKey=="DEFAULT",false,ct);
        return e is null?Map(new PackingPolicy{BranchCode=branch}):Map(e);
    }
    public async Task<PackingPolicyDto> UpdatePolicyAsync(UpdatePackingPolicyRequest r,long actor,CancellationToken ct=default)
    {
        if(r.WeightTolerancePercent is <0 or >100)throw AppException.BadRequest("Ağırlık toleransı 0-100 arasında olmalıdır.");
        var branch=Branch(r.BranchCode);var e=await Policies.FirstOrDefaultAsync(x=>x.BranchCode==branch&&x.PolicyKey=="DEFAULT",true,ct);
        if(e is null){e=new PackingPolicy{BranchCode=branch,CreatedBy=actor};await Policies.AddAsync(e,ct);}else{CheckVersion(e.RowVersion,r.RowVersion);e.UpdatedBy=actor;e.UpdatedDate=DateTime.UtcNow;}
        e.RequirePacking=r.RequirePacking;e.AllowPartialPacking=r.AllowPartialPacking;e.AllowMixedStock=r.AllowMixedStock;e.AllowMixedLot=r.AllowMixedLot;e.AllowMixedCustomer=r.AllowMixedCustomer;e.RequireSerialLotScan=r.RequireSerialLotScan;e.RequireWeight=r.RequireWeight;e.WeightTolerancePercent=r.WeightTolerancePercent;e.RequireDimensions=r.RequireDimensions;e.RequireSscc=r.RequireSscc;e.AutoGenerateSscc=r.AutoGenerateSscc;e.AutoPrintLabelOnClose=r.AutoPrintLabelOnClose;e.AllowReopen=r.AllowReopen;e.AllowRepack=r.AllowRepack;e.ClosePolicy=r.ClosePolicy;e.ReleasePolicy=r.ReleasePolicy;
        await uow.SaveChangesAsync(ct);await Audit("packing.policy.update",e.Id,e,ct);return Map(e);
    }
    public Task<PagedResponse<PackingSessionRow>> GetSessionsAsync(PagedRequest r,CancellationToken ct=default)
    {
        var s=r.Search?.Trim();var lines=UnitLines.Query();var units=Units.Query();
        var q=Sessions.Query().Where(x=>string.IsNullOrWhiteSpace(s)||x.PackingNo.Contains(s)||(x.SourceDocumentNo!=null&&x.SourceDocumentNo.Contains(s))||(x.CustomerCodeSnapshot!=null&&x.CustomerCodeSnapshot.Contains(s)))
            .Select(x=>new PackingSessionRow(x.Id,x.BranchCode,x.PackingNo,x.SourceType,x.SourceHeaderId,x.SourceDocumentNo,x.WarehouseId,x.PackingStationId,x.CustomerId,x.CustomerCodeSnapshot,x.Status,units.Count(u=>u.PackingSessionId==x.Id),lines.Where(l=>units.Where(u=>u.PackingSessionId==x.Id).Select(u=>u.Id).Contains(l.HandlingUnitId)).Sum(l=>(decimal?)l.Quantity)??0,units.Where(u=>u.PackingSessionId==x.Id).Sum(u=>(decimal?)u.GrossWeight)??0,x.OpenedAtUtc,x.ClosedAtUtc,x.ReleasedAtUtc,x.CreatedBy,x.CreatedDate,x.UpdatedBy,x.UpdatedDate))
            .ApplyAdvancedFilters(r).ApplySort(r,nameof(PackingSessionRow.OpenedAtUtc));return q.ToPagedResponseAsync(r,ct);
    }
    public async Task<PackingSessionDetail> GetSessionAsync(long id,CancellationToken ct=default)
    {
        var e=await Sessions.Query().Include(x=>x.HandlingUnits).ThenInclude(x=>x.Lines).FirstOrDefaultAsync(x=>x.Id==id,ct)??throw AppException.NotFound("Paketleme oturumu bulunamadı.");return Detail(e);
    }
    public async Task<IReadOnlyList<PackingSourceLineOption>> GetSourceLinesAsync(long id,CancellationToken ct=default)
    {
        var session=await Sessions.FindByIdAsync(id,false,ct)??throw AppException.NotFound("Paketleme oturumu bulunamadı.");
        if(!session.SourceHeaderId.HasValue)return [];
        return await sourceAdapters.Resolve(session.SourceType).GetLinesAsync(session.SourceHeaderId.Value,ct);
    }
    public Task<PackingSessionDetail> CreateSessionAsync(CreatePackingSessionRequest r,long actor,CancellationToken ct=default)=>uow.ExecuteInTransactionAsync(async token=>
    {
        var replay=await Sessions.FirstOrDefaultAsync(x=>x.IdempotencyKey==r.IdempotencyKey,false,token);if(replay!=null)return await GetSessionAsync(replay.Id,token);
        var station=await Stations.FirstOrDefaultAsync(x=>x.Id==r.PackingStationId&&x.IsActive,false,token)??throw AppException.BadRequest("Aktif paketleme istasyonu bulunamadı.");
        if(station.WarehouseId!=r.WarehouseId)throw AppException.BadRequest("İstasyon seçilen depoya ait değil.");
        if(!r.SourceHeaderId.HasValue)throw AppException.BadRequest("Paketleme kaynağı zorunludur.");
        var source=await sourceAdapters.Resolve(r.SourceType).GetHeaderAsync(r.SourceHeaderId.Value,r.WarehouseId,token);
        var e=new PackingSession{BranchCode=Branch(r.BranchCode),PackingNo=$"PK-{DateTime.UtcNow:yyyyMMdd}-{r.IdempotencyKey.ToString("N")[..8].ToUpperInvariant()}",SourceType=r.SourceType,SourceHeaderId=r.SourceHeaderId,SourceDocumentNo=source.DocumentNo,WarehouseId=r.WarehouseId,PackingStationId=r.PackingStationId,CustomerId=source.CustomerId,CustomerCodeSnapshot=source.CustomerCode,IdempotencyKey=r.IdempotencyKey,OpenedAtUtc=DateTimeOffset.UtcNow,Notes=Clean(r.Notes),CreatedBy=actor};
        await Sessions.AddAsync(e,token);await uow.SaveChangesAsync(token);await AddEvent(e.Id,null,"SessionOpened",null,e.Status.ToString(),r.IdempotencyKey,r.Notes,actor,token);await uow.SaveChangesAsync(token);await Audit("packing.session.create",e.Id,e,token);return Detail(e);
    },ct,IsolationLevel.Serializable);
    public Task<HandlingUnitDto> CreateHandlingUnitAsync(long sessionId,CreateHandlingUnitRequest r,long actor,CancellationToken ct=default)=>uow.ExecuteInTransactionAsync(async token=>
    {
        var prior=await Events.FirstOrDefaultAsync(x=>x.PackingSessionId==sessionId&&x.IdempotencyKey==r.IdempotencyKey,false,token);if(prior?.HandlingUnitId is long oldId)return Map(await LoadUnit(oldId,token));
        var session=await Sessions.FindByIdAsync(sessionId,true,token)??throw AppException.NotFound("Paketleme oturumu bulunamadı.");if(session.Status is PackingSessionStatus.Released or PackingSessionStatus.Cancelled)throw AppException.Conflict("Kapalı oturuma paket eklenemez.");
        var material=await Materials.FirstOrDefaultAsync(x=>x.Id==r.PackagingMaterialId&&x.IsActive,false,token)??throw AppException.BadRequest("Aktif ambalaj malzemesi bulunamadı.");
        if(r.ParentHandlingUnitId.HasValue){var parent=await Units.FindByIdAsync(r.ParentHandlingUnitId.Value,false,token)??throw AppException.BadRequest("Üst paket bulunamadı.");if(parent.PackingSessionId!=sessionId||parent.Status!=HandlingUnitStatus.Open)throw AppException.Conflict("Üst paket bu oturumda ve açık olmalıdır.");}
        var no=string.IsNullOrWhiteSpace(r.HandlingUnitNo)?$"HU-{DateTime.UtcNow:yyyyMMdd}-{r.IdempotencyKey.ToString("N")[..10].ToUpperInvariant()}":Code(r.HandlingUnitNo);
        if(await Units.AnyAsync(x=>x.BranchCode==session.BranchCode&&x.HandlingUnitNo==no,token))throw AppException.Conflict("Paket numarası zaten kullanılıyor.");
        var policy=await GetPolicyEntity(session.BranchCode,token);var sscc=Clean(r.Sscc);
        if(sscc is null&&policy.AutoGenerateSscc)sscc=GenerateSscc(session.Id,r.IdempotencyKey);
        ValidateSscc(sscc,policy.RequireSscc);
        var e=new HandlingUnit{BranchCode=session.BranchCode,PackingSessionId=sessionId,ParentHandlingUnitId=r.ParentHandlingUnitId,PackagingMaterialId=material.Id,HandlingUnitNo=no,Sscc=sscc,TareWeight=material.TareWeight,GrossWeight=material.TareWeight,Length=r.Length??material.InnerLength,Width=r.Width??material.InnerWidth,Height=r.Height??material.InnerHeight,CreatedBy=actor};
        e.Volume=Volume(e.Length,e.Width,e.Height);await Units.AddAsync(e,token);session.Status=PackingSessionStatus.InProgress;session.UpdatedBy=actor;session.UpdatedDate=DateTime.UtcNow;await uow.SaveChangesAsync(token);await AddEvent(sessionId,e.Id,"HandlingUnitCreated",null,e.Status.ToString(),r.IdempotencyKey,null,actor,token);await uow.SaveChangesAsync(token);return Map(e);
    },ct,IsolationLevel.Serializable);
    public Task<HandlingUnitDto> PackAsync(long handlingUnitId,PackHandlingUnitLineRequest r,long actor,CancellationToken ct=default)=>uow.ExecuteInTransactionAsync(async token=>
    {
        var unit=await LoadUnit(handlingUnitId,token);var replay=await Events.AnyAsync(x=>x.PackingSessionId==unit.PackingSessionId&&x.IdempotencyKey==r.IdempotencyKey,token);if(replay)return Map(unit);
        if(unit.Status!=HandlingUnitStatus.Open)throw AppException.Conflict("Yalnızca açık pakete ürün eklenebilir.");if(r.Quantity<=0)throw AppException.BadRequest("Miktar sıfırdan büyük olmalıdır.");
        var session=await Sessions.FindByIdAsync(unit.PackingSessionId,true,token)??throw AppException.NotFound("Paketleme oturumu bulunamadı.");
        if(!session.SourceHeaderId.HasValue)throw AppException.BadRequest("Paketleme kaynağı bulunamadı.");
        var adapter=sourceAdapters.Resolve(session.SourceType);
        var line=await adapter.GetLineAsync(session.SourceHeaderId.Value,r.SourceLineId,true,token);
        if(line.PackedQuantity+r.Quantity>line.PickedQuantity)throw AppException.Conflict("Paketlenen miktar toplanan miktarı aşamaz.");
        var policy=await GetPolicyEntity(session.BranchCode,token);
        var tracking=line.TrackingType.ToString();
        if(tracking.Contains("Serial",StringComparison.OrdinalIgnoreCase)&&string.IsNullOrWhiteSpace(r.SerialNo))throw AppException.BadRequest("Seri takibi olan stokta seri okutulmalıdır.");
        if(tracking.Contains("Lot",StringComparison.OrdinalIgnoreCase)&&string.IsNullOrWhiteSpace(r.LotNo))throw AppException.BadRequest("Lot takibi olan stokta lot okutulmalıdır.");
        if(!string.IsNullOrWhiteSpace(r.SerialNo)&&r.Quantity!=1)throw AppException.BadRequest("Her seri satırı yalnızca 1 adet içerebilir.");
        if(!string.IsNullOrWhiteSpace(r.SerialNo)&&await UnitLines.AnyAsync(x=>x.StockId==line.StockId&&x.SerialNo==Clean(r.SerialNo),token))throw AppException.Conflict("Bu stok ve seri numarası daha önce paketlenmiş.");
        if(!policy.AllowMixedStock&&unit.Lines.Any(x=>x.StockId!=line.StockId))throw AppException.Conflict("Politika aynı pakette farklı stoklara izin vermiyor.");
        if(!policy.AllowMixedLot&&unit.Lines.Any(x=>!Eq(x.LotNo,r.LotNo)))throw AppException.Conflict("Politika aynı pakette farklı lotlara izin vermiyor.");
        var material=await Materials.FindByIdAsync(unit.PackagingMaterialId,false,token)??throw AppException.NotFound("Ambalaj malzemesi bulunamadı.");
        var stockGroup=await uow.Repository<StockEntity>().Query().Where(x=>x.Id==line.StockId).Select(x=>x.GroupCode).FirstOrDefaultAsync(token);
        var spec=await Specifications.Query().Where(x=>x.IsActive&&x.BranchCode==session.BranchCode&&x.PackagingMaterialId==material.Id&&(x.StockId==null||x.StockId==line.StockId)&&(x.StockGroupCode==null||x.StockGroupCode==stockGroup)&&(x.CustomerId==null||x.CustomerId==session.CustomerId))
            .OrderByDescending(x=>x.Priority).ThenByDescending(x=>x.StockId==line.StockId).ThenByDescending(x=>x.CustomerId==session.CustomerId).ThenByDescending(x=>x.StockGroupCode==stockGroup).FirstOrDefaultAsync(token);
        if(spec?.UnitsPerHandlingUnit is decimal maxQty&&unit.Lines.Sum(x=>x.Quantity)+r.Quantity>maxQty)throw AppException.Conflict("Paket spesifikasyonundaki azami miktar aşılıyor.");
        var e=new HandlingUnitLine{BranchCode=session.BranchCode,HandlingUnitId=unit.Id,SourceLineId=line.Id,StockId=line.StockId,StockCodeSnapshot=line.StockCode,YapCodeId=line.YapCodeId,YapCodeSnapshot=line.YapCode,UnitCode=line.UnitCode,Quantity=r.Quantity,LotNo=Clean(r.LotNo),SerialNo=Clean(r.SerialNo),PackedAtUtc=DateTimeOffset.UtcNow,PackedBy=actor,CreatedBy=actor};
        unit.Lines.Add(e);CheckCapacity(unit,material,spec);await adapter.ApplyPackedDeltaAsync(session.SourceHeaderId.Value,line.Id,r.Quantity,r.LotNo,r.SerialNo,unit.HandlingUnitNo,actor,token);
        unit.UpdatedBy=actor;unit.UpdatedDate=DateTime.UtcNow;await uow.SaveChangesAsync(token);await AddEvent(session.Id,unit.Id,"ItemPacked",unit.Status.ToString(),unit.Status.ToString(),r.IdempotencyKey,$"{line.StockCode} x {r.Quantity}",actor,token);await uow.SaveChangesAsync(token);
        if(policy.ClosePolicy==PackingClosePolicy.AutoWhenComplete&&!policy.RequireWeight&&await SourceComplete(session,adapter,token))await CloseCore(unit,session,policy,null,"Kaynak tamamlandığı için otomatik kapatıldı.",actor,Guid.NewGuid(),token);
        return Map(unit);
    },ct,IsolationLevel.Serializable);
    public Task<HandlingUnitDto> UnpackAsync(long id,UnpackHandlingUnitLineRequest r,long actor,CancellationToken ct=default)=>uow.ExecuteInTransactionAsync(async token=>
    {
        var unit=await LoadUnit(id,token);if(await Events.AnyAsync(x=>x.PackingSessionId==unit.PackingSessionId&&x.IdempotencyKey==r.IdempotencyKey,token))return Map(unit);
        if(unit.Status!=HandlingUnitStatus.Open)throw AppException.Conflict("Paketten ürün çıkarmadan önce paket yeniden açılmalıdır.");
        var session=await Sessions.FindByIdAsync(unit.PackingSessionId,true,token)??throw AppException.NotFound("Paketleme oturumu bulunamadı.");var policy=await GetPolicyEntity(session.BranchCode,token);
        if(!policy.AllowRepack)throw AppException.Conflict("Paketleme politikası paketten çıkarmaya izin vermiyor.");
        if(r.Quantity<=0)throw AppException.BadRequest("Miktar sıfırdan büyük olmalıdır.");
        var packedLine=unit.Lines.FirstOrDefault(x=>x.Id==r.HandlingUnitLineId)??throw AppException.NotFound("Paket satırı bulunamadı.");
        if(r.Quantity>packedLine.Quantity)throw AppException.Conflict("Çıkarılacak miktar paket satırı miktarını aşamaz.");
        if(packedLine.SerialNo is not null&&r.Quantity!=packedLine.Quantity)throw AppException.Conflict("Serili paket satırı bölünemez; satırın tamamı çıkarılmalıdır.");
        if(!session.SourceHeaderId.HasValue)throw AppException.BadRequest("Paketleme kaynağı bulunamadı.");
        await sourceAdapters.Resolve(session.SourceType).ApplyPackedDeltaAsync(session.SourceHeaderId.Value,packedLine.SourceLineId,-r.Quantity,packedLine.LotNo,packedLine.SerialNo,unit.HandlingUnitNo,actor,token);
        if(r.Quantity==packedLine.Quantity){packedLine.DeletedBy=actor;await UnitLines.SoftDeleteAsync(packedLine.Id,token);unit.Lines.Remove(packedLine);}else{packedLine.Quantity-=r.Quantity;packedLine.UpdatedBy=actor;packedLine.UpdatedDate=DateTime.UtcNow;}
        session.Status=PackingSessionStatus.InProgress;session.ClosedAtUtc=null;session.ReleasedAtUtc=null;unit.UpdatedBy=actor;unit.UpdatedDate=DateTime.UtcNow;
        await uow.SaveChangesAsync(token);await AddEvent(session.Id,unit.Id,"ItemUnpacked",unit.Status.ToString(),unit.Status.ToString(),r.IdempotencyKey,r.Reason,actor,token);await uow.SaveChangesAsync(token);return Map(unit);
    },ct,IsolationLevel.Serializable);
    public Task<HandlingUnitDto> MoveAsync(long id,MoveHandlingUnitLineRequest r,long actor,CancellationToken ct=default)=>uow.ExecuteInTransactionAsync(async token=>
    {
        if(id==r.TargetHandlingUnitId)throw AppException.BadRequest("Kaynak ve hedef paket aynı olamaz.");
        var source=await LoadUnit(id,token);if(await Events.AnyAsync(x=>x.PackingSessionId==source.PackingSessionId&&x.IdempotencyKey==r.IdempotencyKey,token))return Map(await LoadUnit(r.TargetHandlingUnitId,token));
        var target=await LoadUnit(r.TargetHandlingUnitId,token);
        if(source.PackingSessionId!=target.PackingSessionId)throw AppException.Conflict("Paketler aynı oturumda olmalıdır.");
        if(source.Status!=HandlingUnitStatus.Open||target.Status!=HandlingUnitStatus.Open)throw AppException.Conflict("Taşıma için kaynak ve hedef paket açık olmalıdır.");
        var session=await Sessions.FindByIdAsync(source.PackingSessionId,true,token)??throw AppException.NotFound("Paketleme oturumu bulunamadı.");var policy=await GetPolicyEntity(session.BranchCode,token);
        if(!policy.AllowRepack)throw AppException.Conflict("Paketleme politikası yeniden paketlemeye izin vermiyor.");if(r.Quantity<=0)throw AppException.BadRequest("Miktar sıfırdan büyük olmalıdır.");
        var line=source.Lines.FirstOrDefault(x=>x.Id==r.HandlingUnitLineId)??throw AppException.NotFound("Kaynak paket satırı bulunamadı.");if(r.Quantity>line.Quantity)throw AppException.Conflict("Taşınacak miktar paket satırını aşamaz.");
        if((line.SerialNo is not null||line.LotNo is not null)&&r.Quantity!=line.Quantity)throw AppException.Conflict("Takipli paket satırı bölünemez; satırın tamamı taşınmalıdır.");
        if(!policy.AllowMixedStock&&target.Lines.Any(x=>x.StockId!=line.StockId))throw AppException.Conflict("Politika hedef pakette farklı stoklara izin vermiyor.");
        if(!policy.AllowMixedLot&&target.Lines.Any(x=>!Eq(x.LotNo,line.LotNo)))throw AppException.Conflict("Politika hedef pakette farklı lotlara izin vermiyor.");
        if(!session.SourceHeaderId.HasValue)throw AppException.BadRequest("Paketleme kaynağı bulunamadı.");var adapter=sourceAdapters.Resolve(session.SourceType);
        await adapter.ApplyPackedDeltaAsync(session.SourceHeaderId.Value,line.SourceLineId,-r.Quantity,line.LotNo,line.SerialNo,source.HandlingUnitNo,actor,token);
        await adapter.ApplyPackedDeltaAsync(session.SourceHeaderId.Value,line.SourceLineId,r.Quantity,line.LotNo,line.SerialNo,target.HandlingUnitNo,actor,token);
        var moved=new HandlingUnitLine{BranchCode=line.BranchCode,HandlingUnitId=target.Id,SourceLineId=line.SourceLineId,StockId=line.StockId,StockCodeSnapshot=line.StockCodeSnapshot,YapCodeId=line.YapCodeId,YapCodeSnapshot=line.YapCodeSnapshot,UnitCode=line.UnitCode,Quantity=r.Quantity,LotNo=line.LotNo,SerialNo=line.SerialNo,PackedAtUtc=DateTimeOffset.UtcNow,PackedBy=actor,CreatedBy=actor};target.Lines.Add(moved);
        if(r.Quantity==line.Quantity){line.DeletedBy=actor;await UnitLines.SoftDeleteAsync(line.Id,token);source.Lines.Remove(line);}else{line.Quantity-=r.Quantity;line.UpdatedBy=actor;line.UpdatedDate=DateTime.UtcNow;}
        source.UpdatedBy=target.UpdatedBy=actor;source.UpdatedDate=target.UpdatedDate=DateTime.UtcNow;await uow.SaveChangesAsync(token);await AddEvent(session.Id,target.Id,"ItemRepacked",source.HandlingUnitNo,target.HandlingUnitNo,r.IdempotencyKey,r.Reason,actor,token);await uow.SaveChangesAsync(token);return Map(target);
    },ct,IsolationLevel.Serializable);
    public Task<HandlingUnitDto> CloseAsync(long id,CloseHandlingUnitRequest r,long actor,CancellationToken ct=default)=>uow.ExecuteInTransactionAsync(async token=>
    {
        var unit=await LoadUnit(id,token);if(await Events.AnyAsync(x=>x.PackingSessionId==unit.PackingSessionId&&x.IdempotencyKey==r.IdempotencyKey,token))return Map(unit);if(unit.Status!=HandlingUnitStatus.Open)throw AppException.Conflict("Yalnızca açık paket kapatılabilir.");if(unit.Lines.Count==0&&unit.Children.Count==0)throw AppException.Conflict("Boş paket kapatılamaz.");
        var session=await Sessions.FindByIdAsync(unit.PackingSessionId,true,token)??throw AppException.NotFound("Paketleme oturumu bulunamadı.");var policy=await GetPolicyEntity(session.BranchCode,token);var material=await Materials.FindByIdAsync(unit.PackagingMaterialId,false,token)??throw AppException.NotFound("Ambalaj malzemesi bulunamadı.");
        if(policy.RequireWeight&&!r.MeasuredGrossWeight.HasValue)throw AppException.BadRequest("Ölçülen brüt ağırlık zorunludur.");if(policy.RequireDimensions&&(!unit.Length.HasValue||!unit.Width.HasValue||!unit.Height.HasValue))throw AppException.BadRequest("Paket ölçüleri zorunludur.");
        if(r.MeasuredGrossWeight.HasValue){if(r.MeasuredGrossWeight<=unit.TareWeight)throw AppException.BadRequest("Ölçülen brüt ağırlık dara ağırlığından büyük olmalıdır.");var expected=unit.GrossWeight;var diff=expected<=unit.TareWeight?0:Math.Abs(r.MeasuredGrossWeight.Value-expected)/expected*100;if(diff>policy.WeightTolerancePercent)throw AppException.Conflict($"Ağırlık farkı %{policy.WeightTolerancePercent} toleransını aşıyor.");unit.MeasuredGrossWeight=r.MeasuredGrossWeight;unit.GrossWeight=r.MeasuredGrossWeight.Value;unit.NetWeight=r.MeasuredGrossWeight.Value-unit.TareWeight;}
        CheckCapacity(unit,material,null);await CloseCore(unit,session,policy,r.MeasuredGrossWeight,r.Reason,actor,r.IdempotencyKey,token);return Map(unit);
    },ct,IsolationLevel.Serializable);
    public Task<HandlingUnitDto> ReopenAsync(long id,Guid key,string? reason,long actor,CancellationToken ct=default)=>uow.ExecuteInTransactionAsync(async token=>
    {
        var unit=await LoadUnit(id,token);if(await Events.AnyAsync(x=>x.PackingSessionId==unit.PackingSessionId&&x.IdempotencyKey==key,token))return Map(unit);var session=await Sessions.FindByIdAsync(unit.PackingSessionId,true,token)??throw AppException.NotFound("Paketleme oturumu bulunamadı.");var policy=await GetPolicyEntity(session.BranchCode,token);if(!policy.AllowReopen)throw AppException.Conflict("Politika paket açmaya izin vermiyor.");if(unit.Status is not (HandlingUnitStatus.Closed or HandlingUnitStatus.Released))throw AppException.Conflict("Yalnızca kapalı veya serbest paket açılabilir.");if(unit.Children.Any(x=>x.Status is HandlingUnitStatus.Loaded or HandlingUnitStatus.Shipped)||unit.Status is HandlingUnitStatus.Loaded or HandlingUnitStatus.Shipped)throw AppException.Conflict("Yüklenen/sevk edilen paket açılamaz.");
        var from=unit.Status.ToString();unit.Status=HandlingUnitStatus.Open;unit.ClosedAtUtc=null;unit.ClosedBy=null;session.Status=PackingSessionStatus.InProgress;session.ClosedAtUtc=null;session.ReleasedAtUtc=null;await uow.SaveChangesAsync(token);await AddEvent(session.Id,unit.Id,"HandlingUnitReopened",from,unit.Status.ToString(),key,reason,actor,token);await uow.SaveChangesAsync(token);return Map(unit);
    },ct,IsolationLevel.Serializable);
    public async Task DeleteHandlingUnitAsync(long id,long actor,CancellationToken ct=default)
    {
        var unit=await LoadUnit(id,ct);if(unit.Status!=HandlingUnitStatus.Open||unit.Lines.Count>0||unit.Children.Count>0)throw AppException.Conflict("Yalnızca boş ve açık paket silinebilir.");unit.DeletedBy=actor;await Units.SoftDeleteAsync(id,ct);await uow.SaveChangesAsync(ct);
    }
    public Task<PackingPrintJobRow> EnqueuePrintAsync(long handlingUnitId,PrintHandlingUnitRequest r,long actor,CancellationToken ct=default)=>devices.EnqueueAsync(handlingUnitId,r.IdempotencyKey,r.Copies,actor,ct);
    public Task<PagedResponse<PackingPrintJobRow>> GetPrintJobsAsync(PagedRequest r,CancellationToken ct=default)=>devices.GetJobsAsync(r,ct);
    public Task<ScaleReadingDto> ReadScaleAsync(long handlingUnitId,ScaleReadingRequest r,long actor,CancellationToken ct=default)=>devices.ReadScaleAsync(handlingUnitId,r.IdempotencyKey,actor,ct);

    private async Task<bool> SourceComplete(PackingSession session,IPackingSourceAdapter adapter,CancellationToken ct)=>session.SourceHeaderId.HasValue&&(await adapter.GetLinesAsync(session.SourceHeaderId.Value,ct)).Count==0;
    private async Task CloseCore(HandlingUnit unit,PackingSession session,PackingPolicy policy,decimal? measuredGrossWeight,string? reason,long actor,Guid key,CancellationToken ct)
    {
        var from=unit.Status.ToString();
        unit.Status=policy.ReleasePolicy==PackingReleasePolicy.OnClose?HandlingUnitStatus.Released:HandlingUnitStatus.Closed;
        unit.ClosedAtUtc=DateTimeOffset.UtcNow;unit.ClosedBy=actor;unit.UpdatedBy=actor;unit.UpdatedDate=DateTime.UtcNow;
        var open=await Units.AnyAsync(x=>x.PackingSessionId==session.Id&&x.Id!=unit.Id&&x.Status==HandlingUnitStatus.Open,ct);
        if(!open){session.Status=unit.Status==HandlingUnitStatus.Released?PackingSessionStatus.Released:PackingSessionStatus.Packed;session.ClosedAtUtc=DateTimeOffset.UtcNow;if(session.Status==PackingSessionStatus.Released)session.ReleasedAtUtc=DateTimeOffset.UtcNow;}
        await uow.SaveChangesAsync(ct);await AddEvent(session.Id,unit.Id,"HandlingUnitClosed",from,unit.Status.ToString(),key,reason,actor,ct);await uow.SaveChangesAsync(ct);
        if(policy.AutoPrintLabelOnClose)await devices.EnqueueAsync(unit.Id,key,1,actor,ct);
    }
    private async Task<HandlingUnit> LoadUnit(long id,CancellationToken ct)=>await Units.Query(true).Include(x=>x.Lines).Include(x=>x.Children).FirstOrDefaultAsync(x=>x.Id==id,ct)??throw AppException.NotFound("Paket bulunamadı.");
    private async Task<PackingPolicy> GetPolicyEntity(string branch,CancellationToken ct)=>await Policies.FirstOrDefaultAsync(x=>x.BranchCode==branch&&x.PolicyKey=="DEFAULT",false,ct)??new PackingPolicy{BranchCode=branch};
    private async Task AddEvent(long sessionId,long? unitId,string type,string? from,string to,Guid key,string? description,long actor,CancellationToken ct)=>await Events.AddAsync(new PackingEvent{PackingSessionId=sessionId,HandlingUnitId=unitId,EventType=type,FromStatus=from,ToStatus=to,IdempotencyKey=key,Description=Clean(description),OccurredAtUtc=DateTimeOffset.UtcNow,ActorId=actor,CreatedBy=actor},ct);
    private Task Audit(string action,long id,object value,CancellationToken ct)=>audit.WriteAsync(new AuditLogWriteEntry(action,"Packing",id.ToString(),"Succeeded","packing",NewValues:value),ct);
    private static PackingSessionDetail Detail(PackingSession e){var units=e.HandlingUnits.Select(Map).ToList();var row=new PackingSessionRow(e.Id,e.BranchCode,e.PackingNo,e.SourceType,e.SourceHeaderId,e.SourceDocumentNo,e.WarehouseId,e.PackingStationId,e.CustomerId,e.CustomerCodeSnapshot,e.Status,units.Count,units.Sum(x=>x.Lines.Sum(l=>l.Quantity)),units.Sum(x=>x.GrossWeight),e.OpenedAtUtc,e.ClosedAtUtc,e.ReleasedAtUtc,e.CreatedBy,e.CreatedDate,e.UpdatedBy,e.UpdatedDate);return new(row,Convert.ToBase64String(e.RowVersion),units);}
    private static HandlingUnitDto Map(HandlingUnit x)=>new(x.Id,x.ParentHandlingUnitId,x.PackagingMaterialId,x.HandlingUnitNo,x.Sscc,x.Status,x.TareWeight,x.NetWeight,x.MeasuredGrossWeight,x.GrossWeight,x.Length,x.Width,x.Height,x.Volume,Convert.ToBase64String(x.RowVersion),x.Lines.Select(l=>new HandlingUnitLineDto(l.Id,l.SourceLineId,l.StockId,l.StockCodeSnapshot,l.YapCodeSnapshot,l.UnitCode,l.Quantity,l.LotNo,l.SerialNo,l.PackedAtUtc,l.PackedBy)).ToList());
    private static PackingPolicyDto Map(PackingPolicy x)=>new(x.Id,x.BranchCode,x.RequirePacking,x.AllowPartialPacking,x.AllowMixedStock,x.AllowMixedLot,x.AllowMixedCustomer,x.RequireSerialLotScan,x.RequireWeight,x.WeightTolerancePercent,x.RequireDimensions,x.RequireSscc,x.AutoGenerateSscc,x.AutoPrintLabelOnClose,x.AllowReopen,x.AllowRepack,x.ClosePolicy,x.ReleasePolicy,Convert.ToBase64String(x.RowVersion));
    private static void Apply(PackagingMaterial e,PackagingMaterialRequest r,string code){e.BranchCode=Branch(r.BranchCode);e.Code=code;e.Name=r.Name.Trim();e.Type=r.Type;e.TareWeight=r.TareWeight;e.MaxNetWeight=r.MaxNetWeight;e.MaxGrossWeight=r.MaxGrossWeight;e.InnerLength=r.InnerLength;e.InnerWidth=r.InnerWidth;e.InnerHeight=r.InnerHeight;e.MaxVolume=r.MaxVolume;e.IsReturnable=r.IsReturnable;e.IsActive=r.IsActive;e.Description=Clean(r.Description);}
    private static void Apply(PackingStation e,PackingStationRequest r){e.BranchCode=Branch(r.BranchCode);e.WarehouseId=r.WarehouseId;e.LocationId=r.LocationId;e.Code=Code(r.Code);e.Name=r.Name.Trim();e.ScaleDeviceCode=Clean(r.ScaleDeviceCode);e.PrinterDefinitionId=r.PrinterDefinitionId;e.IsActive=r.IsActive;e.Description=Clean(r.Description);}
    private static void Apply(PackagingSpecification e,PackagingSpecificationRequest r,string branch,string? group){e.BranchCode=branch;e.StockId=r.StockId;e.StockGroupCode=group;e.CustomerId=r.CustomerId;e.PackagingMaterialId=r.PackagingMaterialId;e.UnitsPerHandlingUnit=r.UnitsPerHandlingUnit;e.MaxNetWeight=r.MaxNetWeight;e.MaxVolume=r.MaxVolume;e.Priority=r.Priority;e.IsActive=r.IsActive;e.Notes=Clean(r.Notes);}
    private async Task ValidateStation(PackingStationRequest r,long? id,CancellationToken ct){if(r.WarehouseId<=0||string.IsNullOrWhiteSpace(r.Name))throw AppException.BadRequest("Depo, kod ve ad zorunludur.");var code=Code(r.Code);var branch=Branch(r.BranchCode);if(await Stations.AnyAsync(x=>x.Id!=id&&x.BranchCode==branch&&x.WarehouseId==r.WarehouseId&&x.Code==code,ct))throw AppException.Conflict("İstasyon kodu bu depoda zaten kullanılıyor.");if(r.LocationId.HasValue&&!await uow.Repository<Modules.Location.Domain.WarehouseLocation>().AnyAsync(x=>x.Id==r.LocationId&&x.WarehouseId==r.WarehouseId,ct))throw AppException.BadRequest("İstasyon rafı seçilen depoya ait değil.");}
    private async Task<(string Branch,string? Group)> ValidateSpecification(PackagingSpecificationRequest r,long? id,CancellationToken ct)
    {
        var branch=Branch(r.BranchCode);var group=Clean(r.StockGroupCode)?.ToUpperInvariant();
        if(r.PackagingMaterialId<=0)throw AppException.BadRequest("Ambalaj malzemesi zorunludur.");
        if(r.UnitsPerHandlingUnit is <=0||r.MaxNetWeight is <=0||r.MaxVolume is <=0)throw AppException.BadRequest("Girilen kapasite değerleri sıfırdan büyük olmalıdır.");
        if(r.Priority is <0 or >10000)throw AppException.BadRequest("Öncelik 0-10000 arasında olmalıdır.");
        if(!await Materials.AnyAsync(x=>x.Id==r.PackagingMaterialId&&x.BranchCode==branch&&x.IsActive,ct))throw AppException.BadRequest("Aktif ambalaj malzemesi bulunamadı veya şube ile eşleşmiyor.");
        if(r.StockId.HasValue&&!await uow.Repository<StockEntity>().AnyAsync(x=>x.Id==r.StockId&&x.BranchCode==branch,ct))throw AppException.BadRequest("Stok bulunamadı veya şube ile eşleşmiyor.");
        if(r.CustomerId.HasValue&&!await uow.Repository<CustomerEntity>().AnyAsync(x=>x.Id==r.CustomerId&&x.BranchCode==branch,ct))throw AppException.BadRequest("Müşteri bulunamadı veya şube ile eşleşmiyor.");
        if(await Specifications.AnyAsync(x=>x.Id!=id&&x.BranchCode==branch&&x.StockId==r.StockId&&x.StockGroupCode==group&&x.CustomerId==r.CustomerId&&x.PackagingMaterialId==r.PackagingMaterialId&&x.Priority==r.Priority,ct))throw AppException.Conflict("Aynı kapsam, ambalaj ve öncelik için spesifikasyon zaten var.");
        return(branch,group);
    }
    private static void ValidateMaterial(PackagingMaterialRequest r){if(string.IsNullOrWhiteSpace(r.Name))throw AppException.BadRequest("Kod ve ad zorunludur.");_ = Code(r.Code);if(r.TareWeight<0||r.MaxNetWeight<=0||r.MaxGrossWeight<=0||r.InnerLength<=0||r.InnerWidth<=0||r.InnerHeight<=0||r.MaxVolume<=0)throw AppException.BadRequest("Ağırlık, ölçü ve kapasite değerleri pozitif olmalıdır.");if(r.MaxGrossWeight.HasValue&&r.MaxGrossWeight<r.TareWeight)throw AppException.BadRequest("Azami brüt ağırlık dara ağırlığından küçük olamaz.");}
    private static void CheckCapacity(HandlingUnit u,PackagingMaterial m,PackagingSpecification? s){var maxNet=s?.MaxNetWeight??m.MaxNetWeight;var maxVolume=s?.MaxVolume??m.MaxVolume;if(maxNet.HasValue&&u.NetWeight>maxNet)throw AppException.Conflict("Paket net ağırlık kapasitesi aşılıyor.");if(m.MaxGrossWeight.HasValue&&u.GrossWeight>m.MaxGrossWeight)throw AppException.Conflict("Paket brüt ağırlık kapasitesi aşılıyor.");if(maxVolume.HasValue&&u.Volume>maxVolume)throw AppException.Conflict("Paket hacim kapasitesi aşılıyor.");}
    private static void ValidateSscc(string? value,bool required){if(required&&string.IsNullOrWhiteSpace(value))throw AppException.BadRequest("SSCC zorunludur.");if(value is not null&&(value.Length!=18||!value.All(char.IsDigit)||CheckDigit(value[..17])!=value[17]-'0'))throw AppException.BadRequest("SSCC 18 haneli ve geçerli kontrol basamaklı olmalıdır.");}
    private static string GenerateSscc(long sessionId,Guid key){var seed=$"{sessionId:D6}{Math.Abs(key.GetHashCode()):D10}";var body=("0"+seed)[..17];return body+CheckDigit(body);}
    private static int CheckDigit(string body){var sum=0;for(var i=body.Length-1;i>=0;i--)sum+=(body[i]-'0')*(((body.Length-1-i)%2==0)?3:1);return(10-sum%10)%10;}
    private static decimal? Volume(decimal? l,decimal? w,decimal? h)=>l.HasValue&&w.HasValue&&h.HasValue?l*w*h:null;
    private static void CheckVersion(byte[] current,string? supplied){if(string.IsNullOrWhiteSpace(supplied)||!current.SequenceEqual(Convert.FromBase64String(supplied)))throw AppException.Conflict("Kayıt başka bir kullanıcı tarafından güncellendi; sayfayı yenileyin.");}
    private static string Code(string? v){var x=v?.Trim().ToUpperInvariant()??"";if(x.Length is <1 or >100||x.Any(c=>!(char.IsLetterOrDigit(c)||c is '-' or '_' or '.')))throw AppException.BadRequest("Kod yalnızca harf, rakam, nokta, tire ve alt çizgi içerebilir.");return x;}
    private static string Branch(string? v){var x=string.IsNullOrWhiteSpace(v)?"0":v.Trim();if(x.Length>10)throw AppException.BadRequest("Şube kodu en fazla 10 karakter olabilir.");return x;}
    private static string? Clean(string? v)=>string.IsNullOrWhiteSpace(v)?null:v.Trim();
    private static bool Eq(string? a,string? b)=>string.Equals(Clean(a),Clean(b),StringComparison.OrdinalIgnoreCase);
}
