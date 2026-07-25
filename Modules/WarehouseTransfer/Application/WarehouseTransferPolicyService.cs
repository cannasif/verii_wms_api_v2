using verii_wms_api_v2.Modules.Audit.Application;
using verii_wms_api_v2.Modules.WarehouseTransfer.Domain;
using verii_wms_api_v2.Shared.Application.Abstractions.Persistence;
using verii_wms_api_v2.Shared.Application.Exceptions;

namespace verii_wms_api_v2.Modules.WarehouseTransfer.Application;

public sealed record WarehouseTransferPolicyDto(
    long Id,string BranchCode,bool AllowOrderBasedTask,bool AllowStockBasedTask,bool AllowOrderBasedDirect,bool AllowStockBasedDirect,
    bool RequireApproval,bool RequireAssigneeForTask,bool AllowMultipleAssignees,bool AutoReleaseTaskBased,
    WarehouseTransferReservationPolicy ReservationPolicy,decimal MinimumFulfillmentPercent,
    bool AllowPartialPicking,bool AllowPartialShipment,bool AllowPartialReceipt,bool RequireDestinationAcceptance,
    bool CreateTransitInventory,bool RequirePutaway,bool RequireSourceLocation,bool RequireTargetLocation,
    bool RequireShipmentInformation,WarehouseTransferDirectPostingPolicy DirectPostingPolicy,
    WarehouseTransferDiscrepancyPolicy DiscrepancyPolicy,long? UpdatedBy,DateTime? UpdatedDate);

public sealed record UpdateWarehouseTransferPolicyRequest(
    string BranchCode,bool AllowOrderBasedTask,bool AllowStockBasedTask,bool AllowOrderBasedDirect,bool AllowStockBasedDirect,
    bool RequireApproval,bool RequireAssigneeForTask,bool AllowMultipleAssignees,bool AutoReleaseTaskBased,
    WarehouseTransferReservationPolicy ReservationPolicy,decimal MinimumFulfillmentPercent,
    bool AllowPartialPicking,bool AllowPartialShipment,bool AllowPartialReceipt,bool RequireDestinationAcceptance,
    bool CreateTransitInventory,bool RequirePutaway,bool RequireSourceLocation,bool RequireTargetLocation,
    bool RequireShipmentInformation,WarehouseTransferDirectPostingPolicy DirectPostingPolicy,
    WarehouseTransferDiscrepancyPolicy DiscrepancyPolicy);

public interface IWarehouseTransferPolicyService
{
    Task<WarehouseTransferPolicyDto> GetAsync(string branchCode,CancellationToken ct=default);
    Task<WarehouseTransferPolicyDto> UpdateAsync(UpdateWarehouseTransferPolicyRequest request,long actor,CancellationToken ct=default);
}

public sealed class WarehouseTransferPolicyService(IUnitOfWork uow,IAuditLogWriter audit):IWarehouseTransferPolicyService
{
    private IGenericRepository<WarehouseTransferPolicy> Policies=>uow.Repository<WarehouseTransferPolicy>();
    public async Task<WarehouseTransferPolicyDto> GetAsync(string branchCode,CancellationToken ct=default)
    {
        var branch=Branch(branchCode);
        var entity=await Policies.FirstOrDefaultAsync(x=>x.BranchCode==branch&&x.PolicyKey=="DEFAULT",false,ct)??Default(branch);
        return Map(entity);
    }
    public async Task<WarehouseTransferPolicyDto> UpdateAsync(UpdateWarehouseTransferPolicyRequest r,long actor,CancellationToken ct=default)
    {
        if(r.MinimumFulfillmentPercent is <0 or >100)throw AppException.BadRequest("Minimum karşılama oranı 0-100 arasında olmalıdır.");
        if(!r.AllowOrderBasedTask&&!r.AllowStockBasedTask&&!r.AllowOrderBasedDirect&&!r.AllowStockBasedDirect)
            throw AppException.BadRequest("En az bir transfer başlangıç/yürütme tipi açık olmalıdır.");
        if(r.DirectPostingPolicy==WarehouseTransferDirectPostingPolicy.OneStep&&r.RequireDestinationAcceptance)
            throw AppException.BadRequest("Tek adımlı doğrudan transferde hedef kabul zorunlu olamaz.");
        if(!r.CreateTransitInventory&&r.DirectPostingPolicy==WarehouseTransferDirectPostingPolicy.TwoStepTransit)
            throw AppException.BadRequest("İki adımlı doğrudan transfer transit stok gerektirir.");
        var branch=Branch(r.BranchCode);
        var entity=await Policies.FirstOrDefaultAsync(x=>x.BranchCode==branch&&x.PolicyKey=="DEFAULT",true,ct);
        var before=entity is null?null:Map(entity);
        if(entity is null){entity=Default(branch);entity.CreatedBy=actor;entity.CreatedDate=DateTime.UtcNow;await Policies.AddAsync(entity,ct);}
        entity.AllowOrderBasedTask=r.AllowOrderBasedTask;entity.AllowStockBasedTask=r.AllowStockBasedTask;
        entity.AllowOrderBasedDirect=r.AllowOrderBasedDirect;entity.AllowStockBasedDirect=r.AllowStockBasedDirect;
        entity.RequireApproval=r.RequireApproval;entity.RequireAssigneeForTask=r.RequireAssigneeForTask;
        entity.AllowMultipleAssignees=r.AllowMultipleAssignees;entity.AutoReleaseTaskBased=r.AutoReleaseTaskBased;
        entity.ReservationPolicy=r.ReservationPolicy;entity.MinimumFulfillmentPercent=r.MinimumFulfillmentPercent;
        entity.AllowPartialPicking=r.AllowPartialPicking;entity.AllowPartialShipment=r.AllowPartialShipment;
        entity.AllowPartialReceipt=r.AllowPartialReceipt;entity.RequireDestinationAcceptance=r.RequireDestinationAcceptance;
        entity.CreateTransitInventory=r.CreateTransitInventory;entity.RequirePutaway=r.RequirePutaway;
        entity.RequireSourceLocation=r.RequireSourceLocation;entity.RequireTargetLocation=r.RequireTargetLocation;
        entity.RequireShipmentInformation=r.RequireShipmentInformation;entity.DirectPostingPolicy=r.DirectPostingPolicy;
        entity.DiscrepancyPolicy=r.DiscrepancyPolicy;entity.UpdatedBy=actor;entity.UpdatedDate=DateTime.UtcNow;
        await uow.SaveChangesAsync(ct);var result=Map(entity);
        await audit.WriteAsync(new("warehouse-transfer.policy.update",nameof(WarehouseTransferPolicy),entity.Id.ToString(),"Succeeded","warehouse-transfer",OldValues:before,NewValues:result,ChangedFields:["Policy"]),ct);
        return result;
    }
    private static WarehouseTransferPolicy Default(string branch)=>new(){BranchCode=branch,PolicyKey="DEFAULT"};
    private static string Branch(string? value)=>string.IsNullOrWhiteSpace(value)?"0":value.Trim();
    private static WarehouseTransferPolicyDto Map(WarehouseTransferPolicy x)=>new(
        x.Id,x.BranchCode,x.AllowOrderBasedTask,x.AllowStockBasedTask,x.AllowOrderBasedDirect,x.AllowStockBasedDirect,
        x.RequireApproval,x.RequireAssigneeForTask,x.AllowMultipleAssignees,x.AutoReleaseTaskBased,x.ReservationPolicy,
        x.MinimumFulfillmentPercent,x.AllowPartialPicking,x.AllowPartialShipment,x.AllowPartialReceipt,
        x.RequireDestinationAcceptance,x.CreateTransitInventory,x.RequirePutaway,x.RequireSourceLocation,x.RequireTargetLocation,
        x.RequireShipmentInformation,x.DirectPostingPolicy,x.DiscrepancyPolicy,x.UpdatedBy,x.UpdatedDate);
}
