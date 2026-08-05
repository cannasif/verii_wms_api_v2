using System.Net;
using System.Net.Mail;
using Microsoft.AspNetCore.DataProtection;
using verii_wms_api_v2.Modules.Identity.Application;
using verii_wms_api_v2.Modules.Smtp.Application;
using verii_wms_api_v2.Modules.Smtp.Domain;
using verii_wms_api_v2.Modules.Procurement.Application;
using verii_wms_api_v2.Shared.Application.Abstractions.Persistence;
using verii_wms_api_v2.Shared.Application.Exceptions;

namespace verii_wms_api_v2.Modules.Smtp.Infrastructure;

public sealed class SmtpSettingsService(IUnitOfWork unitOfWork, IDataProtectionProvider protection)
    : ISmtpSettingsService, IIdentityEmailSender, IProcurementEmailSender
{
    private readonly IDataProtector protector = protection.CreateProtector("verii-wms-v2.smtp-password");
    private IGenericRepository<SmtpSetting> Settings => unitOfWork.Repository<SmtpSetting>();

    public async Task<SmtpSettingsResponse?> GetAsync(CancellationToken cancellationToken = default)
    {
        var setting = await Settings.FirstOrDefaultAsync(_ => true, cancellationToken: cancellationToken);
        return setting is null ? null : ToResponse(setting);
    }

    public async Task<SmtpSettingsResponse> UpdateAsync(SmtpRequest request, CancellationToken cancellationToken = default)
    {
        Validate(request);
        var setting = await Settings.FirstOrDefaultAsync(_ => true, tracking: true, cancellationToken);
        if (setting is null)
        {
            setting = new SmtpSetting();
            await Settings.AddAsync(setting, cancellationToken);
        }
        setting.Host = request.Host.Trim();
        setting.Port = request.Port;
        setting.EnableSsl = request.EnableSsl;
        setting.Username = request.Username.Trim();
        setting.FromEmail = request.FromEmail.Trim();
        setting.FromName = request.FromName.Trim();
        setting.Timeout = request.Timeout;
        if (!string.IsNullOrWhiteSpace(request.Password)) setting.PasswordEncrypted = protector.Protect(request.Password);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return ToResponse(setting);
    }

    public async Task TestAsync(TestMailRequest request, CancellationToken cancellationToken = default)
    {
        if (!MailAddress.TryCreate(request.To?.Trim(), out var recipient))
            throw AppException.BadRequest("Geçerli bir alıcı e-posta adresi giriniz.");
        var setting = await GetConfiguredSettingAsync(cancellationToken);
        using var client = CreateClient(setting);
        using var message = new MailMessage(new MailAddress(setting.FromEmail, setting.FromName), recipient)
        {
            Subject = "V3RII WMS SMTP Test",
            Body = "SMTP ayarları başarıyla çalışıyor."
        };
        await client.SendMailAsync(message, cancellationToken);
    }

    public async Task SendPasswordResetAsync(string recipientEmail, string resetUrl, CancellationToken cancellationToken = default)
    {
        if (!MailAddress.TryCreate(recipientEmail?.Trim(), out var recipient))
            throw new InvalidOperationException("Password reset recipient is invalid.");

        var setting = await GetConfiguredSettingAsync(cancellationToken);
        var safeUrl = WebUtility.HtmlEncode(resetUrl);
        using var client = CreateClient(setting);
        using var message = new MailMessage(new MailAddress(setting.FromEmail, setting.FromName), recipient)
        {
            Subject = "V3RII WMS şifre yenileme",
            IsBodyHtml = true,
            Body = $"""
                <p>V3RII WMS hesabınız için şifre yenileme isteği alındı.</p>
                <p><a href="{safeUrl}">Şifremi yenile</a></p>
                <p>Bu isteği siz yapmadıysanız bu e-postayı yok sayabilirsiniz.</p>
                """
        };
        await client.SendMailAsync(message, cancellationToken);
    }

    public async Task SendQuoteInvitationAsync(string recipientEmail,string supplierName,string rfqNo,string subject,DateOnly responseDueDate,string portalUrl,CancellationToken cancellationToken=default)
    {
        if(!MailAddress.TryCreate(recipientEmail?.Trim(),out var recipient))
            throw AppException.BadRequest("Geçerli bir tedarikçi e-posta adresi giriniz.");
        var setting=await GetConfiguredSettingAsync(cancellationToken);
        using var client=CreateClient(setting);
        using var message=new MailMessage(new MailAddress(setting.FromEmail,setting.FromName),recipient)
        {
            Subject=$"{rfqNo} teklif talebi",
            IsBodyHtml=true,
            Body=$"""
                <p>Sayın {WebUtility.HtmlEncode(supplierName)},</p>
                <p><strong>{WebUtility.HtmlEncode(subject)}</strong> konusu için teklifinizi güvenli tedarikçi portalından iletebilirsiniz.</p>
                <p>Teklif son tarihi: <strong>{responseDueDate:dd.MM.yyyy}</strong></p>
                <p><a href="{WebUtility.HtmlEncode(portalUrl)}">Teklif talebini aç ve fiyatları gir</a></p>
                <p>Bu bağlantı yalnız bu teklif talebi içindir. Yetkisiz kişilerle paylaşmayınız.</p>
                """
        };
        await client.SendMailAsync(message,cancellationToken);
    }

    private async Task<SmtpSetting> GetConfiguredSettingAsync(CancellationToken cancellationToken)
    {
        var setting = await Settings.FirstOrDefaultAsync(_ => true, cancellationToken: cancellationToken);
        if (setting is null || string.IsNullOrWhiteSpace(setting.PasswordEncrypted))
            throw AppException.BadRequest("SMTP ayarı veya şifresi bulunamadı.");
        return setting;
    }

    private SmtpClient CreateClient(SmtpSetting setting) => new(setting.Host, setting.Port)
    {
        EnableSsl = setting.EnableSsl,
        Credentials = new NetworkCredential(setting.Username, protector.Unprotect(setting.PasswordEncrypted)),
        Timeout = checked(setting.Timeout * 1000)
    };

    private static void Validate(SmtpRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Host) || request.Host.Length > 255) throw AppException.BadRequest("SMTP sunucu adresi geçersiz.");
        if (request.Port is < 1 or > 65535) throw AppException.BadRequest("SMTP portu geçersiz.");
        if (request.Timeout is < 1 or > 300) throw AppException.BadRequest("SMTP zaman aşımı 1-300 saniye arasında olmalıdır.");
        if (string.IsNullOrWhiteSpace(request.Username) || request.Username.Length > 255) throw AppException.BadRequest("SMTP kullanıcı adı geçersiz.");
        if (!MailAddress.TryCreate(request.FromEmail?.Trim(), out _)) throw AppException.BadRequest("Gönderen e-posta adresi geçersiz.");
        if (string.IsNullOrWhiteSpace(request.FromName) || request.FromName.Length > 200) throw AppException.BadRequest("Gönderen adı geçersiz.");
    }

    private static SmtpSettingsResponse ToResponse(SmtpSetting setting) =>
        new(setting.Id, setting.Host, setting.Port, setting.EnableSsl, setting.Username, setting.FromEmail, setting.FromName, setting.Timeout, !string.IsNullOrWhiteSpace(setting.PasswordEncrypted));
}
