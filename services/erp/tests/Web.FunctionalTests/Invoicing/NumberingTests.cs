using ERP.Domain.Invoicing;
using ERP.Web.FunctionalTests.Pricing;
using Microsoft.EntityFrameworkCore;
using static ERP.Web.FunctionalTests.Invoicing.InvoiceTestSupport;

namespace ERP.Web.FunctionalTests.Invoicing;

/// <summary>Gerçek PostgreSQL ve paralel HTTP istekleri: numaralar boşluksuz ve tekil.</summary>
public class NumberingTests : TestBase {
    [Test]
    public async Task ParallelIssue_GaplessSequence() {
        var env = await NewEnv();
        await CreateSeries(env, "FTR");
        var ids = new List<Guid>();
        for (var i = 0; i < 20; i++) ids.Add((await Create(env, Body(env, lines: [FreeLine(price: 10m + i)]))).GetProperty("id").GetGuid());

        var responses = await Task.WhenAll(ids.Select(id => Task.Run(async () => {
                                                                         using var response = await env.Client.PostAsync($"{Url}/{id}/issue", null);
                                                                         return (response.StatusCode, Body: await response.Content.ReadAsStringAsync());
                                                                     })));

        responses.ShouldAllBe(r => r.StatusCode == HttpStatusCode.OK, string.Join(" | ", responses.Where(r => r.StatusCode != HttpStatusCode.OK).Select(r => r.Body)));
        var numbers = new List<string>();
        foreach (var id in ids) numbers.Add((await (await env.Client.GetAsync($"{Url}/{id}")).JsonAsync()).GetProperty("number").GetString()!);
        numbers.Order().ToArray().ShouldBe(Enumerable.Range(1, 20).Select(n => $"FTR2026{n:D9}").Order().ToArray());
        (await (await env.Client.GetAsync(SeriesUrl)).JsonAsync())[0].GetProperty("nextPreview").GetString().ShouldBe("FTR2026000000021");
    }

    [Test]
    public async Task ParallelIssue_OfTheSameDraft_NumbersOnlyOnce() {
        var env = await NewEnv();
        await CreateSeries(env, "FTR");
        var id = (await Create(env, Body(env))).GetProperty("id").GetGuid();

        var codes = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Task.Run(async () => {
                                                                                       using var response = await env.Client.PostAsync($"{Url}/{id}/issue", null);
                                                                                       return response.StatusCode;
                                                                                   })));

        codes.Count(c => c == HttpStatusCode.OK).ShouldBe(1);
        codes.Where(c => c != HttpStatusCode.OK).ShouldAllBe(c => c == HttpStatusCode.Conflict);
        (await (await env.Client.GetAsync(SeriesUrl)).JsonAsync())[0].GetProperty("nextPreview").GetString().ShouldBe("FTR2026000000002");
    }

    [Test]
    public async Task FailedIssue_ReturnsTheNumber() {
        var env = await NewEnv();
        await CreateSeries(env, "FTR");
        var warehouse = await PostingSupport.Warehouse(env);
        await PostingSupport.Stock(env, warehouse, 1m);
        var draft = await Create(env, Body(env, lines: [ProductLine(env, 4m, 50m)], extra: new { affectsStock = true, warehouseId = warehouse }));

        // Stok yetersiz: işlem geri alınır, numara harcanmaz.
        using var failed = await env.Client.PostAsync($"{Url}/{draft.GetProperty("id").GetGuid()}/issue", null);

        failed.IsSuccessStatusCode.ShouldBeFalse();
        (await (await env.Client.GetAsync(SeriesUrl)).JsonAsync())[0].GetProperty("nextPreview").GetString().ShouldBe("FTR2026000000001");
        (await (await env.Client.GetAsync($"{Url}/{draft.GetProperty("id").GetGuid()}")).JsonAsync()).GetProperty("status").GetString().ShouldBe("draft");
    }

    [Test]
    public async Task Series_DefaultIsBackedByAPartialUniqueIndex() {
        var env   = await NewEnv();
        var first = await CreateSeries(env, "ALS");
        await using var db = PricingTestSupport.Db(env.Tenant);
        db.DocumentSeries.Add(new DocumentSeries { KindGroup = "sales", BranchId = env.Branch, Mode = InvoiceMode.Paper, Prefix = "B", Year = 2026, IsDefault = true });

        var failure = await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync());

        failure.InnerException!.Message.ShouldContain("IX_document_series_default");
        first.GetProperty("isDefault").GetBoolean().ShouldBeTrue();
        // Varsayılan olmayan seriler çoğalabilir; pasif ve farklı mod/şube serbest.
        db.ChangeTracker.Clear();
        db.DocumentSeries.Add(new DocumentSeries { KindGroup = "sales", BranchId = env.Branch, Mode = InvoiceMode.Paper, Prefix = "B", Year = 2026, IsDefault = false });
        db.DocumentSeries.Add(new DocumentSeries { KindGroup = "sales", BranchId = env.Branch, Mode = InvoiceMode.EArchive, Prefix = "ABC", Year = 2026, IsDefault = true });
        await db.SaveChangesAsync();
    }

    [Test]
    public async Task Series_ParallelMakeDefault_LeavesExactlyOneDefault() {
        var env = await NewEnv();
        var ids = new List<Guid>();
        foreach (var prefix in new[] { "AAA", "BBB", "CCC", "DDD", "EEE" }) ids.Add((await CreateSeries(env, prefix)).GetProperty("id").GetGuid());

        var results = await Task.WhenAll(ids.Concat(ids).Select((id, i) => Task.Run(async () => {
                                                                                        var prefix = new string((char)('A' + i % 5), 3);
                                                                                        using var response = await env.Client.PutAsJsonAsync($"{SeriesUrl}/{id}",
                                                                                                                                             new { kindGroup = "sales", mode = "Paper", prefix, isDefault = true, isActive = true });
                                                                                        return (response.StatusCode, Code: response.IsSuccessStatusCode ? null : await response.ErrorCodeAsync());
                                                                                    })));

        results.Where(r => r.StatusCode != HttpStatusCode.OK).ShouldAllBe(r => r.StatusCode == HttpStatusCode.Conflict && r.Code == "series_default_conflict");
        results.Any(r => r.StatusCode == HttpStatusCode.OK).ShouldBeTrue();
        var list = await (await env.Client.GetAsync(SeriesUrl)).JsonAsync();
        list.EnumerateArray().Count(s => s.GetProperty("isDefault").GetBoolean()).ShouldBe(1);
    }
}
