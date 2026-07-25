namespace verii_wms_api_v2.Modules.Identity.Domain;
public sealed class UserDetail
{
    public long UserId { get; set; }
    public User User { get; set; } = null!;
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string? Phone { get; set; }
    public string? ProfilePictureUrl { get; set; }
    public decimal? Height { get; set; }
    public decimal? Weight { get; set; }
    public string? Description { get; set; }
    public int? Gender { get; set; }
    public DateTime? CreatedDate { get; set; }
    public DateTime? UpdatedDate { get; set; }
}
