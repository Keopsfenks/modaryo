namespace ERP.Infrastructure.Outbox;

public sealed class OutboxOptions {
    public const string Section = "Outbox";

    public const string StreamKey = "erp.events";

    public const int BatchSize = 100;

    public const int MaxLastErrorLength = 2000;

    public bool RelayEnabled { get; set; } = true;

    public int PollIntervalMs { get; set; } = 1000;
}
