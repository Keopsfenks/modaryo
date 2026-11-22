using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using IdentityService.Application.Common.Interfaces;

namespace IdentityService.Web.FunctionalTests.Infrastructure;

/// <summary>Gönderilen e-postaları bellekte toplar.</summary>
public sealed partial class FakeEmailSender : IEmailSender
{
    private readonly ConcurrentQueue<EmailMessage> _sent = new();

    public IReadOnlyList<EmailMessage> Sent => _sent.ToArray();

    public Task SendAsync(EmailMessage message, CancellationToken ct)
    {
        _sent.Enqueue(message);
        return Task.CompletedTask;
    }

    public void Clear() => _sent.Clear();

    public IReadOnlyList<EmailMessage> To(string email) =>
        Sent.Where(m => string.Equals(m.To, email, StringComparison.OrdinalIgnoreCase)).ToArray();

    public EmailMessage LastTo(string email) => To(email).Last();

    /// <summary>Düz metin gövdedeki ilk http(s) linki.</summary>
    public static string ExtractLink(EmailMessage message) => LinkPattern().Match(message.TextBody).Value;

    /// <summary>Linkteki sorgu parametresini (URL-decode edilmiş) döndürür.</summary>
    public static string QueryValue(string link, string name)
    {
        var query = new Uri(link).Query.TrimStart('?').Split('&')
            .Select(p => p.Split('=', 2))
            .ToDictionary(p => p[0], p => Uri.UnescapeDataString(p[1]));
        return query[name];
    }

    [GeneratedRegex(@"https?://\S+")]
    private static partial Regex LinkPattern();
}
