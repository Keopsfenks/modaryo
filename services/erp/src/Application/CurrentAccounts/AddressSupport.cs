using ERP.Application.Common.Interfaces;
using ERP.Domain.CurrentAccounts;

namespace ERP.Application.CurrentAccounts;

public sealed class AddressInputValidator : AbstractValidator<AddressInput> {
    public AddressInputValidator() {
        RuleFor(a => a.Title).NotEmpty().MaximumLength(100)
            .WithMessage("Adres başlığı 1 ile 100 karakter arasında olmalıdır.");
        RuleFor(a => a.Line).NotEmpty().MaximumLength(500)
            .WithMessage("Adres 1 ile 500 karakter arasında olmalıdır.");
        RuleFor(a => a.District).MaximumLength(100).WithMessage("İlçe en çok 100 karakter olabilir.");
        RuleFor(a => a.City).MaximumLength(100).WithMessage("Şehir en çok 100 karakter olabilir.");
        RuleFor(a => a.PostalCode).MaximumLength(20).WithMessage("Posta kodu en çok 20 karakter olabilir.");
        RuleFor(a => a.Country).MaximumLength(100).WithMessage("Ülke en çok 100 karakter olabilir.");
        RuleFor(a => a).Must(a => a.IsBilling || a.IsShipping)
            .WithMessage("Adres fatura veya sevk adresi olarak işaretlenmelidir.");
    }
}

public static class AddressRules {
    public static void Apply(CurrentAccountAddress address, AddressInput input) {
        address.Title      = input.Title.Trim();
        address.Line       = input.Line.Trim();
        address.District   = CardRules.Clean(input.District);
        address.City       = CardRules.Clean(input.City);
        address.PostalCode = CardRules.Clean(input.PostalCode);
        address.Country    = CardRules.Clean(input.Country) ?? "Türkiye";
        address.IsBilling  = input.IsBilling;
        address.IsShipping = input.IsShipping;
    }

    /// <summary>
    ///     Fatura ve sevk türleri birbirinden bağımsız çözülür. <paramref name="all" /> kartın tüm adresleridir (en eskiden
    ///     yeniye).
    ///     Değişen adres o türdeyse ve varsayılan istenmişse ya da türün tek adresiyse varsayılan olur, diğerlerinin işareti
    ///     kalkar.
    ///     Türü olmayan adresin varsayılan işareti kaldırılır. Sonunda her türde (adres varsa) tam bir varsayılan bulunur.
    /// </summary>
    public static void ResolveDefaults(IReadOnlyList<CurrentAccountAddress> all,
                                       CurrentAccountAddress                changed,
                                       bool                                 requestedDefaultBilling,
                                       bool                                 requestedDefaultShipping
    ) {
        var billing = all.Where(a => a.IsBilling).ToList();
        // false (veya gönderilmemiş) bayrak mevcut varsayılanı düşürmez; varsayılan yalnızca başka adres seçilince,
        // tür kapatılınca ya da silinince taşınır.
        changed.IsDefaultBilling = changed.IsBilling &&
                                   (requestedDefaultBilling || changed.IsDefaultBilling || billing.Count == 1);
        if (changed.IsDefaultBilling)
            billing.Where(a => a != changed).ToList().ForEach(a => a.IsDefaultBilling = false);

        var shipping = all.Where(a => a.IsShipping).ToList();
        changed.IsDefaultShipping = changed.IsShipping &&
                                    (requestedDefaultShipping || changed.IsDefaultShipping || shipping.Count == 1);
        if (changed.IsDefaultShipping)
            shipping.Where(a => a != changed).ToList().ForEach(a => a.IsDefaultShipping = false);

        EnsureDefaults(all);
    }

    /// <summary>Varsayılanı kalmayan türde en eski adresi varsayılan yapar.</summary>
    public static void EnsureDefaults(IReadOnlyList<CurrentAccountAddress> all) {
        var billing = all.Where(a => a.IsBilling).ToList();
        if (billing.Count > 0 && !billing.Any(a => a.IsDefaultBilling)) billing[0].IsDefaultBilling = true;

        var shipping = all.Where(a => a.IsShipping).ToList();
        if (shipping.Count > 0 && !shipping.Any(a => a.IsDefaultShipping)) shipping[0].IsDefaultShipping = true;
    }

    /// <summary>Kartın adreslerini en eskiden yeniye (Guid v7 sırası, veritabanında) getirir; kart yoksa 404.</summary>
    public static async Task<List<CurrentAccountAddress>> LoadAsync(IApplicationDbContext db,
                                                                    Guid                  accountId,
                                                                    CancellationToken     ct
    ) {
        if (!await db.CurrentAccounts.AnyAsync(a => a.Id == accountId, ct))
            throw new NotFoundException(accountId.ToString(), "CurrentAccount");

        return await db.CurrentAccountAddresses.Where(a => a.CurrentAccountId == accountId).OrderBy(a => a.Id)
                   .ToListAsync(ct);
    }
}