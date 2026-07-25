using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using verii_wms_api_v2.Modules.Identity.Application;
using verii_wms_api_v2.Shared;

namespace verii_wms_api_v2.Modules.Identity.Api;

[ApiController, Authorize, Route("api/userdetail")]
public sealed class UserDetailController(IUserProfileService profileService) : ControllerBase
{
    [HttpGet("current")]
    public async Task<IActionResult> Current(CancellationToken cancellationToken) =>
        Ok(ApiResponse<UserProfileResponse>.Ok(await profileService.GetCurrentAsync(CurrentUserId(), cancellationToken)));

    [HttpPost]
    public async Task<IActionResult> Create(ProfileRequest request, CancellationToken cancellationToken) =>
        Ok(ApiResponse<UserProfileResponse>.Ok(await profileService.UpsertAsync(CurrentUserId(), FirstName(), LastName(), request, cancellationToken)));

    [HttpPut("current")]
    public async Task<IActionResult> Update(ProfileRequest request, CancellationToken cancellationToken) =>
        Ok(ApiResponse<UserProfileResponse>.Ok(await profileService.UpsertAsync(CurrentUserId(), FirstName(), LastName(), request, cancellationToken)));

    [HttpPost("upload-profile-picture"), RequestSizeLimit(5_500_000)]
    public async Task<IActionResult> Upload(IFormFile file, CancellationToken cancellationToken)
    {
        await using var stream = file.OpenReadStream();
        var url = await profileService.UploadPictureAsync(CurrentUserId(), FirstName(), LastName(), new ProfileImageUpload(stream, file.FileName, file.ContentType, file.Length), cancellationToken);
        return Ok(ApiResponse<string>.Ok(url));
    }

    [HttpDelete("delete-profile-picture")]
    public async Task<IActionResult> DeletePicture(CancellationToken cancellationToken)
    {
        await profileService.DeletePictureAsync(CurrentUserId(), cancellationToken);
        return Ok(ApiResponse<bool>.Ok(true));
    }

    private long CurrentUserId() => long.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private string FirstName() => User.FindFirstValue("firstName") ?? string.Empty;
    private string LastName() => User.FindFirstValue("lastName") ?? string.Empty;
}
