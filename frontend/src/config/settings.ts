import {
    IconBuilding,
    IconBuildingStore,
    IconCoin,
    IconHistory,
    IconPackage,
    IconPuzzle,
    IconShieldLock,
    IconTag,
    IconAdjustments,
    IconUsers,
    type Icon,
} from "@tabler/icons-react";
import type { SessionState } from "@/session/SessionProvider";

export type SettingsCategory = {
    key: string;
    title: string;
    description: string;
    href: string;
    icon: Icon;
    group: "Firma" | "Operasyon" | "Finans" | "Kullanıcılar" | "Sistem";
    /** Bu izni olmayan kullanıcı kategoriyi görmez ("*" her izni karşılar). */
    permission?: string;
    /** Bu modül firmada kapalıysa kategori görünmez. */
    module?: string;
};

// Ayarlar merkezi kategorileri; sonraki planlar buraya ekler.
export const settingsCategories: SettingsCategory[] = [
    { key: "firma", title: "Firma Bilgileri", description: "Unvan, vergi ve iletişim bilgileri.", href: "/ayarlar/firma", icon: IconBuilding, group: "Firma", permission: "ayarlar.firma" },
    { key: "genel", title: "Genel Ayarlar", description: "Para birimi, tarih ve numaralandırma tercihleri.", href: "/ayarlar/genel", icon: IconAdjustments, group: "Firma", permission: "ayarlar.genel" },
    { key: "moduller", title: "Modüller", description: "Firmanızda açık olan modülleri yönetin.", href: "/ayarlar/moduller", icon: IconPuzzle, group: "Operasyon", permission: "ayarlar.moduller" },
    { key: "subeler", title: "Şubeler", description: "Şubeleri ve depolarını tanımlayın.", href: "/ayarlar/subeler", icon: IconBuildingStore, group: "Operasyon", permission: "ayarlar.subeler" },
    { key: "katalog", title: "Katalog", description: "Ürün kodu, KDV, birimler, barkod ve özel alanlar.", href: "/ayarlar/katalog", icon: IconPackage, group: "Operasyon", permission: "ayarlar.katalog" },
    { key: "finans", title: "Finans", description: "Vade, iskonto ve tahsilat varsayılanları.", href: "/ayarlar/finans", icon: IconCoin, group: "Finans", permission: "ayarlar.finans" },
    { key: "fiyat", title: "Fiyatlandırma", description: "Varsayılan fiyat listeleri, KDV gösterimi ve yuvarlama kuralı.", href: "/ayarlar/fiyatlandirma", icon: IconTag, group: "Finans", permission: "ayarlar.fiyat" },
    { key: "kullanicilar", title: "Kullanıcılar", description: "Firma üyeleri ve davetler.", href: "/ayarlar/kullanicilar", icon: IconUsers, group: "Kullanıcılar", permission: "members.read" },
    { key: "roller", title: "Roller ve İzinler", description: "Rolleri ve izin matrisini düzenleyin.", href: "/ayarlar/roller", icon: IconShieldLock, group: "Kullanıcılar", permission: "roller.read" },
    { key: "denetim", title: "Denetim Kaydı", description: "Firmada yapılan değişikliklerin geçmişi.", href: "/ayarlar/denetim", icon: IconHistory, group: "Sistem", permission: "denetim.read" },
];

export function visibleCategories(s: Pick<SessionState, "hasPermission" | "hasModule">): SettingsCategory[] {
    return settingsCategories.filter(
        (c) => (!c.permission || s.hasPermission(c.permission)) && (!c.module || s.hasModule(c.module)),
    );
}
