using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using verii_wms_api_v2.Modules.Audit.Domain;
using verii_wms_api_v2.Shared;
using verii_wms_api_v2.Shared.Application.Abstractions.Persistence;
using verii_wms_api_v2.Shared.Application.Exceptions;

namespace verii_wms_api_v2.Modules.Audit.Application;

public sealed class AuditLogQueryService(IUnitOfWork unitOfWork) : IAuditLogQueryService
{
    private static readonly IReadOnlyDictionary<string,string> SearchColumns=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase)
    {
        ["id"]=nameof(AuditLogRow.Id),["actionType"]=nameof(AuditLogRow.ActionType),
        ["entityType"]=nameof(AuditLogRow.EntitySearchText),["result"]=nameof(AuditLogRow.Result),
        ["source"]=nameof(AuditLogRow.Source),["performedByUserEmail"]=nameof(AuditLogRow.PerformedBySearchText),
        ["requestMethod"]=nameof(AuditLogRow.RequestSearchText),["traceId"]=nameof(AuditLogRow.TraceId)
    };
    private static readonly string[] DefaultSearchColumns=["actionType","entityType","result","source","performedByUserEmail","requestMethod","traceId"];
    public async Task<PagedResponse<AuditLogRow>> GetPagedAsync(PagedRequest request, CancellationToken ct)
    {
        var effective = string.IsNullOrWhiteSpace(request.SortBy) ? Clone(request, nameof(AuditLog.CreatedDate), "desc") : request;
        var search = effective.Search?.Trim();
        var query = unitOfWork.Repository<AuditLog>().Query().Where(x => string.IsNullOrWhiteSpace(search) || x.TraceId.Contains(search) || x.ActionType.Contains(search) || x.EntityType.Contains(search) || x.EntityId.Contains(search) || x.Result.Contains(search) || x.Source.Contains(search) || (x.PerformedByUserEmail != null && x.PerformedByUserEmail.Contains(search)))
            .Select(x => new AuditLogRow(x.Id, x.TraceId, x.ActionType, x.EntityType, x.EntityId, x.Result, x.Source, x.Reason, x.FailureReason, x.BranchCode, x.RequestPath, x.RequestMethod, x.PerformedByUserId, x.PerformedByUserEmail, x.OldValuesJson, x.NewValuesJson, x.ChangedFieldsJson, x.CreatedDate,
                x.EntityType+" "+x.EntityId,
                (x.RequestMethod??"")+" "+(x.RequestPath??""),
                (x.PerformedByUserEmail??"")+" "+(x.PerformedByUserId.HasValue?x.PerformedByUserId.Value.ToString():"System Sistem")))
            .ApplySearch(effective,SearchColumns,DefaultSearchColumns)
            .ApplyAdvancedFilters(effective).ApplySort(effective, nameof(AuditLogRow.CreatedDate));
        return await query.ToPagedResponseAsync(effective, ct);
    }

    public async Task<AuditLogDetail> GetByIdAsync(long id, CancellationToken ct)
    {
        var x = await unitOfWork.Repository<AuditLog>().FindByIdAsync(id, false, ct) ?? throw AppException.NotFound("Audit kaydı bulunamadı.");
        return new(x.Id, x.TraceId, x.ActionType, x.EntityType, x.EntityId, x.Result, x.Source, x.Reason, x.FailureReason, x.BranchCode, x.RequestPath, x.RequestMethod, x.PerformedByUserId, x.PerformedByUserEmail, x.CreatedDate, Parse(x.OldValuesJson), Parse(x.NewValuesJson), Parse(x.ChangedFieldsJson));
    }

    private static JsonElement? Parse(string? json) { if (string.IsNullOrWhiteSpace(json)) return null; try { return JsonSerializer.Deserialize<JsonElement>(json); } catch { return null; } }
    private static PagedRequest Clone(PagedRequest request, string sortBy, string direction) => new() { PageNumber = request.PageNumber, Page = request.Page, PageSize = request.PageSize, Search = request.EffectiveSearch, SearchFields = request.SearchFields, SortBy = sortBy, SortDirection = direction, FilterLogic = request.FilterLogic, Filters = request.Filters };
}
