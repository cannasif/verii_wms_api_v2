using Hangfire;
using Hangfire.Storage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using verii_wms_api_v2.Modules.AccessControl.Application;
using verii_wms_api_v2.Modules.SystemManagement.Application;
using verii_wms_api_v2.Shared;
using verii_wms_api_v2.Shared.Application.Exceptions;

namespace verii_wms_api_v2.Modules.SystemManagement.Api;

[Authorize, ApiController, Route("api/hangfire")]
public sealed class HangfireManagementController(
    JobStorage storage,
    IRecurringJobManager manager,
    IHangfireExecutionLogService executionLogs,
    IPermissionAuthorizationService permissions) : ControllerBase
{
    [HttpGet("stats")]
    public async Task<IActionResult> Stats(CancellationToken cancellationToken)
    {
        await Require("SYSTEM.HANGFIRE.VIEW", cancellationToken);
        var statistics = storage.GetMonitoringApi().GetStatistics();
        return Ok(new { enqueued = statistics.Enqueued, processing = statistics.Processing, scheduled = statistics.Scheduled, succeeded = statistics.Succeeded, failed = statistics.Failed, deleted = statistics.Deleted, servers = statistics.Servers, queues = statistics.Queues, timestamp = DateTime.UtcNow });
    }

    [HttpGet("recurring-jobs")]
    public async Task<IActionResult> Recurring(CancellationToken cancellationToken)
    {
        await Require("SYSTEM.HANGFIRE.VIEW", cancellationToken);
        using var connection = storage.GetConnection();
        var items = connection.GetRecurringJobs().Select(x => new { x.Id, jobName = x.Job?.Type.Name, method = x.Job?.Method.Name, x.Cron, x.Queue, x.NextExecution, x.LastExecution, x.LastJobId, x.Error }).ToList();
        return Ok(new { items, total = items.Count, timestamp = DateTime.UtcNow });
    }

    [HttpPost("executions/paged")]
    public async Task<IActionResult> Executions(PagedRequest request, CancellationToken cancellationToken)
    {
        await Require("SYSTEM.HANGFIRE.VIEW", cancellationToken);
        return Ok(ApiResponse<PagedResponse<HangfireExecutionRow>>.Ok(await executionLogs.GetPagedAsync(request, cancellationToken)));
    }

    [HttpPost("recurring-jobs/{jobId}/trigger")]
    public async Task<IActionResult> Trigger(string jobId, CancellationToken cancellationToken)
    {
        await Require("SYSTEM.HANGFIRE.TRIGGER", cancellationToken);
        using var connection = storage.GetConnection();
        if (!connection.GetRecurringJobs().Any(x => string.Equals(x.Id, jobId, StringComparison.Ordinal)))
            return NotFound(ApiResponse<object>.Error("Recurring job bulunamadı."));

        try
        {
            manager.Trigger(jobId);
            return Ok(ApiResponse<object>.Ok(new { jobId, triggeredAt = DateTime.UtcNow }, "Recurring job kuyruğa alındı."));
        }
        catch (Exception exception)
        {
            await executionLogs.RecordTriggerFailureAsync(jobId, exception, CancellationToken.None);
            return StatusCode(StatusCodes.Status500InternalServerError, ApiResponse<object>.Error("Recurring job tetiklenemedi."));
        }
    }

    private async Task Require(string code, CancellationToken ct)
    {
        if (!await permissions.HasPermissionAsync(User, code, ct)) throw AppException.Forbidden();
    }
}
