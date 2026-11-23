"use client";

import { useCallback, useEffect, useState } from "react";
import Alert from "@mui/material/Alert";
import Button from "@mui/material/Button";
import Chip from "@mui/material/Chip";
import CircularProgress from "@mui/material/CircularProgress";
import Dialog from "@mui/material/Dialog";
import DialogActions from "@mui/material/DialogActions";
import DialogContent from "@mui/material/DialogContent";
import DialogContentText from "@mui/material/DialogContentText";
import DialogTitle from "@mui/material/DialogTitle";
import Stack from "@mui/material/Stack";
import Switch from "@mui/material/Switch";
import Table from "@mui/material/Table";
import TableBody from "@mui/material/TableBody";
import TableCell from "@mui/material/TableCell";
import TableContainer from "@mui/material/TableContainer";
import TableHead from "@mui/material/TableHead";
import TableRow from "@mui/material/TableRow";
import Tooltip from "@mui/material/Tooltip";
import { errorMessages, errorMessage } from "@/lib/api/errorMessages";
import { identity, type BranchDto } from "@/lib/api/identity";
import BranchDialog from "./BranchDialog";

const DEFAULT_HINT = errorMessages.branch_is_default;

/** Şube listesi: ekle/düzenle, pasifleştir, sil. Varsayılan şubede silme ve pasifleştirme kapalıdır. */
export default function BranchesTable() {
    const [branches, setBranches] = useState<BranchDto[] | null>(null);
    const [error, setError] = useState<string | null>(null);
    const [editing, setEditing] = useState<BranchDto | null | undefined>(undefined);
    const [deleting, setDeleting] = useState<BranchDto | null>(null);

    const load = useCallback(async () => {
        try {
            setBranches(await identity.branches.list());
        } catch (e) {
            setBranches([]);
            setError(errorMessage(e));
        }
    }, []);

    useEffect(() => {
        identity.branches.list().then(setBranches, (e: unknown) => {
            setBranches([]);
            setError(errorMessage(e));
        });
    }, []);

    const run = async (action: () => Promise<unknown>) => {
        setError(null);
        try {
            await action();
            await load();
        } catch (e) {
            setError(errorMessage(e));
        }
    };

    const remove = async () => {
        if (!deleting) return;
        const target = deleting;
        setDeleting(null);
        await run(() => identity.branches.remove(target.id));
    };

    if (!branches) {
        return <CircularProgress aria-label="Yükleniyor" />;
    }

    return (
        <Stack spacing={2}>
            {error && <Alert severity="error">{error}</Alert>}
            <div>
                <Button variant="contained" onClick={() => setEditing(null)}>
                    Yeni şube
                </Button>
            </div>
            <TableContainer>
                <Table size="small">
                    <TableHead>
                        <TableRow>
                            <TableCell>Kod</TableCell>
                            <TableCell>Ad</TableCell>
                            <TableCell>Durum</TableCell>
                            <TableCell align="right">İşlemler</TableCell>
                        </TableRow>
                    </TableHead>
                    <TableBody>
                        {branches.map((b) => (
                            <TableRow key={b.id}>
                                <TableCell>{b.code}</TableCell>
                                <TableCell>
                                    {b.name} {b.isDefault && <Chip size="small" label="Varsayılan" sx={{ ml: 1 }} />}
                                </TableCell>
                                <TableCell>
                                    <Tooltip title={b.isDefault ? DEFAULT_HINT : ""}>
                                        <span>
                                            <Switch
                                                size="small"
                                                checked={b.isActive}
                                                disabled={b.isDefault}
                                                onChange={(e) => void run(() => identity.branches.update(b.id, { code: b.code, name: b.name, isActive: e.target.checked }))}
                                                slotProps={{ input: { "aria-label": `${b.name} aktif` } }}
                                            />
                                            {b.isActive ? "Aktif" : "Pasif"}
                                        </span>
                                    </Tooltip>
                                </TableCell>
                                <TableCell align="right">
                                    <Button size="small" onClick={() => setEditing(b)}>
                                        Düzenle
                                    </Button>
                                    <Tooltip title={b.isDefault ? DEFAULT_HINT : ""}>
                                        <span>
                                            <Button size="small" color="error" disabled={b.isDefault} onClick={() => setDeleting(b)}>
                                                Sil
                                            </Button>
                                        </span>
                                    </Tooltip>
                                </TableCell>
                            </TableRow>
                        ))}
                    </TableBody>
                </Table>
            </TableContainer>
            {editing !== undefined && (
                <BranchDialog
                    branch={editing}
                    onClose={() => setEditing(undefined)}
                    onSaved={() => {
                        setEditing(undefined);
                        void load();
                    }}
                />
            )}
            <Dialog open={!!deleting} onClose={() => setDeleting(null)}>
                <DialogTitle>Şube silinsin mi?</DialogTitle>
                <DialogContent>
                    <DialogContentText>{deleting?.name} şubesi silinecek. Bu işlem geri alınamaz.</DialogContentText>
                </DialogContent>
                <DialogActions>
                    <Button onClick={() => setDeleting(null)}>Vazgeç</Button>
                    <Button color="error" variant="contained" onClick={() => void remove()}>
                        Sil
                    </Button>
                </DialogActions>
            </Dialog>
        </Stack>
    );
}
