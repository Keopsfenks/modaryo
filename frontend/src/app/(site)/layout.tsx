import { cookies } from "next/headers";
import Box from "@mui/material/Box";
import SiteFooter from "@/components/site/SiteFooter";
import SiteHeader from "@/components/site/SiteHeader";

/**
 * Herkese açık tanıtım sitesi. Oturum çerezi varsa üst menüde "Panele git" gösterilir;
 * çerezin geçerliliğini panel kendi doğrular (proxy yalnızca varlığına bakar).
 */
export default async function SiteLayout({ children }: LayoutProps<"/">) {
    const hasSession = (await cookies()).has("modaryo_sid");

    return (
        <Box sx={{ minHeight: "100vh", display: "flex", flexDirection: "column", bgcolor: "background.default" }}>
            <Box
                component="a"
                href="#icerik"
                sx={{
                    position: "absolute",
                    left: 8,
                    top: -48,
                    zIndex: 2000,
                    px: 2,
                    py: 1,
                    bgcolor: "background.paper",
                    color: "text.primary",
                    borderRadius: 1,
                    "&:focus": { top: 8 },
                }}
            >
                İçeriğe geç
            </Box>
            <SiteHeader hasSession={hasSession} />
            <Box component="main" id="icerik" sx={{ flexGrow: 1 }}>
                {children}
            </Box>
            <SiteFooter />
        </Box>
    );
}
