"use client";

import { useEffect, useState } from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import Box from "@mui/material/Box";
import Button from "@mui/material/Button";
import Card from "@mui/material/Card";
import CardContent from "@mui/material/CardContent";
import Stack from "@mui/material/Stack";
import TextField from "@mui/material/TextField";
import Typography from "@mui/material/Typography";
import { IconBuildingPlus, IconMail } from "@tabler/icons-react";
import FormAlert from "@/components/auth/FormAlert";
import { ApiError } from "@/lib/api/client";
import { identity, type MyInvitationDto } from "@/lib/api/identity";
import { formatDate } from "@/lib/format";
import { validateCompanyName } from "@/lib/validation";
import { useSession } from "@/session/SessionProvider";

/** Firmasız kullanıcının ilk ekranı: bekleyen bir daveti kabul et ya da kendi firmanı oluştur. */
export default function Onboarding() {
    const { session, logout } = useSession();
    const router = useRouter();
    const [invitations, setInvitations] = useState<MyInvitationDto[] | null>(null);
    const [error, setError] = useState<ApiError | null>(null);
    const [busy, setBusy] = useState(false);

    useEffect(() => {
        identity.getMyInvitations().then(setInvitations, () => setInvitations([]));
    }, []);

    const accept = async (id: string) => {
        setBusy(true);
        setError(null);
        try {
            await identity.acceptMyInvitation(id);
            router.replace("/panel");
        } catch (e) {
            setBusy(false);
            if (!(e instanceof ApiError)) throw e;
            setError(e);
        }
    };

    return (
        <Stack spacing={3}>
            <Box>
                <Typography variant="h3" component="h1">
                    Hoş geldin, {session.user.fullName}
                </Typography>
                <Typography color="text.secondary" sx={{ mt: 1 }}>
                    Modaryo&apos;yu kullanmaya başlamak için bir firmaya katıl ya da kendi firmanı oluştur.
                </Typography>
                {session.activeCompany && (
                    <Button component={Link} href="/panel" sx={{ mt: 1, px: 0 }}>
                        Panele dön
                    </Button>
                )}
            </Box>

            <FormAlert error={error} />

            {invitations && invitations.length > 0 && (
                <Card>
                    <CardContent>
                        <Box sx={{ display: "flex", alignItems: "center", gap: 1, mb: 2, color: "primary.main" }}>
                            <IconMail size={22} />
                            <Typography variant="h5" component="h2" color="text.primary">
                                Bekleyen davetler
                            </Typography>
                        </Box>
                        <Stack spacing={2}>
                            {invitations.map((invitation) => (
                                <Box
                                    key={invitation.id}
                                    sx={{ display: "flex", alignItems: "center", gap: 2, p: 2, borderRadius: "7px", bgcolor: "primary.light" }}
                                >
                                    <Box sx={{ flexGrow: 1, minWidth: 0 }}>
                                        <Typography variant="h6" noWrap>
                                            {invitation.companyName}
                                        </Typography>
                                        <Typography variant="body2" color="text.secondary">
                                            {invitation.invitedBy} davet etti · Rol: {invitation.roleName} · Son gün: {formatDate(invitation.expiresAt)}
                                        </Typography>
                                    </Box>
                                    <Button variant="contained" onClick={() => accept(invitation.id)} disabled={busy}>
                                        Katıl
                                    </Button>
                                </Box>
                            ))}
                        </Stack>
                    </CardContent>
                </Card>
            )}

            <CreateCompanyCard onCreated={() => router.replace("/panel")} />

            <Box sx={{ textAlign: "center" }}>
                <Button color="inherit" onClick={() => void logout()}>
                    Çıkış yap
                </Button>
            </Box>
        </Stack>
    );
}

function CreateCompanyCard({ onCreated }: { onCreated: () => void }) {
    const [name, setName] = useState("");
    const [fieldError, setFieldError] = useState<string>();
    const [error, setError] = useState<ApiError | null>(null);
    const [busy, setBusy] = useState(false);

    const onSubmit = async (event: React.FormEvent) => {
        event.preventDefault();
        if (busy) return;

        const validation = validateCompanyName(name);
        setFieldError(validation);
        setError(null);
        if (validation) return;

        setBusy(true);
        try {
            await identity.createCompany(name.trim());
            onCreated();
        } catch (e) {
            setBusy(false);
            if (!(e instanceof ApiError)) throw e;
            setError(e);
            setFieldError(e.fieldErrors.name);
        }
    };

    return (
        <Card>
            <CardContent>
                <Box sx={{ display: "flex", alignItems: "center", gap: 1, mb: 1, color: "primary.main" }}>
                    <IconBuildingPlus size={22} />
                    <Typography variant="h5" component="h2" color="text.primary">
                        Firma oluştur
                    </Typography>
                </Box>
                <Typography color="text.secondary" sx={{ mb: 2 }}>
                    Firmanın sahibi sen olursun; çalışma arkadaşlarını sonra davet edebilirsin.
                </Typography>
                <FormAlert error={error && Object.keys(error.fieldErrors).length === 0 ? error : null} />
                <Box component="form" noValidate onSubmit={onSubmit}>
                    <Stack direction={{ xs: "column", sm: "row" }} spacing={1.5} sx={{ alignItems: { sm: "flex-start" } }}>
                        <TextField
                            id="companyName"
                            label="Firma adı"
                            fullWidth
                            value={name}
                            onChange={(e) => setName(e.target.value)}
                            error={Boolean(fieldError)}
                            helperText={fieldError}
                        />
                        <Button type="submit" variant="contained" size="large" disabled={busy} sx={{ flexShrink: 0, height: 56 }}>
                            Oluştur
                        </Button>
                    </Stack>
                </Box>
            </CardContent>
        </Card>
    );
}
