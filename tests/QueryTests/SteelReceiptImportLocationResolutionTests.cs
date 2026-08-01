using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using verii_wms_api_v2.Modules.Audit.Application;
using verii_wms_api_v2.Modules.Customer.Domain;
using verii_wms_api_v2.Modules.DocumentSeries.Domain;
using verii_wms_api_v2.Modules.ErpIntegration.Application;
using verii_wms_api_v2.Modules.GoodsReceipt.Application;
using verii_wms_api_v2.Modules.GoodsReceipt.Domain;
using verii_wms_api_v2.Modules.Identity.Infrastructure;
using verii_wms_api_v2.Modules.Location.Domain;
using verii_wms_api_v2.Modules.SteelReceipt.Application;
using verii_wms_api_v2.Modules.SteelReceipt.Domain;
using verii_wms_api_v2.Modules.Stock.Domain;
using verii_wms_api_v2.Modules.StockMovement.Application;
using verii_wms_api_v2.Modules.Warehouse.Domain;
using verii_wms_api_v2.Modules.StockMovement.Domain;
using verii_wms_api_v2.Shared;
using verii_wms_api_v2.Shared.Infrastructure.Persistence;
using Xunit;

namespace verii_wms_api_v2.QueryTests;

public sealed class SteelReceiptImportLocationResolutionTests
{
    [Fact]
    public async Task Import_uses_warehouse_default_location_when_request_location_is_null()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Warehouse.DefaultGoodsReceiptLocationId = fixture.DefaultLocation.Id;
        await fixture.Context.SaveChangesAsync();

        var planId = await fixture.Service.CommitAsync(
            fixture.BuildImportRequest(receivingLocationId: null, importReferenceNo: "DEFAULT-LOC"),
            actor: 1);

        var line = await fixture.Context.Set<SteelReceiptPlanLine>()
            .SingleAsync(x => x.PlanId == planId);
        var plan = await fixture.Context.Set<SteelReceiptPlan>().SingleAsync(x => x.Id == planId);

        Assert.Equal(fixture.DefaultLocation.Id, line.ReceivingLocationId);
        Assert.Equal(fixture.DefaultLocation.Id, plan.ReceivingLocationId);
    }

    [Fact]
    public async Task Import_falls_back_to_first_eligible_location_when_warehouse_default_is_missing()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Warehouse.DefaultGoodsReceiptLocationId = null;
        await fixture.Context.SaveChangesAsync();

        var planId = await fixture.Service.CommitAsync(
            fixture.BuildImportRequest(receivingLocationId: null, importReferenceNo: "FALLBACK-LOC"),
            actor: 1);

        var line = await fixture.Context.Set<SteelReceiptPlanLine>()
            .SingleAsync(x => x.PlanId == planId);

        Assert.Equal(fixture.FirstEligibleLocation.Id, line.ReceivingLocationId);
    }

    [Fact]
    public async Task Import_honors_explicit_request_location_over_warehouse_default()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Warehouse.DefaultGoodsReceiptLocationId = fixture.DefaultLocation.Id;
        await fixture.Context.SaveChangesAsync();

        var planId = await fixture.Service.CommitAsync(
            fixture.BuildImportRequest(
                receivingLocationId: fixture.FirstEligibleLocation.Id,
                importReferenceNo: "EXPLICIT-LOC"),
            actor: 1);

        var line = await fixture.Context.Set<SteelReceiptPlanLine>()
            .SingleAsync(x => x.PlanId == planId);

        Assert.Equal(fixture.FirstEligibleLocation.Id, line.ReceivingLocationId);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private Fixture(
            WmsDbContext context,
            UnitOfWork unitOfWork,
            SteelReceiptService service,
            Warehouse warehouse,
            WarehouseLocation firstEligibleLocation,
            WarehouseLocation defaultLocation,
            Customer supplier,
            Stock stock,
            DocumentSeries series)
        {
            Context = context;
            UnitOfWork = unitOfWork;
            Service = service;
            Warehouse = warehouse;
            FirstEligibleLocation = firstEligibleLocation;
            DefaultLocation = defaultLocation;
            Supplier = supplier;
            Stock = stock;
            Series = series;
        }

        public WmsDbContext Context { get; }
        public UnitOfWork UnitOfWork { get; }
        public SteelReceiptService Service { get; }
        public Warehouse Warehouse { get; }
        public WarehouseLocation FirstEligibleLocation { get; }
        public WarehouseLocation DefaultLocation { get; }
        public Customer Supplier { get; }
        public Stock Stock { get; }
        public DocumentSeries Series { get; }

        public CommitSteelReceiptImportRequest BuildImportRequest(
            long? receivingLocationId,
            string importReferenceNo) =>
            new(
                Guid.NewGuid(),
                new PreviewSteelReceiptImportRequest(
                    "LOC",
                    importReferenceNo,
                    "locations.xlsx",
                    null,
                    null,
                    Supplier.Id,
                    Warehouse.Id,
                    receivingLocationId,
                    Series.Id,
                    "GIB2026CD000001",
                    DateOnly.FromDateTime(DateTime.UtcNow),
                    null,
                    [
                        new SteelImportLineRequest(
                            1, null, null, Stock.Id, Stock.ErpStockCode,
                            null, null, $"SER-{importReferenceNo}", null, 1, "ADET",
                            null, null, null, null, null, null)
                    ]));

        public static async Task<Fixture> CreateAsync()
        {
            var options = new DbContextOptionsBuilder<WmsDbContext>()
                .UseInMemoryDatabase($"steel-import-location-{Guid.NewGuid():N}")
                .ConfigureWarnings(warnings =>
                    warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options;
            var context = new WmsDbContext(options);
            const string branch = "LOC";
            var supplier = new Customer
            {
                BranchCode = branch,
                BusinessUnitCode = 1,
                CustomerCode = "LOC-SUP",
                CustomerName = "Location supplier"
            };
            var warehouse = new Warehouse
            {
                BranchCode = branch,
                WarehouseCode = 65_001,
                WarehouseName = "Location warehouse"
            };
            var stock = new Stock
            {
                BranchCode = branch,
                BusinessUnitCode = 1,
                ErpStockCode = "LOC-STOCK",
                StockName = "Steel plate",
                BaseUnitCode = "ADET"
            };
            var series = new DocumentSeries
            {
                BranchCode = branch,
                Code = "LOC-GR",
                Name = "Location receipt",
                DocumentType = WmsDocumentType.GoodsReceipt,
                Prefix = "LC",
                NumberLength = 8,
                StartNumber = 1,
                NextNumber = 1,
                IsActive = true
            };
            context.AddRange(supplier, warehouse, stock, series);
            await context.SaveChangesAsync();

            var firstEligibleLocation = new WarehouseLocation
            {
                BranchCode = branch,
                WarehouseId = warehouse.Id,
                Code = "LOC-REC",
                Name = "Receiving",
                LocationType = LocationTypes.Receiving,
                IsActive = true,
                IsPutaway = true
            };
            var defaultLocation = new WarehouseLocation
            {
                BranchCode = branch,
                WarehouseId = warehouse.Id,
                Code = "LOC-DEFAULT",
                Name = "Configured default",
                LocationType = LocationTypes.Shelf,
                IsActive = true,
                IsPutaway = true
            };
            context.AddRange(firstEligibleLocation, defaultLocation);
            await context.SaveChangesAsync();

            var http = new HttpContextAccessor();
            var unitOfWork = new UnitOfWork(context, http);
            var service = new SteelReceiptService(
                unitOfWork,
                new UnsupportedGoodsReceiptOperations(),
                new NullErpPostingCoordinator(),
                new UnsupportedStockMovementService(),
                new NullAuditWriter(),
                new NullSteelAttachmentStorage());

            return new Fixture(
                context,
                unitOfWork,
                service,
                warehouse,
                firstEligibleLocation,
                defaultLocation,
                supplier,
                stock,
                series);
        }

        public async ValueTask DisposeAsync()
        {
            await UnitOfWork.DisposeAsync();
            await Context.DisposeAsync();
        }
    }

    private sealed class NullAuditWriter : IAuditLogWriter
    {
        public Task WriteAsync(
            AuditLogWriteEntry entry,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class NullErpPostingCoordinator : IGoodsReceiptErpPostingCoordinator
    {
        public Task<ErpPostingResult?> PostIfEligibleAsync(
            long goodsReceiptId,
            long actorUserId,
            CancellationToken cancellationToken) =>
            Task.FromResult<ErpPostingResult?>(null);
    }

    private sealed class NullSteelAttachmentStorage : ISteelReceiptAttachmentStorage
    {
        public Task<string> SaveAsync(
            long lineId,
            SteelReceiptAttachmentUpload upload,
            CancellationToken ct = default) =>
            Task.FromResult($"memory/steel/{lineId}/{upload.FileName}");

        public Task<Stream> OpenReadAsync(string storagePath, CancellationToken ct = default) =>
            Task.FromResult<Stream>(new MemoryStream([1]));

        public void Delete(string storagePath) { }
    }

    private sealed class UnsupportedGoodsReceiptOperations : IGoodsReceiptOperationsService
    {
        public Task<GoodsReceiptQualityRequirementResult> ResolveQualityRequirementsAsync(
            ResolveGoodsReceiptQualityRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ManualGoodsReceiptResult> CreateOrderlessTaskAsync(
            CreateManualGoodsReceiptRequest request,
            long actorUserId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ManualGoodsReceiptResult> CreateDirectReceiptAsync(
            CreateManualGoodsReceiptRequest request,
            long actorUserId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ManualGoodsReceiptResult> CreateDirectReceiptDeferredErpAsync(
            CreateManualGoodsReceiptRequest request,
            long actorUserId,
            bool qualityAlreadyApproved,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<PagedResponse<GoodsReceiptGridRow>> GetPagedAsync(
            PagedRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<GoodsReceiptDetail> GetDetailAsync(
            long id,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class UnsupportedStockMovementService : IStockMovementService
    {
        public Task<PagedResponse<StockMovementGridRow>> GetPagedAsync(
            PagedRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<StockMovementDetail> GetByIdAsync(
            long id,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<StockMovementPostResult> PostAsync(
            PostStockMovementRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<StockMovementPostResult> ReverseAsync(
            long operationId,
            ReverseStockMovementRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
