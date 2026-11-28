"use client";

import { useEffect, useMemo, useRef, useState } from "react";
import Button from "@mui/material/Button";
import Checkbox from "@mui/material/Checkbox";
import FormControlLabel from "@mui/material/FormControlLabel";
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
import FormAlert from "@/components/auth/FormAlert";
import LotSerialDialog, { type TrackingResult } from "@/components/inventory/LotSerialDialog";
import { catalog } from "@/lib/api/catalog";
import { ApiError } from "@/lib/api/client";
import { erp, type AddressDto } from "@/lib/api/erp";
import { errorMessage } from "@/lib/api/errorMessages";
import { inventory, type WarehouseDto } from "@/lib/api/inventory";
import { sales, type OrderDto, type ShipmentDto, type ShipmentInput } from "@/lib/api/sales";
import { parseAmount } from "@/lib/money";
import { trackingPayload, useTrackingModes, type Tracking } from "./useTracking";

const qty = (n: number) => n.toLocaleString("tr-TR", { maximumFractionDigits: 4 });
const asText = (n: number) => String(n).replace(".", ",");
const nativeSelect = (label: string) => ({ select: { native: true }, htmlInput: { "aria-label": label }, inputLabel: { shrink: true } });

type ShipmentFormProps = { order: OrderDto; onClose(): void; onDone(shipment: ShipmentDto): void };

/**
 * Sevk formu: açık miktarlar önceden dolar, teslimat adresi ve depo seçilir, takipli kalemlerde lot / seri P3 penceresiyle girilir.
 * Barkod okutma ilgili satırın sevk miktarını 1 artırır; miktar açığı aşarsa kayıt engellenir.
 */
export default function ShipmentForm({ order, onClose, onDone }: ShipmentFormProps) {
    const open = useMemo(() => order.lines.filter((l) => l.openQuantity > 0), [order.lines]);
    const [values, setValues] = useState<Record<string, string>>(() => Object.fromEntries(open.map((l) => [l.id, asText(l.openQuantity)])));
    const [cargoNote, setCargoNote] = useState("");
    const [warehouses, setWarehouses] = useState<WarehouseDto[]>([]);
    const [warehouseId, setWarehouseId] = useState("");
    const [addresses, setAddresses] = useState<AddressDto[]>([]);
    const [addressId, setAddressId] = useState("");
    const [tracking, setTracking] = useState<Record<string, Tracking>>({});
    const [trackingLine, setTrackingLine] = useState<string | null>(null);
    const [draft, setDraft] = useState(false);
    const [scan, setScan] = useState("");
    const [notice, setNotice] = useState<string | null>(null);
    const [error, setError] = useState<ApiError | string | null>(null);
    const [busy, setBusy] = useState(false);
    const scanRef = useRef<HTMLInputElement>(null);
    const modes = useTrackingModes(open.map((l) => l.productId));

    useEffect(() => {
        let cancelled = false;
        // Depo ve adres listeleri yetkiye bağlıdır; okunamazsa sunucu varsayılanları (varsayılan depo / varsayılan teslimat adresi) kullanılır.
        inventory.warehouses().then(
            (list) => !cancelled && setWarehouses(list.filter((w) => w.isActive && w.kind === "normal" && w.branchId === order.branchId)),
            () => undefined,
        );
        erp.getAccount(order.currentAccountId).then(
            (account) => {
                if (cancelled) return;
                const shipping = account.addresses.filter((a) => a.isShipping);
                setAddresses(shipping);
                setAddressId(shipping.find((a) => a.isDefaultShipping)?.id ?? "");
            },
            () => undefined,
        );
        return () => {
            cancelled = true;
        };
    }, [order.branchId, order.currentAccountId]);

    const quantityOf = (id: string) => parseAmount((values[id] ?? "").trim(), { maxDecimals: 4 });
    const activeLine = open.find((l) => l.id === trackingLine) ?? null;
    const activeQuantity = activeLine ? quantityOf(activeLine.id) : null;
    const lotWarehouse = warehouseId || activeLine?.warehouseId || warehouses.find((w) => w.isDefault)?.id || "";

    async function onScan(code: string) {
        const text = code.trim();
        if (!text) return;
        setScan("");
        setError(null);
        setNotice(null);
        try {
            const items = await catalog.searchItems(text, 5);
            const hit = items.find((i) => open.some((l) => l.productId === i.productId && (l.variantId ?? null) === (i.variantId ?? null)));
            const line = hit ? open.find((l) => l.productId === hit.productId && (l.variantId ?? null) === (hit.variantId ?? null)) : null;
            if (!hit || !line) {
                setError(`"${text}" barkoduna ait kalem bu siparişin açık satırlarında yok.`);
                return;
            }
            setValues((cur) => ({ ...cur, [line.id]: asText(Math.round(((parseAmount(cur[line.id] ?? "", { maxDecimals: 4 }) ?? 0) + 1) * 1e4) / 1e4) }));
            setTracking((cur) => ({ ...cur, [line.id]: { lots: [], serials: [] } }));
            setNotice(`${hit.code} · ${hit.name} okutuldu.`);
        } catch (e) {
            setError(errorMessage(e));
        } finally {
            scanRef.current?.focus();
        }
    }

    async function submit() {
        const lines: ShipmentInput["lines"] = [];
        for (const l of open) {
            const q = quantityOf(l.id);
            if (q === null) return setError(`${l.productCode}: sevk miktarı geçersiz.`);
            if (q === 0) continue;
            if (q > l.openQuantity) return setError(`${l.productCode}: ${errorMessage(new ApiError(400, "", "ship_exceeds_open"))}`);
            const extra = trackingPayload(l.productCode, modes[l.productId] ?? null, q, l.factor, tracking[l.id]);
            if (typeof extra === "string") return setError(extra);
            lines.push({ orderLineId: l.id, quantity: q, ...extra });
        }
        if (lines.length === 0) return setError("Sevk edilecek miktar girilmelidir.");
        setBusy(true);
        setError(null);
        try {
            const shipment = await sales.shipments.create({
                orderId: order.id,
                warehouseId: warehouseId || null,
                deliveryAddressId: addressId || null,
                cargoNote: cargoNote.trim() || null,
                draft: draft || undefined,
                lines,
            });
            onDone(shipment);
        } catch (e) {
            if (!(e instanceof ApiError)) throw e;
            setError(errorMessage(e));
        } finally {
            setBusy(false);
        }
    }

    return (
        <>
            <Dialog open onClose={onClose} fullWidth maxWidth="md">
                <DialogTitle>Sevk et · {order.number}</DialogTitle>
                <DialogContent>
                    <Stack spacing={2} sx={{ pt: 1 }}>
                        <FormAlert error={error} />
                        {notice && !error && <FormAlert severity="success">{notice}</FormAlert>}
                        <TextField
                            inputRef={scanRef}
                            size="small"
                            label="Barkod okut"
                            helperText="Barkodu okutup Enter'a basın; her okutma sevk miktarını 1 artırır."
                            value={scan}
                            onChange={(e) => setScan(e.target.value)}
                            onKeyDown={(e) => {
                                if (e.key === "Enter") {
                                    e.preventDefault();
                                    void onScan(scan);
                                }
                            }}
                            sx={{ maxWidth: 360 }}
                        />
                        <Table size="small">
                            <TableHead>
                                <TableRow>
                                    <TableCell>Kalem</TableCell>
                                    <TableCell align="right">Açık</TableCell>
                                    <TableCell sx={{ width: 150 }}>Sevk miktarı</TableCell>
                                    <TableCell>Lot / seri</TableCell>
                                </TableRow>
                            </TableHead>
                            <TableBody>
                                {open.map((l) => {
                                    const mode = modes[l.productId] ?? null;
                                    return (
                                        <TableRow key={l.id}>
                                            <TableCell>
                                                {l.productCode} · {l.productName}
                                            </TableCell>
                                            <TableCell align="right">
                                                {qty(l.openQuantity)} {l.unit}
                                            </TableCell>
                                            <TableCell>
                                                <TextField
                                                    size="small"
                                                    value={values[l.id] ?? ""}
                                                    onChange={(e) => {
                                                        setValues((cur) => ({ ...cur, [l.id]: e.target.value }));
                                                        setTracking((cur) => ({ ...cur, [l.id]: { lots: [], serials: [] } }));
                                                    }}
                                                    slotProps={{ htmlInput: { "aria-label": `${l.productName} sevk miktarı`, inputMode: "decimal" } }}
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
                        <Stack direction={{ xs: "column", md: "row" }} spacing={2}>
                            {warehouses.length > 0 && (
                                <TextField select size="small" label="Depo" value={warehouseId} onChange={(e) => setWarehouseId(e.target.value)} slotProps={nativeSelect("Depo")} sx={{ minWidth: 220 }}>
                                    <option value="">Varsayılan depo</option>
                                    {warehouses.map((w) => (
                                        <option key={w.id} value={w.id}>
                                            {w.name}
                                        </option>
                                    ))}
                                </TextField>
                            )}
                            {addresses.length > 0 && (
                                <TextField select size="small" label="Teslimat adresi" value={addressId} onChange={(e) => setAddressId(e.target.value)} slotProps={nativeSelect("Teslimat adresi")} sx={{ minWidth: 260 }}>
                                    <option value="">Varsayılan teslimat adresi</option>
                                    {addresses.map((a) => (
                                        <option key={a.id} value={a.id}>
                                            {a.title} · {a.line}
                                        </option>
                                    ))}
                                </TextField>
                            )}
                            <TextField size="small" label="Kargo / teslimat notu" value={cargoNote} onChange={(e) => setCargoNote(e.target.value)} sx={{ flexGrow: 1 }} />
                        </Stack>
                        <FormControlLabel
                            control={<Checkbox checked={draft} onChange={(e) => setDraft(e.target.checked)} />}
                            label="Taslak oluştur (toplama ve paketleme akışına gönder; stok sevk anında çıkar)"
                        />
                    </Stack>
                </DialogContent>
                <DialogActions>
                    <Button onClick={onClose}>Vazgeç</Button>
                    <Button variant="contained" disabled={busy} onClick={() => void submit()}>
                        {busy ? "Kaydediliyor..." : draft ? "Taslak oluştur" : "Sevk et"}
                    </Button>
                </DialogActions>
            </Dialog>
            {activeLine && activeQuantity !== null && activeQuantity > 0 && modes[activeLine.productId] && lotWarehouse && (
                <LotSerialDialog
                    open
                    mode="out"
                    trackingMode={modes[activeLine.productId] as "lot" | "serial"}
                    productId={activeLine.productId}
                    variantId={activeLine.variantId}
                    warehouseId={lotWarehouse}
                    quantity={activeQuantity}
                    factor={activeLine.factor}
                    title={`${activeLine.productCode} · ${modes[activeLine.productId] === "serial" ? "Sevk edilecek seri numaraları" : "Sevk edilecek lotlar"}`}
                    initialLots={tracking[activeLine.id]?.lots}
                    initialSerials={tracking[activeLine.id]?.serials}
                    onClose={() => setTrackingLine(null)}
                    onSave={(r: TrackingResult) => setTracking((cur) => ({ ...cur, [activeLine.id]: { lots: r.lots ?? [], serials: r.serials ?? [] } }))}
                />
            )}
        </>
    );
}
