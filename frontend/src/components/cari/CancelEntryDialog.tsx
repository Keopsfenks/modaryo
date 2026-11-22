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
import { erp, type StatementRow } from "@/lib/api/erp";
import { formatDate } from "@/lib/date";

type CancelEntryDialogProps = {
    accountId: string;
    entry: StatementRow;
    onClose: () => void;
    onCancelled: () => void;
};

function serverMessage(e: ApiError): ApiError | string {
    if (e.code === "already_cancelled") return "Bu hareket zaten iptal edilmiş.";
    if (e.code === "not_manual") return "Bu hareket buradan iptal edilemez; kaynak belgesinden iptal edin.";
    return e;
}

/** Hareket iptali: neden zorunlu (2-200 karakter). İptal geri alınamaz. */
export default function CancelEntryDialog({ accountId, entry, onClose, onCancelled }: CancelEntryDialogProps) {
    const [reason, setReason] = useState("");
    const [error, setError] = useState<string>();
    const [alert, setAlert] = useState<ApiError | string | null>(null);
    const [busy, setBusy] = useState(false);

    const submit = async (event: React.FormEvent) => {
        event.preventDefault();
        if (busy) return;
        const trimmed = reason.trim();
        if (trimmed.length < 2 || trimmed.length > 200) {
            setError("İptal nedeni 2 ile 200 karakter arasında olmalı.");
            return;
        }
        setError(undefined);
        setAlert(null);
        setBusy(true);
        try {
            await erp.cancelEntry(accountId, entry.id, trimmed);
            onCancelled();
        } catch (e) {
            if (!(e instanceof ApiError)) throw e;
            setAlert(serverMessage(e));
        } finally {
            setBusy(false);
        }
    };

    return (
        <Dialog open onClose={onClose} fullWidth maxWidth="sm">
            <form noValidate onSubmit={submit}>
                <DialogTitle>Hareketi iptal et</DialogTitle>
                <DialogContent>
                    <FormAlert error={alert} />
                    <DialogContentText sx={{ mb: 2 }}>
                        {formatDate(entry.date)} tarihli {entry.documentNo ? `"${entry.documentNo}" ` : ""}hareket iptal edilecek. İptal geri alınamaz; iptal edilen hareket bakiyeye katılmaz.
                    </DialogContentText>
                    <TextField
                        autoFocus
                        id="cancel-reason"
                        label="İptal nedeni"
                        fullWidth
                        multiline
                        minRows={2}
                        value={reason}
                        onChange={(e) => setReason(e.target.value)}
                        error={Boolean(error)}
                        helperText={error}
                    />
                </DialogContent>
                <DialogActions>
                    <Button onClick={onClose}>Vazgeç</Button>
                    <Button type="submit" variant="contained" color="error" disabled={busy}>
                        Hareketi iptal et
                    </Button>
                </DialogActions>
            </form>
        </Dialog>
    );
}
