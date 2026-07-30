using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using verii_wms_api_v2.Modules.Audit.Application;
using verii_wms_api_v2.Modules.GoodsReceipt.Application;
using verii_wms_api_v2.Modules.Identity.Infrastructure;
using verii_wms_api_v2.Modules.Location.Domain;
using verii_wms_api_v2.Shared.Application.Exceptions;
using verii_wms_api_v2.Shared.Infrastructure.Persistence;
using WarehouseEntity = verii_wms_api_v2.Modules.Warehouse.Domain.Warehouse;
using Xunit;

namespace verii_wms_api_v2.QueryTests;

public sealed class GoodsReceiptWarehouseDefaultTests
{
    [Fact]
    public async Task UpdateWarehouseDefault_selects_active_Yer1_in_same_warehouse()
    {
        await using var fixture = await Fixture.CreateAsync();

        var result = await fixture.Service.UpdateWarehouseDefaultAsync(
            new("1", fixture.Warehouse.Id, fixture.Yer1.Id), 42);

        Assert.Equal(fixture.Yer1.Id, result.DefaultLocationId);
        Assert.Equal("Yer1", result.DefaultLocationCode);
        var warehouse = await fixture.Db.Warehouses.SingleAsync(x => x.Id == fixture.Warehouse.Id);
        Assert.Equal(fixture.Yer1.Id, warehouse.DefaultGoodsReceiptLocationId);
    }

    [Fact]
    public async Task UpdateWarehouseDefault_rejects_location_from_another_warehouse()
    {
        await using var fixture = await Fixture.CreateAsync();

        var exception = await Assert.ThrowsAsync<AppException>(() =>
            fixture.Service.UpdateWarehouseDefaultAsync(
                new("1", fixture.Warehouse.Id, fixture.OtherWarehouseLocation.Id), 42));

        Assert.Equal(StatusCodes.Status400BadRequest, exception.StatusCode);
        Assert.Contains("seçilen depoya ait", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly UnitOfWork _unitOfWork;

        private Fixture(
            WmsDbContext db,
            UnitOfWork unitOfWork,
            WarehouseEntity warehouse,
            WarehouseLocation yer1,
            WarehouseLocation otherWarehouseLocation)
        {
            Db = db;
            _unitOfWork = unitOfWork;
            Warehouse = warehouse;
            Yer1 = yer1;
            OtherWarehouseLocation = otherWarehouseLocation;
            Service = new GoodsReceiptPolicyService(unitOfWork, new NoopAuditLogWriter());
        }

        public WmsDbContext Db { get; }
        public WarehouseEntity Warehouse { get; }
        public WarehouseLocation Yer1 { get; }
        public WarehouseLocation OtherWarehouseLocation { get; }
        public GoodsReceiptPolicyService Service { get; }

        public static async Task<Fixture> CreateAsync()
        {
            var options = new DbContextOptionsBuilder<WmsDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
                .ConfigureWarnings(warnings =>
                    warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options;
            var db = new WmsDbContext(options);
            var warehouse = new WarehouseEntity
            {
                BranchCode = "1",
                WarehouseCode = 100,
                WarehouseName = "Test Deposu"
            };
            var otherWarehouse = new WarehouseEntity
            {
                BranchCode = "1",
                WarehouseCode = 200,
                WarehouseName = "Diğer Depo"
            };
            db.AddRange(warehouse, otherWarehouse);
            await db.SaveChangesAsync();
            var yer1 = new WarehouseLocation
            {
                BranchCode = "1",
                WarehouseId = warehouse.Id,
                Code = "Yer1",
                Name = "Yer1",
                LocationType = LocationTypes.Shelf,
                IsActive = true
            };
            var otherWarehouseLocation = new WarehouseLocation
            {
                BranchCode = "1",
                WarehouseId = otherWarehouse.Id,
                Code = "Yer1",
                Name = "Diğer Depo Yer1",
                LocationType = LocationTypes.Shelf,
                IsActive = true
            };
            db.AddRange(yer1, otherWarehouseLocation);
            await db.SaveChangesAsync();
            var unitOfWork = new UnitOfWork(db, CreateHttpContextAccessor("1"));
            return new Fixture(db, unitOfWork, warehouse, yer1, otherWarehouseLocation);
        }

        public async ValueTask DisposeAsync()
        {
            await _unitOfWork.DisposeAsync();
            await Db.DisposeAsync();
        }
    }

    private static HttpContextAccessor CreateHttpContextAccessor(string branchCode)
    {
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, "42"),
                    new Claim(JwtTokenIssuer.BranchCodeClaim, branchCode)
                ],
                "Test"))
        };
        return new HttpContextAccessor { HttpContext = context };
    }

    private sealed class NoopAuditLogWriter : IAuditLogWriter
    {
        public Task WriteAsync(
            AuditLogWriteEntry entry,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
