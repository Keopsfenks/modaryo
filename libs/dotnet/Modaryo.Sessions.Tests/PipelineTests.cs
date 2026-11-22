using System.Net;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Modaryo.Sessions.Tests;

/// <summary>Handler ve CSRF middleware'ini tek uç noktalı minimal bir host üzerinde dener.</summary>
public class PipelineTests
{
    private WebApplication _app = null!;
    private HttpClient _client = null!;

    [OneTimeSetUp]
    public async Task StartHost()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddModaryoSessions(ValkeyFixture.ConnectionString);
        builder.Services.AddAuthorization();

        _app = builder.Build();
        _app.UseModaryoCsrf();
        _app.UseAuthentication();
        _app.UseAuthorization();

        _app.MapGet("/whoami", (ClaimsPrincipal user) => Results.Ok(new
        {
            userId = user.GetUserId(),
            tenantId = user.GetTenantId(),
            permissions = user.GetPermissions(),
            sessionId = user.GetSessionId(),
            sidHash = user.GetSidHash(),
            email = user.FindFirstValue(ClaimTypes.Email),
            name = user.FindFirstValue(ClaimTypes.Name),
        })).RequireAuthorization();
        _app.MapPost("/echo", () => Results.Ok("ok"));

        await _app.StartAsync();
        _client = _app.GetTestClient();
    }

    [OneTimeTearDown]
    public async Task StopHost()
    {
        _client.Dispose();
        await _app.DisposeAsync();
    }

    [Test]
    public async Task Handler_ValidCookie_SetsClaims()
    {
        var record = ValkeyFixture.NewRecord(DateTimeOffset.UtcNow, TimeSpan.FromHours(24)) with { Permissions = ["members.read", "company.update"] };
        var sid = await ValkeyFixture.StoreAsync(JsonSerializer.Serialize(record, SessionRecord.Json), TimeSpan.FromHours(8));

        using var request = new HttpRequestMessage(HttpMethod.Get, "/whoami");
        request.Headers.Add("Cookie", $"{SessionKeys.CookieName}={sid}");
        using var response = await _client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("userId").GetGuid().ShouldBe(record.UserId);
        body.RootElement.GetProperty("tenantId").GetGuid().ShouldBe(record.TenantId!.Value);
        body.RootElement.GetProperty("permissions").EnumerateArray().Select(e => e.GetString()).ShouldBe(["members.read", "company.update"]);
        body.RootElement.GetProperty("sessionId").GetString().ShouldBe(record.Id);
        body.RootElement.GetProperty("sidHash").GetString().ShouldBe(SessionKeys.HashSid(sid));
        body.RootElement.GetProperty("email").GetString().ShouldBe(record.Email);
        body.RootElement.GetProperty("name").GetString().ShouldBe(record.FullName);
    }

    [Test]
    public async Task Handler_NoCookie_Returns401()
    {
        using var response = await _client.GetAsync("/whoami");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task Handler_UnknownSid_Returns401()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/whoami");
        request.Headers.Add("Cookie", $"{SessionKeys.CookieName}={SessionKeys.NewSid()}");
        using var response = await _client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task Csrf_PostWithoutHeader_Returns403WithCode()
    {
        using var response = await _client.PostAsync("/echo", null);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("code").GetString().ShouldBe("csrf_header_missing");
    }

    [Test]
    public async Task Csrf_PostWithHeader_Passes()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/echo");
        request.Headers.Add(SessionKeys.CsrfHeader, "1");
        using var response = await _client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Test]
    public async Task Csrf_GetWithoutHeader_Passes()
    {
        using var response = await _client.GetAsync("/whoami");

        response.StatusCode.ShouldNotBe(HttpStatusCode.Forbidden);
    }
}
