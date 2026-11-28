namespace ERP.Domain.Shipping;

/// <summary>
///     Sevk durum makinesi (tek kaynak). Durumlar ShipmentStatus adlarıyla verilir. usePickPack kapalıyken P7 kısayolu
///     (Draft ve Packed doğrudan Shipped) açılır. İptal dışındaki P7 "sevkli iptal" yolu bu tabloda değildir (ShipmentService.CancelAsync).
/// </summary>
public static class ShipmentStatusMachine {
    private static readonly Dictionary<string, string[]> Table = new(StringComparer.Ordinal) {
        ["Draft"]          = ["Picking", "Cancelled"],
        ["Picking"]        = ["Packed", "Draft", "Cancelled"],
        ["Packed"]         = ["Shipped", "Draft", "Cancelled"],
        ["Shipped"]        = ["Delivered", "DeliveryFailed"],
        ["DeliveryFailed"] = ["Shipped", "Returned"],
        ["Delivered"]      = [],
        ["Returned"]       = [],
        ["Cancelled"]      = []
    };

    public static bool CanMove(string from, string to, bool usePickPack) {
        return Next(from, usePickPack).Contains(to);
    }

    public static IReadOnlyList<string> Next(string from, bool usePickPack) {
        if (!Table.TryGetValue(from, out var next)) return [];
        if (!usePickPack && from == "Draft") return [.. next, "Shipped"];
        return next;
    }
}
