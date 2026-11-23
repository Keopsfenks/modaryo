import { moduleGroups, siteModules } from "./modules";
import { siteSectors } from "./sectors";

export type MenuItem = { href: string; label: string; summary?: string; soon?: boolean };
export type MenuGroup = { title?: string; items: MenuItem[] };

export const featureMenu: MenuGroup[] = moduleGroups.map((group) => ({
    title: group,
    items: siteModules
        .filter((m) => m.group === group)
        .map((m) => ({ href: `/ozellikler/${m.slug}`, label: m.name, summary: m.summary, soon: m.status === "coming" })),
}));

export const solutionMenu: MenuGroup[] = [
    { items: siteSectors.map((s) => ({ href: `/cozumler/${s.slug}`, label: s.name, summary: s.summary })) },
];

export const resourceMenu: MenuGroup[] = [
    {
        items: [
            { href: "/blog", label: "Blog", summary: "Rehberler ve ürün notları." },
            { href: "/yardim", label: "Yardım merkezi", summary: "Sık sorulan sorular ve kullanım rehberleri." },
            { href: "/iletisim", label: "İletişim", summary: "Bize yazın, size dönelim." },
        ],
    },
];

export const footerLegal: MenuItem[] = [
    { href: "/kvkk", label: "KVKK aydınlatma metni" },
    { href: "/gizlilik", label: "Gizlilik politikası" },
    { href: "/cerez-politikasi", label: "Çerez politikası" },
    { href: "/kullanim-sartlari", label: "Kullanım şartları" },
];
