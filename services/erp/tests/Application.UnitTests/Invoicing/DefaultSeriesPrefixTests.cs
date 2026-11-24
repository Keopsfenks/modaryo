using System.Text.RegularExpressions;
using ERP.Application.Invoicing.Series;
using NUnit.Framework;
using Shouldly;

namespace ERP.Application.UnitTests.Invoicing;

public class DefaultSeriesPrefixTests {
    private static readonly HashSet<string> None = [];

    [Test]
    public void MainBranch_GetsFixedReadablePrefixes() {
        DefaultSeriesPrefixes.Pick("sales", Guid.NewGuid(), true, None).ShouldBe("SAT");
        DefaultSeriesPrefixes.Pick("purchase", Guid.NewGuid(), true, None).ShouldBe("ALS");
    }

    [Test]
    public void OtherBranch_IsDeterministic_AndStartsWithGroupLetter() {
        var branch = Guid.Parse("0197a1b2-c3d4-7e5f-8a6b-1c2d3e4f5a6b");

        var a = DefaultSeriesPrefixes.Pick("sales", branch, false, None);
        var b = DefaultSeriesPrefixes.Pick("sales", branch, false, None);

        a.ShouldBe(b);
        Regex.IsMatch(a, "^S[A-Z0-9]{2}$").ShouldBeTrue(a);
        Regex.IsMatch(DefaultSeriesPrefixes.Pick("purchase", branch, false, None), "^A[A-Z0-9]{2}$").ShouldBeTrue();
    }

    [Test]
    public void TakenPrefixes_AreSkipped_UntilAFreeOneIsFound() {
        var branch = Guid.NewGuid();
        var taken  = new HashSet<string>();
        var picked = new List<string>();
        for (var i = 0; i < 1296; i++) {
            var next = DefaultSeriesPrefixes.Pick("sales", branch, false, taken);
            taken.Add(next);
            picked.Add(next);
        }

        picked.Distinct().Count().ShouldBe(1296);
        Should.Throw<InvalidOperationException>(() => DefaultSeriesPrefixes.Pick("sales", branch, false, taken));
    }

    [Test]
    public void MainBranch_FallsBackWhenFixedPrefixIsTaken() {
        var picked = DefaultSeriesPrefixes.Pick("sales", Guid.NewGuid(), true, new HashSet<string> { "SAT" });

        picked.ShouldNotBe("SAT");
        Regex.IsMatch(picked, "^S[A-Z0-9]{2}$").ShouldBeTrue();
    }
}
