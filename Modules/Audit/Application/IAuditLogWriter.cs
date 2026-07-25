namespace verii_wms_api_v2.Modules.Audit.Application;

public sealed record AuditLogWriteEntry(string ActionType, string EntityType, string EntityId, string Result, string Source, string? Reason = null, string? FailureReason = null, object? OldValues = null, object? NewValues = null, IReadOnlyCollection<string>? ChangedFields = null);
public interface IAuditLogWriter { Task WriteAsync(AuditLogWriteEntry entry, CancellationToken cancellationToken = default); }
