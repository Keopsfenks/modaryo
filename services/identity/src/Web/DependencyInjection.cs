using Azure.Identity;
using IdentityService.Application.Common.Interfaces;
using IdentityService.Infrastructure.Erp;
using IdentityService.Infrastructure.Data;
using IdentityService.Web.Services;
using IdentityService.Web.Infrastructure;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Modaryo.Internal;

namespace Microsoft.Extensions.DependencyInjection;

public static class DependencyInjection
{
    public static void AddWebServices(this IHostApplicationBuilder builder)
    {
        builder.Services.AddDatabaseDeveloperPageExceptionFilter();

        builder.Services.AddScoped<ICurrentSession, CurrentSession>();

        builder.Services.Configure<SessionCookieOptions>(builder.Configuration.GetSection(SessionCookieOptions.Section));
        builder.Services.AddSingleton<SessionCookie>();

        builder.Services.AddIdentityRateLimiting(builder.Configuration);

        // İstemci adresi (hız sınırı anahtarı) yalnızca güvenilir proxy'nin eklediği SAĞDAKİ X-Forwarded-For girdisinden alınır.
        // ForwardLimit = 1: istemcinin başa eklediği sahte girdiler yok sayılır. Güvenilir proxy'ler yapılandırmadan gelir:
        // ForwardedHeaders:KnownProxies (IP dizisi) ve ForwardedHeaders:KnownNetworks (CIDR dizisi); varsayılan yalnızca loopback.
        // Dağıtımda Next.js'in önünde X-Forwarded-For'u ezen/ekleyen bir uç (nginx, yük dengeleyici) bulunmalıdır.
        var forwarded = builder.Configuration.GetSection("ForwardedHeaders");
        builder.Services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.ForwardLimit = 1;
            foreach (var proxy in forwarded.GetSection("KnownProxies").Get<string[]>() ?? [])
            {
                options.KnownProxies.Add(System.Net.IPAddress.Parse(proxy));
            }
            foreach (var network in forwarded.GetSection("KnownNetworks").Get<string[]>() ?? [])
            {
                var parts = network.Split('/');
                options.KnownIPNetworks.Add(new System.Net.IPNetwork(System.Net.IPAddress.Parse(parts[0]), int.Parse(parts[1])));
            }
        });

        builder.Services.AddHttpContextAccessor();

        // İç API (/internal/*): imzalı servis çağrıları. Kimlik yalnızca GET sunar; idempotency deposu gerekmez.
        builder.Services.AddModaryoInternal(builder.Configuration);

        // ERP'ye giden imzalı istemci: Internal:Outbound:Erp:{BaseUrl,ServiceName,Secret}. Şube silmeden önce kullanım sorulur.
        builder.Services.AddHttpClient<IErpBranchUsageClient, ErpBranchUsageClient>((sp, client) =>
            {
                var section = sp.GetRequiredService<IConfiguration>().GetSection("Internal:Outbound:Erp");
                var baseUrl = section["BaseUrl"];
                if (!string.IsNullOrWhiteSpace(baseUrl))
                {
                    client.BaseAddress = new Uri(baseUrl);
                }

                client.Timeout = TimeSpan.FromSeconds(5);
            })
            .AddHttpMessageHandler(sp =>
            {
                var section = sp.GetRequiredService<IConfiguration>().GetSection("Internal:Outbound:Erp");
                return new InternalHttpClientHandler(section["ServiceName"] ?? "identity", section["Secret"] ?? "");
            });

        builder.Services.AddExceptionHandler<ProblemDetailsExceptionHandler>();

        // Customise default API behaviour
        builder.Services.Configure<ApiBehaviorOptions>(options =>
            options.SuppressModelStateInvalidFilter = true);

        builder.Services.AddEndpointsApiExplorer();

        builder.Services.AddOpenApi(options =>
        {
            options.AddOperationTransformer<ApiExceptionOperationTransformer>();
        });

    }

    public static void AddKeyVaultIfConfigured(this IHostApplicationBuilder builder)
    {
        var keyVaultUri = builder.Configuration["AZURE_KEY_VAULT_ENDPOINT"];
        if (!string.IsNullOrWhiteSpace(keyVaultUri))
        {
            builder.Configuration.AddAzureKeyVault(
                new Uri(keyVaultUri),
                new DefaultAzureCredential());
        }
    }
}
