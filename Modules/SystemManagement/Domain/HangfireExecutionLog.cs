using verii_wms_api_v2.Shared.Domain;

namespace verii_wms_api_v2.Modules.SystemManagement.Domain;

public sealed class HangfireExecutionLog : BaseEntity
{
    public string JobKey { get; set; } = string.Empty;
    public string? HangfireJobId { get; set; }
    public string TriggerSource { get; set; } = "Hangfire";
    public string Status { get; set; } = "Running";
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public long? DurationMs { get; set; }
    public int? SourceCount { get; set; }
    public int? InsertedCount { get; set; }
    public int? UpdatedCount { get; set; }
    public int? DeactivatedCount { get; set; }
    public string? ResultSummary { get; set; }
    public string? ErrorType { get; set; }
    public string? ErrorMessage { get; set; }
    public string? StackTrace { get; set; }
}
