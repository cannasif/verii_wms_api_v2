using verii_wms_api_v2.Shared.Domain;

namespace verii_wms_api_v2.Modules.ErpIntegration.Domain;

public enum ErpPostingSourceType
{
    GoodsReceipt = 1,
    WarehouseTransfer = 2,
    Shipment = 3,
    WarehouseInbound = 4,
    WarehouseOutbound = 5
}

public enum ErpPostingStatus
{
    Pending = 1,
    Processing = 2,
    Succeeded = 3,
    Failed = 4,
    CommitUncertain = 5
}

public enum ErpCancellationStatus
{
    Pending = 1,
    Processing = 2,
    ErpDeletionConfirmed = 3,
    Succeeded = 4,
    Failed = 5,
    CommitUncertain = 6,
    CompensationRequired = 7
}

public sealed class ErpPostingRecord : BaseEntity
{
    public ErpPostingSourceType SourceType { get; set; }
    public long SourceEntityId { get; set; }
    public string SourceDocumentNo { get; set; } = string.Empty;
    public Guid IdempotencyKey { get; set; }
    public string RequestHash { get; set; } = string.Empty;
    public ErpPostingStatus Status { get; set; } = ErpPostingStatus.Pending;
    public int AttemptCount { get; set; }
    public DateTimeOffset? StartedAtUtc { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
    public int? LastHttpStatusCode { get; set; }
    public string? ErpDocumentNo { get; set; }
    public string? ErpWaybillNo { get; set; }
    public string? ErpRecordNo { get; set; }
    public long? ErpRecordId { get; set; }
    public string? ErpReferenceNo { get; set; }
    public string? LastErrorCode { get; set; }
    public string? LastErrorMessage { get; set; }
    public string? TraceId { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

public sealed class ErpCancellationRecord : BaseEntity
{
    public long ErpPostingRecordId { get; set; }
    public ErpPostingRecord PostingRecord { get; set; } = null!;
    public Guid IdempotencyKey { get; set; }
    public string RequestHash { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public ErpCancellationStatus Status { get; set; } = ErpCancellationStatus.Pending;
    public int AttemptCount { get; set; }
    public DateTimeOffset? StartedAtUtc { get; set; }
    public DateTimeOffset? ErpDeletedAtUtc { get; set; }
    public DateTimeOffset? WmsReversedAtUtc { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
    public int? LastHttpStatusCode { get; set; }
    public string? LastErrorCode { get; set; }
    public string? LastErrorMessage { get; set; }
    public string? TraceId { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

public sealed class ErpIntegrationAttempt : BaseEntity
{
    public long ErpPostingRecordId { get; set; }
    public ErpPostingRecord PostingRecord { get; set; } = null!;
    public int AttemptNo { get; set; }
    public string Operation { get; set; } = string.Empty;
    public string HttpMethod { get; set; } = "POST";
    public string Endpoint { get; set; } = string.Empty;
    public string RequestHash { get; set; } = string.Empty;
    public string RequestPayload { get; set; } = string.Empty;
    public int? HttpStatusCode { get; set; }
    public bool IsSuccessful { get; set; }
    public bool CommitUncertain { get; set; }
    public long DurationMs { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public string? ProviderResponse { get; set; }
    public string? TraceId { get; set; }
    public DateTimeOffset StartedAtUtc { get; set; }
    public DateTimeOffset CompletedAtUtc { get; set; }
}

public sealed class ErpCancellationAttempt : BaseEntity
{
    public long ErpCancellationRecordId { get; set; }
    public ErpCancellationRecord CancellationRecord { get; set; } = null!;
    public int AttemptNo { get; set; }
    public string Operation { get; set; } = string.Empty;
    public string HttpMethod { get; set; } = "DELETE";
    public string Endpoint { get; set; } = string.Empty;
    public int? HttpStatusCode { get; set; }
    public bool IsSuccessful { get; set; }
    public bool CommitUncertain { get; set; }
    public long DurationMs { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public string? ProviderResponse { get; set; }
    public string? TraceId { get; set; }
    public DateTimeOffset StartedAtUtc { get; set; }
    public DateTimeOffset CompletedAtUtc { get; set; }
}
