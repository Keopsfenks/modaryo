namespace ERP.Domain.Catalog;

public static class Ean13 {
    /// <summary>12 haneli gövde için kontrol hanesi (tek sıra ağırlık 1, çift sıra ağırlık 3).</summary>
    public static int CheckDigit(string twelveDigits) {
        if (twelveDigits is null || twelveDigits.Length != 12 || !twelveDigits.All(char.IsAsciiDigit))
            throw new ArgumentException("EAN-13 gövdesi 12 haneli olmalıdır.", nameof(twelveDigits));
        var sum = 0;
        for (var i = 0; i < 12; i++) sum += (twelveDigits[i] - '0') * (i % 2 == 0 ? 1 : 3);
        return (10 - sum % 10) % 10;
    }

    public static bool IsValid(string code) {
        return code is { Length: 13 } && code.All(char.IsAsciiDigit) && CheckDigit(code[..12]) == code[12] - '0';
    }

    /// <summary>7 haneli önek + 5 haneli sıra + kontrol hanesi.</summary>
    public static string Build(string prefix7, int sequence) {
        if (prefix7 is null || prefix7.Length != 7 || !prefix7.All(char.IsAsciiDigit))
            throw new ArgumentException("Barkod öneki 7 haneli olmalıdır.", nameof(prefix7));
        if (sequence is < 0 or > 99999)
            throw new ArgumentOutOfRangeException(nameof(sequence), "Sıra 0 ile 99999 arasında olmalıdır.");
        var body = prefix7 + sequence.ToString("D5");
        return body + CheckDigit(body);
    }
}
