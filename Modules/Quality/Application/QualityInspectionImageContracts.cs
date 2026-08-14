namespace verii_wms_api_v2.Modules.Quality.Application;

public sealed record QualityInspectionImageDto(
    long Id,
    long QualityInspectionId,
    long QualityInspectionLineId,
    string ContentUrl,
    string OriginalFileName,
    string ContentType,
    long FileLength,
    string? Caption,
    long? UploadedBy,
    DateTime? UploadedAtUtc);

public sealed record QualityInspectionImageUpload(
    Stream Content,
    string FileName,
    string? ContentType,
    long Length,
    string? Caption);

public sealed record QualityInspectionImageContent(
    Stream Content,
    string ContentType,
    string OriginalFileName,
    long FileLength);

public interface IQualityInspectionImageService
{
    Task<IReadOnlyList<QualityInspectionImageDto>> ListAsync(long inspectionId,long lineId,string branchCode,CancellationToken ct=default);
    Task<IReadOnlyList<QualityInspectionImageDto>> UploadAsync(long inspectionId,long lineId,string branchCode,long actorId,IReadOnlyList<QualityInspectionImageUpload> uploads,CancellationToken ct=default);
    Task<QualityInspectionImageContent> OpenAsync(long inspectionId,long lineId,long imageId,string branchCode,CancellationToken ct=default);
    Task DeleteAsync(long inspectionId,long lineId,long imageId,string branchCode,long actorId,CancellationToken ct=default);
}
