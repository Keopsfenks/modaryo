using ERP.Application.Common.Interfaces;
using ERP.Infrastructure.Data;
using ERP.Infrastructure.Internal;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Modaryo.Internal;

namespace ERP.Web.FunctionalTests.Platform;

/// <summary>
///     Yalnızca testlerde kayıtlı iç API uçları ve test çağıranı yapılandırması. Üretim kodunda test yolu yoktur.
///     Hat sonuna bağlanır: iç API middleware'i zaten çalışmış olur.
/// </summary>
public static class InternalProbes {
    public const string Secret        = "test-secret-0123456789";
    public const string OtherSecret   = "other-secret-0123456789";
    public const string TestCaller    = "test";
    public const string LimitedCaller = "limited";

    /// <summary>Kimlik servisinin ERP'ye çağıran olarak tanımı (şube kullanım ucu).</summary>
    public const string IdentityCaller = "identity";

    public const string IdentitySecret = "identity-secret-0123456789";

    /// <summary>İK servisi (mahsup fişi iç API'si).</summary>
    public const string HrCaller = "hr";

    public const string HrSecret = "hr-secret-0123456789";

    public const string AssetsCaller = "assets";

    public const string AssetsSecret = "assets-secret-0123456789";

    private static int _counter;
    private static int _flaky;

    public static int Counter => Volatile.Read(ref _counter);

    /// <summary>"slow" ucu: ilk istek buraya girince set edilir, ikinci istek gönderilebilir.</summary>
    public static TaskCompletionSource SlowEntered { get; private set; } = new();

    public static TaskCompletionSource SlowRelease { get; private set; } = new();

    public static void Reset() {
        Volatile.Write(ref _counter, 0);
        Volatile.Write(ref _flaky,   0);
        FailLateComplete = false;
        LoseOwnership    = false;
        SlowEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        SlowRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public static void Configure(IWebHostBuilder builder) {
        builder.UseSetting($"Internal:Callers:{TestCaller}:Secret",   Secret);
        builder.UseSetting($"Internal:Callers:{TestCaller}:Routes:0", "/internal/ping");
        builder.UseSetting($"Internal:Callers:{TestCaller}:Routes:1", "/internal/test/");
        builder.UseSetting($"Internal:Callers:{IdentityCaller}:Secret",   IdentitySecret);
        builder.UseSetting($"Internal:Callers:{IdentityCaller}:Routes:0", "/internal/branches/");
        builder.UseSetting($"Internal:Callers:{HrCaller}:Secret",   HrSecret);
        builder.UseSetting($"Internal:Callers:{HrCaller}:Routes:0", "/internal/ping");
        builder.UseSetting($"Internal:Callers:{HrCaller}:Routes:1", "/internal/journal-vouchers");
        builder.UseSetting($"Internal:Callers:{AssetsCaller}:Secret",   AssetsSecret);
        builder.UseSetting($"Internal:Callers:{AssetsCaller}:Routes:0", "/internal/ping");
        builder.UseSetting($"Internal:Callers:{AssetsCaller}:Routes:1", "/internal/journal-vouchers");
        builder.UseSetting($"Internal:Callers:{LimitedCaller}:Secret",   OtherSecret);
        builder.UseSetting($"Internal:Callers:{LimitedCaller}:Routes:0", "/internal/ping");
    }

    public static void Register(IServiceCollection services) {
        services.AddSingleton<IStartupFilter, ProbeStartupFilter>();
        // İdempotency deposunu sarar: tamamlama çağrılarını (işlemde mi, sonuç) kaydeder ve istenirse middleware'in geç çağrısını bozar.
        services.RemoveAll<IInternalIdempotencyStore>();
        services.AddScoped<IInternalIdempotencyStore>(sp => new RecordingIdempotencyStore(sp.GetRequiredService<IdempotencyStore>(), sp.GetRequiredService<ApplicationDbContext>()));
    }

    public sealed record CompleteCall(string Key, bool InTransaction, bool Result);

    private static readonly List<CompleteCall> CompleteLog = [];

    /// <summary>true ise bir anahtarın ikinci ve sonraki CompleteAsync çağrıları (middleware'in geç çağrısı) istisna fırlatır.</summary>
    public static volatile bool FailLateComplete;

    /// <summary>true ise ilk CompleteAsync çağrısı kaydı tamamlamadan false döner (kayıt başkasına devredilmiş gibi).</summary>
    public static volatile bool LoseOwnership;

    public static IReadOnlyList<CompleteCall> CompleteCalls(string key) {
        lock (CompleteLog) return CompleteLog.Where(c => c.Key == key).ToList();
    }

    private sealed class RecordingIdempotencyStore(IdempotencyStore inner, ApplicationDbContext db) : IInternalIdempotencyStore {
        public Task<IdempotencyReservation> ReserveAsync(string caller, string key, Guid tenantId, string requestHash, TimeSpan pendingTimeout, CancellationToken ct) {
            return inner.ReserveAsync(caller, key, tenantId, requestHash, pendingTimeout, ct);
        }

        public async Task<bool> CompleteAsync(string caller, string key, Guid ownerToken, int status, string responseJson, CancellationToken ct) {
            var inTransaction = db.Database.CurrentTransaction is not null;
            int previous;
            lock (CompleteLog) previous = CompleteLog.Count(c => c.Key == key);
            if (FailLateComplete && previous >= 1) throw new InvalidOperationException("middleware tamamlaması bozuldu");

            if (LoseOwnership && previous == 0) {
                lock (CompleteLog) CompleteLog.Add(new CompleteCall(key, inTransaction, false));
                return false;
            }

            var result = await inner.CompleteAsync(caller, key, ownerToken, status, responseJson, ct);
            lock (CompleteLog) CompleteLog.Add(new CompleteCall(key, inTransaction, result));
            return result;
        }

        public Task ReleaseAsync(string caller, string key, Guid ownerToken, CancellationToken ct) {
            return inner.ReleaseAsync(caller, key, ownerToken, ct);
        }
    }

    private sealed class ProbeStartupFilter : IStartupFilter {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) {
            return app => {
                       next(app);
                       app.Map("/internal/test/counter", b => b.Run(async ctx => {
                                                                        using var reader = new StreamReader(ctx.Request.Body);
                                                                        var body = await reader.ReadToEndAsync();
                                                                        var n = Interlocked.Increment(ref _counter);
                                                                        await ctx.Response.WriteAsJsonAsync(new { count = n, body });
                                                                    }));
                       app.Map("/internal/test/flaky", b => b.Run(async ctx => {
                                                                      if (Interlocked.Increment(ref _flaky) == 1) {
                                                                          ctx.Response.StatusCode = 500;
                                                                          return;
                                                                      }

                                                                      await ctx.Response.WriteAsJsonAsync(new { ok = true });
                                                                  }));
                       app.Map("/internal/test/slow", b => b.Run(async ctx => {
                                                                     SlowEntered.TrySetResult();
                                                                     await SlowRelease.Task;
                                                                     await ctx.Response.WriteAsJsonAsync(new { slow = true });
                                                                 }));
                       app.Map("/internal/test/whoami", b => b.Run(async ctx => {
                                                                       var t = ctx.RequestServices.GetRequiredService<ICurrentTenant>();
                                                                       await ctx.Response.WriteAsJsonAsync(new {
                                                                           tenantId = t.TenantId,
                                                                           userId = t.UserId,
                                                                           userFullName = t.UserFullName,
                                                                           hasPos = t.HasModule("pos"),
                                                                           branchIds = t.BranchIds,
                                                                           authenticated = ctx.User.Identity?.IsAuthenticated,
                                                                           name = ctx.User.Identity?.Name
                                                                       });
                                                                   }));
                       app.Map("/internal/other/x", b => b.Run(ctx => {
                                                                   ctx.Response.StatusCode = 200;
                                                                   return Task.CompletedTask;
                                                               }));
                   };
        }
    }
}
