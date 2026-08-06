using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using verii_wms_api_v2.Modules.Customer.Domain;
using verii_wms_api_v2.Modules.Identity.Infrastructure;
using verii_wms_api_v2.Modules.Kkd.Application;
using verii_wms_api_v2.Modules.Kkd.Domain;
using verii_wms_api_v2.Modules.Stock.Domain;
using verii_wms_api_v2.Shared.Infrastructure.Persistence;
using Xunit;

namespace verii_wms_api_v2.QueryTests;

public sealed class KkdMatrixBulkValidationTests
{
    [Fact]
    public async Task Three_thousand_stock_rules_are_validated_in_one_request()
    {
        var options = new DbContextOptionsBuilder<WmsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        await using var db = new WmsDbContext(options);
        var department = new KkdDepartment { BranchCode = "0", Code = "URETIM", Name = "Üretim", IsActive = true };
        var role = new KkdRole { BranchCode = "0", Department = department, Code = "OP", Name = "Operatör", IsActive = true };
        var customer = new Customer { BranchCode = "0", CustomerCode = "C-1", CustomerName = "Test Cari" };
        var stocks = Enumerable.Range(1, 3000).Select(index => new Stock
        {
            BranchCode = "0", ErpStockCode = $"KKD-{index:00000}", StockName = $"KKD {index}", GroupCode = "KKD", BaseUnitCode = "ADET"
        }).ToArray();
        db.AddRange(department, role, customer);
        db.AddRange(stocks);
        await db.SaveChangesAsync();

        var rules = stocks.Select((stock, index) => new KkdRuleUpsertRequest(
            "KKD", "Kişisel Koruyucu Donanım", stock.Id, null, null, 1, 1, 0,
            true, true, index + 1, true, null,
            [new KkdPhaseUpsertRequest("Initial", 0, 1, true, null, null, null, null, 1)])).ToArray();
        var request = new KkdMatrixUpsertRequest(customer.Id, department.Id, role.Id, "M-3000", "Toplu Matris",
            null, null, true, null, rules);
        var service = new KkdDefinitionService(new UnitOfWork(db, new HttpContextAccessor()));

        var result = await service.ValidateMatrixAsync(null, request);

        Assert.True(result.IsValid);
        Assert.Equal(3000, result.RuleCount);
        Assert.Equal(3000, result.StockSpecificRuleCount);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public async Task Duplicate_rows_return_row_level_error_without_writing()
    {
        var options = new DbContextOptionsBuilder<WmsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        await using var db = new WmsDbContext(options);
        var department = new KkdDepartment { BranchCode = "0", Code = "URETIM", Name = "Üretim", IsActive = true };
        var role = new KkdRole { BranchCode = "0", Department = department, Code = "OP", Name = "Operatör", IsActive = true };
        var customer = new Customer { BranchCode = "0", CustomerCode = "C-1", CustomerName = "Test Cari" };
        var stock = new Stock { BranchCode = "0", ErpStockCode = "KKD-1", StockName = "Baret", GroupCode = "KKD" };
        db.AddRange(department, role, customer, stock);
        await db.SaveChangesAsync();
        var phase = new KkdPhaseUpsertRequest("Initial", 0, 1, true, null, null, null, null, 1);
        var rule = new KkdRuleUpsertRequest("KKD", "KKD", stock.Id, null, null, 1, 1, 0, true, true, 1, true, null, [phase]);
        var request = new KkdMatrixUpsertRequest(customer.Id, department.Id, role.Id, "DUP", "Duplicate", null, null, true, null, [rule, rule]);
        var service = new KkdDefinitionService(new UnitOfWork(db, new HttpContextAccessor()));

        var result = await service.ValidateMatrixAsync(null, request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, issue => issue.RowNumber == 2 && issue.Code == "DUPLICATE");
        Assert.Empty(db.KkdEntitlementMatrices);
    }
}
