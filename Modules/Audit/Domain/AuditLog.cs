using verii_wms_api_v2.Shared.Domain;

namespace verii_wms_api_v2.Modules.Audit.Domain;

public sealed class AuditLog : BaseEntity
{
    public string TraceId { get; set; } = string.Empty;
    public string ActionType { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public string EntityId { get; set; } = string.Empty;
    public string Result { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string? Reason { get; set; }
    public string? FailureReason { get; set; }
    public string? RequestPath { get; set; }
    public string? RequestMethod { get; set; }
    public long? PerformedByUserId { get; set; }
    public string? PerformedByUserEmail { get; set; }
    public string? OldValuesJson { get; set; }
    public string? NewValuesJson { get; set; }
    public string? ChangedFieldsJson { get; set; }
}
