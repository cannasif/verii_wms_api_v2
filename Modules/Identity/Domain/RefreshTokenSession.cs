using verii_wms_api_v2.Shared.Domain;

namespace verii_wms_api_v2.Modules.Identity.Domain;

public sealed class RefreshTokenSession : BaseEntity
{
    public long UserId { get; set; }
    public Guid FamilyId { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public string? RevokedReason { get; set; }
    public string? ReplacedByTokenHash { get; set; }
    public string? CreatedByIp { get; set; }
    public string? RevokedByIp { get; set; }
    public string? UserAgent { get; set; }
    public byte[] RowVersion { get; set; } = [];
    public User User { get; set; } = null!;
}
