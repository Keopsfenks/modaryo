using System.Text.Json.Nodes;

namespace ERP.Web.FunctionalTests.Infrastructure;

/// <summary>Kimlik servisini başlatmadan, verilen firmada ve izinlerle oturumu olan istemci üretir.</summary>
public static class TestSessions
{
    public static async Task<HttpClient> ClientAsync(this ErpApiFactory factory, Guid tenantId, params string[] permissions)
    {
        var record = ContractSessions.Example();
        record["tenant_id"] = tenantId.ToString();
        record["permissions"] = new JsonArray(permissions.Select(p => (JsonNode?)JsonValue.Create(p)).ToArray());
        var sid = await ContractSessions.StoreAsync(record);
        return factory.CreateClientWithSession(sid);
    }

    /// <summary>Aktif firması olmayan (tenant_id null) oturumla istemci üretir.</summary>
    public static async Task<HttpClient> ClientWithoutTenantAsync(this ErpApiFactory factory, params string[] permissions)
    {
        var record = ContractSessions.Example();
        record["tenant_id"] = null;
        record["permissions"] = new JsonArray(permissions.Select(p => (JsonNode?)JsonValue.Create(p)).ToArray());
        var sid = await ContractSessions.StoreAsync(record);
        return factory.CreateClientWithSession(sid);
    }
}
