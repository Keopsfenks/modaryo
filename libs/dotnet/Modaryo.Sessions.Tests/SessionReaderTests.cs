using System.Text.Json;
using System.Text.Json.Nodes;
using Shouldly;

namespace Modaryo.Sessions.Tests;

public class SessionReaderTests
{
    private static SessionReader CreateReader() => new(ValkeyFixture.Redis, TimeProvider.System);

    [Test]
    public async Task ReadAsync_ValidRecord_ReturnsLookupWithHash()
    {
        var record = ValkeyFixture.NewRecord(DateTimeOffset.UtcNow, TimeSpan.FromHours(24));
        var sid = await ValkeyFixture.StoreAsync(JsonSerializer.Serialize(record, SessionRecord.Json), TimeSpan.FromHours(8));

        var lookup = await CreateReader().ReadAsync(sid, CancellationToken.None);

        lookup.ShouldNotBeNull();
        lookup.SidHash.ShouldBe(SessionKeys.HashSid(sid));
        lookup.Record.UserId.ShouldBe(record.UserId);
        lookup.Record.Permissions.ShouldBe(["members.read"]);
    }

    [Test]
    public async Task ReadAsync_NeverExtendsBeyondAbsoluteExpiry()
    {
        var record = ValkeyFixture.NewRecord(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(10));
        var sid = await ValkeyFixture.StoreAsync(JsonSerializer.Serialize(record, SessionRecord.Json), TimeSpan.FromSeconds(10));

        var lookup = await CreateReader().ReadAsync(sid, CancellationToken.None);

        lookup.ShouldNotBeNull();
        var ttl = await ValkeyFixture.Redis.GetDatabase().KeyTimeToLiveAsync(SessionKeys.SessionKey(lookup.SidHash));
        ttl.ShouldNotBeNull();
        ttl.Value.ShouldBeLessThanOrEqualTo(TimeSpan.FromSeconds(10));
    }

    [Test]
    public async Task ReadAsync_ExtendsIdleTtl()
    {
        var record = ValkeyFixture.NewRecord(DateTimeOffset.UtcNow, TimeSpan.FromHours(24), idleSeconds: 28800);
        var sid = await ValkeyFixture.StoreAsync(JsonSerializer.Serialize(record, SessionRecord.Json), TimeSpan.FromSeconds(60));

        await CreateReader().ReadAsync(sid, CancellationToken.None);

        var ttl = await ValkeyFixture.Redis.GetDatabase().KeyTimeToLiveAsync(SessionKeys.SessionKey(SessionKeys.HashSid(sid)));
        ttl!.Value.ShouldBeGreaterThan(TimeSpan.FromHours(7.9));
    }

    [Test]
    public async Task ReadAsync_ExpiredAbsolute_ReturnsNull()
    {
        var record = ValkeyFixture.NewRecord(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(-5));
        var sid = await ValkeyFixture.StoreAsync(JsonSerializer.Serialize(record, SessionRecord.Json), TimeSpan.FromMinutes(1));

        (await CreateReader().ReadAsync(sid, CancellationToken.None)).ShouldBeNull();
    }

    [Test]
    public async Task ReadAsync_UnknownVersion_ReturnsNull()
    {
        var record = ValkeyFixture.NewRecord(DateTimeOffset.UtcNow, TimeSpan.FromHours(24)) with { V = 2 };
        var sid = await ValkeyFixture.StoreAsync(JsonSerializer.Serialize(record, SessionRecord.Json), TimeSpan.FromMinutes(1));

        (await CreateReader().ReadAsync(sid, CancellationToken.None)).ShouldBeNull();
    }

    [Test]
    public async Task ReadAsync_IgnoresUnknownFields()
    {
        var record = ValkeyFixture.NewRecord(DateTimeOffset.UtcNow, TimeSpan.FromHours(24));
        var node = JsonSerializer.SerializeToNode(record, SessionRecord.Json)!.AsObject();
        node["extra"] = 1;
        var sid = await ValkeyFixture.StoreAsync(node.ToJsonString(), TimeSpan.FromMinutes(1));

        (await CreateReader().ReadAsync(sid, CancellationToken.None)).ShouldNotBeNull();
    }

    [Test]
    public async Task ReadAsync_MissingKey_ReturnsNull()
    {
        (await CreateReader().ReadAsync(SessionKeys.NewSid(), CancellationToken.None)).ShouldBeNull();
    }

    [Test]
    public async Task ReadAsync_MalformedJson_ReturnsNull()
    {
        var sid = await ValkeyFixture.StoreAsync("{not json", TimeSpan.FromMinutes(1));

        (await CreateReader().ReadAsync(sid, CancellationToken.None)).ShouldBeNull();
    }
}
