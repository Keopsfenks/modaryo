"use client";

import { useState } from "react";
import Link from "next/link";
import { useRouter, useSearchParams } from "next/navigation";
import Box from "@mui/material/Box";
import Button from "@mui/material/Button";
import Checkbox from "@mui/material/Checkbox";
import FormControlLabel from "@mui/material/FormControlLabel";
import Stack from "@mui/material/Stack";
import TextField from "@mui/material/TextField";
import Typography from "@mui/material/Typography";
import AuthCard from "@/components/auth/AuthCard";
import FormAlert from "@/components/auth/FormAlert";
import PasswordField from "@/components/auth/PasswordField";
import { ApiError } from "@/lib/api/client";
import { identity } from "@/lib/api/identity";
import { afterSignInPath } from "@/lib/navigation";
import { collectErrors, required, type FieldErrors } from "@/lib/validation";

export default function LoginForm() {
    const router = useRouter();
    const searchParams = useSearchParams();

    const [email, setEmail] = useState("");
    const [password, setPassword] = useState("");
    const [rememberMe, setRememberMe] = useState(false);
    const [fieldErrors, setFieldErrors] = useState<FieldErrors>({});
    const [error, setError] = useState<ApiError | null>(null);
    const [submitting, setSubmitting] = useState(false);
    const [resendState, setResendState] = useState<"idle" | "sending" | "sent">("idle");

    const onSubmit = async (event: React.FormEvent) => {
        event.preventDefault();
        if (submitting) return;

        const errors = collectErrors({
            email: required(email, "E-posta adresi gereklidir."),
            password: required(password, "Şifre gereklidir."),
        });
        setFieldErrors(errors);
        setError(null);
        setResendState("idle");
        if (Object.keys(errors).length > 0) return;

        setSubmitting(true);
        try {
            const session = await identity.login({ email, password, rememberMe });
            router.replace(afterSignInPath(session, searchParams.get("next")));
        } catch (e) {
            if (e instanceof ApiError) {
                setError(e);
                setFieldErrors(e.fieldErrors);
            } else {
                throw e;
            }
            setSubmitting(false);
        }
    };

    const resend = async () => {
        setResendState("sending");
        try {
            await identity.resendConfirmation(email);
            setResendState("sent");
        } catch (e) {
            setResendState("idle");
            if (e instanceof ApiError) setError(e);
        }
    };

    return (
        <AuthCard
            title="Giriş yap"
            subtitle="Modaryo hesabınıza giriş yapın."
            footer={
                <Typography variant="subtitle1" color="text.secondary">
                    Hesabın yok mu?{" "}
                    <Typography component={Link} href="/kayit" color="primary" sx={{ fontWeight: 600, textDecoration: "none" }}>
                        Kayıt ol
                    </Typography>
                </Typography>
            }
        >
            {resendState === "sent" ? (
                <FormAlert severity="success" error="Doğrulama e-postası gönderildi. Gelen kutunuzu kontrol edin." />
            ) : (
                <FormAlert error={error && Object.keys(error.fieldErrors).length === 0 ? error : null}>
                    {error?.code === "email_not_confirmed" && (
                        <Box sx={{ mt: 1 }}>
                            <Button size="small" variant="outlined" color="error" onClick={resend} disabled={resendState === "sending"}>
                                Doğrulama e-postasını yeniden gönder
                            </Button>
                        </Box>
                    )}
                </FormAlert>
            )}

            <Box component="form" noValidate onSubmit={onSubmit}>
                <Stack spacing={2.5}>
                    <TextField
                        id="email"
                        label="E-posta"
                        type="email"
                        autoComplete="email"
                        autoFocus
                        fullWidth
                        value={email}
                        onChange={(e) => setEmail(e.target.value)}
                        error={Boolean(fieldErrors.email)}
                        helperText={fieldErrors.email}
                    />
                    <PasswordField
                        id="password"
                        label="Şifre"
                        autoComplete="current-password"
                        fullWidth
                        value={password}
                        onChange={(e) => setPassword(e.target.value)}
                        error={Boolean(fieldErrors.password)}
                        helperText={fieldErrors.password}
                    />
                    <Box sx={{ display: "flex", alignItems: "center", justifyContent: "space-between" }}>
                        <FormControlLabel
                            control={<Checkbox checked={rememberMe} onChange={(e) => setRememberMe(e.target.checked)} />}
                            label="Beni hatırla"
                        />
                        <Typography component={Link} href="/sifremi-unuttum" variant="subtitle2" color="primary" sx={{ fontWeight: 600, textDecoration: "none" }}>
                            Şifremi unuttum
                        </Typography>
                    </Box>
                    <Button type="submit" variant="contained" size="large" fullWidth disabled={submitting}>
                        Giriş yap
                    </Button>
                </Stack>
            </Box>
        </AuthCard>
    );
}
