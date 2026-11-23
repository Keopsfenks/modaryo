"use client";

import Link from "next/link";
import { usePathname, useRouter } from "next/navigation";
import Box from "@mui/material/Box";
import List from "@mui/material/List";
import ListItemButton from "@mui/material/ListItemButton";
import ListItemIcon from "@mui/material/ListItemIcon";
import ListItemText from "@mui/material/ListItemText";
import ListSubheader from "@mui/material/ListSubheader";
import MenuItem from "@mui/material/MenuItem";
import Select from "@mui/material/Select";
import { useTheme } from "@mui/material/styles";
import useMediaQuery from "@mui/material/useMediaQuery";
import { visibleCategories, type SettingsCategory } from "@/config/settings";
import { useSession } from "@/session/SessionProvider";

function isActive(pathname: string, category: SettingsCategory) {
    return pathname === category.href || pathname.startsWith(`${category.href}/`);
}

/** Masaüstünde gruplu sol liste, mobilde (< md) üstte açılır liste. */
export default function SettingsNav() {
    const pathname = usePathname();
    const router = useRouter();
    const session = useSession();
    const theme = useTheme();
    const isMobile = useMediaQuery(theme.breakpoints.down("md"));
    const categories = visibleCategories(session);

    if (categories.length === 0) {
        return null;
    }

    if (isMobile) {
        const current = categories.find((c) => isActive(pathname, c));
        return (
            <Select
                fullWidth
                size="small"
                value={current?.href ?? ""}
                displayEmpty
                onChange={(e) => router.push(e.target.value)}
                inputProps={{ "aria-label": "Ayar kategorisi" }}
                sx={{ mb: 3 }}
            >
                {categories.map((c) => (
                    <MenuItem key={c.key} value={c.href}>
                        {c.title}
                    </MenuItem>
                ))}
            </Select>
        );
    }

    const groups = categories.reduce<Map<string, SettingsCategory[]>>((acc, c) => {
        acc.set(c.group, [...(acc.get(c.group) ?? []), c]);
        return acc;
    }, new Map());

    return (
        <Box component="nav" aria-label="Ayarlar" sx={{ width: 260, flexShrink: 0 }}>
            {[...groups.entries()].map(([group, items]) => (
                <List
                    key={group}
                    role="group"
                    aria-label={group}
                    disablePadding
                    subheader={
                        <ListSubheader
                            disableSticky
                            sx={{ px: 1.5, pt: 2, pb: 0.5, lineHeight: 1.6, fontSize: "0.75rem", fontWeight: 700, textTransform: "uppercase", bgcolor: "transparent" }}
                        >
                            {group}
                        </ListSubheader>
                    }
                >
                    {items.map((c) => {
                        const Icon = c.icon;
                        return (
                            <ListItemButton
                                key={c.key}
                                component={Link}
                                href={c.href}
                                selected={isActive(pathname, c)}
                                sx={{ borderRadius: "7px", mb: 0.5, py: 1, px: 1.5, color: "text.secondary" }}
                            >
                                <ListItemIcon sx={{ minWidth: 36, color: "inherit" }}>
                                    <Icon size={20} stroke={1.6} />
                                </ListItemIcon>
                                <ListItemText primary={c.title} slotProps={{ primary: { sx: { fontSize: "0.875rem", fontWeight: 500 } } }} />
                            </ListItemButton>
                        );
                    })}
                </List>
            ))}
        </Box>
    );
}
