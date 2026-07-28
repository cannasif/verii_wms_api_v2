using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using verii_wms_api_v2.Modules.Identity.Application;
using verii_wms_api_v2.Modules.Identity.Domain;
using verii_wms_api_v2.Modules.Identity.Infrastructure;
using verii_wms_api_v2.Modules.Warehouse.Domain;
using verii_wms_api_v2.Shared.Infrastructure.Persistence;
using Xunit;

namespace verii_wms_api_v2.QueryTests;

public sealed class UserWarehouseAccessServiceTests
{
    [Theory]
    [InlineData("Admin")]
    [InlineData("superadmin")]
    public async Task Administrator_is_never_restricted(string role)
    {
        await using var fixture = CreateFixture();
        var user = User(role);
        fixture.Db.Users.Add(user);
        await fixture.Db.SaveChangesAsync();

        var access = await UserWarehouseAccessService.ResolveAsync(
            fixture.UnitOfWork, user.Id, "0", default);

        Assert.False(access.IsRestricted);
        Assert.Empty(access.WarehouseIds);
    }

    [Fact]
    public async Task User_without_assignments_can_use_all_warehouses()
    {
        await using var fixture = CreateFixture();
        var user = User("User");
        fixture.Db.Users.Add(user);
        await fixture.Db.SaveChangesAsync();

        var access = await UserWarehouseAccessService.ResolveAsync(
            fixture.UnitOfWork, user.Id, "0", default);

        Assert.False(access.IsRestricted);
    }

    [Fact]
    public async Task Assigned_user_is_restricted_to_warehouses_in_current_branch()
    {
        await using var fixture = CreateFixture();
        var user = User("User");
        var allowed = Warehouse("0", 100);
        var otherBranch = Warehouse("1", 200);
        fixture.Db.AddRange(user, allowed, otherBranch);
        await fixture.Db.SaveChangesAsync();
        fixture.Db.UserWarehouseAssignments.AddRange(
            Assignment(user.Id, allowed),
            Assignment(user.Id, otherBranch));
        await fixture.Db.SaveChangesAsync();

        var access = await UserWarehouseAccessService.ResolveAsync(
            fixture.UnitOfWork, user.Id, "0", default);

        Assert.True(access.IsRestricted);
        Assert.Equal([allowed.Id], access.WarehouseIds);
        Assert.Equal([100], access.WarehouseCodes);
    }

    private static Fixture CreateFixture()
    {
        var db = new WmsDbContext(new DbContextOptionsBuilder<WmsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);
        return new(db, new UnitOfWork(db, new HttpContextAccessor()));
    }

    private static User User(string role) => new()
    {
        Username = $"{role}-{Guid.NewGuid():N}",
        Email = $"{Guid.NewGuid():N}@test.local",
        PasswordHash = "hash",
        Role = role,
        IsActive = true
    };

    private static Warehouse Warehouse(string branchCode, int code) => new()
    {
        BranchCode = branchCode,
        WarehouseCode = code,
        WarehouseName = $"Depo {code}"
    };

    private static UserWarehouseAssignment Assignment(long userId, Warehouse warehouse) => new()
    {
        BranchCode = warehouse.BranchCode,
        UserId = userId,
        WarehouseId = warehouse.Id
    };

    private sealed class Fixture(WmsDbContext db, UnitOfWork unitOfWork) : IAsyncDisposable
    {
        public WmsDbContext Db { get; } = db;
        public UnitOfWork UnitOfWork { get; } = unitOfWork;
        public ValueTask DisposeAsync() => UnitOfWork.DisposeAsync();
    }
}
