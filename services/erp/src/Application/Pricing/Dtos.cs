using System.Text.Json;
using ERP.Application.Common.Exceptions;
using ERP.Application.Common.Interfaces;
using ERP.Domain.Catalog;
using ERP.Domain.Pricing;

namespace ERP.Application.Pricing;

public sealed record PriceListDto(
    Guid          Id,
    string        Name,
    PriceListKind Kind,
    string        Currency,
    bool          VatIncluded,
    DateOnly?     ValidFrom,
    DateOnly?     ValidTo,
    bool          IsDefault,
    Guid?         SupplierId,
    Guid?         BaseListId,
    decimal?      AdjustmentPercent,
    bool          IsActive,
    uint          Version,
    int           ItemCount
) {
    public static PriceListDto From(PriceList l, int itemCount) {
        return new PriceListDto(l.Id, l.Name, l.Kind, l.Currency, l.VatIncluded, l.ValidFrom, l.ValidTo, l.IsDefault,
                                l.SupplierId, l.BaseListId, l.AdjustmentPercent, l.IsActive, l.Version, itemCount);
    }
}

public sealed record PriceItemDto(
    Guid?                    Id,
    Guid                     ProductId,
    string                   ProductCode,
    string                   ProductName,
    Guid?                    VariantId,
    string?                  VariantCode,
    string                   Unit,
    IReadOnlyList<PriceTier> Tiers,
    uint                     Version,
    bool                     Derived
);

public sealed record PriceItemsResult(IReadOnlyList<PriceItemDto> Rows);

public sealed record BulkAdjustResult(int Updated);

public sealed record CustomerGroupDto(Guid Id, string Name, Guid? SalePriceListId, int CustomerCount);

public sealed record CustomerPriceDto(
    Guid                     Id,
    Guid                     CurrentAccountId,
    Guid                     ProductId,
    string                   ProductCode,
    string                   ProductName,
    Guid?                    VariantId,
    string?                  VariantCode,
    string                   Unit,
    string                   Currency,
    bool                     VatIncluded,
    IReadOnlyList<PriceTier> Tiers,
    DateOnly?                ValidFrom,
    DateOnly?                ValidTo
);

public sealed record PriceHistoryDto(
    Guid           Id,
    DateTimeOffset At,
    string         ActorLabel,
    string         Scope,
    Guid           ScopeId,
    Guid           ProductId,
    string?        ProductCode,
    Guid?          VariantId,
    string         Unit,
    JsonElement?   Before,
    JsonElement?   After,
    PriceListKind  Kind
);

/// <summary>Fiyat modülünün ortak kuralları ve hataları.</summary>
public static class PricingRules {
    public static readonly string[] Currencies = ["TRY", "USD", "EUR", "GBP"];

    private const decimal MaxPrice = 99_999_999_999_999.9999m;

    public static AppException InvalidTiers(string? detail = null) {
        return new AppException(400, "invalid_tiers",
                                detail ?? "Kademeler geçersiz: ilk kademe 1'den başlamalı, miktarlar artmalı, fiyatlar negatif olmamalı.");
    }

    public static AppException VersionConflict() {
        return new AppException(409, "version_conflict", "Kayıt başkası tarafından değiştirildi. Sayfayı yenileyip tekrar deneyin.");
    }

    /// <summary>Kademeleri doğrular ve 4 haneye yuvarlar; geçersizse 400 invalid_tiers.</summary>
    public static List<PriceTier> NormalizeTiers(IReadOnlyList<PriceTier>? tiers) {
        if (tiers is null || !PriceResolver.TiersValid(tiers)) throw InvalidTiers();

        var result = tiers.Select(t => new PriceTier(Math.Round(t.MinQty, 4), Math.Round(t.Price, 4))).ToList();
        if (result.Any(t => t.Price > MaxPrice) || !PriceResolver.TiersValid(result))
            throw InvalidTiers();

        return result;
    }

    /// <summary>Birim kartın temel birimi, ölçü birimi ya da tanımlı birimlerinden biri olmalı.</summary>
    public static void EnsureUnit(Product product, string unit) {
        if (unit == product.BaseUnit || unit == product.MeasureUnit || product.Units.Any(u => u.Unit == unit)) return;

        throw new AppException(400, "invalid_unit", $"'{unit}' bu ürünün birimlerinden biri değil.");
    }

    /// <summary>Alış listeleri her işlemde fiyat.alis izni ister.</summary>
    public static void EnsureKindAccess(ICurrentSession session, PriceListKind kind) {
        if (kind == PriceListKind.Purchase && !session.HasPermission(PricingPermissions.Purchase))
            throw new ForbiddenAccessException();
    }

    public static bool IsUniqueViolation(DbUpdateException ex) {
        var inner = ex.InnerException;
        return inner?.GetType().Name == "PostgresException"
               && inner.GetType().GetProperty("SqlState")?.GetValue(inner) as string == "23505";
    }

    public static bool SameTiers(IReadOnlyList<PriceTier> a, IReadOnlyList<PriceTier> b) {
        return a.SequenceEqual(b);
    }

    public static async Task<PriceList> LoadListAsync(IApplicationDbContext db, ICurrentSession session, Guid id, CancellationToken ct,
                                                      bool track = true) {
        var query = track ? db.PriceLists.AsQueryable() : db.PriceLists.AsNoTracking();
        var list = await query.FirstOrDefaultAsync(l => l.Id == id, ct)
                   ?? throw new NotFoundException(id.ToString(), "PriceList");
        EnsureKindAccess(session, list.Kind);
        return list;
    }

    public static string FoldedSearch(string search) {
        return ERP.Domain.Common.TurkishText.Fold(search);
    }
}
