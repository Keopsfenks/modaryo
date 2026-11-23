namespace ERP.Application.Common.Interfaces;

/// <summary>
///     Olayı mevcut DbContext'e ekler; iş değişikliğiyle aynı SaveChanges işleminde kaydedilir.
///     data, JSON'a snake_case anahtarlarla serileştirilir.
/// </summary>
public interface IOutbox {
    void Enqueue(string type, object data);
}
