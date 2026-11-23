using ERP.Application.Common.Interfaces;
using ERP.Infrastructure.Data;
using ERP.Infrastructure.Data.Interceptors;
using ERP.Infrastructure.Internal;
using ERP.Infrastructure.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
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
        builder.Services.AddSingleton<IXlsxService, ERP.Infrastructure.Excel.XlsxService>();

        builder.Services.AddScoped<IdempotencyStore>();
        builder.Services.AddScoped<IInternalIdempotencyStore>(sp => sp.GetRequiredService<IdempotencyStore>());
        builder.Services.AddHostedService<IdempotencyPurgeService>();

        builder.Services.AddOptions<ERP.Infrastructure.Files.FileStorageOptions>()
            .Bind(builder.Configuration.GetSection(ERP.Infrastructure.Files.FileStorageOptions.Section));
        builder.Services.AddSingleton(sp => ERP.Infrastructure.Files.S3FileStorage.CreateClient(
                                          sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<ERP.Infrastructure.Files.FileStorageOptions>>().Value));
        builder.Services.AddSingleton<IFileStorage, ERP.Infrastructure.Files.S3FileStorage>();
        builder.Services.AddSingleton<IFileSniffer, ERP.Infrastructure.Files.FileSniffer>();

        builder.Services.AddScoped<IOutbox, Outbox>();
        builder.Services.AddScoped<IPeriodLock, ERP.Infrastructure.Settings.PeriodLock>();
        builder.Services.AddOptions<OutboxOptions>().Bind(builder.Configuration.GetSection(OutboxOptions.Section));
        builder.Services.AddSingleton(new RelayDatabase(connectionString));
        builder.Services.AddSingleton<IEventStreamPublisher>(sp => new ValkeyEventStreamPublisher(
                                                                 sp.GetRequiredService<IConnectionMultiplexer>()));
        builder.Services.AddSingleton<OutboxRelay>();
        builder.Services.AddSingleton(sp => new StoredAllocationsBackfill(
                                          sp.GetRequiredService<RelayDatabase>(),
                                          sp.GetRequiredService<TimeProvider>(),
                                          sp.GetRequiredService<ILogger<StoredAllocationsBackfill>>()));
        if (builder.Configuration.GetValue(OutboxOptions.Section + ":RelayEnabled", true))
            builder.Services.AddHostedService(sp => sp.GetRequiredService<OutboxRelay>());
    }
}