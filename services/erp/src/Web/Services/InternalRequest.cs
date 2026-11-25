using System.Security.Claims;
using System.Text.Json;
using ERP.Application.Common.Interfaces;
using Modaryo.Internal;

namespace ERP.Web.Services;

public sealed class InternalRequest(
    IHttpContextAccessor accessor,
    IIdempotencyContext  idempotency
) : IInternalRequest {
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public string? Service => accessor.HttpContext?.User.FindFirstValue(InternalClaimTypes.Service) is { Length: > 0 } service ? service : null;

    public Task<bool> CompleteIdempotencyAsync(int status, object response, CancellationToken ct) {
        return idempotency.IsActive ? idempotency.CompleteAsync(status, JsonSerializer.Serialize(response, Json), ct) : Task.FromResult(true);
    }
}
