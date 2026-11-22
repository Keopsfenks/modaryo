using IdentityService.Application.Audit.Queries;
using IdentityService.Application.Branches;
using IdentityService.Application.Branches.Commands;
using IdentityService.Application.Branches.Queries;
using IdentityService.Application.Companies;
using IdentityService.Application.Companies.Commands;
using IdentityService.Application.Companies.Queries;
using IdentityService.Application.Invitations;
using IdentityService.Application.Invitations.Commands;
using IdentityService.Application.Invitations.Queries;
using IdentityService.Application.Modules.Commands;
using IdentityService.Application.Modules.Queries;
using IdentityService.Application.Roles;
using IdentityService.Application.Roles.Commands;
using IdentityService.Application.Roles.Queries;
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
        groupBuilder.MapPost(CreateRole, "current/roles");
        groupBuilder.MapGet(GetRole, "current/roles/{id:guid}");
        groupBuilder.MapPut(UpdateRole, "current/roles/{id:guid}");
        groupBuilder.MapDelete(DeleteRole, "current/roles/{id:guid}");
        groupBuilder.MapPost(ResetRole, "current/roles/{id:guid}/reset");
        groupBuilder.MapGet(GetMembers, "current/members");
        groupBuilder.MapPatch(ChangeMemberRole, "current/members/{userId:guid}");
        groupBuilder.MapDelete(RemoveMember, "current/members/{userId:guid}");
        groupBuilder.MapPut(SetMemberBranches, "current/members/{userId:guid}/branches");
        groupBuilder.MapGet(ListBranches, "current/branches");
        groupBuilder.MapPost(CreateBranch, "current/branches");
        groupBuilder.MapPut(UpdateBranch, "current/branches/{id:guid}");
        groupBuilder.MapDelete(DeleteBranch, "current/branches/{id:guid}");
        groupBuilder.MapGet(ListAudit, "current/audit");
        groupBuilder.MapGet(GetModules, "current/modules");
        groupBuilder.MapPut(SetModules, "current/modules");
        groupBuilder.MapGet(ListInvitations, "current/invitations");
        groupBuilder.MapPost(CreateInvitation, "current/invitations");
        groupBuilder.MapPost(ResendInvitation, "current/invitations/{id:guid}/resend");
        groupBuilder.MapDelete(RevokeInvitation, "current/invitations/{id:guid}");
    }

    [EndpointSummary("Denetim kaydı")]
    [EndpointDescription("denetim.read gerekir. Rol, modül, şube ve üye değişiklikleri önceki/sonraki değerleriyle, yeniden eskiye listelenir. pageSize varsayılan 50, en çok 200; entityType: role | modules | branch | member.")]
    public static async Task<Ok<AuditPageDto>> ListAudit(ISender sender, int? page, int? pageSize, string? entityType) =>
        TypedResults.Ok(await sender.Send(new ListAuditQuery { Page = page, PageSize = pageSize, EntityType = entityType }));

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
    [EndpointDescription("roller.read veya members.read gerekir. Sahip rolü Locked=true ve izinleri [\"*\"] olarak döner.")]
    public static async Task<Ok<IReadOnlyList<RoleDto>>> GetRoles(ISender sender) =>
        TypedResults.Ok(await sender.Send(new ListRolesQuery()));

    [EndpointSummary("Rol detayı")]
    public static async Task<Ok<RoleDto>> GetRole(ISender sender, Guid id) =>
        TypedResults.Ok(await sender.Send(new GetRoleQuery(id)));

    [EndpointSummary("Rol oluştur")]
    [EndpointDescription("copyFromRoleId verilirse izinler ve ayarlar kopyalanır, gövdedeki alanlar üzerine uygulanır. Çağıranın sahip olmadığı izin verilemez (403 permission_not_grantable); ad çakışması 409 role_name_taken.")]
    public static async Task<Created<RoleDto>> CreateRole(ISender sender, CreateRoleCommand command)
    {
        var role = await sender.Send(command);
        return TypedResults.Created($"/api/identity/companies/current/roles/{role.Id}", role);
    }

    public sealed record UpdateRoleRequest(string Name, string[]? Permissions, decimal? MaxDiscountPercent, bool? AllBranches, Guid[]? BranchIds);

    [EndpointSummary("Rolü güncelle")]
    [EndpointDescription("Sahip rolü değiştirilemez (409 role_locked). Çağıran yalnızca kendisinde olan izinleri ekleyebilir. Roldeki üyelerin açık oturumları hemen güncellenir.")]
    public static async Task<Ok<RoleDto>> UpdateRole(ISender sender, Guid id, UpdateRoleRequest request) =>
        TypedResults.Ok(await sender.Send(new UpdateRoleCommand
        {
            Id = id,
            Name = request.Name,
            Permissions = request.Permissions,
            MaxDiscountPercent = request.MaxDiscountPercent,
            AllBranches = request.AllBranches,
            BranchIds = request.BranchIds,
        }));

    [EndpointSummary("Rolü sil")]
    [EndpointDescription("Hazır roller silinemez (409 role_locked); üyesi veya bekleyen daveti olan rol silinemez (409 role_in_use).")]
    public static async Task<NoContent> DeleteRole(ISender sender, Guid id)
    {
        await sender.Send(new DeleteRoleCommand(id));
        return TypedResults.NoContent();
    }

    [EndpointSummary("Hazır rolü varsayılana döndür")]
    [EndpointDescription("Yalnızca Yönetici ve Kullanıcı rolleri; diğerleri 409 role_locked.")]
    public static async Task<Ok<RoleDto>> ResetRole(ISender sender, Guid id) =>
        TypedResults.Ok(await sender.Send(new ResetRoleCommand(id)));

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

    public sealed record SetMemberBranchesRequest(Guid[]? BranchIds);

    [EndpointSummary("Üyenin şube erişimini daralt")]
    [EndpointDescription("Rol kapsamının içinde daraltır; null daraltmayı kaldırır; boş liste reddedilir (400). Şubeler firmanın aktif şubeleri olmalı (400). Çağıran kendi kapsamı dışındaki şubeyi veremez (403 permission_not_grantable). Üyenin açık oturumları hemen güncellenir.")]
    public static async Task<NoContent> SetMemberBranches(ISender sender, Guid userId, SetMemberBranchesRequest request)
    {
        await sender.Send(new SetMemberBranchesCommand { UserId = userId, BranchIds = request.BranchIds });
        return TypedResults.NoContent();
    }

    [EndpointSummary("Firmanın şubeleri")]
    [EndpointDescription("Aktif şubeler önce, sonra koda göre. Her üye görebilir.")]
    public static async Task<Ok<IReadOnlyList<BranchDto>>> ListBranches(ISender sender) =>
        TypedResults.Ok(await sender.Send(new ListBranchesQuery()));

    public sealed record BranchRequest(string Code, string Name, bool IsActive = true);

    [EndpointSummary("Şube oluştur")]
    [EndpointDescription("ayarlar.subeler gerekir. Kod firmada benzersizdir ve büyük harfe çevrilir (409 branch_code_taken).")]
    public static async Task<Created<BranchDto>> CreateBranch(ISender sender, BranchRequest request)
    {
        var branch = await sender.Send(new CreateBranchCommand { Code = request.Code, Name = request.Name });
        return TypedResults.Created($"/api/identity/companies/current/branches/{branch.Id}", branch);
    }

    [EndpointSummary("Şubeyi güncelle")]
    [EndpointDescription("Varsayılan şube pasif yapılamaz (409 branch_is_default); kod çakışması 409 branch_code_taken.")]
    public static async Task<Ok<BranchDto>> UpdateBranch(ISender sender, Guid id, BranchRequest request) =>
        TypedResults.Ok(await sender.Send(new UpdateBranchCommand { Id = id, Code = request.Code, Name = request.Name, IsActive = request.IsActive }));

    [EndpointSummary("Şubeyi sil")]
    [EndpointDescription("Varsayılan şube silinemez (409 branch_is_default). Rollerdeki ve üyelerdeki şube kayıtları silinir, açık oturumlar yenilenir.")]
    public static async Task<NoContent> DeleteBranch(ISender sender, Guid id)
    {
        await sender.Send(new DeleteBranchCommand(id));
        return TypedResults.NoContent();
    }

    [EndpointSummary("Üyeyi firmadan çıkar")]
    public static async Task<NoContent> RemoveMember(ISender sender, Guid userId)
    {
        await sender.Send(new RemoveMemberCommand(userId));
        return TypedResults.NoContent();
    }
}
