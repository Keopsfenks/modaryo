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
import { errorMessage } from "@/lib/api/errorMessages";
import { invoicing } from "@/lib/api/invoicing";

type EmailDialogProps = { open: boolean; invoiceId: string; defaultTo: string; onClose(): void; onSent(): void };

const split = (text: string): string[] =>
    text
        .split(/[,;\s]+/)
        .map((s) => s.trim())
        .filter(Boolean);

const EMAIL = /^[^\s@,;]+@[^\s@,;]+\.[^\s@,;]+$/;

/** Faturayı PDF eki olarak e-postayla gönderir. Konu ve metin boş bırakılırsa belge ayarlarındaki şablon kullanılır. */
export default function EmailDialog({ open, invoiceId, defaultTo, onClose, onSent }: EmailDialogProps) {
    const [to, setTo] = useState(defaultTo);
    const [cc, setCc] = useState("");
    const [subject, setSubject] = useState("");
    const [body, setBody] = useState("");
    const [error, setError] = useState<string | null>(null);
    const [busy, setBusy] = useState(false);

    async function submit() {
        const toList = split(to);
        const ccList = split(cc);
        if (toList.length === 0 || [...toList, ...ccList].some((a) => !EMAIL.test(a))) {
            setError("Geçerli e-posta adresleri girin.");
            return;
        }
        setBusy(true);
        setError(null);
        try {
            await invoicing.email(invoiceId, { to: toList, cc: ccList.length ? ccList : undefined, subject: subject.trim() || undefined, body: body.trim() || undefined });
            onSent();
        } catch (e) {
            if (!(e instanceof ApiError)) throw e;
            setError(errorMessage(e));
        } finally {
            setBusy(false);
        }
    }

    return (
        <Dialog open={open} onClose={onClose} fullWidth maxWidth="sm">
            <DialogTitle>Faturayı e-posta ile gönder</DialogTitle>
            <DialogContent>
                <Stack spacing={2} sx={{ pt: 1 }}>
                    <FormAlert error={error} />
                    <TextField label="Alıcı" value={to} onChange={(e) => setTo(e.target.value)} helperText="Birden çok adres virgülle ayrılır." fullWidth />
                    <TextField label="Bilgi (cc)" value={cc} onChange={(e) => setCc(e.target.value)} fullWidth />
                    <TextField label="Konu" value={subject} onChange={(e) => setSubject(e.target.value)} placeholder="Boşsa ayardaki şablon kullanılır" fullWidth />
                    <TextField label="Mesaj" value={body} onChange={(e) => setBody(e.target.value)} placeholder="Boşsa ayardaki şablon kullanılır" multiline minRows={3} fullWidth />
                    <Typography variant="caption" color="text.secondary">
                        Fatura PDF olarak eklenir.
                    </Typography>
                </Stack>
            </DialogContent>
            <DialogActions>
                <Button onClick={onClose}>Vazgeç</Button>
                <Button variant="contained" disabled={busy} onClick={() => void submit()}>
                    {busy ? "Gönderiliyor..." : "Gönder"}
                </Button>
            </DialogActions>
        </Dialog>
    );
}
