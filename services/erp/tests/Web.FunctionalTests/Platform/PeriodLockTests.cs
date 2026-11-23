using System.Text.Json;

namespace ERP.Web.FunctionalTests.Platform;

public class PeriodLockTests : TestBase {
    private const string Url  = "/api/erp/current-accounts";
    private const string Lock = "2026-10-31";

    private static Task<HttpClient> Client(Guid tenant) {
        return Factory.ClientAsync(tenant, "cari.read", "cari.manage", "cari.movements", "ayarlar.finans");
    }

    private static async Task SetLockAsync(HttpClient client, string date, int version = 0) {
        using var response = await client.PutAsJsonAsync("/api/erp/settings/finans",
                                                         new { version, value = new { periodLockDate = date } });
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    private static async Task<Guid> CreateCardAsync(HttpClient client, object? opening = null) {
        using var response = await client.PostAsJsonAsync(Url, new {
                                                                       code           = "C0001",
                                                                       name           = "Örnek Ltd.",
                                                                       kind           = "corporate",
                                                                       isCustomer     = true,
                                                                       currency       = "TRY",
                                                                       defaultDueDays = 30,
                                                                       openingBalance = opening
                                                                   });
        return (await response.JsonAsync()).GetProperty("id").GetGuid();
    }

    private static Task<HttpResponseMessage> PostDebitAsync(HttpClient client, Guid id, string date) {
        return client.PostAsJsonAsync($"{Url}/{id}/entries", new { type = "debit", date, amount = 100m });
    }

    [Test]
    public async Task AddEntry_OnLockDate_Returns409PeriodLocked() {
        using var client = await Client(Guid.CreateVersion7());
        await SetLockAsync(client, Lock);
        var id = await CreateCardAsync(client);

        using var response = await PostDebitAsync(client, id, Lock);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.ErrorCodeAsync()).ShouldBe("period_locked");
    }

    [Test]
    public async Task AddEntry_UnknownAccountInLockedPeriod_Returns404() {
        using var client = await Client(Guid.CreateVersion7());
        await SetLockAsync(client, Lock);

        using var response = await PostDebitAsync(client, Guid.CreateVersion7(), Lock);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task AddEntry_DayAfterLock_Returns201() {
        using var client = await Client(Guid.CreateVersion7());
        await SetLockAsync(client, Lock);
        var id = await CreateCardAsync(client);

        using var response = await PostDebitAsync(client, id, "2026-11-01");

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Test]
    public async Task CancelEntry_DatedInLockedPeriod_Returns409() {
        using var client = await Client(Guid.CreateVersion7());
        var       id     = await CreateCardAsync(client);
        using var added  = await PostDebitAsync(client, id, "2026-10-15");
        var       entry  = (await added.JsonAsync()).GetProperty("entry").GetProperty("id").GetGuid();
        await SetLockAsync(client, Lock);

        using var response = await client.PostAsJsonAsync($"{Url}/{id}/entries/{entry}/cancel",
                                                          new { reason = "Hatalı giriş" });

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.ErrorCodeAsync()).ShouldBe("period_locked");
    }

    [Test]
    public async Task CreateCard_OpeningInLockedPeriod_Returns409() {
        using var client = await Client(Guid.CreateVersion7());
        await SetLockAsync(client, Lock);

        using var response = await client.PostAsJsonAsync(Url, new {
                                                                       code           = "C0001",
                                                                       name           = "Örnek Ltd.",
                                                                       kind           = "corporate",
                                                                       isCustomer     = true,
                                                                       currency       = "TRY",
                                                                       defaultDueDays = 30,
                                                                       openingBalance = new {
                                                                           date      = "2026-01-01",
                                                                           amount    = 50m,
                                                                           direction = "debit"
                                                                       }
                                                                   });

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.ErrorCodeAsync()).ShouldBe("period_locked");
    }

    [Test]
    public async Task CreateCard_NoOpening_InLockedTenant_Returns201() {
        using var client = await Client(Guid.CreateVersion7());
        await SetLockAsync(client, Lock);

        using var response = await client.PostAsJsonAsync(Url, new {
                                                                       code           = "C0001",
                                                                       name           = "Örnek Ltd.",
                                                                       kind           = "corporate",
                                                                       isCustomer     = true,
                                                                       currency       = "TRY",
                                                                       defaultDueDays = 30
                                                                   });

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Test]
    public async Task NoLockSet_AllowsAnyDate() {
        using var client = await Client(Guid.CreateVersion7());
        var       id     = await CreateCardAsync(client);

        using var response = await PostDebitAsync(client, id, "2020-01-01");

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Test]
    public async Task PeriodLock_Error_HasTurkishMessageWithDate() {
        using var client = await Client(Guid.CreateVersion7());
        await SetLockAsync(client, Lock);
        var id = await CreateCardAsync(client);

        using var response = await PostDebitAsync(client, id, "2026-10-01");

        (await response.JsonAsync()).GetProperty("title").GetString()
            .ShouldBe("31.10.2026 ve öncesi kilitli dönemde.");
    }
}
