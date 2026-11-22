namespace IdentityService.Web.FunctionalTests.Infrastructure;

using IdentityService.Web.FunctionalTests;

public abstract class TestBase
{
    protected static IdentityApiFactory Factory => TestEnvironment.Factory;

    [SetUp]
    public async Task ResetState()
    {
        await TestEnvironment.ResetAsync();
    }
}
