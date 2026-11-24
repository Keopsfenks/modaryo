using System.Globalization;
using ERP.Application.Common.Exceptions;
using ERP.Application.Common.Interfaces;
using ERP.Domain.Catalog;
using ERP.Domain.Common;

namespace ERP.Application.Catalog.Excel;

/// <summary>
///     Önizlemeden uygulamaya taşınan ham satır. Alanlar dosyadaki hücre metnidir; null = sütun dosyada hiç yok
///     (mevcut değer korunur), boş metin = hücre boş.
/// </summary>
public sealed record ImportRow(
    int     RowNumber,
    string? Code,
    string? Name,
    string? Kind,
    string? Category,
    string? Brand,
    string? Unit,
    string? Vat,
    string? Barcode,
    string? PurchaseAmount,
    string? PurchaseCurrency,
    string? PurchaseVatIncluded,
    string? SaleAmount,
    string? SaleCurrency,
    string? SaleVatIncluded,
    string? Tags,
    string? Passive
);

public sealed record ImportRowResult(
    int                 RowNumber,
    string              Action,
    string?             Code,
    string?             Name,
    IReadOnlyList<string> Errors
);

public sealed record ImportPreview(
    Guid                         BatchId,
    int                          Total,
    int                          Creates,
    int                          Updates,
    int                          Errors,
    IReadOnlyList<ImportRowResult> Rows
);

public sealed record ImportCommitResult(int Created, int Updated);

/// <summary>Toplu ürün tablosunun sütunları: Türkçe başlıklar yalnızca burada tanımlıdır.</summary>
public static class ProductSheet {
    public const string SheetName = "Ürünler";
    public const int    MaxRows   = 5000;
    public const long   MaxBytes  = 5L * 1024 * 1024;
    public const string Separator = " > ";

    public const string CodeH                = "Kod";
    public const string NameH                = "Ad";
    public const string KindH                = "Tür";
    public const string CategoryH            = "Kategori";
    public const string BrandH               = "Marka";
    public const string UnitH                = "Birim";
    public const string VatH                 = "KDV";
    public const string BarcodeH             = "Barkod";
    public const string PurchaseAmountH      = "Alış fiyatı";
    public const string PurchaseCurrencyH    = "Alış para birimi";
    public const string PurchaseVatIncludedH = "Alış KDV dahil";
    public const string SaleAmountH          = "Satış fiyatı";
    public const string SaleCurrencyH        = "Satış para birimi";
    public const string SaleVatIncludedH     = "Satış KDV dahil";
    public const string TagsH                = "Etiketler";
    public const string PassiveH             = "Pasif";

    public static readonly IReadOnlyList<string> Headers = [
        CodeH, NameH, KindH, CategoryH, BrandH, UnitH, VatH, BarcodeH, PurchaseAmountH, PurchaseCurrencyH,
        PurchaseVatIncludedH, SaleAmountH, SaleCurrencyH, SaleVatIncludedH, TagsH, PassiveH
    ];

    /// <summary>Headers içindeki alış sütunlarının sıraları.</summary>
    public static readonly IReadOnlyList<int> PurchaseColumns = [8, 9, 10];

    public static readonly IReadOnlyList<string> HeadersWithoutPurchase =
        Headers.Where((_, i) => !PurchaseColumns.Contains(i)).ToList();

    public const string Yes = "Evet";
    public const string No  = "Hayır";

    public static string KindText(ProductKind kind) {
        return kind == ProductKind.Service ? "Hizmet" : "Ürün";
    }

    public static string Flag(bool value) {
        return value ? Yes : No;
    }

    /// <summary>Başlık satırını doğrular (Kod ve Ad şart) ve veri satırlarını ham satırlara çevirir.</summary>
    public static List<ImportRow> ToRows(XlsxTable table) {
        var index = new Dictionary<string, int>();
        for (var i = 0; i < table.Headers.Count; i++) index.TryAdd(TurkishText.Fold(table.Headers[i]), i);

        foreach (var required in new[] { CodeH, NameH })
            if (!index.ContainsKey(TurkishText.Fold(required)))
                throw new AppException(400, "invalid_template",
                                       $"Dosyada \"{required}\" sütunu yok. Şablonu indirip başlıkları değiştirmeden kullanın.");

        string? Cell(XlsxRow row, string header) {
            return index.TryGetValue(TurkishText.Fold(header), out var i) && i < row.Cells.Count
                       ? row.Cells[i].Trim()
                       : null;
        }

        return table.Rows.Select(r => new ImportRow(
                                     r.RowNumber, Cell(r, CodeH), Cell(r, NameH), Cell(r, KindH), Cell(r, CategoryH),
                                     Cell(r, BrandH), Cell(r, UnitH), Cell(r, VatH), Cell(r, BarcodeH),
                                     Cell(r, PurchaseAmountH), Cell(r, PurchaseCurrencyH), Cell(r, PurchaseVatIncludedH),
                                     Cell(r, SaleAmountH), Cell(r, SaleCurrencyH), Cell(r, SaleVatIncludedH),
                                     Cell(r, TagsH), Cell(r, PassiveH))).ToList();
    }

    /// <summary>Virgül varsa Türkçe (1.234,56), yoksa nokta ondalık ayırıcıdır.</summary>
    public static bool TryDecimal(string text, out decimal value) {
        var s = text.Trim();
        if (s.Contains(',')) s = s.Replace(".", string.Empty).Replace(',', '.');

        return decimal.TryParse(s, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                                CultureInfo.InvariantCulture, out value);
    }

    /// <summary>Evet/Hayır (e, h, true, false, 1, 0 da kabul edilir). Tanınmayan metinde false döner.</summary>
    public static bool TryFlag(string text, out bool value) {
        switch (TurkishText.Fold(text)) {
            case "evet" or "e" or "yes" or "true" or "1":
                value = true;
                return true;
            case "hayir" or "h" or "no" or "false" or "0":
                value = false;
                return true;
            default:
                value = false;
                return false;
        }
    }
}
