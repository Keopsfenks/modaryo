"use client";

import { useState } from "react";
import Link from "next/link";
import { useSearchParams } from "next/navigation";
import Box from "@mui/material/Box";
import Button from "@mui/material/Button";
import Stack from "@mui/material/Stack";
import AuthCard from "@/components/auth/AuthCard";
import FormAlert from "@/components/auth/FormAlert";
import PasswordField from "@/components/auth/PasswordField";
import { ApiError } from "@/lib/api/client";
import { identity } from "@/lib/api/identity";
import { collectErrors, validateNewPassword, type FieldErrors } from "@/lib/validation";

/** E-postadaki bağlantıyla (?e=&t=) yeni şifre belirler. Başarılıysa tüm oturumlar kapanır. */
export default function ResetPasswordForm() {
    const searchParams = useSearchParams();
    const email = searchParams.get("e");
    const token = searchParams.get("t");

    const [password, setPassword] = useState("");
    const [repeat, setRepeat] = useState("");
    const [fieldErrors, setFieldErrors] = useState<FieldErrors>({});
    const [error, setError] = useState<ApiError | null>(null);
    const [state, setState] = useState<"idle" | "sending" | "done">("idle");

    if (!email || !token) {
        return (
            <AuthCard title="Bağlantı geçersiz">
                <FormAlert severity="warning" error="Bu şifre sıfırlama bağlantısı eksik veya bozuk." />
                <Button component={Link} href="/sifremi-unuttum" variant="contained">
                    Yeni bağlantı iste
                </Button>
            </AuthCard>
        );
    }

    if (state === "done") {
        return (
            <AuthCard title="Şifre güncellendi">
                <FormAlert severity="success" error="Şifreniz değiştirildi. Güvenliğiniz için tüm cihazlardaki oturumlarınız kapatıldı." />
                <Button component={Link} href="/giris" variant="contained" fullWidth>
                    Giriş yap
                </Button>
            </AuthCard>
        );
    }

    const onSubmit = async (event: React.FormEvent) => {
        event.preventDefault();
        if (state === "sending") return;

        const errors = collectErrors({
            newPassword: validateNewPassword(password),
            repeat: password === repeat ? undefined : "Şifreler eşleşmiyor.",
        });
        setFieldErrors(errors);
        setError(null);
        if (Object.keys(errors).length > 0) return;

        setState("sending");
        try {
            await identity.resetPassword({ email, token, newPassword: password });
            setState("done");
        } catch (e) {
            setState("idle");
            if (!(e instanceof ApiError)) throw e;
            setError(e);
            setFieldErrors(e.fieldErrors);
        }
    };

    return (
        <AuthCard title="Yeni şifre belirle" subtitle={email}>
            <FormAlert error={error && Object.keys(error.fieldErrors).length === 0 ? error : null}>
                {error?.code === "invalid_token" && (
                    <Box sx={{ mt: 1 }}>
                        <Button component={Link} href="/sifremi-unuttum" size="small" variant="outlined" color="error">
                            Yeni bağlantı iste
                        </Button>
                    </Box>
                )}
            </FormAlert>
            <Box component="form" noValidate onSubmit={onSubmit}>
                <Stack spacing={2.5}>
                    <PasswordField
                        id="newPassword"
                        label="Yeni şifre"
                        autoComplete="new-password"
                        autoFocus
                        fullWidth
                        value={password}
                        onChange={(e) => setPassword(e.target.value)}
                        error={Boolean(fieldErrors.newPassword)}
                        helperText={fieldErrors.newPassword ?? "En az 10 karakter."}
                    />
                    <PasswordField
                        id="repeat"
                        label="Yeni şifre (tekrar)"
                        autoComplete="new-password"
                        fullWidth
                        value={repeat}
                        onChange={(e) => setRepeat(e.target.value)}
                        error={Boolean(fieldErrors.repeat)}
                        helperText={fieldErrors.repeat}
                    />
                    <Button type="submit" variant="contained" size="large" fullWidth disabled={state === "sending"}>
                        Şifreyi değiştir
                    </Button>
                </Stack>
            </Box>
        </AuthCard>
    );
}
