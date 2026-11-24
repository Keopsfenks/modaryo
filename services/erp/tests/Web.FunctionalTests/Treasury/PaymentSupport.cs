using System.Text.Json;
using ERP.Domain.CurrentAccounts;
using ERP.Domain.Treasury;
using ERP.Web.FunctionalTests.Invoicing;
using ERP.Web.FunctionalTests.Pricing;
using Microsoft.EntityFrameworkCore;
using static ERP.Web.FunctionalTests.Invoicing.InvoiceTestSupport;

namespace ERP.Web.FunctionalTests.Treasury;

/// <summary>Tahsilat, ödeme ve eşleştirme testleri için ortak yardımcılar: fatura ortamı + finans izinleri.</summary>
internal static class PaymentSupport {
    public const string Base = TreasuryTestSupport.Base;

    public static readonly DateOnly Today = new(2026, 3, 12);

    public static Task<Env> NewEnv() {
        return InvoiceTestSupport.NewEnv(AllPermissions.Concat(TreasuryTestSupport.Permissions).Distinct().ToArray());
    }

    public static async Task<Guid> Account(Env env, string name, string kind = "cash", string currency = "TRY") {
        var bank = kind != "cash";
        return (await TreasuryTestSupport.CreateAccount(env.Client, env.Branch, name, kind, currency, kind == "bank" ? TreasuryTestSupport.Iban : null, bank ? "Garanti" : null))
            .GetProperty("id").GetGuid();
    }

    /// <summary>KDV dahil tutarı verilen (KDV %20) tek hizmet satırlı, vade planı verilmezse cari vadesindeki fatura keser; faturanın id'sini ve numarasını döner.</summary>
    public static async Task<(Guid Id, string Number)> Invoice(Env env, decimal gross, DateOnly? date = null, string kind = "Sales", object[]? schedule = null, int? termDays = null) {
        var net   = Math.Round(gross / 1.2m, 2);
        var body = Body(env, kind, [FreeLine("Hizmet", 1m, net, 20)], null, date ?? new DateOnly(2026, 3, 1));
        if (schedule is not null) body["dueSchedule"] = schedule;
        if (termDays is not null) body["paymentTermDays"] = termDays;
        var issued = await CreateAndIssue(env, body);
        return (issued.GetProperty("id").GetGuid(), issued.GetProperty("number").GetString()!);
    }

    public static Task<HttpResponseMessage> Collect(Env env, Guid account, decimal amount, Guid? cari = null, string method = "cash", object? extra = null, DateOnly? date = null) {
        return Post(env, "collections", account, cari ?? env.Customer, amount, method, extra, date);
    }

    public static Task<HttpResponseMessage> Pay(Env env, Guid account, decimal amount, Guid? cari = null, string method = "cash", object? extra = null, DateOnly? date = null) {
        return Post(env, "payments", account, cari ?? env.Supplier, amount, method, extra, date);
    }

    private static Task<HttpResponseMessage> Post(Env env, string path, Guid account, Guid cari, decimal amount, string method, object? extra, DateOnly? date) {
        var body = new Dictionary<string, object?> {
            ["currentAccountId"] = cari, ["accountId"] = account, ["method"] = method, ["date"] = (date ?? Today).ToString("yyyy-MM-dd"), ["amount"] = amount
        };
        if (extra is not null)
            foreach (var property in extra.GetType().GetProperties())
                body[property.Name] = property.GetValue(extra);
        return env.Client.PostAsJsonAsync($"{Base}/{path}", body);
    }

    public static async Task<JsonElement> Created(HttpResponseMessage response) {
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return await response.JsonAsync();
    }

    public static async Task<Guid> CollectOk(Env env, Guid account, decimal amount, Guid? cari = null, object? extra = null, DateOnly? date = null) {
        using var response = await Collect(env, account, amount, cari, "cash", extra, date);
        return (await Created(response)).GetProperty("id").GetGuid();
    }

    public static async Task<Guid> PayOk(Env env, Guid account, decimal amount, Guid? cari = null, object? extra = null, DateOnly? date = null) {
        using var response = await Pay(env, account, amount, cari, "cash", extra, date);
        return (await Created(response)).GetProperty("id").GetGuid();
    }

    public static async Task<List<JsonElement>> OpenItems(Env env, Guid cari) {
        using var response = await env.Client.GetAsync($"{Base}/open-items?currentAccountId={cari}");
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.JsonAsync()).EnumerateArray().ToList();
    }

    public static async Task<Guid> LineOf(Env env, Guid cari, string documentNo, int sequence = 1) {
        var items = await OpenItems(env, cari);
        return items.First(i => i.GetProperty("documentNo").GetString() == documentNo && i.GetProperty("sequence").GetInt32() == sequence).GetProperty("dueLineId").GetGuid();
    }

    public static Task<HttpResponseMessage> Allocate(Env env, Guid? paymentId, Guid? creditLineId, params (Guid Line, decimal Amount)[] items) {
        return env.Client.PostAsJsonAsync($"{Base}/allocations", new {
            paymentId, creditLineId, allocations = items.Select(i => new { dueLineId = i.Line, amount = i.Amount }).ToArray()
        });
    }

    public static async Task AllocateOk(Env env, Guid? paymentId, Guid? creditLineId, params (Guid Line, decimal Amount)[] items) {
        using var response = await Allocate(env, paymentId, creditLineId, items);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
    }

    public static async Task<List<LedgerAllocation>> Allocations(Guid tenant, AllocationKind? kind = null) {
        await using var db = PricingTestSupport.Db(tenant);
        var query = db.LedgerAllocations.AsNoTracking().AsQueryable();
        if (kind is { } k) query = query.Where(a => a.Kind == k);
        return await query.ToListAsync();
    }

    public static async Task<List<LedgerEntry>> Entries(Guid tenant) {
        await using var db = PricingTestSupport.Db(tenant);
        return await db.LedgerEntries.AsNoTracking().OrderBy(e => e.Created).ToListAsync();
    }

    public static async Task<List<LedgerDueLine>> DueLines(Guid tenant) {
        await using var db = PricingTestSupport.Db(tenant);
        return await db.LedgerDueLines.AsNoTracking().ToListAsync();
    }

    public static async Task<List<FinanceDocument>> Documents(Guid tenant) {
        await using var db = PricingTestSupport.Db(tenant);
        return await db.FinanceDocuments.AsNoTracking().OrderBy(d => d.Created).ToListAsync();
    }

    public static async Task<List<TreasuryMovement>> Movements(Guid tenant) {
        await using var db = PricingTestSupport.Db(tenant);
        return await db.TreasuryMovements.AsNoTracking().ToListAsync();
    }

    public static async Task<decimal> AccountBalance(Env env, Guid account) {
        using var response = await env.Client.GetAsync($"{Base}/accounts/{account}");
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.JsonAsync()).GetProperty("balance").GetDecimal();
    }

    public static async Task<decimal> CariBalance(Env env, Guid cari) {
        using var response = await env.Client.GetAsync($"/api/erp/current-accounts/{cari}");
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.JsonAsync()).GetProperty("balance").GetDecimal();
    }

    public static async Task<List<JsonElement>> DueItems(Env env, Guid cari) {
        using var response = await env.Client.GetAsync($"/api/erp/current-accounts/{cari}/due-items");
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.JsonAsync()).EnumerateArray().ToList();
    }

    public static Task<HttpResponseMessage> CancelDocument(Env env, Guid id, string reason = "Hatalı giriş") {
        return env.Client.PostAsJsonAsync($"{Base}/finance-documents/{id}/cancel", new { reason });
    }

    public static Task<HttpResponseMessage> CancelInvoice(Env env, Guid id, string reason = "Hatalı fatura") {
        return env.Client.PostAsJsonAsync($"{Url}/{id}/cancel", new { reason });
    }
}
