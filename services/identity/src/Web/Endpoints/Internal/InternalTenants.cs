using System.Security.Claims;
using IdentityService.Application.Branches;
using IdentityService.Application.Common.Interfaces;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Modaryo.Internal;
using Modaryo.Sessions;

namespace IdentityService.Web.Endpoints.Internal;

/// <summary>Servisten servise (ERP) firma verisi okuma. Yalnızca imzalı çağrılar (Modaryo.Internal); oturum aranmaz.</summary>
public class InternalTenants : IEndpointGroup {
    public static string? RoutePrefix => "/internal/tenants";

    public static void Map(RouteGroupBuilder groupBuilder) {
        groupBuilder.ExcludeFromDescription();
        groupBuilder.MapGet(ListTenantBranches, "{tenantId:guid}/branches");
        groupBuilder.MapGet(GetTenantMember, "{tenantId:guid}/members/{userId:guid}");
    }

    [EndpointSummary("Firmanın şubeleri (iç API)")]
    [EndpointDescription("Aktif ve pasif tüm şubeler. Yoldaki firma, imzalı X-Modaryo-Tenant başlığıyla aynı olmalıdır; değilse 403 internal_forbidden.")]
    public static async Task<Results<Ok<IReadOnlyList<BranchDto>>, ProblemHttpResult>> ListTenantBranches(
        ClaimsPrincipal         user,
        Guid                    tenantId,
        IApplicationDbContext   db,
        CancellationToken       ct
    ) {
        if (user.GetTenantId() != tenantId) {
            return TypedResults.Problem(title: "İstek reddedildi.", detail: "Yoldaki firma imzalı firma başlığıyla uyuşmuyor.",
                                        statusCode: StatusCodes.Status403Forbidden,
                                        extensions: new Dictionary<string, object?> { ["code"] = InternalAuthMiddleware.ForbiddenCode });
        }

        // Oturum firma filtresi yok; firma açıkça süzülür.
        var branches = await db.Branches.AsNoTracking().Where(b => b.TenantId == tenantId).ToListAsync(ct);
        IReadOnlyList<BranchDto> list = branches
            .OrderByDescending(b => b.IsActive)
            .ThenBy(b => b.Code, StringComparer.Ordinal)
            .Select(BranchRules.ToDto)
            .ToList();
        return TypedResults.Ok(list);
    }

    /// <summary>ERP'nin satış temsilcisi doğrulaması için bir firma üyesinin özeti.</summary>
    public sealed record InternalMemberDto(Guid UserId, string Email, string FullName);

    [EndpointSummary("Firma üyesi (iç API)")]
    [EndpointDescription("Firmanın üyesi değilse 404. Yoldaki firma, imzalı X-Modaryo-Tenant başlığıyla aynı olmalıdır; değilse 403 internal_forbidden.")]
    public static async Task<Results<Ok<InternalMemberDto>, NotFound, ProblemHttpResult>> GetTenantMember(
        ClaimsPrincipal         user,
        Guid                    tenantId,
        Guid                    userId,
        IApplicationDbContext   db,
        IUserAccounts           users,
        CancellationToken       ct
    ) {
        if (user.GetTenantId() != tenantId) {
            return TypedResults.Problem(title: "İstek reddedildi.", detail: "Yoldaki firma imzalı firma başlığıyla uyuşmuyor.",
                                        statusCode: StatusCodes.Status403Forbidden,
                                        extensions: new Dictionary<string, object?> { ["code"] = InternalAuthMiddleware.ForbiddenCode });
        }

        var isMember = await db.Memberships.AsNoTracking().AnyAsync(m => m.TenantId == tenantId && m.UserId == userId, ct);
        if (!isMember) return TypedResults.NotFound();
        var people = await users.FindManyAsync([userId], ct);
        return people.TryGetValue(userId, out var person)
                   ? TypedResults.Ok(new InternalMemberDto(userId, person.Email, person.FullName))
                   : TypedResults.NotFound();
    }
}
