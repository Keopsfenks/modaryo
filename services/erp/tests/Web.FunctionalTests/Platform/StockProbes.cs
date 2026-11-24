using System.Text.Json;
using System.Text.Json.Serialization;
using ERP.Application.Common.Interfaces;
using ERP.Application.Inventory;
using ERP.Application.Inventory.Ledger;
using ERP.Domain.Inventory;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace ERP.Web.FunctionalTests.Platform;

/// <summary>
///     Yalnızca testlerde: IStockService için üretim ucu yok (sonraki görevlerin belgeleri çağırır). Bu uçlar gerçek bir
///     çağıran gibi işlem açar, servisi çağırır, kaydeder ve onaylar; her istek kendi kapsam ve bağlantısını kullanır.
/// </summary>
public static class StockProbes {
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) {
        Converters = { new JsonStringEnumConverter() }
    };

    public static void Register(IServiceCollection services) {
        services.AddSingleton<IStartupFilter, ProbeStartupFilter>();
    }

    private sealed class ProbeStartupFilter : IStartupFilter {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) {
            return app => {
                       next(app);
                       app.Map("/test-probe/stock", b => b.Run(Handle));
                   };
        }
    }

    private sealed record Doc(string SourceType, Guid SourceId, DateOnly Date, MovementType Type, List<StockLine>? Lines);

    private sealed record ReserveDoc(string SourceType, Guid SourceId, List<ReserveLine> Lines);

    private sealed record ReleaseDoc(string SourceType, Guid SourceId, Guid? SourceLineId);

    private sealed record ConsumeDoc(string SourceType, Guid SourceId, Guid SourceLineId, decimal BaseQuantity,
                                     string? MovementSourceType, Guid? MovementSourceId, DateOnly? Date, bool Exempt);

    private sealed record RetargetDoc(Guid ExpectedReceiptSourceId, Guid WarehouseId, decimal BaseQuantity, Guid? ProductId, Guid? VariantId);

    private sealed record CorrectDoc(Guid WarehouseId, Guid ProductId, Guid? VariantId, DateOnly Date, Guid InboundMovementId,
                                     decimal DeltaTotalTl, string SourceType, Guid SourceId);

    private sealed record BalanceDoc(List<BalanceItem> Items, Guid? WarehouseId);

    private sealed record BalanceItem(Guid ProductId, Guid? VariantId);

    private static async Task Handle(HttpContext ctx) {
        var ct   = ctx.RequestAborted;
        var op   = ctx.Request.Query["op"].ToString();
        var body = await JsonSerializer.DeserializeAsync<JsonElement>(ctx.Request.Body, Options, ct);
        var sp   = ctx.RequestServices;
        var db   = sp.GetRequiredService<IApplicationDbContext>();
        var svc  = sp.GetRequiredService<IStockService>();

        T Read<T>() {
            return body.Deserialize<T>(Options)!;
        }

        if (op == "balance") {
            var doc = Read<BalanceDoc>();
            var rows = await sp.GetRequiredService<BalanceReader>()
                               .ForItemsAsync(doc.Items.Select(i => (i.ProductId, i.VariantId)).ToList(), doc.WarehouseId, ct);
            await ctx.Response.WriteAsJsonAsync(rows, Options, ct);
            return;
        }

        await using var transaction = await db.BeginTransactionAsync(ct);
        switch (op) {
            case "receive": {
                var d = Read<Doc>();
                await svc.ReceiveAsync(d.Type, d.SourceType, d.SourceId, d.Date, d.Lines!, ct);
                break;
            }
            case "issue": {
                var d = Read<Doc>();
                await svc.IssueAsync(d.Type, d.SourceType, d.SourceId, d.Date, d.Lines!, ct);
                break;
            }
            case "reverse": {
                var d = Read<Doc>();
                await svc.ReverseAsync(d.SourceType, d.SourceId, d.Date, ct);
                break;
            }
            case "reserve": {
                var d = Read<ReserveDoc>();
                await svc.ReserveAsync(d.SourceType, d.SourceId, d.Lines, ct);
                break;
            }
            case "release": {
                var d = Read<ReleaseDoc>();
                await svc.ReleaseAsync(d.SourceType, d.SourceId, d.SourceLineId, ct);
                break;
            }
            case "consume": {
                var d = Read<ConsumeDoc>();
                await svc.ConsumeAsync(d.SourceType, d.SourceId, d.SourceLineId, d.BaseQuantity, ct, d.MovementSourceType,
                                       d.MovementSourceId, d.Date, d.Exempt);
                break;
            }
            case "restore": {
                var d = Read<ConsumeDoc>();
                await svc.RestoreConsumedAsync(d.SourceType, d.SourceId, d.SourceLineId, d.BaseQuantity, ct);
                break;
            }
            case "retarget": {
                var d = Read<RetargetDoc>();
                await svc.RetargetExpectedAsync(d.ExpectedReceiptSourceId, d.WarehouseId, d.BaseQuantity, ct, d.ProductId, d.VariantId);
                break;
            }
            case "correct": {
                var d = Read<CorrectDoc>();
                await svc.CorrectCostAsync(d.WarehouseId, d.ProductId, d.VariantId, d.Date, d.InboundMovementId, d.DeltaTotalTl,
                                           d.SourceType, d.SourceId, ct);
                break;
            }
            default: throw new InvalidOperationException("Bilinmeyen işlem: " + op);
        }

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        ctx.Response.StatusCode = 200;
    }
}
