using System.Reflection;
using System.Text.RegularExpressions;
using IdentityService.Domain.Constants;
using NUnit.Framework;
using Shouldly;

namespace IdentityService.Domain.UnitTests;

public class PermissionCatalogTests
{
    [Test]
    public void EveryPermissionConstant_IsInCatalog()
    {
        var constants = typeof(Permissions)
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
            .Where(f => f.IsLiteral && f.Name != nameof(Permissions.All))
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToList();

        constants.ShouldNotBeEmpty();
        foreach (var code in constants)
        {
            PermissionCatalog.IsKnown(code).ShouldBeTrue($"{code} katalogda yok");
        }
    }

    [Test]
    public void CatalogCodes_AreUniqueAndLowercaseDotted()
    {
        var codes = PermissionCatalog.All.Select(p => p.Code).ToList();

        codes.Distinct().Count().ShouldBe(codes.Count);
        codes.ShouldAllBe(c => Regex.IsMatch(c, @"^[a-z0-9]+(-[a-z0-9]+)*(\.[a-z0-9]+(-[a-z0-9]+)*)+$"));
        PermissionCatalog.IsKnown(Permissions.All).ShouldBeFalse();
    }

    [Test]
    public void Catalog_HasTurkishNameAndDescriptionAndKnownGroups()
    {
        string[] groups = ["Firma", "Üyeler", "Roller", "Ayarlar", "Cari hesaplar", "Katalog", "Fiyatlandırma", "Stok", "Denetim"];

        PermissionCatalog.All.ShouldAllBe(p => !string.IsNullOrWhiteSpace(p.Name) && !string.IsNullOrWhiteSpace(p.Description));
        PermissionCatalog.All.ShouldAllBe(p => p.ModuleKey == null || ModuleKeys.IsKnown(p.ModuleKey));
        PermissionCatalog.All.Select(p => p.Group).Distinct().ShouldBe(groups, ignoreOrder: true);
    }
}
