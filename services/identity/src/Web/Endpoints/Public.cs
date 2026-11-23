using IdentityService.Application.Public.Commands;
using IdentityService.Web.Infrastructure;
using Microsoft.AspNetCore.Http.HttpResults;

namespace IdentityService.Web.Endpoints;

/// <summary>Oturumsuz, herkese açık uçlar (tanıtım sitesi).</summary>
public class Public : IEndpointGroup
{
    public static string? RoutePrefix => "/api/identity/public";

    public static void Map(RouteGroupBuilder groupBuilder)
    {
        groupBuilder.MapPost(Contact, "contact").RequireRateLimiting(RateLimiting.Contact);
    }

    [EndpointSummary("Demo / iletişim talebi")]
    [EndpointDescription("Satış ekibine e-posta gönderir; yanıt 202'dir. website alanı tuzaktır: doluysa hiçbir şey gönderilmez ama yine 202 döner. KVKK onayı zorunludur (400). IP başına saatte 5 istek (429). CSRF başlığı gerekir.")]
    public static async Task<Accepted> Contact(ISender sender, SubmitContactCommand command)
    {
        await sender.Send(command);
        return TypedResults.Accepted((string?)null);
    }
}
