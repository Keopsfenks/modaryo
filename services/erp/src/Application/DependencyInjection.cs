using System.Reflection;
using ERP.Application.Common.Behaviours;
using Microsoft.Extensions.Hosting;

namespace Microsoft.Extensions.DependencyInjection;

public static class DependencyInjection {
    public static void AddApplicationServices(this IHostApplicationBuilder builder) {
        builder.Services.AddAutoMapper(cfg =>
                                           cfg.AddMaps(Assembly.GetExecutingAssembly()));

        builder.Services.AddScoped<ERP.Application.Settings.ITenantSettings, ERP.Application.Settings.TenantSettingsService>();
        builder.Services.AddScoped<ERP.Application.CurrentAccounts.Allocation.AutoAllocator>();
        builder.Services.AddScoped<ERP.Application.CurrentAccounts.Allocation.OpenItemsReader>();

        builder.Services.AddScoped<ERP.Application.Catalog.Products.BarcodeRegistry>();
        builder.Services.AddScoped<ERP.Application.Catalog.Products.ProductWriter>();
        builder.Services.AddScoped<ERP.Application.Common.Interfaces.ItemUsage>();

        builder.Services.AddValidatorsFromAssembly(Assembly.GetExecutingAssembly());

        builder.Services.AddMediatR(cfg => {
                                        cfg.RegisterServicesFromAssembly(Assembly.GetExecutingAssembly());
                                        cfg.AddOpenRequestPreProcessor(typeof(LoggingBehaviour<>));
                                        cfg.AddOpenBehavior(typeof(UnhandledExceptionBehaviour<,>));
                                        cfg.AddOpenBehavior(typeof(AuthorizationBehaviour<,>));
                                        cfg.AddOpenBehavior(typeof(ValidationBehaviour<,>));
                                        cfg.AddOpenBehavior(typeof(PerformanceBehaviour<,>));
                                    });
    }
}