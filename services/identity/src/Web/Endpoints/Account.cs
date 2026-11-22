using IdentityService.Application.Account.Commands;
using IdentityService.Application.Account.Queries;
using IdentityService.Application.Auth;
using IdentityService.Application.Invitations;
using IdentityService.Application.Invitations.Commands;
using IdentityService.Application.Invitations.Queries;
using Microsoft.AspNetCore.Http.HttpResults;

namespace IdentityService.Web.Endpoints;

public class Account : IEndpointGroup
{
    public static string? RoutePrefix => "/api/identity/account";

    public static void Map(RouteGroupBuilder groupBuilder)
    {
        groupBuilder.MapPost(ChangePassword, "change-password");
        groupBuilder.MapPatch(UpdateProfile, "profile");
        groupBuilder.MapGet(GetSessions, "sessions");
        groupBuilder.MapDelete(RevokeSession, "sessions/{id}");
        groupBuilder.MapGet(GetMyInvitations, "invitations");
        groupBuilder.MapPost(AcceptMyInvitation, "invitations/{id:guid}/accept");
    }

    [EndpointSummary("Bana gelen davetler")]
    [EndpointDescription("Oturumdaki e-postaya gelmiş bekleyen davetler (başlangıç ekranı).")]
    public static async Task<Ok<IReadOnlyList<MyInvitationDto>>> GetMyInvitations(ISender sender) =>
        TypedResults.Ok(await sender.Send(new GetMyInvitationsQuery()));

    [EndpointSummary("Bana gelen daveti kabul et")]
    [EndpointDescription("Başka birine ait veya bekleyen olmayan davet: 404.")]
    public static async Task<Ok<SessionInfo>> AcceptMyInvitation(ISender sender, Guid id) =>
        TypedResults.Ok(await sender.Send(new AcceptMyInvitationCommand(id)));

    [EndpointSummary("Şifre değiştir")]
    [EndpointDescription("Mevcut şifre doğruysa değiştirir; bu oturum dışındaki tüm oturumlar kapanır.")]
    public static async Task<NoContent> ChangePassword(ISender sender, ChangePasswordCommand command)
    {
        await sender.Send(command);
        return TypedResults.NoContent();
    }

    [EndpointSummary("Profili güncelle")]
    public static async Task<NoContent> UpdateProfile(ISender sender, UpdateProfileCommand command)
    {
        await sender.Send(command);
        return TypedResults.NoContent();
    }

    [EndpointSummary("Aktif oturumlarım")]
    public static async Task<Ok<IReadOnlyList<SessionListItem>>> GetSessions(ISender sender) =>
        TypedResults.Ok(await sender.Send(new GetSessionsQuery()));

    [EndpointSummary("Oturumu kapat")]
    [EndpointDescription("Kullanıcının kendi oturumlarından birini kapatır. Başkasının oturumu veya bilinmeyen kimlik: 404.")]
    public static async Task<NoContent> RevokeSession(ISender sender, string id)
    {
        await sender.Send(new RevokeSessionCommand(id));
        return TypedResults.NoContent();
    }
}
