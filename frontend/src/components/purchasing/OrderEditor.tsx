"use client";

import { useEffect, useState } from "react";
import { useRouter } from "next/navigation";
import Alert from "@mui/material/Alert";
import Autocomplete from "@mui/material/Autocomplete";
import Box from "@mui/material/Box";
import Button from "@mui/material/Button";
import CircularProgress from "@mui/material/CircularProgress";
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
import ItemPicker from "@/components/pricing/ItemPicker";
import { useDebounced } from "@/components/pricing/useDebounced";
import { catalog, type SellableItem } from "@/lib/api/catalog";
import { ApiError } from "@/lib/api/client";
import { errorMessage } from "@/lib/api/errorMessages";
import { erp, type CurrentAccountListItem } from "@/lib/api/erp";
import { identity, type BranchDto } from "@/lib/api/identity";
import { purchasing, type LastPurchase, type PoDto, type PoInput, type PoLineDto } from "@/lib/api/purchasing";
import { formatDate, todayIso } from "@/lib/date";
import { CURRENCIES, formatMoney, parseAmount } from "@/lib/money";
import { useSession } from "@/session/SessionProvider";
import OrderDetail, { OrderActions, PO_BASE } from "./OrderDetail";

const VAT_RATES = [0, 1, 10, 20];
const nativeSelect = (label: string) => ({ select: { native: true }, htmlInput: { "aria-label": label } });

export type PoLineDraft = {
    key: string;
    productId: string;
    variantId: string | null;
    code: string;
    name: string;
    baseUnit: string;
    units: string[];
    unit: string;
    quantity: string;
    /** Boşsa sunucu alış fiyat önceliğinden (tedarikçi listesi, varsayılan liste, kart) doldurur. */
    unitPrice: string;
    discountPercent: string;
    vatRate: number;
    expectedDate: string;
    salesOrderLineId: string | null;
    lastPurchase: LastPurchase | null;
    error?: string | null;
};

let counter = 0;
const nextKey = () => `po-line-${++counter}`;
const text = (n: number | null | undefined) => (n === null || n === undefined ? "" : String(n).replace(".", ","));

function lineFromDto(l: PoLineDto, units: string[] | null): PoLineDraft {
    return {
        key: nextKey(),
        productId: l.productId,
        variantId: l.variantId,
        code: l.productCode,
        name: l.productName,
        baseUnit: units?.[0] ?? l.unit,
        units: units ?? [l.unit],
        unit: l.unit,
        quantity: text(l.quantity),
        unitPrice: text(l.unitPrice),
        discountPercent: text(l.discountPercent),
        vatRate: l.vatRate,
        expectedDate: l.expectedDate ?? "",
        salesOrderLineId: l.salesOrderLineId,
        lastPurchase: l.lastPurchase,
        error: null,
    };
}

export function validatePoLine(l: PoLineDraft): string | null {
    const q = parseAmount(l.quantity, { maxDecimals: 4 });
    if (q === null || q <= 0) return "Miktar sıfırdan büyük olmalıdır.";
    if (l.unitPrice.trim() && parseAmount(l.unitPrice, { maxDecimals: 4 }) === null) return "Birim fiyat geçersiz.";
    if (l.discountPercent.trim()) {
        const d = parseAmount(l.discountPercent, { maxDecimals: 2 });
        if (d === null || d > 100) return "İskonto yüzdesi 0-100 arasında olmalıdır.";
    }
    return null;
}

function toLineInput(l: PoLineDraft) {
    return {
        productId: l.productId,
        variantId: l.variantId,
        unit: l.unit,
        quantity: parseAmount(l.quantity, { maxDecimals: 4 }) ?? 0,
        unitPrice: l.unitPrice.trim() ? parseAmount(l.unitPrice, { maxDecimals: 4 }) : null,
        discountPercent: l.discountPercent.trim() ? parseAmount(l.discountPercent, { maxDecimals: 2 }) : null,
        vatRate: l.vatRate,
        expectedDate: l.expectedDate || null,
        salesOrderLineId: l.salesOrderLineId,
    };
}

/** "Son alış: 12,50 ₺ (10.03.2026)" ipucu. */
export function lastPurchaseHint(lp: LastPurchase | null): string | null {
    return lp ? `Son alış: ${formatMoney(lp.unitPriceTl, "TRY")} (${formatDate(lp.date)})` : null;
}

type EditorProps = { id?: string };

/** Sipariş sayfası: yeni taslak, taslak düzenleme ya da (taslak değilse) salt okunur ayrıntı. */
export default function OrderEditor({ id }: EditorProps) {
    const { hasPermission } = useSession();
    const [po, setPo] = useState<PoDto | null>(null);
    const [error, setError] = useState<ApiError | null>(null);
    const [version, setVersion] = useState(0);
    const allowed = hasPermission("satinalma.read");

    useEffect(() => {
        if (!id || !allowed) return;
        let cancelled = false;
        purchasing.orders.get(id).then(
            (dto) => !cancelled && setPo(dto),
            (e: unknown) => {
                if (cancelled) return;
                if (!(e instanceof ApiError)) throw e;
                setError(e);
            },
        );
        return () => {
            cancelled = true;
        };
    }, [id, allowed, version]);

    if (!allowed) return <Alert severity="warning">Siparişi görüntülemek için yetkiniz yok.</Alert>;
    if (error) return <FormAlert error={error} />;

    const canEdit = hasPermission("satinalma.manage") && hasPermission("fiyat.alis");
    if (!id) {
        return canEdit ? <OrderForm initial={null} onChanged={() => undefined} /> : <Alert severity="warning">Sipariş girmek için satınalma yönetimi ve alış fiyatı yetkisi gerekir.</Alert>;
    }
    if (!po) {
        return (
            <Box sx={{ display: "flex", justifyContent: "center", py: 6 }}>
                <CircularProgress />
            </Box>
        );
    }
    if (po.status !== "draft" || !canEdit) return <OrderDetail po={po} onChanged={() => setVersion((v) => v + 1)} />;
    return <OrderForm key={`${po.id}-${po.version}`} initial={po} onChanged={() => setVersion((v) => v + 1)} />;
}

function OrderForm({ initial, onChanged }: { initial: PoDto | null; onChanged(): void }) {
    const router = useRouter();
    const { canAccessBranch } = useSession();
    const [poId, setPoId] = useState<string | null>(initial?.id ?? null);
    const [version, setVersion] = useState<number | null>(initial?.version ?? null);
    const [supplier, setSupplier] = useState<{ id: string; name: string } | null>(initial ? { id: initial.supplierId, name: initial.supplierName } : null);
    const [branchId, setBranchId] = useState(initial?.branchId ?? "");
    const [date, setDate] = useState(initial?.date ?? todayIso());
    const [expectedDate, setExpectedDate] = useState(initial?.expectedDate ?? "");
    const [currency, setCurrency] = useState(initial?.currency ?? "TRY");
    const [notes, setNotes] = useState(initial?.notes ?? "");
    const [lines, setLines] = useState<PoLineDraft[]>(() => (initial ? initial.lines.map((l) => lineFromDto(l, null)) : []));
    const [branches, setBranches] = useState<BranchDto[]>([]);
    const [error, setError] = useState<ApiError | string | null>(null);
    const [saved, setSaved] = useState(false);
    const [busy, setBusy] = useState(false);

    useEffect(() => {
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
        return () => {
            cancelled = true;
        };
    }, [canAccessBranch]);

    // Kayıtlı taslakta birim listesi kataloğdan tamamlanır.
    useEffect(() => {
        if (!initial) return;
        let cancelled = false;
        const ids = [...new Set(initial.lines.map((l) => l.productId))];
        Promise.all(ids.map((pid) => catalog.getProduct(pid).then((p) => [pid, [p.baseUnit, ...p.units.map((u) => u.unit)]] as const, () => null))).then((entries) => {
            if (cancelled) return;
            const map = new Map(entries.filter((e): e is NonNullable<typeof e> => e !== null));
            setLines((cur) => cur.map((l) => (map.has(l.productId) ? { ...l, baseUnit: map.get(l.productId)![0], units: [...new Set(map.get(l.productId)!)] } : l)));
        });
        return () => {
            cancelled = true;
        };
    }, [initial]);

    // Tedarikçi seçimi.
    const [supplierInput, setSupplierInput] = useState("");
    const supplierSearch = useDebounced(supplierInput, 250);
    const [supplierOptions, setSupplierOptions] = useState<CurrentAccountListItem[]>([]);
    useEffect(() => {
        let cancelled = false;
        erp.listAccounts({ search: supplierSearch || undefined, role: "supplier", pageSize: 20 }).then(
            (r) => !cancelled && setSupplierOptions(r.items),
            () => !cancelled && setSupplierOptions([]),
        );
        return () => {
            cancelled = true;
        };
    }, [supplierSearch]);

    const patch = (key: string, change: Partial<PoLineDraft>) => setLines((cur) => cur.map((l) => (l.key === key ? { ...l, ...change, error: null } : l)));

    async function addItem(item: SellableItem) {
        const line: PoLineDraft = {
            key: nextKey(),
            productId: item.productId,
            variantId: item.variantId,
            code: item.code,
            name: item.name,
            baseUnit: item.baseUnit,
            units: [item.baseUnit, ...item.units.map((u) => u.unit).filter((u) => u !== item.baseUnit)],
            unit: item.baseUnit,
            quantity: "1",
            unitPrice: "",
            discountPercent: "",
            vatRate: item.vatRate,
            expectedDate: "",
            salesOrderLineId: null,
            lastPurchase: null,
            error: null,
        };
        setLines((cur) => [...cur, line]);
        try {
            const stats = await purchasing.priceStats(item.productId, item.variantId);
            const latest = [...stats].sort((a, b) => b.lastPurchaseDate.localeCompare(a.lastPurchaseDate))[0];
            if (latest) patch(line.key, { lastPurchase: { unitPriceTl: latest.lastUnitPriceTl, date: latest.lastPurchaseDate } });
        } catch {
            /* ipucu isteğe bağlı */
        }
    }

    function fail(message: string): null {
        setError(message);
        return null;
    }

    async function save(): Promise<PoDto | null> {
        setError(null);
        setSaved(false);
        if (!supplier) return fail("Tedarikçi seçilmelidir.");
        if (!branchId) return fail("Şube seçilmelidir.");
        if (lines.length === 0) return fail("En az bir satır eklenmelidir.");
        const found = lines.map(validatePoLine);
        if (found.some(Boolean)) {
            setLines(lines.map((l, i) => ({ ...l, error: found[i] })));
            return fail("Satırlardaki hataları düzeltin.");
        }
        const input: PoInput = {
            supplierId: supplier.id,
            branchId,
            date,
            expectedDate: expectedDate || null,
            currency,
            notes: notes.trim() || null,
            version: poId && version !== null ? version : null,
            lines: lines.map(toLineInput),
        };
        setBusy(true);
        try {
            const dto = poId ? await purchasing.orders.update(poId, input) : await purchasing.orders.create(input);
            setPoId(dto.id);
            setVersion(dto.version);
            setSaved(true);
            if (!poId) router.replace(`${PO_BASE}/${dto.id}`);
            else onChanged();
            return dto;
        } catch (e) {
            if (!(e instanceof ApiError)) throw e;
            setError(Object.keys(e.fieldErrors).length === 0 ? errorMessage(e) : e);
            return null;
        } finally {
            setBusy(false);
        }
    }

    async function saveAndApprove() {
        const dto = await save();
        if (!dto) return;
        setBusy(true);
        try {
            await purchasing.orders.approve(dto.id);
            router.replace(`${PO_BASE}/${dto.id}`);
            onChanged();
        } catch (e) {
            if (!(e instanceof ApiError)) throw e;
            setError(errorMessage(e));
        } finally {
            setBusy(false);
        }
    }

    return (
        <>
            <PageHeader title={initial ? `Taslak sipariş ${initial.number}` : "Yeni sipariş"} subtitle="Taslak olarak kaydedilir; onaylanınca tedarikçiye gönderilebilir" />
            <Stack spacing={3}>
                <FormAlert error={error} />
                {saved && <Alert severity="success">Taslak kaydedildi.</Alert>}
                {initial && <OrderActions po={initial} onChanged={onChanged} onError={setError} />}

                <DashboardCard title="Başlık">
                    <Stack spacing={2}>
                        <Stack direction={{ xs: "column", md: "row" }} spacing={2}>
                            <Autocomplete
                                size="small"
                                sx={{ minWidth: 320, flexGrow: 1 }}
                                options={supplierOptions}
                                filterOptions={(x) => x}
                                getOptionLabel={(o) => `${o.code} · ${o.name}`}
                                isOptionEqualToValue={(a, b) => a.id === b.id}
                                value={supplier ? ({ id: supplier.id, code: "", name: supplier.name } as CurrentAccountListItem) : null}
                                inputValue={supplierInput}
                                onInputChange={(_, t) => setSupplierInput(t)}
                                onChange={(_, item) => {
                                    setSupplier(item ? { id: item.id, name: item.name } : null);
                                    if (item && item.currency) setCurrency(item.currency);
                                }}
                                noOptionsText="Tedarikçi bulunamadı."
                                renderInput={(params) => <TextField {...params} label="Tedarikçi" />}
                            />
                            <TextField select size="small" label="Şube" value={branchId} onChange={(e) => setBranchId(e.target.value)} slotProps={{ select: { native: true } }} sx={{ minWidth: 180 }}>
                                {branches.map((b) => (
                                    <option key={b.id} value={b.id}>
                                        {b.name}
                                    </option>
                                ))}
                            </TextField>
                        </Stack>
                        <Stack direction={{ xs: "column", md: "row" }} spacing={2}>
                            <TextField size="small" type="date" label="Sipariş tarihi" value={date} onChange={(e) => setDate(e.target.value)} slotProps={{ inputLabel: { shrink: true } }} />
                            <TextField size="small" type="date" label="Beklenen teslim" value={expectedDate} onChange={(e) => setExpectedDate(e.target.value)} slotProps={{ inputLabel: { shrink: true } }} />
                            <TextField select size="small" label="Para birimi" value={currency} onChange={(e) => setCurrency(e.target.value)} slotProps={nativeSelect("Para birimi")} sx={{ width: 130 }}>
                                {CURRENCIES.map((c) => (
                                    <option key={c} value={c}>
                                        {c}
                                    </option>
                                ))}
                            </TextField>
                        </Stack>
                        <TextField size="small" label="Not" value={notes} onChange={(e) => setNotes(e.target.value)} multiline minRows={2} slotProps={{ htmlInput: { maxLength: 1000 } }} fullWidth />
                    </Stack>
                </DashboardCard>

                <DashboardCard title="Satırlar">
                    <ItemPicker label="Kalem ekle" onPick={(item) => void addItem(item)} clearOnPick />
                    {lines.length === 0 ? (
                        <Typography color="text.secondary" sx={{ py: 2 }}>
                            Henüz satır yok. Kalem arayıp ekleyin.
                        </Typography>
                    ) : (
                        <TableContainer sx={{ mt: 2 }}>
                            <Table size="small" sx={{ minWidth: 900 }}>
                                <TableHead>
                                    <TableRow>
                                        <TableCell>Kalem</TableCell>
                                        <TableCell sx={{ width: 100 }}>Miktar</TableCell>
                                        <TableCell sx={{ width: 110 }}>Birim</TableCell>
                                        <TableCell sx={{ width: 120 }}>Birim fiyat</TableCell>
                                        <TableCell sx={{ width: 90 }}>İskonto %</TableCell>
                                        <TableCell sx={{ width: 90 }}>KDV %</TableCell>
                                        <TableCell sx={{ width: 150 }}>Beklenen</TableCell>
                                        <TableCell />
                                    </TableRow>
                                </TableHead>
                                <TableBody>
                                    {lines.map((l) => {
                                        const hint = lastPurchaseHint(l.lastPurchase);
                                        return (
                                            <TableRow key={l.key} sx={l.error ? { bgcolor: "error.light", "& td": { color: "error.contrastText" } } : undefined}>
                                                <TableCell>
                                                    <Typography variant="body2">
                                                        {l.code} · {l.name}
                                                    </Typography>
                                                    {hint && (
                                                        <Typography variant="caption" color="text.secondary" sx={{ display: "block" }}>
                                                            {hint}
                                                        </Typography>
                                                    )}
                                                    {l.error && (
                                                        <Typography variant="caption" role="alert">
                                                            {l.error}
                                                        </Typography>
                                                    )}
                                                </TableCell>
                                                <TableCell>
                                                    <TextField size="small" value={l.quantity} onChange={(e) => patch(l.key, { quantity: e.target.value })} slotProps={{ htmlInput: { "aria-label": `${l.code} miktar`, inputMode: "decimal" } }} />
                                                </TableCell>
                                                <TableCell>
                                                    <TextField select size="small" value={l.unit} onChange={(e) => patch(l.key, { unit: e.target.value })} slotProps={nativeSelect(`${l.code} birim`)} fullWidth>
                                                        {l.units.map((u) => (
                                                            <option key={u} value={u}>
                                                                {u}
                                                            </option>
                                                        ))}
                                                    </TextField>
                                                </TableCell>
                                                <TableCell>
                                                    <TextField size="small" value={l.unitPrice} placeholder="Liste" onChange={(e) => patch(l.key, { unitPrice: e.target.value })} slotProps={{ htmlInput: { "aria-label": `${l.code} birim fiyat`, inputMode: "decimal" } }} />
                                                </TableCell>
                                                <TableCell>
                                                    <TextField size="small" value={l.discountPercent} onChange={(e) => patch(l.key, { discountPercent: e.target.value })} slotProps={{ htmlInput: { "aria-label": `${l.code} iskonto`, inputMode: "decimal" } }} />
                                                </TableCell>
                                                <TableCell>
                                                    <TextField select size="small" value={l.vatRate} onChange={(e) => patch(l.key, { vatRate: Number(e.target.value) })} slotProps={nativeSelect(`${l.code} KDV`)} fullWidth>
                                                        {VAT_RATES.map((r) => (
                                                            <option key={r} value={r}>
                                                                {r}
                                                            </option>
                                                        ))}
                                                    </TextField>
                                                </TableCell>
                                                <TableCell>
                                                    <TextField size="small" type="date" value={l.expectedDate} onChange={(e) => patch(l.key, { expectedDate: e.target.value })} slotProps={{ htmlInput: { "aria-label": `${l.code} beklenen tarih` } }} />
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

                <Stack direction="row" spacing={1} sx={{ flexWrap: "wrap", gap: 1 }}>
                    <Button variant="contained" disabled={busy} onClick={() => void save()}>
                        {busy ? "Kaydediliyor..." : "Taslağı kaydet"}
                    </Button>
                    <Button variant="contained" color="success" disabled={busy} onClick={() => void saveAndApprove()}>
                        Kaydet ve onayla
                    </Button>
                </Stack>
            </Stack>
        </>
    );
}
