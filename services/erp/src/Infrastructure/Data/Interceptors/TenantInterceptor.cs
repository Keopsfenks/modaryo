using ERP.Application.Common.Interfaces;
using ERP.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace ERP.Infrastructure.Data.Interceptors;

/// <summary>
///     Yeni ITenantOwned kayıtlarına aktif firmayı yazar; aktif firma yoksa ya da kayıt başka bir firmaya
///     yazılmaya çalışılırsa kaydetmeyi reddeder.
/// </summary>
public sealed class TenantInterceptor(
    ICurrentTenant currentTenant
) : SaveChangesInterceptor {
    public override InterceptionResult<int>
        SavingChanges(DbContextEventData eventData, InterceptionResult<int> result) {
        Apply(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData      eventData,
        InterceptionResult<int> result,
        CancellationToken       cancellationToken = default
    ) {
        Apply(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void Apply(DbContext? context) {
        if (context is null) return;

        foreach (var entry in context.ChangeTracker.Entries<ITenantOwned>()) {
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted)) continue;

            var tenantId = currentTenant.TenantId
                           ?? throw new InvalidOperationException("Aktif firma olmadan firmaya ait veri kaydedilemez.");

            if (entry.State == EntityState.Added && entry.Entity.TenantId == Guid.Empty)
                entry.Entity.TenantId = tenantId;

            if (entry.Entity.TenantId != tenantId)
                throw new InvalidOperationException("Başka bir firmaya ait veri kaydedilemez.");
        }
    }
}