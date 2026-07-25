using System.Text;
using System.Text.RegularExpressions;
using System.Data;
using Microsoft.EntityFrameworkCore;
using verii_wms_api_v2.Modules.Audit.Application;
using verii_wms_api_v2.Modules.SerialNumberPolicy.Domain;
using verii_wms_api_v2.Modules.Stock.Domain;
using verii_wms_api_v2.Modules.StockMovement.Domain;
using verii_wms_api_v2.Modules.StockTracking.Domain;
using verii_wms_api_v2.Modules.WarehouseOperations.Domain;
using verii_wms_api_v2.Shared;
using verii_wms_api_v2.Shared.Application.Abstractions.Persistence;
using verii_wms_api_v2.Shared.Application.Exceptions;
using StockEntity = verii_wms_api_v2.Modules.Stock.Domain.Stock;

namespace verii_wms_api_v2.Modules.SerialNumberPolicy.Application;

public sealed class SerialNumberPolicyService(IUnitOfWork uow, IAuditLogWriter audit, ISerialSequenceAllocator sequenceAllocator)
    : ISerialNumberPolicyService, ISerialNumberPolicyResolver
{
    private IGenericRepository<SerialNumberRule> Rules => uow.Repository<SerialNumberRule>();
    private IGenericRepository<StockSerialRegistry> SerialRegistry => uow.Repository<StockSerialRegistry>();

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
        next.RuleCode=current.RuleCode; next.Version=current.Version+1; next.NextSequence=current.NextSequence; next.CreatedBy=actor; next.CreatedDate=DateTime.UtcNow;
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

    public Task<GenerateStockSerialsResult> GenerateAsync(
        GenerateStockSerialsRequest request, long actor, CancellationToken ct = default) =>
        uow.ExecuteInTransactionAsync(async token =>
        {
            if (request.Quantity is < 1 or > 10000)
                throw AppException.BadRequest("Tek istekte 1-10000 arasında seri üretilebilir.");
            var requestKey = request.IdempotencyKey?.Trim();
            if (string.IsNullOrWhiteSpace(requestKey) || requestKey.Length > 100)
                throw AppException.BadRequest("Geçerli bir idempotency anahtarı zorunludur.");

            var branch = Branch(request.BranchCode);
            var stock = await uow.Repository<StockEntity>().FirstOrDefaultAsync(
                x => x.Id == request.StockId && x.BranchCode == branch, false, token)
                ?? throw AppException.NotFound("Stok bulunamadı.");
            var existing = await SerialRegistry.Query()
                .Where(x => x.StockId == stock.Id && x.GenerationRequestKey == requestKey)
                .OrderBy(x => x.GenerationOrdinal).ToListAsync(token);
            if (existing.Count > 0)
            {
                if (existing.Count != request.Quantity)
                    throw AppException.Conflict("Aynı işlem anahtarı farklı seri adediyle tekrar kullanılamaz.");
                return Result(stock, existing[0].SerialNumberRuleId, "Kayıtlı üretim", true, existing);
            }

            var now = DateTimeOffset.UtcNow;
            var policy = await ResolvePolicyAsync(branch, stock, now, token);
            if (!policy.RequireSerial || !policy.AutoGenerateSerials)
                throw AppException.BadRequest("Bu stokta otomatik seri üretimi açık değildir.");
            if (policy.SerialQuantityRule != SerialQuantityRule.OneSerialPerBaseUnit)
                throw AppException.Conflict("Otomatik seri üretimi için stok miktar kadar seriyle takip edilmelidir.");

            var rule = await Rules.Query(true)
                .Where(x => x.BranchCode == branch && x.Scope == SerialRuleScope.Stock
                    && x.StockId == stock.Id && x.IsActive && x.EffectiveFromUtc <= now
                    && (!x.EffectiveToUtc.HasValue || x.EffectiveToUtc > now))
                .OrderByDescending(x => x.Version).FirstOrDefaultAsync(token)
                ?? throw AppException.Conflict("Stok seri maskesi tanımlı değildir.");
            ValidateAutomaticTemplate(rule.MaskTemplate);

            var firstSequence = await sequenceAllocator.AllocateAsync(rule.Id, request.Quantity, token);
            var rows = new List<StockSerialRegistry>(request.Quantity);
            for (var ordinal = 0; ordinal < request.Quantity; ordinal++)
            {
                var sequence = firstSequence + ordinal;
                var serial = Render(rule.MaskTemplate, stock.ErpStockCode, stock.GroupCode, sequence, now);
                if (rule.NormalizeToUpper) serial = serial.ToUpperInvariant();
                if (serial.Length < rule.MinLength || serial.Length > rule.MaxLength || !Allowed(serial, rule.CharacterSet))
                    throw AppException.Conflict($"Üretilen seri stok kuralına uymuyor: {serial}");
                rows.Add(new StockSerialRegistry
                {
                    BranchCode = branch, StockId = stock.Id, SerialNo = serial,
                    NormalizedSerialNo = serial.ToUpperInvariant(), Status = StockSerialStatus.Reserved,
                    SerialNumberRuleId = rule.Id, SequenceNumber = sequence,
                    GenerationRequestKey = requestKey, GenerationOrdinal = ordinal + 1,
                    SourceOperationType = Clean(request.SourceOperationType, 50),
                    SourceOperationId = request.SourceOperationId, ReservedAtUtc = now,
                    CreatedBy = actor, CreatedDate = DateTime.UtcNow
                });
            }
            await SerialRegistry.AddRangeAsync(rows, token);
            await uow.SaveChangesAsync(token);
            await audit.WriteAsync(new("stock-serial.generate", nameof(StockSerialRegistry), requestKey,
                "Succeeded", "serial-number-policy",
                NewValues: new { stock.Id, stock.ErpStockCode, request.Quantity, FirstSequence = firstSequence },
                ChangedFields: ["Serials"]), token);
            return Result(stock, rule.Id, rule.MaskTemplate, false, rows);
        }, ct, IsolationLevel.Serializable);

    public Task<VoidGeneratedSerialsResult> VoidAsync(
        VoidGeneratedSerialsRequest request, long actor, CancellationToken ct = default) =>
        uow.ExecuteInTransactionAsync<VoidGeneratedSerialsResult>(async token =>
        {
            var branch = Branch(request.BranchCode);
            var requestKey = request.IdempotencyKey?.Trim();
            var reason = Clean(request.Reason, 500);
            if (string.IsNullOrWhiteSpace(requestKey) || requestKey.Length > 100 || string.IsNullOrWhiteSpace(reason))
                throw AppException.BadRequest("Seri üretim anahtarı ve iptal nedeni zorunludur.");

            var rows = await SerialRegistry.Query(true)
                .Where(x => x.BranchCode == branch && x.StockId == request.StockId
                    && x.GenerationRequestKey == requestKey)
                .OrderBy(x => x.GenerationOrdinal)
                .ToListAsync(token);
            if (rows.Count == 0)
                throw AppException.NotFound("İptal edilecek otomatik seri üretimi bulunamadı.");
            if (rows.All(x => x.Status == StockSerialStatus.Voided))
                return new(request.StockId, requestKey, rows.Count, true);
            if (rows.Any(x => x.Status != StockSerialStatus.Reserved))
                throw AppException.Conflict("Kullanılmış veya stoğa alınmış seriler iptal edilemez.");

            var now = DateTimeOffset.UtcNow;
            foreach (var row in rows)
            {
                row.Status = StockSerialStatus.Voided;
                row.VoidedAtUtc = now;
                row.VoidedReason = reason;
                row.UpdatedBy = actor;
                row.UpdatedDate = DateTime.UtcNow;
            }
            await uow.SaveChangesAsync(token);
            await audit.WriteAsync(new("stock-serial.void", nameof(StockSerialRegistry), requestKey,
                "Succeeded", "serial-number-policy",
                NewValues: new { request.StockId, VoidedCount = rows.Count, Reason = reason },
                ChangedFields: ["Status", "VoidedAtUtc", "VoidedReason"]), token);
            return new(request.StockId, requestKey, rows.Count, false);
        }, ct, IsolationLevel.Serializable);

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
        if (await SerialRegistry.AnyAsync(x => x.StockId == stockId && x.NormalizedSerialNo == value.ToUpper()
            && x.Status != StockSerialStatus.Reserved, ct))
            return true;
        var q=uow.Repository<StockMovementEntry>().Query().Where(x=>x.SerialNo==value);
        if(r.UniquenessScope!=SerialUniquenessScope.Global)q=q.Where(x=>x.StockId==stockId);
        if(r.UniquenessScope==SerialUniquenessScope.StockAndYapCode)q=q.Where(x=>x.YapCodeId==yapId);
        return await q.AnyAsync(ct);
    }
    private async Task<StockTrackingPolicy> ResolvePolicyAsync(string branch,StockEntity stock,DateTimeOffset now,CancellationToken ct)
    {
        var candidates=await uow.Repository<StockTrackingPolicy>().Query().Where(x=>
            x.BranchCode==branch&&x.IsActive&&x.EffectiveFromUtc<=now
            &&(!x.EffectiveToUtc.HasValue||x.EffectiveToUtc>now)
            &&(x.Scope==StockTrackingPolicyScope.BranchDefault
                ||(x.Scope==StockTrackingPolicyScope.Stock&&x.StockId==stock.Id)
                ||(x.Scope==StockTrackingPolicyScope.StockGroup&&x.StockGroupCode==stock.GroupCode)))
            .ToListAsync(ct);
        return candidates.OrderByDescending(x=>x.Scope).ThenByDescending(x=>x.Priority)
            .ThenByDescending(x=>x.Version).FirstOrDefault()
            ??throw AppException.BadRequest("Stok takip ayarı bulunamadı.");
    }
    private static GenerateStockSerialsResult Result(
        StockEntity stock,long? ruleId,string mask,bool replayed,IReadOnlyCollection<StockSerialRegistry> rows)=>
        new(stock.Id,stock.ErpStockCode,ruleId,mask,replayed,rows.OrderBy(x=>x.GenerationOrdinal)
            .Select(x=>new GeneratedStockSerial(x.Id,x.SerialNo,x.SequenceNumber,x.GenerationOrdinal,x.Status.ToString())).ToArray());
    private static void ValidateAutomaticTemplate(string mask)
    {
        if(Regex.Matches(mask,@"\{N:[1-9]\d?\}").Count!=1||Regex.IsMatch(mask,@"\{[AX]:[1-9]\d?\}"))
            throw AppException.Conflict("Otomatik seri maskesi bir adet {N:n} alanı içermeli ve rastgele A/X alanı içermemelidir.");
    }
    private static string Render(string mask,string stock,string? group,long sequence,DateTimeOffset now)
    {
        var result=Regex.Replace(mask,@"\{N:([1-9]\d?)\}",m=>sequence.ToString().PadLeft(int.Parse(m.Groups[1].Value),'0'));
        return result.Replace("{STOCK}",stock.ToUpperInvariant(),StringComparison.Ordinal)
            .Replace("{GROUP}",(group??string.Empty).ToUpperInvariant(),StringComparison.Ordinal)
            .Replace("{YYYY}",now.Year.ToString("0000"),StringComparison.Ordinal)
            .Replace("{YY}",(now.Year%100).ToString("00"),StringComparison.Ordinal)
            .Replace("{MM}",now.Month.ToString("00"),StringComparison.Ordinal)
            .Replace("{DD}",now.Day.ToString("00"),StringComparison.Ordinal);
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
