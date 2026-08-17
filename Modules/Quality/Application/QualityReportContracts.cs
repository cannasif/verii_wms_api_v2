using verii_wms_api_v2.Modules.Quality.Domain;
using verii_wms_api_v2.Shared;

namespace verii_wms_api_v2.Modules.Quality.Application;

public sealed class QualityInspectionReportRow
{
    public long Id { get; init; }
    public string InspectionNo { get; init; } = string.Empty;
    public string SourceDocumentNo { get; init; } = string.Empty;
    public string? WaybillNo { get; init; }
    public string? SupplierCode { get; init; }
    public string? SupplierName { get; init; }
    public int? WarehouseCode { get; init; }
    public string? WarehouseName { get; init; }
    public QualityInspectionStatus Status { get; init; }
    public int LineCount { get; set; }
    public decimal TotalQuantity { get; set; }
    public decimal RequiredInspectionQuantity { get; set; }
    public decimal InspectedQuantity { get; set; }
    public decimal AcceptedQuantity { get; set; }
    public decimal RejectedQuantity { get; set; }
    public decimal QuarantineQuantity { get; set; }
    public int ControlCount { get; set; }
    public int ImageCount { get; set; }
    public DateTimeOffset CreatedAtUtc { get; init; }
    public DateTimeOffset? StartedAtUtc { get; set; }
    public DateTimeOffset? DecidedAtUtc { get; init; }
    public long ActiveWorkSeconds { get; set; }
    public long ElapsedSeconds { get; set; }
    public long PauseSeconds { get; set; }
    public int PauseCount { get; set; }
    public int BreakCount { get; set; }
    public int ParticipantCount { get; set; }
    public string? Participants { get; set; }
    public decimal InspectionCoveragePercent => TotalQuantity <= 0
        ? 0
        : Math.Round(InspectedQuantity * 100m / TotalQuantity, 2);
}

public sealed class QualityStockReportRow
{
    public long Id { get; init; }
    public long StockId { get; init; }
    public string StockCode { get; init; } = string.Empty;
    public string? StockName { get; init; }
    public int InspectionCount { get; init; }
    public int ReceiptCount { get; init; }
    public decimal TotalQuantity { get; init; }
    public decimal RequiredInspectionQuantity { get; init; }
    public decimal InspectedQuantity { get; init; }
    public decimal AcceptedQuantity { get; init; }
    public decimal RejectedQuantity { get; init; }
    public decimal QuarantineQuantity { get; init; }
    public DateTimeOffset FirstInspectionAtUtc { get; init; }
    public DateTimeOffset LastInspectionAtUtc { get; init; }
    public long ActiveWorkSeconds { get; set; }
    public long AverageWorkSeconds { get; set; }
    public int ParticipantCount { get; set; }
    public decimal InspectionCoveragePercent => TotalQuantity <= 0
        ? 0
        : Math.Round(InspectedQuantity * 100m / TotalQuantity, 2);
}

public sealed record QualityReportWorkerDto(
    long UserId,
    string UserName,
    long ActiveWorkSeconds,
    int SessionCount,
    DateTimeOffset FirstStartedAtUtc,
    DateTimeOffset? LastEndedAtUtc);

public sealed record QualityReportPauseDto(
    int SequenceNo,
    long WorkerUserId,
    string WorkerName,
    QualityInspectionWorkStopReason Reason,
    string? Note,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset EndedAtUtc,
    long WorkSecondsBeforeStop,
    long? PauseSecondsUntilNextSession);

public sealed record QualityInspectionReportLineDto(
    long Id,
    long StockId,
    string StockCode,
    string? StockName,
    string? YapCode,
    string? LotNo,
    string? SerialNo,
    decimal TotalQuantity,
    decimal RequiredInspectionQuantity,
    decimal InspectedQuantity,
    decimal AcceptedQuantity,
    decimal RejectedQuantity,
    decimal QuarantineQuantity,
    QualityDecision Decision,
    int ControlCount,
    int ImageCount,
    string? DecisionCode,
    string? DecisionNote,
    DateTimeOffset? DecisionAtUtc);

public sealed record QualityInspectionReportDetailDto(
    QualityInspectionReportRow Header,
    IReadOnlyList<QualityInspectionReportLineDto> Lines,
    IReadOnlyList<QualityReportWorkerDto> Workers,
    IReadOnlyList<QualityReportPauseDto> Pauses);

public interface IQualityReportService
{
    Task<PagedResponse<QualityInspectionReportRow>> GetInspectionsPagedAsync(
        PagedRequest request,
        CancellationToken ct = default);

    Task<QualityInspectionReportDetailDto> GetInspectionDetailAsync(
        long inspectionId,
        CancellationToken ct = default);

    Task<PagedResponse<QualityStockReportRow>> GetStocksPagedAsync(
        PagedRequest request,
        CancellationToken ct = default);
}
