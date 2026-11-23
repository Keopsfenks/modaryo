using System.Net.Http.Json;
using ERP.Application.Common.Interfaces;
using Microsoft.Extensions.Caching.Memory;
using Modaryo.Internal;

namespace ERP.Infrastructure.Identity;

/// <summary>
///     Kimlik servisinin şube listesini imzalı iç API ile okur (HttpClient, <see cref="InternalHttpClientHandler" />
///     ile kurulur). Liste firma başına 60 sn önbelleğe alınır; hata önbelleğe yazılmaz, çağırana istisna olarak iner.
/// </summary>
public sealed class IdentityBranchClient(
    HttpClient   http,
    IMemoryCache cache
) : IBranchDirectory {
    public static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(60);

    public async Task<IReadOnlyList<BranchRef>> ListAsync(Guid tenantId, CancellationToken ct) {
        var key = $"identity-branches:{tenantId:D}";
        if (cache.TryGetValue(key, out IReadOnlyList<BranchRef>? cached) && cached is not null) return cached;

        using var request = new HttpRequestMessage(HttpMethod.Get, $"/internal/tenants/{tenantId:D}/branches");
        request.Headers.Add(InternalHeaders.Tenant, tenantId.ToString("D"));

        using var response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        var list = await response.Content.ReadFromJsonAsync<List<BranchRef>>(ct)
                   ?? throw new HttpRequestException("Kimlik servisi şube listesi yerine boş yanıt döndü.");

        IReadOnlyList<BranchRef> result = list;
        cache.Set(key, result, CacheDuration);
        return result;
    }

    public async Task<bool> ExistsActiveAsync(Guid tenantId, Guid branchId, CancellationToken ct) {
        return (await ListAsync(tenantId, ct)).Any(b => b.Id == branchId && b.IsActive);
    }
}
