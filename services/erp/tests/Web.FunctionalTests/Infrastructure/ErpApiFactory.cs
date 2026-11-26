using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using ERP.Application.Common.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ERP.Web.FunctionalTests.Infrastructure;

/// <summary>ERP servisini compose'daki PostgreSQL'de, test çalıştırmasına özel bir veritabanıyla ayağa kaldırır.</summary>
public class ErpApiFactory(
    string connectionString
) : WebApplicationFactory<Program> {
    /// <summary>ERP testleri Valkey'in 3 numaralı veritabanını kullanır (üretim 0, kütüphane 1, kimlik 2).</summary>
    public const string ValkeyConnectionString = "localhost:6379,defaultDatabase=3";

    /// <summary>Bu test çalıştırmasının nesne deposu öneki; teardown'da altındaki her şey silinir.</summary>
    public static readonly string FileKeyPrefix = $"test/{Guid.NewGuid():N}/";

    /// <summary>Kimlik servisinin şube dizininin yerine geçen, testin doldurduğu sahte dizin.</summary>
    public FakeBranchDirectory Branches { get; } = new();

    /// <summary>Kimlik servisinin üye dizininin yerine geçen, testin doldurduğu sahte dizin.</summary>
    public FakeMemberDirectory Members { get; } = new();

    /// <summary>Başka modüllerin kalem kullanımını taklit eden sahte sağlayıcı.</summary>
    public FakeItemUsageProvider ItemUsage { get; } = new();

    /// <summary>TCMB'nin yerine geçen sahte kaynak: gerçek siteye hiçbir test gitmez.</summary>
    public FakeTcmbSource Tcmb { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder) {
        builder.UseEnvironment("Development");
        builder.UseSetting($"ConnectionStrings:{Shared.Services.Database}", connectionString);
        builder.UseSetting("ConnectionStrings:Valkey",                      ValkeyConnectionString);
        builder.UseSetting("Files:S3:KeyPrefix", FileKeyPrefix);
        builder.UseSetting("Inventory:RevaluationEnabled", "false"); // testler PublishPendingAsync / PublicRunOnceAsync çağırır
        builder.UseSetting("Rates:FetchEnabled", "false"); // testler sahte TCMB kaynağını POST /exchange-rates/fetch ile çalıştırır
        builder.UseSetting("Sales:JobsEnabled", "false"); // testler QuoteExpiryJob.PublicRunOnceAsync çağırır
        builder.UseSetting("Outbox:RelayEnabled", "false"); // testler PublishPendingAsync'i kendileri çağırır
        Platform.InternalProbes.Configure(builder);
        builder.ConfigureTestServices(services => {
                                          services.AddSingleton<IStartupFilter, LoopbackClientStartupFilter>();
                                          Platform.GuardProbes.Register(services);
                                          Platform.InternalProbes.Register(services);
                                          Platform.StockProbes.Register(services);
                                          services.RemoveAll<IBranchDirectory>();
                                          services.AddSingleton<IBranchDirectory>(Branches);
                                          services.RemoveAll<IMemberDirectory>();
                                          services.AddSingleton<IMemberDirectory>(Members);
                                          services.AddSingleton<IItemUsageProvider>(ItemUsage);
                                          services.RemoveAll<ITcmbRateSource>();
                                          services.AddSingleton<ITcmbRateSource>(Tcmb);
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