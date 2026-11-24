using ERP.Application.Common.Exceptions;
using ERP.Application.Common.Interfaces;
using ERP.Application.Inventory.Ledger;
using ERP.Domain.Catalog;
using ERP.Domain.Common;

namespace ERP.Application.Inventory.Documents;

/// <summary>Belge/transfer satırı girdisi: birim ve miktar satırın biriminde, maliyet satır birimi başınadır.</summary>
public sealed record LineInput(
    Guid                     ProductId,
    Guid?                    VariantId,
    string                   Unit,
    decimal                  Quantity,
    decimal?                 UnitCost,
    IReadOnlyList<LotInput>? Lots,
    IReadOnlyList<string>?   Serials
);

public sealed record ResolvedLine(
    Guid                     ProductId,
    Guid?                    VariantId,
    string                   Unit,
    decimal                  Quantity,
    decimal                  BaseQuantity,
    decimal?                 BaseUnitCost,
    IReadOnlyList<LotInput>? Lots,
    IReadOnlyList<string>?   Serials
);

/// <summary>Satırları temel birime çevirir ve ürün/varyant/birim kurallarını denetler (P1 UnitConversion).</summary>
public sealed class LineResolver(IApplicationDbContext db) {
    public async Task<Dictionary<Guid, Product>> LoadProductsAsync(IEnumerable<Guid> ids, CancellationToken ct) {
        var wanted   = ids.Distinct().ToList();
        var products = await db.Products.AsNoTracking().Include(p => p.Variants).Include(p => p.Units)
                               .Where(p => wanted.Contains(p.Id)).ToListAsync(ct);
        foreach (var id in wanted)
            if (products.All(p => p.Id != id))
                throw new NotFoundException(id.ToString(), "Product");

        return products.ToDictionary(p => p.Id);
    }

    public async Task<List<ResolvedLine>> ResolveAsync(IReadOnlyList<LineInput> lines, CancellationToken ct) {
        var products = await LoadProductsAsync(lines.Select(l => l.ProductId), ct);
        return lines.Select(l => Resolve(l, products[l.ProductId])).ToList();
    }

    public static ResolvedLine Resolve(LineInput line, Product product) {
        var label = $"{product.Code} - {product.Name}";
        if (line.Quantity <= 0m || decimal.Round(line.Quantity, 6) != line.Quantity)
            throw new AppException(400, "invalid_quantity", $"{label}: miktar sıfırdan büyük olmalı ve en fazla 6 ondalık içermelidir.");
        if (line.UnitCost is < 0m) throw new AppException(400, "invalid_cost", $"{label}: birim maliyet negatif olamaz.");

        if (line.VariantId is { } variantId) {
            if (product.Variants.All(v => v.Id != variantId))
                throw new AppException(400, "invalid_variant", $"{label}: varyant bu ürüne ait değil.");
        } else if (product.Variants.Any(v => v.IsActive)) {
            throw new AppException(400, "variant_required", $"{label}: varyant seçilmelidir.");
        }

        var unit   = (line.Unit ?? string.Empty).Trim();
        var factor = 1m;
        if (!string.Equals(unit, product.BaseUnit, StringComparison.OrdinalIgnoreCase)) {
            var match = product.Units.FirstOrDefault(u => string.Equals(u.Unit, unit, StringComparison.OrdinalIgnoreCase))
                        ?? throw new AppException(400, "invalid_unit", $"{label}: \"{unit}\" birimi tanımlı değil.");
            factor = match.Factor;
        }

        var baseQuantity = decimal.Round(UnitConversion.ToBase(line.Quantity, factor), 6);
        var baseCost     = line.UnitCost is { } cost ? decimal.Round(cost / factor, 6) : (decimal?)null;
        var lots         = line.Lots?.Select(l => l with { Quantity = decimal.Round(UnitConversion.ToBase(l.Quantity, factor), 6) }).ToList();
        return new ResolvedLine(line.ProductId, line.VariantId, unit, line.Quantity, baseQuantity, baseCost, lots, line.Serials);
    }

    public static string? TrackingJson(ResolvedLine line) {
        if (line.Lots is null && line.Serials is null) return null;
        return System.Text.Json.JsonSerializer.Serialize(new { lots = line.Lots, serials = line.Serials },
                                                         ERP.Application.Settings.SettingsRegistry.Json);
    }
}

public static class DocumentNumbers {
    public static async Task<string> NextAsync(IApplicationDbContext db, string prefix, DateOnly date, CancellationToken ct) {
        var key = $"{prefix}{date.Year}";
        var n   = await db.NextDocumentNumberAsync(key, ct);
        return $"{key}{n:000000}";
    }
}
