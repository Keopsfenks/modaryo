using ERP.Domain.Invoicing;

namespace ERP.Application.Common.Interfaces;

/// <summary>Belge tarihi için kur: firmanın elle kuru varsa o, yoksa TCMB. Tarihte kur yoksa en fazla 10 gün geriye gider.</summary>
public interface IExchangeRates {
    /// <summary>TRY için 1 döner. 10 gün içinde kur yoksa 409 rate_missing.</summary>
    Task<(decimal Rate, DateOnly EffectiveDate)> GetAsync(string currency, DateOnly date, RateType type, CancellationToken ct);
}

/// <summary>TCMB kur kaynağı. Testler sahte gerçekleme kullanır; gerçek siteye hiç gidilmez.</summary>
public interface ITcmbRateSource {
    /// <summary>date null ise bugünün (today.xml) kurları. O gün bülten yoksa (tatil, hafta sonu) null.</summary>
    Task<IReadOnlyList<ExchangeRate>?> FetchAsync(DateOnly? date, CancellationToken ct);
}
