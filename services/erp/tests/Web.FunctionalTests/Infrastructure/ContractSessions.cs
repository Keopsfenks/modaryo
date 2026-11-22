using System.Text.Json.Nodes;
using Modaryo.Sessions;
using StackExchange.Redis;

namespace ERP.Web.FunctionalTests.Infrastructure;

/// <summary>
/// Kimlik servisini başlatmadan, sözleşme örneğinden (contracts/session-v1.example.json) Valkey'e oturum yazar.
/// ERP'nin, kimlik servisinin yazdığı biçimi okuyabildiğinin kanıtı.
/// </summary>
public static class ContractSessions
{
    public static JsonObject Example()
    {
        var path = Path.Combine(TestContext.CurrentContext.TestDirectory, "session-v1.example.json");
        var json = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        var now = DateTimeOffset.UtcNow;
        json["created_at"] = now.ToUnixTimeSeconds();
        json["absolute_expires_at"] = now.AddHours(24).ToUnixTimeSeconds();
        json["idle_timeout_s"] = 28800;
        return json;
    }

    /// <summary>Kaydı yazar ve cookie'ye konacak sid'i döndürür.</summary>
    public static async Task<string> StoreAsync(JsonObject record)
    {
        var sid = SessionKeys.NewSid();
        await using var redis = await ConnectionMultiplexer.ConnectAsync(ErpApiFactory.ValkeyConnectionString);
        await redis.GetDatabase().StringSetAsync(SessionKeys.SessionKey(SessionKeys.HashSid(sid)), record.ToJsonString(), TimeSpan.FromHours(8));
        return sid;
    }

    public static async Task DeleteAsync(string sid)
    {
        await using var redis = await ConnectionMultiplexer.ConnectAsync(ErpApiFactory.ValkeyConnectionString);
        await redis.GetDatabase().KeyDeleteAsync(SessionKeys.SessionKey(SessionKeys.HashSid(sid)));
    }
}
