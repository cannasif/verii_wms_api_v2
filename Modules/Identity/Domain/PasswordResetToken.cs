using verii_wms_api_v2.Shared.Domain;

namespace verii_wms_api_v2.Modules.Identity.Domain;

public sealed class PasswordResetToken : BaseEntity
{
    public long UserId { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public DateTime? ConsumedAt { get; set; }
    public string? RequestedByIp { get; set; }
    public byte[] RowVersion { get; set; } = [];
    public User User { get; set; } = null!;
}
