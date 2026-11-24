using ERP.Domain.Treasury;
using NUnit.Framework;
using Shouldly;

namespace ERP.Domain.UnitTests.Treasury;

public class AgingBucketsTests {
    [TestCase(-5, "current")]
    [TestCase(0, "current")]
    [TestCase(1, "1-30")]
    [TestCase(30, "1-30")]
    [TestCase(31, "31-60")]
    [TestCase(60, "31-60")]
    [TestCase(61, "61-90")]
    [TestCase(90, "61-90")]
    [TestCase(91, "90+")]
    public void Aging_BucketsBoundaries(int days, string bucket) => AgingBuckets.Of(days).ShouldBe(bucket);

    [Test]
    public void Aging_AllListsFiveBucketsInOrder() =>
        AgingBuckets.All.ShouldBe(["current", "1-30", "31-60", "61-90", "90+"]);
}
