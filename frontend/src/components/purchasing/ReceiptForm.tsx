"use client";

import { useEffect, useState } from "react";
import Link from "next/link";
import Alert from "@mui/material/Alert";
import Autocomplete from "@mui/material/Autocomplete";
import Button from "@mui/material/Button";
import IconButton from "@mui/material/IconButton";
import Stack from "@mui/material/Stack";
import Table from "@mui/material/Table";
import TableBody from "@mui/material/TableBody";
import TableCell from "@mui/material/TableCell";
import TableContainer from "@mui/material/TableContainer";
import TableHead from "@mui/material/TableHead";
import TableRow from "@mui/material/TableRow";
import TextField from "@mui/material/TextField";
import Typography from "@mui/material/Typography";
import { IconTrash } from "@tabler/icons-react";
import FormAlert from "@/components/auth/FormAlert";
import PageHeader from "@/components/common/PageHeader";
import DashboardCard from "@/components/dashboard/DashboardCard";
import LotSerialDialog, { type TrackingResult } from "@/components/inventory/LotSerialDialog";
import ItemPicker from "@/components/pricing/ItemPicker";
import { useDebounced } from "@/components/pricing/useDebounced";
import { catalog, type SellableItem } from "@/lib/api/catalog";
import { ApiError } from "@/lib/api/client";
import { errorMessage } from "@/lib/api/errorMessages";
import { erp, type CurrentAccountListItem } from "@/lib/api/erp";
import { identity, type BranchDto } from "@/lib/api/identity";
import { inventory, type LotInput, type WarehouseDto } from "@/lib/api/inventory";
import { purchasing, type PoDto, type ReceiptDto } from "@/lib/api/purchasing";
import { todayIso } from "@/lib/date";
import { parseAmount } from "@/lib/money";
import { useSession } from "@/session/SessionProvider";

export type ReceiptLineDraft = {
    key: string;
    poLineId: string | null;
    productId: string;
    variantId: string | null;
    code: string;
    name: string;
    units: string[];
    unit: string;
    quantity: string;
    /** Siparişteki açık miktar (sipariş birimi); siparişsiz kabulde null. */
    open: number | null;
    tracksSerialLot: boolean;
    trackingMode: "lot" | "serial" | null;
    lots: LotInput[];
    serials: string[];
    error?: string | null;
};

let counter = 0;
const nextKey = () => `rc-line-${++counter}`;
const asText = (n: number) => String(n).replace(".", ",");

export const receiptQuantity = (l: ReceiptLineDraft): number | null => parseAmount(l.quantity, { maxDecimals: 4 });
const isBlank = (l: ReceiptLineDraft) => {
    const q = receiptQuantity(l);
    return l.quantity.trim() === "" || q === 0;
};

/** Barkod, ürün kodu ya da birim barkoduyla eşleşen kalemi bulur; tek sonuç varsa onu alır. */
export function matchScanned(items: SellableItem[], scanned: string): SellableItem | null {
    const exact = items.find((i) => i.code === scanned || i.units.some((u) => u.barcode === scanned));
    return exact ?? (items.length === 1 ? items[0] : null);
}

type ReceiptFormProps = { poId?: string | null };

/** Mal kabul: siparişten (açık miktarlar önceden dolu) ya da siparişsiz; barkod okutma, lot/seri ve tolerans uyarısı. */
export default function ReceiptForm({ poId = null }: ReceiptFormProps) {
    const { hasPermission, canAccessBranch } = useSession();
    const allowed = hasPermission("satinalma.receive");

    const [po, setPo] = useState<PoDto | null>(null);
    const [loaded, setLoaded] = useState(!poId);
    const [supplier, setSupplier] = useState<{ id: string; name: string } | null>(null);
    const [branchId, setBranchId] = useState("");
    const [warehouseId, setWarehouseId] = useState("");
    const [date, setDate] = useState(todayIso());
    const [noteNo, setNoteNo] = useState("");
    const [notes, setNotes] = useState("");
    const [lines, setLines] = useState<ReceiptLineDraft[]>([]);
    const [branches, setBranches] = useState<BranchDto[]>([]);
    const [warehouses, setWarehouses] = useState<WarehouseDto[]>([]);
    const [scan, setScan] = useState("");
    const [lotKey, setLotKey] = useState<string | null>(null);
    const [error, setError] = useState<ApiError | string | null>(null);
    const [result, setResult] = useState<ReceiptDto | null>(null);
    const [busy, setBusy] = useState(false);

    useEffect(() => {
        if (!allowed) return;
        let cancelled = false;
        identity.branches.list().then(
            (list) => {
                if (cancelled) return;
                const usable = list.filter((b) => b.isActive && canAccessBranch(b.id));
                setBranches(usable);
                setBranchId((cur) => cur || (usable.find((b) => b.isDefault) ?? usable[0])?.id || "");
            },
            () => undefined,
        );
        inventory.warehouses().then(
            (w) => !cancelled && setWarehouses(w.filter((x) => x.isActive && x.kind === "normal")),
            () => undefined,
        );
        return () => {
            cancelled = true;
        };
    }, [allowed, canAccessBranch]);

    // Siparişten açık satırlar kalan miktarla dolar; kalem bilgisi (birim, takip) kataloğdan tamamlanır.
    useEffect(() => {
        if (!poId || !allowed) return;
        let cancelled = false;
        purchasing.orders.get(poId).then(
            async (dto) => {
                const products = new Map(
                    (await Promise.all([...new Set(dto.lines.map((l) => l.productId))].map((pid) => catalog.getProduct(pid).then((p) => [pid, p] as const, () => null)))).filter(
                        (e): e is NonNullable<typeof e> => e !== null,
                    ),
                );
                if (cancelled) return;
                setPo(dto);
                setSupplier({ id: dto.supplierId, name: dto.supplierName });
                setBranchId(dto.branchId);
                setLines(
                    dto.lines
                        .filter((l) => l.openQuantity > 0)
                        .map((l) => {
                            const p = products.get(l.productId);
                            return {
                                key: nextKey(),
                                poLineId: l.id,
                                productId: l.productId,
                                variantId: l.variantId,
                                code: l.productCode,
                                name: l.productName,
                                units: [...new Set([l.unit, ...(p ? [p.baseUnit, ...p.units.map((u) => u.unit)] : [])])],
                                unit: l.unit,
                                quantity: asText(l.openQuantity),
                                open: l.openQuantity,
                                tracksSerialLot: p?.tracksSerialLot ?? false,
                                trackingMode: p?.tracksSerialLot ? (p.trackingMode ?? "lot") : null,
                                lots: [],
                                serials: [],
                                error: null,
                            };
                        }),
                );
                setLoaded(true);
            },
            (e: unknown) => {
                if (cancelled) return;
                if (!(e instanceof ApiError)) throw e;
                setError(errorMessage(e));
                setLoaded(true);
            },
        );
        return () => {
            cancelled = true;
        };
    }, [poId, allowed]);

    // Şubenin varsayılan (yoksa ilk) deposu.
    const branchWarehouses = warehouses.filter((w) => w.branchId === branchId);
    const effectiveWarehouse = warehouseId || (branchWarehouses.find((w) => w.isDefault) ?? branchWarehouses[0])?.id || "";

    // Siparişsiz kabulde tedarikçi seçimi.
    const [supplierInput, setSupplierInput] = useState("");
    const supplierSearch = useDebounced(supplierInput, 250);
    const [supplierOptions, setSupplierOptions] = useState<CurrentAccountListItem[]>([]);
    useEffect(() => {
        if (poId || !allowed) return;
        let cancelled = false;
        erp.listAccounts({ search: supplierSearch || undefined, role: "supplier", pageSize: 20 }).then(
            (r) => !cancelled && setSupplierOptions(r.items),
            () => !cancelled && setSupplierOptions([]),
        );
        return () => {
            cancelled = true;
        };
    }, [supplierSearch, poId, allowed]);

    const patch = (key: string, change: Partial<ReceiptLineDraft>) => setLines((cur) => cur.map((l) => (l.key === key ? { ...l, ...change, error: null } : l)));

    async function addFreeLine(item: SellableItem) {
        let mode: "lot" | "serial" | null = null;
        if (item.tracksSerialLot) mode = await catalog.getProduct(item.productId).then((p) => p.trackingMode ?? "lot", () => "lot" as const);
        setLines((cur) => [
            ...cur,
            {
                key: nextKey(),
                poLineId: null,
                productId: item.productId,
                variantId: item.variantId,
                code: item.code,
                name: item.name,
                units: [item.baseUnit, ...item.units.map((u) => u.unit).filter((u) => u !== item.baseUnit)],
                unit: item.baseUnit,
                quantity: "1",
                open: null,
                tracksSerialLot: item.tracksSerialLot,
                trackingMode: mode,
                lots: [],
                serials: [],
                error: null,
            },
        ]);
    }

    /** Barkod okutma: eşleşen satırın miktarı 1 artar; siparişli kabulde siparişte olmayan kalem reddedilir. */
    async function onScan() {
        const code = scan.trim();
        if (!code) return;
        setScan("");
        setError(null);
        let items: SellableItem[];
        try {
            items = await catalog.searchItems(code);
        } catch (e) {
            if (!(e instanceof ApiError)) throw e;
            setError(errorMessage(e));
            return;
        }
        const item = matchScanned(items, code);
        if (!item) return setError(`"${code}" ile eşleşen kalem bulunamadı.`);
        const existing = lines.find((l) => l.productId === item.productId && l.variantId === item.variantId);
        if (existing) {
            const current = receiptQuantity(existing) ?? 0;
            patch(existing.key, { quantity: asText(current + 1) });
        } else if (poId) {
            setError(`${item.code} bu siparişte yok ya da tamamı gelmiş.`);
        } else {
            await addFreeLine(item);
        }
    }

    async function submit() {
        setError(null);
        if (!supplier) return setError("Tedarikçi seçilmelidir.");
        if (!branchId) return setError("Şube seçilmelidir.");
        if (!effectiveWarehouse) return setError("Depo seçilmelidir.");
        const active = lines.filter((l) => !isBlank(l));
        if (active.length === 0) return setError("Teslim alınan miktar girilmelidir.");
        let invalid = false;
        const marked = lines.map((l) => {
            if (isBlank(l)) return l;
            const q = receiptQuantity(l);
            let message: string | null = null;
            if (q === null || q < 0) message = "Miktar geçersiz.";
            else if (l.tracksSerialLot) {
                const filled = l.trackingMode === "serial" ? l.serials.length === q : l.lots.length > 0 && Math.abs(l.lots.reduce((s, x) => s + x.quantity, 0) - q) < 1e-6;
                if (!filled) message = l.trackingMode === "serial" ? "Seri numaralarını girin." : "Lot bilgisini girin.";
            }
            if (message) invalid = true;
            return { ...l, error: message };
        });
        if (invalid) {
            setLines(marked);
            return setError("Satırlardaki hataları düzeltin.");
        }

        setBusy(true);
        try {
            const receipt = await purchasing.receipts.create({
                purchaseOrderId: poId,
                supplierId: supplier.id,
                branchId,
                warehouseId: effectiveWarehouse,
                date,
                deliveryNoteNo: noteNo.trim() || null,
                notes: notes.trim() || null,
                lines: active.map((l) => ({
                    poLineId: l.poLineId,
                    productId: l.productId,
                    variantId: l.variantId,
                    unit: l.unit,
                    quantity: receiptQuantity(l) ?? 0,
                    lots: l.tracksSerialLot && l.trackingMode === "lot" ? l.lots : undefined,
                    serials: l.tracksSerialLot && l.trackingMode === "serial" ? l.serials : undefined,
                })),
            });
            setResult(receipt);
        } catch (e) {
            if (!(e instanceof ApiError)) throw e;
            setError(errorMessage(e));
        } finally {
            setBusy(false);
        }
    }

    if (!allowed) return <Alert severity="warning">Mal kabul için yetkiniz yok.</Alert>;
    if (result) {
        return (
            <>
                <PageHeader title="Mal kabul tamamlandı" subtitle={result.number} />
                <Stack spacing={2}>
                    <Alert severity="success">Mal kabul {result.number} kaydedildi ve stoğa işlendi.</Alert>
                    {result.warnings.includes("over_delivery_within_tolerance") && <Alert severity="warning">Sipariş miktarından fazla teslimat tolerans dahilinde kabul edildi.</Alert>}
                    <Stack direction="row" spacing={1}>
                        {poId && (
                            <Button component={Link} href={`/satin-alma/siparisler/${poId}`} variant="contained">
                                Siparişe dön
                            </Button>
                        )}
                        <Button component={Link} href="/satin-alma/mal-kabul" variant="outlined">
                            Mal kabul listesi
                        </Button>
                    </Stack>
                </Stack>
            </>
        );
    }

    const lotLine = lines.find((l) => l.key === lotKey) ?? null;
    const lotQty = lotLine ? receiptQuantity(lotLine) : null;

    return (
        <>
            <PageHeader title="Yeni mal kabul" subtitle={po ? `${po.number} · ${po.supplierName}` : "Siparişsiz mal kabul"} />
            <Stack spacing={3}>
                <FormAlert error={error} />
                <DashboardCard title="Başlık">
                    <Stack spacing={2}>
                        <Stack direction={{ xs: "column", md: "row" }} spacing={2}>
                            {poId ? (
                                <TextField size="small" label="Tedarikçi" value={supplier?.name ?? ""} disabled sx={{ minWidth: 280 }} />
                            ) : (
                                <Autocomplete
                                    size="small"
                                    sx={{ minWidth: 320 }}
                                    options={supplierOptions}
                                    filterOptions={(x) => x}
                                    getOptionLabel={(o) => `${o.code} · ${o.name}`}
                                    isOptionEqualToValue={(a, b) => a.id === b.id}
                                    value={supplier ? ({ id: supplier.id, code: "", name: supplier.name } as CurrentAccountListItem) : null}
                                    inputValue={supplierInput}
                                    onInputChange={(_, t) => setSupplierInput(t)}
                                    onChange={(_, item) => setSupplier(item ? { id: item.id, name: item.name } : null)}
                                    noOptionsText="Tedarikçi bulunamadı."
                                    renderInput={(params) => <TextField {...params} label="Tedarikçi" />}
                                />
                            )}
                            <TextField select size="small" label="Şube" value={branchId} disabled={!!poId} onChange={(e) => { setBranchId(e.target.value); setWarehouseId(""); }} slotProps={{ select: { native: true } }} sx={{ minWidth: 170 }}>
                                {branches.map((b) => (
                                    <option key={b.id} value={b.id}>
                                        {b.name}
                                    </option>
                                ))}
                            </TextField>
                            <TextField select size="small" label="Depo" value={effectiveWarehouse} onChange={(e) => setWarehouseId(e.target.value)} slotProps={{ select: { native: true } }} sx={{ minWidth: 190 }}>
                                {branchWarehouses.map((w) => (
                                    <option key={w.id} value={w.id}>
                                        {w.name}
                                    </option>
                                ))}
                            </TextField>
                        </Stack>
                        <Stack direction={{ xs: "column", md: "row" }} spacing={2}>
                            <TextField size="small" type="date" label="Teslim tarihi" value={date} onChange={(e) => setDate(e.target.value)} slotProps={{ inputLabel: { shrink: true } }} />
                            <TextField size="small" label="İrsaliye no" value={noteNo} onChange={(e) => setNoteNo(e.target.value)} slotProps={{ htmlInput: { maxLength: 50 } }} />
                            <TextField size="small" label="Not" value={notes} onChange={(e) => setNotes(e.target.value)} sx={{ flexGrow: 1 }} />
                        </Stack>
                    </Stack>
                </DashboardCard>

                <DashboardCard title="Teslim alınan kalemler">
                    <Stack direction={{ xs: "column", md: "row" }} spacing={2} sx={{ mb: 2 }}>
                        <TextField
                            size="small"
                            label="Barkod"
                            value={scan}
                            onChange={(e) => setScan(e.target.value)}
                            onKeyDown={(e) => {
                                if (e.key === "Enter") {
                                    e.preventDefault();
                                    void onScan();
                                }
                            }}
                            helperText="Okutunca eşleşen satırın miktarı 1 artar."
                            sx={{ minWidth: 260 }}
                        />
                        {!poId && <ItemPicker label="Kalem ekle" onPick={(item) => void addFreeLine(item)} clearOnPick />}
                    </Stack>
                    {!loaded ? (
                        <Typography color="text.secondary">Sipariş yükleniyor...</Typography>
                    ) : lines.length === 0 ? (
                        <Typography color="text.secondary" sx={{ py: 2 }}>
                            {poId ? "Siparişte teslim alınacak açık satır kalmamış." : "Henüz satır yok. Barkod okutun ya da kalem ekleyin."}
                        </Typography>
                    ) : (
                        <TableContainer>
                            <Table size="small" sx={{ minWidth: 700 }}>
                                <TableHead>
                                    <TableRow>
                                        <TableCell>Kalem</TableCell>
                                        {poId && <TableCell align="right">Açık</TableCell>}
                                        <TableCell sx={{ width: 130 }}>Gelen miktar</TableCell>
                                        <TableCell sx={{ width: 120 }}>Birim</TableCell>
                                        <TableCell>Lot / seri</TableCell>
                                        <TableCell />
                                    </TableRow>
                                </TableHead>
                                <TableBody>
                                    {lines.map((l) => {
                                        const q = receiptQuantity(l);
                                        const over = l.open !== null && q !== null && q > l.open;
                                        const filled = l.trackingMode === "serial" ? l.serials.length : l.lots.length;
                                        return (
                                            <TableRow key={l.key} sx={l.error ? { bgcolor: "error.light", "& td": { color: "error.contrastText" } } : undefined}>
                                                <TableCell>
                                                    <Typography variant="body2">
                                                        {l.code} · {l.name}
                                                    </Typography>
                                                    {over && (
                                                        <Typography variant="caption" color="warning.main" role="status" sx={{ display: "block" }}>
                                                            Açık miktarı aşıyor; tolerans dahilindeyse uyarıyla kabul edilir, aksi halde reddedilir.
                                                        </Typography>
                                                    )}
                                                    {l.error && (
                                                        <Typography variant="caption" role="alert">
                                                            {l.error}
                                                        </Typography>
                                                    )}
                                                </TableCell>
                                                {poId && <TableCell align="right">{l.open === null ? "—" : l.open.toLocaleString("tr-TR", { maximumFractionDigits: 4 })}</TableCell>}
                                                <TableCell>
                                                    <TextField
                                                        size="small"
                                                        value={l.quantity}
                                                        onChange={(e) => patch(l.key, { quantity: e.target.value, lots: [], serials: [] })}
                                                        error={l.quantity.trim() !== "" && q === null}
                                                        slotProps={{ htmlInput: { "aria-label": `${l.code} miktar`, inputMode: "decimal" } }}
                                                    />
                                                </TableCell>
                                                <TableCell>
                                                    <TextField select size="small" value={l.unit} disabled={!!l.poLineId} onChange={(e) => patch(l.key, { unit: e.target.value })} slotProps={{ select: { native: true }, htmlInput: { "aria-label": `${l.code} birim` } }} fullWidth>
                                                        {l.units.map((u) => (
                                                            <option key={u} value={u}>
                                                                {u}
                                                            </option>
                                                        ))}
                                                    </TextField>
                                                </TableCell>
                                                <TableCell>
                                                    {l.tracksSerialLot ? (
                                                        <Button size="small" variant="outlined" disabled={q === null || q <= 0} onClick={() => setLotKey(l.key)}>
                                                            {l.trackingMode === "serial" ? `Seri (${filled})` : `Lot (${filled})`}
                                                        </Button>
                                                    ) : (
                                                        "—"
                                                    )}
                                                </TableCell>
                                                <TableCell align="right">
                                                    <IconButton aria-label={`${l.code} satırını kaldır`} onClick={() => setLines((cur) => cur.filter((x) => x.key !== l.key))}>
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
                </DashboardCard>

                <Stack direction="row" spacing={1}>
                    <Button variant="contained" disabled={busy} onClick={() => void submit()}>
                        {busy ? "Kaydediliyor..." : "Mal kabulü kaydet"}
                    </Button>
                </Stack>
            </Stack>

            {lotLine && lotQty !== null && lotLine.trackingMode && (
                <LotSerialDialog
                    open
                    mode="in"
                    trackingMode={lotLine.trackingMode}
                    productId={lotLine.productId}
                    variantId={lotLine.variantId}
                    warehouseId={effectiveWarehouse}
                    quantity={lotQty}
                    title={`${lotLine.code} · ${lotLine.trackingMode === "serial" ? "Seri numaraları" : "Lot bilgisi"}`}
                    initialLots={lotLine.lots}
                    initialSerials={lotLine.serials}
                    onClose={() => setLotKey(null)}
                    onSave={(r: TrackingResult) => patch(lotLine.key, { lots: r.lots ?? [], serials: r.serials ?? [] })}
                />
            )}
        </>
    );
}
