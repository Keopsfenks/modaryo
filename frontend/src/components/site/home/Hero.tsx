import Box from "@mui/material/Box";
import Container from "@mui/material/Container";
import Typography from "@mui/material/Typography";
import { hero } from "@/content/site/home";
import CtaButtons from "../CtaButtons";
import DashboardShot from "../shots/DashboardShot";

export default function Hero() {
    return (
        <Box component="section" aria-labelledby="hero-baslik" sx={{ py: { xs: 6, md: 10 } }}>
            <Container maxWidth="lg">
                <Box sx={{ display: "grid", gridTemplateColumns: { xs: "1fr", md: "minmax(0, 1.05fr) minmax(0, 1fr)" }, gap: { xs: 5, md: 8 }, alignItems: "center" }}>
                    <Box>
                        <Typography id="hero-baslik" component="h1" sx={{ fontSize: "clamp(2.125rem, 5vw, 3.5rem)", fontWeight: 800, lineHeight: 1.08, letterSpacing: "-0.03em", mb: 2.5 }}>
                            {hero.title}
                        </Typography>
                        <Typography sx={{ color: "text.secondary", fontSize: "1.125rem", lineHeight: 1.6, maxWidth: 520, mb: 4 }}>{hero.subline}</Typography>
                        <CtaButtons />
                    </Box>
                    <DashboardShot />
                </Box>
            </Container>
        </Box>
    );
}
