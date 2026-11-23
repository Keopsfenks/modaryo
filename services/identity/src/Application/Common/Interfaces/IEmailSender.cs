using IdentityService.Application.Public.Commands;

namespace IdentityService.Application.Common.Interfaces;

public sealed record EmailMessage(string To, string Subject, string HtmlBody, string TextBody);

public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken ct);
}

/// <summary>Türkçe e-posta şablonları. Linkler App:FrontendBaseUrl adresine gider.</summary>
public interface IEmailComposer
{
    EmailMessage ConfirmEmail(string to, string fullName, Guid userId, string token);

    EmailMessage ResetPassword(string to, string fullName, string token);

    EmailMessage AccountAlreadyExists(string to);

    EmailMessage Invitation(string to, string companyName, string inviterName, string inviteToken);

    EmailMessage CleanupReminder(string to, string fullName, DateTimeOffset deleteOn);

    EmailMessage ContactRequest(string to, SubmitContactCommand request);
}
