using ERP.Application.Common.Exceptions;
using ERP.Application.Common.Interfaces;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;

namespace ERP.Infrastructure.Documents;

public sealed class SmtpOptions {
    public const string Section = "Smtp";

    public string Host { get; set; } = "localhost";

    public int Port { get; set; } = 1025;

    public string From { get; set; } = "Modaryo <belge@modaryo.local>";

    public string? UserName { get; set; }

    public string? Password { get; set; }

    /// <summary>Mailpit gibi yerel sunucularda kapalı; gerçek SMTP'de StartTls kullanılmalı.</summary>
    public bool UseStartTls { get; set; }
}

/// <summary>Belge e-postalarını MailKit ile gönderir (geliştirmede Mailpit).</summary>
public sealed class SmtpDocumentMailer(
    IOptions<SmtpOptions>        options,
    ILogger<SmtpDocumentMailer>  logger
) : IDocumentMailer {
    public async Task SendAsync(IReadOnlyList<string>                                                to,
                                IReadOnlyList<string>?                                               cc,
                                string                                                               subject,
                                string                                                               htmlBody,
                                IReadOnlyList<(string FileName, byte[] Content, string ContentType)> attachments,
                                CancellationToken                                                    ct
    ) {
        var settings = options.Value;

        var mime = new MimeMessage();
        mime.From.Add(MailboxAddress.Parse(settings.From));
        foreach (var address in to) mime.To.Add(MailboxAddress.Parse(address));
        foreach (var address in cc ?? []) mime.Cc.Add(MailboxAddress.Parse(address));
        mime.Subject = subject;

        var builder = new BodyBuilder { HtmlBody = htmlBody };
        foreach (var (fileName, content, contentType) in attachments) builder.Attachments.Add(fileName, content, ContentType.Parse(contentType));
        mime.Body = builder.ToMessageBody();

        try {
            using var client = new SmtpClient();
            await client.ConnectAsync(settings.Host, settings.Port, settings.UseStartTls ? SecureSocketOptions.StartTls : SecureSocketOptions.None, ct);
            if (!string.IsNullOrEmpty(settings.UserName)) await client.AuthenticateAsync(settings.UserName, settings.Password ?? string.Empty, ct);

            await client.SendAsync(mime, ct);
            await client.DisconnectAsync(true, ct);
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            logger.LogError(ex, "Belge e-postası gönderilemedi ({Host}:{Port}).", settings.Host, settings.Port);
            throw new AppException(502, "mail_send_failed", "E-posta gönderilemedi; posta sunucusuna ulaşılamadı ya da adres reddedildi.");
        }
    }
}
