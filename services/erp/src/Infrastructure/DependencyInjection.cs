using ERP.Application.Common.Interfaces;
using ERP.Infrastructure.Data;
using ERP.Infrastructure.Data.Interceptors;
using ERP.Infrastructure.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Modaryo.Internal;

namespace Microsoft.Extensions.DependencyInjection;

public static class DependencyInjection {
    public static void AddInfrastructureServices(this IHostApplicationBuilder builder) {
        var connectionString = builder.Configuration.GetConnectionString(Services.Database);
        Guard.Against.Null(connectionString, message: $"Connection string '{Services.Database}' not found.");

        builder.Services.AddScoped<ISaveChangesInterceptor, AuditableEntityInterceptor>();
        builder.Services.AddScoped<ISaveChangesInterceptor, DispatchDomainEventsInterceptor>();
        builder.Services.AddScoped<ISaveChangesInterceptor, TenantInterceptor>();

        builder.Services.AddDbContext<ApplicationDbContext>((sp, options) => {
                                                                options.AddInterceptors(sp.GetServices<
                                                                    ISaveChangesInterceptor>());
                                                                options.UseNpgsql(connectionString);
                                                            });

        builder.Services.AddScoped<IApplicationDbContext>(provider =>
                                                              provider.GetRequiredService<ApplicationDbContext>());

        builder.Services.AddScoped<ApplicationDbContextInitialiser>();

        builder.Services.AddSingleton(TimeProvider.System);

        builder.Services.AddScoped<IdempotencyStore>();
        builder.Services.AddScoped<IInternalIdempotencyStore>(sp => sp.GetRequiredService<IdempotencyStore>());
        builder.Services.AddHostedService<IdempotencyPurgeService>();
    }
}