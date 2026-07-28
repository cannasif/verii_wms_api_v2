using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using verii_wms_api_v2.Shared.Application.Exceptions;

namespace verii_wms_api_v2.Modules.SystemManagement.Application.Users;

public sealed partial class UserManagementService
{
    public const int MaxImportRows = 500;
    public const int MaxImportFileSize = 5 * 1024 * 1024;

    private static readonly string[] ImportHeaders =
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

    public async Task<UserImportResult> ImportAsync(Stream workbookStream, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workbookStream);

        await using var bufferedWorkbook = await BufferWorkbookAsync(workbookStream, cancellationToken);
        using var workbook = OpenWorkbook(bufferedWorkbook);
        var worksheet = workbook.Worksheets.FirstOrDefault()
            ?? throw AppException.BadRequest("Excel dosyasında çalışma sayfası bulunamadı.");

        ValidateHeaders(worksheet);
        var sourceRows = worksheet.RowsUsed()
            .Where(row => row.RowNumber() > 1 && HasImportData(row))
            .Take(MaxImportRows + 1)
            .ToList();

        if (sourceRows.Count > MaxImportRows)
            throw AppException.BadRequest($"Excel dosyası en fazla {MaxImportRows} veri satırı içerebilir.");

        var existingUsers = await Users.Query()
            .Select(user => new { user.Username, user.Email })
            .ToListAsync(cancellationToken);
        var knownUsernames = new HashSet<string>(
            existingUsers.Select(user => user.Username.Trim()),
            StringComparer.OrdinalIgnoreCase);
        var knownEmails = new HashSet<string>(
            existingUsers.Select(user => user.Email.Trim()),
            StringComparer.OrdinalIgnoreCase);

        var results = new List<UserImportRowResult>(sourceRows.Count);
        foreach (var row in sourceRows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var username = CellText(row, 1);
            var email = CellText(row, 2).ToLowerInvariant();

            if (knownUsernames.Contains(username))
            {
                results.Add(Skipped(row, username, email, "Kullanıcı adı zaten mevcut; kayıt değiştirilmeden atlandı."));
                continue;
            }

            if (knownEmails.Contains(email))
            {
                results.Add(Skipped(row, username, email, "E-posta adresi zaten mevcut; kayıt değiştirilmeden atlandı."));
                continue;
            }

            try
            {
                var request = ParseRequest(row, username, email);
                await CreateAsync(request, cancellationToken);
                knownUsernames.Add(username);
                knownEmails.Add(email);
                results.Add(new UserImportRowResult(
                    row.RowNumber(),
                    "Created",
                    username,
                    email,
                    "Kullanıcı oluşturuldu."));
            }
            catch (AppException exception) when (exception.StatusCode == StatusCodes.Status409Conflict)
            {
                // A concurrent create may win after the initial duplicate scan.
                if (await Users.AnyAsync(user => user.Username == username, cancellationToken))
                    knownUsernames.Add(username);
                if (await Users.AnyAsync(user => user.Email == email, cancellationToken))
                    knownEmails.Add(email);
                results.Add(Skipped(row, username, email, $"{exception.Message} Kayıt değiştirilmeden atlandı."));
            }
            catch (AppException exception)
            {
                results.Add(Failed(row, username, email, exception.Message));
            }
            catch (DbUpdateException)
            {
                var duplicateCreatedConcurrently =
                    await Users.AnyAsync(user => user.Username == username || user.Email == email, cancellationToken);
                results.Add(duplicateCreatedConcurrently
                    ? Skipped(row, username, email, "Kullanıcı adı veya e-posta aynı anda başka bir işlem tarafından oluşturuldu; kayıt değiştirilmeden atlandı.")
                    : Failed(row, username, email, "Kullanıcı veritabanına kaydedilemedi."));
            }
        }

        return new UserImportResult(
            results.Count,
            results.Count(row => row.Status == "Created"),
            results.Count(row => row.Status == "Skipped"),
            results.Count(row => row.Status == "Failed"),
            results);
    }

    private static CreateUserRequest ParseRequest(IXLRow row, string username, string email)
    {
        var password = CellText(row, 3, trim: false);
        var firstName = CellText(row, 4);
        var lastName = CellText(row, 5);
        var phoneNumber = CellText(row, 6);
        var role = CellText(row, 7);
        var isActive = ParseBoolean(CellText(row, 8));
        var permissionGroupIds = ParsePermissionGroupIds(CellText(row, 9));

        return new CreateUserRequest(
            username,
            email,
            password,
            firstName,
            lastName,
            phoneNumber,
            role,
            isActive,
            permissionGroupIds);
    }

    private static bool ParseBoolean(string value) => value.Trim().ToLowerInvariant() switch
    {
        "true" or "1" or "evet" or "yes" => true,
        "false" or "0" or "hayır" or "hayir" or "no" => false,
        _ => throw AppException.BadRequest("IsActive alanı true/false, 1/0 veya evet/hayır olmalıdır.")
    };

    private static IReadOnlyList<long> ParsePermissionGroupIds(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return [];

        var parts = value.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0 || parts.Any(part => !long.TryParse(part, out var id) || id <= 0))
            throw AppException.BadRequest("PermissionGroupIds alanı virgül veya noktalı virgülle ayrılmış pozitif sayılardan oluşmalıdır.");

        return parts.Select(long.Parse).Distinct().ToArray();
    }

    private static void ValidateHeaders(IXLWorksheet worksheet)
    {
        var actualHeaders = Enumerable.Range(1, ImportHeaders.Length)
            .Select(column => worksheet.Cell(1, column).GetString().Trim())
            .ToArray();
        var unexpectedHeader = worksheet.Row(1).CellsUsed()
            .Any(cell => cell.Address.ColumnNumber > ImportHeaders.Length && !string.IsNullOrWhiteSpace(cell.GetString()));

        if (unexpectedHeader || !actualHeaders.SequenceEqual(ImportHeaders, StringComparer.Ordinal))
        {
            throw AppException.BadRequest(
                $"Excel başlıkları geçersiz. Beklenen sıra: {string.Join(", ", ImportHeaders)}.");
        }
    }

    private static bool HasImportData(IXLRow row) =>
        Enumerable.Range(1, ImportHeaders.Length)
            .Any(column => !string.IsNullOrWhiteSpace(row.Cell(column).GetString()));

    private static string CellText(IXLRow row, int column, bool trim = true)
    {
        var value = row.Cell(column).GetString();
        return trim ? value.Trim() : value;
    }

    private static UserImportRowResult Skipped(IXLRow row, string? username, string? email, string message) =>
        new(row.RowNumber(), "Skipped", NullIfEmpty(username), NullIfEmpty(email), message);

    private static UserImportRowResult Failed(IXLRow row, string? username, string? email, string message) =>
        new(row.RowNumber(), "Failed", NullIfEmpty(username), NullIfEmpty(email), message);

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static XLWorkbook OpenWorkbook(Stream stream)
    {
        try
        {
            return new XLWorkbook(stream);
        }
        catch (Exception exception) when (exception is not OperationCanceledException and not OutOfMemoryException)
        {
            throw AppException.BadRequest("Dosya geçerli bir XLSX çalışma kitabı değil.");
        }
    }

    private static async Task<MemoryStream> BufferWorkbookAsync(Stream source, CancellationToken cancellationToken)
    {
        var target = new MemoryStream();
        var buffer = new byte[81920];
        var totalBytes = 0;

        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken);
            if (read == 0) break;
            totalBytes += read;
            if (totalBytes > MaxImportFileSize)
            {
                await target.DisposeAsync();
                throw AppException.BadRequest("XLSX dosyası en fazla 5 MB olabilir.");
            }
            await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }

        if (totalBytes == 0)
        {
            await target.DisposeAsync();
            throw AppException.BadRequest("Yüklenecek XLSX dosyası boş olamaz.");
        }

        target.Position = 0;
        return target;
    }
}
