using Microsoft.EntityFrameworkCore;
using verii_wms_api_v2.Modules.Audit.Application;
using verii_wms_api_v2.Modules.Procurement.Domain;
using verii_wms_api_v2.Shared;
using verii_wms_api_v2.Shared.Application.Abstractions.Persistence;
using verii_wms_api_v2.Shared.Application.Exceptions;
using CustomerEntity = verii_wms_api_v2.Modules.Customer.Domain.Customer;
using StockEntity = verii_wms_api_v2.Modules.Stock.Domain.Stock;

namespace verii_wms_api_v2.Modules.Procurement.Application;

public sealed class ProcurementService(IUnitOfWork uow,IAuditLogWriter audit,IProcurementPolicyService policyService) : IProcurementService
{
    private IGenericRepository<ProcurementRequest> Requests=>uow.Repository<ProcurementRequest>();
    private IGenericRepository<ProcurementRfq> Rfqs=>uow.Repository<ProcurementRfq>();
    private IGenericRepository<ProcurementSupplierQuote> Quotes=>uow.Repository<ProcurementSupplierQuote>();
    private IGenericRepository<ProcurementPurchaseOrder> Orders=>uow.Repository<ProcurementPurchaseOrder>();
    private IGenericRepository<ProcurementStatusHistory> History=>uow.Repository<ProcurementStatusHistory>();

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
            return new(x.Id,type,x.RequestNo,x.RequestDate,x.Status.ToString(),x.Subject,x.Description,null,null,"TRY",1,x.RequiredDate,x.Lines.OrderBy(l=>l.LineNo).Select(l=>new ProcurementLineDetail(l.Id,l.LineNo,l.StockId,l.StockCodeSnapshot,l.StockNameSnapshot,l.UnitCode,l.RequestedQuantity,l.ConvertedQuantity,0,0,0,l.RequiredDate,l.ProjectCode,l.RequestedQuantity-l.ConvertedQuantity)).ToList(),histories);
        }
        if(type=="rfq")
        {
            var x=await Rfqs.Query().Include(x=>x.Lines).Include(x=>x.Suppliers).FirstOrDefaultAsync(x=>x.Id==id,ct)??throw AppException.NotFound("Teklif talebi bulunamadı.");
            return new(x.Id,type,x.RfqNo,x.RfqDate,x.Status.ToString(),x.Subject,x.BuyerMessage,null,string.Join(", ",x.Suppliers.Select(s=>s.SupplierNameSnapshot)),"TRY",1,x.ResponseDueDate,x.Lines.OrderBy(l=>l.LineNo).Select(l=>new ProcurementLineDetail(l.Id,l.LineNo,l.StockId,l.StockCodeSnapshot,l.StockNameSnapshot,l.UnitCode,l.RequestedQuantity,0,0,0,0,l.RequiredDate,l.ProjectCode,l.RequestedQuantity)).ToList(),histories,x.Suppliers.OrderBy(s=>s.SupplierNameSnapshot).Select(s=>new ProcurementSupplierParticipant(s.SupplierId,s.SupplierCodeSnapshot,s.SupplierNameSnapshot)).ToList());
        }
        if(type=="quote")
        {
            var x=await Quotes.Query().Include(x=>x.Lines).ThenInclude(l=>l.Quote).FirstOrDefaultAsync(x=>x.Id==id,ct)??throw AppException.NotFound("Tedarikçi teklifi bulunamadı.");
            var rfqLines=await uow.Repository<ProcurementRfqLine>().Query().Where(l=>l.ProcurementRfqId==x.ProcurementRfqId).ToDictionaryAsync(l=>l.Id,ct);
            return new(x.Id,type,x.QuoteNo,x.QuoteDate,x.Status.ToString(),"Tedarikçi teklifi",x.Note,x.SupplierCodeSnapshot,x.SupplierNameSnapshot,x.CurrencyCode,x.ExchangeRate,x.ValidUntil,x.Lines.OrderBy(l=>l.LineNo).Select(l=>{var r=rfqLines[l.ProcurementRfqLineId];return new ProcurementLineDetail(l.Id,l.LineNo,r.StockId,r.StockCodeSnapshot,r.StockNameSnapshot,r.UnitCode,l.QuotedQuantity,l.ConvertedQuantity,l.UnitPrice,l.DiscountRate,l.VatRate,l.DeliveryDate,r.ProjectCode,l.QuotedQuantity-l.ConvertedQuantity);}).ToList(),histories);
        }
        if(type=="order")
        {
            var x=await Orders.Query().Include(x=>x.Lines).FirstOrDefaultAsync(x=>x.Id==id,ct)??throw AppException.NotFound("Satınalma siparişi bulunamadı.");
            return new(x.Id,type,x.OrderNo,x.OrderDate,x.Status.ToString(),"Satınalma siparişi",x.Description,x.SupplierCodeSnapshot,x.SupplierNameSnapshot,x.CurrencyCode,x.ExchangeRate,x.DeliveryDate,x.Lines.OrderBy(l=>l.LineNo).Select(l=>new ProcurementLineDetail(l.Id,l.LineNo,l.StockId,l.StockCodeSnapshot,l.StockNameSnapshot,l.UnitCode,l.OrderedQuantity,l.ReceivedQuantity,l.UnitPrice,l.DiscountRate,l.VatRate,l.DeliveryDate,l.ProjectCode,l.OrderedQuantity-l.ReceivedQuantity-l.CancelledQuantity)).ToList(),histories);
        }
        throw AppException.BadRequest("Geçersiz satınalma belge türü.");
    }

    public async Task<long> CreateRequestAsync(CreateProcurementRequest request,long actorUserId,CancellationToken ct=default)
    {
        ValidateHeader(request.Subject,request.Lines);
        var lines=await ResolveLines(request.Lines,ct);
        var entity=new ProcurementRequest{RequestNo=TemporaryNo("REQ"),RequestDate=request.RequestDate??DateOnly.FromDateTime(DateTime.Today),RequiredDate=request.RequiredDate,DepartmentCode=Norm(request.DepartmentCode),ProjectCode=Norm(request.ProjectCode),Subject=request.Subject.Trim(),Description=Norm(request.Description),Status=ProcurementRequestStatus.Draft};
        entity.Lines=lines.Select((l,i)=>new ProcurementRequestLine{LineNo=i+1,StockId=l.StockId,StockCodeSnapshot=l.StockCode,StockNameSnapshot=l.StockName,UnitCode=l.UnitCode,RequestedQuantity=l.Quantity,RequiredDate=l.RequiredDate??request.RequiredDate,ProjectCode=l.ProjectCode??Norm(request.ProjectCode),Description=l.Description}).ToList();
        await Requests.AddAsync(entity,ct); await uow.SaveChangesAsync(ct); entity.RequestNo=Number("REQ",entity.Id); await uow.SaveChangesAsync(ct);
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
            var suppliers=await ResolveSuppliers(request.SupplierIds,token);
            if(suppliers.Count==0)throw AppException.BadRequest("En az bir tedarikçi seçilmelidir.");

            var openLines=source.Lines.Where(x=>x.RequestedQuantity-x.ConvertedQuantity>0).OrderBy(x=>x.LineNo).ToList();
            if(openLines.Count==0)throw AppException.Conflict("Talebin siparişe bağlanmamış açık miktarı bulunmuyor.");
            var selections=ResolveRfqSelections(openLines,request.Lines,policy.AllowPartialRfqLines);
            var rfq=new ProcurementRfq
            {
                RfqNo=TemporaryNo("RFQ"),RfqDate=DateOnly.FromDateTime(DateTime.Today),ResponseDueDate=request.ResponseDueDate,
                ProcurementRequestId=source.Id,Subject=source.Subject,BuyerMessage=Norm(request.BuyerMessage),Status=ProcurementRfqStatus.Draft,
                Lines=selections.Select((selection,index)=>
                {
                    var line=openLines.Single(x=>x.Id==selection.RequestLineId);
                    return new ProcurementRfqLine{LineNo=index+1,ProcurementRequestLineId=line.Id,StockId=line.StockId,StockCodeSnapshot=line.StockCodeSnapshot,StockNameSnapshot=line.StockNameSnapshot,UnitCode=line.UnitCode,RequestedQuantity=selection.Quantity,RequiredDate=line.RequiredDate,ProjectCode=line.ProjectCode};
                }).ToList(),
                Suppliers=suppliers.Select(s=>new ProcurementRfqSupplier{SupplierId=s.Id,SupplierCodeSnapshot=s.CustomerCode,SupplierNameSnapshot=s.CustomerName}).ToList()
            };
            await Rfqs.AddAsync(rfq,token);await uow.SaveChangesAsync(token);rfq.RfqNo=Number("RFQ",rfq.Id);
            await AddHistory("rfq",rfq.Id,"",rfq.Status.ToString(),actorUserId,$"{rfq.Lines.Count} kalem, {rfq.Suppliers.Count} tedarikçi",token);
            await uow.SaveChangesAsync(token);return rfq.Id;
        },ct);
    }

    public async Task TransitionRfqAsync(long id,string action,ProcurementTransitionRequest request,long actorUserId,CancellationToken ct=default)
    {
        var x=await Rfqs.FindByIdAsync(id,true,ct)??throw AppException.NotFound("Teklif talebi bulunamadı.");var from=x.Status;x.Status=action.Trim().ToLowerInvariant() switch{"send" when from==ProcurementRfqStatus.Draft=>ProcurementRfqStatus.Sent,"close" when from is ProcurementRfqStatus.Sent or ProcurementRfqStatus.Quoted=>ProcurementRfqStatus.Closed,"cancel" when from is ProcurementRfqStatus.Draft or ProcurementRfqStatus.Sent or ProcurementRfqStatus.Quoted=>ProcurementRfqStatus.Cancelled,_=>throw AppException.Conflict("Teklif talebi mevcut durumunda bu işleme uygun değil.")};if(x.Status==ProcurementRfqStatus.Sent)x.SentAtUtc=DateTimeOffset.UtcNow;await AddHistory("rfq",id,from.ToString(),x.Status.ToString(),actorUserId,request.Note,ct);await uow.SaveChangesAsync(ct);
    }

    public async Task<long> CreateQuoteAsync(long rfqId,CreateSupplierQuoteRequest request,long actorUserId,CancellationToken ct=default)
    {
        var rfq=await Rfqs.Query(true).Include(x=>x.Lines).Include(x=>x.Suppliers).FirstOrDefaultAsync(x=>x.Id==rfqId,ct)??throw AppException.NotFound("Teklif talebi bulunamadı.");
        if(rfq.Status is not (ProcurementRfqStatus.Sent or ProcurementRfqStatus.Quoted))throw AppException.Conflict("Teklif kaydı için teklif talebi gönderilmiş olmalıdır.");
        var supplier=rfq.Suppliers.FirstOrDefault(x=>x.SupplierId==request.SupplierId)??throw AppException.BadRequest("Tedarikçi bu teklif talebinin katılımcısı değil.");
        if(string.IsNullOrWhiteSpace(request.QuoteNo)||request.ExchangeRate<=0||request.Lines.Count==0)throw AppException.BadRequest("Teklif numarası, kur ve satırlar zorunludur.");
        var policy=await policyService.GetAsync(rfq.BranchCode,ct);
        if(!policy.AllowMultipleQuotesPerSupplier&&await Quotes.Query().AnyAsync(x=>x.ProcurementRfqId==rfqId&&x.SupplierId==request.SupplierId&&x.Status!=ProcurementQuoteStatus.Cancelled&&x.Status!=ProcurementQuoteStatus.Rejected,ct))throw AppException.Conflict("Satınalma politikası aynı tedarikçinin bu teklif talebine birden fazla teklif vermesine izin vermiyor.");
        var rfqLines=rfq.Lines.ToDictionary(x=>x.Id);
        if(request.Lines.Select(x=>x.RfqLineId).Distinct().Count()!=request.Lines.Count||request.Lines.Any(x=>!rfqLines.TryGetValue(x.RfqLineId,out var line)||x.Quantity<=0||x.Quantity>line.RequestedQuantity||x.UnitPrice<0||x.DiscountRate is <0 or >100||x.VatRate<0))throw AppException.BadRequest("Teklif satırları geçersiz veya teklif miktarı istenen miktarı aşıyor.");
        var quote=new ProcurementSupplierQuote{ProcurementRfqId=rfqId,SupplierId=supplier.SupplierId,SupplierCodeSnapshot=supplier.SupplierCodeSnapshot,SupplierNameSnapshot=supplier.SupplierNameSnapshot,QuoteNo=request.QuoteNo.Trim(),QuoteDate=request.QuoteDate??DateOnly.FromDateTime(DateTime.Today),ValidUntil=request.ValidUntil,CurrencyCode=Currency(request.CurrencyCode),ExchangeRate=request.ExchangeRate,Note=Norm(request.Note),Status=ProcurementQuoteStatus.Submitted,Lines=request.Lines.Select((l,i)=>new ProcurementSupplierQuoteLine{LineNo=i+1,ProcurementRfqLineId=l.RfqLineId,QuotedQuantity=l.Quantity,UnitPrice=l.UnitPrice,DiscountRate=l.DiscountRate,VatRate=l.VatRate,DeliveryDate=l.DeliveryDate}).ToList()};
        await Quotes.AddAsync(quote,ct);rfq.Status=ProcurementRfqStatus.Quoted;await uow.SaveChangesAsync(ct);await AddHistory("quote",quote.Id,"",quote.Status.ToString(),actorUserId,null,ct);await uow.SaveChangesAsync(ct);return quote.Id;
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
            if(await Orders.Query().AnyAsync(x=>x.SourceQuoteId.HasValue&&quoteIds.Contains(x.SourceQuoteId.Value)&&x.SupplierId!=quote.SupplierId&&x.Status!=ProcurementOrderStatus.Cancelled,token))throw AppException.Conflict("Satınalma politikası aynı talep için birden fazla tedarikçiye sipariş bölünmesine izin vermiyor.");
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
        var order=new ProcurementPurchaseOrder{OrderNo=TemporaryNo("PO"),OrderDate=request.OrderDate??DateOnly.FromDateTime(DateTime.Today),DeliveryDate=request.DeliveryDate??(deliveryDates.Count>0?deliveryDates.Min():null),SupplierId=quote.SupplierId,SupplierCodeSnapshot=quote.SupplierCodeSnapshot,SupplierNameSnapshot=quote.SupplierNameSnapshot,SourceQuoteId=quote.Id,CurrencyCode=quote.CurrencyCode,ExchangeRate=quote.ExchangeRate,ProjectCode=Norm(request.ProjectCode),Description=Norm(request.Description),Status=ProcurementOrderStatus.Draft,Lines=selectedLines};
        await Orders.AddAsync(order,token);await uow.SaveChangesAsync(token);order.OrderNo=Number("PO",order.Id);
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
        if(request.SupplierId<=0||request.ExchangeRate<=0||request.Lines.Count==0)throw AppException.BadRequest("Tedarikçi, kur ve sipariş satırları zorunludur.");var supplier=(await ResolveSuppliers([request.SupplierId],ct)).Single();var lines=await ResolveOrderLines(request.Lines,ct);var order=new ProcurementPurchaseOrder{OrderNo=TemporaryNo("PO"),OrderDate=request.OrderDate??DateOnly.FromDateTime(DateTime.Today),DeliveryDate=request.DeliveryDate,SupplierId=supplier.Id,SupplierCodeSnapshot=supplier.CustomerCode,SupplierNameSnapshot=supplier.CustomerName,CurrencyCode=Currency(request.CurrencyCode),ExchangeRate=request.ExchangeRate,ProjectCode=Norm(request.ProjectCode),Description=Norm(request.Description),Status=ProcurementOrderStatus.Draft,Lines=lines.Select((l,i)=>new ProcurementPurchaseOrderLine{LineNo=i+1,StockId=l.StockId,StockCodeSnapshot=l.StockCode,StockNameSnapshot=l.StockName,UnitCode=l.UnitCode,OrderedQuantity=l.Quantity,UnitPrice=l.UnitPrice,DiscountRate=l.DiscountRate,VatRate=l.VatRate,DeliveryDate=l.DeliveryDate??request.DeliveryDate,ProjectCode=l.ProjectCode??Norm(request.ProjectCode)}).ToList()};await Orders.AddAsync(order,ct);await uow.SaveChangesAsync(ct);order.OrderNo=Number("PO",order.Id);await AddHistory("order",order.Id,"",order.Status.ToString(),actorUserId,null,ct);await uow.SaveChangesAsync(ct);return order.Id;
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

    private IQueryable<ProcurementGridRow> RequestRows(PagedRequest r){var s=r.Search?.Trim();return Requests.Query().Where(x=>string.IsNullOrWhiteSpace(s)||x.RequestNo.Contains(s)||x.Subject.Contains(s)).Select(x=>new ProcurementGridRow(x.Id,"request",x.RequestNo,x.RequestDate,x.Status.ToString(),x.Subject,null,x.Lines.Count,0,"TRY",x.RequiredDate,x.CreatedDate)).ApplyAdvancedFilters(r).ApplySort(r,nameof(ProcurementGridRow.DocumentDate));}
    private IQueryable<ProcurementGridRow> RfqRows(PagedRequest r){var s=r.Search?.Trim();return Rfqs.Query().Where(x=>string.IsNullOrWhiteSpace(s)||x.RfqNo.Contains(s)||x.Subject.Contains(s)).Select(x=>new ProcurementGridRow(x.Id,"rfq",x.RfqNo,x.RfqDate,x.Status.ToString(),x.Subject,x.Suppliers.OrderBy(y=>y.Id).Select(y=>y.SupplierNameSnapshot).FirstOrDefault(),x.Lines.Count,0,"TRY",x.ResponseDueDate,x.CreatedDate)).ApplyAdvancedFilters(r).ApplySort(r,nameof(ProcurementGridRow.DocumentDate));}
    private IQueryable<ProcurementGridRow> QuoteRows(PagedRequest r){var s=r.Search?.Trim();return Quotes.Query().Where(x=>string.IsNullOrWhiteSpace(s)||x.QuoteNo.Contains(s)||x.SupplierNameSnapshot.Contains(s)).Select(x=>new ProcurementGridRow(x.Id,"quote",x.QuoteNo,x.QuoteDate,x.Status.ToString(),"Tedarikçi teklifi",x.SupplierNameSnapshot,x.Lines.Count,x.Lines.Sum(l=>l.QuotedQuantity*l.UnitPrice*(1-l.DiscountRate/100)*(1+l.VatRate/100)),x.CurrencyCode,x.ValidUntil,x.CreatedDate)).ApplyAdvancedFilters(r).ApplySort(r,nameof(ProcurementGridRow.DocumentDate));}
    private IQueryable<ProcurementGridRow> OrderRows(PagedRequest r){var s=r.Search?.Trim();return Orders.Query().Where(x=>string.IsNullOrWhiteSpace(s)||x.OrderNo.Contains(s)||x.SupplierNameSnapshot.Contains(s)).Select(x=>new ProcurementGridRow(x.Id,"order",x.OrderNo,x.OrderDate,x.Status.ToString(),"Satınalma siparişi",x.SupplierNameSnapshot,x.Lines.Count,x.Lines.Sum(l=>l.OrderedQuantity*l.UnitPrice*(1-l.DiscountRate/100)*(1+l.VatRate/100)),x.CurrencyCode,x.DeliveryDate,x.CreatedDate)).ApplyAdvancedFilters(r).ApplySort(r,nameof(ProcurementGridRow.DocumentDate));}

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
    private static string TemporaryNo(string prefix)=>$"{prefix}-TMP-{Guid.NewGuid():N}"[..Math.Min(50,prefix.Length+36)];
    private static string Number(string prefix,long id)=>$"{prefix}-{DateTime.Today:yyyy}-{id:00000000}";
    private static string? Norm(string? value)=>string.IsNullOrWhiteSpace(value)?null:value.Trim();
    private static string Currency(string? value)=>string.IsNullOrWhiteSpace(value)?"TRY":value.Trim().ToUpperInvariant();
    private sealed record ResolvedLine(long? StockId,string? StockCode,string StockName,string UnitCode,decimal Quantity,DateOnly? RequiredDate,string? ProjectCode,string? Description);
    private sealed record ResolvedOrderLine(long? StockId,string? StockCode,string StockName,string UnitCode,decimal Quantity,decimal UnitPrice,decimal DiscountRate,decimal VatRate,DateOnly? DeliveryDate,string? ProjectCode);
}
