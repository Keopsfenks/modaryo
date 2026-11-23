namespace ERP.Application.Common.Interfaces;

/// <summary>Kilit tarihi (dahil) ve öncesindeki defter yazımlarını reddeder: 409 period_locked.</summary>
public interface IPeriodLock {
    Task EnsureOpenAsync(DateOnly date, CancellationToken ct);
}
