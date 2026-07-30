using System.Globalization;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Options;
using verii_wms_api_v2.Modules.IncomingInvoice.Application;
using verii_wms_api_v2.Shared.Application.Exceptions;

namespace verii_wms_api_v2.Modules.IncomingInvoice.Infrastructure;

public sealed class AzureDocumentIntelligenceOcrClient(
    HttpClient httpClient,
    IOptions<IncomingInvoiceOcrOptions> options) : IIncomingInvoiceOcrClient
{
    private static readonly string[] ContentTypes =
        ["application/pdf", "image/png", "image/jpeg", "image/tiff"];
    private readonly IncomingInvoiceOcrOptions _options = options.Value;

    public IncomingInvoiceOcrStatus Status => new(
        IsConfigured: !string.IsNullOrWhiteSpace(_options.Endpoint)
            && !string.IsNullOrWhiteSpace(_options.ApiKey),
        Provider: _options.Provider,
        Message: !string.IsNullOrWhiteSpace(_options.Endpoint)
            && !string.IsNullOrWhiteSpace(_options.ApiKey)
            ? "OCR sağlayıcısı kullanıma hazır."
            : "OCR sağlayıcısı yapılandırılmadı. Endpoint ve API anahtarı sunucu ayarlarından girilmelidir.",
        SupportedContentTypes: ContentTypes,
        MaximumFileSizeBytes: Math.Clamp(_options.MaximumFileSizeMb, 1, 100) * 1024L * 1024L);

    public async Task<OcrAnalyzedInvoice> AnalyzeAsync(
        byte[] content,
        string contentType,
        CancellationToken ct = default)
    {
        if (!Status.IsConfigured) throw AppException.Conflict(Status.Message);
        var endpoint = _options.Endpoint!.TrimEnd('/');
        var url = $"{endpoint}/documentintelligence/documentModels/{Uri.EscapeDataString(_options.ModelId)}" +
                  $":analyze?_overload=analyzeDocument&api-version={Uri.EscapeDataString(_options.ApiVersion)}";
        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.TryAddWithoutValidation("Ocp-Apim-Subscription-Key", _options.ApiKey);
        request.Content = new ByteArrayContent(content);
        request.Content.Headers.ContentType =
            new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
        using var response = await httpClient.SendAsync(request, ct);
        if (response.StatusCode != HttpStatusCode.Accepted)
            throw AppException.Conflict(
                $"OCR sağlayıcısı belgeyi kabul etmedi ({(int)response.StatusCode}).");
        if (response.Headers.Location is null)
            throw AppException.Conflict("OCR sağlayıcısı işlem adresi döndürmedi.");

        var deadline = DateTimeOffset.UtcNow.AddSeconds(Math.Clamp(_options.TimeoutSeconds, 15, 300));
        while (DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(750), ct);
            using var poll = new HttpRequestMessage(HttpMethod.Get, response.Headers.Location);
            poll.Headers.TryAddWithoutValidation("Ocp-Apim-Subscription-Key", _options.ApiKey);
            using var pollResponse = await httpClient.SendAsync(poll, ct);
            var json = await pollResponse.Content.ReadAsStringAsync(ct);
            if (!pollResponse.IsSuccessStatusCode)
                throw AppException.Conflict(
                    $"OCR sonucu okunamadı ({(int)pollResponse.StatusCode}).");
            using var document = JsonDocument.Parse(json);
            var status = document.RootElement.TryGetProperty("status", out var statusElement)
                ? statusElement.GetString()
                : null;
            if (string.Equals(status, "failed", StringComparison.OrdinalIgnoreCase))
                throw AppException.Conflict("OCR sağlayıcısı belgeyi işleyemedi.");
            if (string.Equals(status, "succeeded", StringComparison.OrdinalIgnoreCase))
                return Parse(document.RootElement, response.Headers.Location.ToString());
        }
        throw AppException.Conflict("OCR işlemi zaman aşımına uğradı.");
    }

    internal static OcrAnalyzedInvoice Parse(JsonElement root, string operationId)
    {
        var analyzeResult = root.GetProperty("analyzeResult");
        var document = analyzeResult.GetProperty("documents")[0];
        var fields = document.GetProperty("fields");
        var invoiceNo = Text(fields, "InvoiceId") ?? string.Empty;
        var issueDate = Date(fields, "InvoiceDate") ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var currency = CurrencyCode(fields, "InvoiceTotal") ?? "TRY";
        var supplier = new ParsedInvoiceParty(
            Text(fields, "VendorTaxId") ?? string.Empty,
            Text(fields, "VendorName") ?? string.Empty,
            null, null, null, null, Text(fields, "VendorAddress"));
        var customer = new ParsedInvoiceParty(
            Text(fields, "CustomerTaxId") ?? string.Empty,
            Text(fields, "CustomerName") ?? string.Empty,
            null, null, null, null, Text(fields, "CustomerAddress"));
        var lines = new List<ParsedIncomingInvoiceLine>();
        var lineConfidences = new List<decimal?>();
        if (fields.TryGetProperty("Items", out var items)
            && items.TryGetProperty("valueArray", out var array))
        {
            var lineNo = 0;
            foreach (var item in array.EnumerateArray())
            {
                lineNo++;
                if (!item.TryGetProperty("valueObject", out var values)) continue;
                var quantity = Number(values, "Quantity") ?? 0m;
                var amount = CurrencyAmount(values, "Amount") ?? Number(values, "Amount") ?? 0m;
                var unitPrice = CurrencyAmount(values, "UnitPrice")
                    ?? Number(values, "UnitPrice")
                    ?? (quantity == 0 ? 0 : amount / quantity);
                var tax = CurrencyAmount(values, "Tax") ?? Number(values, "Tax") ?? 0m;
                lines.Add(new ParsedIncomingInvoiceLine(
                    lineNo, lineNo.ToString(CultureInfo.InvariantCulture),
                    Text(values, "ProductCode") ?? string.Empty, null,
                    Text(values, "Description") ?? string.Empty,
                    Text(values, "Description"),
                    quantity, Text(values, "Unit") ?? "ADET",
                    unitPrice, amount, 0m, tax));
                lineConfidences.Add(Confidence(item));
            }
        }
        var subtotal = CurrencyAmount(fields, "SubTotal")
            ?? lines.Sum(x => x.LineExtensionAmount);
        var totalTax = CurrencyAmount(fields, "TotalTax") ?? lines.Sum(x => x.TaxAmount);
        var total = CurrencyAmount(fields, "InvoiceTotal") ?? subtotal + totalTax;
        var parsed = new ParsedIncomingInvoice(
            "OCR", invoiceNo, "SATIS", issueDate, null, currency, null, null,
            supplier, customer, subtotal, subtotal, totalTax, total, 0m, total, lines);
        return new OcrAnalyzedInvoice(
            parsed, Confidence(document), lineConfidences, operationId);
    }

    private static string? Text(JsonElement fields, string name)
    {
        if (!fields.TryGetProperty(name, out var field)) return null;
        foreach (var property in new[] { "valueString", "content", "valuePhoneNumber" })
            if (field.TryGetProperty(property, out var value)
                && !string.IsNullOrWhiteSpace(value.GetString()))
                return value.GetString()!.Trim();
        return null;
    }

    private static decimal? Number(JsonElement fields, string name) =>
        fields.TryGetProperty(name, out var field)
        && field.TryGetProperty("valueNumber", out var value)
        && value.TryGetDecimal(out var result) ? result : null;

    private static decimal? CurrencyAmount(JsonElement fields, string name)
    {
        if (!fields.TryGetProperty(name, out var field)
            || !field.TryGetProperty("valueCurrency", out var currency)
            || !currency.TryGetProperty("amount", out var amount)
            || !amount.TryGetDecimal(out var result)) return null;
        return result;
    }

    private static string? CurrencyCode(JsonElement fields, string name) =>
        fields.TryGetProperty(name, out var field)
        && field.TryGetProperty("valueCurrency", out var currency)
        && currency.TryGetProperty("currencyCode", out var code)
            ? code.GetString()
            : null;

    private static DateOnly? Date(JsonElement fields, string name) =>
        fields.TryGetProperty(name, out var field)
        && field.TryGetProperty("valueDate", out var value)
        && DateOnly.TryParse(value.GetString(), CultureInfo.InvariantCulture, out var result)
            ? result
            : null;

    private static decimal? Confidence(JsonElement element) =>
        element.TryGetProperty("confidence", out var value)
        && value.TryGetDecimal(out var result) ? result : null;
}
