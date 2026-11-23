import Box from "@mui/material/Box";
import Typography from "@mui/material/Typography";
import { testimonials, type Testimonial } from "@/content/site/testimonials";
import Section from "./Section";

/** Gerçek yorum yokken hiçbir şey çizmez; yorum uydurulmaz. */
export default function Testimonials({ items = testimonials }: { items?: Testimonial[] }) {
    if (items.length === 0) {
        return null;
    }
    return (
        <Section id="yorumlar" title="Müşteri yorumları" tone="band">
            <Box sx={{ display: "grid", gridTemplateColumns: { xs: "1fr", md: "repeat(3, 1fr)" }, gap: 4 }}>
                {items.map((t) => (
                    <Box key={t.name} component="figure" sx={{ m: 0 }}>
                        <Typography component="blockquote" sx={{ m: 0, lineHeight: 1.6 }}>{t.quote}</Typography>
                        <Typography component="figcaption" sx={{ mt: 1.5, color: "text.secondary" }}>{t.name}, {t.role}, {t.company}</Typography>
                    </Box>
                ))}
            </Box>
        </Section>
    );
}
