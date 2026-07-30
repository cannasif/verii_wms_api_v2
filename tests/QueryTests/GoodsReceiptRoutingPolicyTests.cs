using verii_wms_api_v2.Modules.GoodsReceipt.Application;
using verii_wms_api_v2.Modules.WarehouseOperations.Domain;
using Xunit;

namespace verii_wms_api_v2.QueryTests;

public sealed class GoodsReceiptRoutingPolicyTests
{
    [Theory]
    [InlineData(OperationQualityStatus.NotRequired)]
    [InlineData(OperationQualityStatus.Passed)]
    [InlineData(OperationQualityStatus.Failed)]
    public void Completed_and_erp_posted_receipt_can_create_downstream_document(
        OperationQualityStatus qualityStatus)
    {
        Assert.True(GoodsReceiptRoutingService.CanRouteAfterErpReceipt(
            WarehouseOperationStatus.Completed,
            OperationApprovalStatus.Approved,
            qualityStatus,
            ErpIntegrationStatus.Succeeded));
    }

    [Theory]
    [InlineData(WarehouseOperationStatus.Processed, OperationQualityStatus.NotRequired, ErpIntegrationStatus.Succeeded)]
    [InlineData(WarehouseOperationStatus.Completed, OperationQualityStatus.Pending, ErpIntegrationStatus.Succeeded)]
    [InlineData(WarehouseOperationStatus.Completed, OperationQualityStatus.Passed, ErpIntegrationStatus.Pending)]
    [InlineData(WarehouseOperationStatus.Completed, OperationQualityStatus.Failed, ErpIntegrationStatus.Failed)]
    public void Incomplete_quality_or_erp_state_cannot_create_downstream_document(
        WarehouseOperationStatus operationStatus,
        OperationQualityStatus qualityStatus,
        ErpIntegrationStatus erpStatus)
    {
        Assert.False(GoodsReceiptRoutingService.CanRouteAfterErpReceipt(
            operationStatus,
            OperationApprovalStatus.Approved,
            qualityStatus,
            erpStatus));
    }
}
