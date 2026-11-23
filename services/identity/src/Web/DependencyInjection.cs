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

        builder.Services.Configure<ForwardedHeadersOptions>(options =>
        {
            // Varsayılan KnownProxies yalnızca loopback: geliştirmede Next.js proxy aynı makinede çalışır.
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
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
