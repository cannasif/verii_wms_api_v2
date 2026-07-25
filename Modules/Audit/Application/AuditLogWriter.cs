using System.Diagnostics;
using System.Security.Claims;
using System.Text.Json;
using verii_wms_api_v2.Modules.Audit.Domain;
using verii_wms_api_v2.Shared.Application.Abstractions.Persistence;

namespace verii_wms_api_v2.Modules.Audit.Application;

public sealed class AuditLogWriter(IUnitOfWork unitOfWork, IHttpContextAccessor httpContextAccessor, ILogger<AuditLogWriter> logger) : IAuditLogWriter
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    public async Task WriteAsync(AuditLogWriteEntry entry, CancellationToken cancellationToken = default)
    {
        var context = httpContextAccessor.HttpContext; long? userId = long.TryParse(context?.User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
        var log = new AuditLog { TraceId = Activity.Current?.TraceId.ToString() ?? context?.TraceIdentifier ?? Guid.NewGuid().ToString("N"), ActionType = entry.ActionType, EntityType = entry.EntityType, EntityId = entry.EntityId, Result = entry.Result, Source = entry.Source, Reason = entry.Reason, FailureReason = entry.FailureReason, BranchCode = context?.Items["BranchCode"]?.ToString() ?? "0", RequestPath = context?.Request.Path.Value, RequestMethod = context?.Request.Method, PerformedByUserId = userId, PerformedByUserEmail = context?.User.FindFirstValue(ClaimTypes.Email), OldValuesJson = Serialize(entry.OldValues), NewValuesJson = Serialize(entry.NewValues), ChangedFieldsJson = Serialize(entry.ChangedFields), CreatedBy = userId, CreatedDate = DateTime.UtcNow };
        try { await unitOfWork.Repository<AuditLog>().AddAsync(log, cancellationToken); await unitOfWork.SaveChangesAsync(cancellationToken); }
        catch (Exception exception) { logger.LogError(exception, "Audit log write failed for {EntityType}/{EntityId}, action {ActionType}.", entry.EntityType, entry.EntityId, entry.ActionType); }
    }
    private static string? Serialize(object? value) => value is null ? null : JsonSerializer.Serialize(value, SerializerOptions);
}
