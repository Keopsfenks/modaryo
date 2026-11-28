using ERP.Domain.Shipping;
using NUnit.Framework;
using Shouldly;

namespace ERP.Domain.UnitTests.Shipping;

public class StatusMachineTests {
    private static readonly string[] All = ["Draft", "Picking", "Packed", "Shipped", "Delivered", "DeliveryFailed", "Returned", "Cancelled"];

    private static readonly (string From, string To)[] Allowed = [
        ("Draft", "Picking"), ("Draft", "Cancelled"), ("Picking", "Packed"), ("Picking", "Draft"), ("Picking", "Cancelled"), ("Packed", "Shipped"), ("Packed", "Draft"), ("Packed", "Cancelled"),
        ("Shipped", "Delivered"), ("Shipped", "DeliveryFailed"), ("DeliveryFailed", "Shipped"), ("DeliveryFailed", "Returned")
    ];

    [Test]
    public void StatusMachine_TableDriven() {
        foreach (var from in All)
            foreach (var to in All) {
                var expected = Allowed.Contains((from, to));
                ShipmentStatusMachine.CanMove(from, to, true).ShouldBe(expected, $"{from} -> {to}");
            }
    }

    [Test]
    public void StatusMachine_ShortcutWhenPickPackOff() {
        ShipmentStatusMachine.CanMove("Draft", "Shipped", false).ShouldBeTrue();
        ShipmentStatusMachine.CanMove("Draft", "Shipped", true).ShouldBeFalse();
        ShipmentStatusMachine.Next("Draft", false).ShouldContain("Shipped");
        ShipmentStatusMachine.Next("Draft", true).ShouldNotContain("Shipped");
        ShipmentStatusMachine.Next("Draft", true).ShouldBe(["Picking", "Cancelled"], ignoreOrder: true);
        ShipmentStatusMachine.CanMove("Packed", "Shipped", false).ShouldBeTrue();
    }

    [Test]
    public void StatusMachine_UnknownStatusIsNotAllowed() {
        ShipmentStatusMachine.CanMove("Nope", "Shipped", true).ShouldBeFalse();
        ShipmentStatusMachine.Next("Nope", true).ShouldBeEmpty();
    }
}
