using ERP.Application.Common.Exceptions;
using ERP.Application.Common.Interfaces;
using ERP.Domain.Invoicing;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Rates;

public sealed class ExchangeRates(
    IApplicationDbContext db
) : IExchangeRates {
    public const int LookBackDays = 10;

    public async Task<(decimal Rate, DateOnly EffectiveDate)> GetAsync(string currency, DateOnly date, RateType type, CancellationToken ct) {
        currency = currency.Trim().ToUpperInvariant();
        if (currency == "TRY") return (1m, date);

        var from = date.AddDays(-LookBackDays);
        var overrides = await db.TenantRateOverrides.AsNoTracking()
                            .Where(o => o.Currency == currency && o.Type == type && o.Date >= from && o.Date <= date)
                            .ToDictionaryAsync(o => o.Date, o => o.Rate, ct);
        var tcmb = await db.ExchangeRates.AsNoTracking()
                       .Where(r => r.Currency == currency && r.Type == type && r.Date >= from && r.Date <= date)
                       .ToDictionaryAsync(r => r.Date, r => r.Rate, ct);
        for (var day = date; day >= from; day = day.AddDays(-1)) {
            // Aynı gün için firmanın elle kuru TCMB'nin önündedir.
            if (overrides.TryGetValue(day, out var manual)) return (manual, day);
            if (tcmb.TryGetValue(day, out var rate)) return (rate, day);
        }

        throw new AppException(409, "rate_missing", $"{currency} için {date:dd.MM.yyyy} tarihinde ya da önceki {LookBackDays} günde kur bulunamadı.");
    }
}
