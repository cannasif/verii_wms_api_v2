using Microsoft.EntityFrameworkCore;
using verii_wms_api_v2.Modules.SystemManagement.Application;
using verii_wms_api_v2.Modules.SystemManagement.Domain;
using verii_wms_api_v2.Shared;
using verii_wms_api_v2.Shared.Application.Abstractions.Persistence;

namespace verii_wms_api_v2.Modules.SystemManagement.Infrastructure;

public sealed class HangfireExecutionLogStore(IUnitOfWork unitOfWork) : IHangfireExecutionLogStore
{
    private IGenericRepository<HangfireExecutionLog> Logs => unitOfWork.Repository<HangfireExecutionLog>();

    public async Task<long> CreateAsync(HangfireExecutionLog entity, CancellationToken cancellationToken = default)
    {
        await Logs.AddAsync(entity, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return entity.Id;
    }

    public async Task UpdateAsync(long id, Action<HangfireExecutionLog> update, CancellationToken cancellationToken = default)
    {
        var entity = await Logs.FindByIdAsync(id, tracking: true, cancellationToken);
        if (entity is null) return;
        update(entity);
        entity.UpdatedDate = DateTime.UtcNow;
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task<PagedResponse<HangfireExecutionRow>> GetPagedAsync(PagedRequest request, CancellationToken cancellationToken = default)
    {
        var rows = Logs.Query()
            .Where(x => string.IsNullOrWhiteSpace(request.LegacySearch) || x.JobKey.Contains(request.LegacySearch) || x.Status.Contains(request.LegacySearch) || (x.ErrorMessage != null && x.ErrorMessage.Contains(request.LegacySearch)))
            .Select(x => new HangfireExecutionRow(x.Id, x.JobKey, x.HangfireJobId, x.TriggerSource, x.Status, x.StartedAt, x.CompletedAt, x.DurationMs, x.SourceCount, x.InsertedCount, x.UpdatedCount, x.DeactivatedCount, x.ResultSummary, x.ErrorType, x.ErrorMessage, x.StackTrace, x.CreatedBy, x.CreatedDate, x.UpdatedBy, x.UpdatedDate))
            .ApplyAdvancedFilters(request);
        var sorted = string.IsNullOrWhiteSpace(request.SortBy)
            ? rows.OrderByDescending(x => x.StartedAt).ThenByDescending(x => x.Id)
            : rows.ApplySort(request, nameof(HangfireExecutionRow.StartedAt));
        return await sorted.ToPagedResponseAsync(request, cancellationToken);
    }
}
