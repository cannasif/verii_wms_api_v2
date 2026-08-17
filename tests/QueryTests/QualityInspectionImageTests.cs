using System.Security.Claims;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using verii_wms_api_v2.Modules.Identity.Infrastructure;
using verii_wms_api_v2.Modules.Quality.Application;
using verii_wms_api_v2.Modules.Quality.Domain;
using verii_wms_api_v2.Modules.Quality.Localization;
using verii_wms_api_v2.Shared.Application.Exceptions;
using verii_wms_api_v2.Shared.Infrastructure.Files;
using verii_wms_api_v2.Shared.Infrastructure.Persistence;
using Xunit;

namespace verii_wms_api_v2.QueryTests;

public sealed class QualityInspectionImageTests
{
    private static readonly byte[] PngBytes=[0x89,0x50,0x4E,0x47,0x0D,0x0A,0x1A,0x0A,0x00];

    [Fact]
    public void Model_keeps_quality_evidence_separate_from_stock_images()
    {
        using var context=new WmsDbContext(new DbContextOptionsBuilder<WmsDbContext>()
            .UseSqlServer("Server=invalid;Database=invalid;User Id=invalid;Password=invalid;TrustServerCertificate=True").Options);
        var entity=context.Model.FindEntityType(typeof(QualityInspectionImage));
        Assert.NotNull(entity);
        Assert.Equal("RII_QUALITY_INSPECTION_IMAGES",entity.GetTableName());
        Assert.Equal(3,entity.GetForeignKeys().Count());
        Assert.DoesNotContain(entity.GetProperties(),property=>property.Name.Contains("StockImage",StringComparison.OrdinalIgnoreCase));
        Assert.Contains(entity.GetProperties(),property=>property.Name==nameof(QualityInspectionImage.StoragePath));
    }

    [Fact]
    public async Task Upload_open_and_delete_use_private_line_directory_and_soft_delete_metadata()
    {
        var root=Path.Combine(Path.GetTempPath(),"wms-quality-image-tests",Guid.NewGuid().ToString("N"));
        try
        {
            var options=new DbContextOptionsBuilder<WmsDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
                .ConfigureWarnings(x=>x.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options;
            await using var db=new WmsDbContext(options);
            var inspection=new QualityInspection
            {
                BranchCode="7",InspectionNo="QC-1",SourceDocumentType="GR",SourceDocumentId=1,
                SourceDocumentNo="IRS-1",WarehouseId=1
            };
            var line=new QualityInspectionLine
            {
                BranchCode="7",Inspection=inspection,StockId=11,StockCodeSnapshot="STK-1",Quantity=1,SampleQuantity=1
            };
            db.Add(line);
            await db.SaveChangesAsync();

            await using var unitOfWork=new UnitOfWork(db,Accessor("7"));
            var storage=new PrivateUploadStorage(new TestEnvironment(root));
            var service=new QualityInspectionImageService(db,unitOfWork,storage,TestLocalizer.Instance,NullLogger<QualityInspectionImageService>.Instance);
            await using var uploadStream=new MemoryStream(PngBytes);

            var result=await service.UploadAsync(inspection.Id,line.Id,"7",42,
                [new QualityInspectionImageUpload(uploadStream,"kanıt.png","image/png",PngBytes.Length,"Ambalaj kontrolü","route-a")]);

            var image=Assert.Single(result);
            Assert.Equal(line.Id,image.QualityInspectionLineId);
            Assert.StartsWith($"/api/quality/inspections/{inspection.Id}/lines/{line.Id}/images/",image.ContentUrl,StringComparison.Ordinal);
            var entity=await db.QualityInspectionImages.AsNoTracking().SingleAsync();
            Assert.StartsWith($"Upload/Kalite/{line.Id}/",entity.StoragePath,StringComparison.Ordinal);
            Assert.True(File.Exists(Path.Combine(root,entity.StoragePath.Replace('/',Path.DirectorySeparatorChar))));

            var opened=await service.OpenAsync(inspection.Id,line.Id,image.Id,"7");
            await using(opened.Content)
            {
                using var copy=new MemoryStream();
                await opened.Content.CopyToAsync(copy);
                Assert.Equal(PngBytes,copy.ToArray());
            }

            await service.DeleteAsync(inspection.Id,line.Id,image.Id,"7",42);
            Assert.Empty(await service.ListAsync(inspection.Id,line.Id,"7"));
            Assert.True((await db.QualityInspectionImages.IgnoreQueryFilters().SingleAsync()).IsDeleted);
            Assert.False(File.Exists(Path.Combine(root,entity.StoragePath.Replace('/',Path.DirectorySeparatorChar))));
        }
        finally
        {
            if(Directory.Exists(root))Directory.Delete(root,true);
        }
    }

    [Fact]
    public async Task Cross_branch_access_is_rejected()
    {
        var options=new DbContextOptionsBuilder<WmsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .ConfigureWarnings(x=>x.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options;
        await using var db=new WmsDbContext(options);
        var inspection=new QualityInspection{BranchCode="7",InspectionNo="QC-2",SourceDocumentType="GR",SourceDocumentId=2,SourceDocumentNo="IRS-2",WarehouseId=1};
        var line=new QualityInspectionLine{BranchCode="7",Inspection=inspection,StockId=12,StockCodeSnapshot="STK-2",Quantity=1,SampleQuantity=1};
        db.Add(line);
        await db.SaveChangesAsync();
        await using var unitOfWork=new UnitOfWork(db,Accessor("8"));
        var root=Path.Combine(Path.GetTempPath(),"wms-quality-image-tests",Guid.NewGuid().ToString("N"));
        try
        {
            var service=new QualityInspectionImageService(db,unitOfWork,new PrivateUploadStorage(new TestEnvironment(root)),TestLocalizer.Instance,NullLogger<QualityInspectionImageService>.Instance);
            var error=await Assert.ThrowsAsync<AppException>(()=>service.ListAsync(inspection.Id,line.Id,"8"));
            Assert.Equal(StatusCodes.Status404NotFound,error.StatusCode);
        }
        finally
        {
            if(Directory.Exists(root))Directory.Delete(root,true);
        }
    }

    private static HttpContextAccessor Accessor(string branch)=>new(){HttpContext=new DefaultHttpContext
    {
        User=new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier,"42"),new Claim(JwtTokenIssuer.BranchCodeClaim,branch)],"Test"))
    }};

    private sealed class TestEnvironment(string root):IWebHostEnvironment
    {
        public string ApplicationName { get; set; }="verii_wms_api_v2.QueryTests";
        public IFileProvider WebRootFileProvider { get; set; }=new NullFileProvider();
        public string WebRootPath { get; set; }=Path.Combine(root,"wwwroot");
        public string EnvironmentName { get; set; }="Test";
        public string ContentRootPath { get; set; }=root;
        public IFileProvider ContentRootFileProvider { get; set; }=new NullFileProvider();
    }

    private sealed class TestLocalizer:IStringLocalizer<QualityResource>
    {
        public static TestLocalizer Instance { get; }=new();
        public LocalizedString this[string name]=>new(name,name,true);
        public LocalizedString this[string name,params object[] arguments]=>new(name,$"{name}: {string.Join(", ",arguments)}",true);
        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures)=>[];
    }
}
