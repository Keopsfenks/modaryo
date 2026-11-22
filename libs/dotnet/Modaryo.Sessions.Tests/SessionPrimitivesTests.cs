using System.Security.Claims;
using System.Text.Json;
using Shouldly;

namespace Modaryo.Sessions.Tests;

public class SessionPrimitivesTests
{
    [Test]
    public void HashSid_IsLowercaseHexSha256()
    {
        SessionKeys.HashSid("abc").ShouldBe("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad");
    }

    [Test]
    public void Keys_UseContractFormat()
    {
        var userId = Guid.Parse("0193a6f2-6c1e-7b3a-9d4e-2f5a8c7b1e90");

        SessionKeys.SessionKey("abcd").ShouldBe("sess:abcd");
        SessionKeys.UserIndexKey(userId).ShouldBe("user-sess:0193a6f2-6c1e-7b3a-9d4e-2f5a8c7b1e90");
        SessionKeys.CookieName.ShouldBe("modaryo_sid");
        SessionKeys.CsrfHeader.ShouldBe("X-Modaryo-Csrf");
    }

    [Test]
    public void NewSid_IsBase64UrlOf32Bytes()
    {
        var sid = SessionKeys.NewSid();

        sid.Length.ShouldBe(43);
        sid.ShouldNotContain("+");
        sid.ShouldNotContain("/");
        sid.ShouldNotContain("=");
        SessionKeys.NewSid().ShouldNotBe(sid);
    }

    [Test]
    public void NewPublicId_Is16HexChars()
    {
        SessionKeys.NewPublicId().ShouldMatch("^[0-9a-f]{16}$");
    }

    [Test]
    public void Compute_RegularSession_ReturnsIdleWhenFarFromAbsolute()
    {
        var now = DateTimeOffset.UtcNow;
        var record = ValkeyFixture.NewRecord(now, TimeSpan.FromHours(24), idleSeconds: 28800);

        SessionTtl.Compute(record, now).ShouldBe(TimeSpan.FromHours(8));
    }

    [Test]
    public void Compute_NearAbsolute_ReturnsRemaining()
    {
        var now = DateTimeOffset.UtcNow;
        var record = ValkeyFixture.NewRecord(now, TimeSpan.FromSeconds(30));

        SessionTtl.Compute(record, now).ShouldBe(TimeSpan.FromSeconds(30));
    }

    [Test]
    public void Compute_PastAbsolute_ReturnsNull()
    {
        var now = DateTimeOffset.UtcNow;
        var record = ValkeyFixture.NewRecord(now, TimeSpan.FromSeconds(-1));

        SessionTtl.Compute(record, now).ShouldBeNull();
    }

    [Test]
    public void Lifetimes_MatchSpec()
    {
        SessionTtl.RegularIdle.ShouldBe(TimeSpan.FromHours(8));
        SessionTtl.RegularAbsolute.ShouldBe(TimeSpan.FromHours(24));
        SessionTtl.RememberIdle.ShouldBe(TimeSpan.FromDays(14));
        SessionTtl.RememberAbsolute.ShouldBe(TimeSpan.FromDays(30));
    }

    [Test]
    public void ContractExample_Deserializes()
    {
        var json = File.ReadAllText(Path.Combine(TestContext.CurrentContext.TestDirectory, "session-v1.example.json"));

        var record = JsonSerializer.Deserialize<SessionRecord>(json, SessionRecord.Json)!;

        record.V.ShouldBe(1);
        record.Id.ShouldBe("a1b2c3d4e5f60718");
        record.UserId.ShouldBe(Guid.Parse("0193a6f2-6c1e-7b3a-9d4e-2f5a8c7b1e90"));
        record.Email.ShouldBe("ali@ornek.com");
        record.FullName.ShouldBe("Ali Veli");
        record.TenantId.ShouldBe(Guid.Parse("0193a6f3-0a42-7c11-8e2b-6d9f4a3c5b21"));
        record.Permissions.ShouldBe(["*"]);
        record.RememberMe.ShouldBeTrue();
        record.CreatedAt.ShouldBe(1795200000);
        record.AbsoluteExpiresAt.ShouldBe(1797792000);
        record.IdleTimeoutS.ShouldBe(1209600);
        record.Ip.ShouldBe("203.0.113.5");
        record.UserAgent.ShouldNotBeNullOrEmpty();
    }

    [Test]
    public void Serialize_UsesSnakeCaseContractNames()
    {
        var record = ValkeyFixture.NewRecord(DateTimeOffset.UtcNow, TimeSpan.FromHours(1));

        var json = JsonSerializer.Serialize(record, SessionRecord.Json);

        foreach (var name in new[] { "\"v\"", "\"user_id\"", "\"tenant_id\"", "\"full_name\"", "\"absolute_expires_at\"", "\"idle_timeout_s\"", "\"remember_me\"", "\"user_agent\"" })
        {
            json.ShouldContain(name);
        }
    }

    [Test]
    public void HasPermission_StarGrantsEverything()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(SessionClaimTypes.Permission, "*")], "test"));

        principal.HasPermission("members.manage").ShouldBeTrue();
    }

    [Test]
    public void HasPermission_ExactMatchOnly()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(SessionClaimTypes.Permission, "members.read")], "test"));

        principal.HasPermission("members.read").ShouldBeTrue();
        principal.HasPermission("members.manage").ShouldBeFalse();
    }
}
