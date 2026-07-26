using verii_wms_api_v2.Modules.VehicleCheckIn.Application;
using verii_wms_api_v2.Shared;

namespace verii_wms_api_v2.Modules.SteelReceipt.Application;

public sealed record SteelVehicleAcceptanceCandidateRow(
    long Id,
    long PlanId,
    string ImportReferenceNo,
    string SourceFileName,
    int LineNo,
    string DCode,
    string? NetsisOrderNo,
    string StockCode,
    string? StockName,
    string SupplierSerialNo,
    string? SecondarySerialNo,
    string? CombinedSize,
    string? MaterialGrade,
    string? HeatNumber,
    string? CertificateNumber,
    decimal ExpectedQuantity,
    string UnitCode,
    long TargetWarehouseId,
    int WarehouseCode,
    string WarehouseName,
    long ReceivingLocationId,
    string ReceivingLocationCode,
    string ReceivingLocationName,
    int AttachmentCount,
    string RowVersion);

public sealed record AcceptSteelPlateRequest(
    long PlanLineId,
    long ReceivingLocationId,
    string RowVersion,
    string? Note);

public sealed record CompleteSteelVehicleAcceptanceRequest(
    Guid IdempotencyKey,
    SaveVehicleCheckInRequest Vehicle,
    IReadOnlyList<AcceptSteelPlateRequest> Plates,
    string? Note);

public sealed record SteelPlateImageUpload(
    long PlanLineId,
    Stream Content,
    string FileName,
    string ContentType,
    long Length);

public sealed record AcceptedSteelPlateRow(
    long PlanLineId,
    long PlanId,
    string ImportReferenceNo,
    string DCode,
    string StockCode,
    string SupplierSerialNo,
    decimal AcceptedQuantity,
    string UnitCode,
    long ReceivingLocationId,
    DateTimeOffset AcceptedAtUtc);

public sealed record CompleteSteelVehicleAcceptanceResult(
    long AcceptanceId,
    bool Replayed,
    VehicleCheckInDetail Vehicle,
    IReadOnlyList<AcceptedSteelPlateRow> Plates);

public interface ISteelVehicleAcceptanceService
{
    Task<PagedResponse<SteelVehicleAcceptanceCandidateRow>> GetCandidatesPagedAsync(
        string branchCode,
        PagedRequest request,
        CancellationToken ct = default);

    Task<CompleteSteelVehicleAcceptanceResult?> GetLatestByVehicleAsync(
        long vehicleCheckInId,
        CancellationToken ct = default);

    Task<CompleteSteelVehicleAcceptanceResult> CompleteAsync(
        CompleteSteelVehicleAcceptanceRequest request,
        IReadOnlyList<VehicleImageUpload> vehicleImages,
        IReadOnlyList<SteelPlateImageUpload> plateImages,
        long actor,
        CancellationToken ct = default);
}
