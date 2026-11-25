namespace ERP.Application.Common.Interfaces;

/// <summary>İmzalı iç API isteği bağlamı: çağıran servis ve idempotency kaydının işleyicinin işleminde tamamlanması.</summary>
public interface IInternalRequest {
    /// <summary>İç API çağrısında çağıran servis adı (hr, assets, ...); kullanıcı oturumunda null.</summary>
    string? Service { get; }

    /// <summary>
    ///     İdempotency kaydını yanıtla (JSON, web varsayılanları) tamamlar. İşleyicinin açık veritabanı işlemi içinde, onaydan önce çağrılır:
    ///     kayıt iş değişikliğiyle birlikte onaylanır. Kayıt başkasına devredilmişse false döner (işlem geri alınmalıdır).
    ///     İdempotency anahtarı ayrılmamış istekte (ör. testte doğrudan MediatR) true döner ve hiçbir şey yazmaz.
    /// </summary>
    Task<bool> CompleteIdempotencyAsync(int status, object response, CancellationToken ct);
}
