using Microsoft.EntityFrameworkCore;
using verii_wms_api_v2.Modules.Identity.Infrastructure;
using verii_wms_api_v2.Modules.SerialNumberPolicy.Domain;
using Xunit;

namespace verii_wms_api_v2.QueryTests;

public sealed class StockSerialRegistryModelTests
{
    [Fact]
    public void Registry_enforces_stock_scoped_permanent_serial_uniqueness()
    {
        using var context = CreateContext();
        var entity = context.Model.FindEntityType(typeof(StockSerialRegistry));
        Assert.NotNull(entity);
        var index = Assert.Single(entity!.GetIndexes(),
            x => x.GetDatabaseName() == "UX_RII_STOCK_SERIAL_STOCK_NUMBER");

        Assert.True(index.IsUnique);
        Assert.Equal(["StockId", "NormalizedSerialNo"], index.Properties.Select(x => x.Name));
        Assert.Null(index.GetFilter());
    }

    [Fact]
    public void Registry_rows_cannot_be_deleted_or_soft_deleted()
    {
        using var context = CreateContext();
        var row = new StockSerialRegistry
        {
            Id = 1,
            StockId = 10,
            SerialNo = "SERIAL-1",
            NormalizedSerialNo = "SERIAL-1",
            GenerationRequestKey = "TEST",
            ReservedAtUtc = DateTimeOffset.UtcNow
        };
        context.Attach(row);
        context.Remove(row);

        var exception = Assert.Throws<InvalidOperationException>(() => context.SaveChanges());
        Assert.Contains("Voided", exception.Message);

        context.Entry(row).State = EntityState.Unchanged;
        row.IsDeleted = true;
        context.Entry(row).State = EntityState.Modified;
        exception = Assert.Throws<InvalidOperationException>(() => context.SaveChanges());
        Assert.Contains("Voided", exception.Message);
    }

    private static WmsDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<WmsDbContext>()
            .UseSqlServer("Server=invalid;Database=invalid;User Id=invalid;Password=invalid;TrustServerCertificate=True")
            .Options);
}
