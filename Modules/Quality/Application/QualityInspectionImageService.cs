using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using verii_wms_api_v2.Modules.Identity.Infrastructure;
using verii_wms_api_v2.Modules.Quality.Domain;
using verii_wms_api_v2.Modules.Quality.Localization;
using verii_wms_api_v2.Shared.Application.Abstractions.Persistence;
using verii_wms_api_v2.Shared.Application.Exceptions;
using verii_wms_api_v2.Shared.Infrastructure.Files;

namespace verii_wms_api_v2.Modules.Quality.Application;

public sealed class QualityInspectionImageService(
    WmsDbContext db,
    IUnitOfWork unitOfWork,
    IPrivateUploadStorage storage,
    IStringLocalizer<QualityResource> localizer,
    ILogger<QualityInspectionImageService> logger):IQualityInspectionImageService
{
    public const int MaximumUploadBatch=10;
    public const long MaximumFileLength=10*1024*1024;
    public const int MaximumDraftDispositionKeyLength=64;
    private static readonly PrivateUploadPolicy ImagePolicy=new(MaximumFileLength,new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase)
    {
        ["image/jpeg"]=".jpg",
        ["image/png"]=".png",
        ["image/webp"]=".webp"
    });

    public async Task<IReadOnlyList<QualityInspectionImageDto>> ListAsync(
        long inspectionId,
        long lineId,
        string branchCode,
        string? draftDispositionKey=null,
        CancellationToken ct=default)
    {
        await EnsureLineAsync(inspectionId,lineId,branchCode,ct);
        var query=Query(inspectionId,lineId,branchCode);
        if(!string.IsNullOrWhiteSpace(draftDispositionKey))
        {
            var normalized=NormalizeDraftDispositionKey(draftDispositionKey);
            query=query.Where(x=>x.DraftDispositionKey==normalized);
        }
        return await query.Select(x=>ToDto(x)).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<QualityInspectionImageDto>> UploadAsync(
        long inspectionId,
        long lineId,
        string branchCode,
        long actorId,
        IReadOnlyList<QualityInspectionImageUpload> uploads,
        CancellationToken ct=default)
    {
        if(uploads.Count is 0 or >MaximumUploadBatch)
            throw AppException.BadRequest(Message(QualityMessageKeys.ImageUploadBatchLimit,MaximumUploadBatch));
        if(uploads.Any(x=>(x.Caption?.Length??0)>500))
            throw AppException.BadRequest(Message(QualityMessageKeys.ImageCaptionLengthLimit,500));
        if(uploads.Any(x=>string.IsNullOrWhiteSpace(x.DraftDispositionKey)))
            throw AppException.BadRequest(Message(QualityMessageKeys.DraftDispositionKeyRequired));
        var normalizedKeys=uploads
            .Select(x=>NormalizeDraftDispositionKey(x.DraftDispositionKey!))
            .ToArray();

        await EnsureLineAsync(inspectionId,lineId,branchCode,ct);
        var savedPaths=new List<string>(uploads.Count);
        try
        {
            foreach(var upload in uploads)
                savedPaths.Add(await storage.SaveAsync(PrivateUploadArea.QualityInspection,lineId,upload.Content,upload.ContentType,upload.Length,ImagePolicy,ct));

            return await unitOfWork.ExecuteInTransactionAsync<IReadOnlyList<QualityInspectionImageDto>>(async token=>
            {
                var now=DateTime.UtcNow;
                for(var index=0;index<uploads.Count;index++)
                {
                    var upload=uploads[index];
                    await db.QualityInspectionImages.AddAsync(new QualityInspectionImage
                    {
                        BranchCode=branchCode,
                        QualityInspectionId=inspectionId,
                        QualityInspectionLineId=lineId,
                        DraftDispositionKey=normalizedKeys[index],
                        StoragePath=savedPaths[index],
                        OriginalFileName=PrivateUploadFileName.ForDisplay(upload.FileName),
                        ContentType=NormalizeContentType(upload.ContentType),
                        FileLength=upload.Length,
                        Caption=Clean(upload.Caption),
                        CreatedBy=actorId,
                        CreatedDate=now
                    },token);
                }
                await db.SaveChangesAsync(token);
                var keys=normalizedKeys.Distinct(StringComparer.Ordinal).ToArray();
                return await Query(inspectionId,lineId,branchCode)
                    .Where(x=>x.DraftDispositionKey!=null&&keys.Contains(x.DraftDispositionKey))
                    .Select(x=>ToDto(x))
                    .ToListAsync(token);
            },ct,IsolationLevel.Serializable);
        }
        catch
        {
            foreach(var path in savedPaths)
                try{storage.Delete(PrivateUploadArea.QualityInspection,path);}catch(Exception cleanupError){logger.LogWarning(cleanupError,"Kaydedilemeyen kalite görseli diskten temizlenemedi: {Path}",path);}
            throw;
        }
    }

    public async Task<QualityInspectionImageContent> OpenAsync(long inspectionId,long lineId,long imageId,string branchCode,CancellationToken ct=default)
    {
        var image=await FindAsync(inspectionId,lineId,imageId,branchCode,tracking:false,ct);
        var content=await storage.OpenReadAsync(PrivateUploadArea.QualityInspection,image.StoragePath,cancellationToken:ct);
        return new(content,image.ContentType,image.OriginalFileName,image.FileLength);
    }

    public async Task DeleteAsync(long inspectionId,long lineId,long imageId,string branchCode,long actorId,CancellationToken ct=default)
    {
        string? path=null;
        await unitOfWork.ExecuteInTransactionAsync(async token=>
        {
            var image=await FindAsync(inspectionId,lineId,imageId,branchCode,tracking:true,token);
            if(image.QualityInspectionDispositionId.HasValue)
                throw AppException.Conflict(Message(QualityMessageKeys.InspectionImageLockedAfterDecision));
            path=image.StoragePath;
            image.IsDeleted=true;
            image.DeletedBy=actorId;
            image.DeletedDate=DateTime.UtcNow;
            await db.SaveChangesAsync(token);
            return true;
        },ct,IsolationLevel.Serializable);

        if(path is null)return;
        try{storage.Delete(PrivateUploadArea.QualityInspection,path);}
        catch(Exception error){logger.LogWarning(error,"Silinen kalite görseli diskten kaldırılamadı: {Path}",path);}
    }

    internal static string NormalizeDraftDispositionKey(string value)
    {
        var normalized=Clean(value,MaximumDraftDispositionKeyLength);
        if(string.IsNullOrWhiteSpace(normalized))
            throw AppException.BadRequest(QualityMessageKeys.DraftDispositionKeyRequired);
        return normalized;
    }

    private IQueryable<QualityInspectionImage> Query(long inspectionId,long lineId,string branchCode)=>
        db.QualityInspectionImages.AsNoTracking()
            .Where(x=>x.QualityInspectionId==inspectionId&&x.QualityInspectionLineId==lineId&&x.BranchCode==branchCode)
            .OrderByDescending(x=>x.CreatedDate).ThenByDescending(x=>x.Id);

    private async Task EnsureLineAsync(long inspectionId,long lineId,string branchCode,CancellationToken ct)
    {
        if(!await db.QualityInspectionLines.AsNoTracking().AnyAsync(x=>x.Id==lineId&&x.QualityInspectionId==inspectionId&&x.BranchCode==branchCode,ct))
            throw AppException.NotFound(Message(QualityMessageKeys.InspectionLineNotFound));
    }

    private async Task<QualityInspectionImage> FindAsync(long inspectionId,long lineId,long imageId,string branchCode,bool tracking,CancellationToken ct)
    {
        var query=tracking?db.QualityInspectionImages.AsQueryable():db.QualityInspectionImages.AsNoTracking();
        return await query.FirstOrDefaultAsync(x=>x.Id==imageId&&x.QualityInspectionId==inspectionId&&x.QualityInspectionLineId==lineId&&x.BranchCode==branchCode,ct)
            ??throw AppException.NotFound(Message(QualityMessageKeys.InspectionImageNotFound));
    }

    private static QualityInspectionImageDto ToDto(QualityInspectionImage image)=>new(
        image.Id,image.QualityInspectionId,image.QualityInspectionLineId,image.QualityInspectionDispositionId,
        image.DraftDispositionKey,
        $"/api/quality/inspections/{image.QualityInspectionId}/lines/{image.QualityInspectionLineId}/images/{image.Id}/content",
        image.OriginalFileName,image.ContentType,image.FileLength,image.Caption,image.CreatedBy,image.CreatedDate);
    private static string NormalizeContentType(string? value)=>(value??string.Empty).Split(';',2)[0].Trim().ToLowerInvariant();
    private static string? Clean(string? value,int maxLength)
    {
        if(string.IsNullOrWhiteSpace(value))return null;
        var trimmed=value.Trim();
        return trimmed.Length<=maxLength?trimmed:trimmed[..maxLength];
    }
    private static string? Clean(string? value)=>Clean(value,500);
    private string Message(string key,params object[] arguments)=>
        arguments.Length==0?localizer[key].Value:localizer[key,arguments].Value;
}
