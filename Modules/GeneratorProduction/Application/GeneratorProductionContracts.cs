using verii_wms_api_v2.Modules.GeneratorProduction.Domain;
using verii_wms_api_v2.Shared;

namespace verii_wms_api_v2.Modules.GeneratorProduction.Application;

public sealed record GeneratorPolicyRow(
    long Id, string BranchCode, int MinimumProjectPriority, int MaximumProjectPriority, int DefaultProjectPriority,
    int DefaultProjectQuantity, int MaximumProjectQuantity, int DefaultLeadTimeDays,
    int MinimumPlanReasonLength, int MinimumOperationReasonLength, int MaximumScheduleRangeDays,
    int SchedulePastDays, int ScheduleFutureDays, int GanttDefaultWindowDays, int AndonRefreshSeconds,
    int WorkingCalendarSearchLimitDays, bool RequireComponentForFinalAssembly,
    bool RequireMaterialAvailabilityToStart, bool RequireProblemClosureToComplete,
    bool RequirePositiveCompletionQuantity, GeneratorPlanningOrderStrategy PlanningOrderStrategy,
    string RowVersion);

public sealed record UpdateGeneratorPolicyRequest(
    int MinimumProjectPriority, int MaximumProjectPriority, int DefaultProjectPriority,
    int DefaultProjectQuantity, int MaximumProjectQuantity, int DefaultLeadTimeDays,
    int MinimumPlanReasonLength, int MinimumOperationReasonLength, int MaximumScheduleRangeDays,
    int SchedulePastDays, int ScheduleFutureDays, int GanttDefaultWindowDays, int AndonRefreshSeconds,
    int WorkingCalendarSearchLimitDays, bool RequireComponentForFinalAssembly,
    bool RequireMaterialAvailabilityToStart, bool RequireProblemClosureToComplete,
    bool RequirePositiveCompletionQuantity, GeneratorPlanningOrderStrategy PlanningOrderStrategy,
    string? RowVersion);

public sealed record CreateGeneratorProjectRequest(
    string ProjectCode, string ProjectName, string? GeneratorType, string? SerialNumber,
    string? CustomerCode, string? CustomerName, string? ExternalWorkOrderNo, string? SourceSystemCode,
    DateTime PlannedStartAtUtc, DateTime PlannedDeliveryAtUtc, int? Priority = null, int? Quantity = null,
    bool HasStator = true, bool HasRotor = true, bool HasStiffener = true, bool IncludeFinalAssembly = true,
    int PlanningOrder = 0, string? Description = null, long? ProductionHeaderId = null);

public sealed record UpdateGeneratorProjectRequest(
    string ProjectName, string? GeneratorType, string? SerialNumber, string? CustomerCode, string? CustomerName,
    DateTime PlannedStartAtUtc, DateTime PlannedDeliveryAtUtc, int Priority, int Quantity,
    bool HasStator, bool HasRotor, bool HasStiffener, bool IncludeFinalAssembly, int PlanningOrder,
    string? Description, string RowVersion);

public sealed record GeneratorProjectRow(
    long Id, string ProjectCode, string ProjectName, string? GeneratorType, string? SerialNumber,
    string? CustomerName, GeneratorProjectStatus Status, int Priority, int Quantity,
    DateTime PlannedStartAtUtc, DateTime PlannedDeliveryAtUtc, int PlanningOrder, int OperationCount, int CompletedOperationCount,
    string RowVersion);

public sealed record GeneratorProjectDetail(
    long Id, long? ProductionHeaderId, string ProjectCode, string ProjectName, string? GeneratorType, string? SerialNumber,
    string? CustomerCode, string? CustomerName, string? ExternalWorkOrderNo, string? SourceSystemCode,
    DateTime PlannedStartAtUtc, DateTime PlannedDeliveryAtUtc, GeneratorProjectStatus Status, int Priority, int Quantity,
    bool HasStator, bool HasRotor, bool HasStiffener, bool IncludeFinalAssembly, int PlanningOrder, string? Description, string RowVersion);

public sealed record GeneratorStationRow(
    long Id, string Code, string Name, GeneratorStationArea Area, int PlanningOrder, int MaxParallelJobs,
    int DefaultPersonnelCapacity, bool IsActive, bool IsCritical, bool IsBottleneck, bool RequiresCrane, bool RequiresTransport, string? Description,
    string RowVersion);

public sealed record GeneratorRouteOperationRow(
    long Id, string OperationCode, string OperationName, int Sequence, int DurationMinutes,
    int MinimumDurationMinutes, int MaximumDurationMinutes, bool IsCritical, long StationId, string StationCode, string StationName,
    string RowVersion);

public sealed record GeneratorRouteDependencyRow(
    long Id, long PredecessorOperationId, long SuccessorOperationId, GeneratorDependencyType DependencyType, int LagMinutes);
public sealed record GeneratorRouteRow(
    long Id, string Code, string Name, GeneratorPartType PartType, int VersionNumber, bool IsActive,
    IReadOnlyList<GeneratorRouteOperationRow> Operations, IReadOnlyList<GeneratorRouteDependencyRow> Dependencies);
public sealed record GeneratorShiftRow(long Id, string Code, string Name, TimeOnly StartTime, TimeOnly EndTime, int PlanningOrder, bool IsActive, string RowVersion);
public sealed record GeneratorStationShiftRow(
    long Id, long StationId, string StationCode, string StationName, long ShiftId, string ShiftCode, string ShiftName,
    int WeekdayMask, int CapacityMinutes, int PersonnelCapacity, int MachineCapacity,
    bool CraneAvailable, bool TransportAvailable, bool IsActive, string RowVersion);
public sealed record GeneratorCalendarExceptionRow(
    long Id, long? StationId, string? StationCode, long? ShiftId, string? ShiftCode,
    DateOnly ExceptionDate, bool IsWorking, int? CapacityMinutes, string Reason);
public sealed record GeneratorResourceStationRow(long StationId, string StationCode, string StationName, int RequiredQuantity);
public sealed record GeneratorResourceRow(
    long Id, string Code, string Name, GeneratorResourceType ResourceType, int Capacity,
    bool IsExclusive, bool IsActive, IReadOnlyList<GeneratorResourceStationRow> Stations, string RowVersion);
public sealed record GeneratorRuleRow(
    long Id, string Code, string Name, string Description, GeneratorRuleSeverity Severity,
    bool IsEnabled, bool IsSystemRequired, string? ParametersJson, string RowVersion);
public sealed record UpdateGeneratorRuleRequest(
    string Name, string Description, GeneratorRuleSeverity Severity, bool IsEnabled, string? ParametersJson, string RowVersion);
public sealed record GeneratorDefinitionsResult(
    GeneratorPolicyRow Policy, IReadOnlyList<GeneratorStationRow> Stations, IReadOnlyList<GeneratorShiftRow> Shifts,
    IReadOnlyList<GeneratorStationShiftRow> StationShifts, IReadOnlyList<GeneratorCalendarExceptionRow> CalendarExceptions,
    IReadOnlyList<GeneratorResourceRow> Resources, IReadOnlyList<GeneratorRouteRow> Routes,
    IReadOnlyList<GeneratorRuleRow> Rules, bool IsBootstrapped);
public sealed record GeneratorBootstrapResult(int StationCount, int RouteCount, int OperationCount, int RuleCount);

public sealed record GeneratorPlanPreviewRequest(IReadOnlyCollection<long> ProjectIds, DateTime? EarliestStartAtUtc = null);
public sealed record GeneratorPlanApplyRequest(IReadOnlyCollection<long> ProjectIds, string Reason, DateTime? EarliestStartAtUtc = null);
public sealed record GeneratorPlanItem(
    string Key, long ProjectId, string ProjectCode, int UnitIndex, GeneratorPartType PartType,
    long RouteOperationId, long StationId, string StationCode, string StationName,
    string OperationCode, string OperationName, DateTime PlannedStartAtUtc, DateTime PlannedEndAtUtc,
    bool IsCritical, IReadOnlyList<GeneratorPlanPredecessor> Predecessors);
public sealed record GeneratorPlanPredecessor(string Key, GeneratorDependencyType DependencyType, int LagMinutes);
public sealed record GeneratorPlanningIssue(string RuleCode, GeneratorRuleSeverity Severity, long? ProjectId, string Message);
public sealed record GeneratorPlanPreviewResult(IReadOnlyList<GeneratorPlanItem> Items, IReadOnlyList<GeneratorPlanningIssue> Issues, DateTime CalculatedAtUtc, bool CanApply);
public sealed record GeneratorPlanApplyResult(int ProjectCount, int OperationCount, int DependencyCount, long RevisionId, IReadOnlyList<GeneratorPlanningIssue> Issues);

public sealed record GeneratorScheduleRow(
    long Id, long ProjectId, string ProjectCode, string ProjectName, int UnitIndex, GeneratorPartType PartType,
    long StationId, string StationCode, string StationName, string OperationCode, string OperationName,
    GeneratorOperationStatus Status, DateTime PlannedStartAtUtc, DateTime PlannedEndAtUtc,
    DateTime? ActualStartAtUtc, DateTime? ActualEndAtUtc, bool IsCritical, bool HasMaterialShortage, bool HasProblem, string RowVersion);
public sealed record GeneratorOperationTransitionRequest(
    GeneratorOperationAction Action, string RowVersion, string? Reason = null,
    decimal GoodQuantity = 0, decimal DefectQuantity = 0, decimal ScrapQuantity = 0);

public sealed record GeneratorPlanRevisionRow(
    long Id, long? ProjectId, string? ProjectCode, string ActionType, string Reason,
    DateTime OccurredAtUtc, long ActorUserId, bool HasPreviousPlan, int OperationCount);

public sealed record GeneratorOverviewResult(int ProjectCount, int PlannedProjectCount, int ActiveProjectCount, int OperationCount, int DelayedOperationCount, int BottleneckStationCount);

public interface IGeneratorProductionService
{
    Task<GeneratorOverviewResult> GetOverviewAsync(CancellationToken ct = default);
    Task<PagedResponse<GeneratorProjectRow>> GetProjectsAsync(PagedRequest request, CancellationToken ct = default);
    Task<GeneratorProjectDetail> GetProjectAsync(long id, CancellationToken ct = default);
    Task<GeneratorProjectDetail> CreateProjectAsync(CreateGeneratorProjectRequest request, long userId, CancellationToken ct = default);
    Task<GeneratorProjectDetail> UpdateProjectAsync(long id, UpdateGeneratorProjectRequest request, long userId, CancellationToken ct = default);
    Task DeleteProjectAsync(long id, long userId, CancellationToken ct = default);
    Task<GeneratorPolicyRow> GetPolicyAsync(CancellationToken ct = default);
    Task<GeneratorPolicyRow> UpdatePolicyAsync(UpdateGeneratorPolicyRequest request, long userId, CancellationToken ct = default);
    Task<GeneratorDefinitionsResult> GetDefinitionsAsync(CancellationToken ct = default);
    Task<GeneratorRuleRow> UpdateRuleAsync(long id, UpdateGeneratorRuleRequest request, long userId, CancellationToken ct = default);
    Task<GeneratorBootstrapResult> BootstrapDefinitionsAsync(long userId, CancellationToken ct = default);
    Task<GeneratorPlanPreviewResult> PreviewPlanAsync(GeneratorPlanPreviewRequest request, CancellationToken ct = default);
    Task<GeneratorPlanApplyResult> ApplyPlanAsync(GeneratorPlanApplyRequest request, long userId, CancellationToken ct = default);
    Task<IReadOnlyList<GeneratorScheduleRow>> GetScheduleAsync(DateTime fromUtc, DateTime toUtc, CancellationToken ct = default);
    Task<IReadOnlyList<GeneratorScheduleRow>> GetProjectOperationsAsync(long projectId, CancellationToken ct = default);
    Task<IReadOnlyList<GeneratorPlanRevisionRow>> GetPlanRevisionsAsync(long? projectId, int take, CancellationToken ct = default);
    Task<GeneratorScheduleRow> TransitionOperationAsync(long operationId, GeneratorOperationTransitionRequest request, long userId, CancellationToken ct = default);
}
