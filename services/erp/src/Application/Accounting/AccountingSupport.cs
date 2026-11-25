using ERP.Application.Common.Interfaces;
using ERP.Application.Settings;
using ERP.Application.Settings.Categories;
using ERP.Domain.CurrentAccounts;
using ERP.Domain.Treasury;

namespace ERP.Application.Accounting;

/// <summary>Hesap eşlemesi + cari alt hesap ayarı; belge tarafı hesap kodlarını üretir.</summary>
public sealed class AccountCodes(AccountMapping map, bool subAccountPerCari) {
    public AccountMapping Map => map;

    /// <summary>Cari hesap kodu: ayar açıksa {taban}.{cariKodu}.</summary>
    public string Sub(string baseCode, CurrentAccount? cari) {
        return subAccountPerCari && cari is not null ? $"{baseCode}.{cari.Code}" : baseCode;
    }

    /// <summary>Müşteri tarafı (120); yalnızca personel olan cari 335.</summary>
    public string Customer(CurrentAccount? cari) {
        return Sub(cari is { IsEmployee: true, IsCustomer: false, IsSupplier: false } ? map.Employees : map.Customers, cari);
    }

    /// <summary>Tedarikçi tarafı (320); yalnızca personel olan cari 335.</summary>
    public string Supplier(CurrentAccount? cari) {
        return Sub(cari is { IsEmployee: true, IsCustomer: false, IsSupplier: false } ? map.Employees : map.Suppliers, cari);
    }

    public string Treasury(TreasuryAccount? account) {
        return account is { Kind: TreasuryAccountKind.Cash } ? map.Cash : map.Bank;
    }

    public string InstrumentPortfolio(InstrumentKind kind, InstrumentDirection direction) {
        return direction == InstrumentDirection.Received
            ? kind == InstrumentKind.Check ? map.ChecksReceived : map.NotesReceived
            : kind == InstrumentKind.Check ? map.ChecksIssued : map.NotesPayable;
    }
}

/// <summary>Projektörlerin ortak bağlamı: hesap kodları, TL çevirimi, cari kayıtları.</summary>
public sealed class AccountingContext(ITenantSettings settings, IExchangeRates rates, IApplicationDbContext db) {
    private readonly Dictionary<(string, DateOnly), decimal> _rates = new();
    private FinanceSettings? _settings;

    public async Task<AccountCodes> CodesAsync(AccountMapping map, CancellationToken ct) {
        _settings ??= await settings.GetAsync<FinanceSettings>(ct);
        return new AccountCodes(map, _settings.SubAccountPerCari);
    }

    /// <summary>Tutarı TL'ye çevirir. Kur verilmezse ayarlardaki kur türüyle TCMB kuru (bulunamazsa 409 rate_missing).</summary>
    public async Task<decimal> TlAsync(decimal amount, string currency, DateOnly date, decimal? rate, CancellationToken ct) {
        if (currency == "TRY") return amount;

        if (rate is { } given) return Round(amount * given);

        if (!_rates.TryGetValue((currency, date), out var cached)) {
            _settings ??= await settings.GetAsync<FinanceSettings>(ct);
            cached = (await rates.GetAsync(currency, date, _settings.RateType, ct)).Rate;
            _rates[(currency, date)] = cached;
        }

        return Round(amount * cached);
    }

    public async Task<IReadOnlyDictionary<Guid, CurrentAccount>> CarisAsync(IEnumerable<Guid?> ids, CancellationToken ct) {
        var wanted = ids.Where(i => i is not null).Select(i => i!.Value).Distinct().ToList();
        if (wanted.Count == 0) return new Dictionary<Guid, CurrentAccount>();

        return await db.CurrentAccounts.AsNoTracking().Where(c => wanted.Contains(c.Id)).ToDictionaryAsync(c => c.Id, ct);
    }

    public static decimal Round(decimal value) {
        return Math.Round(value, 2, MidpointRounding.AwayFromZero);
    }

    /// <summary>İptal tarihi (Türkiye saati) → tarih.</summary>
    public static DateOnly DateOf(DateTimeOffset moment) {
        return DateOnly.FromDateTime(moment.ToOffset(TimeSpan.FromHours(3)).DateTime);
    }
}

/// <summary>Fiş satırı kurucu: sıfır tutarlı satırlar yazılmaz.</summary>
public sealed class LineSet {
    private readonly List<JournalLine> _lines = [];

    public IReadOnlyList<JournalLine> Lines => _lines;

    public LineSet Debit(string code, decimal amount, string description, Guid? cariId = null) {
        if (amount != 0m) _lines.Add(new JournalLine(code, amount, 0m, description, cariId));
        return this;
    }

    public LineSet Credit(string code, decimal amount, string description, Guid? cariId = null) {
        if (amount != 0m) _lines.Add(new JournalLine(code, 0m, amount, description, cariId));
        return this;
    }
}

public static class VoucherRules {
    public const string ReversalSuffix = "-IPT";

    public static bool IsBalanced(ProjectedVoucher voucher) {
        return voucher.Lines.Sum(l => l.Debit) == voucher.Lines.Sum(l => l.Credit);
    }

    /// <summary>Ters fiş: borç/alacak yer değiştirir; iptal tarihine yazılır.</summary>
    public static ProjectedVoucher Reverse(ProjectedVoucher original, DateOnly date) {
        return new ProjectedVoucher(original.VoucherNo + ReversalSuffix, date, original.DocumentType, original.DocumentNo,
                                    original.Lines.Select(l => new JournalLine(l.AccountCode, l.Credit, l.Debit, "İptal: " + l.Description, l.CurrentAccountId)).ToList());
    }

    public static bool InRange(DateOnly date, DateOnly from, DateOnly to) {
        return date >= from && date <= to;
    }
}
