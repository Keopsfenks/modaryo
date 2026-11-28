"use client";

import { useState } from "react";
import Button from "@mui/material/Button";
import Dialog from "@mui/material/Dialog";
import DialogActions from "@mui/material/DialogActions";
import DialogContent from "@mui/material/DialogContent";
import DialogTitle from "@mui/material/DialogTitle";
import Stack from "@mui/material/Stack";
import TextField from "@mui/material/TextField";
import Typography from "@mui/material/Typography";
import FormAlert from "@/components/auth/FormAlert";
import { ApiError } from "@/lib/api/client";
import { erp } from "@/lib/api/erp";
import { errorMessage } from "@/lib/api/errorMessages";
import type { DeliverInput } from "@/lib/api/shipping";

type DeliveryDialogProps = {
    open: boolean;
    title?: string;
    onClose(): void;
    /** Teslim kanıtıyla işlemi yapar; ApiError fırlatırsa iletisi gösterilir. */
    onSubmit(body: DeliverInput): Promise<void>;
};

const pad = (n: number) => String(n).padStart(2, "0");
const nowLocal = () => {
    const d = new Date();
    return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}`;
};

/** Teslim kanıtı: teslim alan (2-100 karakter), teslim zamanı, isteğe bağlı fotoğraf/imza görseli ve not. */
export default function DeliveryDialog({ open, title = "Teslim edildi", onClose, onSubmit }: DeliveryDialogProps) {
    const [receivedBy, setReceivedBy] = useState("");
    const [deliveredAt, setDeliveredAt] = useState(nowLocal);
    const [note, setNote] = useState("");
    const [file, setFile] = useState<File | null>(null);
    const [error, setError] = useState<ApiError | string | null>(null);
    const [nameError, setNameError] = useState<string | null>(null);
    const [busy, setBusy] = useState(false);

    async function submit() {
        const name = receivedBy.trim();
        if (name.length < 2 || name.length > 100) return setNameError("Teslim alan 2 ile 100 karakter arasında olmalıdır.");
        setNameError(null);
        setBusy(true);
        setError(null);
        try {
            const proof = file ? await erp.files.upload(file, "image") : null;
            await onSubmit({
                receivedBy: name,
                deliveredAt: deliveredAt ? new Date(deliveredAt).toISOString() : null,
                proofFileId: proof?.id ?? null,
                note: note.trim() || null,
            });
            setReceivedBy("");
            setNote("");
            setFile(null);
            onClose();
        } catch (e) {
            if (!(e instanceof ApiError)) throw e;
            setError(errorMessage(e));
        } finally {
            setBusy(false);
        }
    }

    return (
        <Dialog open={open} onClose={onClose} fullWidth maxWidth="xs">
            <DialogTitle>{title}</DialogTitle>
            <DialogContent>
                <Stack spacing={2} sx={{ pt: 1 }}>
                    <FormAlert error={error} />
                    <TextField label="Teslim alan" size="small" value={receivedBy} onChange={(e) => setReceivedBy(e.target.value)} error={nameError !== null} helperText={nameError ?? undefined} autoFocus />
                    <TextField
                        label="Teslim zamanı"
                        type="datetime-local"
                        size="small"
                        value={deliveredAt}
                        onChange={(e) => setDeliveredAt(e.target.value)}
                        slotProps={{ inputLabel: { shrink: true } }}
                    />
                    <div>
                        <Button component="label" variant="outlined" size="small">
                            {file ? "Görseli değiştir" : "Fotoğraf / imza ekle"}
                            <input hidden type="file" accept="image/*" aria-label="Teslim kanıtı görseli" onChange={(e) => setFile(e.target.files?.[0] ?? null)} />
                        </Button>
                        {file && (
                            <Typography variant="caption" sx={{ ml: 1 }}>
                                {file.name}
                            </Typography>
                        )}
                    </div>
                    <TextField label="Not" size="small" multiline minRows={2} value={note} onChange={(e) => setNote(e.target.value)} />
                </Stack>
            </DialogContent>
            <DialogActions>
                <Button onClick={onClose}>Vazgeç</Button>
                <Button variant="contained" disabled={busy} onClick={() => void submit()}>
                    {busy ? "Kaydediliyor..." : "Kaydet"}
                </Button>
            </DialogActions>
        </Dialog>
    );
}
