"use client";

import { useState } from "react";
import Box from "@mui/material/Box";
import Container from "@mui/material/Container";
import Drawer from "@mui/material/Drawer";
import useMediaQuery from "@mui/material/useMediaQuery";
import { useTheme } from "@mui/material/styles";
import Header from "./Header";
import SidebarContent from "./SidebarContent";

export const SIDEBAR_WIDTH = 270;

const drawerPaperSx = {
    width: SIDEBAR_WIDTH,
    boxSizing: "border-box",
    borderRight: "1px solid",
    borderColor: "divider",
    bgcolor: "background.default",
} as const;

export default function PanelShell({ children }: { children: React.ReactNode }) {
    const theme = useTheme();
    const isDesktop = useMediaQuery(theme.breakpoints.up("lg"));
    const [desktopOpen, setDesktopOpen] = useState(true);
    const [mobileOpen, setMobileOpen] = useState(false);

    const toggleSidebar = () => {
        if (isDesktop) {
            setDesktopOpen((open) => !open);
        } else {
            setMobileOpen((open) => !open);
        }
    };

    return (
        <Box sx={{ display: "flex", minHeight: "100vh" }}>
            {/* Masaüstü: kalıcı menü, başlıktaki butonla gizlenebilir */}
            <Box
                component="aside"
                sx={{
                    display: { xs: "none", lg: "block" },
                    width: desktopOpen ? SIDEBAR_WIDTH : 0,
                    flexShrink: 0,
                    transition: theme.transitions.create("width", {
                        duration: theme.transitions.duration.shortest,
                    }),
                }}
            >
                <Drawer
                    variant="persistent"
                    open={desktopOpen}
                    slotProps={{ paper: { sx: drawerPaperSx } }}
                >
                    <SidebarContent />
                </Drawer>
            </Box>

            {/* Mobil / tablet: üstte açılan geçici menü */}
            <Drawer
                variant="temporary"
                open={mobileOpen}
                onClose={() => setMobileOpen(false)}
                ModalProps={{ keepMounted: true }}
                sx={{ display: { xs: "block", lg: "none" } }}
                slotProps={{ paper: { sx: drawerPaperSx } }}
            >
                <SidebarContent onNavigate={() => setMobileOpen(false)} />
            </Drawer>

            <Box sx={{ display: "flex", flexDirection: "column", flexGrow: 1, minWidth: 0 }}>
                <Header onToggleSidebar={toggleSidebar} />
                <Container component="main" maxWidth="lg" sx={{ py: 3, flexGrow: 1 }}>
                    {children}
                </Container>
            </Box>
        </Box>
    );
}
