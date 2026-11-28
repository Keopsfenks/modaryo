"use client";

import { useState } from "react";
import { IconPlus } from "@tabler/icons-react";
import Box from "@mui/material/Box";
import Button from "@mui/material/Button";
import Dialog from "@mui/material/Dialog";
import DialogActions from "@mui/material/DialogActions";
import DialogContent from "@mui/material/DialogContent";
import DialogTitle from "@mui/material/DialogTitle";
import Stack from "@mui/material/Stack";
import Tab from "@mui/material/Tab";
import Tabs from "@mui/material/Tabs";
import TextField from "@mui/material/TextField";
import Typography from "@mui/material/Typography";
import ItemPicker, { itemLabel } from "@/components/pricing/ItemPicker";
import type { SellableItem } from "@/lib/api/catalog";
import { BUTTON_COLS, BUTTONS_PER_PAGE, DEFAULT_BUTTON_COLOR, MAX_PAGES, type QuickButtonDto } from "@/lib/api/pos";

type Pages = (QuickButtonDto | null)[][];

const pad = (page: (QuickButtonDto | null)[]): (QuickButtonDto | null)[] => Array.from({ length: BUTTONS_PER_PAGE }, (_, i) => page[i] ?? null);

/** Renk koyuysa beyaz, açıksa koyu yazı. */
export function textOn(hex: string): string {
    const m = /^#([0-9a-f]{2})([0-9a-f]{2})([0-9a-f]{2})$/i.exec(hex);
    if (!m) return "#fff";
    const [r, g, b] = [m[1], m[2], m[3]].map((x) => parseInt(x, 16));
    return (r * 299 + g * 587 + b * 114) / 1000 > 150 ? "#1f2937" : "#fff";
}

/** Hızlı tuş tasarımcısı: sayfa başına 6x6 hücre; hücreye tıkla, kalem ara, renk ve hazır miktar seç. */
export default function QuickButtonDesigner({ pages, onChange }: { pages: Pages; onChange: (next: Pages) => void }) {
    const list: Pages = pages.length === 0 ? [[]] : pages;
    const [pageIndex, setPageIndex] = useState(0);
    const [cell, setCell] = useState<number | null>(null);
    const [item, setItem] = useState<SellableItem | null>(null);
    const [color, setColor] = useState(DEFAULT_BUTTON_COLOR);
    const [qty, setQty] = useState("");

    const active = Math.min(pageIndex, list.length - 1);
    const current = pad(list[active] ?? []);

    const emit = (page: (QuickButtonDto | null)[]) => onChange(list.map((p, i) => (i === active ? page : pad(p))));

    const openCell = (index: number) => {
        const existing = current[index];
        setCell(index);
        setItem(existing ? ({ productId: existing.productId, variantId: existing.variantId, code: existing.code ?? "", name: existing.name ?? "" } as SellableItem) : null);
        setColor(existing?.color ?? DEFAULT_BUTTON_COLOR);
        setQty(existing?.presetQty ? String(existing.presetQty).replace(".", ",") : "");
    };

    const apply = () => {
        if (cell === null || !item) return;
        const parsed = qty.trim() === "" ? null : Number(qty.replace(",", "."));
        const next = [...current];
        next[cell] = { productId: item.productId, variantId: item.variantId, code: item.code, name: item.name, presetQty: parsed !== null && parsed > 0 ? parsed : null, color };
        emit(next);
        setCell(null);
    };

    const clear = () => {
        if (cell === null) return;
        const next = [...current];
        next[cell] = null;
        emit(next);
        setCell(null);
    };

    return (
        <Stack spacing={2}>
            <Stack direction="row" spacing={1} sx={{ alignItems: "center" }}>
                <Tabs value={active} onChange={(_, v: number) => setPageIndex(v)} variant="scrollable" sx={{ flexGrow: 1 }}>
                    {list.map((_, i) => (
                        <Tab key={i} label={`Sayfa ${i + 1}`} value={i} />
                    ))}
                </Tabs>
                <Button
                    size="small"
                    startIcon={<IconPlus size={16} />}
                    disabled={list.length >= MAX_PAGES}
                    onClick={() => {
                        onChange([...list.map(pad), pad([])]);
                        setPageIndex(list.length);
                    }}
                >
                    Sayfa ekle
                </Button>
            </Stack>
            <Box sx={{ display: "grid", gridTemplateColumns: `repeat(${BUTTON_COLS}, minmax(0, 1fr))`, gap: 1, maxWidth: 720 }}>
                {current.map((b, i) => (
                    <Button
                        key={i}
                        aria-label={`Hücre ${i + 1}`}
                        variant={b ? "contained" : "outlined"}
                        onClick={() => openCell(i)}
                        sx={{
                            minHeight: 64,
                            p: 0.5,
                            textTransform: "none",
                            lineHeight: 1.2,
                            ...(b ? { bgcolor: b.color, color: textOn(b.color), "&:hover": { bgcolor: b.color, opacity: 0.9 } } : { color: "text.disabled", borderStyle: "dashed" }),
                        }}
                    >
                        {b ? (
                            <span>
                                {b.name ?? b.code}
                                {b.presetQty ? ` ×${b.presetQty}` : ""}
                            </span>
                        ) : (
                            i + 1
                        )}
                    </Button>
                ))}
            </Box>
            <Dialog open={cell !== null} onClose={() => setCell(null)} fullWidth maxWidth="xs">
                <DialogTitle>Hücre {cell === null ? "" : cell + 1}</DialogTitle>
                <DialogContent>
                    <Stack spacing={2} sx={{ pt: 1 }}>
                        <ItemPicker label="Kalem ara" value={item?.name ? item : null} onPick={setItem} size="medium" />
                        {item?.name && <Typography variant="body2">{itemLabel(item)}</Typography>}
                        <TextField label="Hazır miktar" value={qty} onChange={(e) => setQty(e.target.value.replace(/[^\d,.]/g, ""))} helperText="Boşsa 1 adet eklenir." slotProps={{ htmlInput: { inputMode: "decimal" } }} />
                        <TextField label="Renk" type="color" value={color} onChange={(e) => setColor(e.target.value)} />
                    </Stack>
                </DialogContent>
                <DialogActions>
                    <Button color="error" onClick={clear} disabled={cell === null || !current[cell]}>
                        Temizle
                    </Button>
                    <Button onClick={() => setCell(null)}>Vazgeç</Button>
                    <Button variant="contained" onClick={apply} disabled={!item}>
                        Uygula
                    </Button>
                </DialogActions>
            </Dialog>
        </Stack>
    );
}
