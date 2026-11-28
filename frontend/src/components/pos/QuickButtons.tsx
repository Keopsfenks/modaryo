"use client";

import { useState } from "react";
import Box from "@mui/material/Box";
import Button from "@mui/material/Button";
import Stack from "@mui/material/Stack";
import Tab from "@mui/material/Tab";
import Tabs from "@mui/material/Tabs";
import { textOn } from "@/components/pos/settings/QuickButtonDesigner";
import { BUTTON_COLS, type QuickButtonDto } from "@/lib/api/pos";

/** Hızlı tuş sayfaları: kasanın yerleşimi; dolu hücre kalemi (varsa hazır miktarla) sepete ekler. */
export default function QuickButtons({ pages, onPick, disabled }: { pages: (QuickButtonDto | null)[][]; onPick: (button: QuickButtonDto) => void; disabled?: boolean }) {
    const [page, setPage] = useState(0);
    const visible = pages.map((p, i) => ({ p, i })).filter(({ p }) => p.some(Boolean));
    if (visible.length === 0) return null;
    const active = visible.find((v) => v.i === page) ?? visible[0];

    return (
        <Stack spacing={1}>
            {visible.length > 1 && (
                <Tabs value={active.i} onChange={(_, v: number) => setPage(v)} variant="scrollable" sx={{ minHeight: 48 }}>
                    {visible.map(({ i }) => (
                        <Tab key={i} value={i} label={`Sayfa ${i + 1}`} sx={{ minHeight: 48 }} />
                    ))}
                </Tabs>
            )}
            <Box sx={{ display: "grid", gridTemplateColumns: `repeat(${BUTTON_COLS}, minmax(0, 1fr))`, gap: 1 }}>
                {active.p.map((b, i) =>
                    b ? (
                        <Button
                            key={i}
                            variant="contained"
                            disabled={disabled}
                            onClick={() => onPick(b)}
                            sx={{ minHeight: 64, p: 0.5, textTransform: "none", lineHeight: 1.15, bgcolor: b.color, color: textOn(b.color), "&:hover": { bgcolor: b.color, opacity: 0.9 } }}
                        >
                            {b.name ?? b.code}
                            {b.presetQty ? ` ×${b.presetQty}` : ""}
                        </Button>
                    ) : (
                        <Box key={i} />
                    ),
                )}
            </Box>
        </Stack>
    );
}
