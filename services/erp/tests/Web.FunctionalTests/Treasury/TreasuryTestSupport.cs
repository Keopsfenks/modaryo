using System.Text.Json;
using ERP.Application.Common.Interfaces;
using ERP.Domain.Treasury;
using ERP.Web.FunctionalTests.Pricing;

namespace ERP.Web.FunctionalTests.Treasury;

internal static class TreasuryTestSupport {
    public const string Base = "/api/erp/treasury";
    public const string Iban = "TR330006100519786457841326";

    public static readonly string[] Permissions = [
        "finans.read", "finans.manage", "tahsilat.manage", "odeme.manage", "virman.manage", "masraf.manage", "ceksenet.manage",
        "muhasebe.manage", "muhasebe.export", "ayarlar.finans", "cari.read", "cari.manage", "cari.movements"
    ];

    public sealed record Env(Guid Tenant, Guid Branch, Guid OtherBranch, HttpClient Client);

    public static async Task<Env> NewEnv(string[]? permissions = null, Guid[]? branchIds = null, string[]? modules = null) {
        var tenant = Guid.CreateVersion7();
        var branch = Guid.CreateVersion7();
        var other  = Guid.CreateVersion7();
        TestEnvironment.Factory.Branches.Seed(tenant, new BranchRef(branch, "MRK", "Merkez", true, true), new BranchRef(other, "KDK", "Kadıköy", true, false));
        var client = await TestEnvironment.Factory.ClientAsync(tenant, modules, branchIds, null, permissions ?? Permissions);
        return new Env(tenant, branch, other, client);
    }

    public static async Task<JsonElement> CreateAccount(HttpClient client, Guid branch, string name, string kind = "cash", string currency = "TRY", string? iban = null,
                                                        string? bankName = null) {
        using var response = await client.PostAsJsonAsync($"{Base}/accounts", new { kind, name, currency, branchId = branch, iban, bankName });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return await response.JsonAsync();
    }

    /// <summary>Hareket yazan uçlar sonraki görevlerde gelir; şimdilik belge ve hareket doğrudan veritabanına yazılır.</summary>
    public static async Task AddMovementAsync(Guid tenant, Guid accountId, decimal amount, DateOnly date, string currency = "TRY") {
        await using var db = PricingTestSupport.Db(tenant);
        var document = new FinanceDocument {
            Kind = amount >= 0 ? FinanceDocumentKind.Income : FinanceDocumentKind.Expense, Number = "MF" + Guid.NewGuid().ToString("N")[..10], Date = date, AccountId = accountId,
            Amount = Math.Abs(amount), Currency = currency
        };
        db.FinanceDocuments.Add(document);
        db.TreasuryMovements.Add(new TreasuryMovement { AccountId = accountId, DocumentId = document.Id, Date = date, Amount = amount, Currency = currency });
        await db.SaveChangesAsync();
    }
}
