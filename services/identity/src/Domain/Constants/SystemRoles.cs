using IdentityService.Domain.Entities;

namespace IdentityService.Domain.Constants;

/// <summary>Her firma oluşturulduğunda otomatik eklenen hazır roller.</summary>
public static class SystemRoles
{
    public const string Owner = "owner";
    public const string Admin = "admin";
    public const string Member = "member";

    /// <summary>
    /// Hazır rol izin seti sürümleri. Her sürüm yalnızca o sürümde eklenen izinleri taşır; yeni izin eklemek için yeni sürüm açılır.
    /// Senkronizasyon her firmaya bir sürümü yalnızca bir kez uygular, böylece yöneticinin sonradan kaldırdığı izin geri eklenmez.
    /// </summary>
    public static IReadOnlyList<PermissionSetVersion> PermissionSetVersions { get; } =
    [
        new(1,
            [
                Permissions.CompanyUpdate, Permissions.MembersRead, Permissions.MembersInvite, Permissions.MembersManage,
                Permissions.CariRead, Permissions.CariManage, Permissions.CariMovements,
            ],
            [Permissions.MembersRead, Permissions.CariRead]),
        new(2,
            [
                Permissions.RolesRead, Permissions.RolesManage, Permissions.SettingsModules,
                Permissions.SettingsBranches, Permissions.AuditRead,
            ],
            []),
        new(3,
            [Permissions.SettingsCompany, Permissions.SettingsGeneral, Permissions.SettingsFinance],
            []),
        new(4,
            [Permissions.CatalogRead, Permissions.CatalogManage, Permissions.SettingsCatalog],
            [Permissions.CatalogRead]),
        new(5,
            [Permissions.PricingRead, Permissions.PricingManage, Permissions.PricingPurchase, Permissions.SettingsPricing],
            [Permissions.PricingRead]),
        new(6,
            [Permissions.StockRead, Permissions.StockManage, Permissions.StockCount, Permissions.CostRead, Permissions.SettingsStock],
            [Permissions.StockRead]),
        new(7,
            [
                Permissions.InvoiceRead, Permissions.InvoiceManage, Permissions.InvoiceIssue, Permissions.InvoiceCancel,
                Permissions.SettingsDocuments, Permissions.SettingsEBelge, Permissions.RateManage,
            ],
            [Permissions.InvoiceRead]),
        new(8,
            [
                Permissions.FinanceRead, Permissions.FinanceManage, Permissions.CollectionManage, Permissions.PaymentManage,
                Permissions.TransferManage, Permissions.ExpenseManage, Permissions.InstrumentManage,
                Permissions.AccountingManage, Permissions.AccountingExport,
            ],
            [Permissions.FinanceRead]),
        new(9,
            [
                Permissions.PurchasingRead, Permissions.PurchasingManage, Permissions.PurchasingReceive,
                Permissions.PurchasingQuarantine, Permissions.SettingsPurchasing,
            ],
            [Permissions.PurchasingRead]),
        new(10,
            [
                Permissions.SalesRead, Permissions.SalesManage, Permissions.SalesApprove, Permissions.SalesLimitOverride,
                Permissions.SalesShip, Permissions.SalesReturn, Permissions.SalesRecurring, Permissions.SalesTargets,
                Permissions.SettingsSales,
            ],
            [Permissions.SalesRead, Permissions.SalesManage]),
        new(11,
            [
                Permissions.ShippingRead, Permissions.ShippingPick, Permissions.ShippingPack, Permissions.ShippingShip,
                Permissions.ShippingDeliver, Permissions.ShippingPlan, Permissions.SettingsShipping,
            ],
            [Permissions.ShippingRead]),
        new(12,
            [
                Permissions.PosSell, Permissions.PosReturn, Permissions.PosShift, Permissions.PosCashMove,
                Permissions.PosReports, Permissions.PosManage, Permissions.SettingsPos,
            ],
            []),
    ];

    public static int LatestPermissionSetVersion { get; } = PermissionSetVersions.Max(v => v.Version);

    /// <summary>Hazır rollerin izinleri: tüm sürümlerin birleşimi (yeni firmalar ve rol sıfırlama bunu kullanır).</summary>
    public static IReadOnlyDictionary<string, string[]> DefaultPermissions { get; } = new Dictionary<string, string[]>
    {
        [Owner] = [Permissions.All],
        [Admin] = PermissionSetVersions.SelectMany(v => v.AddToAdmin).Distinct().ToArray(),
        [Member] = PermissionSetVersions.SelectMany(v => v.AddToMember).Distinct().ToArray(),
    };

    public static IReadOnlyList<TenantRole> CreateDefaults(Guid tenantId) =>
    [
        Create(tenantId, "Sahip", Owner, DefaultPermissions[Owner]),
        Create(tenantId, "Yönetici", Admin, DefaultPermissions[Admin]),
        Create(tenantId, "Kullanıcı", Member, DefaultPermissions[Member]),
    ];

    private static TenantRole Create(Guid tenantId, string name, string systemKey, string[] permissions)
    {
        var role = new TenantRole { TenantId = tenantId, Name = name, SystemKey = systemKey, IsSystem = true };
        role.Permissions.AddRange(permissions.Select(p => new TenantRolePermission { RoleId = role.Id, Permission = p }));
        return role;
    }
}

/// <summary>Bir izin seti sürümünde admin ve member hazır rollerine eklenen izinler.</summary>
public sealed record PermissionSetVersion(int Version, string[] AddToAdmin, string[] AddToMember);
