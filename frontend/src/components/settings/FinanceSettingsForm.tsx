"use client";

import { useState } from "react";
import Button from "@mui/material/Button";
import Dialog from "@mui/material/Dialog";
import DialogActions from "@mui/material/DialogActions";
import DialogContent from "@mui/material/DialogContent";
import DialogContentText from "@mui/material/DialogContentText";
import DialogTitle from "@mui/material/DialogTitle";
import TextField from "@mui/material/TextField";
import type { FinanceSettings } from "@/lib/api/erp";
import SettingsFormShell from "./SettingsFormShell";
import { useSettingsForm } from "./useSettingsForm";

/** Finans ayarları: dönem kilidi. Kilidi kaldırmak (tarihi temizlemek) onay ister. */
export default function FinanceSettingsForm() {
    const form = useSettingsForm<FinanceSettings>("finans");
    const [saved, setSaved] = useState(false);
    const [savedLock, setSavedLock] = useState<string | null | undefined>(undefined);
    const [confirming, setConfirming] = useState(false);
    const value = form.value;

    // İlk yüklenen değeri hatırla: temizleme onayı yalnızca kayıtlı bir kilit varken sorulur.
    if (savedLock === undefined && value) {
        setSavedLock(value.periodLockDate);
    }

    const persist = async () => {
        setConfirming(false);
        const next = value!.periodLockDate;
        const ok = await form.save();
        setSaved(ok);
        if (ok) {
            setSavedLock(next);
        }
    };

    const submit = async () => {
        setSaved(false);
        if (savedLock && !value!.periodLockDate) {
            setConfirming(true);
            return;
        }
        await persist();
    };

    return (
        <SettingsFormShell form={form} saved={saved} onSubmit={submit}>
            {value && (
                <TextField
                    type="date"
                    label="Dönem kilit tarihi"
                    error={!!form.fieldErrors.periodLockDate}
                    helperText={form.fieldErrors.periodLockDate ?? "Bu tarih ve öncesine kayıt, iptal ve maliyet yeniden hesabı yapılamaz."}
                    value={value.periodLockDate ?? ""}
                    onChange={(e) => form.setValue({ periodLockDate: e.target.value || null })}
                    slotProps={{ inputLabel: { shrink: true } }}
                    sx={{ maxWidth: 360 }}
                />
            )}
            <Dialog open={confirming} onClose={() => setConfirming(false)}>
                <DialogTitle>Dönem kilidi kaldırılsın mı?</DialogTitle>
                <DialogContent>
                    <DialogContentText>Kilit kalkınca geçmiş dönemlere yeniden kayıt girilebilir ve kayıtlar iptal edilebilir.</DialogContentText>
                </DialogContent>
                <DialogActions>
                    <Button onClick={() => setConfirming(false)}>Vazgeç</Button>
                    <Button color="error" onClick={() => void persist()}>
                        Kilidi kaldır
                    </Button>
                </DialogActions>
            </Dialog>
        </SettingsFormShell>
    );
}
