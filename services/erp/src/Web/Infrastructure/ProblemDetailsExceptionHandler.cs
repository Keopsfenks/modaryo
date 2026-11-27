using ERP.Application.Common.Exceptions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Web.Infrastructure;

/// <summary>
///     Converts well-known application exceptions into RFC 9110-compliant <see cref="ProblemDetails" /> responses,
///     mapping <see cref="ValidationException" /> → 400, <see cref="NotFoundException" /> → 404,
///     <see cref="UnauthorizedAccessException" /> → 401, and <see cref="ForbiddenAccessException" /> → 403.
///     Unrecognised exceptions are not handled and fall through to the default middleware.
/// </summary>
public class ProblemDetailsExceptionHandler : IExceptionHandler {
    public async ValueTask<bool> TryHandleAsync(HttpContext       httpContext,
                                                Exception         exception,
                                                CancellationToken cancellationToken
    ) {
        var (statusCode, problemDetails) = exception switch {
                                               AppException ae => (ae.Status, WithDetails(new ProblemDetails {
                                                                                  Status     = ae.Status,
                                                                                  Title      = ae.Message,
                                                                                  Extensions = { ["code"] = ae.Code }
                                                                              }, ae)),
                                               // Bozuk JSON / geçersiz UTF-8 gövde: istemci hatası, 500 değil.
                                               BadHttpRequestException bad => (bad.StatusCode, new ProblemDetails {
                                                                                   Status = bad.StatusCode,
                                                                                   Title
                                                                                       = "İstek okunamadı. Gönderilen verinin biçimini kontrol edin.",
                                                                                   Type
                                                                                       = "https://tools.ietf.org/html/rfc9110#section-15.5.1",
                                                                                   Extensions = { ["code"] = "bad_request" }
                                                                               }),
                                               ValidationException ve => (StatusCodes.Status400BadRequest,
                                                                          new ValidationProblemDetails(ve.Errors) {
                                                                              Status = StatusCodes.Status400BadRequest,
                                                                              Type
                                                                                  = "https://tools.ietf.org/html/rfc9110#section-15.5.1",
                                                                              Title      = "Formdaki hataları düzeltin.",
                                                                              Extensions = { ["code"] = "validation_failed" }
                                                                          }),
                                               NotFoundException ne => (StatusCodes.Status404NotFound,
                                                                        new ProblemDetails {
                                                                            Status = StatusCodes.Status404NotFound,
                                                                            Type
                                                                                = "https://tools.ietf.org/html/rfc9110#section-15.5.5",
                                                                            Title      = "Kayıt bulunamadı.",
                                                                            Detail     = ne.Message,
                                                                            Extensions = { ["code"] = "not_found" }
                                                                        }),
                                               UnauthorizedAccessException => (StatusCodes.Status401Unauthorized,
                                                                               new ProblemDetails {
                                                                                   Status = StatusCodes
                                                                                       .Status401Unauthorized,
                                                                                   Title = "Oturum açmanız gerekiyor.",
                                                                                   Type
                                                                                       = "https://tools.ietf.org/html/rfc9110#section-15.5.2",
                                                                                   Extensions = { ["code"] = "unauthorized" }
                                                                               }),
                                               ForbiddenAccessException => (StatusCodes.Status403Forbidden,
                                                                            new ProblemDetails {
                                                                                Status = StatusCodes.Status403Forbidden,
                                                                                Title  = "Bu işlem için yetkiniz yok.",
                                                                                Type
                                                                                    = "https://tools.ietf.org/html/rfc9110#section-15.5.4",
                                                                                Extensions = { ["code"] = "forbidden" }
                                                                            }),
                                               _ => (-1, null)
                                           };

        if (problemDetails is null) return false;

        httpContext.Response.StatusCode = statusCode;
        await httpContext.Response.WriteAsJsonAsync(problemDetails, problemDetails.GetType(), (System.Text.Json.JsonSerializerOptions?)null,
            "application/problem+json", cancellationToken);
        return true;
    }

    private static ProblemDetails WithDetails(ProblemDetails problem, AppException exception) {
        if (exception.Details is not null) problem.Extensions["details"] = exception.Details;
        return problem;
    }
}
