using ERP.Application.Invoicing;
using ERP.Application.Invoicing.Documents;
using ERP.Domain.Invoicing;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace ERP.Infrastructure.Documents;

/// <summary>Bir taraf (satıcı ya da alıcı) için PDF'te gösterilen bilgi.</summary>
internal sealed record PartyInfo(string Title, string? TaxLine, string? Address, string? Contact);

/// <summary>PDF'in ihtiyaç duyduğu, çözümlenmiş veriler (yazı tipi/IO yok; şablon yalnızca çizer).</summary>
internal sealed record InvoicePdfModel(
    InvoiceDto                  Invoice,
    PartyInfo                   Seller,
    PartyInfo                   Buyer,
    string                      AccentColor,
    string?                     FooterNote,
    byte[]?                     Logo,
    IReadOnlyList<(string BankName, string Iban, string Currency)> Ibans,
    IReadOnlyDictionary<string, string> WithholdingNotes,
    IReadOnlyDictionary<string, string> ExemptionNotes
);

/// <summary>A4 fatura şablonu. Yazı tipi: QuestPDF'in gömülü Lato'su (Türkçe İ ı Ş ş Ğ ğ Ç Ö Ü içerir); eksik glifler için gömülü Noto Sans yedeği.</summary>
internal sealed class InvoiceTemplate(InvoicePdfModel model) : IDocument {
    private const string Gray  = "#6B7280";
    private const string Light = "#F3F4F6";
    private const string Line  = "#D1D5DB";

    private static readonly System.Globalization.CultureInfo Tr = InvoiceMail.Tr;

    private InvoiceDto Invoice => model.Invoice;

    public DocumentMetadata GetMetadata() {
        return new DocumentMetadata { Title = Invoice.Number ?? "Taslak fatura", Author = model.Seller.Title };
    }

    public void Compose(IDocumentContainer container) {
        container.Page(page => {
            page.Size(PageSizes.A4);
            page.Margin(28);
            page.DefaultTextStyle(t => QuestPdfRenderer.BaseStyle(t).FontSize(8.5f).FontColor("#111827"));

            if (Invoice.Status == InvoiceStatus.Draft) Watermark(page, "TASLAK", Colors.Grey.Lighten2);
            else if (Invoice.Status == InvoiceStatus.Cancelled) Watermark(page, "İPTAL", Colors.Red.Lighten4);

            page.Header().Element(Header);
            page.Content().PaddingTop(10).Element(Body);
            page.Footer().Element(Footer);
        });
    }

    private static void Watermark(PageDescriptor page, string text, string color) {
        page.Foreground().AlignCenter().AlignMiddle().Rotate(-35).Text(text).FontSize(120).Bold().FontColor(color);
    }

    // ---- üst bilgi ---------------------------------------------------------------------------------------------

    private static string KindTitle(InvoiceKind kind) {
        return kind switch {
                   InvoiceKind.Sales          => "SATIŞ FATURASI",
                   InvoiceKind.Purchase       => "ALIŞ FATURASI",
                   InvoiceKind.SalesReturn    => "SATIŞ İADE FATURASI",
                   InvoiceKind.PurchaseReturn => "ALIŞ İADE FATURASI",
                   _                          => "KUR FARKI FATURASI"
               };
    }

    private static string ModeLabel(InvoiceMode mode, EInvoiceScenario? scenario) {
        return mode switch {
                   InvoiceMode.EInvoice => scenario == EInvoiceScenario.Commercial ? "e-Fatura (Ticari)" : "e-Fatura (Temel)",
                   InvoiceMode.EArchive => "e-Arşiv Fatura",
                   _                    => "Kâğıt Fatura"
               };
    }

    private void Header(IContainer c) {
        c.Column(column => {
            column.Item().Row(row => {
                row.RelativeItem().Row(left => {
                    if (model.Logo is not null) left.ConstantItem(70).PaddingRight(8).AlignMiddle().MaxHeight(55).Image(model.Logo).FitArea();
                    left.RelativeItem().Column(info => {
                        info.Item().Text(model.Seller.Title).FontSize(13).Bold().FontColor(model.AccentColor);
                        if (model.Seller.TaxLine is { } tax) info.Item().Text(tax).FontColor(Gray);
                        if (model.Seller.Address is { } address) info.Item().Text(address).FontColor(Gray);
                        if (model.Seller.Contact is { } contact) info.Item().Text(contact).FontColor(Gray);
                    });
                });

                row.ConstantItem(190).Column(meta => {
                    meta.Item().AlignRight().Text(KindTitle(Invoice.Kind)).FontSize(13).Bold().FontColor(model.AccentColor);
                    if (Invoice.Status != InvoiceStatus.Draft) meta.Item().AlignRight().Text(ModeLabel(Invoice.Mode, Invoice.Scenario)).FontColor(Gray);
                    Meta(meta, "Fatura No", Invoice.Number);
                    Meta(meta, "Tedarikçi Fatura No", Invoice.ExternalNumber);
                    Meta(meta, "Tarih", Invoice.Date.ToString("dd.MM.yyyy", Tr) + (Invoice.Time is { } time ? " " + time.ToString("HH:mm", Tr) : string.Empty));
                    Meta(meta, "Para Birimi", Invoice.Currency == "TRY" ? "TL" : Invoice.Currency);
                    if (Invoice.Currency != "TRY") Meta(meta, "Kur", Invoice.Rate.ToString("N4", Tr));
                });
            });
            column.Item().PaddingTop(6).LineHorizontal(1.5f).LineColor(model.AccentColor);
        });
    }

    private static void Meta(ColumnDescriptor column, string label, string? value) {
        if (string.IsNullOrWhiteSpace(value)) return;
        column.Item().AlignRight().Text(t => {
            t.Span(label + ": ").FontColor(Gray);
            t.Span(value).SemiBold();
        });
    }

    // ---- gövde -------------------------------------------------------------------------------------------------

    private void Body(IContainer c) {
        c.Column(column => {
            column.Spacing(10);
            column.Item().Element(Parties);
            column.Item().Element(Lines);
            column.Item().Element(Summary);
            column.Item().ShowEntire().Element(Words);
            if (Invoice.DueSchedule.Count > 1) column.Item().ShowEntire().Element(Dues);
            if (model.WithholdingNotes.Count + model.ExemptionNotes.Count > 0) column.Item().Element(CodeNotes);
            if (!string.IsNullOrWhiteSpace(Invoice.Notes)) column.Item().Element(Notes);
            if (model.Ibans.Count > 0) column.Item().ShowEntire().Element(Ibans);
        });
    }

    private void Parties(IContainer c) {
        c.Row(row => {
            Party(row.RelativeItem(), model.Seller, "SATICI");
            row.ConstantItem(12);
            Party(row.RelativeItem(), model.Buyer, "ALICI");
        });
    }

    private void Party(IContainer c, PartyInfo party, string label) {
        c.Border(0.5f).BorderColor(Line).Padding(6).Column(column => {
            column.Item().Text(label).FontSize(7).Bold().FontColor(model.AccentColor);
            column.Item().Text(party.Title).Bold();
            if (party.TaxLine is { } tax) column.Item().Text(tax).FontColor(Gray);
            if (party.Address is { } address) column.Item().Text(address).FontColor(Gray);
            if (party.Contact is { } contact) column.Item().Text(contact).FontColor(Gray);
        });
    }

    private IContainer HeadCell(IContainer c) {
        return c.Background(model.AccentColor).PaddingVertical(3).PaddingHorizontal(3);
    }

    private static IContainer BodyCell(IContainer c) {
        return c.BorderBottom(0.5f).BorderColor(Line).PaddingVertical(3).PaddingHorizontal(3);
    }

    private static string Qty(decimal value) {
        return value.ToString("#,##0.######", Tr);
    }

    private static string Price(decimal value) {
        return value.ToString("#,##0.00####", Tr);
    }

    private static string Money(decimal value) {
        return value.ToString("N2", Tr);
    }

    private void Lines(IContainer c) {
        var hasDiscount = Invoice.Lines.Any(l => l.LineDiscount != 0m);
        var hasOtv      = Invoice.Lines.Any(l => l.Otv != 0m);

        c.Table(table => {
            table.ColumnsDefinition(columns => {
                columns.ConstantColumn(18);
                columns.RelativeColumn(4);
                columns.ConstantColumn(48);
                columns.ConstantColumn(30);
                columns.ConstantColumn(56);
                if (hasDiscount) columns.ConstantColumn(48);
                if (hasOtv) columns.ConstantColumn(44);
                columns.ConstantColumn(32);
                columns.ConstantColumn(62);
            });

            table.Header(header => {
                void Head(string text, bool right = false) {
                    var cell = HeadCell(header.Cell());
                    var t    = right ? cell.AlignRight() : cell.AlignLeft();
                    t.Text(text).FontColor(Colors.White).Bold().FontSize(7.5f);
                }

                Head("#");
                Head("Açıklama");
                Head("Miktar", true);
                Head("Birim");
                Head("Birim Fiyat", true);
                if (hasDiscount) Head("İskonto", true);
                if (hasOtv) Head("ÖTV", true);
                Head("KDV %", true);
                Head("Tutar", true);
            });

            var index = 0;
            foreach (var line in Invoice.Lines) {
                index++;
                BodyCell(table.Cell()).Text(index.ToString(Tr));
                BodyCell(table.Cell()).Column(description => {
                    description.Item().Text(line.Description);
                    var details = new List<string>();
                    if (line.Width is not null && line.Length is not null) details.Add($"{Qty(line.Width.Value)} x {Qty(line.Length.Value)}");
                    if (line.VatIncluded) details.Add("KDV dahil fiyat");
                    if (line.WithholdingCode is { } w && model.WithholdingNotes.TryGetValue(w, out var wn)) details.Add($"Tevkifat: {wn}");
                    if (line.ExemptionCode is { } x) details.Add($"İstisna: {x}" + (string.IsNullOrWhiteSpace(line.ExemptionReason) ? string.Empty : $" - {line.ExemptionReason}"));
                    if (details.Count > 0) description.Item().Text(string.Join(" | ", details)).FontSize(7).FontColor(Gray);
                });
                BodyCell(table.Cell()).AlignRight().Text(Qty(line.Quantity));
                BodyCell(table.Cell()).Text(line.Unit);
                BodyCell(table.Cell()).AlignRight().Text(Price(line.UnitPrice));
                if (hasDiscount) BodyCell(table.Cell()).AlignRight().Text(line.LineDiscount == 0m ? "-" : Money(line.LineDiscount));
                if (hasOtv) BodyCell(table.Cell()).AlignRight().Text(line.Otv == 0m ? "-" : Money(line.Otv));
                BodyCell(table.Cell()).AlignRight().Text("%" + line.VatRate.ToString(Tr));
                BodyCell(table.Cell()).AlignRight().Text(Money(line.Net));
            }
        });
    }

    private void Summary(IContainer c) {
        var totals   = Invoice.Totals;
        var currency = Invoice.Currency == "TRY" ? "TL" : Invoice.Currency;

        c.ShowEntire().Row(row => {
            // KDV özeti
            row.RelativeItem().PaddingRight(12).Column(left => {
                left.Item().Text("KDV Özeti").Bold().FontColor(model.AccentColor);
                left.Item().Table(table => {
                    table.ColumnsDefinition(columns => {
                        columns.RelativeColumn(1);
                        columns.RelativeColumn(2);
                        columns.RelativeColumn(2);
                    });
                    table.Header(header => {
                        header.Cell().Background(Light).Padding(3).Text("Oran").Bold();
                        header.Cell().Background(Light).Padding(3).AlignRight().Text("Matrah").Bold();
                        header.Cell().Background(Light).Padding(3).AlignRight().Text("KDV").Bold();
                    });
                    foreach (var rate in Invoice.VatSummary) {
                        BodyCell(table.Cell()).Text("%" + rate.Rate.ToString(Tr));
                        BodyCell(table.Cell()).AlignRight().Text(Money(rate.Base));
                        BodyCell(table.Cell()).AlignRight().Text(Money(rate.Vat));
                    }
                });
            });

            // Toplamlar
            row.ConstantItem(210).Column(right => {
                void Row(string label, decimal value, bool strong = false, bool always = true) {
                    if (!always && value == 0m) return;
                    right.Item().PaddingVertical(1.5f).Row(r => {
                        var l = r.RelativeItem().Text(label);
                        var v = r.ConstantItem(90).AlignRight().Text($"{Money(value)} {currency}");
                        if (strong) {
                            l.Bold();
                            v.Bold();
                        }
                    });
                }

                Row("Mal/Hizmet Toplamı", totals.Gross);
                Row("İskonto", -totals.Discount, always: false);
                Row("Matrah (Net)", totals.Net);
                Row("ÖTV", totals.Otv, always: false);
                Row("Hesaplanan KDV", totals.Vat);
                Row("KDV Tevkifatı", -totals.Withheld, always: false);
                right.Item().PaddingTop(2).Background(model.AccentColor).Padding(4).Row(r => {
                    r.RelativeItem().Text("ÖDENECEK TUTAR").Bold().FontColor(Colors.White);
                    r.ConstantItem(100).AlignRight().Text($"{Money(totals.Payable)} {currency}").Bold().FontColor(Colors.White);
                });
                if (Invoice.Currency != "TRY")
                    right.Item().PaddingTop(2).AlignRight().Text($"TL karşılığı: {Money(Invoice.TotalsTl.Payable)} TL").FontColor(Gray);
            });
        });
    }

    private void Words(IContainer c) {
        var words = TurkishAmountInWords.Format(Invoice.Totals.Payable, Invoice.Currency);
        c.Background(Light).Padding(6).Text(words).Italic();
    }

    private void Dues(IContainer c) {
        c.Column(column => {
            column.Item().Text("Vade Planı").Bold().FontColor(model.AccentColor);
            foreach (var due in Invoice.DueSchedule)
                column.Item().Text($"{due.DueDate.ToString("dd.MM.yyyy", Tr)}  -  {Money(due.Amount)}");
        });
    }

    private void CodeNotes(IContainer c) {
        c.Column(column => {
            foreach (var (code, text) in model.WithholdingNotes) column.Item().Text($"Tevkifat {code}: {text}").FontSize(7).FontColor(Gray);
            foreach (var (code, text) in model.ExemptionNotes) column.Item().Text($"İstisna {code}: {text}").FontSize(7).FontColor(Gray);
        });
    }

    private void Notes(IContainer c) {
        c.Column(column => {
            column.Item().Text("Açıklama").Bold().FontColor(model.AccentColor);
            column.Item().Text(Invoice.Notes!);
        });
    }

    private void Ibans(IContainer c) {
        c.Column(column => {
            column.Item().Text("Banka Hesapları").Bold().FontColor(model.AccentColor);
            foreach (var (bank, iban, currency) in model.Ibans)
                column.Item().Text($"{bank} ({currency}): {FormatIban(iban)}");
        });
    }

    private static string FormatIban(string iban) {
        return string.Join(" ", Enumerable.Range(0, (iban.Length + 3) / 4).Select(i => iban.Substring(i * 4, Math.Min(4, iban.Length - i * 4))));
    }

    // ---- alt bilgi ---------------------------------------------------------------------------------------------

    private void Footer(IContainer c) {
        c.Column(column => {
            column.Item().LineHorizontal(0.5f).LineColor(Line);
            column.Item().PaddingTop(3).Row(row => {
                row.RelativeItem().Text(model.FooterNote ?? string.Empty).FontSize(7.5f).FontColor(Gray);
                row.ConstantItem(80).AlignRight().Text(t => {
                    t.DefaultTextStyle(s => s.FontSize(7.5f).FontColor(Gray));
                    t.Span("Sayfa ");
                    t.CurrentPageNumber();
                    t.Span(" / ");
                    t.TotalPages();
                });
            });
        });
    }
}
