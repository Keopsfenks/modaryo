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

type ReasonDialogProps = {
    open: boolean;
    title: string;
    text: string;
    confirmLabel: string;
    onClose(): void;
    /** Gerekçeyle işlemi yapar; ApiError fırlatırsa iletisi gösterilir. */
    onConfirm(reason: string): Promise<void>;
};

/** Gerekçe isteyen işlemler için ortak onay penceresi (sipariş iptali / kısa kapatma). */
export default function ReasonDialog({ open, title, text, confirmLabel, onClose, onConfirm }: ReasonDialogProps) {
    const [reason, setReason] = useState("");
    const [error, setError] = useState<string | null>(null);
    const [busy, setBusy] = useState(false);
    const trimmed = reason.trim();
    const valid = trimmed.length >= 2 && trimmed.length <= 200;

    async function submit() {
        setBusy(true);
        setError(null);
        try {
            await onConfirm(trimmed);
        } catch (e) {
            if (!(e instanceof ApiError)) throw e;
            setError(errorMessage(e));
        } finally {
            setBusy(false);
        }
    }

    return (
        <Dialog open={open} onClose={onClose} fullWidth maxWidth="sm">
            <DialogTitle>{title}</DialogTitle>
            <DialogContent>
                <DialogContentText sx={{ mb: 2 }}>{text}</DialogContentText>
                <FormAlert error={error} />
                <TextField
                    label="Gerekçe"
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
                <Button variant="contained" color="error" disabled={!valid || busy} onClick={() => void submit()}>
                    {busy ? "İşleniyor..." : confirmLabel}
                </Button>
            </DialogActions>
        </Dialog>
    );
}
