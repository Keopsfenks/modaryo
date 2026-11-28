using ERP.Domain.CurrentAccounts;
using ERP.Domain.Inventory;
using ERP.Domain.Pos;
using ERP.Domain.Treasury;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ERP.Infrastructure.Data.Configurations;

public class RegisterConfiguration : IEntityTypeConfiguration<Register> {
    public void Configure(EntityTypeBuilder<Register> builder) {
        builder.ToTable("pos_registers");
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.Code).HasMaxLength(20).IsRequired();
        builder.Property(e => e.Name).HasMaxLength(100).IsRequired();
        builder.Property(e => e.Version).AsXmin();
        builder.HasOne<Warehouse>().WithMany().HasForeignKey(e => e.WarehouseId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<TreasuryAccount>().WithMany().HasForeignKey(e => e.CashAccountId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<TreasuryAccount>().WithMany().HasForeignKey(e => e.CardAccountId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(e => e.Buttons).WithOne().HasForeignKey(b => b.RegisterId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(e => new { e.TenantId, e.Code }).IsUnique();
        builder.HasIndex(e => new { e.TenantId, e.BranchId });
    }
}

public class QuickButtonConfiguration : IEntityTypeConfiguration<QuickButton> {
    public void Configure(EntityTypeBuilder<QuickButton> builder) {
        builder.ToTable("pos_quick_buttons");
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.PresetQty).HasPrecision(18, 6);
        builder.Property(e => e.Color).HasMaxLength(20).IsRequired();
        builder.HasIndex(e => new { e.RegisterId, e.Page, e.Row, e.Col }).IsUnique();
    }
}

public class ShiftConfiguration : IEntityTypeConfiguration<Shift> {
    public void Configure(EntityTypeBuilder<Shift> builder) {
        builder.ToTable("pos_shifts");
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.OpeningCash).HasPrecision(18, 2);
        builder.Property(e => e.CountedCash).HasPrecision(18, 2);
        builder.Property(e => e.ExpectedCash).HasPrecision(18, 2);
        builder.Property(e => e.Difference).HasPrecision(18, 2);
        builder.Property(e => e.CloseNote).HasMaxLength(500);
        builder.Property(e => e.Version).AsXmin();
        builder.HasOne<Register>().WithMany().HasForeignKey(e => e.RegisterId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(e => e.CashMoves).WithOne().HasForeignKey(m => m.ShiftId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(e => new { e.TenantId, e.RegisterId, e.OpenedAt });
        // Bir kasada aynı anda tek açık vardiya (kasiyer başına da tek açık vardiya ayrıca uygulama katmanında denetlenir).
        builder.HasIndex(e => new { e.TenantId, e.RegisterId }).IsUnique().HasFilter("\"Status\" = 'Open'");
    }
}

public class CashMoveConfiguration : IEntityTypeConfiguration<CashMove> {
    public void Configure(EntityTypeBuilder<CashMove> builder) {
        builder.ToTable("pos_cash_moves");
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.Direction).HasConversion<string>().HasMaxLength(10);
        builder.Property(e => e.Amount).HasPrecision(18, 2);
        builder.Property(e => e.Note).HasMaxLength(500);
        builder.HasIndex(e => e.ShiftId);
    }
}

public class PosSaleConfiguration : IEntityTypeConfiguration<PosSale> {
    public void Configure(EntityTypeBuilder<PosSale> builder) {
        builder.ToTable("pos_sales");
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.Number).HasMaxLength(40).IsRequired();
        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.Mode).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.Subtotal).HasPrecision(18, 2);
        builder.Property(e => e.Discount).HasPrecision(18, 2);
        builder.Property(e => e.VatTotal).HasPrecision(18, 2);
        builder.Property(e => e.Total).HasPrecision(18, 2);
        builder.Property(e => e.ChangeGiven).HasPrecision(18, 2);
        builder.Property(e => e.CancelReason).HasMaxLength(500);
        builder.Property(e => e.Version).AsXmin();
        builder.HasOne<Register>().WithMany().HasForeignKey(e => e.RegisterId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Shift>().WithMany().HasForeignKey(e => e.ShiftId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<CurrentAccount>().WithMany().HasForeignKey(e => e.CurrentAccountId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(e => e.Lines).WithOne().HasForeignKey(l => l.SaleId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(e => e.Payments).WithOne().HasForeignKey(p => p.SaleId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(e => new { e.TenantId, e.Number }).IsUnique();
        builder.HasIndex(e => new { e.TenantId, e.RegisterId, e.Date });
        builder.HasIndex(e => e.ShiftId);
    }
}

public class PosSaleLineConfiguration : IEntityTypeConfiguration<PosSaleLine> {
    public void Configure(EntityTypeBuilder<PosSaleLine> builder) {
        builder.ToTable("pos_sale_lines");
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.Description).HasMaxLength(500).IsRequired();
        builder.Property(e => e.Unit).HasMaxLength(20).IsRequired();
        builder.Property(e => e.Factor).HasPrecision(18, 6);
        builder.Property(e => e.Quantity).HasPrecision(18, 6);
        builder.Property(e => e.UnitPrice).HasPrecision(18, 4);
        builder.Property(e => e.Discount).HasPrecision(18, 2);
        builder.Property(e => e.CampaignDiscount).HasPrecision(18, 2);
        builder.Property(e => e.Net).HasPrecision(18, 2);
        builder.Property(e => e.Vat).HasPrecision(18, 2);
        builder.Property(e => e.Total).HasPrecision(18, 2);
        builder.Property(e => e.UnitCost).HasPrecision(18, 4);
        builder.Property(e => e.ReturnedQuantity).HasPrecision(18, 6);
        builder.HasIndex(e => new { e.SaleId, e.Sort }).IsUnique();
    }
}

public class PosPaymentConfiguration : IEntityTypeConfiguration<PosPayment> {
    public void Configure(EntityTypeBuilder<PosPayment> builder) {
        builder.ToTable("pos_payments");
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.Method).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.Amount).HasPrecision(18, 2);
        builder.HasIndex(e => e.SaleId);
    }
}

public class PosReturnConfiguration : IEntityTypeConfiguration<PosReturn> {
    public void Configure(EntityTypeBuilder<PosReturn> builder) {
        builder.ToTable("pos_returns");
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.Number).HasMaxLength(40).IsRequired();
        builder.Property(e => e.RefundAmount).HasPrecision(18, 2);
        builder.Property(e => e.RefundMethod).HasConversion<string>().HasMaxLength(10);
        builder.Property(e => e.Reason).HasMaxLength(500);
        builder.Property(e => e.Version).AsXmin();
        builder.HasOne<PosSale>().WithMany().HasForeignKey(e => e.SaleId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Shift>().WithMany().HasForeignKey(e => e.ShiftId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(e => e.Lines).WithOne().HasForeignKey(l => l.ReturnId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(e => new { e.TenantId, e.Number }).IsUnique();
        builder.HasIndex(e => e.SaleId);
        builder.HasIndex(e => e.ShiftId);
    }
}

public class PosReturnLineConfiguration : IEntityTypeConfiguration<PosReturnLine> {
    public void Configure(EntityTypeBuilder<PosReturnLine> builder) {
        builder.ToTable("pos_return_lines");
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.Quantity).HasPrecision(18, 6);
        builder.Property(e => e.Amount).HasPrecision(18, 2);
        builder.HasOne<PosSaleLine>().WithMany().HasForeignKey(e => e.SaleLineId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(e => e.ReturnId);
    }
}
