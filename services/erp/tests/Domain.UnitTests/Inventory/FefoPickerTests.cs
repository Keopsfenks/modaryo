using ERP.Domain.Inventory;
using NUnit.Framework;
using Shouldly;

namespace ERP.Domain.UnitTests.Inventory;

public class FefoPickerTests {
    static readonly DateTimeOffset T0 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Test]
    public void Fefo_NearestExpiryFirstNullsLast() {
        var noExpiry = Guid.NewGuid();
        var late     = Guid.NewGuid();
        var soon     = Guid.NewGuid();

        var picks = FefoPicker.Suggest(new (Guid, DateOnly?, DateTimeOffset, decimal)[] {
            (noExpiry, null, T0, 10m),
            (late, new DateOnly(2027, 6, 1), T0, 10m),
            (soon, new DateOnly(2027, 1, 1), T0, 10m)
        }, 4m);

        picks.Count.ShouldBe(1);
        picks[0].ShouldBe((soon, 4m));
    }

    [Test]
    public void Fefo_SplitsAcrossLots() {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var c = Guid.NewGuid();

        var picks = FefoPicker.Suggest(new (Guid, DateOnly?, DateTimeOffset, decimal)[] {
            (c, null, T0, 10m),
            (b, new DateOnly(2027, 2, 1), T0, 5m),
            (a, new DateOnly(2027, 1, 1), T0, 3m)
        }, 12m);

        picks.Count.ShouldBe(3);
        picks[0].ShouldBe((a, 3m));
        picks[1].ShouldBe((b, 5m));
        picks[2].ShouldBe((c, 4m));
    }
}
