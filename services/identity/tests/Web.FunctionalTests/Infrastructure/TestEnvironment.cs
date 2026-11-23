using Npgsql;
using StackExchange.Redis;

namespace IdentityService.Web.FunctionalTests;

/// <summary>
/// Test çalıştırması başında geçici bir veritabanı oluşturur, sonunda siler.
/// Ön koşul: infra/docker-compose.yml ile altyapı ayakta.
/// </summary>
[SetUpFixture]
public class TestEnvironment
{
    private const string ServerConnection = "Host=localhost;Port=5432;Username=modaryo;Password=modaryo_dev";

    private static string _databaseName = null!;
    private static DatabaseResetter? _resetter;

    public static IdentityApiFactory Factory { get; private set; } = null!;

    public static string ConnectionString { get; private set; } = null!;

    [OneTimeSetUp]
    public async Task CreateEnvironment()
    {
        _databaseName = $"identity_test_{Guid.NewGuid():N}";
        await ExecuteOnServerAsync($"CREATE DATABASE \"{_databaseName}\"");

        ConnectionString = $"{ServerConnection};Database={_databaseName}";
        Factory = new IdentityApiFactory(ConnectionString);
        _ = Factory.Server; // uygulamayı başlatır; geliştirme ortamında migration'lar uygulanır

        _resetter = await DatabaseResetter.CreateAsync(ConnectionString);
    }

    [OneTimeTearDown]
    public async Task DropEnvironment()
    {
        if (_resetter is not null)
        {
            await _resetter.DisposeAsync();
        }

        await Factory.DisposeAsync();
        NpgsqlConnection.ClearAllPools();
        await ExecuteOnServerAsync($"DROP DATABASE IF EXISTS \"{_databaseName}\" WITH (FORCE)");
    }

    public static async Task ResetAsync()
    {
        if (_resetter is not null)
        {
            await _resetter.ResetAsync();
        }

        await using var admin = await ConnectionMultiplexer.ConnectAsync($"{IdentityApiFactory.ValkeyConnectionString},allowAdmin=true");
        await admin.GetServers().First().FlushDatabaseAsync(2);

        Factory.Emails.Clear();
        Factory.Erp.Reset();
    }

    private static async Task ExecuteOnServerAsync(string sql)
    {
        await using var connection = new NpgsqlConnection($"{ServerConnection};Database=postgres");
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}
