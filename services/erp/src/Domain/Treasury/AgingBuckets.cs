namespace ERP.Domain.Treasury;

public static class AgingBuckets {
    public static IReadOnlyList<string> All { get; } = ["current", "1-30", "31-60", "61-90", "90+"];

    /// <summary>Gecikme gününe göre dilim; vadesi gelmemiş (0 veya negatif) "current".</summary>
    public static string Of(int daysOverdue) => daysOverdue switch {
        <= 0  => "current",
        <= 30 => "1-30",
        <= 60 => "31-60",
        <= 90 => "61-90",
        _     => "90+"
    };
}
