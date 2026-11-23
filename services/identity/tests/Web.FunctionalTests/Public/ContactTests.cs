using IdentityService.Web.FunctionalTests.Infrastructure;

namespace IdentityService.Web.FunctionalTests.Public;

public class ContactTests : TestBase
{
    private const string Url = "/api/identity/public/contact";

    private static object Valid(string website = "", bool consent = true, string name = "Ayşe Yılmaz", string email = "ayse@ornek.com") => new
    {
        name,
        company = "Örnek Halı Ltd.",
        email,
        phone = "0555 000 00 00",
        topic = "demo",
        interest = "satis",
        message = "Demo görmek istiyoruz.",
        kvkkConsent = consent,
        website,
    };

    [Test]
    public async Task Contact_Valid_Returns202AndSendsEmailToSales()
    {
        using var client = Factory.CreateClientWithCookies();

        using var response = await client.PostAsJsonAsync(Url, Valid());

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        var mail = Factory.Emails.Sent.ShouldHaveSingleItem();
        mail.To.ShouldBe("satis@modaryo.local");
        mail.Subject.ShouldBe("Yeni demo talebi: Örnek Halı Ltd.");
        mail.TextBody.ShouldContain("ayse@ornek.com");
        mail.TextBody.ShouldContain("Demo görmek istiyoruz.");
    }

    [Test]
    public async Task Contact_NoCompany_SubjectUsesName()
    {
        using var client = Factory.CreateClientWithCookies();

        using var response = await client.PostAsJsonAsync(Url, new { name = "Ayşe Yılmaz", email = "ayse@ornek.com", topic = "iletisim", kvkkConsent = true, website = "" });

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        Factory.Emails.Sent.ShouldHaveSingleItem().Subject.ShouldBe("Yeni demo talebi: Ayşe Yılmaz");
    }

    [Test]
    public async Task Contact_HtmlAndLineBreaksInInput_AreEscapedAndNotInSubject()
    {
        using var client = Factory.CreateClientWithCookies();

        using var response = await client.PostAsJsonAsync(Url, new
        {
            name = "Ali",
            company = "X\r\nBcc: kotu@ornek.com <b>",
            email = "ali@ornek.com",
            topic = "demo",
            message = "<script>alert(1)</script>",
            kvkkConsent = true,
            website = "",
        });

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        var mail = Factory.Emails.Sent.ShouldHaveSingleItem();
        mail.Subject.ShouldNotContain("\r");
        mail.Subject.ShouldNotContain("\n");
        mail.HtmlBody.ShouldNotContain("<script>");
        mail.HtmlBody.ShouldNotContain("<b>");
    }

    [Test]
    public async Task Contact_WithoutConsent_Returns400()
    {
        using var client = Factory.CreateClientWithCookies();

        using var response = await client.PostAsJsonAsync(Url, Valid(consent: false));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        Factory.Emails.Sent.ShouldBeEmpty();
    }

    [Test]
    [TestCase("A", "ayse@ornek.com", "demo")]
    [TestCase("Ayşe", "gecersiz", "demo")]
    [TestCase("Ayşe", "ayse@ornek.com", "baska")]
    public async Task Contact_InvalidFields_Returns400(string name, string email, string topic)
    {
        using var client = Factory.CreateClientWithCookies();

        using var response = await client.PostAsJsonAsync(Url, new { name, email, topic, kvkkConsent = true, website = "" });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        Factory.Emails.Sent.ShouldBeEmpty();
    }

    [Test]
    public async Task Contact_TooLongFields_Returns400()
    {
        using var client = Factory.CreateClientWithCookies();

        using var longMessage = await client.PostAsJsonAsync(Url, new { name = "Ayşe", email = "a@ornek.com", topic = "demo", message = new string('x', 2001), kvkkConsent = true, website = "" });
        using var longPhone = await client.PostAsJsonAsync(Url, new { name = "Ayşe", email = "a@ornek.com", topic = "demo", phone = new string('1', 21), kvkkConsent = true, website = "" });
        using var longInterest = await client.PostAsJsonAsync(Url, new { name = "Ayşe", email = "a@ornek.com", topic = "demo", interest = new string('i', 51), kvkkConsent = true, website = "" });

        longMessage.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        longPhone.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        longInterest.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        Factory.Emails.Sent.ShouldBeEmpty();
    }

    [Test]
    public async Task Contact_Honeypot_Returns202AndSendsNothing()
    {
        using var client = Factory.CreateClientWithCookies();

        using var filled = await client.PostAsJsonAsync(Url, Valid(website: "http://spam.example"));
        using var filledAndInvalid = await client.PostAsJsonAsync(Url, new { name = "", email = "x", topic = "?", kvkkConsent = false, website = "http://spam.example" });

        filled.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        filledAndInvalid.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        Factory.Emails.Sent.ShouldBeEmpty();
    }

    [Test]
    public async Task Contact_SixthRequest_Returns429()
    {
        await using var limited = new IdentityApiFactory(TestEnvironment.ConnectionString, new Dictionary<string, string?> { ["RateLimits:Enabled"] = "true" });
        using var client = limited.CreateClientWithCookies();
        var statuses = new List<HttpStatusCode>();

        for (var i = 0; i < 6; i++)
        {
            using var response = await client.PostAsJsonAsync(Url, Valid(email: $"kisi{i}@ornek.com"));
            statuses.Add(response.StatusCode);
        }

        statuses.Take(5).ShouldAllBe(s => s == HttpStatusCode.Accepted);
        statuses[5].ShouldBe(HttpStatusCode.TooManyRequests);
        limited.Emails.Sent.Count.ShouldBe(5);
    }

    [Test]
    public async Task Contact_SpoofedForwardedForPrefix_SharesTheTrustedProxyBucket()
    {
        await using var limited = new IdentityApiFactory(TestEnvironment.ConnectionString, new Dictionary<string, string?> { ["RateLimits:Enabled"] = "true" });
        using var client = limited.CreateClientWithCookies();
        var statuses = new List<HttpStatusCode>();

        for (var i = 0; i < 6; i++)
        {
            // Güvenilir proxy istemciyi sağa ekler (203.0.113.9); istemcinin başa yazdığı sahte adres her istekte farklı.
            using var request = new HttpRequestMessage(HttpMethod.Post, Url) { Content = JsonContent.Create(Valid(email: $"kisi{i}@ornek.com")) };
            request.Headers.Add("X-Forwarded-For", $"198.51.100.{i + 1}, 203.0.113.9");
            using var response = await client.SendAsync(request);
            statuses.Add(response.StatusCode);
        }

        statuses.Take(5).ShouldAllBe(s => s == HttpStatusCode.Accepted);
        statuses[5].ShouldBe(HttpStatusCode.TooManyRequests);
    }

    [Test]
    public async Task Contact_WithoutCsrfHeader_Returns403()
    {
        using var client = Factory.CreateClient();

        using var response = await client.PostAsJsonAsync(Url, Valid());

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        Factory.Emails.Sent.ShouldBeEmpty();
    }
}
