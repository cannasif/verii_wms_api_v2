using System.Globalization;
using Microsoft.Extensions.Localization;

namespace verii_wms_api_v2.Shared.Host.Localization;

public sealed class WmsApiMessageResource;

public sealed record WmsApiLocalizedMessage(string Code, string Text);

public sealed class WmsApiMessageResolver(IStringLocalizer<WmsApiMessageResource> localizer)
{
    public static IReadOnlyList<string> CatalogCodes { get; } =
    [
        "Unauthorized", "Forbidden", "NotFound", "MethodNotAllowed", "RequestTimeout",
        "PayloadTooLarge", "UnsupportedMediaType", "TooManyRequests", "BadRequest",
        "ValidationFailed", "Conflict", "BadGateway", "ServiceUnavailable",
        "InternalServerError", "OperationFailed", "Required", "Invalid",
        "ResourceNotFound", "Duplicate", "Concurrency", "Quantity", "InvalidState",
        "File", "Erp", "Created", "Updated", "Deleted", "Cancelled", "Approved",
        "Completed", "OperationCompleted"
    ];

    public WmsApiLocalizedMessage Resolve(int statusCode, string? rawMessage, bool success)
    {
        var code = Classify(statusCode, rawMessage, success);
        var language = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;

        // Turkish is the API's default culture. Preserve its detailed operational
        // message while every other supported culture receives the shared catalog text.
        if (language.Equals("tr", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(rawMessage))
        {
            return new(code, rawMessage);
        }

        return ResolveCode(code, rawMessage);
    }

    public WmsApiLocalizedMessage ResolveCode(string code, string? fallback = null)
    {
        var localized = localizer[code];
        if (!localized.ResourceNotFound && !string.IsNullOrWhiteSpace(localized.Value))
            return new(code, localized.Value);

        var safeCode = string.IsNullOrWhiteSpace(fallback) ? "OperationFailed" : Classify(400, fallback, false);
        var safeLocalized = localizer[safeCode];
        return new(safeCode, safeLocalized.ResourceNotFound ? localizer["OperationFailed"].Value : safeLocalized.Value);
    }

    public static string Classify(int statusCode, string? message, bool success)
    {
        if (statusCode == StatusCodes.Status401Unauthorized) return "Unauthorized";
        if (statusCode == StatusCodes.Status403Forbidden) return "Forbidden";
        if (statusCode == StatusCodes.Status404NotFound) return "NotFound";
        if (statusCode == StatusCodes.Status405MethodNotAllowed) return "MethodNotAllowed";
        if (statusCode == StatusCodes.Status408RequestTimeout) return "RequestTimeout";
        if (statusCode == StatusCodes.Status413PayloadTooLarge) return "PayloadTooLarge";
        if (statusCode == StatusCodes.Status415UnsupportedMediaType) return "UnsupportedMediaType";
        if (statusCode == StatusCodes.Status422UnprocessableEntity) return "ValidationFailed";
        if (statusCode == StatusCodes.Status429TooManyRequests) return "TooManyRequests";
        if (statusCode == StatusCodes.Status502BadGateway) return "BadGateway";
        if (statusCode == StatusCodes.Status503ServiceUnavailable) return "ServiceUnavailable";
        if (statusCode >= StatusCodes.Status500InternalServerError) return "InternalServerError";

        var value = message?.Trim() ?? string.Empty;
        if (success)
        {
            if (ContainsAny(value, "oluşturuldu", "created", "erzeugt", "créé", "creado", "creato")) return "Created";
            if (ContainsAny(value, "güncellendi", "updated", "geändert", "mis à jour", "actualizado")) return "Updated";
            if (ContainsAny(value, "silindi", "deleted", "gelöscht", "supprim", "eliminado")) return "Deleted";
            if (ContainsAny(value, "iptal", "cancelled", "cancelled", "storniert", "annulé", "cancelado")) return "Cancelled";
            if (ContainsAny(value, "onaylandı", "approved", "genehmigt", "approuvé", "aprobado")) return "Approved";
            if (ContainsAny(value, "tamamlandı", "completed", "abgeschlossen", "terminé", "completado")) return "Completed";
            return "OperationCompleted";
        }

        if (statusCode == StatusCodes.Status409Conflict) return ClassifyConflict(value);
        if (statusCode == StatusCodes.Status400BadRequest) return ClassifyValidation(value);
        return "OperationFailed";
    }

    private static string ClassifyConflict(string value)
    {
        if (ContainsAny(value, "başka bir kullanıcı", "eşzaman", "concurrency", "another user", "anderer benutzer")) return "Concurrency";
        if (ContainsAny(value, "zaten", "already", "duplicate", "bereits", "déjà", "ya existe")) return "Duplicate";
        if (ContainsAny(value, "miktar", "stok", "quantity", "stock", "menge", "quantité", "cantidad")) return "Quantity";
        if (ContainsAny(value, "iptal", "silinemez", "yapılamaz", "cannot", "nicht", "impossible")) return "InvalidState";
        return "Conflict";
    }

    private static string ClassifyValidation(string value)
    {
        if (ContainsAny(value, "zorunlu", "gerekli", "required", "must be", "obligatoire", "obligatorio")) return "Required";
        if (ContainsAny(value, "bulunamadı", "bulunamıyor", "not found", "nicht gefunden", "introuvable", "no encontrado")) return "ResourceNotFound";
        if (ContainsAny(value, "dosya", "file", "xlsx", "ocr", "archivo", "fichier")) return "File";
        if (ContainsAny(value, "erp", "netsis", "eLogo")) return "Erp";
        if (ContainsAny(value, "geçersiz", "uyuşmuyor", "invalid", "not valid", "ungültig", "invalide", "inválido")) return "Invalid";
        return "BadRequest";
    }

    private static bool ContainsAny(string value, params string[] candidates) =>
        candidates.Any(candidate => value.Contains(candidate, StringComparison.OrdinalIgnoreCase));
}
