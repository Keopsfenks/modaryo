using ERP.Application.Common.Exceptions;
using ERP.Application.Common.Interfaces;
using ERP.Application.CurrentAccounts;
using ERP.Application.CurrentAccounts.Allocation;
using ERP.Application.Inventory.Documents;
using ERP.Application.Settings;
using ERP.Application.Settings.Categories;
using ERP.Application.Treasury.Accounts;
using ERP.Application.Treasury.Allocations;
using ERP.Domain.CurrentAccounts;
using ERP.Domain.Treasury;

namespace ERP.Application.Treasury;

public sealed record PostResult(Guid Id, string Number, IReadOnlyList<string> Warnings);

public sealed record PaymentAllocation(Guid DueLineId, decimal Amount);

/// <summary>Tahsilat/ödeme girdisi (ortak). Tutar hesabın para birimindedir.</summary>
public abstract record PaymentInput {
    public Guid CurrentAccountId { get; init; }

    public Guid AccountId { get; init; }

    public PaymentMethod Method { get; init; }

    public DateOnly Date { get; init; }

    public decimal Amount { get; init; }

    public decimal? Rate { get; init; }

    public string? Description { get; init; }

    public bool IsAdvance { get; init; }

    public List<PaymentAllocation>? Allocations { get; init; }
}

public static class PaymentRules {
    public static void AddRules<T>(this AbstractValidator<T> v) where T : PaymentInput {
        v.RuleFor(p => p.CurrentAccountId).NotEmpty().WithMessage("Cari seçilmelidir.");
        v.RuleFor(p => p.AccountId).NotEmpty().WithMessage("Kasa/banka hesabı seçilmelidir.");
        v.RuleFor(p => p.Method).IsInEnum().WithMessage("Ödeme yöntemi geçersiz.");
        v.RuleFor(p => p.Date).Must(d => d != default && CardRules.IsValidDate(d)).WithMessage(CardRules.DateMessage);
        v.RuleFor(p => p.Amount).GreaterThan(0m).LessThanOrEqualTo(CardRules.MaxAmount)
            .WithMessage("Tutar 0 değerinden büyük ve 999.999.999.999,99 değerinden küçük olmalıdır.");
        v.RuleFor(p => p.Amount).Must(CardRules.HasValidScale).WithMessage("Tutar en fazla 2 ondalık basamak içerebilir.");
        v.RuleFor(p => p.Rate).GreaterThan(0m).LessThan(1_000_000m).When(p => p.Rate is not null).WithMessage("Kur sıfırdan büyük olmalıdır.")
            .Must(r => r is null || Math.Round(r.Value, 6) == r).WithMessage("Kur en fazla 6 ondalık basamak içerebilir.");
        v.RuleFor(p => p.Description).MaximumLength(500).WithMessage("Açıklama en çok 500 karakter olabilir.");
        v.RuleFor(p => p.Allocations).Must(a => a is null || a.Count <= 200).WithMessage("Bir belgede en fazla 200 satır eşleştirilebilir.");
        v.RuleForEach(p => p.Allocations).ChildRules(a => {
            a.RuleFor(x => x.DueLineId).NotEmpty().WithMessage("Vade satırı zorunludur.");
            a.RuleFor(x => x.Amount).GreaterThan(0m).LessThanOrEqualTo(CardRules.MaxAmount).WithMessage("Eşleştirme tutarı sıfırdan büyük olmalıdır.");
            a.RuleFor(x => x.Amount).Must(CardRules.HasValidScale).WithMessage("Eşleştirme tutarı en fazla 2 ondalık basamak içerebilir.");
        }).When(p => p.Allocations is not null);
    }
}

/// <summary>
///     Tahsilat/ödeme belgesinin etkilerinin tek yazıcısı. Çağıran işlemi kendisi açmaz: poster açar, carinin kilidini alır ve onaylar.
///     Sıra: cari kilidi, dönem kilidi, hesap hareketi, defter hareketi + vade satırı, elle eşleştirmeler, otomatik eşleştirmenin yeniden kurulması, outbox.
///     İptal aynı kilit sırasını izler ve yalnızca bu etkileri geri alır.
/// </summary>
public sealed class FinancePoster(
    IApplicationDbContext db,
    ICurrentTenant        tenant,
    ICurrentSession       session,
    IPeriodLock           periodLock,
    IExchangeRates        rates,
    ITenantSettings       settings,
    AutoAllocator         auto,
    ManualAllocator       manual,
    IOutbox               outbox,
    TimeProvider          clock
) {
    public const string Collection = "TH";
    public const string Payment    = "OD";

    private static string Prefix(FinanceDocumentKind kind) {
        return kind == FinanceDocumentKind.Collection ? Collection : Payment;
    }

    public async Task<PostResult> PostAsync(FinanceDocumentKind kind, PaymentInput input, Guid? salesOrderId, Guid? purchaseOrderId, CancellationToken ct) {
        var collection = kind == FinanceDocumentKind.Collection;

        await using var tx = await db.BeginTransactionAsync(ct);
        if (!await db.LockCurrentAccountAsync(input.CurrentAccountId, ct)) throw new NotFoundException(input.CurrentAccountId.ToString(), "CurrentAccount");

        await periodLock.EnsureOpenAsync(input.Date, ct);

        var cari = await db.CurrentAccounts.AsNoTracking().FirstOrDefaultAsync(c => c.Id == input.CurrentAccountId, ct)
                   ?? throw new NotFoundException(input.CurrentAccountId.ToString(), "CurrentAccount");
        if (!cari.IsActive) throw CariErrors.AccountInactive();

        var account = await AccountRules.FindAsync(db, tenant, input.AccountId, ct);
        if (!account.IsActive) throw TreasuryErrors.AccountInactive();

        EnsureMethod(input.Method, account.Kind);

        var (ledgerAmount, rate) = await ConvertAsync(input, account.Currency, cari.Currency, ct);

        var number = await DocumentNumbers.NextAsync(db, Prefix(kind), input.Date, ct);
        var document = new FinanceDocument {
                                               Kind             = kind,
                                               Number           = number,
                                               Date             = input.Date,
                                               Method           = input.Method,
                                               AccountId        = account.Id,
                                               CurrentAccountId = cari.Id,
                                               Amount           = input.Amount,
                                               Currency         = account.Currency,
                                               Rate             = rate,
                                               IsAdvance        = input.IsAdvance,
                                               SalesOrderId     = collection ? salesOrderId : null,
                                               PurchaseOrderId  = collection ? null : purchaseOrderId,
                                               Description      = CardRules.Clean(input.Description)
                                           };
        db.FinanceDocuments.Add(document);

        // Hesap hareketi: giriş +, çıkış - (hesabın para biriminde).
        db.TreasuryMovements.Add(new TreasuryMovement {
                                                          AccountId   = account.Id,
                                                          DocumentId  = document.Id,
                                                          Date        = input.Date,
                                                          Amount      = collection ? input.Amount : -input.Amount,
                                                          Currency    = account.Currency,
                                                          Description = document.Description
                                                      });

        // Defter hareketi: tahsilat cari alacağı, ödeme cari borcu (cari para biriminde); tek vade satırı hareket tarihindedir.
        var foreign = !string.Equals(account.Currency, cari.Currency, StringComparison.Ordinal);
        var title   = collection ? "Tahsilat" : "Ödeme";
        var entry = new LedgerEntry {
                                        CurrentAccountId = cari.Id,
                                        Type             = collection ? LedgerEntryType.CreditNote : LedgerEntryType.DebitNote,
                                        Source           = LedgerEntrySource.Payment,
                                        SourceId         = document.Id,
                                        Date             = input.Date,
                                        DueDate          = input.Date,
                                        DocumentNo       = number,
                                        Description      = document.Description is { } d ? $"{title}: {d}" : title,
                                        Debit            = collection ? 0m : ledgerAmount,
                                        Credit           = collection ? ledgerAmount : 0m,
                                        Currency         = cari.Currency,
                                        SourceCurrency   = foreign ? account.Currency : null,
                                        SourceAmount     = foreign ? input.Amount : null,
                                        Rate             = foreign ? rate : null
                                    };
        EntryRules.Add(db, entry);
        var dueLineId = db.LedgerDueLines.Local.First(l => l.EntryId == entry.Id).Id;
        await db.SaveChangesAsync(ct);

        if (input.Allocations is { Count: > 0 } allocations)
            await manual.AllocateAsync(cari.Id, dueLineId, allocations.Select(a => new AllocationItem(a.DueLineId, a.Amount)).ToList(), ct);
        else {
            await auto.RebuildAsync(cari.Id, ct);
            await db.SaveChangesAsync(ct);
        }

        var warnings = new List<string>();
        if (!collection && account.Kind == TreasuryAccountKind.Cash && await AccountRules.BalanceAsync(db, account.Id, ct) < 0m) warnings.Add("cash_negative");

        outbox.Enqueue("payment.recorded", new {
                                                   documentId       = document.Id,
                                                   kind             = kind.ToString(),
                                                   number,
                                                   currentAccountId = cari.Id,
                                                   amount           = input.Amount,
                                                   currency         = account.Currency,
                                                   isAdvance        = input.IsAdvance
                                               });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return new PostResult(document.Id, number, warnings);
    }

    /// <summary>Tahsilat/ödemeyi geri alır: hesap hareketi, defter hareketi ve (iptal edilen satıra dokunan) elle eşleştirmeler; numara kullanılmış kalır.</summary>
    public async Task CancelAsync(Guid documentId, string reason, CancellationToken ct) {
        // Kilit için yalnızca cari ve tür okunur (izlemesiz); durum kilit alındıktan sonra yeniden okunur.
        var head = await db.FinanceDocuments.AsNoTracking().Where(d => d.Id == documentId).Select(d => new { d.Kind, d.CurrentAccountId, d.AccountId }).FirstOrDefaultAsync(ct)
                   ?? throw new NotFoundException(documentId.ToString(), "FinanceDocument");
        if (head.Kind is not (FinanceDocumentKind.Collection or FinanceDocumentKind.Payment) || head.CurrentAccountId is not { } cariId)
            throw TreasuryErrors.DocumentKindUnsupported();

        TreasuryAccess.RequireAny(session, head.Kind == FinanceDocumentKind.Collection ? TreasuryPermissions.Collection : TreasuryPermissions.Payment);
        if (head.AccountId is { } headAccount) await AccountRules.FindAsync(db, tenant, headAccount, ct);

        await using var tx = await db.BeginTransactionAsync(ct);
        if (!await db.LockCurrentAccountAsync(cariId, ct)) throw new NotFoundException(cariId.ToString(), "CurrentAccount");

        var document = await db.FinanceDocuments.FirstOrDefaultAsync(d => d.Id == documentId, ct)
                       ?? throw new NotFoundException(documentId.ToString(), "FinanceDocument");
        if (document.IsCancelled) throw TreasuryErrors.DocumentCancelled();

        await periodLock.EnsureOpenAsync(document.Date, ct);

        var now = clock.GetUtcNow();
        document.CancelledAt  = now;
        document.CancelledBy  = session.UserId?.ToString();
        document.CancelReason = reason;

        foreach (var movement in await db.TreasuryMovements.Where(m => m.DocumentId == document.Id && m.CancelledAt == null).ToListAsync(ct))
            movement.CancelledAt = now;

        foreach (var entry in await db.LedgerEntries.Where(e => e.Source == LedgerEntrySource.Payment && e.SourceId == document.Id && e.CancelledAt == null).ToListAsync(ct)) {
            entry.CancelledAt  = now;
            entry.CancelledBy  = session.UserId?.ToString();
            entry.CancelReason = reason;
        }

        await db.SaveChangesAsync(ct);

        // İptal edilen satıra dokunan eşleştirmeler silinir, kalan tutarlar sonraki satırlara kayar.
        await auto.RebuildAsync(cariId, ct);
        outbox.Enqueue("payment.cancelled", new {
                                                    documentId = document.Id,
                                                    kind       = document.Kind.ToString(),
                                                    number     = document.Number,
                                                    reason
                                                });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    /// <summary>Kart tahsilatı POS hesabına, nakit kasaya, havale/EFT banka hesabına yazılır.</summary>
    private static void EnsureMethod(PaymentMethod method, TreasuryAccountKind kind) {
        var ok = method switch {
                     PaymentMethod.Cash       => kind == TreasuryAccountKind.Cash,
                     PaymentMethod.Transfer   => kind == TreasuryAccountKind.Bank,
                     PaymentMethod.CreditCard => kind == TreasuryAccountKind.CardPos,
                     _                        => false
                 };
        if (ok) return;

        throw TreasuryErrors.AccountMethodMismatch(method switch {
                                                       PaymentMethod.CreditCard => "Kredi kartı işlemi için kart POS hesabı seçilmelidir.",
                                                       PaymentMethod.Cash       => "Nakit işlem için kasa hesabı seçilmelidir.",
                                                       _                        => "Havale/EFT işlemi için banka hesabı seçilmelidir."
                                                   });
    }

    /// <summary>
    ///     Tutarı (hesap para biriminde) cari para birimine çevirir. Para birimleri aynıysa kur yoktur. Kur, yabancı para biriminin TL karşılığıdır:
    ///     hesap TL ise carinin, değilse hesabın para birimi için; verilmezse ayarlardaki kur türüyle TCMB kuru kullanılır (yoksa 400 rate_required).
    ///     İki farklı yabancı para birimi TL üzerinden çevrilir (verilen kur hesabınındır, carinin kuru aynı gün TCMB'den gelir).
    /// </summary>
    private async Task<(decimal LedgerAmount, decimal? Rate)> ConvertAsync(PaymentInput input, string accountCurrency, string cariCurrency, CancellationToken ct) {
        if (accountCurrency == cariCurrency) return (input.Amount, null);

        var type    = (await settings.GetAsync<FinanceSettings>(ct)).RateType;
        var foreign = accountCurrency != "TRY" ? accountCurrency : cariCurrency;
        var rate    = input.Rate ?? await DefaultRateAsync(foreign, input.Date, type, ct);

        if (accountCurrency == "TRY") return (Round(input.Amount / rate), rate);

        var tl = input.Amount * rate;
        if (cariCurrency == "TRY") return (Round(tl), rate);

        var cariRate = await DefaultRateAsync(cariCurrency, input.Date, type, ct);
        return (Round(tl / cariRate), rate);
    }

    private async Task<decimal> DefaultRateAsync(string currency, DateOnly date, Domain.Invoicing.RateType type, CancellationToken ct) {
        try {
            return (await rates.GetAsync(currency, date, type, ct)).Rate;
        } catch (AppException e) when (e.Code == "rate_missing") {
            throw TreasuryErrors.RateRequired();
        }
    }

    private static decimal Round(decimal value) {
        return Math.Round(value, 2, MidpointRounding.AwayFromZero);
    }
}
