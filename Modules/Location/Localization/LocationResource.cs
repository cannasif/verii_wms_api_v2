namespace verii_wms_api_v2.Modules.Location.Localization;

public sealed class LocationResource;

public static class LocationMessageKeys
{
    public const string LocationNotFound = nameof(LocationNotFound);
    public const string WarehouseChangeBlocked = nameof(WarehouseChangeBlocked);
    public const string DeleteHasChildren = nameof(DeleteHasChildren);
    public const string InvalidCode = nameof(InvalidCode);
    public const string InvalidName = nameof(InvalidName);
    public const string InvalidFieldLengths = nameof(InvalidFieldLengths);
    public const string InvalidAddressNumber = nameof(InvalidAddressNumber);
    public const string NegativeCapacity = nameof(NegativeCapacity);
    public const string CapacityUnitRequired = nameof(CapacityUnitRequired);
    public const string QuarantineCannotBePickable = nameof(QuarantineCannotBePickable);
    public const string InvalidLocationType = nameof(InvalidLocationType);
    public const string InvalidBarcodeMode = nameof(InvalidBarcodeMode);
    public const string WarehouseNotFound = nameof(WarehouseNotFound);
    public const string DuplicateCode = nameof(DuplicateCode);
    public const string ManualBarcodeRequired = nameof(ManualBarcodeRequired);
    public const string GeneratedBarcodeTooLong = nameof(GeneratedBarcodeTooLong);
    public const string DuplicateBarcode = nameof(DuplicateBarcode);
    public const string ParentRequired = nameof(ParentRequired);
    public const string ParentNotFoundInWarehouse = nameof(ParentNotFoundInWarehouse);
    public const string InvalidParentType = nameof(InvalidParentType);
    public const string HierarchyCycle = nameof(HierarchyCycle);
    public const string ConcurrencyConflict = nameof(ConcurrencyConflict);
    public const string Created = nameof(Created);
    public const string Updated = nameof(Updated);
    public const string Deleted = nameof(Deleted);
    public const string Forbidden = nameof(Forbidden);
}
