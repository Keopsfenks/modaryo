import type { ReactNode } from "react";
import Alert from "@mui/material/Alert";
import Container from "@mui/material/Container";
import Typography from "@mui/material/Typography";
import Prose from "./Prose";

export const SAMPLE_NOTICE = "Bu içerik örnektir; gerçek bir yazı veya rehber değildir.";

export default function ArticleLayout({ title, date, children }: { title: string; date?: string; children: ReactNode }) {
    return (
        <Container maxWidth="lg" component="article" sx={{ py: { xs: 6, md: 10 } }}>
            <Alert severity="info" sx={{ mb: 4, maxWidth: 720 }}>{SAMPLE_NOTICE}</Alert>
            <Typography component="h1" sx={{ fontSize: "clamp(2rem, 4.6vw, 3rem)", fontWeight: 800, lineHeight: 1.1, letterSpacing: "-0.03em", mb: 1 }}>
                {title}
            </Typography>
            {date && (
                <Typography component="time" dateTime={date} sx={{ display: "block", color: "text.secondary", mb: 3 }}>
                    {new Date(date).toLocaleDateString("tr-TR", { dateStyle: "long" })}
                </Typography>
            )}
            <Prose>{children}</Prose>
        </Container>
    );
}
