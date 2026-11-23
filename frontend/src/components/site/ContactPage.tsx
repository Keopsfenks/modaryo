import { Suspense } from "react";
import Box from "@mui/material/Box";
import Container from "@mui/material/Container";
import Typography from "@mui/material/Typography";
import type { ContactInput } from "@/lib/api/site";
import ContactForm from "./ContactForm";

type Props = { topic: ContactInput["topic"]; title: string; lead: string; points: string[] };

/** /demo ve /iletisim ortak düzeni: solda kısa anlatım, sağda form. */
export default function ContactPage({ topic, title, lead, points }: Props) {
    return (
        <Box component="section" aria-labelledby="iletisim-baslik" sx={{ py: { xs: 6, md: 10 } }}>
            <Container maxWidth="lg" sx={{ display: "grid", gap: { xs: 5, md: 10 }, gridTemplateColumns: { xs: "1fr", md: "5fr 6fr" }, alignItems: "start" }}>
                <Box>
                    <Typography id="iletisim-baslik" component="h1" sx={{ fontSize: "clamp(2rem, 4.2vw, 3rem)", fontWeight: 800, lineHeight: 1.1, letterSpacing: "-0.03em", mb: 2 }}>
                        {title}
                    </Typography>
                    <Typography sx={{ color: "text.secondary", fontSize: "1.125rem", lineHeight: 1.6, maxWidth: 440, mb: 3 }}>{lead}</Typography>
                    <Box component="ul" sx={{ m: 0, p: 0, listStyle: "none", maxWidth: 440 }}>
                        {points.map((p) => (
                            <Box key={p} component="li" sx={{ py: 1.25, borderTop: "1px solid", borderColor: "divider", color: "text.secondary", lineHeight: 1.5 }}>{p}</Box>
                        ))}
                    </Box>
                </Box>
                <Box sx={{ position: "relative" }}>
                    <Suspense fallback={null}>
                        <ContactForm topic={topic} />
                    </Suspense>
                </Box>
            </Container>
        </Box>
    );
}
