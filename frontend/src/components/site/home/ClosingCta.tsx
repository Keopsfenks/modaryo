import Box from "@mui/material/Box";
import Container from "@mui/material/Container";
import Typography from "@mui/material/Typography";
import CtaButtons from "../CtaButtons";

export default function ClosingCta() {
    return (
        <Box component="section" aria-labelledby="kapanis-baslik" sx={{ py: { xs: 8, md: 12 }, borderTop: "1px solid", borderColor: "divider" }}>
            <Container maxWidth="lg">
                <Typography id="kapanis-baslik" component="h2" sx={{ fontSize: "clamp(1.75rem, 3.6vw, 2.5rem)", fontWeight: 800, lineHeight: 1.15, letterSpacing: "-0.02em", maxWidth: 560, mb: 1.5 }}>
                    Bugün ücretsiz başlayın
                </Typography>
                <Typography sx={{ color: "text.secondary", fontSize: "1.0625rem", mb: 3.5, maxWidth: 520 }}>Firmanızı oluşturun, carilerinizi ekleyin. Kart bilgisi istemiyoruz.</Typography>
                <CtaButtons />
            </Container>
        </Box>
    );
}
