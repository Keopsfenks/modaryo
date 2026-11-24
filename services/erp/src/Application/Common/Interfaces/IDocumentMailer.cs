namespace ERP.Application.Common.Interfaces;

/// <summary>Belge e-postası gönderir (SMTP). Gönderilemezse 502 <c>mail_send_failed</c> AppException fırlatır.</summary>
public interface IDocumentMailer {
    Task SendAsync(IReadOnlyList<string>                                                to,
                   IReadOnlyList<string>?                                               cc,
                   string                                                               subject,
                   string                                                               htmlBody,
                   IReadOnlyList<(string FileName, byte[] Content, string ContentType)> attachments,
                   CancellationToken                                                    ct);
}
