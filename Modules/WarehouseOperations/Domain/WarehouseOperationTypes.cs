namespace verii_wms_api_v2.Modules.WarehouseOperations.Domain;

public enum WarehouseOperationStatus
{
    Draft = 1,
    Released = 2,
    InProgress = 3,
    PartiallyProcessed = 4,
    Processed = 5,
    Completed = 6,
    Cancelled = 7
}

public enum OperationApprovalStatus
{
    NotRequired = 1,
    Pending = 2,
    Approved = 3,
    Rejected = 4
}

public enum OperationQualityStatus
{
    NotRequired = 1,
    Pending = 2,
    InProgress = 3,
    PartiallyCompleted = 4,
    Passed = 5,
    Failed = 6
}

public enum OperationPutawayStatus
{
    NotRequired = 1,
    Pending = 2,
    InProgress = 3,
    PartiallyCompleted = 4,
    Completed = 5
}

public enum ErpIntegrationStatus
{
    NotRequired = 1,
    Pending = 2,
    Processing = 3,
    Succeeded = 4,
    Failed = 5,
    CommitUncertain = 6,
    Cancelled = 7
}

public enum WarehouseOperationSourceSystem
{
    Manual = 1,
    Netsis = 2,
    Api = 3,
    Import = 4
}

public enum StockTrackingType
{
    None = 1,
    Lot = 2,
    Serial = 3,
    LotAndSerial = 4
}

public interface IWarehouseOperationHeader
{
    string DocumentNo { get; }
    DateOnly DocumentDate { get; }
    long TargetWarehouseId { get; }
    WarehouseOperationStatus Status { get; }
    Guid CorrelationId { get; }
}

public interface IWarehouseOperationLine
{
    int LineNo { get; }
    long StockId { get; }
    decimal ExpectedQuantity { get; }
    decimal ProcessedQuantity { get; }
    string UnitCode { get; }
}
