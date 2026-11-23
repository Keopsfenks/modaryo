using System.Collections.Concurrent;
using ERP.Application.Common.Interfaces;

namespace ERP.Web.FunctionalTests.Infrastructure;

/// <summary>Kimlik servisine gitmeyen, testin firma başına şube doldurduğu şube dizini.</summary>
public sealed class FakeBranchDirectory : IBranchDirectory {
    private readonly ConcurrentDictionary<Guid, List<BranchRef>> _branches = new();

    public void Seed(Guid tenantId, params BranchRef[] branches) {
        _branches[tenantId] = branches.ToList();
    }

    public void Clear() {
        _branches.Clear();
    }

    public Task<IReadOnlyList<BranchRef>> ListAsync(Guid tenantId, CancellationToken ct) {
        IReadOnlyList<BranchRef> list = _branches.TryGetValue(tenantId, out var found) ? found.ToList() : [];
        return Task.FromResult(list);
    }

    public async Task<bool> ExistsActiveAsync(Guid tenantId, Guid branchId, CancellationToken ct) {
        return (await ListAsync(tenantId, ct)).Any(b => b.Id == branchId && b.IsActive);
    }
}
