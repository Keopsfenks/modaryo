using ERP.Application.Common.Interfaces;
using ERP.Domain.Invoicing;
using ERP.Infrastructure.Rates;

namespace ERP.Web.FunctionalTests.Infrastructure;

/// <summary>Kayıtlı TCMB XML dosyalarından yanıt veren sahte kaynak. Kayıtsız tarih için bülten yok (null) döner.</summary>
public sealed class FakeTcmbSource : ITcmbRateSource {
    private readonly object                    _gate     = new();
    private readonly Dictionary<DateOnly, string> _bulletins = new();
    private          string?                   _today;

    public List<DateOnly?> Requests { get; } = new();

    public static string Fixture(string name) {
        return File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));
    }

    public void Reset() {
        lock (_gate) {
            _bulletins.Clear();
            _today = null;
            Requests.Clear();
        }
    }

    /// <summary>Arşivde o tarihin bülteni olarak sunulur.</summary>
    public void Publish(DateOnly date, string fixture) {
        lock (_gate) _bulletins[date] = Fixture(fixture);
    }

    /// <summary>today.xml bülteni.</summary>
    public void PublishToday(string fixture) {
        lock (_gate) _today = Fixture(fixture);
    }

    public Task<IReadOnlyList<ExchangeRate>?> FetchAsync(DateOnly? date, CancellationToken ct) {
        string? xml;
        lock (_gate) {
            Requests.Add(date);
            xml = date is null ? _today : _bulletins.GetValueOrDefault(date.Value);
        }

        return Task.FromResult<IReadOnlyList<ExchangeRate>?>(xml is null ? null : TcmbClient.Parse(xml));
    }
}
