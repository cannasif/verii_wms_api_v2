using verii_wms_api_v2.Modules.StockBalance.Application;

namespace verii_wms_api_v2.Modules.StockBalance;

public static class StockBalanceModule
{
    public static IServiceCollection AddStockBalanceModule(this IServiceCollection services) => services
        .AddScoped<IStockBalanceService, StockBalanceService>()
        .AddScoped<IOpeningBalanceImportService, OpeningBalanceImportService>()
        .AddScoped<IWarehouseOpeningImportService, WarehouseOpeningImportService>()
        .AddScoped<IStockBalanceJobRunner, StockBalanceJobRunner>();
}
