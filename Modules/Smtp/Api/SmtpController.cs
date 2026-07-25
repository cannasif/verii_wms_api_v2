using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using verii_wms_api_v2.Modules.AccessControl.Application;
using verii_wms_api_v2.Modules.Smtp.Application;
using verii_wms_api_v2.Shared;
using verii_wms_api_v2.Shared.Application.Exceptions;

namespace verii_wms_api_v2.Modules.Smtp.Api;

[Authorize, ApiController, Route("api/smtp-settings")]
public sealed class SmtpController(ISmtpSettingsService service, IPermissionAuthorizationService permissions) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        await Require("SYSTEM.SMTP.MANAGE", cancellationToken);
        return Ok(ApiResponse<object>.Ok(await service.GetAsync(cancellationToken) ?? new object()));
    }

    [HttpPut]
    public async Task<IActionResult> Update(SmtpRequest request, CancellationToken cancellationToken)
    {
        await Require("SYSTEM.SMTP.MANAGE", cancellationToken);
        return Ok(ApiResponse<SmtpSettingsResponse>.Ok(await service.UpdateAsync(request, cancellationToken)));
    }

    [HttpPost("test")]
    public async Task<IActionResult> Test(TestMailRequest request, CancellationToken cancellationToken)
    {
        await Require("SYSTEM.SMTP.MANAGE", cancellationToken);
        await service.TestAsync(request, cancellationToken);
        return Ok(ApiResponse<bool>.Ok(true));
    }

    private async Task Require(string code, CancellationToken ct)
    {
        if (!await permissions.HasPermissionAsync(User, code, ct)) throw AppException.Forbidden();
    }
}
