namespace ERP.Application.Common.Interfaces;

/// <summary>İsteği yapan oturumun aktif firması (Valkey'deki oturum kaydından). Yoksa null.</summary>
public interface ICurrentTenant {
    Guid? TenantId { get; }
}