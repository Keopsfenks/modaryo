import Box from "@mui/material/Box";
import SettingsNav from "@/components/settings/SettingsNav";

export default function SettingsLayout({ children }: LayoutProps<"/ayarlar">) {
    return (
        <Box sx={{ display: "flex", flexDirection: { xs: "column", md: "row" }, gap: { md: 4 } }}>
            <SettingsNav />
            <Box sx={{ flexGrow: 1, minWidth: 0 }}>{children}</Box>
        </Box>
    );
}
