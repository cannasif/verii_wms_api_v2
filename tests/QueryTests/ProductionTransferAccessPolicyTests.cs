using System.Security.Claims;
using verii_wms_api_v2.Modules.AccessControl.Application;
using verii_wms_api_v2.Modules.ProductionTransfer.Api;
using Xunit;

namespace verii_wms_api_v2.tests.QueryTests;

public sealed class ProductionTransferAccessPolicyTests
{
    [Theory]
    [InlineData("WMS.PRODUCTION_TRANSFER.CREATE")]
    [InlineData("WMS.PRODUCTION_TRANSFER.OPERATE")]
    [InlineData("WMS.PRODUCTION_TRANSFER.VIEW")]
    [InlineData("WMS.PRODUCTION_TRANSFER.SETTINGS.VIEW")]
    public async Task Effective_policy_is_readable_by_operational_users_without_settings_access(string grantedPermission)
    {
        var permissions = new PermissionStub(grantedPermission);

        var allowed = await ProductionTransferAccessPolicy.CanReadEffectivePolicyAsync(
            permissions,
            new ClaimsPrincipal(new ClaimsIdentity()),
            CancellationToken.None);

        Assert.True(allowed);
    }

    [Fact]
    public async Task Effective_policy_rejects_users_without_a_production_transfer_permission()
    {
        var permissions = new PermissionStub("WMS.QUALITY.VIEW");

        var allowed = await ProductionTransferAccessPolicy.CanReadEffectivePolicyAsync(
            permissions,
            new ClaimsPrincipal(new ClaimsIdentity()),
            CancellationToken.None);

        Assert.False(allowed);
    }

    private sealed class PermissionStub(params string[] grantedPermissions) : IPermissionAuthorizationService
    {
        private readonly HashSet<string> granted = new(grantedPermissions, StringComparer.OrdinalIgnoreCase);

        public Task<bool> HasPermissionAsync(
            ClaimsPrincipal principal,
            string permissionCode,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(granted.Contains(permissionCode));
    }
}
