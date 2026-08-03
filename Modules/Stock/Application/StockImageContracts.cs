namespace verii_wms_api_v2.Modules.Stock.Application;

public sealed record StockImageDto(long Id,long StockId,string Url,string OriginalFileName,string ContentType,long FileLength,
    string? AltText,int SortOrder,bool IsPrimary,DateTime? CreatedDate);
public sealed record StockImageUpload(Stream Content,string FileName,string? ContentType,long Length,string? AltText);
public sealed record UpdateStockImageRequest(string? AltText);
public sealed record ReorderStockImagesRequest(IReadOnlyList<long> ImageIds);

public interface IStockImageService
{
    Task<IReadOnlyList<StockImageDto>> ListAsync(long stockId,string branchCode,CancellationToken ct=default);
    Task<IReadOnlyList<StockImageDto>> UploadAsync(long stockId,string branchCode,long actorId,IReadOnlyList<StockImageUpload> uploads,CancellationToken ct=default);
    Task<StockImageDto> UpdateAsync(long stockId,long imageId,string branchCode,long actorId,string? altText,CancellationToken ct=default);
    Task<StockImageDto> SetPrimaryAsync(long stockId,long imageId,string branchCode,long actorId,CancellationToken ct=default);
    Task<IReadOnlyList<StockImageDto>> ReorderAsync(long stockId,string branchCode,long actorId,IReadOnlyList<long> imageIds,CancellationToken ct=default);
    Task DeleteAsync(long stockId,long imageId,string branchCode,long actorId,CancellationToken ct=default);
}

public interface IStockImageStorage
{
    Task<StoredStockImage> SaveAsync(string branchCode,long stockId,StockImageUpload upload,CancellationToken ct=default);
    Task DeleteIfManagedAsync(string? relativeUrl,CancellationToken ct=default);
}
public sealed record StoredStockImage(string Url,string OriginalFileName,string ContentType,long Length);
