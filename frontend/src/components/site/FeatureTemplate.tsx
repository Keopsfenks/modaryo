import Box from "@mui/material/Box";
import Container from "@mui/material/Container";
import Typography from "@mui/material/Typography";
import type { SiteModule } from "@/content/site/modules";
import ClosingCta from "./home/ClosingCta";
import Faq from "./home/Faq";
import Section from "./home/Section";
import JsonLd, { breadcrumbJsonLd } from "./JsonLd";
import { siteUrl } from "@/content/site/seo";
import ComingSoonChip from "./ComingSoonChip";
import { PrimaryCta, SecondaryCta } from "./CtaButtons";
import { Shot } from "./shots/registry";
import Stack from "@mui/material/Stack";

/** Modül sayfası: sorun, çözüm, özellikler, örnek ekran. "Yakında" modüllerde "Haber ver" çağrısı çıkar. */
export default function FeatureTemplate({ module: m }: { module: SiteModule }) {
    const coming = m.status === "coming";

    return (
        <>
            <JsonLd data={breadcrumbJsonLd(siteUrl(), [{ name: "Ana sayfa", path: "" }, { name: "Özellikler", path: "/ozellikler" }, { name: m.name, path: `/ozellikler/${m.slug}` }])} />
            <Box component="section" aria-labelledby="modul-baslik" sx={{ py: { xs: 6, md: 10 } }}>
                <Container maxWidth="lg">
                    <Box sx={{ display: "grid", gridTemplateColumns: { xs: "1fr", md: "minmax(0, 1fr) minmax(0, 1.1fr)" }, gap: { xs: 5, md: 8 }, alignItems: "center" }}>
                        <Box>
                            <Box sx={{ display: "flex", alignItems: "center", gap: 1.5, mb: 2 }}>
                                <Box component={m.icon} size={28} stroke={1.6} aria-hidden sx={{ color: "primary.main" }} />
                                {coming && <ComingSoonChip />}
                            </Box>
                            <Typography id="modul-baslik" component="h1" sx={{ fontSize: "clamp(2rem, 4.6vw, 3.25rem)", fontWeight: 800, lineHeight: 1.1, letterSpacing: "-0.03em", mb: 2 }}>
                                {m.name}
                            </Typography>
                            <Typography sx={{ color: "text.secondary", fontSize: "1.125rem", lineHeight: 1.6, maxWidth: 520, mb: 4 }}>{m.summary}</Typography>
                            <Stack direction={{ xs: "column", sm: "row" }} spacing={1.5}>
                                {coming ? (
                                    <>
                                        <SecondaryCta label="Haber ver" href={`/demo?ilgi=${m.slug}`} size="large" />
                                        <PrimaryCta size="large" />
                                    </>
                                ) : (
                                    <>
                                        <PrimaryCta size="large" />
                                        <SecondaryCta size="large" />
                                    </>
                                )}
                            </Stack>
                            {coming && (
                                <Typography sx={{ mt: 2, color: "text.secondary", fontSize: "0.9375rem", maxWidth: 480 }}>
                                    Bu modül henüz yayında değil. Hazır olduğunda size haber verelim.
                                </Typography>
                            )}
                        </Box>
                        <Shot name={m.shot} />
                    </Box>
                </Container>
            </Box>

            <Section id="sorun-cozum" title="Sorun ve çözüm" tone="band">
                <Box sx={{ display: "grid", gridTemplateColumns: { xs: "1fr", md: "1fr 1fr" }, gap: { xs: 4, md: 8 } }}>
                    <Box>
                        <Typography component="h3" sx={{ fontWeight: 700, mb: 1 }}>Bugün</Typography>
                        <Typography sx={{ color: "text.secondary", lineHeight: 1.65, fontSize: "1.0625rem" }}>{m.problem}</Typography>
                    </Box>
                    <Box>
                        <Typography component="h3" sx={{ fontWeight: 700, mb: 1 }}>Modaryo ile</Typography>
                        <Typography sx={{ lineHeight: 1.65, fontSize: "1.0625rem" }}>{m.solution}</Typography>
                    </Box>
                </Box>
            </Section>

            <Section id="ozellikler" title={coming ? "Planlanan özellikler" : "Neler yapabilirsiniz"}>
                <Box component="ul" sx={{ listStyle: "none", p: 0, m: 0, display: "grid", gridTemplateColumns: { xs: "1fr", md: "1fr 1fr" }, columnGap: 8 }}>
                    {m.features.map((f) => (
                        <Box key={f.title} component="li" sx={{ py: 2.5, borderTop: "1px solid", borderColor: "divider" }}>
                            <Typography component="h3" sx={{ fontWeight: 700, fontSize: "1.125rem", mb: 0.5 }}>{f.title}</Typography>
                            <Typography sx={{ color: "text.secondary", lineHeight: 1.6 }}>{f.text}</Typography>
                        </Box>
                    ))}
                </Box>
            </Section>

            {m.faq && <Faq items={m.faq} id="modul-sss" tone="band" />}
            <ClosingCta />
        </>
    );
}
