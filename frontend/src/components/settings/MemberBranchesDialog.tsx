"use client";

import { useEffect, useState } from "react";
import Alert from "@mui/material/Alert";
import Button from "@mui/material/Button";
import Checkbox from "@mui/material/Checkbox";
import Dialog from "@mui/material/Dialog";
import DialogActions from "@mui/material/DialogActions";
import DialogContent from "@mui/material/DialogContent";
import DialogTitle from "@mui/material/DialogTitle";
import FormControlLabel from "@mui/material/FormControlLabel";
import Radio from "@mui/material/Radio";
import RadioGroup from "@mui/material/RadioGroup";
import Stack from "@mui/material/Stack";
import Typography from "@mui/material/Typography";
import { errorMessage } from "@/lib/api/errorMessages";
import { identity, type BranchDto, type MemberDto, type RoleDto } from "@/lib/api/identity";

type MemberBranchesDialogProps = {
    member: MemberDto;
    role: RoleDto;
    onClose: () => void;
    onSaved: () => void;
};

/** Üyenin şube erişimi: rolün şubeleri (null) ya da rol kapsamı içinden seçilmiş alt küme. */
export default function MemberBranchesDialog({ member, role, onClose, onSaved }: MemberBranchesDialogProps) {
    const [branches, setBranches] = useState<BranchDto[] | null>(null);
    const [mode, setMode] = useState<"role" | "subset">(member.branchIds ? "subset" : "role");
    const [selected, setSelected] = useState<string[]>(member.branchIds ?? []);
    const [saving, setSaving] = useState(false);
    const [error, setError] = useState<string | null>(null);

    useEffect(() => {
        identity.branches.list().then(setBranches, (e: unknown) => {
            setBranches([]);
            setError(errorMessage(e));
        });
    }, []);

    const options = (branches ?? []).filter((b) => role.allBranches || role.branchIds.includes(b.id));

    const toggle = (id: string) => setSelected((list) => (list.includes(id) ? list.filter((x) => x !== id) : [...list, id]));

    const save = async () => {
        setSaving(true);
        setError(null);
        try {
            await identity.members.setBranches(member.userId, mode === "role" ? null : selected);
            onSaved();
        } catch (e) {
            setError(errorMessage(e));
        } finally {
            setSaving(false);
        }
    };

    return (
        <Dialog open onClose={onClose} fullWidth maxWidth="xs">
            <DialogTitle>Şube erişimi: {member.fullName}</DialogTitle>
            <DialogContent>
                <Stack spacing={1} sx={{ pt: 1 }}>
                    {error && <Alert severity="error">{error}</Alert>}
                    <RadioGroup value={mode} onChange={(e) => setMode(e.target.value as "role" | "subset")}>
                        <FormControlLabel value="role" control={<Radio />} label="Rolün şubeleri" />
                        <FormControlLabel value="subset" control={<Radio />} label="Seçili şubeler" />
                    </RadioGroup>
                    {mode === "subset" && (
                        <Stack sx={{ pl: 3 }}>
                            <Typography variant="body2" color="text.secondary">
                                {role.name} rolünün şubeleri arasından seçin.
                            </Typography>
                            {options.map((b) => (
                                <FormControlLabel
                                    key={b.id}
                                    control={<Checkbox checked={selected.includes(b.id)} onChange={() => toggle(b.id)} />}
                                    label={`${b.name} (${b.code})`}
                                />
                            ))}
                        </Stack>
                    )}
                </Stack>
            </DialogContent>
            <DialogActions>
                <Button onClick={onClose}>Vazgeç</Button>
                <Button variant="contained" disabled={saving || (mode === "subset" && selected.length === 0)} onClick={() => void save()}>
                    Kaydet
                </Button>
            </DialogActions>
        </Dialog>
    );
}
