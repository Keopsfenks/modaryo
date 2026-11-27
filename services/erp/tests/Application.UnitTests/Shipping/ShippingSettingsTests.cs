using ERP.Application.Settings.Categories;
using NUnit.Framework;
using Shouldly;

namespace ERP.Application.UnitTests.Shipping;

public class ShippingSettingsTests {
    [Test]
    public void Settings_CarrierSeedHasTrackingTemplates() {
        var s = new ShippingSettings();
        s.UsePickPack.ShouldBeTrue();
        s.NotifyCustomerOnShip.ShouldBeTrue();
        s.Carriers!.Select(c => c.Name).ShouldBe(["Yurtiçi", "Aras", "MNG", "PTT Kargo", "Sürat", "HepsiJet", "Kolay Gelsin", "Diğer", "Kendi aracımız"], ignoreOrder: true);
        foreach (var c in s.Carriers!.Where(c => c.Code is not ("diger" or ShippingSettings.OwnVehicleCode))) {
            c.TrackingUrlTemplate.ShouldStartWith("https://");
            c.TrackingUrlTemplate.ShouldContain("{no}");
            c.IsActive.ShouldBeTrue();
        }

        s.Carriers!.Count(c => c.Code == "diger").ShouldBe(1);
        s.ShipEmailBody.ShouldContain("{takipLink}");
        new ShippingSettingsValidator().Validate(s).IsValid.ShouldBeTrue();
    }

    [Test]
    public void Settings_RejectsTemplateWithoutPlaceholder() {
        var s = new ShippingSettings() with { Carriers = [new CarrierInfo("x", "X", "https://x.example/track", true)] };
        new ShippingSettingsValidator().Validate(s).IsValid.ShouldBeFalse();
    }
}
