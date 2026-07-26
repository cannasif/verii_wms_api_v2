using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using verii_wms_api_v2.Modules.ErpIntegration.Application;

namespace verii_wms_api_v2.Modules.ErpIntegration.Infrastructure;

public sealed class NetsisRestClient(
    HttpClient httpClient,
    INetsisTokenService tokenService,
    IOptions<NetsisOptions> optionsAccessor,
    ILogger<NetsisRestClient> logger) : INetsisRestClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<NetsisCallResult<NetsisItemSlipResponse>> CreateItemSlipAsync(
        NetsisItemSlipRequest request,
        CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Serialize(request, JsonOptions);
        var watch = Stopwatch.StartNew();
        try
        {
            var result = await SendAsync(payload, false, cancellationToken);
            if (result.HttpStatusCode == (int)HttpStatusCode.Unauthorized)
                result = await SendAsync(payload, true, cancellationToken);
            return result with { DurationMs = watch.ElapsedMilliseconds };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogError("Netsis ItemSlips isteği zaman aşımına uğradı; ERP commit sonucu belirsiz.");
            return new(false, false, true, null, watch.ElapsedMilliseconds, null, null,
                "ERP_TIMEOUT_COMMIT_UNCERTAIN",
                "Netsis yanıt vermedi. Belge ERP'ye kaydedilmiş olabilir; otomatik tekrar engellendi.");
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "Netsis ItemSlips taşıma hatası; ERP commit sonucu belirsiz.");
            return new(false, false, true, null, watch.ElapsedMilliseconds, null, null,
                "ERP_TRANSPORT_COMMIT_UNCERTAIN",
                "Netsis bağlantısı yanıt alınmadan kesildi. Belge ERP'ye kaydedilmiş olabilir; otomatik tekrar engellendi.");
        }
        finally
        {
            watch.Stop();
        }
    }

    private async Task<NetsisCallResult<NetsisItemSlipResponse>> SendAsync(
        string payload,
        bool forceRefresh,
        CancellationToken cancellationToken)
    {
        var token = await tokenService.GetAccessTokenAsync(forceRefresh, cancellationToken);
        var options = optionsAccessor.Value.Rest;
        using var message = new HttpRequestMessage(HttpMethod.Post, options.ItemSlipsPath)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        message.Headers.Accept.ParseAdd("application/json");

        using var response = await httpClient.SendAsync(message, cancellationToken);
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);
        NetsisItemSlipResponse? data = null;
        try
        {
            if (!string.IsNullOrWhiteSpace(raw))
                data = JsonSerializer.Deserialize<NetsisItemSlipResponse>(raw, JsonOptions);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Netsis ItemSlips yanıtı JSON olarak çözümlenemedi.");
        }

        var businessSucceeded = response.IsSuccessStatusCode
            && (data?.IsSuccessful == true || data?.IsSuccessStatusCode == true);
        var error = data?.ErrorDescription ?? data?.ErrorDesc;
        if (string.IsNullOrWhiteSpace(error) && !response.IsSuccessStatusCode)
            error = $"Netsis ItemSlips isteği {(int)response.StatusCode} ile sonuçlandı.";
        if (response.IsSuccessStatusCode && data is null)
            error = "Netsis başarılı HTTP kodu döndürdü ancak iş sonucu çözümlenemedi.";

        return new(response.IsSuccessStatusCode, businessSucceeded, false, (int)response.StatusCode, 0,
            data, Truncate(raw, 8000), data?.ErrorCode, error);
    }

    private static string? Truncate(string? value, int maxLength) =>
        string.IsNullOrWhiteSpace(value) || value.Length <= maxLength ? value : value[..maxLength];
}
