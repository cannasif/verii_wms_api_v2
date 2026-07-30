using verii_wms_api_v2.Modules.GoodsReceipt.Application;
using verii_wms_api_v2.Modules.GoodsReceipt.Infrastructure;

namespace verii_wms_api_v2.Modules.GoodsReceipt;

public static class GoodsReceiptModule
{
    public static IServiceCollection AddGoodsReceiptModule(this IServiceCollection services) => services
        .AddScoped<IGoodsReceiptService, GoodsReceiptService>()
        .AddScoped<IGoodsReceiptOperationsService, GoodsReceiptOperationsService>()
        .AddScoped<IGoodsReceiptTaskService, GoodsReceiptTaskService>()
        .AddScoped<IGoodsReceiptLabelService, GoodsReceiptLabelService>()
        .AddScoped<IGoodsReceiptOnReceiptLabelService, GoodsReceiptOnReceiptLabelService>()
        .AddScoped<IGoodsReceiptExecutionService, GoodsReceiptExecutionService>()
        .AddScoped<IGoodsReceiptLifecycleService, GoodsReceiptLifecycleService>()
        .AddScoped<IGoodsReceiptRoutingService, GoodsReceiptRoutingService>()
        .AddScoped<IGoodsReceiptPolicyService, GoodsReceiptPolicyService>()
        .AddScoped<ISupplierStockMappingService, SupplierStockMappingService>()
        .AddScoped<IGoodsReceiptOrderSource, SqlGoodsReceiptOrderSource>();
}
