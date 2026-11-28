using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace ERP.Infrastructure.Documents;

internal sealed record PlanPdfStop(int No, string ShipmentNumber, string Customer, string? Address, string? Phone, string Items);

internal sealed record PlanPdfModel(
    string                     Number,
    DateOnly                   Date,
    string?                    Vehicle,
    string?                    Driver,
    string                     Firm,
    IReadOnlyList<PlanPdfStop> Stops,
    string                     AccentColor,
    string?                    FooterNote
);

/// <summary>A4 rota föyü: sürücünün elinde tutacağı, sıralı duraklar (müşteri, adres, telefon, kalemler) ve teslim alan imza satırı.</summary>
internal sealed class DeliveryPlanTemplate(PlanPdfModel model) : IDocument {
    private const string Gray = "#6B7280";
    private const string Line = "#D1D5DB";

    private static readonly System.Globalization.CultureInfo Tr = System.Globalization.CultureInfo.GetCultureInfo("tr-TR");

    public DocumentMetadata GetMetadata() {
        return new DocumentMetadata { Title = model.Number, Author = model.Firm };
    }

    public void Compose(IDocumentContainer container) {
        container.Page(page => {
            page.Size(PageSizes.A4);
            page.Margin(28);
            page.DefaultTextStyle(t => QuestPdfRenderer.BaseStyle(t).FontSize(9f).FontColor("#111827"));

            page.Header().Column(column => {
                column.Item().Row(row => {
                    row.RelativeItem().Text(model.Firm).FontSize(12).Bold().FontColor(model.AccentColor);
                    row.ConstantItem(230).Column(meta => {
                        meta.Item().AlignRight().Text("TESLİMAT ROTA FÖYÜ").FontSize(13).Bold().FontColor(model.AccentColor);
                        meta.Item().AlignRight().Text($"Plan No: {model.Number}");
                        meta.Item().AlignRight().Text($"Tarih: {model.Date.ToString("dd.MM.yyyy", Tr)}");
                        if (!string.IsNullOrWhiteSpace(model.Vehicle)) meta.Item().AlignRight().Text($"Araç: {model.Vehicle}");
                        if (!string.IsNullOrWhiteSpace(model.Driver)) meta.Item().AlignRight().Text($"Sürücü: {model.Driver}");
                    });
                });
                column.Item().PaddingTop(6).LineHorizontal(1.5f).LineColor(model.AccentColor);
            });

            page.Content().PaddingTop(10).Table(table => {
                table.ColumnsDefinition(columns => {
                    columns.ConstantColumn(18);
                    columns.RelativeColumn(4);
                    columns.RelativeColumn(4);
                    columns.RelativeColumn(3);
                });

                table.Header(header => {
                    void Head(string text) => header.Cell().Background(model.AccentColor).PaddingVertical(3).PaddingHorizontal(3).Text(text).FontColor(Colors.White).Bold().FontSize(7.5f);

                    Head("#");
                    Head("Müşteri / Adres / Telefon");
                    Head("Sevk / Kalemler");
                    Head("Teslim alan (ad, imza)");
                });

                IContainer Cell(IContainer c) => c.BorderBottom(0.5f).BorderColor(Line).PaddingVertical(5).PaddingHorizontal(3);

                foreach (var stop in model.Stops) {
                    Cell(table.Cell()).Text(stop.No.ToString(Tr)).Bold();
                    Cell(table.Cell()).Column(who => {
                        who.Item().Text(stop.Customer).Bold();
                        if (!string.IsNullOrWhiteSpace(stop.Address)) who.Item().Text(stop.Address).FontSize(8);
                        if (!string.IsNullOrWhiteSpace(stop.Phone)) who.Item().Text($"Tel: {stop.Phone}").FontSize(8).FontColor(Gray);
                    });
                    Cell(table.Cell()).Column(what => {
                        what.Item().Text(stop.ShipmentNumber).FontSize(8).FontColor(Gray);
                        what.Item().Text(stop.Items).FontSize(8);
                    });
                    Cell(table.Cell()).PaddingTop(18).LineHorizontal(0.5f).LineColor(Gray);
                }
            });

            page.Footer().Row(row => {
                row.RelativeItem().Text(model.FooterNote ?? string.Empty).FontSize(7.5f).FontColor(Gray);
                row.ConstantItem(80).AlignRight().Text(t => {
                    t.CurrentPageNumber();
                    t.Span(" / ");
                    t.TotalPages();
                });
            });
        });
    }
}
