"use client";

import { useRef, useState } from "react";
import Alert from "@mui/material/Alert";
import Button from "@mui/material/Button";
import Link from "@mui/material/Link";
import Stack from "@mui/material/Stack";
import Typography from "@mui/material/Typography";
import { ApiError } from "@/lib/api/client";
import { erp, type UploadedFile } from "@/lib/api/erp";

type AttachmentUploadProps = {
    file: UploadedFile | null;
    onChange: (file: UploadedFile | null) => void;
    label?: string;
};

/** Belge eki (fatura/fiş görseli ya da PDF): P0b dosya API'sine "attachment" olarak yüklenir, kimliği üst forma bildirilir. */
export default function AttachmentUpload({ file, onChange, label = "Belge eki" }: AttachmentUploadProps) {
    const input = useRef<HTMLInputElement>(null);
    const [busy, setBusy] = useState(false);
    const [error, setError] = useState<string | null>(null);

    const pick = async (selected: File | undefined) => {
        if (!selected) return;
        setBusy(true);
        setError(null);
        try {
            onChange(await erp.files.upload(selected, "attachment"));
        } catch (e) {
            setError(e instanceof ApiError ? e.message : "Dosya yüklenemedi.");
        } finally {
            setBusy(false);
            if (input.current) input.current.value = "";
        }
    };

    return (
        <Stack spacing={1}>
            {error && <Alert severity="error">{error}</Alert>}
            <Stack direction="row" spacing={2} sx={{ alignItems: "center" }}>
                <Button variant="outlined" component="label" disabled={busy}>
                    {busy ? "Yükleniyor..." : file ? `${label} değiştir` : `${label} yükle`}
                    <input
                        ref={input}
                        hidden
                        type="file"
                        accept="image/png,image/jpeg,image/webp,application/pdf"
                        aria-label={`${label} dosyası`}
                        onChange={(e) => void pick(e.target.files?.[0])}
                    />
                </Button>
                {file && (
                    <>
                        <Typography variant="body2">
                            <Link href={erp.files.url(file.id)} target="_blank" rel="noopener">
                                {file.fileName}
                            </Link>
                        </Typography>
                        <Button color="inherit" onClick={() => onChange(null)}>
                            Kaldır
                        </Button>
                    </>
                )}
            </Stack>
        </Stack>
    );
}
