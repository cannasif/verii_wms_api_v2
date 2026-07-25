using verii_wms_api_v2.Shared.Domain;

namespace verii_wms_api_v2.Modules.SerialNumberPolicy.Domain;

public enum SerialRuleScope { BranchDefault = 1, StockGroup = 2, Stock = 3 }
public enum SerialCharacterSet { Numeric = 1, UpperAlphaNumeric = 2, AlphaNumeric = 3, Gs1 = 4 }
public enum SerialUniquenessScope { Stock = 1, StockAndYapCode = 2, Global = 3 }

/// <summary>A versioned, effective-dated serial validation rule. Published rows are never overwritten.</summary>
public sealed class SerialNumberRule : BaseEntity
{
    public string RuleCode { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public SerialRuleScope Scope { get; set; }
    public long? StockId { get; set; }
    public string? StockGroupCode { get; set; }
    public int Version { get; set; } = 1;
    public int Priority { get; set; } = 100;
    public string MaskTemplate { get; set; } = string.Empty;
    public SerialCharacterSet CharacterSet { get; set; } = SerialCharacterSet.UpperAlphaNumeric;
    public SerialUniquenessScope UniquenessScope { get; set; } = SerialUniquenessScope.Stock;
    public int MinLength { get; set; } = 1;
    public int MaxLength { get; set; } = 100;
    public bool TrimWhitespace { get; set; } = true;
    public bool NormalizeToUpper { get; set; } = true;
    public bool IsRequired { get; set; } = true;
    public bool IsActive { get; set; } = true;
    public DateTimeOffset EffectiveFromUtc { get; set; }
    public DateTimeOffset? EffectiveToUtc { get; set; }
    public string? Description { get; set; }
    public byte[] RowVersion { get; set; } = [];
}
