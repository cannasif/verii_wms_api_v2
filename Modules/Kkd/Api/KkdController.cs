using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using verii_wms_api_v2.Modules.AccessControl.Application;
using verii_wms_api_v2.Modules.BarcodeDesigner.Application;
using verii_wms_api_v2.Modules.Identity.Infrastructure;
using verii_wms_api_v2.Modules.Kkd.Application;
using verii_wms_api_v2.Modules.Kkd.Localization;
using verii_wms_api_v2.Shared;
using verii_wms_api_v2.Shared.Application.Exceptions;

namespace verii_wms_api_v2.Modules.Kkd.Api;

[Authorize, ApiController, Route("api/kkd")]
public sealed class KkdController(
    IKkdDefinitionService definitions,
    IKkdEntitlementService entitlements,
    IKkdDistributionService distributions,
    IKkdRequestService requests,
    IKkdPreparationTaskService preparationTasks,
    IKkdReportService reports,
    IKkdPolicyService policy,
    IWarehouseBarcodeResolver barcodeResolver,
    IPermissionAuthorizationService permissions,
    IStringLocalizer<KkdRequestResource> requestLocalizer) : ControllerBase
{
    [HttpGet("policy")]
    public async Task<IActionResult> GetPolicy(CancellationToken ct)
    { await Require("WMS.KKD.POLICY.VIEW", ct); return Ok(ApiResponse<KkdPolicyDto>.Ok(await policy.GetAsync(BranchCode(), ct))); }

    [HttpPut("policy")]
    public async Task<IActionResult> UpdatePolicy(UpdateKkdPolicyRequest request, CancellationToken ct)
    { await Require("WMS.KKD.POLICY.MANAGE", ct); return Ok(ApiResponse<KkdPolicyDto>.Ok(await policy.UpdateAsync(BranchCode(), request, UserId(), ct), "KKD süreç politikası kaydedildi.")); }

    [HttpGet("departments")]
    public async Task<IActionResult> Departments(CancellationToken ct)
    { await Require("WMS.KKD.DEFINITIONS.VIEW", ct); return Ok(ApiResponse<IReadOnlyList<KkdLookupRow>>.Ok(await definitions.GetDepartmentsAsync(ct))); }

    [HttpPost("departments")]
    [HttpPut("departments/{id:long}")]
    public async Task<IActionResult> UpsertDepartment(long? id, KkdDepartmentUpsertRequest request, CancellationToken ct)
    { await Require("WMS.KKD.DEFINITIONS.MANAGE", ct); return Ok(ApiResponse<long>.Ok(await definitions.UpsertDepartmentAsync(id, request, UserId(), ct), "KKD departmanı kaydedildi.")); }

    [HttpGet("roles")]
    public async Task<IActionResult> Roles([FromQuery] long? departmentId, CancellationToken ct)
    { await Require("WMS.KKD.DEFINITIONS.VIEW", ct); return Ok(ApiResponse<IReadOnlyList<KkdLookupRow>>.Ok(await definitions.GetRolesAsync(departmentId, ct))); }

    [HttpPost("lookups/customers/paged")]
    public async Task<IActionResult> CustomersPaged(PagedRequest request, CancellationToken ct)
    { await Require("WMS.KKD.DEFINITIONS.VIEW", ct); return Ok(ApiResponse<PagedResponse<KkdCustomerLookupRow>>.Ok(await definitions.GetCustomersPagedAsync(request, ct))); }

    [HttpPost("lookups/stocks/paged")]
    public async Task<IActionResult> StocksPaged(PagedRequest request, [FromQuery] string? groupCode, CancellationToken ct)
    { await Require("WMS.KKD.DEFINITIONS.VIEW", ct); return Ok(ApiResponse<PagedResponse<KkdStockLookupRow>>.Ok(await definitions.GetStocksPagedAsync(request, groupCode, ct))); }

    [HttpPost("lookups/stocks/resolve")]
    public async Task<IActionResult> ResolveStocks(KkdStockBulkResolveRequest request, CancellationToken ct)
    { await Require("WMS.KKD.DEFINITIONS.VIEW", ct); return Ok(ApiResponse<IReadOnlyList<KkdStockBulkResolveRow>>.Ok(await definitions.ResolveStocksAsync(request, ct))); }

    [HttpPost("lookups/stock-groups/paged")]
    public async Task<IActionResult> StockGroupsPaged(PagedRequest request, CancellationToken ct)
    { await Require("WMS.KKD.DEFINITIONS.VIEW", ct); return Ok(ApiResponse<PagedResponse<KkdStockGroupLookupRow>>.Ok(await definitions.GetStockGroupsPagedAsync(request, ct))); }

    [HttpPost("lookups/entitlement-groups/paged")]
    public async Task<IActionResult> EntitlementGroupsPaged(PagedRequest request, CancellationToken ct)
    { await Require("WMS.KKD.DEFINITIONS.VIEW", ct); return Ok(ApiResponse<PagedResponse<KkdEntitlementGroupLookupRow>>.Ok(await definitions.GetEntitlementGroupsPagedAsync(request, ct))); }

    [HttpPost("roles")]
    [HttpPut("roles/{id:long}")]
    public async Task<IActionResult> UpsertRole(long? id, KkdRoleUpsertRequest request, CancellationToken ct)
    { await Require("WMS.KKD.DEFINITIONS.MANAGE", ct); return Ok(ApiResponse<long>.Ok(await definitions.UpsertRoleAsync(id, request, UserId(), ct), "KKD rolü kaydedildi.")); }

    [HttpGet("employees")]
    public async Task<IActionResult> Employees(CancellationToken ct)
    { await Require("WMS.KKD.EMPLOYEES.VIEW", ct); return Ok(ApiResponse<IReadOnlyList<KkdEmployeeRow>>.Ok(await definitions.GetEmployeesAsync(ct))); }

    [HttpPost("employees")]
    [HttpPut("employees/{id:long}")]
    public async Task<IActionResult> UpsertEmployee(long? id, KkdEmployeeUpsertRequest request, CancellationToken ct)
    { await Require("WMS.KKD.EMPLOYEES.MANAGE", ct); return Ok(ApiResponse<long>.Ok(await definitions.UpsertEmployeeAsync(id, request, UserId(), ct), "KKD personeli kaydedildi.")); }

    [HttpPost("employees/qr-resolve")]
    public async Task<IActionResult> ResolveEmployeeByQr(KkdEmployeeQrResolveRequest request, CancellationToken ct)
    { await Require("WMS.KKD.EMPLOYEES.VIEW", ct); return Ok(ApiResponse<KkdEmployeeRow>.Ok(await definitions.ResolveEmployeeByQrAsync(request.QrCode, ct))); }

    [HttpGet("matrices")]
    public async Task<IActionResult> Matrices(CancellationToken ct)
    { await Require("WMS.KKD.MATRICES.VIEW", ct); return Ok(ApiResponse<IReadOnlyList<KkdMatrixRow>>.Ok(await definitions.GetMatricesAsync(ct))); }

    [HttpGet("matrices/{id:long}")]
    public async Task<IActionResult> Matrix(long id, CancellationToken ct)
    { await Require("WMS.KKD.MATRICES.VIEW", ct); return Ok(ApiResponse<KkdMatrixDetail>.Ok(await definitions.GetMatrixAsync(id, ct))); }

    [HttpPost("matrices")]
    [HttpPut("matrices/{id:long}")]
    public async Task<IActionResult> UpsertMatrix(long? id, KkdMatrixUpsertRequest request, CancellationToken ct)
    { await Require("WMS.KKD.MATRICES.MANAGE", ct); return Ok(ApiResponse<long>.Ok(await definitions.UpsertMatrixAsync(id, request, UserId(), ct), "KKD hak matrisi kaydedildi.")); }

    [HttpPost("matrices/validate")]
    [HttpPost("matrices/{id:long}/validate")]
    public async Task<IActionResult> ValidateMatrix(long? id, KkdMatrixUpsertRequest request, CancellationToken ct)
    {
        await Require("WMS.KKD.MATRICES.MANAGE", ct);
        return Ok(ApiResponse<KkdMatrixValidationResult>.Ok(await definitions.ValidateMatrixAsync(id, request, ct)));
    }

    [HttpPost("overrides/paged")]
    public async Task<IActionResult> OverridesPaged(PagedRequest request, CancellationToken ct)
    { await Require("WMS.KKD.OVERRIDES.MANAGE", ct); return Ok(ApiResponse<PagedResponse<KkdOverrideRow>>.Ok(await definitions.GetOverridesPagedAsync(request, ct))); }

    [HttpPost("overrides")]
    public async Task<IActionResult> CreateOverride(KkdOverrideCreateRequest request, CancellationToken ct)
    { await Require("WMS.KKD.OVERRIDES.MANAGE", ct); return Ok(ApiResponse<long>.Ok(await definitions.CreateOverrideAsync(request, UserId(), ct), "Personel ek hakkı kaydedildi.")); }

    [HttpPut("overrides/{id:long}")]
    public async Task<IActionResult> UpdateOverride(long id, KkdOverrideUpdateRequest request, CancellationToken ct)
    { await Require("WMS.KKD.OVERRIDES.MANAGE", ct); return Ok(ApiResponse<long>.Ok(await definitions.UpdateOverrideAsync(id, request, UserId(), ct), "Personel ek hakkı güncellendi.")); }

    [HttpDelete("overrides/{id:long}")]
    public async Task<IActionResult> DeleteOverride(long id, CancellationToken ct)
    { await Require("WMS.KKD.OVERRIDES.MANAGE", ct); await definitions.DeleteOverrideAsync(id, UserId(), ct); return Ok(ApiResponse<object?>.Ok(null, "Personel ek hakkı silindi.")); }

    [HttpPost("entitlements/check")]
    public async Task<IActionResult> Check(KkdEntitlementCheckRequest request, CancellationToken ct)
    { await Require("WMS.KKD.ENTITLEMENT.CHECK", ct); return Ok(ApiResponse<KkdEntitlementCheckResult>.Ok(await entitlements.CheckAsync(request, ct))); }

    [HttpPost("requests/paged")]
    public async Task<IActionResult> RequestsPaged(PagedRequest request, [FromQuery] string? tab, CancellationToken ct)
    {
        await Require("WMS.KKD.REQUESTS.VIEW", ct);
        var boardTab = Enum.TryParse<KkdRequestBoardTab>(tab, ignoreCase: true, out var parsed) ? parsed : KkdRequestBoardTab.All;
        return Ok(ApiResponse<PagedResponse<KkdRequestGridRow>>.Ok(await requests.GetPagedAsync(request, UserId(), boardTab, ct)));
    }

    [HttpGet("requests/tab-counts")]
    public async Task<IActionResult> RequestTabCounts(CancellationToken ct)
    { await Require("WMS.KKD.REQUESTS.VIEW", ct); return Ok(ApiResponse<KkdRequestTabCounts>.Ok(await requests.GetTabCountsAsync(UserId(), ct))); }

    [HttpGet("requests/{id:long}")]
    public async Task<IActionResult> RequestDetail(long id, CancellationToken ct)
    { await Require("WMS.KKD.REQUESTS.VIEW", ct); return Ok(ApiResponse<KkdRequestDetail>.Ok(await requests.GetDetailAsync(id, UserId(), ct))); }

    [HttpPost("requests")]
    public async Task<IActionResult> CreateRequest(KkdRequestCreateRequest request, CancellationToken ct)
    {
        await Require("WMS.KKD.REQUESTS.CREATE", ct);
        return Ok(ApiResponse<KkdRequestDetail>.Ok(await requests.CreateAsync(request, UserId(), ct), RequestMessage(KkdRequestMessageKeys.Created)));
    }

    [HttpPost("requests/{id:long}/lines/{lineId:long}/resolve")]
    public async Task<IActionResult> ResolveRequestLine(long id, long lineId, KkdRequestResolveLineRequest request, CancellationToken ct)
    {
        await Require("WMS.KKD.REQUESTS.RESOLVE", ct);
        return Ok(ApiResponse<KkdRequestDetail>.Ok(await requests.ResolveLineAsync(id, lineId, request, UserId(), ct), RequestMessage(KkdRequestMessageKeys.Resolved)));
    }

    [HttpPut("requests/{id:long}/assignment")]
    public async Task<IActionResult> AssignRequest(long id, KkdRequestAssignRequest request, CancellationToken ct)
    {
        await Require("WMS.KKD.REQUESTS.RESOLVE", ct);
        return Ok(ApiResponse<KkdRequestDetail>.Ok(await requests.AssignAsync(id, request, UserId(), ct), RequestMessage(KkdRequestMessageKeys.Assigned)));
    }

    [HttpGet("requests/{id:long}/preparation-tasks")]
    public async Task<IActionResult> RequestPreparationTasks(long id, CancellationToken ct)
    { await Require("WMS.KKD.REQUESTS.VIEW", ct); return Ok(ApiResponse<IReadOnlyList<KkdPreparationTaskRow>>.Ok(await preparationTasks.GetByRequestAsync(id, UserId(), ct))); }

    [HttpPost("requests/{id:long}/preparation-tasks")]
    public async Task<IActionResult> AssignPreparationTasks(long id, KkdPreparationAssignRequest request, CancellationToken ct)
    {
        await Require("WMS.KKD.REQUESTS.RESOLVE", ct);
        return Ok(ApiResponse<IReadOnlyList<KkdPreparationTaskRow>>.Ok(
            await preparationTasks.AssignAsync(id, request, UserId(), ct), RequestMessage(KkdRequestMessageKeys.TasksAssigned)));
    }

    [HttpPost("requests/{id:long}/claim")]
    public async Task<IActionResult> ClaimRequest(long id, KkdPreparationClaimRequest request, CancellationToken ct)
    {
        await Require("WMS.KKD.REQUESTS.RESOLVE", ct);
        return Ok(ApiResponse<KkdPreparationTaskRow>.Ok(
            await preparationTasks.ClaimAsync(id, request, UserId(), ct), RequestMessage(KkdRequestMessageKeys.TaskClaimed)));
    }

    [HttpPost("preparation-tasks/{id:long}/claim")]
    public async Task<IActionResult> ClaimPreparationTask(long id, KkdPreparationClaimTaskRequest request, CancellationToken ct)
    {
        await Require("WMS.KKD.REQUESTS.RESOLVE", ct);
        return Ok(ApiResponse<KkdPreparationTaskRow>.Ok(
            await preparationTasks.ClaimTaskAsync(id, request, UserId(), ct), RequestMessage(KkdRequestMessageKeys.TaskClaimedFromPool)));
    }

    [HttpPost("preparation-tasks/{id:long}/handoff")]
    public async Task<IActionResult> HandoffPreparationTask(long id, KkdPreparationHandoffRequest request, CancellationToken ct)
    {
        await Require("WMS.KKD.REQUESTS.RESOLVE", ct);
        return Ok(ApiResponse<KkdPreparationTaskRow>.Ok(
            await preparationTasks.HandoffAsync(id, request, UserId(), ct), RequestMessage(KkdRequestMessageKeys.TaskHandedOver)));
    }

    [HttpPost("preparation-tasks/{id:long}/return")]
    public async Task<IActionResult> ReturnPreparationTask(long id, KkdPreparationReturnRequest request, CancellationToken ct)
    {
        await Require("WMS.KKD.REQUESTS.RESOLVE", ct);
        await preparationTasks.ReturnAsync(id, request, UserId(), ct);
        return Ok(ApiResponse<object?>.Ok(null, RequestMessage(KkdRequestMessageKeys.TaskReturned)));
    }

    [HttpGet("requests/{id:long}/cancel-precheck")]
    public async Task<IActionResult> RequestCancelPrecheck(long id, CancellationToken ct)
    { await Require("WMS.KKD.REQUESTS.CANCEL", ct); return Ok(ApiResponse<KkdRequestCancelPrecheckResult>.Ok(await requests.GetCancelPrecheckAsync(id, ct))); }

    [HttpPost("requests/{id:long}/cancel")]
    public async Task<IActionResult> CancelRequest(long id, KkdRequestCancelRequest request, CancellationToken ct)
    {
        await Require("WMS.KKD.REQUESTS.CANCEL", ct);
        return Ok(ApiResponse<KkdRequestDetail>.Ok(await requests.CancelAsync(id, request, UserId(), ct), RequestMessage(KkdRequestMessageKeys.Cancelled)));
    }

    [HttpPost("requests/{id:long}/reactivate")]
    public async Task<IActionResult> ReactivateRequest(long id, KkdRequestReactivateRequest request, CancellationToken ct)
    {
        await Require("WMS.KKD.REQUESTS.CANCEL", ct);
        return Ok(ApiResponse<KkdRequestDetail>.Ok(await requests.ReactivateAsync(id, request, UserId(), ct), RequestMessage(KkdRequestMessageKeys.Reactivated)));
    }

    [HttpGet("distributions")]
    public async Task<IActionResult> Distributions(CancellationToken ct)
    { await Require("WMS.KKD.DISTRIBUTION.OPERATE", ct); return Ok(ApiResponse<IReadOnlyList<KkdDistributionRow>>.Ok(await distributions.GetRecentAsync(UserId(), ct))); }

    [HttpPost("distributions/paged")]
    public async Task<IActionResult> DistributionsPaged(PagedRequest request, CancellationToken ct)
    { await Require("WMS.KKD.DISTRIBUTION.OPERATE", ct); return Ok(ApiResponse<PagedResponse<KkdDistributionRow>>.Ok(await distributions.GetPagedAsync(request, UserId(), ct))); }

    [HttpGet("distributions/{id:long}")]
    public async Task<IActionResult> DistributionDetail(long id, CancellationToken ct)
    { await Require("WMS.KKD.DISTRIBUTION.OPERATE", ct); return Ok(ApiResponse<KkdDistributionDetail>.Ok(await distributions.GetDetailAsync(id, UserId(), ct))); }

    [HttpGet("distributions/context/{employeeId:long}")]
    public async Task<IActionResult> DistributionContext(long employeeId, [FromQuery] bool includeOpenOrders = true, CancellationToken ct = default)
    { await Require("WMS.KKD.DISTRIBUTION.OPERATE", ct); return Ok(ApiResponse<KkdDistributionContext>.Ok(await distributions.GetContextAsync(employeeId, includeOpenOrders, ct))); }

    [HttpGet("distributions/context/{employeeId:long}/lines")]
    public async Task<IActionResult> DistributionOrderLines(long employeeId, [FromQuery] string orderNumbersCsv, CancellationToken ct)
    { await Require("WMS.KKD.DISTRIBUTION.OPERATE", ct); return Ok(ApiResponse<IReadOnlyList<KkdOpenOrderLine>>.Ok(await distributions.GetOpenOrderLinesAsync(employeeId, orderNumbersCsv, ct))); }

    [HttpGet("material-requests/configuration")]
    public async Task<IActionResult> MaterialRequestConfiguration(CancellationToken ct)
    {
        await Require("WMS.KKD.DISTRIBUTION.OPERATE", ct);
        var effective = await policy.GetAsync(BranchCode(), ct);
        return Ok(ApiResponse<KkdMaterialRequestConfiguration>.Ok(new(effective.EnableMaterialRequestOrderFlow)));
    }

    [HttpGet("material-requests/context/{employeeId:long}")]
    public async Task<IActionResult> MaterialRequestContext(long employeeId, CancellationToken ct)
    {
        await RequireMaterialRequestOrderFlow(ct);
        return Ok(ApiResponse<KkdDistributionContext>.Ok(await distributions.GetContextAsync(employeeId, true, ct)));
    }

    [HttpGet("material-requests/context/{employeeId:long}/lines")]
    public async Task<IActionResult> MaterialRequestOrderLines(long employeeId, [FromQuery] string orderNumbersCsv, CancellationToken ct)
    {
        await RequireMaterialRequestOrderFlow(ct);
        return Ok(ApiResponse<IReadOnlyList<KkdOpenOrderLine>>.Ok(await distributions.GetOpenOrderLinesAsync(employeeId, orderNumbersCsv, ct)));
    }

    [HttpPost("distributions/stock/barcode-resolve")]
    public async Task<IActionResult> ResolveDistributionStock(KkdBarcodeResolveRequest request, CancellationToken ct)
    {
        await Require("WMS.KKD.DISTRIBUTION.OPERATE", ct);
        return Ok(ApiResponse<ResolvedWarehouseBarcode>.Ok(await barcodeResolver.ResolveAsync(new(
            request.Barcode, BranchCode(), WarehouseBarcodePurpose.Outbound, request.WarehouseId), ct)));
    }

    [HttpPost("distributions")]
    public async Task<IActionResult> CreateDistribution(KkdDistributionCreateRequest request, CancellationToken ct)
    { await Require("WMS.KKD.DISTRIBUTION.OPERATE", ct); return Ok(ApiResponse<KkdDistributionCreateResult>.Ok(await distributions.CreateAsync(request, UserId(), ct), "KKD dağıtımı ve ambar çıkış taslağı oluşturuldu.")); }

    [HttpPost("distributions/{id:long}/complete")]
    public async Task<IActionResult> CompleteDistribution(long id, KkdDistributionCompleteRequest request, CancellationToken ct)
    { await Require("WMS.KKD.DISTRIBUTION.OPERATE", ct); return Ok(ApiResponse<KkdDistributionCompleteResult>.Ok(await distributions.CompleteAsync(id, request, UserId(), ct), "KKD teslimi ve ERP ambar çıkışı tamamlandı.")); }

    [HttpPost("distributions/{id:long}/excess-approval")]
    public async Task<IActionResult> DecideExcessApproval(long id, KkdExcessApprovalRequest request, CancellationToken ct)
    {
        await Require("WMS.KKD.DISTRIBUTION.OPERATE", ct);
        var result = await distributions.DecideExcessApprovalAsync(id, request, UserId(), ct);
        return Ok(ApiResponse<KkdDistributionRow>.Ok(result,
            request.Approve ? "KKD kota aşımı onaylandı." : "KKD kota aşımı reddedildi."));
    }

    [HttpPost("distributions/{id:long}/cancel")]
    public async Task<IActionResult> CancelDistribution(long id, KkdDistributionCancelRequest request, CancellationToken ct)
    { await Require("WMS.KKD.DISTRIBUTION.OPERATE", ct); await distributions.CancelAsync(id, request.IdempotencyKey, request.Reason, request.ExpectedRowVersion, UserId(), ct); return Ok(ApiResponse<object?>.Ok(null, "KKD dağıtımı iptal edildi ve ayrılan hak serbest bırakıldı.")); }

    [HttpGet("reports/validation-logs")]
    public async Task<IActionResult> ValidationLogs([FromQuery] int take = 250, CancellationToken ct = default)
    { await Require("WMS.KKD.REPORTS.VIEW", ct); return Ok(ApiResponse<IReadOnlyList<KkdValidationLogRow>>.Ok(await reports.GetValidationLogsAsync(take, ct))); }

    [HttpGet("reports/remaining-entitlements/{employeeId:long}")]
    public async Task<IActionResult> RemainingEntitlements(long employeeId, [FromQuery] DateOnly? atDate = null,
        CancellationToken ct = default)
    { await Require("WMS.KKD.ENTITLEMENT.CHECK", ct); return Ok(ApiResponse<IReadOnlyList<KkdRemainingEntitlementRow>>.Ok(await reports.GetRemainingEntitlementsAsync(employeeId, atDate, ct))); }

    [HttpGet("reports/usage")]
    public async Task<IActionResult> Usage([FromQuery] string dimension = "Group", [FromQuery] DateOnly? from = null,
        [FromQuery] DateOnly? to = null, CancellationToken ct = default)
    { await Require("WMS.KKD.REPORTS.VIEW", ct); return Ok(ApiResponse<IReadOnlyList<KkdUsageSummaryRow>>.Ok(await reports.GetUsageAsync(dimension, from, to, ct))); }

    private long UserId() => long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
        ? id : throw AppException.Unauthorized("Kullanıcı kimliği bulunamadı.");

    private string BranchCode() => User.FindFirstValue(JwtTokenIssuer.BranchCodeClaim)?.Trim() is { Length: > 0 } branch
        ? branch : throw AppException.Unauthorized("Oturum şube bilgisi bulunamadı.");

    private async Task Require(string code, CancellationToken ct)
    { if (!await permissions.HasPermissionAsync(User, code, ct)) throw AppException.Forbidden(); }

    private string RequestMessage(string key) => requestLocalizer[key].Value;

    private async Task RequireMaterialRequestOrderFlow(CancellationToken ct)
    {
        await Require("WMS.KKD.DISTRIBUTION.OPERATE", ct);
        var effective = await policy.GetAsync(BranchCode(), ct);
        if (!effective.EnableMaterialRequestOrderFlow)
            throw AppException.Conflict("Malzeme talep siparişleri bu şubenin KKD süreç politikasında kapalıdır.");
    }
}
