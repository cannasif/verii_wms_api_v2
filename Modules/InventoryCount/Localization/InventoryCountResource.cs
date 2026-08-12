namespace verii_wms_api_v2.Modules.InventoryCount.Localization;

public sealed class InventoryCountResource { }

public static class InventoryCountMessageKeys
{
    public const string NotFound = nameof(NotFound);
    public const string WarehouseNotFound = nameof(WarehouseNotFound);
    public const string DraftOnly = nameof(DraftOnly);
    public const string InvalidRequest = nameof(InvalidRequest);
    public const string ScopeRequired = nameof(ScopeRequired);
    public const string LocationNotCountable = nameof(LocationNotCountable);
    public const string ActiveCountConflict = nameof(ActiveCountConflict);
    public const string EmptyScope = nameof(EmptyScope);
    public const string ConcurrencyConflict = nameof(ConcurrencyConflict);
    public const string PermissionDenied = nameof(PermissionDenied);
    public const string DraftCreated = nameof(DraftCreated);
    public const string DraftUpdated = nameof(DraftUpdated);
    public const string DraftDeleted = nameof(DraftDeleted);
    public const string Released = nameof(Released);
    public const string PolicySaved = nameof(PolicySaved);
}
