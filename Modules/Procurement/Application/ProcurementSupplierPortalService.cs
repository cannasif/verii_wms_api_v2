using Microsoft.EntityFrameworkCore;
using verii_wms_api_v2.Modules.Identity.Application;
using verii_wms_api_v2.Modules.Procurement.Domain;
using verii_wms_api_v2.Shared.Application.Abstractions.Persistence;
using verii_wms_api_v2.Shared.Application.Exceptions;

namespace verii_wms_api_v2.Modules.Procurement.Application;

public sealed class ProcurementSupplierPortalService(IUnitOfWork uow,IProcurementPolicyService policyService):IProcurementSupplierPortalService
{
    private IGenericRepository<ProcurementQuoteInvitation> Invitations=>uow.Repository<ProcurementQuoteInvitation>();
    private IGenericRepository<ProcurementSupplierQuote> Quotes=>uow.Repository<ProcurementSupplierQuote>();
    private IGenericRepository<ProcurementStatusHistory> History=>uow.Repository<ProcurementStatusHistory>();

    public async Task<SupplierPortalQuote> GetAsync(string token,CancellationToken ct=default)
    {
        var invitation=await FindAsync(token,true,ct);var policy=await policyService.GetAsync(invitation.BranchCode,ct);EnsureUsable(invitation,policy);
        var now=DateTimeOffset.UtcNow;invitation.FirstOpenedAtUtc??=now;invitation.LastOpenedAtUtc=now;
        if(invitation.Status==ProcurementInvitationStatus.Sent)invitation.Status=ProcurementInvitationStatus.Opened;
        await uow.SaveChangesAsync(ct);return Map(invitation,policy);
    }

    public Task SaveDraftAsync(string token,SaveSupplierPortalQuoteRequest request,CancellationToken ct=default)=>SaveAsync(token,request,false,ct);
    public Task SubmitAsync(string token,SaveSupplierPortalQuoteRequest request,CancellationToken ct=default)=>SaveAsync(token,request,true,ct);

    private async Task SaveAsync(string token,SaveSupplierPortalQuoteRequest request,bool submit,CancellationToken ct)
    {
        try
        {
            await uow.ExecuteInTransactionAsync(async innerCt=>
            {
                var invitation=await FindAsync(token,true,innerCt);var policy=await policyService.GetAsync(invitation.BranchCode,innerCt);EnsureUsable(invitation,policy);
                if(invitation.Status==ProcurementInvitationStatus.Submitted)throw AppException.Conflict("Teklif daha önce gönderildi. Değişiklik için satınalma sorumlusundan revizyon isteyiniz.");
                if(!submit&&!policy.AllowSupplierDraftSave)throw AppException.Conflict("Satınalma politikası tedarikçinin taslak kaydetmesine izin vermiyor.");
                Validate(invitation,request,submit,policy);
                var quote=invitation.CurrentQuote;
                if(quote is null)
                {
                    quote=new ProcurementSupplierQuote{BranchCode=invitation.BranchCode,ProcurementRfqId=invitation.ProcurementRfqId,SupplierId=invitation.SupplierId,SupplierCodeSnapshot=invitation.RfqSupplier.SupplierCodeSnapshot,SupplierNameSnapshot=invitation.RfqSupplier.SupplierNameSnapshot,Status=ProcurementQuoteStatus.Draft,RevisionNo=1};
                    await Quotes.AddAsync(quote,innerCt);await uow.SaveChangesAsync(innerCt);invitation.CurrentQuoteId=quote.Id;invitation.CurrentQuote=quote;
                }
                Apply(quote,request,invitation.Rfq.Lines);
                invitation.LastOpenedAtUtc=DateTimeOffset.UtcNow;invitation.Status=submit?ProcurementInvitationStatus.Submitted:ProcurementInvitationStatus.DraftSaved;
                if(submit)
                {
                    quote.Status=ProcurementQuoteStatus.Submitted;quote.SubmittedAtUtc=DateTimeOffset.UtcNow;invitation.SubmittedAtUtc=quote.SubmittedAtUtc;
                    var from=invitation.Rfq.Status;invitation.Rfq.Status=ProcurementRfqStatus.Quoted;
                    await History.AddAsync(new ProcurementStatusHistory{BranchCode=invitation.BranchCode,DocumentType="quote",DocumentId=quote.Id,FromStatus=ProcurementQuoteStatus.Draft.ToString(),ToStatus=quote.Status.ToString(),ActorUserId=0,Note="Tedarikçi portalından gönderildi.",ChangedAtUtc=DateTimeOffset.UtcNow},innerCt);
                    if(from!=invitation.Rfq.Status)await History.AddAsync(new ProcurementStatusHistory{BranchCode=invitation.BranchCode,DocumentType="rfq",DocumentId=invitation.Rfq.Id,FromStatus=from.ToString(),ToStatus=invitation.Rfq.Status.ToString(),ActorUserId=0,Note=$"{invitation.RfqSupplier.SupplierNameSnapshot} teklifini gönderdi.",ChangedAtUtc=DateTimeOffset.UtcNow},innerCt);
                }
                await uow.SaveChangesAsync(innerCt);return true;
            },ct);
        }
        catch(DbUpdateConcurrencyException){throw AppException.Conflict("Teklif başka bir oturumda güncellendi. Sayfayı yenileyip tekrar deneyiniz.");}
    }

    private async Task<ProcurementQuoteInvitation> FindAsync(string token,bool tracking,CancellationToken ct)
    {
        var hash=IdentitySecurity.HashToken(token);
        if(hash.Length!=64)throw AppException.NotFound("Teklif bağlantısı geçersiz veya kullanım dışı.");
        return await Invitations.Query(tracking).Include(x=>x.Rfq).ThenInclude(x=>x.Lines).Include(x=>x.RfqSupplier).Include(x=>x.CurrentQuote).ThenInclude(x=>x!.Lines).FirstOrDefaultAsync(x=>x.TokenHash==hash,ct)??throw AppException.NotFound("Teklif bağlantısı geçersiz veya kullanım dışı.");
    }

    private static void EnsureUsable(ProcurementQuoteInvitation invitation,ProcurementPolicyDto policy)
    {
        if(policy.SupplierQuoteChannelMode==SupplierQuoteChannelMode.InternalOnly.ToString())throw AppException.Conflict("Tedarikçi teklif portalı satınalma politikasında kapalı.");
        if(invitation.Status==ProcurementInvitationStatus.Revoked||invitation.RevokedAtUtc.HasValue)throw AppException.Conflict("Teklif bağlantısı iptal edilmiş.");
        if(invitation.ExpiresAtUtc<=DateTimeOffset.UtcNow||invitation.Rfq.ResponseDueDate<DateOnly.FromDateTime(DateTime.UtcNow))throw AppException.Conflict("Teklif bağlantısının süresi dolmuş.");
        if(invitation.Rfq.Status is ProcurementRfqStatus.Closed or ProcurementRfqStatus.Cancelled)throw AppException.Conflict("Teklif toplama süreci kapatılmış.");
    }

    private static void Validate(ProcurementQuoteInvitation invitation,SaveSupplierPortalQuoteRequest request,bool submit,ProcurementPolicyDto policy)
    {
        if(request.ExchangeRate<=0)throw AppException.BadRequest("Kur sıfırdan büyük olmalıdır.");
        if(request.Lines.Count==0)throw AppException.BadRequest("En az bir teklif kalemi zorunludur.");
        if(submit&&string.IsNullOrWhiteSpace(request.QuoteNo))throw AppException.BadRequest("Teklif numarası zorunludur.");
        var rfqLines=invitation.Rfq.Lines.ToDictionary(x=>x.Id);
        if(request.Lines.Select(x=>x.RfqLineId).Distinct().Count()!=request.Lines.Count||request.Lines.Any(x=>!rfqLines.TryGetValue(x.RfqLineId,out var line)||x.Quantity<=0||x.Quantity>line.RequestedQuantity||(!policy.AllowSupplierQuantityChange&&x.Quantity!=line.RequestedQuantity)||x.UnitPrice<0||(!policy.AllowZeroUnitPrice&&submit&&x.UnitPrice<=0)||x.DiscountRate is <0 or >100||x.VatRate<0||(policy.RequireSupplierDeliveryDate&&submit&&!x.DeliveryDate.HasValue)))throw AppException.BadRequest("Teklif kalemleri satınalma politikasına veya istenen miktarlara uygun değil.");
    }

    private static void Apply(ProcurementSupplierQuote quote,SaveSupplierPortalQuoteRequest request,ICollection<ProcurementRfqLine> rfqLines)
    {
        quote.QuoteNo=string.IsNullOrWhiteSpace(request.QuoteNo)?quote.QuoteNo:request.QuoteNo.Trim();quote.QuoteDate=request.QuoteDate??DateOnly.FromDateTime(DateTime.UtcNow);quote.ValidUntil=request.ValidUntil;quote.CurrencyCode=string.IsNullOrWhiteSpace(request.CurrencyCode)?"TRY":request.CurrencyCode.Trim().ToUpperInvariant();quote.ExchangeRate=request.ExchangeRate;quote.Note=string.IsNullOrWhiteSpace(request.Note)?null:request.Note.Trim();
        var existing=quote.Lines.ToDictionary(x=>x.ProcurementRfqLineId);var requestedIds=request.Lines.Select(x=>x.RfqLineId).ToHashSet();
        if(existing.Keys.Any(x=>!requestedIds.Contains(x)))throw AppException.BadRequest("Revizyon teklifinde mevcut kalemler çıkarılamaz; miktarı değiştiriniz veya satınalma sorumlusuna bildiriniz.");
        foreach(var input in request.Lines)
        {
            if(!existing.TryGetValue(input.RfqLineId,out var line)){line=new ProcurementSupplierQuoteLine{BranchCode=quote.BranchCode,LineNo=rfqLines.Single(x=>x.Id==input.RfqLineId).LineNo,ProcurementRfqLineId=input.RfqLineId};quote.Lines.Add(line);}
            line.QuotedQuantity=input.Quantity;line.UnitPrice=input.UnitPrice;line.DiscountRate=input.DiscountRate;line.VatRate=input.VatRate;line.DeliveryDate=input.DeliveryDate;
        }
    }

    private static SupplierPortalQuote Map(ProcurementQuoteInvitation invitation,ProcurementPolicyDto policy)
    {
        var quote=invitation.CurrentQuote;var values=quote?.Lines.ToDictionary(x=>x.ProcurementRfqLineId)??[];
        return new(invitation.Rfq.RfqNo,invitation.Rfq.Subject,invitation.Rfq.BuyerMessage,invitation.RfqSupplier.SupplierCodeSnapshot,invitation.RfqSupplier.SupplierNameSnapshot,invitation.Status.ToString(),invitation.Rfq.ResponseDueDate,invitation.ExpiresAtUtc,quote?.QuoteNo,quote?.QuoteDate,quote?.ValidUntil,quote?.CurrencyCode??"TRY",quote?.ExchangeRate??1,quote?.Note,quote?.RevisionNo??1,policy.AllowSupplierDraftSave,policy.AllowSupplierQuantityChange,policy.RequireSupplierDeliveryDate,policy.AllowZeroUnitPrice,invitation.Rfq.Lines.OrderBy(x=>x.LineNo).Select(x=>{values.TryGetValue(x.Id,out var value);return new SupplierPortalLine(x.Id,x.LineNo,x.StockCodeSnapshot,x.StockNameSnapshot,x.UnitCode,x.RequestedQuantity,x.RequiredDate,value?.QuotedQuantity??x.RequestedQuantity,value?.UnitPrice??0,value?.DiscountRate??0,value?.VatRate??20,value?.DeliveryDate??x.RequiredDate);}).ToList());
    }
}
