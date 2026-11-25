using System.Runtime.CompilerServices;
using ERP.Application.Common.Interfaces;
using ERP.Application.Settings.Categories;
using ERP.Domain.Treasury;

namespace ERP.Application.Accounting.Projectors;

/// <summary>
///     Çek/senet hareketleri (her hareket bir CS belgesi). P = portföy hesabı: alınan çek 101, alınan senet 121, verilen çek 103, verilen senet 321.
///     Alınan:   alındı                      B P            A 120[.müşteri]
///               portföy → ciro              B 320[.tedarikçi]  A P
///               portföy → tahsile verildi   fiş yok (aynı portföy hesabı, cari yok)
///               portföy → müşteriye iade    B 120[.müşteri]    A P
///               tahsilde → tahsil edildi    B 102 (tahsil hesabı)  A P
///               tahsilde → karşılıksız      B 120[.müşteri]    A P (protesto)
///               ciro → karşılıksız          B P  A 320[.tedarikçi]  ve  B 120[.müşteri]  A P (iade gelir, müşteri yeniden borçlanır)
///               karşılıksız → geç tahsil    B 100 | 102 (hesap)  A 120[.müşteri]
///     Verilen:  düzenlendi                  B 320[.tedarikçi]  A P
///               ödendi                      B P  A 102 (ödeme hesabı)
///               tedarikçiden iade           B P  A 320[.tedarikçi]
///     Tutarlar çek/senet para biriminden TL'ye vade tarihine değil hareket tarihine göre çevrilir.
/// </summary>
public sealed class InstrumentProjector(IApplicationDbContext db, AccountingContext context) : IJournalProjector {
    public const string DocumentType = "Çek/Senet";

    public string SourceType => "Instrument";

    public async IAsyncEnumerable<ProjectedVoucher> ProjectAsync(DateOnly from, DateOnly to, AccountMapping map, [EnumeratorCancellation] CancellationToken ct) {
        var codes = await context.CodesAsync(map, ct);
        var first = from;

        var events = await (from e in db.InstrumentEvents.AsNoTracking()
                            join i in db.Instruments.AsNoTracking() on e.InstrumentId equals i.Id
                            join d in db.FinanceDocuments.AsNoTracking() on e.DocumentId equals d.Id
                            where e.Date >= first && e.Date <= to
                            orderby e.Date, d.Number
                            select new { Event = e, Instrument = i, Document = d }).ToListAsync(ct);

        var accounts = await db.TreasuryAccounts.AsNoTracking().ToDictionaryAsync(a => a.Id, ct);
        var caris    = await context.CarisAsync(events.SelectMany(e => new Guid?[] { e.Instrument.CurrentAccountId, e.Instrument.EndorsedToId }), ct);

        foreach (var row in events) {
            var instrument = row.Instrument;
            var amount     = await context.TlAsync(instrument.Amount, instrument.Currency, row.Event.Date, null, ct);
            var portfolio  = codes.InstrumentPortfolio(instrument.Kind, instrument.Direction);
            var owner      = caris.GetValueOrDefault(instrument.CurrentAccountId);
            var supplier   = instrument.EndorsedToId is { } s ? caris.GetValueOrDefault(s) : null;
            var account    = row.Document.AccountId is { } a ? accounts.GetValueOrDefault(a) : null;
            var text       = $"{(instrument.Kind == InstrumentKind.Check ? "Çek" : "Senet")} {instrument.Number}";
            var lines      = new LineSet();

            switch (instrument.Direction, row.Event.From, row.Event.To) {
                case (InstrumentDirection.Received, null, InstrumentStatus.InPortfolio):
                    lines.Debit(portfolio, amount, text + ": alındı").Credit(codes.Customer(owner), amount, text + ": alındı", instrument.CurrentAccountId);
                    break;
                case (InstrumentDirection.Received, InstrumentStatus.InPortfolio, InstrumentStatus.Endorsed):
                    lines.Debit(codes.Supplier(supplier), amount, text + ": ciro edildi", instrument.EndorsedToId).Credit(portfolio, amount, text + ": ciro edildi");
                    break;
                case (InstrumentDirection.Received, InstrumentStatus.InPortfolio, InstrumentStatus.ReturnedToCustomer):
                    lines.Debit(codes.Customer(owner), amount, text + ": müşteriye iade", instrument.CurrentAccountId).Credit(portfolio, amount, text + ": müşteriye iade");
                    break;
                case (InstrumentDirection.Received, InstrumentStatus.InCollection, InstrumentStatus.Collected):
                    lines.Debit(codes.Treasury(account), amount, text + ": tahsil edildi").Credit(portfolio, amount, text + ": tahsil edildi");
                    break;
                case (InstrumentDirection.Received, InstrumentStatus.InCollection, InstrumentStatus.Bounced):
                    lines.Debit(codes.Customer(owner), amount, text + ": karşılıksız", instrument.CurrentAccountId).Credit(portfolio, amount, text + ": karşılıksız");
                    break;
                case (InstrumentDirection.Received, InstrumentStatus.Endorsed, InstrumentStatus.Bounced):
                    lines.Debit(portfolio, amount, text + ": tedarikçiden iade geldi").Credit(codes.Supplier(supplier), amount, text + ": tedarikçiden iade geldi", instrument.EndorsedToId)
                         .Debit(codes.Customer(owner), amount, text + ": müşteri yeniden borçlandı", instrument.CurrentAccountId).Credit(portfolio, amount, text + ": müşteri yeniden borçlandı");
                    break;
                case (InstrumentDirection.Received, InstrumentStatus.Bounced, InstrumentStatus.Collected):
                    lines.Debit(codes.Treasury(account), amount, text + ": geç tahsil edildi").Credit(codes.Customer(owner), amount, text + ": geç tahsil edildi", instrument.CurrentAccountId);
                    break;
                case (InstrumentDirection.Issued, null, InstrumentStatus.Issued):
                    lines.Debit(codes.Supplier(owner), amount, text + ": düzenlendi", instrument.CurrentAccountId).Credit(portfolio, amount, text + ": düzenlendi");
                    break;
                case (InstrumentDirection.Issued, InstrumentStatus.Issued, InstrumentStatus.Paid):
                    lines.Debit(portfolio, amount, text + ": ödendi").Credit(codes.Treasury(account), amount, text + ": ödendi");
                    break;
                case (InstrumentDirection.Issued, InstrumentStatus.Issued, InstrumentStatus.ReturnedFromSupplier):
                    lines.Debit(portfolio, amount, text + ": tedarikçiden iade alındı").Credit(codes.Supplier(owner), amount, text + ": tedarikçiden iade alındı", instrument.CurrentAccountId);
                    break;
            }

            if (lines.Lines.Count == 0) continue; // tahsile verme: muhasebe etkisi yok

            yield return new ProjectedVoucher(row.Document.Number, row.Event.Date, DocumentType, row.Document.Number, lines.Lines);
        }
    }
}
