using ERP.Domain.Catalog;

namespace ERP.Domain.Pos;

public sealed record ScaleFormat(string Prefix, int ItemCodeLength, int ValueLength, ScaleValueKind Kind);

/// <summary>Terazi barkodundan çıkan kalem kodu ile ağırlık (kg) veya tutar (TL). Biri dolu, diğeri null.</summary>
public sealed record ScaleHit(string ItemCode, decimal? WeightKg, decimal? PriceTry);

/// <summary>EAN-13 uzunluğunda terazi barkodu: önek + kalem kodu + değer + kontrol hanesi.</summary>
public static class ScaleBarcode {
    public const int Ean13Length = 13;

    /// <summary>Barkod hiçbir biçime uymuyorsa veya kontrol hanesi hatalıysa null.</summary>
    public static ScaleHit? TryParse(string barcode, IReadOnlyList<ScaleFormat> formats) {
        if (barcode is not { Length: Ean13Length } || !barcode.All(char.IsAsciiDigit)) return null;
        var format = formats.FirstOrDefault(f => Matches(barcode, f));
        if (format is null || !Ean13.IsValid(barcode)) return null;

        var itemCode = barcode.Substring(format.Prefix.Length, format.ItemCodeLength);
        var raw      = long.Parse(barcode.AsSpan(format.Prefix.Length + format.ItemCodeLength, format.ValueLength));
        return format.Kind == ScaleValueKind.WeightGrams
            ? new ScaleHit(itemCode, raw / 1000m, null)
            : new ScaleHit(itemCode, null, raw / 100m);
    }

    /// <summary>Önek eşleşiyor mu ve biçim 13 haneye (kontrol hanesi dahil) tam oturuyor mu.</summary>
    public static bool Matches(string barcode, ScaleFormat f) {
        return f.Prefix.Length > 0 && f.Prefix.All(char.IsAsciiDigit)
                                   && f.Prefix.Length + f.ItemCodeLength + f.ValueLength + 1 == Ean13Length
                                   && barcode.StartsWith(f.Prefix, StringComparison.Ordinal);
    }
}
