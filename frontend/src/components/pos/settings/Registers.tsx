"use client";

import { useCallback, useEffect, useState } from "react";
import { IconPlus } from "@tabler/icons-react";
import Alert from "@mui/material/Alert";
import Button from "@mui/material/Button";
import Chip from "@mui/material/Chip";
import Dialog from "@mui/material/Dialog";
import DialogActions from "@mui/material/DialogActions";
import DialogContent from "@mui/material/DialogContent";
import DialogTitle from "@mui/material/DialogTitle";
import FormControlLabel from "@mui/material/FormControlLabel";
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
import { errorMessage } from "@/lib/api/errorMessages";
import { identity, type BranchDto } from "@/lib/api/identity";
import { inventory, type WarehouseDto } from "@/lib/api/inventory";
import { pos, type QuickButtonDto, type RegisterDto, type RegisterInput, type RegisterRow } from "@/lib/api/pos";
import { treasury, type TreasuryAccountDto } from "@/lib/api/treasury";
import QuickButtonDesigner from "./QuickButtonDesigner";

type Lookups = { branches: BranchDto[]; warehouses: WarehouseDto[]; accounts: TreasuryAccountDto[] };

function RegisterDialog({ register, lookups, onClose, onSaved }: { register: RegisterDto | null; lookups: Lookups; onClose: () => void; onSaved: () => void }) {
    const [form, setForm] = useState<RegisterInput>({
        code: register?.code ?? "",
        name: register?.name ?? "",
        branchId: register?.branchId ?? "",
        warehouseId: register?.warehouseId ?? "",
        cashAccountId: register?.cashAccountId ?? "",
        cardAccountId: register?.cardAccountId ?? "",
        isActive: register?.isActive ?? true,
        version: register?.version,
    });
    const [pages, setPages] = useState<(QuickButtonDto | null)[][]>(register?.pages ?? [[]]);
    const [saving, setSaving] = useState(false);
    const [error, setError] = useState<string | null>(null);

    const warehouses = lookups.warehouses.filter((w) => w.branchId === form.branchId && w.kind === "normal" && w.isActive);
    const cashAccounts = lookups.accounts.filter((a) => a.branchId === form.branchId && a.kind === "cash" && a.currency === "TRY" && a.isActive);
    const cardAccounts = lookups.accounts.filter((a) => a.branchId === form.branchId && a.kind === "cardPos" && a.currency === "TRY" && a.isActive);
    const valid = form.code.trim() !== "" && form.name.trim() !== "" && form.branchId && form.warehouseId && form.cashAccountId && form.cardAccountId;

    const setBranch = (branchId: string) => setForm((f) => ({ ...f, branchId, warehouseId: "", cashAccountId: "", cardAccountId: "" }));

    const submit = async () => {
        setSaving(true);
        setError(null);
        try {
            const saved = register ? await pos.updateRegister(register.id, form) : await pos.createRegister(form);
            const buttons = pages.map((p) => p.map((b) => (b ? { productId: b.productId, variantId: b.variantId, presetQty: b.presetQty, color: b.color } : null)));
            await pos.setButtons(saved.id, buttons);
            onSaved();
        } catch (e) {
            setError(errorMessage(e));
        } finally {
            setSaving(false);
        }
    };

    return (
        <Dialog open onClose={onClose} fullWidth maxWidth="md">
            <DialogTitle>{register ? "Kasa noktasını düzenle" : "Yeni kasa noktası"}</DialogTitle>
            <DialogContent>
                <Stack spacing={2} sx={{ pt: 1 }}>
                    {error && <Alert severity="error">{error}</Alert>}
                    <Stack direction={{ xs: "column", sm: "row" }} spacing={2}>
                        <TextField label="Kod" value={form.code} onChange={(e) => setForm({ ...form, code: e.target.value.toUpperCase() })} helperText="En çok 10 harf/rakam; fiş numarasında kullanılır." slotProps={{ htmlInput: { maxLength: 10 } }} sx={{ width: 220 }} />
                        <TextField label="Ad" value={form.name} onChange={(e) => setForm({ ...form, name: e.target.value })} fullWidth />
                    </Stack>
                    <TextField select label="Şube" value={form.branchId} onChange={(e) => setBranch(e.target.value)}>
                        {lookups.branches.filter((b) => b.isActive).map((b) => (
                            <MenuItem key={b.id} value={b.id}>
                                {b.name}
                            </MenuItem>
                        ))}
                    </TextField>
                    <Stack direction={{ xs: "column", sm: "row" }} spacing={2}>
                        <TextField select fullWidth label="Depo" value={form.warehouseId} onChange={(e) => setForm({ ...form, warehouseId: e.target.value })} disabled={!form.branchId}>
                            {warehouses.map((w) => (
                                <MenuItem key={w.id} value={w.id}>
                                    {w.name}
                                </MenuItem>
                            ))}
                        </TextField>
                        <TextField select fullWidth label="Nakit hesabı" value={form.cashAccountId} onChange={(e) => setForm({ ...form, cashAccountId: e.target.value })} disabled={!form.branchId}>
                            {cashAccounts.map((a) => (
                                <MenuItem key={a.id} value={a.id}>
                                    {a.name}
                                </MenuItem>
                            ))}
                        </TextField>
                        <TextField select fullWidth label="Kart hesabı" value={form.cardAccountId} onChange={(e) => setForm({ ...form, cardAccountId: e.target.value })} disabled={!form.branchId}>
                            {cardAccounts.map((a) => (
                                <MenuItem key={a.id} value={a.id}>
                                    {a.name}
                                </MenuItem>
                            ))}
                        </TextField>
                    </Stack>
                    {register && <FormControlLabel control={<Switch checked={form.isActive ?? true} onChange={(e) => setForm({ ...form, isActive: e.target.checked })} />} label="Aktif" />}
                    <Typography variant="subtitle1">Hızlı tuşlar</Typography>
                    <QuickButtonDesigner pages={pages} onChange={setPages} />
                </Stack>
            </DialogContent>
            <DialogActions>
                <Button onClick={onClose}>Vazgeç</Button>
                <Button variant="contained" onClick={() => void submit()} disabled={saving || !valid}>
                    {saving ? "Kaydediliyor..." : "Kaydet"}
                </Button>
            </DialogActions>
        </Dialog>
    );
}

/** Kasa noktaları: liste, ekleme/düzenleme (şube, depo, nakit ve kart hesabı) ve hızlı tuş yerleşimi. */
export default function Registers() {
    const [rows, setRows] = useState<RegisterRow[] | null>(null);
    const [lookups, setLookups] = useState<Lookups | null>(null);
    const [error, setError] = useState<string | null>(null);
    const [editing, setEditing] = useState<{ register: RegisterDto | null } | null>(null);

    const load = useCallback(async () => {
        try {
            const [list, branches, warehouses, accounts] = await Promise.all([pos.registers(), identity.branches.list(), inventory.warehouses(), treasury.accounts()]);
            setRows(list);
            setLookups({ branches, warehouses, accounts });
            setError(null);
        } catch (e) {
            setError(errorMessage(e));
        }
    }, []);

    useEffect(() => {
        // Yükleme tamamlanınca durum güncellenir; ilk yükleme için effect içinde başlatılır.
        // eslint-disable-next-line react-hooks/set-state-in-effect
        void load();
    }, [load]);

    const edit = async (id: string | null) => {
        try {
            setEditing({ register: id ? await pos.register(id) : null });
        } catch (e) {
            setError(errorMessage(e));
        }
    };

    const branchName = (id: string) => lookups?.branches.find((b) => b.id === id)?.name ?? "";

    return (
        <Stack spacing={2}>
            <Stack direction="row" sx={{ alignItems: "center", justifyContent: "space-between" }}>
                <Typography variant="h6">Kasa noktaları</Typography>
                <Button variant="contained" startIcon={<IconPlus size={18} />} onClick={() => void edit(null)} disabled={!lookups}>
                    Kasa noktası ekle
                </Button>
            </Stack>
            {error && <Alert severity="error">{error}</Alert>}
            <Table size="small">
                <TableHead>
                    <TableRow>
                        <TableCell>Kod</TableCell>
                        <TableCell>Ad</TableCell>
                        <TableCell>Şube</TableCell>
                        <TableCell>Durum</TableCell>
                        <TableCell />
                    </TableRow>
                </TableHead>
                <TableBody>
                    {(rows ?? []).map((r) => (
                        <TableRow key={r.id}>
                            <TableCell>{r.code}</TableCell>
                            <TableCell>{r.name}</TableCell>
                            <TableCell>{branchName(r.branchId)}</TableCell>
                            <TableCell>
                                <Chip size="small" label={r.isActive ? "Aktif" : "Pasif"} color={r.isActive ? "success" : "default"} />
                            </TableCell>
                            <TableCell align="right">
                                <Button size="small" onClick={() => void edit(r.id)}>
                                    Düzenle
                                </Button>
                            </TableCell>
                        </TableRow>
                    ))}
                    {rows?.length === 0 && (
                        <TableRow>
                            <TableCell colSpan={5}>Henüz kasa noktası yok.</TableCell>
                        </TableRow>
                    )}
                </TableBody>
            </Table>
            {editing && lookups && (
                <RegisterDialog
                    register={editing.register}
                    lookups={lookups}
                    onClose={() => setEditing(null)}
                    onSaved={() => {
                        setEditing(null);
                        void load();
                    }}
                />
            )}
        </Stack>
    );
}
