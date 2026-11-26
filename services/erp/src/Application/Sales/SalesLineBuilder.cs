using ERP.Application.Common.Exceptions;
using ERP.Application.Common.Interfaces;
using ERP.Application.Common.Security;
using ERP.Application.Invoicing;
using ERP.Application.Pricing;
using ERP.Application.Pricing.Quotes;
using ERP.Application.Sales.Quotes;
using ERP.Application.Settings;
using ERP.Application.Settings.Categories;
using ERP.Domain.Catalog;
using ERP.Domain.CurrentAccounts;
using ERP.Domain.Invoicing;
using ERP.Domain.Pricing;
using ERP.Domain.Sales;

namespace ERP.Application.Sales;

public sealed class QuoteInputValidator : AbstractValidator<QuoteInput> {
    public QuoteInputValidator() {
        RuleFor(i => i.CurrentAccountId).NotEmpty().WithMessage("Müşteri seçilmelidir.");
        RuleFor(i => i.BranchId).NotEmpty().WithMessage("Şube seçilmelidir.");
        RuleFor(i => i.Date).Must(d => d != default).WithMessage("Teklif tarihi zorunludur.");
        RuleFor(i => i.ValidUntil).Must((i, v) => v is null || v >= i.Date).WithMessage("Geçerlilik tarihi teklif tarihinden önce olamaz.");
        RuleFor(i => i.Currency).Must(c => c is not null && InvoiceRules.Currencies.Contains(c.Trim().ToUpperInvariant()))
            .WithMessage("Para birimi TRY, USD, EUR ya da GBP olmalıdır.");
        RuleFor(i => i.DocDiscount).InclusiveBetween(0m, 100m).When(i => i.DocDiscount is not null).WithMessage("Belge iskonto yüzdesi 0 ile 100 arasında olmalıdır.");
        RuleFor(i => i.Notes).MaximumLength(1000).WithMessage("Not en fazla 1000 karakter olabilir.");
        RuleFor(i => i.Lines).NotEmpty().WithMessage("Teklifte en az bir satır olmalıdır.")
            .Must(l => l is null || l.Count <= 500).WithMessage("Teklifte en fazla 500 satır olabilir.");
        RuleForEach(i => i.Lines).ChildRules(l => {
            l.RuleFor(x => x.ProductId).NotEmpty().WithMessage("Kalem seçilmelidir.");
            l.RuleFor(x => x.Quantity).GreaterThanOrEqualTo(0m).WithMessage("Miktar negatif olamaz.")
                .Must(q => Math.Round(q, 6) == q).WithMessage("Miktar en fazla 6 ondalık basamak içerebilir.");
            l.RuleFor(x => x.Width).Must(w => w is null || (w > 0m && Math.Round(w.Value, 4) == w)).WithMessage("En sıfırdan büyük olmalı ve en fazla 4 ondalık basamak içermelidir.");
            l.RuleFor(x => x.Length).Must(w => w is null || (w > 0m && Math.Round(w.Value, 4) == w)).WithMessage("Boy sıfırdan büyük olmalı ve en fazla 4 ondalık basamak içermelidir.");
            l.RuleFor(x => x.UnitPrice).GreaterThanOrEqualTo(0m).When(x => x.UnitPrice is not null).WithMessage("Birim fiyat negatif olamaz.")
                .Must(p => p is null || Math.Round(p.Value, 6) == p).WithMessage("Birim fiyat en fazla 6 ondalık basamak içerebilir.");
            l.RuleFor(x => x.DiscountPercent).InclusiveBetween(0m, 100m).When(x => x.DiscountPercent is not null)
                .WithMessage("Satır iskonto yüzdesi 0 ile 100 arasında olmalıdır.");
            l.RuleFor(x => x.VatRate).Must(r => InvoiceCalculator.AllowedVatRates.Contains(r!.Value)).When(x => x.VatRate is not null)
                .WithMessage("KDV oranı 0, 1, 10 ya da 20 olmalıdır.");
            l.RuleFor(x => x.Description).MaximumLength(500).WithMessage("Açıklama en fazla 500 karakter olabilir.");
        });
    }
}

public sealed record ResolvedLine(
    int        Sort,
    Guid       ProductId,
    Guid?      VariantId,
    string     Description,
    string     Unit,
    decimal    Factor,
    decimal    Quantity,
    decimal?   Width,
    decimal?   Length,
    decimal    UnitPrice,
    bool       VatIncluded,
    decimal?   DiscountPercent,
    int        VatRate,
    SupplyMode SupplyMode,
    Guid?      SupplierId,
    decimal    Net,
    decimal    Vat,
    decimal    Total
);

public sealed record ResolvedDocument(IReadOnlyList<ResolvedLine> Lines, CalcResult Calc);

/// <summary>Teklif/sipariş satır girdisini çözer: kalem, birim, ölçü, fiyat (P2 önceliği), iskonto politikası ve toplamlar.</summary>
public sealed class SalesLineBuilder(
    IApplicationDbContext db,
    ICurrentTenant        tenant,
    IPriceQuoteService    quotes,
    IMemberDirectory      members,
    IBranchDirectory      branches,
    ITenantSettings       settings
) {
    public async Task<CurrentAccount> LoadCustomerAsync(Guid id, CancellationToken ct) {
        var cari = await db.CurrentAccounts.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct)
                   ?? throw new NotFoundException(id.ToString(), "CurrentAccount");
        if (!cari.IsActive) throw SalesErrors.Conflict("current_account_inactive", "Cari pasif; belge düzenlenemez.");
        if (!cari.IsCustomer) throw SalesErrors.Bad("cari_not_customer", "Satış belgesi için cari müşteri olmalıdır.");
        return cari;
    }

    /// <summary>Temsilci: verilen, yoksa carinin varsayılanı. Ayar zorunluysa 400 sales_rep_required; üye değilse 400 sales_rep_invalid.</summary>
    public async Task<(Guid? UserId, string? Name)> ResolveRepAsync(Guid? requested, CurrentAccount cari, CancellationToken ct) {
        var rep      = requested ?? cari.SalesRepUserId;
        var required = (await settings.GetAsync<SalesSettings>(ct)).RequireSalesRep;
        if (rep is null) return required ? throw SalesErrors.SalesRepRequired() : (null, null);

        var tenantId = tenant.TenantId ?? throw new InvalidOperationException("Aktif firma yok.");
        var member   = await members.GetAsync(tenantId, rep.Value, ct)
                       ?? throw SalesErrors.Bad("sales_rep_invalid", "Satış temsilcisi bu firmanın üyesi değil.");
        return (member.UserId, member.FullName);
    }

    public async Task EnsureBranchAsync(Guid branchId, CancellationToken ct) {
        BranchGuard.Ensure(tenant, branchId);
        var tenantId = tenant.TenantId ?? throw new InvalidOperationException("Aktif firma yok.");
        if (!await branches.ExistsActiveAsync(tenantId, branchId, ct))
            throw new AppException(404, "branch_not_found", "Şube bulunamadı ya da pasif.");
    }

    public async Task<ResolvedDocument> BuildAsync(CurrentAccount customer, string currency, DateOnly date, decimal? docDiscount,
                                                   IReadOnlyList<QuoteLineInput> input, CancellationToken ct) {
        var productIds = input.Select(l => l.ProductId).Distinct().ToList();
        var products   = await db.Products.AsNoTracking().Include(p => p.Units).Where(p => productIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
        var variantIds = input.Where(l => l.VariantId is not null).Select(l => l.VariantId!.Value).Distinct().ToList();
        var variants   = await db.ProductVariants.AsNoTracking().Where(v => variantIds.Contains(v.Id)).ToDictionaryAsync(v => v.Id, ct);

        var lines     = new List<ResolvedLine>();
        var calcLines = new List<CalcLine>();
        for (var n = 0; n < input.Count; n++) {
            var l     = input[n];
            var label = $"{n + 1}. satır";
            if (!products.TryGetValue(l.ProductId, out var product)) throw SalesErrors.Bad("product_not_found", $"{label}: kalem bulunamadı.");
            if (!product.IsActive) throw SalesErrors.Bad("product_inactive", $"{label}: kalem pasif.");
            if (l.VariantId is { } vid && (!variants.TryGetValue(vid, out var variant) || variant.ProductId != product.Id))
                throw SalesErrors.Bad("variant_invalid", $"{label}: varyant kaleme ait değil.");

            var (unit, factor)            = ResolveUnit(l, product, label);
            var (quantity, width, length) = ResolveQuantity(l, product, label);
            var vatRate                   = l.VatRate ?? product.VatRate;
            if (!InvoiceCalculator.AllowedVatRates.Contains(vatRate)) throw SalesErrors.Bad("invalid_vat_rate", $"{label}: KDV oranı geçersiz.");

            decimal price;
            bool    vatIncluded;
            if (l.UnitPrice is { } given) {
                (price, vatIncluded) = (given, l.VatIncluded ?? false);
            } else {
                var quote = await quotes.QuoteAsync(new QuoteRequest(PriceListKind.Sale, customer.Id, product.Id, l.VariantId, unit, quantity, date), ct);
                if (quote is null || !string.Equals(quote.Currency, currency, StringComparison.OrdinalIgnoreCase))
                    throw SalesErrors.Bad("price_required", $"{label}: fiyat bulunamadı; birim fiyat girilmelidir.");
                (price, vatIncluded) = (quote.UnitPrice, quote.VatIncluded);
            }

            var supply = l.SupplyMode ?? SupplyMode.Stock;
            lines.Add(new ResolvedLine(n + 1, product.Id, l.VariantId, string.IsNullOrWhiteSpace(l.Description) ? product.Name : l.Description.Trim(), unit, factor,
                                       quantity, width, length, price, vatIncluded, l.DiscountPercent, vatRate, supply, l.SupplierId, 0m, 0m, 0m));
            calcLines.Add(new CalcLine(quantity, price, vatIncluded, l.DiscountPercent, null, vatRate, null, null, null, false));
        }

        CalcResult calc;
        try {
            calc = InvoiceCalculator.Calculate(calcLines, docDiscount is > 0m ? docDiscount : null, null);
        } catch (ArgumentException e) {
            throw SalesErrors.Bad("invalid_amounts", e.Message);
        }

        // İskonto politikası: satır + belge iskontosunun brüte etkisi kullanıcının sınırını aşamaz.
        if (calc.Gross > 0m) DiscountPolicy.Ensure(Math.Round(calc.Discount / calc.Gross * 100m, 4), tenant);

        var resolved = lines.Select((l, i) => l with { Net = calc.Lines[i].Net, Vat = calc.Lines[i].Vat, Total = calc.Lines[i].Total }).ToList();
        return new ResolvedDocument(resolved, calc);
    }

    private static (string Unit, decimal Factor) ResolveUnit(QuoteLineInput l, Product product, string label) {
        var requested = l.Unit?.Trim();
        if (string.IsNullOrEmpty(requested) || string.Equals(requested, product.BaseUnit, StringComparison.OrdinalIgnoreCase)) return (product.BaseUnit, 1m);
        var unit = product.Units.FirstOrDefault(u => string.Equals(u.Unit, requested, StringComparison.OrdinalIgnoreCase))
                   ?? throw SalesErrors.Bad("unit_invalid", $"{label}: \"{requested}\" birimi kalemde tanımlı değil.");
        return (unit.Unit, unit.Factor);
    }

    /// <summary>Ölçüyle satılan kalemlerde miktar en x boy (m2) ya da boydur (m); ölçüler metre cinsindendir.</summary>
    private static (decimal Quantity, decimal? Width, decimal? Length) ResolveQuantity(QuoteLineInput l, Product product, string label) {
        if (!product.SoldByMeasure) {
            if (l.Quantity <= 0m) throw SalesErrors.Bad("quantity_required", $"{label}: miktar sıfırdan büyük olmalıdır.");
            return (l.Quantity, null, null);
        }

        var byLength = string.Equals(product.MeasureUnit, "m", StringComparison.OrdinalIgnoreCase);
        if (l.Length is not > 0m || (!byLength && l.Width is not > 0m))
            throw SalesErrors.Bad("measure_dimensions_required", byLength ? $"{label}: boy zorunludur." : $"{label}: en ve boy zorunludur.");
        var quantity = byLength ? l.Length.Value : Math.Round(l.Width!.Value * l.Length.Value, 6, MidpointRounding.AwayFromZero);
        return (quantity, byLength ? null : l.Width, l.Length);
    }
}
