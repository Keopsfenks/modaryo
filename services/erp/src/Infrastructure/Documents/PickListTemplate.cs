using ERP.Domain.Shipping;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace ERP.Infrastructure.Documents;

internal sealed record PickPdfLine(int No, string Code, string Name, string Unit, decimal Quantity, decimal? Picked, string? Location, string? Lots);

internal sealed record PickPdfModel(
    string                Number,
    PickListStatus        Status,
    DateOnly              Date,
    string                Warehouse,
    string                Firm,
    IReadOnlyList<string> Shipments,
    IReadOnlyList<PickPdfLine> Lines,
    string                AccentColor,
    string?               FooterNote
);

/// <summary>A4 toplama listesi: depocunun elinde tikleyeceği kalem listesi.</summary>
internal sealed class PickListTemplate(PickPdfModel model) : IDocument {
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

            if (model.Status == PickListStatus.Cancelled)
                page.Foreground().AlignCenter().AlignMiddle().Rotate(-35).Text("İPTAL").FontSize(120).Bold().FontColor(Colors.Red.Lighten4);

            page.Header().Column(column => {
                column.Item().Row(row => {
                    row.RelativeItem().Text(model.Firm).FontSize(12).Bold().FontColor(model.AccentColor);
                    row.ConstantItem(220).Column(meta => {
                        meta.Item().AlignRight().Text("TOPLAMA LİSTESİ").FontSize(13).Bold().FontColor(model.AccentColor);
                        meta.Item().AlignRight().Text($"Liste No: {model.Number}");
                        meta.Item().AlignRight().Text($"Tarih: {model.Date.ToString("dd.MM.yyyy", Tr)}");
                        meta.Item().AlignRight().Text($"Depo: {model.Warehouse}");
                    });
                });
                column.Item().PaddingTop(6).LineHorizontal(1.5f).LineColor(model.AccentColor);
                column.Item().PaddingTop(4).Text($"Sevkler: {string.Join(", ", model.Shipments)}").FontColor(Gray);
            });

            page.Content().PaddingTop(10).Table(table => {
                table.ColumnsDefinition(columns => {
                    columns.ConstantColumn(18);
                    columns.RelativeColumn(4);
                    columns.ConstantColumn(56);
                    columns.ConstantColumn(34);
                    columns.RelativeColumn(2);
                    columns.ConstantColumn(40);
                });

                table.Header(header => {
                    void Head(string text, bool right = false) {
                        var cell = header.Cell().Background(model.AccentColor).PaddingVertical(3).PaddingHorizontal(3);
                        (right ? cell.AlignRight() : cell.AlignLeft()).Text(text).FontColor(Colors.White).Bold().FontSize(7.5f);
                    }

                    Head("#");
                    Head("Kalem");
                    Head("Miktar", true);
                    Head("Birim");
                    Head("Konum / Lot");
                    Head("Toplandı");
                });

                IContainer Cell(IContainer c) => c.BorderBottom(0.5f).BorderColor(Line).PaddingVertical(4).PaddingHorizontal(3);

                foreach (var line in model.Lines) {
                    Cell(table.Cell()).Text(line.No.ToString(Tr));
                    Cell(table.Cell()).Column(description => {
                        description.Item().Text(line.Name);
                        description.Item().Text(line.Code).FontSize(7).FontColor(Gray);
                    });
                    Cell(table.Cell()).AlignRight().Text(line.Quantity.ToString("#,##0.######", Tr));
                    Cell(table.Cell()).Text(line.Unit);
                    Cell(table.Cell()).Column(where => {
                        if (!string.IsNullOrWhiteSpace(line.Location)) where.Item().Text(line.Location);
                        if (!string.IsNullOrWhiteSpace(line.Lots)) where.Item().Text(line.Lots).FontSize(7.5f).FontColor(Gray);
                    });
                    Cell(table.Cell()).AlignCenter().Text(line.Picked is { } picked ? picked.ToString("#,##0.######", Tr) : "[   ]");
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
