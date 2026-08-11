using verii_wms_api_v2.Modules.ProductionTransfer.Application;
using verii_wms_api_v2.Modules.ProductionTransfer.Domain;
using verii_wms_api_v2.Modules.WarehouseTransfer.Domain;
using Xunit;

namespace verii_wms_api_v2.tests.QueryTests;

public sealed class ProductionTransferCancellationReturnRemainderSupportTests
{
    [Fact]
    public void IsProductionCancellationReturnTask_distinguishes_iptaliade_from_warehouse_cancel_return()
    {
        var iptaliade = new WarehouseTransferTask
        {
            TaskType = WarehouseTransferTaskType.CancellationReturn,
            TaskNo = "MK202600000113-IPTALIADE1",
        };
        var warehouseReturn = new WarehouseTransferTask
        {
            TaskType = WarehouseTransferTaskType.CancellationReturn,
            TaskNo = "MK202600000113-C01",
        };

        Assert.True(ProductionWorkOrderTransferGrouping.IsProductionCancellationReturnTask(iptaliade));
        Assert.False(ProductionWorkOrderTransferGrouping.IsProductionCancellationReturnTask(warehouseReturn));
    }

    [Fact]
    public void MatchesTab_picking_excludes_transfer_after_iptaliade_finalize_cancels_header()
    {
        var iptaliade = new WarehouseTransferTask
        {
            TaskType = WarehouseTransferTaskType.CancellationReturn,
            TaskNo = "TR-100-IPTALIADE1",
            Status = WarehouseTransferTaskStatus.Completed,
        };
        var header = new WarehouseTransferHeader
        {
            Status = WarehouseTransferStatus.Cancelled,
            Tasks = [iptaliade],
        };
        var link = new ProductionTransferHeaderLink { WorkflowStatus = ProductionTransferWorkflowStatus.Cancelled };

        Assert.False(ProductionWorkOrderTransferGrouping.MatchesTab(
            ProductionWorkOrderTransferTab.Picking,
            header,
            link));
        Assert.True(ProductionWorkOrderTransferGrouping.MatchesTab(
            ProductionWorkOrderTransferTab.Cancelled,
            header,
            link));
    }
}
