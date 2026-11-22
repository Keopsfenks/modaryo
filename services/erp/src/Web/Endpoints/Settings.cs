using System.Text.Json;
using ERP.Application.Audit.Queries;
using ERP.Application.CurrentAccounts;
using ERP.Application.Settings;
using ERP.Application.Settings.Commands;
using ERP.Application.Settings.Queries;
using Microsoft.AspNetCore.Http.HttpResults;

namespace ERP.Web.Endpoints;

public sealed record UpdateSettingsRequest(int Version, JsonElement Value);

public class Settings : IEndpointGroup {
    public static string? RoutePrefix => "/api/erp/settings";

    public static void Map(RouteGroupBuilder groupBuilder) {
        groupBuilder.MapGet(GetSettings, "{category}");
        groupBuilder.MapPut(PutSettings, "{category}");
    }

    [EndpointSummary("Ayar kategorisini getir")]
    [EndpointDescription("category: firma (ayarlar.firma) | genel (ayarlar.genel) | finans (ayarlar.finans); kategorinin izni gerekir, bilinmeyen kategori 404. Kayıt yoksa varsayılan değerler version 0 ile döner.")]
    public static async Task<Ok<SettingsDto>> GetSettings(ISender sender, string category) {
        return TypedResults.Ok(await sender.Send(new GetSettingsQuery(category)));
    }

    [EndpointSummary("Ayar kategorisini güncelle")]
    [EndpointDescription("Gövde: { version, value }. version kayıtlı sürümle eşleşmezse 409 version_conflict (kayıt yoksa 0 beklenir). Başarıda yeni sürümle birlikte kaydedilen değer döner; her yazma denetim kaydına işlenir.")]
    public static async Task<Ok<SettingsDto>> PutSettings(ISender sender, string category, UpdateSettingsRequest body) {
        return TypedResults.Ok(await sender.Send(new UpdateSettingsCommand(category, body.Version, body.Value)));
    }
}

public class Audit : IEndpointGroup {
    public static string? RoutePrefix => "/api/erp/audit";

    public static void Map(RouteGroupBuilder groupBuilder) {
        groupBuilder.MapGet(ListAudit);
    }

    [EndpointSummary("Denetim kaydı")]
    [EndpointDescription("denetim.read gerekir. En yeni önce; entityType ile süzülür. pageSize varsayılan 50, en fazla 200.")]
    public static async Task<Ok<PagedResult<ErpAuditEntryDto>>> ListAudit(
        ISender sender,
        string? entityType,
        int     page     = 1,
        int     pageSize = 50
    ) {
        return TypedResults.Ok(await sender.Send(new ListAuditQuery(entityType, page, pageSize)));
    }
}
