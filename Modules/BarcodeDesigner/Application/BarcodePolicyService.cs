using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using verii_wms_api_v2.Modules.Audit.Application;
using verii_wms_api_v2.Modules.BarcodeDesigner.Domain;
using verii_wms_api_v2.Shared;
using verii_wms_api_v2.Shared.Application.Abstractions.Persistence;
using verii_wms_api_v2.Shared.Application.Exceptions;

namespace verii_wms_api_v2.Modules.BarcodeDesigner.Application;

public sealed partial class BarcodePolicyService(IUnitOfWork unitOfWork, IAuditLogWriter audit) : IBarcodePolicyService
{
    private const string GlobalPolicyKey = "GLOBAL";
    private static readonly HashSet<string> AllowedDateFormats = ["yyyyMMdd", "yyMMdd", "yyyyMM", "yyyy"];
    private IGenericRepository<BarcodePolicy> Policies => unitOfWork.Repository<BarcodePolicy>();
    private IGenericRepository<BarcodePolicyProfile> Profiles => unitOfWork.Repository<BarcodePolicyProfile>();
    private IGenericRepository<BarcodePolicyProfileSegment> Segments => unitOfWork.Repository<BarcodePolicyProfileSegment>();
    private IGenericRepository<GeneratedBarcode> Generated => unitOfWork.Repository<GeneratedBarcode>();

    public async Task<BarcodePolicyResponse> GetAsync(CancellationToken ct = default)
    {
        var policy = await RequirePolicy(false, ct);
        var profiles = await Profiles.Query().Where(x => x.BarcodePolicyId == policy.Id).OrderBy(x => x.Scope).ToListAsync(ct);
        var ids = profiles.Select(x => x.Id).ToArray();
        var segments = await Segments.Query().Where(x => ids.Contains(x.BarcodePolicyProfileId)).OrderBy(x => x.Order).ToListAsync(ct);
        return Map(policy, profiles, segments);
    }

    public Task<BarcodePolicyResponse> UpdateProfileAsync(BarcodePolicyScope scope, BarcodePolicyProfileUpdateRequest request, CancellationToken ct = default)
    {
        Validate(scope, request);
        return unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            var policy = await RequirePolicy(true, token);
            var profile = await RequireProfile(policy.Id, scope, true, token);
            ValidateConcurrency(profile.RowVersion, request.ConcurrencyToken);
            var old = new { profile.Scope, profile.DisplayName, profile.Prefix, profile.Separator, profile.IsEnabled, policy.CurrentVersion };
            profile.DisplayName = request.DisplayName.Trim(); profile.Prefix = CleanAndNormalize(request.Prefix, request.Separator); profile.Separator = request.Separator.Trim(); profile.IsEnabled = request.IsEnabled;
            var current = await Segments.Query(true).Where(x => x.BarcodePolicyProfileId == profile.Id).ToListAsync(token);
            foreach (var item in current) { item.IsDeleted = true; item.DeletedDate = DateTime.UtcNow; }
            await unitOfWork.SaveChangesAsync(token);
            await Segments.AddRangeAsync(CreateSegments(profile, request.Segments), token);
            policy.CurrentVersion++;
            try { await unitOfWork.SaveChangesAsync(token); }
            catch (DbUpdateConcurrencyException) { throw AppException.Conflict("Barkod politikası başka bir kullanıcı tarafından güncellendi. Ekranı yenileyip tekrar deneyin."); }
            await audit.WriteAsync(new("barcode-policy.profile.update", "BarcodePolicyProfile", profile.Id.ToString(), "Succeeded", "barcode-designer", OldValues: old, NewValues: new { profile.Scope, profile.DisplayName, profile.Prefix, profile.Separator, profile.IsEnabled, policy.CurrentVersion }, ChangedFields: ["DisplayName", "Prefix", "Separator", "IsEnabled", "Segments", "CurrentVersion"]), token);
            var profiles = await Profiles.Query().Where(x => x.BarcodePolicyId == policy.Id).OrderBy(x => x.Scope).ToListAsync(token);
            var ids = profiles.Select(x => x.Id).ToArray(); var segments = await Segments.Query().Where(x => ids.Contains(x.BarcodePolicyProfileId)).OrderBy(x => x.Order).ToListAsync(token);
            return Map(policy, profiles, segments);
        }, ct, IsolationLevel.Serializable);
    }

    public async Task<BarcodePreviewResponse> PreviewAsync(BarcodePolicyScope scope, BarcodeGenerateRequest request, CancellationToken ct = default)
    {
        var policy = await RequirePolicy(false, ct); var profile = await RequireProfile(policy.Id, scope, false, ct);
        EnsureEnabled(policy, profile); var segments = await GetSegments(profile.Id, ct);
        return new(Build(profile, segments, request, profile.NextSequence), profile.NextSequence, false, policy.CurrentVersion, scope.ToString());
    }

    public Task<BarcodePreviewResponse> GenerateAsync(BarcodePolicyScope scope, BarcodeGenerateRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey) || request.IdempotencyKey.Trim().Length > 200) throw AppException.BadRequest("İdempotency anahtarı zorunludur ve en fazla 200 karakter olabilir.");
        return unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            var policy = await RequirePolicy(true, token); var profile = await RequireProfile(policy.Id, scope, true, token); EnsureEnabled(policy, profile);
            var idemHash = Hash($"{policy.Id}:{scope}:{Normalize(request.IdempotencyKey, null)}");
            var existing = await Generated.FirstOrDefaultAsync(x => x.BarcodePolicyId == policy.Id && x.Scope == scope && x.IdempotencyHash == idemHash, false, token);
            if (existing is not null) return new(existing.BarcodeValue, existing.SequenceNo, true, existing.PolicyVersion, existing.Scope.ToString());
            var segments = await GetSegments(profile.Id, token); var sequence = profile.NextSequence; var value = Build(profile, segments, request, sequence); var barcodeHash = Hash(value);
            if (await Generated.AnyAsync(x => x.BarcodeHash == barcodeHash, token)) throw AppException.Conflict("Üretilen barkod başka bir kayıtta mevcut. Politika profilini kontrol edin.");
            await Generated.AddAsync(new GeneratedBarcode { BranchCode=policy.BranchCode, BarcodePolicyId=policy.Id, BarcodePolicyProfileId=profile.Id, PolicyVersion=policy.CurrentVersion, Scope=scope, BarcodeValue=value, BarcodeHash=barcodeHash, IdempotencyHash=idemHash, StockCode=Clean(request.StockCode), SerialNo=Clean(request.SerialNo), YapCode=Clean(request.YapCode), LotNo=Clean(request.LotNo), WarehouseCode=Clean(request.WarehouseCode), LocationCode=Clean(request.LocationCode), DocumentNo=Clean(request.DocumentNo), SequenceNo=sequence, GeneratedAt=DateTime.UtcNow }, token);
            profile.NextSequence++;
            try { await unitOfWork.SaveChangesAsync(token); }
            catch (DbUpdateConcurrencyException) { throw AppException.Conflict("Aynı anda barkod üretildi. İsteği aynı idempotency anahtarıyla tekrar gönderin."); }
            await audit.WriteAsync(new("barcode.generate", "GeneratedBarcode", value, "Succeeded", "barcode-designer", NewValues: new { Scope=scope.ToString(), policy.CurrentVersion, value, sequence }, ChangedFields: ["BarcodeValue", "SequenceNo", "PolicyVersion", "Scope"]), token);
            return new BarcodePreviewResponse(value, sequence, true, policy.CurrentVersion, scope.ToString());
        }, ct, IsolationLevel.Serializable);
    }

    public async Task<PagedResponse<GeneratedBarcodeRow>> GetGeneratedPagedAsync(PagedRequest request, CancellationToken ct = default)
    {
        var search=request.LegacySearch?.Trim();
        var rows=Generated.Query()
            .Where(x=>string.IsNullOrWhiteSpace(search)||x.BarcodeValue.Contains(search)||(x.StockCode??"").Contains(search)||(x.SerialNo??"").Contains(search)||(x.LocationCode??"").Contains(search)||(x.DocumentNo??"").Contains(search))
            .Select(x=>new GeneratedBarcodeRow(x.Id,x.Scope.ToString(),x.PolicyVersion,x.BarcodeValue,x.StockCode,x.SerialNo,x.YapCode,x.LotNo,x.WarehouseCode,x.LocationCode,x.DocumentNo,x.SequenceNo,x.GeneratedAt,x.CreatedBy));
        var filtered=rows.ApplyAdvancedFilters(request);
        var sorted=string.IsNullOrWhiteSpace(request.SortBy)
            ? filtered.OrderByDescending(x=>x.GeneratedAt)
            : filtered.ApplySort(request,nameof(GeneratedBarcodeRow.GeneratedAt));
        return await sorted.ToPagedResponseAsync(request,ct);
    }

    private static void Validate(BarcodePolicyScope scope, BarcodePolicyProfileUpdateRequest request)
    {
        var separator=request.Separator?.Trim()??"";
        if(string.IsNullOrWhiteSpace(request.DisplayName)||request.DisplayName.Trim().Length is <2 or >150||separator.Length is <1 or >5||request.Segments is null||request.Segments.Count is <2 or >12) throw AppException.BadRequest("Barkod profil bilgileri geçersiz.");
        if(request.Segments.Count(x=>x.SegmentType==BarcodePolicySegmentType.Sequence)!=1) throw AppException.BadRequest("Benzersizlik için profilde tam bir adet Sıra segmenti bulunmalıdır.");
        var orders=request.Segments.Select(x=>x.Order).Order().ToArray(); if(!orders.SequenceEqual(Enumerable.Range(1,orders.Length))) throw AppException.BadRequest("Segment sıraları 1'den başlayarak kesintisiz olmalıdır.");
        foreach(var item in request.Segments){if(item.SegmentType==BarcodePolicySegmentType.Field&&item.SourceField is null)throw AppException.BadRequest("Alan segmentinde kaynak zorunludur.");if(item.SegmentType==BarcodePolicySegmentType.Literal&&string.IsNullOrWhiteSpace(item.LiteralValue))throw AppException.BadRequest("Sabit segment değeri zorunludur.");if(item.SegmentType==BarcodePolicySegmentType.Sequence&&item.SequenceLength is <4 or >18)throw AppException.BadRequest("Sıra uzunluğu 4-18 arasında olmalıdır.");if(item.SegmentType==BarcodePolicySegmentType.Date&&!AllowedDateFormats.Contains(item.DateFormat))throw AppException.BadRequest("Tarih biçimi izin verilen değerlerden biri olmalıdır.");}
        BarcodePolicyField[] required=scope switch{BarcodePolicyScope.ProductSerial=>[BarcodePolicyField.StockCode,BarcodePolicyField.SerialNo],BarcodePolicyScope.ProductLot=>[BarcodePolicyField.StockCode,BarcodePolicyField.LotNo],BarcodePolicyScope.Location=>[BarcodePolicyField.WarehouseCode,BarcodePolicyField.LocationCode],BarcodePolicyScope.Logistics=>[BarcodePolicyField.DocumentNo],BarcodePolicyScope.Document=>[BarcodePolicyField.DocumentNo],_=>[]};
        foreach(var field in required)if(!request.Segments.Any(x=>x.SegmentType==BarcodePolicySegmentType.Field&&x.SourceField==field&&x.IsRequired))throw AppException.BadRequest($"{scope} profili için {field} zorunlu alan olmalıdır.");
    }

    private static string Build(BarcodePolicyProfile profile,IReadOnlyList<BarcodePolicyProfileSegment> segments,BarcodeGenerateRequest request,long sequence)
    {
        var values=new Dictionary<BarcodePolicyField,string?>{{BarcodePolicyField.StockCode,request.StockCode},{BarcodePolicyField.SerialNo,request.SerialNo},{BarcodePolicyField.YapCode,request.YapCode},{BarcodePolicyField.LotNo,request.LotNo},{BarcodePolicyField.WarehouseCode,request.WarehouseCode},{BarcodePolicyField.LocationCode,request.LocationCode},{BarcodePolicyField.DocumentNo,request.DocumentNo}};
        var parts=new List<string>();if(!string.IsNullOrWhiteSpace(profile.Prefix))parts.Add(profile.Prefix);
        foreach(var item in segments.OrderBy(x=>x.Order)){string? value=item.SegmentType switch{BarcodePolicySegmentType.Field=>values[item.SourceField!.Value],BarcodePolicySegmentType.Literal=>item.LiteralValue,BarcodePolicySegmentType.Sequence=>sequence.ToString(CultureInfo.InvariantCulture).PadLeft(item.SequenceLength,'0'),BarcodePolicySegmentType.Date=>DateTime.UtcNow.ToString(item.DateFormat,CultureInfo.InvariantCulture),_=>null};value=Normalize(value,profile.Separator);if(string.IsNullOrWhiteSpace(value)){if(item.IsRequired)throw AppException.BadRequest($"{item.SourceField?.ToString()??item.SegmentType.ToString()} alanı zorunludur.");continue;}parts.Add(item.Transform==BarcodeValueTransform.Lower?value.ToLowerInvariant():item.Transform==BarcodeValueTransform.Upper?value.ToUpperInvariant():value);}
        var result=string.Join(profile.Separator,parts);if(result.Length is <4 or >120)throw AppException.BadRequest("Üretilen barkod 4-120 karakter aralığında olmalıdır.");return result;
    }

    private async Task<BarcodePolicy> RequirePolicy(bool tracking,CancellationToken ct)=>await Policies.FirstOrDefaultAsync(x=>x.PolicyKey==GlobalPolicyKey,tracking,ct)??throw AppException.NotFound("Genel barkod politikası bulunamadı.");
    private async Task<BarcodePolicyProfile> RequireProfile(long policyId,BarcodePolicyScope scope,bool tracking,CancellationToken ct)=>await Profiles.FirstOrDefaultAsync(x=>x.BarcodePolicyId==policyId&&x.Scope==scope,tracking,ct)??throw AppException.NotFound("Barkod politika profili bulunamadı.");
    private async Task<IReadOnlyList<BarcodePolicyProfileSegment>> GetSegments(long profileId,CancellationToken ct)=>await Segments.Query().Where(x=>x.BarcodePolicyProfileId==profileId).OrderBy(x=>x.Order).ToListAsync(ct);
    private static void EnsureEnabled(BarcodePolicy policy,BarcodePolicyProfile profile){if(!policy.IsActive||!profile.IsEnabled)throw AppException.Conflict("Barkod politikası veya seçilen kapsam profili pasif.");}
    private static void ValidateConcurrency(byte[] actual,string supplied){if(string.IsNullOrWhiteSpace(supplied))throw AppException.Conflict("Güncellik anahtarı bulunamadı. Ekranı yenileyin.");byte[] expected;try{expected=Convert.FromBase64String(supplied);}catch{throw AppException.Conflict("Güncellik anahtarı geçersiz.");}if(!actual.SequenceEqual(expected))throw AppException.Conflict("Profil başka bir kullanıcı tarafından değiştirilmiş. Ekranı yenileyin.");}
    private static IEnumerable<BarcodePolicyProfileSegment> CreateSegments(BarcodePolicyProfile p,IEnumerable<BarcodePolicySegmentRequest> items)=>items.Select(x=>new BarcodePolicyProfileSegment{BranchCode=p.BranchCode,BarcodePolicyProfileId=p.Id,Order=x.Order,SegmentType=x.SegmentType,SourceField=x.SourceField,LiteralValue=Clean(x.LiteralValue),IsRequired=x.IsRequired,Transform=x.Transform,SequenceLength=x.SequenceLength,DateFormat=string.IsNullOrWhiteSpace(x.DateFormat)?"yyyyMMdd":x.DateFormat.Trim()});
    private static BarcodePolicyResponse Map(BarcodePolicy policy,IEnumerable<BarcodePolicyProfile> profiles,IEnumerable<BarcodePolicyProfileSegment> all)=>new(policy.Id,policy.PolicyKey,policy.DisplayName,policy.CurrentVersion,policy.IsActive,Convert.ToBase64String(policy.RowVersion),profiles.Select(p=>new BarcodePolicyProfileRow(p.Id,p.Scope.ToString(),p.DisplayName,p.Prefix,p.Separator,p.NextSequence,p.IsEnabled,Convert.ToBase64String(p.RowVersion),all.Where(x=>x.BarcodePolicyProfileId==p.Id).OrderBy(x=>x.Order).Select(x=>new BarcodePolicySegmentRow(x.Id,x.Order,x.SegmentType.ToString(),x.SourceField?.ToString(),x.LiteralValue,x.IsRequired,x.Transform.ToString(),x.SequenceLength,x.DateFormat)).ToList(),p.UpdatedBy,p.UpdatedDate)).ToList(),policy.UpdatedDate,policy.UpdatedBy);
    private static string? Clean(string? value)=>string.IsNullOrWhiteSpace(value)?null:value.Trim();
    private static string? CleanAndNormalize(string? value,string separator)=>string.IsNullOrWhiteSpace(value)?null:Normalize(value,separator);
    private static string Normalize(string? value,string? separator){var result=(value??"").Normalize(NormalizationForm.FormKC).Trim();if(!string.IsNullOrEmpty(separator))result=result.Replace(separator,string.Empty,StringComparison.Ordinal);return InvalidChars().Replace(result,string.Empty);}
    private static string Hash(string value)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    [GeneratedRegex("[^A-Za-z0-9._-]+",RegexOptions.CultureInvariant)]private static partial Regex InvalidChars();
}
