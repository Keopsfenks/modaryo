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
        builder.Services.AddScoped<ERP.Application.Pricing.PriceHistoryWriter>();
        builder.Services.AddScoped<ERP.Application.Pricing.Quotes.IPriceQuoteService, ERP.Application.Pricing.Quotes.PriceQuoteService>();
        builder.Services.AddScoped<ERP.Application.Pricing.Campaigns.ICampaignService, ERP.Application.Pricing.Campaigns.CampaignService>();
        builder.Services.AddScoped<ERP.Application.Common.Interfaces.ItemUsage>();
        builder.Services.AddScoped<ERP.Application.Common.Interfaces.IItemUsageProvider, ERP.Application.Inventory.Usage.InventoryItemUsageProvider>();
        builder.Services.AddScoped<ERP.Application.Common.Interfaces.IBranchUsageProvider, ERP.Application.Inventory.Usage.InventoryBranchUsageProvider>();
        builder.Services.AddScoped<ERP.Application.Inventory.Warehouses.DefaultWarehouseService>();
        builder.Services.AddScoped<ERP.Application.Inventory.Ledger.StockLedger>();
        builder.Services.AddScoped<ERP.Application.Inventory.IStockService, ERP.Application.Inventory.Ledger.StockService>();
        builder.Services.AddScoped<ERP.Application.Inventory.Ledger.BalanceReader>();
        builder.Services.AddScoped<ERP.Application.Catalog.Excel.ImportEngine>();

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