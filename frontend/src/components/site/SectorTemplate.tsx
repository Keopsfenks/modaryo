import Link from "next/link";
import Box from "@mui/material/Box";
import Container from "@mui/material/Container";
import Typography from "@mui/material/Typography";
import { getModule, type SiteModule } from "@/content/site/modules";
import type { SiteSector } from "@/content/site/sectors";
import ClosingCta from "./home/ClosingCta";
import Section from "./home/Section";
import ComingSoonChip from "./ComingSoonChip";
import CtaButtons from "./CtaButtons";
import { Shot } from "./shots/registry";

/** Sektör sayfası: sorun, çözüm, adım adım hikaye ve ilgili modüller. */
export default function SectorTemplate({ sector }: { sector: SiteSector }) {
    const related = sector.modules.map(getModule).filter((m): m is SiteModule => m !== undefined);

    return (
        <>
            <Box component="section" aria-labelledby="sektor-baslik" sx={{ py: { xs: 6, md: 10 } }}>
                <Container maxWidth="lg">
                    <Box sx={{ display: "grid", gridTemplateColumns: { xs: "1fr", md: "minmax(0, 1fr) minmax(0, 1.1fr)" }, gap: { xs: 5, md: 8 }, alignItems: "center" }}>
                        <Box>
                            <Typography id="sektor-baslik" component="h1" sx={{ fontSize: "clamp(2rem, 4.6vw, 3.25rem)", fontWeight: 800, lineHeight: 1.1, letterSpacing: "-0.03em", mb: 2 }}>
                                {sector.name}
                            </Typography>
                            <Typography sx={{ color: "text.secondary", fontSize: "1.125rem", lineHeight: 1.6, maxWidth: 520, mb: 4 }}>{sector.summary}</Typography>
                            <CtaButtons />
                        </Box>
                        <Shot name={sector.shot} />
                    </Box>
                </Container>
            </Box>

            <Section id="sorun-cozum" title="Sorun ve çözüm" tone="band">
                <Box sx={{ display: "grid", gridTemplateColumns: { xs: "1fr", md: "1fr 1fr" }, gap: { xs: 4, md: 8 } }}>
                    <Box>
                        <Typography component="h3" sx={{ fontWeight: 700, mb: 1 }}>Bugün</Typography>
                        <Typography sx={{ color: "text.secondary", lineHeight: 1.65, fontSize: "1.0625rem" }}>{sector.problem}</Typography>
                    </Box>
                    <Box>
                        <Typography component="h3" sx={{ fontWeight: 700, mb: 1 }}>Modaryo ile</Typography>
                        <Typography sx={{ lineHeight: 1.65, fontSize: "1.0625rem" }}>{sector.solution}</Typography>
                    </Box>
                </Box>
            </Section>

            <Section id="hikaye" title="Bir gün nasıl geçer?">
                <Box component="ol" sx={{ listStyle: "none", p: 0, m: 0, maxWidth: 720 }}>
                    {sector.story.map((step, i) => (
                        <Box key={step.title} component="li" sx={{ display: "grid", gridTemplateColumns: "48px 1fr", py: 3, borderTop: "1px solid", borderColor: "divider" }}>
                            <Typography aria-hidden sx={{ fontWeight: 700, color: "text.secondary" }}>{i + 1}</Typography>
                            <Box>
                                <Typography component="h3" sx={{ fontWeight: 700, fontSize: "1.25rem", mb: 0.5 }}>{step.title}</Typography>
                                <Typography sx={{ color: "text.secondary", lineHeight: 1.6 }}>{step.text}</Typography>
                            </Box>
                        </Box>
                    ))}
                </Box>
            </Section>

            <Section id="moduller" title="Bu sektörde işinize yarayan modüller" tone="band">
                <Box component="ul" sx={{ listStyle: "none", p: 0, m: 0, display: "grid", gridTemplateColumns: { xs: "1fr", sm: "1fr 1fr" }, columnGap: 8 }}>
                    {related.map((m) => (
                        <Box key={m.slug} component="li" sx={{ py: 2, borderTop: "1px solid", borderColor: "divider", display: "flex", alignItems: "center", justifyContent: "space-between", gap: 2 }}>
                            <Box component={Link} href={`/ozellikler/${m.slug}`} sx={{ fontWeight: 600, color: "text.primary" }}>{m.name}</Box>
                            {m.status === "coming" && <ComingSoonChip />}
                        </Box>
                    ))}
                </Box>
            </Section>
            <ClosingCta />
        </>
    );
}
