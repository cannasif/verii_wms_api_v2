using System.Text.Json;
using System.Text.Json.Serialization;
using verii_wms_api_v2.Shared;

namespace verii_wms_api_v2.Modules.Audit.Application;

public sealed record AuditLogRow(long Id, string TraceId, string ActionType, string EntityType, string EntityId, string Result, string Source, string? Reason, string? FailureReason, string BranchCode, string? RequestPath, string? RequestMethod, long? PerformedByUserId, string? PerformedByUserEmail, string? OldValuesJson, string? NewValuesJson, string? ChangedFieldsJson, DateTime? CreatedDate,
    [property: JsonIgnore] string? EntitySearchText=null,
    [property: JsonIgnore] string? RequestSearchText=null,
    [property: JsonIgnore] string? PerformedBySearchText=null);
public sealed record AuditLogDetail(long Id, string TraceId, string ActionType, string EntityType, string EntityId, string Result, string Source, string? Reason, string? FailureReason, string BranchCode, string? RequestPath, string? RequestMethod, long? PerformedByUserId, string? PerformedByUserEmail, DateTime? CreatedDate, JsonElement? OldValues, JsonElement? NewValues, JsonElement? ChangedFields);

public interface IAuditLogQueryService
{
    Task<PagedResponse<AuditLogRow>> GetPagedAsync(PagedRequest request, CancellationToken ct);
    Task<AuditLogDetail> GetByIdAsync(long id, CancellationToken ct);
}
