using IdentityService.Application.Common.Interfaces;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace IdentityService.Web.FunctionalTests.Infrastructure;

/// <summary>
/// Kimlik servisini compose'daki PostgreSQL'de, test çalıştırmasına özel bir veritabanıyla ayağa kaldırır.
/// </summary>
public class IdentityApiFactory(
    string connectionString,
    IDictionary<string, string?>? extraSettings = null,
    Action<IServiceCollection>? configureServices = null) : WebApplicationFactory<Program>
{
    /// <summary>Kimlik servisi testleri Valkey'in 2 numaralı veritabanını kullanır (üretim 0, kütüphane testleri 1).</summary>
    public const string ValkeyConnectionString = "localhost:6379,defaultDatabase=2";

    /// <summary>Uygulamanın gönderdiği tüm e-postalar.</summary>
    public FakeEmailSender Emails { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting($"ConnectionStrings:{IdentityService.Shared.Services.Database}", connectionString);
        builder.UseSetting("ConnectionStrings:Valkey", ValkeyConnectionString);

        // Testler aynı "IP"den çok sayıda istek atar; sınırlar yalnızca bunu test eden factory'de açılır.
        builder.UseSetting("RateLimits:Enabled", "false");
        // Günlük temizlik işi testlerde arka planda çalışmasın; komutu testler doğrudan çağırır.
        builder.UseSetting("Maintenance:CleanupEnabled", "false");

        foreach (var (key, value) in extraSettings ?? new Dictionary<string, string?>())
        {
            builder.UseSetting(key, value);
        }

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IEmailSender>();
            services.AddSingleton<IEmailSender>(Emails);
            services.AddSingleton<IStartupFilter, LoopbackClientStartupFilter>();
            configureServices?.Invoke(services);
        });
    }

    /// <summary>
    /// TestServer istemci adresi vermez; gerçekte servis loopback'teki Next.js proxy'sinin arkasındadır.
    /// Bu filtre o durumu taklit eder ki X-Forwarded-For başlığı dikkate alınsın.
    /// </summary>
    private sealed class LoopbackClientStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((context, nextMiddleware) =>
            {
                context.Connection.RemoteIpAddress ??= IPAddress.Loopback;
                return nextMiddleware(context);
            });
            next(app);
        };
    }

    /// <summary>
    /// Cookie'leri saklayan ve CSRF başlığını gönderen bir istemci; her çağrı ayrı bir "tarayıcı" gibidir.
    /// </summary>
    public HttpClient CreateClientWithCookies()
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true, AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add("X-Modaryo-Csrf", "1");
        return client;
    }
}
