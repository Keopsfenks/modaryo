namespace Modaryo.Internal;

/// <summary>Yapılandırma bölümü "Internal": Callers:{ad}:Secret ve Callers:{ad}:Routes (yol önekleri).</summary>
public sealed class InternalOptions {
    public const string SectionName = "Internal";

    public Dictionary<string, InternalCaller> Callers { get; } = new();

    public int MaxSkewSeconds { get; set; } = 300;
}

public sealed class InternalCaller {
    public string Secret { get; set; } = "";

    /// <summary>Çağıranın erişebildiği yol önekleri (örn. "/internal/einvoice/"); eşleşmeyen yol 403.</summary>
    public string[] Routes { get; set; } = [];
}
