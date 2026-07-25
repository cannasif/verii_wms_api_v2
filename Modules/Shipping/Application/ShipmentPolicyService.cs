using verii_wms_api_v2.Modules.Audit.Application;
using verii_wms_api_v2.Modules.Shipping.Domain;
using verii_wms_api_v2.Shared.Application.Abstractions.Persistence;
using verii_wms_api_v2.Shared.Application.Exceptions;

namespace verii_wms_api_v2.Modules.Shipping.Application;

public sealed class ShipmentPolicyService(IUnitOfWork uow,IAuditLogWriter audit):IShipmentPolicyService{
 private IGenericRepository<ShipmentPolicy>Repo=>uow.Repository<ShipmentPolicy>();
 public async Task<ShipmentPolicyDto>GetAsync(string branchCode,CancellationToken ct=default){var branch=Branch(branchCode);return Map(await Repo.FirstOrDefaultAsync(x=>x.BranchCode==branch&&x.PolicyKey=="DEFAULT",false,ct)??Default(branch));}
 public async Task<ShipmentPolicyDto>UpdateAsync(UpdateShipmentPolicyRequest r,long actor,CancellationToken ct=default){
  if(!r.AllowOrderBasedTask&&!r.AllowStockBasedTask&&!r.AllowOrderBasedDirect&&!r.AllowStockBasedDirect)throw AppException.BadRequest("En az bir sevk türü açık olmalıdır.");
  if(r.MinimumFulfillmentPercent is <0 or >100||r.OverPickTolerancePercent is <0 or >100)throw AppException.BadRequest("Yüzdesel değerler 0-100 arasında olmalıdır.");
  if(r.OverPickPolicy==ShipmentOverPickPolicy.AllowWithinTolerance&&r.OverPickTolerancePercent<=0)throw AppException.BadRequest("Fazla toplama toleransı sıfırdan büyük olmalıdır.");
  if(r.AutoPostErpAfterApproval&&!r.RequireApproval)throw AppException.BadRequest("Onay sonrası ERP aktarımı için sevk onayı zorunlu olmalıdır.");
  var branch=Branch(r.BranchCode);var e=await Repo.FirstOrDefaultAsync(x=>x.BranchCode==branch&&x.PolicyKey=="DEFAULT",true,ct);var before=e is null?null:Map(e);
  if(e is null){e=Default(branch);e.CreatedBy=actor;await Repo.AddAsync(e,ct);}
  e.AllowOrderBasedTask=r.AllowOrderBasedTask;e.AllowStockBasedTask=r.AllowStockBasedTask;e.AllowOrderBasedDirect=r.AllowOrderBasedDirect;e.AllowStockBasedDirect=r.AllowStockBasedDirect;e.RequireApproval=r.RequireApproval;e.RequireAssigneeForTask=r.RequireAssigneeForTask;e.AllowMultipleAssignees=r.AllowMultipleAssignees;e.AutoReleaseTaskBased=r.AutoReleaseTaskBased;e.AllowPartialPicking=r.AllowPartialPicking;e.AllowPartialShipment=r.AllowPartialShipment;e.RequireSourceLocation=r.RequireSourceLocation;e.RequireShipmentInformation=r.RequireShipmentInformation;e.RequireLoadingConfirmation=r.RequireLoadingConfirmation;e.AutoPostErpAfterApproval=r.AutoPostErpAfterApproval;e.MinimumFulfillmentPercent=r.MinimumFulfillmentPercent;e.OverPickTolerancePercent=r.OverPickTolerancePercent;e.ReservationPolicy=r.ReservationPolicy;e.PackingPolicy=r.PackingPolicy;e.ShortagePolicy=r.ShortagePolicy;e.OverPickPolicy=r.OverPickPolicy;e.UpdatedBy=actor;e.UpdatedDate=DateTime.UtcNow;
  await uow.SaveChangesAsync(ct);var result=Map(e);await audit.WriteAsync(new("shipping.policy.update",nameof(ShipmentPolicy),e.Id.ToString(),"Succeeded","shipping",OldValues:before,NewValues:result,ChangedFields:["Policy"]),ct);return result;
 }
 private static string Branch(string?x)=>string.IsNullOrWhiteSpace(x)?"0":x.Trim();private static ShipmentPolicy Default(string b)=>new(){BranchCode=b};
 private static ShipmentPolicyDto Map(ShipmentPolicy x)=>new(x.Id,x.BranchCode,x.AllowOrderBasedTask,x.AllowStockBasedTask,x.AllowOrderBasedDirect,x.AllowStockBasedDirect,x.RequireApproval,x.RequireAssigneeForTask,x.AllowMultipleAssignees,x.AutoReleaseTaskBased,x.AllowPartialPicking,x.AllowPartialShipment,x.RequireSourceLocation,x.RequireShipmentInformation,x.RequireLoadingConfirmation,x.AutoPostErpAfterApproval,x.MinimumFulfillmentPercent,x.OverPickTolerancePercent,x.ReservationPolicy,x.PackingPolicy,x.ShortagePolicy,x.OverPickPolicy,x.UpdatedBy,x.UpdatedDate);
}
