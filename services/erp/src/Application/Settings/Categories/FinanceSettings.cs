using ERP.Application.Common.Security;
using ERP.Domain.Invoicing;

namespace ERP.Application.Settings.Categories;

/// <summary>Muhasebe aktarımı hesap eşlemesi; varsayılanlar Tek Düzen Hesap Planı kodlarıdır.</summary>
public sealed record AccountMapping {
    public string Customers              { get; init; } = "120";
    public string Suppliers              { get; init; } = "320";
    public string Employees              { get; init; } = "335";
    public string AdvancesGiven          { get; init; } = "159";
    public string AdvancesReceived       { get; init; } = "340";
    public string Cash                   { get; init; } = "100";
    public string Bank                   { get; init; } = "102";
    public string ChecksReceived         { get; init; } = "101";
    public string ChecksIssued           { get; init; } = "103";
    public string NotesReceived          { get; init; } = "121";
    public string NotesPayable           { get; init; } = "321";
    public string Sales                  { get; init; } = "600";
    public string SalesReturns           { get; init; } = "610";
    public string SalesDiscounts         { get; init; } = "611";
    public string PurchasesInventory     { get; init; } = "153";
    public string VatOutput              { get; init; } = "391";
    public string VatInput               { get; init; } = "191";
    public string WithholdingVatPayable  { get; init; } = "360";
    public string FxGain                 { get; init; } = "646";
    public string FxLoss                 { get; init; } = "656";
    public string ExpenseDefault         { get; init; } = "770";

    public AccountMapping Normalize() {
        var d = new AccountMapping();
        return this with {
            Customers = Pick(Customers, d.Customers), Suppliers = Pick(Suppliers, d.Suppliers),
            Employees = Pick(Employees, d.Employees), AdvancesGiven = Pick(AdvancesGiven, d.AdvancesGiven),
            AdvancesReceived = Pick(AdvancesReceived, d.AdvancesReceived), Cash = Pick(Cash, d.Cash),
            Bank = Pick(Bank, d.Bank), ChecksReceived = Pick(ChecksReceived, d.ChecksReceived),
            ChecksIssued = Pick(ChecksIssued, d.ChecksIssued), NotesReceived = Pick(NotesReceived, d.NotesReceived),
            NotesPayable = Pick(NotesPayable, d.NotesPayable), Sales = Pick(Sales, d.Sales),
            SalesReturns = Pick(SalesReturns, d.SalesReturns), SalesDiscounts = Pick(SalesDiscounts, d.SalesDiscounts),
            PurchasesInventory = Pick(PurchasesInventory, d.PurchasesInventory), VatOutput = Pick(VatOutput, d.VatOutput),
            VatInput = Pick(VatInput, d.VatInput), WithholdingVatPayable = Pick(WithholdingVatPayable, d.WithholdingVatPayable),
            FxGain = Pick(FxGain, d.FxGain), FxLoss = Pick(FxLoss, d.FxLoss), ExpenseDefault = Pick(ExpenseDefault, d.ExpenseDefault)
        };
    }

    public IEnumerable<string> Codes() {
        yield return Customers; yield return Suppliers; yield return Employees; yield return AdvancesGiven;
        yield return AdvancesReceived; yield return Cash; yield return Bank; yield return ChecksReceived;
        yield return ChecksIssued; yield return NotesReceived; yield return NotesPayable; yield return Sales;
        yield return SalesReturns; yield return SalesDiscounts; yield return PurchasesInventory; yield return VatOutput;
        yield return VatInput; yield return WithholdingVatPayable; yield return FxGain; yield return FxLoss;
        yield return ExpenseDefault;
    }

    private static string Pick(string? value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
}

/// <summary>PeriodLockDate (dahil) ve öncesindeki tarihli kayıtlar kilitlidir.</summary>
public sealed record FinanceSettings(
    DateOnly? PeriodLockDate,
    RateType  RateType = RateType.ForexBuying
) : ISettingsCategory<FinanceSettings> {
    public FinanceSettings() : this((DateOnly?)null) { }

    /// <summary>Masraf/gelir kategorisi kodu → hesap kodu varsayılanları (kategoriler ilk kullanımda bunlarla açılır).</summary>
    public static readonly IReadOnlyDictionary<string, string> DefaultExpenseCategories = new Dictionary<string, string> {
        ["genel"]           = "770",
        ["kira"]            = "770",
        ["ulasim"]          = "770",
        ["banka-masrafi"]   = "780",
        ["kur-farki"]       = "656",
        ["genel-gelir"]     = "649",
        ["kur-farki-geliri"] = "646"
    };

    public AccountMapping AccountMapping { get; init; } = new();

    /// <summary>Açıksa cari hesaplar {taban}.{cariKodu} alt hesabıyla aktarılır.</summary>
    public bool SubAccountPerCari { get; init; }

    public Dictionary<string, string> ExpenseCategoryCodes { get; init; } = new(DefaultExpenseCategories);

    public static string Key => "finans";

    public static string Permission => SettingsPermissions.Finance;

    public static FinanceSettings Normalize(FinanceSettings s) {
        var categories = new Dictionary<string, string>(DefaultExpenseCategories);
        foreach (var (code, account) in s.ExpenseCategoryCodes ?? [])
            if (!string.IsNullOrWhiteSpace(code) && !string.IsNullOrWhiteSpace(account))
                categories[code.Trim().ToLowerInvariant()] = account.Trim();

        return s with { AccountMapping = (s.AccountMapping ?? new AccountMapping()).Normalize(), ExpenseCategoryCodes = categories };
    }
}

public sealed class FinanceSettingsValidator : AbstractValidator<FinanceSettings> {
    public FinanceSettingsValidator() {
        RuleFor(s => s.RateType).IsInEnum().WithMessage("Kur türü geçersiz.");
        RuleFor(s => s.AccountMapping).NotNull().WithMessage("Hesap eşlemesi zorunludur.");
        RuleFor(s => s.AccountMapping).Must(m => m is null || m.Codes().All(IsAccountCode))
            .WithMessage("Hesap kodları en fazla 30 karakter olup yalnızca rakam, nokta ve tire içerebilir.");
        RuleFor(s => s.ExpenseCategoryCodes).Must(c => c is null || c.Values.All(v => string.IsNullOrWhiteSpace(v) || IsAccountCode(v.Trim())))
            .WithMessage("Kategori hesap kodları en fazla 30 karakter olup yalnızca rakam, nokta ve tire içerebilir.");
    }

    private static bool IsAccountCode(string code) =>
        !string.IsNullOrWhiteSpace(code) && code.Trim().Length <= 30 && code.Trim().All(c => char.IsAsciiDigit(c) || c is '.' or '-');
}
