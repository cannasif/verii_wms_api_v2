using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using verii_wms_api_v2.Modules.AccessControl.Application;
using verii_wms_api_v2.Modules.BarcodeDesigner.Application;
using verii_wms_api_v2.Shared;
using verii_wms_api_v2.Shared.Application.Exceptions;

namespace verii_wms_api_v2.Modules.BarcodeDesigner.Api;

[Authorize, ApiController, Route("api/barcodes")]
public sealed class WarehouseBarcodesController(
    IWarehouseBarcodeResolver resolver,
    IPermissionAuthorizationService permissions) : ControllerBase
{
    private static readonly string[] AllowedPermissions =
    [
        "WMS.GOODS_RECEIPT.RECEIVE",
        "WMS.WAREHOUSE_INBOUND.RECEIVE",
        "WMS.WAREHOUSE_TRANSFER.OPERATE",
        "WMS.WAREHOUSE_OUTBOUND.OPERATE",
        "WMS.SHIPPING.OPERATE",
        "WMS.STOCK_BALANCES.VIEW"
    ];

    [HttpPost("resolve")]
    public async Task<IActionResult> Resolve(
        ResolveWarehouseBarcodeRequest request,
        CancellationToken cancellationToken)
    {
        foreach (var permission in AllowedPermissions)
            if (await permissions.HasPermissionAsync(User, permission, cancellationToken))
                return Ok(ApiResponse<ResolvedWarehouseBarcode>.Ok(
                    await resolver.ResolveAsync(request, cancellationToken)));
        throw AppException.Forbidden("Barkod çözümleme için operasyon yetkiniz bulunmuyor.");
    }
}
