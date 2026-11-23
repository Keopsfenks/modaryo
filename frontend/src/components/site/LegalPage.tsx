import type { ReactNode } from "react";
import Alert from "@mui/material/Alert";
import Container from "@mui/material/Container";
import Typography from "@mui/material/Typography";
import Prose from "./Prose";

export const DRAFT_NOTICE = "Bu metin taslaktır; yayın öncesi hukuki kontrolden geçirilmelidir.";

/** Yasal metin sayfası. Taslak uyarısı her zaman en üstte görünür. */
export default function LegalPage({ title, children }: { title: string; children: ReactNode }) {
    return (
        <Container maxWidth="lg" sx={{ py: { xs: 6, md: 10 } }}>
            <Alert severity="warning" sx={{ mb: 4, maxWidth: 720 }}>{DRAFT_NOTICE}</Alert>
            <Typography component="h1" sx={{ fontSize: "clamp(2rem, 4.6vw, 3rem)", fontWeight: 800, lineHeight: 1.1, letterSpacing: "-0.03em", mb: 3 }}>
                {title}
            </Typography>
            <Prose>{children}</Prose>
        </Container>
    );
}
