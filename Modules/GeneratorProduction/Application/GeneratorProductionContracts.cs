using verii_wms_api_v2.Modules.GeneratorProduction.Domain;
using verii_wms_api_v2.Shared;

namespace verii_wms_api_v2.Modules.GeneratorProduction.Application;

public sealed record CreateGeneratorProjectRequest(
    string ProjectCode, string ProjectName, string? GeneratorType, string? SerialNumber,
    string? CustomerCode, string? CustomerName, string? ExternalWorkOrderNo, string? SourceSystemCode,
    DateTime PlannedStartAtUtc, DateTime PlannedDeliveryAtUtc, int Priority = 50, int Quantity = 1,
    bool HasStator = true, bool HasRotor = true, bool HasStiffener = true, bool IncludeFinalAssembly = true,
    string? Description = null, long? ProductionHeaderId = null);

public sealed record UpdateGeneratorProjectRequest(
    string ProjectName, string? GeneratorType, string? SerialNumber, string? CustomerCode, string? CustomerName,
    DateTime PlannedStartAtUtc, DateTime PlannedDeliveryAtUtc, int Priority, int Quantity,
    bool HasStator, bool HasRotor, bool HasStiffener, bool IncludeFinalAssembly,
    string? Description, string RowVersion);

public sealed record GeneratorProjectRow(
    long Id, string ProjectCode, string ProjectName, string? GeneratorType, string? SerialNumber,
    string? CustomerName, GeneratorProjectStatus Status, int Priority, int Quantity,
    DateTime PlannedStartAtUtc, DateTime PlannedDeliveryAtUtc, int OperationCount, int CompletedOperationCount,
    string RowVersion);

public sealed record GeneratorProjectDetail(
    long Id, long? ProductionHeaderId, string ProjectCode, string ProjectName, string? GeneratorType, string? SerialNumber,
    string? CustomerCode, string? CustomerName, string? ExternalWorkOrderNo, string? SourceSystemCode,
    DateTime PlannedStartAtUtc, DateTime PlannedDeliveryAtUtc, GeneratorProjectStatus Status, int Priority, int Quantity,
    bool HasStator, bool HasRotor, bool HasStiffener, bool IncludeFinalAssembly, string? Description, string RowVersion);

public sealed record GeneratorStationRow(
    long Id, string Code, string Name, GeneratorStationArea Area, int PlanningOrder, int MaxParallelJobs,
    bool IsActive, bool IsCritical, bool IsBottleneck, bool RequiresCrane, bool RequiresTransport);

public sealed record GeneratorRouteOperationRow(
    long Id, string OperationCode, string OperationName, int Sequence, int DurationMinutes,
    int MinimumDurationMinutes, int MaximumDurationMinutes, bool IsCritical, long StationId, string StationCode, string StationName);

public sealed record GeneratorRouteRow(long Id, string Code, string Name, GeneratorPartType PartType, int VersionNumber, bool IsActive, IReadOnlyList<GeneratorRouteOperationRow> Operations);
public sealed record GeneratorShiftRow(long Id, string Code, string Name, TimeOnly StartTime, TimeOnly EndTime, int PlanningOrder, bool IsActive);
public sealed record GeneratorRuleRow(long Id, string Code, string Name, string Description, GeneratorRuleSeverity Severity, bool IsEnabled);
public sealed record GeneratorDefinitionsResult(IReadOnlyList<GeneratorStationRow> Stations, IReadOnlyList<GeneratorShiftRow> Shifts, IReadOnlyList<GeneratorRouteRow> Routes, IReadOnlyList<GeneratorRuleRow> Rules, bool IsBootstrapped);
public sealed record GeneratorBootstrapResult(int StationCount, int RouteCount, int OperationCount, int RuleCount);

public sealed record GeneratorPlanPreviewRequest(IReadOnlyCollection<long> ProjectIds, DateTime? EarliestStartAtUtc = null);
public sealed record GeneratorPlanApplyRequest(IReadOnlyCollection<long> ProjectIds, string Reason, DateTime? EarliestStartAtUtc = null);
public sealed record GeneratorPlanItem(
    string Key, long ProjectId, string ProjectCode, int UnitIndex, GeneratorPartType PartType,
    long RouteOperationId, long StationId, string StationCode, string StationName,
    string OperationCode, string OperationName, DateTime PlannedStartAtUtc, DateTime PlannedEndAtUtc,
    bool IsCritical, IReadOnlyList<string> PredecessorKeys);
public sealed record GeneratorPlanningIssue(string RuleCode, GeneratorRuleSeverity Severity, long? ProjectId, string Message);
public sealed record GeneratorPlanPreviewResult(IReadOnlyList<GeneratorPlanItem> Items, IReadOnlyList<GeneratorPlanningIssue> Issues, DateTime CalculatedAtUtc, bool CanApply);
public sealed record GeneratorPlanApplyResult(int ProjectCount, int OperationCount, int DependencyCount, long RevisionId, IReadOnlyList<GeneratorPlanningIssue> Issues);

public sealed record GeneratorScheduleRow(
    long Id, long ProjectId, string ProjectCode, string ProjectName, int UnitIndex, GeneratorPartType PartType,
    long StationId, string StationCode, string StationName, string OperationCode, string OperationName,
    GeneratorOperationStatus Status, DateTime PlannedStartAtUtc, DateTime PlannedEndAtUtc,
    DateTime? ActualStartAtUtc, DateTime? ActualEndAtUtc, bool IsCritical, bool HasMaterialShortage, bool HasProblem, string RowVersion);

public sealed record GeneratorOverviewResult(int ProjectCount, int PlannedProjectCount, int ActiveProjectCount, int OperationCount, int DelayedOperationCount, int BottleneckStationCount);

public interface IGeneratorProductionService
{
    Task<GeneratorOverviewResult> GetOverviewAsync(CancellationToken ct = default);
    Task<PagedResponse<GeneratorProjectRow>> GetProjectsAsync(PagedRequest request, CancellationToken ct = default);
    Task<GeneratorProjectDetail> GetProjectAsync(long id, CancellationToken ct = default);
    Task<GeneratorProjectDetail> CreateProjectAsync(CreateGeneratorProjectRequest request, long userId, CancellationToken ct = default);
    Task<GeneratorProjectDetail> UpdateProjectAsync(long id, UpdateGeneratorProjectRequest request, long userId, CancellationToken ct = default);
    Task DeleteProjectAsync(long id, long userId, CancellationToken ct = default);
    Task<GeneratorDefinitionsResult> GetDefinitionsAsync(CancellationToken ct = default);
    Task<GeneratorBootstrapResult> BootstrapDefinitionsAsync(long userId, CancellationToken ct = default);
    Task<GeneratorPlanPreviewResult> PreviewPlanAsync(GeneratorPlanPreviewRequest request, CancellationToken ct = default);
    Task<GeneratorPlanApplyResult> ApplyPlanAsync(GeneratorPlanApplyRequest request, long userId, CancellationToken ct = default);
    Task<IReadOnlyList<GeneratorScheduleRow>> GetScheduleAsync(DateTime fromUtc, DateTime toUtc, CancellationToken ct = default);
}
