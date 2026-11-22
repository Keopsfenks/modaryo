"use client";

import { useState } from "react";
import Link from "next/link";
import Box from "@mui/material/Box";
import Button from "@mui/material/Button";
import Stack from "@mui/material/Stack";
import TextField from "@mui/material/TextField";
import AuthCard from "@/components/auth/AuthCard";
import FormAlert from "@/components/auth/FormAlert";
import { ApiError } from "@/lib/api/client";
import { identity } from "@/lib/api/identity";
import { validateEmail } from "@/lib/validation";

/** Yanıt, hesabın var olup olmadığını belli etmez: her durumda aynı mesaj gösterilir. */
export default function ForgotPasswordForm() {
    const [email, setEmail] = useState("");
    const [fieldError, setFieldError] = useState<string>();
    const [error, setError] = useState<ApiError | null>(null);
    const [state, setState] = useState<"idle" | "sending" | "sent">("idle");

    const onSubmit = async (event: React.FormEvent) => {
        event.preventDefault();
        if (state === "sending") return;

        const validation = validateEmail(email);
        setFieldError(validation);
        setError(null);
        if (validation) return;

        setState("sending");
        try {
            await identity.forgotPassword(email.trim());
            setState("sent");
        } catch (e) {
            setState("idle");
            if (!(e instanceof ApiError)) throw e;
            setError(e);
        }
    };

    return (
        <AuthCard
            title="Şifremi unuttum"
            subtitle="Hesabınızın e-posta adresini girin; şifrenizi sıfırlamanız için bir bağlantı gönderelim."
            footer={
                <Button component={Link} href="/giris">
                    Giriş sayfasına dön
                </Button>
            }
        >
            {state === "sent" ? (
                <FormAlert
                    severity="success"
                    error="Bu adrese kayıtlı bir hesap varsa sıfırlama bağlantısı gönderdik. Bağlantı 1 saat geçerlidir."
                />
            ) : (
                <>
                    <FormAlert error={error} />
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
                                error={Boolean(fieldError)}
                                helperText={fieldError}
                            />
                            <Button type="submit" variant="contained" size="large" fullWidth disabled={state === "sending"}>
                                Sıfırlama bağlantısı gönder
                            </Button>
                        </Stack>
                    </Box>
                </>
            )}
        </AuthCard>
    );
}
