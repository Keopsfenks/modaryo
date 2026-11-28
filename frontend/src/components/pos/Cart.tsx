"use client";

import { useState } from "react";
import { IconMinus, IconPlus, IconTrash } from "@tabler/icons-react";
import Box from "@mui/material/Box";
import Chip from "@mui/material/Chip";
import IconButton from "@mui/material/IconButton";
import Stack from "@mui/material/Stack";
import TextField from "@mui/material/TextField";
import Typography from "@mui/material/Typography";
import type { PosLookup } from "@/lib/api/pos";
import { fmtQty, money, parseNumber, round2 } from "./format";
import OtherStoresPopover from "./OtherStoresPopover";

export type CartLine = {
    key: string;
    info: PosLookup;
    quantity: number;
    /** Kampanya, sorgudaki miktar için hesaplanmıştır; miktar değişince orantılı tahmin edilir. */
    baseQuantity: number;
    discountPercent: number;
};

/** Yer tutucu: sunucu önizlemesi gelene kadar gösterilen ham tutar (KDV/kampanya hesabı yok; kesin tutar sunucudan gelir). */
export function lineEstimate(line: CartLine): number {
    const { info } = line;
    if (info.fixedTotal !== null) return info.fixedTotal;
    return Math.max(0, round2(line.quantity * info.unitPrice * (1 - line.discountPercent / 100)));
}

type CartProps = {
    lines: CartLine[];
    onQuantity: (key: string, quantity: number) => void;
    onDiscount: (key: string, percent: number) => void;
    onRemove: (key: string) => void;
    /** Sunucu önizlemesinden satır toplamları (satır sırasıyla); yoksa yer tutucu tutar soluk gösterilir. */
    serverTotals?: number[] | null;
    /** Satır iskontosu yetkisi: izin verilen en yüksek yüzde (0 → iskonto yok). */
    maxDiscount: number;
};

function QuantityField({ line, onQuantity }: { line: CartLine; onQuantity: CartProps["onQuantity"] }) {
    const [draft, setDraft] = useState<string | null>(null);
    const name = line.info.item.name;
    return (
        <TextField
            size="small"
            value={draft ?? fmtQty(line.quantity)}
            disabled={line.info.fixedTotal !== null}
            onFocus={(e) => e.target.select()}
            onChange={(e) => {
                setDraft(e.target.value);
                const n = parseNumber(e.target.value);
                if (n !== null && n > 0) onQuantity(line.key, n);
            }}
            onBlur={() => setDraft(null)}
            slotProps={{ htmlInput: { "aria-label": `Miktar ${name}`, inputMode: "decimal", style: { textAlign: "center", fontSize: 18, width: 70 } } }}
        />
    );
}

/** Sepet: büyük satırlar, miktar +/−/düzenleme, iskonto (yetkiliyse) ve diğer şubelerin stoğu. */
export default function Cart({ lines, onQuantity, onDiscount, onRemove, maxDiscount, serverTotals = null }: CartProps) {
    if (lines.length === 0) {
        return (
            <Box sx={{ flexGrow: 1, display: "grid", placeItems: "center", color: "text.disabled" }}>
                <Typography variant="h5">Barkod okutun ya da bir tuşa basın</Typography>
            </Box>
        );
    }
    return (
        <Stack spacing={1} sx={{ flexGrow: 1, overflowY: "auto", pr: 0.5 }}>
            {lines.map((l, idx) => {
                const name = l.info.item.name;
                const step = 1;
                return (
                    <Box key={l.key} sx={{ display: "flex", alignItems: "center", gap: 1, p: 1.5, borderRadius: 2, border: 1, borderColor: "divider" }}>
                        <Box sx={{ flexGrow: 1, minWidth: 0 }}>
                            <Typography variant="h6" noWrap>
                                {name}
                            </Typography>
                            <Stack direction="row" spacing={1} sx={{ alignItems: "center", flexWrap: "wrap" }}>
                                <Typography variant="body2" color="text.secondary">
                                    {l.info.item.code} · {money(l.info.unitPrice)} / {l.info.unit}
                                </Typography>
                                {l.info.campaign && <Chip size="small" color="success" label={l.info.campaign.name} />}
                                {l.info.otherStores.length > 0 && <OtherStoresPopover stores={l.info.otherStores} name={name} />}
                            </Stack>
                        </Box>
                        {maxDiscount > 0 && l.info.fixedTotal === null && (
                            <TextField
                                size="small"
                                label="İsk. %"
                                value={l.discountPercent || ""}
                                onChange={(e) => {
                                    const n = parseNumber(e.target.value) ?? 0;
                                    onDiscount(l.key, Math.min(n, maxDiscount));
                                }}
                                slotProps={{ htmlInput: { "aria-label": `İskonto ${name}`, inputMode: "decimal" } }}
                                sx={{ width: 80 }}
                            />
                        )}
                        <IconButton aria-label={`Azalt ${name}`} sx={{ width: 48, height: 48 }} disabled={l.info.fixedTotal !== null} onClick={() => onQuantity(l.key, Math.max(step, round2(l.quantity - step)))}>
                            <IconMinus />
                        </IconButton>
                        <QuantityField line={l} onQuantity={onQuantity} />
                        <IconButton aria-label={`Artır ${name}`} sx={{ width: 48, height: 48 }} disabled={l.info.fixedTotal !== null} onClick={() => onQuantity(l.key, round2(l.quantity + step))}>
                            <IconPlus />
                        </IconButton>
                        <Typography variant="h6" sx={{ minWidth: 110, textAlign: "right" }} color={serverTotals?.[idx] === undefined ? "text.disabled" : "text.primary"}>
                            {money(serverTotals?.[idx] ?? lineEstimate(l))}
                        </Typography>
                        <IconButton aria-label={`Sil ${name}`} color="error" sx={{ width: 48, height: 48 }} onClick={() => onRemove(l.key)}>
                            <IconTrash />
                        </IconButton>
                    </Box>
                );
            })}
        </Stack>
    );
}
