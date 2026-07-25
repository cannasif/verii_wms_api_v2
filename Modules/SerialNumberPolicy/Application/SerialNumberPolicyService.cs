using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using verii_wms_api_v2.Modules.Audit.Application;
using verii_wms_api_v2.Modules.SerialNumberPolicy.Domain;
using verii_wms_api_v2.Modules.Stock.Domain;
using verii_wms_api_v2.Modules.StockMovement.Domain;
using verii_wms_api_v2.Shared;
using verii_wms_api_v2.Shared.Application.Abstractions.Persistence;
using verii_wms_api_v2.Shared.Application.Exceptions;
using StockEntity = verii_wms_api_v2.Modules.Stock.Domain.Stock;

namespace verii_wms_api_v2.Modules.SerialNumberPolicy.Application;

public sealed class SerialNumberPolicyService(IUnitOfWork uow, IAuditLogWriter audit)
    : ISerialNumberPolicyService, ISerialNumberPolicyResolver
{
    private IGenericRepository<SerialNumberRule> Rules => uow.Repository<SerialNumberRule>();

    public async Task<PagedResponse<SerialRuleRow>> GetPagedAsync(PagedRequest request, CancellationToken ct = default)
    {
        var joined = from r in Rules.Query()
                     join s in uow.Repository<StockEntity>().Query() on r.StockId equals s.Id into ss
                     from s in ss.DefaultIfEmpty()
                     select new { Rule = r, Stock = s };
        var q = joined.Select(x => new SerialRuleRow(x.Rule.Id,x.Rule.BranchCode,x.Rule.RuleCode,x.Rule.DisplayName,
            x.Rule.Scope.ToString(),x.Rule.StockId,x.Stock==null?null:x.Stock.ErpStockCode,x.Stock==null?null:x.Stock.StockName,
            x.Rule.StockGroupCode,x.Rule.Version,x.Rule.Priority,x.Rule.MaskTemplate,x.Rule.CharacterSet.ToString(),
            x.Rule.UniquenessScope.ToString(),x.Rule.MinLength,x.Rule.MaxLength,x.Rule.IsRequired,x.Rule.IsActive,
            x.Rule.EffectiveFromUtc,x.Rule.EffectiveToUtc,x.Rule.Description,Convert.ToBase64String(x.Rule.RowVersion),
            x.Rule.CreatedBy,x.Rule.CreatedDate));
        var search=request.Search?.Trim();
        q=q.Where(x=>string.IsNullOrWhiteSpace(search)||x.RuleCode.Contains(search)||x.DisplayName.Contains(search)
            ||(x.StockCode!=null&&x.StockCode.Contains(search))||(x.StockGroupCode!=null&&x.StockGroupCode.Contains(search)));
        return await q.ApplyAdvancedFilters(request).ApplySort(request,nameof(SerialRuleRow.Id)).ToPagedResponseAsync(request,ct);
    }

    public async Task<long> CreateAsync(SerialRuleUpsertRequest request,long actor,CancellationToken ct=default)
    {
        var entity=new SerialNumberRule(); await Apply(entity,request,null,ct); entity.Version=1;
        entity.CreatedBy=actor; entity.CreatedDate=DateTime.UtcNow; await Rules.AddAsync(entity,ct); await uow.SaveChangesAsync(ct);
        await audit.WriteAsync(new("serial-rule.create",nameof(SerialNumberRule),entity.Id.ToString(),"Succeeded","serial-number-policy",NewValues:Snapshot(entity),ChangedFields:["Rule"]),ct);
        return entity.Id;
    }

    public async Task<long> CreateNextVersionAsync(long id,SerialRuleUpsertRequest request,long actor,string? token,CancellationToken ct=default)
    {
        var current=await Rules.FindByIdAsync(id,true,ct)??throw AppException.NotFound("Seri kuralı bulunamadı.");
        ApplyVersion(current,token); var next=new SerialNumberRule(); await Apply(next,request,current.Id,ct);
        next.RuleCode=current.RuleCode; next.Version=current.Version+1; next.CreatedBy=actor; next.CreatedDate=DateTime.UtcNow;
        current.IsActive=false; current.EffectiveToUtc=DateTimeOffset.UtcNow; current.UpdatedBy=actor; current.UpdatedDate=DateTime.UtcNow;
        await Rules.AddAsync(next,ct); await uow.SaveChangesAsync(ct);
        await audit.WriteAsync(new("serial-rule.version",nameof(SerialNumberRule),next.Id.ToString(),"Succeeded","serial-number-policy",OldValues:Snapshot(current),NewValues:Snapshot(next),ChangedFields:["Version"]),ct);
        return next.Id;
    }

    public async Task DeleteAsync(long id,long actor,CancellationToken ct=default)
    {
        var entity=await Rules.FindByIdAsync(id,true,ct)??throw AppException.NotFound("Seri kuralı bulunamadı.");
        entity.IsActive=false; entity.DeletedBy=actor; await Rules.SoftDeleteAsync(id,ct); await uow.SaveChangesAsync(ct);
    }

    public Task<SerialValidationResult> ValidateAsync(ValidateSerialRequest r,CancellationToken ct=default)
        =>ValidateAsync(r.BranchCode,r.StockId,r.YapCodeId,r.SerialNo,ct);

    public async Task<SerialValidationResult> ValidateAsync(string branchCode,long stockId,long? yapCodeId,string? serialNo,CancellationToken ct=default)
    {
        var branch=Branch(branchCode); var stock=await uow.Repository<StockEntity>().FirstOrDefaultAsync(x=>x.Id==stockId&&x.BranchCode==branch,false,ct)
            ??throw AppException.BadRequest("Stok bulunamadı.");
        var now=DateTimeOffset.UtcNow;
        var candidates=await Rules.Query().Where(x=>x.BranchCode==branch&&x.IsActive&&x.EffectiveFromUtc<=now
            &&(!x.EffectiveToUtc.HasValue||x.EffectiveToUtc>now)
            &&(x.Scope==SerialRuleScope.BranchDefault||(x.Scope==SerialRuleScope.Stock&&x.StockId==stockId)
                ||(x.Scope==SerialRuleScope.StockGroup&&x.StockGroupCode==stock.GroupCode))).ToListAsync(ct);
        var rule=candidates.OrderByDescending(x=>x.Scope).ThenByDescending(x=>x.Priority).ThenByDescending(x=>x.Version).FirstOrDefault();
        if(rule is null) return new(serialNo?.Trim(),true,"NoRule",null,null,null,null,null);
        var value=serialNo; if(rule.TrimWhitespace)value=value?.Trim(); if(rule.NormalizeToUpper)value=value?.ToUpperInvariant();
        if(string.IsNullOrWhiteSpace(value))
            return rule.IsRequired?Fail(rule,value,"Seri numarası zorunludur."):Pass(rule,value);
        if(value.Length<rule.MinLength||value.Length>rule.MaxLength) return Fail(rule,value,$"Seri uzunluğu {rule.MinLength}-{rule.MaxLength} karakter olmalıdır.");
        if(!Allowed(value,rule.CharacterSet)) return Fail(rule,value,"Seri numarası izin verilen karakter kümesine uymuyor.");
        Regex regex; try{regex=new Regex(Compile(rule.MaskTemplate,stock.ErpStockCode,stock.GroupCode),RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(200));}
        catch(ArgumentException){throw AppException.Conflict("Yayımlanmış seri maskesi geçersizdir.");}
        if(!regex.IsMatch(value)) return Fail(rule,value,$"Seri beklenen maskeye uymuyor: {rule.MaskTemplate}");
        var duplicate=await IsDuplicate(rule,stockId,yapCodeId,value,ct);
        return duplicate?Fail(rule,value,"Seri numarası seçilen benzersizlik kapsamında daha önce kullanılmış."):Pass(rule,value);
    }

    private async Task Apply(SerialNumberRule e,SerialRuleUpsertRequest r,long? currentId,CancellationToken ct)
    {
        if(string.IsNullOrWhiteSpace(r.RuleCode)||string.IsNullOrWhiteSpace(r.DisplayName)||r.Priority is <0 or >1000
            ||r.MinLength<1||r.MaxLength<r.MinLength||r.MaxLength>100||r.EffectiveToUtc<=r.EffectiveFromUtc)
            throw AppException.BadRequest("Seri kuralı alanları geçersiz.");
        ValidateTemplate(r.MaskTemplate);
        var branch=Branch(r.BranchCode); var stockId=r.Scope==SerialRuleScope.Stock?r.StockId:null;
        var group=r.Scope==SerialRuleScope.StockGroup?Clean(r.StockGroupCode,50):null;
        if(r.Scope==SerialRuleScope.Stock&&(!stockId.HasValue||!await uow.Repository<StockEntity>().AnyAsync(x=>x.Id==stockId&&x.BranchCode==branch,ct))) throw AppException.BadRequest("Kapsam stoğu bulunamadı.");
        if(r.Scope==SerialRuleScope.StockGroup&&string.IsNullOrWhiteSpace(group)) throw AppException.BadRequest("Stok grubu zorunludur.");
        if(await Rules.AnyAsync(x=>x.Id!=currentId&&x.BranchCode==branch&&x.IsActive&&x.Scope==r.Scope&&x.StockId==stockId&&x.StockGroupCode==group,ct))
            throw AppException.Conflict("Bu kapsam için aktif bir seri kuralı zaten var.");
        e.BranchCode=branch;e.RuleCode=r.RuleCode.Trim().ToUpperInvariant();e.DisplayName=r.DisplayName.Trim();e.Scope=r.Scope;e.StockId=stockId;e.StockGroupCode=group;
        e.Priority=r.Priority;e.MaskTemplate=r.MaskTemplate.Trim();e.CharacterSet=r.CharacterSet;e.UniquenessScope=r.UniquenessScope;e.MinLength=r.MinLength;e.MaxLength=r.MaxLength;
        e.TrimWhitespace=r.TrimWhitespace;e.NormalizeToUpper=r.NormalizeToUpper;e.IsRequired=r.IsRequired;e.IsActive=r.IsActive;e.EffectiveFromUtc=r.EffectiveFromUtc.ToUniversalTime();
        e.EffectiveToUtc=r.EffectiveToUtc?.ToUniversalTime();e.Description=Clean(r.Description,500);
    }
    private async Task<bool> IsDuplicate(SerialNumberRule r,long stockId,long? yapId,string value,CancellationToken ct)
    {
        var q=uow.Repository<StockMovementEntry>().Query().Where(x=>x.SerialNo==value);
        if(r.UniquenessScope!=SerialUniquenessScope.Global)q=q.Where(x=>x.StockId==stockId);
        if(r.UniquenessScope==SerialUniquenessScope.StockAndYapCode)q=q.Where(x=>x.YapCodeId==yapId);
        return await q.AnyAsync(ct);
    }
    private static string Compile(string mask,string stock,string? group)
    {
        var result=new StringBuilder("^"); for(var i=0;i<mask.Length;)
        { if(mask[i]!='{'){result.Append(Regex.Escape(mask[i++].ToString()));continue;} var end=mask.IndexOf('}',i);if(end<0)throw new ArgumentException();
          var t=mask[(i+1)..end]; result.Append(t switch{"YYYY"=>@"\d{4}","YY"=>@"\d{2}","MM"=>@"(?:0[1-9]|1[0-2])","DD"=>@"(?:0[1-9]|[12]\d|3[01])",
            "STOCK"=>Regex.Escape(stock.ToUpperInvariant()),"GROUP"=>Regex.Escape((group??"").ToUpperInvariant()),_ when Regex.IsMatch(t,@"^[NAX]:[1-9]\d?$")=>Token(t),_=>throw new ArgumentException()});i=end+1;} return result.Append('$').ToString();
    }
    private static string Token(string t){var p=t.Split(':');return p[0] switch{"N"=>$@"\d{{{p[1]}}}","A"=>$@"[A-Z]{{{p[1]}}}","X"=>$@"[A-Z0-9]{{{p[1]}}}",_=>throw new ArgumentException()};}
    private static void ValidateTemplate(string x){if(string.IsNullOrWhiteSpace(x)||x.Length>250)throw AppException.BadRequest("Maske zorunludur.");try{_=Compile(x,"STOCK","GROUP");}catch{throw AppException.BadRequest("Maske geçersiz. Desteklenenler: {STOCK}, {GROUP}, {YYYY}, {YY}, {MM}, {DD}, {N:n}, {A:n}, {X:n}.");}}
    private static bool Allowed(string x,SerialCharacterSet s)=>s switch{SerialCharacterSet.Numeric=>Regex.IsMatch(x,@"^\d+$"),SerialCharacterSet.UpperAlphaNumeric=>Regex.IsMatch(x,@"^[A-Z0-9._\-/]+$"),SerialCharacterSet.AlphaNumeric=>Regex.IsMatch(x,@"^[A-Za-z0-9._\-/]+$"),_=>Regex.IsMatch(x,@"^[A-Z0-9!""%&'()*+,\-./:;<=>?_ ]+$")};
    private static SerialValidationResult Pass(SerialNumberRule r,string? v)=>new(v,true,r.Scope.ToString(),r.Id,r.Version,r.RuleCode,r.MaskTemplate,null);
    private static SerialValidationResult Fail(SerialNumberRule r,string? v,string error)=>new(v,false,r.Scope.ToString(),r.Id,r.Version,r.RuleCode,r.MaskTemplate,error);
    private static object Snapshot(SerialNumberRule x)=>new{x.Id,x.RuleCode,x.Version,x.Scope,x.StockId,x.StockGroupCode,x.MaskTemplate,x.CharacterSet,x.UniquenessScope,x.EffectiveFromUtc,x.EffectiveToUtc,x.IsActive};
    private static string Branch(string? x)=>string.IsNullOrWhiteSpace(x)?"0":x.Trim(); private static string? Clean(string? x,int max){var v=string.IsNullOrWhiteSpace(x)?null:x.Trim();return v?.Length>max?v[..max]:v;}
    private static void ApplyVersion(SerialNumberRule x,string? t){if(string.IsNullOrWhiteSpace(t))return;try{x.RowVersion=Convert.FromBase64String(t);}catch{throw AppException.Conflict("Kayıt güncellik bilgisi geçersiz.");}}
}
