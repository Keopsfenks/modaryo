"use client";

import { useEffect, useState } from "react";
import Alert from "@mui/material/Alert";
import Autocomplete from "@mui/material/Autocomplete";
import Button from "@mui/material/Button";
import Dialog from "@mui/material/Dialog";
import DialogActions from "@mui/material/DialogActions";
import DialogContent from "@mui/material/DialogContent";
import DialogTitle from "@mui/material/DialogTitle";
import IconButton from "@mui/material/IconButton";
import Stack from "@mui/material/Stack";
import TextField from "@mui/material/TextField";
import Typography from "@mui/material/Typography";
import { IconTrash } from "@tabler/icons-react";
import { errorMessage } from "@/lib/api/errorMessages";
import { formatQuantity, inventory, type LotInput } from "@/lib/api/inventory";
import { parseAmount } from "@/lib/money";

export type TrackingResult = { lots?: LotInput[]; serials?: string[] };

type LotSerialDialogProps = {
    open: boolean;
    onClose: () => void;
    onSave: (result: TrackingResult) => void;
    /** "in": giriş (lot/seri yazılır), "out": çıkış (stoktaki lot/seri seçilir). */
    mode: "in" | "out";
    trackingMode: "lot" | "serial";
    productId: string;
    variantId: string | null;
    warehouseId: string;
    /** Satır miktarı (satır biriminde). */
    quantity: number;
    /** Satır biriminin temel birime çarpanı; FEFO önerisi temel birimde sorulur. */
    factor?: number;
    title?: string;
    initialLots?: LotInput[];
    initialSerials?: string[];
};

type LotRow = { key: number; lotId: string | null; lotNo: string; expiry: string; quantity: string };

let rowKey = 0;
const nextKey = () => ++rowKey;

const toRows = (lots: LotInput[]): LotRow[] =>
    lots.map((l) => ({ key: nextKey(), lotId: l.lotId ?? null, lotNo: l.lotNo, expiry: l.expiry ?? "", quantity: formatQuantity(l.quantity) }));

const near = (a: number, b: number) => Math.abs(a - b) < 1e-6;

/** Lot ve seri seçim penceresi: girişte lot/seri yazılır, çıkışta FEFO önerisiyle (son kullanma tarihi en yakın lot önce) doldurulur. */
export default function LotSerialDialog(props: LotSerialDialogProps) {
    return (
        <Dialog open={props.open} onClose={props.onClose} fullWidth maxWidth="sm">
            {props.open && <Body {...props} />}
        </Dialog>
    );
}

function Body({ onClose, onSave, mode, trackingMode, productId, variantId, warehouseId, quantity, factor = 1, title, initialLots = [], initialSerials = [] }: LotSerialDialogProps) {
    const [rows, setRows] = useState<LotRow[]>(() => (initialLots.length > 0 ? toRows(initialLots) : mode === "in" ? [{ key: nextKey(), lotId: null, lotNo: "", expiry: "", quantity: "" }] : []));
    const [serialText, setSerialText] = useState(initialSerials.join("\n"));
    const [picked, setPicked] = useState<string[]>(initialSerials);
    const [available, setAvailable] = useState<string[]>([]);
    const [error, setError] = useState<string | null>(null);

    const lotOut = mode === "out" && trackingMode === "lot";
    const serialOut = mode === "out" && trackingMode === "serial";

    useEffect(() => {
        let cancelled = false;
        if (lotOut && initialLots.length === 0) {
            inventory.suggestLots(productId, variantId, warehouseId, quantity * factor).then(
                (picks) => {
                    if (!cancelled)
                        setRows(picks.map((p) => ({ key: nextKey(), lotId: p.lotId, lotNo: p.lotNo, expiry: p.expiry ?? "", quantity: formatQuantity(p.quantity / factor) })));
                },
                (e) => !cancelled && setError(errorMessage(e)),
            );
        }
        if (serialOut) {
            inventory.serials(productId, variantId, warehouseId, true).then(
                (list) => !cancelled && setAvailable(list.map((s) => s.serial)),
                (e) => !cancelled && setError(errorMessage(e)),
            );
        }
        return () => {
            cancelled = true;
        };
        // Açılışta bir kez yüklenir.
        // eslint-disable-next-line react-hooks/exhaustive-deps
    }, []);

    const serials = serialOut
        ? picked
        : serialText
              .split(/[\n,;]+/)
              .map((s) => s.trim())
              .filter(Boolean);
    const duplicate = new Set(serials).size !== serials.length;

    const parsedRows = rows.map((r) => parseAmount(r.quantity, { maxDecimals: 4 }));
    const lotTotal = parsedRows.reduce<number>((sum, q) => sum + (q ?? 0), 0);

    let problem: string | null = null;
    if (trackingMode === "serial") {
        if (!Number.isInteger(quantity)) problem = "Seri takipli kalemin miktarı tam sayı olmalıdır.";
        else if (serials.length !== quantity) problem = `Seri numarası sayısı miktarla aynı olmalıdır (${serials.length} / ${formatQuantity(quantity)}).`;
        else if (duplicate) problem = "Aynı seri numarası birden fazla kez girilmiş.";
    } else if (rows.length === 0) {
        problem = "En az bir lot girilmelidir.";
    } else if (rows.some((r, i) => !r.lotNo.trim() || parsedRows[i] === null || (parsedRows[i] ?? 0) <= 0)) {
        problem = "Her lot için ad ve miktar girilmelidir.";
    } else if (!near(lotTotal, quantity)) {
        problem = `Lot miktarlarının toplamı satır miktarıyla aynı olmalıdır (${formatQuantity(lotTotal)} / ${formatQuantity(quantity)}).`;
    }

    const patch = (key: number, change: Partial<LotRow>) => setRows((current) => current.map((r) => (r.key === key ? { ...r, ...change } : r)));

    const save = () => {
        if (problem) return;
        if (trackingMode === "serial") {
            onSave({ serials });
        } else {
            onSave({ lots: rows.map((r, i) => ({ lotNo: r.lotNo.trim(), expiry: r.expiry || null, quantity: parsedRows[i] ?? 0, lotId: r.lotId })) });
        }
        onClose();
    };

    return (
        <>
            <DialogTitle>{title ?? (trackingMode === "serial" ? "Seri numaraları" : "Lot bilgisi")}</DialogTitle>
            <DialogContent>
                <Stack spacing={2} sx={{ pt: 1 }}>
                    {error && <Alert severity="error">{error}</Alert>}
                    <Typography variant="body2" color="text.secondary">
                        Satır miktarı: {formatQuantity(quantity)}
                        {lotOut && " — lotlar son kullanma tarihi en yakın olandan başlayarak önerilir."}
                    </Typography>

                    {trackingMode === "lot" &&
                        rows.map((r) => (
                            <Stack key={r.key} direction="row" spacing={1} sx={{ alignItems: "center" }}>
                                <TextField size="small" label="Lot no" value={r.lotNo} disabled={lotOut} onChange={(e) => patch(r.key, { lotNo: e.target.value })} />
                                <TextField
                                    size="small"
                                    type="date"
                                    label="SKT"
                                    value={r.expiry}
                                    disabled={lotOut}
                                    onChange={(e) => patch(r.key, { expiry: e.target.value })}
                                    slotProps={{ inputLabel: { shrink: true } }}
                                />
                                <TextField size="small" label="Miktar" value={r.quantity} onChange={(e) => patch(r.key, { quantity: e.target.value })} sx={{ width: 110 }} />
                                <IconButton aria-label={`${r.lotNo || "Lot"} satırını kaldır`} onClick={() => setRows((current) => current.filter((x) => x.key !== r.key))}>
                                    <IconTrash size={18} />
                                </IconButton>
                            </Stack>
                        ))}
                    {mode === "in" && trackingMode === "lot" && (
                        <div>
                            <Button size="small" onClick={() => setRows((current) => [...current, { key: nextKey(), lotId: null, lotNo: "", expiry: "", quantity: "" }])}>
                                Lot ekle
                            </Button>
                        </div>
                    )}

                    {trackingMode === "serial" && !serialOut && (
                        <TextField
                            multiline
                            minRows={4}
                            label="Seri numaraları"
                            helperText="Her satıra bir seri numarası yazın (ya da virgülle ayırın)."
                            value={serialText}
                            onChange={(e) => setSerialText(e.target.value)}
                        />
                    )}
                    {serialOut && (
                        <Autocomplete
                            multiple
                            options={available}
                            value={picked}
                            onChange={(_, value) => setPicked(value)}
                            renderInput={(params) => <TextField {...params} label="Seri numaraları" helperText="Bu depoda stokta olan seri numaralarından seçin." />}
                        />
                    )}

                    {problem && <Alert severity="warning">{problem}</Alert>}
                </Stack>
            </DialogContent>
            <DialogActions>
                <Button onClick={onClose}>Vazgeç</Button>
                <Button variant="contained" disabled={problem !== null} onClick={save}>
                    Tamam
                </Button>
            </DialogActions>
        </>
    );
}
