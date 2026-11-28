using ERP.Application.Common.Exceptions;
using ERP.Application.Common.Interfaces;
using ERP.Application.Common.Security;
using ERP.Application.Pricing;
using ERP.Application.Settings;
using ERP.Application.Settings.Categories;
using ERP.Domain.CurrentAccounts;
using ERP.Domain.Invoicing;

namespace ERP.Application.Invoicing;

public static class InvoiceErrors {
    public static AppException Bad(string code, string message) {
        return new AppException(400, code, message);
    }

    public static AppException Conflict(string code, string message) {
        return new AppException(409, code, message);
    }

    public static AppException NotDraft(InvoiceStatus status) {
        return status == InvoiceStatus.Cancelled
                   ? Conflict("invoice_cancelled", "İptal edilmiş fatura değiştirilemez.")
                   : Conflict("invoice_not_draft", "Yalnızca taslak faturalar değiştirilebilir.");
    }

    public static AppException VersionConflict() {
        return Conflict("version_conflict", "Fatura başkası tarafından değiştirildi. Sayfayı yenileyip tekrar deneyin.");
    }

    public static AppException ExternalNumberTaken() {
        return Conflict("external_number_taken", "Bu tedarikçi için aynı fatura numarası daha önce girilmiş.");
    }
}

public static class InvoiceRules {
    public const string PurchasePermission = "fiyat.alis";

    public static readonly string[] Currencies = ["TRY", "USD", "EUR", "GBP"];

    public static bool IsPurchase(InvoiceKind kind) {
        return kind is InvoiceKind.Purchase or InvoiceKind.PurchaseReturn;
    }

    public static bool IsReturn(InvoiceKind kind) {
        return kind is InvoiceKind.SalesReturn or InvoiceKind.PurchaseReturn;
    }

    /// <summary>"sales" | "purchase" (seri grubu).</summary>
    public static string GroupOf(InvoiceKind kind) {
        return IsPurchase(kind) ? "purchase" : "sales";
    }

    /// <summary>Alış faturaları ve iadeleri her işlemde ayrıca fiyat.alis ister.</summary>
    public static void EnsureKindPermission(ICurrentSession session, InvoiceKind kind) {
        if (IsPurchase(kind) && !session.HasPermission(PurchasePermission)) throw new ForbiddenAccessException();
    }

    public static decimal Round(decimal value, int decimals = 2) {
        return Math.Round(value, decimals, MidpointRounding.AwayFromZero);
    }

    public static IReadOnlyList<string>? NormalizeSerials(IEnumerable<string>? serials) {
        var list = serials?.Select(x => x?.Trim() ?? string.Empty).Where(x => x.Length > 0).ToList();
        return list is { Count: > 0 } ? list : null;
    }

    /// <summary>
    ///     Stok etkili faturada seri/lot takipli kalem için seri numaraları (seri takibi) ya da lot bilgisi (lot takibi) girilmiş olmalıdır.
    ///     Hizmet, set ve takipsiz kalemler etkilenmez; sayı/miktar denetimi stok katmanında yapılır.
    /// </summary>
    public static void EnsureTrackingEntered(Domain.Catalog.Product product, IReadOnlyList<InvoiceLot>? lots, IReadOnlyList<string>? serials, string label) {
        if (!product.TracksSerialLot || product.IsBundle || product.Kind == Domain.Catalog.ProductKind.Service) return;

        var serial  = product.TrackingMode == Domain.Inventory.TrackingMode.Serial;
        var entered = serial ? serials is { Count: > 0 } : lots is { Count: > 0 };
        if (!entered)
            throw InvoiceErrors.Bad("tracking_required",
                                    $"{label}: \"{product.Code} - {product.Name}\" seri/lot takipli bir kalem; stok etkili faturada {(serial ? "seri numaraları" : "lot bilgisi")} girilmelidir.");
    }

    public static TotalsDto Totals(Invoice i) {
        return new TotalsDto(i.Gross, i.Discount, i.Net, i.Otv, i.Vat, i.Withheld, i.Payable);
    }

    public static TotalsDto TotalsTl(Invoice i) {
        return new TotalsDto(i.GrossTl, i.DiscountTl, i.NetTl, i.OtvTl, i.VatTl, i.WithheldTl, i.PayableTl);
    }

    public static LineDto ToDto(InvoiceLine l) {
        return new LineDto(l.Id, l.Sort, l.ProductId, l.VariantId, l.Description, l.Unit, l.Factor, l.Quantity, l.Width, l.Length, l.UnitPrice,
                           l.VatIncluded, l.DiscountPercent, l.DiscountAmount, l.VatRate, l.WithholdingCode, l.ExemptionCode, l.ExemptionReason,
                           l.OtvPercent, l.OtvPerUnit, l.Gross, l.LineDiscount, l.DocDiscount, l.Net, l.Otv, l.VatBase, l.Vat, l.Withheld, l.Total,
                           l.NetTl, l.OtvTl, l.VatTl, l.WithheldTl, l.TotalTl, l.SourceLineId, l.Lots, l.Serials);
    }

    public static IReadOnlyList<VatSummaryDto> VatSummary(IEnumerable<InvoiceLine> lines) {
        return lines.GroupBy(l => l.VatRate).OrderBy(g => g.Key).Select(g => new VatSummaryDto(g.Key, g.Sum(l => l.VatBase), g.Sum(l => l.Vat))).ToList();
    }

    public static InvoiceDto ToDto(Invoice i, string cariName) {
        var lines = i.Lines.OrderBy(l => l.Sort).ToList();
        return new InvoiceDto(i.Id, i.Kind, i.Status, i.Mode, i.Scenario, i.BranchId, i.SeriesId, i.Number, i.ExternalNumber, i.CurrentAccountId,
                              cariName, i.Date, i.Time, i.Currency, i.Rate, i.RateType, i.RateOverridden, i.DocDiscountPercent, i.DocDiscountAmount,
                              Totals(i), TotalsTl(i), i.AffectsStock, i.WarehouseId, i.PaymentTermDays, i.Notes, i.ReturnOfInvoiceId, i.CancelReason,
                              i.CancelledAt, i.SourceType, i.Version, i.BuyerOrSeller, lines.Select(ToDto).ToList(),
                              i.DueSchedule.OrderBy(d => d.Sort).Select(d => new DueDto(d.DueDate, d.Amount)).ToList(), VatSummary(lines));
    }

    /// <summary>Belge tutarının cari para birimindeki karşılığı (defter tutarı ve vade planı toplamı için).</summary>
    public static async Task<decimal> LedgerAmountAsync(IExchangeRates rates, string docCurrency, decimal payable, decimal payableTl, string cariCurrency,
                                                        DateOnly date, RateType type, CancellationToken ct) {
        if (cariCurrency == docCurrency) return payable;
        if (cariCurrency == "TRY") return payableTl;
        var (cariRate, _) = await rates.GetAsync(cariCurrency, date, type, ct);
        return Round(payableTl / cariRate);
    }
}

/// <summary>Girdi doğrulaması (biçim); iş kuralları <see cref="InvoiceBuilder" /> içindedir.</summary>
public sealed class InvoiceInputValidator : AbstractValidator<InvoiceInput> {
    public InvoiceInputValidator() {
        RuleFor(i => i.Kind).IsInEnum().WithMessage("Fatura türü geçersiz.");
        RuleFor(i => i.BranchId).NotEmpty().WithMessage("Şube seçilmelidir.");
        RuleFor(i => i.CurrentAccountId).NotEmpty().WithMessage("Cari seçilmelidir.");
        RuleFor(i => i.Date).Must(d => d != default).WithMessage("Fatura tarihi zorunludur.");
        RuleFor(i => i.Currency).Must(c => c is not null && InvoiceRules.Currencies.Contains(c.Trim().ToUpperInvariant()))
            .WithMessage("Para birimi TRY, USD, EUR ya da GBP olmalıdır.");
        RuleFor(i => i.Rate).GreaterThan(0m).LessThan(1_000_000m).When(i => i.Rate is not null).WithMessage("Kur sıfırdan büyük olmalıdır.")
            .Must(r => r is null || Math.Round(r.Value, 6) == r).WithMessage("Kur en fazla 6 ondalık basamak içerebilir.");
        RuleFor(i => i.ExternalNumber).MaximumLength(50).WithMessage("Tedarikçi fatura numarası en fazla 50 karakter olabilir.");
        RuleFor(i => i.Notes).MaximumLength(1000).WithMessage("Not en fazla 1000 karakter olabilir.");
        RuleFor(i => i.PaymentTermDays).InclusiveBetween(0, 3650).When(i => i.PaymentTermDays is not null).WithMessage("Vade günü 0 ile 3650 arasında olmalıdır.");
        RuleFor(i => i.DocDiscountPercent).InclusiveBetween(0m, 100m).When(i => i.DocDiscountPercent is not null)
            .WithMessage("Belge iskonto yüzdesi 0 ile 100 arasında olmalıdır.");
        RuleFor(i => i.DocDiscountAmount).GreaterThanOrEqualTo(0m).When(i => i.DocDiscountAmount is not null)
            .WithMessage("Belge iskonto tutarı negatif olamaz.");
        RuleFor(i => i).Must(i => i.DocDiscountPercent is null || i.DocDiscountAmount is null).WithName("DocDiscountPercent")
            .WithMessage("Belge iskontosu yüzde ve tutar olarak birlikte verilemez.");
        RuleFor(i => i.Lines).NotEmpty().WithMessage("Faturada en az bir satır olmalıdır.")
            .Must(l => l.Count <= 500).WithMessage("Faturada en fazla 500 satır olabilir.");
        RuleForEach(i => i.Lines).SetValidator(new LineInputValidator());
        RuleForEach(i => i.DueSchedule!).ChildRules(d => {
            d.RuleFor(x => x.Amount).GreaterThan(0m).WithMessage("Vade tutarı sıfırdan büyük olmalıdır.");
            d.RuleFor(x => x.DueDate).Must(x => x != default).WithMessage("Vade tarihi zorunludur.");
        }).When(i => i.DueSchedule is not null);
    }
}

public sealed class LineInputValidator : AbstractValidator<LineInput> {
    public LineInputValidator() {
        RuleFor(l => l.Description).MaximumLength(500).WithMessage("Satır açıklaması en fazla 500 karakter olabilir.");
        RuleFor(l => l.Unit).MaximumLength(20).WithMessage("Birim en fazla 20 karakter olabilir.");
        RuleFor(l => l.Quantity).GreaterThanOrEqualTo(0m).WithMessage("Miktar negatif olamaz.")
            .Must(q => Math.Round(q, 6) == q).WithMessage("Miktar en fazla 6 ondalık basamak içerebilir.");
        RuleFor(l => l.UnitPrice).GreaterThanOrEqualTo(0m).WithMessage("Birim fiyat negatif olamaz.")
            .Must(p => Math.Round(p, 6) == p).WithMessage("Birim fiyat en fazla 6 ondalık basamak içerebilir.");
        RuleFor(l => l.Width).GreaterThanOrEqualTo(0m).When(l => l.Width is not null).WithMessage("En negatif olamaz.");
        RuleFor(l => l.Length).GreaterThanOrEqualTo(0m).When(l => l.Length is not null).WithMessage("Boy negatif olamaz.");
        RuleFor(l => l.DiscountPercent).InclusiveBetween(0m, 100m).When(l => l.DiscountPercent is not null)
            .WithMessage("Satır iskonto yüzdesi 0 ile 100 arasında olmalıdır.");
        RuleFor(l => l.DiscountAmount).GreaterThanOrEqualTo(0m).When(l => l.DiscountAmount is not null).WithMessage("Satır iskonto tutarı negatif olamaz.");
        RuleFor(l => l).Must(l => l.DiscountPercent is null || l.DiscountAmount is null).WithName("DiscountPercent")
            .WithMessage("Satır iskontosu yüzde ve tutar olarak birlikte verilemez.");
        RuleFor(l => l.VatRate).Must(r => InvoiceCalculator.AllowedVatRates.Contains(r!.Value)).When(l => l.VatRate is not null)
            .WithMessage("KDV oranı 0, 1, 10 ya da 20 olmalıdır.");
        RuleFor(l => l.OtvPercent).GreaterThanOrEqualTo(0m).When(l => l.OtvPercent is not null).WithMessage("ÖTV oranı negatif olamaz.");
        RuleFor(l => l.OtvPerUnit).GreaterThanOrEqualTo(0m).When(l => l.OtvPerUnit is not null).WithMessage("ÖTV tutarı negatif olamaz.");
        RuleFor(l => l.ExemptionReason).MaximumLength(500).WithMessage("İstisna nedeni en fazla 500 karakter olabilir.");
    }
}

/// <summary>Fatura girdisini doğrular, çözümler ve hesaplar (taslak, güncelleme ve önizleme için ortak).</summary>
public sealed class InvoiceBuilder(
    IApplicationDbContext db,
    ICurrentTenant        tenant,
    ICurrentSession       session,
    IBranchDirectory      branches,
    IExchangeRates        rates,
    ITenantSettings       settings
) {
    public sealed record Built(
        CurrentAccount       Cari,
        string               Currency,
        decimal              Rate,
        DateOnly?            RateDate,
        bool                 RateOverridden,
        RateType             RateType,
        int                  PaymentTermDays,
        List<InvoiceLine>    Lines,
        TotalsDto            Totals,
        TotalsDto            TotalsTl,
        List<InvoiceDueLine> Due,
        decimal              LedgerAmount);

    /// <param name="systemGenerated">
    ///     Sistemin kendi ürettiği belge (ör. karantina reddinden alış iade taslağı): alış faturası için oturumun fiyat yetkisi aranmaz.
    ///     Fiyat görünürlüğü DTO düzeyinde ayrıca kapalı kalır.
    /// </param>
    public async Task<Built> BuildAsync(InvoiceInput input, Guid? existingInvoiceId, bool preview, CancellationToken ct, bool systemGenerated = false) {
        if (!systemGenerated) InvoiceRules.EnsureKindPermission(session, input.Kind);
        if (!systemGenerated && input.Lines.Any(l => l.VatOverride is not null)) throw InvoiceErrors.Bad("invalid_amounts", "Açık KDV tutarı yalnızca sistem üretimi belgelerde kullanılabilir.");
        if (input.Kind == InvoiceKind.ExchangeDifference)
            throw InvoiceErrors.Bad("invalid_kind", "Kur farkı faturası elle oluşturulamaz.");

        BranchGuard.Ensure(tenant, input.BranchId);
        var tenantId = tenant.TenantId ?? throw new InvalidOperationException("Aktif firma yok.");
        if (!await branches.ExistsActiveAsync(tenantId, input.BranchId, ct)) throw InvoicingErrors.BranchNotFound();

        var cari = await db.CurrentAccounts.AsNoTracking().FirstOrDefaultAsync(c => c.Id == input.CurrentAccountId, ct)
                   ?? throw new NotFoundException(input.CurrentAccountId.ToString(), "CurrentAccount");
        EnsureCari(cari, input.Kind);

        var currency = input.Currency.Trim().ToUpperInvariant();
        var type     = (await settings.GetAsync<FinanceSettings>(ct)).RateType;
        var (rate, rateDate, overridden) = await ResolveRateAsync(currency, input.Rate, input.Date, type, ct);

        var isPurchase = InvoiceRules.IsPurchase(input.Kind);
        if (isPurchase && !preview && string.IsNullOrWhiteSpace(input.ExternalNumber))
            throw InvoiceErrors.Bad("external_number_required", "Alış faturasında tedarikçinin fatura numarası zorunludur.");
        if (isPurchase && !preview && !string.IsNullOrWhiteSpace(input.ExternalNumber) && await ExternalNumberExistsAsync(input.CurrentAccountId, input.ExternalNumber!.Trim(), existingInvoiceId, ct))
            throw InvoiceErrors.ExternalNumberTaken();

        await EnsureWarehouseAsync(input, ct);
        var original = await LoadReturnOriginalAsync(input, cari, ct);

        var lines = await ResolveLinesAsync(input, preview, ct);

        // Mal kabulden kesilen alış faturalarında satır bağı (mal kabul satırı) taslak düzenlenirken de korunur; geçersiz bağ atılır.
        var receiptLineIds = new HashSet<Guid>();
        if (original is null && input.Kind is InvoiceKind.Purchase or InvoiceKind.Sales) {
            var wanted = lines.Where(r => r.Line.SourceLineId != null).Select(r => r.Line.SourceLineId!.Value).ToList();
            if (wanted.Count > 0 && input.Kind == InvoiceKind.Purchase)
                receiptLineIds = (await db.GoodsReceiptLines.AsNoTracking().Where(g => wanted.Contains(g.Id)).Select(g => g.Id).ToListAsync(ct)).ToHashSet();
            // Sevkten kesilen satış faturalarında satır bağı (sevk satırı) taslak düzenlenirken de korunur.
            else if (wanted.Count > 0)
                receiptLineIds = (await db.ShipmentLines.AsNoTracking().Where(g => wanted.Contains(g.Id)).Select(g => g.Id).ToListAsync(ct)).ToHashSet();
        }

        var calc  = Calculate(lines.Select(r => r.Calc).ToList(), input);
        var built = new List<InvoiceLine>();
        for (var i = 0; i < lines.Count; i++) {
            var line = lines[i].Line;
            var r    = calc.Lines[i];
            if (original is null && !(line.SourceLineId is { } linked && receiptLineIds.Contains(linked))) line.SourceLineId = null;
            line.Sort = i + 1;
            line.Gross = r.Gross; line.LineDiscount = r.LineDiscount; line.DocDiscount = r.DocDiscount; line.Net = r.Net; line.Otv = r.Otv;
            line.VatBase = r.VatBase; line.Vat = r.Vat; line.Withheld = r.Withheld; line.Total = r.Total;
            line.NetTl = InvoiceRules.Round(r.Net * rate); line.OtvTl = InvoiceRules.Round(r.Otv * rate); line.VatTl = InvoiceRules.Round(r.Vat * rate);
            line.WithheldTl = InvoiceRules.Round(r.Withheld * rate);
            line.TotalTl = line.NetTl + line.OtvTl + line.VatTl - line.WithheldTl;
            built.Add(line);
        }

        if (original is not null) await EnsureReturnQuantitiesAsync(original.Id, built, existingInvoiceId, ct);

        // İskonto politikası yalnızca satış türlerine uygulanır.
        if (!isPurchase && !systemGenerated && calc.Gross > 0m)
            DiscountPolicy.Ensure(Math.Round(calc.Discount / calc.Gross * 100m, 4), tenant);

        var totals   = new TotalsDto(calc.Gross, calc.Discount, calc.Net, calc.Otv, calc.Vat, calc.Withheld, calc.Payable);
        var grossTl  = InvoiceRules.Round(calc.Gross * rate);
        var netTl    = built.Sum(l => l.NetTl);
        var otvTl    = built.Sum(l => l.OtvTl);
        var vatTl    = built.Sum(l => l.VatTl);
        var heldTl   = built.Sum(l => l.WithheldTl);
        var totalsTl = new TotalsDto(grossTl, grossTl - netTl, netTl, otvTl, vatTl, heldTl, netTl + otvTl + vatTl - heldTl);

        var ledger = await InvoiceRules.LedgerAmountAsync(rates, currency, totals.Payable, totalsTl.Payable, cari.Currency, input.Date, type, ct);
        var due    = BuildDue(input, ledger);
        return new Built(cari, currency, rate, rateDate, overridden, type, input.PaymentTermDays ?? cari.DefaultDueDays, built, totals, totalsTl, due, ledger);
    }

    /// <summary>Hesaplanan değerleri ve girdiyi fatura kaydına yazar (satırları ve vade planını yeniden kurar).</summary>
    public static void Apply(Invoice invoice, InvoiceInput input, Built b) {
        invoice.Kind               = input.Kind;
        invoice.BranchId           = input.BranchId;
        invoice.CurrentAccountId   = input.CurrentAccountId;
        invoice.Date               = input.Date;
        invoice.Time               = input.Time;
        invoice.Currency           = b.Currency;
        invoice.Rate               = b.Rate;
        invoice.RateType           = b.RateType;
        invoice.RateOverridden     = b.RateOverridden;
        invoice.ExternalNumber     = InvoiceRules.IsPurchase(input.Kind) ? input.ExternalNumber?.Trim() : null;
        invoice.DocDiscountPercent = input.DocDiscountPercent;
        invoice.DocDiscountAmount  = input.DocDiscountAmount;
        invoice.Gross              = b.Totals.Gross;
        invoice.Discount           = b.Totals.Discount;
        invoice.Net                = b.Totals.Net;
        invoice.Otv                = b.Totals.Otv;
        invoice.Vat                = b.Totals.Vat;
        invoice.Withheld           = b.Totals.Withheld;
        invoice.Payable            = b.Totals.Payable;
        invoice.GrossTl            = b.TotalsTl.Gross;
        invoice.DiscountTl         = b.TotalsTl.Discount;
        invoice.NetTl              = b.TotalsTl.Net;
        invoice.OtvTl              = b.TotalsTl.Otv;
        invoice.VatTl              = b.TotalsTl.Vat;
        invoice.WithheldTl         = b.TotalsTl.Withheld;
        invoice.PayableTl          = b.TotalsTl.Payable;
        invoice.AffectsStock       = input.AffectsStock;
        invoice.WarehouseId        = input.AffectsStock ? input.WarehouseId : null;
        invoice.PaymentTermDays    = b.PaymentTermDays;
        invoice.Notes              = string.IsNullOrWhiteSpace(input.Notes) ? null : input.Notes.Trim();
        invoice.ReturnOfInvoiceId  = input.ReturnOfInvoiceId;
    }

    public static void EnsureCari(CurrentAccount cari, InvoiceKind kind) {
        if (!cari.IsActive) throw InvoiceErrors.Conflict("current_account_inactive", "Cari pasif; fatura kesilemez.");
        if (InvoiceRules.IsPurchase(kind)) {
            if (!cari.IsSupplier) throw InvoiceErrors.Bad("cari_not_supplier", "Alış faturası için cari tedarikçi olmalıdır.");
        } else if (!cari.IsCustomer) {
            throw InvoiceErrors.Bad("cari_not_customer", "Satış faturası için cari müşteri olmalıdır.");
        }
    }

    private async Task<(decimal Rate, DateOnly? Date, bool Overridden)> ResolveRateAsync(string currency, decimal? given, DateOnly date, RateType type,
                                                                                         CancellationToken ct) {
        if (currency == "TRY") {
            if (given is not null && given != 1m) throw InvoiceErrors.Bad("invalid_rate", "TL faturada kur 1 olmalıdır.");
            return (1m, null, false);
        }

        if (given is { } manual) return (manual, null, true);
        var (rate, effective) = await rates.GetAsync(currency, date, type, ct);
        return (rate, effective, false);
    }

    private Task<bool> ExternalNumberExistsAsync(Guid cariId, string number, Guid? exceptInvoiceId, CancellationToken ct) {
        return db.Invoices.AnyAsync(i => i.CurrentAccountId == cariId && i.ExternalNumber == number && i.Status != InvoiceStatus.Cancelled
                                         && (exceptInvoiceId == null || i.Id != exceptInvoiceId), ct);
    }

    private async Task EnsureWarehouseAsync(InvoiceInput input, CancellationToken ct) {
        if (!input.AffectsStock) return;
        if (input.WarehouseId is not { } warehouseId)
            throw InvoiceErrors.Bad("warehouse_required", "Stok etkili faturada depo seçilmelidir.");
        var warehouse = await db.Warehouses.AsNoTracking().FirstOrDefaultAsync(w => w.Id == warehouseId, ct)
                        ?? throw new NotFoundException(warehouseId.ToString(), "Warehouse");
        if (!warehouse.IsActive) throw InvoiceErrors.Conflict("warehouse_inactive", "Depo pasif.");
        if (warehouse.BranchId != input.BranchId) throw InvoiceErrors.Bad("warehouse_branch_mismatch", "Depo, faturanın şubesine ait değil.");
    }

    /// <summary>İade faturasının asıl faturasını doğrular: kesilmiş, uyumlu tür, aynı cari.</summary>
    private async Task<Invoice?> LoadReturnOriginalAsync(InvoiceInput input, CurrentAccount cari, CancellationToken ct) {
        if (!InvoiceRules.IsReturn(input.Kind)) {
            if (input.ReturnOfInvoiceId is not null) throw InvoiceErrors.Bad("return_of_unexpected", "Yalnızca iade faturaları bir faturaya bağlanır.");
            return null;
        }

        if (input.ReturnOfInvoiceId is not { } id) throw InvoiceErrors.Bad("return_of_required", "İade faturası için asıl fatura seçilmelidir.");
        var original = await db.Invoices.AsNoTracking().FirstOrDefaultAsync(i => i.Id == id, ct)
                       ?? throw new NotFoundException(id.ToString(), "Invoice");
        var expected = input.Kind == InvoiceKind.SalesReturn ? InvoiceKind.Sales : InvoiceKind.Purchase;
        if (original.Status != InvoiceStatus.Issued) throw InvoiceErrors.Conflict("return_of_not_issued", "İade yalnızca kesilmiş faturaya yapılabilir.");
        if (original.Kind != expected || original.CurrentAccountId != cari.Id)
            throw InvoiceErrors.Bad("return_of_mismatch", "Asıl fatura iade türüne ya da cariye uymuyor.");
        return original;
    }

    /// <summary>
    ///     İade miktarları, asıl satırın miktarından önceki (kesilmiş) iadeler düşüldükten sonra kalanı aşamaz.
    ///     Karşılaştırma temel birimde yapılır; excludeInvoiceId güncellenen/kesilen faturanın kendisini sayım dışı bırakır.
    /// </summary>
    public async Task EnsureReturnQuantitiesAsync(Guid originalId, IReadOnlyList<InvoiceLine> returnLines, Guid? excludeInvoiceId, CancellationToken ct) {
        if (returnLines.Any(l => l.SourceLineId is null))
            throw InvoiceErrors.Bad("return_line_required", "İade satırı asıl faturanın bir satırına bağlanmalıdır.");

        var requested = returnLines.GroupBy(l => l.SourceLineId!.Value).ToDictionary(g => g.Key, g => g.Sum(l => l.Quantity * l.Factor));
        var ids       = requested.Keys.ToList();
        var originals = await db.InvoiceLines.AsNoTracking().Where(l => l.InvoiceId == originalId && ids.Contains(l.Id)).ToListAsync(ct);
        if (originals.Count != ids.Count)
            throw InvoiceErrors.Bad("return_line_invalid", "İade satırı asıl faturaya ait değil.");

        var previous = await db.InvoiceLines.AsNoTracking()
                           .Where(l => l.SourceLineId != null && ids.Contains(l.SourceLineId.Value))
                           .Join(db.Invoices.Where(i => i.Status == InvoiceStatus.Issued && (excludeInvoiceId == null || i.Id != excludeInvoiceId)),
                                 l => l.InvoiceId, i => i.Id, (l, _) => new { Source = l.SourceLineId!.Value, l.Quantity, l.Factor })
                           .ToListAsync(ct);
        foreach (var source in originals) {
            var returned  = previous.Where(p => p.Source == source.Id).Sum(p => p.Quantity * p.Factor);
            var remaining = source.Quantity * source.Factor - returned;
            if (requested[source.Id] > remaining)
                throw InvoiceErrors.Bad("return_exceeds_original",
                                        $"\"{source.Description}\" için iade edilebilecek en fazla miktar {remaining.ToString("0.######", System.Globalization.CultureInfo.GetCultureInfo("tr-TR"))}.");
        }
    }

    private sealed record Resolved(InvoiceLine Line, CalcLine Calc);

    private async Task<List<Resolved>> ResolveLinesAsync(InvoiceInput input, bool preview, CancellationToken ct) {
        var productIds = input.Lines.Where(l => l.ProductId is not null).Select(l => l.ProductId!.Value).Distinct().ToList();
        var products   = await db.Products.AsNoTracking().Include(p => p.Units).Where(p => productIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
        var variantIds = input.Lines.Where(l => l.VariantId is not null).Select(l => l.VariantId!.Value).Distinct().ToList();
        var variants   = await db.ProductVariants.AsNoTracking().Where(v => variantIds.Contains(v.Id)).ToDictionaryAsync(v => v.Id, ct);
        var wCodes     = input.Lines.Select(l => l.WithholdingCode?.Trim().ToUpperInvariant()).Where(c => !string.IsNullOrEmpty(c)).Distinct().ToList();
        var withholding = await db.WithholdingCodes.AsNoTracking().Where(c => wCodes.Contains(c.Code)).ToDictionaryAsync(c => c.Code, ct);
        var eCodes     = input.Lines.Select(l => l.ExemptionCode?.Trim().ToUpperInvariant()).Where(c => !string.IsNullOrEmpty(c)).Distinct().ToList();
        var exemptions = await db.ExemptionCodes.AsNoTracking().Where(c => eCodes.Contains(c.Code)).ToDictionaryAsync(c => c.Code, ct);

        var result = new List<Resolved>();
        for (var n = 0; n < input.Lines.Count; n++) {
            var l     = input.Lines[n];
            var label = $"{n + 1}. satır";
            Domain.Catalog.Product? product = null;
            if (l.ProductId is { } pid) {
                if (!products.TryGetValue(pid, out product)) throw InvoiceErrors.Bad("product_not_found", $"{label}: kalem bulunamadı.");
                if (!product.IsActive) throw InvoiceErrors.Bad("product_inactive", $"{label}: kalem pasif.");
            }

            if (l.VariantId is { } vid && (product is null || !variants.TryGetValue(vid, out var variant) || variant.ProductId != product.Id))
                throw InvoiceErrors.Bad("variant_invalid", $"{label}: varyant kaleme ait değil.");

            // Önizleme toplamları gösterir; seri/lot eksikliği taslak kaydında ve kesimde reddedilir.
            if (input.AffectsStock && !preview && product is not null)
                InvoiceRules.EnsureTrackingEntered(product, l.Lots, InvoiceRules.NormalizeSerials(l.Serials), label);

            var description = !string.IsNullOrWhiteSpace(l.Description) ? l.Description.Trim() : product?.Name;
            if (string.IsNullOrWhiteSpace(description)) throw InvoiceErrors.Bad("description_required", $"{label}: açıklama ya da kalem gerekli.");

            var (unit, factor) = ResolveUnit(l, product, label);
            var (quantity, width, length) = ResolveQuantity(l, product, label);

            var vatRate = l.VatRate ?? product?.VatRate ?? 20;
            if (!InvoiceCalculator.AllowedVatRates.Contains(vatRate)) throw InvoiceErrors.Bad("invalid_vat_rate", $"{label}: KDV oranı geçersiz.");

            var wCode = l.WithholdingCode?.Trim().ToUpperInvariant();
            var eCode = l.ExemptionCode?.Trim().ToUpperInvariant();
            if (!string.IsNullOrEmpty(wCode) && !string.IsNullOrEmpty(eCode))
                throw InvoiceErrors.Bad("withholding_with_exemption", $"{label}: tevkifat ve istisna birlikte kullanılamaz.");
            if (!string.IsNullOrEmpty(eCode) && vatRate != 0)
                throw InvoiceErrors.Bad("exemption_requires_zero_vat", $"{label}: istisna yalnızca %0 KDV ile kullanılabilir.");
            WithholdingCode? withheld = null;
            if (!string.IsNullOrEmpty(wCode) && (!withholding.TryGetValue(wCode, out withheld) || !withheld.IsActive))
                throw InvoiceErrors.Bad("withholding_code_unknown", $"{label}: tevkifat kodu geçersiz.");
            if (!string.IsNullOrEmpty(eCode) && !exemptions.ContainsKey(eCode))
                throw InvoiceErrors.Bad("exemption_code_unknown", $"{label}: istisna kodu geçersiz.");

            var line = new InvoiceLine {
                ProductId = l.ProductId, VariantId = l.VariantId, Description = description, Unit = unit, Factor = factor, Quantity = quantity,
                Width = width, Length = length, UnitPrice = l.UnitPrice, VatIncluded = l.VatIncluded, DiscountPercent = l.DiscountPercent,
                DiscountAmount = l.DiscountAmount, VatRate = vatRate, WithholdingCode = string.IsNullOrEmpty(wCode) ? null : wCode,
                ExemptionCode = string.IsNullOrEmpty(eCode) ? null : eCode,
                ExemptionReason = string.IsNullOrEmpty(eCode) ? null : l.ExemptionReason?.Trim(), OtvPercent = l.OtvPercent,
                OtvPerUnit = l.OtvPerUnit, SourceLineId = l.SourceLineId, Lots = l.Lots?.Count > 0 ? l.Lots : null,
                Serials = InvoiceRules.NormalizeSerials(l.Serials)
            };
            var calc = new CalcLine(quantity, l.UnitPrice, l.VatIncluded, l.DiscountPercent, l.DiscountAmount, vatRate, l.OtvPercent, l.OtvPerUnit,
                                    withheld?.Tenths, !string.IsNullOrEmpty(eCode), l.VatOverride);
            result.Add(new Resolved(line, calc));
        }

        return result;
    }

    private static (string Unit, decimal Factor) ResolveUnit(LineInput l, Domain.Catalog.Product? product, string label) {
        var requested = l.Unit?.Trim();
        if (product is null) return (string.IsNullOrEmpty(requested) ? "adet" : requested, 1m);
        if (string.IsNullOrEmpty(requested) || string.Equals(requested, product.BaseUnit, StringComparison.OrdinalIgnoreCase)) return (product.BaseUnit, 1m);
        var unit = product.Units.FirstOrDefault(u => string.Equals(u.Unit, requested, StringComparison.OrdinalIgnoreCase))
                   ?? throw InvoiceErrors.Bad("unit_invalid", $"{label}: \"{requested}\" birimi kalemde tanımlı değil.");
        return (unit.Unit, unit.Factor);
    }

    /// <summary>Ölçüyle satılan kalemlerde miktar en x boy (m2) ya da boydur (m); en/boy zorunludur.</summary>
    private static (decimal Quantity, decimal? Width, decimal? Length) ResolveQuantity(LineInput l, Domain.Catalog.Product? product, string label) {
        if (product is not { SoldByMeasure: true }) {
            if (l.Quantity <= 0m) throw InvoiceErrors.Bad("quantity_required", $"{label}: miktar sıfırdan büyük olmalıdır.");
            return (l.Quantity, null, null);
        }

        var byLength = string.Equals(product.MeasureUnit, "m", StringComparison.OrdinalIgnoreCase);
        if (l.Length is not > 0m || (!byLength && l.Width is not > 0m))
            throw InvoiceErrors.Bad("measure_dimensions_required", byLength ? $"{label}: boy zorunludur." : $"{label}: en ve boy zorunludur.");
        var quantity = byLength ? l.Length.Value : InvoiceRules.Round(l.Width!.Value * l.Length.Value, 6);
        return (quantity, byLength ? null : l.Width, l.Length);
    }

    private static CalcResult Calculate(IReadOnlyList<CalcLine> lines, InvoiceInput input) {
        try {
            return InvoiceCalculator.Calculate(lines, input.DocDiscountPercent, input.DocDiscountAmount);
        } catch (ArgumentException e) {
            throw InvoiceErrors.Bad("invalid_amounts", e.Message);
        }
    }

    /// <summary>Vade planı verilmişse toplamı cari para birimindeki ödenecek tutara eşit olmalıdır; verilmemişse boş döner.</summary>
    private static List<InvoiceDueLine> BuildDue(InvoiceInput input, decimal ledgerAmount) {
        var due = new List<InvoiceDueLine>();
        if (input.DueSchedule is not { Count: > 0 } schedule) return due;
        if (schedule.Sum(d => d.Amount) != ledgerAmount)
            throw InvoiceErrors.Bad("due_schedule_mismatch", "Vade planı toplamı ödenecek tutara eşit olmalıdır.");
        var sort = 1;
        foreach (var d in schedule.OrderBy(d => d.DueDate)) due.Add(new InvoiceDueLine { Sort = sort++, DueDate = d.DueDate, Amount = d.Amount });
        return due;
    }
}
