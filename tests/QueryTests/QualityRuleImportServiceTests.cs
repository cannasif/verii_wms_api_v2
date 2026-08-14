using ClosedXML.Excel;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using verii_wms_api_v2.Modules.Identity.Infrastructure;
using verii_wms_api_v2.Modules.Quality.Application;
using verii_wms_api_v2.Modules.Quality.Domain;
using verii_wms_api_v2.Modules.Stock.Domain;
using verii_wms_api_v2.Shared;
using verii_wms_api_v2.Shared.Infrastructure.Persistence;
using Xunit;

namespace verii_wms_api_v2.QueryTests;

public sealed class QualityRuleImportServiceTests
{
    private static readonly string[] Headers =
    [
        "ScopeType", "StockCode", "StockGroupCode", "InspectionMode", "SamplingMode",
        "SamplingValue", "FailAction", "AutoQuarantine", "RequireLot", "RequireSerial",
        "RequireExpiryDate", "MinimumRemainingShelfLifeDays", "IsActive", "Description"
    ];

    [Fact]
    public async Task Template_contains_distinct_stock_groups_for_selected_branch()
    {
        await using var db = CreateDbContext();
        db.Stocks.AddRange(
            Stock("01/001", "SAC", "0"),
            Stock("01/002", "SAC", "0"),
            Stock("01/003", "KIMYA", "0"),
            Stock("01/004", "DIGER_SUBE", "1"));
        await db.SaveChangesAsync();
        var service = CreateService(db, new RecordingQualityService());

        var bytes = await service.CreateTemplateAsync("0");

        using var workbook = new XLWorkbook(new MemoryStream(bytes));
        var groups = workbook.Worksheet("Stok Grupları");
        Assert.Equal("KIMYA", groups.Cell(2, 1).GetString());
        Assert.Equal(1, groups.Cell(2, 2).GetValue<int>());
        Assert.Equal("SAC", groups.Cell(3, 1).GetString());
        Assert.Equal(2, groups.Cell(3, 2).GetValue<int>());
        Assert.DoesNotContain(groups.CellsUsed().Select(x => x.GetString()), x => x == "DIGER_SUBE");
        Assert.Equal(XLAllowedValues.List, workbook.Worksheet("Kalite Kuralları").Cell(2, 1).GetDataValidation().AllowedValues);
    }

    [Fact]
    public async Task Import_creates_new_stock_and_group_rules_but_never_overwrites_active_scope()
    {
        await using var db = CreateDbContext();
        var stockOne = Stock("01/001", "SAC", "0");
        var stockTwo = Stock("01/002", "KIMYA", "0");
        db.Stocks.AddRange(stockOne, stockTwo);
        await db.SaveChangesAsync();
        db.QualityRules.Add(new QualityRule
        {
            BranchCode = "0",
            ScopeType = QualityRuleScopeTypes.Stock,
            StockId = stockOne.Id,
            IsActive = true
        });
        await db.SaveChangesAsync();
        var recorder = new RecordingQualityService();
        var service = CreateService(db, recorder);
        await using var workbook = Workbook(
            ["Stock", "01/001", "", "InspectionRequired", "All", "100", "Quarantine", "true", "false", "false", "false", "", "true", "Mevcut"],
            ["StockGroup", "", "SAC", "QuickCheck", "Percentage", "10", "ManagerApproval", "false", "false", "false", "false", "", "true", "Grup"],
            ["StockGroup", "", "SAC", "QuickCheck", "Percentage", "10", "ManagerApproval", "false", "false", "false", "false", "", "true", "Tekrar"],
            ["Stock", "BULUNAMADI", "", "InspectionRequired", "All", "100", "Quarantine", "true", "false", "false", "false", "", "true", "Hatalı"]);

        var result = await service.ImportAsync(workbook, "0", 42);

        Assert.Equal(4, result.TotalRows);
        Assert.Equal(1, result.CreatedCount);
        Assert.Equal(2, result.SkippedCount);
        Assert.Equal(1, result.FailedCount);
        var created = Assert.Single(recorder.Created);
        Assert.Equal(QualityRuleScopeTypes.StockGroup, created.Request.ScopeType);
        Assert.Equal("SAC", created.Request.StockGroupCode);
        Assert.Equal(42, created.Actor);
    }

    private static QualityRuleImportService CreateService(WmsDbContext db, IQualityService qualityService) =>
        new(new UnitOfWork(db, new HttpContextAccessor()), qualityService);

    private static WmsDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<WmsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new WmsDbContext(options);
    }

    private static Stock Stock(string code, string group, string branch) =>
        new()
        {
            BranchCode = branch,
            ErpStockCode = code,
            StockName = code,
            GroupCode = group
        };

    private static MemoryStream Workbook(params string[][] rows)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Kalite Kuralları");
        for (var column = 0; column < Headers.Length; column++)
            sheet.Cell(1, column + 1).Value = Headers[column];
        for (var row = 0; row < rows.Length; row++)
            for (var column = 0; column < rows[row].Length; column++)
                sheet.Cell(row + 2, column + 1).Value = rows[row][column];
        var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;
        return stream;
    }

    private sealed class RecordingQualityService : IQualityService
    {
        public List<(QualityRuleUpsertRequest Request, long Actor)> Created { get; } = [];

        public Task<long> CreateRuleAsync(QualityRuleUpsertRequest request, long actor, CancellationToken ct = default)
        {
            Created.Add((request, actor));
            return Task.FromResult((long)Created.Count);
        }

        public Task<QualityParameterDto> GetParametersAsync(string branchCode, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<QualityParameterDto> UpdateParametersAsync(UpdateQualityParameterRequest request, long actor, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<PagedResponse<QualityRuleGridRow>> GetRulesPagedAsync(PagedRequest request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<PagedResponse<QualityStockGroupOption>> GetStockGroupsPagedAsync(string branchCode, PagedRequest request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<PagedResponse<QualityDecisionCodeGridRow>> GetDecisionCodesPagedAsync(PagedRequest request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<PagedResponse<QualityDecisionCodeOption>> GetDecisionCodeOptionsPagedAsync(string branchCode, QualityDecision decision, PagedRequest request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<long> CreateDecisionCodeAsync(QualityDecisionCodeUpsertRequest request, long actor, CancellationToken ct = default) => throw new NotSupportedException();
        public Task UpdateDecisionCodeAsync(long id, QualityDecisionCodeUpsertRequest request, long actor, CancellationToken ct = default) => throw new NotSupportedException();
        public Task DeleteDecisionCodeAsync(long id, long actor, CancellationToken ct = default) => throw new NotSupportedException();
        public Task UpdateRuleAsync(long id, QualityRuleUpsertRequest request, long actor, CancellationToken ct = default) => throw new NotSupportedException();
        public Task DeleteRuleAsync(long id, long actor, CancellationToken ct = default) => throw new NotSupportedException();
        public QualityInspectionStatusCatalogDto GetInspectionStatusCatalog() => throw new NotSupportedException();
        public Task<PagedResponse<QualityInspectionGridRow>> GetInspectionsPagedAsync(PagedRequest request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<QualityInspectionDetail> GetInspectionAsync(long id, long actor, bool canExecute, bool canSupervise, bool canDecide, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<QualityInspectionWorkSummaryDto> StartInspectionWorkAsync(long id, StartQualityInspectionWorkRequest request, long actor, bool canExecute, bool canSupervise, bool canDecide, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<QualityInspectionWorkSummaryDto> PauseInspectionWorkAsync(long id, PauseQualityInspectionWorkRequest request, long actor, bool canExecute, bool canSupervise, bool canDecide, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<QualityInspectionPriorityResult> ToggleInspectionPriorityAsync(long id, long actor, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<QualityDecisionResult> DecideInspectionAsync(long id, DecideQualityInspectionRequest request, long actor, bool canReleaseQuarantine, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
