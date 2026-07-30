using verii_wms_api_v2.Modules.Audit.Application;
using verii_wms_api_v2.Modules.GoodsReceipt.Domain;
using verii_wms_api_v2.Shared.Application.Abstractions.Persistence;
using verii_wms_api_v2.Shared.Application.Exceptions;

namespace verii_wms_api_v2.Modules.GoodsReceipt.Application;

public sealed record GoodsReceiptPolicyDto(long Id,string BranchCode,OverReceiptPolicy OverReceiptPolicy,decimal OverReceiptTolerancePercent,
    bool AllowUnderReceipt,bool RequireShortCloseApproval,bool RequireReceiptApproval,bool RequireQualityApproval,bool RequireErpApproval,
    bool HoldInventoryUntilQualityDecision,bool BlockPutawayUntilQualityDecision,InventoryAvailabilityPolicy InventoryAvailabilityPolicy,
    GoodsReceiptErpPostingPolicy ErpPostingPolicy,bool AllowOrderlessReceipt,bool AllowUnplannedReceipt,bool ShowAllocatedOpenOrderLines,
    long? UpdatedBy,DateTime? UpdatedDate);

public sealed record UpdateGoodsReceiptPolicyRequest(string BranchCode,OverReceiptPolicy OverReceiptPolicy,decimal OverReceiptTolerancePercent,
    bool AllowUnderReceipt,bool RequireShortCloseApproval,bool RequireReceiptApproval,bool RequireQualityApproval,bool RequireErpApproval,
    bool HoldInventoryUntilQualityDecision,bool BlockPutawayUntilQualityDecision,InventoryAvailabilityPolicy InventoryAvailabilityPolicy,
    GoodsReceiptErpPostingPolicy ErpPostingPolicy,bool AllowOrderlessReceipt,bool AllowUnplannedReceipt,bool ShowAllocatedOpenOrderLines);

public interface IGoodsReceiptPolicyService
{
    Task<GoodsReceiptPolicyDto> GetAsync(string branchCode,CancellationToken ct=default);
    Task<GoodsReceiptPolicyDto> UpdateAsync(UpdateGoodsReceiptPolicyRequest request,long actor,CancellationToken ct=default);
}

public sealed class GoodsReceiptPolicyService(IUnitOfWork uow,IAuditLogWriter audit):IGoodsReceiptPolicyService
{
    private IGenericRepository<GoodsReceiptPolicy> Policies=>uow.Repository<GoodsReceiptPolicy>();
    public async Task<GoodsReceiptPolicyDto> GetAsync(string branchCode,CancellationToken ct=default){var branch=Branch(branchCode);var entity=await Policies.FirstOrDefaultAsync(x=>x.BranchCode==branch&&x.PolicyKey=="DEFAULT",false,ct)??Default(branch);return Map(entity);}
    public async Task<GoodsReceiptPolicyDto> UpdateAsync(UpdateGoodsReceiptPolicyRequest r,long actor,CancellationToken ct=default)
    {
        if(r.OverReceiptTolerancePercent is <0 or >100) throw AppException.BadRequest("Fazla kabul toleransı 0-100 arasında olmalıdır.");
        if(r.OverReceiptPolicy==OverReceiptPolicy.NotAllowed&&r.OverReceiptTolerancePercent!=0) throw AppException.BadRequest("Fazla kabul kapalıyken tolerans sıfır olmalıdır.");
        var branch=Branch(r.BranchCode);var entity=await Policies.FirstOrDefaultAsync(x=>x.BranchCode==branch&&x.PolicyKey=="DEFAULT",true,ct);var before=entity is null?null:Map(entity);
        if(entity is null){entity=Default(branch);entity.CreatedBy=actor;entity.CreatedDate=DateTime.UtcNow;await Policies.AddAsync(entity,ct);}
        entity.OverReceiptPolicy=r.OverReceiptPolicy;entity.OverReceiptTolerancePercent=r.OverReceiptTolerancePercent;entity.AllowUnderReceipt=r.AllowUnderReceipt;entity.RequireShortCloseApproval=r.RequireShortCloseApproval;
        entity.RequireReceiptApproval=r.RequireReceiptApproval;entity.RequireQualityApproval=r.RequireQualityApproval;entity.RequireErpApproval=r.RequireErpApproval;entity.HoldInventoryUntilQualityDecision=r.HoldInventoryUntilQualityDecision;
        entity.BlockPutawayUntilQualityDecision=r.BlockPutawayUntilQualityDecision;entity.InventoryAvailabilityPolicy=r.InventoryAvailabilityPolicy;entity.ErpPostingPolicy=r.ErpPostingPolicy;entity.AllowOrderlessReceipt=r.AllowOrderlessReceipt;entity.AllowUnplannedReceipt=r.AllowUnplannedReceipt;
        entity.ShowAllocatedOpenOrderLines=r.ShowAllocatedOpenOrderLines;
        entity.LocationSelectionPolicy=GoodsReceiptLocationPolicy.ResolveSelectionPolicy(r.BlockPutawayUntilQualityDecision);
        entity.UpdatedBy=actor;entity.UpdatedDate=DateTime.UtcNow;await uow.SaveChangesAsync(ct);var result=Map(entity);await audit.WriteAsync(new("goods-receipt.policy.update",nameof(GoodsReceiptPolicy),entity.Id.ToString(),"Succeeded","goods-receipt",OldValues:before,NewValues:result,ChangedFields:["Policy"]),ct);return result;
    }
    private static GoodsReceiptPolicy Default(string branch)=>new(){BranchCode=branch,PolicyKey="DEFAULT"}; private static string Branch(string? x)=>string.IsNullOrWhiteSpace(x)?"0":x.Trim();
    private static GoodsReceiptPolicyDto Map(GoodsReceiptPolicy x)=>new(x.Id,x.BranchCode,x.OverReceiptPolicy,x.OverReceiptTolerancePercent,x.AllowUnderReceipt,x.RequireShortCloseApproval,x.RequireReceiptApproval,x.RequireQualityApproval,x.RequireErpApproval,x.HoldInventoryUntilQualityDecision,x.BlockPutawayUntilQualityDecision,x.InventoryAvailabilityPolicy,x.ErpPostingPolicy,x.AllowOrderlessReceipt,x.AllowUnplannedReceipt,x.ShowAllocatedOpenOrderLines,x.UpdatedBy,x.UpdatedDate);
}
