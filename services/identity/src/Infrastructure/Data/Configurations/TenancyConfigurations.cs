using IdentityService.Domain.Constants;
using IdentityService.Domain.Entities;
using IdentityService.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace IdentityService.Infrastructure.Data.Configurations;

public class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> builder)
    {
        builder.ToTable("tenants");
        builder.Property(t => t.Name).HasMaxLength(100).IsRequired();
        builder.Property(t => t.EnabledModules).HasColumnType("text[]").HasDefaultValueSql("'{}'").IsRequired();
        builder.Property(t => t.AppliedPermissionSetVersion).HasDefaultValue(1);
    }
}

public class TenantRoleConfiguration : IEntityTypeConfiguration<TenantRole>
{
    public void Configure(EntityTypeBuilder<TenantRole> builder)
    {
        builder.ToTable("tenant_roles");
        builder.Property(r => r.Name).HasMaxLength(100).IsRequired();
        builder.Property(r => r.SystemKey).HasMaxLength(20);
        builder.HasOne<Tenant>().WithMany().HasForeignKey(r => r.TenantId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(r => new { r.TenantId, r.SystemKey }).IsUnique().HasFilter("\"SystemKey\" IS NOT NULL");
        builder.HasMany(r => r.Permissions).WithOne().HasForeignKey(p => p.RoleId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(r => r.Permissions).AutoInclude();
        builder.Property(r => r.MaxDiscountPercent).HasPrecision(5, 2).HasDefaultValue(0m);
        builder.Property(r => r.AllBranches).HasDefaultValue(true);
        builder.ToTable(t => t.HasCheckConstraint("CK_tenant_roles_MaxDiscountPercent", "\"MaxDiscountPercent\" >= 0 AND \"MaxDiscountPercent\" <= 100"));
        builder.HasMany(r => r.Branches).WithOne().HasForeignKey(b => b.RoleId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class TenantRolePermissionConfiguration : IEntityTypeConfiguration<TenantRolePermission>
{
    public void Configure(EntityTypeBuilder<TenantRolePermission> builder)
    {
        builder.ToTable("tenant_role_permissions");
        builder.HasKey(p => new { p.RoleId, p.Permission });
        builder.Property(p => p.Permission).HasMaxLength(100);
    }
}

public class MembershipConfiguration : IEntityTypeConfiguration<Membership>
{
    public void Configure(EntityTypeBuilder<Membership> builder)
    {
        builder.ToTable("memberships");
        builder.HasIndex(m => new { m.TenantId, m.UserId }).IsUnique();
        builder.HasIndex(m => m.UserId);
        builder.Property(m => m.Kind).HasMaxLength(20).HasDefaultValue(MembershipKinds.Employee).IsRequired();
        builder.Property(m => m.BranchesRestricted).HasDefaultValue(false);
        builder.HasMany(m => m.Branches).WithOne().HasForeignKey(b => b.MembershipId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(m => m.Tenant).WithMany().HasForeignKey(m => m.TenantId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(m => m.Role).WithMany().HasForeignKey(m => m.RoleId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(m => m.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class InvitationConfiguration : IEntityTypeConfiguration<Invitation>
{
    public void Configure(EntityTypeBuilder<Invitation> builder)
    {
        builder.ToTable("invitations");
        builder.Property(i => i.Email).HasMaxLength(256).IsRequired();
        builder.Property(i => i.TokenHash).HasMaxLength(64).IsRequired();
        builder.HasIndex(i => i.TokenHash).IsUnique();
        builder.HasIndex(i => new { i.TenantId, i.Email })
            .IsUnique()
            .HasFilter("\"AcceptedAt\" IS NULL AND \"RevokedAt\" IS NULL");
        builder.HasIndex(i => i.Email);
        builder.HasOne(i => i.Tenant).WithMany().HasForeignKey(i => i.TenantId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(i => i.Role).WithMany().HasForeignKey(i => i.RoleId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class ApplicationUserConfiguration : IEntityTypeConfiguration<ApplicationUser>
{
    public void Configure(EntityTypeBuilder<ApplicationUser> builder)
    {
        builder.Property(u => u.FullName).HasMaxLength(100).IsRequired();
    }
}
