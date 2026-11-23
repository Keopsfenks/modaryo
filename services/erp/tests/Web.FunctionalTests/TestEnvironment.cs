using Amazon.S3;
using Amazon.S3.Model;
using ERP.Infrastructure.Files;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;
using StackExchange.Redis;

namespace ERP.Web.FunctionalTests;

/// <summary>
///     Test çalıştırması başında geçici bir veritabanı oluşturur, sonunda siler.
///     Ön koşul: infra/docker-compose.yml ile altyapı ayakta.
/// </summary>
[SetUpFixture]
public class TestEnvironment {
    public const string ServerConnection = "Host=localhost;Port=5432;Username=modaryo;Password=modaryo_dev";

    private static string           _databaseName = null!;
    private static DatabaseResetter _resetter     = null!;

    public static ErpApiFactory Factory { get; private set; } = null!;

    public static string ConnectionString { get; private set; } = null!;

    [OneTimeSetUp]
    public async Task CreateEnvironment() {
        _databaseName = $"erp_test_{Guid.NewGuid():N}";
        await ExecuteOnServerAsync($"CREATE DATABASE \"{_databaseName}\"");

        ConnectionString = $"{ServerConnection};Database={_databaseName}";
        Factory          = new ErpApiFactory(ConnectionString);
        _                = Factory.Server; // uygulamayı başlatır; geliştirme ortamında migration'lar uygulanır
        _resetter        = await DatabaseResetter.CreateAsync(ConnectionString);
    }

    [OneTimeTearDown]
    public async Task DropEnvironment() {
        await DeleteTestFilesAsync();
        await _resetter.DisposeAsync();
        await Factory.DisposeAsync();
        NpgsqlConnection.ClearAllPools();
        await ExecuteOnServerAsync($"DROP DATABASE IF EXISTS \"{_databaseName}\" WITH (FORCE)");
    }

    /// <summary>Bu çalıştırmanın öneki altındaki tüm nesneleri siler.</summary>
    private static async Task DeleteTestFilesAsync() {
        var s3      = Factory.Services.GetRequiredService<IAmazonS3>();
        var options = Factory.Services.GetRequiredService<IOptions<FileStorageOptions>>().Value;
        string? token = null;
        do {
            var page = await s3.ListObjectsV2Async(new ListObjectsV2Request {
                                                                                BucketName        = options.Bucket,
                                                                                Prefix            = ErpApiFactory.FileKeyPrefix,
                                                                                ContinuationToken = token
                                                                            });
            foreach (var item in page.S3Objects ?? []) await s3.DeleteObjectAsync(options.Bucket, item.Key);

            token = page.IsTruncated == true ? page.NextContinuationToken : null;
        } while (token is not null);
    }

    public static async Task ResetAsync() {
        await _resetter.ResetAsync();
        await using var admin
            = await ConnectionMultiplexer.ConnectAsync($"{ErpApiFactory.ValkeyConnectionString},allowAdmin=true");
        await admin.GetServers().First().FlushDatabaseAsync(3);
        Factory.Branches.Clear();
    }

    public static async Task ExecuteOnServerAsync(string sql) {
        await using var connection = new NpgsqlConnection($"{ServerConnection};Database=postgres");
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}