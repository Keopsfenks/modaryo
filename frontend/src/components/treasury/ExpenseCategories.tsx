"use client";

import { useCallback, useEffect, useState } from "react";
import Button from "@mui/material/Button";
import Chip from "@mui/material/Chip";
import Dialog from "@mui/material/Dialog";
import DialogActions from "@mui/material/DialogActions";
import DialogContent from "@mui/material/DialogContent";
import DialogTitle from "@mui/material/DialogTitle";
import FormControlLabel from "@mui/material/FormControlLabel";
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
import Tooltip from "@mui/material/Tooltip";
import { IconPencil, IconTrash } from "@tabler/icons-react";
import FormAlert from "@/components/auth/FormAlert";
import DashboardCard from "@/components/dashboard/DashboardCard";
import { ApiError } from "@/lib/api/client";
import { errorMessage } from "@/lib/api/errorMessages";
import { treasury, type ExpenseCategoryDto } from "@/lib/api/treasury";

type Draft = { id?: string; name: string; accountCode: string; isIncome: boolean; isActive: boolean };

const emptyDraft: Draft = { name: "", accountCode: "", isIncome: false, isActive: true };

/** Masraf ve gelir kategorileri: ad, muhasebe hesap kodu ve etkinlik. Sistem kategorileri silinemez. */
export default function ExpenseCategories({ canManage }: { canManage: boolean }) {
    const [categories, setCategories] = useState<ExpenseCategoryDto[] | null>(null);
    const [error, setError] = useState<ApiError | string | null>(null);
    const [draft, setDraft] = useState<Draft | null>(null);
    const [draftError, setDraftError] = useState<ApiError | string | null>(null);

    const load = useCallback(async () => {
        try {
            setCategories(await treasury.expenseCategories());
        } catch (e) {
            setError(errorMessage(e));
            setCategories((c) => c ?? []);
        }
    }, []);

    useEffect(() => {
        // eslint-disable-next-line react-hooks/set-state-in-effect -- ilk yükleme
        void load();
    }, [load]);

    async function save() {
        if (!draft) return;
        setDraftError(null);
        const body = { name: draft.name.trim(), accountCode: draft.accountCode.trim(), isIncome: draft.isIncome, isActive: draft.isActive };
        try {
            if (draft.id) await treasury.updateExpenseCategory(draft.id, body);
            else await treasury.createExpenseCategory(body);
            setDraft(null);
            await load();
        } catch (e) {
            setDraftError(e instanceof ApiError ? errorMessage(e) : errorMessage(e));
        }
    }

    async function remove(c: ExpenseCategoryDto) {
        setError(null);
        try {
            await treasury.deleteExpenseCategory(c.id);
        } catch (e) {
            setError(errorMessage(e));
        }
        await load();
    }

    return (
        <>
            <DashboardCard
                title="Kategoriler"
                action={
                    canManage && (
                        <Button variant="contained" onClick={() => setDraft(emptyDraft)}>
                            Yeni kategori
                        </Button>
                    )
                }
            >
                <FormAlert error={error} />
                {categories === null ? (
                    <Skeleton variant="rounded" height={160} />
                ) : (
                    <TableContainer>
                        <Table size="small">
                            <TableHead>
                                <TableRow>
                                    <TableCell>Ad</TableCell>
                                    <TableCell>Tür</TableCell>
                                    <TableCell>Hesap kodu</TableCell>
                                    <TableCell>Durum</TableCell>
                                    {canManage && <TableCell align="right">İşlem</TableCell>}
                                </TableRow>
                            </TableHead>
                            <TableBody>
                                {categories.map((c) => (
                                    <TableRow key={c.id}>
                                        <TableCell>
                                            {c.name} {c.isSystem && <Chip size="small" label="Sistem" sx={{ ml: 1 }} />}
                                        </TableCell>
                                        <TableCell>{c.isIncome ? "Gelir" : "Masraf"}</TableCell>
                                        <TableCell>{c.accountCode}</TableCell>
                                        <TableCell>{c.isActive ? "Aktif" : "Pasif"}</TableCell>
                                        {canManage && (
                                            <TableCell align="right">
                                                <Tooltip title="Düzenle">
                                                    <IconButton aria-label={`${c.name} düzenle`} onClick={() => setDraft({ id: c.id, name: c.name, accountCode: c.accountCode, isIncome: c.isIncome, isActive: c.isActive })}>
                                                        <IconPencil size={18} />
                                                    </IconButton>
                                                </Tooltip>
                                                <Tooltip title={c.isSystem ? "Sistem kategorisi silinemez" : "Sil"}>
                                                    <span>
                                                        <IconButton aria-label={`${c.name} sil`} disabled={c.isSystem} onClick={() => void remove(c)}>
                                                            <IconTrash size={18} />
                                                        </IconButton>
                                                    </span>
                                                </Tooltip>
                                            </TableCell>
                                        )}
                                    </TableRow>
                                ))}
                            </TableBody>
                        </Table>
                    </TableContainer>
                )}
            </DashboardCard>

            <Dialog open={draft !== null} onClose={() => setDraft(null)} fullWidth maxWidth="xs">
                <DialogTitle>{draft?.id ? "Kategoriyi düzenle" : "Yeni kategori"}</DialogTitle>
                <DialogContent>
                    <Stack spacing={2} sx={{ pt: 1 }}>
                        <FormAlert error={draftError} />
                        <TextField label="Ad" value={draft?.name ?? ""} onChange={(e) => setDraft((d) => d && { ...d, name: e.target.value })} />
                        <TextField
                            label="Muhasebe hesap kodu"
                            value={draft?.accountCode ?? ""}
                            helperText="Örn. 770 (gider) ya da 649 (gelir)."
                            onChange={(e) => setDraft((d) => d && { ...d, accountCode: e.target.value })}
                        />
                        {!draft?.id && <FormControlLabel control={<Switch checked={draft?.isIncome ?? false} onChange={(e) => setDraft((d) => d && { ...d, isIncome: e.target.checked })} />} label="Gelir kategorisi" />}
                        {draft?.id && <FormControlLabel control={<Switch checked={draft.isActive} onChange={(e) => setDraft((d) => d && { ...d, isActive: e.target.checked })} />} label="Etkin" />}
                    </Stack>
                </DialogContent>
                <DialogActions>
                    <Button onClick={() => setDraft(null)}>Vazgeç</Button>
                    <Button variant="contained" disabled={!draft?.name.trim() || !draft?.accountCode.trim()} onClick={() => void save()}>
                        Kaydet
                    </Button>
                </DialogActions>
            </Dialog>
        </>
    );
}
