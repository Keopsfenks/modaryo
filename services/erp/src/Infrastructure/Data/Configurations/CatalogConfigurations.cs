using System.Linq.Expressions;
using System.Text.Json;
using ERP.Domain.Catalog;
using ERP.Domain.CurrentAccounts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ERP.Infrastructure.Data.Configurations;

internal static class CatalogJson {
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    /// <summary>Nesneyi jsonb sütununa metin olarak yazar; değişiklik izleme için içerik karşılaştırması kullanır.</summary>
    public static PropertyBuilder<T> AsJsonb<T>(this PropertyBuilder<T> property) where T : class, new() {
        property.HasConversion(v => JsonSerializer.Serialize(v, Options),
                               v => JsonSerializer.Deserialize<T>(v, Options) ?? new T(),
                               new ValueComparer<T>((a, b) => JsonSerializer.Serialize(a, Options) == JsonSerializer.Serialize(b, Options),
                                                    v => JsonSerializer.Serialize(v, Options).GetHashCode(),
                                                    v => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(v, Options), Options)!));
        property.HasColumnType("jsonb");
        return property;
    }

    /// <summary>Money4 alanını prefix_amount, prefix_currency ve prefix_vat_included sütunlarına eşler (isteğe bağlı).</summary>
    public static void MapMoney<T>(this EntityTypeBuilder<T> builder, Expression<Func<T, Money4?>> property, string prefix)
        where T : class {
        builder.ComplexProperty(property, m => {
            m.Property(x => x.Amount).HasColumnName(prefix + "_amount").HasPrecision(18, 4);
            m.Property(x => x.Currency).HasColumnName(prefix + "_currency").HasMaxLength(3);
            m.Property(x => x.VatIncluded).HasColumnName(prefix + "_vat_included");
        });
    }
}

public class ProductConfiguration : IEntityTypeConfiguration<Product> {
    public void Configure(EntityTypeBuilder<Product> builder) {
        builder.ToTable("products");
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.Kind).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.Code).HasMaxLength(30).IsRequired();
        builder.Property(e => e.Name).HasMaxLength(200).IsRequired();
        builder.Property(e => e.Description).HasMaxLength(4000);
        builder.Property(e => e.BaseUnit).HasMaxLength(20).IsRequired();
        builder.Property(e => e.MeasureUnit).HasMaxLength(20);
        builder.Property(e => e.WeightKg).HasPrecision(18, 4);
        builder.Property(e => e.WidthCm).HasPrecision(18, 4);
        builder.Property(e => e.LengthCm).HasPrecision(18, 4);
        builder.Property(e => e.HeightCm).HasPrecision(18, 4);
        builder.Property(e => e.OriginCountry).HasMaxLength(100);
        builder.Property(e => e.Gtip).HasMaxLength(20);
        builder.Property(e => e.Tags).HasColumnType("text[]");
        builder.Property(e => e.CustomFieldsJson).HasColumnType("jsonb").IsRequired();
        builder.Property(e => e.Attributes).AsJsonb().HasColumnName("attributes");
        builder.MapMoney(e => e.DefaultPurchasePrice, "default_purchase_price");
        builder.MapMoney(e => e.DefaultSalePrice, "default_sale_price");

        builder.HasIndex(e => new { e.TenantId, e.Code }).IsUnique();
        builder.HasIndex(e => new { e.TenantId, e.Kind, e.IsActive });

        builder.HasOne<Category>().WithMany().HasForeignKey(e => e.CategoryId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Brand>().WithMany().HasForeignKey(e => e.BrandId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(e => e.Variants).WithOne().HasForeignKey(v => v.ProductId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(e => e.Units).WithOne().HasForeignKey(v => v.ProductId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(e => e.Suppliers).WithOne().HasForeignKey(v => v.ProductId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(e => e.Images).WithOne().HasForeignKey(v => v.ProductId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class ProductVariantConfiguration : IEntityTypeConfiguration<ProductVariant> {
    public void Configure(EntityTypeBuilder<ProductVariant> builder) {
        builder.ToTable("product_variants");
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.Code).HasMaxLength(40).IsRequired();
        builder.Property(e => e.Barcode).HasMaxLength(40);
        builder.Property(e => e.Values).AsJsonb();
        builder.MapMoney(e => e.SalePriceOverride, "sale_price_override");
        builder.MapMoney(e => e.PurchasePriceOverride, "purchase_price_override");

        builder.HasIndex(e => new { e.TenantId, e.Code }).IsUnique();
    }
}

public class ProductUnitConfiguration : IEntityTypeConfiguration<ProductUnit> {
    public void Configure(EntityTypeBuilder<ProductUnit> builder) {
        builder.ToTable("product_units");
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.Unit).HasMaxLength(20).IsRequired();
        builder.Property(e => e.Factor).HasPrecision(18, 6);
        builder.Property(e => e.Barcode).HasMaxLength(40);

        builder.HasIndex(e => new { e.ProductId, e.Unit }).IsUnique();
    }
}

public class ProductSupplierConfiguration : IEntityTypeConfiguration<ProductSupplier> {
    public void Configure(EntityTypeBuilder<ProductSupplier> builder) {
        builder.ToTable("product_suppliers");
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.SupplierCode).HasMaxLength(60);

        builder.HasIndex(e => new { e.ProductId, e.CurrentAccountId }).IsUnique();
        builder.HasIndex(e => new { e.TenantId, e.SupplierCode });
        builder.HasOne<CurrentAccount>().WithMany().HasForeignKey(e => e.CurrentAccountId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class ProductImageConfiguration : IEntityTypeConfiguration<ProductImage> {
    public void Configure(EntityTypeBuilder<ProductImage> builder) {
        builder.ToTable("product_images");
        builder.Property(e => e.Id).ValueGeneratedNever();
    }
}

public class CategoryConfiguration : IEntityTypeConfiguration<Category> {
    public void Configure(EntityTypeBuilder<Category> builder) {
        builder.ToTable("categories");
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.Name).HasMaxLength(100).IsRequired();

        // ParentId boş olan kök kategorilerde de ad tekil olsun.
        builder.HasIndex(e => new { e.TenantId, e.ParentId, e.Name }).IsUnique().AreNullsDistinct(false);
        builder.HasOne<Category>().WithMany().HasForeignKey(e => e.ParentId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class BrandConfiguration : IEntityTypeConfiguration<Brand> {
    public void Configure(EntityTypeBuilder<Brand> builder) {
        builder.ToTable("brands");
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.Name).HasMaxLength(100).IsRequired();

        builder.HasIndex(e => new { e.TenantId, e.Name }).IsUnique();
    }
}

public class BarcodeConfiguration : IEntityTypeConfiguration<Barcode> {
    public void Configure(EntityTypeBuilder<Barcode> builder) {
        builder.ToTable("barcodes");
        builder.HasKey(e => new { e.TenantId, e.Value });
        builder.Property(e => e.Value).HasMaxLength(40);
        builder.Property(e => e.Owner).HasConversion<string>().HasMaxLength(20);

        builder.HasIndex(e => e.ProductId);
        builder.HasOne<Product>().WithMany().HasForeignKey(e => e.ProductId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class CustomFieldDefinitionConfiguration : IEntityTypeConfiguration<CustomFieldDefinition> {
    public void Configure(EntityTypeBuilder<CustomFieldDefinition> builder) {
        builder.ToTable("custom_field_definitions");
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.Key).HasMaxLength(31).IsRequired();
        builder.Property(e => e.Label).HasMaxLength(100).IsRequired();
        builder.Property(e => e.Type).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.Options).HasColumnType("text[]");

        builder.HasIndex(e => new { e.TenantId, e.Key }).IsUnique();
    }
}

public class ImportBatchConfiguration : IEntityTypeConfiguration<ImportBatch> {
    public void Configure(EntityTypeBuilder<ImportBatch> builder) {
        builder.ToTable("import_batches");
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.Kind).HasMaxLength(20).IsRequired();
        builder.Property(e => e.RowsJson).HasColumnType("jsonb").IsRequired();

        builder.HasIndex(e => new { e.TenantId, e.ExpiresAt });
    }
}
