using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using verii_wms_api_v2.Modules.Audit.Application;
using verii_wms_api_v2.Modules.Customer.Domain;
using verii_wms_api_v2.Modules.GoodsReceipt.Application;
using verii_wms_api_v2.Modules.Identity.Infrastructure;
using verii_wms_api_v2.Modules.Stock.Domain;
using verii_wms_api_v2.Shared.Application.Exceptions;
using verii_wms_api_v2.Shared.Infrastructure.Persistence;
using Xunit;

namespace verii_wms_api_v2.QueryTests;

public sealed class SupplierStockMappingServiceTests
{
    [Fact]
    public async Task Create_normalizes_external_code_and_resolves_active_mapping()
    {
        await using var fixture = await Fixture.CreateAsync("1");

        var created = await fixture.Service.CreateAsync(new(
            "1",
            fixture.Supplier.Id,
            "  ted-ürün-01  ",
            "Tedarikçi ürün adı",
            " koli ",
            fixture.Stock.Id,
            12.5m,
            true,
            null,
            null));

        Assert.Equal("1", created.BranchCode);
        Assert.Equal("ted-ürün-01", created.SupplierStockCode);
        Assert.Equal("KOLI", created.SupplierUnitCode);
        var resolved = await fixture.Service.ResolveAsync(
            "1", fixture.Supplier.Id, " TED-ÜRÜN-01 ");
        Assert.NotNull(resolved);
        Assert.Equal(fixture.Stock.Id, resolved.StockId);
        Assert.Equal(12.5m, resolved.ConversionFactor);
    }

    [Fact]
    public async Task Same_supplier_and_external_code_cannot_be_mapped_twice()
    {
        await using var fixture = await Fixture.CreateAsync("1");
        var request = new SaveSupplierStockMappingRequest(
            "1", fixture.Supplier.Id, "ABC-01", null, null,
            fixture.Stock.Id, 1m, true, null, null);
        await fixture.Service.CreateAsync(request);

        var error = await Assert.ThrowsAsync<AppException>(
            () => fixture.Service.CreateAsync(request with
            {
                SupplierStockCode = " abc-01 "
            }));

        Assert.Equal(409, error.StatusCode);
    }

    [Fact]
    public async Task Supplier_and_stock_must_belong_to_authenticated_branch()
    {
        await using var fixture = await Fixture.CreateAsync("1");

        var error = await Assert.ThrowsAsync<AppException>(
            () => fixture.Service.CreateAsync(new(
                "1",
                fixture.OtherBranchSupplier.Id,
                "ABC-01",
                null,
                null,
                fixture.Stock.Id,
                1m,
                true,
                null,
                null)));

        Assert.Equal(400, error.StatusCode);
        Assert.Contains("şubede", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Inactive_mapping_is_not_used_for_document_resolution()
    {
        await using var fixture = await Fixture.CreateAsync("1");
        await fixture.Service.CreateAsync(new(
            "1", fixture.Supplier.Id, "ABC-01", null, null,
            fixture.Stock.Id, 1m, false, null, null));

        var resolved = await fixture.Service.ResolveAsync(
            "1", fixture.Supplier.Id, "ABC-01");

        Assert.Null(resolved);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly WmsDbContext _db;
        private readonly UnitOfWork _unitOfWork;

        private Fixture(
            WmsDbContext db,
            UnitOfWork unitOfWork,
            Customer supplier,
            Customer otherBranchSupplier,
            Stock stock)
        {
            _db = db;
            _unitOfWork = unitOfWork;
            Supplier = supplier;
            OtherBranchSupplier = otherBranchSupplier;
            Stock = stock;
            Service = new SupplierStockMappingService(
                unitOfWork, new NoopAuditLogWriter());
        }

        public Customer Supplier { get; }
        public Customer OtherBranchSupplier { get; }
        public Stock Stock { get; }
        public SupplierStockMappingService Service { get; }

        public static async Task<Fixture> CreateAsync(string branchCode)
        {
            var options = new DbContextOptionsBuilder<WmsDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
                .ConfigureWarnings(warnings =>
                    warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options;
            var db = new WmsDbContext(options);
            var supplier = new Customer
            {
                BranchCode = branchCode,
                CustomerCode = "320.001",
                CustomerName = "Test Tedarikçi"
            };
            var otherBranchSupplier = new Customer
            {
                BranchCode = "2",
                CustomerCode = "320.002",
                CustomerName = "Diğer Şube Tedarikçisi"
            };
            var stock = new Stock
            {
                BranchCode = branchCode,
                ErpStockCode = "STK-001",
                StockName = "Sistem Stoğu",
                BaseUnitCode = "ADET"
            };
            db.AddRange(supplier, otherBranchSupplier, stock);
            await db.SaveChangesAsync();
            var unitOfWork = new UnitOfWork(
                db, CreateHttpContextAccessor(branchCode));
            return new Fixture(
                db, unitOfWork, supplier, otherBranchSupplier, stock);
        }

        public async ValueTask DisposeAsync()
        {
            await _unitOfWork.DisposeAsync();
            await _db.DisposeAsync();
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
