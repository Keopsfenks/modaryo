using System.Text.RegularExpressions;
using IdentityService.Domain.Constants;
using NUnit.Framework;
using Shouldly;

namespace IdentityService.Domain.UnitTests;

public class ModuleKeysTests
{
    [Test]
    public void ModuleKeys_HasEighteenUniqueKebabKeysWithTurkishNames()
    {
        ModuleKeys.All.Count.ShouldBe(18);
        ModuleKeys.All.Select(m => m.Key).Distinct().Count().ShouldBe(18);
        ModuleKeys.All.ShouldAllBe(m => Regex.IsMatch(m.Key, "^[a-z0-9]+(-[a-z0-9]+)*$"));
        ModuleKeys.All.ShouldAllBe(m => !string.IsNullOrWhiteSpace(m.Name) && !string.IsNullOrWhiteSpace(m.Description));
        ModuleKeys.All.ShouldAllBe(m => m.Wave == 1 || m.Wave == 2);
        ModuleKeys.All.Count(m => m.Wave == 1).ShouldBe(7);
        ModuleKeys.All.Count(m => m.Wave == 2).ShouldBe(11);
    }

    [Test]
    public void IsKnown_ChecksKeys()
    {
        ModuleKeys.IsKnown("pos").ShouldBeTrue();
        ModuleKeys.IsKnown("sube").ShouldBeTrue();
        ModuleKeys.IsKnown("POS").ShouldBeFalse();
        ModuleKeys.IsKnown("yok").ShouldBeFalse();
    }
}
