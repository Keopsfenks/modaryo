using Azure.Identity;
using IdentityService.Application.Common.Interfaces;
using IdentityService.Infrastructure.Data;
using IdentityService.Web.Services;
using IdentityService.Web.Infrastructure;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;

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
