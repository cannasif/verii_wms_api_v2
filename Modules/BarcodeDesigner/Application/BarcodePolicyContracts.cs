using verii_wms_api_v2.Modules.BarcodeDesigner.Domain;
using verii_wms_api_v2.Shared;

namespace verii_wms_api_v2.Modules.BarcodeDesigner.Application;

public sealed record BarcodePolicySegmentRequest(int Order, BarcodePolicySegmentType SegmentType, BarcodePolicyField? SourceField, string? LiteralValue, bool IsRequired, BarcodeValueTransform Transform, int SequenceLength = 8, string DateFormat = "yyyyMMdd");
public sealed record BarcodePolicyProfileUpdateRequest(string DisplayName, string? Prefix, string Separator, bool IsEnabled, string ConcurrencyToken, IReadOnlyList<BarcodePolicySegmentRequest> Segments);
public sealed record BarcodeGenerateRequest(string IdempotencyKey, string? StockCode, string? SerialNo, string? YapCode, string? LotNo, string? WarehouseCode, string? LocationCode, string? DocumentNo);
public sealed record BarcodePolicySegmentRow(long Id, int Order, string SegmentType, string? SourceField, string? LiteralValue, bool IsRequired, string Transform, int SequenceLength, string DateFormat);
public sealed record BarcodePolicyProfileRow(long Id, string Scope, string DisplayName, string? Prefix, string Separator, long NextSequence, bool IsEnabled, string ConcurrencyToken, IReadOnlyList<BarcodePolicySegmentRow> Segments, long? UpdatedBy, DateTime? UpdatedDate);
public sealed record BarcodePolicyResponse(long Id, string PolicyKey, string DisplayName, int CurrentVersion, bool IsActive, string ConcurrencyToken, IReadOnlyList<BarcodePolicyProfileRow> Profiles, DateTime? UpdatedDate, long? UpdatedBy);
public sealed record BarcodePreviewResponse(string Value, long SequenceNo, bool Reserved, int PolicyVersion, string Scope);
public sealed record GeneratedBarcodeRow(long Id, string Scope, int PolicyVersion, string BarcodeValue, string? StockCode, string? SerialNo, string? YapCode, string? LotNo, string? WarehouseCode, string? LocationCode, string? DocumentNo, long SequenceNo, DateTime GeneratedAt, long? CreatedBy);

public interface IBarcodePolicyService
{
    Task<BarcodePolicyResponse> GetAsync(CancellationToken ct = default);
    Task<BarcodePolicyResponse> UpdateProfileAsync(BarcodePolicyScope scope, BarcodePolicyProfileUpdateRequest request, CancellationToken ct = default);
    Task<BarcodePreviewResponse> PreviewAsync(BarcodePolicyScope scope, BarcodeGenerateRequest request, CancellationToken ct = default);
    Task<BarcodePreviewResponse> GenerateAsync(BarcodePolicyScope scope, BarcodeGenerateRequest request, CancellationToken ct = default);
    Task<PagedResponse<GeneratedBarcodeRow>> GetGeneratedPagedAsync(PagedRequest request, CancellationToken ct = default);
}
