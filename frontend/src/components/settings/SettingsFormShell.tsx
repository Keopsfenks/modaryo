"use client";

import Alert from "@mui/material/Alert";
import Button from "@mui/material/Button";
import CircularProgress from "@mui/material/CircularProgress";
import Stack from "@mui/material/Stack";
import type { useSettingsForm } from "./useSettingsForm";

type SettingsFormShellProps = {
    form: Pick<ReturnType<typeof useSettingsForm>, "loading" | "saving" | "error" | "conflict" | "reload">;
    saved: boolean;
    onSubmit: () => void;
    children: React.ReactNode;
    submitLabel?: string;
};

/** Ayar formlarının ortak çerçevesi: yükleniyor, hata/çakışma uyarısı, kaydet düğmesi. */
export default function SettingsFormShell({ form, saved, onSubmit, children, submitLabel = "Kaydet" }: SettingsFormShellProps) {
    if (form.loading) {
        return <CircularProgress aria-label="Yükleniyor" />;
    }

    return (
        <form
            noValidate
            onSubmit={(e) => {
                e.preventDefault();
                onSubmit();
            }}
        >
            <Stack spacing={3}>
                {form.error && (
                    <Alert
                        severity="error"
                        action={
                            form.conflict ? (
                                <Button color="inherit" size="small" onClick={() => void form.reload()}>
                                    Yenile
                                </Button>
                            ) : undefined
                        }
                    >
                        {form.error}
                    </Alert>
                )}
                {saved && !form.error && <Alert severity="success">Ayarlar kaydedildi.</Alert>}
                {children}
                <div>
                    <Button type="submit" variant="contained" disabled={form.saving}>
                        {form.saving ? "Kaydediliyor..." : submitLabel}
                    </Button>
                </div>
            </Stack>
        </form>
    );
}
