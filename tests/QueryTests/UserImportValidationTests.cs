using System.Data;
using System.Security.Claims;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using verii_wms_api_v2.Modules.AccessControl.Application;
using verii_wms_api_v2.Modules.Audit.Application;
using verii_wms_api_v2.Modules.Identity.Application;
using verii_wms_api_v2.Modules.SystemManagement.Api;
using verii_wms_api_v2.Modules.SystemManagement.Application.Users;
using verii_wms_api_v2.Shared;
using verii_wms_api_v2.Shared.Application.Abstractions.Persistence;
using verii_wms_api_v2.Shared.Application.Exceptions;
using Xunit;

namespace verii_wms_api_v2.QueryTests;

public sealed class UserImportValidationTests
{
    private static readonly string[] Headers =
    [
        "Username",
        "Email",
        "Password",
        "FirstName",
        "LastName",
        "PhoneNumber",
        "Role",
        "IsActive",
        "PermissionGroupIds"
    ];

    [Fact]
    public async Task Service_rejects_corrupt_xlsx_content()
    {
        var service = ServiceWithoutDatabase();
        await using var stream = new MemoryStream("not-an-xlsx"u8.ToArray());

        var exception = await Assert.ThrowsAsync<AppException>(
            () => service.ImportAsync(stream, CancellationToken.None));

        Assert.Equal(StatusCodes.Status400BadRequest, exception.StatusCode);
        Assert.Contains("geçerli bir XLSX", exception.Message);
    }

    [Fact]
    public async Task Service_rejects_more_than_five_hundred_data_rows()
    {
        var service = ServiceWithoutDatabase();
        await using var stream = CreateWorkbook(UserManagementService.MaxImportRows + 1);

        var exception = await Assert.ThrowsAsync<AppException>(
            () => service.ImportAsync(stream, CancellationToken.None));

        Assert.Equal(StatusCodes.Status400BadRequest, exception.StatusCode);
        Assert.Contains(UserManagementService.MaxImportRows.ToString(), exception.Message);
    }

    [Theory]
    [InlineData("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")]
    [InlineData("application/octet-stream")]
    public async Task Controller_accepts_standard_and_browser_fallback_content_types(string contentType)
    {
        var service = new RecordingUserManagementService();
        var controller = Controller(service);
        var file = FormFile([1, 2, 3], "users.xlsx", contentType);

        var response = await controller.Import(file, CancellationToken.None);

        Assert.IsType<OkObjectResult>(response);
        Assert.True(service.ImportCalled);
    }

    [Theory]
    [InlineData("text/plain", "users.xlsx")]
    [InlineData("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "users.xls")]
    public async Task Controller_rejects_invalid_content_type_or_extension(string contentType, string fileName)
    {
        var service = new RecordingUserManagementService();
        var controller = Controller(service);
        var file = FormFile([1, 2, 3], fileName, contentType);

        var exception = await Assert.ThrowsAsync<AppException>(
            () => controller.Import(file, CancellationToken.None));

        Assert.Equal(StatusCodes.Status400BadRequest, exception.StatusCode);
        Assert.False(service.ImportCalled);
    }

    [Fact]
    public async Task Controller_rejects_files_larger_than_five_megabytes()
    {
        var service = new RecordingUserManagementService();
        var controller = Controller(service);
        var file = FormFile(
            new byte[UserManagementService.MaxImportFileSize + 1],
            "users.xlsx",
            "application/octet-stream");

        var exception = await Assert.ThrowsAsync<AppException>(
            () => controller.Import(file, CancellationToken.None));

        Assert.Equal(StatusCodes.Status400BadRequest, exception.StatusCode);
        Assert.False(service.ImportCalled);
    }

    private static MemoryStream CreateWorkbook(int rowCount)
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.AddWorksheet("Users");
        for (var column = 0; column < Headers.Length; column++)
            worksheet.Cell(1, column + 1).Value = Headers[column];
        for (var index = 1; index <= rowCount; index++)
            worksheet.Cell(index + 1, 1).Value = $"user{index}";
        var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;
        return stream;
    }

    private static UserManagementService ServiceWithoutDatabase() =>
        new(new ThrowingUnitOfWork(), new NoopAuditWriter(), new NoopSessionValidator(), new FixedPasswordPolicyService());

    private static UserManagementController Controller(IUserManagementService service) =>
        new(service, new AllowAllPermissions())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity())
                }
            }
        };

    private static FormFile FormFile(byte[] bytes, string fileName, string contentType) =>
        new(new MemoryStream(bytes), 0, bytes.Length, "file", fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType
        };

    private sealed class AllowAllPermissions : IPermissionAuthorizationService
    {
        public Task<bool> HasPermissionAsync(
            ClaimsPrincipal principal,
            string permissionCode,
            CancellationToken cancellationToken = default) => Task.FromResult(true);
    }

    private sealed class RecordingUserManagementService : IUserManagementService
    {
        public bool ImportCalled { get; private set; }

        public Task<UserImportResult> ImportAsync(Stream workbookStream, CancellationToken cancellationToken)
        {
            ImportCalled = true;
            return Task.FromResult(new UserImportResult(0, 0, 0, 0, []));
        }

        public Task<PagedResponse<UserGridRow>> GetPagedAsync(PagedRequest request, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<UserDetailResponse> GetByIdAsync(long id, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<object> CreateAsync(CreateUserRequest request, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<bool> UpdateAsync(long id, UpdateUserRequest request, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<bool> DeactivateAsync(long id, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class FixedPasswordPolicyService : IPasswordPolicyService
    {
        public Task<PasswordPolicyResponse> GetAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new PasswordPolicyResponse(6, 15));

        public Task ValidateAsync(string? password, CancellationToken cancellationToken = default)
        {
            IdentitySecurity.ValidatePassword(password, 6);
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingUnitOfWork : IUnitOfWork
    {
        public IGenericRepository<TEntity> Repository<TEntity>() where TEntity : class =>
            throw new InvalidOperationException("Bu doğrulama testinde veritabanına erişilmemelidir.");
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException();
        public Task BeginTransactionAsync(IsolationLevel isolationLevel = IsolationLevel.ReadCommitted, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException();
        public Task CommitTransactionAsync(CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException();
        public Task RollbackTransactionAsync(CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException();
        public Task<TResult> ExecuteInTransactionAsync<TResult>(
            Func<CancellationToken, Task<TResult>> operation,
            CancellationToken cancellationToken = default,
            IsolationLevel isolationLevel = IsolationLevel.ReadCommitted) =>
            throw new InvalidOperationException();
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class NoopAuditWriter : IAuditLogWriter
    {
        public Task WriteAsync(AuditLogWriteEntry entry, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class NoopSessionValidator : IIdentitySessionValidator
    {
        public Task<bool> IsValidAsync(long userId, int tokenVersion) => Task.FromResult(true);
        public void Invalidate(long userId) { }
    }
}
