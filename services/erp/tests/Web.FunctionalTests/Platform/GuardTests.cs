namespace ERP.Web.FunctionalTests.Platform;

public class GuardTests : TestBase {
    private static readonly Guid Tenant   = Guid.NewGuid();
    private static readonly Guid BranchA  = Guid.NewGuid();
    private static readonly Guid BranchB  = Guid.NewGuid();

    [Test]
    public async Task RequireModule_Disabled_Returns403ModuleDisabled() {
        using var client = await Factory.ClientAsync(Tenant, modules: ["sube"]);

        using var response = await client.GetAsync("/test-probe/module");

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        var body = await response.JsonAsync();
        body.GetProperty("code").GetString().ShouldBe("module_disabled");
        body.GetProperty("title").GetString().ShouldBe("Bu modül firmanızda kapalı.");
    }

    [Test]
    public async Task RequireModule_Enabled_Returns200() {
        using var client = await Factory.ClientAsync(Tenant, modules: ["pos"]);

        (await client.GetAsync("/test-probe/module")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Test]
    public async Task BranchGuard_OtherBranch_Returns403BranchForbidden() {
        using var client = await Factory.ClientAsync(Tenant, branchIds: [BranchA]);

        using var response = await client.GetAsync($"/test-probe/branch?id={BranchB}");

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await response.JsonAsync()).GetProperty("code").GetString().ShouldBe("branch_forbidden");
        (await client.GetAsync($"/test-probe/branch?id={BranchA}")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Test]
    public async Task BranchGuard_EmptyBranchIds_DeniesAll() {
        using var client = await Factory.ClientAsync(Tenant, branchIds: []);

        (await client.GetAsync($"/test-probe/branch?id={BranchA}")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task BranchGuard_NullBranchIds_AllowsAny() {
        using var client = await Factory.ClientAsync(Tenant, branchIds: null);

        (await client.GetAsync($"/test-probe/branch?id={BranchB}")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Test]
    public async Task CurrentTenant_ExposesMaxDiscount() {
        using var client = await Factory.ClientAsync(Tenant, modules: ["pos"], maxDiscountPct: 12.5m);

        using var response = await client.GetAsync("/test-probe/tenant");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.JsonAsync();
        body.GetProperty("maxDiscountPercent").GetDecimal().ShouldBe(12.5m);
        body.GetProperty("hasPos").GetBoolean().ShouldBeTrue();
        body.GetProperty("hasSube").GetBoolean().ShouldBeFalse();
    }
}
