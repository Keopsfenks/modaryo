using System.Globalization;

namespace ERP.Domain.Common;

public static class TurkishText {
    static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");

    /// <summary>Arama terimini veritabanındaki lower(...).Replace("ı","i") ile aynı biçimde katlar (Türkçe I/ı/İ/i).</summary>
    public static string Fold(string text) {
        return text.Trim().ToLower(Tr).Replace('ı', 'i');
    }
}
