using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StackExchange.Redis;

namespace Modaryo.Sessions;

public static class ServiceCollectionExtensions {
    /// <summary>
    ///     Valkey bağlantısını, oturum okuyucusunu ve "ModaryoSession" kimlik doğrulama şemasını (varsayılan olarak) kaydeder.
    /// </summary>
    public static AuthenticationBuilder AddModaryoSessions(this IServiceCollection               services,
                                                           string                                valkeyConnectionString,
                                                           Action<SessionAuthenticationOptions>? configure = null
    ) {
        services.TryAddSingleton<IConnectionMultiplexer>(_ => {
                                                             var options
                                                                 = ConfigurationOptions.Parse(valkeyConnectionString);
                                                             options.AbortOnConnectFail = false;
                                                             return ConnectionMultiplexer.Connect(options);
                                                         });
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<SessionReader>();

        return services
            .AddAuthentication(SessionAuthenticationDefaults.Scheme)
            .AddScheme<SessionAuthenticationOptions, SessionAuthenticationHandler>(SessionAuthenticationDefaults.Scheme,
                options => configure?.Invoke(options));
    }

    public static IApplicationBuilder UseModaryoCsrf(this IApplicationBuilder app) {
        return app.UseMiddleware<CsrfHeaderMiddleware>();
    }
}