namespace verii_wms_api_v2.Modules.Kkd.Localization;

public sealed class KkdRequestResource;

public static class KkdRequestMessageKeys
{
    public const string NotFound = nameof(NotFound);
    public const string LineNotFound = nameof(LineNotFound);
    public const string EmployeeNotFound = nameof(EmployeeNotFound);
    public const string EmployeeInactive = nameof(EmployeeInactive);
    public const string UserNotFound = nameof(UserNotFound);
    public const string StockNotFound = nameof(StockNotFound);
    public const string InvalidIdempotencyKey = nameof(InvalidIdempotencyKey);
    public const string InvalidLines = nameof(InvalidLines);
    public const string InvalidQuantity = nameof(InvalidQuantity);
    public const string InvalidGroupCode = nameof(InvalidGroupCode);
    public const string InvalidDescription = nameof(InvalidDescription);
    public const string InvalidReason = nameof(InvalidReason);
    public const string InvalidSearchField = nameof(InvalidSearchField);
    public const string GroupEntitlementNotFound = nameof(GroupEntitlementNotFound);
    public const string StockGroupMismatch = nameof(StockGroupMismatch);
    public const string StockCannotChange = nameof(StockCannotChange);
    public const string ClosedRequestCannotChange = nameof(ClosedRequestCannotChange);
    public const string NotCancelled = nameof(NotCancelled);
    public const string RequestHasProgress = nameof(RequestHasProgress);
    public const string ConcurrencyConflict = nameof(ConcurrencyConflict);
    public const string Created = nameof(Created);
    public const string Resolved = nameof(Resolved);
    public const string Assigned = nameof(Assigned);
    public const string Cancelled = nameof(Cancelled);
    public const string Reactivated = nameof(Reactivated);
    public const string TaskNotFound = nameof(TaskNotFound);
    public const string TaskNotActive = nameof(TaskNotActive);
    public const string TaskHasProgress = nameof(TaskHasProgress);
    public const string TaskGroupsRequired = nameof(TaskGroupsRequired);
    public const string DuplicateAssignee = nameof(DuplicateAssignee);
    public const string DuplicateLineAssignment = nameof(DuplicateLineAssignment);
    public const string LineAlreadyAssigned = nameof(LineAlreadyAssigned);
    public const string NothingToAssign = nameof(NothingToAssign);
    public const string NothingToHandoff = nameof(NothingToHandoff);
    public const string HandoffSameUser = nameof(HandoffSameUser);
    public const string TasksAssigned = nameof(TasksAssigned);
    public const string TaskClaimed = nameof(TaskClaimed);
    public const string TaskHandedOver = nameof(TaskHandedOver);
    public const string TaskReturned = nameof(TaskReturned);
    public const string DuplicatePoolGroup = nameof(DuplicatePoolGroup);
    public const string TaskAlreadyClaimed = nameof(TaskAlreadyClaimed);
    public const string TaskNotPooled = nameof(TaskNotPooled);
    public const string WarehouseAccessDenied = nameof(WarehouseAccessDenied);
    public const string TaskClaimedFromPool = nameof(TaskClaimedFromPool);
    public const string PoolLabel = nameof(PoolLabel);
}
