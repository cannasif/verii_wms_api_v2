namespace verii_wms_api_v2.Modules.Identity.Application;

public interface IIdentitySessionValidator
{
    Task<bool> IsValidAsync(long userId, int tokenVersion);
    void Invalidate(long userId);
}
