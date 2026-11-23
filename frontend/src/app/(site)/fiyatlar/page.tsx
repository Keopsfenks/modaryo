import type { Metadata } from "next";
import Box from "@mui/material/Box";
import Chip from "@mui/material/Chip";
import Container from "@mui/material/Container";
import Typography from "@mui/material/Typography";
import ComingSoonChip from "@/components/site/ComingSoonChip";
import CtaButtons from "@/components/site/CtaButtons";
import Faq from "@/components/site/home/Faq";
import Section from "@/components/site/home/Section";
import type { FaqItem } from "@/content/site/faq";
import { siteModules } from "@/content/site/modules";

export const metadata: Metadata = {
    title: { absolute: "Fiyatlar | Modaryo" },
    description: "Şu an tüm Modaryo modülleri ücretsiz. Hangi modüllerin hazır, hangilerinin yakında olduğunu görün.",
};

const billingFaq: FaqItem[] = [
    { question: "Ücretli paketler ne zaman gelecek?", answer: "Ücretli paketler geldiğinde mevcut firmalara önceden haber verilir." },
    { question: "Kart bilgisi gerekiyor mu?", answer: "Hayır. Şu an kayıt olmak için ödeme bilgisi istemiyoruz." },
    { question: "Henüz yayında olmayan modülleri kullanabilir miyim?", answer: "Yakında etiketli modüller yapım aşamasında. Hazır olduğunda haber verilmesini isteyebilirsiniz." },
];

export default function PricingPage() {
    return (
        <>
            <Box component="section" aria-labelledby="fiyat-baslik" sx={{ py: { xs: 6, md: 10 } }}>
                <Container maxWidth="lg">
                    <Typography id="fiyat-baslik" component="h1" sx={{ fontSize: "clamp(2rem, 4.6vw, 3.25rem)", fontWeight: 800, lineHeight: 1.1, letterSpacing: "-0.03em", mb: 2, maxWidth: 640 }}>
                        Şu an tüm modüller ücretsiz.
                    </Typography>
                    <Typography sx={{ color: "text.secondary", fontSize: "1.125rem", lineHeight: 1.6, maxWidth: 520, mb: 4 }}>
                        Kayıt olun, firmanızı oluşturun, hazır modülleri hemen kullanın.
                    </Typography>
                    <CtaButtons />
                </Container>
            </Box>

            <Section id="modul-listesi" title="Modüller ve durumları" intro="Hazır olanlar bugün kullanılabilir; yakında etiketli olanlar yapım aşamasında." tone="band">
                <Box component="ul" sx={{ listStyle: "none", p: 0, m: 0, display: "grid", gridTemplateColumns: { xs: "1fr", md: "1fr 1fr" }, columnGap: 8 }}>
                    {siteModules.map((m) => (
                        <Box key={m.slug} component="li" data-module={m.slug} sx={{ py: 1.5, borderTop: "1px solid", borderColor: "divider", display: "flex", alignItems: "center", justifyContent: "space-between", gap: 2 }}>
                            <Typography sx={{ fontWeight: 600 }}>{m.name}</Typography>
                            {m.status === "available" ? <Chip size="small" color="primary" label="Hazır" sx={{ height: 20, fontSize: "0.6875rem", fontWeight: 600 }} /> : <ComingSoonChip />}
                        </Box>
                    ))}
                </Box>
            </Section>

            <Faq items={billingFaq} id="fatura-sss" title="Ücretlendirme hakkında" />
        </>
    );
}
