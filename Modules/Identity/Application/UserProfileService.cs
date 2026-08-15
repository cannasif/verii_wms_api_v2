using verii_wms_api_v2.Modules.Identity.Domain;
using verii_wms_api_v2.Shared.Application.Abstractions.Persistence;
using verii_wms_api_v2.Shared.Application.Exceptions;

namespace verii_wms_api_v2.Modules.Identity.Application;

public sealed class UserProfileService(IUnitOfWork unitOfWork, IProfileImageStorage imageStorage) : IUserProfileService
{
    private static readonly HashSet<string> SupportedBackgroundVariants = new(StringComparer.Ordinal)
    {
        "rack-scanner",
        "conveyor-flow",
        "forklift-route",
        "pick-to-light",
        "agv-shuttle",
        "dock-inbound",
        "barcode-scan",
    };

    private IGenericRepository<UserDetail> Details => unitOfWork.Repository<UserDetail>();

    public async Task<UserProfileResponse> GetCurrentAsync(long userId, CancellationToken cancellationToken = default)
    {
        var detail = await Details.FindByIdAsync(userId, cancellationToken: cancellationToken)
            ?? throw AppException.NotFound("Kullanıcı detayı bulunamadı.");
        return ToResponse(detail);
    }

    public async Task<UserProfileResponse> UpdateAppearanceAsync(
        long userId,
        string firstName,
        string lastName,
        UserAppearanceRequest request,
        CancellationToken cancellationToken = default)
    {
        var variant = request.BackgroundMotionVariant?.Trim().ToLowerInvariant() ?? string.Empty;
        if (!SupportedBackgroundVariants.Contains(variant))
            throw AppException.BadRequest("Seçilen arka plan animasyonu desteklenmiyor.");

        var detail = await GetOrCreateAsync(userId, firstName, lastName, cancellationToken);
        detail.BackgroundMotionEnabled = request.BackgroundMotionEnabled;
        detail.BackgroundMotionVariant = variant;
        detail.NavbarCenterMode = NavbarAppearance.NormalizeMode(request.NavbarCenterMode, detail.NavbarCenterMode);
        detail.NavbarKpiKeys = NavbarAppearance.NormalizeKeys(request.NavbarKpiKeys, detail.NavbarKpiKeys);
        detail.UpdatedDate = DateTime.UtcNow;
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return ToResponse(detail);
    }

    public async Task<UserProfileResponse> UpsertAsync(long userId, string firstName, string lastName, ProfileRequest request, CancellationToken cancellationToken = default)
    {
        Validate(request);
        var detail = await GetOrCreateAsync(userId, firstName, lastName, cancellationToken);
        detail.Height = request.Height;
        detail.Weight = request.Weight;
        detail.Description = request.Description?.Trim();
        detail.Gender = request.Gender;
        detail.UpdatedDate = DateTime.UtcNow;
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return ToResponse(detail);
    }

    public async Task<string> UploadPictureAsync(long userId, string firstName, string lastName, ProfileImageUpload upload, CancellationToken cancellationToken = default)
    {
        var detail = await GetOrCreateAsync(userId, firstName, lastName, cancellationToken);
        var previousUrl = detail.ProfilePictureUrl;
        var newUrl = await imageStorage.SaveAsync(userId, upload, cancellationToken);
        try
        {
            detail.ProfilePictureUrl = newUrl;
            detail.UpdatedDate = DateTime.UtcNow;
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            await imageStorage.DeleteIfManagedAsync(newUrl, CancellationToken.None);
            throw;
        }
        await imageStorage.DeleteIfManagedAsync(previousUrl, CancellationToken.None);
        return newUrl;
    }

    public async Task DeletePictureAsync(long userId, CancellationToken cancellationToken = default)
    {
        var detail = await Details.FindByIdAsync(userId, tracking: true, cancellationToken)
            ?? throw AppException.NotFound("Kullanıcı detayı bulunamadı.");
        var previousUrl = detail.ProfilePictureUrl;
        detail.ProfilePictureUrl = null;
        detail.UpdatedDate = DateTime.UtcNow;
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await imageStorage.DeleteIfManagedAsync(previousUrl, CancellationToken.None);
    }

    private async Task<UserDetail> GetOrCreateAsync(long userId, string firstName, string lastName, CancellationToken cancellationToken)
    {
        var detail = await Details.FindByIdAsync(userId, tracking: true, cancellationToken);
        if (detail is not null) return detail;
        detail = new UserDetail { UserId = userId, FirstName = firstName, LastName = lastName, CreatedDate = DateTime.UtcNow };
        await Details.AddAsync(detail, cancellationToken);
        return detail;
    }

    private static void Validate(ProfileRequest request)
    {
        if (request.Description?.Length > 2000) throw AppException.BadRequest("Açıklama en fazla 2000 karakter olabilir.");
        if (request.Height is < 0 or > 9999 || request.Weight is < 0 or > 9999) throw AppException.BadRequest("Boy veya kilo değeri geçersiz.");
    }

    private static UserProfileResponse ToResponse(UserDetail detail) => new(
        detail.UserId,
        detail.UserId,
        detail.ProfilePictureUrl,
        detail.Height,
        detail.Weight,
        detail.Description,
        detail.Gender,
        detail.BackgroundMotionEnabled,
        detail.BackgroundMotionVariant,
        NavbarAppearance.CoerceMode(detail.NavbarCenterMode),
        NavbarAppearance.SplitKeys(detail.NavbarKpiKeys),
        detail.CreatedDate,
        detail.UpdatedDate);
}
