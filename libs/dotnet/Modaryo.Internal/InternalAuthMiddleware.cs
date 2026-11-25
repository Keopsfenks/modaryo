using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Modaryo.Sessions;

namespace Modaryo.Internal;

/// <summary>
///     "/internal/*" istekleri için: HMAC imzası + zaman aşımı doğrulaması, çağıranın yol izni, tenant başlığı
///     ve yazma isteklerinde idempotency. Doğrulanan istekte kimlik yalnızca servis adı ve tenant'tır
///     (kullanıcı, izin ve modül yoktur).
///     Gövde okunmadan önce gövdesiz kontroller (başlıklar, çağıran, sır, zaman sapması, gövde sınırı) yapılır.
/// </summary>
public sealed class InternalAuthMiddleware(
    RequestDelegate           next,
    IOptions<InternalOptions> options,
    TimeProvider              time
) {
    public const string AuthenticationType = "ModaryoInternal";
    public const string UnauthorizedCode   = "internal_unauthorized";
    public const string ForbiddenCode      = "internal_forbidden";
    public const string TenantInvalidCode  = "internal_tenant_invalid";
    public const string BodyTooLargeCode   = "internal_body_too_large";
    public const string KeyRequiredCode    = "idempotency_key_required";
    public const string ConflictCode       = "idempotency_conflict";
    public const string InProgressCode     = "idempotency_in_progress";
    public const string MethodNotAllowedCode = "internal_method_not_allowed";
    public const string PingPath           = "/internal/ping";

    private const int MaxIdempotencyKeyLength = 200;

    /// <summary>"/internal" ve altındaki yollar.</summary>
    public static bool IsInternalPath(PathString path) {
        return path.StartsWithSegments("/internal", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>İstek özeti: SHA-256(method \n pathAndQuery \n tenant \n body), küçük harfli onaltılık.</summary>
    public static string RequestHash(string method, string pathAndQuery, string tenant, ReadOnlySpan<byte> body) {
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        sha.AppendData(Encoding.UTF8.GetBytes($"{method}\n{pathAndQuery}\n{tenant}\n"));
        sha.AppendData(body);
        return Convert.ToHexStringLower(sha.GetHashAndReset());
    }

    public async Task InvokeAsync(HttpContext context) {
        if (!IsInternalPath(context.Request.Path)) {
            await next(context);
            return;
        }

        // Oturum kimliği iç yollarda geçerli sayılmaz; imza doğrulanana kadar anonim.
        context.User = new ClaimsPrincipal(new ClaimsIdentity());

        var request = context.Request;
        var opts    = options.Value;

        // Gövdesiz ön kontroller: hepsi aynı genel 401.
        var pre = PreAuthenticate(request, opts);
        if (pre is null) {
            await Unauthorized(context);
            return;
        }

        var (service, config, timestamp, signature) = pre.Value;

        // Gövde sınırı: imza henüz doğrulanmadı ama çağıran bilinen ve zaman damgası taze; yine de sınırlı oku.
        var max = opts.MaxBodyBytes;
        if (context.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } sizeFeature)
            sizeFeature.MaxRequestBodySize = max;

        if (request.ContentLength > max) {
            await BodyTooLarge(context);
            return;
        }

        var body = await ReadBodyAsync(request, max, context.RequestAborted);
        if (body is null) {
            await BodyTooLarge(context);
            return;
        }

        var pathAndQuery   = RawPathAndQuery(context);
        var tenantHeader   = request.Headers[InternalHeaders.Tenant].ToString();
        var idempotencyKey = request.Headers[InternalHeaders.IdempotencyKey].ToString();

        var valid = InternalSignature.Verify(config.Secret, request.Method, pathAndQuery, timestamp,
                                             tenantHeader.Length == 0 ? null : tenantHeader,
                                             idempotencyKey.Length == 0 ? null : idempotencyKey, body, signature);
        if (!valid) {
            await Unauthorized(context);
            return;
        }

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

        await RunIdempotentAsync(context, service, tenantId.Value, pathAndQuery, tenantHeader, idempotencyKey, body,
                                 TimeSpan.FromSeconds(opts.PendingTimeoutSeconds));
    }

    /// <summary>Başlıklar, bilinen çağıran, boş olmayan sır ve zaman sapması; gövde ve imza okunmaz.</summary>
    private (string Service, InternalCaller Config, long Timestamp, string Signature)? PreAuthenticate(
        HttpRequest     request,
        InternalOptions opts
    ) {
        var service      = request.Headers[InternalHeaders.Service].ToString();
        var timestampRaw = request.Headers[InternalHeaders.Timestamp].ToString();
        var signature    = request.Headers[InternalHeaders.Signature].ToString();

        if (service.Length == 0 || signature.Length == 0 ||
            !long.TryParse(timestampRaw, NumberStyles.None, CultureInfo.InvariantCulture, out var timestamp))
            return null;

        if (!opts.Callers.TryGetValue(service, out var config) || string.IsNullOrEmpty(config.Secret)) return null;

        if (Math.Abs(time.GetUtcNow().ToUnixTimeSeconds() - timestamp) > opts.MaxSkewSeconds) return null;

        return (service, config, timestamp, signature);
    }

    private async Task RunIdempotentAsync(HttpContext context,
                                          string      caller,
                                          Guid        tenantId,
                                          string      pathAndQuery,
                                          string      tenantHeader,
                                          string      key,
                                          byte[]      body,
                                          TimeSpan    pendingTimeout
    ) {
        // Idempotency deposu olmayan servis (kimlik) iç yazma isteği kabul etmez.
        var store = context.RequestServices.GetService<IInternalIdempotencyStore>();
        if (store is null) {
            context.Response.Headers.Allow = "GET, HEAD";
            await Problem(context, StatusCodes.Status405MethodNotAllowed, MethodNotAllowedCode, "Yöntem desteklenmiyor.",
                          "Bu servis iç API yazma isteklerini kabul etmez.");
            return;
        }

        if (key.Length == 0 || key.Length > MaxIdempotencyKeyLength) {
            await Problem(context, StatusCodes.Status400BadRequest, KeyRequiredCode, "Geçersiz istek.",
                          $"Yazma istekleri için '{InternalHeaders.IdempotencyKey}' başlığı zorunludur.");
            return;
        }

        var hash  = RequestHash(context.Request.Method, pathAndQuery, tenantHeader, body);
        var ct    = context.RequestAborted;

        var reservation = await store.ReserveAsync(caller, key, tenantId, hash, pendingTimeout, ct);
        switch (reservation.Outcome) {
            case IdempotencyOutcome.Conflict:
                await Problem(context, StatusCodes.Status409Conflict, ConflictCode, "Çakışma.",
                              "Bu idempotency anahtarı farklı bir istek için kullanılmış.");
                return;
            case IdempotencyOutcome.InProgress:
                context.Response.Headers.RetryAfter = "1";
                await Problem(context, StatusCodes.Status409Conflict, InProgressCode, "Çakışma.",
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

        var owner = reservation.OwnerToken;
        // İşleyiciler kaydı kendi işlemlerinde tamamlayabilsin (bkz. IIdempotencyContext).
        context.RequestServices.GetService<IdempotencyContext>()?.Begin(store, caller, key, owner);
        var original = context.Response.Body;
        await using var buffer = new MemoryStream();
        context.Response.Body = buffer;
        // Uç nokta < 500 ile döndüyse işlem uygulanmış olabilir: kayıt artık serbest bırakılmaz.
        var handled = false;
        try {
            await next(context);

            context.Response.Body = original;
            var status = context.Response.StatusCode;
            if (status < 500) {
                handled = true;
                var response = Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
                try {
                    await store.CompleteAsync(caller, key, owner, status, response, CancellationToken.None);
                }
                catch (Exception e) {
                    // Kayıt bekleyen kalır; zaman aşımından sonra devralınabilir. Yanıt yine de istemciye gider.
                    context.RequestServices.GetService<ILoggerFactory>()?.CreateLogger<InternalAuthMiddleware>()
                        .LogWarning(e, "İç API idempotency kaydı tamamlanamadı: {Caller}/{Key}", caller, key);
                }
            }

            buffer.Position = 0;
            await buffer.CopyToAsync(original, ct);
        }
        finally {
            context.Response.Body = original;
            // 5xx veya istisna: ayrılan kayıt silinir ki aynı anahtarla yeniden deneme çalışabilsin.
            if (!handled) await store.ReleaseAsync(caller, key, owner, CancellationToken.None);
        }
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

    /// <summary>Gövdeyi en çok <paramref name="max" /> bayt okur ve başa sarar; sınır aşılırsa null.</summary>
    private static async Task<byte[]?> ReadBodyAsync(HttpRequest request, long max, CancellationToken ct) {
        request.EnableBuffering();
        await using var memory = new MemoryStream();
        var             chunk  = new byte[16 * 1024];
        int             read;
        try {
            while ((read = await request.Body.ReadAsync(chunk, ct)) > 0) {
                if (memory.Length + read > max) return null;

                memory.Write(chunk, 0, read);
            }
        }
        catch (Microsoft.AspNetCore.Http.BadHttpRequestException e) when (e.StatusCode == StatusCodes.Status413PayloadTooLarge) {
            return null;
        }

        request.Body.Position = 0;
        return memory.ToArray();
    }

    private static Task Unauthorized(HttpContext context) {
        return Problem(context, StatusCodes.Status401Unauthorized, UnauthorizedCode, "Kimlik doğrulanamadı.",
                       "İç API isteği doğrulanamadı.");
    }

    private static Task BodyTooLarge(HttpContext context) {
        return Problem(context, StatusCodes.Status413PayloadTooLarge, BodyTooLargeCode, "İstek çok büyük.",
                       "İç API istek gövdesi izin verilen boyutu aşıyor.");
    }

    private static Task Problem(HttpContext context, int status, string code, string title, string detail) {
        return TypedResults.Problem(title: title, detail: detail, statusCode: status,
                                    extensions: new Dictionary<string, object?> { ["code"] = code })
            .ExecuteAsync(context);
    }
}
