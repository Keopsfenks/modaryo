using System.Net;
using System.Text;

namespace IdentityService.Web.FunctionalTests.Infrastructure;

/// <summary>ERP şube kullanım ucunun yerine geçen sahte HTTP işleyicisi; varsayılan yanıt {"inUse":false}.</summary>
public sealed class FakeErpUsageHandler : HttpMessageHandler {
    public sealed record Call(string PathAndQuery, string? Tenant, bool HasSignature, string? Service);

    private readonly List<Call> _calls = [];

    public bool InUse { get; set; }

    /// <summary>true ise HttpRequestException fırlatır (ERP'ye ulaşılamıyor).</summary>
    public bool Unreachable { get; set; }

    public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;

    /// <summary>Doluysa yanıt gövdesi olarak aynen döner (bozuk gövde testleri için).</summary>
    public string? RawBody { get; set; }

    public IReadOnlyList<Call> Calls {
        get {
            lock (_calls) return _calls.ToList();
        }
    }

    public void Reset() {
        InUse       = false;
        Unreachable = false;
        Status      = HttpStatusCode.OK;
        RawBody     = null;
        lock (_calls) _calls.Clear();
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
        lock (_calls) {
            _calls.Add(new Call(request.RequestUri!.PathAndQuery,
                                request.Headers.TryGetValues("X-Modaryo-Tenant", out var t) ? t.First() : null,
                                request.Headers.Contains("X-Modaryo-Signature"),
                                request.Headers.TryGetValues("X-Modaryo-Service", out var s) ? s.First() : null));
        }

        if (Unreachable) throw new HttpRequestException("ERP'ye ulaşılamıyor.");

        var body = RawBody ?? $"{{\"inUse\":{(InUse ? "true" : "false")}}}";
        return Task.FromResult(new HttpResponseMessage(Status) {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        });
    }
}
