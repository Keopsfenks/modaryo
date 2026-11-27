"use client";

import { useEffect, useState } from "react";
import Link from "next/link";
import Button from "@mui/material/Button";
import Dialog from "@mui/material/Dialog";
import DialogActions from "@mui/material/DialogActions";
import DialogContent from "@mui/material/DialogContent";
import DialogTitle from "@mui/material/DialogTitle";
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
import { ApiError } from "@/lib/api/client";
import { errorMessage } from "@/lib/api/errorMessages";
import { inventory, type WarehouseDto } from "@/lib/api/inventory";
import type { InvoiceDto } from "@/lib/api/invoicing";
import { RETURN_KIND_LABELS, sales, type ReturnDto, type ReturnInput, type ReturnWarehouseKind } from "@/lib/api/sales";
import { todayIso } from "@/lib/date";
import { parseAmount } from "@/lib/money";
import { trackingPayload, useTrackingModes, type Tracking } from "./useTracking";

const qty = (n: number) => n.toLocaleString("tr-TR", { maximumFractionDigits: 4 });
const nativeSelect = (label: string) => ({ select: { native: true }, htmlInput: { "aria-label": label }, inputLabel: { shrink: true } });

type ReturnFormProps = { invoice: InvoiceDto; onClose(): void; onDone(): void };

/** Satış faturasından müşteri iadesi: hedef depo türü (normal / karantina / hasarlı), depo ve satır başına iade miktarı. */
export default function ReturnForm({ invoice, onClose, onDone }: ReturnFormProps) {
    const lines = invoice.lines.filter((l) => l.productId);
    const [kind, setKind] = useState<ReturnWarehouseKind>("normal");
    const [warehouses, setWarehouses] = useState<WarehouseDto[]>([]);
    const [warehouseId, setWarehouseId] = useState("");
    const [quantities, setQuantities] = useState<Record<string, string>>({});
    const [reason, setReason] = useState("");
    const [date, setDate] = useState(todayIso());
    const [tracking, setTracking] = useState<Record<string, Tracking>>({});
    const [trackingLine, setTrackingLine] = useState<string | null>(null);
    const [error, setError] = useState<string | null>(null);
    const [result, setResult] = useState<ReturnDto | null>(null);
    const [busy, setBusy] = useState(false);
    const modes = useTrackingModes(lines.map((l) => l.productId as string));

    useEffect(() => {
        let cancelled = false;
        inventory.warehouses().then(
            (list) => !cancelled && setWarehouses(list.filter((w) => w.isActive && w.branchId === invoice.branchId)),
            () => !cancelled && setError("Depolar yüklenemedi; depo listesi için stok görüntüleme yetkisi gerekir."),
        );
        return () => {
            cancelled = true;
        };
    }, [invoice.branchId]);

    const options = warehouses.filter((w) => w.kind === kind);
    // Seçili depo yeni türe uymuyorsa ilk uygun depoya düşer (varsayılan varsa o).
    const effectiveWarehouse = options.some((w) => w.id === warehouseId) ? warehouseId : (options.find((w) => w.isDefault) ?? options[0])?.id ?? "";
    const activeLine = lines.find((l) => l.id === trackingLine) ?? null;
    const quantityOf = (id: string) => parseAmount((quantities[id] ?? "").trim(), { maxDecimals: 4 });

    async function submit() {
        const payload: ReturnInput["lines"] = [];
        for (const l of lines) {
            const text = (quantities[l.id] ?? "").trim();
            if (!text) continue;
            const q = parseAmount(text, { maxDecimals: 4 });
            const label = l.description || "Satır";
            if (q === null || q <= 0) return setError(`${label}: iade miktarı geçersiz.`);
            if (q > l.quantity) return setError(`${label}: ${errorMessage(new ApiError(400, "", "return_exceeds_invoiced"))}`);
            const extra = trackingPayload(label, modes[l.productId as string] ?? null, q, l.factor, tracking[l.id]);
            if (typeof extra === "string") return setError(extra);
            payload.push({ invoiceLineId: l.id, quantity: q, ...extra });
        }
        if (payload.length === 0) return setError("En az bir satır için iade miktarı girin.");
        if (!effectiveWarehouse) return setError("İade için bir depo seçin.");
        setBusy(true);
        setError(null);
        try {
            setResult(await sales.returns.create({ invoiceId: invoice.id, warehouseKind: kind, warehouseId: effectiveWarehouse, date, reason: reason.trim() || null, lines: payload }));
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
                <DialogTitle>Müşteri iadesi · {invoice.number ?? "Fatura"}</DialogTitle>
                <DialogContent>
                    <Stack spacing={2} sx={{ pt: 1 }}>
                        <FormAlert error={error} />
                        {result ? (
                            <Stack spacing={1}>
                                <FormAlert severity="success">İade kaydedildi ve stoğa girdi ({result.number}).</FormAlert>
                                {result.returnInvoiceId && (
                                    <Typography component={Link} href={`/satis/faturalar/${result.returnInvoiceId}`} sx={{ color: "primary.main" }}>
                                        İade faturası taslağını aç
                                    </Typography>
                                )}
                            </Stack>
                        ) : (
                            <>
                                <Stack direction={{ xs: "column", md: "row" }} spacing={2}>
                                    <TextField
                                        select
                                        size="small"
                                        label="İade türü"
                                        value={kind}
                                        onChange={(e) => setKind(e.target.value as ReturnWarehouseKind)}
                                        slotProps={nativeSelect("İade türü")}
                                        sx={{ minWidth: 200 }}
                                    >
                                        {(Object.keys(RETURN_KIND_LABELS) as ReturnWarehouseKind[]).map((k) => (
                                            <option key={k} value={k}>
                                                {RETURN_KIND_LABELS[k]}
                                            </option>
                                        ))}
                                    </TextField>
                                    <TextField
                                        select
                                        size="small"
                                        label="Hedef depo"
                                        value={effectiveWarehouse}
                                        onChange={(e) => setWarehouseId(e.target.value)}
                                        slotProps={nativeSelect("Hedef depo")}
                                        helperText={options.length === 0 && warehouses.length > 0 ? `Bu şubede ${RETURN_KIND_LABELS[kind].toLowerCase()} türünde depo yok.` : undefined}
                                        sx={{ minWidth: 240 }}
                                    >
                                        {options.length === 0 && <option value="">Depo yok</option>}
                                        {options.map((w) => (
                                            <option key={w.id} value={w.id}>
                                                {w.name}
                                            </option>
                                        ))}
                                    </TextField>
                                    <TextField size="small" type="date" label="İade tarihi" value={date} onChange={(e) => setDate(e.target.value)} slotProps={{ inputLabel: { shrink: true } }} />
                                </Stack>
                                <Table size="small">
                                    <TableHead>
                                        <TableRow>
                                            <TableCell>Kalem</TableCell>
                                            <TableCell align="right">Faturalanan</TableCell>
                                            <TableCell sx={{ width: 150 }}>İade miktarı</TableCell>
                                            <TableCell>Lot / seri</TableCell>
                                        </TableRow>
                                    </TableHead>
                                    <TableBody>
                                        {lines.map((l) => {
                                            const mode = modes[l.productId as string] ?? null;
                                            return (
                                                <TableRow key={l.id}>
                                                    <TableCell>{l.description}</TableCell>
                                                    <TableCell align="right">
                                                        {qty(l.quantity)} {l.unit}
                                                    </TableCell>
                                                    <TableCell>
                                                        <TextField
                                                            size="small"
                                                            value={quantities[l.id] ?? ""}
                                                            onChange={(e) => {
                                                                setQuantities((cur) => ({ ...cur, [l.id]: e.target.value }));
                                                                setTracking((cur) => ({ ...cur, [l.id]: { lots: [], serials: [] } }));
                                                            }}
                                                            slotProps={{ htmlInput: { "aria-label": `${l.description} iade miktarı`, inputMode: "decimal" } }}
                                                        />
                                                    </TableCell>
                                                    <TableCell>
                                                        {mode ? (
                                                            <Button size="small" variant="outlined" disabled={!(quantityOf(l.id) ?? 0)} onClick={() => setTrackingLine(l.id)}>
                                                                {mode === "serial" ? `Seri (${tracking[l.id]?.serials.length ?? 0})` : `Lot (${tracking[l.id]?.lots.length ?? 0})`}
                                                            </Button>
                                                        ) : (
                                                            "—"
                                                        )}
                                                    </TableCell>
                                                </TableRow>
                                            );
                                        })}
                                    </TableBody>
                                </Table>
                                <TextField size="small" label="İade nedeni" value={reason} onChange={(e) => setReason(e.target.value)} slotProps={{ htmlInput: { maxLength: 500 } }} />
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
                                {busy ? "İşleniyor..." : "İade al"}
                            </Button>
                        </>
                    )}
                </DialogActions>
            </Dialog>
            {activeLine && (quantityOf(activeLine.id) ?? 0) > 0 && modes[activeLine.productId as string] && effectiveWarehouse && (
                <LotSerialDialog
                    open
                    mode="in"
                    trackingMode={modes[activeLine.productId as string] as "lot" | "serial"}
                    productId={activeLine.productId as string}
                    variantId={activeLine.variantId ?? null}
                    warehouseId={effectiveWarehouse}
                    quantity={quantityOf(activeLine.id) as number}
                    factor={activeLine.factor}
                    title={`${activeLine.description} · ${modes[activeLine.productId as string] === "serial" ? "İade seri numaraları" : "İade lotları"}`}
                    initialLots={tracking[activeLine.id]?.lots}
                    initialSerials={tracking[activeLine.id]?.serials}
                    onClose={() => setTrackingLine(null)}
                    onSave={(r: TrackingResult) => setTracking((cur) => ({ ...cur, [activeLine.id]: { lots: r.lots ?? [], serials: r.serials ?? [] } }))}
                />
            )}
        </>
    );
}
