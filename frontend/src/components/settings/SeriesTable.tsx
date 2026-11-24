"use client";

import { useEffect, useState } from "react";
import Alert from "@mui/material/Alert";
import Button from "@mui/material/Button";
import Checkbox from "@mui/material/Checkbox";
import Chip from "@mui/material/Chip";
import Dialog from "@mui/material/Dialog";
import DialogActions from "@mui/material/DialogActions";
import DialogContent from "@mui/material/DialogContent";
import DialogTitle from "@mui/material/DialogTitle";
import FormControlLabel from "@mui/material/FormControlLabel";
import MenuItem from "@mui/material/MenuItem";
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
import DashboardCard from "@/components/dashboard/DashboardCard";
import { ApiError } from "@/lib/api/client";
import { errorMessage } from "@/lib/api/errorMessages";
import { identity, type BranchDto } from "@/lib/api/identity";
import { invoicing, MODE_LABELS, type InvoiceMode, type SeriesDto, type SeriesGroup } from "@/lib/api/invoicing";

const GROUP_LABELS: Record<SeriesGroup, string> = { sales: "Satış", purchase: "Alış" };

/** e-Belge öneki tam 3 karakter [A-Z0-9]; kâğıt öneki 1-10 karakter, boşluksuz. Hata iletisi ya da null. */
export function validatePrefix(mode: InvoiceMode, prefix: string): string | null {
    if (mode === "paper") {
        return /^\S{1,10}$/.test(prefix) ? null : "Kâğıt serisinin öneki 1-10 karakter olmalı ve boşluk içermemelidir.";
    }
    return /^[A-Z0-9]{3}$/.test(prefix) ? null : "e-Belge serisinin öneki 3 karakter (A-Z, 0-9) olmalıdır.";
}

type FormState = { id: string | null; kindGroup: SeriesGroup; mode: InvoiceMode; prefix: string; year: string; isDefault: boolean; isActive: boolean; locked: boolean };

const emptyForm = (): FormState => ({ id: null, kindGroup: "sales", mode: "eInvoice", prefix: "", year: String(new Date().getFullYear()), isDefault: false, isActive: true, locked: false });

/** Belge serileri: şube bazında liste, ekleme ve düzenleme; e-belge öneki doğrulaması. */
export default function SeriesTable() {
    const [branches, setBranches] = useState<BranchDto[]>([]);
    const [branchId, setBranchId] = useState("");
    const [series, setSeries] = useState<SeriesDto[] | null>(null);
    const [error, setError] = useState<ApiError | string | null>(null);
    const [form, setForm] = useState<FormState | null>(null);
    const [busy, setBusy] = useState(false);
    const [reload, setReload] = useState(0);

    useEffect(() => {
        identity.branches.list().then(
            (list) => {
                const active = list.filter((b) => b.isActive);
                setBranches(active);
                setBranchId((cur) => cur || (active.find((b) => b.isDefault) ?? active[0])?.id || "");
            },
            () => undefined,
        );
    }, []);

    useEffect(() => {
        if (!branchId) return;
        let cancelled = false;
        invoicing.series.list(branchId).then(
            (list) => {
                if (cancelled) return;
                setSeries(list);
                setError(null);
            },
            (e: unknown) => {
                if (cancelled) return;
                if (!(e instanceof ApiError)) throw e;
                setError(e);
                setSeries([]);
            },
        );
        return () => {
            cancelled = true;
        };
    }, [branchId, reload]);

    const prefixError = form && form.prefix !== "" ? validatePrefix(form.mode, form.prefix) : null;
    const yearNumber = form ? Number(form.year) : 0;
    const valid = form !== null && validatePrefix(form.mode, form.prefix) === null && Number.isInteger(yearNumber) && yearNumber >= 2000 && yearNumber <= 2100;

    async function submit() {
        if (!form || !valid) return;
        setBusy(true);
        setError(null);
        try {
            if (form.id) {
                await invoicing.series.update(form.id, { kindGroup: form.kindGroup, mode: form.mode, prefix: form.prefix, year: yearNumber, isDefault: form.isDefault, isActive: form.isActive });
            } else {
                await invoicing.series.create({ branchId, kindGroup: form.kindGroup, mode: form.mode, prefix: form.prefix, year: yearNumber, isDefault: form.isDefault, isActive: form.isActive });
            }
            setForm(null);
            setReload((n) => n + 1);
        } catch (e) {
            if (!(e instanceof ApiError)) throw e;
            setError(errorMessage(e));
        } finally {
            setBusy(false);
        }
    }

    const edit = (s: SeriesDto) =>
        setForm({ id: s.id, kindGroup: s.kindGroup, mode: s.mode, prefix: s.prefix, year: String(s.year), isDefault: s.isDefault, isActive: s.isActive, locked: s.nextNumber > 1 });

    return (
        <DashboardCard
            title="Belge serileri"
            action={
                <Button variant="contained" disabled={!branchId} onClick={() => setForm(emptyForm())}>
                    Yeni seri
                </Button>
            }
        >
            <Stack spacing={2}>
                <TextField select size="small" label="Şube" value={branchId} onChange={(e) => setBranchId(e.target.value)} sx={{ maxWidth: 260 }}>
                    {branches.map((b) => (
                        <MenuItem key={b.id} value={b.id}>
                            {b.name}
                        </MenuItem>
                    ))}
                </TextField>
                <FormAlert error={form ? null : error} />
                {series && series.length === 0 ? (
                    <Typography color="text.secondary">Bu şubede seri yok. Fatura kesmek için en az bir seri tanımlayın.</Typography>
                ) : (
                    series && (
                        <TableContainer>
                            <Table size="small">
                                <TableHead>
                                    <TableRow>
                                        <TableCell>Grup</TableCell>
                                        <TableCell>Mod</TableCell>
                                        <TableCell>Önek</TableCell>
                                        <TableCell>Yıl</TableCell>
                                        <TableCell>Sıradaki numara</TableCell>
                                        <TableCell />
                                        <TableCell />
                                    </TableRow>
                                </TableHead>
                                <TableBody>
                                    {series.map((s) => (
                                        <TableRow key={s.id} sx={s.isActive ? undefined : { opacity: 0.6 }}>
                                            <TableCell>{GROUP_LABELS[s.kindGroup]}</TableCell>
                                            <TableCell>{MODE_LABELS[s.mode]}</TableCell>
                                            <TableCell>{s.prefix}</TableCell>
                                            <TableCell>{s.year}</TableCell>
                                            <TableCell>{s.nextPreview}</TableCell>
                                            <TableCell>
                                                {s.isDefault && <Chip size="small" color="primary" label="Varsayılan" />} {!s.isActive && <Chip size="small" label="Pasif" />}
                                            </TableCell>
                                            <TableCell align="right">
                                                <Button size="small" onClick={() => edit(s)}>
                                                    Düzenle
                                                </Button>
                                            </TableCell>
                                        </TableRow>
                                    ))}
                                </TableBody>
                            </Table>
                        </TableContainer>
                    )
                )}
            </Stack>

            <Dialog open={!!form} onClose={() => setForm(null)} fullWidth maxWidth="xs">
                <DialogTitle>{form?.id ? "Seriyi düzenle" : "Yeni seri"}</DialogTitle>
                <DialogContent>
                    {form && (
                        <Stack spacing={2} sx={{ pt: 1 }}>
                            <FormAlert error={error} />
                            {form.locked && <Alert severity="info">Numara alınmış serinin öneki, yılı ve modu değiştirilemez.</Alert>}
                            <TextField select size="small" label="Belge grubu" value={form.kindGroup} disabled={form.locked} onChange={(e) => setForm({ ...form, kindGroup: e.target.value as SeriesGroup })}>
                                <MenuItem value="sales">Satış</MenuItem>
                                <MenuItem value="purchase">Alış</MenuItem>
                            </TextField>
                            <TextField
                                select
                                size="small"
                                label="Mod"
                                value={form.mode}
                                disabled={form.locked}
                                onChange={(e) => {
                                    const mode = e.target.value as InvoiceMode;
                                    setForm({ ...form, mode, prefix: mode === "paper" ? form.prefix : form.prefix.toUpperCase() });
                                }}
                            >
                                {(Object.keys(MODE_LABELS) as InvoiceMode[]).map((m) => (
                                    <MenuItem key={m} value={m}>
                                        {MODE_LABELS[m]}
                                    </MenuItem>
                                ))}
                            </TextField>
                            <TextField
                                size="small"
                                label="Önek"
                                value={form.prefix}
                                disabled={form.locked}
                                onChange={(e) => setForm({ ...form, prefix: form.mode === "paper" ? e.target.value : e.target.value.toUpperCase() })}
                                error={!!prefixError}
                                helperText={prefixError ?? (form.mode === "paper" ? "1-10 karakter" : "3 karakter, örn. ABC; numara ABC2026000000001 biçiminde olur")}
                            />
                            <TextField size="small" label="Yıl" value={form.year} disabled={form.locked} onChange={(e) => setForm({ ...form, year: e.target.value })} slotProps={{ htmlInput: { inputMode: "numeric" } }} />
                            <FormControlLabel control={<Checkbox checked={form.isDefault} onChange={(e) => setForm({ ...form, isDefault: e.target.checked })} />} label="Varsayılan seri" />
                            <FormControlLabel control={<Checkbox checked={form.isActive} onChange={(e) => setForm({ ...form, isActive: e.target.checked })} />} label="Aktif" />
                        </Stack>
                    )}
                </DialogContent>
                <DialogActions>
                    <Button onClick={() => setForm(null)}>Vazgeç</Button>
                    <Button variant="contained" disabled={!valid || busy} onClick={() => void submit()}>
                        Kaydet
                    </Button>
                </DialogActions>
            </Dialog>
        </DashboardCard>
    );
}
