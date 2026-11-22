using IdentityService.Application.Companies;
using IdentityService.Application.Companies.Commands;
using IdentityService.Application.Companies.Queries;
using IdentityService.Application.Invitations;
using IdentityService.Application.Invitations.Commands;
using IdentityService.Application.Invitations.Queries;
using IdentityService.Application.Modules.Commands;
using IdentityService.Application.Modules.Queries;
using Microsoft.AspNetCore.Http.HttpResults;

namespace IdentityService.Web.Endpoints;

public class Companies : IEndpointGroup
{
    public static string? RoutePrefix => "/api/identity/companies";

    public static void Map(RouteGroupBuilder groupBuilder)
    {
        groupBuilder.MapPost(CreateCompany);
        groupBuilder.MapGet(GetCurrentCompany, "current");
        groupBuilder.MapPatch(UpdateCompany, "current");
        groupBuilder.MapPost(LeaveCompany, "current/leave");
        groupBuilder.MapGet(GetRoles, "current/roles");
        groupBuilder.MapGet(GetMembers, "current/members");
        groupBuilder.MapPatch(ChangeMemberRole, "current/members/{userId:guid}");
        groupBuilder.MapDelete(RemoveMember, "current/members/{userId:guid}");
        groupBuilder.MapGet(GetModules, "current/modules");
        groupBuilder.MapPut(SetModules, "current/modules");
        groupBuilder.MapGet(ListInvitations, "current/invitations");
        groupBuilder.MapPost(CreateInvitation, "current/invitations");
        groupBuilder.MapPost(ResendInvitation, "current/invitations/{id:guid}/resend");
        groupBuilder.MapDelete(RevokeInvitation, "current/invitations/{id:guid}");
    }

    [EndpointSummary("Firmanın modülleri")]
    [EndpointDescription("Tüm modül kataloğu; açık olanlar enabled=true ile işaretlidir.")]
    public static async Task<Ok<IReadOnlyList<ModuleDto>>> GetModules(ISender sender) =>
        TypedResults.Ok(await sender.Send(new GetModulesQuery()));

    public sealed record SetModulesRequest(string[] Enabled);

    [EndpointSummary("Modülleri aç/kapat")]
    [EndpointDescription("Gövdedeki liste açık modüllerin tamamıdır. Bilinmeyen anahtar 400 unknown_module. Üyelerin açık oturumları hemen güncellenir.")]
    public static async Task<Ok<IReadOnlyList<ModuleDto>>> SetModules(ISender sender, SetModulesRequest request) =>
        TypedResults.Ok(await sender.Send(new SetModulesCommand { Enabled = request.Enabled }));

    [EndpointSummary("Bekleyen davetler")]
    public static async Task<Ok<IReadOnlyList<InvitationDto>>> ListInvitations(ISender sender) =>
        TypedResults.Ok(await sender.Send(new ListInvitationsQuery()));

    [EndpointSummary("Davet gönder")]
    [EndpointDescription("Aynı e-postaya bekleyen davet varsa yenilenir. Zaten üyeyse 409 already_member. Sahip rolüyle davet yalnızca sahibe açıktır.")]
    public static async Task<Created<InvitationDto>> CreateInvitation(ISender sender, CreateInvitationCommand command)
    {
        var invitation = await sender.Send(command);
        return TypedResults.Created($"/api/identity/companies/current/invitations/{invitation.Id}", invitation);
    }

    [EndpointSummary("Daveti yeniden gönder")]
    [EndpointDescription("Yeni token üretir; eski link geçersiz olur ve süre 7 gün olarak yeniden başlar.")]
    public static async Task<NoContent> ResendInvitation(ISender sender, Guid id)
    {
        await sender.Send(new ResendInvitationCommand(id));
        return TypedResults.NoContent();
    }

    [EndpointSummary("Daveti iptal et")]
    public static async Task<NoContent> RevokeInvitation(ISender sender, Guid id)
    {
        await sender.Send(new RevokeInvitationCommand(id));
        return TypedResults.NoContent();
    }

    [EndpointSummary("Firma oluştur")]
    [EndpointDescription("Firma ve hazır roller oluşur; çağıran sahip olur ve oturumun aktif firması bu firma olur.")]
    public static async Task<Created<CompanyDto>> CreateCompany(ISender sender, CreateCompanyCommand command)
    {
        var company = await sender.Send(command);
        return TypedResults.Created($"/api/identity/companies/current", company);
    }

    [EndpointSummary("Aktif firma")]
    public static async Task<Ok<CompanyDto>> GetCurrentCompany(ISender sender) =>
        TypedResults.Ok(await sender.Send(new GetCurrentCompanyQuery()));

    [EndpointSummary("Firmayı güncelle")]
    public static async Task<NoContent> UpdateCompany(ISender sender, UpdateCompanyCommand command)
    {
        await sender.Send(command);
        return TypedResults.NoContent();
    }

    [EndpointSummary("Firmadan ayrıl")]
    [EndpointDescription("Son sahip ayrılamaz (409 last_owner).")]
    public static async Task<NoContent> LeaveCompany(ISender sender)
    {
        await sender.Send(new LeaveCompanyCommand());
        return TypedResults.NoContent();
    }

    [EndpointSummary("Firmanın rolleri")]
    public static async Task<Ok<IReadOnlyList<RoleDto>>> GetRoles(ISender sender) =>
        TypedResults.Ok(await sender.Send(new GetRolesQuery()));

    [EndpointSummary("Firmanın üyeleri")]
    public static async Task<Ok<IReadOnlyList<MemberDto>>> GetMembers(ISender sender) =>
        TypedResults.Ok(await sender.Send(new GetMembersQuery()));

    public sealed record ChangeRoleRequest(Guid RoleId);

    [EndpointSummary("Üyenin rolünü değiştir")]
    [EndpointDescription("Sahip rolünü yalnızca sahip verebilir/alabilir. Son sahip düşürülemez (409 last_owner).")]
    public static async Task<NoContent> ChangeMemberRole(ISender sender, Guid userId, ChangeRoleRequest request)
    {
        await sender.Send(new ChangeMemberRoleCommand { UserId = userId, RoleId = request.RoleId });
        return TypedResults.NoContent();
    }

    [EndpointSummary("Üyeyi firmadan çıkar")]
    public static async Task<NoContent> RemoveMember(ISender sender, Guid userId)
    {
        await sender.Send(new RemoveMemberCommand(userId));
        return TypedResults.NoContent();
    }
}
