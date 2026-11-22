namespace ERP.Domain.Common;

/// <summary>
/// Firmaya (kiracıya) ait veri. Bu arayüzü uygulayan her varlık otomatik olarak aktif firmaya filtrelenir
/// ve yeni kayıtlara TenantId otomatik yazılır.
/// </summary>
public interface ITenantOwned
{
    Guid TenantId { get; set; }
}
