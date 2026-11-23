"use client";

import { useState } from "react";
import Alert from "@mui/material/Alert";
import Button from "@mui/material/Button";
import Dialog from "@mui/material/Dialog";
import DialogActions from "@mui/material/DialogActions";
import DialogContent from "@mui/material/DialogContent";
import DialogTitle from "@mui/material/DialogTitle";
import Stack from "@mui/material/Stack";
import TextField from "@mui/material/TextField";
import { ApiError } from "@/lib/api/client";
import { errorMessage } from "@/lib/api/errorMessages";
import { identity, type BranchDto } from "@/lib/api/identity";

type BranchDialogProps = {
    /** null → yeni şube. */
    branch: BranchDto | null;
    onClose: () => void;
    onSaved: () => void;
};

/** Şube ekleme/düzenleme penceresi. Kod çakışması kod alanında gösterilir. */
export default function BranchDialog({ branch, onClose, onSaved }: BranchDialogProps) {
    const [code, setCode] = useState(branch?.code ?? "");
    const [name, setName] = useState(branch?.name ?? "");
    const [saving, setSaving] = useState(false);
    const [codeError, setCodeError] = useState<string | null>(null);
    const [nameError, setNameError] = useState<string | null>(null);
    const [error, setError] = useState<string | null>(null);

    const submit = async () => {
        setSaving(true);
        setCodeError(null);
        setNameError(null);
        setError(null);
        try {
            if (branch) {
                await identity.branches.update(branch.id, { code, name, isActive: branch.isActive });
            } else {
                await identity.branches.create({ code, name });
            }
            onSaved();
        } catch (e) {
            if (e instanceof ApiError && e.code === "branch_code_taken") {
                setCodeError(errorMessage(e));
            } else if (e instanceof ApiError && Object.keys(e.fieldErrors).length > 0) {
                setCodeError(e.fieldErrors.code ?? null);
                setNameError(e.fieldErrors.name ?? null);
                if (!e.fieldErrors.code && !e.fieldErrors.name) setError(errorMessage(e));
            } else {
                setError(errorMessage(e));
            }
        } finally {
            setSaving(false);
        }
    };

    return (
        <Dialog open onClose={onClose} fullWidth maxWidth="xs">
            <form
                noValidate
                onSubmit={(e) => {
                    e.preventDefault();
                    void submit();
                }}
            >
                <DialogTitle>{branch ? "Şubeyi düzenle" : "Yeni şube"}</DialogTitle>
                <DialogContent>
                    <Stack spacing={2} sx={{ pt: 1 }}>
                        {error && <Alert severity="error">{error}</Alert>}
                        <TextField
                            label="Şube kodu"
                            value={code}
                            onChange={(e) => setCode(e.target.value)}
                            error={!!codeError}
                            helperText={codeError ?? "En fazla 10 karakter; harf, rakam, tire."}
                            required
                            autoFocus
                        />
                        <TextField label="Şube adı" value={name} onChange={(e) => setName(e.target.value)} error={!!nameError} helperText={nameError} required />
                    </Stack>
                </DialogContent>
                <DialogActions>
                    <Button onClick={onClose}>Vazgeç</Button>
                    <Button type="submit" variant="contained" disabled={saving}>
                        Kaydet
                    </Button>
                </DialogActions>
            </form>
        </Dialog>
    );
}
