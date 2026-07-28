using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using verii_wms_api_v2.Modules.BarcodeDesigner.Application;
using verii_wms_api_v2.Modules.BarcodeDesigner.Domain;
using verii_wms_api_v2.Modules.GoodsReceipt.Application;
using verii_wms_api_v2.Modules.GoodsReceipt.Domain;
using verii_wms_api_v2.Modules.Identity.Infrastructure;
using verii_wms_api_v2.Modules.Warehouse.Domain;
using verii_wms_api_v2.Shared;
using verii_wms_api_v2.Shared.Infrastructure.Persistence;
using Xunit;

namespace verii_wms_api_v2.QueryTests;

public sealed class GoodsReceiptOnReceiptLabelServiceTests
{
    [Fact]
    public async Task Generate_on_receipt_creates_one_tracked_label_and_replays_idempotently()
    {
        await using var fixture = await Fixture.CreateAsync(GoodsReceiptLabelStrategy.GenerateOnReceipt, withTask: true);

        var first = await fixture.Service.GenerateForExecutionAsync(
            fixture.Header, fixture.Execution, fixture.Execution.Lines.ToArray(), Fixture.Actor);
        var replay = await fixture.Service.GenerateForExecutionAsync(
            fixture.Header, fixture.Execution, fixture.Execution.Lines.ToArray(), Fixture.Actor);

        Assert.Single(first);
        Assert.Equal(first, replay);
        Assert.Equal(1, await fixture.Db.Set<GoodsReceiptLabelBatch>().CountAsync());
        var label = await fixture.Db.Set<GoodsReceiptLabel>().SingleAsync();
        Assert.Equal(fixture.TaskLineId, label.GrTaskLineId);
        Assert.Equal(3.5m, label.LabelQuantity);
        Assert.Equal("LOT-01", label.LotNo);
        Assert.Equal("SER-01", label.SerialNo);
        Assert.Equal(GoodsReceiptLabelStatus.Generated, label.Status);
    }

    [Fact]
    public async Task Other_label_strategies_do_not_generate_post_receipt_labels()
    {
        await using var fixture = await Fixture.CreateAsync(GoodsReceiptLabelStrategy.None, withTask: true);

        var ids = await fixture.Service.GenerateForExecutionAsync(
            fixture.Header, fixture.Execution, fixture.Execution.Lines.ToArray(), Fixture.Actor);

        Assert.Empty(ids);
        Assert.Empty(await fixture.Db.Set<GoodsReceiptLabelBatch>().ToListAsync());
    }

    [Fact]
    public async Task Direct_receipt_label_is_created_without_a_task_link()
    {
        await using var fixture = await Fixture.CreateAsync(GoodsReceiptLabelStrategy.GenerateOnReceipt, withTask: false);

        var ids = await fixture.Service.GenerateForExecutionAsync(
            fixture.Header, fixture.Execution, fixture.Execution.Lines.ToArray(), Fixture.Actor);

        Assert.Single(ids);
        Assert.Null((await fixture.Db.Set<GoodsReceiptLabel>().SingleAsync()).GrTaskLineId);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public const long Actor = 42;

        private Fixture(
            WmsDbContext db,
            UnitOfWork unitOfWork,
            GoodsReceiptOnReceiptLabelService service,
            GoodsReceiptHeader header,
            GoodsReceiptExecution execution,
            long? taskLineId)
        {
            Db = db;
            UnitOfWork = unitOfWork;
            Service = service;
            Header = header;
            Execution = execution;
            TaskLineId = taskLineId;
        }

        public WmsDbContext Db { get; }
        public UnitOfWork UnitOfWork { get; }
        public GoodsReceiptOnReceiptLabelService Service { get; }
        public GoodsReceiptHeader Header { get; }
        public GoodsReceiptExecution Execution { get; }
        public long? TaskLineId { get; }

        public static async Task<Fixture> CreateAsync(
            GoodsReceiptLabelStrategy strategy,
            bool withTask)
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
            db.Set<Warehouse>().Add(warehouse);
            await db.SaveChangesAsync();

            var header = new GoodsReceiptHeader
            {
                BranchCode = "0",
                DocumentNo = "GR-AUTO-001",
                DocumentDate = DateOnly.FromDateTime(DateTime.Today),
                TargetWarehouseId = warehouse.Id,
                ReceivingLocationId = 1,
                LabelStrategy = strategy
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
                ExpectedQuantity = 3.5m,
                TargetWarehouseId = warehouse.Id
            };
            header.Lines.Add(line);
            GoodsReceiptTask? task = null;
            GoodsReceiptTaskLine? taskLine = null;
            if (withTask)
            {
                task = new GoodsReceiptTask
                {
                    BranchCode = "0",
                    Header = header,
                    TaskNo = "GR-AUTO-001-RCV-01",
                    WarehouseId = warehouse.Id
                };
                taskLine = new GoodsReceiptTaskLine
                {
                    BranchCode = "0",
                    Task = task,
                    Line = line,
                    SequenceNo = 1,
                    PlannedQuantity = 3.5m,
                    UnitCode = "AD"
                };
                task.Lines.Add(taskLine);
                header.Tasks.Add(task);
            }
            db.GoodsReceiptHeaders.Add(header);
            await db.SaveChangesAsync();

            var execution = new GoodsReceiptExecution
            {
                BranchCode = "0",
                Header = header,
                GrTaskId = task?.Id,
                IdempotencyKey = Guid.NewGuid(),
                RequestHash = "HASH",
                ExecutionNo = "GR-AUTO-001-EX-01"
            };
            execution.Lines.Add(new GoodsReceiptExecutionLine
            {
                BranchCode = "0",
                Execution = execution,
                Line = line,
                LineNo = 1,
                StockId = line.StockId,
                Quantity = 3.5m,
                UnitCode = "AD",
                LotNo = "LOT-01",
                SerialNo = "SER-01",
                WarehouseId = warehouse.Id,
                LocationId = 1
            });
            db.Set<GoodsReceiptExecution>().Add(execution);
            await db.SaveChangesAsync();

            var unitOfWork = new UnitOfWork(db, new HttpContextAccessor());
            return new Fixture(
                db,
                unitOfWork,
                new GoodsReceiptOnReceiptLabelService(unitOfWork, new FakeBarcodePolicyService()),
                header,
                execution,
                taskLine?.Id);
        }

        public async ValueTask DisposeAsync()
        {
            await UnitOfWork.DisposeAsync();
            await Db.DisposeAsync();
        }
    }

    private sealed class FakeBarcodePolicyService : IBarcodePolicyService
    {
        public Task<BarcodePreviewResponse> GenerateAsync(
            BarcodePolicyScope scope,
            BarcodeGenerateRequest request,
            CancellationToken ct = default) =>
            Task.FromResult(new BarcodePreviewResponse(
                $"AUTO-{request.IdempotencyKey}", 1, true, 1, scope.ToString()));

        public Task<BarcodePolicyResponse> GetAsync(CancellationToken ct = default) =>
            throw new NotSupportedException();
        public Task<BarcodePolicyResponse> UpdateProfileAsync(
            BarcodePolicyScope scope,
            BarcodePolicyProfileUpdateRequest request,
            CancellationToken ct = default) =>
            throw new NotSupportedException();
        public Task<BarcodePreviewResponse> PreviewAsync(
            BarcodePolicyScope scope,
            BarcodeGenerateRequest request,
            CancellationToken ct = default) =>
            throw new NotSupportedException();
        public Task<PagedResponse<GeneratedBarcodeRow>> GetGeneratedPagedAsync(
            PagedRequest request,
            CancellationToken ct = default) =>
            throw new NotSupportedException();
    }
}
