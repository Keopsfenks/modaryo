"use client";

import { useEffect, useRef, useState } from "react";
import Box from "@mui/material/Box";
import Typography from "@mui/material/Typography";
import useMediaQuery from "@mui/material/useMediaQuery";
import { flowSteps } from "@/content/site/home";
import Section from "./Section";

/**
 * Teklif → tahsilat adımları. Kaydırdıkça ekranın ortasındaki adım vurgulanır;
 * "hareketi azalt" tercihinde gözlemci kurulmaz, geçiş yoktur ve tüm adımlar eşit görünür.
 */
export default function FlowStory() {
    const reduceMotion = useMediaQuery("(prefers-reduced-motion: reduce)");
    const animated = !reduceMotion;
    const [active, setActive] = useState(0);
    const refs = useRef<(HTMLLIElement | null)[]>([]);

    useEffect(() => {
        if (!animated || typeof IntersectionObserver === "undefined") {
            return;
        }
        const observer = new IntersectionObserver(
            (entries) => {
                for (const entry of entries) {
                    if (entry.isIntersecting) {
                        setActive(refs.current.indexOf(entry.target as HTMLLIElement));
                    }
                }
            },
            { rootMargin: "-45% 0px -45% 0px" },
        );
        refs.current.forEach((el) => el && observer.observe(el));
        return () => observer.disconnect();
    }, [animated]);

    return (
        <Section id="akis" title="Tekliften tahsilata tek akış" intro="Her adım bir öncekinin kaydından devam eder; aynı bilgiyi ikinci kez yazmazsınız.">
            <Box component="ol" data-animated={animated ? "true" : "false"} sx={{ listStyle: "none", p: 0, m: 0, maxWidth: 720 }}>
                {flowSteps.map((step, i) => {
                    const highlighted = animated && i === active;
                    return (
                        <Box
                            key={step.title}
                            component="li"
                            ref={(el: HTMLLIElement | null) => {
                                refs.current[i] = el;
                            }}
                            aria-current={highlighted ? "step" : undefined}
                            sx={{
                                display: "grid",
                                gridTemplateColumns: "56px 1fr",
                                py: 3,
                                pl: 2,
                                borderTop: "1px solid",
                                borderColor: "divider",
                                borderLeft: "3px solid",
                                borderLeftColor: highlighted ? "primary.main" : "transparent",
                                opacity: !animated || highlighted ? 1 : 0.5,
                                transition: animated ? "opacity .3s, border-color .3s" : "none",
                            }}
                        >
                            <Typography aria-hidden sx={{ fontWeight: 700, color: highlighted ? "primary.main" : "text.secondary" }}>{i + 1}</Typography>
                            <Box>
                                <Typography component="h3" sx={{ fontWeight: 700, fontSize: "1.25rem", mb: 0.5 }}>{step.title}</Typography>
                                <Typography sx={{ color: "text.secondary", lineHeight: 1.6 }}>{step.text}</Typography>
                            </Box>
                        </Box>
                    );
                })}
            </Box>
        </Section>
    );
}
