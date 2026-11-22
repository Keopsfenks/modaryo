namespace Modaryo.Internal;

/// <summary>
///     Giden isteği imzalar: X-Modaryo-Service, X-Modaryo-Timestamp ve X-Modaryo-Signature başlıklarını ekler.
///     İmza, isteğin X-Modaryo-Tenant başlığı varsa onun değeri üzerinden atılır. İstek adresi mutlak olmalıdır
///     (HttpClient BaseAddress ile çözümler).
/// </summary>
public sealed class InternalHttpClientHandler(
    string       serviceName,
    string       secret,
    TimeProvider? timeProvider = null
) : DelegatingHandler {
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) {
        var uri = request.RequestUri ?? throw new InvalidOperationException("İç API isteğinin adresi yok.");
        var pathAndQuery = uri.IsAbsoluteUri ? uri.PathAndQuery : uri.OriginalString;

        var tenant = request.Headers.TryGetValues(InternalHeaders.Tenant, out var values)
                         ? values.FirstOrDefault()
                         : null;
        var idempotencyKey = request.Headers.TryGetValues(InternalHeaders.IdempotencyKey, out var keys)
                                 ? keys.FirstOrDefault()
                                 : null;
        var body = request.Content is null ? [] : await request.Content.ReadAsByteArrayAsync(ct);
        var timestamp = _time.GetUtcNow().ToUnixTimeSeconds();

        request.Headers.Remove(InternalHeaders.Service);
        request.Headers.Remove(InternalHeaders.Timestamp);
        request.Headers.Remove(InternalHeaders.Signature);
        request.Headers.Add(InternalHeaders.Service,   serviceName);
        request.Headers.Add(InternalHeaders.Timestamp, timestamp.ToString(System.Globalization.CultureInfo.InvariantCulture));
        request.Headers.Add(InternalHeaders.Signature,
                            InternalSignature.Compute(secret, request.Method.Method, pathAndQuery, timestamp, tenant,
                                                      idempotencyKey, body));

        return await base.SendAsync(request, ct);
    }
}
