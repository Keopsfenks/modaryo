import type { ReactNode } from "react";
import Box from "@mui/material/Box";
import Container from "@mui/material/Container";
import Typography from "@mui/material/Typography";

type SectionProps = {
    id: string;
    title: string;
    intro?: string;
    /** "band": defter şeridi (gri zemin). */
    tone?: "plain" | "band";
    children: ReactNode;
};

/** Ana sayfa bölümü: çizgili ayraç, h2 başlık ve isteğe bağlı giriş cümlesi. Bölüm landmark'tır. */
export default function Section({ id, title, intro, tone = "plain", children }: SectionProps) {
    return (
        <Box
            component="section"
            aria-labelledby={`${id}-baslik`}
            sx={{ py: { xs: 7, md: 10 }, borderTop: "1px solid", borderColor: "divider", bgcolor: tone === "band" ? "grey.100" : "background.default" }}
        >
            <Container maxWidth="lg">
                <Typography id={`${id}-baslik`} component="h2" sx={{ fontSize: "clamp(1.625rem, 3.2vw, 2.25rem)", fontWeight: 700, lineHeight: 1.15, letterSpacing: "-0.02em", maxWidth: 640 }}>
                    {title}
                </Typography>
                {intro && (
                    <Typography sx={{ mt: 1.5, color: "text.secondary", fontSize: "1.0625rem", lineHeight: 1.6, maxWidth: 600 }}>{intro}</Typography>
                )}
                <Box sx={{ mt: { xs: 4, md: 6 } }}>{children}</Box>
            </Container>
        </Box>
    );
}
