"use client";

import { useState } from "react";
import Link from "next/link";
import AppBar from "@mui/material/AppBar";
import Avatar from "@mui/material/Avatar";
import Badge from "@mui/material/Badge";
import Box from "@mui/material/Box";
import Divider from "@mui/material/Divider";
import IconButton from "@mui/material/IconButton";
import InputBase from "@mui/material/InputBase";
import ListItemIcon from "@mui/material/ListItemIcon";
import ListItemText from "@mui/material/ListItemText";
import ListSubheader from "@mui/material/ListSubheader";
import Menu from "@mui/material/Menu";
import MenuItem from "@mui/material/MenuItem";
import Toolbar from "@mui/material/Toolbar";
import Tooltip from "@mui/material/Tooltip";
import Typography from "@mui/material/Typography";
import { IconBell, IconBuilding, IconCheck, IconLogout, IconMenu2, IconSearch, IconUser } from "@tabler/icons-react";
import { useSession } from "@/session/SessionProvider";
import ThemeModeToggle from "./ThemeModeToggle";

type HeaderProps = {
    onToggleSidebar: () => void;
};

export function initials(fullName: string) {
    return fullName
        .split(/\s+/)
        .filter(Boolean)
        .slice(0, 2)
        .map((part) => part.charAt(0))
        .join("")
        .toLocaleUpperCase("tr-TR");
}

export default function Header({ onToggleSidebar }: HeaderProps) {
    const { session, logout, switchTenant } = useSession();
    const [profileAnchor, setProfileAnchor] = useState<HTMLElement | null>(null);
    const closeMenu = () => setProfileAnchor(null);
    const hasManyCompanies = session.companies.length > 1;

    return (
        <AppBar
            className="no-print"
            position="sticky"
            color="inherit"
            elevation={0}
            sx={{
                bgcolor: "background.default",
                color: "text.secondary",
                backdropFilter: "blur(4px)",
            }}
        >
            <Toolbar sx={{ minHeight: { xs: 64, lg: 70 }, gap: 1 }}>
                <IconButton color="inherit" aria-label="Menüyü aç/kapat" onClick={onToggleSidebar}>
                    <IconMenu2 size={21} stroke={1.5} />
                </IconButton>

                <Box
                    sx={{
                        display: { xs: "none", sm: "flex" },
                        alignItems: "center",
                        gap: 1,
                        px: 1.5,
                        py: 0.5,
                        borderRadius: "7px",
                        bgcolor: "grey.100",
                        width: 280,
                    }}
                >
                    <IconSearch size={18} stroke={1.5} />
                    <InputBase
                        placeholder="Müşteri, sipariş, ürün ara…"
                        inputProps={{ "aria-label": "Ara" }}
                        sx={{ flexGrow: 1, fontSize: "0.875rem" }}
                    />
                </Box>

                <Box sx={{ flexGrow: 1 }} />

                {session.activeCompany && (
                    <Typography
                        variant="subtitle2"
                        sx={{ display: { xs: "none", md: "block" }, color: "text.primary", fontWeight: 600, mr: 1 }}
                    >
                        {session.activeCompany.name}
                    </Typography>
                )}

                <ThemeModeToggle />

                <Tooltip title="Bildirimler">
                    <IconButton color="inherit" aria-label="Bildirimler">
                        <Badge variant="dot" color="primary">
                            <IconBell size={21} stroke={1.5} />
                        </Badge>
                    </IconButton>
                </Tooltip>

                <IconButton
                    aria-label="Profil menüsü"
                    aria-controls={profileAnchor ? "profil-menu" : undefined}
                    aria-haspopup="true"
                    onClick={(event) => setProfileAnchor(event.currentTarget)}
                    sx={{ ml: 0.5 }}
                >
                    <Avatar sx={{ width: 35, height: 35, bgcolor: "primary.main", fontSize: "0.85rem" }}>
                        {initials(session.user.fullName)}
                    </Avatar>
                </IconButton>

                <Menu
                    id="profil-menu"
                    anchorEl={profileAnchor}
                    open={Boolean(profileAnchor)}
                    onClose={closeMenu}
                    anchorOrigin={{ vertical: "bottom", horizontal: "right" }}
                    transformOrigin={{ vertical: "top", horizontal: "right" }}
                    slotProps={{ paper: { sx: { width: 260, mt: 1 } } }}
                >
                    <Box sx={{ px: 2, py: 1.5 }}>
                        <Typography variant="h6">{session.user.fullName}</Typography>
                        <Typography variant="body2" color="text.secondary">
                            {session.user.email}
                        </Typography>
                    </Box>
                    <Divider />
                    <MenuItem component={Link} href="/hesap" onClick={closeMenu}>
                        <ListItemIcon>
                            <IconUser size={18} />
                        </ListItemIcon>
                        Profilim
                    </MenuItem>

                    {hasManyCompanies && <Divider />}
                    {hasManyCompanies && (
                        <ListSubheader sx={{ lineHeight: 2.5, bgcolor: "transparent" }}>Firma değiştir</ListSubheader>
                    )}
                    {hasManyCompanies &&
                        session.companies.map((company) => {
                            const active = company.id === session.activeCompany?.id;
                            return (
                                <MenuItem
                                    key={company.id}
                                    selected={active}
                                    onClick={async () => {
                                        closeMenu();
                                        if (!active) await switchTenant(company.id);
                                    }}
                                >
                                    <ListItemIcon>{active ? <IconCheck size={18} /> : <IconBuilding size={18} />}</ListItemIcon>
                                    <ListItemText primary={company.name} secondary={company.roleName} />
                                </MenuItem>
                            );
                        })}

                    <Divider />
                    <MenuItem
                        onClick={async () => {
                            closeMenu();
                            await logout();
                        }}
                    >
                        <ListItemIcon>
                            <IconLogout size={18} />
                        </ListItemIcon>
                        Çıkış Yap
                    </MenuItem>
                </Menu>
            </Toolbar>
        </AppBar>
    );
}
