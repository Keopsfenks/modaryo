using IdentityService.Application.Common.Interfaces;
using IdentityService.Infrastructure.Data;
using IdentityService.Infrastructure.Data.Interceptors;
using IdentityService.Infrastructure.Email;
using IdentityService.Infrastructure.Identity;
using IdentityService.Infrastructure.Maintenance;
using IdentityService.Infrastructure.Security;
using IdentityService.Infrastructure.Sessions;
using Modaryo.Sessions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Microsoft.Extensions.DependencyInjection;

public static class DependencyInjection
{
    public static void AddInfrastructureServices(this IHostApplicationBuilder builder)
    {
        var connectionString = builder.Configuration.GetConnectionString(Services.Database);
        Guard.Against.Null(connectionString, message: $"Connection string '{Services.Database}' not found.");

        builder.Services.AddScoped<ISaveChangesInterceptor, AuditableEntityInterceptor>();
        builder.Services.AddScoped<ISaveChangesInterceptor, DispatchDomainEventsInterceptor>();

        builder.Services.AddDbContext<ApplicationDbContext>((sp, options) =>
        {
            options.AddInterceptors(sp.GetServices<ISaveChangesInterceptor>());
            options.UseNpgsql(connectionString);
        });

        builder.Services.AddScoped<IApplicationDbContext>(provider => provider.GetRequiredService<ApplicationDbContext>());

        builder.Services.AddScoped<ApplicationDbContextInitialiser>();
        builder.Services.AddScoped<SystemRolePermissionSync>();

        builder.Services.AddAuthorizationBuilder();

        builder.Services.AddModaryoIdentity(builder.Configuration);
        builder.Services.AddScoped<IUserAccounts, UserAccounts>();
        builder.Services.AddScoped<IAuditLog, IdentityService.Infrastructure.Audit.AuditLog>();

        builder.Services.AddDataProtection()
            .SetApplicationName("Modaryo.Identity")
            .PersistKeysToDbContext<ApplicationDbContext>();

        builder.Services.AddSingleton(TimeProvider.System);

        var valkeyConnectionString = builder.Configuration.GetConnectionString("Valkey");
        Guard.Against.NullOrWhiteSpace(valkeyConnectionString, message: "Connection string 'Valkey' not found.");
        builder.Services.AddModaryoSessions(valkeyConnectionString);
        builder.Services.AddSingleton<ISessionStore, SessionStore>();
        builder.Services.AddSingleton<IAttemptLimiter, AttemptLimiter>();

        builder.Services.Configure<SmtpOptions>(builder.Configuration.GetSection(SmtpOptions.Section));
        builder.Services.Configure<AppOptions>(builder.Configuration.GetSection(AppOptions.Section));
        builder.Services.Configure<IdentityService.Application.Public.Commands.SiteOptions>(builder.Configuration.GetSection(IdentityService.Application.Public.Commands.SiteOptions.Section));
        builder.Services.AddSingleton<IEmailSender, SmtpEmailSender>();
        builder.Services.AddSingleton<IEmailComposer, EmailComposer>();

        builder.Services.AddHostedService<AccountCleanupWorker>();
    }
}
