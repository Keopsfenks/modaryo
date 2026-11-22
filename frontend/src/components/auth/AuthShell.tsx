import Box from "@mui/material/Box";
import Logo from "@/components/layout/Logo";
import ThemeModeToggle from "@/components/layout/ThemeModeToggle";

/** Panel dışı sayfaların (giriş, kayıt, başlangıç...) ortalanmış düzeni. */
export default function AuthShell({ children }: { children: React.ReactNode }) {
    return (
        <Box
            sx={{
                minHeight: "100vh",
                display: "flex",
                flexDirection: "column",
                bgcolor: "grey.100",
            }}
        >
            <Box sx={{ display: "flex", alignItems: "center", justifyContent: "space-between", px: { xs: 2, sm: 4 }, py: 2 }}>
                <Logo />
                <ThemeModeToggle />
            </Box>
            <Box
                component="main"
                sx={{ flexGrow: 1, display: "flex", alignItems: { xs: "flex-start", sm: "center" }, justifyContent: "center", px: 2, pb: 6 }}
            >
                <Box sx={{ width: "100%", maxWidth: 440 }}>{children}</Box>
            </Box>
        </Box>
    );
}
