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
}
