using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using verii_wms_api_v2.Modules.ErpIntegration.Application;
using verii_wms_api_v2.Shared.Application.Exceptions;

namespace verii_wms_api_v2.Modules.ErpIntegration.Infrastructure;

public sealed class NetsisTokenService(
    HttpClient httpClient,
    IMemoryCache cache,
    IOptions<NetsisOptions> optionsAccessor,
    ILogger<NetsisTokenService> logger) : INetsisTokenService
{
    private const string CacheKey = "netsis:rest:access-token";
    private static readonly SemaphoreSlim TokenLock = new(1, 1);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<string> GetAccessTokenAsync(bool forceRefresh, CancellationToken cancellationToken)
    {
        var options = optionsAccessor.Value;
        Validate(options);
        if (!forceRefresh && TryReadCached(options, out var cached)) return cached!;

        await TokenLock.WaitAsync(cancellationToken);
        try
        {
            if (!forceRefresh && TryReadCached(options, out cached)) return cached!;

            var failures = new List<string>();
            foreach (var requestFactory in BuildLoginAttempts(options))
            {
                try
                {
                    using var request = requestFactory();
                    using var response = await httpClient.SendAsync(request, cancellationToken);
                    var raw = await response.Content.ReadAsStringAsync(cancellationToken);
                    if (!response.IsSuccessStatusCode)
                    {
                        failures.Add($"{(int)response.StatusCode}: {Sanitize(raw)}");
                        continue;
                    }

                    var token = ParseToken(raw, options.Rest.DefaultTokenLifetimeMinutes);
                    if (string.IsNullOrWhiteSpace(token.AccessToken))
                    {
                        failures.Add("Token yanıtında access_token bulunamadı.");
                        continue;
                    }

                    var lifetime = TimeSpan.FromSeconds(Math.Max(60,
                        token.ExpiresInSeconds - Math.Max(0, options.Rest.TokenExpirySkewSeconds)));
                    cache.Set(CacheKey, token.AccessToken, lifetime);
                    return token.AccessToken;
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    failures.Add("Netsis token isteği zaman aşımına uğradı.");
                }
                catch (HttpRequestException ex)
                {
                    failures.Add(Sanitize(ex.Message));
                }
            }

            logger.LogError("Netsis token alınamadı. DenemeSayısı={AttemptCount}", failures.Count);
            throw AppException.BadRequest(
                $"Netsis REST oturumu açılamadı. Bağlantı, şirket ve kullanıcı bilgilerini kontrol edin. {string.Join(" | ", failures.Take(4))}");
        }
        finally
        {
            TokenLock.Release();
        }
    }

    private bool TryReadCached(NetsisOptions options, out string? token)
    {
        token = cache.Get<string>(CacheKey);
        return options.Enabled && !string.IsNullOrWhiteSpace(token);
    }

    private static IReadOnlyList<Func<HttpRequestMessage>> BuildLoginAttempts(NetsisOptions options)
    {
        var rest = options.Rest;
        var paths = new[] { rest.LoginPath, "/token", "/api/v2/token" }
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(NormalizePath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var attempts = new List<Func<HttpRequestMessage>>();

        foreach (var path in paths)
        {
            attempts.Add(() =>
            {
                var fields = new Dictionary<string, string>
                {
                    ["grant_type"] = "password",
                    ["branchcode"] = rest.BranchCode,
                    ["username"] = rest.Username,
                    ["password"] = rest.Password,
                    ["dbname"] = rest.DbName,
                    ["dbuser"] = rest.DbUser,
                    ["dbpassword"] = rest.DbPassword,
                    ["dbtype"] = NormalizeOAuthDbType(rest.DbType)
                };
                return NewRequest(path, new FormUrlEncodedContent(fields));
            });

            attempts.Add(() =>
            {
                var payload = new
                {
                    BranchCode = int.TryParse(rest.BranchCode, out var branch) ? branch : 0,
                    NetsisUser = rest.Username,
                    NetsisPassword = rest.Password,
                    DbName = rest.DbName,
                    DbUser = rest.DbUser,
                    DbPassword = rest.DbPassword,
                    DbType = NormalizeNetOpenXDbType(rest.DbType)
                };
                return NewRequest(path,
                    new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"));
            });
        }

        return attempts;
    }

    private static HttpRequestMessage NewRequest(string path, HttpContent content)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = content };
        request.Headers.Accept.ParseAdd("application/json");
        return request;
    }

    private static ParsedToken ParseToken(string raw, int fallbackLifetimeMinutes)
    {
        using var document = JsonDocument.Parse(raw);
        var root = document.RootElement;
        foreach (var wrapper in new[] { "data", "Data", "result", "Result" })
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty(wrapper, out var nested))
                root = nested;

        if (root.ValueKind == JsonValueKind.String)
            return new(root.GetString() ?? string.Empty, Math.Max(1, fallbackLifetimeMinutes) * 60);

        var accessToken = ReadString(root, "access_token", "accessToken", "AccessToken", "token", "Token");
        var expiresIn = ReadInt(root, "expires_in", "expiresIn", "ExpiresIn");
        return new(accessToken ?? string.Empty,
            expiresIn > 0 ? expiresIn : Math.Max(1, fallbackLifetimeMinutes) * 60);
    }

    private static string? ReadString(JsonElement root, params string[] names)
    {
        foreach (var name in names)
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out var value))
                return value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
        return null;
    }

    private static int ReadInt(JsonElement root, params string[] names)
    {
        var value = ReadString(root, names);
        return int.TryParse(value, out var result) ? result : 0;
    }

    private static string NormalizePath(string value)
    {
        var path = value.Trim();
        return path.StartsWith('/') ? path : "/" + path;
    }

    private static string NormalizeOAuthDbType(string value) =>
        value.Equals("MSSQL", StringComparison.OrdinalIgnoreCase)
        || value.Equals("vtMSSQL", StringComparison.OrdinalIgnoreCase) ? "0" : value;

    private static string NormalizeNetOpenXDbType(string value) =>
        value.Equals("MSSQL", StringComparison.OrdinalIgnoreCase)
        || value == "0" ? "vtMSSQL" : value;

    private static string Sanitize(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        return value.Length <= 500 ? value : value[..500];
    }

    private static void Validate(NetsisOptions options)
    {
        if (!options.Enabled) throw AppException.BadRequest("Netsis REST entegrasyonu devre dışı.");
        if (string.IsNullOrWhiteSpace(options.Rest.BaseUrl)
            || string.IsNullOrWhiteSpace(options.Rest.Username)
            || string.IsNullOrWhiteSpace(options.Rest.Password)
            || string.IsNullOrWhiteSpace(options.Rest.DbName)
            || string.IsNullOrWhiteSpace(options.Rest.DbUser))
            throw AppException.BadRequest(
                "Netsis REST bağlantı bilgileri eksik. BaseUrl, kullanıcı, parola, şirket ve veritabanı kullanıcısını secret store üzerinden tanımlayın.");
    }

    private sealed record ParsedToken(string AccessToken, int ExpiresInSeconds);
}
