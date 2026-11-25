"use client";

import { useEffect, useState } from "react";
import Link from "next/link";
import Button from "@mui/material/Button";
import Checkbox from "@mui/material/Checkbox";
import Dialog from "@mui/material/Dialog";
import DialogActions from "@mui/material/DialogActions";
import DialogContent from "@mui/material/DialogContent";
import DialogTitle from "@mui/material/DialogTitle";
import FormControlLabel from "@mui/material/FormControlLabel";
import Stack from "@mui/material/Stack";
import Table from "@mui/material/Table";
import TableBody from "@mui/material/TableBody";
import TableCell from "@mui/material/TableCell";
import TableHead from "@mui/material/TableHead";
import TableRow from "@mui/material/TableRow";
import TextField from "@mui/material/TextField";
import Typography from "@mui/material/Typography";
import FormAlert from "@/components/auth/FormAlert";
import LotSerialDialog, { type TrackingResult } from "@/components/inventory/LotSerialDialog";
import { catalog } from "@/lib/api/catalog";
import { ApiError } from "@/lib/api/client";
import { errorMessage } from "@/lib/api/errorMessages";
import type { LotInput } from "@/lib/api/inventory";
import { purchasing, type ReceiptDto, type ReceiptLineDto, type ReturnResult } from "@/lib/api/purchasing";
import { todayIso } from "@/lib/date";
import { parseAmount } from "@/lib/money";
import { useSession } from "@/session/SessionProvider";

type ReturnDialogProps = { receipt: ReceiptDto; onClose(): void; onDone(): void };

type Tracking = { lots: LotInput[]; serials: string[] };

/** Stoğun fiilen durduğu depo: serbest bırakılmış karantina satırı hedef depoda, diğerleri mal kabul deposundadır. */
const stockWarehouse = (l: ReceiptLineDto) => (l.quarantine === "released" ? (l.targetWarehouseId ?? l.warehouseId) : l.warehouseId);
/** İade satırı biriminin temel birime çarpanı. */
const factorOf = (l: ReceiptLineDto) => (l.quantity > 0 ? l.baseQuantity / l.quantity : 1);
const near = (a: number, b: number) => Math.abs(a - b) < 1e-6;

/** Mal kabulden tedarikçiye iade: satır başına iade miktarı (boş = iade yok), gerekçe ve isteğe bağlı iade faturası taslağı. */
export default function ReturnDialog({ receipt, onClose, onDone }: ReturnDialogProps) {
    const { hasPermission } = useSession();
    const canInvoice = hasPermission("fatura.manage") && hasPermission("fiyat.alis");
    const [quantities, setQuantities] = useState<Record<string, string>>({});
    const [reason, setReason] = useState("");
    const [date, setDate] = useState(todayIso());
    const [createInvoice, setCreateInvoice] = useState(false);
    const [error, setError] = useState<string | null>(null);
    const [result, setResult] = useState<ReturnResult | null>(null);
    const [busy, setBusy] = useState(false);
    // Takip türü (lot / seri) kalem kartından okunur; takipsiz kalemde null.
    const [modes, setModes] = useState<Record<string, "lot" | "serial" | null>>({});
    const [tracking, setTracking] = useState<Record<string, Tracking>>({});
    const [trackingLine, setTrackingLine] = useState<string | null>(null);

    useEffect(() => {
        let cancelled = false;
        const ids = [...new Set(receipt.lines.map((l) => l.productId))];
        for (const id of ids) {
            catalog.getProduct(id).then(
                (p) => !cancelled && setModes((cur) => ({ ...cur, [id]: p.tracksSerialLot ? (p.trackingMode ?? "lot") : null })),
                () => undefined,
            );
        }
        return () => {
            cancelled = true;
        };
    }, [receipt]);

    const activeLine = receipt.lines.find((l) => l.id === trackingLine) ?? null;
    const activeQuantity = activeLine ? parseAmount((quantities[activeLine.id] ?? "").trim(), { maxDecimals: 4 }) : null;

    async function submit() {
        const lines: { receiptLineId: string; quantity: number; lots?: LotInput[]; serials?: string[] }[] = [];
        for (const l of receipt.lines) {
            const text = (quantities[l.id] ?? "").trim();
            if (!text) continue;
            const q = parseAmount(text, { maxDecimals: 4 });
            if (q === null || q <= 0) return setError(`${l.productCode}: iade miktarı geçersiz.`);
            if (q > l.quantity) return setError(`${l.productCode}: iade miktarı kabul edilen miktarı aşıyor.`);
            const mode = modes[l.productId] ?? null;
            const t = tracking[l.id];
            if (mode === "serial") {
                if (!t || t.serials.length !== q) return setError(`${l.productCode}: seri numaralarını girin (${q} adet).`);
                lines.push({ receiptLineId: l.id, quantity: q, serials: t.serials });
            } else if (mode === "lot") {
                if (!t || t.lots.length === 0 || !near(t.lots.reduce((sum, x) => sum + x.quantity, 0), q)) return setError(`${l.productCode}: lot bilgisini girin.`);
                // Lot miktarları satır biriminde seçilir; stok hareketi temel birimde yazıldığı için çevrilir.
                const factor = factorOf(l);
                lines.push({ receiptLineId: l.id, quantity: q, lots: t.lots.map((x) => ({ ...x, quantity: Math.round(x.quantity * factor * 1e6) / 1e6 })) });
            } else {
                lines.push({ receiptLineId: l.id, quantity: q });
            }
        }
        if (lines.length === 0) return setError("En az bir satır için iade miktarı girin.");
        if (reason.trim().length < 2) return setError("İade gerekçesi zorunludur.");
        setBusy(true);
        setError(null);
        try {
            setResult(await purchasing.createReturn({ receiptId: receipt.id, lines, date, reason: reason.trim(), createInvoice: createInvoice && canInvoice }));
        } catch (e) {
            if (!(e instanceof ApiError)) throw e;
            setError(errorMessage(e));
        } finally {
            setBusy(false);
        }
    }

    return (
        <>
        <Dialog open onClose={result ? onDone : onClose} fullWidth maxWidth="md">
            <DialogTitle>Tedarikçiye iade · {receipt.number}</DialogTitle>
            <DialogContent>
                <Stack spacing={2} sx={{ pt: 1 }}>
                    <FormAlert error={error} />
                    {result ? (
                        <Stack spacing={1}>
                            <FormAlert severity="success">İade stoktan düşüldü.</FormAlert>
                            {result.invoiceId && (
                                <Typography component={Link} href={`/satin-alma/faturalar/${result.invoiceId}`} sx={{ color: "primary.main" }}>
                                    İade faturası taslağını aç{result.invoiceNumber ? ` (${result.invoiceNumber})` : ""}
                                </Typography>
                            )}
                        </Stack>
                    ) : (
                        <>
                            <Table size="small">
                                <TableHead>
                                    <TableRow>
                                        <TableCell>Kalem</TableCell>
                                        <TableCell align="right">Kabul edilen</TableCell>
                                        <TableCell sx={{ width: 150 }}>İade miktarı</TableCell>
                                        <TableCell>Lot / seri</TableCell>
                                    </TableRow>
                                </TableHead>
                                <TableBody>
                                    {receipt.lines.map((l) => (
                                        <TableRow key={l.id}>
                                            <TableCell>
                                                {l.productCode} · {l.productName}
                                            </TableCell>
                                            <TableCell align="right">
                                                {l.quantity.toLocaleString("tr-TR", { maximumFractionDigits: 4 })} {l.unit}
                                            </TableCell>
                                            <TableCell>
                                                <TextField
                                                    size="small"
                                                    value={quantities[l.id] ?? ""}
                                                    onChange={(e) => {
                                                        setQuantities((cur) => ({ ...cur, [l.id]: e.target.value }));
                                                        setTracking((cur) => ({ ...cur, [l.id]: { lots: [], serials: [] } }));
                                                    }}
                                                    slotProps={{ htmlInput: { "aria-label": `${l.productCode} iade miktarı`, inputMode: "decimal" } }}
                                                />
                                            </TableCell>
                                            <TableCell>
                                                {modes[l.productId] ? (
                                                    <Button
                                                        size="small"
                                                        variant="outlined"
                                                        disabled={!(parseAmount((quantities[l.id] ?? "").trim(), { maxDecimals: 4 }) ?? 0)}
                                                        onClick={() => setTrackingLine(l.id)}
                                                    >
                                                        {modes[l.productId] === "serial" ? `Seri (${tracking[l.id]?.serials.length ?? 0})` : `Lot (${tracking[l.id]?.lots.length ?? 0})`}
                                                    </Button>
                                                ) : (
                                                    "—"
                                                )}
                                            </TableCell>
                                        </TableRow>
                                    ))}
                                </TableBody>
                            </Table>
                            <Stack direction={{ xs: "column", md: "row" }} spacing={2}>
                                <TextField size="small" type="date" label="İade tarihi" value={date} onChange={(e) => setDate(e.target.value)} slotProps={{ inputLabel: { shrink: true } }} />
                                <TextField size="small" label="Gerekçe" value={reason} onChange={(e) => setReason(e.target.value)} sx={{ flexGrow: 1 }} slotProps={{ htmlInput: { maxLength: 200 } }} />
                            </Stack>
                            {canInvoice && <FormControlLabel control={<Checkbox checked={createInvoice} onChange={(e) => setCreateInvoice(e.target.checked)} />} label="İade faturası taslağı oluştur" />}
                        </>
                    )}
                </Stack>
            </DialogContent>
            <DialogActions>
                {result ? (
                    <Button variant="contained" onClick={onDone}>
                        Tamam
                    </Button>
                ) : (
                    <>
                        <Button onClick={onClose}>Vazgeç</Button>
                        <Button variant="contained" color="error" disabled={busy} onClick={() => void submit()}>
                            {busy ? "İşleniyor..." : "İade et"}
                        </Button>
                    </>
                )}
            </DialogActions>
        </Dialog>
            {activeLine && activeQuantity !== null && activeQuantity > 0 && modes[activeLine.productId] && (
                <LotSerialDialog
                    open
                    mode="out"
                    trackingMode={modes[activeLine.productId] as "lot" | "serial"}
                    productId={activeLine.productId}
                    variantId={activeLine.variantId}
                    warehouseId={stockWarehouse(activeLine)}
                    quantity={activeQuantity}
                    factor={factorOf(activeLine)}
                    title={`${activeLine.productCode} · ${modes[activeLine.productId] === "serial" ? "İade edilecek seri numaraları" : "İade edilecek lotlar"}`}
                    initialLots={tracking[activeLine.id]?.lots}
                    initialSerials={tracking[activeLine.id]?.serials}
                    onClose={() => setTrackingLine(null)}
                    onSave={(r: TrackingResult) => setTracking((cur) => ({ ...cur, [activeLine.id]: { lots: r.lots ?? [], serials: r.serials ?? [] } }))}
                />
            )}
        </>
    );
}
