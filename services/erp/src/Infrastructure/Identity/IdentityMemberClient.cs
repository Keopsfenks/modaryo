using System.Net;
using System.Net.Http.Json;
using ERP.Application.Common.Interfaces;
using Microsoft.Extensions.Caching.Memory;
using Modaryo.Internal;

namespace ERP.Infrastructure.Identity;

/// <summary>Kimlik servisinin firma üyesi ucunu imzalı iç API ile okur; sonuç (üye değil dahil) firma x kullanıcı başına 60 sn önbelleğe alınır.</summary>
public sealed class IdentityMemberClient(
    HttpClient   http,
    IMemoryCache cache
) : IMemberDirectory {
    public static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(60);

    private sealed record Box(MemberRef? Member);

    public async Task<MemberRef?> GetAsync(Guid tenantId, Guid userId, CancellationToken ct) {
        var key = $"identity-member:{tenantId:D}:{userId:D}";
        if (cache.TryGetValue(key, out Box? cached) && cached is not null) return cached.Member;

        using var request = new HttpRequestMessage(HttpMethod.Get, $"/internal/tenants/{tenantId:D}/members/{userId:D}");
        request.Headers.Add(InternalHeaders.Tenant, tenantId.ToString("D"));

        using var response = await http.SendAsync(request, ct);
        MemberRef? member = null;
        if (response.StatusCode != HttpStatusCode.NotFound) {
            response.EnsureSuccessStatusCode();
            member = await response.Content.ReadFromJsonAsync<MemberRef>(ct)
                     ?? throw new HttpRequestException("Kimlik servisi üye yerine boş yanıt döndü.");
        }

        cache.Set(key, new Box(member), CacheDuration);
        return member;
    }
}
