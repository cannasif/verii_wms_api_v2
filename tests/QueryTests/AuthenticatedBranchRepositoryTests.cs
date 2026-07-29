using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using verii_wms_api_v2.Modules.Identity.Infrastructure;
using verii_wms_api_v2.Modules.Warehouse.Domain;
using verii_wms_api_v2.Shared.Infrastructure.Persistence;
using Xunit;

namespace verii_wms_api_v2.QueryTests;

public sealed class AuthenticatedBranchRepositoryTests
{
    [Fact]
    public async Task Business_queries_and_inserts_are_scoped_to_the_authenticated_branch()
    {
        await using var db = CreateDb();
        var branchZero = new Warehouse { BranchCode = "0", WarehouseCode = 1, WarehouseName = "Merkez" };
        var branchOne = new Warehouse { BranchCode = "1", WarehouseCode = 2, WarehouseName = "Şube 1" };
        db.AddRange(branchZero, branchOne);
        await db.SaveChangesAsync();

        var unitOfWork = new UnitOfWork(db, CreateHttpContextAccessor("1"));
        var repository = unitOfWork.Repository<Warehouse>();

        var visible = await repository.Query().ToListAsync();
        Assert.Collection(visible, warehouse => Assert.Equal("1", warehouse.BranchCode));
        Assert.Null(await repository.FindByIdAsync(branchZero.Id));

        var created = new Warehouse
        {
            BranchCode = "999",
            WarehouseCode = 3,
            WarehouseName = "Yeni depo"
        };
        await repository.AddAsync(created);
        await unitOfWork.SaveChangesAsync();

        Assert.Equal("1", created.BranchCode);
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

    private static WmsDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<WmsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new WmsDbContext(options);
    }
}
