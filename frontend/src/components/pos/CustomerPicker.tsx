"use client";

import { useEffect, useState } from "react";
import Button from "@mui/material/Button";
import Dialog from "@mui/material/Dialog";
import DialogActions from "@mui/material/DialogActions";
import DialogContent from "@mui/material/DialogContent";
import DialogTitle from "@mui/material/DialogTitle";
import List from "@mui/material/List";
import ListItemButton from "@mui/material/ListItemButton";
import ListItemText from "@mui/material/ListItemText";
import TextField from "@mui/material/TextField";
import { erp, type CurrentAccountListItem } from "@/lib/api/erp";
import { useDebounced } from "@/components/pricing/useDebounced";

export type PosCustomer = { id: string; name: string };

type CustomerPickerProps = {
    open: boolean;
    onClose: () => void;
    /** null → Perakende Müşteri. */
    onPick: (customer: PosCustomer | null) => void;
};

/** Müşteri seçimi (F4): cari arama; "Perakende Müşteri" varsayılandır ve veresiye vermez. */
export default function CustomerPicker({ open, onClose, onPick }: CustomerPickerProps) {
    const [text, setText] = useState("");
    const search = useDebounced(text);
    const [rows, setRows] = useState<CurrentAccountListItem[]>([]);
    const [failed, setFailed] = useState(false);

    useEffect(() => {
        if (!open || search.length < 2) return;
        let cancelled = false;
        erp.listAccounts({ search, role: "customer", pageSize: 20 }).then(
            (r) => {
                if (!cancelled) {
                    setRows(r.items);
                    setFailed(false);
                }
            },
            () => !cancelled && setFailed(true),
        );
        return () => {
            cancelled = true;
        };
    }, [open, search]);

    const shown = search.length < 2 ? [] : rows;

    return (
        <Dialog open={open} onClose={onClose} fullWidth maxWidth="sm">
            <DialogTitle>Müşteri seç</DialogTitle>
            <DialogContent>
                <TextField autoFocus fullWidth margin="dense" label="Müşteri ara" value={text} onChange={(e) => setText(e.target.value)} helperText={failed ? "Müşteriler yüklenemedi." : "En az 2 karakter yazın."} />
                <List>
                    {shown.map((a) => (
                        <ListItemButton key={a.id} onClick={() => onPick({ id: a.id, name: a.name })} sx={{ minHeight: 48 }}>
                            <ListItemText primary={a.name} secondary={a.code} />
                        </ListItemButton>
                    ))}
                </List>
            </DialogContent>
            <DialogActions>
                <Button onClick={() => onPick(null)}>Perakende Müşteri</Button>
                <Button onClick={onClose}>Vazgeç</Button>
            </DialogActions>
        </Dialog>
    );
}
