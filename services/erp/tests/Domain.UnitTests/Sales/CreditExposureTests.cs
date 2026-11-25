using ERP.Domain.Sales;
using NUnit.Framework;
using Shouldly;

namespace ERP.Domain.UnitTests.Sales;

public class CreditExposureTests {
    [Test]
    public void Exposure_BoundaryEqualNotApproval() {
        CreditExposure.NeedsApproval(10000m, 5000m, 3000m, 2000m).ShouldBeFalse();
        CreditExposure.NeedsApproval(10000m, 6000m, 3000m, 2000m).ShouldBeTrue();
    }

    [Test]
    public void Exposure_NoLimit_NeverNeedsApproval() {
        CreditExposure.NeedsApproval(null, 1_000_000m, 1_000_000m, 1_000_000m).ShouldBeFalse();
    }
}
