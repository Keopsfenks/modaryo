using System.Text.Json;
using ERP.Web.FunctionalTests.Treasury;
using Microsoft.Extensions.DependencyInjection;
using static ERP.Web.FunctionalTests.Pos.PosTestSupport;

namespace ERP.Web.FunctionalTests.Pos;

public class RegisterTests : TestBase {
    private static async Task<string?> Fail(HttpResponseMessage response, HttpStatusCode status) {
        response.StatusCode.ShouldBe(status, await response.Content.ReadAsStringAsync());
        return await response.ErrorCodeAsync();
    }

    [Test]
    public async Task Register_RequiresCashAndCardAccountsOfBranch() {
        var env         = await NewEnv();
        var foreignCash = (await TreasuryTestSupport.CreateAccount(env.Client, env.OtherBranch, "Kadıköy Kasa")).GetProperty("id").GetGuid();

        using var wrongBranchCash = await env.Client.PostAsJsonAsync($"{Api}/registers", RegisterBody(env, "K1", new { cashAccountId = foreignCash }));
        (await Fail(wrongBranchCash, HttpStatusCode.BadRequest)).ShouldBe("cash_account_invalid");

        using var cashAsCard = await env.Client.PostAsJsonAsync($"{Api}/registers", RegisterBody(env, "K1", new { cardAccountId = env.CashAccount }));
        (await Fail(cashAsCard, HttpStatusCode.BadRequest)).ShouldBe("card_account_invalid");

        using var otherWarehouse = await env.Client.PostAsJsonAsync($"{Api}/registers", RegisterBody(env, "K1", new { warehouseId = env.OtherWarehouse }));
        (await Fail(otherWarehouse, HttpStatusCode.BadRequest)).ShouldBe("warehouse_invalid");

        using var badCode = await env.Client.PostAsJsonAsync($"{Api}/registers", RegisterBody(env, "k 1!"));
        badCode.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await badCode.Content.ReadAsStringAsync());

        var created = await CreateRegister(env, "k1");
        created.GetProperty("code").GetString().ShouldBe("K1");
        created.GetProperty("isActive").GetBoolean().ShouldBeTrue();

        using var duplicate = await env.Client.PostAsJsonAsync($"{Api}/registers", RegisterBody(env, "K1"));
        (await Fail(duplicate, HttpStatusCode.Conflict)).ShouldBe("code_taken");
    }

    [Test]
    public async Task Register_UpdateAndPickerPermissions() {
        var env      = await NewEnv();
        var register = await CreateRegister(env);
        var id       = register.GetProperty("id").GetGuid();

        using var renamed = await env.Client.PutAsJsonAsync($"{Api}/registers/{id}", RegisterBody(env, "K1", new { name = "Ön Kasa", isActive = false }));
        renamed.StatusCode.ShouldBe(HttpStatusCode.OK, await renamed.Content.ReadAsStringAsync());
        (await renamed.JsonAsync()).GetProperty("isActive").GetBoolean().ShouldBeFalse();

        using var stale = await env.Client.PutAsJsonAsync($"{Api}/registers/{id}", RegisterBody(env, "K1", new { version = 1u }));
        (await Fail(stale, HttpStatusCode.Conflict)).ShouldBe("version_conflict");

        // Kasiyer kasaları görebilir ama oluşturamaz.
        var cashier = await TestEnvironment.Factory.ClientAsync(env.Tenant, null, null, null, ["pos.sell"]);
        using var list = await cashier.GetAsync($"{Api}/registers");
        list.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await list.JsonAsync()).GetArrayLength().ShouldBe(1);
        using var denied = await cashier.PostAsJsonAsync($"{Api}/registers", RegisterBody(env, "K2"));
        denied.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task Register_ButtonsLayoutRoundTrips() {
        var env     = await NewEnv();
        var id      = (await CreateRegister(env)).GetProperty("id").GetGuid();
        var product = await Product(env.Client, "EKM");

        var pages = new object?[] {
            new object?[] { new { productId = product, presetQty = 2m, color = "#ff0000" }, null, new { productId = product } },
            new object?[] { new { productId = product, color = "#00ff00" } }
        };
        using var put = await env.Client.PutAsJsonAsync($"{Api}/registers/{id}/buttons", new { pages });
        put.StatusCode.ShouldBe(HttpStatusCode.OK, await put.Content.ReadAsStringAsync());

        using var get = await env.Client.GetAsync($"{Api}/registers/{id}");
        var       p   = (await get.JsonAsync()).GetProperty("pages");
        p.GetArrayLength().ShouldBe(2);
        p[0].GetArrayLength().ShouldBe(3);
        p[0][0].GetProperty("presetQty").GetDecimal().ShouldBe(2m);
        p[0][0].GetProperty("color").GetString().ShouldBe("#ff0000");
        p[0][0].GetProperty("code").GetString().ShouldBe("EKM");
        p[0][1].ValueKind.ShouldBe(JsonValueKind.Null);
        p[0][2].GetProperty("color").GetString().ShouldBe("#1976d2");
        p[1][0].GetProperty("color").GetString().ShouldBe("#00ff00");

        // Yerleşim bütünüyle değişir.
        using var replaced = await env.Client.PutAsJsonAsync($"{Api}/registers/{id}/buttons", new { pages = new object?[] { new object?[] { new { productId = product } } } });
        replaced.StatusCode.ShouldBe(HttpStatusCode.OK, await replaced.Content.ReadAsStringAsync());
        (await replaced.JsonAsync()).GetProperty("pages").GetArrayLength().ShouldBe(1);
    }

    [Test]
    public async Task Register_ButtonsLimitsAndProductsValidated() {
        var env     = await NewEnv();
        var id      = (await CreateRegister(env)).GetProperty("id").GetGuid();
        var product = await Product(env.Client, "EKM");
        var button  = new { productId = product };

        var tooMany = new object?[] { Enumerable.Repeat<object?>(button, 37).ToArray() };
        using var cells = await env.Client.PutAsJsonAsync($"{Api}/registers/{id}/buttons", new { pages = tooMany });
        cells.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await cells.Content.ReadAsStringAsync());

        var elevenPages = Enumerable.Range(0, 11).Select(_ => (object?)new object?[] { button }).ToArray();
        using var pagesResponse = await env.Client.PutAsJsonAsync($"{Api}/registers/{id}/buttons", new { pages = elevenPages });
        pagesResponse.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await pagesResponse.Content.ReadAsStringAsync());

        using var unknown = await env.Client.PutAsJsonAsync($"{Api}/registers/{id}/buttons", new { pages = new object?[] { new object?[] { new { productId = Guid.NewGuid() } } } });
        (await Fail(unknown, HttpStatusCode.BadRequest)).ShouldBe("product_invalid");

        using var color = await env.Client.PutAsJsonAsync($"{Api}/registers/{id}/buttons", new { pages = new object?[] { new object?[] { new { productId = product, color = "kırmızı" } } } });
        color.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await color.Content.ReadAsStringAsync());

        // Tam 36 hücre ve 10 sayfa sınırı kabul edilir.
        var full = Enumerable.Range(0, 10).Select(_ => (object?)Enumerable.Repeat<object?>(button, 36).ToArray()).ToArray();
        using var ok = await env.Client.PutAsJsonAsync($"{Api}/registers/{id}/buttons", new { pages = full });
        ok.StatusCode.ShouldBe(HttpStatusCode.OK, await ok.Content.ReadAsStringAsync());
    }

    [Test]
    public async Task Register_AccountInUseCannotBeDeleted() {
        var env = await NewEnv();
        await CreateRegister(env);

        using var account = await env.Client.DeleteAsync($"/api/erp/treasury/accounts/{env.CashAccount}");
        (await Fail(account, HttpStatusCode.Conflict)).ShouldBe("account_in_use");
    }

    [Test]
    public async Task Register_BranchUsageProviderReportsBranch() {
        var env = await NewEnv();
        await CreateRegister(env);

        await using var scope     = TestEnvironment.Factory.Services.CreateAsyncScope();
        var             providers = scope.ServiceProvider.GetServices<ERP.Application.Common.Interfaces.IBranchUsageProvider>().ToList();
        var             inUse     = new List<bool>();
        foreach (var provider in providers) inUse.Add(await provider.IsInUseAsync(env.Tenant, env.Branch, CancellationToken.None));
        inUse.ShouldContain(true);
        var free = new List<bool>();
        foreach (var provider in providers) free.Add(await provider.IsInUseAsync(env.Tenant, env.OtherBranch, CancellationToken.None));
        free.ShouldAllBe(r => !r);
    }
}
