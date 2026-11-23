using System.Globalization;
using ERP.Application.Common.Exceptions;
using ERP.Application.Common.Interfaces;

namespace ERP.Application.Pricing;

public static class PricingPermissions {
    public const string Read     = "fiyat.read";
    public const string Manage   = "fiyat.manage";
    public const string Purchase = "fiyat.alis";
    public const string Settings = "ayarlar.fiyat";
}

public static class DiscountPolicy {
    private static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");

    /// <summary>
    ///     Satır ve belge iskontosundan sonraki net etkiyle yüzde: 1 - net / brüt.
    ///     Örn. %5 satır + %4 belge = %8,8 (toplamları %9 değildir).
    /// </summary>
    public static decimal EffectivePercent(decimal gross, decimal lineDiscountPercent, decimal documentDiscountPercent) {
        if (gross <= 0m) return 0m;

        var net = gross * (1m - lineDiscountPercent / 100m);
        net -= net * documentDiscountPercent / 100m;
        return Math.Round((1m - net / gross) * 100m, 4);
    }

    /// <summary>
    ///     Etkin iskonto kullanıcının sınırını aşarsa 403 verir. Sahip oturumu sınır olarak 100 taşıdığı için
    ///     doğal olarak muaftır.
    /// </summary>
    public static void Ensure(decimal effectivePercent, ICurrentTenant tenant) {
        if (effectivePercent <= tenant.MaxDiscountPercent) return;

        throw new AppException(403, "discount_limit_exceeded",
                               $"İskonto sınırınız %{tenant.MaxDiscountPercent.ToString("0.##", Tr)}.");
    }
}
