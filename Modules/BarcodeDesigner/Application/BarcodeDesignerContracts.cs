using System.Text.Json.Serialization;
using verii_wms_api_v2.Modules.BarcodeDesigner.Domain;
using verii_wms_api_v2.Shared;

namespace verii_wms_api_v2.Modules.BarcodeDesigner.Application;

public sealed record BarcodeTemplateUpsertRequest(string BranchCode, string TemplateCode, string DisplayName, BarcodeLabelType LabelType, decimal WidthMm, decimal HeightMm, int Dpi, bool IsActive);
public sealed record BarcodeDraftSaveRequest(string TemplateJson, string? Notes);
public sealed record BarcodePublishRequest(long VersionId);

public sealed class BarcodeTemplateGridRow
{
    public long Id { get; init; }
    public string BranchCode { get; init; } = "0";
    public string TemplateCode { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string LabelType { get; init; } = string.Empty;
    public decimal WidthMm { get; init; }
    public decimal HeightMm { get; init; }
    public int Dpi { get; init; }
    public string EngineType { get; init; } = string.Empty;
    public bool IsActive { get; init; }
    public long? DraftVersionId { get; init; }
    public long? PublishedVersionId { get; init; }
    public long? CreatedBy { get; init; }
    public DateTime? CreatedDate { get; init; }
    public long? UpdatedBy { get; init; }
    public DateTime? UpdatedDate { get; init; }
    [JsonIgnore] public string? DimensionsSearchText { get; init; }
}

public sealed record BarcodeTemplateVersionRow(long Id, long BarcodeTemplateId, int VersionNo, bool IsPublished, DateTime? PublishedAt, string? Notes, string TemplateJson, DateTime? CreatedDate, long? CreatedBy);
public sealed record BarcodeSchemaField(string Key, string Label, string SampleValue, string Group, string TargetType = "text");

public interface IBarcodeDesignerService
{
    Task<PagedResponse<BarcodeTemplateGridRow>> GetPagedAsync(PagedRequest request, CancellationToken cancellationToken = default);
    Task<BarcodeTemplateGridRow> GetByIdAsync(long id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<BarcodeTemplateVersionRow>> GetVersionsAsync(long id, CancellationToken cancellationToken = default);
    Task<BarcodeTemplateVersionRow?> GetDraftAsync(long id, CancellationToken cancellationToken = default);
    IReadOnlyList<BarcodeSchemaField> GetSchemaFields();
    Task<long> CreateAsync(BarcodeTemplateUpsertRequest request, CancellationToken cancellationToken = default);
    Task UpdateAsync(long id, BarcodeTemplateUpsertRequest request, CancellationToken cancellationToken = default);
    Task DeleteAsync(long id, CancellationToken cancellationToken = default);
    Task<BarcodeTemplateVersionRow> SaveDraftAsync(long id, BarcodeDraftSaveRequest request, CancellationToken cancellationToken = default);
    Task<BarcodeTemplateVersionRow> PublishAsync(long id, BarcodePublishRequest request, CancellationToken cancellationToken = default);
}
