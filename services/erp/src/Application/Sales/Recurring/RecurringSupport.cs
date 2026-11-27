using ERP.Application.Common.Exceptions;
using ERP.Application.Common.Interfaces;
using ERP.Application.Invoicing;
using ERP.Application.Pricing;
using ERP.Domain.Catalog;
using ERP.Domain.Invoicing;
using ERP.Domain.Sales;

namespace ERP.Application.Sales.Recurring;

/// <summary>Arka plan işi için firma bağlamı: kullanıcı yoktur, şube kısıtı yoktur. İskonto sınırı şablon kaydedilirken kullanıcının yetkisine göre denetlenmiştir.</summary>
public sealed class SalesBackgroundTenant(
    Guid tenantId
) : ICurrentTenant {
    public Guid? TenantId => tenantId;

    public Guid? UserId => null;

    public string? UserFullName => "sistem:satis";

    public bool HasModule(string key) {
        return false;
    }

    public IReadOnlyList<Guid>? BranchIds => null;

    public bool CanAccessBranch(Guid branchId) {
        return true;
    }

    public decimal MaxDiscountPercent => 100m;
}

public static class RecurringRules {
    public const string Source = "recurring";

    /// <summary>Şablonun dönem başına tutarı (kayıtlı fiyatlarla; fiyat listesinden yeniden fiyatlanacak satırlar için tahmindir).</summary>
    public static CalcResult Totals(IEnumerable<RecurringTemplateLine> lines) {
        var calc = lines.Select(l => new CalcLine(l.Quantity, l.UnitPrice, l.VatIncluded, l.DiscountPercent, null, l.VatRate, null, null, null, false)).ToList();
        return InvoiceCalculator.Calculate(calc, null, null);
    }

    /// <summary>Üretilmemiş ilk dönem (geçmişte kalmış olabilir) ve en son üretilen dönem.</summary>
    public static (DateOnly? Next, DateOnly? Last) Periods(RecurringTemplate t, IReadOnlyCollection<DateOnly> generated, DateOnly today) {
        var horizon = today.AddMonths(14);
        var next    = RecurrenceCalendar.DueDates(t.Interval, t.DayOfMonth, t.StartDate, t.EndDate, horizon).Where(d => !generated.Contains(d)).Cast<DateOnly?>().FirstOrDefault();
        var last    = generated.Count > 0 ? generated.Max() : (DateOnly?)null;
        return (next, last);
    }
}

public sealed class RecurringInputValidator : AbstractValidator<RecurringInput> {
    public RecurringInputValidator() {
        RuleFor(i => i.Name).MaximumLength(200).WithMessage("Ad en fazla 200 karakter olabilir.");
        RuleFor(i => i.Notes).MaximumLength(1000).WithMessage("Not en fazla 1000 karakter olabilir.");
        RuleFor(i => i.CurrentAccountId).NotEmpty().WithMessage("Cari seçilmelidir.");
        RuleFor(i => i.BranchId).NotEmpty().WithMessage("Şube seçilmelidir.");
        RuleFor(i => i.Currency).Must(c => c is not null && InvoiceRules.Currencies.Contains(c.Trim().ToUpperInvariant()))
            .WithMessage("Para birimi TRY, USD, EUR ya da GBP olmalıdır.");
        RuleFor(i => i.Interval).IsInEnum().WithMessage("Dönem türü geçersiz.");
        RuleFor(i => i.Mode).IsInEnum().WithMessage("Üretim şekli geçersiz.");
        RuleFor(i => i.DayOfMonth).InclusiveBetween(1, 28).WithMessage("Ayın günü 1 ile 28 arasında olmalıdır.");
        RuleFor(i => i.StartDate).Must(d => d != default).WithMessage("Başlangıç tarihi zorunludur.");
        RuleFor(i => i.EndDate).Must((i, end) => end is null || end >= i.StartDate).WithMessage("Bitiş tarihi başlangıçtan önce olamaz.");
        RuleFor(i => i.Lines).NotEmpty().WithMessage("En az bir satır olmalıdır.").Must(l => l.Count <= 100).WithMessage("En fazla 100 satır olabilir.");
        RuleForEach(i => i.Lines).ChildRules(l => {
            l.RuleFor(x => x.Quantity).GreaterThan(0m).WithMessage("Miktar sıfırdan büyük olmalıdır.").Must(q => Math.Round(q, 6) == q).WithMessage("Miktar en fazla 6 ondalık basamak içerebilir.");
            l.RuleFor(x => x.UnitPrice).GreaterThanOrEqualTo(0m).WithMessage("Birim fiyat negatif olamaz.").Must(p => Math.Round(p, 6) == p).WithMessage("Birim fiyat en fazla 6 ondalık basamak içerebilir.");
            l.RuleFor(x => x.DiscountPercent).InclusiveBetween(0m, 100m).When(x => x.DiscountPercent is not null).WithMessage("Satır iskonto yüzdesi 0 ile 100 arasında olmalıdır.");
            l.RuleFor(x => x.VatRate).Must(r => InvoiceCalculator.AllowedVatRates.Contains(r!.Value)).When(x => x.VatRate is not null).WithMessage("KDV oranı 0, 1, 10 ya da 20 olmalıdır.");
            l.RuleFor(x => x.Description).MaximumLength(500).WithMessage("Satır açıklaması en fazla 500 karakter olabilir.");
            l.RuleFor(x => x.Unit).MaximumLength(20).WithMessage("Birim en fazla 20 karakter olabilir.");
        });
    }
}

/// <summary>Girdiyi doğrulayıp şablona yazar: cari/şube, kalem ve birim çözümü, kullanıcının iskonto sınırı.</summary>
public sealed class RecurringWriter(
    IApplicationDbContext db,
    ICurrentTenant        tenant,
    SalesLineBuilder      builder
) {
    public async Task ApplyAsync(RecurringTemplate template, RecurringInput input, CancellationToken ct) {
        await builder.EnsureBranchAsync(input.BranchId, ct);
        var cari = await builder.LoadCustomerAsync(input.CurrentAccountId, ct);

        var productIds = input.Lines.Where(l => l.ProductId is not null).Select(l => l.ProductId!.Value).Distinct().ToList();
        var products   = await db.Products.AsNoTracking().Include(p => p.Units).Where(p => productIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
        var lines      = new List<RecurringTemplateLine>();
        foreach (var (l, n) in input.Lines.Select((l, n) => (l, n))) {
            var label = $"{n + 1}. satır";
            Product? product = null;
            if (l.ProductId is { } productId) {
                if (!products.TryGetValue(productId, out product)) throw SalesErrors.Bad("product_not_found", $"{label}: kalem bulunamadı.");
                if (!product.IsActive) throw SalesErrors.Bad("product_inactive", $"{label}: kalem pasif.");
                if (product.SoldByMeasure) throw SalesErrors.Bad("measure_unsupported", $"{label}: ölçüyle satılan kalem tekrarlayan faturada kullanılamaz.");
            } else if (l.VariantId is not null || string.IsNullOrWhiteSpace(l.Description)) {
                throw SalesErrors.Bad("description_required", $"{label}: kalem ya da açıklama verilmelidir.");
            }

            var unit = l.Unit?.Trim();
            if (product is not null) {
                if (string.IsNullOrEmpty(unit) || string.Equals(unit, product.BaseUnit, StringComparison.OrdinalIgnoreCase)) unit = product.BaseUnit;
                else unit = product.Units.FirstOrDefault(u => string.Equals(u.Unit, unit, StringComparison.OrdinalIgnoreCase))?.Unit
                            ?? throw SalesErrors.Bad("unit_invalid", $"{label}: \"{unit}\" birimi kalemde tanımlı değil.");
            }

            var vatRate = l.VatRate ?? product?.VatRate ?? 20;
            if (l.DiscountPercent is > 0m)
                DiscountPolicy.Ensure(l.DiscountPercent.Value, tenant);

            lines.Add(new RecurringTemplateLine {
                RecurringTemplateId = template.Id, Sort = n + 1, ProductId = l.ProductId, VariantId = l.VariantId,
                Description = string.IsNullOrWhiteSpace(l.Description) ? product!.Name : l.Description.Trim(), Unit = string.IsNullOrEmpty(unit) ? "adet" : unit,
                Quantity = l.Quantity, UnitPrice = l.UnitPrice, VatIncluded = l.VatIncluded, DiscountPercent = l.DiscountPercent is > 0m ? l.DiscountPercent : null,
                VatRate = vatRate, IsFixedPrice = l.ProductId is null || l.FixedPrice
            });
        }

        try {
            RecurringRules.Totals(lines);
        } catch (ArgumentException e) {
            throw SalesErrors.Bad("invalid_amounts", e.Message);
        }

        template.Name             = string.IsNullOrWhiteSpace(input.Name) ? cari.Name : input.Name.Trim();
        template.CurrentAccountId = cari.Id;
        template.BranchId         = input.BranchId;
        template.Currency         = input.Currency.Trim().ToUpperInvariant();
        template.Interval         = input.Interval;
        template.DayOfMonth       = input.DayOfMonth;
        template.StartDate        = input.StartDate;
        template.EndDate          = input.EndDate;
        template.Mode             = input.Mode;
        template.Notes            = string.IsNullOrWhiteSpace(input.Notes) ? null : input.Notes.Trim();
        template.Lines.Clear();
        foreach (var line in lines) template.Lines.Add(line);
    }
}

public sealed class RecurringMapper(
    IApplicationDbContext db,
    TimeProvider          clock
) {
    public async Task<RecurringDto> ToDtoAsync(RecurringTemplate t, CancellationToken ct) {
        var name      = await db.CurrentAccounts.AsNoTracking().Where(c => c.Id == t.CurrentAccountId).Select(c => c.Name).FirstOrDefaultAsync(ct) ?? string.Empty;
        var generated = await db.RecurringRuns.AsNoTracking().Where(r => r.RecurringTemplateId == t.Id).Select(r => r.Period).ToListAsync(ct);
        var (next, last) = RecurringRules.Periods(t, generated, Today());
        var calc  = RecurringRules.Totals(t.Lines);
        var lines = t.Lines.OrderBy(l => l.Sort).Select(l => new RecurringLineDto(l.Id, l.Sort, l.ProductId, l.VariantId, l.Description, l.Unit, l.Quantity, l.UnitPrice, l.VatIncluded,
                                                                                    l.DiscountPercent, l.VatRate, l.IsFixedPrice)).ToList();
        return new RecurringDto(t.Id, t.Number, t.Name, t.CurrentAccountId, name, t.BranchId, t.Currency, t.Interval, t.DayOfMonth, t.StartDate, t.EndDate, t.Mode, t.IsActive, t.Notes,
                                calc.Net, calc.Vat, calc.Payable, next, last, lines);
    }

    public DateOnly Today() => DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime.AddHours(3));
}
