using ClosedXML.Excel;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using verii_wms_api_v2.Modules.Identity.Infrastructure;
using verii_wms_api_v2.Modules.Kkd.Application;
using verii_wms_api_v2.Modules.Kkd.Domain;
using verii_wms_api_v2.Shared.Infrastructure.Persistence;
using Xunit;

namespace verii_wms_api_v2.QueryTests;

public sealed class KkdDefinitionWorkbookServiceTests
{
    [Fact]
    public async Task Template_contains_guide_all_definition_sheets_and_current_rows()
    {
        await using var fixture = await CreateFixtureAsync();

        var bytes = await fixture.Workbook.CreateTemplateAsync("0");

        using var stream = new MemoryStream(bytes);
        using var workbook = new XLWorkbook(stream);
        Assert.Equal(
            ["00_KILAVUZ", "01_DEPARTMANLAR", "02_ROLLER", "03_PERSONELLER", "04_HAK_MATRISLERI",
             "05_HAK_KURALLARI", "06_HAK_DONEMLERI", "REF_CARILER", "REF_STOKLAR", "REF_KULLANICILAR"],
            workbook.Worksheets.Select(x => x.Name).ToArray());
        Assert.Equal("URETIM", workbook.Worksheet("01_DEPARTMANLAR").Cell(2, 2).GetString());
        Assert.Contains(
            workbook.Worksheet("00_KILAVUZ").CellsUsed().Select(x => x.GetString()),
            value => value.Contains("Satırı Excel'den silmek", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Import_updates_existing_creates_new_and_is_idempotent()
    {
        await using var fixture = await CreateFixtureAsync();
        var bytes = await fixture.Workbook.CreateTemplateAsync("0");
        using var template = new MemoryStream(bytes);
        using var workbook = new XLWorkbook(template);
        var departments = workbook.Worksheet("01_DEPARTMANLAR");
        departments.Cell(2, 3).Value = "Üretim ve Montaj";
        departments.Cell(3, 2).Value = "LOJISTIK";
        departments.Cell(3, 3).Value = "Lojistik";
        departments.Cell(3, 4).Value = "EVET";

        using var upload = new MemoryStream();
        workbook.SaveAs(upload);
        upload.Position = 0;
        var first = await fixture.Workbook.ImportAsync(upload, "0", 99);

        Assert.Equal(1, first.Departments.Created);
        Assert.Equal(1, first.Departments.Updated);
        Assert.Equal(2, await fixture.Db.KkdDepartments.CountAsync());
        Assert.Equal("Üretim ve Montaj", (await fixture.Db.KkdDepartments.SingleAsync(x => x.Code == "URETIM")).Name);

        upload.Position = 0;
        var second = await fixture.Workbook.ImportAsync(upload, "0", 99);

        Assert.Equal(0, second.Departments.Created);
        Assert.Equal(0, second.Departments.Updated);
        Assert.Equal(2, second.Departments.Unchanged);
        Assert.Equal(2, await fixture.Db.KkdDepartments.CountAsync());
    }

    [Theory]
    [InlineData("Gün", "Day")]
    [InlineData("Ay", "Month")]
    [InlineData("Yıl", "Year")]
    [InlineData("Günlük", "Day")]
    [InlineData("Aylık", "Month")]
    [InlineData("Yıllık", "Year")]
    [InlineData("Yillik", "Year")]
    [InlineData("Day", "Day")]
    [InlineData("Month", "Month")]
    [InlineData("Year", "Year")]
    [InlineData("", null)]
    public void ParsePeriodType_accepts_turkish_and_english_aliases(string input, string? expected)
    {
        var method = typeof(KkdDefinitionWorkbookService).GetMethod(
            "ParsePeriodType",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.NotNull(method);
        var actual = method!.Invoke(null, [input]);
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("Haftalık")]
    [InlineData("xyz")]
    [InlineData("Quarter")]
    public void ParsePeriodType_rejects_unknown_values(string input)
    {
        var method = typeof(KkdDefinitionWorkbookService).GetMethod(
            "ParsePeriodType",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.NotNull(method);
        var ex = Assert.Throws<System.Reflection.TargetInvocationException>(() => method!.Invoke(null, [input]));
        Assert.IsType<FormatException>(ex.InnerException);
        Assert.Contains("Periyot Tipi", ex.InnerException!.Message, StringComparison.Ordinal);
    }

    private static async Task<Fixture> CreateFixtureAsync()
    {
        var options = new DbContextOptionsBuilder<WmsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .ConfigureWarnings(x => x.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        var db = new WmsDbContext(options);
        db.KkdDepartments.Add(new KkdDepartment
        {
            BranchCode = "0",
            Code = "URETIM",
            Name = "Üretim",
            IsActive = true
        });
        await db.SaveChangesAsync();
        var uow = new UnitOfWork(db, new HttpContextAccessor());
        var definitions = new KkdDefinitionService(uow);
        return new Fixture(db, uow, new KkdDefinitionWorkbookService(uow, definitions));
    }

    private sealed class Fixture(WmsDbContext db, UnitOfWork uow, KkdDefinitionWorkbookService workbook) : IAsyncDisposable
    {
        public WmsDbContext Db { get; } = db;
        public KkdDefinitionWorkbookService Workbook { get; } = workbook;

        public async ValueTask DisposeAsync()
        {
            await uow.DisposeAsync();
            await Db.DisposeAsync();
        }
    }
}
