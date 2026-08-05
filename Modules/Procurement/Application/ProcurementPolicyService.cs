using Microsoft.EntityFrameworkCore;
using verii_wms_api_v2.Modules.Audit.Application;
using verii_wms_api_v2.Modules.Procurement.Domain;
using verii_wms_api_v2.Shared.Application.Abstractions.Persistence;
using verii_wms_api_v2.Shared.Application.Exceptions;

namespace verii_wms_api_v2.Modules.Procurement.Application;

public sealed class ProcurementPolicyService(IUnitOfWork uow,IAuditLogWriter audit):IProcurementPolicyService
{
    private const string DefaultKey="DEFAULT";
    private IGenericRepository<ProcurementPolicy> Policies=>uow.Repository<ProcurementPolicy>();

    public async Task<ProcurementPolicyDto> GetAsync(string branchCode,CancellationToken ct=default)
    {
        var branch=Branch(branchCode);
        var entity=await Policies.Query().SingleOrDefaultAsync(x=>x.BranchCode==branch&&x.PolicyKey==DefaultKey,ct);
        return Map(entity??Default(branch));
    }

    public async Task<ProcurementPolicyDto> UpdateAsync(string branchCode,UpdateProcurementPolicyRequest request,long actorUserId,CancellationToken ct=default)
    {
        var branch=Branch(branchCode);
        var entity=await Policies.Query(true).SingleOrDefaultAsync(x=>x.BranchCode==branch&&x.PolicyKey==DefaultKey,ct);
        var before=entity is null?null:Map(entity);
        if(entity is null){entity=Default(branch);entity.CreatedBy=actorUserId;entity.CreatedDate=DateTime.UtcNow;await Policies.AddAsync(entity,ct);}
        entity.AllowMultipleRfqsPerRequest=request.AllowMultipleRfqsPerRequest;
        entity.AllowPartialRfqLines=request.AllowPartialRfqLines;
        entity.AllowMultipleQuotesPerSupplier=request.AllowMultipleQuotesPerSupplier;
        entity.AllowMultipleOrdersPerQuote=request.AllowMultipleOrdersPerQuote;
        entity.AllowPartialOrderLines=request.AllowPartialOrderLines;
        entity.AllowSplitAwardsAcrossSuppliers=request.AllowSplitAwardsAcrossSuppliers;
        if(!Enum.TryParse<SupplierQuoteChannelMode>(request.SupplierQuoteChannelMode,true,out var channelMode)||!Enum.IsDefined(channelMode))throw AppException.BadRequest("Geçersiz tedarikçi teklif kanalı.");
        if(request.InvitationValidityDays is <1 or >30)throw AppException.BadRequest("Teklif bağlantısı süresi 1-30 gün arasında olmalıdır.");
        if(request.MaximumSupplierRevisionCount is <0 or >20)throw AppException.BadRequest("Azami teklif revizyon sayısı 0-20 arasında olmalıdır.");
        entity.SupplierQuoteChannelMode=channelMode;
        entity.InvitationValidityDays=request.InvitationValidityDays;
        entity.AllowSupplierDraftSave=request.AllowSupplierDraftSave;
        entity.AllowSupplierQuantityChange=request.AllowSupplierQuantityChange;
        entity.AllowSupplierRevisions=request.AllowSupplierRevisions;
        entity.MaximumSupplierRevisionCount=request.MaximumSupplierRevisionCount;
        entity.RequireSupplierDeliveryDate=request.RequireSupplierDeliveryDate;
        entity.AllowZeroUnitPrice=request.AllowZeroUnitPrice;
        entity.UpdatedBy=actorUserId;entity.UpdatedDate=DateTime.UtcNow;
        try{await uow.SaveChangesAsync(ct);}
        catch(DbUpdateConcurrencyException){throw AppException.Conflict("Satınalma politikası başka bir kullanıcı tarafından güncellendi. Ekranı yenileyip tekrar deneyin.");}
        var result=Map(entity);
        await audit.WriteAsync(new("procurement.policy.update",nameof(ProcurementPolicy),entity.Id.ToString(),"Succeeded","procurement",OldValues:before,NewValues:result,ChangedFields:["Policy"]),ct);
        return result;
    }

    private static ProcurementPolicy Default(string branch)=>new(){BranchCode=branch,PolicyKey=DefaultKey};
    private static string Branch(string? value)=>string.IsNullOrWhiteSpace(value)?"0":value.Trim();
    private static ProcurementPolicyDto Map(ProcurementPolicy x)=>new(x.Id,x.BranchCode,x.AllowMultipleRfqsPerRequest,x.AllowPartialRfqLines,x.AllowMultipleQuotesPerSupplier,x.AllowMultipleOrdersPerQuote,x.AllowPartialOrderLines,x.AllowSplitAwardsAcrossSuppliers,x.SupplierQuoteChannelMode.ToString(),x.InvitationValidityDays,x.AllowSupplierDraftSave,x.AllowSupplierQuantityChange,x.AllowSupplierRevisions,x.MaximumSupplierRevisionCount,x.RequireSupplierDeliveryDate,x.AllowZeroUnitPrice,x.UpdatedBy,x.UpdatedDate);
}
