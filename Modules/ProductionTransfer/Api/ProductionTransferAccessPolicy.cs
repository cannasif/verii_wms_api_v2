using System.Security.Claims;
using verii_wms_api_v2.Modules.AccessControl.Application;

namespace verii_wms_api_v2.Modules.ProductionTransfer.Api;

public static class ProductionTransferAccessPolicy
{
    public static IReadOnlyList<string> EffectivePolicyReadPermissions { get; } =
    [
        "WMS.PRODUCTION_TRANSFER.VIEW",
        "WMS.PRODUCTION_TRANSFER.CREATE",
        "WMS.PRODUCTION_TRANSFER.UPDATE",
        "WMS.PRODUCTION_TRANSFER.DELETE",
        "WMS.PRODUCTION_TRANSFER.APPROVE",
        "WMS.PRODUCTION_TRANSFER.OPERATE",
        "WMS.PRODUCTION_TRANSFER.ASSIGN",
        "WMS.PRODUCTION_TRANSFER.CANCEL",
        "WMS.PRODUCTION_TRANSFER.SETTINGS.VIEW",
    ];

    public static async Task<bool> CanReadEffectivePolicyAsync(
        IPermissionAuthorizationService permissions,
        ClaimsPrincipal principal,
        CancellationToken cancellationToken)
    {
        foreach (var permission in EffectivePolicyReadPermissions)
            if (await permissions.HasPermissionAsync(principal, permission, cancellationToken))
                return true;

        return false;
    }
}
