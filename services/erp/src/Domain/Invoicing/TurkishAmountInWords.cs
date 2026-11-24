namespace ERP.Domain.Invoicing;

/// <summary>Tutarı Türkçe yazıya çevirir: "Yalnız Bin Üç Yüz Elli Türk Lirası Elli Kuruş".</summary>
public static class TurkishAmountInWords {
    private static readonly string[] Ones   = ["", "Bir", "İki", "Üç", "Dört", "Beş", "Altı", "Yedi", "Sekiz", "Dokuz"];
    private static readonly string[] Tens   = ["", "On", "Yirmi", "Otuz", "Kırk", "Elli", "Altmış", "Yetmiş", "Seksen", "Doksan"];
    private static readonly string[] Scales = ["", "Bin", "Milyon", "Milyar", "Trilyon", "Katrilyon"];

    private static readonly Dictionary<string, (string Major, string Minor)> Currencies = new(StringComparer.OrdinalIgnoreCase) {
        ["TRY"] = ("Türk Lirası", "Kuruş"),
        ["USD"] = ("Amerikan Doları", "Sent"),
        ["EUR"] = ("Avro", "Sent"),
        ["GBP"] = ("İngiliz Sterlini", "Peni")
    };

    public static string Format(decimal amount, string currency) {
        if (!Currencies.TryGetValue(currency, out var names)) {
            throw new ArgumentException($"Desteklenmeyen para birimi: {currency}.", nameof(currency));
        }

        var rounded = Math.Round(Math.Abs(amount), 2, MidpointRounding.AwayFromZero);
        var whole   = (long)Math.Truncate(rounded);
        var minor   = (int)Math.Round((rounded - whole) * 100m);

        var parts = new List<string> { "Yalnız" };
        if (amount < 0 && rounded > 0) {
            parts.Add("Eksi");
        }

        if (whole > 0 || minor == 0) {
            parts.Add(Number(whole));
            parts.Add(names.Major);
        }

        if (minor > 0) {
            parts.Add(Number(minor));
            parts.Add(names.Minor);
        }

        return string.Join(' ', parts);
    }

    private static string Number(long n) {
        if (n == 0) {
            return "Sıfır";
        }

        var words = new List<string>();
        var scale = 0;
        while (n > 0) {
            var group = (int)(n % 1000);
            n /= 1000;
            if (group > 0) {
                var text = Group(group);
                // "Bir Bin" denmez; "Bin".
                if (scale == 1 && group == 1) {
                    text = string.Empty;
                }

                words.Insert(0, string.Join(' ', new[] { text, Scales[scale] }.Where(s => s.Length > 0)));
            }

            scale++;
        }

        return string.Join(' ', words);
    }

    private static string Group(int g) {
        var parts   = new List<string>();
        var hundred = g / 100;
        var ten     = g / 10 % 10;
        var one     = g % 10;
        if (hundred > 0) {
            parts.Add(hundred == 1 ? "Yüz" : $"{Ones[hundred]} Yüz");
        }

        if (ten > 0) {
            parts.Add(Tens[ten]);
        }

        if (one > 0) {
            parts.Add(Ones[one]);
        }

        return string.Join(' ', parts);
    }
}
