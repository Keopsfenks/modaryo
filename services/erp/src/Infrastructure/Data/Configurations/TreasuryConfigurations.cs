using System.Text.Json;
using ERP.Domain.CurrentAccounts;
using ERP.Domain.Treasury;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ERP.Infrastructure.Data.Configurations;

public class TreasuryAccountConfiguration : IEntityTypeConfiguration<TreasuryAccount> {
    public void Configure(EntityTypeBuilder<TreasuryAccount> builder) {
        builder.ToTable("treasury_accounts");
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.Kind).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.Name).HasMaxLength(100).IsRequired();
        builder.Property(e => e.Currency).HasMaxLength(3).IsRequired();
        builder.Property(e => e.Iban).HasMaxLength(34);
        builder.Property(e => e.BankName).HasMaxLength(100);

        builder.HasIndex(e => new { e.TenantId, e.Name }).IsUnique();
        builder.HasIndex(e => new { e.TenantId, e.BranchId });
    }
}

public class TreasuryMovementConfiguration : IEntityTypeConfiguration<TreasuryMovement> {
    public void Configure(EntityTypeBuilder<TreasuryMovement> builder) {
        builder.ToTable("treasury_movements");
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.Amount).HasPrecision(18, 2);
        builder.Property(e => e.Currency).HasMaxLength(3).IsRequired();
        builder.Property(e => e.Description).HasMaxLength(500);

        builder.HasOne<TreasuryAccount>().WithMany().HasForeignKey(e => e.AccountId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<FinanceDocument>().WithMany().HasForeignKey(e => e.DocumentId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(e => new { e.TenantId, e.AccountId, e.Date });
        builder.HasIndex(e => e.DocumentId);
    }
}

public class ExpenseCategoryConfiguration : IEntityTypeConfiguration<ExpenseCategory> {
    public void Configure(EntityTypeBuilder<ExpenseCategory> builder) {
        builder.ToTable("expense_categories");
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.Code).HasMaxLength(50).IsRequired();
        builder.Property(e => e.Name).HasMaxLength(100).IsRequired();
        builder.Property(e => e.AccountCode).HasMaxLength(30).IsRequired();

        builder.HasIndex(e => new { e.TenantId, e.Code }).IsUnique();
    }
}

public class FinanceDocumentConfiguration : IEntityTypeConfiguration<FinanceDocument> {
    public void Configure(EntityTypeBuilder<FinanceDocument> builder) {
        builder.ToTable("finance_documents");
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.Kind).HasConversion<string>().HasMaxLength(30);
        builder.Property(e => e.Method).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.Number).HasMaxLength(30).IsRequired();
        builder.Property(e => e.Amount).HasPrecision(18, 2);
        builder.Property(e => e.Currency).HasMaxLength(3).IsRequired();
        builder.Property(e => e.Rate).HasPrecision(18, 6);
        builder.Property(e => e.VatRate).HasPrecision(5, 2);
        builder.Property(e => e.Description).HasMaxLength(500);
        builder.Property(e => e.CancelReason).HasMaxLength(200);

        builder.HasOne<TreasuryAccount>().WithMany().HasForeignKey(e => e.AccountId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<TreasuryAccount>().WithMany().HasForeignKey(e => e.TargetAccountId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<CurrentAccount>().WithMany().HasForeignKey(e => e.CurrentAccountId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Instrument>().WithMany().HasForeignKey(e => e.InstrumentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ExpenseCategory>().WithMany().HasForeignKey(e => e.ExpenseCategoryId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(e => new { e.TenantId, e.Number }).IsUnique();
        builder.HasIndex(e => new { e.TenantId, e.Kind, e.Date });
        builder.HasIndex(e => new { e.TenantId, e.CurrentAccountId, e.Date });
    }
}

public class InstrumentConfiguration : IEntityTypeConfiguration<Instrument> {
    public void Configure(EntityTypeBuilder<Instrument> builder) {
        builder.ToTable("instruments");
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.Kind).HasConversion<string>().HasMaxLength(10);
        builder.Property(e => e.Direction).HasConversion<string>().HasMaxLength(10);
        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(30);
        builder.Property(e => e.Number).HasMaxLength(50).IsRequired();
        builder.Property(e => e.Amount).HasPrecision(18, 2);
        builder.Property(e => e.Currency).HasMaxLength(3).IsRequired();
        builder.Property(e => e.DrawerName).HasMaxLength(200).IsRequired();
        builder.Property(e => e.BankName).HasMaxLength(100);
        builder.Property(e => e.BankBranch).HasMaxLength(100);
        builder.Property(e => e.AccountNo).HasMaxLength(50);

        builder.HasOne<CurrentAccount>().WithMany().HasForeignKey(e => e.CurrentAccountId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<CurrentAccount>().WithMany().HasForeignKey(e => e.EndorsedToId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<TreasuryAccount>().WithMany().HasForeignKey(e => e.CollectionAccountId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(e => new { e.TenantId, e.Status, e.DueDate });
        builder.HasIndex(e => new { e.TenantId, e.Kind, e.Direction, e.BankName, e.Number }).IsUnique().AreNullsDistinct(false);
        builder.HasIndex(e => new { e.TenantId, e.CurrentAccountId });
    }
}

public class InstrumentEventConfiguration : IEntityTypeConfiguration<InstrumentEvent> {
    public void Configure(EntityTypeBuilder<InstrumentEvent> builder) {
        builder.ToTable("instrument_events");
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.From).HasConversion<string>().HasMaxLength(30);
        builder.Property(e => e.To).HasConversion<string>().HasMaxLength(30);
        builder.Property(e => e.Note).HasMaxLength(500);

        builder.HasOne<Instrument>().WithMany().HasForeignKey(e => e.InstrumentId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<FinanceDocument>().WithMany().HasForeignKey(e => e.DocumentId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(e => new { e.InstrumentId, e.Date });
    }
}

public class JournalVoucherConfiguration : IEntityTypeConfiguration<JournalVoucher> {
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public void Configure(EntityTypeBuilder<JournalVoucher> builder) {
        builder.ToTable("journal_vouchers");
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.Number).HasMaxLength(30).IsRequired();
        builder.Property(e => e.Description).HasMaxLength(500).IsRequired();
        builder.Property(e => e.SourceType).HasMaxLength(50).IsRequired();
        builder.Property(e => e.ExternalCaller).HasMaxLength(50);
        builder.Property(e => e.CancelReason).HasMaxLength(200);

        var lines = builder.Property(e => e.Lines).HasColumnType("jsonb");
        lines.HasConversion(v => JsonSerializer.Serialize(v, Json),
                            v => JsonSerializer.Deserialize<List<JournalLine>>(v, Json) ?? new List<JournalLine>());
        lines.Metadata.SetValueComparer(new ValueComparer<List<JournalLine>>(
                                            (a, b) => JsonSerializer.Serialize(a, Json) == JsonSerializer.Serialize(b, Json),
                                            v => JsonSerializer.Serialize(v, Json).GetHashCode(),
                                            v => JsonSerializer.Deserialize<List<JournalLine>>(JsonSerializer.Serialize(v, Json), Json)!));

        builder.HasIndex(e => new { e.TenantId, e.Number }).IsUnique();
        builder.HasIndex(e => new { e.TenantId, e.SourceType, e.SourceId });
        // Dış servisin aynı kaynak için ikinci (farklı idempotency anahtarlı) fişi: iptal edilmedikçe tek fiş.
        builder.HasIndex(e => new { e.TenantId, e.ExternalCaller, e.SourceType, e.SourceId }).IsUnique()
            .HasFilter("\"ExternalCaller\" IS NOT NULL AND \"SourceId\" IS NOT NULL AND NOT \"IsCancelled\"");
        builder.HasIndex(e => new { e.TenantId, e.Date });
    }
}

public class ExchangeDifferenceSettlementConfiguration : IEntityTypeConfiguration<ExchangeDifferenceSettlement> {
    public void Configure(EntityTypeBuilder<ExchangeDifferenceSettlement> builder) {
        builder.ToTable("exchange_difference_settlements");
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.Amount).HasPrecision(18, 2);
        builder.Property(e => e.ResultType).HasMaxLength(20).IsRequired();

        builder.HasOne<CurrentAccount>().WithMany().HasForeignKey(e => e.CurrentAccountId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(e => new { e.TenantId, e.CurrentAccountId });
    }
}
