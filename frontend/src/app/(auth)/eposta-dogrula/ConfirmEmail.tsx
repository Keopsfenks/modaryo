"use client";

import { useEffect, useRef, useState } from "react";
import Link from "next/link";
import { useRouter, useSearchParams } from "next/navigation";
import Box from "@mui/material/Box";
import Button from "@mui/material/Button";
import CircularProgress from "@mui/material/CircularProgress";
import Stack from "@mui/material/Stack";
import TextField from "@mui/material/TextField";
import Typography from "@mui/material/Typography";
import AuthCard from "@/components/auth/AuthCard";
import FormAlert from "@/components/auth/FormAlert";
import { ApiError } from "@/lib/api/client";
import { identity } from "@/lib/api/identity";
import { afterSignInPath } from "@/lib/navigation";
import { validateEmail } from "@/lib/validation";

const INVALID_LINK = "Doğrulama bağlantısı geçersiz veya süresi dolmuş. Aşağıdan yeni bir bağlantı isteyebilirsiniz.";

/** E-postadaki bağlantıyı (?u=&t=) bir kez doğrular; başarılıysa oturum açılmıştır. */
export default function ConfirmEmail() {
    const router = useRouter();
    const searchParams = useSearchParams();
    const started = useRef(false);
    const [requestFailed, setRequestFailed] = useState(false);

    const userId = searchParams.get("u");
    const token = searchParams.get("t");
    const failed = !userId || !token || requestFailed;

    useEffect(() => {
        // StrictMode'da effect iki kez çalışır; doğrulama kodu tek kullanımlık değil ama istek bir kez gitsin.
        if (started.current || !userId || !token) return;
        started.current = true;

        identity
            .confirmEmail({ userId, token })
            .then((session) => router.replace(afterSignInPath(session, "/")))
            .catch(() => setRequestFailed(true));
    }, [router, userId, token]);

    if (!failed) {
        return (
            <AuthCard title="E-posta doğrulanıyor">
                <Box sx={{ display: "flex", alignItems: "center", gap: 2 }}>
                    <CircularProgress size={24} />
                    <Typography color="text.secondary">Lütfen bekleyin…</Typography>
                </Box>
            </AuthCard>
        );
    }

    return <ResendLink />;
}

function ResendLink() {
    const [email, setEmail] = useState("");
    const [fieldError, setFieldError] = useState<string>();
    const [error, setError] = useState<ApiError | null>(null);
    const [state, setState] = useState<"idle" | "sending" | "sent">("idle");

    const onSubmit = async (event: React.FormEvent) => {
        event.preventDefault();
        const validation = validateEmail(email);
        setFieldError(validation);
        if (validation) return;

        setState("sending");
        setError(null);
        try {
            await identity.resendConfirmation(email.trim());
            setState("sent");
        } catch (e) {
            setState("idle");
            if (e instanceof ApiError) setError(e);
        }
    };

    return (
        <AuthCard title="Bağlantı geçersiz">
            <FormAlert error={INVALID_LINK} severity="warning" />
            <FormAlert error={error} />
            {state === "sent" ? (
                <FormAlert severity="success" error="Hesap doğrulanmamışsa bu adrese yeni bir doğrulama bağlantısı gönderdik." />
            ) : (
                <Box component="form" noValidate onSubmit={onSubmit}>
                    <Stack spacing={2}>
                        <TextField
                            id="email"
                            label="E-posta"
                            type="email"
                            autoComplete="email"
                            fullWidth
                            value={email}
                            onChange={(e) => setEmail(e.target.value)}
                            error={Boolean(fieldError)}
                            helperText={fieldError}
                        />
                        <Button type="submit" variant="contained" disabled={state === "sending"}>
                            Yeni bağlantı gönder
                        </Button>
                    </Stack>
                </Box>
            )}
            <Box sx={{ mt: 2 }}>
                <Button component={Link} href="/giris">
                    Giriş sayfasına dön
                </Button>
            </Box>
        </AuthCard>
    );
}
