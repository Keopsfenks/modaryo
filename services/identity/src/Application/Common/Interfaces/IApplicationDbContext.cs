using IdentityService.Domain.Entities;

namespace IdentityService.Application.Common.Interfaces;

public interface IApplicationDbContext
{
    DbSet<Tenant> Tenants { get; }

    DbSet<TenantRole> TenantRoles { get; }

    DbSet<Membership> Memberships { get; }

    DbSet<Invitation> Invitations { get; }

    DbSet<Branch> Branches { get; }

    DbSet<AuditEntry> AuditEntries { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}
