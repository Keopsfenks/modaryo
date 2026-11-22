using ERP.Application.Common.Interfaces;
using ERP.Application.Common.Security;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace ERP.Web.FunctionalTests.Platform;

/// <summary>Yalnızca testlerde kayıtlı MediatR istekleri ve uçları; üretim uçlarına dokunmadan korumaları sınar.</summary>
[RequireModule("pos")]
public sealed record ModuleProbe : IRequest<int>;

public sealed record BranchProbe(
    Guid BranchId
) : IRequest<int>;

public sealed record TenantProbe : IRequest<TenantProbeResult>;

public sealed record TenantProbeResult(
    Guid?  UserId,
    string? UserFullName,
    decimal MaxDiscountPercent,
    bool    HasPos,
    bool    HasSube
);

public sealed class ModuleProbeHandler : IRequestHandler<ModuleProbe, int> {
    public Task<int> Handle(ModuleProbe request, CancellationToken cancellationToken) {
        return Task.FromResult(1);
    }
}

public sealed class BranchProbeHandler(
    ICurrentTenant tenant
) : IRequestHandler<BranchProbe, int> {
    public Task<int> Handle(BranchProbe request, CancellationToken cancellationToken) {
        BranchGuard.Ensure(tenant, request.BranchId);
        return Task.FromResult(1);
    }
}

public sealed class TenantProbeHandler(
    ICurrentTenant tenant
) : IRequestHandler<TenantProbe, TenantProbeResult> {
    public Task<TenantProbeResult> Handle(TenantProbe request, CancellationToken cancellationToken) {
        return Task.FromResult(new TenantProbeResult(tenant.UserId, tenant.UserFullName, tenant.MaxDiscountPercent,
                                                     tenant.HasModule("pos"), tenant.HasModule("sube")));
    }
}

public static class GuardProbes {
    public static void Register(IServiceCollection services) {
        services.AddTransient<IRequestHandler<ModuleProbe, int>, ModuleProbeHandler>();
        services.AddTransient<IRequestHandler<BranchProbe, int>, BranchProbeHandler>();
        services.AddTransient<IRequestHandler<TenantProbe, TenantProbeResult>, TenantProbeHandler>();
        services.AddSingleton<IStartupFilter, ProbeStartupFilter>();
    }

    /// <summary>
    ///     Üretim hattının sonuna bağlanır: kimlik doğrulama ve istisna işleyici zaten çalışmış olur,
    ///     eşleşmeyen istekler bu dallara düşer.
    /// </summary>
    private sealed class ProbeStartupFilter : IStartupFilter {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) {
            return app => {
                       next(app);
                       app.Map("/test-probe/module", b => b.Run(async ctx => {
                                                                    await Sender(ctx).Send(new ModuleProbe());
                                                                    ctx.Response.StatusCode = 200;
                                                                }));
                       app.Map("/test-probe/branch", b => b.Run(async ctx => {
                                                                    var id = Guid.Parse(ctx.Request.Query["id"]!);
                                                                    await Sender(ctx).Send(new BranchProbe(id));
                                                                    ctx.Response.StatusCode = 200;
                                                                }));
                       app.Map("/test-probe/tenant", b => b.Run(async ctx => {
                                                                    var result = await Sender(ctx).Send(new TenantProbe());
                                                                    await ctx.Response.WriteAsJsonAsync(result);
                                                                }));
                   };
        }

        private static ISender Sender(HttpContext ctx) {
            return ctx.RequestServices.GetRequiredService<ISender>();
        }
    }
}
