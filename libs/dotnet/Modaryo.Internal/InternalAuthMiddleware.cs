using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Modaryo.Sessions;

namespace Modaryo.Internal;

/// <summary>
///     "/internal/*" istekleri için: HMAC imzası + zaman aşımı doğrulaması, çağıranın yol izni, tenant başlığı
///     ve yazma isteklerinde idempotency. Doğrulanan istekte kimlik yalnızca servis adı ve tenant'tır
///     (kullanıcı, izin ve modül yoktur).
/// </summary>
public sealed class InternalAuthMiddleware(
    RequestDelegate             next,
    IOptions<InternalOptions>   options,
    TimeProvider                time
) {
    public const string AuthenticationType = "ModaryoInternal";
    public const string UnauthorizedCode   = "internal_unauthorized";
    public const string ForbiddenCode      = "internal_forbidden";
    public const string TenantInvalidCode  = "internal_tenant_invalid";
    public const string KeyRequiredCode    = "idempotency_key_required";
    public const string ConflictCode       = "idempotency_conflict";
    public const string PingPath           = "/internal/ping";

    private const int MaxIdempotencyKeyLength = 200;

    /// <summary>"/internal" ve altındaki yollar.</summary>
    public static bool IsInternalPath(PathString path) {
        return path.StartsWithSegments("/internal", StringComparison.OrdinalIgnoreCase);
    }

    public async Task InvokeAsync(HttpContext context) {
        if (!InternalAuthMiddleware.IsInternalPath(context.Request.Path)) {
            await next(context);
            return;
        }

        // Oturum kimliği iç yollarda geçerli sayılmaz; imza doğrulanana kadar anonim.
        context.User = new ClaimsPrincipal(new ClaimsIdentity());

        var request      = context.Request;
        var pathAndQuery = RawPathAndQuery(context);

        var body = await ReadBodyAsync(request, context.RequestAborted);

        var tenantHeader = request.Headers[InternalHeaders.Tenant].ToString();
        var caller       = Authenticate(request, pathAndQuery, tenantHeader, body);
        if (caller is null) {
            await Problem(context, StatusCodes.Status401Unauthorized, UnauthorizedCode, "Kimlik doğrulanamadı.",
                          "İç API isteği doğrulanamadı.");
            return;
        }

        var (service, config) = caller.Value;
        if (!IsRouteAllowed(config, request.Path)) {
            await Problem(context, StatusCodes.Status403Forbidden, ForbiddenCode, "İstek reddedildi.",
                          "Bu servis bu yola erişemez.");
            return;
        }

        Guid? tenantId = null;
        if (tenantHeader.Length > 0) {
            if (!Guid.TryParse(tenantHeader, out var parsed) || parsed == Guid.Empty) {
                await Problem(context, StatusCodes.Status400BadRequest, TenantInvalidCode, "Geçersiz istek.",
                              $"'{InternalHeaders.Tenant}' başlığı geçerli bir firma kimliği olmalıdır.");
                return;
            }

            tenantId = parsed;
        }
        else if (!IsPing(request.Path)) {
            await Problem(context, StatusCodes.Status400BadRequest, TenantInvalidCode, "Geçersiz istek.",
                          $"'{InternalHeaders.Tenant}' başlığı zorunludur.");
            return;
        }

        var claims = new List<Claim> { new(InternalClaimTypes.Service, service) };
        if (tenantId is { } t) claims.Add(new Claim(SessionClaimTypes.TenantId, t.ToString("D")));
        context.User = new ClaimsPrincipal(new ClaimsIdentity(claims, AuthenticationType));

        if (IsSafeMethod(request.Method) || tenantId is null) {
            await next(context);
            return;
        }

        await RunIdempotentAsync(context, service, tenantId.Value, pathAndQuery, tenantHeader, body);
    }

    private (string Service, InternalCaller Config)? Authenticate(HttpRequest request,
                                                                  string      pathAndQuery,
                                                                  string      tenant,
                                                                  byte[]      body
    ) {
        var service      = request.Headers[InternalHeaders.Service].ToString();
        var timestampRaw = request.Headers[InternalHeaders.Timestamp].ToString();
        var signature    = request.Headers[InternalHeaders.Signature].ToString();

        if (service.Length == 0 || signature.Length == 0 ||
            !long.TryParse(timestampRaw, NumberStyles.None, CultureInfo.InvariantCulture, out var timestamp))
            return null;

        var opts = options.Value;
        if (!opts.Callers.TryGetValue(service, out var config) || string.IsNullOrEmpty(config.Secret)) return null;

        if (Math.Abs(time.GetUtcNow().ToUnixTimeSeconds() - timestamp) > opts.MaxSkewSeconds) return null;

        var valid = InternalSignature.Verify(config.Secret, request.Method, pathAndQuery, timestamp,
                                             tenant.Length == 0 ? null : tenant, body, signature);
        return valid ? (service, config) : null;
    }

    private async Task RunIdempotentAsync(HttpContext context,
                                          string      caller,
                                          Guid        tenantId,
                                          string      pathAndQuery,
                                          string      tenantHeader,
                                          byte[]      body
    ) {
        var key = context.Request.Headers[InternalHeaders.IdempotencyKey].ToString();
        if (key.Length == 0 || key.Length > MaxIdempotencyKeyLength) {
            await Problem(context, StatusCodes.Status400BadRequest, KeyRequiredCode, "Geçersiz istek.",
                          $"Yazma istekleri için '{InternalHeaders.IdempotencyKey}' başlığı zorunludur.");
            return;
        }

        var store = context.RequestServices.GetRequiredService<IInternalIdempotencyStore>();
        var hash  = RequestHash(context.Request.Method, pathAndQuery, tenantHeader, body);
        var ct    = context.RequestAborted;

        var reservation = await store.ReserveAsync(caller, key, tenantId, hash, ct);
        switch (reservation.Outcome) {
            case IdempotencyOutcome.Conflict:
                await Problem(context, StatusCodes.Status409Conflict, ConflictCode, "Çakışma.",
                              "Bu idempotency anahtarı farklı bir istek için kullanılmış.");
                return;
            case IdempotencyOutcome.InProgress:
                await Problem(context, StatusCodes.Status409Conflict, ConflictCode, "Çakışma.",
                              "Aynı idempotency anahtarlı istek hâlâ işleniyor.");
                return;
            case IdempotencyOutcome.Replay:
                context.Response.StatusCode = reservation.Status;
                if (!string.IsNullOrEmpty(reservation.ResponseJson)) {
                    context.Response.ContentType = "application/json; charset=utf-8";
                    await context.Response.WriteAsync(reservation.ResponseJson, Encoding.UTF8);
                }

                return;
        }

        var original = context.Response.Body;
        var buffer   = new MemoryStream();
        context.Response.Body = buffer;
        var completed = false;
        try {
            await next(context);

            context.Response.Body = original;
            var status = context.Response.StatusCode;
            if (status < 500) {
                var response = Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
                await store.CompleteAsync(caller, key, status, response, CancellationToken.None);
                completed = true;
            }

            buffer.Position = 0;
            await buffer.CopyToAsync(original, ct);
        }
        finally {
            context.Response.Body = original;
            // 5xx veya istisna: ayrılan kayıt silinir ki aynı anahtarla yeniden deneme çalışabilsin.
            if (!completed) await store.ReleaseAsync(caller, key, CancellationToken.None);
        }
    }

    private static string RequestHash(string method, string pathAndQuery, string tenant, byte[] body) {
        var head = Encoding.UTF8.GetBytes($"{method}\n{pathAndQuery}\n{tenant}\n");
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        sha.AppendData(head);
        sha.AppendData(body);
        return Convert.ToHexStringLower(sha.GetHashAndReset());
    }

    private static bool IsRouteAllowed(InternalCaller caller, PathString path) {
        var value = path.Value ?? "";
        foreach (var route in caller.Routes) {
            if (string.IsNullOrEmpty(route)) continue;

            var prefix = route.TrimEnd('/');
            if (value.Equals(prefix, StringComparison.OrdinalIgnoreCase) ||
                value.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static bool IsPing(PathString path) {
        return path.Value?.TrimEnd('/').Equals(PingPath, StringComparison.OrdinalIgnoreCase) == true;
    }

    private static bool IsSafeMethod(string method) {
        return HttpMethods.IsGet(method) || HttpMethods.IsHead(method);
    }

    /// <summary>İstemcinin gönderdiği ham yol + sorgu (imza bunun üzerinden atılır).</summary>
    private static string RawPathAndQuery(HttpContext context) {
        var raw = context.Features.Get<IHttpRequestFeature>()?.RawTarget;
        if (!string.IsNullOrEmpty(raw) && raw.StartsWith('/')) return raw;

        return context.Request.PathBase.ToUriComponent() + context.Request.Path.ToUriComponent() +
               context.Request.QueryString.ToUriComponent();
    }

    private static async Task<byte[]> ReadBodyAsync(HttpRequest request, CancellationToken ct) {
        request.EnableBuffering();
        using var memory = new MemoryStream();
        await request.Body.CopyToAsync(memory, ct);
        request.Body.Position = 0;
        return memory.ToArray();
    }

    private static Task Problem(HttpContext context, int status, string code, string title, string detail) {
        return TypedResults.Problem(title: title, detail: detail, statusCode: status,
                                    extensions: new Dictionary<string, object?> { ["code"] = code })
            .ExecuteAsync(context);
    }
}
