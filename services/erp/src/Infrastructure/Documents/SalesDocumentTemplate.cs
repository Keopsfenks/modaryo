using ERP.Application.Invoicing.Documents;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace ERP.Infrastructure.Documents;

internal sealed record SalesPdfLine(int No, string Code, string Name, string Unit, decimal Quantity, decimal? Width, decimal? Length, decimal UnitPrice,
                                    decimal? DiscountPercent, int VatRate, decimal Net, decimal Total);

internal sealed record SalesPdfPlanRow(string Label, DateOnly DueDate, decimal Amount);

/// <summary>Teklif, sipariş onayı ve proforma için ortak model. Watermark null değilse sayfaya filigran basılır.</summary>
internal sealed record SalesPdfModel(
    string                     Title,
    string                     NumberLabel,
    string                     Number,
    DateOnly                   Date,
    DateOnly?                  ValidUntil,
    int?                       Revision,
    string                     Currency,
    string?                    SalesRep,
    string?                    Notes,
    PartyInfo                  Seller,
    PartyInfo                  Customer,
    IReadOnlyList<SalesPdfLine> Lines,
    decimal                    Discount,
    decimal                    Net,
    decimal                    Vat,
    decimal                    Total,
    IReadOnlyList<SalesPdfPlanRow> Plan,
    string?                    Watermark,
    string                     AccentColor,
    string?                    FooterNote,
    byte[]?                    Logo
);

/// <summary>A4 satış belgesi şablonu (teklif / sipariş onayı / proforma). Ölçülü satırlarda "1,60 × 2,30" ebadı basılır.</summary>
internal sealed class SalesDocumentTemplate(SalesPdfModel model) : IDocument {
    private const string Gray  = "#6B7280";
    private const string Line  = "#D1D5DB";

    private static readonly System.Globalization.CultureInfo Tr = InvoiceMail.Tr;

    public DocumentMetadata GetMetadata() {
        return new DocumentMetadata { Title = model.Number, Author = model.Seller.Title };
    }

    public void Compose(IDocumentContainer container) {
        container.Page(page => {
            page.Size(PageSizes.A4);
            page.Margin(28);
            page.DefaultTextStyle(t => QuestPdfRenderer.BaseStyle(t).FontSize(8.5f).FontColor("#111827"));
            if (model.Watermark is { } mark)
                page.Foreground().AlignCenter().AlignMiddle().Rotate(-35).Text(mark).FontSize(120).Bold().FontColor(Colors.Grey.Lighten2);

            page.Header().Element(Header);
            page.Content().PaddingTop(10).Element(Body);
            page.Footer().Element(Footer);
        });
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

                row.ConstantItem(200).Column(meta => {
                    meta.Item().AlignRight().Text(model.Title).FontSize(13).Bold().FontColor(model.AccentColor);
                    Meta(meta, model.NumberLabel, model.Number);
                    Meta(meta, "Tarih", model.Date.ToString("dd.MM.yyyy", Tr));
                    Meta(meta, "Geçerlilik", model.ValidUntil?.ToString("dd.MM.yyyy", Tr));
                    Meta(meta, "Revizyon", model.Revision?.ToString(Tr));
                    Meta(meta, "Satış Temsilcisi", model.SalesRep);
                    Meta(meta, "Para Birimi", model.Currency == "TRY" ? "TL" : model.Currency);
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

    private void Body(IContainer c) {
        c.Column(column => {
            column.Spacing(10);
            column.Item().Element(Customer);
            column.Item().Element(Lines);
            column.Item().Element(Summary);
            if (model.Plan.Count > 0) column.Item().Element(PlanTable);
            if (!string.IsNullOrWhiteSpace(model.Notes)) column.Item().Element(Notes);
        });
    }

    private void Customer(IContainer c) {
        c.Border(0.5f).BorderColor(Line).Padding(6).Column(column => {
            column.Item().Text("MÜŞTERİ").FontSize(7).Bold().FontColor(model.AccentColor);
            column.Item().Text(model.Customer.Title).Bold();
            if (model.Customer.TaxLine is { } tax) column.Item().Text(tax).FontColor(Gray);
            if (model.Customer.Address is { } address) column.Item().Text(address).FontColor(Gray);
            if (model.Customer.Contact is { } contact) column.Item().Text(contact).FontColor(Gray);
        });
    }

    private IContainer HeadCell(IContainer c) {
        return c.Background(model.AccentColor).PaddingVertical(3).PaddingHorizontal(3);
    }

    private static IContainer BodyCell(IContainer c) {
        return c.BorderBottom(0.5f).BorderColor(Line).PaddingVertical(3).PaddingHorizontal(3);
    }

    private static string Qty(decimal value) => value.ToString("#,##0.######", Tr);

    private static string Price(decimal value) => value.ToString("#,##0.00####", Tr);

    private static string Money(decimal value) => value.ToString("N2", Tr);

    /// <summary>Ölçü metre cinsindendir: "1,60 × 2,30" (en x boy) ya da yalnızca boy.</summary>
    internal static string? Dimensions(decimal? width, decimal? length) {
        if (length is null) return null;
        var l = length.Value.ToString("0.00##", Tr);
        return width is { } w ? $"{w.ToString("0.00##", Tr)} × {l}" : l;
    }

    private void Lines(IContainer c) {
        c.Table(table => {
            table.ColumnsDefinition(columns => {
                columns.ConstantColumn(18);
                columns.RelativeColumn(4);
                columns.ConstantColumn(52);
                columns.ConstantColumn(32);
                columns.ConstantColumn(58);
                columns.ConstantColumn(32);
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
                Head("Kalem");
                Head("Miktar", true);
                Head("Birim");
                Head("Birim Fiyat", true);
                Head("İsk. %", true);
                Head("KDV %", true);
                Head("Tutar", true);
            });

            foreach (var line in model.Lines) {
                BodyCell(table.Cell()).Text(line.No.ToString(Tr));
                BodyCell(table.Cell()).Column(description => {
                    description.Item().Text(line.Name);
                    if (Dimensions(line.Width, line.Length) is { } size) description.Item().Text(size).FontSize(7.5f).FontColor(Gray);
                    description.Item().Text(line.Code).FontSize(7).FontColor(Gray);
                });
                BodyCell(table.Cell()).AlignRight().Text(Qty(line.Quantity));
                BodyCell(table.Cell()).Text(line.Unit);
                BodyCell(table.Cell()).AlignRight().Text(Price(line.UnitPrice));
                BodyCell(table.Cell()).AlignRight().Text(line.DiscountPercent is > 0m ? Qty(line.DiscountPercent.Value) : "-");
                BodyCell(table.Cell()).AlignRight().Text("%" + line.VatRate.ToString(Tr));
                BodyCell(table.Cell()).AlignRight().Text(Money(line.Net));
            }
        });
    }

    private void Summary(IContainer c) {
        var currency = model.Currency == "TRY" ? "TL" : model.Currency;
        c.ShowEntire().AlignRight().Width(230).Column(right => {
            void Row(string label, decimal value) {
                right.Item().PaddingVertical(1.5f).Row(r => {
                    r.RelativeItem().Text(label);
                    r.ConstantItem(100).AlignRight().Text($"{Money(value)} {currency}");
                });
            }

            if (model.Discount > 0m) Row("Toplam İskonto", model.Discount);
            Row("Matrah (Net)", model.Net);
            Row("Hesaplanan KDV", model.Vat);
            right.Item().PaddingTop(2).Background(model.AccentColor).Padding(4).Row(r => {
                r.RelativeItem().Text("GENEL TOPLAM").Bold().FontColor(Colors.White);
                r.ConstantItem(110).AlignRight().Text($"{Money(model.Total)} {currency}").Bold().FontColor(Colors.White);
            });
        });
    }

    private void PlanTable(IContainer c) {
        c.ShowEntire().Column(column => {
            column.Item().Text("Ödeme Planı").Bold().FontColor(model.AccentColor);
            foreach (var row in model.Plan)
                column.Item().PaddingVertical(1).Row(r => {
                    r.ConstantItem(90).Text(row.Label);
                    r.ConstantItem(80).Text(row.DueDate.ToString("dd.MM.yyyy", Tr));
                    r.ConstantItem(90).AlignRight().Text(Money(row.Amount));
                });
        });
    }

    private void Notes(IContainer c) {
        c.Column(column => {
            column.Item().Text("Açıklama").Bold().FontColor(model.AccentColor);
            column.Item().Text(model.Notes!);
        });
    }

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
