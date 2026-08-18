using System.Data;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using verii_wms_api_v2.Modules.Audit.Application;
using verii_wms_api_v2.Modules.ProjectSettings.Application;
using verii_wms_api_v2.Modules.VehicleCheckIn.Domain;
using verii_wms_api_v2.Shared;
using verii_wms_api_v2.Shared.Application.Abstractions.Persistence;
using verii_wms_api_v2.Shared.Application.Exceptions;
using verii_wms_api_v2.Shared.Infrastructure.Files;

namespace verii_wms_api_v2.Modules.VehicleCheckIn.Application;

public sealed class VehicleCheckInService(IUnitOfWork uow,IProjectSettingsService projectSettings,IVehicleCheckInImageStorage storage,IAuditLogWriter audit):IVehicleCheckInService
{
    private static readonly IReadOnlyDictionary<string,string> GridSearchColumns=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase)
    {
        ["id"]=nameof(VehicleCheckInRow.Id),["plateNo"]=nameof(VehicleCheckInRow.PlateNo),
        ["trailerPlateNo"]=nameof(VehicleCheckInRow.TrailerPlateNo),["driverFirstName"]=nameof(VehicleCheckInRow.DriverSearchText),
        ["driverPhone"]=nameof(VehicleCheckInRow.DriverPhone),["steelSheetCount"]=nameof(VehicleCheckInRow.SteelSheetCount),
        ["customerCode"]=nameof(VehicleCheckInRow.CustomerCode),["customerName"]=nameof(VehicleCheckInRow.CustomerName),
        ["status"]=nameof(VehicleCheckInRow.Status),["imageCount"]=nameof(VehicleCheckInRow.ImageCount)
    };
    private static readonly string[] DefaultGridSearchColumns=["plateNo","trailerPlateNo","driverFirstName","driverPhone","customerCode","customerName"];
    private IGenericRepository<VehicleCheckInHeader> Headers=>uow.Repository<VehicleCheckInHeader>();
    private IGenericRepository<VehicleCheckInImage> Images=>uow.Repository<VehicleCheckInImage>();

    public async Task<VehicleCheckInDetail?> FindTodayByPlateAsync(string branchCode,string plateNo,CancellationToken ct=default)
    {
        var normalized=NormalizePlate(plateNo);if(normalized.Length<5)throw AppException.BadRequest("Geçerli bir plaka giriniz.");
        var day=await BusinessDateAsync(ct);var branch=NormalizeBranch(branchCode);
        var header=await Headers.Query()
            .Where(x=>x.BranchCode==branch&&x.PlateNoNormalized==normalized&&x.BusinessDate==day)
            .OrderByDescending(x=>x.CheckedInAtUtc)
            .ThenByDescending(x=>x.Id)
            .FirstOrDefaultAsync(ct);
        return header is null?null:await DetailAsync(header,ct);
    }

    public Task<VehicleCheckInDetail> SaveAsync(SaveVehicleCheckInRequest request,long actor,CancellationToken ct=default)=>
        uow.ExecuteInTransactionAsync(async token=>{
            var branch=NormalizeBranch(request.BranchCode);var plate=NormalizePlate(request.PlateNo);
            if(plate.Length<5||plate.Length>25)throw AppException.BadRequest("Plaka 5-25 karakter arasında olmalıdır.");
            if(request.SteelSheetCount<=0||request.SteelSheetCount>100000)throw AppException.BadRequest("Sac levha adedi 1-100.000 arasında olmalıdır.");
            var day=await BusinessDateAsync(token);var now=DateTimeOffset.UtcNow;
            // Her fiziksel geliş ayrı bir aggregate kaydıdır. Aynı plakanın aynı iş günündeki
            // önceki gelişi yalnızca açıkça Id gönderildiğinde güncellenebilir.
            var entity=request.Id.HasValue
                ?await Headers.FindByIdAsync(request.Id.Value,true,token)
                :null;
            if(request.Id.HasValue&&entity is null)throw AppException.NotFound("Güncellenecek araç giriş kaydı bulunamadı.");
            if(entity is not null&&entity.BranchCode!=branch)throw AppException.BadRequest("Araç giriş kaydının şubesi değiştirilemez.");
            if(request.Id.HasValue)
            {
                if(string.IsNullOrWhiteSpace(request.RowVersion))throw AppException.Conflict("Kayıt sürüm bilgisi eksik. Ekranı yenileyip tekrar deneyin.");
                byte[] expected;try{expected=Convert.FromBase64String(request.RowVersion);}catch{throw AppException.Conflict("Kayıt sürüm bilgisi geçersiz. Ekranı yenileyip tekrar deneyin.");}
                if(!entity!.RowVersion.SequenceEqual(expected))throw AppException.Conflict("Araç kaydı başka bir kullanıcı tarafından değiştirildi. Ekranı yenileyip tekrar deneyin.");
            }
            var effectiveCustomerId=ResolveCustomerId(request.CustomerId,entity?.CustomerId);
            var customer=effectiveCustomerId.HasValue?await uow.Repository<verii_wms_api_v2.Modules.Customer.Domain.Customer>().FindByIdAsync(effectiveCustomerId.Value,false,token):null;
            if(request.CustomerId.HasValue&&(customer is null||customer.BranchCode!=branch))throw AppException.BadRequest("Seçilen cari bu şubede bulunamadı.");
            var created=entity is null;
            if(entity is null)
            {
                entity=new VehicleCheckInHeader{BranchCode=branch,PlateNoNormalized=plate,BusinessDate=day,CheckedInAtUtc=now,CreatedBy=actor,CreatedDate=now.UtcDateTime};
                await Headers.AddAsync(entity,token);
            }
            else {entity.UpdatedBy=actor;entity.UpdatedDate=now.UtcDateTime;}
            entity.PlateNo=DisplayPlate(request.PlateNo);entity.TrailerPlateNo=Clean(request.TrailerPlateNo,25)?.ToUpperInvariant();
            entity.TrailerPlateNoNormalized=NormalizeNullablePlate(request.TrailerPlateNo);entity.DriverFirstName=Clean(request.DriverFirstName,100);
            entity.DriverLastName=Clean(request.DriverLastName,100);entity.DriverPhone=Clean(request.DriverPhone,40);
            entity.CarrierName=Clean(request.CarrierName,200);entity.SteelSheetCount=request.SteelSheetCount;
            // Null müşteri mevcut kaydı temizleme komutu değildir; açık bir temizleme sözleşmesi tanımlanana kadar tedarikçiyi koru.
            if(created||customer is not null)
            {
                entity.CustomerId=customer?.Id;
                entity.CustomerCodeSnapshot=customer?.CustomerCode;
                entity.CustomerNameSnapshot=customer?.CustomerName;
            }
            entity.Note=Clean(request.Note,1000);if(created)entity.Status=VehicleCheckInStatus.CheckedIn;
            await uow.SaveChangesAsync(token);
            await audit.WriteAsync(new(created?"vehicle-check-in.create":"vehicle-check-in.update",nameof(VehicleCheckInHeader),entity.Id.ToString(),"Succeeded","vehicle-check-in",
                NewValues:new{entity.PlateNo,entity.TrailerPlateNo,entity.DriverFirstName,entity.DriverLastName,entity.SteelSheetCount,entity.CustomerId,entity.BusinessDate},ChangedFields:["Vehicle","Driver","SteelSheetCount","Customer"]),token);
            return await DetailAsync(entity,token);
        },ct,IsolationLevel.Serializable);

    internal static long? ResolveCustomerId(long? requestedCustomerId,long? existingCustomerId)=>
        requestedCustomerId??existingCustomerId;

    public async Task<VehicleCheckInDetail> GetAsync(long id,CancellationToken ct=default)
    {
        var entity=await Headers.FindByIdAsync(id,false,ct)??throw AppException.NotFound("Araç giriş kaydı bulunamadı.");
        return await DetailAsync(entity,ct);
    }

    public async Task<PagedResponse<VehicleCheckInRow>> GetPagedAsync(PagedRequest request,CancellationToken ct=default)
    {
        var q=Headers.Query();var s=request.LegacySearch?.Trim();var normalized=NormalizePlate(s);
        if(!string.IsNullOrWhiteSpace(s))q=q.Where(x=>x.PlateNo.Contains(s)||x.PlateNoNormalized.Contains(normalized)
            ||(x.TrailerPlateNo!=null&&x.TrailerPlateNo.Contains(s))||(x.DriverFirstName!=null&&x.DriverFirstName.Contains(s))
            ||(x.DriverLastName!=null&&x.DriverLastName.Contains(s))||(x.CustomerCodeSnapshot!=null&&x.CustomerCodeSnapshot.Contains(s))
            ||(x.CustomerNameSnapshot!=null&&x.CustomerNameSnapshot.Contains(s)));
        var projected=q.Select(x=>new VehicleCheckInRow(x.Id,x.BranchCode,x.PlateNo,x.TrailerPlateNo,x.DriverFirstName,x.DriverLastName,
            x.DriverPhone,x.CarrierName,x.SteelSheetCount,x.CustomerId,x.CustomerCodeSnapshot,x.CustomerNameSnapshot,x.CheckedInAtUtc,x.BusinessDate,
            x.Status.ToString(),x.Note,x.Images.Count,x.CreatedBy,x.CreatedDate,x.UpdatedBy,x.UpdatedDate,Convert.ToBase64String(x.RowVersion),
            ((x.DriverFirstName??"")+" "+(x.DriverLastName??"")).Trim()));
        projected=projected.ApplySearch(request,GridSearchColumns,DefaultGridSearchColumns);
        return await projected.ApplyAdvancedFilters(request).ApplySort(request,nameof(VehicleCheckInRow.CheckedInAtUtc)).ToPagedResponseAsync(request,ct);
    }

    public async Task<IReadOnlyList<VehicleCheckInImageRow>> AddImagesAsync(long id,IReadOnlyList<VehicleImageUpload> files,long actor,CancellationToken ct=default)
    {
        if(files.Count is<1 or>10)throw AppException.BadRequest("Bir işlemde 1-10 araç görseli yüklenebilir.");
        var header=await Headers.FindByIdAsync(id,false,ct)??throw AppException.NotFound("Araç giriş kaydı bulunamadı.");
        var next=(await Images.Query().Where(x=>x.HeaderId==id).MaxAsync(x=>(int?)x.SortOrder,ct)??0)+1;
        var saved=new List<string>();try
        {
            foreach(var file in files){var path=await storage.SaveAsync(id,file,ct);saved.Add(path);await Images.AddAsync(new VehicleCheckInImage
                {BranchCode=header.BranchCode,HeaderId=id,FileName=PrivateUploadFileName.ForDisplay(file.FileName),ContentType=file.ContentType,StoragePath=path,
                    FileSize=file.Length,SortOrder=next++,CreatedBy=actor,CreatedDate=DateTime.UtcNow},ct);}
            await uow.SaveChangesAsync(ct);
        }catch{saved.ForEach(storage.Delete);throw;}
        await audit.WriteAsync(new("vehicle-check-in.images.add",nameof(VehicleCheckInHeader),id.ToString(),"Succeeded","vehicle-check-in",NewValues:new{Count=files.Count},ChangedFields:["Images"]),ct);
        return await ImageRowsAsync(id,ct);
    }

    public async Task<VehicleImageDownload> DownloadImageAsync(long imageId,CancellationToken ct=default)
    {
        var image=await Images.FindByIdAsync(imageId,false,ct)??throw AppException.NotFound("Araç görseli bulunamadı.");
        return new(await storage.OpenReadAsync(image.StoragePath,ct),image.FileName,image.ContentType);
    }

    public async Task RemoveImageAsync(long imageId,long actor,CancellationToken ct=default)
    {
        var image=await Images.FindByIdAsync(imageId,true,ct)??throw AppException.NotFound("Araç görseli bulunamadı.");
        image.IsDeleted=true;image.DeletedBy=actor;image.DeletedDate=DateTime.UtcNow;await uow.SaveChangesAsync(ct);storage.Delete(image.StoragePath);
        await audit.WriteAsync(new("vehicle-check-in.images.remove",nameof(VehicleCheckInImage),imageId.ToString(),"Succeeded","vehicle-check-in",ChangedFields:["IsDeleted"]),ct);
    }

    private async Task<VehicleCheckInDetail> DetailAsync(VehicleCheckInHeader x,CancellationToken ct)=>
        new(ToRow(x,await Images.CountAsync(i=>i.HeaderId==x.Id,ct)),await ImageRowsAsync(x.Id,ct));
    private async Task<IReadOnlyList<VehicleCheckInImageRow>> ImageRowsAsync(long id,CancellationToken ct)=>await Images.Query().Where(x=>x.HeaderId==id)
        .OrderBy(x=>x.SortOrder).Select(x=>new VehicleCheckInImageRow(x.Id,x.HeaderId,x.FileName,x.ContentType,x.FileSize,x.SortOrder,x.CreatedDate)).ToListAsync(ct);
    private static VehicleCheckInRow ToRow(VehicleCheckInHeader x,int count)=>new(x.Id,x.BranchCode,x.PlateNo,x.TrailerPlateNo,x.DriverFirstName,x.DriverLastName,
        x.DriverPhone,x.CarrierName,x.SteelSheetCount,x.CustomerId,x.CustomerCodeSnapshot,x.CustomerNameSnapshot,x.CheckedInAtUtc,x.BusinessDate,x.Status.ToString(),
        x.Note,count,x.CreatedBy,x.CreatedDate,x.UpdatedBy,x.UpdatedDate,Convert.ToBase64String(x.RowVersion));
    private async Task<DateOnly> BusinessDateAsync(CancellationToken ct)
    {
        var setting=await projectSettings.GetAsync(ct);TimeZoneInfo zone;
        try{zone=TimeZoneInfo.FindSystemTimeZoneById(setting.TimeZoneId);}catch{zone=TimeZoneInfo.Utc;}
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow,zone).DateTime);
    }
    private static string NormalizeBranch(string? value)=>string.IsNullOrWhiteSpace(value)?"0":value.Trim();
    private static string NormalizePlate(string? value)=>Regex.Replace((value??"").ToUpperInvariant(),@"[^A-Z0-9]","");
    private static string? NormalizeNullablePlate(string? value){var x=NormalizePlate(value);return x.Length==0?null:x;}
    private static string DisplayPlate(string value)=>Regex.Replace(value.Trim().ToUpperInvariant(),@"\s+"," ");
    private static string? Clean(string? value,int max){var x=value?.Trim();if(string.IsNullOrWhiteSpace(x))return null;return x.Length<=max?x:x[..max];}
}
