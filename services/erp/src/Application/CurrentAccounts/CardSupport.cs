using System.Globalization;
using System.Text.RegularExpressions;
using ERP.Application.Common.Exceptions;
using ERP.Application.Common.Interfaces;
using ERP.Application.CurrentAccounts.Allocation;
using ERP.Domain.Common;
using ERP.Domain.CurrentAccounts;

namespace ERP.Application.CurrentAccounts;

public static class CardRules {
    public const decimal MaxAmount = 999_999_999_999.99m;

    public const           string   DateMessage = "Tarih 01.01.2000 ile 31.12.2100 arasında olmalıdır.";
    public static readonly string[] Currencies  = ["TRY", "USD", "EUR", "GBP"];

    public static readonly DateOnly MinDate = new(2000, 1, 1);

    public static readonly DateOnly MaxDate = new(2100, 12, 31);

    /// <summary>En fazla 2 ondalık basamak (veritabanında sessiz yuvarlamayı önler).</summary>
    public static bool HasValidScale(decimal amount) {
        return decimal.Round(amount, 2) == amount;
    }

    public static bool IsValidDate(DateOnly date) {
        return date >= MinDate && date <= MaxDate;
    }

    /// <summary>Arama terimini veritabanındaki lower(...).Replace("ı","i") ile aynı biçimde katlar (Türkçe I/ı/İ/i).</summary>
    public static string FoldSearch(string search) {
        return TurkishText.Fold(search);
    }

    public static void AddCardRules<T>(this AbstractValidator<T> v) where T : ICardFields {
        v.RuleFor(c => c.Name).NotEmpty().WithMessage("Ünvan / ad soyad zorunludur.")
            .Must(n => n is null || n.Trim().Length is >= 2 and <= 200)
            .WithMessage("Ünvan 2 ile 200 karakter arasında olmalıdır.");
        v.RuleFor(c => c.Code).NotEmpty().WithMessage("Cari kodu zorunludur.")
            .Must(c => c is null || c.Trim().Length is >= 1 and <= 20)
            .WithMessage("Cari kodu 1 ile 20 karakter arasında olmalıdır.");
        v.RuleFor(c => c.DefaultDueDays).InclusiveBetween(0, 365)
            .WithMessage("Varsayılan vade 0 ile 365 gün arasında olmalıdır.");
        v.RuleFor(c => c.CreditLimit).GreaterThanOrEqualTo(0m).When(c => c.CreditLimit is not null)
            .WithMessage("Kredi limiti 0 değerinden küçük olamaz.");
        v.RuleFor(c => c.CreditLimit).LessThanOrEqualTo(MaxAmount).When(c => c.CreditLimit is not null)
            .WithMessage("Kredi limiti 999.999.999.999,99 değerinden büyük olamaz.");
        v.RuleFor(c => c.CreditLimit).Must(l => HasValidScale(l!.Value)).When(c => c.CreditLimit is not null)
            .WithMessage("Kredi limiti en fazla 2 ondalık basamak içerebilir.");
        v.RuleFor(c => c.Kind).IsInEnum().WithMessage("Cari türü geçersiz.");
        v.RuleFor(c => c.Currency).Must(c => Currencies.Contains(c))
            .WithMessage("Para birimi TRY, USD, EUR veya GBP olmalıdır.");
        v.RuleFor(c => c).Must(c => c.IsCustomer || c.IsSupplier || c.IsEmployee)
            .WithName("IsCustomer").WithMessage("Cari en az müşteri, tedarikçi veya personel olarak işaretlenmelidir.");
        v.RuleFor(c => c.TaxNumber)
            .Must((c, t) => c.Kind == AccountKind.Corporate
                                ? TaxNumber.IsValidVkn(t!.Trim())
                                : TaxNumber.IsValidTckn(t!.Trim()))
            .When(c => !string.IsNullOrWhiteSpace(c.TaxNumber))
            .WithMessage(c => c.Kind == AccountKind.Corporate
                                  ? "Geçerli bir vergi kimlik numarası (VKN) girin."
                                  : "Geçerli bir T.C. kimlik numarası girin.");
        v.RuleFor(c => c.Email).EmailAddress().MaximumLength(200).When(c => !string.IsNullOrWhiteSpace(c.Email))
            .WithMessage("Geçerli bir e-posta adresi girin.");
        v.RuleFor(c => c.TaxOffice).MaximumLength(100).WithMessage("Vergi dairesi en çok 100 karakter olabilir.");
        v.RuleFor(c => c.Phone).MaximumLength(50).WithMessage("Telefon en çok 50 karakter olabilir.");
        v.RuleFor(c => c.Notes).MaximumLength(2000).WithMessage("Not en çok 2000 karakter olabilir.");
    }

    public static string? Clean(string? value) {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    public static void Apply(CurrentAccount account, ICardFields f) {
        account.Code               = f.Code.Trim();
        account.Name               = f.Name.Trim();
        account.Kind               = f.Kind;
        account.TaxNumber          = Clean(f.TaxNumber);
        account.TaxOffice          = Clean(f.TaxOffice);
        account.IsEInvoiceTaxpayer = f.IsEInvoiceTaxpayer;
        account.IsCustomer         = f.IsCustomer;
        account.IsSupplier         = f.IsSupplier;
        account.IsEmployee         = f.IsEmployee;
        account.Phone              = Clean(f.Phone);
        account.Email              = Clean(f.Email);
        account.Notes              = Clean(f.Notes);
        account.Currency           = f.Currency;
        account.DefaultDueDays     = f.DefaultDueDays;
        account.CreditLimit        = f.CreditLimit;
        account.CustomerGroupId    = f.CustomerGroupId;
        account.SalesRepUserId     = f.SalesRepUserId;
    }

    /// <summary>Satış temsilcisi verilmişse aktif firmanın üyesi olmalı.</summary>
    public static async Task EnsureSalesRepAsync(IMemberDirectory members, ICurrentTenant tenant, Guid? userId, CancellationToken ct) {
        if (userId is not { } id) return;

        var tenantId = tenant.TenantId ?? throw new InvalidOperationException("Aktif firma yok.");
        if (await members.GetAsync(tenantId, id, ct) is null)
            throw new AppException(400, "sales_rep_invalid", "Satış temsilcisi bu firmanın üyesi değil.");
    }

    /// <summary>Müşteri grubu verilmişse aktif firmaya ait olmalı (başka firmanın grubu sorgu filtresiyle görünmez).</summary>
    public static async Task EnsureCustomerGroupAsync(IApplicationDbContext db, Guid? groupId, CancellationToken ct) {
        if (groupId is { } id && !await db.CustomerGroups.AnyAsync(g => g.Id == id, ct))
            throw new AppException(400, "invalid_customer_group", "Müşteri grubu bulunamadı.");
    }
}

public static class CariErrors {
    public static AppException CodeTaken() {
        return new AppException(409, "code_taken", "Bu cari kodu bu firmada zaten kullanılıyor.");
    }

    public static AppException TaxNumberTaken() {
        return new AppException(409, "tax_number_taken", "Bu vergi numarası başka bir cariye kayıtlı.");
    }

    public static AppException HasMovements() {
        return new AppException(409, "has_movements",
                                "Hareketi olan cari silinemez. Bunun yerine pasifleştirebilirsiniz.");
    }

    public static AppException AccountInactive() {
        return new AppException(409, "account_inactive", "Pasif cariye hareket girilemez.");
    }

    public static AppException OpeningExists() {
        return new AppException(409, "opening_exists",
                                "Bu cari için zaten bir açılış bakiyesi var. Önce mevcut açılışı iptal edin.");
    }

    public static AppException NotManual() {
        return new AppException(409, "not_manual", "Yalnızca elle girilen hareketler iptal edilebilir.");
    }

    public static AppException AlreadyCancelled() {
        return new AppException(409, "already_cancelled", "Bu hareket zaten iptal edilmiş.");
    }

    public static AppException CurrencyLocked() {
        return new AppException(409, "currency_locked", "Hareketi olan carinin para birimi değiştirilemez.");
    }

    /// <summary>Kayıt sırasında benzersiz indeks çakışması (yarış durumu) ise ilgili 409 hatasını verir.</summary>
    public static AppException? FromUniqueViolation(DbUpdateException ex) {
        var inner = ex.InnerException;
        if (inner is null || inner.GetType().GetProperty("SqlState")?.GetValue(inner) as string != "23505") return null;

        var constraint = inner.GetType().GetProperty("ConstraintName")?.GetValue(inner) as string ?? string.Empty;
        if (constraint.Contains("_Opening", StringComparison.Ordinal)) return OpeningExists();

        if (constraint.Contains("TaxNumber", StringComparison.Ordinal)) return TaxNumberTaken();

        return constraint.Contains("Code", StringComparison.Ordinal) ? CodeTaken() : null;
    }
}

public static class CurrentAccountClock {
    /// <summary>İstanbul saatiyle bugün.</summary>
    public static DateOnly Today(TimeProvider time) {
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(time.GetUtcNow(), ResolveIstanbul()).DateTime);
    }

    /// <summary>Verilen anın İstanbul duvar saati.</summary>
    public static DateTime ToIstanbul(DateTimeOffset at) {
        return TimeZoneInfo.ConvertTime(at, ResolveIstanbul()).DateTime;
    }

    /// <summary>tzdata olmayan ana makinede çökmemesi için UTC+3'e düşer (Türkiye 2016'dan beri yaz saati uygulamıyor).</summary>
    private static TimeZoneInfo ResolveIstanbul() {
        try {
            return TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul");
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException) {
            return TimeZoneInfo.CreateCustomTimeZone("Europe/Istanbul", TimeSpan.FromHours(3), "Istanbul", "Istanbul");
        }
    }
}

public static class NextCodeCalculator {
    private static readonly Regex Pattern = new(@"^C\d{1,15}$", RegexOptions.Compiled);

    public static string Next(IEnumerable<string> codes) {
        var max = codes.Where(c => Pattern.IsMatch(c)).Select(c => long.Parse(c[1..])).DefaultIfEmpty(0).Max();
        return $"C{max + 1:D4}";
    }
}

public static class CurrentAccountReader {
    /// <summary>Vadesi geçmiş tahsil edilecek açık kalemlerin toplamı.</summary>
    public static decimal Overdue(IEnumerable<OpenItem> openItems) {
        return openItems
            .Where(i => i.Direction == DueDirection.Receivable && i.DaysOverdue > 0)
            .Sum(i => i.Remaining);
    }

    public static async Task<CurrentAccountDto> GetDtoAsync(IApplicationDbContext db,
                                                            Guid                  id,
                                                            DateOnly              today,
                                                            CancellationToken     ct
    ) {
        var account = await db.CurrentAccounts.AsNoTracking().Include(a => a.Addresses)
                          .FirstOrDefaultAsync(a => a.Id == id, ct)
                      ?? throw new NotFoundException(id.ToString(), "CurrentAccount");
        var entries   = await db.LedgerEntries.AsNoTracking().Where(e => e.CurrentAccountId == id).ToListAsync(ct);
        var openItems = await new OpenItemsReader(db).ForAccountAsync(id, today, ct);

        return new CurrentAccountDto(
                                     account.Id, account.Code, account.Name, account.Kind, account.TaxNumber,
                                     account.TaxOffice,
                                     account.IsEInvoiceTaxpayer, account.IsCustomer, account.IsSupplier, account.IsEmployee, account.Phone,
                                     account.Email, account.Notes,
                                     account.Currency, account.DefaultDueDays, account.CreditLimit, account.CustomerGroupId, account.SalesRepUserId,
                                     account.IsActive,
                                     Statement.Balance(entries), Overdue(openItems), entries.Count > 0,
                                     account.Addresses.OrderBy(a => a.Title).ThenBy(a => a.Id).Select(ToDto).ToList());
    }

    public static AddressDto ToDto(CurrentAccountAddress a) {
        return new AddressDto(a.Id, a.Title, a.Line, a.District, a.City, a.PostalCode,
                              a.Country, a.IsBilling, a.IsShipping, a.IsDefaultBilling, a.IsDefaultShipping);
    }
}