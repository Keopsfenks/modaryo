"use client";

import { useState } from "react";
import { useRouter } from "next/navigation";
import Alert from "@mui/material/Alert";
import Button from "@mui/material/Button";
import Stack from "@mui/material/Stack";
import Typography from "@mui/material/Typography";
import type { SetupStepProps } from "@/config/setupSteps";
import { errorMessage } from "@/lib/api/errorMessages";
import { patchSetup } from "../setupSettings";

/** Son adım: kurulumu tamamlandı olarak işaretler ve panele yönlendirir. */
export default function DoneStep({ onBack }: SetupStepProps) {
    const router = useRouter();
    const [saving, setSaving] = useState(false);
    const [error, setError] = useState<string | null>(null);

    const finish = async () => {
        setSaving(true);
        setError(null);
        try {
            await patchSetup({ completedAt: new Date().toISOString() });
            router.push("/panel");
        } catch (e) {
            setError(errorMessage(e));
            setSaving(false);
        }
    };

    return (
        <Stack spacing={2}>
            {error && <Alert severity="error">{error}</Alert>}
            <Typography variant="h6">Kurulum tamam</Typography>
            <Typography variant="body2" color="text.secondary">
                Firmanız kullanıma hazır. Ayarları istediğiniz zaman Ayarlar bölümünden güncelleyebilirsiniz.
            </Typography>
            <Stack direction="row" spacing={1}>
                <Button onClick={onBack}>Geri</Button>
                <Button variant="contained" disabled={saving} onClick={() => void finish()}>
                    Panele git
                </Button>
            </Stack>
        </Stack>
    );
}
