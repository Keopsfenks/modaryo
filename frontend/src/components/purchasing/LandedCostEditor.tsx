"use client";

import { useEffect, useState } from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import Alert from "@mui/material/Alert";
import Autocomplete from "@mui/material/Autocomplete";
import Box from "@mui/material/Box";
import Button from "@mui/material/Button";
import Chip from "@mui/material/Chip";
import Dialog from "@mui/material/Dialog";
import DialogActions from "@mui/material/DialogActions";
import DialogContent from "@mui/material/DialogContent";
import DialogContentText from "@mui/material/DialogContentText";
import DialogTitle from "@mui/material/DialogTitle";
import IconButton from "@mui/material/IconButton";
import Skeleton from "@mui/material/Skeleton";
import Stack from "@mui/material/Stack";
import Table from "@mui/material/Table";
import TableBody from "@mui/material/TableBody";
import TableCell from "@mui/material/TableCell";
import TableHead from "@mui/material/TableHead";
import TableRow from "@mui/material/TableRow";
import TextField from "@mui/material/TextField";
import Typography from "@mui/material/Typography";
import { IconTrash } from "@tabler/icons-react";
import FormAlert from "@/components/auth/FormAlert";
import PageHeader from "@/components/common/PageHeader";
import DashboardCard from "@/components/dashboard/DashboardCard";
import { useDebounced } from "@/components/pricing/useDebounced";
import { ApiError } from "@/lib/api/client";
import { errorMessage } from "@/lib/api/errorMessages";
import { invoicing, type InvoiceListItem } from "@/lib/api/invoicing";
import {
    ALLOCATION_KEY_LABELS,
    COST_TYPES,
    COST_TYPE_LABELS,
    LANDED_STATUS_LABELS,
    purchasing,
    type AllocationKey,
    type CostType,
    type ImportFileRow,
    type LandedCostDto,
    type LandedCostInput,
    type ReceiptRow,
} from "@/lib/api/purchasing";
import { formatDate, todayIso } from "@/lib/date";
import { formatMoney, parseAmount } from "@/lib/money";
import { useSession } from "@/session/SessionProvider";
import AllocationPreview from "./AllocationPreview";
import ReasonDialog from "./ReasonDialog";

const nativeSelect = (label: string) => ({ select: { native: true }, htmlInput: { "aria-label": label } });

/** weight_missing: sunucu mesajı eksik ağırlıklı kalem kodlarını sayar; sabit metin yerine o mesaj gösterilir. */
export function landedErrorText(e: ApiError): string {
    return e.code === "weight_missing" && e.message ? e.message : errorMessage(e);
}

type SourceDraft = {
    key: number;
    mode: "manual" | "invoice";
    amount: string;
    costType: CostType;
    description: string;
    invoice: InvoiceListItem | null;
};

let counter = 0;
const newSource = (): SourceDraft => ({ key: ++counter, mode: "manual", amount: "", costType: "navlun", description: "", invoice: null });

type EditorProps = { id?: string; initialImportFileId?: string | null };

/** Masraf dağıtımı: id yoksa yeni belge formu (önizleme, kaydet, işle); id varsa belge ayrıntısı (işle / iptal). */
export default function LandedCostEditor({ id, initialImportFileId = null }: EditorProps) {
    const { hasPermission } = useSession();
    if (!hasPermission("satinalma.read") || !hasPermission("fiyat.alis")) {
        return <Alert severity="warning">Masraf dağıtımı için satın alma ve alış fiyatı yetkisi gerekir.</Alert>;
    }
    return id ? <Detail id={id} /> : <NewForm initialImportFileId={initialImportFileId} />;
}

function NewForm({ initialImportFileId }: { initialImportFileId: string | null }) {
    const router = useRouter();
    const { hasPermission, hasModule } = useSession();
    const canManage = hasPermission("satinalma.manage");
    const hasImport = hasModule("ithalat");
    const [date, setDate] = useState(todayIso());
    const [key, setKey] = useState<AllocationKey>("amount");
    const [notes, setNotes] = useState("");
    const [receipts, setReceipts] = useState<ReceiptRow[]>([]);
    const [importFileId, setImportFileId] = useState(initialImportFileId ?? "");
    const [importFiles, setImportFiles] = useState<ImportFileRow[]>([]);
    const [sources, setSources] = useState<SourceDraft[]>([newSource()]);
    const [preview, setPreview] = useState<LandedCostDto | null>(null);
    const [error, setError] = useState<string | null>(null);
    const [busy, setBusy] = useState(false);
    const [confirm, setConfirm] = useState(false);

    // Girdi değişince önizleme geçersiz olur; işlemek için yeniden önizlenmelidir.
    const touch = () => {
        setPreview(null);
        setError(null);
    };

    useEffect(() => {
        if (!hasImport) return;
        let cancelled = false;
        purchasing.importFiles.list("open").then(
            (list) => !cancelled && setImportFiles(list),
            () => undefined,
        );
        return () => {
            cancelled = true;
        };
    }, [hasImport]);

    function buildInput(): LandedCostInput | string {
        if (receipts.length === 0 && !importFileId) return "En az bir mal kabul seçin ya da bir ithalat dosyası belirtin.";
        const list: LandedCostInput["sources"] = [];
        for (const s of sources) {
            if (s.mode === "invoice") {
                if (!s.invoice) return "Fatura kaynağı için bir alış faturası seçin.";
                list.push({ invoiceId: s.invoice.id, costType: s.costType, description: s.description.trim() || null });
            } else {
                const amount = parseAmount(s.amount);
                if (amount === null || amount <= 0) return "Masraf tutarı sıfırdan büyük olmalı ve en çok 2 ondalık içermelidir.";
                list.push({ manualAmountTl: amount, costType: s.costType, description: s.description.trim() || null });
            }
        }
        return { date, key, receiptIds: receipts.map((r) => r.id), importFileId: importFileId || null, notes: notes.trim() || null, sources: list };
    }

    async function runPreview() {
        const input = buildInput();
        if (typeof input === "string") return setError(input);
        setBusy(true);
        setError(null);
        setPreview(null);
        try {
            setPreview(await purchasing.landedCosts.preview(input));
        } catch (e) {
            if (!(e instanceof ApiError)) throw e;
            setError(landedErrorText(e));
        } finally {
            setBusy(false);
        }
    }

    async function save(thenPost: boolean) {
        const input = buildInput();
        if (typeof input === "string") return setError(input);
        setBusy(true);
        setError(null);
        try {
            const created = await purchasing.landedCosts.create(input);
            if (thenPost) {
                try {
                    await purchasing.landedCosts.post(created.id);
                } catch (e) {
                    // Taslak oluştu ama işlenemedi: ayrıntıya gidilir, orada yeniden denenebilir.
                    if (!(e instanceof ApiError)) throw e;
                    router.push(`/satin-alma/masraf-dagitimi/${created.id}`);
                    return;
                }
            }
            router.push(`/satin-alma/masraf-dagitimi/${created.id}`);
        } catch (e) {
            if (!(e instanceof ApiError)) throw e;
            setError(landedErrorText(e));
            setConfirm(false);
        } finally {
            setBusy(false);
        }
    }

    const patchSource = (k: number, change: Partial<SourceDraft>) => {
        touch();
        setSources((cur) => cur.map((s) => (s.key === k ? { ...s, ...change } : s)));
    };

    if (!canManage) return <Alert severity="warning">Masraf dağıtımı oluşturmak için yetkiniz yok.</Alert>;

    return (
        <>
            <PageHeader title="Yeni masraf dağıtımı" subtitle="Navlun, gümrük ve benzeri masrafları mal kabul satırlarına dağıtın" />
            <Stack spacing={3}>
                <FormAlert error={error} />
                <DashboardCard title="Hedef ve anahtar">
                    <Stack spacing={2}>
                        <Stack direction={{ xs: "column", md: "row" }} spacing={2}>
                            <TextField size="small" type="date" label="Tarih" value={date} onChange={(e) => { touch(); setDate(e.target.value); }} slotProps={{ inputLabel: { shrink: true } }} />
                            <TextField
                                select
                                size="small"
                                label="Dağıtım anahtarı"
                                value={key}
                                onChange={(e) => {
                                    touch();
                                    setKey(e.target.value as AllocationKey);
                                }}
                                slotProps={{ ...nativeSelect("Dağıtım anahtarı"), inputLabel: { shrink: true } }}
                                sx={{ minWidth: 200 }}
                            >
                                {(Object.keys(ALLOCATION_KEY_LABELS) as AllocationKey[]).map((k) => (
                                    <option key={k} value={k}>
                                        {ALLOCATION_KEY_LABELS[k]}
                                    </option>
                                ))}
                            </TextField>
                            {hasImport && (
                                <TextField
                                    select
                                    size="small"
                                    label="İthalat dosyası"
                                    value={importFileId}
                                    onChange={(e) => {
                                        touch();
                                        setImportFileId(e.target.value);
                                    }}
                                    slotProps={{ ...nativeSelect("İthalat dosyası"), inputLabel: { shrink: true } }}
                                    sx={{ minWidth: 220 }}
                                >
                                    <option value="">Yok</option>
                                    {importFiles.map((f) => (
                                        <option key={f.id} value={f.id}>
                                            {f.number} · {f.supplierName}
                                        </option>
                                    ))}
                                </TextField>
                            )}
                        </Stack>
                        <ReceiptPicker value={receipts} onChange={(next) => { touch(); setReceipts(next); }} />
                        {importFileId && <Typography variant="caption" color="text.secondary">İthalat dosyasına bağlı mal kabuller de hedefe dahil edilir.</Typography>}
                        <TextField size="small" label="Not" value={notes} onChange={(e) => setNotes(e.target.value)} slotProps={{ htmlInput: { maxLength: 500 } }} />
                    </Stack>
                </DashboardCard>

                <DashboardCard
                    title="Masraf kaynakları"
                    action={
                        <Button size="small" onClick={() => { touch(); setSources((cur) => [...cur, newSource()]); }}>
                            Kaynak ekle
                        </Button>
                    }
                >
                    <Stack spacing={2}>
                        {sources.map((s, index) => (
                            <Stack key={s.key} direction={{ xs: "column", md: "row" }} spacing={1.5} sx={{ alignItems: { md: "flex-start" } }}>
                                <TextField
                                    select
                                    size="small"
                                    value={s.mode}
                                    onChange={(e) => patchSource(s.key, { mode: e.target.value as "manual" | "invoice", invoice: null })}
                                    slotProps={nativeSelect(`Kaynak türü ${index + 1}`)}
                                    sx={{ minWidth: 150 }}
                                >
                                    <option value="manual">Elle tutar</option>
                                    <option value="invoice">Alış faturası</option>
                                </TextField>
                                {s.mode === "manual" ? (
                                    <TextField
                                        size="small"
                                        label="Tutar (TL)"
                                        value={s.amount}
                                        onChange={(e) => patchSource(s.key, { amount: e.target.value })}
                                        slotProps={{ htmlInput: { "aria-label": "Tutar (TL)", inputMode: "decimal" } }}
                                        sx={{ width: 160 }}
                                    />
                                ) : (
                                    <InvoicePicker value={s.invoice} onChange={(invoice) => patchSource(s.key, { invoice })} />
                                )}
                                <TextField
                                    select
                                    size="small"
                                    value={s.costType}
                                    onChange={(e) => patchSource(s.key, { costType: e.target.value as CostType })}
                                    slotProps={nativeSelect("Masraf türü")}
                                    sx={{ minWidth: 130 }}
                                >
                                    {COST_TYPES.map((t) => (
                                        <option key={t} value={t}>
                                            {COST_TYPE_LABELS[t]}
                                        </option>
                                    ))}
                                </TextField>
                                <TextField size="small" label="Açıklama" value={s.description} onChange={(e) => patchSource(s.key, { description: e.target.value })} sx={{ flexGrow: 1 }} slotProps={{ htmlInput: { maxLength: 200 } }} />
                                <IconButton aria-label={`Kaynağı sil ${index + 1}`} disabled={sources.length === 1} onClick={() => { touch(); setSources((cur) => cur.filter((x) => x.key !== s.key)); }}>
                                    <IconTrash size={18} />
                                </IconButton>
                            </Stack>
                        ))}
                    </Stack>
                </DashboardCard>

                {preview && (
                    <DashboardCard title="Dağıtım önizlemesi" subtitle={`Masraf toplamı ${formatMoney(preview.totalTl, "TRY")} · anahtar: ${ALLOCATION_KEY_LABELS[preview.key]}`}>
                        <AllocationPreview allocations={preview.allocations} totalTl={preview.totalTl} />
                    </DashboardCard>
                )}

                <Stack direction="row" spacing={1}>
                    <Button variant="outlined" disabled={busy} onClick={() => void runPreview()}>
                        Önizle
                    </Button>
                    <Button variant="outlined" disabled={busy || !preview} onClick={() => void save(false)}>
                        Taslak kaydet
                    </Button>
                    <Button variant="contained" disabled={busy || !preview} onClick={() => setConfirm(true)}>
                        Kaydet ve işle
                    </Button>
                </Stack>
            </Stack>

            <Dialog open={confirm} onClose={() => setConfirm(false)}>
                <DialogTitle>Masraf dağıtımı işlensin mi?</DialogTitle>
                <DialogContent>
                    <DialogContentText>
                        {preview ? formatMoney(preview.totalTl, "TRY") : ""} tutarındaki masraf {receipts.length > 0 ? `${receipts.length} mal kabul` : "ithalat dosyasının mal kabulleri"} üzerinde kalemlerin maliyetine yazılır; maliyet düzeltmesi mal kabul
                        tarihine işlenir ve sonraki çıkışlar yeniden maliyetlenir. İşlenmiş dağıtım iptal edilerek geri alınabilir.
                    </DialogContentText>
                </DialogContent>
                <DialogActions>
                    <Button onClick={() => setConfirm(false)}>Vazgeç</Button>
                    <Button variant="contained" disabled={busy} onClick={() => void save(true)}>
                        {busy ? "İşleniyor..." : "İşle"}
                    </Button>
                </DialogActions>
            </Dialog>
        </>
    );
}

function ReceiptPicker({ value, onChange }: { value: ReceiptRow[]; onChange(next: ReceiptRow[]): void }) {
    const [input, setInput] = useState("");
    const search = useDebounced(input, 250);
    const [options, setOptions] = useState<ReceiptRow[]>([]);
    useEffect(() => {
        let cancelled = false;
        purchasing.receipts.list({ status: "posted", search: search || undefined, pageSize: 20 }).then(
            (r) => !cancelled && setOptions(r.items),
            () => !cancelled && setOptions([]),
        );
        return () => {
            cancelled = true;
        };
    }, [search]);
    return (
        <Autocomplete
            multiple
            size="small"
            options={options}
            value={value}
            filterOptions={(x) => x}
            filterSelectedOptions
            getOptionLabel={(r) => `${r.number} · ${r.supplierName}`}
            isOptionEqualToValue={(a, b) => a.id === b.id}
            onInputChange={(_, v) => setInput(v)}
            onChange={(_, next) => onChange(next)}
            renderInput={(params) => <TextField {...params} label="Mal kabuller" placeholder="Mal kabul no ya da tedarikçi ara" />}
            renderOption={(props, r) => (
                <li {...props} key={r.id}>
                    {r.number} · {r.supplierName} · {formatDate(r.date)}
                </li>
            )}
        />
    );
}

function InvoicePicker({ value, onChange }: { value: InvoiceListItem | null; onChange(next: InvoiceListItem | null): void }) {
    const [input, setInput] = useState("");
    const search = useDebounced(input, 250);
    const [options, setOptions] = useState<InvoiceListItem[]>([]);
    useEffect(() => {
        let cancelled = false;
        invoicing.list({ kind: "purchase", status: "issued", search: search || undefined, pageSize: 20 }).then(
            (r) => !cancelled && setOptions(r.items),
            () => !cancelled && setOptions([]),
        );
        return () => {
            cancelled = true;
        };
    }, [search]);
    return (
        <Autocomplete
            size="small"
            options={options}
            value={value}
            filterOptions={(x) => x}
            getOptionLabel={(i) => `${i.number ?? i.externalNumber ?? "Fatura"} · ${i.currentAccountName}`}
            isOptionEqualToValue={(a, b) => a.id === b.id}
            onInputChange={(_, v) => setInput(v)}
            onChange={(_, next) => onChange(next)}
            renderInput={(params) => <TextField {...params} label="Alış faturası" placeholder="Hizmet (masraf) faturası ara" />}
            renderOption={(props, i) => (
                <li {...props} key={i.id}>
                    {i.number ?? i.externalNumber} · {i.currentAccountName} · {formatDate(i.date)} · Net {formatMoney(i.net, i.currency === "TRY" ? "TRY" : (i.currency as "USD" | "EUR" | "GBP"))}
                </li>
            )}
            sx={{ minWidth: 300, flexGrow: 1 }}
        />
    );
}

function Detail({ id }: { id: string }) {
    const { hasPermission } = useSession();
    const canManage = hasPermission("satinalma.manage");
    const [landed, setLanded] = useState<LandedCostDto | null>(null);
    const [error, setError] = useState<string | null>(null);
    const [version, setVersion] = useState(0);
    const [cancelOpen, setCancelOpen] = useState(false);
    const [busy, setBusy] = useState(false);

    useEffect(() => {
        let cancelled = false;
        purchasing.landedCosts.get(id).then(
            (dto) => !cancelled && setLanded(dto),
            (e: unknown) => {
                if (cancelled) return;
                if (!(e instanceof ApiError)) throw e;
                setError(landedErrorText(e));
            },
        );
        return () => {
            cancelled = true;
        };
    }, [id, version]);

    async function post() {
        setBusy(true);
        setError(null);
        try {
            setLanded(await purchasing.landedCosts.post(id));
        } catch (e) {
            if (!(e instanceof ApiError)) throw e;
            setError(landedErrorText(e));
        } finally {
            setBusy(false);
        }
    }

    if (!landed) return error ? <FormAlert error={error} /> : <Skeleton variant="rounded" height={240} />;

    const statusColor = landed.status === "posted" ? "success" : landed.status === "cancelled" ? "default" : "warning";
    return (
        <>
            <PageHeader title={landed.number} subtitle={`Masraf dağıtımı · ${formatDate(landed.date)} · anahtar: ${ALLOCATION_KEY_LABELS[landed.key]}`} />
            <Stack spacing={3}>
                <FormAlert error={error} />
                {landed.cancelReason && <Alert severity="info">İptal nedeni: {landed.cancelReason}</Alert>}
                <DashboardCard
                    title="Masraf kaynakları"
                    action={<Chip size="small" color={statusColor} label={LANDED_STATUS_LABELS[landed.status]} />}
                >
                    <Table size="small">
                        <TableHead>
                            <TableRow>
                                <TableCell>Açıklama</TableCell>
                                <TableCell>Tür</TableCell>
                                <TableCell align="right">Tutar</TableCell>
                            </TableRow>
                        </TableHead>
                        <TableBody>
                            {landed.sources.map((s) => (
                                <TableRow key={s.id}>
                                    <TableCell>
                                        {s.invoiceId ? (
                                            <Typography component={Link} href={`/satin-alma/faturalar/${s.invoiceId}`} sx={{ color: "primary.main" }}>
                                                {s.description}
                                            </Typography>
                                        ) : (
                                            s.description
                                        )}
                                    </TableCell>
                                    <TableCell>{COST_TYPE_LABELS[(s.costType ?? "diger") as CostType]}</TableCell>
                                    <TableCell align="right">{formatMoney(s.amountTl, "TRY")}</TableCell>
                                </TableRow>
                            ))}
                        </TableBody>
                    </Table>
                    <Box sx={{ mt: 1, textAlign: "right" }}>
                        <Typography variant="subtitle2">Toplam: {formatMoney(landed.totalTl, "TRY")}</Typography>
                    </Box>
                </DashboardCard>
                <DashboardCard title="Dağıtım">
                    <AllocationPreview allocations={landed.allocations} totalTl={landed.totalTl} />
                </DashboardCard>
                {canManage && landed.status !== "cancelled" && (
                    <Stack direction="row" spacing={1}>
                        {landed.status === "draft" && (
                            <Button variant="contained" disabled={busy} onClick={() => void post()}>
                                {busy ? "İşleniyor..." : "İşle"}
                            </Button>
                        )}
                        <Button color="error" onClick={() => setCancelOpen(true)}>
                            İptal
                        </Button>
                    </Stack>
                )}
            </Stack>
            <ReasonDialog
                open={cancelOpen}
                title="Masraf dağıtımı iptal edilsin mi?"
                text={landed.status === "posted" ? "İşlenmiş dağıtımın maliyet düzeltmeleri ters yönde yazılır." : "Taslak iptal edilir."}
                confirmLabel="İptal et"
                onClose={() => setCancelOpen(false)}
                onConfirm={async (reason) => {
                    await purchasing.landedCosts.cancel(id, reason);
                    setCancelOpen(false);
                    setVersion((v) => v + 1);
                }}
            />
        </>
    );
}
