using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.Identity;
using ERP.Application.Common.Interfaces;
using ERP.Infrastructure.Identity;
using ERP.Web.Services;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Modaryo.Internal;
using Modaryo.Sessions;

namespace Microsoft.Extensions.DependencyInjection;

public static class DependencyInjection {
    public static void AddWebServices(this IHostApplicationBuilder builder) {
        builder.Services.AddDatabaseDeveloperPageExceptionFilter();

        builder.Services.AddScoped<CurrentSession>();
        builder.Services.AddScoped<ICurrentSession>(sp => sp.GetRequiredService<CurrentSession>());
        builder.Services.AddScoped<IInternalRequest, InternalRequest>();
        builder.Services.AddScoped<ICurrentTenant>(sp => sp.GetRequiredService<ERP.Application.Inventory.Costing.TenantScope>().Override
                                                                     ?? sp.GetRequiredService<CurrentSession>());

        var valkeyConnectionString = builder.Configuration.GetConnectionString("Valkey");
        Guard.Against.NullOrWhiteSpace(valkeyConnectionString, message: "Connection string 'Valkey' not found.");
        builder.Services.AddModaryoSessions(valkeyConnectionString);
        builder.Services.AddAuthorization();
        builder.Services.AddModaryoInternal(builder.Configuration);

        // Şubeler kimlik servisinde yaşar: imzalı iç API istemcisi (Internal:Outbound:Identity:{BaseUrl,ServiceName,Secret}).
        builder.Services.AddMemoryCache();
        builder.Services.AddHttpClient<IBranchDirectory, IdentityBranchClient>((sp, client) => {
                                                                                   var section = sp.GetRequiredService<IConfiguration>().GetSection("Internal:Outbound:Identity");
                                                                                   var baseUrl = section["BaseUrl"];
                                                                                   if (!string.IsNullOrWhiteSpace(baseUrl)) client.BaseAddress = new Uri(baseUrl);
                                                                                   client.Timeout = TimeSpan.FromSeconds(5);
                                                                               })
            .AddHttpMessageHandler(sp => {
                                       var section = sp.GetRequiredService<IConfiguration>().GetSection("Internal:Outbound:Identity");
                                       return new InternalHttpClientHandler(section["ServiceName"] ?? "erp", section["Secret"] ?? "");
                                   });

        builder.Services.AddHttpClient<IMemberDirectory, IdentityMemberClient>((sp, client) => {
                                                                                   var section = sp.GetRequiredService<IConfiguration>().GetSection("Internal:Outbound:Identity");
                                                                                   var baseUrl = section["BaseUrl"];
                                                                                   if (!string.IsNullOrWhiteSpace(baseUrl)) client.BaseAddress = new Uri(baseUrl);
                                                                                   client.Timeout = TimeSpan.FromSeconds(5);
                                                                               })
            .AddHttpMessageHandler(sp => {
                                       var section = sp.GetRequiredService<IConfiguration>().GetSection("Internal:Outbound:Identity");
                                       return new InternalHttpClientHandler(section["ServiceName"] ?? "erp", section["Secret"] ?? "");
                                   });

        builder.Services.Configure<ForwardedHeadersOptions>(options => {
                                                                // Varsayılan KnownProxies yalnızca loopback: geliştirmede Next.js proxy aynı makinede çalışır.
                                                                options.ForwardedHeaders
                                                                    = ForwardedHeaders.XForwardedFor |
                                                                      ForwardedHeaders.XForwardedProto;
                                                            });

        builder.Services.AddHttpContextAccessor();

        // Enum'lar JSON'da camelCase string ("corporate", "opening").
        builder.Services.ConfigureHttpJsonOptions(options =>
                                                      options.SerializerOptions.Converters
                                                          .Add(new JsonStringEnumConverter(JsonNamingPolicy
                                                                   .CamelCase)));

        builder.Services.AddExceptionHandler<ProblemDetailsExceptionHandler>();

        // Customise default API behaviour
        builder.Services.Configure<ApiBehaviorOptions>(options =>
                                                           options.SuppressModelStateInvalidFilter = true);

        builder.Services.AddEndpointsApiExplorer();

        builder.Services.AddOpenApi(options => {
                                        options.AddOperationTransformer<ApiExceptionOperationTransformer>();
                                    });
    }

    public static void AddKeyVaultIfConfigured(this IHostApplicationBuilder builder) {
        var keyVaultUri = builder.Configuration["AZURE_KEY_VAULT_ENDPOINT"];
        if (!string.IsNullOrWhiteSpace(keyVaultUri))
            builder.Configuration.AddAzureKeyVault(
                                                   new Uri(keyVaultUri),
                                                   new DefaultAzureCredential());
    }
}