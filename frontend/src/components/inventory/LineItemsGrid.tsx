"use client";

import { useState } from "react";
import Button from "@mui/material/Button";
import IconButton from "@mui/material/IconButton";
import MenuItem from "@mui/material/MenuItem";
import Table from "@mui/material/Table";
import TableBody from "@mui/material/TableBody";
import TableCell from "@mui/material/TableCell";
import TableContainer from "@mui/material/TableContainer";
import TableHead from "@mui/material/TableHead";
import TableRow from "@mui/material/TableRow";
import TextField from "@mui/material/TextField";
import Typography from "@mui/material/Typography";
import { IconTrash } from "@tabler/icons-react";
import ItemPicker from "@/components/pricing/ItemPicker";
import { catalog, type SellableItem } from "@/lib/api/catalog";
import type { DocumentLineInput, LotInput } from "@/lib/api/inventory";
import { parseAmount } from "@/lib/money";
import LotSerialDialog, { type TrackingResult } from "./LotSerialDialog";

export type LineDraft = {
    key: string;
    productId: string;
    variantId: string | null;
    code: string;
    name: string;
    baseUnit: string;
    /** Tanımlı birimler ve temel birime çarpanları (temel birim dahil edilmez). */
    units: { unit: string; factor: number }[];
    unit: string;
    quantity: string;
    unitCost: string;
    tracksSerialLot: boolean;
    trackingMode: "lot" | "serial" | null;
    lots: LotInput[];
    serials: string[];
    /** Sunucudan ya da doğrulamadan gelen satır hatası. */
    error?: string | null;
};

let counter = 0;

/** Katalog kaleminden boş bir satır taslağı. */
export function newLine(item: SellableItem, trackingMode: "lot" | "serial" | null = null): LineDraft {
    return {
        key: `line-${++counter}`,
        productId: item.productId,
        variantId: item.variantId,
        code: item.code,
        name: item.name,
        baseUnit: item.baseUnit,
        units: item.units.map((u) => ({ unit: u.unit, factor: u.factor })),
        unit: item.baseUnit,
        quantity: "1",
        unitCost: "",
        tracksSerialLot: item.tracksSerialLot,
        trackingMode: item.tracksSerialLot ? (trackingMode ?? "lot") : null,
        lots: [],
        serials: [],
        error: null,
    };
}

export const lineFactor = (line: LineDraft): number => (line.unit === line.baseUnit ? 1 : (line.units.find((u) => u.unit === line.unit)?.factor ?? 1));

/** Satır miktarı (sayı) ya da geçersizse null. */
export const lineQuantity = (line: LineDraft): number | null => {
    const q = parseAmount(line.quantity, { maxDecimals: 4 });
    return q !== null && q > 0 ? q : null;
};

/** Satırı API gövdesine çevirir; birim maliyet yalnızca giriş fişlerinde (includeCost) gönderilir. */
export function toDocumentLine(line: LineDraft, includeCost: boolean): DocumentLineInput {
    const cost = includeCost && line.unitCost.trim() ? parseAmount(line.unitCost, { maxDecimals: 4 }) : null;
    return {
        productId: line.productId,
        variantId: line.variantId,
        unit: line.unit,
        quantity: lineQuantity(line) ?? 0,
        unitCost: cost,
        lots: line.tracksSerialLot && line.trackingMode === "lot" ? line.lots : undefined,
        serials: line.tracksSerialLot && line.trackingMode === "serial" ? line.serials : undefined,
    };
}

/** Satırların istemci tarafı doğrulaması: her satır için hata iletisi (geçerliyse yok). */
export function validateLines(lines: LineDraft[], includeCost: boolean): Record<string, string> {
    const errors: Record<string, string> = {};
    for (const line of lines) {
        const qty = lineQuantity(line);
        if (qty === null) {
            errors[line.key] = "Miktar sıfırdan büyük olmalıdır.";
        } else if (includeCost && line.unitCost.trim() && parseAmount(line.unitCost, { maxDecimals: 4 }) === null) {
            errors[line.key] = "Birim maliyet geçersiz.";
        } else if (line.tracksSerialLot) {
            const filled = line.trackingMode === "serial" ? line.serials.length === qty : Math.abs(line.lots.reduce((s, l) => s + l.quantity, 0) - qty) < 1e-6 && line.lots.length > 0;
            if (!filled) errors[line.key] = line.trackingMode === "serial" ? "Seri numaralarını girin." : "Lot bilgisini girin.";
        }
    }
    return errors;
}

/**
 * Sunucu hatasını ilgili satıra yazar: hata başlığı kalemin kodunu içeriyorsa (ör. "Yetersiz stok: U00001 - Tişört.")
 * o satırlar işaretlenir. Hiçbir satır eşleşmezse listeyi değiştirmez; çağıran genel uyarıyı gösterir.
 */
export function applyServerLineError(lines: LineDraft[], message: string): { lines: LineDraft[]; matched: boolean } {
    let matched = false;
    const next = lines.map((l) => {
        if (message.includes(l.code)) {
            matched = true;
            return { ...l, error: message };
        }
        return l;
    });
    return { lines: next, matched };
}

type LineItemsGridProps = {
    lines: LineDraft[];
    onChange(lines: LineDraft[]): void;
    /** Maliyet sütununu gösterir (maliyet.read); yalnızca giriş kipinde anlamlıdır. */
    showCost: boolean;
    /** Lot/seri seçiminin yapılacağı depo. */
    warehouseId: string;
    mode: "in" | "out";
};

/** Stok satır tablosu: kalem arama, birim seçimi, miktar (parseAmount) ve lot/seri düğmesi. Satın alma, satış ve sevkiyat da kullanır. */
export default function LineItemsGrid({ lines, onChange, showCost, warehouseId, mode }: LineItemsGridProps) {
    const [dialogKey, setDialogKey] = useState<string | null>(null);
    const costColumn = showCost && mode === "in";
    const dialogLine = lines.find((l) => l.key === dialogKey) ?? null;

    const patch = (key: string, change: Partial<LineDraft>) => onChange(lines.map((l) => (l.key === key ? { ...l, ...change, error: null } : l)));

    async function add(item: SellableItem) {
        let trackingMode: "lot" | "serial" | null = null;
        if (item.tracksSerialLot) {
            try {
                trackingMode = (await catalog.getProduct(item.productId)).trackingMode ?? "lot";
            } catch {
                trackingMode = "lot";
            }
        }
        onChange([...lines, newLine(item, trackingMode)]);
    }

    return (
        <>
            <ItemPicker label="Kalem ekle" onPick={(item) => void add(item)} clearOnPick />
            {lines.length === 0 ? (
                <Typography color="text.secondary" sx={{ py: 2 }}>
                    Henüz satır yok. Kalem arayıp ekleyin.
                </Typography>
            ) : (
                <TableContainer sx={{ mt: 2 }}>
                    <Table size="small">
                        <TableHead>
                            <TableRow>
                                <TableCell>Kalem</TableCell>
                                <TableCell sx={{ width: 120 }}>Miktar</TableCell>
                                <TableCell sx={{ width: 130 }}>Birim</TableCell>
                                {costColumn && <TableCell sx={{ width: 140 }}>Birim maliyet</TableCell>}
                                <TableCell>Lot / seri</TableCell>
                                <TableCell />
                            </TableRow>
                        </TableHead>
                        <TableBody>
                            {lines.map((line) => {
                                const qty = lineQuantity(line);
                                const tracked = line.tracksSerialLot;
                                const filled = line.trackingMode === "serial" ? line.serials.length : line.lots.length;
                                return (
                                    <TableRow key={line.key} sx={line.error ? { bgcolor: "error.light", "& td": { color: "error.contrastText" } } : undefined}>
                                        <TableCell>
                                            <Typography variant="body2">
                                                {line.code} · {line.name}
                                            </Typography>
                                            {line.error && (
                                                <Typography variant="caption" role="alert">
                                                    {line.error}
                                                </Typography>
                                            )}
                                        </TableCell>
                                        <TableCell>
                                            <TextField
                                                size="small"
                                                value={line.quantity}
                                                onChange={(e) => patch(line.key, { quantity: e.target.value })}
                                                error={line.quantity.trim() !== "" && qty === null}
                                                slotProps={{ htmlInput: { "aria-label": `${line.code} miktar`, inputMode: "decimal" } }}
                                            />
                                        </TableCell>
                                        <TableCell>
                                            <TextField
                                                select
                                                size="small"
                                                value={line.unit}
                                                onChange={(e) => patch(line.key, { unit: e.target.value, lots: [], serials: [] })}
                                                slotProps={{ htmlInput: { "aria-label": `${line.code} birim` } }}
                                                fullWidth
                                            >
                                                {[line.baseUnit, ...line.units.map((u) => u.unit).filter((u) => u !== line.baseUnit)].map((u) => (
                                                    <MenuItem key={u} value={u}>
                                                        {u}
                                                    </MenuItem>
                                                ))}
                                            </TextField>
                                        </TableCell>
                                        {costColumn && (
                                            <TableCell>
                                                <TextField
                                                    size="small"
                                                    value={line.unitCost}
                                                    placeholder="Ortalama"
                                                    onChange={(e) => patch(line.key, { unitCost: e.target.value })}
                                                    slotProps={{ htmlInput: { "aria-label": `${line.code} birim maliyet`, inputMode: "decimal" } }}
                                                />
                                            </TableCell>
                                        )}
                                        <TableCell>
                                            {tracked ? (
                                                <Button size="small" variant="outlined" disabled={qty === null} onClick={() => setDialogKey(line.key)}>
                                                    {line.trackingMode === "serial" ? `Seri (${filled})` : `Lot (${filled})`}
                                                </Button>
                                            ) : (
                                                "—"
                                            )}
                                        </TableCell>
                                        <TableCell align="right">
                                            <IconButton aria-label={`${line.code} satırını kaldır`} onClick={() => onChange(lines.filter((l) => l.key !== line.key))}>
                                                <IconTrash size={18} />
                                            </IconButton>
                                        </TableCell>
                                    </TableRow>
                                );
                            })}
                        </TableBody>
                    </Table>
                </TableContainer>
            )}
            {dialogLine && lineQuantity(dialogLine) !== null && dialogLine.trackingMode && (
                <LotSerialDialog
                    open
                    mode={mode}
                    trackingMode={dialogLine.trackingMode}
                    productId={dialogLine.productId}
                    variantId={dialogLine.variantId}
                    warehouseId={warehouseId}
                    quantity={lineQuantity(dialogLine) ?? 0}
                    factor={lineFactor(dialogLine)}
                    title={`${dialogLine.code} · ${dialogLine.trackingMode === "serial" ? "Seri numaraları" : "Lot bilgisi"}`}
                    initialLots={dialogLine.lots}
                    initialSerials={dialogLine.serials}
                    onClose={() => setDialogKey(null)}
                    onSave={(result: TrackingResult) => patch(dialogLine.key, { lots: result.lots ?? [], serials: result.serials ?? [] })}
                />
            )}
        </>
    );
}
