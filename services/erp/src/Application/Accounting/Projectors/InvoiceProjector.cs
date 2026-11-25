using System.Runtime.CompilerServices;
using ERP.Application.Common.Interfaces;
using ERP.Application.Settings.Categories;
using ERP.Domain.CurrentAccounts;
using ERP.Domain.Invoicing;

namespace ERP.Application.Accounting.Projectors;

/// <summary>
///     Kesilmiş (taslak olmayan) faturalar. Tutarlar TL alanlarındandır; cari satırı bileşenlerin toplamından türetilir, fiş her zaman dengelidir.
///     Satış:        B 120[.cari] (net + ÖTV + KDV)                 A 600 (net + ÖTV)   A 391 (KDV)
///                   KDV tevkifatı: alıcı öder, satıcı alacağını tam yazar; tevkifat ayrı satır açmaz.
///     Alış:         B 153 (net + ÖTV)   B 191 (KDV)                 A 320[.cari] (net + ÖTV + KDV - tevkifat)   A 360 (tevkifat)
///     Satış iade:   B 610 (net + ÖTV)   B 391 (KDV)                 A 120[.cari] (net + ÖTV + KDV)
///     Alış iade:    B 320[.cari] (net + ÖTV + KDV - tevkifat)   B 360 (tevkifat)   A 153 (net + ÖTV)   A 191 (KDV)
///     Kur farkı:    B 120[.cari] (net + KDV)                        A 646 (net)         A 391 (KDV)
///     İptal edilen fatura: özgün fiş + iptal tarihli ters fiş. İndirim netten düşülmüş sayılır (611 kullanılmaz).
/// </summary>
public sealed class InvoiceProjector(IApplicationDbContext db, AccountingContext context) : IJournalProjector {
    public string SourceType => "Invoice";

    public async IAsyncEnumerable<ProjectedVoucher> ProjectAsync(DateOnly from, DateOnly to, AccountMapping map, [EnumeratorCancellation] CancellationToken ct) {
        var codes = await context.CodesAsync(map, ct);

        var invoices = await db.Invoices.AsNoTracking()
                           .Where(i => i.Number != null && i.Status != InvoiceStatus.Draft && ((i.Date >= from && i.Date <= to) || i.CancelledAt != null))
                           .OrderBy(i => i.Date).ThenBy(i => i.Number)
                           .ToListAsync(ct);
        var caris = await context.CarisAsync(invoices.Select(i => (Guid?)i.CurrentAccountId), ct);

        foreach (var invoice in invoices) {
            var cari     = caris.GetValueOrDefault(invoice.CurrentAccountId);
            var original = Build(invoice, codes, cari);

            if (VoucherRules.InRange(invoice.Date, from, to)) yield return original;

            if (invoice.CancelledAt is { } cancelledAt && VoucherRules.InRange(AccountingContext.DateOf(cancelledAt), from, to))
                yield return VoucherRules.Reverse(original, AccountingContext.DateOf(cancelledAt));
        }
    }

    private static ProjectedVoucher Build(Invoice invoice, AccountCodes codes, CurrentAccount? cari) {
        var map      = codes.Map;
        var number   = invoice.Number!;
        var net      = invoice.NetTl + invoice.OtvTl;
        var vat      = invoice.VatTl;
        var withheld = invoice.WithheldTl;
        var text     = $"{Label(invoice.Kind)} {number}";
        var lines    = new LineSet();

        switch (invoice.Kind) {
            case InvoiceKind.Sales:
                lines.Debit(codes.Customer(cari), net + vat, text, invoice.CurrentAccountId).Credit(map.Sales, net, text).Credit(map.VatOutput, vat, text);
                break;
            case InvoiceKind.ExchangeDifference:
                lines.Debit(codes.Customer(cari), net + vat, text, invoice.CurrentAccountId).Credit(map.FxGain, net, text).Credit(map.VatOutput, vat, text);
                break;
            case InvoiceKind.SalesReturn:
                lines.Debit(map.SalesReturns, net, text).Debit(map.VatOutput, vat, text).Credit(codes.Customer(cari), net + vat, text, invoice.CurrentAccountId);
                break;
            case InvoiceKind.Purchase:
                lines.Debit(map.PurchasesInventory, net, text).Debit(map.VatInput, vat, text)
                     .Credit(codes.Supplier(cari), net + vat - withheld, text, invoice.CurrentAccountId).Credit(map.WithholdingVatPayable, withheld, text);
                break;
            case InvoiceKind.PurchaseReturn:
                lines.Debit(codes.Supplier(cari), net + vat - withheld, text, invoice.CurrentAccountId).Debit(map.WithholdingVatPayable, withheld, text)
                     .Credit(map.PurchasesInventory, net, text).Credit(map.VatInput, vat, text);
                break;
        }

        return new ProjectedVoucher(number, invoice.Date, Label(invoice.Kind), number, lines.Lines);
    }

    private static string Label(InvoiceKind kind) {
        return kind switch {
                   InvoiceKind.Sales              => "Satış Faturası",
                   InvoiceKind.Purchase           => "Alış Faturası",
                   InvoiceKind.SalesReturn        => "Satış İade Faturası",
                   InvoiceKind.PurchaseReturn     => "Alış İade Faturası",
                   InvoiceKind.ExchangeDifference => "Kur Farkı Faturası",
                   _                              => kind.ToString()
               };
    }
}
