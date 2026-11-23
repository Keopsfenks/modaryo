using ERP.Application.Common.Exceptions;
using ERP.Application.Pricing;

namespace ERP.Web.FunctionalTests.Pricing;

public class DiscountPolicyTests {
    [Test]
    public void DiscountPolicy_CombinesLineAndDocument() {
        // %5 satır + %4 belge = 1 - 0,95 x 0,96 = %8,8
        DiscountPolicy.EffectivePercent(1000m, 5m, 4m).ShouldBe(8.8m);
        DiscountPolicy.EffectivePercent(1000m, 0m, 0m).ShouldBe(0m);
        DiscountPolicy.EffectivePercent(0m, 5m, 4m).ShouldBe(0m);

        var limit8 = new PricingTestSupport.FakeTenant(Guid.NewGuid(), 8m);
        var ex     = Should.Throw<AppException>(() => DiscountPolicy.Ensure(8.8m, limit8));
        ex.Status.ShouldBe(403);
        ex.Code.ShouldBe("discount_limit_exceeded");
        ex.Message.ShouldBe("İskonto sınırınız %8.");

        var limit9 = new PricingTestSupport.FakeTenant(Guid.NewGuid(), 9m);
        Should.NotThrow(() => DiscountPolicy.Ensure(8.8m, limit9));
        Should.NotThrow(() => DiscountPolicy.Ensure(9m, limit9));
    }

    [Test]
    public void DiscountPolicy_OwnerExempt() {
        // Sahip oturumu max_discount_pct = 100 taşır.
        var owner = new PricingTestSupport.FakeTenant(Guid.NewGuid(), 100m);
        Should.NotThrow(() => DiscountPolicy.Ensure(DiscountPolicy.EffectivePercent(500m, 100m, 100m), owner));
        Should.NotThrow(() => DiscountPolicy.Ensure(99.9m, owner));

        var nobody = new PricingTestSupport.FakeTenant(Guid.NewGuid(), 0m);
        Should.Throw<AppException>(() => DiscountPolicy.Ensure(0.01m, nobody)).Code.ShouldBe("discount_limit_exceeded");
        Should.NotThrow(() => DiscountPolicy.Ensure(0m, nobody));
    }
}
