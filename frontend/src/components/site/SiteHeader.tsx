"use client";

/*
 * Tanıtım sitesinin görsel yönü ("defter"):
 * - Yazı: panelle aynı Plus Jakarta Sans (next/font, yazı tipi kayması yok). Başlıklar sıkı satır aralığıyla
 *   büyük (clamp), gövde 16px/1.6; tek aile, ağırlıkla hiyerarşi.
 * - Vurgu: yalnızca marka mavisi (primary) — birincil çağrı ve odak/aktif durumlarında. Diğer her şey nötr.
 * - Ritim: bölümler beyaz ile grey.100 bantları arasında dönüşür, ayrımı 1px divider çizgisi yapar
 *   (muhasebe defterindeki çizgili satır hissi). Gölge yok; kartlar çizgiyle çevrilir.
 * - Tekrar eden motif: "(B)" / "(A)" bakiye etiketleri; ürün görselleri bu dili taşır.
 * - Hareket: kullanıcı eylemine bağlı olanlar dışında yok.
 */

import { useState } from "react";
import Link from "next/link";
import Box from "@mui/material/Box";
import Button from "@mui/material/Button";
import Collapse from "@mui/material/Collapse";
import Container from "@mui/material/Container";
import Divider from "@mui/material/Divider";
import Drawer from "@mui/material/Drawer";
import IconButton from "@mui/material/IconButton";
import Stack from "@mui/material/Stack";
import Typography from "@mui/material/Typography";
import { IconChevronDown, IconMenu2, IconX } from "@tabler/icons-react";
import { featureMenu, resourceMenu, solutionMenu, type MenuGroup } from "@/content/site/navigation";
import Logo from "@/components/layout/Logo";
import ThemeModeToggle from "@/components/layout/ThemeModeToggle";
import { PrimaryCta } from "./CtaButtons";
import MegaMenu from "./MegaMenu";

type SiteHeaderProps = { hasSession: boolean };

const menus: { label: string; groups: MenuGroup[] }[] = [
    { label: "Özellikler", groups: featureMenu },
    { label: "Çözümler", groups: solutionMenu },
];

function MobileSection({ label, groups, onNavigate }: { label: string; groups: MenuGroup[]; onNavigate: () => void }) {
    const [open, setOpen] = useState(false);

    return (
        <Box>
            <Button
                fullWidth
                color="inherit"
                aria-expanded={open}
                onClick={() => setOpen((v) => !v)}
                endIcon={<IconChevronDown size={16} aria-hidden style={{ transform: open ? "rotate(180deg)" : undefined }} />}
                sx={{ justifyContent: "space-between", py: 1.25, fontWeight: 600, color: "text.primary" }}
            >
                {label}
            </Button>
            <Collapse in={open} unmountOnExit>
                <Box sx={{ pl: 1.5, pb: 1 }}>
                    {groups.map((group, index) => (
                        <Box key={group.title ?? index} sx={{ mb: 1 }}>
                            {group.title && (
                                <Typography variant="subtitle2" color="text.secondary" sx={{ px: 1, py: 0.5 }}>
                                    {group.title}
                                </Typography>
                            )}
                            {group.items.map((item) => (
                                <Box
                                    key={item.href}
                                    component={Link}
                                    href={item.href}
                                    onClick={onNavigate}
                                    sx={{ display: "block", px: 1, py: 0.75, color: "text.primary", textDecoration: "none", borderRadius: 1, "&:hover": { bgcolor: "action.hover" } }}
                                >
                                    {item.label}
                                </Box>
                            ))}
                        </Box>
                    ))}
                </Box>
            </Collapse>
        </Box>
    );
}

export default function SiteHeader({ hasSession }: SiteHeaderProps) {
    const [drawerOpen, setDrawerOpen] = useState(false);
    const closeDrawer = () => setDrawerOpen(false);

    const authActions = hasSession ? (
        <Button component={Link} href="/panel" variant="contained">
            Panele git
        </Button>
    ) : (
        <>
            <Button component={Link} href="/giris" color="inherit" sx={{ color: "text.primary" }}>
                Giriş yap
            </Button>
            <PrimaryCta />
        </>
    );

    return (
        <Box
            component="header"
            sx={{
                position: "sticky",
                top: 0,
                zIndex: "appBar",
                bgcolor: "background.default",
                borderBottom: "1px solid",
                borderColor: "divider",
            }}
        >
            <Container maxWidth="lg" sx={{ display: "flex", alignItems: "center", gap: 2, minHeight: 68 }}>
                <Logo href="/" />
                <Box component="nav" aria-label="Ana menü" sx={{ display: { xs: "none", md: "flex" }, alignItems: "center", gap: 0.5, ml: 2 }}>
                    {menus.map((menu) => (
                        <MegaMenu key={menu.label} label={menu.label} groups={menu.groups} />
                    ))}
                    <Button component={Link} href="/fiyatlar" color="inherit" sx={{ color: "text.primary", fontWeight: 500, px: 1.5 }}>
                        Fiyatlar
                    </Button>
                    <MegaMenu label="Kaynaklar" groups={resourceMenu} />
                </Box>
                <Box sx={{ flexGrow: 1 }} />
                <Stack direction="row" spacing={1} sx={{ alignItems: "center" }}>
                    <ThemeModeToggle />
                    <Stack direction="row" spacing={1} sx={{ alignItems: "center", display: { xs: "none", md: "flex" } }}>
                        {authActions}
                    </Stack>
                    <IconButton
                        aria-label="Menüyü aç"
                        onClick={() => setDrawerOpen(true)}
                        sx={{ display: { xs: "inline-flex", md: "none" } }}
                    >
                        <IconMenu2 size={22} aria-hidden />
                    </IconButton>
                </Stack>
            </Container>

            <Drawer anchor="right" open={drawerOpen} onClose={closeDrawer} slotProps={{ paper: { sx: { width: "min(360px, 100vw)" }, "aria-label": "Menü" } }}>
                <Stack direction="row" sx={{ p: 2, alignItems: "center", justifyContent: "space-between" }}>
                    <Logo href="/" />
                    <IconButton aria-label="Menüyü kapat" onClick={closeDrawer}>
                        <IconX size={22} aria-hidden />
                    </IconButton>
                </Stack>
                <Divider />
                <Box component="nav" aria-label="Mobil menü" sx={{ p: 2, overflowY: "auto" }}>
                    {menus.map((menu) => (
                        <MobileSection key={menu.label} label={menu.label} groups={menu.groups} onNavigate={closeDrawer} />
                    ))}
                    <Button component={Link} href="/fiyatlar" onClick={closeDrawer} fullWidth color="inherit" sx={{ justifyContent: "flex-start", py: 1.25, fontWeight: 600, color: "text.primary" }}>
                        Fiyatlar
                    </Button>
                    <MobileSection label="Kaynaklar" groups={resourceMenu} onNavigate={closeDrawer} />
                </Box>
                <Divider />
                <Stack spacing={1.5} sx={{ p: 2 }} onClick={closeDrawer}>
                    {hasSession ? (
                        <Button component={Link} href="/panel" variant="contained">
                            Panele git
                        </Button>
                    ) : (
                        <>
                            <PrimaryCta fullWidth />
                            <Button component={Link} href="/giris" variant="outlined" color="inherit" fullWidth>
                                Giriş yap
                            </Button>
                        </>
                    )}
                </Stack>
            </Drawer>
        </Box>
    );
}
