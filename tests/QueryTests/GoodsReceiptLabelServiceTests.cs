using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Localization;
using System.Globalization;
using verii_wms_api_v2.Modules.Audit.Application;
using verii_wms_api_v2.Modules.BarcodeDesigner.Application;
using verii_wms_api_v2.Modules.BarcodeDesigner.Domain;
using verii_wms_api_v2.Modules.GoodsReceipt.Application;
using verii_wms_api_v2.Modules.GoodsReceipt.Domain;
using verii_wms_api_v2.Modules.Identity.Infrastructure;
using verii_wms_api_v2.Modules.GoodsReceipt.Localization;
using verii_wms_api_v2.Modules.StockTracking.Application;
using verii_wms_api_v2.Modules.StockTracking.Domain;
using StockEntity = verii_wms_api_v2.Modules.Stock.Domain.Stock;
using verii_wms_api_v2.Modules.Warehouse.Domain;
using verii_wms_api_v2.Modules.WarehouseOperations.Domain;
using verii_wms_api_v2.Shared;
using verii_wms_api_v2.Shared.Application.Exceptions;
using verii_wms_api_v2.Shared.Infrastructure.Persistence;
using Xunit;

namespace verii_wms_api_v2.QueryTests;

public sealed class GoodsReceiptLabelServiceTests
{
    [Fact]
    public async Task Generate_reuses_existing_header_and_replays_idempotently()
    {
        await using var fixture = await Fixture.CreateAsync();
        var key = Guid.NewGuid();
        var request = fixture.Request(key);

        var created = await fixture.Service.GenerateAsync(
            fixture.HeaderId, request, Fixture.AssignedUserId,
            restrictToActorAssignment: true);
        var replay = await fixture.Service.GenerateAsync(
            fixture.HeaderId, request, Fixture.AssignedUserId,
            restrictToActorAssignment: true);

        Assert.Equal(created.Batch.Id, replay.Batch.Id);
        Assert.Single(created.Labels);
        Assert.Equal(Fixture.PlannedQuantity, created.Labels[0].Quantity);
        Assert.Equal(1, await fixture.Db.GoodsReceiptHeaders.CountAsync());
        Assert.Equal(1, await fixture.Db.Set<GoodsReceiptLabelBatch>().CountAsync());
        Assert.Equal(1, await fixture.Db.Set<GoodsReceiptLabel>().CountAsync());
    }

    [Fact]
    public async Task Generate_rejects_receiver_who_is_not_assigned_to_task()
    {
        await using var fixture = await Fixture.CreateAsync();

        var exception = await Assert.ThrowsAsync<AppException>(() =>
            fixture.Service.GenerateAsync(
                fixture.HeaderId, fixture.Request(Guid.NewGuid()), actor: 777,
                restrictToActorAssignment: true));

        Assert.Equal(StatusCodes.Status403Forbidden, exception.StatusCode);
    }

    [Fact]
    public async Task Mark_printed_allows_assigned_receiver_and_rejects_other_receiver()
    {
        await using var fixture = await Fixture.CreateAsync();
        var created = await fixture.Service.GenerateAsync(
            fixture.HeaderId, fixture.Request(Guid.NewGuid()), actor: 999,
            restrictToActorAssignment: false);
        var request = new MarkGoodsReceiptLabelsPrintedRequest(
            created.Labels.Select(x => x.Id).ToArray());

        var forbidden = await Assert.ThrowsAsync<AppException>(() =>
            fixture.Service.MarkPrintedAsync(
                request, actor: 777, restrictToActorAssignment: true));
        Assert.Equal(StatusCodes.Status403Forbidden, forbidden.StatusCode);

        await fixture.Service.MarkPrintedAsync(
            request, Fixture.AssignedUserId, restrictToActorAssignment: true);

        var label = await fixture.Db.Set<GoodsReceiptLabel>().SingleAsync();
        Assert.Equal(GoodsReceiptLabelStatus.Printed, label.Status);
        Assert.Equal(1, label.PrintCount);
    }

    [Fact]
    public async Task Generate_rejects_manual_labels_when_strategy_is_not_pre_generate()
    {
        await using var fixture = await Fixture.CreateAsync();
        var header = await fixture.Db.GoodsReceiptHeaders.SingleAsync(x => x.Id == fixture.HeaderId);
        header.LabelStrategy = GoodsReceiptLabelStrategy.GenerateOnReceipt;
        await fixture.Db.SaveChangesAsync();
        fixture.Db.ChangeTracker.Clear();

        var exception = await Assert.ThrowsAsync<AppException>(() =>
            fixture.Service.GenerateAsync(
                fixture.HeaderId,
                fixture.Request(Guid.NewGuid()),
                Fixture.AssignedUserId,
                restrictToActorAssignment: true));

        Assert.Equal(StatusCodes.Status409Conflict, exception.StatusCode);
        Assert.Empty(await fixture.Db.Set<GoodsReceiptLabel>().ToListAsync());
    }

    [Fact]
    public async Task Completed_assignee_can_print_labels_generated_during_receipt()
    {
        await using var fixture = await Fixture.CreateAsync();
        var created = await fixture.Service.GenerateAsync(
            fixture.HeaderId, fixture.Request(Guid.NewGuid()), actor: 999,
            restrictToActorAssignment: false);

        var task = await fixture.Db.Set<GoodsReceiptTask>().SingleAsync(x => x.Id == fixture.TaskId);
        var assignment = await fixture.Db.Set<GoodsReceiptTaskAssignment>()
            .SingleAsync(x => x.GrTaskId == fixture.TaskId);
        task.Status = GoodsReceiptTaskStatus.Completed;
        assignment.Status = GoodsReceiptAssignmentStatus.Completed;
        assignment.CompletedAtUtc = DateTimeOffset.UtcNow;
        await fixture.Db.SaveChangesAsync();
        fixture.Db.ChangeTracker.Clear();

        await fixture.Service.MarkPrintedAsync(
            new MarkGoodsReceiptLabelsPrintedRequest(created.Labels.Select(x => x.Id).ToArray()),
            Fixture.AssignedUserId,
            restrictToActorAssignment: true);

        Assert.Equal(GoodsReceiptLabelStatus.Printed,
            (await fixture.Db.Set<GoodsReceiptLabel>().SingleAsync()).Status);
    }

    [Fact]
    public async Task Direct_receipt_owner_can_print_receipt_generated_label_without_task_assignment()
    {
        await using var fixture = await Fixture.CreateAsync();
        var created = await fixture.Service.GenerateAsync(
            fixture.HeaderId, fixture.Request(Guid.NewGuid()), actor: 999,
            restrictToActorAssignment: false);
        var header = await fixture.Db.GoodsReceiptHeaders
            .SingleAsync(x => x.Id == fixture.HeaderId);
        var label = await fixture.Db.Set<GoodsReceiptLabel>()
            .SingleAsync(x => x.Id == created.Labels[0].Id);
        header.InitiationMode = GoodsReceiptInitiationMode.DirectReceipt;
        header.ReceivedBy = Fixture.AssignedUserId;
        label.GrTaskLineId = null;
        await fixture.Db.SaveChangesAsync();
        fixture.Db.ChangeTracker.Clear();

        await fixture.Service.MarkPrintedAsync(
            new MarkGoodsReceiptLabelsPrintedRequest([label.Id]),
            Fixture.AssignedUserId,
            restrictToActorAssignment: true);

        Assert.Equal(GoodsReceiptLabelStatus.Printed,
            (await fixture.Db.Set<GoodsReceiptLabel>().SingleAsync()).Status);
    }

    [Fact]
    public async Task Split_supersedes_source_and_creates_two_unique_traceable_labels_idempotently()
    {
        await using var fixture = await Fixture.CreateAsync();
        var generated = await fixture.Service.GenerateAsync(
            fixture.HeaderId, fixture.Request(Guid.NewGuid()), Fixture.AssignedUserId, false);
        var source = generated.Labels.Single();
        var key = Guid.NewGuid();
        var request = new SplitGoodsReceiptLabelRequest(
            key, 5m, "Fiziksel stok iki etikete ayrıldı", Convert.ToBase64String(source.RowVersion));

        var result = await fixture.Service.SplitAsync(source.Id, request, Fixture.AssignedUserId);
        var replay = await fixture.Service.SplitAsync(source.Id, request, Fixture.AssignedUserId);

        Assert.False(result.Replayed);
        Assert.True(replay.Replayed);
        Assert.Equal(GoodsReceiptLabelStatus.Split, result.Source.Status);
        Assert.Equal([5m, 7.5m], result.ChildLabels.Select(x => x.Quantity).Order().ToArray());
        Assert.Equal(2, result.ChildLabels.Select(x => x.BarcodeValue).Distinct().Count());
        Assert.All(result.ChildLabels, x => Assert.Equal(source.Id, x.ParentLabelId));
        Assert.Equal(result.ChildLabels.Select(x => x.Id), replay.ChildLabels.Select(x => x.Id));
        Assert.Equal(3, await fixture.Db.Set<GoodsReceiptLabel>().CountAsync());
        Assert.Equal(2, (await fixture.Db.Set<GoodsReceiptLabelBatch>().SingleAsync()).TotalLabelCount);
    }

    [Fact]
    public async Task Split_rejects_one_serial_per_base_unit_policy()
    {
        await using var fixture = await Fixture.CreateAsync();
        var generated = await fixture.Service.GenerateAsync(
            fixture.HeaderId, fixture.Request(Guid.NewGuid()), Fixture.AssignedUserId, false);
        fixture.Tracking.SerialQuantityRule = SerialQuantityRule.OneSerialPerBaseUnit;
        var source = generated.Labels.Single();

        var exception = await Assert.ThrowsAsync<AppException>(() => fixture.Service.SplitAsync(
            source.Id,
            new SplitGoodsReceiptLabelRequest(Guid.NewGuid(), 5m, "Bölme denemesi",
                Convert.ToBase64String(source.RowVersion)),
            Fixture.AssignedUserId));

        Assert.Equal(StatusCodes.Status409Conflict, exception.StatusCode);
        Assert.Single(await fixture.Db.Set<GoodsReceiptLabel>().ToListAsync());
    }

    [Fact]
    public async Task Split_of_received_inventory_preserves_completed_batch_and_children_can_be_printed()
    {
        await using var fixture = await Fixture.CreateAsync();
        var generated = await fixture.Service.GenerateAsync(
            fixture.HeaderId, fixture.Request(Guid.NewGuid()), Fixture.AssignedUserId, false);
        var source = await fixture.Db.Set<GoodsReceiptLabel>()
            .SingleAsync(x => x.Id == generated.Labels.Single().Id);
        var batch = await fixture.Db.Set<GoodsReceiptLabelBatch>().SingleAsync();
        source.Status = GoodsReceiptLabelStatus.Consumed;
        source.ConsumedAtUtc = DateTimeOffset.UtcNow;
        batch.Status = GoodsReceiptLabelBatchStatus.Consumed;
        batch.ConsumedLabelCount = 1;
        batch.CompletedAtUtc = DateTimeOffset.UtcNow;
        await fixture.Db.SaveChangesAsync();
        fixture.Db.ChangeTracker.Clear();

        var result = await fixture.Service.SplitAsync(source.Id,
            new SplitGoodsReceiptLabelRequest(Guid.NewGuid(), 5m, "Stoktaki etiket bölündü",
                Convert.ToBase64String(source.RowVersion)), Fixture.AssignedUserId);

        Assert.All(result.ChildLabels, x => Assert.Equal(GoodsReceiptLabelStatus.Consumed, x.Status));
        var persistedBatch = await fixture.Db.Set<GoodsReceiptLabelBatch>().SingleAsync();
        Assert.Equal(GoodsReceiptLabelBatchStatus.Consumed, persistedBatch.Status);
        Assert.Equal(2, persistedBatch.TotalLabelCount);
        Assert.Equal(2, persistedBatch.ConsumedLabelCount);

        var child = result.ChildLabels[0];
        await fixture.Service.MarkPrintedAsync(
            new MarkGoodsReceiptLabelsPrintedRequest([child.Id]), Fixture.AssignedUserId, false);
        var printedChild = await fixture.Db.Set<GoodsReceiptLabel>().SingleAsync(x => x.Id == child.Id);
        Assert.Equal(GoodsReceiptLabelStatus.Consumed, printedChild.Status);
        Assert.Equal(1, printedChild.PrintCount);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public const long AssignedUserId = 42;
        public const decimal PlannedQuantity = 12.5m;

        private Fixture(
            WmsDbContext db,
            UnitOfWork uow,
            GoodsReceiptLabelService service,
            FakeTrackingPolicyResolver tracking,
            long headerId,
            long taskId,
            long taskLineId)
        {
            Db = db;
            UnitOfWork = uow;
            Service = service;
            Tracking = tracking;
            HeaderId = headerId;
            TaskId = taskId;
            TaskLineId = taskLineId;
        }

        public WmsDbContext Db { get; }
        public UnitOfWork UnitOfWork { get; }
        public GoodsReceiptLabelService Service { get; }
        public FakeTrackingPolicyResolver Tracking { get; }
        public long HeaderId { get; }
        public long TaskId { get; }
        public long TaskLineId { get; }

        public GenerateGoodsReceiptLabelBatchRequest Request(Guid key) => new(
            key,
            TaskId,
            [new GenerateGoodsReceiptLabelLineRequest(TaskLineId)],
            "Regression test");

        public static async Task<Fixture> CreateAsync()
        {
            var options = new DbContextOptionsBuilder<WmsDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
                .ConfigureWarnings(x => x.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options;
            var db = new WmsDbContext(options);

            var warehouse = new Warehouse
            {
                BranchCode = "0",
                WarehouseCode = 1,
                WarehouseName = "Test depo"
            };
            var stock = new StockEntity
            {
                Id = 100,
                BranchCode = "0",
                ErpStockCode = "STK-001",
                StockName = "Test stok",
                BaseUnitCode = "AD"
            };
            var header = new GoodsReceiptHeader
            {
                BranchCode = "0",
                DocumentNo = "GR-TEST-001",
                DocumentDate = DateOnly.FromDateTime(DateTime.Today),
                TargetWarehouseId = 1,
                ReceivingLocationId = 1,
                Status = WarehouseOperationStatus.Released,
                LabelStrategy = GoodsReceiptLabelStrategy.PreGenerate
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
                ExpectedQuantity = PlannedQuantity,
                TargetWarehouseId = 1
            };
            var task = new GoodsReceiptTask
            {
                BranchCode = "0",
                Header = header,
                TaskNo = "GR-TEST-001-RCV-01",
                Status = GoodsReceiptTaskStatus.Assigned,
                WarehouseId = 1
            };
            var taskLine = new GoodsReceiptTaskLine
            {
                BranchCode = "0",
                Task = task,
                Line = line,
                SequenceNo = 1,
                PlannedQuantity = PlannedQuantity,
                UnitCode = "AD",
                Status = GoodsReceiptTaskStatus.Assigned
            };
            task.Assignments.Add(new GoodsReceiptTaskAssignment
            {
                BranchCode = "0",
                Task = task,
                UserId = AssignedUserId,
                Status = GoodsReceiptAssignmentStatus.Assigned,
                AssignedAtUtc = DateTimeOffset.UtcNow
            });
            task.Lines.Add(taskLine);
            header.Lines.Add(line);
            header.Tasks.Add(task);
            db.Set<Warehouse>().Add(warehouse);
            db.Set<StockEntity>().Add(stock);
            db.GoodsReceiptHeaders.Add(header);
            await db.SaveChangesAsync();

            var headerId = header.Id;
            var taskId = task.Id;
            var taskLineId = taskLine.Id;
            db.ChangeTracker.Clear();

            var uow = new UnitOfWork(db, new HttpContextAccessor());
            var tracking = new FakeTrackingPolicyResolver();
            var service = new GoodsReceiptLabelService(
                uow, new FakeBarcodePolicyService(), new NullAuditLogWriter(), tracking,
                new PassThroughLocalizer<GoodsReceiptResource>());
            return new Fixture(db, uow, service, tracking, headerId, taskId, taskLineId);
        }

        public async ValueTask DisposeAsync()
        {
            await UnitOfWork.DisposeAsync();
            await Db.DisposeAsync();
        }
    }

    private sealed class FakeBarcodePolicyService : IBarcodePolicyService
    {
        private long _sequence;

        public Task<BarcodePreviewResponse> GenerateAsync(
            BarcodePolicyScope scope, BarcodeGenerateRequest request, CancellationToken ct = default)
        {
            var sequence = Interlocked.Increment(ref _sequence);
            return Task.FromResult(new BarcodePreviewResponse(
                $"TEST-{sequence:000000}", sequence, true, 1, scope.ToString()));
        }

        public Task<BarcodePolicyResponse> GetAsync(CancellationToken ct = default) =>
            throw new NotSupportedException();
        public Task<BarcodePolicyResponse> UpdateProfileAsync(
            BarcodePolicyScope scope, BarcodePolicyProfileUpdateRequest request, CancellationToken ct = default) =>
            throw new NotSupportedException();
        public Task<BarcodePreviewResponse> PreviewAsync(
            BarcodePolicyScope scope, BarcodeGenerateRequest request, CancellationToken ct = default) =>
            throw new NotSupportedException();
        public Task<PagedResponse<GeneratedBarcodeRow>> GetGeneratedPagedAsync(
            PagedRequest request, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    private sealed class NullAuditLogWriter : IAuditLogWriter
    {
        public Task WriteAsync(AuditLogWriteEntry entry, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class FakeTrackingPolicyResolver : IStockTrackingPolicyResolver
    {
        public SerialQuantityRule SerialQuantityRule { get; set; } = SerialQuantityRule.NotApplicable;

        public Task<EffectiveStockTrackingPolicy> ResolveAsync(
            string branchCode, long stockId, CancellationToken ct = default) =>
            Task.FromResult(new EffectiveStockTrackingPolicy(stockId, "STK-001", null,
                StockTrackingType.None, false, SerialQuantityRule, false, false, false, false,
                null, true, "Test", 1, 1, "TEST"));
    }

    private sealed class PassThroughLocalizer<T> : IStringLocalizer<T>
    {
        public LocalizedString this[string name] => new(name, name, true);
        public LocalizedString this[string name, params object[] arguments] =>
            new(name, string.Format(CultureInfo.InvariantCulture, name, arguments), true);
        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
    }
}
