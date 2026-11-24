"use client";

import { useCallback, useEffect, useState } from "react";
import Alert from "@mui/material/Alert";
import Button from "@mui/material/Button";
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
import Typography from "@mui/material/Typography";
import { IconPencil, IconTrash } from "@tabler/icons-react";
import FormAlert from "@/components/auth/FormAlert";
import PageHeader from "@/components/common/PageHeader";
import DashboardCard from "@/components/dashboard/DashboardCard";
import { errorMessage } from "@/lib/api/errorMessages";
import { pricing, type CustomerGroupDto, type PriceListDto } from "@/lib/api/pricing";
import { useSession } from "@/session/SessionProvider";

/** Müşteri grupları: ad ve grubun satış listesi. Gruba bağlı müşteri varken silinemez. */
export default function CustomerGroups() {
    const { hasPermission } = useSession();
    const allowed = hasPermission("fiyat.read");
    const canManage = hasPermission("fiyat.manage");

    const [groups, setGroups] = useState<CustomerGroupDto[] | null>(null);
    const [lists, setLists] = useState<PriceListDto[]>([]);
    const [error, setError] = useState<string | null>(null);
    const [editing, setEditing] = useState<{ group?: CustomerGroupDto } | null>(null);
    const [deleting, setDeleting] = useState<CustomerGroupDto | null>(null);
    const [name, setName] = useState("");
    const [listId, setListId] = useState("");
    const [nameError, setNameError] = useState<string | null>(null);
    const [dialogError, setDialogError] = useState<string | null>(null);
    const [busy, setBusy] = useState(false);

    const load = useCallback(async () => {
        try {
            setGroups(await pricing.customerGroups());
        } catch (e) {
            setError(errorMessage(e));
            setGroups((current) => current ?? []);
        }
    }, []);

    useEffect(() => {
        if (!allowed) return;
        // eslint-disable-next-line react-hooks/set-state-in-effect -- ilk yükleme
        void load();
        pricing.lists({ kind: "sale" }).then(setLists, () => undefined);
    }, [allowed, load]);

    if (!allowed) {
        return (
            <>
                <PageHeader title="Müşteri grupları" />
                <Alert severity="warning">Müşteri gruplarını görmek için yetkiniz yok.</Alert>
            </>
        );
    }

    const open = (group?: CustomerGroupDto) => {
        setEditing({ group });
        setName(group?.name ?? "");
        setListId(group?.salePriceListId ?? "");
        setNameError(null);
        setDialogError(null);
    };

    async function save() {
        const trimmed = name.trim();
        if (!trimmed) {
            setNameError("Grup adı zorunludur.");
            return;
        }
        if (trimmed.length > 100) {
            setNameError("Grup adı en fazla 100 karakter olabilir.");
            return;
        }
        setNameError(null);
        setBusy(true);
        setDialogError(null);
        try {
            const body = { name: trimmed, salePriceListId: listId || null };
            if (editing?.group) await pricing.updateGroup(editing.group.id, body);
            else await pricing.createGroup(body);
            setEditing(null);
            await load();
        } catch (e) {
            setDialogError(errorMessage(e));
        } finally {
            setBusy(false);
        }
    }

    async function confirmDelete() {
        if (!deleting) return;
        const target = deleting;
        setDeleting(null);
        setError(null);
        try {
            await pricing.deleteGroup(target.id);
        } catch (e) {
            setError(errorMessage(e));
        }
        await load();
    }

    const listName = (id: string | null) => (id ? (lists.find((l) => l.id === id)?.name ?? "—") : "Liste atanmadı");

    return (
        <>
            <PageHeader title="Müşteri grupları" subtitle="Gruplar bir satış listesine bağlanır; gruptaki müşteriler o listenin fiyatlarını görür." />
            <DashboardCard
                action={
                    canManage && (
                        <Button variant="contained" onClick={() => open()}>
                            Yeni grup
                        </Button>
                    )
                }
            >
                <FormAlert error={error} />
                {groups === null ? (
                    <Skeleton variant="rounded" height={120} />
                ) : groups.length === 0 ? (
                    <Typography color="text.secondary" sx={{ py: 3 }}>
                        Henüz müşteri grubu yok.
                    </Typography>
                ) : (
                    <TableContainer>
                        <Table size="small">
                            <TableHead>
                                <TableRow>
                                    <TableCell>Grup</TableCell>
                                    <TableCell>Satış listesi</TableCell>
                                    <TableCell>Müşteri sayısı</TableCell>
                                    <TableCell align="right">İşlem</TableCell>
                                </TableRow>
                            </TableHead>
                            <TableBody>
                                {groups.map((g) => (
                                    <TableRow key={g.id} hover>
                                        <TableCell>{g.name}</TableCell>
                                        <TableCell>{listName(g.salePriceListId)}</TableCell>
                                        <TableCell>{g.customerCount}</TableCell>
                                        <TableCell align="right">
                                            {canManage && (
                                                <>
                                                    <IconButton aria-label={`${g.name} grubunu düzenle`} size="small" onClick={() => open(g)}>
                                                        <IconPencil size={18} />
                                                    </IconButton>
                                                    <IconButton aria-label={`${g.name} grubunu sil`} size="small" onClick={() => setDeleting(g)}>
                                                        <IconTrash size={18} />
                                                    </IconButton>
                                                </>
                                            )}
                                        </TableCell>
                                    </TableRow>
                                ))}
                            </TableBody>
                        </Table>
                    </TableContainer>
                )}
            </DashboardCard>

            <Dialog open={editing !== null} onClose={() => setEditing(null)} fullWidth maxWidth="xs">
                <DialogTitle>{editing?.group ? "Grubu düzenle" : "Yeni müşteri grubu"}</DialogTitle>
                <DialogContent>
                    <Stack spacing={2.5} sx={{ pt: 1 }}>
                        <FormAlert error={dialogError} />
                        <TextField label="Grup adı" value={name} onChange={(e) => setName(e.target.value)} error={Boolean(nameError)} helperText={nameError} autoFocus />
                        <TextField select label="Satış listesi" value={listId} onChange={(e) => setListId(e.target.value)}>
                            <MenuItem value="">Liste atanmadı</MenuItem>
                            {lists.map((l) => (
                                <MenuItem key={l.id} value={l.id}>
                                    {l.name}
                                </MenuItem>
                            ))}
                        </TextField>
                    </Stack>
                </DialogContent>
                <DialogActions>
                    <Button onClick={() => setEditing(null)}>Vazgeç</Button>
                    <Button variant="contained" disabled={busy} onClick={() => void save()}>
                        Kaydet
                    </Button>
                </DialogActions>
            </Dialog>

            <Dialog open={deleting !== null} onClose={() => setDeleting(null)}>
                <DialogTitle>Grup silinsin mi?</DialogTitle>
                <DialogContent>
                    <DialogContentText>{deleting?.name} grubu silinecek. Gruba bağlı müşteri varsa silinemez.</DialogContentText>
                </DialogContent>
                <DialogActions>
                    <Button onClick={() => setDeleting(null)}>Vazgeç</Button>
                    <Button color="error" variant="contained" onClick={() => void confirmDelete()}>
                        Evet, sil
                    </Button>
                </DialogActions>
            </Dialog>
        </>
    );
}
