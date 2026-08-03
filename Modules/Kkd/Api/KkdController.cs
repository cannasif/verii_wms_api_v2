using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using verii_wms_api_v2.Modules.AccessControl.Application;
using verii_wms_api_v2.Modules.BarcodeDesigner.Application;
using verii_wms_api_v2.Modules.Identity.Infrastructure;
using verii_wms_api_v2.Modules.Kkd.Application;
using verii_wms_api_v2.Shared;
using verii_wms_api_v2.Shared.Application.Exceptions;

namespace verii_wms_api_v2.Modules.Kkd.Api;

[Authorize, ApiController, Route("api/kkd")]
public sealed class KkdController(
    IKkdDefinitionService definitions,
    IKkdEntitlementService entitlements,
    IKkdDistributionService distributions,
    IKkdReportService reports,
    IKkdPolicyService policy,
    IWarehouseBarcodeResolver barcodeResolver,
    IPermissionAuthorizationService permissions) : ControllerBase
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
    { await Require("WMS.KKD.DISTRIBUTION.OPERATE", ct); return Ok(ApiResponse<KkdEmployeeRow>.Ok(await definitions.ResolveEmployeeByQrAsync(request.QrCode, ct))); }

    [HttpGet("matrices")]
    public async Task<IActionResult> Matrices(CancellationToken ct)
    { await Require("WMS.KKD.MATRICES.VIEW", ct); return Ok(ApiResponse<IReadOnlyList<KkdMatrixRow>>.Ok(await definitions.GetMatricesAsync(ct))); }

    [HttpPost("matrices")]
    [HttpPut("matrices/{id:long}")]
    public async Task<IActionResult> UpsertMatrix(long? id, KkdMatrixUpsertRequest request, CancellationToken ct)
    { await Require("WMS.KKD.MATRICES.MANAGE", ct); return Ok(ApiResponse<long>.Ok(await definitions.UpsertMatrixAsync(id, request, UserId(), ct), "KKD hak matrisi kaydedildi.")); }

    [HttpPost("overrides")]
    public async Task<IActionResult> CreateOverride(KkdOverrideCreateRequest request, CancellationToken ct)
    { await Require("WMS.KKD.OVERRIDES.MANAGE", ct); return Ok(ApiResponse<long>.Ok(await definitions.CreateOverrideAsync(request, UserId(), ct), "Personel ek hakkı kaydedildi.")); }

    [HttpPost("entitlements/check")]
    public async Task<IActionResult> Check(KkdEntitlementCheckRequest request, CancellationToken ct)
    { await Require("WMS.KKD.ENTITLEMENT.CHECK", ct); return Ok(ApiResponse<KkdEntitlementCheckResult>.Ok(await entitlements.CheckAsync(request, ct))); }

    [HttpGet("distributions")]
    public async Task<IActionResult> Distributions(CancellationToken ct)
    { await Require("WMS.KKD.DISTRIBUTION.OPERATE", ct); return Ok(ApiResponse<IReadOnlyList<KkdDistributionRow>>.Ok(await distributions.GetRecentAsync(UserId(), ct))); }

    [HttpGet("distributions/context/{employeeId:long}")]
    public async Task<IActionResult> DistributionContext(long employeeId, CancellationToken ct)
    { await Require("WMS.KKD.DISTRIBUTION.OPERATE", ct); return Ok(ApiResponse<KkdDistributionContext>.Ok(await distributions.GetContextAsync(employeeId, ct))); }

    [HttpGet("distributions/context/{employeeId:long}/lines")]
    public async Task<IActionResult> DistributionOrderLines(long employeeId, [FromQuery] string orderNumbersCsv, CancellationToken ct)
    { await Require("WMS.KKD.DISTRIBUTION.OPERATE", ct); return Ok(ApiResponse<IReadOnlyList<KkdOpenOrderLine>>.Ok(await distributions.GetOpenOrderLinesAsync(employeeId, orderNumbersCsv, ct))); }

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
        await Require("WMS.KKD.OVERRIDES.MANAGE", ct);
        var result = await distributions.DecideExcessApprovalAsync(id, request, UserId(), ct);
        return Ok(ApiResponse<KkdDistributionRow>.Ok(result,
            request.Approve ? "KKD kota aşımı onaylandı." : "KKD kota aşımı reddedildi."));
    }

    [HttpPost("distributions/{id:long}/cancel")]
    public async Task<IActionResult> CancelDistribution(long id, KkdDistributionCancelRequest request, CancellationToken ct)
    { await Require("WMS.KKD.DISTRIBUTION.OPERATE", ct); await distributions.CancelAsync(id, request.IdempotencyKey, request.Reason, UserId(), ct); return Ok(ApiResponse<object?>.Ok(null, "KKD dağıtımı iptal edildi ve ayrılan hak serbest bırakıldı.")); }

    [HttpGet("reports/validation-logs")]
    public async Task<IActionResult> ValidationLogs([FromQuery] int take = 250, CancellationToken ct = default)
    { await Require("WMS.KKD.REPORTS.VIEW", ct); return Ok(ApiResponse<IReadOnlyList<KkdValidationLogRow>>.Ok(await reports.GetValidationLogsAsync(take, ct))); }

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
}
