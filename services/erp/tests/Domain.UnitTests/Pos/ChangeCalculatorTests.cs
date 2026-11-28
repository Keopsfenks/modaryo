using ERP.Domain.Pos;
using NUnit.Framework;
using Shouldly;

namespace ERP.Domain.UnitTests.Pos;

public class ChangeCalculatorTests {
    [Test]
    public void Change_MixedPayment() {
        var (change, cashNet) = ChangeCalculator.Compute(347.50m, card: 200m, cash: 200m, onAccount: 0m);
        change.ShouldBe(52.50m);
        cashNet.ShouldBe(147.50m);
    }

    [Test]
    public void Change_VeresiyeReducesRemaining() {
        var (change, cashNet) = ChangeCalculator.Compute(100m, card: 0m, cash: 0m, onAccount: 100m);
        change.ShouldBe(0m);
        cashNet.ShouldBe(0m);
    }

    [Test]
    public void Change_CardExceedsRemaining_Throws() {
        var ex = Should.Throw<PosRuleException>(() => ChangeCalculator.Compute(100m, card: 120m, cash: 0m, onAccount: 0m));
        ex.Code.ShouldBe("card_exceeds_remaining");
    }

    [Test]
    public void Change_InsufficientThrows() {
        var ex = Should.Throw<PosRuleException>(() => ChangeCalculator.Compute(100m, card: 40m, cash: 50m, onAccount: 0m));
        ex.Code.ShouldBe("payment_insufficient");
    }
}
