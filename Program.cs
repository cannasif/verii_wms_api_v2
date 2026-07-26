using Hangfire;
using Hangfire.Dashboard;
using Hangfire.SqlServer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Security.Claims;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using verii_wms_api_v2.Modules.Audit;
using verii_wms_api_v2.Modules.AccessControl;
using verii_wms_api_v2.Modules.BarcodeDesigner;
using verii_wms_api_v2.Modules.DocumentSeries;
using verii_wms_api_v2.Modules.ErpIntegration;
using verii_wms_api_v2.Modules.ErpMirror.Application;
using verii_wms_api_v2.Modules.ErpMirror.Infrastructure;
using verii_wms_api_v2.Modules.Identity;
using verii_wms_api_v2.Modules.Identity.Infrastructure;
using verii_wms_api_v2.Modules.Location;
using verii_wms_api_v2.Modules.Packing;
using verii_wms_api_v2.Modules.Packing.Application;
using verii_wms_api_v2.Modules.GoodsReceipt;
using verii_wms_api_v2.Modules.ProjectSettings;
using verii_wms_api_v2.Modules.Quality;
using verii_wms_api_v2.Modules.SerialNumberPolicy;
using verii_wms_api_v2.Modules.StockTracking;
using verii_wms_api_v2.Modules.NetsisRead;
using verii_wms_api_v2.Modules.Smtp;
using verii_wms_api_v2.Modules.StockMovement;
using verii_wms_api_v2.Modules.Shipping;
using verii_wms_api_v2.Modules.StockBalance;
using verii_wms_api_v2.Modules.StockBalance.Application;
using verii_wms_api_v2.Modules.SteelReceipt;
using verii_wms_api_v2.Modules.SystemManagement.Application;
using verii_wms_api_v2.Modules.SystemManagement;
using verii_wms_api_v2.Modules.VehicleCheckIn;
using verii_wms_api_v2.Modules.WarehouseTransfer;
using verii_wms_api_v2.Modules.WarehouseInbound;
using verii_wms_api_v2.Modules.WarehouseOutbound;
using verii_wms_api_v2.Shared.Infrastructure.Persistence;
using verii_wms_api_v2.Shared.Host.Middleware;
using verii_wms_api_v2.Shared.Host.Localization;
using verii_wms_api_v2.Shared.Host.Routing;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();
builder.Services.AddControllers(options => options.Conventions.Add(new IisSafeHttpMethodConvention()))
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddWmsLocalization();
var databaseConnection = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("ConnectionStrings:DefaultConnection is missing. Configure it through user-secrets or environment variables.");
builder.Services.AddDbContext<WmsDbContext>(options => options.UseSqlServer(databaseConnection));
builder.Services.AddNetsisReadModule();
builder.Services.AddDataProtection();
builder.Services.AddScoped<IErpMirrorService, ErpMirrorService>();
builder.Services.AddWmsPersistence();
builder.Services.AddIdentityModule();
builder.Services.AddBarcodeDesignerModule();
builder.Services.AddDocumentSeriesModule();
builder.Services.AddErpIntegrationModule(builder.Configuration);
builder.Services.AddLocationModule();
builder.Services.AddPackingModule();
builder.Services.AddGoodsReceiptModule();
builder.Services.AddSteelReceiptModule();
builder.Services.AddVehicleCheckInModule();
builder.Services.AddQualityModule();
builder.Services.AddSerialNumberPolicyModule();
builder.Services.AddStockTrackingModule();
builder.Services.AddProjectSettingsModule();
builder.Services.AddStockMovementModule();
builder.Services.AddStockBalanceModule();
builder.Services.AddWarehouseTransferModule();
builder.Services.AddWarehouseInboundModule();
builder.Services.AddWarehouseOutboundModule();
builder.Services.AddShippingModule();
builder.Services.AddSmtpModule();
builder.Services.AddAuditModule();
builder.Services.AddAccessControlModule();
builder.Services.AddSystemManagementModule();
builder.Services.AddHangfire(configuration => configuration.UseSqlServerStorage(databaseConnection, new SqlServerStorageOptions { PrepareSchemaIfNecessary = true, QueuePollInterval = TimeSpan.Zero }));
builder.Services.AddHangfireServer();
var jwtKey = builder.Configuration["JwtSettings:SecretKey"] ?? throw new InvalidOperationException("JwtSettings:SecretKey is missing.");
if (Encoding.UTF8.GetByteCount(jwtKey) < 32) throw new InvalidOperationException("JwtSettings:SecretKey must be at least 32 bytes.");
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = builder.Configuration["JwtSettings:Issuer"],
        ValidAudience = builder.Configuration["JwtSettings:Audience"],
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
        ClockSkew = TimeSpan.Zero
    };
    options.Events = new JwtBearerEvents
    {
        OnTokenValidated = async context =>
        {
            var userIdValue = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
            var tokenVersionValue = context.Principal?.FindFirstValue("tokenVersion");
            if (!long.TryParse(userIdValue, out var userId) || !int.TryParse(tokenVersionValue, out var tokenVersion))
            {
                context.Fail("Invalid token claims.");
                return;
            }

            var dbContext = context.HttpContext.RequestServices.GetRequiredService<WmsDbContext>();
            // Authentication must not fail merely because the browser navigated away
            // while this short session-version lookup was in flight. A request-aborted
            // token here surfaced as a JwtBearer authentication failure and caused the
            // web client to discard an otherwise valid month-long session.
            var valid = await dbContext.Users.AsNoTracking()
                .AnyAsync(x => x.Id == userId && x.IsActive && x.TokenVersion == tokenVersion, CancellationToken.None);
            if (!valid) context.Fail("Token session is no longer valid.");
        }
    };
});
builder.Services.AddAuthorizationBuilder().SetFallbackPolicy(new AuthorizationPolicyBuilder()
    .RequireAuthenticatedUser()
    .Build());
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("identity", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        }));
});
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
    .WithOrigins(builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? ["http://localhost:5173"])
    .AllowAnyHeader()
    .AllowAnyMethod()
    .AllowCredentials()));
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
    options.CustomSchemaIds(type => (type.FullName ?? type.Name).Replace('+', '.')));

var app = builder.Build();
app.UseWmsLocalization();
app.UseMiddleware<ExceptionHandlingMiddleware>();
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
else
{
    app.UseHsts();
    app.UseHttpsRedirection();
}
app.UseStaticFiles();
app.UseCors();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
if (app.Environment.IsDevelopment())
{
    app.UseHangfireDashboard("/hangfire", new DashboardOptions
    {
        Authorization = [new LocalRequestsOnlyAuthorizationFilter()]
    });
}
app.MapControllers();
var recurringJobs = app.Services.GetRequiredService<IRecurringJobManager>();
recurringJobs.AddOrUpdate<ITrackedErpMirrorJobRunner>("erp-warehouse-mirror-sync", service => service.RunWarehousesAsync(CancellationToken.None), Cron.Hourly);
recurringJobs.AddOrUpdate<ITrackedErpMirrorJobRunner>("erp-stock-mirror-sync", service => service.RunStocksAsync(CancellationToken.None), Cron.Hourly);
recurringJobs.AddOrUpdate<ITrackedErpMirrorJobRunner>("erp-customer-mirror-sync", service => service.RunCustomersAsync(CancellationToken.None), Cron.Hourly);
recurringJobs.RemoveIfExists("erp-yap-code-mirror-sync");
recurringJobs.AddOrUpdate<ITrackedErpMirrorJobRunner>("erp-configuration-code-mirror-sync", service => service.RunConfigurationCodesAsync(CancellationToken.None), Cron.Hourly);
recurringJobs.AddOrUpdate<IStockBalanceJobRunner>("stock-balance-reconciliation", service => service.ReconcileAndRepairAsync(CancellationToken.None), Cron.Daily(2, 30));
recurringJobs.AddOrUpdate<IPackingPrintQueueJobRunner>("packing-print-queue", service => service.DispatchPendingAsync(CancellationToken.None), Cron.Minutely);
app.Run();
