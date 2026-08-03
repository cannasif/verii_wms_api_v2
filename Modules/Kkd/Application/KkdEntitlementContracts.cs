namespace verii_wms_api_v2.Modules.Kkd.Application;

public sealed record KkdEntitlementCheckRequest(long EmployeeId, long StockId, decimal Quantity, DateOnly? AtDate = null);
public sealed record KkdEntitlementAllocation(string SourceType, long SourceId, decimal Quantity, DateOnly PeriodStart, DateOnly? PeriodEnd);
public sealed record KkdEntitlementCheckResult(
    bool IsAllowed,
    string ReasonCode,
    string Message,
    long EmployeeId,
    long StockId,
    string GroupCode,
    long? MatrixId,
    long? RuleId,
    long? PhaseId,
    string? PhaseType,
    decimal RequestedQuantity,
    decimal MatrixRemainingQuantity,
    decimal OverrideRemainingQuantity,
    decimal TotalRemainingQuantity,
    DateOnly? NextEligibleDate,
    IReadOnlyList<KkdEntitlementAllocation> Allocations);

public interface IKkdEntitlementService
{
    Task<KkdEntitlementCheckResult> CheckAsync(KkdEntitlementCheckRequest request, CancellationToken cancellationToken = default);
}

public sealed record KkdDepartmentUpsertRequest(string Code, string Name, bool IsActive = true);
public sealed record KkdRoleUpsertRequest(long? DepartmentId, string Code, string Name, bool IsActive = true);
public sealed record KkdEmployeeUpsertRequest(long CustomerId, long? UserId, string EmployeeCode, string FirstName, string LastName,
    long DepartmentId, long RoleId, string QrCode, DateOnly EmploymentStartDate, bool IsActive = true);
public sealed record KkdPhaseUpsertRequest(string PhaseType, int OffsetMonths, decimal Quantity, bool AllowBulkIssue,
    int? FrequencyDays, decimal? QuantityPerFrequency, string? PeriodType, int? PeriodInterval, int SortOrder, bool IsActive = true, string? Description = null);
public sealed record KkdRuleUpsertRequest(string GroupCode, string? GroupName, long? StockId, string? StandardCode,
    string? StandardName, int? AnnualIssueCount, decimal? AnnualQuantity, decimal? MaxCarryQuantity,
    bool AllowBulkIssue, bool IsMandatory, int SortOrder, bool IsActive, string? Description, IReadOnlyList<KkdPhaseUpsertRequest> Phases);
public sealed record KkdMatrixUpsertRequest(long CustomerId, long DepartmentId, long RoleId, string Code, string Name,
    DateOnly? EffectiveFrom, DateOnly? EffectiveTo, bool IsActive, string? Description, IReadOnlyList<KkdRuleUpsertRequest> Rules);
public sealed record KkdOverrideCreateRequest(long EmployeeId, long? RuleId, string GroupCode, decimal Quantity,
    DateOnly ValidFrom, DateOnly? ValidTo, string Reason, bool IsActive = true);

public sealed record KkdLookupRow(long Id, string Code, string Name, bool IsActive);
public sealed record KkdEmployeeRow(long Id, string EmployeeCode, string FullName, string QrCode, long CustomerId,
    long DepartmentId, string DepartmentName, long RoleId, string RoleName, DateOnly EmploymentStartDate, bool IsActive);
public sealed record KkdEmployeeQrResolveRequest(string QrCode);
public sealed record KkdMatrixRow(long Id, string Code, string Name, long CustomerId, long DepartmentId, long RoleId,
    DateOnly? EffectiveFrom, DateOnly? EffectiveTo, bool IsActive, int RuleCount);

public interface IKkdDefinitionService
{
    Task<IReadOnlyList<KkdLookupRow>> GetDepartmentsAsync(CancellationToken ct = default);
    Task<IReadOnlyList<KkdLookupRow>> GetRolesAsync(long? departmentId, CancellationToken ct = default);
    Task<IReadOnlyList<KkdEmployeeRow>> GetEmployeesAsync(CancellationToken ct = default);
    Task<KkdEmployeeRow> ResolveEmployeeByQrAsync(string qrCode, CancellationToken ct = default);
    Task<IReadOnlyList<KkdMatrixRow>> GetMatricesAsync(CancellationToken ct = default);
    Task<long> UpsertDepartmentAsync(long? id, KkdDepartmentUpsertRequest request, long actor, CancellationToken ct = default);
    Task<long> UpsertRoleAsync(long? id, KkdRoleUpsertRequest request, long actor, CancellationToken ct = default);
    Task<long> UpsertEmployeeAsync(long? id, KkdEmployeeUpsertRequest request, long actor, CancellationToken ct = default);
    Task<long> UpsertMatrixAsync(long? id, KkdMatrixUpsertRequest request, long actor, CancellationToken ct = default);
    Task<long> CreateOverrideAsync(KkdOverrideCreateRequest request, long actor, CancellationToken ct = default);
}
