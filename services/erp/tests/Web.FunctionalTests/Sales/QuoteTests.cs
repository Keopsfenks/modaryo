using ERP.Domain.Sales;
using ERP.Infrastructure.Sales;
using ERP.Web.FunctionalTests.Invoicing;
using ERP.Web.FunctionalTests.Pricing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static ERP.Web.FunctionalTests.Invoicing.InvoiceTestSupport;
using static ERP.Web.FunctionalTests.Sales.SalesTestSupport;

namespace ERP.Web.FunctionalTests.Sales;

public class QuoteTests : TestBase {
    private static object Money(decimal amount) => new { amount, currency = "TRY", vatIncluded = true };

    [Test]
    public async Task CreateQuote_PricesFromPriority() {
        var env     = await NewSalesEnv();
        var product = await PricingApi.CreateProduct(env.Client, "FIYAT1", new { vatRate = 20, baseUnit = "adet", defaultSalePrice = Money(100m) });

        var fromCard = await CreateQuote(env, QuoteBody(env, [QLine(product, 1m, null)]));
        fromCard.GetProperty("lines")[0].GetProperty("unitPrice").GetDecimal().ShouldBe(100m);
        fromCard.GetProperty("lines")[0].GetProperty("vatIncluded").GetBoolean().ShouldBeTrue();
        fromCard.GetProperty("total").GetDecimal().ShouldBe(100m);
        fromCard.GetProperty("number").GetString()!.ShouldMatch(@"^TK2026\d{6}$");
        fromCard.GetProperty("status").GetString().ShouldBe("draft");

        using var customerPrice = await env.Client.PostAsJsonAsync($"/api/erp/pricing/customers/{env.Customer}/prices", new {
            productId = product, unit = "adet", currency = "TRY", vatIncluded = true, tiers = PricingApi.Tiers((1, 80m))
        });
        customerPrice.StatusCode.ShouldBe(HttpStatusCode.Created, await customerPrice.Content.ReadAsStringAsync());

        var special = await CreateQuote(env, QuoteBody(env, [QLine(product, 2m, null)]));
        special.GetProperty("lines")[0].GetProperty("unitPrice").GetDecimal().ShouldBe(80m);
        special.GetProperty("total").GetDecimal().ShouldBe(160m); // KDV dahil 2 x 80
        special.GetProperty("vat").GetDecimal().ShouldBe(26.67m);
    }

    [Test]
    public async Task EditSent_CreatesRevision2KeepsRevision1() {
        var env   = await NewSalesEnv();
        var quote = await CreateQuote(env, QuoteBody(env, [QLine(env.Product, 2m, 50m)]));
        var id    = quote.GetProperty("id").GetGuid();
        await Post(env.Client, $"quotes/{id}/send", new { });

        using var update = await env.Client.PutAsJsonAsync($"{Api}/quotes/{id}", QuoteBody(env, [QLine(env.Product, 5m, 50m)]));

        update.StatusCode.ShouldBe(HttpStatusCode.OK, await update.Content.ReadAsStringAsync());
        var edited = await update.JsonAsync();
        edited.GetProperty("currentRevision").GetInt32().ShouldBe(2);
        edited.GetProperty("status").GetString().ShouldBe("draft");
        edited.GetProperty("revisions").GetArrayLength().ShouldBe(2);
        (await GetQuote(env.Client, id, 1)).GetProperty("lines")[0].GetProperty("quantity").GetDecimal().ShouldBe(2m);
        (await GetQuote(env.Client, id)).GetProperty("lines")[0].GetProperty("quantity").GetDecimal().ShouldBe(5m);

        // Taslakta düzenleme yerinde yapılır: yeni revizyon açılmaz.
        using var again = await env.Client.PutAsJsonAsync($"{Api}/quotes/{id}", QuoteBody(env, [QLine(env.Product, 6m, 50m)]));
        (await again.JsonAsync()).GetProperty("revisions").GetArrayLength().ShouldBe(2);
        (await GetQuote(env.Client, id, 2)).GetProperty("lines")[0].GetProperty("quantity").GetDecimal().ShouldBe(6m);
    }

    [Test]
    public async Task Accept_CreatesOrderWithSameLines() {
        var env   = await NewSalesEnv();
        var quote = await CreateQuote(env, QuoteBody(env, [QLine(env.Product, 2m, 50m), QLine(env.Product, 3m, 10m, new { discountPercent = 10m })],
                                                     date: Today(), extra: new { validUntil = Today().AddDays(10).ToString("yyyy-MM-dd") }));
        var id = quote.GetProperty("id").GetGuid();

        using var early = await env.Client.PostAsJsonAsync($"{Api}/quotes/{id}/accept", new { });
        await Fail(early, HttpStatusCode.Conflict, "quote_not_editable");
        await Post(env.Client, $"quotes/{id}/send", new { });

        var accepted = await Post(env.Client, $"quotes/{id}/accept");

        var orderId = accepted.GetProperty("orderId").GetGuid();
        (await GetQuote(env.Client, id)).GetProperty("status").GetString().ShouldBe("accepted");
        (await GetQuote(env.Client, id)).GetProperty("salesOrderId").GetGuid().ShouldBe(orderId);
        await using var db = PricingTestSupport.Db(env.Tenant);
        var order = await db.SalesOrders.Include(o => o.Lines).SingleAsync(o => o.Id == orderId);
        order.Number.ShouldMatch(@"^SS2026\d{6}$");
        order.Status.ShouldBe(SalesOrderStatus.Draft);
        order.QuoteId.ShouldBe(id);
        order.Total.ShouldBe(quote.GetProperty("total").GetDecimal());
        order.Lines.OrderBy(l => l.Sort).Select(l => (l.Quantity, l.UnitPrice, l.DiscountPercent)).ShouldBe([(2m, 50m, (decimal?)null), (3m, 10m, 10m)]);
    }

    [Test]
    public async Task ExpiryJob_ExpiresPastValidUntil() {
        var env     = await NewSalesEnv();
        var overdue = (await CreateQuote(env, QuoteBody(env, extra: new { validUntil = "2026-03-10" }))).GetProperty("id").GetGuid();
        var future  = (await CreateQuote(env, QuoteBody(env, extra: new { validUntil = "2026-03-31" }))).GetProperty("id").GetGuid();
        var draft   = (await CreateQuote(env, QuoteBody(env, extra: new { validUntil = "2026-03-10" }))).GetProperty("id").GetGuid();
        await Post(env.Client, $"quotes/{overdue}/send");
        await Post(env.Client, $"quotes/{future}/send");
        var job = TestEnvironment.Factory.Services.GetRequiredService<QuoteExpiryJob>();

        var first  = await job.PublicRunOnceAsync(CancellationToken.None, env.Tenant, new DateOnly(2026, 3, 11));
        var second = await job.PublicRunOnceAsync(CancellationToken.None, env.Tenant, new DateOnly(2026, 3, 11));

        first.ShouldBe(1);
        second.ShouldBe(0);
        (await GetQuote(env.Client, overdue)).GetProperty("status").GetString().ShouldBe("expired");
        (await GetQuote(env.Client, future)).GetProperty("status").GetString().ShouldBe("sent");
        (await GetQuote(env.Client, draft)).GetProperty("status").GetString().ShouldBe("draft");
        using var edit = await env.Client.PutAsJsonAsync($"{Api}/quotes/{overdue}", QuoteBody(env));
        await Fail(edit, HttpStatusCode.Conflict, "quote_not_editable");
    }

    [Test]
    public async Task DiscountOverLimit_Returns403() {
        var env = await NewSalesEnv(maxDiscount: 5m);

        using var over = await env.Client.PostAsJsonAsync($"{Api}/quotes", QuoteBody(env, [QLine(env.Product, 2m, 50m, new { discountPercent = 10m })]));
        using var ok   = await env.Client.PostAsJsonAsync($"{Api}/quotes", QuoteBody(env, [QLine(env.Product, 2m, 50m, new { discountPercent = 5m })]));

        await Fail(over, HttpStatusCode.Forbidden, "discount_limit_exceeded");
        ok.StatusCode.ShouldBe(HttpStatusCode.Created, await ok.Content.ReadAsStringAsync());
    }

    [Test]
    public async Task SalesRepRequiredSetting_Returns400() {
        var env = await NewSalesEnv();
        await SetSalesSettings(env.Client, new { quoteValidityDays = 15, requireSalesRep = true, defaultInstallmentCount = 3, prepaymentDefaultOff = true });

        using var response = await env.Client.PostAsJsonAsync($"{Api}/quotes", QuoteBody(env));

        await Fail(response, HttpStatusCode.BadRequest, "sales_rep_required");
    }

    [Test]
    public async Task RepNotMember_Returns400() {
        var env    = await NewSalesEnv();
        var member = TestEnvironment.Factory.Members.Seed(env.Tenant, Guid.CreateVersion7(), "Ayşe Temsilci");

        using var stranger = await env.Client.PostAsJsonAsync($"{Api}/quotes", QuoteBody(env, extra: new { salesRepUserId = Guid.CreateVersion7() }));
        var       created  = await CreateQuote(env, QuoteBody(env, extra: new { salesRepUserId = member.UserId }));

        await Fail(stranger, HttpStatusCode.BadRequest, "sales_rep_invalid");
        created.GetProperty("salesRepUserId").GetGuid().ShouldBe(member.UserId);
        created.GetProperty("salesRepName").GetString().ShouldBe("Ayşe Temsilci");
    }

    [Test]
    public async Task QuotePdf_ShowsMeasureDimensions() {
        var env  = await NewSalesEnv();
        var hali = await PricingApi.CreateProduct(env.Client, "HALI1", new { vatRate = 20, baseUnit = "m2", soldByMeasure = true, measureUnit = "m2" });
        var quote = await CreateQuote(env, QuoteBody(env, [QLine(hali, 0m, 2000m, new { vatIncluded = true, width = 1.6m, length = 2.3m })]));
        var id    = quote.GetProperty("id").GetGuid();

        quote.GetProperty("lines")[0].GetProperty("quantity").GetDecimal().ShouldBe(3.68m);
        quote.GetProperty("total").GetDecimal().ShouldBe(7360m);
        using var response = await env.Client.GetAsync($"{Api}/quotes/{id}/pdf");
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/pdf");
        var pdf  = await response.Content.ReadAsByteArrayAsync();
        var text = PdfTests.Text(pdf);

        pdf.AsSpan(0, 5).ToArray().ShouldBe("%PDF-"u8.ToArray());
        text.ShouldContain(quote.GetProperty("number").GetString()!);
        text.ShouldContain("TEKLİF");
        text.ShouldContain("1,60×2,30");
        text.ShouldContain("7.360,00");
    }
}
