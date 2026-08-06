using Microsoft.EntityFrameworkCore;
using verii_wms_api_v2.Modules.Audit.Application;
using verii_wms_api_v2.Modules.Procurement.Domain;
using verii_wms_api_v2.Modules.Procurement.Infrastructure;
using verii_wms_api_v2.Shared;
using verii_wms_api_v2.Shared.Application.Abstractions.Persistence;
using verii_wms_api_v2.Shared.Application.Exceptions;
using verii_wms_api_v2.Shared.Infrastructure.Files;
using CustomerEntity = verii_wms_api_v2.Modules.Customer.Domain.Customer;
using StockEntity = verii_wms_api_v2.Modules.Stock.Domain.Stock;
using System.Net.Mail;
using verii_wms_api_v2.Modules.Identity.Application;

namespace verii_wms_api_v2.Modules.Procurement.Application;

public sealed class ProcurementService(IUnitOfWork uow,IAuditLogWriter audit,IProcurementPolicyService policyService,IProcurementEmailSender emailSender,IConfiguration configuration,IProcurementAttachmentStorage attachmentStorage) : IProcurementService
{
    private IGenericRepository<ProcurementRequest> Requests=>uow.Repository<ProcurementRequest>();
    private IGenericRepository<ProcurementRfq> Rfqs=>uow.Repository<ProcurementRfq>();
    private IGenericRepository<ProcurementSupplierQuote> Quotes=>uow.Repository<ProcurementSupplierQuote>();
    private IGenericRepository<ProcurementPurchaseOrder> Orders=>uow.Repository<ProcurementPurchaseOrder>();
    private IGenericRepository<ProcurementStatusHistory> History=>uow.Repository<ProcurementStatusHistory>();
    private IGenericRepository<ProcurementQuoteInvitation> Invitations=>uow.Repository<ProcurementQuoteInvitation>();
    private IGenericRepository<ProcurementAttachment> Attachments=>uow.Repository<ProcurementAttachment>();

    public async Task<ProcurementWorkspaceSummary> GetSummaryAsync(CancellationToken ct=default)=>new(
        await Requests.CountAsync(x=>x.Status==ProcurementRequestStatus.Draft,ct),
        await Requests.CountAsync(x=>x.Status==ProcurementRequestStatus.PendingApproval,ct),
        await Rfqs.CountAsync(x=>x.Status==ProcurementRfqStatus.Draft||x.Status==ProcurementRfqStatus.Sent||x.Status==ProcurementRfqStatus.Quoted,ct),
        await Quotes.CountAsync(x=>x.Status==ProcurementQuoteStatus.Submitted,ct),
        await Orders.CountAsync(x=>x.Status==ProcurementOrderStatus.PendingApproval,ct),
        await Orders.CountAsync(x=>(x.Status==ProcurementOrderStatus.Approved||x.Status==ProcurementOrderStatus.SentToSupplier||x.Status==ProcurementOrderStatus.PartiallyReceived)&&x.Lines.Any(l=>l.OrderedQuantity-l.ReceivedQuantity-l.CancelledQuantity>0),ct));

    public Task<PagedResponse<ProcurementGridRow>> GetPagedAsync(string documentType,PagedRequest request,CancellationToken ct=default)=>NormalizeType(documentType) switch
    {
        "request"=>RequestRows(request).ToPagedResponseAsync(request,ct),
        "rfq"=>RfqRows(request).ToPagedResponseAsync(request,ct),
        "quote"=>QuoteRows(request).ToPagedResponseAsync(request,ct),
        "order"=>OrderRows(request).ToPagedResponseAsync(request,ct),
        _=>throw AppException.BadRequest("Geçersiz satınalma belge türü.")
    };

    public async Task<ProcurementDocumentDetail> GetDetailAsync(string documentType,long id,CancellationToken ct=default)
    {
        var type=NormalizeType(documentType);
        var histories=await History.Query().Where(x=>x.DocumentType==type&&x.DocumentId==id).OrderBy(x=>x.ChangedAtUtc).Select(x=>new ProcurementHistoryRow(x.FromStatus,x.ToStatus,x.ActorUserId,x.Note,x.ChangedAtUtc)).ToListAsync(ct);
        if(type=="request")
        {
            var x=await Requests.Query().Include(x=>x.Lines).FirstOrDefaultAsync(x=>x.Id==id,ct)??throw AppException.NotFound("Satınalma talebi bulunamadı.");
            var lines=x.Lines.OrderBy(l=>l.LineNo).Select(l=>new ProcurementLineDetail(l.Id,l.LineNo,l.StockId,l.StockCodeSnapshot,l.StockNameSnapshot,l.UnitCode,l.RequestedQuantity,l.ConvertedQuantity,0,0,0,l.RequiredDate,l.ProjectCode,l.RequestedQuantity-l.ConvertedQuantity,l.Id)).ToList();
            var (headerAttachments,lineAttachments)=await LoadAttachmentBundleAsync(ProcurementAttachmentOwnerType.Request,id,ProcurementAttachmentOwnerType.RequestLine,lines.Select(l=>l.Id),ct);
            lines=lines.Select(l=>l with{Attachments=lineAttachments.GetValueOrDefault(l.Id,[])}).ToList();
            return new(x.Id,type,x.RequestNo,x.RequestDate,x.Status.ToString(),x.Subject,x.Description,null,null,"TRY",1,x.RequiredDate,lines,histories,null,x.Id,x.RequestNo,headerAttachments);
        }
        if(type=="rfq")
        {
            var x=await Rfqs.Query().Include(x=>x.Lines).Include(x=>x.Suppliers).Include(x=>x.Request).FirstOrDefaultAsync(x=>x.Id==id,ct)??throw AppException.NotFound("Teklif talebi bulunamadı.");
            var invitations=await Invitations.Query().Where(i=>i.ProcurementRfqId==id).ToListAsync(ct);
            return new(x.Id,type,x.RfqNo,x.RfqDate,x.Status.ToString(),x.Subject,x.BuyerMessage,null,string.Join(", ",x.Suppliers.Select(s=>s.SupplierNameSnapshot)),"TRY",1,x.ResponseDueDate,x.Lines.OrderBy(l=>l.LineNo).Select(l=>new ProcurementLineDetail(l.Id,l.LineNo,l.StockId,l.StockCodeSnapshot,l.StockNameSnapshot,l.UnitCode,l.RequestedQuantity,0,0,0,0,l.RequiredDate,l.ProjectCode,l.RequestedQuantity,l.ProcurementRequestLineId)).ToList(),histories,x.Suppliers.OrderBy(s=>s.SupplierNameSnapshot).Select(s=>{var invitation=s.SupplierId is long sid?invitations.FirstOrDefault(i=>i.SupplierId==sid):null;return new ProcurementSupplierParticipant(s.SupplierId,s.SupplierCodeSnapshot,s.SupplierNameSnapshot,invitation?.Status.ToString(),invitation?.RecipientEmail,invitation?.ExpiresAtUtc);}).ToList(),x.ProcurementRequestId,x.Request?.RequestNo);
        }
        if(type=="quote")
        {
            var x=await Quotes.Query().Include(x=>x.Lines).Include(x=>x.Rfq).ThenInclude(r=>r.Request).FirstOrDefaultAsync(x=>x.Id==id,ct)??throw AppException.NotFound("Tedarikçi teklifi bulunamadı.");
            var rfqLines=await uow.Repository<ProcurementRfqLine>().Query().Where(l=>l.ProcurementRfqId==x.ProcurementRfqId).ToDictionaryAsync(l=>l.Id,ct);
            var subject=x.Rfq.Request?.Subject??x.Rfq.Subject;
            var lines=x.Lines.OrderBy(l=>l.LineNo).Select(l=>{var r=rfqLines[l.ProcurementRfqLineId];return new ProcurementLineDetail(l.Id,l.LineNo,r.StockId,r.StockCodeSnapshot,r.StockNameSnapshot,r.UnitCode,l.QuotedQuantity,l.ConvertedQuantity,l.UnitPrice,l.DiscountRate,l.VatRate,l.DeliveryDate,r.ProjectCode,l.QuotedQuantity-l.ConvertedQuantity,r.ProcurementRequestLineId);}).ToList();
            var (headerAttachments,lineAttachments)=await LoadAttachmentBundleAsync(ProcurementAttachmentOwnerType.Quote,id,ProcurementAttachmentOwnerType.QuoteLine,lines.Select(l=>l.Id),ct);
            lines=lines.Select(l=>l with{Attachments=lineAttachments.GetValueOrDefault(l.Id,[])}).ToList();
            return new(x.Id,type,x.QuoteNo,x.QuoteDate,x.Status.ToString(),subject,x.Note,x.SupplierCodeSnapshot,x.SupplierNameSnapshot,x.CurrencyCode,x.ExchangeRate,x.ValidUntil,lines,histories,null,x.Rfq.ProcurementRequestId,x.Rfq.Request?.RequestNo,headerAttachments);
        }
        if(type=="order")
        {
            var x=await Orders.Query().Include(x=>x.Lines).FirstOrDefaultAsync(x=>x.Id==id,ct)??throw AppException.NotFound("Satınalma siparişi bulunamadı.");
            return new(x.Id,type,x.OrderNo,x.OrderDate,x.Status.ToString(),"Satınalma siparişi",x.Description,x.SupplierCodeSnapshot,x.SupplierNameSnapshot,x.CurrencyCode,x.ExchangeRate,x.DeliveryDate,x.Lines.OrderBy(l=>l.LineNo).Select(l=>new ProcurementLineDetail(l.Id,l.LineNo,l.StockId,l.StockCodeSnapshot,l.StockNameSnapshot,l.UnitCode,l.OrderedQuantity,l.ReceivedQuantity,l.UnitPrice,l.DiscountRate,l.VatRate,l.DeliveryDate,l.ProjectCode,l.OrderedQuantity-l.ReceivedQuantity-l.CancelledQuantity)).ToList(),histories);
        }
        throw AppException.BadRequest("Geçersiz satınalma belge türü.");
    }

    public async Task<ProcurementNextDocumentNo> PeekNextDocumentNoAsync(string documentType,CancellationToken ct=default)
    {
        var type=NormalizeType(documentType);
        var (prefix,maxId)=type switch
        {
            "request"=>("REQ",await Requests.Query(false,true).MaxAsync(x=>(long?)x.Id,ct)??0),
            "rfq"=>("RFQ",await Rfqs.Query(false,true).MaxAsync(x=>(long?)x.Id,ct)??0),
            "quote"=>("QT",await Quotes.Query(false,true).MaxAsync(x=>(long?)x.Id,ct)??0),
            "order"=>("PO",await Orders.Query(false,true).MaxAsync(x=>(long?)x.Id,ct)??0),
            _=>throw AppException.BadRequest("Geçersiz satınalma belge türü.")
        };
        return new(type,Number(prefix,maxId+1));
    }

    public async Task<long> CreateRequestAsync(CreateProcurementRequest request,long actorUserId,CancellationToken ct=default)
    {
        ValidateHeader(request.Subject,request.Lines);
        var lines=await ResolveLines(request.Lines,ct);
        var requestedNo=Norm(request.RequestNo);
        if(!string.IsNullOrWhiteSpace(requestedNo))await EnsureUniqueDocumentNoAsync("request",requestedNo,ct);
        var entity=new ProcurementRequest{RequestNo=requestedNo??TemporaryNo("REQ"),RequestDate=request.RequestDate??DateOnly.FromDateTime(DateTime.Today),RequiredDate=request.RequiredDate,DepartmentCode=Norm(request.DepartmentCode),ProjectCode=Norm(request.ProjectCode),Subject=request.Subject.Trim(),Description=Norm(request.Description),Status=ProcurementRequestStatus.Draft};
        entity.Lines=lines.Select((l,i)=>new ProcurementRequestLine{LineNo=i+1,StockId=l.StockId,StockCodeSnapshot=l.StockCode,StockNameSnapshot=l.StockName,UnitCode=l.UnitCode,RequestedQuantity=l.Quantity,RequiredDate=l.RequiredDate??request.RequiredDate,ProjectCode=l.ProjectCode??Norm(request.ProjectCode),Description=l.Description}).ToList();
        await Requests.AddAsync(entity,ct); await uow.SaveChangesAsync(ct);
        if(string.IsNullOrWhiteSpace(requestedNo)){entity.RequestNo=Number("REQ",entity.Id); await uow.SaveChangesAsync(ct);}
        await audit.WriteAsync(new("procurement.request.create","ProcurementRequest",entity.Id.ToString(),"Succeeded","procurement",NewValues:new{entity.RequestNo,entity.Subject,LineCount=entity.Lines.Count}),ct);
        return entity.Id;
    }

    public async Task TransitionRequestAsync(long id,string action,ProcurementTransitionRequest request,long actorUserId,CancellationToken ct=default)
    {
        var x=await Requests.FindByIdAsync(id,true,ct)??throw AppException.NotFound("Satınalma talebi bulunamadı."); var from=x.Status;
        x.Status=action.Trim().ToLowerInvariant() switch {"submit" when from==ProcurementRequestStatus.Draft=>ProcurementRequestStatus.PendingApproval,"approve" when from==ProcurementRequestStatus.PendingApproval=>ProcurementRequestStatus.Approved,"reject" when from==ProcurementRequestStatus.PendingApproval=>ProcurementRequestStatus.Rejected,"cancel" when from is ProcurementRequestStatus.Draft or ProcurementRequestStatus.PendingApproval or ProcurementRequestStatus.Approved=>ProcurementRequestStatus.Cancelled,_=>throw AppException.Conflict("Talep mevcut durumunda bu işleme uygun değil.")};
        if(x.Status==ProcurementRequestStatus.PendingApproval)x.SubmittedAtUtc=DateTimeOffset.UtcNow; if(x.Status is ProcurementRequestStatus.Approved or ProcurementRequestStatus.Rejected){x.DecidedAtUtc=DateTimeOffset.UtcNow;x.DecidedBy=actorUserId;x.DecisionNote=Norm(request.Note);} await AddHistory("request",x.Id,from.ToString(),x.Status.ToString(),actorUserId,request.Note,ct); await uow.SaveChangesAsync(ct);
    }

    public async Task<long> ConvertRequestToRfqAsync(long id,ConvertRequestToRfqRequest request,long actorUserId,CancellationToken ct=default)
    {
        if(request.ResponseDueDate<DateOnly.FromDateTime(DateTime.Today))throw AppException.BadRequest("Teklif son tarihi geçmiş olamaz.");
        return await uow.ExecuteInTransactionAsync(async token=>
        {
            var source=await Requests.Query(true).Include(x=>x.Lines).FirstOrDefaultAsync(x=>x.Id==id,token)??throw AppException.NotFound("Satınalma talebi bulunamadı.");
            if(source.Status is not (ProcurementRequestStatus.Approved or ProcurementRequestStatus.PartiallyConverted))throw AppException.Conflict("Yalnızca sipariş bakiyesi açık onaylı talep için teklif talebi oluşturulabilir.");
            var policy=await policyService.GetAsync(source.BranchCode,token);
            if(!policy.AllowMultipleRfqsPerRequest&&await Rfqs.Query().AnyAsync(x=>x.ProcurementRequestId==id&&x.Status!=ProcurementRfqStatus.Cancelled,token))throw AppException.Conflict("Satınalma politikası aynı talepten birden fazla teklif talebi oluşturulmasına izin vermiyor.");
            var suppliers=await ResolveSuppliers(request.SupplierIds??[],token);

            var openLines=source.Lines.Where(x=>x.RequestedQuantity-x.ConvertedQuantity>0).OrderBy(x=>x.LineNo).ToList();
            if(openLines.Count==0)throw AppException.Conflict("Talebin siparişe bağlanmamış açık miktarı bulunmuyor.");
            var selections=ResolveRfqSelections(openLines,request.Lines,policy.AllowPartialRfqLines);
            var requestedNo=Norm(request.RfqNo);
            if(!string.IsNullOrWhiteSpace(requestedNo)&&await Rfqs.Query().AnyAsync(x=>x.RfqNo==requestedNo,token))throw AppException.Conflict("Bu teklif talebi numarası zaten kullanılıyor.");
            var rfq=new ProcurementRfq
            {
                RfqNo=requestedNo??TemporaryNo("RFQ"),RfqDate=DateOnly.FromDateTime(DateTime.Today),ResponseDueDate=request.ResponseDueDate,
                ProcurementRequestId=source.Id,Subject=source.Subject,BuyerMessage=Norm(request.BuyerMessage),Status=ProcurementRfqStatus.Draft,
                Lines=selections.Select((selection,index)=>
                {
                    var line=openLines.Single(x=>x.Id==selection.RequestLineId);
                    return new ProcurementRfqLine{LineNo=index+1,ProcurementRequestLineId=line.Id,StockId=line.StockId,StockCodeSnapshot=line.StockCodeSnapshot,StockNameSnapshot=line.StockNameSnapshot,UnitCode=line.UnitCode,RequestedQuantity=selection.Quantity,RequiredDate=line.RequiredDate,ProjectCode=line.ProjectCode};
                }).ToList(),
                Suppliers=suppliers.Select(s=>new ProcurementRfqSupplier{SupplierId=s.Id,SupplierCodeSnapshot=s.CustomerCode,SupplierNameSnapshot=s.CustomerName}).ToList()
            };
            await Rfqs.AddAsync(rfq,token);await uow.SaveChangesAsync(token);
            if(string.IsNullOrWhiteSpace(requestedNo))rfq.RfqNo=Number("RFQ",rfq.Id);
            await AddHistory("rfq",rfq.Id,"",rfq.Status.ToString(),actorUserId,$"{rfq.Lines.Count} kalem, {rfq.Suppliers.Count} tedarikçi",token);
            await uow.SaveChangesAsync(token);return rfq.Id;
        },ct);
    }

    public async Task TransitionRfqAsync(long id,string action,ProcurementTransitionRequest request,long actorUserId,CancellationToken ct=default)
    {
        if(action.Trim().Equals("send",StringComparison.OrdinalIgnoreCase))
        {
            var candidate=await Rfqs.FindByIdAsync(id,false,ct)??throw AppException.NotFound("Teklif talebi bulunamadı.");
            var procurementPolicy=await policyService.GetAsync(candidate.BranchCode,ct);
            if(procurementPolicy.SupplierQuoteChannelMode==SupplierQuoteChannelMode.PortalRequired.ToString())throw AppException.Conflict("Portal zorunlu: RFQ'yu tedarikçi kartlarındaki e-posta davetiyle gönderin.");
        }
        var x=await Rfqs.FindByIdAsync(id,true,ct)??throw AppException.NotFound("Teklif talebi bulunamadı.");var from=x.Status;x.Status=action.Trim().ToLowerInvariant() switch{"send" when from==ProcurementRfqStatus.Draft=>ProcurementRfqStatus.Sent,"close" when from is ProcurementRfqStatus.Sent or ProcurementRfqStatus.Quoted=>ProcurementRfqStatus.Closed,"cancel" when from is ProcurementRfqStatus.Draft or ProcurementRfqStatus.Sent or ProcurementRfqStatus.Quoted=>ProcurementRfqStatus.Cancelled,_=>throw AppException.Conflict("Teklif talebi mevcut durumunda bu işleme uygun değil.")};if(x.Status==ProcurementRfqStatus.Sent)x.SentAtUtc=DateTimeOffset.UtcNow;await AddHistory("rfq",id,from.ToString(),x.Status.ToString(),actorUserId,request.Note,ct);await uow.SaveChangesAsync(ct);
    }

    public async Task<long> CreateQuoteAsync(long rfqId,CreateSupplierQuoteRequest request,long actorUserId,CancellationToken ct=default)
    {
        var rfq=await Rfqs.Query(true).Include(x=>x.Lines).Include(x=>x.Suppliers).FirstOrDefaultAsync(x=>x.Id==rfqId,ct)??throw AppException.NotFound("Teklif talebi bulunamadı.");
        if(rfq.Status is not (ProcurementRfqStatus.Sent or ProcurementRfqStatus.Quoted))throw AppException.Conflict("Teklif kaydı için teklif talebi gönderilmiş olmalıdır.");
        var manualName=Norm(request.SupplierName);
        ProcurementRfqSupplier? supplier;
        if(request.SupplierId is long supplierId and >0)
        {
            supplier=rfq.Suppliers.FirstOrDefault(x=>x.SupplierId==supplierId);
            if(supplier is null)
            {
                // Allow additional suppliers on an existing RFQ so one request can collect competing quotes.
                var resolved=(await ResolveSuppliers([supplierId],ct)).Single();
                supplier=new ProcurementRfqSupplier{SupplierId=resolved.Id,SupplierCodeSnapshot=resolved.CustomerCode,SupplierNameSnapshot=resolved.CustomerName};
                rfq.Suppliers.Add(supplier);
                await uow.SaveChangesAsync(ct);
            }
        }
        else
        {
            if(string.IsNullOrWhiteSpace(manualName))throw AppException.BadRequest("Tedarikçi seçilmeli veya tedarikçi adı girilmelidir.");
            supplier=rfq.Suppliers.FirstOrDefault(x=>x.SupplierId==null&&string.Equals(x.SupplierNameSnapshot,manualName,StringComparison.OrdinalIgnoreCase));
            if(supplier is null)
            {
                supplier=new ProcurementRfqSupplier{SupplierId=null,SupplierCodeSnapshot=string.Empty,SupplierNameSnapshot=manualName};
                rfq.Suppliers.Add(supplier);
                await uow.SaveChangesAsync(ct);
            }
        }
        if(request.ExchangeRate<=0||request.Lines.Count==0)throw AppException.BadRequest("Kur ve satırlar zorunludur.");
        var policy=await policyService.GetAsync(rfq.BranchCode,ct);
        if(policy.SupplierQuoteChannelMode==SupplierQuoteChannelMode.PortalRequired.ToString())throw AppException.Conflict("Satınalma politikası tekliflerin yalnız tedarikçi portalından gönderilmesini zorunlu tutuyor.");
        if(!policy.AllowMultipleQuotesPerSupplier)
        {
            var duplicate=request.SupplierId is long sid and >0
                ?await Quotes.Query().AnyAsync(x=>x.ProcurementRfqId==rfqId&&x.SupplierId==sid&&x.Status!=ProcurementQuoteStatus.Cancelled&&x.Status!=ProcurementQuoteStatus.Rejected,ct)
                :await Quotes.Query().AnyAsync(x=>x.ProcurementRfqId==rfqId&&x.SupplierId==null&&x.SupplierNameSnapshot==supplier.SupplierNameSnapshot&&x.Status!=ProcurementQuoteStatus.Cancelled&&x.Status!=ProcurementQuoteStatus.Rejected,ct);
            if(duplicate)throw AppException.Conflict("Satınalma politikası aynı tedarikçinin bu teklif talebine birden fazla teklif vermesine izin vermiyor.");
        }
        var rfqLines=rfq.Lines.ToDictionary(x=>x.Id);
        if(request.Lines.Select(x=>x.RfqLineId).Distinct().Count()!=request.Lines.Count||request.Lines.Any(x=>!rfqLines.TryGetValue(x.RfqLineId,out var line)||x.Quantity<=0||x.Quantity>line.RequestedQuantity||x.UnitPrice<0||x.DiscountRate is <0 or >100||x.VatRate<0))throw AppException.BadRequest("Teklif satırları geçersiz veya teklif miktarı istenen miktarı aşıyor.");
        var requestedQuoteNo=Norm(request.QuoteNo);
        if(!string.IsNullOrWhiteSpace(requestedQuoteNo)&&await Quotes.Query().AnyAsync(x=>x.ProcurementRfqId==rfqId&&x.SupplierId==supplier.SupplierId&&x.QuoteNo==requestedQuoteNo,ct))throw AppException.Conflict("Bu teklif numarası aynı tedarikçi için zaten kullanılıyor.");
        var quoteNo=requestedQuoteNo??TemporaryNo("QT");
        var quote=new ProcurementSupplierQuote{ProcurementRfqId=rfqId,SupplierId=supplier.SupplierId,SupplierCodeSnapshot=supplier.SupplierCodeSnapshot,SupplierNameSnapshot=supplier.SupplierNameSnapshot,QuoteNo=quoteNo,QuoteDate=request.QuoteDate??DateOnly.FromDateTime(DateTime.Today),ValidUntil=request.ValidUntil,CurrencyCode=Currency(request.CurrencyCode),ExchangeRate=request.ExchangeRate,Note=Norm(request.Note),Status=ProcurementQuoteStatus.Submitted,Lines=request.Lines.Select((l,i)=>new ProcurementSupplierQuoteLine{LineNo=i+1,ProcurementRfqLineId=l.RfqLineId,QuotedQuantity=l.Quantity,UnitPrice=l.UnitPrice,DiscountRate=l.DiscountRate,VatRate=l.VatRate,DeliveryDate=l.DeliveryDate}).ToList()};
        await Quotes.AddAsync(quote,ct);await uow.SaveChangesAsync(ct);
        if(string.IsNullOrWhiteSpace(requestedQuoteNo))quote.QuoteNo=Number("QT",quote.Id);
        rfq.Status=ProcurementRfqStatus.Quoted;await AddHistory("quote",quote.Id,"",quote.Status.ToString(),actorUserId,null,ct);await uow.SaveChangesAsync(ct);return quote.Id;
    }

    public async Task TransitionQuoteAsync(long id,string action,ProcurementTransitionRequest request,long actorUserId,CancellationToken ct=default)
    {
        var x=await Quotes.FindByIdAsync(id,true,ct)??throw AppException.NotFound("Tedarikçi teklifi bulunamadı.");var from=x.Status;x.Status=action.Trim().ToLowerInvariant() switch{"approve" when from==ProcurementQuoteStatus.Submitted=>ProcurementQuoteStatus.Approved,"reject" when from==ProcurementQuoteStatus.Submitted=>ProcurementQuoteStatus.Rejected,"cancel" when from is ProcurementQuoteStatus.Draft or ProcurementQuoteStatus.Submitted or ProcurementQuoteStatus.Approved or ProcurementQuoteStatus.PartiallyConverted=>ProcurementQuoteStatus.Cancelled,_=>throw AppException.Conflict("Teklif mevcut durumunda bu işleme uygun değil.")};await AddHistory("quote",id,from.ToString(),x.Status.ToString(),actorUserId,request.Note,ct);await uow.SaveChangesAsync(ct);
    }

    public async Task<long> ConvertQuoteToOrderAsync(long id,ConvertQuoteToOrderRequest request,long actorUserId,CancellationToken ct=default)=>await ExecuteAllocationAsync(async token=>
    {
        var quote=await Quotes.Query(true).Include(x=>x.Lines).FirstOrDefaultAsync(x=>x.Id==id,token)??throw AppException.NotFound("Tedarikçi teklifi bulunamadı.");
        if(quote.Status is not (ProcurementQuoteStatus.Approved or ProcurementQuoteStatus.PartiallyConverted))throw AppException.Conflict("Yalnızca onaylı ve açık miktarı bulunan teklif siparişe dönüştürülebilir.");
        var policy=await policyService.GetAsync(quote.BranchCode,token);
        if(!policy.AllowMultipleOrdersPerQuote&&await Orders.Query().AnyAsync(x=>x.SourceQuoteId==quote.Id&&x.Status!=ProcurementOrderStatus.Cancelled,token))throw AppException.Conflict("Satınalma politikası aynı tekliften birden fazla sipariş oluşturulmasına izin vermiyor.");
        var openQuoteLines=quote.Lines.Where(x=>x.QuotedQuantity-x.ConvertedQuantity>0).OrderBy(x=>x.LineNo).ToList();
        var selections=ResolveOrderSelections(openQuoteLines,request.Lines,policy.AllowPartialOrderLines);
        var rfqLines=await uow.Repository<ProcurementRfqLine>().Query().Where(x=>x.ProcurementRfqId==quote.ProcurementRfqId).ToDictionaryAsync(x=>x.Id,token);
        var requestLineIds=rfqLines.Values.Where(x=>x.ProcurementRequestLineId.HasValue).Select(x=>x.ProcurementRequestLineId!.Value).Distinct().ToList();
        var requestLines=await uow.Repository<ProcurementRequestLine>().Query(true).Where(x=>requestLineIds.Contains(x.Id)).ToDictionaryAsync(x=>x.Id,token);
        var requestIds=requestLines.Values.Select(x=>x.ProcurementRequestId).Distinct().ToList();
        var sourceRequests=await Requests.Query(true).Include(x=>x.Lines).Where(x=>requestIds.Contains(x.Id)).ToDictionaryAsync(x=>x.Id,token);

        if(!policy.AllowSplitAwardsAcrossSuppliers&&requestIds.Count>0)
        {
            var rfqIds=await Rfqs.Query().Where(x=>x.ProcurementRequestId.HasValue&&requestIds.Contains(x.ProcurementRequestId.Value)).Select(x=>x.Id).ToListAsync(token);
            var quoteIds=await Quotes.Query().Where(x=>rfqIds.Contains(x.ProcurementRfqId)).Select(x=>x.Id).ToListAsync(token);
            if(await Orders.Query().AnyAsync(x=>x.SourceQuoteId.HasValue&&quoteIds.Contains(x.SourceQuoteId.Value)&&x.Status!=ProcurementOrderStatus.Cancelled&&(quote.SupplierId.HasValue?x.SupplierId!=quote.SupplierId:x.SupplierId.HasValue||x.SupplierNameSnapshot!=quote.SupplierNameSnapshot),token))throw AppException.Conflict("Satınalma politikası aynı talep için birden fazla tedarikçiye sipariş bölünmesine izin vermiyor.");
        }

        foreach(var selection in selections)
        {
            var quoteLine=openQuoteLines.Single(x=>x.Id==selection.QuoteLineId);
            var rfqLine=rfqLines[quoteLine.ProcurementRfqLineId];
            if(rfqLine.ProcurementRequestLineId is not long requestLineId)continue;
            var requestLine=requestLines[requestLineId];
            if(selection.Quantity>requestLine.RequestedQuantity-requestLine.ConvertedQuantity)throw AppException.Conflict($"{requestLine.StockCodeSnapshot??requestLine.StockNameSnapshot} için toplam sipariş miktarı talep bakiyesini aşıyor.");
        }

        var selectedLines=selections.Select((selection,index)=>
        {
            var quoteLine=openQuoteLines.Single(x=>x.Id==selection.QuoteLineId);
            var rfqLine=rfqLines[quoteLine.ProcurementRfqLineId];
            quoteLine.ConvertedQuantity+=selection.Quantity;
            if(rfqLine.ProcurementRequestLineId is long requestLineId)requestLines[requestLineId].ConvertedQuantity+=selection.Quantity;
            return new ProcurementPurchaseOrderLine{LineNo=index+1,SourceQuoteLineId=quoteLine.Id,StockId=rfqLine.StockId,StockCodeSnapshot=rfqLine.StockCodeSnapshot,StockNameSnapshot=rfqLine.StockNameSnapshot,UnitCode=rfqLine.UnitCode,OrderedQuantity=selection.Quantity,UnitPrice=quoteLine.UnitPrice,DiscountRate=quoteLine.DiscountRate,VatRate=quoteLine.VatRate,DeliveryDate=quoteLine.DeliveryDate,ProjectCode=rfqLine.ProjectCode};
        }).ToList();
        var deliveryDates=selectedLines.Where(x=>x.DeliveryDate.HasValue).Select(x=>x.DeliveryDate!.Value).ToList();
        var requestedNo=Norm(request.OrderNo);
        if(!string.IsNullOrWhiteSpace(requestedNo)&&await Orders.Query().AnyAsync(x=>x.OrderNo==requestedNo,token))throw AppException.Conflict("Bu sipariş numarası zaten kullanılıyor.");
        var order=new ProcurementPurchaseOrder{OrderNo=requestedNo??TemporaryNo("PO"),OrderDate=request.OrderDate??DateOnly.FromDateTime(DateTime.Today),DeliveryDate=request.DeliveryDate??(deliveryDates.Count>0?deliveryDates.Min():null),SupplierId=quote.SupplierId,SupplierCodeSnapshot=quote.SupplierCodeSnapshot,SupplierNameSnapshot=quote.SupplierNameSnapshot,SourceQuoteId=quote.Id,CurrencyCode=quote.CurrencyCode,ExchangeRate=quote.ExchangeRate,ProjectCode=Norm(request.ProjectCode),Description=Norm(request.Description),Status=ProcurementOrderStatus.Draft,Lines=selectedLines};
        await Orders.AddAsync(order,token);await uow.SaveChangesAsync(token);
        if(string.IsNullOrWhiteSpace(requestedNo))order.OrderNo=Number("PO",order.Id);
        var quoteFrom=quote.Status;quote.Status=quote.Lines.All(x=>x.ConvertedQuantity>=x.QuotedQuantity)?ProcurementQuoteStatus.Converted:ProcurementQuoteStatus.PartiallyConverted;
        if(quoteFrom!=quote.Status)await AddHistory("quote",quote.Id,quoteFrom.ToString(),quote.Status.ToString(),actorUserId,$"{order.OrderNo} siparişi oluşturuldu.",token);
        foreach(var sourceRequest in sourceRequests.Values)
        {
            var from=sourceRequest.Status;
            sourceRequest.Status=sourceRequest.Lines.All(x=>x.ConvertedQuantity>=x.RequestedQuantity)?ProcurementRequestStatus.Converted:sourceRequest.Lines.Any(x=>x.ConvertedQuantity>0)?ProcurementRequestStatus.PartiallyConverted:ProcurementRequestStatus.Approved;
            if(from!=sourceRequest.Status)await AddHistory("request",sourceRequest.Id,from.ToString(),sourceRequest.Status.ToString(),actorUserId,$"{order.OrderNo} siparişi ile talep bakiyesi güncellendi.",token);
        }
        await AddHistory("order",order.Id,"",order.Status.ToString(),actorUserId,null,token);await uow.SaveChangesAsync(token);return order.Id;
    },ct);

    public async Task<long> CreateOrderAsync(CreatePurchaseOrderRequest request,long actorUserId,CancellationToken ct=default)
    {
        if(request.SupplierId<=0||request.ExchangeRate<=0||request.Lines.Count==0)throw AppException.BadRequest("Tedarikçi, kur ve sipariş satırları zorunludur.");
        var supplier=(await ResolveSuppliers([request.SupplierId],ct)).Single();
        var lines=await ResolveOrderLines(request.Lines,ct);
        var requestedNo=Norm(request.OrderNo);
        if(!string.IsNullOrWhiteSpace(requestedNo))await EnsureUniqueDocumentNoAsync("order",requestedNo,ct);
        var order=new ProcurementPurchaseOrder{OrderNo=requestedNo??TemporaryNo("PO"),OrderDate=request.OrderDate??DateOnly.FromDateTime(DateTime.Today),DeliveryDate=request.DeliveryDate,SupplierId=supplier.Id,SupplierCodeSnapshot=supplier.CustomerCode,SupplierNameSnapshot=supplier.CustomerName,CurrencyCode=Currency(request.CurrencyCode),ExchangeRate=request.ExchangeRate,ProjectCode=Norm(request.ProjectCode),Description=Norm(request.Description),Status=ProcurementOrderStatus.Draft,Lines=lines.Select((l,i)=>new ProcurementPurchaseOrderLine{LineNo=i+1,StockId=l.StockId,StockCodeSnapshot=l.StockCode,StockNameSnapshot=l.StockName,UnitCode=l.UnitCode,OrderedQuantity=l.Quantity,UnitPrice=l.UnitPrice,DiscountRate=l.DiscountRate,VatRate=l.VatRate,DeliveryDate=l.DeliveryDate??request.DeliveryDate,ProjectCode=l.ProjectCode??Norm(request.ProjectCode)}).ToList()};
        await Orders.AddAsync(order,ct);await uow.SaveChangesAsync(ct);
        if(string.IsNullOrWhiteSpace(requestedNo))order.OrderNo=Number("PO",order.Id);
        await AddHistory("order",order.Id,"",order.Status.ToString(),actorUserId,null,ct);await uow.SaveChangesAsync(ct);return order.Id;
    }

    public async Task TransitionOrderAsync(long id,string action,ProcurementTransitionRequest request,long actorUserId,CancellationToken ct=default)
    {
        await ExecuteAllocationAsync(async token=>
        {
            var x=await Orders.Query(true).Include(o=>o.Lines).FirstOrDefaultAsync(o=>o.Id==id,token)??throw AppException.NotFound("Satınalma siparişi bulunamadı.");
            var from=x.Status;
            x.Status=action.Trim().ToLowerInvariant() switch{"submit" when from==ProcurementOrderStatus.Draft=>ProcurementOrderStatus.PendingApproval,"approve" when from==ProcurementOrderStatus.PendingApproval=>ProcurementOrderStatus.Approved,"reject" when from==ProcurementOrderStatus.PendingApproval=>ProcurementOrderStatus.Draft,"send" when from==ProcurementOrderStatus.Approved=>ProcurementOrderStatus.SentToSupplier,"cancel" when from is ProcurementOrderStatus.Draft or ProcurementOrderStatus.PendingApproval or ProcurementOrderStatus.Approved or ProcurementOrderStatus.SentToSupplier=>ProcurementOrderStatus.Cancelled,_=>throw AppException.Conflict("Sipariş mevcut durumunda bu işleme uygun değil.")};
            if(x.Status==ProcurementOrderStatus.Approved){x.ApprovedAtUtc=DateTimeOffset.UtcNow;x.ApprovedBy=actorUserId;}
            if(x.Status==ProcurementOrderStatus.Cancelled&&x.SourceQuoteId.HasValue)await ReleaseOrderAwardAsync(x,actorUserId,token);
            await AddHistory("order",id,from.ToString(),x.Status.ToString(),actorUserId,request.Note,token);await uow.SaveChangesAsync(token);
            return true;
        },ct);
    }

    public async Task<IReadOnlyList<ProcurementReceiptSourceLine>> GetOpenReceiptSourceLinesAsync(long? purchaseOrderId,CancellationToken ct=default)=>await Orders.Query().Where(x=>(!purchaseOrderId.HasValue||x.Id==purchaseOrderId)&&(x.Status==ProcurementOrderStatus.Approved||x.Status==ProcurementOrderStatus.SentToSupplier||x.Status==ProcurementOrderStatus.PartiallyReceived)).SelectMany(x=>x.Lines.Where(l=>l.OrderedQuantity-l.ReceivedQuantity-l.CancelledQuantity>0).Select(l=>new ProcurementReceiptSourceLine(x.Id,l.Id,x.OrderNo,l.LineNo,l.StockId,l.StockCodeSnapshot,l.StockNameSnapshot,l.UnitCode,x.SupplierId,x.SupplierCodeSnapshot,x.SupplierNameSnapshot,l.ProjectCode??x.ProjectCode,x.OrderDate,l.DeliveryDate??x.DeliveryDate,l.OrderedQuantity,l.ReceivedQuantity,l.OrderedQuantity-l.ReceivedQuantity-l.CancelledQuantity))).OrderBy(x=>x.OrderNo).ThenBy(x=>x.LineNo).ToListAsync(ct);

    public async Task<ProcurementInvitationResult> SendInvitationAsync(long rfqId,SendProcurementInvitationRequest request,long actorUserId,CancellationToken ct=default)
    {
        if(!MailAddress.TryCreate(request.RecipientEmail?.Trim(),out var recipient))throw AppException.BadRequest("Geçerli bir tedarikçi e-posta adresi giriniz.");
        var rfq=await Rfqs.Query(true).Include(x=>x.Suppliers).FirstOrDefaultAsync(x=>x.Id==rfqId,ct)??throw AppException.NotFound("Teklif talebi bulunamadı.");
        var procurementPolicy=await policyService.GetAsync(rfq.BranchCode,ct);
        if(procurementPolicy.SupplierQuoteChannelMode==SupplierQuoteChannelMode.InternalOnly.ToString())throw AppException.Conflict("Tedarikçi portalı satınalma politikasında kapalı.");
        if(rfq.Status is ProcurementRfqStatus.Closed or ProcurementRfqStatus.Cancelled)throw AppException.Conflict("Kapalı veya iptal edilmiş teklif talebi gönderilemez.");
        var participant=rfq.Suppliers.SingleOrDefault(x=>x.SupplierId==request.SupplierId)??throw AppException.BadRequest("Tedarikçi bu teklif talebinin katılımcısı değil.");
        if(participant.SupplierId is not long participantSupplierId)throw AppException.BadRequest("Manuel tedarikçiye portal daveti gönderilemez.");
        var now=DateTimeOffset.UtcNow;var rawToken=IdentitySecurity.CreateOpaqueToken();var tokenHash=IdentitySecurity.HashToken(rawToken);
        var invitation=await Invitations.Query(true).FirstOrDefaultAsync(x=>x.ProcurementRfqId==rfqId&&x.SupplierId==request.SupplierId,ct);
        if(invitation?.Status==ProcurementInvitationStatus.Submitted)throw AppException.Conflict("Tedarikçi teklifini göndermiş. Yeni fiyat için revizyon isteyin.");
        if(invitation is null)
        {
            invitation=new ProcurementQuoteInvitation{BranchCode=rfq.BranchCode,ProcurementRfqId=rfq.Id,ProcurementRfqSupplierId=participant.Id,SupplierId=participantSupplierId};
            await Invitations.AddAsync(invitation,ct);
        }
        invitation.RecipientEmail=recipient.Address.ToLowerInvariant();invitation.TokenHash=tokenHash;invitation.ExpiresAtUtc=now.AddDays(procurementPolicy.InvitationValidityDays);invitation.LastSentAtUtc=now;invitation.RevokedAtUtc=null;
        invitation.Status=invitation.CurrentQuoteId.HasValue?ProcurementInvitationStatus.DraftSaved:ProcurementInvitationStatus.Sent;
        if(rfq.Status==ProcurementRfqStatus.Draft){rfq.Status=ProcurementRfqStatus.Sent;rfq.SentAtUtc=now;await AddHistory("rfq",rfq.Id,ProcurementRfqStatus.Draft.ToString(),rfq.Status.ToString(),actorUserId,"Tedarikçi portal daveti gönderildi.",ct);}
        await uow.SaveChangesAsync(ct);
        var baseUrl=configuration["FrontendSettings:BaseUrl"]?.TrimEnd('/')??throw new InvalidOperationException("FrontendSettings:BaseUrl is missing.");
        var portalUrl=$"{baseUrl}/supplier/quotes/{Uri.EscapeDataString(rawToken)}";
        try{await emailSender.SendQuoteInvitationAsync(invitation.RecipientEmail,participant.SupplierNameSnapshot,rfq.RfqNo,rfq.Subject,rfq.ResponseDueDate,portalUrl,ct);}
        catch{invitation.Status=ProcurementInvitationStatus.Revoked;invitation.RevokedAtUtc=DateTimeOffset.UtcNow;invitation.TokenHash=IdentitySecurity.HashToken(IdentitySecurity.CreateOpaqueToken());await uow.SaveChangesAsync(CancellationToken.None);throw;}
        await audit.WriteAsync(new("procurement.rfq.invitation.send","ProcurementQuoteInvitation",invitation.Id.ToString(),"Succeeded","procurement",NewValues:new{rfq.RfqNo,participant.SupplierCodeSnapshot,invitation.RecipientEmail,invitation.ExpiresAtUtc}),ct);
        return new(invitation.Id,invitation.Status.ToString(),invitation.RecipientEmail,invitation.ExpiresAtUtc);
    }

    public async Task RevokeInvitationAsync(long rfqId,long supplierId,long actorUserId,CancellationToken ct=default)
    {
        var invitation=await Invitations.Query(true).FirstOrDefaultAsync(x=>x.ProcurementRfqId==rfqId&&x.SupplierId==supplierId,ct)??throw AppException.NotFound("Tedarikçi daveti bulunamadı.");
        if(invitation.Status==ProcurementInvitationStatus.Submitted)throw AppException.Conflict("Gönderilmiş teklifin daveti iptal edilemez; teklif için karar işlemi uygulayın.");
        invitation.Status=ProcurementInvitationStatus.Revoked;invitation.RevokedAtUtc=DateTimeOffset.UtcNow;invitation.TokenHash=IdentitySecurity.HashToken(IdentitySecurity.CreateOpaqueToken());invitation.UpdatedBy=actorUserId;invitation.UpdatedDate=DateTime.UtcNow;
        await uow.SaveChangesAsync(ct);await audit.WriteAsync(new("procurement.rfq.invitation.revoke","ProcurementQuoteInvitation",invitation.Id.ToString(),"Succeeded","procurement"),ct);
    }

    public async Task RequestQuoteRevisionAsync(long quoteId,string? note,long actorUserId,CancellationToken ct=default)
    {
        var quote=await Quotes.Query(true).Include(x=>x.Lines).Include(x=>x.Rfq).FirstOrDefaultAsync(x=>x.Id==quoteId,ct)??throw AppException.NotFound("Tedarikçi teklifi bulunamadı.");
        if(quote.Status!=ProcurementQuoteStatus.Submitted)throw AppException.Conflict("Yalnız sunulmuş teklif için revizyon istenebilir.");
        var procurementPolicy=await policyService.GetAsync(quote.BranchCode,ct);
        if(!procurementPolicy.AllowSupplierRevisions||procurementPolicy.MaximumSupplierRevisionCount==0)throw AppException.Conflict("Satınalma politikası tedarikçi teklif revizyonuna izin vermiyor.");
        if(quote.RevisionNo>=procurementPolicy.MaximumSupplierRevisionCount+1)throw AppException.Conflict("Teklif için izin verilen azami revizyon sayısına ulaşıldı.");
        var invitation=await Invitations.Query(true).FirstOrDefaultAsync(x=>x.CurrentQuoteId==quoteId,ct)??throw AppException.Conflict("Bu teklif tedarikçi portalından oluşturulmamış.");
        var rawToken=IdentitySecurity.CreateOpaqueToken();var now=DateTimeOffset.UtcNow;
        var revision=new ProcurementSupplierQuote{BranchCode=quote.BranchCode,ProcurementRfqId=quote.ProcurementRfqId,SupplierId=quote.SupplierId,SupplierCodeSnapshot=quote.SupplierCodeSnapshot,SupplierNameSnapshot=quote.SupplierNameSnapshot,QuoteNo=$"{quote.QuoteNo}-R{quote.RevisionNo+1}",QuoteDate=DateOnly.FromDateTime(DateTime.UtcNow),ValidUntil=quote.ValidUntil,CurrencyCode=quote.CurrencyCode,ExchangeRate=quote.ExchangeRate,Status=ProcurementQuoteStatus.Draft,Note=Norm(note)??quote.Note,RevisionNo=quote.RevisionNo+1,PreviousQuoteId=quote.Id,Lines=quote.Lines.OrderBy(x=>x.LineNo).Select(x=>new ProcurementSupplierQuoteLine{BranchCode=quote.BranchCode,LineNo=x.LineNo,ProcurementRfqLineId=x.ProcurementRfqLineId,QuotedQuantity=x.QuotedQuantity,UnitPrice=x.UnitPrice,DiscountRate=x.DiscountRate,VatRate=x.VatRate,DeliveryDate=x.DeliveryDate}).ToList()};
        await Quotes.AddAsync(revision,ct);await uow.SaveChangesAsync(ct);
        quote.Status=ProcurementQuoteStatus.Rejected;invitation.CurrentQuoteId=revision.Id;invitation.Status=ProcurementInvitationStatus.RevisionRequested;invitation.SubmittedAtUtc=null;invitation.TokenHash=IdentitySecurity.HashToken(rawToken);invitation.ExpiresAtUtc=now.AddDays(procurementPolicy.InvitationValidityDays);invitation.LastSentAtUtc=now;
        await AddHistory("quote",quote.Id,ProcurementQuoteStatus.Submitted.ToString(),quote.Status.ToString(),actorUserId,Norm(note)??"Revizyon istendi.",ct);await AddHistory("quote",revision.Id,"",revision.Status.ToString(),actorUserId,$"{quote.QuoteNo} teklifinin revizyonu",ct);await uow.SaveChangesAsync(ct);
        var baseUrl=configuration["FrontendSettings:BaseUrl"]?.TrimEnd('/')??throw new InvalidOperationException("FrontendSettings:BaseUrl is missing.");
        await emailSender.SendQuoteInvitationAsync(invitation.RecipientEmail,quote.SupplierNameSnapshot,quote.Rfq.RfqNo,quote.Rfq.Subject,quote.Rfq.ResponseDueDate,$"{baseUrl}/supplier/quotes/{Uri.EscapeDataString(rawToken)}",ct);
    }

    private IQueryable<ProcurementGridRow> RequestRows(PagedRequest r){var s=r.Search?.Trim();return Requests.Query().Where(x=>string.IsNullOrWhiteSpace(s)||x.RequestNo.Contains(s)||x.Subject.Contains(s)).Select(x=>new ProcurementGridRow(x.Id,"request",x.RequestNo,x.RequestDate,x.Status.ToString(),x.Subject,null,x.Lines.Count,0,"TRY",x.RequiredDate,x.CreatedDate,x.Id,x.RequestNo,null)).ApplyAdvancedFilters(r).ApplySort(r,nameof(ProcurementGridRow.DocumentDate));}
    private IQueryable<ProcurementGridRow> RfqRows(PagedRequest r){var s=r.Search?.Trim();return Rfqs.Query().Where(x=>string.IsNullOrWhiteSpace(s)||x.RfqNo.Contains(s)||x.Subject.Contains(s)).Select(x=>new ProcurementGridRow(x.Id,"rfq",x.RfqNo,x.RfqDate,x.Status.ToString(),x.Subject,x.Suppliers.OrderBy(y=>y.Id).Select(y=>y.SupplierNameSnapshot).FirstOrDefault(),x.Lines.Count,0,"TRY",x.ResponseDueDate,x.CreatedDate,x.ProcurementRequestId,x.Request!=null?x.Request.RequestNo:null,x.Id)).ApplyAdvancedFilters(r).ApplySort(r,nameof(ProcurementGridRow.DocumentDate));}
    private IQueryable<ProcurementGridRow> QuoteRows(PagedRequest r){var s=r.Search?.Trim();return Quotes.Query().Where(x=>string.IsNullOrWhiteSpace(s)||x.QuoteNo.Contains(s)||x.SupplierNameSnapshot.Contains(s)||(x.Rfq.Request!=null&&(x.Rfq.Request.RequestNo.Contains(s)||x.Rfq.Request.Subject.Contains(s)))).Select(x=>new ProcurementGridRow(x.Id,"quote",x.QuoteNo,x.QuoteDate,x.Status.ToString(),x.Rfq.Request!=null?x.Rfq.Request.Subject:x.Rfq.Subject,x.SupplierNameSnapshot,x.Lines.Count,x.Lines.Sum(l=>l.QuotedQuantity*l.UnitPrice*(1-l.DiscountRate/100)*(1+l.VatRate/100)),x.CurrencyCode,x.Lines.Where(l=>l.DeliveryDate.HasValue).Select(l=>l.DeliveryDate).Min()??x.ValidUntil,x.CreatedDate,x.Rfq.ProcurementRequestId,x.Rfq.Request!=null?x.Rfq.Request.RequestNo:null,x.ProcurementRfqId)).ApplyAdvancedFilters(r).ApplySort(r,nameof(ProcurementGridRow.DocumentDate));}
    private IQueryable<ProcurementGridRow> OrderRows(PagedRequest r){var s=r.Search?.Trim();return Orders.Query().Where(x=>string.IsNullOrWhiteSpace(s)||x.OrderNo.Contains(s)||x.SupplierNameSnapshot.Contains(s)).Select(x=>new ProcurementGridRow(x.Id,"order",x.OrderNo,x.OrderDate,x.Status.ToString(),"Satınalma siparişi",x.SupplierNameSnapshot,x.Lines.Count,x.Lines.Sum(l=>l.OrderedQuantity*l.UnitPrice*(1-l.DiscountRate/100)*(1+l.VatRate/100)),x.CurrencyCode,x.DeliveryDate,x.CreatedDate,null,null,null)).ApplyAdvancedFilters(r).ApplySort(r,nameof(ProcurementGridRow.DocumentDate));}

    private async Task AddHistory(string type,long id,string from,string to,long actor,string? note,CancellationToken ct)=>await History.AddAsync(new ProcurementStatusHistory{DocumentType=type,DocumentId=id,FromStatus=from,ToStatus=to,ActorUserId=actor,Note=Norm(note),ChangedAtUtc=DateTimeOffset.UtcNow},ct);
    private async Task<T> ExecuteAllocationAsync<T>(Func<CancellationToken,Task<T>> operation,CancellationToken ct)
    {
        try{return await uow.ExecuteInTransactionAsync(operation,ct);}
        catch(DbUpdateConcurrencyException){throw AppException.Conflict("Talep veya teklif bakiyesi başka bir kullanıcı tarafından güncellendi. Ekranı yenileyip açık miktarlar üzerinden tekrar deneyin.");}
    }
    private static IReadOnlyList<RfqRequestLineInput> ResolveRfqSelections(IReadOnlyList<ProcurementRequestLine> openLines,IReadOnlyList<RfqRequestLineInput>? requested,bool allowPartial)
    {
        var openById=openLines.ToDictionary(x=>x.Id);
        var selections=requested is {Count:>0}?requested:openLines.Select(x=>new RfqRequestLineInput(x.Id,x.RequestedQuantity-x.ConvertedQuantity)).ToList();
        if(selections.Select(x=>x.RequestLineId).Distinct().Count()!=selections.Count||selections.Any(x=>!openById.TryGetValue(x.RequestLineId,out var line)||x.Quantity<=0||x.Quantity>line.RequestedQuantity-line.ConvertedQuantity))throw AppException.BadRequest("Teklif talebi kalemleri geçersiz veya açık talep miktarını aşıyor.");
        if(!allowPartial&&(selections.Count!=openLines.Count||selections.Any(x=>x.Quantity!=openById[x.RequestLineId].RequestedQuantity-openById[x.RequestLineId].ConvertedQuantity)))throw AppException.Conflict("Satınalma politikası teklif talebinde kısmi kalem veya miktara izin vermiyor.");
        return selections;
    }
    private static IReadOnlyList<QuoteOrderLineInput> ResolveOrderSelections(IReadOnlyList<ProcurementSupplierQuoteLine> openLines,IReadOnlyList<QuoteOrderLineInput>? requested,bool allowPartial)
    {
        if(openLines.Count==0)throw AppException.Conflict("Teklifin siparişe dönüştürülebilecek açık miktarı bulunmuyor.");
        var openById=openLines.ToDictionary(x=>x.Id);
        var selections=requested is {Count:>0}?requested:openLines.Select(x=>new QuoteOrderLineInput(x.Id,x.QuotedQuantity-x.ConvertedQuantity)).ToList();
        if(selections.Select(x=>x.QuoteLineId).Distinct().Count()!=selections.Count||selections.Any(x=>!openById.TryGetValue(x.QuoteLineId,out var line)||x.Quantity<=0||x.Quantity>line.QuotedQuantity-line.ConvertedQuantity))throw AppException.BadRequest("Siparişe aktarılacak teklif kalemleri geçersiz veya açık teklif miktarını aşıyor.");
        if(!allowPartial&&(selections.Count!=openLines.Count||selections.Any(x=>x.Quantity!=openById[x.QuoteLineId].QuotedQuantity-openById[x.QuoteLineId].ConvertedQuantity)))throw AppException.Conflict("Satınalma politikası tekliften kısmi sipariş oluşturmaya izin vermiyor.");
        return selections;
    }
    private async Task ReleaseOrderAwardAsync(ProcurementPurchaseOrder order,long actorUserId,CancellationToken ct)
    {
        if(order.Lines.Any(x=>x.ReceivedQuantity>0))throw AppException.Conflict("Mal kabul hareketi bulunan sipariş iptal edilerek talep bakiyesi geri açılamaz.");
        var sourceLineIds=order.Lines.Where(x=>x.SourceQuoteLineId.HasValue).Select(x=>x.SourceQuoteLineId!.Value).Distinct().ToList();
        var quote=await Quotes.Query(true).Include(x=>x.Lines).FirstAsync(x=>x.Id==order.SourceQuoteId!.Value,ct);
        var selectedQuoteLines=quote.Lines.Where(x=>sourceLineIds.Contains(x.Id)).ToDictionary(x=>x.Id);
        var rfqLineIds=selectedQuoteLines.Values.Select(x=>x.ProcurementRfqLineId).Distinct().ToList();
        var rfqLines=await uow.Repository<ProcurementRfqLine>().Query().Where(x=>rfqLineIds.Contains(x.Id)).ToDictionaryAsync(x=>x.Id,ct);
        var requestLineIds=rfqLines.Values.Where(x=>x.ProcurementRequestLineId.HasValue).Select(x=>x.ProcurementRequestLineId!.Value).Distinct().ToList();
        var requestLines=await uow.Repository<ProcurementRequestLine>().Query(true).Where(x=>requestLineIds.Contains(x.Id)).ToDictionaryAsync(x=>x.Id,ct);
        foreach(var line in order.Lines.Where(x=>x.SourceQuoteLineId.HasValue))
        {
            var quoteLine=selectedQuoteLines[line.SourceQuoteLineId!.Value];quoteLine.ConvertedQuantity-=line.OrderedQuantity;
            var rfqLine=rfqLines[quoteLine.ProcurementRfqLineId];
            if(rfqLine.ProcurementRequestLineId is long requestLineId)requestLines[requestLineId].ConvertedQuantity-=line.OrderedQuantity;
        }
        var quoteFrom=quote.Status;
        if(quote.Status!=ProcurementQuoteStatus.Cancelled)quote.Status=quote.Lines.All(x=>x.ConvertedQuantity<=0)?ProcurementQuoteStatus.Approved:quote.Lines.All(x=>x.ConvertedQuantity>=x.QuotedQuantity)?ProcurementQuoteStatus.Converted:ProcurementQuoteStatus.PartiallyConverted;
        if(quoteFrom!=quote.Status)await AddHistory("quote",quote.Id,quoteFrom.ToString(),quote.Status.ToString(),actorUserId,$"{order.OrderNo} iptal edildi; teklif bakiyesi geri açıldı.",ct);
        var requestIds=requestLines.Values.Select(x=>x.ProcurementRequestId).Distinct().ToList();
        var requests=await Requests.Query(true).Include(x=>x.Lines).Where(x=>requestIds.Contains(x.Id)).ToListAsync(ct);
        foreach(var source in requests){var from=source.Status;source.Status=source.Lines.All(x=>x.ConvertedQuantity>=x.RequestedQuantity)?ProcurementRequestStatus.Converted:source.Lines.Any(x=>x.ConvertedQuantity>0)?ProcurementRequestStatus.PartiallyConverted:ProcurementRequestStatus.Approved;if(from!=source.Status)await AddHistory("request",source.Id,from.ToString(),source.Status.ToString(),actorUserId,$"{order.OrderNo} iptali ile talep bakiyesi geri açıldı.",ct);}
    }
    private async Task<List<CustomerEntity>> ResolveSuppliers(IEnumerable<long> ids,CancellationToken ct)
    {
        var selected=ids.Where(x=>x>0).Distinct().ToList();
        var rows=await uow.Repository<CustomerEntity>().Query().Where(x=>selected.Contains(x.Id)).ToListAsync(ct);
        if(rows.Count!=selected.Count)throw AppException.BadRequest("Seçilen tedarikçilerden biri bulunamadı.");
        return rows;
    }
    private async Task<List<ResolvedLine>> ResolveLines(IReadOnlyList<ProcurementLineInput> lines,CancellationToken ct){if(lines.Count==0||lines.Any(x=>x.Quantity<=0||string.IsNullOrWhiteSpace(x.StockName)||string.IsNullOrWhiteSpace(x.UnitCode)))throw AppException.BadRequest("En az bir geçerli talep satırı zorunludur.");var ids=lines.Where(x=>x.StockId.HasValue).Select(x=>x.StockId!.Value).Distinct().ToList();var stocks=await uow.Repository<StockEntity>().Query().Where(x=>ids.Contains(x.Id)).ToDictionaryAsync(x=>x.Id,ct);if(stocks.Count!=ids.Count)throw AppException.BadRequest("Seçilen stoklardan biri bulunamadı.");return lines.Select(x=>{var stock=x.StockId.HasValue?stocks[x.StockId.Value]:null;return new ResolvedLine(x.StockId,stock?.ErpStockCode??Norm(x.StockCode),stock?.StockName??x.StockName.Trim(),stock?.BaseUnitCode??x.UnitCode.Trim().ToUpperInvariant(),x.Quantity,x.RequiredDate,Norm(x.ProjectCode),Norm(x.Description));}).ToList();}
    private async Task<List<ResolvedOrderLine>> ResolveOrderLines(IReadOnlyList<PurchaseOrderLineInput> lines,CancellationToken ct){if(lines.Any(x=>x.Quantity<=0||x.UnitPrice<0||x.DiscountRate is <0 or >100||x.VatRate<0))throw AppException.BadRequest("Sipariş satırları geçersiz.");var baseLines=await ResolveLines(lines.Select(x=>new ProcurementLineInput(x.StockId,x.StockCode,x.StockName,x.UnitCode,x.Quantity,x.DeliveryDate,x.ProjectCode,null)).ToList(),ct);return baseLines.Select((x,i)=>new ResolvedOrderLine(x.StockId,x.StockCode,x.StockName,x.UnitCode,x.Quantity,lines[i].UnitPrice,lines[i].DiscountRate,lines[i].VatRate,lines[i].DeliveryDate,x.ProjectCode)).ToList();}
    private static void ValidateHeader(string subject,IReadOnlyList<ProcurementLineInput> lines){if(string.IsNullOrWhiteSpace(subject)||subject.Trim().Length>250||lines.Count==0)throw AppException.BadRequest("Talep konusu ve en az bir satır zorunludur.");}
    private static string NormalizeType(string value)=>value.Trim().ToLowerInvariant() switch{"requests"=>"request","rfqs"=>"rfq","quotes"=>"quote","orders"=>"order",var x=>x};
    private async Task EnsureUniqueDocumentNoAsync(string documentType,string documentNo,CancellationToken ct)
    {
        var exists=documentType switch
        {
            "request"=>await Requests.Query().AnyAsync(x=>x.RequestNo==documentNo,ct),
            "rfq"=>await Rfqs.Query().AnyAsync(x=>x.RfqNo==documentNo,ct),
            "order"=>await Orders.Query().AnyAsync(x=>x.OrderNo==documentNo,ct),
            _=>false
        };
        if(exists)throw AppException.Conflict("Bu belge numarası zaten kullanılıyor.");
    }
    private static string TemporaryNo(string prefix)=>$"{prefix}-TMP-{Guid.NewGuid():N}"[..Math.Min(50,prefix.Length+36)];
    private static string Number(string prefix,long id)=>$"{prefix}-{DateTime.Today:yyyy}-{id:00000000}";
    private static string? Norm(string? value)=>string.IsNullOrWhiteSpace(value)?null:value.Trim();
    private static string? Truncate(string? value,int max)=>value is null?null:value.Length<=max?value:value[..max];
    private static string Currency(string? value)=>string.IsNullOrWhiteSpace(value)?"TRY":value.Trim().ToUpperInvariant();

    public Task<IReadOnlyList<ProcurementAttachmentRow>> ListAttachmentsAsync(string ownerType,long ownerId,CancellationToken ct=default)
        => ListAttachmentsInternalAsync(ParseOwnerType(ownerType),ownerId,ct);

    public async Task<ProcurementAttachmentRow> AddAttachmentAsync(string ownerType,long ownerId,ProcurementAttachmentUpload upload,string? caption,long actorUserId,CancellationToken ct=default)
    {
        var parsed=ParseOwnerType(ownerType);
        await EnsureAttachmentOwnerExistsAsync(parsed,ownerId,ct);
        var path=await attachmentStorage.SaveAsync(ownerId,upload.Content,upload.ContentType,upload.FileName,upload.Length,ct);
        try
        {
            var entity=new ProcurementAttachment
            {
                OwnerType=parsed,
                OwnerId=ownerId,
                FileName=PrivateUploadFileName.ForDisplay(upload.FileName),
                ContentType=upload.ContentType??"application/octet-stream",
                StoragePath=path,
                Caption=Truncate(Norm(caption),500),
                FileSize=upload.Length,
                CreatedBy=actorUserId,
                CreatedDate=DateTime.UtcNow,
            };
            await Attachments.AddAsync(entity,ct);
            await uow.SaveChangesAsync(ct);
            await audit.WriteAsync(new("procurement.attachment.add",nameof(ProcurementAttachment),entity.Id.ToString(),"Succeeded","procurement",
                NewValues:new{entity.OwnerType,entity.OwnerId,entity.FileName,entity.ContentType,entity.FileSize},ChangedFields:["Attachment"]),ct);
            return ToAttachmentRow(entity);
        }
        catch
        {
            attachmentStorage.Delete(path);
            throw;
        }
    }

    public async Task<ProcurementAttachmentDownload> DownloadAttachmentAsync(long attachmentId,CancellationToken ct=default)
    {
        var entity=await Attachments.FindByIdAsync(attachmentId,false,ct)??throw AppException.NotFound("Satınalma eki bulunamadı.");
        return new(await attachmentStorage.OpenReadAsync(entity.StoragePath,ct),entity.FileName,entity.ContentType);
    }

    public async Task RemoveAttachmentAsync(long attachmentId,long actorUserId,CancellationToken ct=default)
    {
        var entity=await Attachments.Query(true).FirstOrDefaultAsync(x=>x.Id==attachmentId,ct)??throw AppException.NotFound("Satınalma eki bulunamadı.");
        entity.IsDeleted=true;
        entity.DeletedBy=actorUserId;
        entity.DeletedDate=DateTime.UtcNow;
        await uow.SaveChangesAsync(ct);
        attachmentStorage.Delete(entity.StoragePath);
        await audit.WriteAsync(new("procurement.attachment.remove",nameof(ProcurementAttachment),entity.Id.ToString(),"Succeeded","procurement",
            OldValues:new{entity.OwnerType,entity.OwnerId,entity.FileName},ChangedFields:["Attachment"]),ct);
    }

    private async Task<(IReadOnlyList<ProcurementAttachmentRow> Header,Dictionary<long,IReadOnlyList<ProcurementAttachmentRow>> Lines)> LoadAttachmentBundleAsync(
        ProcurementAttachmentOwnerType headerType,long headerId,ProcurementAttachmentOwnerType lineType,IEnumerable<long> lineIds,CancellationToken ct)
    {
        var ids=lineIds.Distinct().ToList();
        var rows=await Attachments.Query()
            .Where(x=>(x.OwnerType==headerType&&x.OwnerId==headerId)||(x.OwnerType==lineType&&ids.Contains(x.OwnerId)))
            .OrderByDescending(x=>x.CreatedDate)
            .ToListAsync(ct);
        var header=rows.Where(x=>x.OwnerType==headerType).Select(ToAttachmentRow).ToList();
        var lines=rows.Where(x=>x.OwnerType==lineType)
            .GroupBy(x=>x.OwnerId)
            .ToDictionary(g=>(long)g.Key,g=>(IReadOnlyList<ProcurementAttachmentRow>)g.Select(ToAttachmentRow).ToList());
        return (header,lines);
    }

    private async Task<IReadOnlyList<ProcurementAttachmentRow>> ListAttachmentsInternalAsync(ProcurementAttachmentOwnerType ownerType,long ownerId,CancellationToken ct)
    {
        await EnsureAttachmentOwnerExistsAsync(ownerType,ownerId,ct);
        var rows=await Attachments.Query()
            .Where(x=>x.OwnerType==ownerType&&x.OwnerId==ownerId)
            .OrderByDescending(x=>x.CreatedDate)
            .ToListAsync(ct);
        return rows.Select(ToAttachmentRow).ToList();
    }

    private async Task EnsureAttachmentOwnerExistsAsync(ProcurementAttachmentOwnerType ownerType,long ownerId,CancellationToken ct)
    {
        var exists=ownerType switch
        {
            ProcurementAttachmentOwnerType.Request=>await Requests.Query().AnyAsync(x=>x.Id==ownerId,ct),
            ProcurementAttachmentOwnerType.RequestLine=>await uow.Repository<ProcurementRequestLine>().Query().AnyAsync(x=>x.Id==ownerId,ct),
            ProcurementAttachmentOwnerType.Quote=>await Quotes.Query().AnyAsync(x=>x.Id==ownerId,ct),
            ProcurementAttachmentOwnerType.QuoteLine=>await uow.Repository<ProcurementSupplierQuoteLine>().Query().AnyAsync(x=>x.Id==ownerId,ct),
            _=>false
        };
        if(!exists)throw AppException.NotFound("Ek dosya sahibi bulunamadı.");
    }

    private static ProcurementAttachmentOwnerType ParseOwnerType(string ownerType)=>ownerType.Trim().ToLowerInvariant() switch
    {
        "request"=>ProcurementAttachmentOwnerType.Request,
        "request-line" or "requestline"=>ProcurementAttachmentOwnerType.RequestLine,
        "quote"=>ProcurementAttachmentOwnerType.Quote,
        "quote-line" or "quoteline"=>ProcurementAttachmentOwnerType.QuoteLine,
        _=>throw AppException.BadRequest("Geçersiz ek dosya sahibi türü.")
    };

    private static string OwnerTypeKey(ProcurementAttachmentOwnerType ownerType)=>ownerType switch
    {
        ProcurementAttachmentOwnerType.Request=>"request",
        ProcurementAttachmentOwnerType.RequestLine=>"request-line",
        ProcurementAttachmentOwnerType.Quote=>"quote",
        ProcurementAttachmentOwnerType.QuoteLine=>"quote-line",
        _=>ownerType.ToString().ToLowerInvariant()
    };

    private static ProcurementAttachmentRow ToAttachmentRow(ProcurementAttachment entity)=>
        new(entity.Id,OwnerTypeKey(entity.OwnerType),entity.OwnerId,entity.FileName,entity.ContentType,
            $"/api/procurement/attachments/{entity.Id}/file",entity.FileSize,entity.Caption,entity.CreatedDate);

    private sealed record ResolvedLine(long? StockId,string? StockCode,string StockName,string UnitCode,decimal Quantity,DateOnly? RequiredDate,string? ProjectCode,string? Description);
    private sealed record ResolvedOrderLine(long? StockId,string? StockCode,string StockName,string UnitCode,decimal Quantity,decimal UnitPrice,decimal DiscountRate,decimal VatRate,DateOnly? DeliveryDate,string? ProjectCode);
}
