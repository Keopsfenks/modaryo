"use client";

import { useState } from "react";
import Button from "@mui/material/Button";
import Dialog from "@mui/material/Dialog";
import DialogActions from "@mui/material/DialogActions";
import DialogContent from "@mui/material/DialogContent";
import DialogContentText from "@mui/material/DialogContentText";
import DialogTitle from "@mui/material/DialogTitle";
import TextField from "@mui/material/TextField";
import FormAlert from "@/components/auth/FormAlert";
import { ApiError } from "@/lib/api/client";
import { errorMessage } from "@/lib/api/errorMessages";
import { invoicing } from "@/lib/api/invoicing";

type CancelDialogProps = { open: boolean; invoiceId: string; number: string | null; onClose(): void; onCancelled(): void };

/** İptal: 2-200 karakter gerekçe zorunlu; defter, vade ve stok etkileri geri alınır, numara kullanılmış kalır. */
export default function CancelDialog({ open, invoiceId, number, onClose, onCancelled }: CancelDialogProps) {
    const [reason, setReason] = useState("");
    const [error, setError] = useState<string | null>(null);
    const [busy, setBusy] = useState(false);
    const trimmed = reason.trim();
    const valid = trimmed.length >= 2 && trimmed.length <= 200;

    async function submit() {
        setBusy(true);
        setError(null);
        try {
            await invoicing.cancel(invoiceId, trimmed);
            onCancelled();
        } catch (e) {
            if (!(e instanceof ApiError)) throw e;
            setError(errorMessage(e));
        } finally {
            setBusy(false);
        }
    }

    return (
        <Dialog open={open} onClose={onClose} fullWidth maxWidth="sm">
            <DialogTitle>Fatura iptal edilsin mi?</DialogTitle>
            <DialogContent>
                <DialogContentText sx={{ mb: 2 }}>
                    {number ?? "Fatura"} iptal edilecek: cari, vade ve stok etkileri geri alınır. Numara kullanılmış sayılır ve yeniden verilmez.
                </DialogContentText>
                <FormAlert error={error} />
                <TextField
                    label="İptal gerekçesi"
                    value={reason}
                    onChange={(e) => setReason(e.target.value)}
                    error={reason !== "" && !valid}
                    helperText={reason !== "" && !valid ? "Gerekçe 2-200 karakter olmalıdır." : `${trimmed.length}/200`}
                    fullWidth
                    multiline
                    minRows={2}
                    slotProps={{ htmlInput: { maxLength: 200 } }}
                />
            </DialogContent>
            <DialogActions>
                <Button onClick={onClose}>Vazgeç</Button>
                <Button color="error" variant="contained" disabled={!valid || busy} onClick={() => void submit()}>
                    {busy ? "İptal ediliyor..." : "Faturayı iptal et"}
                </Button>
            </DialogActions>
        </Dialog>
    );
}
