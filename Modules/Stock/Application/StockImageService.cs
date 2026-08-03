using System.Data;
using Microsoft.EntityFrameworkCore;
using verii_wms_api_v2.Modules.Identity.Infrastructure;
using verii_wms_api_v2.Modules.Stock.Domain;
using verii_wms_api_v2.Shared.Application.Exceptions;
using verii_wms_api_v2.Shared.Infrastructure.Persistence;
using verii_wms_api_v2.Shared.Application.Abstractions.Persistence;

namespace verii_wms_api_v2.Modules.Stock.Application;

public sealed class StockImageService(WmsDbContext db,IUnitOfWork unitOfWork,IStockImageStorage storage,ILogger<StockImageService> logger):IStockImageService
{
    public const int MaximumImagesPerStock=20;
    public const int MaximumUploadBatch=10;

    public async Task<IReadOnlyList<StockImageDto>> ListAsync(long stockId,string branchCode,CancellationToken ct=default)
    {
        await EnsureStock(stockId,branchCode,ct);
        return await Query(stockId,branchCode).Select(x=>ToDto(x)).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<StockImageDto>> UploadAsync(long stockId,string branchCode,long actorId,IReadOnlyList<StockImageUpload> uploads,CancellationToken ct=default)
    {
        if(uploads.Count is 0 or >MaximumUploadBatch)throw AppException.BadRequest($"Tek seferde 1-{MaximumUploadBatch} görsel yükleyebilirsiniz.");
        if(uploads.Any(x=>(x.AltText?.Length??0)>200))throw AppException.BadRequest("Görsel açıklaması en fazla 200 karakter olabilir.");
        await EnsureStock(stockId,branchCode,ct);
        var saved=new List<StoredStockImage>();
        try
        {
            foreach(var upload in uploads)saved.Add(await storage.SaveAsync(branchCode,stockId,upload,ct));
            return await unitOfWork.ExecuteInTransactionAsync<IReadOnlyList<StockImageDto>>(async token=>
            {
                var current=await Query(stockId,branchCode).ToListAsync(token);
                if(current.Count+saved.Count>MaximumImagesPerStock)throw AppException.BadRequest($"Bir stokta en fazla {MaximumImagesPerStock} görsel bulunabilir.");
                var next=current.Count==0?1:current.Max(x=>x.SortOrder)+1;
                for(var i=0;i<saved.Count;i++)
                {
                    var file=saved[i];
                    db.Set<StockImage>().Add(new StockImage
                    {
                        BranchCode=branchCode,StockId=stockId,FileUrl=file.Url,OriginalFileName=file.OriginalFileName,
                        ContentType=file.ContentType,FileLength=file.Length,AltText=CleanAlt(uploads[i].AltText),SortOrder=next++,
                        IsPrimary=current.Count==0&&i==0,CreatedBy=actorId,CreatedDate=DateTime.UtcNow
                    });
                }
                await db.SaveChangesAsync(token);
                return await Query(stockId,branchCode).Select(x=>ToDto(x)).ToListAsync(token);
            },ct,IsolationLevel.Serializable);
        }
        catch
        {
            foreach(var file in saved)await storage.DeleteIfManagedAsync(file.Url,ct);
            throw;
        }
    }

    public async Task<StockImageDto> UpdateAsync(long stockId,long imageId,string branchCode,long actorId,string? altText,CancellationToken ct=default)
    {
        if((altText?.Length??0)>200)throw AppException.BadRequest("Görsel açıklaması en fazla 200 karakter olabilir.");
        var image=await Find(stockId,imageId,branchCode,ct); image.AltText=CleanAlt(altText); image.UpdatedBy=actorId; image.UpdatedDate=DateTime.UtcNow;
        await db.SaveChangesAsync(ct); return ToDto(image);
    }

    public Task<StockImageDto> SetPrimaryAsync(long stockId,long imageId,string branchCode,long actorId,CancellationToken ct=default)=>
        unitOfWork.ExecuteInTransactionAsync(async token=>
        {
            var images=await Query(stockId,branchCode).ToListAsync(token);
            var selected=images.FirstOrDefault(x=>x.Id==imageId)??throw AppException.NotFound("Stok görseli bulunamadı.");
            foreach(var image in images){image.IsPrimary=image.Id==imageId; image.UpdatedBy=actorId; image.UpdatedDate=DateTime.UtcNow;}
            await db.SaveChangesAsync(token); return ToDto(selected);
        },ct,IsolationLevel.Serializable);

    public Task<IReadOnlyList<StockImageDto>> ReorderAsync(long stockId,string branchCode,long actorId,IReadOnlyList<long> imageIds,CancellationToken ct=default)=>
        unitOfWork.ExecuteInTransactionAsync<IReadOnlyList<StockImageDto>>(async token=>
        {
            var images=await Query(stockId,branchCode).ToListAsync(token);
            if(images.Count!=imageIds.Count||imageIds.Distinct().Count()!=images.Count||images.Any(x=>!imageIds.Contains(x.Id)))
                throw AppException.BadRequest("Sıralama listesi stoktaki tüm görselleri birer kez içermelidir.");
            var map=images.ToDictionary(x=>x.Id);
            // Unique index ile geçici çakışmayı önlemek için önce negatif ara sıra kullanılır.
            for(var i=0;i<imageIds.Count;i++)map[imageIds[i]].SortOrder=-(i+1);
            await db.SaveChangesAsync(token);
            for(var i=0;i<imageIds.Count;i++){var image=map[imageIds[i]];image.SortOrder=i+1;image.UpdatedBy=actorId;image.UpdatedDate=DateTime.UtcNow;}
            await db.SaveChangesAsync(token);
            return images.OrderBy(x=>x.SortOrder).Select(ToDto).ToList();
        },ct,IsolationLevel.Serializable);

    public async Task DeleteAsync(long stockId,long imageId,string branchCode,long actorId,CancellationToken ct=default)
    {
        string? fileUrl=null;
        await unitOfWork.ExecuteInTransactionAsync(async token=>
        {
            var image=await Find(stockId,imageId,branchCode,token); fileUrl=image.FileUrl;
            image.IsDeleted=true; image.IsPrimary=false; image.DeletedBy=actorId; image.DeletedDate=DateTime.UtcNow;
            var remaining=await Query(stockId,branchCode).Where(x=>x.Id!=imageId).ToListAsync(token);
            if(remaining.Count>0&&!remaining.Any(x=>x.IsPrimary))remaining[0].IsPrimary=true;
            for(var i=0;i<remaining.Count;i++)remaining[i].SortOrder=-(i+1);
            await db.SaveChangesAsync(token);
            for(var i=0;i<remaining.Count;i++)remaining[i].SortOrder=i+1;
            await db.SaveChangesAsync(token); return true;
        },ct,IsolationLevel.Serializable);
        try{await storage.DeleteIfManagedAsync(fileUrl,ct);}catch(Exception ex){logger.LogWarning(ex,"Silinen stok görseli diskten kaldırılamadı: {Url}",fileUrl);}
    }

    private IQueryable<StockImage> Query(long stockId,string branchCode)=>db.Set<StockImage>().Where(x=>x.StockId==stockId&&x.BranchCode==branchCode).OrderBy(x=>x.SortOrder);
    private async Task EnsureStock(long stockId,string branchCode,CancellationToken ct)
    {if(!await db.Stocks.AnyAsync(x=>x.Id==stockId&&x.BranchCode==branchCode,ct))throw AppException.NotFound("Stok bulunamadı.");}
    private async Task<StockImage> Find(long stockId,long imageId,string branchCode,CancellationToken ct)=>
        await db.Set<StockImage>().FirstOrDefaultAsync(x=>x.Id==imageId&&x.StockId==stockId&&x.BranchCode==branchCode,ct)??throw AppException.NotFound("Stok görseli bulunamadı.");
    private static string? CleanAlt(string? value)=>string.IsNullOrWhiteSpace(value)?null:value.Trim();
    private static StockImageDto ToDto(StockImage x)=>new(x.Id,x.StockId,x.FileUrl,x.OriginalFileName,x.ContentType,x.FileLength,x.AltText,x.SortOrder,x.IsPrimary,x.CreatedDate);
}
