using StackExchange.Redis;

namespace ERP.Infrastructure.Outbox;

/// <summary>Olay akışına yazma. Hata durumunda istisna fırlatır (iletim bekleyen olarak kalır).</summary>
public interface IEventStreamPublisher {
    Task PublishAsync(string streamKey, NameValueEntry[] fields, CancellationToken ct);
}

public sealed class ValkeyEventStreamPublisher(
    IConnectionMultiplexer redis
) : IEventStreamPublisher {
    public const int MaxLength = 100_000;

    public async Task PublishAsync(string streamKey, NameValueEntry[] fields, CancellationToken ct) {
        ct.ThrowIfCancellationRequested();
        await redis.GetDatabase().StreamAddAsync(streamKey, fields, maxLength: MaxLength,
                                                 useApproximateMaxLength: true).WaitAsync(ct);
    }
}
