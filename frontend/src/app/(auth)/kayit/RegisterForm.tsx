"use client";

import { useRef, useState } from "react";
import Link from "next/link";
import Box from "@mui/material/Box";
import Button from "@mui/material/Button";
import Stack from "@mui/material/Stack";
import TextField from "@mui/material/TextField";
import Typography from "@mui/material/Typography";
import { IconMailCheck } from "@tabler/icons-react";
import AuthCard from "@/components/auth/AuthCard";
import FormAlert from "@/components/auth/FormAlert";
import PasswordField from "@/components/auth/PasswordField";
import { ApiError } from "@/lib/api/client";
import { identity } from "@/lib/api/identity";
import { collectErrors, validateEmail, validateFullName, validateNewPassword, type FieldErrors } from "@/lib/validation";

export default function RegisterForm() {
    const [fullName, setFullName] = useState("");
    const [email, setEmail] = useState("");
    const [password, setPassword] = useState("");
    const [fieldErrors, setFieldErrors] = useState<FieldErrors>({});
    const [error, setError] = useState<ApiError | null>(null);
    const [submitting, setSubmitting] = useState(false);
    const [sentTo, setSentTo] = useState<string | null>(null);
    const [resend, setResend] = useState<"idle" | "sending" | "sent">("idle");
    const inFlight = useRef(false);

    const onSubmit = async (event: React.FormEvent) => {
        event.preventDefault();
        if (inFlight.current) return;

        const errors = collectErrors({
            fullName: validateFullName(fullName),
            email: validateEmail(email),
            password: validateNewPassword(password),
        });
        setFieldErrors(errors);
        setError(null);
        if (Object.keys(errors).length > 0) return;

        inFlight.current = true;
        setSubmitting(true);
        try {
            await identity.register({ fullName: fullName.trim(), email: email.trim(), password });
            setSentTo(email.trim());
        } catch (e) {
            if (!(e instanceof ApiError)) throw e;
            setError(e);
            setFieldErrors(e.fieldErrors);
        } finally {
            inFlight.current = false;
            setSubmitting(false);
        }
    };

    const onResend = async () => {
        if (!sentTo) return;
        setResend("sending");
        try {
            await identity.resendConfirmation(sentTo);
            setResend("sent");
        } catch (e) {
            setResend("idle");
            if (e instanceof ApiError) setError(e);
        }
    };

    if (sentTo) {
        return (
            <AuthCard title="E-postanızı kontrol edin">
                <Box sx={{ textAlign: "center", color: "primary.main", mb: 2 }}>
                    <IconMailCheck size={56} stroke={1.3} />
                </Box>
                <Typography sx={{ mb: 2 }}>
                    <strong>{sentTo}</strong> adresine bir doğrulama bağlantısı gönderdik. Hesabınızı etkinleştirmek için bağlantıya tıklayın.
                </Typography>
                <FormAlert error={error} />
                {resend === "sent" && <FormAlert severity="success" error="Doğrulama e-postası yeniden gönderildi." />}
                <Stack direction={{ xs: "column", sm: "row" }} spacing={1.5}>
                    <Button variant="outlined" onClick={onResend} disabled={resend === "sending"}>
                        Yeniden gönder
                    </Button>
                    <Button component={Link} href="/giris">
                        Giriş sayfasına dön
                    </Button>
                </Stack>
            </AuthCard>
        );
    }

    return (
        <AuthCard
            title="Kayıt ol"
            subtitle="Ücretsiz hesabınızı oluşturun; firmanızı kayıttan sonra ekleyeceksiniz."
            footer={
                <Typography variant="subtitle1" color="text.secondary">
                    Zaten hesabın var mı?{" "}
                    <Typography component={Link} href="/giris" color="primary" sx={{ fontWeight: 600, textDecoration: "none" }}>
                        Giriş yap
                    </Typography>
                </Typography>
            }
        >
            <FormAlert error={error && Object.keys(error.fieldErrors).length === 0 ? error : null} />
            <Box component="form" noValidate onSubmit={onSubmit}>
                <Stack spacing={2.5}>
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
                    <TextField
                        id="email"
                        label="E-posta"
                        type="email"
                        autoComplete="email"
                        fullWidth
                        value={email}
                        onChange={(e) => setEmail(e.target.value)}
                        error={Boolean(fieldErrors.email)}
                        helperText={fieldErrors.email}
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
                    <Button type="submit" variant="contained" size="large" fullWidth disabled={submitting}>
                        Kayıt ol
                    </Button>
                </Stack>
            </Box>
        </AuthCard>
    );
}
