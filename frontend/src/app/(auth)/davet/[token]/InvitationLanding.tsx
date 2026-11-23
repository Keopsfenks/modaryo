"use client";

import { useEffect, useState } from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import Box from "@mui/material/Box";
import Button from "@mui/material/Button";
import CircularProgress from "@mui/material/CircularProgress";
import Stack from "@mui/material/Stack";
import TextField from "@mui/material/TextField";
import Typography from "@mui/material/Typography";
import AuthCard from "@/components/auth/AuthCard";
import FormAlert from "@/components/auth/FormAlert";
import PasswordField from "@/components/auth/PasswordField";
import { ApiError } from "@/lib/api/client";
import { identity, type InvitationPreview, type SessionInfo } from "@/lib/api/identity";
import { formatDate } from "@/lib/format";
import { loginUrl } from "@/lib/navigation";
import { collectErrors, validateFullName, validateNewPassword, type FieldErrors } from "@/lib/validation";

type State =
    | { kind: "loading" }
    | { kind: "invalid" }
    | { kind: "ready"; preview: InvitationPreview; session: SessionInfo | null };

const sameEmail = (a: string, b: string) => a.trim().toLowerCase() === b.trim().toLowerCase();

/**
 * Davet bağlantısı (/davet/{token}). Oturum durumuna göre: katıl / yanlış hesap / giriş yap / kayıt ol.
 */
export default function InvitationLanding({ token }: { token: string }) {
    const [state, setState] = useState<State>({ kind: "loading" });

    useEffect(() => {
        let cancelled = false;
        Promise.all([
            identity.getInvitationPreview(token),
            identity.getSession().catch((e: unknown) => {
                if (e instanceof ApiError && e.status === 401) return null;
                throw e;
            }),
        ]).then(
            ([preview, session]) => !cancelled && setState({ kind: "ready", preview, session }),
            () => !cancelled && setState({ kind: "invalid" }),
        );
        return () => {
            cancelled = true;
        };
    }, [token]);

    if (state.kind === "loading") {
        return (
            <AuthCard title="Davet">
                <Box sx={{ display: "flex", alignItems: "center", gap: 2 }}>
                    <CircularProgress size={24} />
                    <Typography color="text.secondary">Davet yükleniyor…</Typography>
                </Box>
            </AuthCard>
        );
    }

    if (state.kind === "invalid") {
        return (
            <AuthCard title="Davet bulunamadı">
                <FormAlert severity="warning" error="Bu davet geçersiz, kullanılmış veya süresi dolmuş. Davet eden kişiden yeni bir davet isteyebilirsiniz." />
                <Button component={Link} href="/giris" variant="contained">
                    Giriş sayfasına git
                </Button>
            </AuthCard>
        );
    }

    const { preview, session } = state;
    const subtitle = (
        <>
            <strong>{preview.invitedBy}</strong>, sizi <strong>{preview.companyName}</strong> firmasına davet etti. Davet{" "}
            {formatDate(preview.expiresAt)} tarihine kadar geçerli.
        </>
    );
    const loginHref = loginUrl(`/davet/${token}`);

    if (session && sameEmail(session.user.email, preview.email)) {
        return <AcceptCard token={token} preview={preview} subtitle={subtitle} />;
    }

    if (session) {
        return <WrongAccountCard preview={preview} subtitle={subtitle} loginHref={loginHref} />;
    }

    if (preview.accountExists) {
        return (
            <AuthCard title={`${preview.companyName} daveti`} subtitle={subtitle}>
                <Typography sx={{ mb: 2 }}>
                    <strong>{preview.email}</strong> adresiyle bir hesabınız var. Daveti kabul etmek için giriş yapın.
                </Typography>
                <Button component={Link} href={loginHref} variant="contained" fullWidth>
                    Giriş yap
                </Button>
            </AuthCard>
        );
    }

    return <RegisterWithInvitation token={token} preview={preview} subtitle={subtitle} loginHref={loginHref} />;
}

function AcceptCard({ token, preview, subtitle }: { token: string; preview: InvitationPreview; subtitle: React.ReactNode }) {
    const router = useRouter();
    const [error, setError] = useState<ApiError | null>(null);
    const [busy, setBusy] = useState(false);

    const accept = async () => {
        setBusy(true);
        setError(null);
        try {
            await identity.acceptInvitation(token);
            router.replace("/panel");
        } catch (e) {
            setBusy(false);
            if (!(e instanceof ApiError)) throw e;
            setError(e);
        }
    };

    return (
        <AuthCard title={`${preview.companyName} daveti`} subtitle={subtitle}>
            <FormAlert error={error} />
            <Button variant="contained" size="large" fullWidth onClick={accept} disabled={busy}>
                {preview.companyName} firmasına katıl
            </Button>
        </AuthCard>
    );
}

function WrongAccountCard({ preview, subtitle, loginHref }: { preview: InvitationPreview; subtitle: React.ReactNode; loginHref: string }) {
    const router = useRouter();
    const [busy, setBusy] = useState(false);

    const switchAccount = async () => {
        setBusy(true);
        await identity.logout().catch(() => undefined);
        router.replace(loginHref);
    };

    return (
        <AuthCard title={`${preview.companyName} daveti`} subtitle={subtitle}>
            <FormAlert severity="warning" error={`Bu davet ${preview.email} adresine gönderildi. Şu an başka bir hesapla oturum açmış durumdasınız.`} />
            <Stack spacing={1.5}>
                <Button variant="contained" onClick={switchAccount} disabled={busy}>
                    Çıkış yap ve o hesapla gir
                </Button>
                <Button component={Link} href="/panel">
                    Panele dön
                </Button>
            </Stack>
        </AuthCard>
    );
}

function RegisterWithInvitation({
    token,
    preview,
    subtitle,
    loginHref,
}: {
    token: string;
    preview: InvitationPreview;
    subtitle: React.ReactNode;
    loginHref: string;
}) {
    const router = useRouter();
    const [fullName, setFullName] = useState("");
    const [password, setPassword] = useState("");
    const [fieldErrors, setFieldErrors] = useState<FieldErrors>({});
    const [error, setError] = useState<ApiError | null>(null);
    const [busy, setBusy] = useState(false);

    const onSubmit = async (event: React.FormEvent) => {
        event.preventDefault();
        if (busy) return;

        const errors = collectErrors({ fullName: validateFullName(fullName), password: validateNewPassword(password) });
        setFieldErrors(errors);
        setError(null);
        if (Object.keys(errors).length > 0) return;

        setBusy(true);
        try {
            await identity.register({ email: preview.email, fullName: fullName.trim(), password, inviteToken: token });
            router.replace("/panel");
        } catch (e) {
            setBusy(false);
            if (!(e instanceof ApiError)) throw e;
            setError(e);
            setFieldErrors(e.fieldErrors);
        }
    };

    return (
        <AuthCard title={`${preview.companyName} daveti`} subtitle={subtitle}>
            <FormAlert error={error && Object.keys(error.fieldErrors).length === 0 ? error : null}>
                {error?.code === "account_exists" && (
                    <Box sx={{ mt: 1 }}>
                        <Button component={Link} href={loginHref} size="small" variant="outlined" color="error">
                            Giriş yap
                        </Button>
                    </Box>
                )}
            </FormAlert>
            <Box component="form" noValidate onSubmit={onSubmit}>
                <Stack spacing={2.5}>
                    <TextField id="email" label="E-posta" fullWidth value={preview.email} slotProps={{ htmlInput: { readOnly: true } }} />
                    <TextField
                        id="fullName"
                        label="Ad soyad"
                        autoComplete="name"
                        autoFocus
                        fullWidth
                        value={fullName}
                        onChange={(e) => setFullName(e.target.value)}
                        error={Boolean(fieldErrors.fullName)}
                        helperText={fieldErrors.fullName}
                    />
                    <PasswordField
                        id="password"
                        label="Şifre"
                        autoComplete="new-password"
                        fullWidth
                        value={password}
                        onChange={(e) => setPassword(e.target.value)}
                        error={Boolean(fieldErrors.password)}
                        helperText={fieldErrors.password ?? "En az 10 karakter."}
                    />
                    <Button type="submit" variant="contained" size="large" fullWidth disabled={busy}>
                        Hesabı oluştur ve katıl
                    </Button>
                </Stack>
            </Box>
        </AuthCard>
    );
}
