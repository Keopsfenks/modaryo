using System.Text.Json;

namespace IdentityService.Web.FunctionalTests.Infrastructure;

public static class ApiResponses
{
    /// <summary>ProblemDetails "code" uzantısı.</summary>
    public static async Task<string?> ErrorCodeAsync(this HttpResponseMessage response)
    {
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    /// <summary>Yanıttaki "modaryo_sid" Set-Cookie başlığı (yoksa null).</summary>
    public static string? SessionCookieHeader(this HttpResponseMessage response) =>
        response.Headers.TryGetValues("Set-Cookie", out var values)
            ? values.FirstOrDefault(v => v.StartsWith("modaryo_sid=", StringComparison.Ordinal))
            : null;

    public static async Task<JsonElement> JsonAsync(this HttpResponseMessage response)
    {
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.Clone();
    }
}
