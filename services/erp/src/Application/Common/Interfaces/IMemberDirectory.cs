namespace ERP.Application.Common.Interfaces;

/// <summary>Kimlik servisindeki bir firma üyesinin ERP'nin ihtiyaç duyduğu özeti.</summary>
public sealed record MemberRef(Guid UserId, string Email, string FullName);

/// <summary>
///     Satış temsilcisi doğrulaması için firma üyelerini kimlik servisinden okur (imzalı iç API, 60 sn önbellek).
///     Üye değilse null döner; kimlik servisine ulaşılamazsa istisna fırlatır.
/// </summary>
public interface IMemberDirectory {
    Task<MemberRef?> GetAsync(Guid tenantId, Guid userId, CancellationToken ct);
}
