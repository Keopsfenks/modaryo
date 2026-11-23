import type { Metadata } from "next";
import Link from "next/link";
import Box from "@mui/material/Box";
import Container from "@mui/material/Container";
import Typography from "@mui/material/Typography";
import ComingSoonChip from "@/components/site/ComingSoonChip";
import { moduleGroups, siteModules } from "@/content/site/modules";

export const metadata: Metadata = {
    title: { absolute: "Özellikler | Modaryo" },
    description: "Modaryo modülleri: cari, stok, satış, finans ve operasyon. Hangisinin hazır, hangisinin yakında olduğunu görün.",
};

export default function FeaturesIndexPage() {
    return (
        <Container maxWidth="lg" sx={{ py: { xs: 6, md: 10 } }}>
            <Typography component="h1" sx={{ fontSize: "clamp(2rem, 4.6vw, 3.25rem)", fontWeight: 800, lineHeight: 1.1, letterSpacing: "-0.03em", mb: 6, maxWidth: 640 }}>
                İşletmenizin her işi için bir modül
            </Typography>
            {moduleGroups.map((group) => (
                <Box key={group} component="section" aria-labelledby={`grup-${group}`} sx={{ mb: 6 }}>
                    <Typography id={`grup-${group}`} component="h2" sx={{ fontSize: "1.375rem", fontWeight: 700, mb: 2 }}>{group}</Typography>
                    <Box component="ul" sx={{ listStyle: "none", p: 0, m: 0, display: "grid", gridTemplateColumns: { xs: "1fr", md: "1fr 1fr" }, columnGap: 8 }}>
                        {siteModules.filter((m) => m.group === group).map((m) => (
                            <Box key={m.slug} component="li" sx={{ py: 2, borderTop: "1px solid", borderColor: "divider" }}>
                                <Box sx={{ display: "flex", alignItems: "center", gap: 1.5, mb: 0.5 }}>
                                    <Box component={Link} href={`/ozellikler/${m.slug}`} sx={{ fontWeight: 700, color: "text.primary" }}>{m.name}</Box>
                                    {m.status === "coming" && <ComingSoonChip />}
                                </Box>
                                <Typography sx={{ color: "text.secondary", lineHeight: 1.55 }}>{m.summary}</Typography>
                            </Box>
                        ))}
                    </Box>
                </Box>
            ))}
        </Container>
    );
}
