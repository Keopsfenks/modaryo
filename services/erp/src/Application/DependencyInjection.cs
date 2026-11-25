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
        builder.Services.AddScoped<ERP.Application.Common.Interfaces.IBranchUsageProvider, ERP.Application.Treasury.TreasuryBranchUsageProvider>();
        builder.Services.AddScoped<ERP.Application.Inventory.Warehouses.DefaultWarehouseService>();
        builder.Services.AddScoped<ERP.Application.Inventory.Ledger.StockLedger>();
        builder.Services.AddScoped<ERP.Application.Inventory.IStockService, ERP.Application.Inventory.Ledger.StockService>();
        builder.Services.AddScoped<ERP.Application.Inventory.Ledger.BalanceReader>();
        builder.Services.AddScoped<ERP.Application.Inventory.Tracking.TrackingValidator>();
        builder.Services.AddScoped<ERP.Application.Inventory.Costing.TenantScope>();
        builder.Services.AddScoped<ERP.Application.Invoicing.InvoiceBuilder>();
        builder.Services.AddScoped<ERP.Application.Invoicing.Series.SeriesResolver>();
        builder.Services.AddScoped<ERP.Application.Invoicing.Posting.CurrencyPosting>();
        builder.Services.AddScoped<ERP.Application.Invoicing.Posting.InvoicePoster>();
        builder.Services.AddScoped<ERP.Application.Purchasing.Invoices.CostCorrection>();
        builder.Services.AddScoped<ERP.Application.Purchasing.Invoices.ReceiptInvoiceGuard>();
        builder.Services.AddScoped<ERP.Application.Purchasing.LandedCosts.LandedCostMapper>();
        builder.Services.AddScoped<ERP.Application.Invoicing.Posting.IInvoicePostHook, ERP.Application.Purchasing.Invoices.PurchaseInvoiceHook>();
        builder.Services.AddScoped<ERP.Application.Purchasing.Invoices.PurchasePriceStatsReader>();
        builder.Services.AddScoped<ERP.Application.Invoicing.Posting.IInvoicePaymentGuard, ERP.Application.Treasury.InvoicePaymentGuard>();
        builder.Services.AddScoped<ERP.Application.Treasury.Allocations.ManualAllocator>();
        builder.Services.AddScoped<ERP.Application.Treasury.FinancePoster>();
        builder.Services.AddScoped<ERP.Application.Treasury.Vouchers.ExpensePoster>();
        builder.Services.AddScoped<ERP.Application.Treasury.Journal.JournalVoucherPoster>();
        builder.Services.AddScoped<ERP.Application.Treasury.ExchangeDifferences.ExchangeDifferenceReader>();
        builder.Services.AddScoped<ERP.Application.Treasury.Instruments.InstrumentPoster>();
        builder.Services.AddScoped<ERP.Application.Accounting.AccountingContext>();
        builder.Services.AddScoped<ERP.Application.Accounting.IJournalProjector, ERP.Application.Accounting.Projectors.InvoiceProjector>();
        builder.Services.AddScoped<ERP.Application.Accounting.IJournalProjector, ERP.Application.Accounting.Projectors.FinanceDocumentProjector>();
        builder.Services.AddScoped<ERP.Application.Accounting.IJournalProjector, ERP.Application.Accounting.Projectors.InstrumentProjector>();
        builder.Services.AddScoped<ERP.Application.Accounting.IJournalProjector, ERP.Application.Accounting.Projectors.JournalVoucherProjector>();
        builder.Services.AddScoped<ERP.Application.Purchasing.Orders.PoBuilder>();
        builder.Services.AddScoped<ERP.Application.Purchasing.Orders.PoMapper>();
        builder.Services.AddScoped<ERP.Application.Purchasing.Receipts.ReceiptMapper>();
        builder.Services.AddScoped<ERP.Application.Purchasing.Receipts.ReceiptLineStock>();
        builder.Services.AddScoped<ERP.Application.Purchasing.Returns.PurchaseReturnService>();
        builder.Services.AddScoped<ERP.Application.Inventory.Costing.CostCalculator>();
        builder.Services.AddScoped<ERP.Application.Inventory.Costing.IRevaluationRunner, ERP.Application.Inventory.Costing.RevaluationRunner>();
        builder.Services.AddScoped<ERP.Application.Catalog.Excel.ImportEngine>();
        builder.Services.AddScoped<ERP.Application.Inventory.Documents.LineResolver>();
        builder.Services.AddScoped<ERP.Application.Inventory.Excel.OpeningStockEngine>();

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