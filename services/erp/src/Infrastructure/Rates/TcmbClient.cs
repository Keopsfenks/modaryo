using System.Globalization;
using System.Xml.Linq;
using ERP.Application.Common.Interfaces;
using ERP.Domain.Invoicing;

namespace ERP.Infrastructure.Rates;

/// <summary>TCMB günlük kur bülteni istemcisi (today.xml ve arşiv).</summary>
public sealed class TcmbClient(
    HttpClient http
) : ITcmbRateSource {
    public static readonly string[] Currencies = ["USD", "EUR", "GBP"];

    public async Task<IReadOnlyList<ExchangeRate>?> FetchAsync(DateOnly? date, CancellationToken ct) {
        var url = date is { } d
                      ? $"https://www.tcmb.gov.tr/kurlar/{d:yyyyMM}/{d:ddMMyyyy}.xml"
                      : "https://www.tcmb.gov.tr/kurlar/today.xml";
        using var response = await http.GetAsync(url, ct);
        // Tatil ve hafta sonları arşivde dosya yoktur.
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        var xml = await response.Content.ReadAsStringAsync(ct);
        var rows = Parse(xml);
        return rows.Count == 0 ? null : rows;
    }

    /// <summary>Bülten XML'ini ayrıştırır: USD/EUR/GBP için dört kur türü; birim (Unit) 1'e bölünür. Boş kur atlanır.</summary>
    public static IReadOnlyList<ExchangeRate> Parse(string xml) {
        var root = XDocument.Parse(xml).Root ?? throw new FormatException("TCMB bülteni boş.");
        var date = DateOnly.ParseExact(root.Attribute("Date")?.Value ?? throw new FormatException("TCMB bülten tarihi yok."),
                                       "MM/dd/yyyy", CultureInfo.InvariantCulture);
        var rows = new List<ExchangeRate>();
        foreach (var node in root.Elements("Currency")) {
            var code = node.Attribute("CurrencyCode")?.Value;
            if (code is null || !Currencies.Contains(code)) continue;
            var unit = Number(node.Element("Unit")?.Value) ?? 1m;
            if (unit <= 0) unit = 1m;
            foreach (var (tag, type) in new[] {
                         ("ForexBuying", RateType.ForexBuying), ("ForexSelling", RateType.ForexSelling),
                         ("BanknoteBuying", RateType.BanknoteBuying), ("BanknoteSelling", RateType.BanknoteSelling)
                     }) {
                if (Number(node.Element(tag)?.Value) is not { } value || value <= 0) continue;
                rows.Add(new ExchangeRate { Currency = code, Date = date, Type = type, Rate = Math.Round(value / unit, 6), Source = "tcmb" });
            }
        }

        return rows;
    }

    private static decimal? Number(string? text) {
        return decimal.TryParse(text?.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var v) ? v : null;
    }
}
