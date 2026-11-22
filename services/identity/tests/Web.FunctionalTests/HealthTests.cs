namespace IdentityService.Web.FunctionalTests;

public class HealthTests : TestBase
{
    [Test]
    public async Task Health_ReturnsHealthy()
    {
        using var client = Factory.CreateClient();

        using var response = await client.GetAsync("/health");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
