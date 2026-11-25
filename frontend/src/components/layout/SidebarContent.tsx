"use client";

import { useEffect, useState } from "react";
import Link from "next/link";
import { usePathname } from "next/navigation";
import Avatar from "@mui/material/Avatar";
import Box from "@mui/material/Box";
import Chip from "@mui/material/Chip";
import IconButton from "@mui/material/IconButton";
import List from "@mui/material/List";
import ListItemButton from "@mui/material/ListItemButton";
import ListItemIcon from "@mui/material/ListItemIcon";
import ListItemText from "@mui/material/ListItemText";
import ListSubheader from "@mui/material/ListSubheader";
import Tooltip from "@mui/material/Tooltip";
import Typography from "@mui/material/Typography";
import { IconPower } from "@tabler/icons-react";
import { navigation } from "@/config/navigation";
import { visibleCategories } from "@/config/settings";
import { erp } from "@/lib/api/erp";
import type { PurchasingSettings } from "@/lib/api/purchasing";
import { useSession } from "@/session/SessionProvider";
import { initials } from "./Header";
import Logo from "./Logo";

type SidebarContentProps = {
    onNavigate?: () => void;
};

export default function SidebarContent({ onNavigate }: SidebarContentProps) {
    const pathname = usePathname();
    const { session, logout, hasPermission, hasModule } = useSession();
    const canSeeSettings = visibleCategories({ hasPermission, hasModule }).length > 0;
    // Karantina öğesi yalnızca firmada karantina açıksa görünür; ayar yalnızca öğe yetki süzgecinden geçiyorsa okunur.
    const wantsQuarantine = navigation.some((s) => s.items.some((i) => i.requiresQuarantine && (!i.permission || hasPermission(i.permission))));
    const [quarantineEnabled, setQuarantineEnabled] = useState(false);
    useEffect(() => {
        if (!wantsQuarantine) return;
        let cancelled = false;
        erp.settings.get<PurchasingSettings>("satinalma").then(
            (s) => !cancelled && setQuarantineEnabled(s.value.quarantineEnabled === true),
            () => undefined,
        );
        return () => {
            cancelled = true;
        };
    }, [wantsQuarantine]);
    const visibleNavigation = navigation
        .map((section) => ({ ...section, items: section.items.filter(
                (item) =>
                    (!item.permission || hasPermission(item.permission)) &&
                    (!item.alsoPermission || hasPermission(item.alsoPermission)) &&
                    (!item.module || hasModule(item.module)) &&
                    (!item.requiresQuarantine || quarantineEnabled) &&
                    (item.href !== "/ayarlar" || canSeeSettings),
            ) }))
        .filter((section) => section.items.length > 0);

    return (
        <Box sx={{ display: "flex", flexDirection: "column", height: "100%" }}>
            <Box sx={{ px: 3, height: 70, display: "flex", alignItems: "center", flexShrink: 0 }}>
                <Logo />
            </Box>

            <Box component="nav" aria-label="Ana menü" sx={{ flexGrow: 1, overflowY: "auto", px: 3, pb: 2 }}>
                {visibleNavigation.map((section) => (
                    <List
                        key={section.title}
                        disablePadding
                        subheader={
                            <ListSubheader
                                disableSticky
                                sx={{
                                    px: 1.5,
                                    pt: 2.5,
                                    pb: 1,
                                    lineHeight: 1.6,
                                    fontSize: "0.75rem",
                                    fontWeight: 700,
                                    textTransform: "uppercase",
                                    color: "text.primary",
                                    bgcolor: "transparent",
                                }}
                            >
                                {section.title}
                            </ListSubheader>
                        }
                    >
                        {section.items.map((item) => {
                            const selected = pathname === item.href;
                            const ItemIcon = item.icon;
                            return (
                                <ListItemButton
                                    key={item.href}
                                    component={Link}
                                    href={item.href}
                                    selected={selected}
                                    onClick={onNavigate}
                                    sx={{
                                        borderRadius: "7px",
                                        mb: 0.5,
                                        py: 1,
                                        px: 1.5,
                                        color: "text.secondary",
                                        "&:hover": { bgcolor: "primary.light", color: "primary.main" },
                                        "&.Mui-selected, &.Mui-selected:hover": {
                                            bgcolor: "primary.main",
                                            color: "primary.contrastText",
                                        },
                                    }}
                                >
                                    <ListItemIcon sx={{ minWidth: 36, color: "inherit" }}>
                                        <ItemIcon size={20} stroke={1.6} />
                                    </ListItemIcon>
                                    <ListItemText
                                        primary={item.title}
                                        slotProps={{ primary: { sx: { fontSize: "0.875rem", fontWeight: 500 } } }}
                                    />
                                    {item.badge && (
                                        <Chip
                                            label={item.badge}
                                            size="small"
                                            color={selected ? "default" : "secondary"}
                                            sx={{ height: 20, fontSize: "0.7rem" }}
                                        />
                                    )}
                                </ListItemButton>
                            );
                        })}
                    </List>
                ))}
            </Box>

            <Box sx={{ p: 2, flexShrink: 0 }}>
                <Box
                    sx={{
                        display: "flex",
                        alignItems: "center",
                        gap: 1.5,
                        p: 2,
                        borderRadius: "7px",
                        bgcolor: "secondary.light",
                    }}
                >
                    <Avatar sx={{ bgcolor: "primary.main", width: 40, height: 40, fontSize: "0.9rem" }}>
                        {initials(session.user.fullName)}
                    </Avatar>
                    <Box sx={{ minWidth: 0, flexGrow: 1 }}>
                        <Typography variant="h6" noWrap>
                            {session.user.fullName}
                        </Typography>
                        <Typography variant="body2" color="text.secondary" noWrap>
                            {session.activeCompany?.name ?? session.user.email}
                        </Typography>
                    </Box>
                    <Tooltip title="Çıkış yap">
                        <IconButton size="small" color="primary" aria-label="Çıkış yap" onClick={() => void logout()}>
                            <IconPower size={18} />
                        </IconButton>
                    </Tooltip>
                </Box>
            </Box>
        </Box>
    );
}
