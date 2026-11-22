using Microsoft.AspNetCore.Http;

namespace Modaryo.Sessions;

/// <summary>
///     GET/HEAD/OPTIONS/TRACE dışındaki isteklerde "X-Modaryo-Csrf: 1" başlığını zorunlu kılar (spec 6.8).
///     Servisler CORS'a izin vermediği için başka bir site bu başlığı gönderemez.
/// </summary>
public sealed class CsrfHeaderMiddleware(
    RequestDelegate next
) {
    public const string ErrorCode = "csrf_header_missing";

    public async Task InvokeAsync(HttpContext context) {
        if (IsSafe(context.Request.Method) || context.Request.Headers[SessionKeys.CsrfHeader] == "1") {
            await next(context);
            return;
        }

        var problem = TypedResults.Problem(
                                           title: "İstek reddedildi.",
                                           detail: $"Bu istek için '{SessionKeys.CsrfHeader}: 1' başlığı gereklidir.",
                                           statusCode: StatusCodes.Status403Forbidden,
                                           extensions: new Dictionary<string, object?> { ["code"] = ErrorCode });

        await problem.ExecuteAsync(context);
    }

    private static bool IsSafe(string method) {
        return HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method) ||
               HttpMethods.IsTrace(method);
    }
}