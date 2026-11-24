using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ERP.Domain.Catalog;
using ERP.Domain.CurrentAccounts;
using ERP.Domain.Inventory;
using ERP.Domain.Invoicing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ERP.Infrastructure.Data.Configurations;

/// <summary>GİB kod listelerinin gömülü JSON dosyalarından (Data/Seed) göçe tohum verisi üretir.</summary>
internal static class CodeListSeed {
    private sealed record Item(string Code, string Name, string? Group, int Tenths);

    private sealed record File(List<Item> Items);

    /// <summary>Kod başına sabit Id: göçler arasında değişmez.</summary>
    public static Guid IdFor(string kind, string code) {
        var hash = MD5.HashData(Encoding.UTF8.GetBytes($"{kind}:{code}"));
        return new Guid(hash);
    }

    public static IReadOnlyList<WithholdingCode> Withholding() {
        return Load("withholding-codes.json").Select(i => new WithholdingCode {
                                                                                  Id = IdFor("withholding", i.Code), Code = i.Code, Name = i.Name, Tenths = i.Tenths
                                                                              }).ToList();
    }

    public static IReadOnlyList<ExemptionCode> Exemption() {
        return Load("exemption-codes.json").Select(i => new ExemptionCode {
                                                                              Id = IdFor("exemption", i.Code), Code = i.Code, Name = i.Name, Group = i.Group ?? "istisna"
                                                                          }).ToList();
    }

    private static List<Item> Load(string file) {
        using var stream = typeof(CodeListSeed).Assembly.GetManifestResourceStream($"Seed.{file}")
                           ?? throw new InvalidOperationException($"Gömülü tohum dosyası bulunamadı: {file}");
        var parsed = JsonSerializer.Deserialize<File>(stream, new JsonSerializerOptions(JsonSerializerDefaults.Web))
                     ?? throw new InvalidOperationException($"Tohum dosyası okunamadı: {file}");
        return parsed.Items;
    }
}

public class InvoiceConfiguration : IEntityTypeConfiguration<Invoice> {
    public void Configure(EntityTypeBuilder<Invoice> builder) {
        builder.ToTable("invoices");
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.Kind).HasConversion<string>().HasMaxLength(30);
        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.Mode).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.Scenario).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.RateType).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.Number).HasMaxLength(50);
        builder.Property(e => e.ExternalNumber).HasMaxLength(50);
        builder.Property(e => e.Currency).HasMaxLength(3).IsRequired();
        builder.Property(e => e.Rate).HasPrecision(18, 6);
        builder.Property(e => e.DocDiscountPercent).HasPrecision(5, 2);
        builder.Property(e => e.DocDiscountAmount).HasPrecision(18, 2);
        foreach (var p in new[] { nameof(Invoice.Gross), nameof(Invoice.Discount), nameof(Invoice.Net), nameof(Invoice.Otv), nameof(Invoice.Vat),
                                  nameof(Invoice.Withheld), nameof(Invoice.Payable), nameof(Invoice.GrossTl), nameof(Invoice.DiscountTl),
                                  nameof(Invoice.NetTl), nameof(Invoice.OtvTl), nameof(Invoice.VatTl), nameof(Invoice.WithheldTl),
                                  nameof(Invoice.PayableTl) })
            builder.Property(p).HasPrecision(18, 2);
        builder.Property(e => e.Notes).HasMaxLength(1000);
        builder.Property(e => e.CancelReason).HasMaxLength(200);
        builder.Property(e => e.SourceType).HasMaxLength(50).IsRequired();
        builder.Property(e => e.Version).AsXmin();
        builder.OwnsOne(e => e.BuyerOrSeller, o => o.ToJson());

        builder.HasMany(e => e.Lines).WithOne().HasForeignKey(l => l.InvoiceId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(e => e.DueSchedule).WithOne().HasForeignKey(l => l.InvoiceId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<CurrentAccount>().WithMany().HasForeignKey(e => e.CurrentAccountId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<DocumentSeries>().WithMany().HasForeignKey(e => e.SeriesId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Warehouse>().WithMany().HasForeignKey(e => e.WarehouseId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<LedgerEntry>().WithMany().HasForeignKey(e => e.LedgerEntryId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Invoice>().WithMany().HasForeignKey(e => e.ReturnOfInvoiceId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(e => new { e.TenantId, e.Status, e.Date });
        builder.HasIndex(e => new { e.TenantId, e.CurrentAccountId, e.Date });
        // Kesilmiş fatura numarası firmada tekil; iptal edilen numarayı da tutar.
        builder.HasIndex(e => new { e.TenantId, e.Number }).IsUnique().HasFilter("\"Number\" IS NOT NULL");
        // Aynı tedarikçinin aynı fatura numarası bir kez girilebilir (alış faturaları).
        builder.HasIndex(e => new { e.TenantId, e.CurrentAccountId, e.ExternalNumber }).IsUnique()
            .HasFilter("\"ExternalNumber\" IS NOT NULL AND \"Status\" <> 'Cancelled'")
            .HasDatabaseName("IX_invoices_supplier_external_number");
    }
}

public class InvoiceLineConfiguration : IEntityTypeConfiguration<InvoiceLine> {
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public void Configure(EntityTypeBuilder<InvoiceLine> builder) {
        builder.ToTable("invoice_lines");
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.Description).HasMaxLength(500).IsRequired();
        builder.Property(e => e.Unit).HasMaxLength(20).IsRequired();
        builder.Property(e => e.Factor).HasPrecision(18, 6);
        builder.Property(e => e.Quantity).HasPrecision(18, 6);
        builder.Property(e => e.Width).HasPrecision(18, 6);
        builder.Property(e => e.Length).HasPrecision(18, 6);
        builder.Property(e => e.UnitPrice).HasPrecision(18, 6);
        builder.Property(e => e.DiscountPercent).HasPrecision(5, 2);
        builder.Property(e => e.DiscountAmount).HasPrecision(18, 2);
        builder.Property(e => e.OtvPercent).HasPrecision(9, 4);
        builder.Property(e => e.OtvPerUnit).HasPrecision(18, 6);
        builder.Property(e => e.WithholdingCode).HasMaxLength(10);
        builder.Property(e => e.ExemptionCode).HasMaxLength(10);
        builder.Property(e => e.ExemptionReason).HasMaxLength(500);
        foreach (var p in new[] { nameof(InvoiceLine.Gross), nameof(InvoiceLine.LineDiscount), nameof(InvoiceLine.DocDiscount), nameof(InvoiceLine.Net),
                                  nameof(InvoiceLine.Otv), nameof(InvoiceLine.VatBase), nameof(InvoiceLine.Vat), nameof(InvoiceLine.Withheld),
                                  nameof(InvoiceLine.Total), nameof(InvoiceLine.NetTl), nameof(InvoiceLine.OtvTl), nameof(InvoiceLine.VatTl),
                                  nameof(InvoiceLine.WithheldTl), nameof(InvoiceLine.TotalTl) })
            builder.Property(p).HasPrecision(18, 2);

        var serials = builder.Property(e => e.Serials).HasColumnType("jsonb");
        serials.HasConversion(v => v == null ? null : JsonSerializer.Serialize(v, Json),
                              v => v == null ? null : (IReadOnlyList<string>?)JsonSerializer.Deserialize<List<string>>(v, Json));
        serials.Metadata.SetValueComparer(new ValueComparer<IReadOnlyList<string>?>(
                                              (a, b) => JsonSerializer.Serialize(a, Json) == JsonSerializer.Serialize(b, Json),
                                              v => v == null ? 0 : JsonSerializer.Serialize(v, Json).GetHashCode(),
                                              v => v == null ? null : (IReadOnlyList<string>?)JsonSerializer.Deserialize<List<string>>(JsonSerializer.Serialize(v, Json), Json)));

        var lots = builder.Property(e => e.Lots).HasColumnType("jsonb");
        lots.HasConversion(v => v == null ? null : JsonSerializer.Serialize(v, Json),
                           v => v == null ? null : (IReadOnlyList<InvoiceLot>?)JsonSerializer.Deserialize<List<InvoiceLot>>(v, Json));
        lots.Metadata.SetValueComparer(new ValueComparer<IReadOnlyList<InvoiceLot>?>(
                                           (a, b) => JsonSerializer.Serialize(a, Json) == JsonSerializer.Serialize(b, Json),
                                           v => v == null ? 0 : JsonSerializer.Serialize(v, Json).GetHashCode(),
                                           v => v == null ? null : (IReadOnlyList<InvoiceLot>?)JsonSerializer.Deserialize<List<InvoiceLot>>(JsonSerializer.Serialize(v, Json), Json)));

        builder.HasOne<Product>().WithMany().HasForeignKey(e => e.ProductId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ProductVariant>().WithMany().HasForeignKey(e => e.VariantId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(e => new { e.InvoiceId, e.Sort }).IsUnique();
        builder.HasIndex(e => e.ProductId);
    }
}

public class InvoiceDueLineConfiguration : IEntityTypeConfiguration<InvoiceDueLine> {
    public void Configure(EntityTypeBuilder<InvoiceDueLine> builder) {
        builder.ToTable("invoice_due_lines");
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.Amount).HasPrecision(18, 2);

        builder.HasIndex(e => new { e.InvoiceId, e.Sort }).IsUnique();
    }
}

public class DocumentSeriesConfiguration : IEntityTypeConfiguration<DocumentSeries> {
    public void Configure(EntityTypeBuilder<DocumentSeries> builder) {
        builder.ToTable("document_series");
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Ignore(e => e.IsEBelge);
        builder.Property(e => e.KindGroup).HasMaxLength(20).IsRequired();
        builder.Property(e => e.Mode).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.Prefix).HasMaxLength(10).IsRequired();

        builder.HasIndex(e => new { e.TenantId, e.BranchId, e.KindGroup, e.Mode });
        // (Firma, şube, grup, mod) başına en çok bir varsayılan seri; uygulama kuralı veritabanında da güvence altında.
        builder.HasIndex(e => new { e.TenantId, e.BranchId, e.KindGroup, e.Mode }).IsUnique().HasFilter("\"IsDefault\"")
            .HasDatabaseName("IX_document_series_default");
        // Numaralar firma genelinde tekil olduğundan önek+yıl, grup/mod/şubeden bağımsız olarak firmada tekildir.
        builder.HasIndex(e => new { e.TenantId, e.Prefix, e.Year }).IsUnique().HasDatabaseName("IX_document_series_prefix_year");
    }
}

public class WithholdingCodeConfiguration : IEntityTypeConfiguration<WithholdingCode> {
    public void Configure(EntityTypeBuilder<WithholdingCode> builder) {
        builder.ToTable("withholding_codes");
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.Code).HasMaxLength(10).IsRequired();
        builder.Property(e => e.Name).HasMaxLength(500).IsRequired();
        builder.HasIndex(e => e.Code).IsUnique();
        builder.HasData(CodeListSeed.Withholding());
    }
}

public class ExemptionCodeConfiguration : IEntityTypeConfiguration<ExemptionCode> {
    public void Configure(EntityTypeBuilder<ExemptionCode> builder) {
        builder.ToTable("exemption_codes");
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.Code).HasMaxLength(10).IsRequired();
        builder.Property(e => e.Name).HasMaxLength(500).IsRequired();
        builder.Property(e => e.Group).HasMaxLength(30).IsRequired();
        builder.HasIndex(e => e.Code).IsUnique();
        builder.HasData(CodeListSeed.Exemption());
    }
}

public class ExchangeRateConfiguration : IEntityTypeConfiguration<ExchangeRate> {
    public void Configure(EntityTypeBuilder<ExchangeRate> builder) {
        builder.ToTable("exchange_rates");
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.Currency).HasMaxLength(3).IsRequired();
        builder.Property(e => e.Type).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.Rate).HasPrecision(18, 6);
        builder.Property(e => e.Source).HasMaxLength(20).IsRequired();
        builder.HasIndex(e => new { e.Currency, e.Type, e.Date }).IsUnique();
    }
}

public class TenantRateOverrideConfiguration : IEntityTypeConfiguration<TenantRateOverride> {
    public void Configure(EntityTypeBuilder<TenantRateOverride> builder) {
        builder.ToTable("tenant_rate_overrides");
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.Currency).HasMaxLength(3).IsRequired();
        builder.Property(e => e.Type).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.Rate).HasPrecision(18, 6);
        builder.Property(e => e.ActorLabel).HasMaxLength(200).IsRequired();
        builder.HasIndex(e => new { e.TenantId, e.Currency, e.Type, e.Date }).IsUnique();
    }
}
