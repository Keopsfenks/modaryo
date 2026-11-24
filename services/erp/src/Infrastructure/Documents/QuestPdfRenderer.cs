using ERP.Application.Common.Exceptions;
using ERP.Application.Common.Interfaces;
using ERP.Application.Invoicing;
using ERP.Application.Settings;
using ERP.Application.Settings.Categories;
using ERP.Domain.Invoicing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
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
    static QuestPdfRenderer() {
        QuestPDF.Settings.License = LicenseType.Community;
        // Yalnızca gömülü yazı tipi: çıktı sunucudaki yazı tiplerine bağlı olmaz; eksik glif sessizce kutuya dönmez, hata verir.
        QuestPDF.Settings.UseSystemFonts                  = false;
        QuestPDF.Settings.ThrowOnMissingTextGlyphs         = true;
        QuestPDF.Settings.EnableCaching                    = true;
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

    public async Task<byte[]> RenderSampleAsync(CancellationToken ct) {
        var firm     = await settings.GetAsync<CompanyProfileSettings>(ct);
        var document = await settings.GetAsync<DocumentSettings>(ct);
        var today    = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(3));

        LineDto Line(int sort, string name, decimal qty, decimal price, int vat) {
            var net = qty * price;
            var tax = Math.Round(net * vat / 100m, 2, MidpointRounding.AwayFromZero);
            return new LineDto(Guid.Empty, sort, null, null, name, "adet", 1m, qty, null, null, price, false, null, null, vat, null, null, null, null, null, net, 0m, 0m,
                               net, 0m, net, tax, 0m, net + tax, net, 0m, tax, 0m, net + tax, null, null);
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
