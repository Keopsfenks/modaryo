"use client";

import { useCallback, useEffect, useState } from "react";
import Autocomplete from "@mui/material/Autocomplete";
import Box from "@mui/material/Box";
import Button from "@mui/material/Button";
import Chip from "@mui/material/Chip";
import Dialog from "@mui/material/Dialog";
import DialogActions from "@mui/material/DialogActions";
import DialogContent from "@mui/material/DialogContent";
import DialogContentText from "@mui/material/DialogContentText";
import DialogTitle from "@mui/material/DialogTitle";
import FormControlLabel from "@mui/material/FormControlLabel";
import FormHelperText from "@mui/material/FormHelperText";
import IconButton from "@mui/material/IconButton";
import MenuItem from "@mui/material/MenuItem";
import Stack from "@mui/material/Stack";
import Switch from "@mui/material/Switch";
import Table from "@mui/material/Table";
import TableBody from "@mui/material/TableBody";
import TableCell from "@mui/material/TableCell";
import TableHead from "@mui/material/TableHead";
import TableRow from "@mui/material/TableRow";
import TextField from "@mui/material/TextField";
import Typography from "@mui/material/Typography";
import { IconPencil, IconTrash } from "@tabler/icons-react";
import FormAlert from "@/components/auth/FormAlert";
import DashboardCard from "@/components/dashboard/DashboardCard";
import { catalog, type CustomFieldDto, type CustomFieldInput, type CustomFieldType } from "@/lib/api/catalog";
import { errorMessage } from "@/lib/api/errorMessages";

const MAX_DEFINITIONS = 30;
const KEY = /^[a-z][a-z0-9_]{1,30}$/;

const TYPE_LABELS: Record<CustomFieldType, string> = { text: "Metin", number: "Sayı", date: "Tarih", select: "Liste", bool: "Evet/Hayır" };

type Draft = {
    id: string | null;
    key: string;
    label: string;
    type: CustomFieldType;
    options: string[];
    required: boolean;
    appliesToProduct: boolean;
    appliesToService: boolean;
    sort: string;
};

const blank = (sort: number): Draft => ({ id: null, key: "", label: "", type: "text", options: [], required: false, appliesToProduct: true, appliesToService: true, sort: String(sort) });

const fromDto = (d: CustomFieldDto): Draft => ({ ...d, sort: String(d.sort) });

function validate(d: Draft): Record<string, string> {
    const errors: Record<string, string> = {};
    if (!d.id && !KEY.test(d.key)) errors.key = "Anahtar küçük harfle başlamalı; yalnızca küçük harf, rakam ve alt çizgi içermeli (2-31 karakter).";
    if (!d.label.trim()) errors.label = "Alan adı zorunludur.";
    else if (d.label.trim().length > 100) errors.label = "Alan adı en fazla 100 karakter olabilir.";
    if (d.type === "select" && d.options.length === 0) errors.options = "Liste alanı için en az bir seçenek girin.";
    if (!d.appliesToProduct && !d.appliesToService) errors.applies = "Alan ürün ya da hizmete uygulanmalıdır.";
    if (!/^\d+$/.test(d.sort) || Number(d.sort) > 1000) errors.sort = "Sıra 0 ile 1000 arasında olmalıdır.";
    return errors;
}

/** Özel alan tanımları: ürün ve hizmet kartlarına eklenen alanlar (en fazla 30). Anahtar ve tür sonradan değişmez. */
export default function CustomFieldDefinitions() {
    const [items, setItems] = useState<CustomFieldDto[] | null>(null);
    const [error, setError] = useState<string | null>(null);
    const [draft, setDraft] = useState<Draft | null>(null);
    const [errors, setErrors] = useState<Record<string, string>>({});
    const [busy, setBusy] = useState(false);
    const [deleting, setDeleting] = useState<CustomFieldDto | null>(null);

    const load = useCallback(async () => {
        try {
            setItems(await catalog.customFields());
        } catch (e) {
            setError(errorMessage(e));
            setItems((current) => current ?? []);
        }
    }, []);

    useEffect(() => {
        // eslint-disable-next-line react-hooks/set-state-in-effect -- ilk yükleme
        void load();
    }, [load]);

    const patch = (change: Partial<Draft>) => setDraft((d) => (d ? { ...d, ...change } : d));

    async function save() {
        if (!draft) return;
        const found = validate(draft);
        setErrors(found);
        if (Object.keys(found).length > 0) return;

        const body: CustomFieldInput = {
            label: draft.label.trim(),
            options: draft.type === "select" ? draft.options : [],
            required: draft.required,
            appliesToProduct: draft.appliesToProduct,
            appliesToService: draft.appliesToService,
            sort: Number(draft.sort),
        };
        setBusy(true);
        setError(null);
        try {
            if (draft.id) await catalog.updateCustomField(draft.id, body);
            else await catalog.createCustomField({ ...body, key: draft.key, type: draft.type });
            setDraft(null);
            await load();
        } catch (e) {
            const message = errorMessage(e);
            if ((e as { code?: string }).code === "key_taken") setErrors({ key: message });
            else setErrors({ general: message });
        } finally {
            setBusy(false);
        }
    }

    async function remove() {
        if (!deleting) return;
        const target = deleting;
        setDeleting(null);
        setBusy(true);
        setError(null);
        try {
            await catalog.deleteCustomField(target.id);
            await load();
        } catch (e) {
            setError(errorMessage(e));
        } finally {
            setBusy(false);
        }
    }

    const open = (next: Draft) => {
        setErrors({});
        setDraft(next);
    };

    return (
        <Box sx={{ mt: 4 }}>
            <DashboardCard
                title="Özel alanlar"
                subtitle={`Ürün ve hizmet kartlarına eklenen alanlar (${items?.length ?? 0}/${MAX_DEFINITIONS})`}
                action={
                    <Button variant="contained" disabled={(items?.length ?? 0) >= MAX_DEFINITIONS} onClick={() => open(blank((items?.length ?? 0) + 1))}>
                        Alan ekle
                    </Button>
                }
            >
                <FormAlert error={error} />
                {items && items.length === 0 ? (
                    <Typography color="text.secondary">Henüz özel alan tanımlanmadı.</Typography>
                ) : (
                    <Table size="small">
                        <TableHead>
                            <TableRow>
                                <TableCell>Ad</TableCell>
                                <TableCell>Anahtar</TableCell>
                                <TableCell>Tür</TableCell>
                                <TableCell>Uygulanır</TableCell>
                                <TableCell />
                            </TableRow>
                        </TableHead>
                        <TableBody>
                            {(items ?? []).map((d) => (
                                <TableRow key={d.id}>
                                    <TableCell>
                                        {d.label}
                                        {d.required && <Chip size="small" label="Zorunlu" sx={{ ml: 1 }} />}
                                    </TableCell>
                                    <TableCell>{d.key}</TableCell>
                                    <TableCell>{TYPE_LABELS[d.type]}</TableCell>
                                    <TableCell>{[d.appliesToProduct && "Ürün", d.appliesToService && "Hizmet"].filter(Boolean).join(", ")}</TableCell>
                                    <TableCell align="right">
                                        <IconButton aria-label={`${d.label} düzenle`} onClick={() => open(fromDto(d))}>
                                            <IconPencil size={18} />
                                        </IconButton>
                                        <IconButton aria-label={`${d.label} sil`} onClick={() => setDeleting(d)}>
                                            <IconTrash size={18} />
                                        </IconButton>
                                    </TableCell>
                                </TableRow>
                            ))}
                        </TableBody>
                    </Table>
                )}
            </DashboardCard>

            <Dialog open={draft !== null} onClose={() => !busy && setDraft(null)} fullWidth maxWidth="sm">
                <DialogTitle>{draft?.id ? "Özel alanı düzenle" : "Özel alan ekle"}</DialogTitle>
                {draft && (
                    <DialogContent>
                        <Stack spacing={2.5} sx={{ pt: 1 }}>
                            <FormAlert error={errors.general} />
                            <TextField
                                label="Anahtar"
                                value={draft.key}
                                disabled={Boolean(draft.id)}
                                onChange={(e) => patch({ key: e.target.value })}
                                error={Boolean(errors.key)}
                                helperText={errors.key ?? "Küçük harf, rakam ve alt çizgi (örn. kumas_cinsi). Sonradan değişmez."}
                            />
                            <TextField label="Alan adı" value={draft.label} onChange={(e) => patch({ label: e.target.value })} error={Boolean(errors.label)} helperText={errors.label} />
                            <TextField select label="Tür" value={draft.type} disabled={Boolean(draft.id)} onChange={(e) => patch({ type: e.target.value as CustomFieldType })}>
                                {(Object.keys(TYPE_LABELS) as CustomFieldType[]).map((t) => (
                                    <MenuItem key={t} value={t}>
                                        {TYPE_LABELS[t]}
                                    </MenuItem>
                                ))}
                            </TextField>
                            {draft.type === "select" && (
                                <Autocomplete
                                    multiple
                                    freeSolo
                                    options={[] as string[]}
                                    value={draft.options}
                                    onChange={(_, options) => patch({ options: [...new Set(options.map((o) => o.trim()).filter(Boolean))] })}
                                    renderValue={(values, getItemProps) =>
                                        values.map((option, index) => {
                                            const { key, ...props } = getItemProps({ index });
                                            return <Chip key={key} size="small" label={option} {...props} />;
                                        })
                                    }
                                    renderInput={(params) => <TextField {...params} label="Seçenekler" error={Boolean(errors.options)} helperText={errors.options ?? "Seçenek yazıp Enter'a basın."} />}
                                />
                            )}
                            <Box>
                                <FormControlLabel control={<Switch checked={draft.required} onChange={(e) => patch({ required: e.target.checked })} />} label="Zorunlu" />
                                <FormControlLabel control={<Switch checked={draft.appliesToProduct} onChange={(e) => patch({ appliesToProduct: e.target.checked })} />} label="Ürüne uygula" />
                                <FormControlLabel control={<Switch checked={draft.appliesToService} onChange={(e) => patch({ appliesToService: e.target.checked })} />} label="Hizmete uygula" />
                                {errors.applies && <FormHelperText error>{errors.applies}</FormHelperText>}
                            </Box>
                            <TextField label="Sıra" value={draft.sort} onChange={(e) => patch({ sort: e.target.value })} error={Boolean(errors.sort)} helperText={errors.sort} sx={{ maxWidth: 140 }} />
                        </Stack>
                    </DialogContent>
                )}
                <DialogActions>
                    <Button onClick={() => setDraft(null)} disabled={busy}>
                        Vazgeç
                    </Button>
                    <Button variant="contained" onClick={() => void save()} disabled={busy}>
                        Kaydet
                    </Button>
                </DialogActions>
            </Dialog>

            <Dialog open={deleting !== null} onClose={() => setDeleting(null)}>
                <DialogTitle>Özel alan silinsin mi?</DialogTitle>
                <DialogContent>
                    <DialogContentText>{deleting?.label} alanı tanımı silinecek; kartlardaki değerler bir sonraki kayıtta temizlenir.</DialogContentText>
                </DialogContent>
                <DialogActions>
                    <Button onClick={() => setDeleting(null)}>Vazgeç</Button>
                    <Button color="error" variant="contained" onClick={() => void remove()}>
                        Evet, sil
                    </Button>
                </DialogActions>
            </Dialog>
        </Box>
    );
}
