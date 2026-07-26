using verii_wms_api_v2.Modules.WarehouseTransfer.Domain;
using verii_wms_api_v2.Shared.Domain;
using CustomerEntity = verii_wms_api_v2.Modules.Customer.Domain.Customer;

namespace verii_wms_api_v2.Modules.SubcontractingTransfer.Domain;

public enum SubcontractingTransferDirection
{
    IssueToSupplier = 1,
    ReceiptFromSupplier = 2,
    SupplierToSupplier = 3
}

public enum SubcontractingLineRole
{
    Component = 1,
    WorkInProgress = 2,
    FinishedProduct = 3,
    ReturnMaterial = 4,
    RejectedMaterial = 5
}

public enum SubcontractingOwnershipType
{
    CompanyOwned = 1,
    SupplierOwned = 2,
    CustomerOwned = 3
}

public sealed class SubcontractingTransferHeaderLink : BaseEntity
{
    public long WarehouseTransferHeaderId { get; set; }
    public WarehouseTransferHeader WarehouseTransferHeader { get; set; } = null!;
    public SubcontractingTransferDirection Direction { get; set; }
    public long SupplierId { get; set; }
    public CustomerEntity Supplier { get; set; } = null!;
    public string SupplierCodeSnapshot { get; set; } = string.Empty;
    public string SupplierNameSnapshot { get; set; } = string.Empty;
    public string? SubcontractOrderNo { get; set; }
    public DateOnly? SubcontractOrderDate { get; set; }
    public long? ParentIssueTransferId { get; set; }
    public WarehouseTransferHeader? ParentIssueTransfer { get; set; }
    public DateTimeOffset? ExpectedReturnAtUtc { get; set; }
    public SubcontractingOwnershipType OwnershipType { get; set; } = SubcontractingOwnershipType.CompanyOwned;
    public bool QualityInspectionRequired { get; set; }
    public bool ComponentsIssuedConfirmed { get; set; }
    public string? OperationCode { get; set; }
    public string? SupplierDispatchNo { get; set; }
    public byte[] RowVersion { get; set; } = [];
    public ICollection<SubcontractingTransferLineLink> Lines { get; set; } = [];
}

public sealed class SubcontractingTransferLineLink : BaseEntity
{
    public long SubcontractingTransferHeaderLinkId { get; set; }
    public SubcontractingTransferHeaderLink HeaderLink { get; set; } = null!;
    public long WarehouseTransferLineId { get; set; }
    public WarehouseTransferLine WarehouseTransferLine { get; set; } = null!;
    public SubcontractingLineRole LineRole { get; set; }
    public long? SourceIssueLineId { get; set; }
    public WarehouseTransferLine? SourceIssueLine { get; set; }
    public decimal ExpectedQuantity { get; set; }
    public decimal ScrapQuantity { get; set; }
    public string? RequirementReference { get; set; }
}

public sealed class SubcontractingTransferPolicy : BaseEntity
{
    public string PolicyKey { get; set; } = "DEFAULT";
    public bool RequireSupplier { get; set; } = true;
    public bool RequireSubcontractOrderForReceipt { get; set; } = true;
    public bool RequireIssueBeforeReceipt { get; set; } = true;
    public bool AllowOrderlessIssue { get; set; } = true;
    public bool AllowOrderlessReceipt { get; set; }
    public bool AllowSupplierToSupplier { get; set; }
    public bool AllowPartialIssue { get; set; } = true;
    public bool AllowPartialReceipt { get; set; } = true;
    public bool RequireQualityOnReceipt { get; set; } = true;
    public bool RequireTaskAssignment { get; set; } = true;
    public bool RequireApproval { get; set; }
    public bool AllowOverReceipt { get; set; }
    public decimal OverReceiptTolerancePercent { get; set; }
    public int DefaultLeadTimeDays { get; set; } = 7;
    public byte[] RowVersion { get; set; } = [];
}
