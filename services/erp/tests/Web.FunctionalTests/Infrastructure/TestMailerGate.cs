using System.Collections.Concurrent;
using ERP.Application.Common.Exceptions;
using ERP.Application.Common.Interfaces;
using ERP.Infrastructure.Documents;
using Microsoft.Extensions.DependencyInjection;

namespace ERP.Web.FunctionalTests.Infrastructure;

/// <summary>
///     Testlerde gerçek (Mailpit) göndericinin önüne geçer: belirli alıcılar için gönderimi başarısız kılabilir.
///     Alıcıya göre çalıştığından aynı anda çalışan diğer testlerin postalarını etkilemez.
/// </summary>
public sealed class TestMailerGate(IServiceProvider services) : IDocumentMailer {
    private readonly ConcurrentDictionary<string, bool> _failing = new(StringComparer.OrdinalIgnoreCase);

    private readonly Lazy<SmtpDocumentMailer> _inner = new(() => ActivatorUtilities.CreateInstance<SmtpDocumentMailer>(services));

    public void FailFor(string address) => _failing[address] = true;

    public void Recover(string address) => _failing.TryRemove(address, out _);

    public Task SendAsync(IReadOnlyList<string> to, IReadOnlyList<string>? cc, string subject, string htmlBody,
                          IReadOnlyList<(string FileName, byte[] Content, string ContentType)> attachments, CancellationToken ct) {
        if (to.Any(a => _failing.ContainsKey(a))) throw new AppException(502, "mail_send_failed", "Test: posta sunucusu kapalı.");
        return _inner.Value.SendAsync(to, cc, subject, htmlBody, attachments, ct);
    }
}
