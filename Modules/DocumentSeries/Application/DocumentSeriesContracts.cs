using System.Text.Json.Serialization;
using verii_wms_api_v2.Modules.DocumentSeries.Domain;
using verii_wms_api_v2.Shared;

namespace verii_wms_api_v2.Modules.DocumentSeries.Application;

public sealed record DocumentSeriesUpsertRequest(
    string BranchCode,
    long? WarehouseId,
    string Code,
    string Name,
    WmsDocumentType DocumentType,
    string Prefix,
    string Separator,
    DocumentYearFormat YearFormat,
    int NumberLength,
    long StartNumber,
    long NextNumber,
    int IncrementBy,
    bool IsDefault,
    bool IsActive,
    string? Description);

public sealed class DocumentSeriesGridRow
{
    public long Id { get; init; }
    public string BranchCode { get; init; } = string.Empty;
    public long? WarehouseId { get; init; }
    public int? WarehouseCode { get; init; }
    public string? WarehouseName { get; init; }
    public string Code { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string DocumentType { get; init; } = string.Empty;
    public string Prefix { get; init; } = string.Empty;
    public string Separator { get; init; } = string.Empty;
    public string YearFormat { get; init; } = string.Empty;
    public int NumberLength { get; init; }
    public long StartNumber { get; init; }
    public long NextNumber { get; init; }
    public int IncrementBy { get; init; }
    public string PreviewDocumentNumber
    {
        get
        {
            var year = YearFormat switch { "TwoDigit" => DateTime.UtcNow.ToString("yy"), "FourDigit" => DateTime.UtcNow.ToString("yyyy"), _ => string.Empty };
            var number = NextNumber.ToString().PadLeft(NumberLength, '0');
            return string.IsNullOrEmpty(year) ? $"{Prefix}{Separator}{number}" : $"{Prefix}{Separator}{year}{Separator}{number}";
        }
    }
    public bool IsDefault { get; init; }
    public bool IsActive { get; init; }
    public bool HasIssuedNumbers { get; init; }
    public DateTime? LastIssuedAt { get; init; }
    public string? Description { get; init; }
    public long? CreatedBy { get; init; }
    public string? CreatedByName { get; init; }
    public DateTime? CreatedDate { get; init; }
    public long? UpdatedBy { get; init; }
    public string? UpdatedByName { get; init; }
    public DateTime? UpdatedDate { get; init; }
    [JsonIgnore] public string? WarehouseSearchText { get; init; }
    [JsonIgnore] public string? CreatedBySearchText { get; init; }
    [JsonIgnore] public string? UpdatedBySearchText { get; init; }
}

public sealed record DocumentSeriesLookupRow(long Id, string Code, string Name, string PreviewDocumentNumber, bool IsDefault);
public sealed record AllocatedDocumentNumber(long DocumentSeriesId, WmsDocumentType DocumentType, long SequenceNumber, string DocumentNumber, DateTime IssuedAt);

public interface IDocumentSeriesService
{
    Task<PagedResponse<DocumentSeriesGridRow>> GetPagedAsync(PagedRequest request, CancellationToken cancellationToken = default);
    Task<DocumentSeriesGridRow> GetByIdAsync(long id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DocumentSeriesLookupRow>> GetLookupAsync(WmsDocumentType documentType, long? warehouseId, CancellationToken cancellationToken = default);
    Task<long> CreateAsync(DocumentSeriesUpsertRequest request, CancellationToken cancellationToken = default);
    Task UpdateAsync(long id, DocumentSeriesUpsertRequest request, CancellationToken cancellationToken = default);
    Task DeleteAsync(long id, CancellationToken cancellationToken = default);
}

public interface IDocumentNumberAllocator
{
    Task<AllocatedDocumentNumber> AllocateAsync(long documentSeriesId, WmsDocumentType expectedDocumentType, DateTime? issuedAt = null, CancellationToken cancellationToken = default);
}
