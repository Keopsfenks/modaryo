namespace Modaryo.Internal;

/// <summary>
///     İsteğe özel (scoped) idempotency bağlamı. Yazma isteği anahtarı ayrıldıktan sonra middleware tarafından doldurulur.
///     İşleyici, kayıtlı yanıtı KENDİ veritabanı işlemi içinde tamamlar (<see cref="CompleteAsync" />): iş değişikliği ve
///     idempotency kaydı aynı işlemde onaylanır ya da birlikte geri alınır. Bu yapılmazsa middleware yanıt yazıldıktan sonra tamamlar
///     (işlem onaylandıktan sonra süreç çökerse kayıt bekleyen kalır ve zaman aşımına kadar yeniden deneme 409 alır).
/// </summary>
public interface IIdempotencyContext {
    /// <summary>İstek idempotency anahtarı ayırmışsa true (iç API yazma isteği).</summary>
    bool IsActive { get; }

    /// <summary>
    ///     Ayrılan kaydı verilen yanıtla tamamlar. Idempotency deposu ile işleyicinin veritabanı bağlamı aynı olmalı ve çağrı
    ///     işleyicinin açık işlemi içinde yapılmalıdır. Kayıt başkasına devredilmişse (zaman aşımı sonrası devralma) false döner;
    ///     işleyici bu durumda işlemi geri almalıdır.
    /// </summary>
    Task<bool> CompleteAsync(int status, string responseJson, CancellationToken ct);
}

/// <summary>Middleware'in doldurduğu uygulama; <see cref="IIdempotencyContext" /> olarak da çözülür.</summary>
public sealed class IdempotencyContext : IIdempotencyContext {
    private IInternalIdempotencyStore? _store;
    private string? _caller;
    private string? _key;
    private Guid    _owner;

    public bool IsActive => _store is not null;

    /// <summary>Anahtar ayrıldıktan sonra middleware çağırır.</summary>
    public void Begin(IInternalIdempotencyStore store, string caller, string key, Guid ownerToken) {
        _store  = store;
        _caller = caller;
        _key    = key;
        _owner  = ownerToken;
    }

    public Task<bool> CompleteAsync(int status, string responseJson, CancellationToken ct) {
        if (_store is null || _caller is null || _key is null) throw new InvalidOperationException("Bu istek idempotency anahtarı ayırmadı.");

        return _store.CompleteAsync(_caller, _key, _owner, status, responseJson, ct);
    }
}
