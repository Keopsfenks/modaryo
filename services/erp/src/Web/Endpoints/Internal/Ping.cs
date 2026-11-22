using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Modaryo.Internal;
using Modaryo.Sessions;

namespace ERP.Web.Endpoints.Internal;

public sealed record PingDto(
    string? Service,
    Guid?   Tenant
);

/// <summary>İç API sağlık ve imza doğrulaması: çağıran servis ve firma (başlıktan) döner.</summary>
public class InternalPing : IEndpointGroup {
    public static string? RoutePrefix => "/internal/ping";

    public static void Map(RouteGroupBuilder groupBuilder) {
        groupBuilder.ExcludeFromDescription();
        groupBuilder.MapGet(Ping);
    }

    [EndpointSummary("İç API ping")]
    [EndpointDescription("Yalnızca imzalı servis çağrıları (X-Modaryo-* başlıkları). Firma başlığı isteğe bağlıdır.")]
    public static Ok<PingDto> Ping(ClaimsPrincipal user) {
        return TypedResults.Ok(new PingDto(user.FindFirstValue(InternalClaimTypes.Service), user.GetTenantId()));
    }
}
