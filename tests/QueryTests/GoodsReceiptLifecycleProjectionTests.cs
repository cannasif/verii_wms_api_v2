using verii_wms_api_v2.Modules.GoodsReceipt.Application;
using verii_wms_api_v2.Modules.GoodsReceipt.Domain;
using Xunit;

namespace verii_wms_api_v2.QueryTests;

public sealed class GoodsReceiptLifecycleProjectionTests
{
    [Fact]
    public void Short_close_reduces_open_task_quantity_and_completes_user_assignment()
    {
        var header = new GoodsReceiptHeader
        {
            BranchCode = "0",
            DocumentNo = "GR-SHORT-001",
            DocumentDate = DateOnly.FromDateTime(DateTime.Today)
        };
        var line = new GoodsReceiptLine
        {
            BranchCode = "0",
            Header = header,
            Id = 10,
            LineNo = 1,
            StockId = 1,
            StockCodeSnapshot = "STK-001",
            UnitCode = "AD",
            BaseUnitCode = "AD",
            ExpectedQuantity = 10,
            ReceivedQuantity = 6
        };
        var task = new GoodsReceiptTask
        {
            BranchCode = "0",
            Header = header,
            TaskNo = "GR-SHORT-001-RCV-01",
            Status = GoodsReceiptTaskStatus.InProgress
        };
        task.Lines.Add(new GoodsReceiptTaskLine
        {
            BranchCode = "0",
            Task = task,
            Line = line,
            GrLineId = line.Id,
            PlannedQuantity = 10,
            ProcessedQuantity = 6,
            UnitCode = "AD",
            Status = GoodsReceiptTaskStatus.PartiallyCompleted
        });
        task.Assignments.Add(new GoodsReceiptTaskAssignment
        {
            BranchCode = "0",
            Task = task,
            UserId = 42,
            Status = GoodsReceiptAssignmentStatus.InProgress,
            AssignedAtUtc = DateTimeOffset.UtcNow
        });
        header.Lines.Add(line);
        header.Tasks.Add(task);

        GoodsReceiptLifecycleService.ApplyShortCloseToTasks(
            header,
            new Dictionary<long, GoodsReceiptShortCloseLineRequest>
            {
                [line.Id] = new(line.Id, 4)
            },
            actor: 99);

        Assert.Equal(6, task.Lines.Single().PlannedQuantity);
        Assert.Equal(GoodsReceiptTaskStatus.Completed, task.Lines.Single().Status);
        Assert.Equal(GoodsReceiptTaskStatus.Completed, task.Status);
        Assert.Equal(GoodsReceiptAssignmentStatus.Completed, task.Assignments.Single().Status);
        Assert.NotNull(task.Assignments.Single().CompletedAtUtc);
    }
}
