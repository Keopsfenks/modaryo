using ERP.Domain.CurrentAccounts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ERP.Infrastructure.Data.Configurations;

public class CurrentAccountConfiguration : IEntityTypeConfiguration<CurrentAccount> {
    public void Configure(EntityTypeBuilder<CurrentAccount> builder) {
        builder.ToTable("current_accounts");
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.Code).HasMaxLength(20).IsRequired();
        builder.Property(e => e.Name).HasMaxLength(200).IsRequired();
        builder.Property(e => e.Kind).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.TaxNumber).HasMaxLength(11);
        builder.Property(e => e.TaxOffice).HasMaxLength(100);
        builder.Property(e => e.Phone).HasMaxLength(50);
        builder.Property(e => e.Email).HasMaxLength(200);
        builder.Property(e => e.Notes).HasMaxLength(2000);
        builder.Property(e => e.Currency).HasMaxLength(3).IsRequired();
        builder.Property(e => e.CreditLimit).HasPrecision(18, 2);

        builder.HasIndex(e => new { e.TenantId, e.Code }).IsUnique();
        builder.HasIndex(e => new { e.TenantId, e.TaxNumber }).IsUnique().HasFilter("\"TaxNumber\" IS NOT NULL");

        builder.HasMany(e => e.Addresses).WithOne().HasForeignKey(a => a.CurrentAccountId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class CurrentAccountAddressConfiguration : IEntityTypeConfiguration<CurrentAccountAddress> {
    public void Configure(EntityTypeBuilder<CurrentAccountAddress> builder) {
        builder.ToTable("current_account_addresses");
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.Title).HasMaxLength(100).IsRequired();
        builder.Property(e => e.Line).HasMaxLength(500).IsRequired();
        builder.Property(e => e.District).HasMaxLength(100);
        builder.Property(e => e.City).HasMaxLength(100);
        builder.Property(e => e.PostalCode).HasMaxLength(20);
        builder.Property(e => e.Country).HasMaxLength(100).IsRequired();
    }
}

public class LedgerEntryConfiguration : IEntityTypeConfiguration<LedgerEntry> {
    public void Configure(EntityTypeBuilder<LedgerEntry> builder) {
        builder.ToTable("ledger_entries");
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Ignore(e => e.IsCancelled);
        builder.Ignore(e => e.Signed);
        builder.Property(e => e.Type).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.Source).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.DocumentNo).HasMaxLength(50);
        builder.Property(e => e.Description).HasMaxLength(500);
        builder.Property(e => e.Debit).HasPrecision(18, 2);
        builder.Property(e => e.Credit).HasPrecision(18, 2);
        builder.Property(e => e.Currency).HasMaxLength(3).IsRequired();
        builder.Property(e => e.CancelledBy).HasMaxLength(200);
        builder.Property(e => e.CancelReason).HasMaxLength(200);

        builder.HasOne<CurrentAccount>().WithMany().HasForeignKey(e => e.CurrentAccountId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(e => new { e.TenantId, e.CurrentAccountId, e.Date });
        builder.HasIndex(e => new { e.TenantId, e.CurrentAccountId }).HasFilter("\"CancelledAt\" IS NULL");
        builder.HasIndex(e => e.CurrentAccountId).IsUnique()
            .HasFilter("\"Type\" = 'Opening' AND \"CancelledAt\" IS NULL")
            .HasDatabaseName("IX_ledger_entries_CurrentAccountId_Opening");
    }
}