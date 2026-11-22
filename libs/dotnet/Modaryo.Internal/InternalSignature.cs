using System.Security.Cryptography;
using System.Text;

namespace Modaryo.Internal;

/// <summary>İç API (internal-api v1) sözleşmesindeki sabit adlar.</summary>
public static class InternalHeaders {
    public const string Service        = "X-Modaryo-Service";
    public const string Timestamp      = "X-Modaryo-Timestamp";
    public const string Signature      = "X-Modaryo-Signature";
    public const string Tenant         = "X-Modaryo-Tenant";
    public const string IdempotencyKey = "Idempotency-Key";
}

public static class InternalClaimTypes {
    /// <summary>İç çağrıyı yapan servisin adı.</summary>
    public const string Service = "service";
}

/// <summary>
///     İmza = hex(HMAC-SHA256(secret, "{METHOD}\n{pathAndQuery}\n{timestamp}\n{tenant}\n{sha256hex(body)}")).
///     {tenant} X-Modaryo-Tenant değeridir (yoksa boş); böylece tenant başlığı da imzalanır.
///     {idempotencyKey} Idempotency-Key başlığıdır (yoksa boş); anahtar da imzalanır.
/// </summary>
public static class InternalSignature {
    public static string CanonicalString(string             method,
                                         string             pathAndQuery,
                                         long               timestamp,
                                         string?            tenant,
                                         string?            idempotencyKey,
                                         ReadOnlySpan<byte> body
    ) {
        var bodyHash = Convert.ToHexStringLower(SHA256.HashData(body));
        return $"{method}\n{pathAndQuery}\n{timestamp}\n{tenant}\n{idempotencyKey}\n{bodyHash}";
    }

    public static string Compute(string             secret,
                                 string             method,
                                 string             pathAndQuery,
                                 long               timestamp,
                                 string?            tenant,
                                 string?            idempotencyKey,
                                 ReadOnlySpan<byte> body
    ) {
        return Convert.ToHexStringLower(ComputeBytes(secret, method, pathAndQuery, timestamp, tenant, idempotencyKey, body));
    }

    /// <summary>İmzayı sabit zamanlı karşılaştırır; bozuk onaltılık metin istisna değil false döner.</summary>
    public static bool Verify(string             secret,
                              string             method,
                              string             pathAndQuery,
                              long               timestamp,
                              string?            tenant,
                              string?            idempotencyKey,
                              ReadOnlySpan<byte> body,
                              string             signatureHex
    ) {
        var expected = ComputeBytes(secret, method, pathAndQuery, timestamp, tenant, idempotencyKey, body);
        if (signatureHex is null || !TryDecodeHex(signatureHex, out var actual)) return false;

        return CryptographicOperations.FixedTimeEquals(expected, actual);
    }

    private static byte[] ComputeBytes(string             secret,
                                       string             method,
                                       string             pathAndQuery,
                                       long               timestamp,
                                       string?            tenant,
                                       string?            idempotencyKey,
                                       ReadOnlySpan<byte> body
    ) {
        var canonical = CanonicalString(method, pathAndQuery, timestamp, tenant, idempotencyKey, body);
        return HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(canonical));
    }

    private static bool TryDecodeHex(string hex, out byte[] bytes) {
        bytes = [];
        if (hex.Length != 64) return false; // SHA-256 çıktısı 32 bayt = 64 onaltılık hane

        try {
            bytes = Convert.FromHexString(hex);
            return true;
        }
        catch (FormatException) {
            return false;
        }
    }
}
