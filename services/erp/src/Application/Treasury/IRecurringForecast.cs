namespace ERP.Application.Treasury;

/// <summary>Önümüzdeki tekrarlayan fatura üretimlerinden beklenen tahsilat (fatura para biriminde, KDV dahil).</summary>
public sealed record RecurringForecastItem(Guid TemplateId, DateOnly Date, decimal Amount, string Currency);

/// <summary>Nakit akışı raporu için tekrarlayan faturaların tahmini. Satış modülü uygular; üretilmiş dönemler (fatura vadesi olarak zaten sayılır) dahil edilmez.</summary>
public interface IRecurringForecast {
    /// <summary>from ile to (dahil) arasındaki, henüz üretilmemiş dönemler. Duraklatılmış ve bitmiş şablonlar dışarıda kalır.</summary>
    Task<IReadOnlyList<RecurringForecastItem>> UpcomingAsync(DateOnly from, DateOnly to, CancellationToken ct);
}
