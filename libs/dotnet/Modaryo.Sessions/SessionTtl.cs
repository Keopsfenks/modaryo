namespace Modaryo.Sessions;

/// <summary>Oturum süreleri (spec 6.4) ve Valkey TTL hesabı.</summary>
public static class SessionTtl {
    public static readonly TimeSpan RegularIdle      = TimeSpan.FromHours(8);
    public static readonly TimeSpan RegularAbsolute  = TimeSpan.FromHours(24);
    public static readonly TimeSpan RememberIdle     = TimeSpan.FromDays(14);
    public static readonly TimeSpan RememberAbsolute = TimeSpan.FromDays(30);

    /// <summary>
    ///     min(idle_timeout_s, absolute_expires_at - now). Mutlak süre dolmuşsa null.
    /// </summary>
    public static TimeSpan? Compute(SessionRecord record, DateTimeOffset now) {
        var remaining = record.AbsoluteExpiresAt - now.ToUnixTimeSeconds();
        if (remaining <= 0) return null;

        return TimeSpan.FromSeconds(Math.Min(record.IdleTimeoutS, remaining));
    }
}