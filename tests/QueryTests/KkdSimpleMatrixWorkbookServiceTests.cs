using ClosedXML.Excel;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using verii_wms_api_v2.Modules.Customer.Domain;
using verii_wms_api_v2.Modules.Identity.Infrastructure;
using verii_wms_api_v2.Modules.Kkd.Application;
using verii_wms_api_v2.Modules.Kkd.Domain;
using verii_wms_api_v2.Modules.Stock.Domain;
using verii_wms_api_v2.Shared.Infrastructure.Persistence;
using Xunit;

namespace verii_wms_api_v2.QueryTests;

public sealed class KkdSimpleMatrixWorkbookServiceTests
{
    [Fact]
    public async Task Template_uses_customer_wide_matrix_layout_and_protected_references()
    {
        await using var fixture = await CreateFixtureAsync();

        var bytes = await fixture.Service.CreateTemplateAsync(fixture.Customer.Id, "0");

        using var workbook = new XLWorkbook(new MemoryStream(bytes));
        Assert.Equal("Liste", workbook.Worksheet(1).Name);
        Assert.Equal("Bölüm", workbook.Worksheet("Liste").Cell("A4").GetString());
        Assert.Equal("Görev Tanımı", workbook.Worksheet("Liste").Cell("B4").GetString());
        Assert.Equal("BY/MY", workbook.Worksheet("Liste").Cell("C4").GetString());
        Assert.True(workbook.Worksheet("REF_BOLUM_GOREV").Protection.IsProtected);
        Assert.True(workbook.Worksheet("REF_STOKLAR").Protection.IsProtected);
        Assert.Equal(XLWorksheetVisibility.VeryHidden, workbook.Worksheet("__WMS_META").Visibility);
        Assert.Contains(
            workbook.Worksheet("KILAVUZ").CellsUsed().Select(x => x.GetString()),
            value => value.Contains(fixture.Customer.CustomerName, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Preview_and_commit_accept_reference_shape_with_controlled_repairs()
    {
        await using var fixture = await CreateFixtureAsync();
        var bytes = CreateReferenceShapedWorkbook(fixture.Stock.ErpStockCode);

        var preview = await fixture.Service.PreviewAsync(
            new MemoryStream(bytes), fixture.Customer.Id, new DateOnly(2026, 8, 20), "0");

        Assert.True(preview.CanCommit);
        Assert.Empty(preview.Errors);
        Assert.Equal(1, preview.SourceRowCount);
        Assert.Equal(1, preview.MatrixCount);
        Assert.Equal(1, preview.CreateCount);
        Assert.Equal(1, preview.RuleCount);
        Assert.Equal(2, preview.PhaseCount);
        Assert.Contains(preview.Warnings, x => x.Code == "PRODUCT_NAME_USED_AS_PHASE");
        Assert.Contains(preview.Warnings, x => x.Code == "WORKER_CLASS_INFORMATIONAL");

        var result = await fixture.Service.ImportAsync(
            new MemoryStream(bytes), fixture.Customer.Id, new DateOnly(2026, 8, 20), "0",
            preview.FileHash, preview.StateHash, 99);

        Assert.Equal(1, result.Created);
        var matrix = await fixture.Db.KkdEntitlementMatrices
            .Include(x => x.Rules).ThenInclude(x => x.Phases)
            .SingleAsync();
        var phases = matrix.Rules.Single().Phases.OrderBy(x => x.SortOrder).ToArray();
        Assert.Equal(KkdEntitlementPhaseType.Initial, phases[0].PhaseType);
        Assert.Equal(KkdEntitlementPhaseType.Recurring, phases[1].PhaseType);
        Assert.Equal(KkdPeriodType.Month, phases[1].PeriodType);
        Assert.Equal(2, phases[1].PeriodInterval);
        Assert.Equal(2, phases[1].OffsetMonths);
    }

    [Fact]
    public async Task Preview_rejects_conflicting_rows_for_same_department_and_role()
    {
        await using var fixture = await CreateFixtureAsync();
        var bytes = CreateReferenceShapedWorkbook(fixture.Stock.ErpStockCode, addConflictingDuplicate: true);

        var preview = await fixture.Service.PreviewAsync(
            new MemoryStream(bytes), fixture.Customer.Id, new DateOnly(2026, 8, 20), "0");

        Assert.False(preview.CanCommit);
        Assert.Contains(preview.Errors, x => x.Code == "CONFLICTING_ROLE_ROWS");
        Assert.Empty(fixture.Db.KkdEntitlementMatrices);
    }

    private static byte[] CreateReferenceShapedWorkbook(string stockCode, bool addConflictingDuplicate = false)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Liste");
        sheet.Range("D1:E1").Merge();
        sheet.Range("D2:E2").Merge();
        sheet.Range("D3:E3").Merge();
        sheet.Cell("D1").Value = "CEKET";
        sheet.Cell("D2").Value = "EN ISO 13688";
        sheet.Cell("D3").Value = $"{stockCode} Baret";
        sheet.Cell("A4").Value = "Bölüm";
        sheet.Cell("B4").Value = "Görev Tanımı";
        sheet.Cell("C4").Value = "BY/MY";
        sheet.Cell("D4").Value = "CEKET";
        sheet.Cell("E4").Value = "RUTİNDE HER DÖNEM (2 AYDA 1)";
        sheet.Cell("A5").Value = "üretim";
        sheet.Cell("B5").Value = "OP";
        sheet.Cell("C5").Value = "MY";
        sheet.Cell("D5").Value = 1;
        sheet.Cell("E5").Value = 2;
        if (addConflictingDuplicate)
        {
            sheet.Cell("A6").Value = "URETIM";
            sheet.Cell("B6").Value = "Operatör";
            sheet.Cell("C6").Value = "Saha BY";
            sheet.Cell("D6").Value = 3;
            sheet.Cell("E6").Value = 2;
        }
        using var output = new MemoryStream();
        workbook.SaveAs(output);
        return output.ToArray();
    }

    private static async Task<Fixture> CreateFixtureAsync()
    {
        var options = new DbContextOptionsBuilder<WmsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .ConfigureWarnings(x => x.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        var db = new WmsDbContext(options);
        var department = new KkdDepartment { BranchCode = "0", Code = "URETIM", Name = "Üretim", IsActive = true };
        var role = new KkdRole { BranchCode = "0", Department = department, Code = "OP", Name = "Operatör", IsActive = true };
        var customer = new Customer { BranchCode = "0", CustomerCode = "C-1", CustomerName = "Test Cari" };
        var stock = new Stock
        {
            BranchCode = "0", ErpStockCode = "150-02-101-001-0001", StockName = "Baret", GroupCode = "KKD", BaseUnitCode = "ADET"
        };
        db.AddRange(department, role, customer, stock);
        await db.SaveChangesAsync();
        var uow = new UnitOfWork(db, new HttpContextAccessor());
        var definitions = new KkdDefinitionService(uow);
        return new Fixture(db, uow, new KkdSimpleMatrixWorkbookService(uow, definitions), customer, stock);
    }

    private sealed class Fixture(
        WmsDbContext db,
        UnitOfWork uow,
        KkdSimpleMatrixWorkbookService service,
        Customer customer,
        Stock stock) : IAsyncDisposable
    {
        public WmsDbContext Db { get; } = db;
        public KkdSimpleMatrixWorkbookService Service { get; } = service;
        public Customer Customer { get; } = customer;
        public Stock Stock { get; } = stock;

        public async ValueTask DisposeAsync()
        {
            await uow.DisposeAsync();
            await Db.DisposeAsync();
        }
    }
}
