using System.Collections.Concurrent;
using ERP.Application.Common.Interfaces;

namespace ERP.Web.FunctionalTests.Infrastructure;

/// <summary>Kimlik servisine gitmeyen, testin firma başına üye doldurduğu üye dizini.</summary>
public sealed class FakeMemberDirectory : IMemberDirectory {
    private readonly ConcurrentDictionary<(Guid Tenant, Guid User), MemberRef> _members = new();

    public MemberRef Seed(Guid tenantId, Guid userId, string fullName) {
        var member = new MemberRef(userId, $"{userId:N}@ornek.com", fullName);
        _members[(tenantId, userId)] = member;
        return member;
    }

    public Task<MemberRef?> GetAsync(Guid tenantId, Guid userId, CancellationToken ct) {
        return Task.FromResult(_members.GetValueOrDefault((tenantId, userId)));
    }
}
