using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using verii_wms_api_v2.Modules.Audit.Application;
using verii_wms_api_v2.Modules.BarcodeDesigner.Application;
using verii_wms_api_v2.Modules.BarcodeDesigner.Domain;
using verii_wms_api_v2.Modules.GoodsReceipt.Application;
using verii_wms_api_v2.Modules.GoodsReceipt.Domain;
using verii_wms_api_v2.Modules.Identity.Infrastructure;
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

    private sealed class Fixture : IAsyncDisposable
    {
        public const long AssignedUserId = 42;
        public const decimal PlannedQuantity = 12.5m;

        private Fixture(
            WmsDbContext db,
            UnitOfWork uow,
            GoodsReceiptLabelService service,
            long headerId,
            long taskId,
            long taskLineId)
        {
            Db = db;
            UnitOfWork = uow;
            Service = service;
            HeaderId = headerId;
            TaskId = taskId;
            TaskLineId = taskLineId;
        }

        public WmsDbContext Db { get; }
        public UnitOfWork UnitOfWork { get; }
        public GoodsReceiptLabelService Service { get; }
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
            var header = new GoodsReceiptHeader
            {
                BranchCode = "0",
                DocumentNo = "GR-TEST-001",
                DocumentDate = DateOnly.FromDateTime(DateTime.Today),
                TargetWarehouseId = 1,
                ReceivingLocationId = 1,
                Status = WarehouseOperationStatus.Released
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
            db.GoodsReceiptHeaders.Add(header);
            await db.SaveChangesAsync();

            var headerId = header.Id;
            var taskId = task.Id;
            var taskLineId = taskLine.Id;
            db.ChangeTracker.Clear();

            var uow = new UnitOfWork(db, new HttpContextAccessor());
            var service = new GoodsReceiptLabelService(
                uow, new FakeBarcodePolicyService(), new NullAuditLogWriter());
            return new Fixture(db, uow, service, headerId, taskId, taskLineId);
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
}
