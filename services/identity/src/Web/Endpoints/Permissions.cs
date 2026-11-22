using IdentityService.Application.PermissionCatalogs;
using Microsoft.AspNetCore.Http.HttpResults;

namespace IdentityService.Web.Endpoints;

public class Permissions : IEndpointGroup
{
    public static string? RoutePrefix => "/api/identity/permissions";

    public static void Map(RouteGroupBuilder groupBuilder)
    {
        groupBuilder.MapGet(GetCatalog);
    }

    [EndpointSummary("İzin kataloğu")]
    [EndpointDescription("İzinler gruplu döner; firmada kapalı modüllerin izinleri gizlenir. Gruplar kayıt sırasındadır.")]
    public static async Task<Ok<PermissionCatalogDto>> GetCatalog(ISender sender) =>
        TypedResults.Ok(await sender.Send(new GetPermissionCatalogQuery()));
}
