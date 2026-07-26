using System.Globalization;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using verii_wms_api_v2.Modules.IncomingInvoice.Application;
using verii_wms_api_v2.Modules.IncomingInvoice.Domain;
using verii_wms_api_v2.Shared.Application.Abstractions.Persistence;
using verii_wms_api_v2.Shared.Application.Exceptions;

namespace verii_wms_api_v2.Modules.IncomingInvoice.Infrastructure;

public sealed class ELogoPostboxClient(
    HttpClient httpClient,
    IOptionsMonitor<ELogoPostboxOptions> optionsMonitor,
    IUnitOfWork unitOfWork,
    IDataProtectionProvider dataProtectionProvider,
    ILogger<ELogoPostboxClient> logger) : IELogoPostboxClient
{
    private static readonly XNamespace Soap = "http://schemas.xmlsoap.org/soap/envelope/";
    private static readonly XNamespace Tempuri = "http://tempuri.org/";
    private static readonly XNamespace Contract = "http://schemas.datacontract.org/2004/07/eFaturaWebService";
    private static readonly Regex SafeFileName = new(@"[^a-zA-Z0-9_.-]+", RegexOptions.Compiled);
    private readonly IDataProtector protector =
        dataProtectionProvider.CreateProtector(ELogoConnectionService.ProtectorPurpose);

    public async Task<ELogoFetchedInvoice> FetchAsync(
        long connectionId,
        string branchCode,
        string uuid,
        IncomingInvoiceLookupKind kind,
        bool includePdf,
        CancellationToken ct = default)
    {
        var branch = ELogoConnectionService.NormalizeBranch(branchCode);
        if (!Guid.TryParse(uuid?.Trim(), out var invoiceUuid))
            throw AppException.BadRequest("Geçerli bir fatura UUID değeri girin.");
        var connection = await unitOfWork.Repository<ELogoConnection>().Query()
            .FirstOrDefaultAsync(x => x.Id == connectionId && x.BranchCode == branch && x.IsActive, ct)
            ?? throw AppException.NotFound("Aktif eLogo bağlantı tanımı bulunamadı.");
        var endpoint = ResolveEndpoint(connection);
        var password = Unprotect(connection);
        var sessionId = await LoginAsync(connection, endpoint, password, ct);
        try
        {
            var normalizedUuid = invoiceUuid.ToString().ToUpperInvariant();
            var (xmlPayload, documentKind) = await FetchXmlAsync(
                connection, endpoint, sessionId, normalizedUuid, kind, ct);
            var parsed = ParseInvoice(xmlPayload.Content);
            if (!Guid.TryParse(parsedUuid(xmlPayload.Content), out var ublUuid) || ublUuid != invoiceUuid)
                throw AppException.Conflict("UBL içindeki UUID, sorgulanan belge UUID değeriyle eşleşmiyor.");

            BinaryPayload? pdf = null;
            string? warning = null;
            if (includePdf)
            {
                try
                {
                    pdf = await FetchPdfAsync(
                        connection, endpoint, sessionId, normalizedUuid, documentKind, ct);
                }
                catch (Exception exception) when (exception is AppException or InvalidOperationException)
                {
                    logger.LogWarning(exception,
                        "Invoice PDF could not be fetched. connectionId={ConnectionId}, uuid={Uuid}",
                        connectionId, normalizedUuid);
                    warning = "UBL arşivlendi ancak PDF sağlayıcıdan alınamadı.";
                }
            }

            return new ELogoFetchedInvoice(
                connection.Id, connection.Vkn, invoiceUuid, documentKind,
                xmlPayload.Content, xmlPayload.FileName,
                pdf?.Content, pdf?.FileName, parsed,
                xmlPayload.SourceMethod, warning);
        }
        finally
        {
            await LogoutSafeAsync(connection, endpoint, sessionId);
        }
    }

    private async Task<(TextPayload Payload, IncomingInvoiceKind Kind)> FetchXmlAsync(
        ELogoConnection connection,
        Uri endpoint,
        string sessionId,
        string uuid,
        IncomingInvoiceLookupKind kind,
        CancellationToken ct)
    {
        var attempts = kind switch
        {
            IncomingInvoiceLookupKind.EInvoice => new[]
            {
                ("EINVOICE", IncomingInvoiceKind.EInvoice),
                ("EARCHIVE", IncomingInvoiceKind.EArchive)
            },
            IncomingInvoiceLookupKind.EArchive => new[]
            {
                ("EARCHIVE", IncomingInvoiceKind.EArchive),
                ("EINVOICE", IncomingInvoiceKind.EInvoice)
            },
            _ => new[]
            {
                ("EINVOICE", IncomingInvoiceKind.EInvoice),
                ("EARCHIVE", IncomingInvoiceKind.EArchive)
            }
        };

        foreach (var (documentType, documentKind) in attempts)
        {
            try
            {
                var payload = await GetDocumentXmlAsync(
                    connection, endpoint, sessionId, uuid, documentType, ct);
                if (payload is not null) return (payload, documentKind);
            }
            catch (Exception exception) when (IsRecoverableLookupFailure(exception))
            {
                logger.LogInformation(
                    "eLogo lookup fallback. connectionId={ConnectionId}, uuid={Uuid}, type={DocumentType}, reason={Reason}",
                    connection.Id, uuid, documentType, exception.Message);
            }
        }

        throw AppException.NotFound($"{uuid} UUID değerine sahip e-Fatura/e-Arşiv belgesi bulunamadı.");
    }

    private async Task<BinaryPayload?> FetchPdfAsync(
        ELogoConnection connection,
        Uri endpoint,
        string sessionId,
        string uuid,
        IncomingInvoiceKind kind,
        CancellationToken ct)
    {
        if (kind == IncomingInvoiceKind.EArchive)
        {
            var archivePdf = await TryGetEArchivePdfAsync(connection, endpoint, sessionId, uuid, ct);
            if (archivePdf is not null) return archivePdf;
        }

        var documentType = kind == IncomingInvoiceKind.EInvoice ? "EINVOICE" : "EARCHIVE";
        var documentPdf = await GetDocumentBinaryAsync(
            connection, endpoint, sessionId, uuid, documentType, "PDF", ct);
        if (documentPdf is not null) return documentPdf;
        if (kind == IncomingInvoiceKind.EInvoice)
            return await GetDocumentBinaryAsync(
                connection, endpoint, sessionId, uuid, "EARCHIVE", "PDF", ct);
        return null;
    }

    private async Task<string> LoginAsync(
        ELogoConnection connection, Uri endpoint, string password, CancellationToken ct)
    {
        var options = optionsMonitor.CurrentValue;
        var envelope = Envelope("Login",
            new XElement(Tempuri + "login",
                new XAttribute(XNamespace.Xmlns + "d", Contract.NamespaceName),
                new XElement(Contract + "appStr", connection.ApplicationName ?? options.ApplicationName),
                new XElement(Contract + "passWord", password),
                new XElement(Contract + "source", connection.Source),
                new XElement(Contract + "userName", connection.Username),
                new XElement(Contract + "version", connection.Version ?? options.Version)));
        var response = await SendAsync(connection, endpoint, "Login", envelope, ct);
        var succeeded = bool.TryParse(Value(response, "LoginResult"), out var result) && result;
        var sessionId = Value(response, "sessionID");
        if (!succeeded || string.IsNullOrWhiteSpace(sessionId))
            throw new AppException(StatusCodes.Status502BadGateway,
                "eLogo oturumu açılamadı. Kullanıcı, web servis şifresi, source ve sürüm bilgilerini kontrol edin.");
        return sessionId;
    }

    private async Task<TextPayload?> GetDocumentXmlAsync(
        ELogoConnection connection,
        Uri endpoint,
        string sessionId,
        string uuid,
        string documentType,
        CancellationToken ct)
    {
        var response = await GetDocumentDataAsync(
            connection, endpoint, sessionId, uuid, documentType, "UBL", ct);
        var raw = ReadDocumentResult(response, "getDocumentDataResult");
        if (raw is null) return null;
        var normalized = NormalizeArchiveEntry(raw.Value.Content, raw.Value.FileName, ".xml");
        var xml = DecodeXml(normalized.Content);
        try
        {
            _ = ParseXml(xml, LoadOptions.PreserveWhitespace);
        }
        catch (Exception exception)
        {
            throw new AppException(StatusCodes.Status502BadGateway,
                $"eLogo geçersiz UBL/XML döndürdü: {exception.Message}");
        }
        return new TextPayload(
            xml, SafeName(normalized.FileName, uuid, ".xml"),
            $"getDocumentData:{documentType}:UBL");
    }

    private async Task<BinaryPayload?> GetDocumentBinaryAsync(
        ELogoConnection connection,
        Uri endpoint,
        string sessionId,
        string uuid,
        string documentType,
        string dataType,
        CancellationToken ct)
    {
        var response = await GetDocumentDataAsync(
            connection, endpoint, sessionId, uuid, documentType, dataType, ct);
        var raw = ReadDocumentResult(response, "getDocumentDataResult");
        if (raw is null) return null;
        var normalized = NormalizeArchiveEntry(raw.Value.Content, raw.Value.FileName, ".pdf");
        if (!IsPdf(normalized.Content))
            throw new AppException(StatusCodes.Status502BadGateway, "eLogo PDF olmayan bir belge içeriği döndürdü.");
        return new BinaryPayload(
            normalized.Content, SafeName(normalized.FileName, uuid, ".pdf"),
            $"getDocumentData:{documentType}:{dataType}");
    }

    private async Task<XDocument> GetDocumentDataAsync(
        ELogoConnection connection,
        Uri endpoint,
        string sessionId,
        string uuid,
        string documentType,
        string dataType,
        CancellationToken ct)
    {
        var envelope = Envelope("getDocumentData",
            new XElement(Tempuri + "sessionID", sessionId),
            new XElement(Tempuri + "uuid", uuid),
            new XElement(Tempuri + "docType", documentType),
            new XElement(Tempuri + "dataType", dataType));
        return await SendAsync(connection, endpoint, "getDocumentData", envelope, ct);
    }

    private async Task<BinaryPayload?> TryGetEArchivePdfAsync(
        ELogoConnection connection,
        Uri endpoint,
        string sessionId,
        string uuid,
        CancellationToken ct)
    {
        try
        {
            var envelope = Envelope("getEArchiveInvoicePdfData",
                new XElement(Tempuri + "sessionID", sessionId),
                new XElement(Tempuri + "uuid", uuid),
                new XElement(Tempuri + "allInvoicesOrJustSigned", true),
                new XElement(Tempuri + "isCanceled", false));
            var response = await SendAsync(
                connection, endpoint, "getEArchiveInvoicePdfData", envelope, ct);
            var succeeded = bool.TryParse(
                Value(response, "getEArchiveInvoicePdfDataResult"), out var result) && result;
            var base64 = Value(response, "pdfData");
            if (!succeeded || string.IsNullOrWhiteSpace(base64)) return null;
            var content = DecodeBase64(base64);
            if (!IsPdf(content))
                throw new AppException(StatusCodes.Status502BadGateway,
                    "eLogo e-Arşiv PDF servisi PDF olmayan içerik döndürdü.");
            return new BinaryPayload(content, $"{uuid}.pdf", "getEArchiveInvoicePdfData");
        }
        catch (Exception exception) when (IsRecoverableLookupFailure(exception))
        {
            return null;
        }
    }

    private async Task<XDocument> SendAsync(
        ELogoConnection connection,
        Uri endpoint,
        string operation,
        XDocument envelope,
        CancellationToken ct)
    {
        var options = optionsMonitor.CurrentValue;
        var timeout = connection.TimeoutSeconds is > 0
            ? connection.TimeoutSeconds.Value
            : Math.Clamp(options.TimeoutSeconds, 10, 600);
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(timeout));
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(
                envelope.Declaration + envelope.ToString(SaveOptions.DisableFormatting),
                Encoding.UTF8, "text/xml")
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/xml"));
        request.Headers.TryAddWithoutValidation(
            "SOAPAction", $"\"http://tempuri.org/IPostBoxService/{operation}\"");

        try
        {
            using var response = await httpClient.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token);
            var maximum = Math.Clamp(options.MaximumPayloadBytes, 1 * 1024 * 1024, 100 * 1024 * 1024);
            if (response.Content.Headers.ContentLength > maximum)
                throw new AppException(StatusCodes.Status502BadGateway,
                    "eLogo yanıtı güvenli boyut sınırını aşıyor.");
            var body = await ReadLimitedStringAsync(response.Content, maximum, timeoutCts.Token);
            XDocument? document = null;
            try { document = ParseXml(body); }
            catch when (!response.IsSuccessStatusCode) { }
            if (!response.IsSuccessStatusCode)
            {
                if (document is not null) ThrowSoapFault(document, operation);
                throw new AppException(StatusCodes.Status502BadGateway,
                    $"eLogo {operation} çağrısı HTTP {(int)response.StatusCode} hatası döndürdü.");
            }
            if (document is null)
                throw new AppException(StatusCodes.Status502BadGateway,
                    $"eLogo {operation} çağrısı geçersiz XML yanıtı döndürdü.");
            ThrowSoapFault(document, operation);
            return document;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new AppException(StatusCodes.Status504GatewayTimeout,
                $"eLogo {operation} çağrısı {timeout} saniyede tamamlanamadı.");
        }
        catch (HttpRequestException exception)
        {
            throw new AppException(StatusCodes.Status502BadGateway,
                $"eLogo servisine bağlanılamadı: {exception.Message}");
        }
    }

    private async Task LogoutSafeAsync(ELogoConnection connection, Uri endpoint, string sessionId)
    {
        try
        {
            var envelope = Envelope("Logout", new XElement(Tempuri + "sessionID", sessionId));
            await SendAsync(connection, endpoint, "Logout", envelope, CancellationToken.None);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "eLogo logout failed. connectionId={ConnectionId}", connection.Id);
        }
    }

    private Uri ResolveEndpoint(ELogoConnection connection)
    {
        var options = optionsMonitor.CurrentValue;
        var value = connection.EndpointUrl ?? options.EndpointUrl;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw AppException.BadRequest("eLogo servis adresi geçerli bir HTTPS adresi değil.");
        var allowed = options.AllowedHosts
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim().ToLowerInvariant())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (allowed.Count == 0 || !allowed.Contains(uri.Host))
            throw AppException.Forbidden("eLogo servis adresinin host bilgisi izin verilen listede değil.");
        return uri;
    }

    private string Unprotect(ELogoConnection connection)
    {
        if (string.IsNullOrWhiteSpace(connection.PasswordCipherText))
            throw new AppException(StatusCodes.Status422UnprocessableEntity,
                "eLogo bağlantısında web servis şifresi tanımlı değil.");
        try { return protector.Unprotect(connection.PasswordCipherText); }
        catch (CryptographicException)
        {
            throw new AppException(StatusCodes.Status422UnprocessableEntity,
                "eLogo bağlantı şifresi bu sunucuda çözülemedi. Şifreyi yeniden kaydedin.");
        }
    }

    private (byte[] Content, string? FileName)? ReadDocumentResult(
        XDocument document, string resultElementName)
    {
        var result = document.Descendants().FirstOrDefault(x => x.Name.LocalName == resultElementName);
        var base64 = result?.Descendants().FirstOrDefault(x => x.Name.LocalName == "Value")?.Value;
        if (string.IsNullOrWhiteSpace(base64)) return null;
        var fileName = result?.Descendants().FirstOrDefault(x => x.Name.LocalName == "fileName")?.Value;
        return (DecodeBase64(base64), fileName);
    }

    private byte[] DecodeBase64(string value)
    {
        var maximum = Math.Clamp(
            optionsMonitor.CurrentValue.MaximumPayloadBytes, 1 * 1024 * 1024, 100 * 1024 * 1024);
        if (value.Length > ((maximum + 2) / 3) * 4 + 8)
            throw new AppException(StatusCodes.Status502BadGateway,
                "eLogo belge içeriği güvenli boyut sınırını aşıyor.");
        try { return Convert.FromBase64String(value); }
        catch (FormatException)
        {
            throw new AppException(StatusCodes.Status502BadGateway,
                "eLogo belge içeriği geçerli Base64 formatında değil.");
        }
    }

    private (byte[] Content, string? FileName) NormalizeArchiveEntry(
        byte[] content, string? fileName, string extension)
    {
        if (!IsZip(content)) return (content, fileName);
        using var archive = new ZipArchive(new MemoryStream(content), ZipArchiveMode.Read);
        var entry = archive.Entries.FirstOrDefault(x =>
            x.Name.EndsWith(extension, StringComparison.OrdinalIgnoreCase));
        if (entry is null)
            throw new AppException(StatusCodes.Status502BadGateway,
                $"eLogo ZIP içeriğinde {extension} belgesi bulunamadı.");
        var maximum = Math.Clamp(
            optionsMonitor.CurrentValue.MaximumPayloadBytes, 1 * 1024 * 1024, 100 * 1024 * 1024);
        if (entry.Length > maximum)
            throw new AppException(StatusCodes.Status502BadGateway,
                "eLogo ZIP girdisi güvenli boyut sınırını aşıyor.");
        using var input = entry.Open();
        using var output = new MemoryStream();
        CopyLimited(input, output, maximum);
        return (output.ToArray(), entry.Name);
    }

    private static ParsedIncomingInvoice ParseInvoice(string xml)
    {
        var document = ParseXml(xml, LoadOptions.PreserveWhitespace);
        var root = document.Root ?? throw AppException.BadRequest("UBL kök elemanı bulunamadı.");
        var legal = Child(root, "LegalMonetaryTotal");
        var taxTotal = root.Elements().FirstOrDefault(x => x.Name.LocalName == "TaxTotal");
        var supplierNode = Child(root, "AccountingSupplierParty");
        var customerNode = Child(root, "AccountingCustomerParty");
        var issueDateText = ChildValue(root, "IssueDate");
        if (!DateOnly.TryParse(issueDateText, CultureInfo.InvariantCulture, DateTimeStyles.None, out var issueDate))
            throw AppException.BadRequest("UBL fatura tarihi geçersiz.");
        TimeOnly? issueTime = TimeOnly.TryParse(
            ChildValue(root, "IssueTime"), CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedTime)
            ? parsedTime : null;
        var lines = root.Elements().Where(x => x.Name.LocalName == "InvoiceLine")
            .Select((line, index) => ParseLine(line, index + 1)).ToList();
        if (lines.Count == 0) throw AppException.BadRequest("UBL içinde fatura kalemi bulunamadı.");
        return new ParsedIncomingInvoice(
            ChildValue(root, "ProfileID"),
            RequiredUbl(root, "ID", "Fatura numarası"),
            ChildValue(root, "InvoiceTypeCode"),
            issueDate,
            issueTime,
            string.IsNullOrWhiteSpace(ChildValue(root, "DocumentCurrencyCode"))
                ? "TRY" : ChildValue(root, "DocumentCurrencyCode"),
            DescendantValue(Child(root, "OrderReference"), "ID"),
            DescendantValue(Child(root, "DespatchDocumentReference"), "ID"),
            ParseParty(supplierNode),
            ParseParty(customerNode),
            DecimalValue(Child(legal, "LineExtensionAmount")),
            DecimalValue(Child(legal, "TaxExclusiveAmount")),
            DecimalValue(Child(taxTotal, "TaxAmount")),
            DecimalValue(Child(legal, "TaxInclusiveAmount")),
            DecimalValue(Child(legal, "AllowanceTotalAmount")),
            DecimalValue(Child(legal, "PayableAmount")),
            lines);
    }

    private static ParsedIncomingInvoiceLine ParseLine(XElement line, int fallbackLineNo)
    {
        var item = Child(line, "Item");
        var quantityNode = Child(line, "InvoicedQuantity") ?? Child(line, "CreditedQuantity");
        var sellerCode = DescendantValue(Child(item, "SellersItemIdentification"), "ID");
        var buyerCode = DescendantValue(Child(item, "BuyersItemIdentification"), "ID");
        var externalId = ChildValue(line, "ID");
        var lineNo = int.TryParse(externalId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed : fallbackLineNo;
        var taxSubtotal = Descendant(line, "TaxSubtotal");
        return new ParsedIncomingInvoiceLine(
            lineNo,
            string.IsNullOrWhiteSpace(externalId) ? fallbackLineNo.ToString(CultureInfo.InvariantCulture) : externalId,
            sellerCode ?? buyerCode ?? string.Empty,
            buyerCode,
            ChildValue(item, "Name"),
            NullIfEmpty(ChildValue(item, "Description")),
            DecimalValue(quantityNode),
            quantityNode?.Attribute("unitCode")?.Value?.Trim() ?? string.Empty,
            DecimalValue(Child(Child(line, "Price"), "PriceAmount")),
            DecimalValue(Child(line, "LineExtensionAmount")),
            DecimalValue(Child(taxSubtotal, "Percent")),
            DecimalValue(Child(taxSubtotal, "TaxAmount")));
    }

    private static ParsedInvoiceParty ParseParty(XElement? accountingParty)
    {
        var party = Descendant(accountingParty, "Party");
        var address = Descendant(party, "PostalAddress");
        var taxScheme = Descendant(party, "PartyTaxScheme");
        var taxId = party?.Descendants()
            .FirstOrDefault(x => x.Name.LocalName == "ID"
                && IsTaxScheme(x.Attribute("schemeID")?.Value))?.Value?.Trim()
            ?? party?.Descendants().FirstOrDefault(x => x.Name.LocalName == "ID")?.Value?.Trim()
            ?? string.Empty;
        var name = DescendantValue(Descendant(party, "PartyName"), "Name")
            ?? DescendantValue(party, "RegistrationName")
            ?? string.Empty;
        return new ParsedInvoiceParty(
            taxId, name, DescendantValue(taxScheme, "Name"),
            ChildValueOrNull(address, "CityName"), ChildValueOrNull(address, "CitySubdivisionName"),
            DescendantValue(Descendant(address, "Country"), "Name"),
            DescendantValue(address, "Line") ?? ChildValueOrNull(address, "StreetName"));
    }

    private static string parsedUuid(string xml)
    {
        var root = ParseXml(xml).Root;
        return root is null ? string.Empty : ChildValue(root, "UUID");
    }

    private static XDocument Envelope(string operation, params object[] content) => new(
        new XDeclaration("1.0", "utf-8", null),
        new XElement(Soap + "Envelope",
            new XAttribute(XNamespace.Xmlns + "soapenv", Soap.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "tem", Tempuri.NamespaceName),
            new XElement(Soap + "Body", new XElement(Tempuri + operation, content))));

    private static void ThrowSoapFault(XDocument document, string operation)
    {
        var fault = document.Descendants().FirstOrDefault(x => x.Name.LocalName == "Fault");
        if (fault is null) return;
        var text = string.Join(" | ", fault.Descendants()
            .Where(x => !x.HasElements)
            .Select(x => x.Value.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Take(6));
        throw new AppException(StatusCodes.Status502BadGateway,
            $"eLogo {operation} servis hatası: {text}");
    }

    private static bool IsRecoverableLookupFailure(Exception exception)
    {
        var message = exception.Message;
        return message.Contains("belge bulunamad", StringComparison.OrdinalIgnoreCase)
            || message.Contains("verisi dönmedi", StringComparison.OrdinalIgnoreCase)
            || message.Contains("empty payload", StringComparison.OrdinalIgnoreCase)
            || message.Contains("Invalid enum value", StringComparison.OrdinalIgnoreCase)
            || message.Contains("DataFormat", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<string> ReadLimitedStringAsync(
        HttpContent content, long maximum, CancellationToken ct)
    {
        await using var input = await content.ReadAsStreamAsync(ct);
        using var output = new MemoryStream();
        await CopyLimitedAsync(input, output, maximum, ct);
        return Encoding.UTF8.GetString(output.ToArray());
    }

    private static async Task CopyLimitedAsync(
        Stream input, Stream output, long maximum, CancellationToken ct)
    {
        var buffer = new byte[81920];
        long total = 0;
        while (true)
        {
            var read = await input.ReadAsync(buffer, ct);
            if (read == 0) break;
            total += read;
            if (total > maximum)
                throw new AppException(StatusCodes.Status502BadGateway, "eLogo yanıtı güvenli boyut sınırını aşıyor.");
            await output.WriteAsync(buffer.AsMemory(0, read), ct);
        }
    }

    private static void CopyLimited(Stream input, Stream output, long maximum)
    {
        var buffer = new byte[81920];
        long total = 0;
        while (true)
        {
            var read = input.Read(buffer);
            if (read == 0) break;
            total += read;
            if (total > maximum)
                throw new AppException(StatusCodes.Status502BadGateway, "eLogo ZIP girdisi güvenli boyut sınırını aşıyor.");
            output.Write(buffer, 0, read);
        }
    }

    private static string DecodeXml(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        return Encoding.UTF8.GetString(bytes);
    }

    private static string SafeName(string? fileName, string uuid, string extension)
    {
        var name = SafeFileName.Replace(Path.GetFileName(fileName ?? uuid), "_");
        return name.EndsWith(extension, StringComparison.OrdinalIgnoreCase) ? name : name + extension;
    }

    private static string? Value(XDocument document, string name) =>
        document.Descendants().FirstOrDefault(x => x.Name.LocalName == name)?.Value;
    private static XDocument ParseXml(
        string xml,
        LoadOptions options = LoadOptions.None)
    {
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersFromEntities = 0,
            MaxCharactersInDocument = 100L * 1024 * 1024
        };
        using var textReader = new StringReader(xml);
        using var xmlReader = XmlReader.Create(textReader, settings);
        return XDocument.Load(xmlReader, options);
    }
    private static XElement? Child(XElement? element, string name) =>
        element?.Elements().FirstOrDefault(x => x.Name.LocalName == name);
    private static XElement? Descendant(XElement? element, string name) =>
        element?.Descendants().FirstOrDefault(x => x.Name.LocalName == name);
    private static string ChildValue(XElement? element, string name) =>
        Child(element, name)?.Value?.Trim() ?? string.Empty;
    private static string? ChildValueOrNull(XElement? element, string name) =>
        NullIfEmpty(ChildValue(element, name));
    private static string? DescendantValue(XElement? element, string name) =>
        NullIfEmpty(Descendant(element, name)?.Value);
    private static string? NullIfEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static decimal DecimalValue(XElement? element) =>
        decimal.TryParse(element?.Value?.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var value)
            ? value : 0m;
    private static string RequiredUbl(XElement root, string name, string label)
    {
        var value = ChildValue(root, name);
        return string.IsNullOrWhiteSpace(value)
            ? throw AppException.BadRequest($"UBL içinde {label} bulunamadı.")
            : value;
    }
    private static bool IsTaxScheme(string? value) =>
        string.Equals(value, "VKN", StringComparison.OrdinalIgnoreCase)
        || string.Equals(value, "TCKN", StringComparison.OrdinalIgnoreCase);
    private static bool IsPdf(byte[] bytes) =>
        bytes.Length >= 5 && bytes.AsSpan(0, 5).SequenceEqual("%PDF-"u8);
    private static bool IsZip(byte[] bytes) =>
        bytes.Length >= 4 && bytes.AsSpan(0, 4).SequenceEqual(new byte[] { 0x50, 0x4B, 0x03, 0x04 });

    private sealed record TextPayload(string Content, string FileName, string SourceMethod);
    private sealed record BinaryPayload(byte[] Content, string FileName, string SourceMethod);
}
