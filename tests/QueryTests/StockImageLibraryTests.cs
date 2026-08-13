using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using verii_wms_api_v2.Modules.Identity.Infrastructure;
using verii_wms_api_v2.Modules.Stock.Application;
using verii_wms_api_v2.Modules.Stock.Domain;
using verii_wms_api_v2.Shared.Infrastructure.Persistence;
using Xunit;

namespace verii_wms_api_v2.QueryTests;

public sealed class StockImageLibraryTests
{
    [Fact]
    public void Model_enforces_one_cover_and_deterministic_order_per_stock()
    {
        using var context=CreateSqlModelContext();
        var entity=context.Model.FindEntityType(typeof(StockImage));
        Assert.NotNull(entity);
        Assert.Equal("RII_STOCK_IMAGE",entity.GetTableName());
        var primary=Assert.Single(entity.GetIndexes(),x=>x.GetDatabaseName()=="UX_StockImage_OnePrimary");
        Assert.True(primary.IsUnique);
        Assert.Equal("[IsDeleted] = 0 AND [IsPrimary] = 1",primary.GetFilter());
        var order=Assert.Single(entity.GetIndexes(),x=>x.GetDatabaseName()=="UX_StockImage_Branch_Stock_SortOrder");
        Assert.True(order.IsUnique);
        var stockRelation=Assert.Single(entity.GetForeignKeys());
        Assert.Equal(typeof(Stock),stockRelation.PrincipalEntityType.ClrType);
        Assert.DoesNotContain(entity.GetProperties(),property=>
            property.Name.Contains("GoodsReceipt",StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Upload_sets_first_cover_and_delete_promotes_next_image()
    {
        var options=new DbContextOptionsBuilder<WmsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .ConfigureWarnings(x=>x.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options;
        await using var db=new WmsDbContext(options);
        var stock=new Stock{BranchCode="7",ErpStockCode="STK-1",StockName="Test",BusinessUnitCode=7};
        db.Stocks.Add(stock); await db.SaveChangesAsync();
        await using var unitOfWork=new UnitOfWork(db,Accessor("7"));
        var storage=new FakeStorage();
        var service=new StockImageService(db,unitOfWork,storage,NullLogger<StockImageService>.Instance);
        var uploads=new[]
        {
            new StockImageUpload(Stream.Null,"front.png","image/png",10,"Ön"),
            new StockImageUpload(Stream.Null,"back.png","image/png",10,"Arka")
        };

        var images=await service.UploadAsync(stock.Id,"7",42,uploads);
        Assert.Equal(2,images.Count);
        Assert.True(images[0].IsPrimary);
        Assert.False(images[1].IsPrimary);

        await service.DeleteAsync(stock.Id,images[0].Id,"7",42);
        var remaining=Assert.Single(await service.ListAsync(stock.Id,"7"));
        Assert.True(remaining.IsPrimary);
        Assert.Equal(1,remaining.SortOrder);
        Assert.Single(storage.Deleted);
    }

    private static WmsDbContext CreateSqlModelContext()=>new(new DbContextOptionsBuilder<WmsDbContext>()
        .UseSqlServer("Server=invalid;Database=invalid;User Id=invalid;Password=invalid;TrustServerCertificate=True").Options);
    private static HttpContextAccessor Accessor(string branch)=>new(){HttpContext=new DefaultHttpContext
    {
        User=new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier,"42"),new Claim(JwtTokenIssuer.BranchCodeClaim,branch)],"Test"))
    }};

    private sealed class FakeStorage:IStockImageStorage
    {
        private int _next;
        public List<string> Deleted { get; }=[];
        public Task<StoredStockImage> SaveAsync(string branchCode,long stockId,StockImageUpload upload,CancellationToken ct=default)
        {
            var url=$"/uploads/stock-images/{branchCode}/{stockId}/{++_next}.png";
            return Task.FromResult(new StoredStockImage(url,upload.FileName,"image/png",upload.Length));
        }
        public Task DeleteIfManagedAsync(string? relativeUrl,CancellationToken ct=default){if(relativeUrl is not null)Deleted.Add(relativeUrl);return Task.CompletedTask;}
    }
}
