using verii_wms_api_v2.Modules.VehicleCheckIn.Application;
using verii_wms_api_v2.Modules.SteelReceipt.Domain;
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

public sealed record AcceptSteelPlateSlot(
    SteelPlateIdentityStatus IdentityStatus,
    long? PlanLineId,
    long? ReceivingLocationId,
    string? RowVersion,
    string? Note);

public sealed record CompleteSteelVehicleAcceptanceRequest(
    Guid IdempotencyKey,
    SaveVehicleCheckInRequest Vehicle,
    IReadOnlyList<AcceptSteelPlateSlot> Slots,
    string? Note);

public sealed record SteelPlateImageUpload(
    long PlanLineId,
    Stream Content,
    string FileName,
    string ContentType,
    long Length);

public sealed record AcceptedSteelPlatePlanLineSummary(
    long Id,
    long PlanId,
    string StockCode,
    string? StockName);

public sealed record AcceptedSteelPlateRow(
    long Id,
    int SequenceNo,
    string IdentityStatus,
    long? PlanLineId,
    long? PlanId,
    string? ImportReferenceNo,
    string? DCode,
    string? StockCode,
    string? SupplierSerialNo,
    decimal? AcceptedQuantity,
    string? UnitCode,
    long? ReceivingLocationId,
    DateTimeOffset AcceptedAtUtc,
    string RowVersion,
    bool CanResolve,
    AcceptedSteelPlatePlanLineSummary? PlanLineSummary,
    IReadOnlyList<SteelReceiptAttachmentRow> Attachments);

public sealed record ResolveUnknownPlateRequest(
    long PlanLineId,
    long? ReceivingLocationId,
    string RowVersion,
    string PlanLineRowVersion,
    string? Note);

public sealed record CompleteSteelVehicleAcceptanceResult(
    long AcceptanceId,
    bool Replayed,
    VehicleCheckInDetail Vehicle,
    IReadOnlyList<AcceptedSteelPlateRow> Plates,
    int UnknownCount,
    bool ContainsUnknownPlates,
    bool CanResolveUnknownPlates);

public interface ISteelVehicleAcceptanceService
{
    Task<PagedResponse<SteelVehicleAcceptanceCandidateRow>> GetCandidatesPagedAsync(
        string branchCode,
        PagedRequest request,
        CancellationToken ct = default);

    Task<CompleteSteelVehicleAcceptanceResult?> GetLatestByVehicleAsync(
        long vehicleCheckInId,
        bool canManageVehicleAcceptance,
        CancellationToken ct = default);

    Task<CompleteSteelVehicleAcceptanceResult> CompleteAsync(
        CompleteSteelVehicleAcceptanceRequest request,
        IReadOnlyList<VehicleImageUpload> vehicleImages,
        IReadOnlyList<SteelPlateImageUpload> plateImages,
        long actor,
        CancellationToken ct = default);

    Task<AcceptedSteelPlateRow> ResolveUnknownPlateAsync(
        long acceptedPlateId,
        ResolveUnknownPlateRequest request,
        IReadOnlyList<SteelPlateImageUpload> plateImages,
        long actor,
        CancellationToken ct = default);
}
