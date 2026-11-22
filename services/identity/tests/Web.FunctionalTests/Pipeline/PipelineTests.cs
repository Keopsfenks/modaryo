using System.Text.Json;

namespace IdentityService.Web.FunctionalTests.Pipeline;

public class PipelineTests : TestBase
{
    [Test]
    public async Task Post_WithoutCsrfHeader_Returns403WithCode()
    {
        using var client = Factory.CreateClient();

        using var response = await client.PostAsync("/api/identity/auth/logout", null);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("code").GetString().ShouldBe("csrf_header_missing");
    }

    [Test]
    public async Task MalformedJsonBody_Returns400NotServerError()
    {
        using var client = Factory.CreateClientWithCookies();
        using var content = new ByteArrayContent([0x7B, 0x22, 0x65, 0x6D, 0x61, 0x69, 0x6C, 0x22, 0x3A, 0x22, 0xE7, 0x22, 0x7D]); // {"email":"<geçersiz UTF-8>"}
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");

        using var invalidUtf8 = await client.PostAsync("/api/identity/auth/login", content);
        using var notJson = await client.PostAsync("/api/identity/auth/login", new StringContent("{bozuk", System.Text.Encoding.UTF8, "application/json"));

        invalidUtf8.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        notJson.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task CrossOriginRequest_GetsNoCorsHeaders()
    {
        using var client = Factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.Add("Origin", "https://evil.example");

        using var response = await client.SendAsync(request);

        response.Headers.Contains("Access-Control-Allow-Origin").ShouldBeFalse();
    }
}
