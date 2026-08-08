using Hangfire;
using Hangfire.Dashboard;
using Hangfire.SqlServer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Threading.RateLimiting;
using verii_wms_api_v2.Modules.Audit;
using verii_wms_api_v2.Modules.AccessControl;
using verii_wms_api_v2.Modules.BarcodeDesigner;
using verii_wms_api_v2.Modules.Dashboard;
using verii_wms_api_v2.Modules.DocumentSeries;
using verii_wms_api_v2.Modules.ErpIntegration;
using verii_wms_api_v2.Modules.ErpMirror.Application;
using verii_wms_api_v2.Modules.ErpMirror.Infrastructure;
using verii_wms_api_v2.Modules.Identity;
using verii_wms_api_v2.Modules.Identity.Application;
using verii_wms_api_v2.Modules.Identity.Infrastructure;
using verii_wms_api_v2.Modules.IncomingInvoice;
using verii_wms_api_v2.Modules.Kkd;
using verii_wms_api_v2.Modules.Location;
using verii_wms_api_v2.Modules.Packing;
using verii_wms_api_v2.Modules.Packing.Application;
using verii_wms_api_v2.Modules.GoodsReceipt;
using verii_wms_api_v2.Modules.ProjectSettings;
using verii_wms_api_v2.Modules.Procurement;
using verii_wms_api_v2.Modules.Production;
using verii_wms_api_v2.Modules.ProductionTransfer;
using verii_wms_api_v2.Modules.Quality;
using verii_wms_api_v2.Modules.SerialNumberPolicy;
using verii_wms_api_v2.Modules.StockTracking;
using verii_wms_api_v2.Modules.Stock;
using verii_wms_api_v2.Modules.NetsisRead;
using verii_wms_api_v2.Modules.Smtp;
using verii_wms_api_v2.Modules.StockMovement;
using verii_wms_api_v2.Modules.Shipping;
using verii_wms_api_v2.Modules.StockBalance;
using verii_wms_api_v2.Modules.StockBalance.Application;
using verii_wms_api_v2.Modules.SubcontractingTransfer;
using verii_wms_api_v2.Modules.SteelReceipt;
using verii_wms_api_v2.Modules.SystemManagement.Application;
using verii_wms_api_v2.Modules.SystemManagement;
using verii_wms_api_v2.Modules.VehicleCheckIn;
using verii_wms_api_v2.Modules.WarehouseTransfer;
using verii_wms_api_v2.Modules.WarehouseInbound;
using verii_wms_api_v2.Modules.WarehouseOutbound;
using verii_wms_api_v2.Modules.WarehouseAssistant;
using verii_wms_api_v2.Shared.Infrastructure.Persistence;
using verii_wms_api_v2.Shared;
using verii_wms_api_v2.Shared.Host.BackgroundJobs;
using verii_wms_api_v2.Shared.Host.Filters;
using verii_wms_api_v2.Shared.Host.Middleware;
using verii_wms_api_v2.Shared.Host.Localization;
using verii_wms_api_v2.Shared.Host.Routing;
using verii_wms_api_v2.Shared.Host.Serialization;
using verii_wms_api_v2.Shared.Host.Startup;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();
builder.Services.AddControllers(options =>
    {
        options.Conventions.Add(new IisSafeHttpMethodConvention());
        options.Filters.Add<AuthenticatedBranchScopeFilter>();
    })
    .AddJsonOptions(options => WmsJsonSerialization.Configure(options.JsonSerializerOptions));
builder.Services.ConfigureHttpJsonOptions(options =>
    WmsJsonSerialization.Configure(options.SerializerOptions));
builder.Services.AddWmsLocalization();
builder.Services.Configure<ApiBehaviorOptions>(options =>
    options.InvalidModelStateResponseFactory = WmsApiValidationResponseFactory.Create);
var databaseConnection = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("ConnectionStrings:DefaultConnection is missing. Configure it through user-secrets or environment variables.");
builder.Services.AddDbContextPool<WmsDbContext>(
    options => options.UseSqlServer(databaseConnection),
    poolSize: Math.Clamp(builder.Configuration.GetValue("Performance:DbContextPoolSize", 128), 16, 1024));
builder.Services.AddNetsisReadModule();
var dataProtectionPathSetting = builder.Configuration["DataProtection:KeyRingPath"];
var dataProtectionPath = Path.IsPathRooted(dataProtectionPathSetting)
    ? dataProtectionPathSetting
    : Path.Combine(
        builder.Environment.ContentRootPath,
        string.IsNullOrWhiteSpace(dataProtectionPathSetting)
            ? Path.Combine("App_Data", "DataProtection-Keys")
            : dataProtectionPathSetting);
Directory.CreateDirectory(dataProtectionPath);
var dataProtection = builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionPath))
    .SetApplicationName(
        builder.Configuration["DataProtection:ApplicationName"] ?? "V3RII-WMS-V2");
if (OperatingSystem.IsWindows())
    dataProtection.ProtectKeysWithDpapi(protectToLocalMachine: true);
builder.Services.AddScoped<IErpMirrorService, ErpMirrorService>();
builder.Services.AddWmsPersistence();
builder.Services.AddIdentityModule();
builder.Services.AddBarcodeDesignerModule();
builder.Services.AddDashboardModule();
builder.Services.AddDocumentSeriesModule();
builder.Services.AddErpIntegrationModule(builder.Configuration);
builder.Services.AddLocationModule();
builder.Services.AddPackingModule();
builder.Services.AddGoodsReceiptModule();
builder.Services.AddIncomingInvoiceModule(builder.Configuration);
builder.Services.AddKkdModule();
builder.Services.AddSteelReceiptModule();
builder.Services.AddVehicleCheckInModule();
builder.Services.AddQualityModule();
builder.Services.AddSerialNumberPolicyModule();
builder.Services.AddStockTrackingModule();
builder.Services.AddStockModule();
builder.Services.AddProjectSettingsModule();
builder.Services.AddStockMovementModule();
builder.Services.AddStockBalanceModule();
builder.Services.AddWarehouseTransferModule();
builder.Services.AddProductionModule();
builder.Services.AddProcurementModule();
builder.Services.AddProductionTransferModule();
builder.Services.AddSubcontractingTransferModule();
builder.Services.AddWarehouseInboundModule();
builder.Services.AddWarehouseOutboundModule();
builder.Services.AddShippingModule();
builder.Services.AddWarehouseAssistantModule(builder.Configuration);
builder.Services.AddSmtpModule();
builder.Services.AddAuditModule();
builder.Services.AddAccessControlModule();
builder.Services.AddSystemManagementModule();
builder.Services.AddHangfire(configuration => configuration.UseSqlServerStorage(
    databaseConnection,
    new SqlServerStorageOptions
    {
        PrepareSchemaIfNecessary = builder.Configuration.GetValue("Hangfire:PrepareSchemaIfNecessary", false),
        TryAutoDetectSchemaDependentOptions = false,
        CommandBatchMaxTimeout = TimeSpan.FromMinutes(5),
        SlidingInvisibilityTimeout = TimeSpan.FromMinutes(5),
        QueuePollInterval = TimeSpan.Zero,
        UseRecommendedIsolationLevel = true
    }));
if (builder.Configuration.GetValue("Hangfire:EnableServer", true))
{
    builder.Services.AddHangfireServer();
}
builder.Services.AddHostedService<RecurringJobRegistrationHostedService>();
builder.Services.AddSingleton<StartupReadinessState>();
builder.Services.AddHostedService<StartupWarmupHostedService>();
builder.Services.AddHealthChecks()
    .AddCheck(
        "self",
        () => Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy(),
        tags: ["live"])
    .AddCheck<StartupReadinessHealthCheck>("startup", tags: ["ready"]);
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
        OnChallenge = async context =>
        {
            if (context.Response.HasStarted) return;
            context.HandleResponse();
            var resolver = context.HttpContext.RequestServices.GetRequiredService<WmsApiMessageResolver>();
            var message = resolver.Resolve(StatusCodes.Status401Unauthorized, null, false);
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.ContentType = "application/json; charset=utf-8";
            await context.Response.WriteAsync(JsonSerializer.Serialize(
                ApiResponse<object>.Error(message.Text, context.HttpContext.TraceIdentifier, message.Code),
                WmsJsonSerialization.ResponseOptions));
        },
        OnForbidden = async context =>
        {
            if (context.Response.HasStarted) return;
            var resolver = context.HttpContext.RequestServices.GetRequiredService<WmsApiMessageResolver>();
            var message = resolver.Resolve(StatusCodes.Status403Forbidden, null, false);
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            context.Response.ContentType = "application/json; charset=utf-8";
            await context.Response.WriteAsync(JsonSerializer.Serialize(
                ApiResponse<object>.Error(message.Text, context.HttpContext.TraceIdentifier, message.Code),
                WmsJsonSerialization.ResponseOptions));
        },
        OnTokenValidated = async context =>
        {
            var userIdValue = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
            var tokenVersionValue = context.Principal?.FindFirstValue("tokenVersion");
            var branchCode = context.Principal?.FindFirstValue(JwtTokenIssuer.BranchCodeClaim);
            if (!long.TryParse(userIdValue, out var userId)
                || !int.TryParse(tokenVersionValue, out var tokenVersion)
                || string.IsNullOrWhiteSpace(branchCode))
            {
                context.Fail("Invalid token claims.");
                return;
            }

            var sessionValidator = context.HttpContext.RequestServices.GetRequiredService<IIdentitySessionValidator>();
            var valid = await sessionValidator.IsValidAsync(userId, tokenVersion);
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
    options.OnRejected = static async (context, cancellationToken) =>
    {
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            context.HttpContext.Response.Headers.RetryAfter =
                Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds)).ToString();
        }
        await ApiStatusCodeResponseWriter.WriteAsync(context.HttpContext, cancellationToken);
    };
    options.AddPolicy("identity-sensitive", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        }));
    options.AddPolicy("identity-refresh", context =>
    {
        var ipAddress = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var userAgentHash = StringComparer.Ordinal.GetHashCode(context.Request.Headers.UserAgent.ToString());
        return RateLimitPartition.GetFixedWindowLimiter(
            $"{ipAddress}:{userAgentHash}",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 60,
                Window = TimeSpan.FromMinutes(1),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 1,
                AutoReplenishment = true
            });
    });
    options.AddPolicy("supplier-portal",context=>RateLimitPartition.GetFixedWindowLimiter(
        $"{context.Connection.RemoteIpAddress}:{context.Request.RouteValues["token"]}",
        _=>new FixedWindowRateLimiterOptions{PermitLimit=30,Window=TimeSpan.FromMinutes(1),QueueLimit=0,AutoReplenishment=true}));
    options.AddPolicy("warehouse-assistant", context =>
    {
        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "anonymous";
        return RateLimitPartition.GetFixedWindowLimiter(
            userId,
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 30,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 2,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                AutoReplenishment = true
            });
    });
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
app.UseMiddleware<ApiResponseLocalizationMiddleware>();
app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseStatusCodePages(statusCodeContext =>
    ApiStatusCodeResponseWriter.WriteAsync(statusCodeContext.HttpContext));
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
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("live")
}).AllowAnonymous();
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("ready")
}).AllowAnonymous();
app.Run();
