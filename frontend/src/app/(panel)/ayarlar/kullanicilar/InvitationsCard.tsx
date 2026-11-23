"use client";

import { useEffect, useState } from "react";
import Box from "@mui/material/Box";
import Button from "@mui/material/Button";
import MenuItem from "@mui/material/MenuItem";
import Stack from "@mui/material/Stack";
import Table from "@mui/material/Table";
import TableBody from "@mui/material/TableBody";
import TableCell from "@mui/material/TableCell";
import TableContainer from "@mui/material/TableContainer";
import TableRow from "@mui/material/TableRow";
import TextField from "@mui/material/TextField";
import Typography from "@mui/material/Typography";
import FormAlert from "@/components/auth/FormAlert";
import DashboardCard from "@/components/dashboard/DashboardCard";
import { ApiError } from "@/lib/api/client";
import { identity, type InvitationDto, type RoleDto } from "@/lib/api/identity";
import { formatDate } from "@/lib/format";
import { validateEmail } from "@/lib/validation";
import { useSession } from "@/session/SessionProvider";

/** Davet gönderme ve bekleyen davetler. Yalnızca members.invite izni olanlara görünür. */
export default function InvitationsCard() {
    const { hasPermission } = useSession();
    if (!hasPermission("members.invite")) {
        return null;
    }
    return <InvitationsCardContent />;
}

function InvitationsCardContent() {
    const [invitations, setInvitations] = useState<InvitationDto[]>([]);
    const [roles, setRoles] = useState<RoleDto[]>([]);
    const [email, setEmail] = useState("");
    const [roleId, setRoleId] = useState("");
    const [fieldError, setFieldError] = useState<string>();
    const [error, setError] = useState<ApiError | null>(null);
    const [notice, setNotice] = useState<string | null>(null);
    const [busy, setBusy] = useState(false);

    useEffect(() => {
        Promise.all([identity.listInvitations(), identity.getRoles()]).then(([loadedInvitations, loadedRoles]) => {
            setInvitations(loadedInvitations);
            setRoles(loadedRoles);
            setRoleId(loadedRoles.find((r) => r.systemKey === "member")?.id ?? loadedRoles[0]?.id ?? "");
        }, (e: unknown) => e instanceof ApiError && setError(e));
    }, []);

    const onSubmit = async (event: React.FormEvent) => {
        event.preventDefault();
        if (busy) return;

        const validation = validateEmail(email);
        setFieldError(validation);
        setError(null);
        setNotice(null);
        if (validation) return;

        setBusy(true);
        try {
            const created = await identity.createInvitation(email.trim(), roleId);
            setInvitations((list) => [created, ...list.filter((i) => i.id !== created.id)]);
            setNotice(`${created.email} adresine davet gönderildi.`);
            setEmail("");
        } catch (e) {
            if (!(e instanceof ApiError)) throw e;
            if (e.code === "already_member" || e.fieldErrors.email) {
                setFieldError(e.fieldErrors.email ?? e.message);
            } else {
                setError(e);
            }
        } finally {
            setBusy(false);
        }
    };

    const act = async (action: () => Promise<void>, onDone: () => void) => {
        setError(null);
        setNotice(null);
        try {
            await action();
            onDone();
        } catch (e) {
            if (!(e instanceof ApiError)) throw e;
            setError(e);
        }
    };

    return (
        <DashboardCard title="Davetler" subtitle="Davet edilen kişi e-postadaki bağlantıyla firmaya katılır. Davet 7 gün geçerlidir.">
            <FormAlert error={error} />
            {notice && <FormAlert severity="success" error={notice} />}

            <Box component="form" noValidate onSubmit={onSubmit} sx={{ mb: 3 }}>
                <Stack direction={{ xs: "column", sm: "row" }} spacing={1.5} sx={{ alignItems: { sm: "flex-start" } }}>
                    <TextField
                        id="inviteEmail"
                        label="E-posta"
                        type="email"
                        fullWidth
                        value={email}
                        onChange={(e) => setEmail(e.target.value)}
                        error={Boolean(fieldError)}
                        helperText={fieldError}
                    />
                    <TextField
                        id="inviteRole"
                        select
                        label="Rol"
                        value={roleId}
                        onChange={(e) => setRoleId(e.target.value)}
                        sx={{ minWidth: 160 }}
                    >
                        {roles.map((r) => (
                            <MenuItem key={r.id} value={r.id}>
                                {r.name}
                            </MenuItem>
                        ))}
                    </TextField>
                    <Button type="submit" variant="contained" size="large" disabled={busy || !roleId} sx={{ flexShrink: 0, height: 56 }}>
                        Davet gönder
                    </Button>
                </Stack>
            </Box>

            {invitations.length === 0 ? (
                <Typography color="text.secondary">Bekleyen davet yok.</Typography>
            ) : (
                <TableContainer sx={{ mx: -1 }}>
                    <Table size="small" sx={{ minWidth: 560, "& td": { px: 1, py: 1.5 } }}>
                        <TableBody>
                            {invitations.map((invitation) => (
                                <TableRow key={invitation.id}>
                                    <TableCell>
                                        <Typography variant="subtitle2" color="text.primary" sx={{ fontWeight: 600 }}>
                                            {invitation.email}
                                        </Typography>
                                        <Typography variant="body2" color="text.secondary">
                                            {invitation.roleName} · {invitation.invitedBy} davet etti
                                        </Typography>
                                    </TableCell>
                                    <TableCell>Son gün: {formatDate(invitation.expiresAt)}</TableCell>
                                    <TableCell align="right" sx={{ whiteSpace: "nowrap" }}>
                                        <Button
                                            size="small"
                                            onClick={() => act(() => identity.resendInvitation(invitation.id), () => setNotice(`${invitation.email} adresine davet yeniden gönderildi.`))}
                                        >
                                            Yeniden gönder
                                        </Button>
                                        <Button
                                            size="small"
                                            color="error"
                                            onClick={() =>
                                                act(() => identity.revokeInvitation(invitation.id), () =>
                                                    setInvitations((list) => list.filter((i) => i.id !== invitation.id)))
                                            }
                                        >
                                            İptal
                                        </Button>
                                    </TableCell>
                                </TableRow>
                            ))}
                        </TableBody>
                    </Table>
                </TableContainer>
            )}
        </DashboardCard>
    );
}
