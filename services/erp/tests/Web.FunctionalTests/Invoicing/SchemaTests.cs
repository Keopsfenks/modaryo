using Npgsql;

namespace ERP.Web.FunctionalTests.Invoicing;

public class SchemaTests : TestBase {
    private static async Task<NpgsqlConnection> OpenAsync() {
        var connection = new NpgsqlConnection(TestEnvironment.ConnectionString);
        await connection.OpenAsync();
        return connection;
    }

    private static async Task<long> ScalarAsync(NpgsqlConnection connection, string sql) {
        await using var command = new NpgsqlCommand(sql, connection);
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    [Test]
    public async Task Migration_CreatesInvoicingTablesAndLedgerColumns() {
        await using var connection = await OpenAsync();
        var             tables     = new List<string>();
        await using (var command = new NpgsqlCommand("SELECT table_name FROM information_schema.tables WHERE table_schema = 'public'", connection))
        await using (var reader = await command.ExecuteReaderAsync()) {
            while (await reader.ReadAsync()) tables.Add(reader.GetString(0));
        }

        foreach (var table in new[] { "invoices", "invoice_lines", "invoice_due_lines", "document_series", "withholding_codes", "exemption_codes", "exchange_rates" })
            tables.ShouldContain(table);

        var columns = await ScalarAsync(connection,
                                        "SELECT count(*) FROM information_schema.columns WHERE table_name = 'ledger_entries' AND column_name IN ('SourceCurrency','SourceAmount','Rate')");
        columns.ShouldBe(3);
    }

    [Test]
    public async Task Migration_SeedsCodeLists() {
        await using var connection = await OpenAsync();

        (await ScalarAsync(connection, "SELECT count(*) FROM withholding_codes")).ShouldBeGreaterThanOrEqualTo(20);
        (await ScalarAsync(connection, "SELECT count(*) FROM exemption_codes")).ShouldBeGreaterThanOrEqualTo(50);
        (await ScalarAsync(connection, "SELECT \"Tenths\" FROM withholding_codes WHERE \"Code\" = '601'")).ShouldBe(4);
        (await ScalarAsync(connection, "SELECT \"Tenths\" FROM withholding_codes WHERE \"Code\" = '602'")).ShouldBe(9);
        (await ScalarAsync(connection, "SELECT count(*) FROM exemption_codes WHERE \"Code\" IN ('201', '301', '351')")).ShouldBe(3);
    }

    [Test]
    public async Task Invoice_SupplierExternalNumber_IsUniquePerSupplier() {
        await using var connection = await OpenAsync();
        var             tenant     = Guid.CreateVersion7();
        var             supplier   = Guid.CreateVersion7();

        await using var db = ERP.Web.FunctionalTests.Pricing.PricingTestSupport.Db(tenant);
        db.CurrentAccounts.Add(new ERP.Domain.CurrentAccounts.CurrentAccount { Id = supplier, Code = "T1", Name = "Tedarikçi", TenantId = tenant });
        await db.SaveChangesAsync();
        db.Invoices.Add(NewPurchase(tenant, supplier, "A-1"));
        await db.SaveChangesAsync();

        db.Invoices.Add(NewPurchase(tenant, supplier, "A-1"));
        var ex = await Should.ThrowAsync<Microsoft.EntityFrameworkCore.DbUpdateException>(() => db.SaveChangesAsync());
        ERP.Application.Invoicing.InvoicingErrors.IsUniqueViolation(ex).ShouldBeTrue();
    }

    private static ERP.Domain.Invoicing.Invoice NewPurchase(Guid tenant, Guid supplier, string externalNumber) {
        return new ERP.Domain.Invoicing.Invoice {
            TenantId = tenant, Kind = ERP.Domain.Invoicing.InvoiceKind.Purchase, CurrentAccountId = supplier, BranchId = Guid.CreateVersion7(),
            Date     = new DateOnly(2026, 11, 24), ExternalNumber = externalNumber,
            BuyerOrSeller = new ERP.Domain.Invoicing.AddressSnapshot("Tedarikçi", null, null, null, null, null, null, null)
        };
    }
}
