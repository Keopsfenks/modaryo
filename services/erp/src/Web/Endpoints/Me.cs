using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Modaryo.Sessions;

namespace ERP.Web.Endpoints;

public sealed record MeDto(
    Guid                  UserId,
    string                Email,
    string                FullName,
    Guid?                 TenantId,
    IReadOnlyList<string> Permissions
);

/// <summary>
///     Oturum sözleşmesinin doğrulaması: kimlik servisinin Valkey'e yazdığı oturumu ERP'nin nasıl gördüğünü döndürür.
/// </summary>
public class Me : IEndpointGroup {
    public static string? RoutePrefix => "/api/erp";

    public static void Map(RouteGroupBuilder groupBuilder) {
        groupBuilder.MapGet(GetMe, "me").RequireAuthorization();
    }

    [EndpointSummary("Oturumdaki kullanıcı")]
    [EndpointDescription("Oturum yoksa, silinmişse veya sözleşme sürümü bilinmiyorsa 401.")]
    public static Ok<MeDto> GetMe(ClaimsPrincipal user) {
        return TypedResults.Ok(new MeDto(
                                         user.GetUserId()!.Value,
                                         user.FindFirstValue(ClaimTypes.Email) ?? string.Empty,
                                         user.FindFirstValue(ClaimTypes.Name)  ?? string.Empty,
                                         user.GetTenantId(),
                                         user.GetPermissions()));
    }
}