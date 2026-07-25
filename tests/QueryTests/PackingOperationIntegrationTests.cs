using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using verii_wms_api_v2.Modules.Audit.Application;
using verii_wms_api_v2.Modules.Customer.Domain;
using verii_wms_api_v2.Modules.Identity.Infrastructure;
using verii_wms_api_v2.Modules.Packing.Application;
using verii_wms_api_v2.Modules.Packing.Domain;
using verii_wms_api_v2.Modules.Stock.Domain;
using verii_wms_api_v2.Modules.Shipping.Domain;
using verii_wms_api_v2.Modules.WarehouseOperations.Domain;
using verii_wms_api_v2.Modules.WarehouseOutbound.Domain;
using verii_wms_api_v2.Modules.WarehouseTransfer.Domain;
using verii_wms_api_v2.Shared;
using verii_wms_api_v2.Shared.Application.Exceptions;
using verii_wms_api_v2.Shared.Infrastructure.Persistence;
using Xunit;

namespace verii_wms_api_v2.QueryTests;

public sealed class PackingOperationIntegrationTests
{
    [Fact]
    public async Task Full_packing_flow_is_idempotent_and_updates_outbound_tracking()
    {
        await using var fixture = await PackingFixture.CreateAsync();
        var service = fixture.Service;

        var materialId = await service.CreateMaterialAsync(
            new(fixture.Branch, "BOX-01", "Test kutusu", PackagingMaterialType.Box,
                0.25m, 25, 30, 40, 30, 20, 24000, false, true, null),
            fixture.Actor);
        var stationId = await service.CreateStationAsync(
            new(fixture.Branch, fixture.WarehouseId, null, "PACK-01", "Test istasyonu", null, null, true, null),
            fixture.Actor);

        var source = await fixture.CreateSerialOutboundAsync();
        var sessionKey = Guid.NewGuid();
        var session = await service.CreateSessionAsync(
            new(sessionKey, fixture.Branch, PackingSourceType.WarehouseOutbound, source.HeaderId,
                fixture.WarehouseId, stationId, "integration-test"),
            fixture.Actor);
        var replayedSession = await service.CreateSessionAsync(
            new(sessionKey, fixture.Branch, PackingSourceType.WarehouseOutbound, source.HeaderId,
                fixture.WarehouseId, stationId, "integration-test"),
            fixture.Actor);
        Assert.Equal(session.Header.Id, replayedSession.Header.Id);

        var unitKey = Guid.NewGuid();
        var unit = await service.CreateHandlingUnitAsync(
            session.Header.Id,
            new(unitKey, materialId, null, null, null, null, null, null),
            fixture.Actor);
        Assert.NotNull(unit.Sscc);
        Assert.Equal(18, unit.Sscc!.Length);
        Assert.True(unit.Sscc.All(char.IsDigit));

        var packKey = Guid.NewGuid();
        var packed = await service.PackAsync(
            unit.Id, new(packKey, source.LineId, 1, null, source.SerialNo), fixture.Actor);
        var replayedPack = await service.PackAsync(
            unit.Id, new(packKey, source.LineId, 1, null, source.SerialNo), fixture.Actor);
        Assert.Single(packed.Lines);
        Assert.Single(replayedPack.Lines);

        fixture.Context.ChangeTracker.Clear();
        var outboundLine = await fixture.Context.WarehouseOutboundLines
            .Include(x => x.Trackings).SingleAsync(x => x.Id == source.LineId);
        Assert.Equal(1, outboundLine.PackedQuantity);
        Assert.Equal(WarehouseOutboundLineStatus.Packed, outboundLine.Status);
        Assert.Equal(1, Assert.Single(outboundLine.Trackings).PackedQuantity);
        Assert.Equal(unit.HandlingUnitNo, Assert.Single(outboundLine.Trackings).HandlingUnitNo);

        var excess = await Assert.ThrowsAsync<AppException>(() =>
            service.PackAsync(unit.Id, new(Guid.NewGuid(), source.LineId, 1, null, source.SerialNo), fixture.Actor));
        Assert.Equal(409, excess.StatusCode);

        var closed = await service.CloseAsync(unit.Id, new(Guid.NewGuid(), null, "test close"), fixture.Actor);
        Assert.Equal(HandlingUnitStatus.Closed, closed.Status);
        var reopened = await service.ReopenAsync(unit.Id, Guid.NewGuid(), "test reopen", fixture.Actor);
        Assert.Equal(HandlingUnitStatus.Open, reopened.Status);
    }

    [Fact]
    public async Task Definitions_support_crud_search_sort_paging_and_validation()
    {
        await using var fixture = await PackingFixture.CreateAsync();
        var service = fixture.Service;

        var firstId = await service.CreateMaterialAsync(
            new(fixture.Branch, "BOX-B", "Beta", PackagingMaterialType.Box, 0, 10, 10, 10, 10, 10, 1000, false, true, null),
            fixture.Actor);
        await service.CreateMaterialAsync(
            new(fixture.Branch, "BOX-A", "Alpha", PackagingMaterialType.Box, 0, 10, 10, 10, 10, 10, 1000, false, true, null),
            fixture.Actor);

        var page = await service.GetMaterialsAsync(new PagedRequest
        {
            Search = "BOX",
            SortBy = nameof(PackagingMaterialRow.Code),
            SortDirection = "asc",
            PageNumber = 1,
            PageSize = 1,
            Filters = [new(nameof(PackagingMaterialRow.BranchCode), "equals", fixture.Branch)]
        });
        Assert.Equal(2, page.TotalCount);
        Assert.Single(page.Items);
        Assert.Equal("BOX-A", page.Items[0].Code);

        var duplicate = await Assert.ThrowsAsync<AppException>(() =>
            service.CreateMaterialAsync(
                new(fixture.Branch, "box-b", "Duplicate", PackagingMaterialType.Box, 0, 10, 10, 10, 10, 10, 1000, false, true, null),
                fixture.Actor));
        Assert.Equal(409, duplicate.StatusCode);

        var references = await fixture.CreateSpecificationReferencesAsync();
        var specificationId = await service.CreateSpecificationAsync(
            new(fixture.Branch, references.StockId, null, references.CustomerId, firstId, 12, 20, 8000, 100, true, "customer-stock rule"),
            fixture.Actor);
        var specifications = await service.GetSpecificationsAsync(new PagedRequest
        {
            Search = references.StockCode,
            PageSize = 10,
            Filters = [new(nameof(PackagingSpecificationRow.BranchCode), "equals", fixture.Branch)]
        });
        var specification = Assert.Single(specifications.Items);
        Assert.Equal(specificationId, specification.Id);
        Assert.Equal(12, specification.UnitsPerHandlingUnit);
        Assert.Equal(references.CustomerCode, specification.CustomerCode);

        var duplicateSpecification = await Assert.ThrowsAsync<AppException>(() =>
            service.CreateSpecificationAsync(
                new(fixture.Branch, references.StockId, null, references.CustomerId, firstId, 10, null, null, 100, true, null),
                fixture.Actor));
        Assert.Equal(409, duplicateSpecification.StatusCode);

        await service.UpdateSpecificationAsync(specificationId,
            new(fixture.Branch, references.StockId, null, references.CustomerId, firstId, 24, 22, 9000, 110, false, "updated"),
            fixture.Actor);
        var updatedSpecification = await service.GetSpecificationsAsync(new PagedRequest
        {
            PageSize = 10,
            Filters = [new(nameof(PackagingSpecificationRow.Id), "equals", specificationId.ToString())]
        });
        Assert.Equal(24, Assert.Single(updatedSpecification.Items).UnitsPerHandlingUnit);
        await service.DeleteSpecificationAsync(specificationId, fixture.Actor);

        await service.UpdateMaterialAsync(firstId,
            new(fixture.Branch, "BOX-B", "Beta updated", PackagingMaterialType.Crate, 1, 20, 25, 20, 20, 20, 8000, true, false, "updated"),
            fixture.Actor);
        var updated = await service.GetMaterialsAsync(new PagedRequest
        {
            Search = "Beta updated",
            PageSize = 10,
            Filters = [new(nameof(PackagingMaterialRow.BranchCode), "equals", fixture.Branch)]
        });
        Assert.Equal(PackagingMaterialType.Crate, Assert.Single(updated.Items).Type);

        await service.DeleteMaterialAsync(firstId, fixture.Actor);
        var deleted = await service.GetMaterialsAsync(new PagedRequest
        {
            Search = "Beta updated",
            PageSize = 10,
            Filters = [new(nameof(PackagingMaterialRow.BranchCode), "equals", fixture.Branch)]
        });
        Assert.Empty(deleted.Items);

        var invalid = await Assert.ThrowsAsync<AppException>(() =>
            service.CreateMaterialAsync(
                new(fixture.Branch, "BAD CODE", "Invalid", PackagingMaterialType.Box, 0, 10, 10, 10, 10, 10, 1000, false, true, null),
                fixture.Actor));
        Assert.Equal(400, invalid.StatusCode);
    }

    [Fact]
    public async Task Unpack_and_repack_keep_source_quantity_consistent()
    {
        await using var fixture=await PackingFixture.CreateAsync();var service=fixture.Service;
        var material=await service.CreateMaterialAsync(new(fixture.Branch,"BOX-MOVE","Move box",PackagingMaterialType.Box,0,100,100,10,10,10,1000,false,true,null),fixture.Actor);
        var station=await service.CreateStationAsync(new(fixture.Branch,fixture.WarehouseId,null,"PACK-MOVE","Move station",null,null,true,null),fixture.Actor);
        var source=await fixture.CreateOutboundAsync(4);
        var session=await service.CreateSessionAsync(new(Guid.NewGuid(),fixture.Branch,PackingSourceType.WarehouseOutbound,source.HeaderId,fixture.WarehouseId,station,null),fixture.Actor);
        var first=await service.CreateHandlingUnitAsync(session.Header.Id,new(Guid.NewGuid(),material,null,null,null,null,null,null),fixture.Actor);
        var second=await service.CreateHandlingUnitAsync(session.Header.Id,new(Guid.NewGuid(),material,null,null,null,null,null,null),fixture.Actor);
        first=await service.PackAsync(first.Id,new(Guid.NewGuid(),source.LineId,4,null,null),fixture.Actor);
        second=await service.MoveAsync(first.Id,new(Guid.NewGuid(),Assert.Single(first.Lines).Id,second.Id,2,"split"),fixture.Actor);
        var detail=await service.GetSessionAsync(session.Header.Id);
        Assert.Equal(2,detail.HandlingUnits.Single(x=>x.Id==first.Id).Lines.Single().Quantity);
        Assert.Equal(2,second.Lines.Single().Quantity);
        await service.UnpackAsync(second.Id,new(Guid.NewGuid(),second.Lines.Single().Id,1,"correction"),fixture.Actor);
        fixture.Context.ChangeTracker.Clear();
        Assert.Equal(3,(await fixture.Context.WarehouseOutboundLines.SingleAsync(x=>x.Id==source.LineId)).PackedQuantity);
        Assert.Equal(1,Assert.Single(await service.GetSourceLinesAsync(session.Header.Id)).RemainingQuantity);
    }

    [Fact]
    public async Task Shipment_and_transfer_adapters_update_their_own_packed_quantities()
    {
        await using var fixture=await PackingFixture.CreateAsync();var service=fixture.Service;
        var material=await service.CreateMaterialAsync(new(fixture.Branch,"BOX-SOURCES","Source box",PackagingMaterialType.Box,0,100,100,10,10,10,1000,false,true,null),fixture.Actor);
        var station=await service.CreateStationAsync(new(fixture.Branch,fixture.WarehouseId,null,"PACK-SOURCES","Source station",null,null,true,null),fixture.Actor);
        var shipment=await fixture.CreateShipmentAsync(2);
        var shipmentSession=await service.CreateSessionAsync(new(Guid.NewGuid(),fixture.Branch,PackingSourceType.Shipment,shipment.HeaderId,fixture.WarehouseId,station,null),fixture.Actor);
        var shipmentUnit=await service.CreateHandlingUnitAsync(shipmentSession.Header.Id,new(Guid.NewGuid(),material,null,null,null,null,null,null),fixture.Actor);
        await service.PackAsync(shipmentUnit.Id,new(Guid.NewGuid(),shipment.LineId,2,null,null),fixture.Actor);
        var transfer=await fixture.CreateTransferAsync(3);
        var transferSession=await service.CreateSessionAsync(new(Guid.NewGuid(),fixture.Branch,PackingSourceType.WarehouseTransfer,transfer.HeaderId,fixture.WarehouseId,station,null),fixture.Actor);
        var transferUnit=await service.CreateHandlingUnitAsync(transferSession.Header.Id,new(Guid.NewGuid(),material,null,null,null,null,null,null),fixture.Actor);
        await service.PackAsync(transferUnit.Id,new(Guid.NewGuid(),transfer.LineId,3,null,null),fixture.Actor);
        fixture.Context.ChangeTracker.Clear();
        Assert.Equal(2,(await fixture.Context.ShipmentLines.SingleAsync(x=>x.Id==shipment.LineId)).PackedQuantity);
        Assert.Equal(3,(await fixture.Context.WarehouseTransferLines.SingleAsync(x=>x.Id==transfer.LineId)).PackedQuantity);
    }

    [Fact]
    public async Task Auto_close_scale_and_outbox_print_are_idempotent()
    {
        await using var fixture=await PackingFixture.CreateAsync();var service=fixture.Service;
        var policy=await service.GetPolicyAsync(fixture.Branch);
        await service.UpdatePolicyAsync(new(fixture.Branch,true,true,false,false,false,true,false,5,false,true,true,true,true,true,PackingClosePolicy.AutoWhenComplete,PackingReleasePolicy.Manual,policy.RowVersion),fixture.Actor);
        var material=await service.CreateMaterialAsync(new(fixture.Branch,"BOX-AUTO","Auto box",PackagingMaterialType.Box,0,100,100,10,10,10,1000,false,true,null),fixture.Actor);
        var station=await service.CreateStationAsync(new(fixture.Branch,fixture.WarehouseId,null,"PACK-AUTO","Auto station","SCALE-01",77,true,null),fixture.Actor);
        var source=await fixture.CreateOutboundAsync(1);
        var session=await service.CreateSessionAsync(new(Guid.NewGuid(),fixture.Branch,PackingSourceType.WarehouseOutbound,source.HeaderId,fixture.WarehouseId,station,null),fixture.Actor);
        var unit=await service.CreateHandlingUnitAsync(session.Header.Id,new(Guid.NewGuid(),material,null,null,null,null,null,null),fixture.Actor);
        var packed=await service.PackAsync(unit.Id,new(Guid.NewGuid(),source.LineId,1,null,null),fixture.Actor);
        Assert.Equal(HandlingUnitStatus.Closed,packed.Status);
        var scaleKey=Guid.NewGuid();var scale=await service.ReadScaleAsync(unit.Id,new(scaleKey),fixture.Actor);var replay=await service.ReadScaleAsync(unit.Id,new(scaleKey),fixture.Actor);
        Assert.Equal(scale.Id,replay.Id);Assert.Equal(12.5m,scale.GrossWeight);
        Assert.Single((await service.GetPrintJobsAsync(new PagedRequest{PageSize=10,Filters=[new(nameof(PackingPrintJobRow.HandlingUnitId),"equals",unit.Id.ToString())]})).Items);
        await fixture.DispatchPrintsAsync();fixture.Context.ChangeTracker.Clear();
        Assert.Equal(PackingPrintJobStatus.Completed,(await fixture.Context.PackingPrintJobs.SingleAsync(x=>x.HandlingUnitId==unit.Id)).Status);
        Assert.Single(fixture.Gateway.Printed);
    }

    private sealed class PackingFixture : IAsyncDisposable
    {
        private readonly UnitOfWork _uow;
        public WmsDbContext Context { get; }
        public PackingService Service { get; }
        public FakePackingDeviceGateway Gateway { get; }
        public string Branch { get; } = $"T{Guid.NewGuid():N}"[..10].ToUpperInvariant();
        public long WarehouseId { get; } = 9_000_000 + Random.Shared.Next(1, 900_000);
        public long Actor { get; } = 1;

        private PackingFixture(WmsDbContext context, UnitOfWork uow)
        {
            Context = context;
            _uow = uow;
            var resolver = new PackingSourceAdapterResolver([
                new WarehouseOutboundPackingSourceAdapter(uow),
                new ShipmentPackingSourceAdapter(uow),
                new WarehouseTransferPackingSourceAdapter(uow)
            ]);
            Gateway=new FakePackingDeviceGateway();
            Service = new PackingService(uow, new NullAuditWriter(), resolver, new PackingDeviceService(uow,Gateway));
        }

        public static async Task<PackingFixture> CreateAsync()
        {
            var configuration = new ConfigurationBuilder()
                .AddUserSecrets<PackingOperationIntegrationTests>()
                .AddEnvironmentVariables()
                .Build();
            var connection = configuration.GetConnectionString("DefaultConnection");
            Assert.False(string.IsNullOrWhiteSpace(connection),
                "Integration test için secret store içindeki ConnectionStrings:DefaultConnection gereklidir.");
            var options = new DbContextOptionsBuilder<WmsDbContext>()
                .UseSqlServer(connection)
                .EnableSensitiveDataLogging(false)
                .Options;
            var context = new WmsDbContext(options);
            var http = new HttpContextAccessor
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(ClaimTypes.NameIdentifier, "1")], "test"))
                }
            };
            var uow = new UnitOfWork(context, http);
            await uow.BeginTransactionAsync();
            return new PackingFixture(context, uow);
        }

        public async Task<(long HeaderId, long LineId, string SerialNo)> CreateSerialOutboundAsync()
        {
            var serial = $"SER-{Guid.NewGuid():N}";
            var header = new WarehouseOutboundHeader
            {
                BranchCode = Branch,
                DocumentSeriesId = 1,
                DocumentNo = $"WO-{Guid.NewGuid():N}",
                DocumentDate = DateOnly.FromDateTime(DateTime.UtcNow),
                InitiationMode = WarehouseOutboundInitiationMode.StockBasedDirect,
                SourceSystem = WarehouseOperationSourceSystem.Manual,
                CorrelationId = Guid.NewGuid(),
                CustomerId = 8_000_001,
                CustomerCodeSnapshot = "TEST-CUSTOMER",
                SourceWarehouseId = WarehouseId,
                Status = WarehouseOutboundStatus.Picked,
                PackingPolicy = WarehouseOutboundPackingPolicy.Required,
                CreatedBy = Actor
            };
            var line = new WarehouseOutboundLine
            {
                BranchCode = Branch,
                LineNo = 1,
                StockId = 8_000_001,
                StockCodeSnapshot = "TEST-STOCK",
                UnitCode = "ADET",
                RequestedQuantity = 1,
                ReservedQuantity = 1,
                PickedQuantity = 1,
                TrackingType = StockTrackingType.Serial,
                Status = WarehouseOutboundLineStatus.Picked,
                CreatedBy = Actor
            };
            line.Trackings.Add(new WarehouseOutboundTracking
            {
                BranchCode = Branch,
                SerialNo = serial,
                PlannedQuantity = 1,
                ReservedQuantity = 1,
                PickedQuantity = 1,
                CreatedBy = Actor
            });
            header.Lines.Add(line);
            Context.WarehouseOutboundHeaders.Add(header);
            await Context.SaveChangesAsync();
            return (header.Id, line.Id, serial);
        }

        public async Task<(long HeaderId,long LineId)> CreateOutboundAsync(decimal quantity)
        {
            var h=new WarehouseOutboundHeader{BranchCode=Branch,DocumentSeriesId=1,DocumentNo=$"WO-{Guid.NewGuid():N}"[..20],DocumentDate=DateOnly.FromDateTime(DateTime.UtcNow),CorrelationId=Guid.NewGuid(),CustomerId=1,CustomerCodeSnapshot="TEST",SourceWarehouseId=WarehouseId,Status=WarehouseOutboundStatus.Picked,CreatedBy=Actor};
            var l=new WarehouseOutboundLine{BranchCode=Branch,LineNo=1,StockId=8_000_002,StockCodeSnapshot="TEST-STOCK",UnitCode="ADET",RequestedQuantity=quantity,ReservedQuantity=quantity,PickedQuantity=quantity,TrackingType=StockTrackingType.None,Status=WarehouseOutboundLineStatus.Picked,CreatedBy=Actor};h.Lines.Add(l);Context.WarehouseOutboundHeaders.Add(h);await Context.SaveChangesAsync();return(h.Id,l.Id);
        }
        public async Task<(long HeaderId,long LineId)> CreateShipmentAsync(decimal quantity)
        {
            var h=new ShipmentHeader{BranchCode=Branch,DocumentSeriesId=1,DocumentNo=$"SH-{Guid.NewGuid():N}"[..20],DocumentDate=DateOnly.FromDateTime(DateTime.UtcNow),CorrelationId=Guid.NewGuid(),CustomerId=1,CustomerCodeSnapshot="TEST",SourceWarehouseId=WarehouseId,Status=ShipmentStatus.Picked,CreatedBy=Actor};
            var l=new ShipmentLine{BranchCode=Branch,LineNo=1,StockId=8_000_003,StockCodeSnapshot="TEST-STOCK",UnitCode="ADET",RequestedQuantity=quantity,ReservedQuantity=quantity,PickedQuantity=quantity,TrackingType=StockTrackingType.None,Status=ShipmentLineStatus.Picked,CreatedBy=Actor};h.Lines.Add(l);Context.ShipmentHeaders.Add(h);await Context.SaveChangesAsync();return(h.Id,l.Id);
        }
        public async Task<(long HeaderId,long LineId)> CreateTransferAsync(decimal quantity)
        {
            var h=new WarehouseTransferHeader{BranchCode=Branch,DocumentSeriesId=1,DocumentNo=$"WT-{Guid.NewGuid():N}"[..20],DocumentDate=DateOnly.FromDateTime(DateTime.UtcNow),CorrelationId=Guid.NewGuid(),SourceWarehouseId=WarehouseId,TargetWarehouseId=WarehouseId+1,Status=WarehouseTransferStatus.Picked,CreatedBy=Actor};
            var l=new WarehouseTransferLine{BranchCode=Branch,LineNo=1,StockId=8_000_004,StockCodeSnapshot="TEST-STOCK",UnitCode="ADET",BaseUnitCode="ADET",RequestedQuantity=quantity,ReservedQuantity=quantity,PickedQuantity=quantity,SourceWarehouseId=WarehouseId,TargetWarehouseId=WarehouseId+1,TrackingType=StockTrackingType.None,Status=WarehouseTransferLineStatus.Picked,CreatedBy=Actor};h.Lines.Add(l);Context.WarehouseTransferHeaders.Add(h);await Context.SaveChangesAsync();return(h.Id,l.Id);
        }
        public Task DispatchPrintsAsync()=>new PackingPrintQueueJobRunner(_uow,Gateway,NullLogger<PackingPrintQueueJobRunner>.Instance).DispatchPendingAsync(CancellationToken.None);

        public async Task<(long StockId,string StockCode,long CustomerId,string CustomerCode)> CreateSpecificationReferencesAsync()
        {
            var suffix=Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
            var stock=new Stock
            {
                BranchCode=Branch,
                BusinessUnitCode=1,
                ErpStockCode=$"ST-{suffix}",
                StockName="Packing test stock",
                GroupCode="PACK-GROUP",
                CreatedBy=Actor
            };
            var customer=new Customer
            {
                BranchCode=Branch,
                BusinessUnitCode=1,
                CustomerCode=$"CU-{suffix}",
                CustomerName="Packing test customer",
                CreatedBy=Actor
            };
            Context.Stocks.Add(stock);Context.Customers.Add(customer);await Context.SaveChangesAsync();
            return(stock.Id,stock.ErpStockCode,customer.Id,customer.CustomerCode);
        }

        public async ValueTask DisposeAsync()
        {
            await _uow.RollbackTransactionAsync();
            await _uow.DisposeAsync();
            await Context.DisposeAsync();
        }
    }

    private sealed class FakePackingDeviceGateway : IPackingDeviceGateway
    {
        public List<PackingPrintPayload> Printed { get; }=[];
        public Task PrintAsync(PackingPrintPayload payload,CancellationToken ct){Printed.Add(payload);return Task.CompletedTask;}
        public Task<PackingScaleGatewayResult> ReadScaleAsync(string deviceCode,CancellationToken ct)=>Task.FromResult(new PackingScaleGatewayResult(12.5m,true,"test"));
    }

    private sealed class NullAuditWriter : IAuditLogWriter
    {
        public Task WriteAsync(AuditLogWriteEntry entry, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
