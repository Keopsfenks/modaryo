namespace Modaryo.Internal;

/// <summary>Yapılandırma bölümü "Internal": Callers:{ad}:Secret ve Callers:{ad}:Routes (yol önekleri).</summary>
public sealed class InternalOptions {
    public const string SectionName = "Internal";

    public Dictionary<string, InternalCaller> Callers { get; } = new();

    public int MaxSkewSeconds { get; set; } = 300;

    /// <summary>İç isteğin en büyük gövde boyutu (bayt); aşılırsa 413. İmza doğrulanmadan önce bu kadarı bile okunmaz.</summary>
    public long MaxBodyBytes { get; set; } = 1_048_576;

    /// <summary>Bekleyen (Status 0) bir idempotency kaydı bu süreden eskiyse aynı istek tarafından devralınabilir.</summary>
    public int PendingTimeoutSeconds { get; set; } = 300;
}

public sealed class InternalCaller {
    public string Secret { get; set; } = "";

    /// <summary>Çağıranın erişebildiği yol önekleri (örn. "/internal/einvoice/"); eşleşmeyen yol 403.</summary>
    public string[] Routes { get; set; } = [];
}
