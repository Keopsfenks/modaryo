using System.Security.Cryptography;
using System.Text;

namespace Modaryo.Sessions;

/// <summary>Oturum sözleşmesindeki sabit adlar ve anahtar biçimleri.</summary>
public static class SessionKeys
{
    public const string CookieName = "modaryo_sid";
    public const string CsrfHeader = "X-Modaryo-Csrf";
    public const int CurrentVersion = 1;
    public const string AllPermissions = "*";
    /// <summary>"branch" claim değeri: kullanıcı tüm şubelere erişebilir (oturum kaydında branch_ids null).</summary>
    public const string AllBranches = "*";

    /// <summary>32 baytlık kriptografik rastgele oturum kimliği (base64url, dolgusuz).</summary>
    public static string NewSid() => Base64Url(RandomNumberGenerator.GetBytes(32));

    /// <summary>Aktif oturumlar ekranında gösterilen, cookie'den türetilemeyen kimlik.</summary>
    public static string NewPublicId() => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(8));

    public static string HashSid(string sid) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(sid)));

    public static string SessionKey(string sidHash) => $"sess:{sidHash}";

    public static string UserIndexKey(Guid userId) => $"user-sess:{userId:D}";

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
