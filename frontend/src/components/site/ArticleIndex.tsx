import Link from "next/link";
import Box from "@mui/material/Box";
import Container from "@mui/material/Container";
import Typography from "@mui/material/Typography";
import type { Article } from "@/content/site/articles";

type Props = { title: string; intro: string; basePath: string; items: Article[] };

export default function ArticleIndex({ title, intro, basePath, items }: Props) {
    return (
        <Container maxWidth="lg" sx={{ py: { xs: 6, md: 10 } }}>
            <Typography component="h1" sx={{ fontSize: "clamp(2rem, 4.6vw, 3rem)", fontWeight: 800, lineHeight: 1.1, letterSpacing: "-0.03em", mb: 2 }}>{title}</Typography>
            <Typography sx={{ color: "text.secondary", fontSize: "1.125rem", maxWidth: 560, mb: 4 }}>{intro}</Typography>
            <Box component="ul" sx={{ listStyle: "none", p: 0, m: 0, maxWidth: 720 }}>
                {items.map((a) => (
                    <Box key={a.slug} component="li" sx={{ py: 2.5, borderTop: "1px solid", borderColor: "divider" }}>
                        <Typography component="h2" sx={{ fontWeight: 700, fontSize: "1.25rem" }}>
                            <Link href={`${basePath}/${a.slug}`}>{a.title}</Link>
                        </Typography>
                        <Typography sx={{ color: "text.secondary", mt: 0.5 }}>{a.summary}</Typography>
                    </Box>
                ))}
            </Box>
        </Container>
    );
}
