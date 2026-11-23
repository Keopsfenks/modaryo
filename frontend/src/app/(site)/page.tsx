import type { Metadata } from "next";
import Container from "@mui/material/Container";
import Typography from "@mui/material/Typography";
import CtaButtons from "@/components/site/CtaButtons";

export const metadata: Metadata = {
    title: { absolute: "Modaryo ERP: cari, stok, satış ve muhasebe tek yerde" },
    description: "Modaryo, işletmenizin cari, stok, satış ve muhasebe işlerini tek panelde toplar.",
};

/** Yer tutucu; ana sayfa içeriği Görev 3'te gelir. */
export default function HomePage() {
    return (
        <Container maxWidth="lg" sx={{ py: { xs: 8, md: 12 } }}>
            <Typography component="h1" sx={{ fontSize: "clamp(2rem, 5vw, 3.5rem)", fontWeight: 700, lineHeight: 1.1, letterSpacing: "-0.03em", maxWidth: 720, mb: 3 }}>
                İşletmenizin defteri, tek panelde
            </Typography>
            <CtaButtons />
        </Container>
    );
}
