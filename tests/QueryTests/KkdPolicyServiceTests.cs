using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using verii_wms_api_v2.Modules.Identity.Infrastructure;
using verii_wms_api_v2.Modules.Kkd.Application;
using verii_wms_api_v2.Shared.Infrastructure.Persistence;
using Xunit;

namespace verii_wms_api_v2.QueryTests;

public sealed class KkdPolicyServiceTests
{
    [Fact]
    public async Task Missing_policy_returns_safe_order_required_defaults_without_writing_a_row()
    {
        await using var fixture = Fixture.Create();

        var result = await fixture.Service.GetAsync("0");

        Assert.True(result.EnableMaterialRequestOrderFlow);
        Assert.True(result.RequireOpenOrder);
        Assert.True(result.AllowOpenOrderExcess);
        Assert.True(result.AllowMultipleOrdersPerDistribution);
        Assert.False(result.AllowFutureDatedDistribution);
        Assert.Empty(fixture.Db.KkdPolicies);
    }

    [Fact]
    public async Task Update_persists_one_branch_scoped_default_policy()
    {
        await using var fixture = Fixture.Create();
        var request = new UpdateKkdPolicyRequest(
            EnableMaterialRequestOrderFlow: false,
            RequireOpenOrder: true,
            AllowOpenOrderExcess: false,
            AllowMultipleOrdersPerDistribution: false,
            RequireEmployeeUserLink: true,
            AllowFutureDatedDistribution: false,
            RequireManagerApprovalForExcess: true);

        var first = await fixture.Service.UpdateAsync("0", request, 42);
        var second = await fixture.Service.GetAsync("0");

        Assert.Equal(first.Id, second.Id);
        Assert.False(second.EnableMaterialRequestOrderFlow);
        Assert.True(second.RequireOpenOrder);
        Assert.False(second.AllowOpenOrderExcess);
        Assert.False(second.AllowMultipleOrdersPerDistribution);
        Assert.True(second.RequireEmployeeUserLink);
        Assert.True(second.RequireManagerApprovalForExcess);
        Assert.Single(fixture.Db.KkdPolicies);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private Fixture(WmsDbContext db)
        {
            Db = db;
            Service = new KkdPolicyService(new UnitOfWork(db, new HttpContextAccessor()));
        }

        public WmsDbContext Db { get; }
        public KkdPolicyService Service { get; }

        public static Fixture Create()
        {
            var options = new DbContextOptionsBuilder<WmsDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
                .Options;
            return new Fixture(new WmsDbContext(options));
        }

        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }
}
