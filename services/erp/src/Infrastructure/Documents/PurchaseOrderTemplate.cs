using ERP.Application.Invoicing.Documents;
using ERP.Domain.Purchasing;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace ERP.Infrastructure.Documents;

internal sealed record PoPdfLine(int No, string Code, string Name, string Unit, decimal Quantity, decimal? UnitPrice, decimal? DiscountPercent, int VatRate,
                                 DateOnly? ExpectedDate, decimal? Net);

/// <summary>Fiyat alanları null ise (alış fiyatı yetkisi yok) fiyat sütunları ve toplamlar hiç basılmaz.</summary>
internal sealed record PoPdfModel(
    string                  Number,
    PurchaseOrderStatus     Status,
    DateOnly                Date,
    DateOnly?               ExpectedDate,
    string                  Currency,
    decimal                 Rate,
    string?                 Notes,
    PartyInfo               Buyer,
    PartyInfo               Supplier,
    IReadOnlyList<PoPdfLine> Lines,
    decimal?                Net,
    decimal?                Vat,
    decimal?                Total,
    string                  AccentColor,
    string?                 FooterNote,
    byte[]?                 Logo
);

/// <summary>A4 satın alma siparişi şablonu; yazı tipleri fatura şablonuyla aynıdır.</summary>
internal sealed class PurchaseOrderTemplate(PoPdfModel model) : IDocument {
    private const string Gray  = "#6B7280";
    private const string Light = "#F3F4F6";
    private const string Line  = "#D1D5DB";

    private static readonly System.Globalization.CultureInfo Tr = InvoiceMail.Tr;

    private bool ShowPrices => model.Total is not null;

    public DocumentMetadata GetMetadata() {
        return new DocumentMetadata { Title = model.Number, Author = model.Buyer.Title };
    }

    public void Compose(IDocumentContainer container) {
        container.Page(page => {
            page.Size(PageSizes.A4);
            page.Margin(28);
            page.DefaultTextStyle(t => QuestPdfRenderer.BaseStyle(t).FontSize(8.5f).FontColor("#111827"));

            if (model.Status == PurchaseOrderStatus.Draft)
                page.Foreground().AlignCenter().AlignMiddle().Rotate(-35).Text("TASLAK").FontSize(120).Bold().FontColor(Colors.Grey.Lighten2);
            else if (model.Status is PurchaseOrderStatus.Cancelled)
                page.Foreground().AlignCenter().AlignMiddle().Rotate(-35).Text("İPTAL").FontSize(120).Bold().FontColor(Colors.Red.Lighten4);

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
                        info.Item().Text(model.Buyer.Title).FontSize(13).Bold().FontColor(model.AccentColor);
                        if (model.Buyer.TaxLine is { } tax) info.Item().Text(tax).FontColor(Gray);
                        if (model.Buyer.Address is { } address) info.Item().Text(address).FontColor(Gray);
                        if (model.Buyer.Contact is { } contact) info.Item().Text(contact).FontColor(Gray);
                    });
                });

                row.ConstantItem(200).Column(meta => {
                    meta.Item().AlignRight().Text("SATIN ALMA SİPARİŞİ").FontSize(13).Bold().FontColor(model.AccentColor);
                    Meta(meta, "Sipariş No", model.Number);
                    Meta(meta, "Tarih", model.Date.ToString("dd.MM.yyyy", Tr));
                    Meta(meta, "Beklenen Tarih", model.ExpectedDate?.ToString("dd.MM.yyyy", Tr));
                    if (ShowPrices) Meta(meta, "Para Birimi", model.Currency == "TRY" ? "TL" : model.Currency);
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
            column.Item().Element(Supplier);
            column.Item().Element(Lines);
            if (ShowPrices) column.Item().Element(Summary);
            if (!string.IsNullOrWhiteSpace(model.Notes)) column.Item().Element(Notes);
        });
    }

    private void Supplier(IContainer c) {
        c.Border(0.5f).BorderColor(Line).Padding(6).Column(column => {
            column.Item().Text("TEDARİKÇİ").FontSize(7).Bold().FontColor(model.AccentColor);
            column.Item().Text(model.Supplier.Title).Bold();
            if (model.Supplier.TaxLine is { } tax) column.Item().Text(tax).FontColor(Gray);
            if (model.Supplier.Address is { } address) column.Item().Text(address).FontColor(Gray);
            if (model.Supplier.Contact is { } contact) column.Item().Text(contact).FontColor(Gray);
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

    private void Lines(IContainer c) {
        c.Table(table => {
            table.ColumnsDefinition(columns => {
                columns.ConstantColumn(18);
                columns.RelativeColumn(4);
                columns.ConstantColumn(52);
                columns.ConstantColumn(32);
                columns.ConstantColumn(62);
                if (ShowPrices) {
                    columns.ConstantColumn(56);
                    columns.ConstantColumn(32);
                    columns.ConstantColumn(62);
                }
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
                Head("Beklenen Tarih");
                if (ShowPrices) {
                    Head("Birim Fiyat", true);
                    Head("KDV %", true);
                    Head("Tutar", true);
                }
            });

            foreach (var line in model.Lines) {
                BodyCell(table.Cell()).Text(line.No.ToString(Tr));
                BodyCell(table.Cell()).Column(description => {
                    description.Item().Text(line.Name);
                    description.Item().Text(line.Code).FontSize(7).FontColor(Gray);
                });
                BodyCell(table.Cell()).AlignRight().Text(Qty(line.Quantity));
                BodyCell(table.Cell()).Text(line.Unit);
                BodyCell(table.Cell()).Text(line.ExpectedDate?.ToString("dd.MM.yyyy", Tr) ?? "-");
                if (ShowPrices) {
                    BodyCell(table.Cell()).AlignRight().Text(Price(line.UnitPrice ?? 0m));
                    BodyCell(table.Cell()).AlignRight().Text("%" + line.VatRate.ToString(Tr));
                    BodyCell(table.Cell()).AlignRight().Text(Money(line.Net ?? 0m));
                }
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

            Row("Matrah (Net)", model.Net ?? 0m);
            Row("Hesaplanan KDV", model.Vat ?? 0m);
            right.Item().PaddingTop(2).Background(model.AccentColor).Padding(4).Row(r => {
                r.RelativeItem().Text("GENEL TOPLAM").Bold().FontColor(Colors.White);
                r.ConstantItem(110).AlignRight().Text($"{Money(model.Total ?? 0m)} {currency}").Bold().FontColor(Colors.White);
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
