namespace ERP.Web.FunctionalTests.Infrastructure;

public abstract class TestBase {
    protected static ErpApiFactory Factory => TestEnvironment.Factory;

    [SetUp]
    public async Task ResetState() {
        await TestEnvironment.ResetAsync();
    }
}