using verii_wms_api_v2.Modules.ErpMirror.Application;
using verii_wms_api_v2.Modules.SystemManagement.Domain;
using verii_wms_api_v2.Shared;

namespace verii_wms_api_v2.Modules.SystemManagement.Application;

public interface IHangfireExecutionLogService
{
    Task<long> StartAsync(string jobKey, string triggerSource, CancellationToken cancellationToken = default);
    Task CompleteAsync(long logId, MirrorSyncResult result, CancellationToken cancellationToken = default);
    Task FailAsync(long logId, Exception exception, CancellationToken cancellationToken = default);
    Task RecordTriggerFailureAsync(string jobKey, Exception exception, CancellationToken cancellationToken = default);
    Task<PagedResponse<HangfireExecutionRow>> GetPagedAsync(PagedRequest request, CancellationToken cancellationToken = default);
}

public interface IHangfireExecutionLogStore
{
    Task<long> CreateAsync(HangfireExecutionLog entity, CancellationToken cancellationToken = default);
    Task UpdateAsync(long id, Action<HangfireExecutionLog> update, CancellationToken cancellationToken = default);
    Task<PagedResponse<HangfireExecutionRow>> GetPagedAsync(PagedRequest request, CancellationToken cancellationToken = default);
}

public sealed record HangfireExecutionRow(long Id, string JobKey, string? HangfireJobId, string TriggerSource, string Status, DateTime StartedAt, DateTime? CompletedAt, long? DurationMs, int? SourceCount, int? InsertedCount, int? UpdatedCount, int? DeactivatedCount, string? ResultSummary, string? ErrorType, string? ErrorMessage, string? StackTrace, long? CreatedBy, DateTime? CreatedDate, long? UpdatedBy, DateTime? UpdatedDate);

public sealed class HangfireExecutionLogService(IServiceScopeFactory scopeFactory) : IHangfireExecutionLogService
{
    public async Task<long> StartAsync(string jobKey, string triggerSource, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var entity = new HangfireExecutionLog { JobKey = Limit(jobKey, 150)!, TriggerSource = Limit(triggerSource, 30)!, Status = "Running", StartedAt = now, CreatedDate = now };
        return await WithStoreAsync(store => store.CreateAsync(entity, cancellationToken));
    }

    public Task CompleteAsync(long logId, MirrorSyncResult result, CancellationToken cancellationToken = default) =>
        UpdateAsync(logId, entity =>
        {
            CompleteTiming(entity); entity.Status = "Succeeded"; entity.SourceCount = result.SourceCount;
            entity.InsertedCount = result.Inserted; entity.UpdatedCount = result.Updated; entity.DeactivatedCount = result.Deactivated;
            entity.ResultSummary = $"{result.Entity}: kaynak={result.SourceCount}, eklenen={result.Inserted}, güncellenen={result.Updated}, pasife alınan={result.Deactivated}";
        }, cancellationToken);

    public Task FailAsync(long logId, Exception exception, CancellationToken cancellationToken = default) =>
        UpdateAsync(logId, entity => ApplyFailure(entity, exception, "Failed"), cancellationToken);

    public async Task RecordTriggerFailureAsync(string jobKey, Exception exception, CancellationToken cancellationToken = default)
    {
        var id = await StartAsync(jobKey, "ManualTrigger", cancellationToken);
        await UpdateAsync(id, entity => ApplyFailure(entity, exception, "TriggerFailed"), cancellationToken);
    }

    public Task<PagedResponse<HangfireExecutionRow>> GetPagedAsync(PagedRequest request, CancellationToken cancellationToken = default) =>
        WithStoreAsync(store => store.GetPagedAsync(request, cancellationToken));

    private Task UpdateAsync(long id, Action<HangfireExecutionLog> update, CancellationToken cancellationToken) =>
        WithStoreAsync(store => store.UpdateAsync(id, update, cancellationToken));

    private async Task<TResult> WithStoreAsync<TResult>(Func<IHangfireExecutionLogStore, Task<TResult>> action)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<IHangfireExecutionLogStore>());
    }

    private async Task WithStoreAsync(Func<IHangfireExecutionLogStore, Task> action)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        await action(scope.ServiceProvider.GetRequiredService<IHangfireExecutionLogStore>());
    }

    private static void CompleteTiming(HangfireExecutionLog entity)
    {
        entity.CompletedAt = DateTime.UtcNow;
        entity.DurationMs = Math.Max(0, (long)(entity.CompletedAt.Value - entity.StartedAt).TotalMilliseconds);
    }

    private static void ApplyFailure(HangfireExecutionLog entity, Exception exception, string status)
    {
        CompleteTiming(entity); entity.Status = status; entity.ErrorType = Limit(exception.GetType().FullName, 500);
        entity.ErrorMessage = Limit(exception.GetBaseException().Message, 4000); entity.StackTrace = exception.ToString();
    }

    private static string? Limit(string? value, int length) => string.IsNullOrEmpty(value) || value.Length <= length ? value : value[..length];
}
