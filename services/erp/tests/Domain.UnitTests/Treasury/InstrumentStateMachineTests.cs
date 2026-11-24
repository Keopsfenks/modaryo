using ERP.Domain.Treasury;
using NUnit.Framework;
using Shouldly;

namespace ERP.Domain.UnitTests.Treasury;

public class InstrumentStateMachineTests {
    static readonly InstrumentStatus[] All = Enum.GetValues<InstrumentStatus>();

    static IEnumerable<TestCaseData> ReceivedAllowed() {
        yield return new TestCaseData(InstrumentStatus.InPortfolio, InstrumentStatus.Endorsed);
        yield return new TestCaseData(InstrumentStatus.InPortfolio, InstrumentStatus.InCollection);
        yield return new TestCaseData(InstrumentStatus.InPortfolio, InstrumentStatus.ReturnedToCustomer);
        yield return new TestCaseData(InstrumentStatus.InCollection, InstrumentStatus.Collected);
        yield return new TestCaseData(InstrumentStatus.InCollection, InstrumentStatus.Bounced);
        yield return new TestCaseData(InstrumentStatus.Endorsed, InstrumentStatus.Bounced);
        yield return new TestCaseData(InstrumentStatus.Bounced, InstrumentStatus.Collected);
    }

    [TestCaseSource(nameof(ReceivedAllowed))]
    public void InstrumentStateMachine_ReceivedAllowedTransitions(InstrumentStatus from, InstrumentStatus to) {
        InstrumentStateMachine.CanMove(InstrumentDirection.Received, from, to).ShouldBeTrue();
        InstrumentStateMachine.CanMove(InstrumentDirection.Issued, from, to).ShouldBeFalse();
    }

    [Test]
    public void InstrumentStateMachine_ReceivedHasExactlySevenTransitions() {
        var count = (from a in All
                     from b in All
                     where InstrumentStateMachine.CanMove(InstrumentDirection.Received, a, b)
                     select 1).Count();
        count.ShouldBe(7);
    }

    [Test]
    public void InstrumentStateMachine_IssuedTransitions() {
        InstrumentStateMachine.CanMove(InstrumentDirection.Issued, InstrumentStatus.Issued, InstrumentStatus.Paid).ShouldBeTrue();
        InstrumentStateMachine.CanMove(InstrumentDirection.Issued, InstrumentStatus.Issued, InstrumentStatus.ReturnedFromSupplier).ShouldBeTrue();
        InstrumentStateMachine.CanMove(InstrumentDirection.Issued, InstrumentStatus.Paid, InstrumentStatus.Issued).ShouldBeFalse();
        InstrumentStateMachine.CanMove(InstrumentDirection.Issued, InstrumentStatus.Issued, InstrumentStatus.Collected).ShouldBeFalse();
    }

    [Test]
    public void InstrumentStateMachine_DisallowsCollectedToEndorsed() {
        InstrumentStateMachine.CanMove(InstrumentDirection.Received, InstrumentStatus.Collected, InstrumentStatus.Endorsed).ShouldBeFalse();
        InstrumentStateMachine.CanMove(InstrumentDirection.Received, InstrumentStatus.InPortfolio, InstrumentStatus.Collected).ShouldBeFalse();
        InstrumentStateMachine.CanMove(InstrumentDirection.Received, InstrumentStatus.InPortfolio, InstrumentStatus.InPortfolio).ShouldBeFalse();
    }
}
