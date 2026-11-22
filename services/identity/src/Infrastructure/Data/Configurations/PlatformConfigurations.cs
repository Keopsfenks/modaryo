using IdentityService.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace IdentityService.Infrastructure.Data.Configurations;

public class BranchConfiguration : IEntityTypeConfiguration<Branch>
{
    public void Configure(EntityTypeBuilder<Branch> builder)
    {
        builder.ToTable("branches");
        builder.Property(b => b.Code).HasMaxLength(10).IsRequired();
        builder.Property(b => b.Name).HasMaxLength(100).IsRequired();
        builder.HasOne<Tenant>().WithMany().HasForeignKey(b => b.TenantId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(b => new { b.TenantId, b.Code }).IsUnique();
        builder.HasIndex(b => b.TenantId).IsUnique().HasFilter("\"IsDefault\"").HasDatabaseName("IX_branches_TenantId_default");
    }
}

public class RoleBranchConfiguration : IEntityTypeConfiguration<RoleBranch>
{
    public void Configure(EntityTypeBuilder<RoleBranch> builder)
    {
        builder.ToTable("role_branches");
        builder.HasKey(b => new { b.RoleId, b.BranchId });
        builder.HasOne<Branch>().WithMany().HasForeignKey(b => b.BranchId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class MembershipBranchConfiguration : IEntityTypeConfiguration<MembershipBranch>
{
    public void Configure(EntityTypeBuilder<MembershipBranch> builder)
    {
        builder.ToTable("membership_branches");
        builder.HasKey(b => new { b.MembershipId, b.BranchId });
        builder.HasOne<Branch>().WithMany().HasForeignKey(b => b.BranchId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class AuditEntryConfiguration : IEntityTypeConfiguration<AuditEntry>
{
    public void Configure(EntityTypeBuilder<AuditEntry> builder)
    {
        builder.ToTable("audit_entries");
        builder.Property(a => a.ActorLabel).HasMaxLength(200).IsRequired();
        builder.Property(a => a.Action).HasMaxLength(100).IsRequired();
        builder.Property(a => a.EntityType).HasMaxLength(100).IsRequired();
        builder.Property(a => a.EntityId).HasMaxLength(100).IsRequired();
        builder.Property(a => a.Before).HasColumnType("jsonb");
        builder.Property(a => a.After).HasColumnType("jsonb");
        builder.HasOne<Tenant>().WithMany().HasForeignKey(a => a.TenantId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(a => new { a.TenantId, a.At });
    }
}
