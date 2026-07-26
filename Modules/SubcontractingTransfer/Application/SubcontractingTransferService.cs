using System.Data;
using Microsoft.EntityFrameworkCore;
using verii_wms_api_v2.Modules.Audit.Application;
using verii_wms_api_v2.Modules.SubcontractingTransfer.Domain;
using verii_wms_api_v2.Modules.WarehouseOperations.Domain;
using verii_wms_api_v2.Modules.WarehouseTransfer.Application;
using verii_wms_api_v2.Modules.WarehouseTransfer.Domain;
using verii_wms_api_v2.Shared;
using verii_wms_api_v2.Shared.Application.Abstractions.Persistence;
using verii_wms_api_v2.Shared.Application.Exceptions;
using CustomerEntity=verii_wms_api_v2.Modules.Customer.Domain.Customer;

namespace verii_wms_api_v2.Modules.SubcontractingTransfer.Application;

public sealed class SubcontractingTransferService(
    IUnitOfWork uow,
    IWarehouseTransferService transfers,
    IAuditLogWriter audit) : ISubcontractingTransferService
{
    private static readonly WarehouseTransferBusinessContext[] Contexts=[
        WarehouseTransferBusinessContext.SubcontractingIssue,
        WarehouseTransferBusinessContext.SubcontractingReceipt,
        WarehouseTransferBusinessContext.SubcontractorToSubcontractor];
    private IGenericRepository<SubcontractingTransferHeaderLink> Links=>uow.Repository<SubcontractingTransferHeaderLink>();
    private IGenericRepository<SubcontractingTransferPolicy> Policies=>uow.Repository<SubcontractingTransferPolicy>();

    public Task<CreateWarehouseTransferDraftResult>CreateDraftAsync(
        CreateSubcontractingTransferDraftRequest request,long actor,CancellationToken ct=default)
    {
        Validate(request);
        return uow.ExecuteInTransactionAsync(async token=>{
            var existing=await uow.Repository<WarehouseTransferHeader>().Query()
                .Where(x=>x.CorrelationId==request.Transfer.IdempotencyKey)
                .Select(x=>new{x.Id,x.BusinessContext}).SingleOrDefaultAsync(token);
            if(existing is not null){
                if(!Contexts.Contains(existing.BusinessContext))throw AppException.Conflict("Aynı idempotency anahtarı başka bir transfer bağlamında kullanılmış.");
                var replay=await transfers.CreateDraftAsync(request.Transfer with{BusinessContext=existing.BusinessContext},actor,token);
                if(!await Links.AnyAsync(x=>x.WarehouseTransferHeaderId==existing.Id,token))
                    throw AppException.Conflict("Fason transfer önceki istekte eksik bağlamla oluşmuş; teknik inceleme gereklidir.");
                return replay;
            }
            var policy=await GetPolicyEntityAsync(request.Transfer.BranchCode,token);
            await ValidatePolicyAsync(request,policy,token);
            var supplier=await uow.Repository<CustomerEntity>().Query()
                .SingleOrDefaultAsync(x=>x.Id==request.SupplierId&&x.BranchCode==Branch(request.Transfer.BranchCode),token)
                ??throw AppException.BadRequest("Seçilen fason tedarikçi ERP mirror tablosunda bulunamadı.");
            var context=Context(request.Direction);
            var result=await transfers.CreateDraftAsync(request.Transfer with{BusinessContext=context},actor,token);
            var header=await uow.Repository<WarehouseTransferHeader>().Query(true)
                .Include(x=>x.Lines).SingleAsync(x=>x.Id==result.Id,token);
            header.RequireApproval|=policy.RequireApproval;
            if(header.RequireApproval&&header.ApprovalStatus==OperationApprovalStatus.NotRequired)
                header.ApprovalStatus=OperationApprovalStatus.Pending;
            var now=DateTime.UtcNow;
            var link=new SubcontractingTransferHeaderLink{
                BranchCode=header.BranchCode,CreatedBy=actor,CreatedDate=now,WarehouseTransferHeader=header,
                Direction=request.Direction,SupplierId=supplier.Id,SupplierCodeSnapshot=supplier.CustomerCode,
                SupplierNameSnapshot=supplier.CustomerName,SubcontractOrderNo=Clean(request.SubcontractOrderNo,100),
                SubcontractOrderDate=request.SubcontractOrderDate,ParentIssueTransferId=request.ParentIssueTransferId,
                ExpectedReturnAtUtc=request.ExpectedReturnAtUtc?.ToUniversalTime(),OwnershipType=request.OwnershipType,
                QualityInspectionRequired=request.Direction==SubcontractingTransferDirection.ReceiptFromSupplier
                    &&(request.QualityInspectionRequired||policy.RequireQualityOnReceipt),
                ComponentsIssuedConfirmed=!policy.RequireIssueBeforeReceipt||request.Direction!=SubcontractingTransferDirection.ReceiptFromSupplier
                    ||request.ParentIssueTransferId.HasValue,
                OperationCode=Clean(request.OperationCode,100),SupplierDispatchNo=Clean(request.SupplierDispatchNo,100)
            };
            var contexts=(request.LineContexts??[]).ToDictionary(x=>x.LineIndex);
            foreach(var line in header.Lines.OrderBy(x=>x.LineNo)){
                contexts.TryGetValue(line.LineNo-1,out var lineContext);
                link.Lines.Add(new SubcontractingTransferLineLink{
                    BranchCode=header.BranchCode,CreatedBy=actor,CreatedDate=now,WarehouseTransferLine=line,
                    LineRole=lineContext?.LineRole??DefaultRole(request.Direction),SourceIssueLineId=lineContext?.SourceIssueLineId,
                    ExpectedQuantity=lineContext?.ExpectedQuantity??line.RequestedQuantity,ScrapQuantity=lineContext?.ScrapQuantity??0,
                    RequirementReference=Clean(lineContext?.RequirementReference,150)});
            }
            await Links.AddAsync(link,token);await uow.SaveChangesAsync(token);
            await audit.WriteAsync(new("subcontracting-transfer.draft.create",nameof(SubcontractingTransferHeaderLink),link.Id.ToString(),
                "Succeeded","subcontracting-transfer",NewValues:new{result.Id,result.DocumentNo,request.Direction,supplier.CustomerCode,request.SubcontractOrderNo},
                ChangedFields:["Transfer","SupplierContext","LineLinks"]),token);
            return result;
        },ct,IsolationLevel.Serializable);
    }

    public Task<PagedResponse<WarehouseTransferGridRow>>GetPagedAsync(PagedRequest request,CancellationToken ct=default)=>
        transfers.GetPagedByContextAsync(request,Contexts,ct);

    public Task<PagedResponse<WarehouseTransferGridRow>>GetPagedAsync(
        PagedRequest request,
        SubcontractingTransferDirection direction,
        CancellationToken ct=default)=>
        transfers.GetPagedByContextAsync(request,[Context(direction)],ct);

    public async Task<SubcontractingTransferDetail>GetDetailAsync(long id,CancellationToken ct=default)
    {
        var transfer=await transfers.GetDetailForContextAsync(id,Contexts,ct);
        var context=await Links.Query().Where(x=>x.WarehouseTransferHeaderId==id)
            .Select(x=>new SubcontractingTransferContextDto(x.Id,x.Direction,x.SupplierId,x.SupplierCodeSnapshot,
                x.SupplierNameSnapshot,x.SubcontractOrderNo,x.SubcontractOrderDate,x.ParentIssueTransferId,
                x.ExpectedReturnAtUtc,x.OwnershipType,x.QualityInspectionRequired,x.ComponentsIssuedConfirmed,
                x.OperationCode,x.SupplierDispatchNo)).SingleOrDefaultAsync(ct)
            ??throw AppException.NotFound("Fason transfer bağlamı bulunamadı.");
        return new(transfer,context);
    }

    public async Task<SubcontractingTransferDetail>UpdateDraftAsync(
        long id,UpdateWarehouseTransferDraftRequest request,long actor,CancellationToken ct=default)
    {
        await transfers.EnsureContextAsync(id,Contexts,ct);
        await transfers.UpdateDraftAsync(id,request,actor,ct);
        return await GetDetailAsync(id,ct);
    }

    public Task DeleteDraftAsync(long id,long actor,CancellationToken ct=default)=>
        uow.ExecuteInTransactionAsync(async token=>{
            await transfers.EnsureContextAsync(id,Contexts,token);
            var link=await Links.Query(true).SingleOrDefaultAsync(x=>x.WarehouseTransferHeaderId==id,token)
                ??throw AppException.NotFound("Fason transfer bağlamı bulunamadı.");
            var now=DateTime.UtcNow;
            await uow.Repository<SubcontractingTransferLineLink>().Query(true).Where(x=>x.SubcontractingTransferHeaderLinkId==link.Id)
                .ExecuteUpdateAsync(x=>x.SetProperty(v=>v.IsDeleted,true).SetProperty(v=>v.DeletedBy,actor).SetProperty(v=>v.DeletedDate,now),token);
            link.IsDeleted=true;link.DeletedBy=actor;link.DeletedDate=now;
            await uow.SaveChangesAsync(token);
            await transfers.DeleteDraftAsync(id,actor,token);
            return true;
        },ct);

    public async Task<SubcontractingTransferPolicyDto>GetPolicyAsync(string branchCode,CancellationToken ct=default)=>
        Map(await GetPolicyEntityAsync(branchCode,ct));

    public async Task<SubcontractingTransferPolicyDto>UpdatePolicyAsync(
        UpdateSubcontractingTransferPolicyRequest request,long actor,CancellationToken ct=default)
    {
        if(request.OverReceiptTolerancePercent is <0 or >100)throw AppException.BadRequest("Fazla dönüş toleransı 0-100 arasında olmalıdır.");
        if(!request.AllowOverReceipt&&request.OverReceiptTolerancePercent!=0)throw AppException.BadRequest("Fazla dönüş kapalıyken tolerans sıfır olmalıdır.");
        if(request.DefaultLeadTimeDays is <0 or >3650)throw AppException.BadRequest("Varsayılan fason termin günü 0-3650 arasında olmalıdır.");
        var branch=Branch(request.BranchCode);
        var entity=await Policies.FirstOrDefaultAsync(x=>x.BranchCode==branch&&x.PolicyKey=="DEFAULT",true,ct);
        var before=entity is null?null:Map(entity);
        if(entity is null){entity=Default(branch);entity.CreatedBy=actor;await Policies.AddAsync(entity,ct);}
        else EnsureRowVersion(entity.RowVersion,request.RowVersion);
        entity.RequireSupplier=request.RequireSupplier;entity.RequireSubcontractOrderForReceipt=request.RequireSubcontractOrderForReceipt;
        entity.RequireIssueBeforeReceipt=request.RequireIssueBeforeReceipt;entity.AllowOrderlessIssue=request.AllowOrderlessIssue;
        entity.AllowOrderlessReceipt=request.AllowOrderlessReceipt;entity.AllowSupplierToSupplier=request.AllowSupplierToSupplier;
        entity.AllowPartialIssue=request.AllowPartialIssue;entity.AllowPartialReceipt=request.AllowPartialReceipt;
        entity.RequireQualityOnReceipt=request.RequireQualityOnReceipt;entity.RequireTaskAssignment=request.RequireTaskAssignment;
        entity.RequireApproval=request.RequireApproval;entity.AllowOverReceipt=request.AllowOverReceipt;
        entity.OverReceiptTolerancePercent=request.OverReceiptTolerancePercent;entity.DefaultLeadTimeDays=request.DefaultLeadTimeDays;
        entity.UpdatedBy=actor;entity.UpdatedDate=DateTime.UtcNow;
        try{await uow.SaveChangesAsync(ct);}
        catch(DbUpdateConcurrencyException){throw AppException.Conflict("Fason transfer politikası başka bir kullanıcı tarafından değiştirildi. Sayfayı yenileyin.");}
        var result=Map(entity);
        await audit.WriteAsync(new("subcontracting-transfer.policy.update",nameof(SubcontractingTransferPolicy),entity.Id.ToString(),
            "Succeeded","subcontracting-transfer",OldValues:before,NewValues:result,ChangedFields:["Policy"]),ct);
        return result;
    }

    private async Task ValidatePolicyAsync(
        CreateSubcontractingTransferDraftRequest request,SubcontractingTransferPolicy policy,CancellationToken ct)
    {
        if(policy.RequireSupplier&&request.SupplierId<=0)throw AppException.BadRequest("Fason tedarikçi zorunludur.");
        if(request.Direction==SubcontractingTransferDirection.SupplierToSupplier&&!policy.AllowSupplierToSupplier)
            throw AppException.BadRequest("Tedarikçiden tedarikçiye fason transfer politikada kapalıdır.");
        var orderless=string.IsNullOrWhiteSpace(request.SubcontractOrderNo);
        if(request.Direction==SubcontractingTransferDirection.IssueToSupplier&&orderless&&!policy.AllowOrderlessIssue)
            throw AppException.BadRequest("Siparişsiz fasona çıkış politikada kapalıdır.");
        if(request.Direction==SubcontractingTransferDirection.ReceiptFromSupplier&&orderless
           &&(policy.RequireSubcontractOrderForReceipt||!policy.AllowOrderlessReceipt))
            throw AppException.BadRequest("Fasondan dönüşte fason sipariş referansı zorunludur.");
        if(request.Direction==SubcontractingTransferDirection.ReceiptFromSupplier&&policy.RequireIssueBeforeReceipt)
        {
            if(!request.ParentIssueTransferId.HasValue)throw AppException.BadRequest("Fasondan dönüş için kaynak fasona çıkış transferi zorunludur.");
            var issue=await Links.Query().Include(x=>x.WarehouseTransferHeader)
                .SingleOrDefaultAsync(x=>x.WarehouseTransferHeaderId==request.ParentIssueTransferId
                    &&x.SupplierId==request.SupplierId&&x.Direction==SubcontractingTransferDirection.IssueToSupplier,ct)
                ??throw AppException.BadRequest("Tedarikçiyle eşleşen fasona çıkış transferi bulunamadı.");
            if(issue.WarehouseTransferHeader.Status is not (WarehouseTransferStatus.Shipped or WarehouseTransferStatus.Received
                or WarehouseTransferStatus.PartiallyReceived or WarehouseTransferStatus.PartiallyPutaway or WarehouseTransferStatus.Completed))
                throw AppException.Conflict("Kaynak fasona çıkış transferi henüz tedarikçiye sevk edilmedi.");
        }
        var lineContexts=request.LineContexts??[];
        foreach(var context in lineContexts)
        {
            var transferQuantity=request.Transfer.Lines[context.LineIndex].Quantity;
            var partialAllowed=request.Direction==SubcontractingTransferDirection.ReceiptFromSupplier
                ? policy.AllowPartialReceipt : policy.AllowPartialIssue;
            if(!partialAllowed&&transferQuantity<context.ExpectedQuantity)
                throw AppException.BadRequest("Kısmi fason transferi seçilen işlem yönü için politikada kapalıdır.");
            if(request.Direction==SubcontractingTransferDirection.ReceiptFromSupplier&&transferQuantity>context.ExpectedQuantity)
            {
                if(!policy.AllowOverReceipt)
                    throw AppException.BadRequest("Beklenen miktardan fazla fason dönüşü politikada kapalıdır.");
                var limit=context.ExpectedQuantity*(1+policy.OverReceiptTolerancePercent/100m);
                if(transferQuantity>limit)
                    throw AppException.BadRequest("Fason dönüş miktarı fazla kabul toleransını aşıyor.");
            }
        }
        var sourceLineIds=lineContexts.Where(x=>x.SourceIssueLineId.HasValue).Select(x=>x.SourceIssueLineId!.Value).Distinct().ToArray();
        if(sourceLineIds.Length>0)
        {
            if(!request.ParentIssueTransferId.HasValue)
                throw AppException.BadRequest("Kaynak fason çıkış kalemi seçildiğinde kaynak fason transferi zorunludur.");
            var sourceLines=await uow.Repository<WarehouseTransferLine>().Query()
                .Where(x=>sourceLineIds.Contains(x.Id)).ToDictionaryAsync(x=>x.Id,ct);
            if(sourceLines.Count!=sourceLineIds.Length||sourceLines.Values.Any(x=>x.WtHeaderId!=request.ParentIssueTransferId.Value))
                throw AppException.BadRequest("Kaynak fason çıkış kalemlerinden biri seçilen çıkış transferine ait değil.");
            foreach(var context in lineContexts.Where(x=>x.SourceIssueLineId.HasValue
                         && x.LineRole is SubcontractingLineRole.ReturnMaterial or SubcontractingLineRole.RejectedMaterial))
                if(sourceLines[context.SourceIssueLineId!.Value].StockId!=request.Transfer.Lines[context.LineIndex].StockId)
                    throw AppException.BadRequest("İade veya red kalemi kaynak fason çıkışındaki aynı stokla eşleşmelidir.");
        }
        var taskBased=request.Transfer.InitiationMode is WarehouseTransferInitiationMode.OrderBasedTask or WarehouseTransferInitiationMode.StockBasedTask;
        if(policy.RequireTaskAssignment&&(!taskBased||(request.Transfer.AssignedUserIds?.Count??0)==0))
            throw AppException.BadRequest("Fason transferde emirli yürütme ve kullanıcı ataması zorunludur.");
    }

    private static void Validate(CreateSubcontractingTransferDraftRequest request)
    {
        if(request.Transfer is null)throw AppException.BadRequest("Transfer gövdesi zorunludur.");
        if(request.SupplierId<=0)throw AppException.BadRequest("Fason tedarikçi zorunludur.");
        if(request.ExpectedReturnAtUtc.HasValue&&request.ExpectedReturnAtUtc<DateTimeOffset.UtcNow.AddYears(-1))
            throw AppException.BadRequest("Beklenen dönüş tarihi geçersiz.");
        if(request.LineContexts?.Any(x=>x.LineIndex<0||x.LineIndex>=request.Transfer.Lines.Count||x.ExpectedQuantity<=0
               ||x.ScrapQuantity<0||x.ScrapQuantity>x.ExpectedQuantity)==true)
            throw AppException.BadRequest("Fason transfer kalem bağlamı geçersiz.");
        if(request.LineContexts?.GroupBy(x=>x.LineIndex).Any(x=>x.Count()>1)==true)
            throw AppException.BadRequest("Aynı transfer kalemine birden fazla fason bağlamı atanamaz.");
    }

    private async Task<SubcontractingTransferPolicy>GetPolicyEntityAsync(string branchCode,CancellationToken ct)
    {
        var branch=Branch(branchCode);
        return await Policies.FirstOrDefaultAsync(x=>x.BranchCode==branch&&x.PolicyKey=="DEFAULT",false,ct)??Default(branch);
    }
    private static WarehouseTransferBusinessContext Context(SubcontractingTransferDirection direction)=>direction switch{
        SubcontractingTransferDirection.IssueToSupplier=>WarehouseTransferBusinessContext.SubcontractingIssue,
        SubcontractingTransferDirection.ReceiptFromSupplier=>WarehouseTransferBusinessContext.SubcontractingReceipt,
        SubcontractingTransferDirection.SupplierToSupplier=>WarehouseTransferBusinessContext.SubcontractorToSubcontractor,
        _=>throw AppException.BadRequest("Fason transfer yönü geçersiz.")};
    private static SubcontractingLineRole DefaultRole(SubcontractingTransferDirection direction)=>
        direction==SubcontractingTransferDirection.ReceiptFromSupplier?SubcontractingLineRole.FinishedProduct:SubcontractingLineRole.Component;
    private static SubcontractingTransferPolicy Default(string branch)=>new(){BranchCode=branch,PolicyKey="DEFAULT",CreatedDate=DateTime.UtcNow};
    private static SubcontractingTransferPolicyDto Map(SubcontractingTransferPolicy x)=>new(x.Id,x.BranchCode,Convert.ToBase64String(x.RowVersion),x.RequireSupplier,
        x.RequireSubcontractOrderForReceipt,x.RequireIssueBeforeReceipt,x.AllowOrderlessIssue,x.AllowOrderlessReceipt,
        x.AllowSupplierToSupplier,x.AllowPartialIssue,x.AllowPartialReceipt,x.RequireQualityOnReceipt,x.RequireTaskAssignment,
        x.RequireApproval,x.AllowOverReceipt,x.OverReceiptTolerancePercent,x.DefaultLeadTimeDays,x.UpdatedBy,x.UpdatedDate);
    private static void EnsureRowVersion(byte[] current,string? supplied){
        byte[] expected;try{expected=Convert.FromBase64String(supplied??string.Empty);}
        catch(FormatException){throw AppException.BadRequest("Geçersiz eşzamanlılık anahtarı.");}
        if(!System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(current,expected))
            throw AppException.Conflict("Fason transfer politikası başka bir kullanıcı tarafından değiştirildi. Sayfayı yenileyin.");
    }
    private static string Branch(string? value)=>string.IsNullOrWhiteSpace(value)?"0":value.Trim();
    private static string? Clean(string? value,int max){var result=value?.Trim();return string.IsNullOrEmpty(result)?null:result.Length<=max?result:result[..max];}
}
