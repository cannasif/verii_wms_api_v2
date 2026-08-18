using System.Data;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using verii_wms_api_v2.Modules.Audit.Application;
using verii_wms_api_v2.Modules.BarcodeDesigner.Domain;
using verii_wms_api_v2.Modules.BarcodeDesigner.Localization;
using verii_wms_api_v2.Shared;
using verii_wms_api_v2.Shared.Application.Abstractions.Persistence;
using verii_wms_api_v2.Shared.Application.Exceptions;

namespace verii_wms_api_v2.Modules.BarcodeDesigner.Application;

public sealed partial class BarcodeDesignerService(IUnitOfWork unitOfWork, IAuditLogWriter audit, IStringLocalizer<BarcodeDesignerResource> localizer) : IBarcodeDesignerService
{
    private const int MaxTemplateBytes = 262_144;
    private static readonly IReadOnlyDictionary<string,string> SearchColumns=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase)
    {
        ["id"]=nameof(BarcodeTemplateGridRow.Id),["templateCode"]=nameof(BarcodeTemplateGridRow.TemplateCode),
        ["displayName"]=nameof(BarcodeTemplateGridRow.DisplayName),["widthMm"]=nameof(BarcodeTemplateGridRow.DimensionsSearchText),
        ["dpi"]=nameof(BarcodeTemplateGridRow.Dpi),["draftVersionId"]=nameof(BarcodeTemplateGridRow.DraftVersionId),
        ["publishedVersionId"]=nameof(BarcodeTemplateGridRow.PublishedVersionId)
    };
    private static readonly string[] DefaultSearchColumns=["templateCode","displayName"];
    private IGenericRepository<BarcodeTemplate> Templates => unitOfWork.Repository<BarcodeTemplate>();
    private IGenericRepository<BarcodeTemplateVersion> Versions => unitOfWork.Repository<BarcodeTemplateVersion>();

    public async Task<PagedResponse<BarcodeTemplateGridRow>> GetPagedAsync(PagedRequest request, CancellationToken cancellationToken = default)
    {
        var search = request.LegacySearch?.Trim();
        var query = Grid().Where(x => string.IsNullOrWhiteSpace(search) || x.TemplateCode.Contains(search) || x.DisplayName.Contains(search) || x.LabelType.Contains(search));
        return await query.ApplySearch(request,SearchColumns,DefaultSearchColumns).ApplyAdvancedFilters(request).ApplySort(request, nameof(BarcodeTemplateGridRow.TemplateCode)).ToPagedResponseAsync(request, cancellationToken);
    }

    public async Task<BarcodeTemplateGridRow> GetByIdAsync(long id, CancellationToken cancellationToken = default) =>
        await Grid().FirstOrDefaultAsync(x => x.Id == id, cancellationToken) ?? throw AppException.NotFound(Message(BarcodeDesignerMessageKeys.NotFound));

    public async Task<IReadOnlyList<BarcodeTemplateVersionRow>> GetVersionsAsync(long id, CancellationToken cancellationToken = default)
    {
        await EnsureTemplateAsync(id, false, cancellationToken);
        return await Versions.Query().Where(x => x.BarcodeTemplateId == id).OrderByDescending(x => x.VersionNo)
            .Select(x => new BarcodeTemplateVersionRow(x.Id, x.BarcodeTemplateId, x.VersionNo, x.IsPublished, x.PublishedAt, x.Notes, x.TemplateJson, x.CreatedDate, x.CreatedBy)).ToListAsync(cancellationToken);
    }

    public async Task<BarcodeTemplateVersionRow?> GetDraftAsync(long id, CancellationToken cancellationToken = default)
    {
        var template = await EnsureTemplateAsync(id, false, cancellationToken);
        if (!template.DraftVersionId.HasValue) return null;
        return await Versions.Query().Where(x => x.Id == template.DraftVersionId)
            .Select(x => new BarcodeTemplateVersionRow(x.Id, x.BarcodeTemplateId, x.VersionNo, x.IsPublished, x.PublishedAt, x.Notes, x.TemplateJson, x.CreatedDate, x.CreatedBy)).FirstOrDefaultAsync(cancellationToken);
    }

    public IReadOnlyList<BarcodeSchemaField> GetSchemaFields() =>
    [
        new("stockCode", "Stok Kodu", "STK-0001", "Stok"), new("stockName", "Stok Adı", "Örnek Ürün", "Stok"),
        new("barcode", "Stok Barkodu", "8691234567890", "Stok", "barcode"), new("generatedBarcode", "Kurala Göre Üretilen Barkod", "WMS/STK-0001/SN-2026-000001/00000001", "Stok", "barcode"), new("quantity", "Miktar", "12,50", "Stok"), new("unitCode", "Birim", "ADET", "Stok"),
        new("serialNo", "Seri No", "SN-2026-000001", "İzlenebilirlik", "barcode"), new("lotNo", "Lot No", "LOT-260722", "İzlenebilirlik", "barcode"),
        new("warehouseCode", "Depo Kodu", "01", "Konum"), new("warehouseName", "Depo Adı", "Merkez Depo", "Konum"), new("locationCode", "Raf Kodu", "A-01-02", "Konum", "barcode"),
        new("documentNo", "Belge No", "MK-2026-00000001", "Belge"), new("customerCode", "Cari Kodu", "CARI-001", "Belge"), new("customerName", "Cari Adı", "Örnek Müşteri", "Belge"),
        new("gtin", "GTIN (AI 01)", "08691234567893", "GS1", "barcode"), new("sscc", "SSCC (AI 00)", "386912345678901234", "GS1", "barcode"), new("expiryDate", "SKT (AI 17)", "261231", "GS1")
    ];

    public async Task<long> CreateAsync(BarcodeTemplateUpsertRequest request, CancellationToken cancellationToken = default)
    {
        var normalized = await ValidateMetadataAsync(request, null, cancellationToken);
        var entity = new BarcodeTemplate(); Apply(entity, request, normalized);
        await Templates.AddAsync(entity, cancellationToken); await unitOfWork.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditLogWriteEntry("barcode-template.create", "BarcodeTemplate", entity.Id.ToString(), "Succeeded", "barcode-designer", NewValues: Snapshot(entity), ChangedFields: MetadataFields), cancellationToken);
        return entity.Id;
    }

    public async Task UpdateAsync(long id, BarcodeTemplateUpsertRequest request, CancellationToken cancellationToken = default)
    {
        var entity = await EnsureTemplateAsync(id, true, cancellationToken); var normalized = await ValidateMetadataAsync(request, id, cancellationToken); var old = Snapshot(entity);
        Apply(entity, request, normalized); await SaveAsync(cancellationToken);
        await audit.WriteAsync(new AuditLogWriteEntry("barcode-template.update", "BarcodeTemplate", id.ToString(), "Succeeded", "barcode-designer", OldValues: old, NewValues: Snapshot(entity), ChangedFields: MetadataFields), cancellationToken);
    }

    public async Task DeleteAsync(long id, CancellationToken cancellationToken = default)
    {
        var entity = await EnsureTemplateAsync(id, true, cancellationToken);
        if (entity.PublishedVersionId.HasValue) throw AppException.Conflict(Message(BarcodeDesignerMessageKeys.PublishedDeleteBlocked));
        var old = Snapshot(entity); entity.IsActive = false; await Templates.SoftDeleteAsync(id, cancellationToken); await SaveAsync(cancellationToken);
        await audit.WriteAsync(new AuditLogWriteEntry("barcode-template.delete", "BarcodeTemplate", id.ToString(), "Succeeded", "barcode-designer", OldValues: old, ChangedFields: ["IsDeleted", "IsActive"]), cancellationToken);
    }

    public Task<BarcodeTemplateVersionRow> SaveDraftAsync(long id, BarcodeDraftSaveRequest request, CancellationToken cancellationToken = default)
    {
        ValidateTemplateJson(request.TemplateJson, requireBarcode: false);
        return unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            var template = await EnsureTemplateAsync(id, true, ct);
            var next = (await Versions.Query().Where(x => x.BarcodeTemplateId == id).MaxAsync(x => (int?)x.VersionNo, ct) ?? 0) + 1;
            var version = new BarcodeTemplateVersion { BarcodeTemplateId = id, BranchCode = template.BranchCode, VersionNo = next, TemplateJson = NormalizeJson(request.TemplateJson), Notes = Clean(request.Notes, 500) };
            await Versions.AddAsync(version, ct); await unitOfWork.SaveChangesAsync(ct); template.DraftVersionId = version.Id; await SaveAsync(ct);
            await audit.WriteAsync(new AuditLogWriteEntry("barcode-template.draft", "BarcodeTemplateVersion", version.Id.ToString(), "Succeeded", "barcode-designer", NewValues: new { version.BarcodeTemplateId, version.VersionNo, version.Notes }, ChangedFields: ["TemplateJson", "Notes"]), ct);
            return ToRow(version);
        }, cancellationToken, IsolationLevel.Serializable);
    }

    public Task<BarcodeTemplateVersionRow> PublishAsync(long id, BarcodePublishRequest request, CancellationToken cancellationToken = default) =>
        unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            var template = await EnsureTemplateAsync(id, true, ct);
            var version = await Versions.FirstOrDefaultAsync(x => x.Id == request.VersionId && x.BarcodeTemplateId == id, true, ct) ?? throw AppException.NotFound(Message(BarcodeDesignerMessageKeys.VersionNotFound));
            ValidateTemplateJson(version.TemplateJson, requireBarcode: true);
            version.IsPublished = true; version.PublishedAt = DateTime.UtcNow; template.PublishedVersionId = version.Id; await SaveAsync(ct);
            await audit.WriteAsync(new AuditLogWriteEntry("barcode-template.publish", "BarcodeTemplateVersion", version.Id.ToString(), "Succeeded", "barcode-designer", NewValues: new { version.BarcodeTemplateId, version.VersionNo, version.PublishedAt }, ChangedFields: ["IsPublished", "PublishedAt", "PublishedVersionId"]), ct);
            return ToRow(version);
        }, cancellationToken, IsolationLevel.Serializable);

    private IQueryable<BarcodeTemplateGridRow> Grid() => Templates.Query().Select(x => new BarcodeTemplateGridRow
    {
        Id = x.Id, BranchCode = x.BranchCode, TemplateCode = x.TemplateCode, DisplayName = x.DisplayName,
        LabelType = x.LabelType == BarcodeLabelType.Product ? "Product" : x.LabelType == BarcodeLabelType.SerialLot ? "SerialLot" : x.LabelType == BarcodeLabelType.Location ? "Location" : x.LabelType == BarcodeLabelType.Logistics ? "Logistics" : "Sscc",
        WidthMm = x.WidthMm, HeightMm = x.HeightMm, Dpi = x.Dpi, EngineType = x.EngineType, IsActive = x.IsActive,
        DraftVersionId = x.DraftVersionId, PublishedVersionId = x.PublishedVersionId, CreatedBy = x.CreatedBy, CreatedDate = x.CreatedDate, UpdatedBy = x.UpdatedBy, UpdatedDate = x.UpdatedDate,
        DimensionsSearchText=x.WidthMm+" × "+x.HeightMm+" mm"
    });

    private async Task<BarcodeTemplate> EnsureTemplateAsync(long id, bool tracking, CancellationToken cancellationToken) => await Templates.FindByIdAsync(id, tracking, cancellationToken) ?? throw AppException.NotFound(Message(BarcodeDesignerMessageKeys.NotFound));
    private async Task<Normalized> ValidateMetadataAsync(BarcodeTemplateUpsertRequest request, long? currentId, CancellationToken cancellationToken)
    {
        var branch = string.IsNullOrWhiteSpace(request.BranchCode) ? "0" : request.BranchCode.Trim(); var code = request.TemplateCode?.Trim().ToUpperInvariant() ?? ""; var name = request.DisplayName?.Trim() ?? "";
        if (!CodePattern().IsMatch(code) || name.Length is < 2 or > 150 || request.WidthMm is < 10 or > 300 || request.HeightMm is < 10 or > 500 || request.Dpi is not (203 or 300 or 600)) throw AppException.BadRequest(Message(BarcodeDesignerMessageKeys.InvalidMetadata));
        if (await Templates.AnyAsync(x => x.Id != currentId && x.BranchCode == branch && x.TemplateCode == code, cancellationToken)) throw AppException.Conflict(Message(BarcodeDesignerMessageKeys.DuplicateCode));
        return new(branch, code, name);
    }
    private static void ValidateTemplateJson(string value, bool requireBarcode)
    {
        if (string.IsNullOrWhiteSpace(value) || System.Text.Encoding.UTF8.GetByteCount(value) > MaxTemplateBytes) throw AppException.BadRequest("Etiket tasarımı boş veya çok büyük.");
        try
        {
            using var json = JsonDocument.Parse(value); var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("canvas", out var canvas) || !root.TryGetProperty("elements", out var elements) || elements.ValueKind != JsonValueKind.Array || elements.GetArrayLength() > 100) throw new JsonException();
            var width = canvas.GetProperty("widthMm").GetDecimal(); var height = canvas.GetProperty("heightMm").GetDecimal();
            if (width is < 10 or > 300 || height is < 10 or > 500) throw new JsonException();
            var supported = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "text", "barcode", "qrcode", "datamatrix", "rectangle", "line" }; var hasBarcode = false;
            foreach (var element in elements.EnumerateArray())
            {
                var type = element.GetProperty("type").GetString() ?? ""; if (!supported.Contains(type)) throw new JsonException();
                foreach (var key in new[] { "xMm", "yMm", "widthMm", "heightMm" }) if (!element.TryGetProperty(key, out var number) || !number.TryGetDecimal(out var n) || n < 0 || n > 1000) throw new JsonException();
                if (type is "barcode" or "qrcode" or "datamatrix") { hasBarcode = true; var hasValue = element.TryGetProperty("value", out var v) && !string.IsNullOrWhiteSpace(v.GetString()); var hasBinding = element.TryGetProperty("binding", out var b) && !string.IsNullOrWhiteSpace(b.GetString()); if (!hasValue && !hasBinding) throw new JsonException(); }
            }
            if (requireBarcode && !hasBarcode) throw new JsonException();
        }
        catch (JsonException) { throw AppException.BadRequest("Etiket JSON yapısı veya barkod alanları geçersiz."); }
    }
    private static string NormalizeJson(string value) { using var json = JsonDocument.Parse(value); return JsonSerializer.Serialize(json.RootElement); }
    private async Task SaveAsync(CancellationToken cancellationToken) { try { await unitOfWork.SaveChangesAsync(cancellationToken); } catch (DbUpdateConcurrencyException) { throw AppException.Conflict("Şablon başka bir kullanıcı tarafından değiştirildi. Yenileyip tekrar deneyin."); } }
    private static void Apply(BarcodeTemplate entity, BarcodeTemplateUpsertRequest request, Normalized value) { entity.BranchCode = value.Branch; entity.TemplateCode = value.Code; entity.DisplayName = value.Name; entity.LabelType = request.LabelType; entity.WidthMm = request.WidthMm; entity.HeightMm = request.HeightMm; entity.Dpi = request.Dpi; entity.IsActive = request.IsActive; }
    private static BarcodeTemplateVersionRow ToRow(BarcodeTemplateVersion x) => new(x.Id, x.BarcodeTemplateId, x.VersionNo, x.IsPublished, x.PublishedAt, x.Notes, x.TemplateJson, x.CreatedDate, x.CreatedBy);
    private static object Snapshot(BarcodeTemplate x) => new { x.Id, x.BranchCode, x.TemplateCode, x.DisplayName, x.LabelType, x.WidthMm, x.HeightMm, x.Dpi, x.EngineType, x.IsActive, x.DraftVersionId, x.PublishedVersionId };
    private static string? Clean(string? value, int max) => string.IsNullOrWhiteSpace(value) ? null : value.Trim()[..Math.Min(value.Trim().Length, max)];
    private string Message(string key) => localizer[key].Value;
    private static readonly string[] MetadataFields = ["BranchCode", "TemplateCode", "DisplayName", "LabelType", "WidthMm", "HeightMm", "Dpi", "IsActive"];
    private sealed record Normalized(string Branch, string Code, string Name);
    [GeneratedRegex("^[A-Z0-9_-]{2,50}$", RegexOptions.CultureInvariant)] private static partial Regex CodePattern();
}
