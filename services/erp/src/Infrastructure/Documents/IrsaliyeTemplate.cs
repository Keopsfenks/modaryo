using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace ERP.Infrastructure.Documents;

internal sealed record IrsaliyePdfLine(int No, string Code, string Name, string Unit, decimal Quantity, string? Lots, string? Serials);

internal sealed record IrsaliyePdfModel(
    string                     Number,
    DateTimeOffset             DispatchAt,
    PartyInfo                  Seller,
    PartyInfo                  Buyer,
    string?                    DeliveryAddress,
    string                     ShipmentNumber,
    string                     OrderNumber,
    string?                    Carrier,
    string?                    TrackingNo,
    string?                    Vehicle,
    string?                    Driver,
    IReadOnlyList<IrsaliyePdfLine> Lines,
    string                     AccentColor,
    string?                    FooterNote,
    byte[]?                    Logo
);

/// <summary>A4 sevk irsaliyesi: satıcı, alıcı, teslimat adresi, kalemler (lot/seri), araç-sürücü ve sevk tarihi-saati.</summary>
internal sealed class IrsaliyeTemplate(IrsaliyePdfModel model) : IDocument {
    private const string Gray = "#6B7280";
    private const string Line = "#D1D5DB";

    private static readonly System.Globalization.CultureInfo Tr = System.Globalization.CultureInfo.GetCultureInfo("tr-TR");

    public DocumentMetadata GetMetadata() {
        return new DocumentMetadata { Title = model.Number, Author = model.Seller.Title };
    }

    public void Compose(IDocumentContainer container) {
        container.Page(page => {
            page.Size(PageSizes.A4);
            page.Margin(28);
            page.DefaultTextStyle(t => QuestPdfRenderer.BaseStyle(t).FontSize(9f).FontColor("#111827"));

            page.Header().Column(column => {
                column.Item().Row(row => {
                    row.RelativeItem().Column(firm => {
                        if (model.Logo is { } logo) firm.Item().MaxHeight(40).Image(logo).FitHeight();
                        firm.Item().Text(model.Seller.Title).FontSize(12).Bold().FontColor(model.AccentColor);
                        if (model.Seller.TaxLine is { } tax) firm.Item().Text(tax).FontSize(8).FontColor(Gray);
                        if (model.Seller.Address is { } address) firm.Item().Text(address).FontSize(8).FontColor(Gray);
                        if (model.Seller.Contact is { } contact) firm.Item().Text(contact).FontSize(8).FontColor(Gray);
                    });
                    row.ConstantItem(230).Column(meta => {
                        meta.Item().AlignRight().Text("SEVK İRSALİYESİ").FontSize(13).Bold().FontColor(model.AccentColor);
                        meta.Item().AlignRight().Text($"İrsaliye No: {model.Number}");
                        meta.Item().AlignRight().Text($"Sevk Tarihi: {model.DispatchAt.ToString("dd.MM.yyyy HH:mm", Tr)}");
                        meta.Item().AlignRight().Text($"Sevk No: {model.ShipmentNumber}");
                        meta.Item().AlignRight().Text($"Sipariş No: {model.OrderNumber}");
                    });
                });
                column.Item().PaddingTop(6).LineHorizontal(1.5f).LineColor(model.AccentColor);
            });

            page.Content().PaddingTop(10).Column(content => {
                content.Item().Row(row => {
                    row.RelativeItem().Border(0.5f).BorderColor(Line).Padding(6).Column(buyer => {
                        buyer.Item().Text("ALICI").FontSize(7.5f).Bold().FontColor(Gray);
                        buyer.Item().Text($"Alıcı: {model.Buyer.Title}").Bold();
                        if (model.Buyer.TaxLine is { } tax) buyer.Item().Text(tax).FontSize(8);
                        if (model.Buyer.Address is { } address) buyer.Item().Text(address).FontSize(8);
                        if (model.Buyer.Contact is { } contact) buyer.Item().Text(contact).FontSize(8);
                    });
                    row.ConstantItem(8);
                    row.RelativeItem().Border(0.5f).BorderColor(Line).Padding(6).Column(delivery => {
                        delivery.Item().Text("TESLİMAT ADRESİ").FontSize(7.5f).Bold().FontColor(Gray);
                        delivery.Item().Text(model.DeliveryAddress ?? model.Buyer.Address ?? "-");
                        if (!string.IsNullOrWhiteSpace(model.Carrier)) delivery.Item().PaddingTop(3).Text($"Kargo: {model.Carrier}").FontSize(8);
                        if (!string.IsNullOrWhiteSpace(model.TrackingNo)) delivery.Item().Text($"Takip No: {model.TrackingNo}").FontSize(8);
                        if (!string.IsNullOrWhiteSpace(model.Vehicle)) delivery.Item().Text($"Araç: {model.Vehicle}").FontSize(8);
                        if (!string.IsNullOrWhiteSpace(model.Driver)) delivery.Item().Text($"Sürücü: {model.Driver}").FontSize(8);
                    });
                });

                content.Item().PaddingTop(10).Table(table => {
                    table.ColumnsDefinition(columns => {
                        columns.ConstantColumn(18);
                        columns.RelativeColumn(5);
                        columns.ConstantColumn(60);
                        columns.ConstantColumn(34);
                        columns.RelativeColumn(3);
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
                        Head("Lot / Seri");
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
                        Cell(table.Cell()).Column(tracked => {
                            if (!string.IsNullOrWhiteSpace(line.Lots)) tracked.Item().Text($"Lot: {line.Lots}").FontSize(7.5f);
                            if (!string.IsNullOrWhiteSpace(line.Serials)) tracked.Item().Text($"Seri: {line.Serials}").FontSize(7.5f);
                        });
                    }
                });

                content.Item().PaddingTop(30).Row(row => {
                    row.RelativeItem().Column(c => {
                        c.Item().Text("Teslim Eden").FontSize(8).FontColor(Gray);
                        c.Item().PaddingTop(24).LineHorizontal(0.5f).LineColor(Line);
                    });
                    row.ConstantItem(40);
                    row.RelativeItem().Column(c => {
                        c.Item().Text("Teslim Alan (Ad, imza)").FontSize(8).FontColor(Gray);
                        c.Item().PaddingTop(24).LineHorizontal(0.5f).LineColor(Line);
                    });
                });
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
