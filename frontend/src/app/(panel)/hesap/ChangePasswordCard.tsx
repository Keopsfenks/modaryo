"use client";

import { useState } from "react";
import Box from "@mui/material/Box";
import Button from "@mui/material/Button";
import Stack from "@mui/material/Stack";
import FormAlert from "@/components/auth/FormAlert";
import PasswordField from "@/components/auth/PasswordField";
import DashboardCard from "@/components/dashboard/DashboardCard";
import { ApiError } from "@/lib/api/client";
import { identity } from "@/lib/api/identity";
import { collectErrors, required, validateNewPassword, type FieldErrors } from "@/lib/validation";

const empty = { currentPassword: "", newPassword: "", repeat: "" };

export default function ChangePasswordCard() {
    const [values, setValues] = useState(empty);
    const [fieldErrors, setFieldErrors] = useState<FieldErrors>({});
    const [error, setError] = useState<ApiError | null>(null);
    const [state, setState] = useState<"idle" | "saving" | "done">("idle");

    const set = (field: keyof typeof empty) => (event: React.ChangeEvent<HTMLInputElement>) => {
        setValues((v) => ({ ...v, [field]: event.target.value }));
        setState("idle");
    };

    const onSubmit = async (event: React.FormEvent) => {
        event.preventDefault();
        if (state === "saving") return;

        const errors = collectErrors({
            currentPassword: required(values.currentPassword, "Mevcut şifre gereklidir."),
            newPassword: validateNewPassword(values.newPassword),
            repeat: values.newPassword === values.repeat ? undefined : "Şifreler eşleşmiyor.",
        });
        setFieldErrors(errors);
        setError(null);
        if (Object.keys(errors).length > 0) return;

        setState("saving");
        try {
            await identity.changePassword({ currentPassword: values.currentPassword, newPassword: values.newPassword });
            setValues(empty);
            setState("done");
        } catch (e) {
            setState("idle");
            if (!(e instanceof ApiError)) throw e;
            setError(e);
            setFieldErrors(e.fieldErrors);
        }
    };

    const field = (name: keyof typeof empty, label: string, autoComplete: string, hint?: string) => (
        <PasswordField
            id={name}
            label={label}
            autoComplete={autoComplete}
            fullWidth
            value={values[name]}
            onChange={set(name)}
            error={Boolean(fieldErrors[name])}
            helperText={fieldErrors[name] ?? hint}
        />
    );

    return (
        <DashboardCard title="Şifre değiştir" subtitle="Şifreniz değişince diğer cihazlardaki oturumlarınız kapatılır.">
            {state === "done" && <FormAlert severity="success" error="Şifreniz değişti. Diğer cihazlardaki oturumlarınız kapatıldı." />}
            <FormAlert error={error && Object.keys(error.fieldErrors).length === 0 ? error : null} />
            <Box component="form" noValidate onSubmit={onSubmit}>
                <Stack spacing={2.5}>
                    {field("currentPassword", "Mevcut şifre", "current-password")}
                    {field("newPassword", "Yeni şifre", "new-password", "En az 10 karakter.")}
                    {field("repeat", "Yeni şifre (tekrar)", "new-password")}
                    <Box>
                        <Button type="submit" variant="contained" disabled={state === "saving"}>
                            Şifreyi değiştir
                        </Button>
                    </Box>
                </Stack>
            </Box>
        </DashboardCard>
    );
}
