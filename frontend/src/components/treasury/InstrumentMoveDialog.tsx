"use client";

import { useEffect, useState } from "react";
import Alert from "@mui/material/Alert";
import Button from "@mui/material/Button";
import Dialog from "@mui/material/Dialog";
import DialogActions from "@mui/material/DialogActions";
import DialogContent from "@mui/material/DialogContent";
import DialogTitle from "@mui/material/DialogTitle";
import MenuItem from "@mui/material/MenuItem";
import Stack from "@mui/material/Stack";
import TextField from "@mui/material/TextField";
import FormAlert from "@/components/auth/FormAlert";
import { ApiError } from "@/lib/api/client";
import { errorMessage } from "@/lib/api/errorMessages";
import type { CurrentAccountListItem } from "@/lib/api/erp";
import { ACCOUNT_KIND_LABELS, treasury, type InstrumentDto, type InstrumentPostResult, type InstrumentStatus, type TreasuryAccountDto } from "@/lib/api/treasury";
import { todayIso } from "@/lib/date";
import { allowedTransitions, MOVE_LABELS, STATUS_LABELS } from "@/lib/instruments";
import CariPicker from "./CariPicker";

type InstrumentMoveDialogProps = {
    open: boolean;
    instrument: InstrumentDto;
    onClose(): void;
    onMoved(result: InstrumentPostResult): void;
};

/** Hedef duruma göre hangi ek bilgi istenir: ciro → tedarikçi cari, tahsile verme/tahsil/ödeme → hesap. */
function needs(instrument: InstrumentDto, to: InstrumentStatus): { supplier: boolean; account: "bank" | "any" | "optional" | null } {
    switch (to) {
        case "endorsed":
            return { supplier: true, account: null };
        case "inCollection":
            return { supplier: false, account: "bank" };
        case "collected":
            // Tahsildeki çek, tahsile verilen banka hesabına geçer; geç tahsilatta (iade edilmiş) hesap seçilir.
            return { supplier: false, account: instrument.status === "bounced" ? "any" : null };
        case "paid":
            return { supplier: false, account: instrument.collectionAccountId ? null : instrument.kind === "check" ? "bank" : "any" };
        default:
            return { supplier: false, account: null };
    }
}

/** Çek/senet durum geçişi: yalnızca sunucu durum makinesinin izin verdiği hedefler listelenir. */
export default function InstrumentMoveDialog({ open, instrument, onClose, onMoved }: InstrumentMoveDialogProps) {
    const targets = allowedTransitions(instrument.direction, instrument.status);
    const [to, setTo] = useState<InstrumentStatus | "">("");
    const [date, setDate] = useState(todayIso());
    const [note, setNote] = useState("");
    const [supplier, setSupplier] = useState<CurrentAccountListItem | null>(null);
    const [accountId, setAccountId] = useState("");
    const [accounts, setAccounts] = useState<TreasuryAccountDto[]>([]);
    const [error, setError] = useState<ApiError | string | null>(null);
    const [busy, setBusy] = useState(false);

    useEffect(() => {
        if (!open) return;
        treasury.accounts().then((list) => setAccounts(list), () => setAccounts([]));
    }, [open]);

    const need = to ? needs(instrument, to) : { supplier: false, account: null };
    const accountOptions = accounts.filter(
        (a) => a.isActive && a.currency === instrument.currency && (need.account === "bank" ? a.kind === "bank" : a.kind === "cash" || a.kind === "bank"),
    );

    async function submit() {
        if (!to || busy) return;
        setError(null);
        if (need.supplier && !supplier) return setError("Tedarikçi cari seçilmelidir.");
        if (need.account && need.account !== "optional" && !accountId) return setError("Hesap seçilmelidir.");
        if (to === "paid" && date < instrument.dueDate) return setError(errorMessage(new ApiError(409, "", "instrument_not_due")));

        setBusy(true);
        try {
            onMoved(
                await treasury.moveInstrument(instrument.id, {
                    to,
                    date,
                    currentAccountId: need.supplier ? supplier?.id : null,
                    accountId: need.account ? accountId : null,
                    note: note.trim() || null,
                }),
            );
        } catch (e) {
            if (!(e instanceof ApiError)) throw e;
            setError(errorMessage(e));
        } finally {
            setBusy(false);
        }
    }

    return (
        <Dialog open={open} onClose={onClose} fullWidth maxWidth="sm">
            <DialogTitle>
                {instrument.number} · {STATUS_LABELS[instrument.status]}
            </DialogTitle>
            <DialogContent>
                {targets.length === 0 ? (
                    <Alert severity="info">Bu durumdan başka işlem yapılamaz.</Alert>
                ) : (
                    <Stack spacing={2} sx={{ pt: 1 }}>
                        <FormAlert error={error} />
                        <TextField select label="İşlem" value={to} onChange={(e) => setTo(e.target.value as InstrumentStatus)}>
                            {targets.map((t) => (
                                <MenuItem key={t} value={t}>
                                    {MOVE_LABELS[t]}
                                </MenuItem>
                            ))}
                        </TextField>
                        {need.supplier && <CariPicker label="Tedarikçi" role="supplier" value={supplier} onChange={setSupplier} />}
                        {need.account && (
                            <TextField select label="Hesap" value={accountId} onChange={(e) => setAccountId(e.target.value)} helperText={`Para birimi: ${instrument.currency}`}>
                                {accountOptions.map((a) => (
                                    <MenuItem key={a.id} value={a.id}>
                                        {a.name} · {ACCOUNT_KIND_LABELS[a.kind]}
                                    </MenuItem>
                                ))}
                            </TextField>
                        )}
                        <TextField type="date" label="Tarih" value={date} onChange={(e) => setDate(e.target.value)} slotProps={{ inputLabel: { shrink: true } }} />
                        <TextField label="Not" value={note} onChange={(e) => setNote(e.target.value)} />
                    </Stack>
                )}
            </DialogContent>
            <DialogActions>
                <Button onClick={onClose}>Vazgeç</Button>
                {targets.length > 0 && (
                    <Button variant="contained" disabled={!to || busy} onClick={() => void submit()}>
                        {busy ? "Kaydediliyor..." : "Uygula"}
                    </Button>
                )}
            </DialogActions>
        </Dialog>
    );
}
