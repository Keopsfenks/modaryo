using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shouldly;

namespace Modaryo.Internal.Tests;

public class InternalIdempotencyMiddlewareTests {
    private const string Secret = "unit-secret";
    private const string Tenant = "0193a6f3-0a42-7c11-8e2b-6d9f4a3c5b21";

    private sealed class FakeStore : IInternalIdempotencyStore {
        public bool ThrowOnComplete;
        public int  Completes;
        public int  Releases;
        public Guid LastReserved;
        public Guid LastCompleteOwner;
        public string? LastCompleteJson;

        public Task<IdempotencyReservation> ReserveAsync(string caller, string key, Guid tenantId, string requestHash,
                                                         TimeSpan pendingTimeout, CancellationToken ct) {
            LastReserved = Guid.NewGuid();
            return Task.FromResult(new IdempotencyReservation(IdempotencyOutcome.Reserved, OwnerToken: LastReserved));
        }

        public Task<bool> CompleteAsync(string caller, string key, Guid ownerToken, int status, string responseJson,
                                        CancellationToken ct) {
            Completes++;
            LastCompleteOwner = ownerToken;
            LastCompleteJson  = responseJson;
            if (ThrowOnComplete) throw new InvalidOperationException("tamamlanamadı");
            return Task.FromResult(true);
        }

        public Task ReleaseAsync(string caller, string key, Guid ownerToken, CancellationToken ct) {
            Releases++;
            return Task.CompletedTask;
        }
    }

    private static (InternalAuthMiddleware Middleware, FakeTimeProvider Time) Create(RequestDelegate next) {
        var time    = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var options = new InternalOptions();
        options.Callers["svc"] = new InternalCaller { Secret = Secret, Routes = ["/internal/"] };
        return (new InternalAuthMiddleware(next, Options.Create(options), time), time);
    }

    private static DefaultHttpContext Context(FakeTimeProvider time, FakeStore? store, string method = "POST") {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOptions();
        if (store is not null) {
            services.AddSingleton<IInternalIdempotencyStore>(store);
            services.AddScoped<IdempotencyContext>();
            services.AddScoped<IIdempotencyContext>(sp => sp.GetRequiredService<IdempotencyContext>());
        }

        var path = "/internal/x";
        var ts   = time.GetUtcNow().ToUnixTimeSeconds();
        var body = Encoding.UTF8.GetBytes("{}");
        var ctx  = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        ctx.Request.Method = method;
        ctx.Request.Path   = path;
        ctx.Request.Body   = new MemoryStream(body);
        ctx.Request.Headers[InternalHeaders.Service]        = "svc";
        ctx.Request.Headers[InternalHeaders.Timestamp]      = ts.ToString();
        ctx.Request.Headers[InternalHeaders.Tenant]         = Tenant;
        ctx.Request.Headers[InternalHeaders.IdempotencyKey] = "key-1";
        ctx.Request.Headers[InternalHeaders.Signature]
            = InternalSignature.Compute(Secret, method, path, ts, Tenant, "key-1", body);
        ctx.Response.Body = new MemoryStream();
        return ctx;
    }

    [Test]
    public async Task CompleteFailsAfterSuccessfulHandler_DoesNotReleaseAndStillResponds() {
        var store = new FakeStore { ThrowOnComplete = true };
        var (middleware, time) = Create(async ctx => await ctx.Response.WriteAsync("ok"));
        var ctx = Context(time, store);

        await middleware.InvokeAsync(ctx);

        store.Completes.ShouldBe(1);
        store.Releases.ShouldBe(0);
        ctx.Response.StatusCode.ShouldBe(200);
        ctx.Response.Body.Position = 0;
        new StreamReader(ctx.Response.Body).ReadToEnd().ShouldBe("ok");
    }

    [Test]
    public async Task HandlerCompletesThroughContext_UsesReservationOwner() {
        var store = new FakeStore();
        var (middleware, time) = Create(async ctx => {
                                            var idempotency = ctx.RequestServices.GetRequiredService<IIdempotencyContext>();
                                            idempotency.IsActive.ShouldBeTrue();
                                            (await idempotency.CompleteAsync(201, "{\"id\":1}", ctx.RequestAborted)).ShouldBeTrue();
                                            ctx.Response.StatusCode = 201;
                                            await ctx.Response.WriteAsync("{\"id\":1}");
                                        });

        await middleware.InvokeAsync(Context(time, store));

        store.LastCompleteOwner.ShouldBe(store.LastReserved);
        store.LastCompleteJson.ShouldBe("{\"id\":1}");
        store.Releases.ShouldBe(0);
    }

    [Test]
    public async Task ContextWithoutReservation_IsInactiveAndCompleteThrows() {
        var context = new IdempotencyContext();

        context.IsActive.ShouldBeFalse();
        await Should.ThrowAsync<InvalidOperationException>(() => context.CompleteAsync(200, "{}", CancellationToken.None));
    }

    [Test]
    public async Task ServerError_ReleasesReservation() {
        var store = new FakeStore();
        var (middleware, time) = Create(ctx => {
                                            ctx.Response.StatusCode = 500;
                                            return Task.CompletedTask;
                                        });

        await middleware.InvokeAsync(Context(time, store));

        store.Completes.ShouldBe(0);
        store.Releases.ShouldBe(1);
    }

    [Test]
    public async Task HandlerThrows_ReleasesReservation() {
        var store = new FakeStore();
        var (middleware, time) = Create(_ => throw new InvalidOperationException("boom"));

        await Should.ThrowAsync<InvalidOperationException>(() => middleware.InvokeAsync(Context(time, store)));

        store.Releases.ShouldBe(1);
    }

    [Test]
    public async Task NoStoreRegistered_WriteReturns405() {
        var called = false;
        var (middleware, time) = Create(_ => {
                                            called = true;
                                            return Task.CompletedTask;
                                        });
        var ctx = Context(time, null);

        await middleware.InvokeAsync(ctx);

        ctx.Response.StatusCode.ShouldBe(405);
        called.ShouldBeFalse();
    }
}
