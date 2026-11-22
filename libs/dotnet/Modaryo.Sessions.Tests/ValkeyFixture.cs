using StackExchange.Redis;

namespace Modaryo.Sessions.Tests;

/// <summary>
/// Testler compose ile çalışan Valkey'in 1 numaralı veritabanını kullanır (üretim 0'ı kullanır).
/// </summary>
[SetUpFixture]
public class ValkeyFixture
{
    public const string ConnectionString = "localhost:6379,defaultDatabase=1";

    public static IConnectionMultiplexer Redis { get; private set; } = null!;

    [OneTimeSetUp]
    public async Task Connect()
    {
        Redis = await ConnectionMultiplexer.ConnectAsync(ConnectionString);
    }

    [OneTimeTearDown]
    public async Task Disconnect()
    {
        await Redis.DisposeAsync();
    }

    public static SessionRecord NewRecord(DateTimeOffset now, TimeSpan absoluteIn, int idleSeconds = 28800) => new()
    {
        V = 1,
        Id = SessionKeys.NewPublicId(),
        UserId = Guid.CreateVersion7(),
        Email = "test@ornek.com",
        FullName = "Test Kullanıcı",
        TenantId = Guid.CreateVersion7(),
        Permissions = ["members.read"],
        RememberMe = false,
        CreatedAt = now.ToUnixTimeSeconds(),
        AbsoluteExpiresAt = now.Add(absoluteIn).ToUnixTimeSeconds(),
        IdleTimeoutS = idleSeconds,
    };

    /// <summary>Kaydı ham JSON olarak yazar ve okuyucuya verilecek sid'i döndürür.</summary>
    public static async Task<string> StoreAsync(string json, TimeSpan ttl)
    {
        var sid = SessionKeys.NewSid();
        await Redis.GetDatabase().StringSetAsync(SessionKeys.SessionKey(SessionKeys.HashSid(sid)), json, ttl);
        return sid;
    }
}
