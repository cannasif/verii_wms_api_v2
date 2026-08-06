using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using verii_wms_api_v2.Modules.GoodsReceipt.Application;
using verii_wms_api_v2.Modules.GoodsReceipt.Domain;
using verii_wms_api_v2.Modules.Identity.Infrastructure;
using verii_wms_api_v2.Modules.Quality.Application;
using verii_wms_api_v2.Modules.Quality.Domain;
using verii_wms_api_v2.Modules.Stock.Domain;
using verii_wms_api_v2.Modules.WarehouseOperations.Domain;
using verii_wms_api_v2.Shared.Infrastructure.Persistence;
using Xunit;

namespace verii_wms_api_v2.QueryTests;

public sealed class QualityRuleResolutionTests
{
    [Fact]
    public async Task Stock_rule_has_priority_over_the_stocks_group_rule()
    {
        await using var db = CreateDbContext();
        var stock = NewStock("STOK4", "02");
        db.Stocks.Add(stock);
        await db.SaveChangesAsync();
        db.QualityRules.AddRange(
            NewGroupRule("02", QualityInspectionMode.QuickCheck),
            NewStockRule(stock.Id, QualityInspectionMode.InspectionRequired));
        await db.SaveChangesAsync();
        var service = CreateService(db);

        var resolved = await service.ResolveAsync("0", stock.Id, stock.GroupCode);

        Assert.Equal("StockRule", resolved.Source);
        Assert.Equal(QualityInspectionMode.InspectionRequired, resolved.InspectionMode);
    }

    [Fact]
    public async Task Group_rule_is_used_only_when_the_stock_has_no_active_stock_rule()
    {
        await using var db = CreateDbContext();
        var stockOne = NewStock("STOK1", "01");
        var stockTwo = NewStock("STOK2", "01");
        var stockThree = NewStock("STOK3", "02");
        var stockFour = NewStock("STOK4", "02");
        db.Stocks.AddRange(stockOne, stockTwo, stockThree, stockFour);
        await db.SaveChangesAsync();
        db.QualityRules.AddRange(
            NewStockRule(stockFour.Id, QualityInspectionMode.InspectionRequired),
            NewGroupRule("02", QualityInspectionMode.QuickCheck));
        await db.SaveChangesAsync();
        var service = CreateService(db);

        var resolvedOne = await service.ResolveAsync("0", stockOne.Id, stockOne.GroupCode);
        var resolvedTwo = await service.ResolveAsync("0", stockTwo.Id, stockTwo.GroupCode);
        var resolvedThree = await service.ResolveAsync("0", stockThree.Id, stockThree.GroupCode);
        var resolvedFour = await service.ResolveAsync("0", stockFour.Id, stockFour.GroupCode);

        Assert.Equal("NoRule", resolvedOne.Source);
        Assert.Equal(QualityInspectionMode.NoCheck, resolvedOne.InspectionMode);
        Assert.Equal("NoRule", resolvedTwo.Source);
        Assert.Equal(QualityInspectionMode.NoCheck, resolvedTwo.InspectionMode);
        Assert.Equal("StockGroupRule", resolvedThree.Source);
        Assert.Equal(QualityInspectionMode.QuickCheck, resolvedThree.InspectionMode);
        Assert.Equal("StockRule", resolvedFour.Source);
        Assert.Equal(QualityInspectionMode.InspectionRequired, resolvedFour.InspectionMode);
    }

    [Fact]
    public async Task Missing_rule_never_falls_back_to_global_default_inspection_mode()
    {
        await using var db = CreateDbContext();
        var stock = NewStock("STOK1", "01");
        db.Stocks.Add(stock);
        db.QualityParameters.Add(new QualityParameter
        {
            BranchCode = "0",
            ParameterKey = "DEFAULT",
            DefaultInspectionMode = QualityInspectionMode.InspectionRequired
        });
        await db.SaveChangesAsync();
        var service = CreateService(db);

        var resolved = await service.ResolveAsync("0", stock.Id, stock.GroupCode);

        Assert.Equal("NoRule", resolved.Source);
        Assert.Null(resolved.RuleId);
        Assert.Equal(QualityInspectionMode.NoCheck, resolved.InspectionMode);
    }

    [Fact]
    public async Task Group_matching_is_case_and_whitespace_insensitive_but_branch_isolated()
    {
        await using var db = CreateDbContext();
        var stock = NewStock("STOK3", " 02 ");
        db.Stocks.Add(stock);
        db.QualityRules.AddRange(
            NewGroupRule("02", QualityInspectionMode.QuickCheck, branch: "0"),
            NewGroupRule("02", QualityInspectionMode.InspectionRequired, branch: "1"));
        await db.SaveChangesAsync();
        var service = CreateService(db);

        var resolved = await service.ResolveAsync("0", stock.Id, stock.GroupCode);

        Assert.Equal("StockGroupRule", resolved.Source);
        Assert.Equal(QualityInspectionMode.QuickCheck, resolved.InspectionMode);
    }

    [Fact]
    public void Only_matching_lines_are_marked_for_quality_and_the_receipt_waits_if_any_match_exists()
    {
        var noRule = Policy("NoRule", QualityInspectionMode.NoCheck);
        var groupRule = Policy("StockGroupRule", QualityInspectionMode.QuickCheck);
        var stockRule = Policy("StockRule", QualityInspectionMode.InspectionRequired);

        var lineRequirements = new[]
        {
            GoodsReceiptOperationsService.RequiresQualityForLine(false, noRule),
            GoodsReceiptOperationsService.RequiresQualityForLine(false, noRule),
            GoodsReceiptOperationsService.RequiresQualityForLine(false, groupRule),
            GoodsReceiptOperationsService.RequiresQualityForLine(false, stockRule)
        };

        Assert.Equal([false, false, true, true], lineRequirements);
        Assert.True(GoodsReceiptOperationsService.RequiresQuality(false, lineRequirements.Any(x => x)));
        Assert.False(GoodsReceiptOperationsService.RequiresQuality(true, lineRequirements.Any(x => x)));
        Assert.Equal("SendToQuality", GoodsReceiptOperationsService.ResolveNextAction(lineRequirements.Any(x => x)));
        Assert.Equal("CreateWaybill", GoodsReceiptOperationsService.ResolveNextAction(false));
    }

    [Fact]
    public void ForceQualityControl_overrides_a_no_check_policy_but_not_prior_approval()
    {
        var noRule = Policy("NoRule", QualityInspectionMode.NoCheck);
        var stockRule = Policy("StockRule", QualityInspectionMode.InspectionRequired);

        Assert.True(GoodsReceiptOperationsService.RequiresQualityForLine(false, noRule, forceQualityControl: true));
        Assert.False(GoodsReceiptOperationsService.RequiresQualityForLine(false, noRule, forceQualityControl: false));
        Assert.True(GoodsReceiptOperationsService.RequiresQualityForLine(false, stockRule, forceQualityControl: false));
        Assert.False(GoodsReceiptOperationsService.RequiresQualityForLine(true, noRule, forceQualityControl: true),
            "Already-approved quality must not be re-forced back on.");

        Assert.True(GoodsReceiptOperationsService.RequiresQuality(false, false, forceQualityControl: true));
        Assert.False(GoodsReceiptOperationsService.RequiresQuality(false, false, forceQualityControl: false));
        Assert.False(GoodsReceiptOperationsService.RequiresQuality(true, false, forceQualityControl: true),
            "Already-approved quality must not be re-forced back on.");
    }

    [Fact]
    public void Manual_quality_always_holds_inventory_even_when_general_hold_is_disabled()
    {
        var header = new GoodsReceiptHeader { HoldInventoryUntilQualityDecision = false };
        var line = new GoodsReceiptLine
        {
            RequireQualityControl = true,
            QualityRoutingSource = GoodsReceiptQualityRoutingSource.ManualReceipt
        };

        Assert.True(GoodsReceiptOperationsService.ShouldHoldInventoryForQuality(line, header));
    }

    [Fact]
    public void Receipt_waits_until_every_matching_quality_line_has_a_final_decision()
    {
        var partiallyDecided = QualityService.ResolveDecisionState(
            [QualityDecision.Accepted, QualityDecision.Pending],
            releasesQuarantine: false);

        Assert.False(partiallyDecided.IsTerminal);
        Assert.Equal(QualityInspectionStatus.PartiallyDecided, partiallyDecided.InspectionStatus);
        Assert.Equal(OperationQualityStatus.PartiallyCompleted, partiallyDecided.ReceiptStatus);

        var fullyAccepted = QualityService.ResolveDecisionState(
            [QualityDecision.Accepted, QualityDecision.Accepted],
            releasesQuarantine: false);

        Assert.True(fullyAccepted.IsTerminal);
        Assert.Equal(QualityInspectionStatus.Passed, fullyAccepted.InspectionStatus);
        Assert.Equal(OperationQualityStatus.Passed, fullyAccepted.ReceiptStatus);
    }

    [Fact]
    public void Rejected_matching_line_is_a_final_quality_decision_for_the_receipt()
    {
        var decided = QualityService.ResolveDecisionState(
            [QualityDecision.Accepted, QualityDecision.Rejected],
            releasesQuarantine: false);

        Assert.True(decided.IsTerminal);
        Assert.Equal(QualityInspectionStatus.Failed, decided.InspectionStatus);
        Assert.Equal(OperationQualityStatus.Failed, decided.ReceiptStatus);
        Assert.True(GoodsReceiptRoutingService.CanRouteAfterQuality(decided.ReceiptStatus));
        Assert.False(GoodsReceiptRoutingService.CanRouteAfterQuality(OperationQualityStatus.PartiallyCompleted));
    }

    private static QualityService CreateService(WmsDbContext db) =>
        new(new UnitOfWork(db, new HttpContextAccessor()), null!, null!, null!, null!, null!);

    private static WmsDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<WmsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new WmsDbContext(options);
    }

    private static Stock NewStock(string code, string group) =>
        new() { BranchCode = "0", ErpStockCode = code, StockName = code, GroupCode = group };

    private static QualityRule NewStockRule(long stockId, QualityInspectionMode mode) =>
        new()
        {
            BranchCode = "0",
            ScopeType = QualityRuleScopeTypes.Stock,
            StockId = stockId,
            InspectionMode = mode,
            IsActive = true
        };

    private static QualityRule NewGroupRule(
        string group,
        QualityInspectionMode mode,
        string branch = "0") =>
        new()
        {
            BranchCode = branch,
            ScopeType = QualityRuleScopeTypes.StockGroup,
            StockGroupCode = group,
            InspectionMode = mode,
            IsActive = true
        };

    private static ResolvedQualityPolicy Policy(string source, QualityInspectionMode mode) =>
        new(source, null, mode, QualitySamplingMode.All, 100,
            QualityFailAction.Quarantine, false, false, false, false, null,
            true, true, true);
}
