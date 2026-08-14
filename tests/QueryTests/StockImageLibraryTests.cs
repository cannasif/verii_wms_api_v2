using System.Security.Claims;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using verii_wms_api_v2.Modules.Identity.Infrastructure;
using verii_wms_api_v2.Modules.Stock.Application;
using verii_wms_api_v2.Modules.Stock.Domain;
using verii_wms_api_v2.Modules.Stock.Infrastructure;
using verii_wms_api_v2.Shared.Application.Exceptions;
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

    [Fact]
    public async Task Physical_storage_writes_valid_image_to_managed_directory()
    {
        var root=Path.Combine(Path.GetTempPath(),"wms-stock-image-tests",Guid.NewGuid().ToString("N"));
        try
        {
            var storage=new StockImageStorage(new TestEnvironment(root),NullLogger<StockImageStorage>.Instance);
            var bytes=new byte[]{0x89,0x50,0x4E,0x47,0x0D,0x0A,0x1A,0x0A,0x00};
            await using var stream=new MemoryStream(bytes);

            var result=await storage.SaveAsync("100",42,new StockImageUpload(
                stream,"stock.png","image/png",bytes.Length,null));

            Assert.StartsWith("/uploads/stock-images/100/42/",result.Url,StringComparison.Ordinal);
            var relative=result.Url["/uploads/stock-images/".Length..].Replace('/',Path.DirectorySeparatorChar);
            Assert.True(File.Exists(Path.Combine(root,"wwwroot","uploads","stock-images",relative)));
        }
        finally
        {
            if(Directory.Exists(root))Directory.Delete(root,true);
        }
    }

    [Fact]
    public async Task Physical_storage_maps_unwritable_path_to_service_unavailable()
    {
        var parent=Path.Combine(Path.GetTempPath(),"wms-stock-image-tests",Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(parent);
        var blockingFile=Path.Combine(parent,"not-a-directory");
        await File.WriteAllTextAsync(blockingFile,"blocked");
        try
        {
            var storage=new StockImageStorage(new TestEnvironment(blockingFile),NullLogger<StockImageStorage>.Instance);
            var bytes=new byte[]{0x89,0x50,0x4E,0x47,0x0D,0x0A,0x1A,0x0A,0x00};
            await using var stream=new MemoryStream(bytes);

            var exception=await Assert.ThrowsAsync<AppException>(()=>storage.SaveAsync("100",42,
                new StockImageUpload(stream,"stock.png","image/png",bytes.Length,null)));

            Assert.Equal(StatusCodes.Status503ServiceUnavailable,exception.StatusCode);
            Assert.Contains("klasör yetkisini",exception.Message,StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if(Directory.Exists(parent))Directory.Delete(parent,true);
        }
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

    private sealed class TestEnvironment(string contentRoot):IWebHostEnvironment
    {
        public string ApplicationName { get; set; }="verii_wms_api_v2.QueryTests";
        public IFileProvider WebRootFileProvider { get; set; }=new NullFileProvider();
        public string WebRootPath { get; set; }=Path.Combine(contentRoot,"wwwroot");
        public string EnvironmentName { get; set; }="Test";
        public string ContentRootPath { get; set; }=contentRoot;
        public IFileProvider ContentRootFileProvider { get; set; }=new NullFileProvider();
    }
}
