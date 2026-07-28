using ClosedXML.Excel;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using verii_wms_api_v2.Modules.Audit.Application;
using verii_wms_api_v2.Modules.Identity.Application;
using verii_wms_api_v2.Modules.Identity.Domain;
using verii_wms_api_v2.Modules.Identity.Infrastructure;
using verii_wms_api_v2.Modules.SystemManagement.Application.Users;
using verii_wms_api_v2.Shared.Application.Exceptions;
using verii_wms_api_v2.Shared.Infrastructure.Persistence;
using Xunit;

namespace verii_wms_api_v2.QueryTests;

public sealed class UserImportServiceTests
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
    public async Task Import_creates_only_new_users_and_never_changes_existing_users()
    {
        await using var db = CreateDbContext();
        var existingHash = BCrypt.Net.BCrypt.HashPassword("ExistingPassword!");
        db.Users.Add(new User
        {
            Username = "existing.user",
            Email = "existing@firma.com",
            PasswordHash = existingHash,
            Role = "Manager",
            IsActive = false
        });
        await db.SaveChangesAsync();

        var audit = new RecordingAuditWriter();
        var service = CreateService(db, audit);
        await using var workbook = Workbook(
            ["new.user", "new.user@firma.com", "TempPass!2026", "Yeni", "Kullanıcı", "", "User", "true", ""],
            ["EXISTING.USER", "different@firma.com", "TempPass!2026", "", "", "", "Admin", "true", ""],
            ["another.user", "not-an-email", "TempPass!2026", "", "", "", "User", "true", ""],
            ["duplicate.email", "NEW.USER@FIRMA.COM", "TempPass!2026", "", "", "", "User", "true", ""]);

        var result = await service.ImportAsync(workbook, CancellationToken.None);

        Assert.Equal(4, result.TotalRows);
        Assert.Equal(1, result.CreatedCount);
        Assert.Equal(2, result.SkippedCount);
        Assert.Equal(1, result.FailedCount);
        Assert.Equal(["Created", "Skipped", "Failed", "Skipped"], result.Rows.Select(row => row.Status).ToArray());

        var existing = await db.Users.SingleAsync(user => user.Username == "existing.user");
        Assert.Equal("existing@firma.com", existing.Email);
        Assert.Equal("Manager", existing.Role);
        Assert.False(existing.IsActive);
        Assert.Equal(existingHash, existing.PasswordHash);

        var created = await db.Users.SingleAsync(user => user.Username == "new.user");
        Assert.Equal("new.user@firma.com", created.Email);
        Assert.Equal("TempPass!2026".Length, created.PasswordLength);
        Assert.True(created.IsActive);
        Assert.Single(audit.Entries);
        Assert.DoesNotContain("TempPass!2026", string.Join(" ", result.Rows.Select(row => row.Message)));
    }

    [Fact]
    public async Task Import_rejects_a_workbook_with_changed_headers()
    {
        await using var db = CreateDbContext();
        var service = CreateService(db, new RecordingAuditWriter());
        await using var workbook = Workbook(
            [["new.user", "new.user@firma.com", "TempPass!2026", "", "", "", "User", "true", ""]],
            ["Kullanıcı Adı", .. Headers.Skip(1)]);

        var exception = await Assert.ThrowsAsync<AppException>(
            () => service.ImportAsync(workbook, CancellationToken.None));

        Assert.Equal(StatusCodes.Status400BadRequest, exception.StatusCode);
        Assert.Empty(db.Users);
    }

    private static UserManagementService CreateService(WmsDbContext db, RecordingAuditWriter audit) =>
        new(
            new UnitOfWork(db, new HttpContextAccessor()),
            audit,
            new NoOpSessionValidator(),
            new FixedPasswordPolicyService());

    private static WmsDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<WmsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new WmsDbContext(options);
    }

    private static MemoryStream Workbook(params string[][] rows) => Workbook(rows, Headers);

    private static MemoryStream Workbook(string[][] rows, string[] headers)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Kullanıcılar");
        for (var column = 0; column < headers.Length; column++)
            sheet.Cell(1, column + 1).Value = headers[column];
        for (var row = 0; row < rows.Length; row++)
            for (var column = 0; column < rows[row].Length; column++)
                sheet.Cell(row + 2, column + 1).Value = rows[row][column];

        var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;
        return stream;
    }

    private sealed class RecordingAuditWriter : IAuditLogWriter
    {
        public List<AuditLogWriteEntry> Entries { get; } = [];

        public Task WriteAsync(AuditLogWriteEntry entry, CancellationToken cancellationToken = default)
        {
            Entries.Add(entry);
            return Task.CompletedTask;
        }
    }

    private sealed class NoOpSessionValidator : IIdentitySessionValidator
    {
        public Task<bool> IsValidAsync(long userId, int tokenVersion) => Task.FromResult(true);
        public void Invalidate(long userId) { }
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
}
