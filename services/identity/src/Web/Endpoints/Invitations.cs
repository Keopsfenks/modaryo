using IdentityService.Application.Auth;
using IdentityService.Application.Invitations;
using IdentityService.Application.Invitations.Commands;
using IdentityService.Application.Invitations.Queries;
using Microsoft.AspNetCore.Http.HttpResults;

namespace IdentityService.Web.Endpoints;

/// <summary>Davet linki (/davet/{token}) uç noktaları.</summary>
public class Invitations : IEndpointGroup
{
    public static string? RoutePrefix => "/api/identity/invitations";

    public static void Map(RouteGroupBuilder groupBuilder)
    {
        groupBuilder.MapGet(GetInvitationPreview, "{token}");
        groupBuilder.MapPost(AcceptInvitation, "{token}/accept");
    }

    [EndpointSummary("Davet önizlemesi")]
    [EndpointDescription("Oturum gerektirmez. Geçersiz, kullanılmış, iptal edilmiş veya süresi dolmuş token: 404.")]
    public static async Task<Ok<InvitationPreview>> GetInvitationPreview(ISender sender, string token) =>
        TypedResults.Ok(await sender.Send(new GetInvitationPreviewQuery(token)));

    [EndpointSummary("Daveti kabul et")]
    [EndpointDescription("Oturumdaki e-posta davet edilen e-postayla aynı olmalı (aksi halde 403 invitation_email_mismatch). Kabul edilen firma aktif firma olur.")]
    public static async Task<Ok<SessionInfo>> AcceptInvitation(ISender sender, string token) =>
        TypedResults.Ok(await sender.Send(new AcceptInvitationCommand(token)));
}
