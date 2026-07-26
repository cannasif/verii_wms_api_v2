using Microsoft.Extensions.Options;
using verii_wms_api_v2.Modules.ErpIntegration.Application;
using verii_wms_api_v2.Modules.ErpIntegration.Infrastructure;

namespace verii_wms_api_v2.Modules.ErpIntegration;

public static class ErpIntegrationModule
{
    public static IServiceCollection AddErpIntegrationModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddMemoryCache();
        services.Configure<NetsisOptions>(configuration.GetSection(NetsisOptions.SectionName));
        services.PostConfigure<NetsisOptions>(options => ApplyLegacyOptions(options, configuration));

        services.AddHttpClient<INetsisTokenService, NetsisTokenService>(ConfigureClient)
            .ConfigurePrimaryHttpMessageHandler(BuildHandler);
        services.AddHttpClient<INetsisRestClient, NetsisRestClient>(ConfigureClient)
            .ConfigurePrimaryHttpMessageHandler(BuildHandler);
        services.AddScoped<IErpPostingService, ErpPostingService>();
        services.AddScoped<IErpCancellationService, ErpCancellationService>();
        services.AddScoped<IOperationCancellationCoordinator, OperationCancellationCoordinator>();
        return services;
    }

    private static void ConfigureClient(IServiceProvider serviceProvider, HttpClient client)
    {
        var options = serviceProvider.GetRequiredService<IOptions<NetsisOptions>>().Value.Rest;
        if (!string.IsNullOrWhiteSpace(options.BaseUrl))
            client.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/", UriKind.Absolute);
        client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds is > 0 and <= 300
            ? options.TimeoutSeconds
            : 30);
    }

    private static HttpMessageHandler BuildHandler(IServiceProvider serviceProvider)
    {
        var options = serviceProvider.GetRequiredService<IOptions<NetsisOptions>>().Value.Rest;
        return new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = options.AllowInvalidSslCertificate
                ? HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
                : null
        };
    }

    private static void ApplyLegacyOptions(NetsisOptions options, IConfiguration configuration)
    {
        var legacy = configuration.GetSection("NetsisRest");
        if (!legacy.Exists()) return;
        options.Enabled |= legacy.GetValue<bool>("Enabled");
        options.Rest.BaseUrl = Prefer(options.Rest.BaseUrl, legacy["BaseUrl"]);
        options.Rest.LoginPath = Prefer(options.Rest.LoginPath, legacy["LoginPath"]);
        options.Rest.ItemSlipsPath = Prefer(options.Rest.ItemSlipsPath,
            legacy["ItemSlipsPath"] ?? legacy["SalesInvoicePath"]);
        options.Rest.Username = Prefer(options.Rest.Username, legacy["Username"]);
        options.Rest.Password = Prefer(options.Rest.Password, legacy["Password"]);
        options.Rest.BranchCode = Prefer(options.Rest.BranchCode, legacy["BranchCode"]);
        options.Rest.DbName = Prefer(options.Rest.DbName, legacy["DbName"] ?? legacy["Database"]);
        options.Rest.DbUser = Prefer(options.Rest.DbUser, legacy["DbUser"]);
        options.Rest.DbPassword = Prefer(options.Rest.DbPassword, legacy["DbPassword"]);
        options.Rest.DbType = Prefer(options.Rest.DbType, legacy["DbType"]);
        options.Rest.TimeoutSeconds = legacy.GetValue<int?>("TimeoutSeconds") ?? options.Rest.TimeoutSeconds;
        options.Rest.AllowInvalidSslCertificate |= legacy.GetValue<bool>("AllowInvalidSslCertificate");
        options.Rest.GoodsReceiptDocumentType =
            legacy.GetValue<int?>("GoodsReceiptDocumentType")
            ?? legacy.GetValue<int?>("PurchaseDispatchDocumentType")
            ?? options.Rest.GoodsReceiptDocumentType;
        options.Rest.WarehouseTransferDocumentType =
            legacy.GetValue<int?>("WarehouseTransferDocumentType")
            ?? options.Rest.WarehouseTransferDocumentType;
        options.Rest.ShipmentDocumentType =
            legacy.GetValue<int?>("ShipmentDocumentType")
            ?? options.Rest.ShipmentDocumentType;
    }

    private static string Prefer(string current, string? fallback) =>
        string.IsNullOrWhiteSpace(current) ? fallback?.Trim() ?? string.Empty : current;
}
