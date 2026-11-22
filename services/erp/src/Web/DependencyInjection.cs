using Azure.Identity;
using ERP.Application.Common.Interfaces;
using ERP.Infrastructure.Data;
using ERP.Web.Services;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Modaryo.Sessions;

namespace Microsoft.Extensions.DependencyInjection;

public static class DependencyInjection
{
    public static void AddWebServices(this IHostApplicationBuilder builder)
    {
        builder.Services.AddDatabaseDeveloperPageExceptionFilter();

        builder.Services.AddScoped<CurrentSession>();
        builder.Services.AddScoped<ICurrentSession>(sp => sp.GetRequiredService<CurrentSession>());
        builder.Services.AddScoped<ICurrentTenant>(sp => sp.GetRequiredService<CurrentSession>());

        var valkeyConnectionString = builder.Configuration.GetConnectionString("Valkey");
        Guard.Against.NullOrWhiteSpace(valkeyConnectionString, message: "Connection string 'Valkey' not found.");
        builder.Services.AddModaryoSessions(valkeyConnectionString);
        builder.Services.AddAuthorization();

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
