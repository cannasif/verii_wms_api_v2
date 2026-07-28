using verii_wms_api_v2.Shared;

namespace verii_wms_api_v2.Modules.SystemManagement.Application.Users;

public sealed record UserGridRow(long Id, string Username, string Email, string Role, bool IsActive, DateTime? LastLoginAt, string FirstName, string LastName, long? CreatedBy, DateTime? CreatedDate, long? UpdatedBy, DateTime? UpdatedDate);
public sealed record UserDetailResponse(long Id, string Username, string Email, string Role, bool IsActive, DateTime? LastLoginAt, string FirstName, string LastName, string? PhoneNumber, IReadOnlyList<long> PermissionGroupIds);
public sealed record CreateUserRequest(string Username, string Email, string Password, string? FirstName, string? LastName, string? PhoneNumber, string Role, bool IsActive, IReadOnlyList<long> PermissionGroupIds);
public sealed record UpdateUserRequest(string Username, string Email, string? Password, string? FirstName, string? LastName, string? PhoneNumber, string Role, bool IsActive, IReadOnlyList<long> PermissionGroupIds);
public sealed record UserImportRowResult(int RowNumber, string Status, string? Username, string? Email, string Message);
public sealed record UserImportResult(
    int TotalRows,
    int CreatedCount,
    int SkippedCount,
    int FailedCount,
    IReadOnlyList<UserImportRowResult> Rows);

public interface IUserManagementService
{
    Task<PagedResponse<UserGridRow>> GetPagedAsync(PagedRequest request, CancellationToken cancellationToken);
    Task<UserDetailResponse> GetByIdAsync(long id, CancellationToken cancellationToken);
    Task<object> CreateAsync(CreateUserRequest request, CancellationToken cancellationToken);
    Task<UserImportResult> ImportAsync(Stream workbookStream, CancellationToken cancellationToken);
    Task<bool> UpdateAsync(long id, UpdateUserRequest request, CancellationToken cancellationToken);
    Task<bool> DeactivateAsync(long id, CancellationToken cancellationToken);
}
