using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Modaryo.Internal;

public static class ServiceCollectionExtensions {
    /// <summary>"Internal" yapılandırma bölümünü bağlar. Idempotency deposu servis tarafından ayrıca kaydedilir.</summary>
    public static IServiceCollection AddModaryoInternal(this IServiceCollection services,
                                                        IConfiguration          configuration
    ) {
        services.TryAddSingleton(TimeProvider.System);
        services.AddOptions<InternalOptions>().Bind(configuration.GetSection(InternalOptions.SectionName));
        return services;
    }

    /// <summary>
    ///     "/internal/*" yollarında imzayı doğrular ve idempotency uygular. Oturum kimlik doğrulamasından SONRA
    ///     eklenmelidir (oturum cookie'si olsa bile iç istekte yalnızca servis kimliği geçerlidir); CSRF
    ///     middleware'i bu yollar için atlanmalıdır (<see cref="InternalAuthMiddleware.IsInternalPath" />).
    /// </summary>
    public static IApplicationBuilder UseModaryoInternal(this IApplicationBuilder app) {
        return app.UseMiddleware<InternalAuthMiddleware>();
    }
}
