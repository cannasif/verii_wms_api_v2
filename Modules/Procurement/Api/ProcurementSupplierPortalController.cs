using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using verii_wms_api_v2.Modules.Procurement.Application;
using verii_wms_api_v2.Shared;

namespace verii_wms_api_v2.Modules.Procurement.Api;

[AllowAnonymous,ApiController,Route("api/public/procurement/quotes"),EnableRateLimiting("supplier-portal")]
public sealed class ProcurementSupplierPortalController(IProcurementSupplierPortalService service):ControllerBase
{
    [HttpGet("{token}")]
    public async Task<IActionResult> Get(string token,CancellationToken ct)=>Ok(ApiResponse<SupplierPortalQuote>.Ok(await service.GetAsync(token,ct)));

    [HttpPut("{token}/draft")]
    public async Task<IActionResult> SaveDraft(string token,SaveSupplierPortalQuoteRequest request,CancellationToken ct){await service.SaveDraftAsync(token,request,ct);return Ok(ApiResponse<bool>.Ok(true,"Teklif taslağı kaydedildi."));}

    [HttpPost("{token}/submit")]
    public async Task<IActionResult> Submit(string token,SaveSupplierPortalQuoteRequest request,CancellationToken ct){await service.SubmitAsync(token,request,ct);return Ok(ApiResponse<bool>.Ok(true,"Teklif satınalma ekibine gönderildi."));}
}
