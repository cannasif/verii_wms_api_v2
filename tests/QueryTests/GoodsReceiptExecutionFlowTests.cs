using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using verii_wms_api_v2.Modules.Audit.Application;
using verii_wms_api_v2.Modules.BarcodeDesigner.Application;
using verii_wms_api_v2.Modules.ErpIntegration.Application;
using verii_wms_api_v2.Modules.GoodsReceipt.Application;
using verii_wms_api_v2.Modules.GoodsReceipt.Domain;
using verii_wms_api_v2.Modules.Identity.Infrastructure;
using verii_wms_api_v2.Modules.Location.Domain;
using verii_wms_api_v2.Modules.Quality.Application;
using verii_wms_api_v2.Modules.Quality.Domain;
using verii_wms_api_v2.Modules.SerialNumberPolicy.Application;
using verii_wms_api_v2.Modules.StockMovement.Application;
using verii_wms_api_v2.Modules.StockTracking.Application;
using verii_wms_api_v2.Modules.StockTracking.Domain;
using verii_wms_api_v2.Modules.Warehouse.Domain;
using verii_wms_api_v2.Modules.WarehouseOperations.Domain;
using verii_wms_api_v2.Shared;
using verii_wms_api_v2.Shared.Infrastructure.Persistence;
using Xunit;

namespace verii_wms_api_v2.QueryTests;

public sealed class GoodsReceiptExecutionFlowTests
{
    [Fact]
    public async Task Printed_label_completes_receipt_once_and_invokes_synchronous_erp_coordinator()
    {
        var options = new DbContextOptionsBuilder<WmsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .ConfigureWarnings(x => x.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        await using var db = new WmsDbContext(options);

        var warehouse = new Warehouse
        {
            BranchCode = "0",
            WarehouseCode = 1,
            WarehouseName = "Test depo"
        };
        db.Set<Warehouse>().Add(warehouse);
        await db.SaveChangesAsync();
        var location = new WarehouseLocation
        {
            BranchCode = "0",
            WarehouseId = warehouse.Id,
            Code = "KABUL-01",
            Name = "Kabul alanı",
            LocationType = LocationTypes.Receiving,
            IsActive = true
        };
        db.Set<WarehouseLocation>().Add(location);
        await db.SaveChangesAsync();

        const long actor = 42;
        const decimal quantity = 5m;
        var header = new GoodsReceiptHeader
        {
            BranchCode = "0",
            DocumentNo = "GR-FLOW-001",
            DocumentDate = DateOnly.FromDateTime(DateTime.Today),
            TargetWarehouseId = warehouse.Id,
            ReceivingLocationId = location.Id,
            Status = WarehouseOperationStatus.InProgress,
            ErpPostingPolicy = GoodsReceiptErpPostingPolicy.AfterReceipt
        };
        var line = new GoodsReceiptLine
        {
            BranchCode = "0",
            Header = header,
            LineNo = 1,
            StockId = 100,
            StockCodeSnapshot = "STK-001",
            StockNameSnapshot = "Test stok",
            UnitCode = "AD",
            BaseUnitCode = "AD",
            ExpectedQuantity = quantity,
            TargetWarehouseId = warehouse.Id
        };
        var task = new GoodsReceiptTask
        {
            BranchCode = "0",
            Header = header,
            TaskNo = "GR-FLOW-001-RCV-01",
            Status = GoodsReceiptTaskStatus.InProgress,
            WarehouseId = warehouse.Id,
            StartedAtUtc = DateTimeOffset.UtcNow
        };
        var taskLine = new GoodsReceiptTaskLine
        {
            BranchCode = "0",
            Task = task,
            Line = line,
            SequenceNo = 1,
            PlannedQuantity = quantity,
            UnitCode = "AD",
            Status = GoodsReceiptTaskStatus.InProgress,
            ToLocationId = location.Id
        };
        task.Assignments.Add(new GoodsReceiptTaskAssignment
        {
            BranchCode = "0",
            Task = task,
            UserId = actor,
            Status = GoodsReceiptAssignmentStatus.InProgress,
            AssignedAtUtc = DateTimeOffset.UtcNow,
            StartedAtUtc = DateTimeOffset.UtcNow
        });
        task.Lines.Add(taskLine);
        header.Lines.Add(line);
        header.Tasks.Add(task);
        db.GoodsReceiptHeaders.Add(header);
        await db.SaveChangesAsync();

        var batch = new GoodsReceiptLabelBatch
        {
            BranchCode = "0",
            GrHeaderId = header.Id,
            BatchNo = "GR-FLOW-001-LB-01",
            Status = GoodsReceiptLabelBatchStatus.Printed,
            TotalLabelCount = 1,
            PrintedLabelCount = 1
        };
        batch.Labels.Add(new GoodsReceiptLabel
        {
            BranchCode = "0",
            GrHeaderId = header.Id,
            GrLineId = line.Id,
            GrTaskLineId = taskLine.Id,
            StockId = line.StockId,
            StockCodeSnapshot = line.StockCodeSnapshot,
            StockNameSnapshot = line.StockNameSnapshot,
            LabelQuantity = quantity,
            UnitCode = line.UnitCode,
            BarcodeValue = "WMS-GR-FLOW-001",
            Status = GoodsReceiptLabelStatus.Printed,
            PrintCount = 1,
            LastPrintedAtUtc = DateTimeOffset.UtcNow
        });
        db.Set<GoodsReceiptLabelBatch>().Add(batch);
        await db.SaveChangesAsync();

        var taskId = task.Id;
        var taskLineId = taskLine.Id;
        var headerId = header.Id;
        var labelId = batch.Labels.Single().Id;
        db.ChangeTracker.Clear();

        await using var uow = new UnitOfWork(db, new HttpContextAccessor());
        var movements = new RecordingStockMovementService();
        var erp = new RecordingErpCoordinator();
        var service = new GoodsReceiptExecutionService(
            uow,
            movements,
            new NoQualityPolicyResolver(),
            new NoTrackingPolicyResolver(),
            new PermissiveSerialPolicyResolver(),
            new UnexpectedBarcodeResolver(),
            new NullAuditLogWriter(),
            erp);
        var request = new ReceiveGoodsReceiptTaskRequest(
            Guid.NewGuid(),
            taskLineId,
            "WMS-GR-FLOW-001",
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            "test");

        var result = await service.ReceiveAsync(taskId, request, actor);
        var replay = await service.ReceiveAsync(taskId, request, actor);

        Assert.False(result.Replayed);
        Assert.True(replay.Replayed);
        Assert.Equal("Completed", result.TaskStatus);
        Assert.Equal(labelId, result.ConsumedLabelId);
        Assert.Equal(1, movements.PostCount);
        Assert.Equal(2, erp.CallCount);
        Assert.All(erp.GoodsReceiptIds, x => Assert.Equal(headerId, x));
        Assert.Equal(1, await db.Set<GoodsReceiptExecution>().CountAsync());

        var persistedHeader = await db.GoodsReceiptHeaders.SingleAsync(x => x.Id == headerId);
        var persistedTask = await db.Set<GoodsReceiptTask>().SingleAsync(x => x.Id == taskId);
        var persistedAssignment = await db.Set<GoodsReceiptTaskAssignment>().SingleAsync(x => x.GrTaskId == taskId);
        var persistedLine = await db.Set<GoodsReceiptLine>().SingleAsync(x => x.Id == line.Id);
        var persistedLabel = await db.Set<GoodsReceiptLabel>().SingleAsync(x => x.Id == labelId);
        var persistedBatch = await db.Set<GoodsReceiptLabelBatch>().SingleAsync(x => x.Id == batch.Id);

        Assert.Equal(WarehouseOperationStatus.Processed, persistedHeader.Status);
        Assert.Equal(GoodsReceiptTaskStatus.Completed, persistedTask.Status);
        Assert.Equal(GoodsReceiptAssignmentStatus.Completed, persistedAssignment.Status);
        Assert.Equal(quantity, persistedLine.ReceivedQuantity);
        Assert.Equal(GoodsReceiptLabelStatus.Consumed, persistedLabel.Status);
        Assert.Equal(GoodsReceiptLabelBatchStatus.Consumed, persistedBatch.Status);
    }

    private sealed class RecordingStockMovementService : IStockMovementService
    {
        public int PostCount { get; private set; }

        public Task<StockMovementPostResult> PostAsync(
            PostStockMovementRequest request, CancellationToken cancellationToken = default)
        {
            PostCount++;
            return Task.FromResult(new StockMovementPostResult(501, Guid.NewGuid(), false, request.Lines.Count));
        }

        public Task<PagedResponse<StockMovementGridRow>> GetPagedAsync(
            PagedRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<StockMovementDetail> GetByIdAsync(
            long id, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<StockMovementPostResult> ReverseAsync(
            long operationId, ReverseStockMovementRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class RecordingErpCoordinator : IGoodsReceiptErpPostingCoordinator
    {
        public List<long> GoodsReceiptIds { get; } = [];
        public int CallCount => GoodsReceiptIds.Count;

        public Task<ErpPostingResult?> PostIfEligibleAsync(
            long goodsReceiptId, long actorUserId, CancellationToken cancellationToken)
        {
            GoodsReceiptIds.Add(goodsReceiptId);
            return Task.FromResult<ErpPostingResult?>(null);
        }
    }

    private sealed class NoQualityPolicyResolver : IQualityPolicyResolver
    {
        public Task<ResolvedQualityPolicy> ResolveAsync(
            string branchCode, long stockId, string? stockGroupCode, CancellationToken ct = default) =>
            Task.FromResult(new ResolvedQualityPolicy(
                "Test", null, QualityInspectionMode.NoCheck, QualitySamplingMode.All, 100m,
                QualityFailAction.Quarantine, false, false, false, false, null,
                false, false, false));
    }

    private sealed class NoTrackingPolicyResolver : IStockTrackingPolicyResolver
    {
        public Task<EffectiveStockTrackingPolicy> ResolveAsync(
            string branchCode, long stockId, CancellationToken ct = default) =>
            Task.FromResult(new EffectiveStockTrackingPolicy(
                stockId, "STK-001", null, StockTrackingType.None, false,
                SerialQuantityRule.NotApplicable, false, false, false, false,
                null, false, "Test", null, null, null));
    }

    private sealed class PermissiveSerialPolicyResolver : ISerialNumberPolicyResolver
    {
        public Task<SerialValidationResult> ValidateAsync(
            string branchCode, long stockId, long? yapCodeId, string? serialNo,
            CancellationToken ct = default) =>
            Task.FromResult(new SerialValidationResult(
                serialNo, true, "Test", null, null, null, null, null));
    }

    private sealed class UnexpectedBarcodeResolver : IWarehouseBarcodeResolver
    {
        public Task<ResolvedWarehouseBarcode> ResolveAsync(
            ResolveWarehouseBarcodeRequest request, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Basılmış WMS etiketi harici barkod çözümleyiciye gitmemelidir.");
    }

    private sealed class NullAuditLogWriter : IAuditLogWriter
    {
        public Task WriteAsync(AuditLogWriteEntry entry, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
