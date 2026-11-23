"use client";

import { useRef, useState } from "react";
import Alert from "@mui/material/Alert";
import Box from "@mui/material/Box";
import Button from "@mui/material/Button";
import Stack from "@mui/material/Stack";
import Typography from "@mui/material/Typography";
import { ApiError } from "@/lib/api/client";
import { erp } from "@/lib/api/erp";

type LogoUploadProps = { fileId: string | null; onChange: (fileId: string | null) => void };

/** Firma logosu: yüklenince dosya kimliğini üst forma bildirir (kaydetme formun işidir). */
export default function LogoUpload({ fileId, onChange }: LogoUploadProps) {
    const input = useRef<HTMLInputElement>(null);
    const [busy, setBusy] = useState(false);
    const [error, setError] = useState<string | null>(null);

    const pick = async (file: File | undefined) => {
        if (!file) {
            return;
        }
        setBusy(true);
        setError(null);
        try {
            const uploaded = await erp.files.upload(file, "logo");
            onChange(uploaded.id);
        } catch (e) {
            setError(e instanceof ApiError ? e.message : "Logo yüklenemedi.");
        } finally {
            setBusy(false);
            if (input.current) {
                input.current.value = "";
            }
        }
    };

    return (
        <Stack spacing={1}>
            <Typography variant="subtitle2">Logo</Typography>
            {error && <Alert severity="error">{error}</Alert>}
            <Stack direction="row" spacing={2} sx={{ alignItems: "center" }}>
                {fileId && (
                    <Box component="img" src={erp.files.url(fileId)} alt="Firma logosu" sx={{ height: 56, maxWidth: 160, objectFit: "contain" }} />
                )}
                <Button variant="outlined" component="label" disabled={busy}>
                    {fileId ? "Logoyu değiştir" : "Logo yükle"}
                    <input
                        ref={input}
                        hidden
                        type="file"
                        accept="image/png,image/jpeg,image/webp"
                        aria-label="Logo dosyası"
                        onChange={(e) => void pick(e.target.files?.[0])}
                    />
                </Button>
                {fileId && (
                    <Button color="inherit" onClick={() => onChange(null)}>
                        Kaldır
                    </Button>
                )}
            </Stack>
        </Stack>
    );
}
