"use client";

import Accordion from "@mui/material/Accordion";
import AccordionDetails from "@mui/material/AccordionDetails";
import AccordionSummary from "@mui/material/AccordionSummary";
import Box from "@mui/material/Box";
import Typography from "@mui/material/Typography";
import { IconChevronDown } from "@tabler/icons-react";
import type { FaqItem } from "@/content/site/faq";
import Section from "./Section";

/** FAQPage JSON-LD; ekrandaki soru-cevaplarla aynı içerikten üretilir. */
export function faqJsonLd(items: FaqItem[]) {
    return {
        "@context": "https://schema.org",
        "@type": "FAQPage",
        mainEntity: items.map((i) => ({ "@type": "Question", name: i.question, acceptedAnswer: { "@type": "Answer", text: i.answer } })),
    };
}

type FaqProps = { items: FaqItem[]; id?: string; title?: string; tone?: "plain" | "band" };

export default function Faq({ items, id = "sss", title = "Sık sorulan sorular", tone }: FaqProps) {
    return (
        <Section id={id} title={title} tone={tone}>
            <script type="application/ld+json" dangerouslySetInnerHTML={{ __html: JSON.stringify(faqJsonLd(items)).replace(/</g, "\\u003c") }} />
            <Box sx={{ maxWidth: 760 }}>
                {items.map((item, i) => (
                    <Accordion
                        key={item.question}
                        disableGutters
                        elevation={0}
                        square
                        slotProps={{ heading: { component: "h3" } }}
                        sx={{ bgcolor: "transparent", borderTop: "1px solid", borderColor: "divider", "&:before": { display: "none" }, "&:last-of-type": { borderBottom: "1px solid", borderColor: "divider" } }}
                    >
                        <AccordionSummary expandIcon={<IconChevronDown size={18} aria-hidden />} aria-controls={`${id}-icerik-${i}`} id={`${id}-baslik-${i}`} sx={{ px: 0, py: 0.5 }}>
                            <Typography sx={{ fontWeight: 600 }}>{item.question}</Typography>
                        </AccordionSummary>
                        <AccordionDetails sx={{ px: 0 }}>
                            <Typography sx={{ color: "text.secondary", lineHeight: 1.65 }}>{item.answer}</Typography>
                        </AccordionDetails>
                    </Accordion>
                ))}
            </Box>
        </Section>
    );
}
