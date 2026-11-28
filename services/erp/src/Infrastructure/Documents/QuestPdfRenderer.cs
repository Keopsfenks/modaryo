using ERP.Application.Common.Exceptions;
using ERP.Application.Common.Interfaces;
using ERP.Application.Invoicing;
using ERP.Application.Settings;
using ERP.Application.Settings.Categories;
using ERP.Domain.Invoicing;
using ERP.Domain.Purchasing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using QuestPDF.Drawing;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using DocumentSettings = ERP.Application.Settings.Categories.DocumentSettings;

namespace ERP.Infrastructure.Documents;

/// <summary>Fatura PDF'ini QuestPDF ile üretir. Veriyi aktif firmanın kapsamından okur; yetki denetimi çağıranındır.</summary>
public sealed class QuestPdfRenderer(
    IApplicationDbContext      db,
    ITenantSettings            settings,
    IFileStorage               storage,
    ILogger<QuestPdfRenderer>  logger
) : IDocumentPdf {
    /// <summary>Ana yazı tipi (QuestPDF'in gömülü Lato'su) ve eksik glifler için gömülü yedek (Noto Sans, SIL OFL 1.1).</summary>
    internal const string PrimaryFont  = "Lato";
    internal const string FallbackFont = "Noto Sans";

    static QuestPdfRenderer() {
        QuestPDF.Settings.License = LicenseType.Community;
        // Yalnızca gömülü yazı tipleri: çıktı sunucudaki yazı tiplerine bağlı olmaz.
        QuestPDF.Settings.UseSystemFonts = false;
        // Lato'da olmayan karakterler (emoji, CJK, Arapça, simgeler) önce Noto Sans'a düşer; onda da yoksa glif kutusu çizilir.
        // Hiçbir karakter PDF üretimini düşürmesin: eksik glif hatası kapalıdır.
        QuestPDF.Settings.ThrowOnMissingTextGlyphs = false;
        QuestPDF.Settings.EnableCaching            = true;
        var assembly = typeof(QuestPdfRenderer).Assembly;
        foreach (var name in assembly.GetManifestResourceNames().Where(n => n.StartsWith("Font.NotoSans-", StringComparison.Ordinal)))
            using (var stream = assembly.GetManifestResourceStream(name)!)
                FontManager.RegisterFontFromStream(stream);
    }

    /// <summary>Tüm belge metni için varsayılan stil: Lato, eksik glifte Noto Sans.</summary>
    internal static TextStyle BaseStyle(TextStyle style) {
        return style.FontFamily(PrimaryFont, FallbackFont);
    }

    public async Task<byte[]> RenderInvoiceAsync(Guid invoiceId, CancellationToken ct) {
        var invoice = await db.Invoices.AsNoTracking().Include(i => i.Lines).Include(i => i.DueSchedule).FirstOrDefaultAsync(i => i.Id == invoiceId, ct)
                      ?? throw new NotFoundException(invoiceId.ToString(), "Invoice");
        var cari = await db.CurrentAccounts.AsNoTracking().FirstOrDefaultAsync(c => c.Id == invoice.CurrentAccountId, ct)
                   ?? throw new NotFoundException(invoice.CurrentAccountId.ToString(), "CurrentAccount");

        var firm     = await settings.GetAsync<CompanyProfileSettings>(ct);
        var document = await settings.GetAsync<DocumentSettings>(ct);
        var dto      = InvoiceRules.ToDto(invoice, cari.Name);

        // Alıcı/satıcı: kesilmiş faturada dondurulmuş bilgi, taslakta carinin güncel bilgisi.
        var counterparty = invoice.BuyerOrSeller ?? await LiveSnapshotAsync(cari, ct);
        var own          = FirmParty(firm);
        var cariParty    = CariParty(counterparty);
        var purchase     = InvoiceRules.IsPurchase(invoice.Kind);

        var withholdingCodes = dto.Lines.Select(l => l.WithholdingCode).Where(c => c is not null).Distinct().ToList();
        var withholding = withholdingCodes.Count == 0
                              ? new Dictionary<string, string>()
                              : await db.WithholdingCodes.AsNoTracking().Where(w => withholdingCodes.Contains(w.Code))
                                        .ToDictionaryAsync(w => w.Code, w => $"{w.Tenths}/10 - {w.Name}", ct);
        var exemptionCodes = dto.Lines.Select(l => l.ExemptionCode).Where(c => c is not null).Distinct().ToList();
        var exemption = exemptionCodes.Count == 0
                            ? new Dictionary<string, string>()
                            : await db.ExemptionCodes.AsNoTracking().Where(e => exemptionCodes.Contains(e.Code)).ToDictionaryAsync(e => e.Code, e => e.Name, ct);

        // Banka hesapları satıcıya aittir: yalnızca firmanın satıcı olduğu belgelerde gösterilir.
        var ibans = document.ShowIbans && !purchase ? firm.Ibans.Select(i => (i.BankName, i.Iban, i.Currency)).ToList() : [];

        var model = new InvoicePdfModel(dto, purchase ? cariParty : own, purchase ? own : cariParty, document.AccentColor, document.FooterNote,
                                        await LoadLogoAsync(firm.LogoFileId, ct), ibans, withholding, exemption);

        // QuestPDF eşzamanlı ve CPU yoğun: istek iş parçacığını bloklamamak için havuza alınır.
        return await Task.Run(() => new InvoiceTemplate(model).GeneratePdf(), ct);
    }

    public async Task<byte[]> RenderPurchaseOrderAsync(Guid purchaseOrderId, bool showPrices, CancellationToken ct) {
        var po = await db.PurchaseOrders.AsNoTracking().Include(p => p.Lines).FirstOrDefaultAsync(p => p.Id == purchaseOrderId, ct)
                 ?? throw new NotFoundException(purchaseOrderId.ToString(), "PurchaseOrder");
        var supplier = await db.CurrentAccounts.AsNoTracking().FirstOrDefaultAsync(c => c.Id == po.SupplierId, ct)
                       ?? throw new NotFoundException(po.SupplierId.ToString(), "CurrentAccount");
        var productIds = po.Lines.Select(l => l.ProductId).Distinct().ToList();
        var products   = await db.Products.AsNoTracking().Where(p => productIds.Contains(p.Id)).Select(p => new { p.Id, p.Code, p.Name }).ToDictionaryAsync(p => p.Id, ct);

        var firm     = await settings.GetAsync<CompanyProfileSettings>(ct);
        var document = await settings.GetAsync<DocumentSettings>(ct);
        var lines = po.Lines.OrderBy(l => l.Sort).Select(l => {
            var product = products.GetValueOrDefault(l.ProductId);
            return new PoPdfLine(l.Sort, product?.Code ?? string.Empty, product?.Name ?? l.Description, l.Unit, l.Quantity, showPrices ? l.UnitPrice : null,
                                 showPrices ? l.DiscountPercent : null, l.VatRate, l.ExpectedDate, showPrices ? l.Net : null);
        }).ToList();

        var model = new PoPdfModel(po.Number, po.Status, po.Date, po.ExpectedDate, po.Currency, po.Rate, po.Notes, FirmParty(firm), CariParty(await LiveSnapshotAsync(supplier, ct)),
                                   lines, showPrices ? po.Net : null, showPrices ? po.Vat : null, showPrices ? po.Total : null, document.AccentColor, document.FooterNote,
                                   await LoadLogoAsync(firm.LogoFileId, ct));
        return await Task.Run(() => new PurchaseOrderTemplate(model).GeneratePdf(), ct);
    }

    public async Task<byte[]> RenderQuoteAsync(Guid quoteId, int? revision, CancellationToken ct) {
        var quote = await db.Quotes.AsNoTracking().Include(q => q.Revisions).ThenInclude(r => r.Lines).FirstOrDefaultAsync(q => q.Id == quoteId, ct)
                    ?? throw new NotFoundException(quoteId.ToString(), "Quote");
        var number = revision ?? quote.CurrentRevision;
        var rev    = quote.Revisions.FirstOrDefault(r => r.Revision == number) ?? throw new NotFoundException(number.ToString(), "QuoteRevision");
        var lines  = await SalesLinesAsync(rev.Lines.OrderBy(l => l.Sort).Select(l => (l.Sort, l.ProductId, l.Description, l.Unit, l.Quantity, l.Width, l.Length, l.UnitPrice,
                                                                                         l.DiscountPercent, l.VatRate, l.Net, l.Total)).ToList(), ct);
        var model = await SalesModelAsync("TEKLİF", "Teklif No", quote.Number, rev.Date, rev.ValidUntil, rev.Revision, quote.Currency, quote.SalesRepName, rev.Notes,
                                          quote.CurrentAccountId, lines, rev.Discount, rev.Net, rev.Vat, rev.Total, [],
                                          quote.Status == Domain.Sales.QuoteStatus.Draft ? "TASLAK" : null, ct);
        return await Task.Run(() => new SalesDocumentTemplate(model).GeneratePdf(), ct);
    }

    public async Task<byte[]> RenderSalesOrderAsync(Guid orderId, bool proforma, CancellationToken ct) {
        var order = await db.SalesOrders.AsNoTracking().Include(o => o.Lines).Include(o => o.Plan).ThenInclude(p => p!.Rows).FirstOrDefaultAsync(o => o.Id == orderId, ct)
                    ?? throw new NotFoundException(orderId.ToString(), "SalesOrder");
        var lines = await SalesLinesAsync(order.Lines.OrderBy(l => l.Sort).Select(l => (l.Sort, l.ProductId, l.Description, l.Unit, l.Quantity, l.Width, l.Length, l.UnitPrice,
                                                                                         l.DiscountPercent, l.VatRate, l.Net, l.Total)).ToList(), ct);
        var plan = new List<SalesPdfPlanRow>();
        if (order.Plan is { } p) {
            if (p.DownPayment > 0m) plan.Add(new SalesPdfPlanRow("Peşinat", order.Date, p.DownPayment));
            plan.AddRange(p.Rows.OrderBy(r => r.Sequence).Select(r => new SalesPdfPlanRow($"{r.Sequence}. taksit", r.DueDate, r.Amount)));
        }

        var model = await SalesModelAsync(proforma ? "PROFORMA FATURA" : "SİPARİŞ ONAYI", proforma ? "Proforma No" : "Sipariş No", order.Number, order.Date, null, null,
                                          order.Currency, order.SalesRepName, order.Notes, order.CurrentAccountId, lines, order.Discount, order.Net, order.Vat, order.Total, plan,
                                          order.Status == Domain.Sales.SalesOrderStatus.Cancelled ? "İPTAL" : order.Status == Domain.Sales.SalesOrderStatus.Draft ? "TASLAK" : null, ct);
        return await Task.Run(() => new SalesDocumentTemplate(model).GeneratePdf(), ct);
    }

    public async Task<byte[]> RenderPickListAsync(Guid pickListId, CancellationToken ct) {
        var list = await db.PickLists.AsNoTracking().Include(p => p.Lines).FirstOrDefaultAsync(p => p.Id == pickListId, ct)
                   ?? throw new NotFoundException(pickListId.ToString(), "PickList");
        var warehouse  = await db.Warehouses.AsNoTracking().Where(w => w.Id == list.WarehouseId).Select(w => w.Name).FirstOrDefaultAsync(ct);
        var ids        = list.ShipmentIds.ToList();
        var shipments  = await db.Shipments.AsNoTracking().Where(s => ids.Contains(s.Id)).OrderBy(s => s.Number).Select(s => s.Number).ToListAsync(ct);
        var productIds = list.Lines.Select(l => l.ProductId).Distinct().ToList();
        var products   = await db.Products.AsNoTracking().Where(p => productIds.Contains(p.Id)).Select(p => new { p.Id, p.Code }).ToDictionaryAsync(p => p.Id, p => p.Code, ct);
        var firm       = await settings.GetAsync<CompanyProfileSettings>(ct);
        var document   = await settings.GetAsync<DocumentSettings>(ct);
        var lines = list.Lines.OrderBy(l => l.Sort).Select(l => new PickPdfLine(
                                                               l.Sort, products.GetValueOrDefault(l.ProductId) ?? string.Empty, l.Description, l.Unit, l.Quantity, l.PickedQuantity, l.Location,
                                                               l.SuggestedLots is { Count: > 0 } lots
                                                                   ? string.Join(", ", lots.Select(x => $"{x.LotNo} ({x.Quantity:0.######})"))
                                                                   : null)).ToList();
        var date  = DateOnly.FromDateTime(list.Created.ToOffset(TimeSpan.FromHours(3)).DateTime);
        var model = new PickPdfModel(list.Number, list.Status, date, warehouse ?? string.Empty, firm.LegalName ?? string.Empty, shipments, lines, document.AccentColor, document.FooterNote);
        return await Task.Run(() => new PickListTemplate(model).GeneratePdf(), ct);
    }

    public async Task<byte[]> RenderIrsaliyeAsync(Guid shipmentId, CancellationToken ct) {
        var shipment = await db.Shipments.AsNoTracking().Include(s => s.Lines).FirstOrDefaultAsync(s => s.Id == shipmentId, ct)
                       ?? throw new NotFoundException(shipmentId.ToString(), "Shipment");
        var customer = await db.CurrentAccounts.AsNoTracking().FirstOrDefaultAsync(c => c.Id == shipment.CurrentAccountId, ct)
                       ?? throw new NotFoundException(shipment.CurrentAccountId.ToString(), "CurrentAccount");
        var order      = await db.SalesOrders.AsNoTracking().Where(o => o.Id == shipment.SalesOrderId).Select(o => o.Number).FirstOrDefaultAsync(ct);
        var productIds = shipment.Lines.Select(l => l.ProductId).Distinct().ToList();
        var products   = await db.Products.AsNoTracking().Where(p => productIds.Contains(p.Id)).Select(p => new { p.Id, p.Code }).ToDictionaryAsync(p => p.Id, p => p.Code, ct);
        var firm       = await settings.GetAsync<CompanyProfileSettings>(ct);
        var document   = await settings.GetAsync<DocumentSettings>(ct);
        var shipping   = await settings.GetAsync<ShippingSettings>(ct);

        var lines = shipment.Lines.OrderBy(l => l.Sort).Select(l => new IrsaliyePdfLine(
                                                                   l.Sort, products.GetValueOrDefault(l.ProductId) ?? string.Empty, l.Description, l.Unit, l.Quantity,
                                                                   l.Lots is { Count: > 0 } lots ? string.Join(", ", lots.Select(x => $"{x.LotNo} ({x.Quantity:0.######})")) : null,
                                                                   l.Serials is { Count: > 0 } serials ? string.Join(", ", serials) : null)).ToList();
        var delivery = shipment.DeliveryAddress is { } a ? Address(a.Address, a.District, a.City, a.Country) : null;
        var dispatch = (shipment.DispatchAt ?? shipment.ShippedAt ?? shipment.Created).ToOffset(TimeSpan.FromHours(3));
        var model = new IrsaliyePdfModel(shipment.IrsaliyeNo ?? shipment.Number, dispatch, FirmParty(firm), CariParty(await LiveSnapshotAsync(customer, ct)), delivery, shipment.Number,
                                         order ?? string.Empty, ShipmentCarrierName(shipping, shipment.CarrierCode), shipment.TrackingNo, shipment.Vehicle, shipment.Driver, lines,
                                         document.AccentColor, document.FooterNote, await LoadLogoAsync(firm.LogoFileId, ct));
        return await Task.Run(() => new IrsaliyeTemplate(model).GeneratePdf(), ct);
    }

    private static string? ShipmentCarrierName(ShippingSettings shipping, string? code) {
        return code is null ? null : (shipping.Carriers ?? ShippingSettings.CarrierSeed).FirstOrDefault(c => c.Code == code)?.Name ?? code;
    }

    private async Task<List<SalesPdfLine>> SalesLinesAsync(
        IReadOnlyList<(int Sort, Guid ProductId, string Description, string Unit, decimal Quantity, decimal? Width, decimal? Length, decimal UnitPrice, decimal? DiscountPercent, int VatRate, decimal Net, decimal Total)> rows,
        CancellationToken ct) {
        var ids      = rows.Select(r => r.ProductId).Distinct().ToList();
        var products = await db.Products.AsNoTracking().Where(p => ids.Contains(p.Id)).Select(p => new { p.Id, p.Code, p.Name }).ToDictionaryAsync(p => p.Id, ct);
        return rows.Select(r => {
            var product = products.GetValueOrDefault(r.ProductId);
            return new SalesPdfLine(r.Sort, product?.Code ?? string.Empty, string.IsNullOrWhiteSpace(r.Description) ? product?.Name ?? string.Empty : r.Description, r.Unit,
                                    r.Quantity, r.Width, r.Length, r.UnitPrice, r.DiscountPercent, r.VatRate, r.Net, r.Total);
        }).ToList();
    }

    private async Task<SalesPdfModel> SalesModelAsync(string title, string numberLabel, string number, DateOnly date, DateOnly? validUntil, int? revision, string currency,
                                                      string? rep, string? notes, Guid customerId, List<SalesPdfLine> lines, decimal discount, decimal net, decimal vat,
                                                      decimal total, List<SalesPdfPlanRow> plan, string? watermark, CancellationToken ct) {
        var customer = await db.CurrentAccounts.AsNoTracking().FirstOrDefaultAsync(c => c.Id == customerId, ct)
                       ?? throw new NotFoundException(customerId.ToString(), "CurrentAccount");
        var firm     = await settings.GetAsync<CompanyProfileSettings>(ct);
        var document = await settings.GetAsync<DocumentSettings>(ct);
        return new SalesPdfModel(title, numberLabel, number, date, validUntil, revision, currency, rep, notes, FirmParty(firm), CariParty(await LiveSnapshotAsync(customer, ct)),
                                 lines, discount, net, vat, total, plan, watermark, document.AccentColor, document.FooterNote, await LoadLogoAsync(firm.LogoFileId, ct));
    }

    public async Task<byte[]> RenderSampleAsync(CancellationToken ct) {
        var firm     = await settings.GetAsync<CompanyProfileSettings>(ct);
        var document = await settings.GetAsync<DocumentSettings>(ct);
        var today    = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(3));

        LineDto Line(int sort, string name, decimal qty, decimal price, int vat) {
            var net = qty * price;
            var tax = Math.Round(net * vat / 100m, 2, MidpointRounding.AwayFromZero);
            return new LineDto(Guid.Empty, sort, null, null, name, "adet", 1m, qty, null, null, price, false, null, null, vat, null, null, null, null, null, net, 0m, 0m,
                               net, 0m, net, tax, 0m, net + tax, net, 0m, tax, 0m, net + tax, null, null, null);
        }

        var lines = new List<LineDto> { Line(1, "Örnek ürün A", 10m, 100m, 20), Line(2, "Örnek hizmet B", 1m, 500m, 10) };
        var vatSummary = lines.GroupBy(l => l.VatRate).Select(g => new VatSummaryDto(g.Key, g.Sum(l => l.Net), g.Sum(l => l.Vat))).ToList();
        var totals = new TotalsDto(lines.Sum(l => l.Gross), 0m, lines.Sum(l => l.Net), 0m, lines.Sum(l => l.Vat), 0m, lines.Sum(l => l.Total));
        var dto = new InvoiceDto(Guid.Empty, InvoiceKind.Sales, InvoiceStatus.Issued, InvoiceMode.Paper, null, Guid.Empty, null, "ORNEK2026000001", null, Guid.Empty,
                                 "Örnek Müşteri A.Ş.", today, null, "TRY", 1m, RateType.ForexBuying, false, null, null, totals, totals, false, null, 30,
                                 "Bu belge ayar önizlemesi için örnek verilerle üretilmiştir.", null, null, null, "invoice", 0u, null, lines,
                                 [new DueDto(today.AddDays(30), totals.Payable)], vatSummary);

        var buyer  = new PartyInfo("Örnek Müşteri A.Ş.", "V.D.: Örnek / VKN/TCKN: 1234567890", "Örnek Mah. 1. Sok. No 1, Kadıköy/İstanbul", null);
        var ibans  = document.ShowIbans ? firm.Ibans.Select(i => (i.BankName, i.Iban, i.Currency)).ToList() : [];
        var model  = new InvoicePdfModel(dto, FirmParty(firm), buyer, document.AccentColor, document.FooterNote, await LoadLogoAsync(firm.LogoFileId, ct), ibans,
                                         new Dictionary<string, string>(), new Dictionary<string, string>());
        return await Task.Run(() => new InvoiceTemplate(model).GeneratePdf(), ct);
    }

    private static PartyInfo FirmParty(CompanyProfileSettings firm) {
        var tax = string.Join(" / ", new[] { firm.TaxOffice is { } o ? $"V.D.: {o}" : null, firm.TaxNumber is { } n ? $"VKN: {n}" : null }.Where(s => s is not null));
        var contact = string.Join(" | ", new[] { firm.Phone, firm.Email, firm.Website }.Where(s => !string.IsNullOrWhiteSpace(s)));
        return new PartyInfo(firm.LegalName ?? string.Empty, Nullable(tax), Address(firm.Address, firm.District, firm.City, null), Nullable(contact));
    }

    private static PartyInfo CariParty(AddressSnapshot party) {
        var tax = string.Join(" / ", new[] { party.TaxOffice is { } o ? $"V.D.: {o}" : null, party.TaxNumber is { } n ? $"VKN/TCKN: {n}" : null }.Where(s => s is not null));
        return new PartyInfo(party.Title, Nullable(tax), Address(party.Address, party.District, party.City, party.Country), Nullable(party.Email));
    }

    private static string? Address(string? line, string? district, string? city, string? country) {
        var parts = new[] { line, district is not null && city is not null ? $"{district}/{city}" : district ?? city, country }.Where(s => !string.IsNullOrWhiteSpace(s));
        return Nullable(string.Join(", ", parts));
    }

    private static string? Nullable(string? value) {
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private async Task<AddressSnapshot> LiveSnapshotAsync(ERP.Domain.CurrentAccounts.CurrentAccount cari, CancellationToken ct) {
        var addresses = await db.CurrentAccountAddresses.AsNoTracking().Where(a => a.CurrentAccountId == cari.Id).ToListAsync(ct);
        var address   = addresses.FirstOrDefault(a => a.IsDefaultBilling) ?? addresses.FirstOrDefault(a => a.IsBilling) ?? addresses.FirstOrDefault();
        return new AddressSnapshot(cari.Name, cari.TaxNumber, cari.TaxOffice, address?.Line, address?.City, address?.District, address?.Country, cari.Email);
    }

    /// <summary>Firma logosu; yoksa ya da okunamıyorsa PDF logosuz üretilir (belge hiçbir zaman logo yüzünden düşmez).</summary>
    private async Task<byte[]?> LoadLogoAsync(Guid? fileId, CancellationToken ct) {
        if (fileId is null) return null;
        try {
            var file = await db.StoredFiles.AsNoTracking().FirstOrDefaultAsync(f => f.Id == fileId, ct);
            if (file is null || !file.ContentType.StartsWith("image/", StringComparison.Ordinal)) return null;

            await using var stream = await storage.GetAsync(file.ObjectKey, ct);
            using var       buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, ct);
            var bytes = buffer.ToArray();
            _ = Image.FromBinaryData(bytes); // çözülemeyen görüntü şablonu düşürmesin
            return bytes;
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            logger.LogWarning(ex, "Firma logosu okunamadı; PDF logosuz üretiliyor.");
            return null;
        }
    }
}
