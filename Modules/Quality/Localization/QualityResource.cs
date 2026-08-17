namespace verii_wms_api_v2.Modules.Quality.Localization;

public sealed class QualityResource;

public static class QualityMessageKeys
{
    public const string ReceiptMustBeCompletedBeforeRouting = nameof(ReceiptMustBeCompletedBeforeRouting);
    public const string DatDocumentSeriesRequired = nameof(DatDocumentSeriesRequired);
    public const string DatDocumentSeriesInvalid = nameof(DatDocumentSeriesInvalid);
    public const string InspectionNotFound = nameof(InspectionNotFound);
    public const string PriorityOnlyForOpenInspection = nameof(PriorityOnlyForOpenInspection);
    public const string ControlQuantityRequired = nameof(ControlQuantityRequired);
    public const string ControlQuantityMustBePositive = nameof(ControlQuantityMustBePositive);
    public const string ControlQuantityMustBeInteger = nameof(ControlQuantityMustBeInteger);
    public const string ControlQuantityExceedsLot = nameof(ControlQuantityExceedsLot);
    public const string ControlQuantityBelowMinimum = nameof(ControlQuantityBelowMinimum);
    public const string WorkIdempotencyKeyRequired = nameof(WorkIdempotencyKeyRequired);
    public const string WorkCannotStartForClosedInspection = nameof(WorkCannotStartForClosedInspection);
    public const string ReceiptMustBeCompletedBeforeWork = nameof(ReceiptMustBeCompletedBeforeWork);
    public const string WorkAlreadyActiveByAnotherUser = nameof(WorkAlreadyActiveByAnotherUser);
    public const string WorkStopReasonRequired = nameof(WorkStopReasonRequired);
    public const string WorkOtherStopNoteRequired = nameof(WorkOtherStopNoteRequired);
    public const string WorkHasNoActiveSession = nameof(WorkHasNoActiveSession);
    public const string WorkPauseRequiresOwnerOrSupervisor = nameof(WorkPauseRequiresOwnerOrSupervisor);
    public const string WorkMustBeActiveForCurrentUser = nameof(WorkMustBeActiveForCurrentUser);
    public const string ImageUploadBatchLimit = nameof(ImageUploadBatchLimit);
    public const string ImageCaptionLengthLimit = nameof(ImageCaptionLengthLimit);
    public const string ImageLineLimit = nameof(ImageLineLimit);
    public const string InspectionLineNotFound = nameof(InspectionLineNotFound);
    public const string InspectionImageNotFound = nameof(InspectionImageNotFound);
    public const string InspectionImageRequired = nameof(InspectionImageRequired);
    public const string InspectionImagesUploaded = nameof(InspectionImagesUploaded);
    public const string InspectionImageDeleted = nameof(InspectionImageDeleted);
    public const string InspectionWarehouseAcceptedLocationMissing = nameof(InspectionWarehouseAcceptedLocationMissing);
    public const string InspectionWarehouseRejectLocationMissing = nameof(InspectionWarehouseRejectLocationMissing);
    public const string InspectionWarehouseQuarantineLocationMissing = nameof(InspectionWarehouseQuarantineLocationMissing);
    public const string InspectionWarehouseQualityHoldLocationMissing = nameof(InspectionWarehouseQualityHoldLocationMissing);
}
