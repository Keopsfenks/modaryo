"use client";

import { useCallback, useEffect, useState } from "react";
import Link from "next/link";
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
import Table from "@mui/material/Table";
import TableBody from "@mui/material/TableBody";
import TableCell from "@mui/material/TableCell";
import TableContainer from "@mui/material/TableContainer";
import TableHead from "@mui/material/TableHead";
import TableRow from "@mui/material/TableRow";
import Tooltip from "@mui/material/Tooltip";
import { errorMessage, errorMessages } from "@/lib/api/errorMessages";
import { identity, type RoleDto } from "@/lib/api/identity";
import { useSession } from "@/session/SessionProvider";

const permissionLabel = (role: RoleDto) => (role.permissions.includes("*") ? "Tüm yetkiler" : String(role.permissions.length));

/** Rol listesi. Yazma eylemleri (yeni, kopyala, sil) roller.manage ister; yoksa yalnızca görüntüleme vardır. */
export default function RolesTable() {
    const { hasPermission } = useSession();
    const canManage = hasPermission("roller.manage");
    const [roles, setRoles] = useState<RoleDto[] | null>(null);
    const [error, setError] = useState<string | null>(null);
    const [deleting, setDeleting] = useState<RoleDto | null>(null);

    const load = useCallback(async () => {
        try {
            setRoles(await identity.roles.list());
        } catch (e) {
            setRoles([]);
            setError(errorMessage(e));
        }
    }, []);

    useEffect(() => {
        identity.roles.list().then(setRoles, (e: unknown) => {
            setRoles([]);
            setError(errorMessage(e));
        });
    }, []);

    const remove = async () => {
        if (!deleting) return;
        const target = deleting;
        setDeleting(null);
        setError(null);
        try {
            await identity.roles.remove(target.id);
            await load();
        } catch (e) {
            setError(errorMessage(e));
        }
    };

    if (!roles) {
        return <CircularProgress aria-label="Yükleniyor" />;
    }

    const deleteHint = (role: RoleDto) => (role.systemKey ? errorMessages.role_locked : role.memberCount > 0 ? errorMessages.role_in_use : "");

    return (
        <Stack spacing={2}>
            {error && <Alert severity="error">{error}</Alert>}
            {canManage && (
                <div>
                    <Button component={Link} href="/ayarlar/roller/yeni" variant="contained">
                        Yeni rol
                    </Button>
                </div>
            )}
            <TableContainer>
                <Table size="small">
                    <TableHead>
                        <TableRow>
                            <TableCell>Rol</TableCell>
                            <TableCell>İzin</TableCell>
                            <TableCell>En çok iskonto</TableCell>
                            <TableCell>Şubeler</TableCell>
                            <TableCell>Üye</TableCell>
                            <TableCell align="right">İşlemler</TableCell>
                        </TableRow>
                    </TableHead>
                    <TableBody>
                        {roles.map((r) => {
                            const hint = deleteHint(r);
                            return (
                                <TableRow key={r.id}>
                                    <TableCell>
                                        {r.name} {r.systemKey && <Chip size="small" label="Hazır rol" sx={{ ml: 1 }} />}
                                    </TableCell>
                                    <TableCell>{permissionLabel(r)}</TableCell>
                                    <TableCell>%{r.maxDiscountPercent}</TableCell>
                                    <TableCell>{r.allBranches ? "Tümü" : r.branchIds.length}</TableCell>
                                    <TableCell>{r.memberCount}</TableCell>
                                    <TableCell align="right">
                                        <Button size="small" component={Link} href={`/ayarlar/roller/${r.id}`}>
                                            {canManage ? "Düzenle" : "Görüntüle"}
                                        </Button>
                                        {canManage && (
                                            <>
                                                <Button size="small" component={Link} href={`/ayarlar/roller/yeni?kopya=${r.id}`}>
                                                    Kopyala
                                                </Button>
                                                <Tooltip title={hint}>
                                                    <span>
                                                        <Button size="small" color="error" disabled={!!hint} onClick={() => setDeleting(r)}>
                                                            Sil
                                                        </Button>
                                                    </span>
                                                </Tooltip>
                                            </>
                                        )}
                                    </TableCell>
                                </TableRow>
                            );
                        })}
                    </TableBody>
                </Table>
            </TableContainer>
            <Dialog open={!!deleting} onClose={() => setDeleting(null)}>
                <DialogTitle>Rol silinsin mi?</DialogTitle>
                <DialogContent>
                    <DialogContentText>{deleting?.name} rolü silinecek. Bu işlem geri alınamaz.</DialogContentText>
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
