using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("IdentityService.Domain.UnitTests")]
[assembly: InternalsVisibleTo("IdentityService.Web.FunctionalTests")]

namespace IdentityService.Domain.Constants;

/// <summary>Katalogdaki tek bir izin. ModuleKey null ise çekirdek izindir; doluysa yalnızca o modül açık firmalara görünür.</summary>
public sealed record PermissionInfo(string Code, string? ModuleKey, string Group, string Name, string Description);

/// <summary>
/// Kod tanımlı izin kataloğu (tek kaynak). Rol düzenleyici bu listeyi gruplu gösterir.
/// Yeni modüller girişlerini aşağıdaki statik kurucuda Register ile ekler. "*" katalog girdisi değildir.
/// </summary>
public static class PermissionCatalog
{
    private static readonly List<PermissionInfo> Entries = [];
    private static readonly HashSet<string> Codes = new(StringComparer.Ordinal);
    private static readonly object Gate = new();
    private static readonly int CoreCount;

    static PermissionCatalog()
    {
        Register(
            new(Permissions.CompanyUpdate, null, "Firma", "Firma bilgilerini düzenle", "Firma adı ve genel bilgilerini güncelleyebilir."),

            new(Permissions.MembersRead, null, "Üyeler", "Üyeleri görüntüle", "Firmanın üyelerini ve rollerini listeleyebilir."),
            new(Permissions.MembersInvite, null, "Üyeler", "Üye davet et", "Yeni üye daveti gönderebilir, yeniden gönderebilir veya iptal edebilir."),
            new(Permissions.MembersManage, null, "Üyeler", "Üyeleri yönet", "Üyelerin rolünü değiştirebilir ve üyeleri firmadan çıkarabilir."),

            new(Permissions.RolesRead, null, "Roller", "Rolleri görüntüle", "Rolleri ve izin listesini görüntüleyebilir."),
            new(Permissions.RolesManage, null, "Roller", "Rolleri yönet", "Rol oluşturabilir, düzenleyebilir ve silebilir."),

            new(Permissions.SettingsModules, null, "Ayarlar", "Modülleri yönet", "Firma için modülleri açıp kapatabilir."),
            new(Permissions.SettingsBranches, null, "Ayarlar", "Şubeleri yönet", "Şube tanımlayabilir ve düzenleyebilir."),
            new(Permissions.SettingsCompany, null, "Ayarlar", "Firma bilgilerini yönet", "Unvan, vergi bilgileri, adres ve banka hesaplarını görüntüleyip düzenleyebilir."),
            new(Permissions.SettingsGeneral, null, "Ayarlar", "Genel ayarları yönet", "Para birimi, ondalık basamak, çalışma günleri ve tatil günlerini düzenleyebilir."),
            new(Permissions.SettingsFinance, null, "Ayarlar", "Mali ayarları yönet", "Dönem kilidi gibi mali ayarları düzenleyebilir."),

            new(Permissions.CariRead, null, "Cari hesaplar", "Cari hesapları görüntüle", "Cari hesapları, ekstreleri ve vade bilgilerini görüntüleyebilir."),
            new(Permissions.CariManage, null, "Cari hesaplar", "Cari hesapları yönet", "Cari hesap oluşturabilir, düzenleyebilir ve pasife alabilir."),
            new(Permissions.CariMovements, null, "Cari hesaplar", "Cari hareketleri gir", "Cari hesaplara borç, alacak ve tahsilat hareketi girebilir."),

            new(Permissions.AuditRead, null, "Denetim", "Denetim kaydını görüntüle", "Firmadaki işlem geçmişini ve denetim kayıtlarını görüntüleyebilir."));
        CoreCount = Entries.Count;
    }

    public static IReadOnlyList<PermissionInfo> All
    {
        get
        {
            lock (Gate)
            {
                return Entries.ToArray();
            }
        }
    }

    public static bool IsKnown(string code)
    {
        lock (Gate)
        {
            return Codes.Contains(code);
        }
    }

    /// <summary>Yalnızca statik kurucudan çağrılır; mükerrer kod programlama hatasıdır.</summary>
    public static void Register(params PermissionInfo[] entries)
    {
        lock (Gate)
        {
            foreach (var entry in entries)
            {
                if (!Codes.Add(entry.Code))
                {
                    throw new InvalidOperationException($"İzin kodu zaten kayıtlı: {entry.Code}");
                }

                Entries.Add(entry);
            }
        }
    }

    internal static void RegisterForTests(PermissionInfo entry) => Register(entry);

    /// <summary>Testlerde eklenen girdileri kaldırır; kurucudaki girdiler kalır.</summary>
    internal static void ResetForTests()
    {
        lock (Gate)
        {
            for (var i = Entries.Count - 1; i >= CoreCount; i--)
            {
                Codes.Remove(Entries[i].Code);
                Entries.RemoveAt(i);
            }
        }
    }
}
