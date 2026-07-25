namespace verii_wms_api_v2.Modules.Smtp.Application;

public sealed record SmtpRequest(string Host, int Port, bool EnableSsl, string Username, string? Password, string FromEmail, string FromName, int Timeout);
public sealed record TestMailRequest(string To);
public sealed record SmtpSettingsResponse(long Id, string Host, int Port, bool EnableSsl, string Username, string FromEmail, string FromName, int Timeout, bool HasPassword);

public interface ISmtpSettingsService
{
    Task<SmtpSettingsResponse?> GetAsync(CancellationToken cancellationToken = default);
    Task<SmtpSettingsResponse> UpdateAsync(SmtpRequest request, CancellationToken cancellationToken = default);
    Task TestAsync(TestMailRequest request, CancellationToken cancellationToken = default);
}
