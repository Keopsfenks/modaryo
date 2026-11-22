using Npgsql;

namespace ERP.Web.FunctionalTests;

public class MigrationTests : TestBase
{
    [Test]
    public async Task Migration_CreatesTables()
    {
        await using var connection = new NpgsqlConnection(TestEnvironment.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT table_name FROM information_schema.tables WHERE table_schema = 'public'", connection);
        var tables = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            tables.Add(reader.GetString(0));
        }

        tables.ShouldContain("current_accounts");
        tables.ShouldContain("current_account_addresses");
        tables.ShouldContain("ledger_entries");
    }

    [Test]
    [Ignore("Task 4")]
    public void AppException_MapsToProblemWithCode()
    {
        // Task 4'teki Create_DuplicateCode_Returns409CodeTaken ile tamamlanır.
    }
}
