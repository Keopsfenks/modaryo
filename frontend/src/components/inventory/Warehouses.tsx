"use client";

import { useCallback, useEffect, useState } from "react";
import Alert from "@mui/material/Alert";
import Button from "@mui/material/Button";
import Chip from "@mui/material/Chip";
import Dialog from "@mui/material/Dialog";
import DialogActions from "@mui/material/DialogActions";
import DialogContent from "@mui/material/DialogContent";
import DialogContentText from "@mui/material/DialogContentText";
import DialogTitle from "@mui/material/DialogTitle";
import IconButton from "@mui/material/IconButton";
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
import Tooltip from "@mui/material/Tooltip";
import Typography from "@mui/material/Typography";
import { IconPencil, IconPower, IconTrash } from "@tabler/icons-react";
import FormAlert from "@/components/auth/FormAlert";
import PageHeader from "@/components/common/PageHeader";
import DashboardCard from "@/components/dashboard/DashboardCard";
import { ApiError } from "@/lib/api/client";
import { errorMessage } from "@/lib/api/errorMessages";
import { identity, type BranchDto } from "@/lib/api/identity";
import { WAREHOUSE_KIND_LABELS, inventory, type WarehouseDto, type WarehouseKind } from "@/lib/api/inventory";
import { useSession } from "@/session/SessionProvider";

type Draft = { id?: string; code: string; name: string; kind: WarehouseKind; branchId: string; isDefault: boolean };

const emptyDraft = (branchId: string): Draft => ({ code: "", name: "", kind: "normal", branchId, isDefault: false });

/** Depo listesi: oluşturma, düzenleme, pasifleştirme ve silme. Varsayılan depo silinemez ve pasifleştirilemez. */
export default function Warehouses() {
    const { hasPermission } = useSession();
    const canRead = hasPermission("stok.read");
    const canManage = hasPermission("stok.manage");

    const [warehouses, setWarehouses] = useState<WarehouseDto[] | null>(null);
    const [branches, setBranches] = useState<BranchDto[]>([]);
    const [error, setError] = useState<string | null>(null);
    const [draft, setDraft] = useState<Draft | null>(null);
    const [draftError, setDraftError] = useState<ApiError | string | null>(null);
    const [deleting, setDeleting] = useState<WarehouseDto | null>(null);

    const load = useCallback(async () => {
        try {
            setWarehouses(await inventory.warehouses());
        } catch (e) {
            setError(errorMessage(e));
            setWarehouses((current) => current ?? []);
        }
    }, []);

    useEffect(() => {
        if (!canRead) return;
        // eslint-disable-next-line react-hooks/set-state-in-effect -- ilk yükleme
        void load();
        identity.branches.list().then(setBranches, () => setBranches([]));
    }, [canRead, load]);

    if (!canRead) return <Alert severity="warning">Depoları görmek için yetkiniz yok.</Alert>;

    const branchName = (id: string) => branches.find((b) => b.id === id)?.name ?? "—";
    // Şube listesi okunamıyorsa (ayarlar.subeler yok) depolardan türetilen şubeler seçilebilir.
    const branchOptions: { id: string; name: string }[] =
        branches.length > 0
            ? branches.filter((b) => b.isActive).map((b) => ({ id: b.id, name: b.name }))
            : [...new Set((warehouses ?? []).map((w) => w.branchId))].map((id, i) => ({ id, name: `Şube ${i + 1}` }));

    async function save() {
        if (!draft) return;
        setDraftError(null);
        try {
            if (draft.id) {
                await inventory.updateWarehouse(draft.id, { code: draft.code.trim(), name: draft.name.trim(), kind: draft.kind });
            } else {
                await inventory.createWarehouse({ code: draft.code.trim(), name: draft.name.trim(), kind: draft.kind, branchId: draft.branchId });
            }
            setDraft(null);
            await load();
        } catch (e) {
            if (e instanceof ApiError && e.code === "code_taken") {
                setDraftError("Bu depo kodu kullanılıyor.");
            } else {
                setDraftError(e instanceof ApiError ? e : errorMessage(e));
            }
        }
    }

    async function toggleActive(w: WarehouseDto) {
        setError(null);
        try {
            await inventory.setWarehouseActive(w.id, !w.isActive);
        } catch (e) {
            setError(errorMessage(e));
        }
        await load();
    }

    async function confirmDelete() {
        if (!deleting) return;
        const target = deleting;
        setDeleting(null);
        setError(null);
        try {
            await inventory.deleteWarehouse(target.id);
        } catch (e) {
            setError(errorMessage(e));
        }
        await load();
    }

    return (
        <>
            <PageHeader title="Depolar" subtitle="Her şubenin bir varsayılan deposu vardır; karantina ve hasarlı depolar eklenebilir." />
            <DashboardCard
                action={
                    canManage && (
                        <Button variant="contained" onClick={() => setDraft(emptyDraft(branchOptions[0]?.id ?? ""))}>
                            Yeni depo
                        </Button>
                    )
                }
            >
                <FormAlert error={error} />
                {warehouses === null ? (
                    <Skeleton variant="rounded" height={160} />
                ) : warehouses.length === 0 ? (
                    <Typography color="text.secondary" sx={{ py: 3 }}>
                        Henüz depo yok. İlk stok işleminde şubenin varsayılan deposu otomatik oluşturulur.
                    </Typography>
                ) : (
                    <TableContainer>
                        <Table size="small">
                            <TableHead>
                                <TableRow>
                                    <TableCell>Kod</TableCell>
                                    <TableCell>Ad</TableCell>
                                    <TableCell>Şube</TableCell>
                                    <TableCell>Tür</TableCell>
                                    <TableCell>Durum</TableCell>
                                    {canManage && <TableCell align="right">İşlem</TableCell>}
                                </TableRow>
                            </TableHead>
                            <TableBody>
                                {warehouses.map((w) => (
                                    <TableRow key={w.id}>
                                        <TableCell>{w.code}</TableCell>
                                        <TableCell>
                                            {w.name} {w.isDefault && <Chip size="small" label="Varsayılan" sx={{ ml: 1 }} />}
                                        </TableCell>
                                        <TableCell>{branchName(w.branchId)}</TableCell>
                                        <TableCell>{WAREHOUSE_KIND_LABELS[w.kind]}</TableCell>
                                        <TableCell>{w.isActive ? "Aktif" : "Pasif"}</TableCell>
                                        {canManage && (
                                            <TableCell align="right">
                                                <Tooltip title="Düzenle">
                                                    <IconButton
                                                        aria-label={`${w.name} düzenle`}
                                                        onClick={() => setDraft({ id: w.id, code: w.code, name: w.name, kind: w.kind, branchId: w.branchId, isDefault: w.isDefault })}
                                                    >
                                                        <IconPencil size={18} />
                                                    </IconButton>
                                                </Tooltip>
                                                <Tooltip title={w.isDefault ? "Varsayılan depo pasifleştirilemez" : w.isActive ? "Pasifleştir" : "Etkinleştir"}>
                                                    <span>
                                                        <IconButton aria-label={`${w.name} ${w.isActive ? "pasifleştir" : "etkinleştir"}`} disabled={w.isDefault} onClick={() => void toggleActive(w)}>
                                                            <IconPower size={18} />
                                                        </IconButton>
                                                    </span>
                                                </Tooltip>
                                                <Tooltip title={w.isDefault ? "Varsayılan depo silinemez" : w.inUse ? "Hareketi olan depo silinemez; pasifleştirin" : "Sil"}>
                                                    <span>
                                                        <IconButton aria-label={`${w.name} sil`} disabled={w.isDefault || w.inUse} onClick={() => setDeleting(w)}>
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
                <DialogTitle>{draft?.id ? "Depoyu düzenle" : "Yeni depo"}</DialogTitle>
                <DialogContent>
                    <Stack spacing={2} sx={{ pt: 1 }}>
                        <FormAlert error={draftError} />
                        <TextField label="Kod" value={draft?.code ?? ""} onChange={(e) => setDraft((d) => d && { ...d, code: e.target.value })} />
                        <TextField label="Ad" value={draft?.name ?? ""} onChange={(e) => setDraft((d) => d && { ...d, name: e.target.value })} />
                        <TextField
                            select
                            label="Tür"
                            value={draft?.kind ?? "normal"}
                            disabled={draft?.isDefault}
                            helperText={draft?.isDefault ? "Varsayılan deponun türü değiştirilemez." : "Yalnızca Normal depolar kullanılabilir stoğa sayılır."}
                            onChange={(e) => setDraft((d) => d && { ...d, kind: e.target.value as WarehouseKind })}
                        >
                            {(Object.keys(WAREHOUSE_KIND_LABELS) as WarehouseKind[]).map((k) => (
                                <MenuItem key={k} value={k}>
                                    {WAREHOUSE_KIND_LABELS[k]}
                                </MenuItem>
                            ))}
                        </TextField>
                        {!draft?.id && (
                            <TextField select label="Şube" value={draft?.branchId ?? ""} onChange={(e) => setDraft((d) => d && { ...d, branchId: e.target.value })}>
                                {branchOptions.map((b) => (
                                    <MenuItem key={b.id} value={b.id}>
                                        {b.name}
                                    </MenuItem>
                                ))}
                            </TextField>
                        )}
                    </Stack>
                </DialogContent>
                <DialogActions>
                    <Button onClick={() => setDraft(null)}>Vazgeç</Button>
                    <Button variant="contained" disabled={!draft?.code.trim() || !draft?.name.trim() || !draft?.branchId} onClick={() => void save()}>
                        Kaydet
                    </Button>
                </DialogActions>
            </Dialog>

            <Dialog open={deleting !== null} onClose={() => setDeleting(null)}>
                <DialogTitle>Depoyu sil</DialogTitle>
                <DialogContent>
                    <DialogContentText>{deleting?.name} deposu silinecek. Bu işlem geri alınamaz.</DialogContentText>
                </DialogContent>
                <DialogActions>
                    <Button onClick={() => setDeleting(null)}>Vazgeç</Button>
                    <Button color="error" variant="contained" onClick={() => void confirmDelete()}>
                        Sil
                    </Button>
                </DialogActions>
            </Dialog>
        </>
    );
}
