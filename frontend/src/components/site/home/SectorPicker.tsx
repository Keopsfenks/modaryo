"use client";

import { useState } from "react";
import Link from "next/link";
import Box from "@mui/material/Box";
import Tab from "@mui/material/Tab";
import Tabs from "@mui/material/Tabs";
import Typography from "@mui/material/Typography";
import { sectorStories } from "@/content/site/home";
import { siteSectors } from "@/content/site/sectors";
import { Shot } from "../shots/registry";
import Section from "./Section";

const sectorName = (slug: string) => siteSectors.find((s) => s.slug === slug)?.name ?? slug;

export default function SectorPicker() {
    const [index, setIndex] = useState(0);
    const story = sectorStories[index];

    return (
        <Section id="sektorler" title="Sektörünüze göre bakın" intro="Aynı panel, işinizin diline göre: hangi sektördeyseniz onun gününü anlatalım." tone="band">
            <Tabs value={index} onChange={(_, v: number) => setIndex(v)} variant="scrollable" scrollButtons="auto" aria-label="Sektörler" sx={{ borderBottom: "1px solid", borderColor: "divider", mb: 4 }}>
                {sectorStories.map((s, i) => (
                    <Tab key={s.slug} id={`sektor-sekme-${i}`} aria-controls="sektor-panel" label={sectorName(s.slug)} sx={{ textTransform: "none", fontWeight: 600 }} />
                ))}
            </Tabs>
            <Box role="tabpanel" id="sektor-panel" aria-labelledby={`sektor-sekme-${index}`} sx={{ display: "grid", gridTemplateColumns: { xs: "1fr", md: "minmax(0, 1fr) minmax(0, 1.1fr)" }, gap: { xs: 4, md: 8 }, alignItems: "center" }}>
                <Box>
                    <Typography component="h3" sx={{ fontSize: "1.5rem", fontWeight: 700, lineHeight: 1.2, mb: 1.5 }}>{story.headline}</Typography>
                    <Typography sx={{ color: "text.secondary", lineHeight: 1.65, fontSize: "1.0625rem", mb: 2.5 }}>{story.text}</Typography>
                    <Box component={Link} href={`/cozumler/${story.slug}`} sx={{ color: "primary.main", fontWeight: 600 }}>
                        {sectorName(story.slug)} çözümünü incele
                    </Box>
                </Box>
                <Shot name={story.shot} />
            </Box>
        </Section>
    );
}
