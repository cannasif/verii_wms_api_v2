using verii_wms_api_v2.Modules.SubcontractingTransfer.Domain;
using verii_wms_api_v2.Modules.WarehouseTransfer.Application;
using verii_wms_api_v2.Shared;

namespace verii_wms_api_v2.Modules.SubcontractingTransfer.Application;

public sealed record SubcontractingLineContextRequest(
    int LineIndex,SubcontractingLineRole LineRole,long? SourceIssueLineId,decimal ExpectedQuantity,
    decimal ScrapQuantity,string? RequirementReference);

public sealed record CreateSubcontractingTransferDraftRequest(
    CreateWarehouseTransferDraftRequest Transfer,
    SubcontractingTransferDirection Direction,
    long SupplierId,
    string? SubcontractOrderNo,
    DateOnly? SubcontractOrderDate,
    long? ParentIssueTransferId,
    DateTimeOffset? ExpectedReturnAtUtc,
    SubcontractingOwnershipType OwnershipType,
    bool QualityInspectionRequired,
    string? OperationCode,
    string? SupplierDispatchNo,
    IReadOnlyList<SubcontractingLineContextRequest>? LineContexts);

public sealed record SubcontractingTransferContextDto(
    long LinkId,SubcontractingTransferDirection Direction,long SupplierId,string SupplierCode,string SupplierName,
    string? SubcontractOrderNo,DateOnly? SubcontractOrderDate,long? ParentIssueTransferId,DateTimeOffset? ExpectedReturnAtUtc,
    SubcontractingOwnershipType OwnershipType,bool QualityInspectionRequired,bool ComponentsIssuedConfirmed,
    string? OperationCode,string? SupplierDispatchNo);
public sealed record SubcontractingTransferDetail(WarehouseTransferDetail Transfer,SubcontractingTransferContextDto Context);

public sealed record SubcontractingTransferPolicyDto(
    long Id,string BranchCode,string RowVersion,bool RequireSupplier,bool RequireSubcontractOrderForReceipt,bool RequireIssueBeforeReceipt,
    bool AllowOrderlessIssue,bool AllowOrderlessReceipt,bool AllowSupplierToSupplier,bool AllowPartialIssue,
    bool AllowPartialReceipt,bool RequireQualityOnReceipt,bool RequireTaskAssignment,bool RequireApproval,
    bool AllowOverReceipt,decimal OverReceiptTolerancePercent,int DefaultLeadTimeDays,long? UpdatedBy,DateTime? UpdatedDate);
public sealed record UpdateSubcontractingTransferPolicyRequest(
    string BranchCode,string? RowVersion,bool RequireSupplier,bool RequireSubcontractOrderForReceipt,bool RequireIssueBeforeReceipt,
    bool AllowOrderlessIssue,bool AllowOrderlessReceipt,bool AllowSupplierToSupplier,bool AllowPartialIssue,
    bool AllowPartialReceipt,bool RequireQualityOnReceipt,bool RequireTaskAssignment,bool RequireApproval,
    bool AllowOverReceipt,decimal OverReceiptTolerancePercent,int DefaultLeadTimeDays);

public interface ISubcontractingTransferService
{
    Task<CreateWarehouseTransferDraftResult>CreateDraftAsync(CreateSubcontractingTransferDraftRequest request,long actor,CancellationToken ct=default);
    Task<PagedResponse<WarehouseTransferGridRow>>GetPagedAsync(PagedRequest request,CancellationToken ct=default);
    Task<SubcontractingTransferDetail>GetDetailAsync(long id,CancellationToken ct=default);
    Task<SubcontractingTransferDetail>UpdateDraftAsync(long id,UpdateWarehouseTransferDraftRequest request,long actor,CancellationToken ct=default);
    Task DeleteDraftAsync(long id,long actor,CancellationToken ct=default);
    Task<SubcontractingTransferPolicyDto>GetPolicyAsync(string branchCode,CancellationToken ct=default);
    Task<SubcontractingTransferPolicyDto>UpdatePolicyAsync(UpdateSubcontractingTransferPolicyRequest request,long actor,CancellationToken ct=default);
}
