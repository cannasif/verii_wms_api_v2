using verii_wms_api_v2.Modules.Production.Domain;
using verii_wms_api_v2.Shared.Domain;
using StockEntity = verii_wms_api_v2.Modules.Stock.Domain.Stock;
using WarehouseEntity = verii_wms_api_v2.Modules.Warehouse.Domain.Warehouse;
using YapCodeEntity = verii_wms_api_v2.Modules.YapCode.Domain.YapCode;

namespace verii_wms_api_v2.Modules.GeneratorProduction.Domain;

public enum GeneratorProjectStatus { Draft, ReadyToPlan, Planned, Released, InProgress, OnHold, Completed, Cancelled }
public enum GeneratorPartType { Common, Stator, Rotor, Stiffener, FinalAssembly, Outbound }
public enum GeneratorStationArea { CommonEntry, Stator, Rotor, Stiffener, FinalAssembly, Outbound }
public enum GeneratorOperationStatus { Draft, Planned, Ready, InProgress, Paused, Completed, Blocked, Cancelled }
public enum GeneratorOperationAction { Start, Pause, Resume, Complete, ReportProblem, ResolveProblem }
public enum GeneratorQualityGateStatus { Pending, Passed, Rejected }
public enum GeneratorDependencyType { FinishToStart, StartToStart, FinishToFinish }
public enum GeneratorRuleSeverity { Information, Warning, Error }
public enum GeneratorResourceType { Personnel, Team, Welding, RobotWelding, ResinCassette, CuringOven, Laser, PigCart, Crane, Transport, Machine }
public enum GeneratorPlanningOrderStrategy { PriorityThenDelivery, DeliveryThenPriority, ManualOrderThenDelivery }

public sealed class GeneratorProductionPolicy : BaseEntity
{
    public string PolicyKey { get; set; } = "DEFAULT";
    public int MinimumProjectPriority { get; set; } = 0;
    public int MaximumProjectPriority { get; set; } = 100;
    public int DefaultProjectPriority { get; set; } = 50;
    public int DefaultProjectQuantity { get; set; } = 1;
    public int MaximumProjectQuantity { get; set; } = 100;
    public int DefaultLeadTimeDays { get; set; } = 30;
    public int MinimumPlanReasonLength { get; set; } = 5;
    public int MinimumOperationReasonLength { get; set; } = 3;
    public int MaximumScheduleRangeDays { get; set; } = 366;
    public int SchedulePastDays { get; set; } = 60;
    public int ScheduleFutureDays { get; set; } = 180;
    public int GanttDefaultWindowDays { get; set; } = 45;
    public int AndonRefreshSeconds { get; set; } = 15;
    public int InboundQualityBufferDays { get; set; } = 2;
    public int WorkingCalendarSearchLimitDays { get; set; } = 3660;
    public bool RequireComponentForFinalAssembly { get; set; } = true;
    public bool RequireMaterialAvailabilityToStart { get; set; } = true;
    public bool RequireProblemClosureToComplete { get; set; } = true;
    public bool RequirePositiveCompletionQuantity { get; set; } = true;
    public GeneratorPlanningOrderStrategy PlanningOrderStrategy { get; set; } = GeneratorPlanningOrderStrategy.PriorityThenDelivery;
    public byte[] RowVersion { get; set; } = [];
}

public sealed class GeneratorProductionProject : BaseEntity
{
    public long? ProductionHeaderId { get; set; }
    public ProductionHeader? ProductionHeader { get; set; }
    public long? ProductId { get; set; }
    public GeneratorProductionProduct? Product { get; set; }
    public string ProjectCode { get; set; } = string.Empty;
    public string ProjectName { get; set; } = string.Empty;
    public string? GeneratorType { get; set; }
    public string? SerialNumber { get; set; }
    public string? CustomerCodeSnapshot { get; set; }
    public string? CustomerNameSnapshot { get; set; }
    public string? ExternalWorkOrderNo { get; set; }
    public string? SourceSystemCode { get; set; }
    public DateTime PlannedStartAtUtc { get; set; }
    public DateTime PlannedDeliveryAtUtc { get; set; }
    public GeneratorProjectStatus Status { get; set; } = GeneratorProjectStatus.Draft;
    public int Priority { get; set; }
    public int Quantity { get; set; } = 1;
    public bool HasStator { get; set; } = true;
    public bool HasRotor { get; set; } = true;
    public bool HasStiffener { get; set; } = true;
    public bool IncludeFinalAssembly { get; set; } = true;
    public int PlanningOrder { get; set; }
    public string? Description { get; set; }
    public byte[] RowVersion { get; set; } = [];
    public ICollection<GeneratorProductionOperation> Operations { get; set; } = [];
}

public sealed class GeneratorProductionProduct : BaseEntity
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? GeneratorType { get; set; }
    public long? ProducedStockId { get; set; }
    public StockEntity? ProducedStock { get; set; }
    public string? ProducedStockCodeSnapshot { get; set; }
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public byte[] RowVersion { get; set; } = [];
    public ICollection<GeneratorProductionProductRoute> Routes { get; set; } = [];
    public ICollection<GeneratorProductionStationCapability> StationCapabilities { get; set; } = [];
    public ICollection<GeneratorProductionOperationMaterial> Materials { get; set; } = [];
}

public sealed class GeneratorProductionProductRoute : BaseEntity
{
    public long ProductId { get; set; }
    public GeneratorProductionProduct Product { get; set; } = null!;
    public GeneratorPartType PartType { get; set; }
    public long RouteId { get; set; }
    public GeneratorProductionRoute Route { get; set; } = null!;
    public bool IsActive { get; set; } = true;
}

public sealed class GeneratorProductionStation : BaseEntity
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public GeneratorStationArea Area { get; set; }
    public int PlanningOrder { get; set; }
    public int MaxParallelJobs { get; set; } = 1;
    public int DefaultPersonnelCapacity { get; set; } = 1;
    public bool IsActive { get; set; } = true;
    public bool IsCritical { get; set; }
    public bool IsBottleneck { get; set; }
    public bool RequiresCrane { get; set; }
    public bool RequiresTransport { get; set; }
    public string? Description { get; set; }
    public byte[] RowVersion { get; set; } = [];
    public ICollection<GeneratorProductionStationShift> Shifts { get; set; } = [];
    public ICollection<GeneratorProductionStationResource> Resources { get; set; } = [];
}

public sealed class GeneratorProductionShift : BaseEntity
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }
    public int PlanningOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public byte[] RowVersion { get; set; } = [];
}

public sealed class GeneratorProductionStationShift : BaseEntity
{
    public long StationId { get; set; }
    public GeneratorProductionStation Station { get; set; } = null!;
    public long ShiftId { get; set; }
    public GeneratorProductionShift Shift { get; set; } = null!;
    public int WeekdayMask { get; set; } = 31;
    public int CapacityMinutes { get; set; } = 480;
    public int PersonnelCapacity { get; set; } = 1;
    public int MachineCapacity { get; set; } = 1;
    public bool CraneAvailable { get; set; }
    public bool TransportAvailable { get; set; }
    public bool IsActive { get; set; } = true;
    public byte[] RowVersion { get; set; } = [];
}

public sealed class GeneratorProductionCalendarException : BaseEntity
{
    public long? StationId { get; set; }
    public GeneratorProductionStation? Station { get; set; }
    public long? ShiftId { get; set; }
    public GeneratorProductionShift? Shift { get; set; }
    public DateOnly ExceptionDate { get; set; }
    public bool IsWorking { get; set; }
    public int? CapacityMinutes { get; set; }
    public string Reason { get; set; } = string.Empty;
}

public sealed class GeneratorProductionResource : BaseEntity
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public GeneratorResourceType ResourceType { get; set; }
    public int Capacity { get; set; } = 1;
    public bool IsExclusive { get; set; }
    public bool IsActive { get; set; } = true;
    public byte[] RowVersion { get; set; } = [];
}

public sealed class GeneratorProductionStationResource : BaseEntity
{
    public long StationId { get; set; }
    public GeneratorProductionStation Station { get; set; } = null!;
    public long ResourceId { get; set; }
    public GeneratorProductionResource Resource { get; set; } = null!;
    public int RequiredQuantity { get; set; } = 1;
}

public sealed class GeneratorProductionRoute : BaseEntity
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public GeneratorPartType PartType { get; set; }
    public int VersionNumber { get; set; } = 1;
    public bool IsActive { get; set; } = true;
    public byte[] RowVersion { get; set; } = [];
    public ICollection<GeneratorProductionRouteOperation> Operations { get; set; } = [];
    public ICollection<GeneratorProductionRouteDependency> Dependencies { get; set; } = [];
}

public sealed class GeneratorProductionRouteOperation : BaseEntity
{
    public long RouteId { get; set; }
    public GeneratorProductionRoute Route { get; set; } = null!;
    public long StationId { get; set; }
    public GeneratorProductionStation Station { get; set; } = null!;
    public string OperationCode { get; set; } = string.Empty;
    public string OperationName { get; set; } = string.Empty;
    public int Sequence { get; set; }
    public int DurationMinutes { get; set; }
    public int MinimumDurationMinutes { get; set; }
    public int MaximumDurationMinutes { get; set; }
    public bool IsCritical { get; set; }
    public bool IsRequired { get; set; } = true;
    public bool IsActive { get; set; } = true;
    public byte[] RowVersion { get; set; } = [];
}

public sealed class GeneratorProductionStationCapability : BaseEntity
{
    public long ProductId { get; set; }
    public GeneratorProductionProduct Product { get; set; } = null!;
    public long RouteOperationId { get; set; }
    public GeneratorProductionRouteOperation RouteOperation { get; set; } = null!;
    public long StationId { get; set; }
    public GeneratorProductionStation Station { get; set; } = null!;
    public bool IsPrimary { get; set; }
    public int EfficiencyPercent { get; set; } = 100;
    public int SetupMinutes { get; set; }
    public bool IsActive { get; set; } = true;
    public byte[] RowVersion { get; set; } = [];
}

public sealed class GeneratorProductionOperationMaterial : BaseEntity
{
    public long ProductId { get; set; }
    public GeneratorProductionProduct Product { get; set; } = null!;
    public long RouteOperationId { get; set; }
    public GeneratorProductionRouteOperation RouteOperation { get; set; } = null!;
    public long StockId { get; set; }
    public StockEntity Stock { get; set; } = null!;
    public long? YapCodeId { get; set; }
    public YapCodeEntity? YapCode { get; set; }
    public long WarehouseId { get; set; }
    public WarehouseEntity Warehouse { get; set; } = null!;
    public string StockCodeSnapshot { get; set; } = string.Empty;
    public string StockNameSnapshot { get; set; } = string.Empty;
    public string UnitCode { get; set; } = "ADET";
    public decimal QuantityPerUnit { get; set; }
    public decimal WasteRate { get; set; }
    public int NeedOffsetMinutes { get; set; }
    public bool IsMandatory { get; set; } = true;
    public byte[] RowVersion { get; set; } = [];
}

public sealed class GeneratorProductionRouteDependency : BaseEntity
{
    public long RouteId { get; set; }
    public GeneratorProductionRoute Route { get; set; } = null!;
    public long PredecessorOperationId { get; set; }
    public GeneratorProductionRouteOperation PredecessorOperation { get; set; } = null!;
    public long SuccessorOperationId { get; set; }
    public GeneratorProductionRouteOperation SuccessorOperation { get; set; } = null!;
    public GeneratorDependencyType DependencyType { get; set; } = GeneratorDependencyType.FinishToStart;
    public int LagMinutes { get; set; }
}

public sealed class GeneratorProductionOperation : BaseEntity
{
    public long ProjectId { get; set; }
    public GeneratorProductionProject Project { get; set; } = null!;
    public long RouteOperationId { get; set; }
    public GeneratorProductionRouteOperation RouteOperation { get; set; } = null!;
    public long StationId { get; set; }
    public GeneratorProductionStation Station { get; set; } = null!;
    public long? ProductionOrderId { get; set; }
    public ProductionOrder? ProductionOrder { get; set; }
    public int UnitIndex { get; set; } = 1;
    public GeneratorOperationStatus Status { get; set; } = GeneratorOperationStatus.Draft;
    public DateTime PlannedStartAtUtc { get; set; }
    public DateTime PlannedEndAtUtc { get; set; }
    public DateTime? ActualStartAtUtc { get; set; }
    public DateTime? ActualEndAtUtc { get; set; }
    public decimal GoodQuantity { get; set; }
    public decimal DefectQuantity { get; set; }
    public decimal ScrapQuantity { get; set; }
    public bool HasMaterialShortage { get; set; }
    public bool HasProblem { get; set; }
    public string? ProblemDescription { get; set; }
    public bool IsCritical { get; set; }
    public bool IsScheduleLocked { get; set; }
    public string? ManualScheduleReason { get; set; }
    public long? ManualScheduledBy { get; set; }
    public DateTime? ManualScheduledAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];
    public GeneratorProductionQualityGate? QualityGate { get; set; }
    public ICollection<GeneratorProductionOperationDependency> Predecessors { get; set; } = [];
    public ICollection<GeneratorProductionOperationDependency> Successors { get; set; } = [];
}

public sealed class GeneratorProductionOperationDependency : BaseEntity
{
    public long PredecessorOperationId { get; set; }
    public GeneratorProductionOperation PredecessorOperation { get; set; } = null!;
    public long SuccessorOperationId { get; set; }
    public GeneratorProductionOperation SuccessorOperation { get; set; } = null!;
    public GeneratorDependencyType DependencyType { get; set; } = GeneratorDependencyType.FinishToStart;
    public int LagMinutes { get; set; }
    public bool RequiresAcceptedOutput { get; set; }
    public bool RequiresWarehouseTransfer { get; set; }
}

public sealed class GeneratorProductionPlanRevision : BaseEntity
{
    public long? ProjectId { get; set; }
    public GeneratorProductionProject? Project { get; set; }
    public string ActionType { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public string? PreviousPlanJson { get; set; }
    public string NewPlanJson { get; set; } = string.Empty;
    public DateTime OccurredAtUtc { get; set; }
    public long ActorUserId { get; set; }
}

public sealed class GeneratorProductionRule : BaseEntity
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public GeneratorRuleSeverity Severity { get; set; }
    public bool IsEnabled { get; set; } = true;
    public bool IsSystemRequired { get; set; }
    public string? ParametersJson { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

public sealed class GeneratorProductionQualityGate : BaseEntity
{
    public long OperationId { get; set; }
    public GeneratorProductionOperation Operation { get; set; } = null!;
    public GeneratorQualityGateStatus Status { get; set; } = GeneratorQualityGateStatus.Pending;
    public DateTime RequestedAtUtc { get; set; }
    public long? DecisionBy { get; set; }
    public DateTime? DecisionAtUtc { get; set; }
    public string? DecisionNote { get; set; }
    public byte[] RowVersion { get; set; } = [];
}
