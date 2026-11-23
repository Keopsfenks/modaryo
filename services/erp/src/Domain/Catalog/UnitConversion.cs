namespace ERP.Domain.Catalog;

public static class UnitConversion {
    /// <summary>1 {birim} = factor x temel birim. Yuvarlama yapılmaz.</summary>
    public static decimal ToBase(decimal qty, decimal factor) => qty * factor;

    public static decimal FromBase(decimal baseQty, decimal factor) => baseQty / factor;
}
