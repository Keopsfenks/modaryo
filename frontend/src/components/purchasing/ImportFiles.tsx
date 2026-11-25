"use client";

import { useEffect, useState } from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import Alert from "@mui/material/Alert";
import Autocomplete from "@mui/material/Autocomplete";
import Button from "@mui/material/Button";
import Chip from "@mui/material/Chip";
import Dialog from "@mui/material/Dialog";
import DialogActions from "@mui/material/DialogActions";
import DialogContent from "@mui/material/DialogContent";
import DialogTitle from "@mui/material/DialogTitle";
import MenuItem from "@mui/material/MenuItem";
import Skeleton from "@mui/material/Skeleton";
import Stack from "@mui/material/Stack";
import Table from "@mui/material/Table";
import TableBody from "@mui/material/TableBody";
import TableCell from "@mui/material/TableCell";
import TableContainer from "@mui/material/TableContainer";
import TableHead from "@mui/material/TableHead";
import TableRow from "@mui/material/TableRow";
import TextField from "@mui/material/TextField";
import Typography from "@mui/material/Typography";
import FormAlert from "@/components/auth/FormAlert";
import PageHeader from "@/components/common/PageHeader";
import DashboardCard from "@/components/dashboard/DashboardCard";
import { useDebounced } from "@/components/pricing/useDebounced";
import { ApiError } from "@/lib/api/client";
import { errorMessage } from "@/lib/api/errorMessages";
import { erp } from "@/lib/api/erp";
import { INCOTERMS, purchasing, type ImportFileDto, type ImportFileRow, type ImportFileStatus, type Incoterm } from "@/lib/api/purchasing";
import { CURRENCIES } from "@/lib/money";
import { useSession } from "@/session/SessionProvider";

export const IMPORT_STATUS_LABELS: Record<ImportFileStatus, string> = { open: "Açık", closed: "Kapalı" };

export function ModuleOff() {
    return <Alert severity="info">İthalat modülü firmanızda kapalı. Ayarlar &gt; Modüller bölümünden açabilirsiniz.</Alert>;
}

/** Incoterm sunucudan küçük harfle gelebilir ("fob"); gösterimde büyük harf. */
export const incotermText = (v: string) => v.toUpperCase();

/** İthalat dosyaları listesi (ithalat modülü). Yeni dosya penceresi buradan açılır. */
export default function ImportFiles() {
    const router = useRouter();
    const { hasPermission, hasModule } = useSession();
    const allowed = hasPermission("satinalma.read") && hasModule("ithalat");
    const [status, setStatus] = useState<ImportFileStatus | "">("");
    const [rows, setRows] = useState<ImportFileRow[] | null>(null);
    const [error, setError] = useState<ApiError | null>(null);
    const [creating, setCreating] = useState(false);

    useEffect(() => {
        if (!allowed) return;
        let cancelled = false;
        purchasing.importFiles.list(status || undefined).then(
            (r) => {
                if (cancelled) return;
                setError(null);
                setRows(r);
            },
            (e: unknown) => {
                if (cancelled) return;
                if (!(e instanceof ApiError)) throw e;
                setError(e);
                setRows((c) => c ?? []);
            },
        );
        return () => {
            cancelled = true;
        };
    }, [allowed, status]);

    if (!hasModule("ithalat")) return <ModuleOff />;
    if (!hasPermission("satinalma.read")) return <Alert severity="warning">İthalat dosyalarını görüntülemek için yetkiniz yok.</Alert>;

    return (
        <>
            <PageHeader title="İthalat dosyaları" subtitle="Sipariş, mal kabul, fatura ve masrafları tek dosyada toplayın" />
            <DashboardCard
                action={
                    hasPermission("satinalma.manage") ? (
                        <Button variant="contained" onClick={() => setCreating(true)}>
                            Yeni ithalat dosyası
                        </Button>
                    ) : undefined
                }
            >
                <TextField select size="small" label="Durum" value={status} onChange={(e) => setStatus(e.target.value as ImportFileStatus | "")} sx={{ minWidth: 160, mb: 2 }}>
                    <MenuItem value="">Tümü</MenuItem>
                    <MenuItem value="open">Açık</MenuItem>
                    <MenuItem value="closed">Kapalı</MenuItem>
                </TextField>
                <FormAlert error={error} />
                {rows === null ? (
                    <Skeleton variant="rounded" height={160} />
                ) : rows.length === 0 && !error ? (
                    <Typography color="text.secondary" sx={{ py: 4, textAlign: "center" }}>
                        Gösterilecek ithalat dosyası yok.
                    </Typography>
                ) : (
                    <TableContainer>
                        <Table size="small" sx={{ minWidth: 640 }}>
                            <TableHead>
                                <TableRow>
                                    <TableCell>Numara</TableCell>
                                    <TableCell>Tedarikçi</TableCell>
                                    <TableCell>Menşei</TableCell>
                                    <TableCell>Incoterm</TableCell>
                                    <TableCell>Para birimi</TableCell>
                                    <TableCell>Durum</TableCell>
                                </TableRow>
                            </TableHead>
                            <TableBody>
                                {rows.map((r) => (
                                    <TableRow key={r.id} hover>
                                        <TableCell>
                                            <Typography component={Link} href={`/satin-alma/ithalat/${r.id}`} sx={{ color: "primary.main", fontWeight: 600 }}>
                                                {r.number}
                                            </Typography>
                                        </TableCell>
                                        <TableCell>{r.supplierName}</TableCell>
                                        <TableCell>{r.originCountry}</TableCell>
                                        <TableCell>{incotermText(r.incoterm)}</TableCell>
                                        <TableCell>{r.currency}</TableCell>
                                        <TableCell>
                                            <Chip size="small" label={IMPORT_STATUS_LABELS[r.status]} color={r.status === "open" ? "primary" : "default"} />
                                        </TableCell>
                                    </TableRow>
                                ))}
                            </TableBody>
                        </Table>
                    </TableContainer>
                )}
            </DashboardCard>
            {creating && <ImportFileDialog onClose={() => setCreating(false)} onSaved={(f) => router.push(`/satin-alma/ithalat/${f.id}`)} />}
        </>
    );
}

type DialogProps = { file?: ImportFileDto; onClose(): void; onSaved(file: ImportFileDto): void };

/** İthalat dosyası başlığı: tedarikçi, menşei ülke, Incoterm, para birimi. Yeni oluşturma ve düzenleme için ortak. */
export function ImportFileDialog({ file, onClose, onSaved }: DialogProps) {
    const [supplier, setSupplier] = useState<{ id: string; name: string } | null>(file ? { id: file.supplierId, name: file.supplierName } : null);
    const [origin, setOrigin] = useState(file?.originCountry ?? "");
    const [incoterm, setIncoterm] = useState<Incoterm>((file ? incotermText(file.incoterm) : "FOB") as Incoterm);
    const [currency, setCurrency] = useState(file?.currency ?? "USD");
    const [notes, setNotes] = useState(file?.notes ?? "");
    const [error, setError] = useState<string | null>(null);
    const [busy, setBusy] = useState(false);
    const [input, setInput] = useState("");
    const search = useDebounced(input, 250);
    const [options, setOptions] = useState<{ id: string; name: string }[]>([]);

    useEffect(() => {
        let cancelled = false;
        erp.listAccounts({ search: search || undefined, role: "supplier", pageSize: 20 }).then(
            (r) => !cancelled && setOptions(r.items),
            () => !cancelled && setOptions([]),
        );
        return () => {
            cancelled = true;
        };
    }, [search]);

    async function submit() {
        if (!supplier) return setError("Tedarikçi seçin.");
        if (!origin.trim()) return setError("Menşei ülke zorunludur.");
        setBusy(true);
        setError(null);
        const body = { supplierId: supplier.id, originCountry: origin.trim(), incoterm, currency, notes: notes.trim() || null };
        try {
            onSaved(file ? await purchasing.importFiles.update(file.id, body) : await purchasing.importFiles.create(body));
        } catch (e) {
            if (!(e instanceof ApiError)) throw e;
            setError(errorMessage(e));
        } finally {
            setBusy(false);
        }
    }

    return (
        <Dialog open onClose={onClose} fullWidth maxWidth="sm">
            <DialogTitle>{file ? `İthalat dosyasını düzenle · ${file.number}` : "Yeni ithalat dosyası"}</DialogTitle>
            <DialogContent>
                <Stack spacing={2} sx={{ pt: 1 }}>
                    <FormAlert error={error} />
                    <Autocomplete
                        size="small"
                        options={options}
                        value={supplier}
                        filterOptions={(x) => x}
                        getOptionLabel={(o) => o.name}
                        isOptionEqualToValue={(a, b) => a.id === b.id}
                        onInputChange={(_, v) => setInput(v)}
                        onChange={(_, next) => setSupplier(next)}
                        renderInput={(params) => <TextField {...params} label="Tedarikçi" />}
                    />
                    <Stack direction={{ xs: "column", sm: "row" }} spacing={2}>
                        <TextField size="small" label="Menşei ülke" value={origin} onChange={(e) => setOrigin(e.target.value)} sx={{ flexGrow: 1 }} slotProps={{ htmlInput: { maxLength: 80 } }} />
                        <TextField select size="small" label="Incoterm" value={incoterm} onChange={(e) => setIncoterm(e.target.value as Incoterm)} sx={{ minWidth: 120 }}>
                            {INCOTERMS.map((t) => (
                                <MenuItem key={t} value={t}>
                                    {t}
                                </MenuItem>
                            ))}
                        </TextField>
                        <TextField select size="small" label="Para birimi" value={currency} onChange={(e) => setCurrency(e.target.value)} sx={{ minWidth: 120 }}>
                            {CURRENCIES.map((c) => (
                                <MenuItem key={c} value={c}>
                                    {c}
                                </MenuItem>
                            ))}
                        </TextField>
                    </Stack>
                    <TextField size="small" label="Not" value={notes} onChange={(e) => setNotes(e.target.value)} multiline minRows={2} slotProps={{ htmlInput: { maxLength: 500 } }} />
                </Stack>
            </DialogContent>
            <DialogActions>
                <Button onClick={onClose}>Vazgeç</Button>
                <Button variant="contained" disabled={busy} onClick={() => void submit()}>
                    {busy ? "Kaydediliyor..." : "Kaydet"}
                </Button>
            </DialogActions>
        </Dialog>
    );
}
