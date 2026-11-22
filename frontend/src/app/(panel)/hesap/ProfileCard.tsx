"use client";

import { useState } from "react";
import Box from "@mui/material/Box";
import Button from "@mui/material/Button";
import Stack from "@mui/material/Stack";
import TextField from "@mui/material/TextField";
import DashboardCard from "@/components/dashboard/DashboardCard";
import FormAlert from "@/components/auth/FormAlert";
import { ApiError } from "@/lib/api/client";
import { identity } from "@/lib/api/identity";
import { validateFullName } from "@/lib/validation";
import { useSession } from "@/session/SessionProvider";

export default function ProfileCard() {
    const { session, refresh } = useSession();
    const [fullName, setFullName] = useState(session.user.fullName);
    const [fieldError, setFieldError] = useState<string>();
    const [error, setError] = useState<ApiError | null>(null);
    const [state, setState] = useState<"idle" | "saving" | "saved">("idle");

    const onSubmit = async (event: React.FormEvent) => {
        event.preventDefault();
        const validation = validateFullName(fullName);
        setFieldError(validation);
        setError(null);
        if (validation) return;

        setState("saving");
        try {
            await identity.updateProfile(fullName.trim());
            await refresh();
            setState("saved");
        } catch (e) {
            setState("idle");
            if (!(e instanceof ApiError)) throw e;
            setError(e);
            setFieldError(e.fieldErrors.fullName);
        }
    };

    return (
        <DashboardCard title="Profil" subtitle="Adınız tüm firmalarda görünür.">
            {state === "saved" && <FormAlert severity="success" error="Bilgileriniz kaydedildi." />}
            <FormAlert error={error && Object.keys(error.fieldErrors).length === 0 ? error : null} />
            <Box component="form" noValidate onSubmit={onSubmit}>
                <Stack spacing={2.5}>
                    <TextField
                        id="fullName"
                        label="Ad soyad"
                        fullWidth
                        value={fullName}
                        onChange={(e) => {
                            setFullName(e.target.value);
                            setState("idle");
                        }}
                        error={Boolean(fieldError)}
                        helperText={fieldError}
                    />
                    <TextField
                        id="email"
                        label="E-posta"
                        fullWidth
                        value={session.user.email}
                        helperText="E-posta değiştirme yakında."
                        slotProps={{ htmlInput: { readOnly: true } }}
                    />
                    <Box>
                        <Button type="submit" variant="contained" disabled={state === "saving"}>
                            Kaydet
                        </Button>
                    </Box>
                </Stack>
            </Box>
        </DashboardCard>
    );
}
