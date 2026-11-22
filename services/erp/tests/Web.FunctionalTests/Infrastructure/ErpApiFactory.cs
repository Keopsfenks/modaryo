using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace ERP.Web.FunctionalTests.Infrastructure;

/// <summary>ERP servisini compose'daki PostgreSQL'de, test çalıştırmasına özel bir veritabanıyla ayağa kaldırır.</summary>
public class ErpApiFactory(
    string connectionString
) : WebApplicationFactory<Program> {
    /// <summary>ERP testleri Valkey'in 3 numaralı veritabanını kullanır (üretim 0, kütüphane 1, kimlik 2).</summary>
    public const string ValkeyConnectionString = "localhost:6379,defaultDatabase=3";

    protected override void ConfigureWebHost(IWebHostBuilder builder) {
        builder.UseEnvironment("Development");
        builder.UseSetting($"ConnectionStrings:{Shared.Services.Database}", connectionString);
        builder.UseSetting("ConnectionStrings:Valkey",                      ValkeyConnectionString);
        Platform.InternalProbes.Configure(builder);
        builder.ConfigureTestServices(services => {
                                          services.AddSingleton<IStartupFilter, LoopbackClientStartupFilter>();
                                          Platform.GuardProbes.Register(services);
                                          Platform.InternalProbes.Register(services);
                                      });
    }

    /// <summary>Verilen oturum kimliğini cookie olarak gönderen, CSRF başlığı ekli istemci.</summary>
    public HttpClient CreateClientWithSession(string? sid) {
        var client = CreateClient(new WebApplicationFactoryClientOptions
                                  { HandleCookies = false, AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add("X-Modaryo-Csrf", "1");
        if (sid is not null) client.DefaultRequestHeaders.Add("Cookie", $"modaryo_sid={sid}");

        return client;
    }

    private sealed class LoopbackClientStartupFilter : IStartupFilter {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) {
            return app => {
                       app.Use((context, nextMiddleware) => {
                                   context.Connection.RemoteIpAddress ??= IPAddress.Loopback;
                                   return nextMiddleware(context);
                               });
                       next(app);
                   };
        }
    }
}