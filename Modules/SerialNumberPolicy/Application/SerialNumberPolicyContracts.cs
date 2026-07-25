using verii_wms_api_v2.Modules.SerialNumberPolicy.Domain;
using verii_wms_api_v2.Shared;

namespace verii_wms_api_v2.Modules.SerialNumberPolicy.Application;

public sealed record SerialRuleUpsertRequest(string BranchCode, string RuleCode, string DisplayName,
    SerialRuleScope Scope, long? StockId, string? StockGroupCode, int Priority, string MaskTemplate,
    SerialCharacterSet CharacterSet, SerialUniquenessScope UniquenessScope, int MinLength, int MaxLength,
    bool TrimWhitespace, bool NormalizeToUpper, bool IsRequired, bool IsActive,
    DateTimeOffset EffectiveFromUtc, DateTimeOffset? EffectiveToUtc, string? Description);
public sealed record SerialRuleRow(long Id, string BranchCode, string RuleCode, string DisplayName, string Scope,
    long? StockId, string? StockCode, string? StockName, string? StockGroupCode, int Version, int Priority,
    string MaskTemplate, string CharacterSet, string UniquenessScope, int MinLength, int MaxLength,
    bool IsRequired, bool IsActive, DateTimeOffset EffectiveFromUtc, DateTimeOffset? EffectiveToUtc,
    string? Description, string ConcurrencyToken, long? CreatedBy, DateTime? CreatedDate);
public sealed record ValidateSerialRequest(string BranchCode, long StockId, long? YapCodeId, string? SerialNo);
public sealed record SerialValidationResult(string? NormalizedSerial, bool IsValid, string Source,
    long? RuleId, int? RuleVersion, string? RuleCode, string? MaskTemplate, string? Error);

public interface ISerialNumberPolicyService
{
    Task<PagedResponse<SerialRuleRow>> GetPagedAsync(PagedRequest request, CancellationToken ct = default);
    Task<long> CreateAsync(SerialRuleUpsertRequest request, long actor, CancellationToken ct = default);
    Task<long> CreateNextVersionAsync(long id, SerialRuleUpsertRequest request, long actor, string? concurrencyToken, CancellationToken ct = default);
    Task DeleteAsync(long id, long actor, CancellationToken ct = default);
    Task<SerialValidationResult> ValidateAsync(ValidateSerialRequest request, CancellationToken ct = default);
}
public interface ISerialNumberPolicyResolver
{
    Task<SerialValidationResult> ValidateAsync(string branchCode, long stockId, long? yapCodeId, string? serialNo, CancellationToken ct = default);
}
