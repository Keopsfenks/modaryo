"use client";

import { useState } from "react";
import { useRouter } from "next/navigation";
import Button from "@mui/material/Button";
import Checkbox from "@mui/material/Checkbox";
import Dialog from "@mui/material/Dialog";
import DialogActions from "@mui/material/DialogActions";
import DialogContent from "@mui/material/DialogContent";
import DialogContentText from "@mui/material/DialogContentText";
import DialogTitle from "@mui/material/DialogTitle";
import FormControlLabel from "@mui/material/FormControlLabel";
import Stack from "@mui/material/Stack";
import FormAlert from "@/components/auth/FormAlert";
import { ApiError } from "@/lib/api/client";
import { errorMessage } from "@/lib/api/errorMessages";
import { SHIPMENT_STATUS_LABELS, sales, type OrderRef, type ShipmentStatus } from "@/lib/api/sales";
import { formatDate } from "@/lib/date";

type Props = { shipments: OrderRef[]; onClose(): void };

/** Seçilen sevklerin faturalanmamış miktarlarından satış faturası taslağı oluşturur ve taslağa gider. */
export default function InvoiceFromShipmentsDialog({ shipments, onClose }: Props) {
    const router = useRouter();
    const usable = shipments.filter((s) => s.status !== "cancelled");
    const [selected, setSelected] = useState<string[]>(usable.map((s) => s.id));
    const [error, setError] = useState<ApiError | string | null>(null);
    const [busy, setBusy] = useState(false);

    async function create() {
        if (selected.length === 0) return setError("En az bir sevk seçin.");
        setBusy(true);
        setError(null);
        try {
            const invoice = await sales.shipments.invoiceDraft(selected);
            router.push(`/satis/faturalar/${invoice.id}`);
        } catch (e) {
            if (!(e instanceof ApiError)) throw e;
            setError(e.code === "nothing_to_invoice" ? "Seçilen sevklerde faturalanacak miktar kalmamış." : errorMessage(e));
            setBusy(false);
        }
    }

    return (
        <Dialog open onClose={onClose} fullWidth maxWidth="sm">
            <DialogTitle>Sevklerden fatura</DialogTitle>
            <DialogContent>
                <Stack spacing={1} sx={{ pt: 1 }}>
                    <FormAlert error={error} />
                    <DialogContentText>Seçilen sevklerin faturalanmamış miktarları için satış faturası taslağı oluşturulur. Fiyat ve iskontolar siparişten gelir.</DialogContentText>
                    {usable.map((s) => (
                        <FormControlLabel
                            key={s.id}
                            control={<Checkbox checked={selected.includes(s.id)} onChange={(e) => setSelected((cur) => (e.target.checked ? [...cur, s.id] : cur.filter((id) => id !== s.id)))} />}
                            label={`${s.number} · ${formatDate(s.date)} · ${SHIPMENT_STATUS_LABELS[s.status as ShipmentStatus] ?? s.status}`}
                        />
                    ))}
                </Stack>
            </DialogContent>
            <DialogActions>
                <Button onClick={onClose}>Vazgeç</Button>
                <Button variant="contained" disabled={busy} onClick={() => void create()}>
                    {busy ? "Oluşturuluyor..." : "Taslak oluştur"}
                </Button>
            </DialogActions>
        </Dialog>
    );
}
