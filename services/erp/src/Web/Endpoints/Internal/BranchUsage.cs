using System.Security.Claims;
using ERP.Application.Common.Interfaces;
using Microsoft.AspNetCore.Http.HttpResults;
using Modaryo.Sessions;

namespace ERP.Web.Endpoints.Internal;

public sealed record BranchUsageDto(
    bool InUse
);

/// <summary>Kimlik servisi şube silmeden önce şubenin ERP kayıtlarında kullanılıp kullanılmadığını sorar.</summary>
public class InternalBranches : IEndpointGroup {
    public static string? RoutePrefix => "/internal/branches";

    public static void Map(RouteGroupBuilder groupBuilder) {
        groupBuilder.ExcludeFromDescription();
        groupBuilder.MapGet(GetUsage, "{branchId:guid}/usage");
    }

    [EndpointSummary("Şube kullanım durumu")]
    [EndpointDescription("Yalnızca imzalı servis çağrıları. Firma X-Modaryo-Tenant başlığından gelir; kayıtlı herhangi bir kullanım sağlayıcısı true derse inUse=true.")]
    public static async Task<Ok<BranchUsageDto>> GetUsage(ClaimsPrincipal                  user,
                                                          Guid                             branchId,
                                                          IEnumerable<IBranchUsageProvider> providers,
                                                          CancellationToken                ct
    ) {
        // Ara katman firma başlığını doğrulamıştır (ping dışında zorunlu).
        var tenantId = user.GetTenantId() ?? throw new InvalidOperationException("İç istekte firma kimliği yok.");
        foreach (var provider in providers) {
            if (await provider.IsInUseAsync(tenantId, branchId, ct)) return TypedResults.Ok(new BranchUsageDto(true));
        }

        return TypedResults.Ok(new BranchUsageDto(false));
    }
}
