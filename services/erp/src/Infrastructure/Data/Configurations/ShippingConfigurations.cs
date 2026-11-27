using System.Text.Json;
using ERP.Domain.Files;
using ERP.Domain.Sales;
using ERP.Domain.Shipping;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ERP.Infrastructure.Data.Configurations;

internal static class ShippingMapping {
    /// <summary>Null olamayan liste: jsonb olarak saklanır.</summary>
    public static void AsJsonRequired<T>(PropertyBuilder<IReadOnlyList<T>> property) {
        var options = SalesMapping.JsonOptions;
        property.HasColumnType("jsonb");
        property.HasConversion(v => JsonSerializer.Serialize(v, options), v => (IReadOnlyList<T>)JsonSerializer.Deserialize<List<T>>(v, options)!);
        property.Metadata.SetValueComparer(new ValueComparer<IReadOnlyList<T>>(
                                               (a, b) => JsonSerializer.Serialize(a, options) == JsonSerializer.Serialize(b, options),
                                               v => JsonSerializer.Serialize(v, options).GetHashCode(),
                                               v => (IReadOnlyList<T>)JsonSerializer.Deserialize<List<T>>(JsonSerializer.Serialize(v, options), options)!));
    }
}

public class ParcelConfiguration : IEntityTypeConfiguration<Parcel> {
    public void Configure(EntityTypeBuilder<Parcel> builder) {
        builder.ToTable("parcels");
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.WeightKg).HasPrecision(18, 3);
        builder.Property(e => e.WidthCm).HasPrecision(18, 2);
        builder.Property(e => e.LengthCm).HasPrecision(18, 2);
        builder.Property(e => e.HeightCm).HasPrecision(18, 2);
        builder.Property(e => e.Desi).HasPrecision(18, 1);
        ShippingMapping.AsJsonRequired(builder.Property(e => e.Contents));
        builder.HasOne<Shipment>().WithMany(s => s.Parcels).HasForeignKey(e => e.ShipmentId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(e => new { e.ShipmentId, e.Index }).IsUnique();
    }
}

public class DeliveryAttemptConfiguration : IEntityTypeConfiguration<DeliveryAttempt> {
    public void Configure(EntityTypeBuilder<DeliveryAttempt> builder) {
        builder.ToTable("delivery_attempts");
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.Outcome).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.FailureReason).HasMaxLength(500);
        builder.HasOne<Shipment>().WithMany(s => s.Attempts).HasForeignKey(e => e.ShipmentId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(e => new { e.ShipmentId, e.AttemptNo }).IsUnique();
    }
}

public class DeliveryProofConfiguration : IEntityTypeConfiguration<DeliveryProof> {
    public void Configure(EntityTypeBuilder<DeliveryProof> builder) {
        builder.ToTable("delivery_proofs");
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.ReceivedBy).HasMaxLength(100).IsRequired();
        builder.Property(e => e.Note).HasMaxLength(500);
        builder.HasOne<Shipment>().WithMany().HasForeignKey(e => e.ShipmentId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<StoredFile>().WithMany().HasForeignKey(e => e.PhotoFileId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(e => e.ShipmentId);
    }
}

public class PickListConfiguration : IEntityTypeConfiguration<PickList> {
    public void Configure(EntityTypeBuilder<PickList> builder) {
        builder.ToTable("pick_lists");
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.Number).HasMaxLength(30).IsRequired();
        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.Version).AsXmin();
        ShippingMapping.AsJsonRequired(builder.Property(e => e.ShipmentIds));
        builder.HasMany(e => e.Lines).WithOne().HasForeignKey(l => l.PickListId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(e => new { e.TenantId, e.Number }).IsUnique();
        builder.HasIndex(e => new { e.TenantId, e.Status });
    }
}

public class PickLineConfiguration : IEntityTypeConfiguration<PickLine> {
    public void Configure(EntityTypeBuilder<PickLine> builder) {
        builder.ToTable("pick_lines");
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.Description).HasMaxLength(500).IsRequired();
        builder.Property(e => e.Unit).HasMaxLength(20).IsRequired();
        builder.Property(e => e.Factor).HasPrecision(18, 6);
        builder.Property(e => e.Quantity).HasPrecision(18, 6);
        builder.Property(e => e.PickedQuantity).HasPrecision(18, 6);
        builder.Property(e => e.Location).HasMaxLength(100);
        SalesMapping.AsJson<PickLot>(builder.Property(e => e.SuggestedLots));
        SalesMapping.AsJson<PickLot>(builder.Property(e => e.PickedLots));
        SalesMapping.AsJson<string>(builder.Property(e => e.PickedSerials));
        SalesMapping.AsJson<PickSource>(builder.Property(e => e.Sources));
        builder.HasIndex(e => new { e.PickListId, e.Sort }).IsUnique();
    }
}

public class DeliveryPlanConfiguration : IEntityTypeConfiguration<DeliveryPlan> {
    public void Configure(EntityTypeBuilder<DeliveryPlan> builder) {
        builder.ToTable("delivery_plans");
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.Number).HasMaxLength(30);
        builder.Property(e => e.Vehicle).HasMaxLength(100);
        builder.Property(e => e.DriverName).HasMaxLength(100);
        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.Version).AsXmin();
        builder.HasMany(e => e.Stops).WithOne().HasForeignKey(s => s.DeliveryPlanId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(e => new { e.TenantId, e.Date });
    }
}

public class DeliveryStopConfiguration : IEntityTypeConfiguration<DeliveryStop> {
    public void Configure(EntityTypeBuilder<DeliveryStop> builder) {
        builder.ToTable("delivery_stops");
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.Note).HasMaxLength(500);
        builder.OwnsOne(e => e.Address, o => o.ToJson());
        builder.HasOne<Shipment>().WithMany().HasForeignKey(e => e.ShipmentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(e => new { e.DeliveryPlanId, e.Sort }).IsUnique();
        builder.HasIndex(e => e.ShipmentId);
    }
}
