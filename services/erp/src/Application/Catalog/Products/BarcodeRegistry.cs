using ERP.Application.Common.Exceptions;
using ERP.Application.Common.Interfaces;
using ERP.Domain.Catalog;

namespace ERP.Application.Catalog.Products;

/// <summary>Firma genelinde barkod tekilliği: ürün, varyant ve birim barkodları tek tabloda (barcodes) tutulur.</summary>
public sealed class BarcodeRegistry(IApplicationDbContext db, ICurrentTenant tenant) {
    public static AppException Taken(string value) {
        return new AppException(409, "barcode_taken", $"\"{value}\" barkodu bu firmada zaten kullanılıyor.");
    }

    /// <summary>
    ///     Verilen barkodların hiçbiri başka bir kartta kayıtlı olmamalı ve kendi aralarında tekrar etmemelidir.
    ///     productId: kaydedilen ürün; kendi barkodları çakışma sayılmaz.
    /// </summary>
    public async Task EnsureFreeAsync(IEnumerable<string> values, Guid productId, CancellationToken ct) {
        var list = values.ToList();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in list)
            if (!seen.Add(value))
                throw Taken(value);

        if (list.Count == 0) return;

        var clash = await db.Barcodes.AsNoTracking().Where(b => list.Contains(b.Value) && b.ProductId != productId)
                        .Select(b => b.Value).FirstOrDefaultAsync(ct);
        if (clash is not null) throw Taken(clash);
    }

    /// <summary>İçe aktarma: aynı denetim, ama veritabanına gitmeden önceden yüklenmiş barkod sahiplerine karşı.</summary>
    public static void EnsureFree(IEnumerable<string> values, Guid productId, ImportContext import) {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in values) {
            if (!seen.Add(value)) throw Taken(value);
            if (import.BarcodeOwners.TryGetValue(value, out var owner) && owner != productId) throw Taken(value);
        }
    }

    /// <summary>Verilen değerin herhangi bir kartta kayıtlı olup olmadığı.</summary>
    public Task<bool> IsTakenAsync(string value, CancellationToken ct) {
        return db.Barcodes.AnyAsync(b => b.Value == value, ct);
    }

    /// <summary>Ürünün barkod satırlarını kartın güncel durumuyla eşitler; ürünün barkodu productBarcode'dur.</summary>
    public async Task SyncAsync(Product product, string? productBarcode, CancellationToken ct, bool isNew = false) {
        var tenantId = tenant.TenantId ?? throw new InvalidOperationException("Aktif firma yok.");
        var wanted   = new Dictionary<string, (BarcodeOwner Owner, Guid OwnerId)>(StringComparer.Ordinal);

        if (productBarcode is not null) wanted[productBarcode] = (BarcodeOwner.Product, product.Id);
        foreach (var v in product.Variants.Where(v => v.Barcode is not null))
            wanted[v.Barcode!] = (BarcodeOwner.Variant, v.Id);
        foreach (var u in product.Units.Where(u => u.Barcode is not null))
            wanted[u.Barcode!] = (BarcodeOwner.Unit, u.Id);

        var existing = isNew ? [] : await db.Barcodes.Where(b => b.ProductId == product.Id).ToListAsync(ct);
        foreach (var row in existing) {
            if (wanted.Remove(row.Value, out var target)) {
                row.Owner   = target.Owner;
                row.OwnerId = target.OwnerId;
            } else {
                db.Barcodes.Remove(row);
            }
        }

        foreach (var (value, target) in wanted)
            db.Barcodes.Add(new Barcode {
                                TenantId = tenantId, Value = value, Owner = target.Owner, OwnerId = target.OwnerId,
                                ProductId = product.Id
                            });
    }
}
