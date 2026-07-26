using verii_wms_api_v2.Modules.ErpIntegration.Application;
using verii_wms_api_v2.Modules.WarehouseOperations.Domain;
using Xunit;

namespace verii_wms_api_v2.QueryTests;

public sealed class OperationCancellationPolicyTests
{
    [Theory]
    [InlineData(ErpIntegrationStatus.NotRequired)]
    [InlineData(ErpIntegrationStatus.Pending)]
    [InlineData(ErpIntegrationStatus.Failed)]
    public void Non_posted_operations_use_local_compensation(ErpIntegrationStatus status)
    {
        var route = OperationCancellationPolicy.Decide(status, false, true);

        Assert.Equal(OperationCancellationRoute.LocalCompensation, route);
    }

    [Theory]
    [InlineData(ErpIntegrationStatus.Succeeded)]
    [InlineData(ErpIntegrationStatus.Cancelled)]
    public void Posted_operations_use_erp_compensation_when_supported(ErpIntegrationStatus status)
    {
        var route = OperationCancellationPolicy.Decide(status, false, true);

        Assert.Equal(OperationCancellationRoute.ErpCompensation, route);
    }

    [Theory]
    [InlineData(ErpIntegrationStatus.Processing)]
    [InlineData(ErpIntegrationStatus.CommitUncertain)]
    public void Ambiguous_erp_states_require_reconciliation(ErpIntegrationStatus status)
    {
        var route = OperationCancellationPolicy.Decide(status, false, true);

        Assert.Equal(OperationCancellationRoute.ManualReconciliationRequired, route);
    }

    [Theory]
    [InlineData(ErpIntegrationStatus.Succeeded)]
    [InlineData(ErpIntegrationStatus.Cancelled)]
    public void Posted_operations_without_a_delete_adapter_are_never_reversed_locally(
        ErpIntegrationStatus status)
    {
        var route = OperationCancellationPolicy.Decide(status, false, false);

        Assert.Equal(OperationCancellationRoute.ManualReconciliationRequired, route);
    }

    [Fact]
    public void An_already_cancelled_operation_is_an_idempotent_replay()
    {
        var route = OperationCancellationPolicy.Decide(
            ErpIntegrationStatus.Cancelled,
            operationAlreadyCancelled: true,
            erpCancellationSupported: true);

        Assert.Equal(OperationCancellationRoute.AlreadyCancelled, route);
    }
}
