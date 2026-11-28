"use client";

import { useEffect, useState } from "react";
import Alert from "@mui/material/Alert";
import Button from "@mui/material/Button";
import Dialog from "@mui/material/Dialog";
import DialogActions from "@mui/material/DialogActions";
import DialogContent from "@mui/material/DialogContent";
import DialogTitle from "@mui/material/DialogTitle";
import Stack from "@mui/material/Stack";
import TextField from "@mui/material/TextField";
import { ApiError } from "@/lib/api/client";
import { erp } from "@/lib/api/erp";
import { errorMessage } from "@/lib/api/errorMessages";
import type { ShipmentDto } from "@/lib/api/sales";
import { DEFAULT_CARRIERS, OWN_VEHICLE, shipping, type CarrierInfo, type ShippingSettings } from "@/lib/api/shipping";

type DispatchTarget = { id: string; number: string; customerName?: string };

type DispatchDialogProps = {
    open: boolean;
    shipments: DispatchTarget[];
    /** Verilmezse sevkiyat ayarlarından okunur; ayara erişim yoksa varsayılan firmalar kullanılır. */
    carriers?: CarrierInfo[];
    onClose(): void;
    /** Başarıyla çıkarılan sevklerle çağrılır (kısmi başarıda da). */
    onDone(shipped: ShipmentDto[]): void;
};

const nativeSelect = (label: string) => ({ select: { native: true }, htmlInput: { "aria-label": label }, inputLabel: { shrink: true } });

/**
 * Sevk çıkışı: kargo firması seçilir, "Kendi aracımız" dışındaki firmalarda her sevk için takip numarası zorunludur.
 * Birden çok sevk seçiliyse firma ortaktır, takip numarası sevk başınadır; hatalar sevk başına gösterilir.
 */
export default function DispatchDialog({ open, shipments, carriers: given, onClose, onDone }: DispatchDialogProps) {
    const [loaded, setLoaded] = useState<CarrierInfo[] | null>(null);
    const [carrierCode, setCarrierCode] = useState("");
    const [tracking, setTracking] = useState<Record<string, string>>({});
    const [vehicle, setVehicle] = useState("");
    const [driver, setDriver] = useState("");
    const [errors, setErrors] = useState<Record<string, string>>({});
    const [formError, setFormError] = useState<string | null>(null);
    const [doneIds, setDoneIds] = useState<string[]>([]);
    const [busy, setBusy] = useState(false);

    useEffect(() => {
        if (!open || given) return;
        let cancelled = false;
        erp.settings.get<ShippingSettings>("sevkiyat").then(
            (s) => !cancelled && setLoaded(s.value.carriers?.length ? s.value.carriers : DEFAULT_CARRIERS),
            () => !cancelled && setLoaded(DEFAULT_CARRIERS),
        );
        return () => {
            cancelled = true;
        };
    }, [open, given]);

    const carriers = (given ?? loaded ?? DEFAULT_CARRIERS).filter((c) => c.isActive);
    const own = carrierCode === OWN_VEHICLE;
    const pending = shipments.filter((s) => !doneIds.includes(s.id));

    async function submit() {
        setFormError(null);
        if (!carrierCode) return setFormError("Kargo firmasını seçin.");
        const missing: Record<string, string> = {};
        if (!own) for (const s of pending) if (!(tracking[s.id] ?? "").trim()) missing[s.id] = "Takip numarası zorunludur.";
        setErrors(missing);
        if (Object.keys(missing).length > 0) return;

        setBusy(true);
        const failures: Record<string, string> = {};
        const shipped: ShipmentDto[] = [];
        for (const s of pending) {
            try {
                shipped.push(
                    await shipping.shipments.ship(s.id, {
                        carrierCode,
                        trackingNo: own ? null : (tracking[s.id] ?? "").trim(),
                        dispatchAt: null,
                        vehicle: vehicle.trim() || null,
                        driver: driver.trim() || null,
                    }),
                );
            } catch (e) {
                if (!(e instanceof ApiError)) throw e;
                failures[s.id] = errorMessage(e);
            }
        }
        setBusy(false);
        setErrors(failures);
        if (shipped.length > 0) {
            setDoneIds((cur) => [...cur, ...shipped.map((x) => x.id)]);
            onDone(shipped);
        }
        if (Object.keys(failures).length === 0) {
            setDoneIds([]);
            setTracking({});
            onClose();
        }
    }

    return (
        <Dialog open={open} onClose={onClose} fullWidth maxWidth="sm">
            <DialogTitle>{shipments.length === 1 ? `Sevk et · ${shipments[0].number}` : `Toplu sevk · ${shipments.length} sevk`}</DialogTitle>
            <DialogContent>
                <Stack spacing={2} sx={{ pt: 1 }}>
                    {formError && <Alert severity="error">{formError}</Alert>}
                    <TextField select size="small" label="Kargo firması" value={carrierCode} onChange={(e) => setCarrierCode(e.target.value)} slotProps={nativeSelect("Kargo firması")}>
                        <option value="">Seçin</option>
                        {carriers.map((c) => (
                            <option key={c.code} value={c.code}>
                                {c.name}
                            </option>
                        ))}
                    </TextField>
                    {own ? (
                        <>
                            <TextField size="small" label="Araç" value={vehicle} onChange={(e) => setVehicle(e.target.value)} slotProps={{ htmlInput: { "aria-label": "Araç" } }} />
                            <TextField size="small" label="Sürücü" value={driver} onChange={(e) => setDriver(e.target.value)} slotProps={{ htmlInput: { "aria-label": "Sürücü" } }} />
                        </>
                    ) : (
                        pending.map((s) => (
                            <TextField
                                key={s.id}
                                size="small"
                                label={shipments.length === 1 ? "Takip numarası" : `${s.number} · ${s.customerName ?? ""}`}
                                value={tracking[s.id] ?? ""}
                                onChange={(e) => setTracking((cur) => ({ ...cur, [s.id]: e.target.value }))}
                                error={Boolean(errors[s.id])}
                                helperText={errors[s.id]}
                                slotProps={{ htmlInput: { "aria-label": `${s.number} takip numarası` } }}
                            />
                        ))
                    )}
                    {own &&
                        pending
                            .filter((s) => errors[s.id])
                            .map((s) => (
                                <Alert key={s.id} severity="error">
                                    {s.number}: {errors[s.id]}
                                </Alert>
                            ))}
                </Stack>
            </DialogContent>
            <DialogActions>
                <Button onClick={onClose}>Kapat</Button>
                <Button variant="contained" disabled={busy || pending.length === 0} onClick={() => void submit()}>
                    {busy ? "Sevk ediliyor..." : "Sevk et"}
                </Button>
            </DialogActions>
        </Dialog>
    );
}
