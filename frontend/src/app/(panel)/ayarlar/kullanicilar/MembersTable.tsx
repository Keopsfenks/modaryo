"use client";

import { useCallback, useEffect, useState } from "react";
import Avatar from "@mui/material/Avatar";
import Box from "@mui/material/Box";
import Button from "@mui/material/Button";
import Dialog from "@mui/material/Dialog";
import DialogActions from "@mui/material/DialogActions";
import DialogContent from "@mui/material/DialogContent";
import DialogContentText from "@mui/material/DialogContentText";
import DialogTitle from "@mui/material/DialogTitle";
import MenuItem from "@mui/material/MenuItem";
import Select from "@mui/material/Select";
import Skeleton from "@mui/material/Skeleton";
import Table from "@mui/material/Table";
import TableBody from "@mui/material/TableBody";
import TableCell from "@mui/material/TableCell";
import TableContainer from "@mui/material/TableContainer";
import TableHead from "@mui/material/TableHead";
import TableRow from "@mui/material/TableRow";
import Typography from "@mui/material/Typography";
import FormAlert from "@/components/auth/FormAlert";
import DashboardCard from "@/components/dashboard/DashboardCard";
import { initials } from "@/components/layout/Header";
import MemberBranchesDialog from "@/components/settings/MemberBranchesDialog";
import { ApiError } from "@/lib/api/client";
import { identity, type MemberDto, type RoleDto } from "@/lib/api/identity";
import { formatDate } from "@/lib/format";
import { useSession } from "@/session/SessionProvider";

/**
 * Üye listesi. members.manage izni varsa rol değiştirilebilir ve üye çıkarılabilir.
 * Sahip rolünü yalnızca sahip verebilir/alabilir; kendi satırında "Çıkar" yoktur (bunun için "Firmadan ayrıl").
 */
export default function MembersTable() {
    const { session, hasPermission, refresh } = useSession();
    const [members, setMembers] = useState<MemberDto[] | null>(null);
    const [roles, setRoles] = useState<RoleDto[]>([]);
    const [error, setError] = useState<ApiError | null>(null);
    const [removing, setRemoving] = useState<MemberDto | null>(null);
    const [branchesFor, setBranchesFor] = useState<MemberDto | null>(null);

    const canManage = hasPermission("members.manage");
    const me = members?.find((m) => m.userId === session.user.id);
    const callerIsOwner = me?.roleSystemKey === "owner";

    const load = useCallback(async () => {
        const [loadedMembers, loadedRoles] = await Promise.all([identity.getMembers(), identity.roles.list()]);
        setMembers(loadedMembers);
        setRoles(loadedRoles);
    }, []);

    useEffect(() => {
        Promise.all([identity.getMembers(), identity.roles.list()]).then(
            ([loadedMembers, loadedRoles]) => {
                setMembers(loadedMembers);
                setRoles(loadedRoles);
            },
            (e: unknown) => {
                setMembers([]);
                if (e instanceof ApiError) setError(e);
            },
        );
    }, []);

    const changeRole = async (target: MemberDto, roleId: string) => {
        setError(null);
        try {
            await identity.changeMemberRole(target.userId, roleId);
            await load();
            if (target.userId === session.user.id) await refresh();
        } catch (e) {
            if (!(e instanceof ApiError)) throw e;
            setError(e);
        }
    };

    const remove = async () => {
        if (!removing) return;
        const target = removing;
        setRemoving(null);
        setError(null);
        try {
            await identity.removeMember(target.userId);
            setMembers((list) => list?.filter((m) => m.userId !== target.userId) ?? null);
        } catch (e) {
            if (!(e instanceof ApiError)) throw e;
            setError(e);
        }
    };

    const assignableRoles = roles.filter((r) => r.systemKey !== "owner" || callerIsOwner);

    return (
        <DashboardCard title="Üyeler" subtitle={members ? `${members.length} kişi` : undefined}>
            <FormAlert error={error} />
            {members === null ? (
                <Skeleton variant="rounded" height={140} />
            ) : (
                <TableContainer sx={{ mx: -1 }}>
                    <Table size="small" sx={{ minWidth: 620, "& td, & th": { px: 1, py: 1.5 } }}>
                        <TableHead>
                            <TableRow>
                                <TableCell>Ad soyad</TableCell>
                                <TableCell>Rol</TableCell>
                                <TableCell>Katılma</TableCell>
                                <TableCell align="right" />
                            </TableRow>
                        </TableHead>
                        <TableBody>
                            {members.map((m) => {
                                const isMe = m.userId === session.user.id;
                                const touchesOwner = m.roleSystemKey === "owner";
                                const editable = canManage && (!touchesOwner || callerIsOwner);
                                return (
                                    <TableRow key={m.userId} hover>
                                        <TableCell>
                                            <Box sx={{ display: "flex", alignItems: "center", gap: 1.5 }}>
                                                <Avatar sx={{ width: 36, height: 36, fontSize: "0.8rem", bgcolor: "primary.light", color: "primary.main" }}>
                                                    {initials(m.fullName)}
                                                </Avatar>
                                                <Box sx={{ minWidth: 0 }}>
                                                    <Typography variant="subtitle2" color="text.primary" sx={{ fontWeight: 600 }}>
                                                        {m.fullName}
                                                        {isMe && " (sen)"}
                                                    </Typography>
                                                    <Typography variant="body2" color="text.secondary">
                                                        {m.email}
                                                    </Typography>
                                                </Box>
                                            </Box>
                                        </TableCell>
                                        <TableCell>
                                            {editable ? (
                                                <Select
                                                    size="small"
                                                    value={m.roleId}
                                                    onChange={(e) => void changeRole(m, e.target.value)}
                                                    inputProps={{ "aria-label": `${m.fullName} rolü` }}
                                                    sx={{ minWidth: 140 }}
                                                >
                                                    {assignableRoles.map((r) => (
                                                        <MenuItem key={r.id} value={r.id}>
                                                            {r.name}
                                                        </MenuItem>
                                                    ))}
                                                    {/* Seçili rol listede yoksa (ör. sahip olmayan için Sahip) gizli seçenek */}
                                                    {!assignableRoles.some((r) => r.id === m.roleId) && (
                                                        <MenuItem value={m.roleId} sx={{ display: "none" }}>
                                                            {m.roleName}
                                                        </MenuItem>
                                                    )}
                                                </Select>
                                            ) : (
                                                <Typography variant="subtitle2">{m.roleName}</Typography>
                                            )}
                                        </TableCell>
                                        <TableCell>{formatDate(m.joinedAt)}</TableCell>
                                        <TableCell align="right">
                                            {editable && (
                                                <Button size="small" onClick={() => setBranchesFor(m)}>
                                                    Şube erişimi
                                                </Button>
                                            )}
                                            {editable && !isMe && (
                                                <Button size="small" color="error" onClick={() => setRemoving(m)}>
                                                    Çıkar
                                                </Button>
                                            )}
                                        </TableCell>
                                    </TableRow>
                                );
                            })}
                        </TableBody>
                    </Table>
                </TableContainer>
            )}

            {branchesFor && roles.find((r) => r.id === branchesFor.roleId) && (
                <MemberBranchesDialog
                    member={branchesFor}
                    role={roles.find((r) => r.id === branchesFor.roleId)!}
                    onClose={() => setBranchesFor(null)}
                    onSaved={() => {
                        setBranchesFor(null);
                        void load();
                    }}
                />
            )}

            <Dialog open={Boolean(removing)} onClose={() => setRemoving(null)}>
                <DialogTitle>Üye firmadan çıkarılsın mı?</DialogTitle>
                <DialogContent>
                    <DialogContentText>
                        {removing?.fullName} bu firmadaki verilere artık erişemeyecek. Açık oturumları da firmadan çıkarılır.
                    </DialogContentText>
                </DialogContent>
                <DialogActions>
                    <Button onClick={() => setRemoving(null)}>Vazgeç</Button>
                    <Button color="error" variant="contained" onClick={() => void remove()}>
                        Çıkar
                    </Button>
                </DialogActions>
            </Dialog>
        </DashboardCard>
    );
}
