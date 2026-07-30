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
        NetsisItemSlipDefaults.Apply(request, DateTime.Now);
        var payload = JsonSerializer.Serialize(request, JsonOptions);
        var branchCode = request.FatUst.SubeKodu.ToString(
            System.Globalization.CultureInfo.InvariantCulture);
        var watch = Stopwatch.StartNew();
        try
        {
            var result = await SendAsync(payload, branchCode, false, cancellationToken);
            if (result.HttpStatusCode == (int)HttpStatusCode.Unauthorized)
                result = await SendAsync(payload, branchCode, true, cancellationToken);
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

    public async Task<NetsisCallResult<NetsisDeleteItemSlipResponse>> DeleteItemSlipAsync(
        NetsisItemSlipDeleteRequest request,
        CancellationToken cancellationToken)
    {
        var providerId = request.ToProviderId();

        var watch = Stopwatch.StartNew();
        try
        {
            var result = await SendDeleteAsync(
                providerId, request.BranchCode, false, cancellationToken);
            if (result.HttpStatusCode == (int)HttpStatusCode.Unauthorized)
                result = await SendDeleteAsync(
                    providerId, request.BranchCode, true, cancellationToken);
            return result with { DurationMs = watch.ElapsedMilliseconds };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogError("Netsis ItemSlips silme isteği zaman aşımına uğradı; ERP silme sonucu belirsiz.");
            return new(false, false, true, null, watch.ElapsedMilliseconds, null, null,
                "ERP_DELETE_TIMEOUT_COMMIT_UNCERTAIN",
                "Netsis yanıt vermedi. Belge ERP'den silinmiş olabilir; yerel ters hareket durduruldu.");
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "Netsis ItemSlips silme taşıma hatası; ERP silme sonucu belirsiz.");
            return new(false, false, true, null, watch.ElapsedMilliseconds, null, null,
                "ERP_DELETE_TRANSPORT_COMMIT_UNCERTAIN",
                "Netsis bağlantısı yanıt alınmadan kesildi. Belge ERP'den silinmiş olabilir; yerel ters hareket durduruldu.");
        }
        finally
        {
            watch.Stop();
        }
    }

    private async Task<NetsisCallResult<NetsisItemSlipResponse>> SendAsync(
        string payload,
        string branchCode,
        bool forceRefresh,
        CancellationToken cancellationToken)
    {
        var token = await tokenService.GetAccessTokenAsync(
            branchCode, forceRefresh, cancellationToken);
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
            {
                data = JsonSerializer.Deserialize<NetsisItemSlipResponse>(raw, JsonOptions);
                HydrateReferenceFields(data, raw);
            }
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

    private async Task<NetsisCallResult<NetsisDeleteItemSlipResponse>> SendDeleteAsync(
        string providerId,
        string? branchCode,
        bool forceRefresh,
        CancellationToken cancellationToken)
    {
        var token = await tokenService.GetAccessTokenAsync(
            branchCode, forceRefresh, cancellationToken);
        var basePath = optionsAccessor.Value.Rest.ItemSlipsPath.TrimEnd('/');
        using var message = new HttpRequestMessage(HttpMethod.Delete, $"{basePath}/{providerId}");
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        message.Headers.Accept.ParseAdd("application/json");

        using var response = await httpClient.SendAsync(message, cancellationToken);
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);
        NetsisDeleteItemSlipResponse? data = null;
        try
        {
            if (!string.IsNullOrWhiteSpace(raw))
                data = JsonSerializer.Deserialize<NetsisDeleteItemSlipResponse>(raw, JsonOptions);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Netsis ItemSlips silme yanıtı JSON olarak çözümlenemedi.");
        }

        var providerRejected = data?.IsSuccessful == false || data?.IsSuccessStatusCode == false;
        var businessSucceeded = response.IsSuccessStatusCode && !providerRejected;
        var error = data?.ErrorDescription ?? data?.ErrorDesc;
        if (string.IsNullOrWhiteSpace(error) && !response.IsSuccessStatusCode)
            error = $"Netsis ItemSlips silme isteği {(int)response.StatusCode} ile sonuçlandı.";
        if (response.IsSuccessStatusCode && providerRejected && string.IsNullOrWhiteSpace(error))
            error = "Netsis belge silme işlemini iş kuralı nedeniyle reddetti.";

        return new(response.IsSuccessStatusCode, businessSucceeded, false, (int)response.StatusCode, 0,
            data, Truncate(raw, 8000), data?.ErrorCode, error);
    }

    private static string? Truncate(string? value, int maxLength) =>
        string.IsNullOrWhiteSpace(value) || value.Length <= maxLength ? value : value[..maxLength];

    private static void HydrateReferenceFields(NetsisItemSlipResponse? response, string raw)
    {
        if (response is null || string.IsNullOrWhiteSpace(raw)) return;

        try
        {
            using var document = JsonDocument.Parse(raw);
            var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            CollectReferenceCandidates(document.RootElement, values);

            response.Data ??= new NetsisItemSlipResponseData();
            response.Data.FisNo ??= FirstValue(
                values, "FisNo", "FISNO", "Fis_No", "FIS_NO", "FATIRS_NO", "FatirsNo");
            response.Data.BelgeNo ??= FirstValue(
                values, "BelgeNo", "BELGE_NO", "Belge_No", "BelgeNumarasi", "BelgeNumarası");
            response.Data.KayitNo ??= FirstValue(
                values, "KayitNo", "KAYIT_NO", "Kayit_No", "KayıtNo", "KayitNumarasi");
            response.Data.ReferenceNumber ??= FirstValue(
                values, "ReferenceNumber", "REFERENCE_NUMBER", "ReferansNo", "ReferansKodu", "RefNo");
        }
        catch (JsonException)
        {
            // Ham yanıt denetim kaydında korunur; referans alanları bulunamazsa normal akış devam eder.
        }
    }

    private static void CollectReferenceCandidates(
        JsonElement element,
        IDictionary<string, string?> values)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (property.Value.ValueKind is JsonValueKind.String or JsonValueKind.Number)
                        values.TryAdd(property.Name, property.Value.ToString());
                    CollectReferenceCandidates(property.Value, values);
                }
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                    CollectReferenceCandidates(item, values);
                break;
        }
    }

    private static string? FirstValue(
        IReadOnlyDictionary<string, string?> values,
        params string[] names)
    {
        foreach (var name in names)
            if (values.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value))
                return value.Trim();
        return null;
    }
}

internal static class NetsisItemSlipDefaults
{
    internal const string DefaultProjectCode = "0";

    internal static void Apply(NetsisItemSlipRequest request, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.FatUst);

        request.FatUst.ProjeKodu = NormalizeProjectCode(request.FatUst.ProjeKodu);
        if (request.FatUst.Tarih == default)
            request.FatUst.Tarih = now;
        if (request.FatUst.FiiliTarih == default)
            request.FatUst.FiiliTarih = now;

        request.Kalems ??= [];
        foreach (var line in request.Kalems)
        {
            line.ProjeKodu = NormalizeProjectCode(line.ProjeKodu);
            line.SiparisNumarasi = line.SiparisNumarasi?.Trim() ?? string.Empty;
            if (line.SiparisNumarasi.Length == 0)
                line.SiparisKontrol = 0;
        }
    }

    internal static string NormalizeProjectCode(string? value) =>
        string.IsNullOrWhiteSpace(value) ? DefaultProjectCode : value.Trim();
}
