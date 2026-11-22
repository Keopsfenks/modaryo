using ERP.Web.FunctionalTests.Infrastructure;
using Npgsql;
using StackExchange.Redis;

namespace ERP.Web.FunctionalTests;

/// <summary>
/// Test çalıştırması başında geçici bir veritabanı oluşturur, sonunda siler.
/// Ön koşul: infra/docker-compose.yml ile altyapı ayakta.
/// </summary>
[SetUpFixture]
public class TestEnvironment
{
    public const string ServerConnection = "Host=localhost;Port=5432;Username=modaryo;Password=modaryo_dev";

    private static string _databaseName = null!;

    public static ErpApiFactory Factory { get; private set; } = null!;

    public static string ConnectionString { get; private set; } = null!;

    [OneTimeSetUp]
    public async Task CreateEnvironment()
    {
        _databaseName = $"erp_test_{Guid.NewGuid():N}";
        await ExecuteOnServerAsync($"CREATE DATABASE \"{_databaseName}\"");

        ConnectionString = $"{ServerConnection};Database={_databaseName}";
        Factory = new ErpApiFactory(ConnectionString);
        _ = Factory.Server; // uygulamayı başlatır; geliştirme ortamında migration'lar uygulanır
    }

    [OneTimeTearDown]
    public async Task DropEnvironment()
    {
        await Factory.DisposeAsync();
        NpgsqlConnection.ClearAllPools();
        await ExecuteOnServerAsync($"DROP DATABASE IF EXISTS \"{_databaseName}\" WITH (FORCE)");
    }

    public static async Task ResetAsync()
    {
        await using var admin = await ConnectionMultiplexer.ConnectAsync($"{ErpApiFactory.ValkeyConnectionString},allowAdmin=true");
        await admin.GetServers().First().FlushDatabaseAsync(3);
    }

    public static async Task ExecuteOnServerAsync(string sql)
    {
        await using var connection = new NpgsqlConnection($"{ServerConnection};Database=postgres");
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}
