using System.Text.Json.Nodes;

namespace ERP.Web.FunctionalTests.Infrastructure;

/// <summary>Kimlik servisini başlatmadan, verilen firmada ve izinlerle oturumu olan istemci üretir.</summary>
public static class TestSessions {
    public static Task<HttpClient> ClientAsync(this ErpApiFactory factory,
                                               Guid               tenantId,
                                               params string[]    permissions
    ) {
        return factory.ClientAsync(tenantId, null, null, null, permissions);
    }

    /// <summary>
    ///     Modül, şube ve iskonto sınırı da yazılabilen sürüm. modules null ise sözleşme örneğindeki modüller kalır;
    ///     branchIds null tüm şubeler, boş dizi hiçbir şube demektir. permissions verilmezse "*".
    /// </summary>
    public static async Task<HttpClient> ClientAsync(this ErpApiFactory factory,
                                                     Guid               tenantId,
                                                     string[]?          modules        = null,
                                                     Guid[]?            branchIds      = null,
                                                     decimal?           maxDiscountPct = null,
                                                     string[]?          permissions    = null
    ) {
        var record = ContractSessions.Example();
        record["tenant_id"]   = tenantId.ToString();
        record["permissions"] = ToArray(permissions ?? ["*"]);
        if (modules is not null) record["modules"] = ToArray(modules);
        record["branch_ids"] = branchIds is null ? null : ToArray(branchIds.Select(b => b.ToString()));
        if (maxDiscountPct is { } pct) record["max_discount_pct"] = pct;
        var sid = await ContractSessions.StoreAsync(record);
        return factory.CreateClientWithSession(sid);
    }

    private static JsonArray ToArray(IEnumerable<string> values) {
        return new JsonArray(values.Select(v => (JsonNode?)JsonValue.Create(v)).ToArray());
    }

    /// <summary>Aktif firması olmayan (tenant_id null) oturumla istemci üretir.</summary>
    public static async Task<HttpClient> ClientWithoutTenantAsync(this   ErpApiFactory factory,
                                                                  params string[]      permissions
    ) {
        var record = ContractSessions.Example();
        record["tenant_id"]   = null;
        record["permissions"] = new JsonArray(permissions.Select(p => (JsonNode?)JsonValue.Create(p)).ToArray());
        var sid = await ContractSessions.StoreAsync(record);
        return factory.CreateClientWithSession(sid);
    }
}