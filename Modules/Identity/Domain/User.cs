namespace verii_wms_api_v2.Modules.Identity.Domain;
public sealed class User
{
    public long Id { get; set; }
    public string Username { get; set; } = "";
    public string Email { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public int PasswordLength { get; set; } = 15;
    public string Role { get; set; } = "User";
    public bool IsActive { get; set; } = true;
    public DateTime? LastLoginAt { get; set; }
    public int TokenVersion { get; set; } = 1;
    public UserDetail? Detail { get; set; }
}
