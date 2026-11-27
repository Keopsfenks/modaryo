"use client";

import { useEffect, useMemo, useState } from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import Alert from "@mui/material/Alert";
import Button from "@mui/material/Button";
import Chip from "@mui/material/Chip";
import IconButton from "@mui/material/IconButton";
import Skeleton from "@mui/material/Skeleton";
import Stack from "@mui/material/Stack";
import Switch from "@mui/material/Switch";
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
import CariPicker from "@/components/treasury/CariPicker";
import { ApiError } from "@/lib/api/client";
import type { CurrentAccountListItem } from "@/lib/api/erp";
import { errorMessage } from "@/lib/api/errorMessages";
import { identity, type BranchDto } from "@/lib/api/identity";
import {
    INTERVAL_LABELS,
    RECURRING_MODE_LABELS,
    sales,
    type RecurrenceInterval,
    type RecurringDto,
    type RecurringInput,
    type RecurringMode,
    type RecurringRun,
} from "@/lib/api/sales";
import { formatDate, todayIso } from "@/lib/date";
import { CURRENCIES, formatMoney, parseAmount, type Currency } from "@/lib/money";
import { useSession } from "@/session/SessionProvider";
import { nextDates } from "./recurringModel";

export const RECURRING_BASE = "/satis/abonelikler";

type LineDraft = {
    key: number;
    productId: string | null;
    variantId: string | null;
    description: string;
    unit: string;
    quantity: string;
    unitPrice: string;
    vatIncluded: boolean;
    discountPercent: string;
    vatRate: string;
    fixedPrice: boolean;
};

let lineKey = 0;
const text = (n: number | null | undefined) => (n === null || n === undefined ? "" : String(n).replace(".", ","));
const nativeSelect = (label: string) => ({ select: { native: true }, htmlInput: { "aria-label": label }, inputLabel: { shrink: true } });

const emptyLine = (): LineDraft => ({
    key: ++lineKey,
    productId: null,
    variantId: null,
    description: "",
    unit: "",
    quantity: "1",
    unitPrice: "",
    vatIncluded: true,
    discountPercent: "",
    vatRate: "",
    fixedPrice: true,
});

function fromDto(d: RecurringDto): LineDraft[] {
    return d.lines.map((l) => ({
        key: ++lineKey,
        productId: l.productId ?? null,
        variantId: l.variantId ?? null,
        description: l.description,
        unit: l.unit,
        quantity: text(l.quantity),
        unitPrice: text(l.unitPrice),
        vatIncluded: l.vatIncluded,
        discountPercent: text(l.discountPercent),
        vatRate: text(l.vatRate),
        fixedPrice: l.fixedPrice,
    }));
}

/** Tekrarlayan fatura şablonu: cari, dönem kuralı (sonraki tarihler önizlenir), kalemler; kayıtlı şablonda duraklat / sürdür ve üretilen faturalar. */
export default function RecurringEditor({ id }: { id?: string }) {
    const router = useRouter();
    const { hasPermission } = useSession();
    const allowed = hasPermission("satis.recurring");
    const [loaded, setLoaded] = useState(!id);
    const [dto, setDto] = useState<RecurringDto | null>(null);
    const [runs, setRuns] = useState<RecurringRun[]>([]);
    const [branches, setBranches] = useState<BranchDto[]>([]);
    const [customer, setCustomer] = useState<CurrentAccountListItem | null>(null);
    const [name, setName] = useState("");
    const [branchId, setBranchId] = useState("");
    const [currency, setCurrency] = useState("TRY");
    const [interval, setInterval] = useState<RecurrenceInterval>("monthly");
    const [dayOfMonth, setDayOfMonth] = useState("1");
    const [startDate, setStartDate] = useState(todayIso());
    const [endDate, setEndDate] = useState("");
    const [mode, setMode] = useState<RecurringMode>("draft");
    const [notes, setNotes] = useState("");
    const [lines, setLines] = useState<LineDraft[]>([]);
    const [error, setError] = useState<ApiError | string | null>(null);
    const [busy, setBusy] = useState(false);

    useEffect(() => {
        if (!allowed) return;
        let cancelled = false;
        identity.branches.list().then(
            (list) => {
                if (cancelled) return;
                const active = list.filter((b) => b.isActive);
                setBranches(active);
                setBranchId((cur) => cur || (active.find((b) => b.isDefault) ?? active[0])?.id || "");
            },
            () => undefined,
        );
        if (id) {
            Promise.all([sales.recurring.get(id), sales.recurring.runs(id)]).then(
                ([d, r]) => {
                    if (cancelled) return;
                    setDto(d);
                    setRuns(r);
                    setCustomer({ id: d.currentAccountId, code: "", name: d.currentAccountName } as CurrentAccountListItem);
                    setName(d.name);
                    setBranchId(d.branchId);
                    setCurrency(d.currency);
                    setInterval(d.interval);
                    setDayOfMonth(String(d.dayOfMonth));
                    setStartDate(d.startDate);
                    setEndDate(d.endDate ?? "");
                    setMode(d.mode);
                    setNotes(d.notes ?? "");
                    setLines(fromDto(d));
                    setLoaded(true);
                },
                (e: unknown) => {
                    if (cancelled) return;
                    if (!(e instanceof ApiError)) throw e;
                    setError(e);
                },
            );
        }
        return () => {
            cancelled = true;
        };
    }, [allowed, id]);

    const day = Number(dayOfMonth);
    // Önizleme: üretilmemiş sonraki tarihler (kayıtlıysa sunucunun hesapladığı sonraki dönemden başlar).
    const preview = useMemo(
        () => nextDates(interval, Number.isInteger(day) ? day : 0, startDate, endDate || null, 6, dto?.nextPeriod ?? (startDate > todayIso() ? startDate : todayIso())),
        [interval, day, startDate, endDate, dto?.nextPeriod],
    );

    if (!allowed) return <Alert severity="warning">Abonelikleri yönetmek için yetkiniz yok.</Alert>;
    if (!loaded) return error ? <FormAlert error={error} /> : <Skeleton variant="rounded" height={240} />;

    const patchLine = (key: number, change: Partial<LineDraft>) => setLines((cur) => cur.map((l) => (l.key === key ? { ...l, ...change } : l)));

    function buildBody(): RecurringInput | string {
        if (!customer) return "Müşteri seçilmelidir.";
        if (!branchId) return "Şube seçilmelidir.";
        if (!Number.isInteger(day) || day < 1 || day > 28) return "Ayın günü 1 ile 28 arasında olmalıdır.";
        if (!startDate) return "Başlangıç tarihi zorunludur.";
        if (endDate && endDate < startDate) return "Bitiş tarihi başlangıçtan önce olamaz.";
        if (lines.length === 0) return "En az bir kalem ekleyin.";
        const out: RecurringInput["lines"] = [];
        for (const [i, l] of lines.entries()) {
            const label = `${i + 1}. satır`;
            const quantity = parseAmount(l.quantity, { maxDecimals: 4 });
            const unitPrice = parseAmount(l.unitPrice, { maxDecimals: 4 });
            const discount = l.discountPercent.trim() ? parseAmount(l.discountPercent) : null;
            if (quantity === null || quantity <= 0) return `${label}: miktar geçersiz.`;
            if (unitPrice === null || unitPrice < 0) return `${label}: birim fiyat geçersiz.`;
            if (discount !== null && (discount < 0 || discount > 100)) return `${label}: iskonto 0 ile 100 arasında olmalıdır.`;
            if (!l.productId && !l.description.trim()) return `${label}: açıklama girin.`;
            out.push({
                productId: l.productId,
                variantId: l.variantId,
                description: l.description.trim() || null,
                unit: l.unit.trim() || null,
                quantity,
                unitPrice,
                vatIncluded: l.vatIncluded,
                discountPercent: discount,
                vatRate: l.vatRate.trim() ? Number(l.vatRate) : null,
                fixedPrice: l.fixedPrice,
            });
        }
        return { name: name.trim() || null, currentAccountId: customer.id, branchId, currency, interval, dayOfMonth: day, startDate, endDate: endDate || null, mode, notes: notes.trim() || null, lines: out };
    }

    async function save() {
        const body = buildBody();
        if (typeof body === "string") return setError(body);
        setBusy(true);
        setError(null);
        try {
            if (id) {
                const d = await sales.recurring.update(id, body);
                setDto(d);
                setLines(fromDto(d));
            } else {
                const d = await sales.recurring.create(body);
                router.push(`${RECURRING_BASE}/${d.id}`);
            }
        } catch (e) {
            if (!(e instanceof ApiError)) throw e;
            setError(errorMessage(e));
        } finally {
            setBusy(false);
        }
    }

    async function toggle() {
        if (!dto) return;
        setBusy(true);
        setError(null);
        try {
            setDto(dto.isActive ? await sales.recurring.pause(dto.id) : await sales.recurring.resume(dto.id));
        } catch (e) {
            if (!(e instanceof ApiError)) throw e;
            setError(errorMessage(e));
        } finally {
            setBusy(false);
        }
    }

    return (
        <>
            <PageHeader title={dto ? `Abonelik ${dto.number}` : "Yeni abonelik"} subtitle="Dönemsel olarak otomatik oluşan satış faturası şablonu" />
            <Stack spacing={3}>
                <FormAlert error={error} />
                {dto && (
                    <Stack direction="row" spacing={1} sx={{ alignItems: "center", flexWrap: "wrap", rowGap: 1 }}>
                        <Chip color={dto.isActive ? "success" : "default"} label={dto.isActive ? "Aktif" : "Duraklatıldı"} />
                        {dto.nextPeriod && <Chip variant="outlined" label={`Sonraki dönem: ${formatDate(dto.nextPeriod)}`} />}
                        {dto.lastPeriod && <Chip variant="outlined" label={`Son üretilen: ${formatDate(dto.lastPeriod)}`} />}
                        <Chip variant="outlined" label={`Dönem tutarı: ${formatMoney(dto.total, dto.currency as Currency)}`} />
                        <Button variant="outlined" size="small" disabled={busy} onClick={() => void toggle()}>
                            {dto.isActive ? "Duraklat" : "Sürdür"}
                        </Button>
                    </Stack>
                )}
                <DashboardCard title="Şablon">
                    <Stack spacing={2}>
                        <Stack direction={{ xs: "column", md: "row" }} spacing={2}>
                            <CariPicker label="Müşteri" role="customer" value={customer} onChange={setCustomer} disabled={Boolean(dto)} />
                            <TextField size="small" label="Ad" value={name} onChange={(e) => setName(e.target.value)} sx={{ minWidth: 220 }} />
                            <TextField select size="small" label="Şube" value={branchId} onChange={(e) => setBranchId(e.target.value)} slotProps={nativeSelect("Şube")} sx={{ minWidth: 180 }}>
                                {branches.map((b) => (
                                    <option key={b.id} value={b.id}>
                                        {b.name}
                                    </option>
                                ))}
                            </TextField>
                            <TextField select size="small" label="Para birimi" value={currency} onChange={(e) => setCurrency(e.target.value)} slotProps={nativeSelect("Para birimi")} sx={{ width: 130 }}>
                                {CURRENCIES.map((c) => (
                                    <option key={c} value={c}>
                                        {c}
                                    </option>
                                ))}
                            </TextField>
                        </Stack>
                        <Stack direction={{ xs: "column", md: "row" }} spacing={2}>
                            <TextField select size="small" label="Dönem" value={interval} onChange={(e) => setInterval(e.target.value as RecurrenceInterval)} slotProps={nativeSelect("Dönem")} sx={{ minWidth: 140 }}>
                                {(Object.keys(INTERVAL_LABELS) as RecurrenceInterval[]).map((k) => (
                                    <option key={k} value={k}>
                                        {INTERVAL_LABELS[k]}
                                    </option>
                                ))}
                            </TextField>
                            <TextField size="small" label="Ayın günü" value={dayOfMonth} onChange={(e) => setDayOfMonth(e.target.value)} helperText="1 ile 28 arası" slotProps={{ htmlInput: { inputMode: "numeric" } }} sx={{ width: 120 }} />
                            <TextField size="small" type="date" label="Başlangıç" value={startDate} onChange={(e) => setStartDate(e.target.value)} slotProps={{ inputLabel: { shrink: true } }} />
                            <TextField size="small" type="date" label="Bitiş" value={endDate} onChange={(e) => setEndDate(e.target.value)} slotProps={{ inputLabel: { shrink: true } }} />
                            <TextField select size="small" label="Üretim şekli" value={mode} onChange={(e) => setMode(e.target.value as RecurringMode)} slotProps={nativeSelect("Üretim şekli")} sx={{ minWidth: 170 }}>
                                {(Object.keys(RECURRING_MODE_LABELS) as RecurringMode[]).map((k) => (
                                    <option key={k} value={k}>
                                        {RECURRING_MODE_LABELS[k]}
                                    </option>
                                ))}
                            </TextField>
                        </Stack>
                        <div>
                            <Typography variant="subtitle2" sx={{ mb: 0.5 }}>
                                Sonraki üretim tarihleri
                            </Typography>
                            {preview.length === 0 ? (
                                <Typography variant="body2" color="text.secondary">
                                    Bu kurala göre üretilecek tarih yok.
                                </Typography>
                            ) : (
                                <Stack direction="row" spacing={1} sx={{ flexWrap: "wrap", rowGap: 1 }} aria-label="Sonraki üretim tarihleri">
                                    {preview.map((d) => (
                                        <Chip key={d} size="small" variant="outlined" label={formatDate(d)} />
                                    ))}
                                </Stack>
                            )}
                        </div>
                        <TextField size="small" label="Not" value={notes} onChange={(e) => setNotes(e.target.value)} multiline minRows={2} fullWidth slotProps={{ htmlInput: { maxLength: 1000 } }} />
                    </Stack>
                </DashboardCard>

                <DashboardCard title="Kalemler">
                    <Stack spacing={2}>
                        <ItemPicker
                            label="Kalem ekle"
                            clearOnPick
                            onPick={(item) =>
                                setLines((cur) => [
                                    ...cur,
                                    {
                                        ...emptyLine(),
                                        productId: item.productId,
                                        variantId: item.variantId,
                                        description: item.name,
                                        unit: item.baseUnit,
                                        unitPrice: text(item.salePrice?.amount ?? null),
                                        vatIncluded: item.salePrice?.vatIncluded ?? true,
                                        vatRate: String(item.vatRate),
                                    },
                                ])
                            }
                        />
                        <Button size="small" sx={{ alignSelf: "flex-start" }} onClick={() => setLines((cur) => [...cur, emptyLine()])}>
                            Serbest satır ekle
                        </Button>
                        {lines.length > 0 && (
                            <TableContainer>
                                <Table size="small" sx={{ minWidth: 900 }}>
                                    <TableHead>
                                        <TableRow>
                                            <TableCell>Açıklama</TableCell>
                                            <TableCell sx={{ width: 90 }}>Miktar</TableCell>
                                            <TableCell sx={{ width: 120 }}>Birim fiyat</TableCell>
                                            <TableCell sx={{ width: 90 }}>İskonto %</TableCell>
                                            <TableCell sx={{ width: 80 }}>KDV %</TableCell>
                                            <TableCell>KDV dahil</TableCell>
                                            <TableCell>Sabit fiyat</TableCell>
                                            <TableCell />
                                        </TableRow>
                                    </TableHead>
                                    <TableBody>
                                        {lines.map((l, i) => (
                                            <TableRow key={l.key}>
                                                <TableCell>
                                                    <TextField size="small" fullWidth value={l.description} onChange={(e) => patchLine(l.key, { description: e.target.value })} slotProps={{ htmlInput: { "aria-label": `${i + 1}. satır açıklama` } }} />
                                                </TableCell>
                                                <TableCell>
                                                    <TextField size="small" value={l.quantity} onChange={(e) => patchLine(l.key, { quantity: e.target.value })} slotProps={{ htmlInput: { "aria-label": `${i + 1}. satır miktar`, inputMode: "decimal" } }} />
                                                </TableCell>
                                                <TableCell>
                                                    <TextField size="small" value={l.unitPrice} onChange={(e) => patchLine(l.key, { unitPrice: e.target.value })} slotProps={{ htmlInput: { "aria-label": `${i + 1}. satır birim fiyat`, inputMode: "decimal" } }} />
                                                </TableCell>
                                                <TableCell>
                                                    <TextField size="small" value={l.discountPercent} onChange={(e) => patchLine(l.key, { discountPercent: e.target.value })} slotProps={{ htmlInput: { "aria-label": `${i + 1}. satır iskonto`, inputMode: "decimal" } }} />
                                                </TableCell>
                                                <TableCell>
                                                    <TextField size="small" value={l.vatRate} onChange={(e) => patchLine(l.key, { vatRate: e.target.value })} slotProps={{ htmlInput: { "aria-label": `${i + 1}. satır KDV`, inputMode: "numeric" } }} />
                                                </TableCell>
                                                <TableCell>
                                                    <Switch checked={l.vatIncluded} onChange={(e) => patchLine(l.key, { vatIncluded: e.target.checked })} slotProps={{ input: { "aria-label": `${i + 1}. satır KDV dahil` } }} />
                                                </TableCell>
                                                <TableCell>
                                                    <Switch
                                                        checked={l.fixedPrice || !l.productId}
                                                        disabled={!l.productId}
                                                        onChange={(e) => patchLine(l.key, { fixedPrice: e.target.checked })}
                                                        slotProps={{ input: { "aria-label": `${i + 1}. satır sabit fiyat` } }}
                                                    />
                                                </TableCell>
                                                <TableCell>
                                                    <IconButton size="small" aria-label={`${i + 1}. satırı sil`} onClick={() => setLines((cur) => cur.filter((x) => x.key !== l.key))}>
                                                        <IconTrash size={18} />
                                                    </IconButton>
                                                </TableCell>
                                            </TableRow>
                                        ))}
                                    </TableBody>
                                </Table>
                            </TableContainer>
                        )}
                        <Typography variant="caption" color="text.secondary">
                            Sabit olmayan kalemli satırlar üretim gününde fiyat listesinden yeniden fiyatlanır.
                        </Typography>
                    </Stack>
                </DashboardCard>

                <Stack direction="row" spacing={1}>
                    <Button variant="contained" disabled={busy} onClick={() => void save()}>
                        {busy ? "Kaydediliyor..." : "Kaydet"}
                    </Button>
                    <Button component={Link} href={RECURRING_BASE}>
                        Vazgeç
                    </Button>
                </Stack>

                {dto && (
                    <DashboardCard title="Üretilen faturalar">
                        {runs.length === 0 ? (
                            <Typography color="text.secondary">Henüz fatura üretilmedi.</Typography>
                        ) : (
                            <Stack spacing={0.5}>
                                {runs.map((r) => (
                                    <Typography key={r.id} variant="body2">
                                        {formatDate(r.period)} ·{" "}
                                        {r.invoiceStatus === "deleted" ? (
                                            "Taslak silindi"
                                        ) : (
                                            <>
                                                <Link href={`/satis/faturalar/${r.invoiceId}`}>{r.invoiceNumber ?? "Taslak"}</Link> · {r.invoiceStatus === "issued" ? "Kesildi" : r.invoiceStatus === "cancelled" ? "İptal" : "Taslak"}
                                                {r.payable !== null && ` · ${formatMoney(r.payable, dto.currency as Currency)}`}
                                            </>
                                        )}
                                    </Typography>
                                ))}
                            </Stack>
                        )}
                    </DashboardCard>
                )}
            </Stack>
        </>
    );
}
