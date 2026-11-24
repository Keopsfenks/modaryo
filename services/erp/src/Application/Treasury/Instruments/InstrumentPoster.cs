using ERP.Application.Common.Exceptions;
using ERP.Application.Common.Interfaces;
using ERP.Application.CurrentAccounts;
using ERP.Application.CurrentAccounts.Allocation;
using ERP.Application.Inventory.Documents;
using ERP.Application.Treasury.Accounts;
using ERP.Domain.CurrentAccounts;
using ERP.Domain.Treasury;

namespace ERP.Application.Treasury.Instruments;

public sealed record InstrumentPostResult(Guid InstrumentId, Guid DocumentId, string DocumentNumber, IReadOnlyList<string> Warnings);

/// <summary>Çek/senet girdisi (alınan ve verilen için ortak alanlar).</summary>
public abstract record InstrumentInput {
    public InstrumentKind Kind { get; init; }

    public Guid CurrentAccountId { get; init; }

    public string Number { get; init; } = string.Empty;

    public DateOnly DueDate { get; init; }

    public decimal Amount { get; init; }

    public string Currency { get; init; } = "TRY";

    public DateOnly Date { get; init; }

    public string? BankName { get; init; }

    public string? BankBranch { get; init; }

    public string? AccountNo { get; init; }

    public string? Drawer { get; init; }
}

public static class InstrumentRules {
    public static void AddRules<T>(this AbstractValidator<T> v) where T : InstrumentInput {
        v.RuleFor(i => i.Kind).IsInEnum().WithMessage("Tür çek ya da senet olmalıdır.");
        v.RuleFor(i => i.CurrentAccountId).NotEmpty().WithMessage("Cari seçilmelidir.");
        v.RuleFor(i => i.Number).Must(n => !string.IsNullOrWhiteSpace(n) && n.Trim().Length <= 50).WithMessage("Çek/senet numarası zorunludur ve en fazla 50 karakter olabilir.");
        v.RuleFor(i => i.DueDate).Must(d => d != default && CardRules.IsValidDate(d)).WithMessage("Vade " + CardRules.DateMessage);
        v.RuleFor(i => i.Date).Must(d => d != default && CardRules.IsValidDate(d)).WithMessage(CardRules.DateMessage);
        v.RuleFor(i => i.Amount).GreaterThan(0m).LessThanOrEqualTo(CardRules.MaxAmount)
            .WithMessage("Tutar 0 değerinden büyük ve 999.999.999.999,99 değerinden küçük olmalıdır.");
        v.RuleFor(i => i.Amount).Must(CardRules.HasValidScale).WithMessage("Tutar en fazla 2 ondalık basamak içerebilir.");
        v.RuleFor(i => i.Currency).Must(c => CardRules.Currencies.Contains(c)).WithMessage("Para birimi TRY, USD, EUR veya GBP olmalıdır.");
        v.RuleFor(i => i.BankName).MaximumLength(100).WithMessage("Banka adı en fazla 100 karakter olabilir.");
        v.RuleFor(i => i.BankBranch).MaximumLength(100).WithMessage("Şube adı en fazla 100 karakter olabilir.");
        v.RuleFor(i => i.AccountNo).MaximumLength(50).WithMessage("Hesap numarası en fazla 50 karakter olabilir.");
        v.RuleFor(i => i.Drawer).MaximumLength(200).WithMessage("Keşideci/borçlu adı en fazla 200 karakter olabilir.");
    }

    public static string KindLabel(InstrumentKind kind) {
        return kind == InstrumentKind.Check ? "Çek" : "Senet";
    }
}

/// <summary>
///     Çek/senet hareketlerinin tek yazıcısı. Her hareket bir CS belgesi, bir geçmiş satırı ve (durum tablosuna göre) cari/hesap hareketi yazar.
///     Kilit: etkilenen tüm cariler kimlik sırasıyla kilitlenir (asıl cari her zaman dahildir; aynı çekin hareketleri birbirini sıraya dizer),
///     durum kilitlerden sonra yeniden okunur.
/// </summary>
public sealed class InstrumentPoster(
    IApplicationDbContext db,
    ICurrentTenant        tenant,
    ICurrentSession       session,
    IPeriodLock           periodLock,
    AutoAllocator         auto,
    IOutbox               outbox,
    TimeProvider          clock
) {
    public const string Prefix = "CS";

    public async Task<InstrumentPostResult> CreateAsync(InstrumentDirection direction, InstrumentInput input, Guid? bankAccountId, int endorsementCount, CancellationToken ct) {
        var received = direction == InstrumentDirection.Received;

        await using var tx = await db.BeginTransactionAsync(ct);
        if (!await db.LockCurrentAccountAsync(input.CurrentAccountId, ct)) throw new NotFoundException(input.CurrentAccountId.ToString(), "CurrentAccount");

        await periodLock.EnsureOpenAsync(input.Date, ct);

        var cari = await db.CurrentAccounts.AsNoTracking().FirstOrDefaultAsync(c => c.Id == input.CurrentAccountId, ct)
                   ?? throw new NotFoundException(input.CurrentAccountId.ToString(), "CurrentAccount");
        if (!cari.IsActive) throw CariErrors.AccountInactive();

        EnsureRole(cari, received ? InstrumentDirection.Received : InstrumentDirection.Issued);
        EnsureCurrency(cari.Currency, input.Currency, "Cari");

        var bankName  = CardRules.Clean(input.BankName);
        var bankBranch = CardRules.Clean(input.BankBranch);
        var accountNo = CardRules.Clean(input.AccountNo);
        TreasuryAccount? bank = null;
        if (received) {
            if (input.Kind == InstrumentKind.Check && bankName is null) throw new AppException(400, "bank_required", "Çek için banka adı zorunludur.");
        } else if (bankAccountId is { } id) {
            bank = await AccountRules.FindAsync(db, tenant, id, ct);
            EnsureBankAccount(bank, input.Currency);
            // Kendi çekimizde banka bilgisi ödeme hesabından gelir.
            bankName   = bank.BankName;
            accountNo  = bank.Iban;
            bankBranch = null;
        } else if (input.Kind == InstrumentKind.Check) {
            throw new AppException(400, "bank_account_required", "Verilen çek için banka hesabı seçilmelidir.");
        }

        var number = input.Number.Trim();
        if (await db.Instruments.AnyAsync(i => i.Kind == input.Kind && i.Direction == direction && i.Number == number && i.BankName == bankName, ct))
            throw TreasuryErrors.InstrumentNumberTaken();

        var instrument = new Instrument {
                                            Kind                = input.Kind,
                                            Direction           = direction,
                                            Status              = received ? InstrumentStatus.InPortfolio : InstrumentStatus.Issued,
                                            Number              = number,
                                            DueDate             = input.DueDate,
                                            Amount              = input.Amount,
                                            Currency            = input.Currency,
                                            DrawerName          = CardRules.Clean(input.Drawer) ?? (received ? cari.Name : "Firma"),
                                            BankName            = bankName,
                                            BankBranch          = bankBranch,
                                            AccountNo           = accountNo,
                                            EndorsementCount    = received ? endorsementCount : 0,
                                            CurrentAccountId    = cari.Id,
                                            CollectionAccountId = bank?.Id
                                        };
        db.Instruments.Add(instrument);

        var document = NewDocument(instrument, input.Date, cari.Id, bank?.Id, null);
        document.Number = await DocumentNumbers.NextAsync(db, Prefix, input.Date, ct);
        db.FinanceDocuments.Add(document);
        AddEvent(instrument, document, null, instrument.Status, input.Date, null);

        // Alınan çek müşteriyi alacaklandırır; verilen çek tedarikçiyi borçlandırır.
        TreasuryPosting.CariEntry(db, cari, !received, input.Amount, input.Date, SourceOf(instrument.Kind), document.Id, document.Number,
                                  $"{InstrumentRules.KindLabel(instrument.Kind)} {number}: {(received ? "alındı" : "düzenlendi")}");

        await SaveAsync(ct);
        await auto.RebuildAsync(cari.Id, ct);
        outbox.Enqueue("instrument.created", new { instrumentId = instrument.Id, documentId = document.Id, number, kind = instrument.Kind.ToString(), direction = direction.ToString(), amount = instrument.Amount, currency = instrument.Currency });
        await SaveAsync(ct);
        await tx.CommitAsync(ct);
        return new InstrumentPostResult(instrument.Id, document.Id, document.Number, []);
    }

    public async Task<InstrumentPostResult> MoveAsync(Guid id, InstrumentStatus to, DateOnly date, Guid? targetCariId, Guid? accountId, string? note, CancellationToken ct) {
        var head = await db.Instruments.AsNoTracking().Where(i => i.Id == id).Select(i => new { i.CurrentAccountId, i.EndorsedToId }).FirstOrDefaultAsync(ct)
                   ?? throw new NotFoundException(id.ToString(), "Instrument");

        var lockIds = new List<Guid> { head.CurrentAccountId };
        if (head.EndorsedToId is { } endorsedTo) lockIds.Add(endorsedTo);
        if (to == InstrumentStatus.Endorsed && targetCariId is { } target) lockIds.Add(target);
        lockIds = lockIds.Distinct().Order().ToList();

        await using var tx = await db.BeginTransactionAsync(ct);
        foreach (var lockId in lockIds)
            if (!await db.LockCurrentAccountAsync(lockId, ct)) throw new NotFoundException(lockId.ToString(), "CurrentAccount");

        var instrument = await db.Instruments.FirstOrDefaultAsync(i => i.Id == id, ct) ?? throw new NotFoundException(id.ToString(), "Instrument");
        var from       = instrument.Status;
        if (!InstrumentStateMachine.CanMove(instrument.Direction, from, to)) throw TreasuryErrors.InvalidTransition();

        // Kilitler hareketten önce okunan duruma göre alındı; durum arada değiştiyse etkilenecek cariler kilitli olmayabilir.
        if (instrument.CurrentAccountId != head.CurrentAccountId || instrument.EndorsedToId != head.EndorsedToId) throw TreasuryErrors.InvalidTransition();

        await periodLock.EnsureOpenAsync(date, ct);

        var owner = await LoadCariAsync(instrument.CurrentAccountId, ct);
        var title = $"{InstrumentRules.KindLabel(instrument.Kind)} {instrument.Number}";
        var warnings = new List<string>();

        TreasuryAccount? account       = null;
        CurrentAccount?  supplier      = null;
        decimal          accountSign   = 0m;
        var              posts         = new List<(CurrentAccount Cari, bool Debit, string Text)>();

        switch (instrument.Direction, from, to) {
            case (InstrumentDirection.Received, InstrumentStatus.InPortfolio, InstrumentStatus.Endorsed):
                supplier = await LoadSupplierAsync(targetCariId, instrument.Currency, ct);
                instrument.EndorsedToId = supplier.Id;
                instrument.EndorsementCount++;
                posts.Add((supplier, true, $"{title}: ciro edildi"));
                break;
            case (InstrumentDirection.Received, InstrumentStatus.InPortfolio, InstrumentStatus.InCollection):
                account = await RequireAccountAsync(accountId, instrument.Currency, bankOnly: true, ct);
                instrument.CollectionAccountId = account.Id;
                break;
            case (InstrumentDirection.Received, InstrumentStatus.InPortfolio, InstrumentStatus.ReturnedToCustomer):
                posts.Add((owner, true, $"{title}: müşteriye iade edildi"));
                break;
            case (InstrumentDirection.Received, InstrumentStatus.InCollection, InstrumentStatus.Collected):
                account     = await RequireAccountAsync(instrument.CollectionAccountId, instrument.Currency, bankOnly: true, ct);
                accountSign = 1m;
                break;
            case (InstrumentDirection.Received, InstrumentStatus.InCollection, InstrumentStatus.Bounced):
                instrument.ProtestFlag = true;
                posts.Add((owner, true, $"{title}: karşılıksız, protesto"));
                break;
            case (InstrumentDirection.Received, InstrumentStatus.Endorsed, InstrumentStatus.Bounced):
                supplier = await LoadSupplierAsync(instrument.EndorsedToId, instrument.Currency, ct);
                posts.Add((supplier, false, $"{title}: tedarikçiden iade geldi"));
                posts.Add((owner, true, $"{title}: iade, müşteri yeniden borçlandı"));
                break;
            case (InstrumentDirection.Received, InstrumentStatus.Bounced, InstrumentStatus.Collected):
                // Geç tahsilat: müşteri iadede yeniden borçlandırılmıştı; tahsil edilen tutar onun borcunu kapatır.
                account     = await RequireAccountAsync(accountId, instrument.Currency, bankOnly: false, ct);
                accountSign = 1m;
                posts.Add((owner, false, $"{title}: geç tahsil edildi"));
                break;
            case (InstrumentDirection.Issued, InstrumentStatus.Issued, InstrumentStatus.Paid):
                if (date < instrument.DueDate) throw TreasuryErrors.InstrumentNotDue();

                account     = await RequireAccountAsync(instrument.CollectionAccountId ?? accountId, instrument.Currency, bankOnly: instrument.CollectionAccountId is not null || instrument.Kind == InstrumentKind.Check, ct);
                accountSign = -1m;
                break;
            case (InstrumentDirection.Issued, InstrumentStatus.Issued, InstrumentStatus.ReturnedFromSupplier):
                posts.Add((owner, false, $"{title}: tedarikçiden iade alındı"));
                break;
            default:
                throw TreasuryErrors.InvalidTransition();
        }

        var cleanNote = CardRules.Clean(note);
        var document  = NewDocument(instrument, date, posts.Count > 0 ? posts[^1].Cari.Id : owner.Id, account?.Id, cleanNote);
        document.Number = await DocumentNumbers.NextAsync(db, Prefix, date, ct);
        db.FinanceDocuments.Add(document);
        instrument.Status = to;
        AddEvent(instrument, document, from, to, date, cleanNote);

        if (account is not null && accountSign != 0m)
            TreasuryPosting.Movement(db, account, document.Id, date, accountSign * instrument.Amount, $"{title}: {StatusText(to)}");

        foreach (var (cari, debit, text) in posts)
            TreasuryPosting.CariEntry(db, cari, debit, instrument.Amount, date, SourceOf(instrument.Kind), document.Id, document.Number, cleanNote is null ? text : $"{text} ({cleanNote})");

        await SaveAsync(ct);
        foreach (var cariId in posts.Select(p => p.Cari.Id).Distinct()) await auto.RebuildAsync(cariId, ct);

        if (accountSign < 0m && account is { Kind: TreasuryAccountKind.Cash } && await AccountRules.BalanceAsync(db, account.Id, ct) < 0m) warnings.Add("cash_negative");

        outbox.Enqueue("instrument.moved", new { instrumentId = instrument.Id, documentId = document.Id, number = instrument.Number, from = from.ToString(), to = to.ToString(), date = date.ToString("yyyy-MM-dd") });
        await SaveAsync(ct);
        await tx.CommitAsync(ct);
        return new InstrumentPostResult(instrument.Id, document.Id, document.Number, warnings);
    }

    private FinanceDocument NewDocument(Instrument instrument, DateOnly date, Guid? cariId, Guid? accountId, string? note) {
        return new FinanceDocument {
                                       Kind             = FinanceDocumentKind.InstrumentEvent,
                                       Date             = date,
                                       InstrumentId     = instrument.Id,
                                       CurrentAccountId = cariId,
                                       AccountId        = accountId,
                                       Amount           = instrument.Amount,
                                       Currency         = instrument.Currency,
                                       Description      = note
                                   };
    }

    private void AddEvent(Instrument instrument, FinanceDocument document, InstrumentStatus? from, InstrumentStatus to, DateOnly date, string? note) {
        db.InstrumentEvents.Add(new InstrumentEvent {
                                                        InstrumentId = instrument.Id,
                                                        DocumentId   = document.Id,
                                                        From         = from,
                                                        To           = to,
                                                        Date         = date,
                                                        Note         = note,
                                                        Created      = clock.GetUtcNow(),
                                                        CreatedBy    = session.UserId?.ToString()
                                                    });
    }

    /// <summary>Aynı çek numarasının eşzamanlı kaydı benzersiz indekste yakalanır.</summary>
    private async Task SaveAsync(CancellationToken ct) {
        try {
            await db.SaveChangesAsync(ct);
        } catch (DbUpdateException ex) when (TreasuryErrors.IsUniqueViolation(ex) && ex.Message.Contains("instruments", StringComparison.OrdinalIgnoreCase)) {
            throw TreasuryErrors.InstrumentNumberTaken();
        }
    }

    private static LedgerEntrySource SourceOf(InstrumentKind kind) {
        return kind == InstrumentKind.Check ? LedgerEntrySource.Check : LedgerEntrySource.Note;
    }

    private static string StatusText(InstrumentStatus status) {
        return status switch {
                   InstrumentStatus.Collected => "tahsil edildi",
                   InstrumentStatus.Paid      => "ödendi",
                   _                          => status.ToString()
               };
    }

    private static void EnsureRole(CurrentAccount cari, InstrumentDirection direction) {
        if (direction == InstrumentDirection.Received && !cari.IsCustomer) throw TreasuryErrors.CariRole("Alınan çek/senet için cari müşteri olmalıdır.");
        if (direction == InstrumentDirection.Issued && !cari.IsSupplier) throw TreasuryErrors.CariRole("Verilen çek/senet için cari tedarikçi olmalıdır.");
    }

    private static void EnsureCurrency(string left, string right, string what) {
        if (left != right) throw TreasuryErrors.AccountCurrencyMismatch($"{what} para birimi çek/senet para birimiyle aynı olmalıdır.");
    }

    private static void EnsureBankAccount(TreasuryAccount account, string currency) {
        if (!account.IsActive) throw TreasuryErrors.AccountInactive();
        if (account.Kind != TreasuryAccountKind.Bank) throw TreasuryErrors.AccountMethodMismatch("Çek/senet işlemi için banka hesabı seçilmelidir.");

        EnsureCurrency(account.Currency, currency, "Hesap");
    }

    private async Task<CurrentAccount> LoadCariAsync(Guid id, CancellationToken ct) {
        return await db.CurrentAccounts.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct) ?? throw new NotFoundException(id.ToString(), "CurrentAccount");
    }

    private async Task<CurrentAccount> LoadSupplierAsync(Guid? id, string currency, CancellationToken ct) {
        if (id is null) throw new AppException(400, "cari_required", "Ciro edilecek tedarikçi carisi seçilmelidir.");

        var cari = await LoadCariAsync(id.Value, ct);
        if (!cari.IsActive) throw CariErrors.AccountInactive();

        EnsureRole(cari, InstrumentDirection.Issued);
        EnsureCurrency(cari.Currency, currency, "Cari");
        return cari;
    }

    private async Task<TreasuryAccount> RequireAccountAsync(Guid? id, string currency, bool bankOnly, CancellationToken ct) {
        if (id is null) throw new AppException(400, "account_required", "Kasa/banka hesabı seçilmelidir.");

        var account = await AccountRules.FindAsync(db, tenant, id.Value, ct);
        if (bankOnly) EnsureBankAccount(account, currency);
        else {
            if (!account.IsActive) throw TreasuryErrors.AccountInactive();
            if (account.Kind == TreasuryAccountKind.CardPos) throw TreasuryErrors.AccountMethodMismatch("Çek/senet işlemi için kasa ya da banka hesabı seçilmelidir.");

            EnsureCurrency(account.Currency, currency, "Hesap");
        }

        return account;
    }
}
