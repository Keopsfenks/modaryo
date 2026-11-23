import { LinkBox } from "@/components/site/LinkBox";
import Box from "@mui/material/Box";
import Container from "@mui/material/Container";
import Typography from "@mui/material/Typography";
import { footerLegal, resourceMenu, solutionMenu, type MenuItem } from "@/content/site/navigation";
import { siteModules } from "@/content/site/modules";
import Logo from "@/components/layout/Logo";

function FooterColumn({ title, items }: { title: string; items: MenuItem[] }) {
    return (
        <Box component="nav" aria-label={title}>
            <Typography component="h2" sx={{ fontWeight: 700, fontSize: "0.9rem", mb: 1.5 }}>
                {title}
            </Typography>
            <Box component="ul" sx={{ listStyle: "none", m: 0, p: 0, display: "grid", gap: 1 }}>
                {items.map((item) => (
                    <li key={item.href}>
                        <LinkBox
                            href={item.href}
                            sx={{ color: "text.secondary", textDecoration: "none", fontSize: "0.875rem", "&:hover": { color: "text.primary", textDecoration: "underline" } }}
                        >
                            {item.label}
                        </LinkBox>
                    </li>
                ))}
            </Box>
        </Box>
    );
}

const productLinks: MenuItem[] = [
    { href: "/ozellikler", label: "Tüm özellikler" },
    ...siteModules.filter((m) => m.status === "available").map((m) => ({ href: `/ozellikler/${m.slug}`, label: m.name })),
    { href: "/fiyatlar", label: "Fiyatlar" },
    { href: "/demo", label: "Demo iste" },
];

export default function SiteFooter() {
    return (
        <Box component="footer" sx={{ borderTop: "1px solid", borderColor: "divider", bgcolor: "grey.100", mt: "auto" }}>
            <Container maxWidth="lg" sx={{ py: { xs: 5, md: 7 } }}>
                <Box sx={{ display: "grid", gap: 4, gridTemplateColumns: { xs: "1fr 1fr", md: "1.4fr repeat(4, 1fr)" } }}>
                    <Box sx={{ gridColumn: { xs: "1 / -1", md: "auto" } }}>
                        <Logo href="/" />
                        <Typography color="text.secondary" sx={{ mt: 1.5, maxWidth: 280, fontSize: "0.875rem", lineHeight: 1.6 }}>
                            Küçük ve orta ölçekli işletmeler için cari, stok, satış ve muhasebeyi tek yerde toplayan ERP.
                        </Typography>
                    </Box>
                    <FooterColumn title="Ürün" items={productLinks} />
                    <FooterColumn title="Çözümler" items={solutionMenu[0].items} />
                    <FooterColumn title="Kaynaklar" items={resourceMenu[0].items} />
                    <FooterColumn title="Yasal" items={footerLegal} />
                </Box>
                <Typography color="text.secondary" sx={{ mt: 5, fontSize: "0.8125rem" }}>
                    © {new Date().getFullYear()} Modaryo. Tüm hakları saklıdır.
                </Typography>
            </Container>
        </Box>
    );
}
