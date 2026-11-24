"use client";

import { useEffect, useMemo, useState } from "react";
import { useRouter } from "next/navigation";
import Alert from "@mui/material/Alert";
import Autocomplete from "@mui/material/Autocomplete";
import Box from "@mui/material/Box";
import Button from "@mui/material/Button";
import CircularProgress from "@mui/material/CircularProgress";
import FormControlLabel from "@mui/material/FormControlLabel";
import Stack from "@mui/material/Stack";
import Switch from "@mui/material/Switch";
import TextField from "@mui/material/TextField";
import FormAlert from "@/components/auth/FormAlert";
import PageHeader from "@/components/common/PageHeader";
import DashboardCard from "@/components/dashboard/DashboardCard";
import { useDebounced } from "@/components/pricing/useDebounced";
import { catalog } from "@/lib/api/catalog";
import { ApiError } from "@/lib/api/client";
import { errorMessage } from "@/lib/api/errorMessages";
import { erp, type CurrentAccountDto, type CurrentAccountListItem } from "@/lib/api/erp";
import { identity, type BranchDto } from "@/lib/api/identity";
import { inventory, type WarehouseDto } from "@/lib/api/inventory";
import {
    invoicing,
    KIND_LABELS,
    type EBelgeSettings,
    type InvoiceCodes,
    type InvoiceDto,
    type InvoiceInput,
    type InvoiceKind,
    type PreviewDto,
    type SeriesDto,
} from "@/lib/api/invoicing";
import { addDaysIso, todayIso } from "@/lib/date";
import { CURRENCIES, parseAmount } from "@/lib/money";
import { useSession } from "@/session/SessionProvider";
import CurrencyRateField, { type RateState } from "./CurrencyRateField";
import DueScheduleEditor from "./DueScheduleEditor";
import { invoiceBase, type InvoiceGroup } from "./InvoiceList";
import InvoiceLines from "./InvoiceLines";
import InvoiceView from "./InvoiceView";
import IssueDialog from "./IssueDialog";
import PdfPreview from "./PdfPreview";
import TotalsPanel from "./TotalsPanel";
import { dueScheduleError, lineFromDto, toDueInput, toLineInput, validateInvoiceLine, type DueRow, type InvoiceLineDraft } from "./invoiceModel";

type EditorProps = { group: InvoiceGroup; id?: string };

/** Fatura sayfası: yeni taslak, taslak düzenleme ya da (taslak değilse) salt okunur görünüm. */
export default function InvoiceEditor({ group, id }: EditorProps) {
    const { hasPermission } = useSession();
    const [invoice, setInvoice] = useState<InvoiceDto | null>(null);
    const [error, setError] = useState<ApiError | null>(null);
    const [cari, setCari] = useState<CurrentAccountDto | null>(null);
    const [version, setVersion] = useState(0);

    const purchase = group === "purchase";
    const allowed = hasPermission("fatura.read") && (!purchase || hasPermission("fiyat.alis"));

    useEffect(() => {
        if (!id || !allowed) return;
        let cancelled = false;
        invoicing.get(id).then(
            async (dto) => {
                const account = await erp.getAccount(dto.currentAccountId).catch(() => null);
                if (cancelled) return;
                setInvoice(dto);
                setCari(account);
            },
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

    if (!allowed) return <Alert severity="warning">Faturayı görüntülemek için yetkiniz yok.</Alert>;
    if (error) return <FormAlert error={error} />;
    if (id && !invoice) {
        return (
            <Box sx={{ display: "flex", justifyContent: "center", py: 6 }}>
                <CircularProgress />
            </Box>
        );
    }

    const editable = hasPermission("fatura.manage") && (!invoice || invoice.status === "draft");
    if (invoice && !editable) {
        return <InvoiceView invoice={invoice} emailDefault={cari?.email ?? ""} onChanged={() => setVersion((v) => v + 1)} />;
    }
    return <InvoiceForm key={invoice?.id ?? "new"} group={group} initial={invoice} initialCari={cari} onIssued={() => setVersion((v) => v + 1)} />;
}

const toRateState = (inv: InvoiceDto | null): RateState => (inv && inv.currency !== "TRY" ? { value: String(inv.rate).replace(".", ","), overridden: inv.rateOverridden } : { value: "", overridden: false });

function InvoiceForm({ group, initial, initialCari, onIssued }: { group: InvoiceGroup; initial: InvoiceDto | null; initialCari: CurrentAccountDto | null; onIssued(): void }) {
    const router = useRouter();
    const { hasPermission, canAccessBranch } = useSession();
    const purchase = group === "purchase";
    const base = invoiceBase(group);

    const [kind] = useState<InvoiceKind>(initial?.kind ?? (purchase ? "purchase" : "sales"));
    const [invoiceId, setInvoiceId] = useState<string | null>(initial?.id ?? null);
    const [draftVersion, setDraftVersion] = useState<number | null>(initial?.version ?? null);
    const [cari, setCari] = useState<CurrentAccountDto | null>(initialCari);
    const [branchId, setBranchId] = useState(initial?.branchId ?? "");
    const [date, setDate] = useState(initial?.date ?? todayIso());
    const [currency, setCurrency] = useState(initial?.currency ?? "TRY");
    const [rate, setRate] = useState<RateState>(toRateState(initial));
    const [externalNumber, setExternalNumber] = useState(initial?.externalNumber ?? "");
    const [termDays, setTermDays] = useState(initial ? String(initial.paymentTermDays) : "");
    const [discountMode, setDiscountMode] = useState<"percent" | "amount">(initial?.docDiscountAmount != null ? "amount" : "percent");
    const [discountValue, setDiscountValue] = useState(String(initial?.docDiscountAmount ?? initial?.docDiscountPercent ?? "").replace(".", ","));
    const [notes, setNotes] = useState(initial?.notes ?? "");
    const [affectsStock, setAffectsStock] = useState(initial?.affectsStock ?? false);
    const [warehouseChoice, setWarehouseId] = useState(initial?.warehouseId ?? "");
    const [lines, setLines] = useState<InvoiceLineDraft[]>([]);
    const [schedule, setSchedule] = useState<DueRow[] | null>(
        initial && initial.dueSchedule.length > 1
            ? initial.dueSchedule.map((d, i) => ({ key: `init-${i}`, dueDate: d.dueDate, amount: d.amount.toFixed(2).replace(".", ",") }))
            : null,
    );
    const [preview, setPreview] = useState<PreviewDto | null>(null);

    const [branches, setBranches] = useState<BranchDto[]>([]);
    const [warehouses, setWarehouses] = useState<WarehouseDto[]>([]);
    const [codes, setCodes] = useState<InvoiceCodes | null>(null);
    const [error, setError] = useState<ApiError | string | null>(null);
    const [saved, setSaved] = useState(false);
    const [busy, setBusy] = useState(false);
    const [issueOpen, setIssueOpen] = useState(false);
    const [issueData, setIssueData] = useState<{ ebelge: EBelgeSettings | null; series: SeriesDto[] } | null>(null);
    const [showPdf, setShowPdf] = useState(false);

    const canIssue = hasPermission("fatura.issue");
    const canStock = hasPermission("stok.read");

    // Referans veriler.
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
        invoicing.codes().then((c) => !cancelled && setCodes(c), () => undefined);
        if (canStock) inventory.warehouses().then((w) => !cancelled && setWarehouses(w.filter((x) => x.isActive)), () => undefined);
        return () => {
            cancelled = true;
        };
    }, [canAccessBranch, canStock]);

    // Kayıtlı taslağın satırları: kalem bilgisi (birimler, ölçü, takip) kataloğdan tamamlanır.
    useEffect(() => {
        if (!initial) return;
        let cancelled = false;
        const ids = [...new Set(initial.lines.map((l) => l.productId).filter((p): p is string => !!p))];
        Promise.all(ids.map((pid) => catalog.getProduct(pid).then((p) => [pid, p] as const, () => null))).then((entries) => {
            if (cancelled) return;
            const products = new Map(entries.filter((e): e is NonNullable<typeof e> => e !== null));
            setLines(
                initial.lines.map((l) => {
                    const p = l.productId ? products.get(l.productId) : undefined;
                    return lineFromDto(l, p ? { code: p.code, name: p.name, baseUnit: p.baseUnit, soldByMeasure: p.soldByMeasure, measureUnit: p.measureUnit, tracksSerialLot: p.tracksSerialLot, units: p.units.map((u) => ({ unit: u.unit, factor: u.factor })) } : undefined);
                }),
            );
        });
        return () => {
            cancelled = true;
        };
    }, [initial]);

    // Cari seçimi.
    const [cariInput, setCariInput] = useState("");
    const cariSearch = useDebounced(cariInput, 250);
    const [cariOptions, setCariOptions] = useState<CurrentAccountListItem[]>([]);
    useEffect(() => {
        let cancelled = false;
        erp.listAccounts({ search: cariSearch || undefined, role: purchase ? "supplier" : "customer", pageSize: 20 }).then(
            (r) => !cancelled && setCariOptions(r.items),
            () => !cancelled && setCariOptions([]),
        );
        return () => {
            cancelled = true;
        };
    }, [cariSearch, purchase]);

    async function pickCari(item: CurrentAccountListItem | null) {
        if (!item) {
            setCari(null);
            return;
        }
        try {
            const full = await erp.getAccount(item.id);
            setCari(full);
            setCurrency(full.currency);
            setTermDays(String(full.defaultDueDays));
            setRate({ value: "", overridden: false });
        } catch (e) {
            if (!(e instanceof ApiError)) throw e;
            setError(e);
        }
    }

    // Seçim yoksa şubenin varsayılan deposu.
    const branchDefault = warehouses.filter((w) => w.branchId === branchId);
    const warehouseId = warehouseChoice || (branchDefault.find((w) => w.isDefault) ?? branchDefault[0])?.id || "";

    const lineErrors = lines.map(validateInvoiceLine);
    const rateNumber = parseAmount(rate.value, { maxDecimals: 4 });

    /** Önizleme/kayıt gövdesi; form eksik ya da geçersizse null (önizleme çağrılmaz). */
    const buildInput = (withSchedule: boolean): InvoiceInput | null => {
        if (!branchId || !cari || !date || lines.length === 0 || lineErrors.some(Boolean)) return null;
        const lineInputs = lines.map((l) => toLineInput(l, affectsStock));
        if (lineInputs.some((l) => l === null)) return null;
        const discount = discountValue.trim() === "" ? null : parseAmount(discountValue, { maxDecimals: 2 });
        if (discountValue.trim() !== "" && discount === null) return null;
        if (currency !== "TRY" && rate.overridden && rateNumber === null) return null;
        const term = termDays.trim() === "" ? null : Math.floor(parseAmount(termDays) ?? NaN);
        return {
            kind,
            branchId,
            currentAccountId: cari.id,
            date,
            currency,
            rate: currency !== "TRY" && rate.overridden ? rateNumber : null,
            externalNumber: purchase ? externalNumber.trim() || null : null,
            docDiscountPercent: discountMode === "percent" ? discount : null,
            docDiscountAmount: discountMode === "amount" ? discount : null,
            affectsStock,
            warehouseId: affectsStock ? warehouseId || null : null,
            paymentTermDays: term !== null && Number.isFinite(term) ? term : null,
            dueSchedule: withSchedule && schedule ? toDueInput(schedule) : null,
            notes: notes.trim() || null,
            returnOfInvoiceId: initial?.returnOfInvoiceId ?? null,
            lines: lineInputs as NonNullable<(typeof lineInputs)[number]>[],
            version: invoiceId && draftVersion !== null ? draftVersion : null,
        };
    };

    const previewInput = useMemo(
        () => buildInput(false),
        // eslint-disable-next-line react-hooks/exhaustive-deps
        [branchId, cari, date, lines, affectsStock, warehouseId, currency, rate, externalNumber, termDays, discountMode, discountValue, notes],
    );

    const payableInCari = preview?.ledgerAmount ?? 0;
    const termNumber = parseAmount(termDays) ?? cari?.defaultDueDays ?? 0;
    const singleDue = addDaysIso(date, termNumber);

    /** Taslağı kaydeder; başarıda faturayı döndürür. */
    async function save(navigate = true): Promise<InvoiceDto | null> {
        setError(null);
        setSaved(false);
        if (!branchId) return fail("Şube seçilmelidir.");
        if (!cari) return fail("Cari seçilmelidir.");
        if (lines.length === 0) return fail("En az bir satır eklenmelidir.");
        const found = lines.map(validateInvoiceLine);
        if (found.some(Boolean)) {
            setLines(lines.map((l, i) => ({ ...l, error: found[i] })));
            return fail("Satırlardaki hataları düzeltin.");
        }
        if (purchase && !externalNumber.trim()) return fail("Tedarikçi fatura numarası zorunludur.");
        if (schedule) {
            const mismatch = dueScheduleError(schedule, payableInCari);
            if (mismatch) return fail(mismatch);
        }
        const input = buildInput(true);
        if (!input) return fail("Formdaki hataları düzeltin.");

        setBusy(true);
        try {
            const dto = invoiceId ? await invoicing.update(invoiceId, input) : await invoicing.create(input);
            setInvoiceId(dto.id);
            setDraftVersion(dto.version);
            setSaved(true);
            if (!invoiceId && navigate) router.replace(`${base}/${dto.id}`);
            return dto;
        } catch (e) {
            if (!(e instanceof ApiError)) throw e;
            setError(e.fieldErrors && Object.keys(e.fieldErrors).length === 0 ? errorMessage(e) : e);
            return null;
        } finally {
            setBusy(false);
        }
    }

    function fail(message: string): null {
        setError(message);
        return null;
    }

    async function startIssue() {
        const dto = await save(false);
        if (!dto || !cari) return;
        const [ebelge, series] = await Promise.all([
            erp.settings.get<EBelgeSettings>("ebelge").then((s) => s.value, () => null),
            invoicing.series.list(branchId).catch(() => [] as SeriesDto[]),
        ]);
        setIssueData({ ebelge, series });
        setIssueOpen(true);
    }

    async function confirmIssue() {
        if (!invoiceId) return;
        setBusy(true);
        try {
            await invoicing.issue(invoiceId);
            setIssueOpen(false);
            if (initial) onIssued();
            else router.replace(`${base}/${invoiceId}`);
        } catch (e) {
            if (!(e instanceof ApiError)) throw e;
            setIssueOpen(false);
            setError(errorMessage(e));
        } finally {
            setBusy(false);
        }
    }

    async function removeDraft() {
        if (!invoiceId) return;
        setBusy(true);
        try {
            await invoicing.remove(invoiceId);
            router.push(base);
        } catch (e) {
            if (!(e instanceof ApiError)) throw e;
            setError(e);
        } finally {
            setBusy(false);
        }
    }

    const branchWarehouses = warehouses.filter((w) => w.branchId === branchId);
    const title = initial ? `Taslak fatura` : purchase ? "Yeni alış faturası" : "Yeni fatura";

    return (
        <>
            <PageHeader title={title} subtitle={`${KIND_LABELS[kind]} · taslak olarak kaydedilir, kesilince numara alır`} />
            <Stack spacing={3}>
                <FormAlert error={error} />
                {saved && <Alert severity="success">Taslak kaydedildi.</Alert>}

                <DashboardCard title="Başlık">
                    <Stack spacing={2}>
                        <Stack direction={{ xs: "column", md: "row" }} spacing={2}>
                            <Autocomplete
                                size="small"
                                sx={{ minWidth: 320, flexGrow: 1 }}
                                options={cariOptions}
                                filterOptions={(x) => x}
                                getOptionLabel={(o) => `${o.code} · ${o.name}`}
                                isOptionEqualToValue={(a, b) => a.id === b.id}
                                value={cari ? ({ id: cari.id, code: cari.code, name: cari.name } as CurrentAccountListItem) : null}
                                inputValue={cariInput}
                                onInputChange={(_, text) => setCariInput(text)}
                                onChange={(_, item) => void pickCari(item)}
                                noOptionsText="Cari bulunamadı."
                                renderInput={(params) => <TextField {...params} label={purchase ? "Tedarikçi" : "Müşteri"} />}
                            />
                            <TextField select size="small" label="Şube" value={branchId} onChange={(e) => setBranchId(e.target.value)} slotProps={{ select: { native: true } }} sx={{ minWidth: 180 }}>
                                {branches.map((b) => (
                                    <option key={b.id} value={b.id}>
                                        {b.name}
                                    </option>
                                ))}
                            </TextField>
                            <TextField size="small" type="date" label="Fatura tarihi" value={date} onChange={(e) => setDate(e.target.value)} slotProps={{ inputLabel: { shrink: true } }} />
                        </Stack>
                        <Stack direction={{ xs: "column", md: "row" }} spacing={2} sx={{ alignItems: { md: "flex-start" } }}>
                            <TextField
                                select
                                size="small"
                                label="Para birimi"
                                value={currency}
                                onChange={(e) => {
                                    setCurrency(e.target.value);
                                    setRate({ value: "", overridden: false });
                                }}
                                slotProps={{ select: { native: true } }}
                                sx={{ minWidth: 130 }}
                            >
                                {CURRENCIES.map((c) => (
                                    <option key={c} value={c}>
                                        {c}
                                    </option>
                                ))}
                            </TextField>
                            <CurrencyRateField currency={currency} date={date} value={rate} onChange={setRate} keepInitial={!!initial} />
                            <TextField size="small" label="Vade günü" value={termDays} onChange={(e) => setTermDays(e.target.value)} slotProps={{ htmlInput: { inputMode: "numeric" } }} sx={{ width: 130 }} />
                            {purchase && (
                                <TextField
                                    size="small"
                                    label="Tedarikçi fatura no"
                                    required
                                    value={externalNumber}
                                    onChange={(e) => setExternalNumber(e.target.value)}
                                    slotProps={{ htmlInput: { maxLength: 50 } }}
                                    sx={{ minWidth: 220 }}
                                />
                            )}
                        </Stack>
                        {canStock && (
                            <Stack direction="row" spacing={2} sx={{ alignItems: "center" }}>
                                <FormControlLabel control={<Switch checked={affectsStock} onChange={(e) => setAffectsStock(e.target.checked)} />} label="Stoğa işle" />
                                {affectsStock && (
                                    <TextField select size="small" label="Depo" value={warehouseId} onChange={(e) => setWarehouseId(e.target.value)} slotProps={{ select: { native: true } }} sx={{ minWidth: 200 }}>
                                        <option value="" />
                                        {branchWarehouses.map((w) => (
                                            <option key={w.id} value={w.id}>
                                                {w.name}
                                            </option>
                                        ))}
                                    </TextField>
                                )}
                            </Stack>
                        )}
                    </Stack>
                </DashboardCard>

                <DashboardCard title="Satırlar">
                    <InvoiceLines
                        lines={lines}
                        onChange={setLines}
                        codes={codes}
                        purchase={purchase}
                        currentAccountId={cari?.id ?? ""}
                        date={date}
                        currency={currency}
                        affectsStock={affectsStock}
                        warehouseId={warehouseId}
                    />
                </DashboardCard>

                <Stack direction={{ xs: "column", md: "row" }} spacing={3} sx={{ alignItems: "flex-start" }}>
                    <Box sx={{ flex: 1, width: "100%" }}>
                        <Stack spacing={3}>
                            <DashboardCard title="Belge iskontosu ve not">
                                <Stack spacing={2}>
                                    <Stack direction="row" spacing={1}>
                                        <TextField select size="small" label="İskonto türü" value={discountMode} onChange={(e) => setDiscountMode(e.target.value as "percent" | "amount")} slotProps={{ select: { native: true } }} sx={{ width: 140 }}>
                                            <option value="percent">Yüzde</option>
                                            <option value="amount">Tutar</option>
                                        </TextField>
                                        <TextField size="small" label="Belge iskontosu" value={discountValue} onChange={(e) => setDiscountValue(e.target.value)} slotProps={{ htmlInput: { inputMode: "decimal" } }} />
                                    </Stack>
                                    <TextField size="small" label="Not" value={notes} onChange={(e) => setNotes(e.target.value)} multiline minRows={2} slotProps={{ htmlInput: { maxLength: 1000 } }} fullWidth />
                                </Stack>
                            </DashboardCard>
                            <DashboardCard title="Vade planı">
                                <DueScheduleEditor total={payableInCari} rows={schedule} onChange={setSchedule} singleDueDate={singleDue} defaultFirstDate={singleDue || date} />
                            </DashboardCard>
                        </Stack>
                    </Box>
                    <Box sx={{ width: { xs: "100%", md: 380 } }}>
                        <DashboardCard>
                            <TotalsPanel input={previewInput} currency={currency} onResult={setPreview} />
                        </DashboardCard>
                    </Box>
                </Stack>

                <Stack direction="row" spacing={1} sx={{ flexWrap: "wrap", gap: 1 }}>
                    <Button variant="contained" disabled={busy} onClick={() => void save()}>
                        {busy ? "Kaydediliyor..." : "Taslağı kaydet"}
                    </Button>
                    {canIssue && (
                        <Button variant="contained" color="success" disabled={busy} onClick={() => void startIssue()}>
                            Kaydet ve kes
                        </Button>
                    )}
                    {invoiceId && (
                        <>
                            <Button variant="outlined" onClick={() => setShowPdf((v) => !v)}>
                                {showPdf ? "PDF'i gizle" : "PDF önizle"}
                            </Button>
                            <Button color="error" disabled={busy} onClick={() => void removeDraft()}>
                                Taslağı sil
                            </Button>
                        </>
                    )}
                </Stack>
                {showPdf && invoiceId && <PdfPreview title="Taslak PDF" load={() => invoicing.pdf(invoiceId)} refreshKey={draftVersion ?? 0} />}
            </Stack>

            {cari && issueData && (
                <IssueDialog
                    open={issueOpen}
                    onClose={() => setIssueOpen(false)}
                    onConfirm={() => void confirmIssue()}
                    busy={busy}
                    kindGroup={group}
                    branchId={branchId}
                    year={Number(date.slice(0, 4))}
                    taxpayer={cari.isEInvoiceTaxpayer}
                    ebelge={issueData.ebelge}
                    series={issueData.series}
                />
            )}
        </>
    );
}
