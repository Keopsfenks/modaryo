"use client";

import { useEffect, useState } from "react";
import { useRouter } from "next/navigation";
import Alert from "@mui/material/Alert";
import Button from "@mui/material/Button";
import MenuItem from "@mui/material/MenuItem";
import Stack from "@mui/material/Stack";
import TextField from "@mui/material/TextField";
import FormAlert from "@/components/auth/FormAlert";
import PageHeader from "@/components/common/PageHeader";
import DashboardCard from "@/components/dashboard/DashboardCard";
import { ApiError } from "@/lib/api/client";
import { errorMessage } from "@/lib/api/errorMessages";
import { WAREHOUSE_KIND_LABELS, inventory, type WarehouseDto } from "@/lib/api/inventory";
import { useSession } from "@/session/SessionProvider";
import LineItemsGrid, { applyServerLineError, toDocumentLine, validateLines, type LineDraft } from "./LineItemsGrid";

const today = (): string => new Date().toISOString().slice(0, 10);

/** Depodan depoya transfer. Hasarlı ya da karantina depoya geçiş de bu formla yapılır. */
export default function TransferForm() {
    const router = useRouter();
    const { hasPermission } = useSession();
    const canManage = hasPermission("stok.manage");

    const [warehouses, setWarehouses] = useState<WarehouseDto[]>([]);
    const [from, setFrom] = useState("");
    const [to, setTo] = useState("");
    const [date, setDate] = useState(today());
    const [note, setNote] = useState("");
    const [lines, setLines] = useState<LineDraft[]>([]);
    const [error, setError] = useState<ApiError | string | null>(null);
    const [busy, setBusy] = useState(false);

    useEffect(() => {
        if (!canManage) return;
        inventory.warehouses().then((list) => {
            const active = list.filter((w) => w.isActive);
            setWarehouses(active);
            setFrom((current) => current || (active.find((w) => w.isDefault) ?? active[0])?.id || "");
        }, () => undefined);
    }, [canManage]);

    if (!canManage) return <Alert severity="warning">Transfer oluşturmak için yetkiniz yok.</Alert>;

    async function submit(event: React.FormEvent) {
        event.preventDefault();
        if (busy) return;
        setError(null);
        if (!from || !to) return setError("Kaynak ve hedef depo seçilmelidir.");
        if (from === to) return setError(errorMessage(new ApiError(400, "", "same_warehouse")));
        if (lines.length === 0) return setError("En az bir satır eklenmelidir.");
        const found = validateLines(lines, false);
        if (Object.keys(found).length > 0) {
            setLines(lines.map((l) => ({ ...l, error: found[l.key] ?? null })));
            return setError("Satırlardaki hataları düzeltin.");
        }

        setBusy(true);
        try {
            const created = await inventory.createTransfer({
                date,
                fromWarehouseId: from,
                toWarehouseId: to,
                note: note.trim() || null,
                lines: lines.map((l) => toDocumentLine(l, false)),
            });
            router.push(`/stok/fisler?transfer=${created.id}`);
        } catch (e) {
            if (!(e instanceof ApiError)) throw e;
            const applied = applyServerLineError(lines, e.message);
            setLines(applied.matched ? applied.lines : lines.map((l) => ({ ...l, error: null })));
            setError(applied.matched ? "Bazı satırlarda hata var." : errorMessage(e));
        } finally {
            setBusy(false);
        }
    }

    const label = (w: WarehouseDto) => (w.kind === "normal" ? w.name : `${w.name} (${WAREHOUSE_KIND_LABELS[w.kind]})`);

    return (
        <>
            <PageHeader title="Yeni transfer" subtitle="Kaynak depodan çıkış ve hedef depoya giriş birlikte yazılır; maliyet kaynaktan taşınır." />
            <DashboardCard>
                <form noValidate onSubmit={submit}>
                    <Stack spacing={2}>
                        <FormAlert error={error} />
                        <Stack direction={{ xs: "column", md: "row" }} spacing={2}>
                            <TextField select size="small" label="Kaynak depo" value={from} onChange={(e) => setFrom(e.target.value)} sx={{ minWidth: 220 }}>
                                {warehouses.map((w) => (
                                    <MenuItem key={w.id} value={w.id}>
                                        {label(w)}
                                    </MenuItem>
                                ))}
                            </TextField>
                            <TextField select size="small" label="Hedef depo" value={to} onChange={(e) => setTo(e.target.value)} sx={{ minWidth: 220 }}>
                                {warehouses.map((w) => (
                                    <MenuItem key={w.id} value={w.id}>
                                        {label(w)}
                                    </MenuItem>
                                ))}
                            </TextField>
                            <TextField size="small" type="date" label="Tarih" value={date} onChange={(e) => setDate(e.target.value)} slotProps={{ inputLabel: { shrink: true } }} />
                            <TextField size="small" label="Not" value={note} onChange={(e) => setNote(e.target.value)} sx={{ flexGrow: 1 }} />
                        </Stack>
                        <LineItemsGrid lines={lines} onChange={setLines} showCost={false} warehouseId={from} mode="out" />
                        <div>
                            <Button type="submit" variant="contained" disabled={busy}>
                                {busy ? "Kaydediliyor..." : "Transfer et"}
                            </Button>
                        </div>
                    </Stack>
                </form>
            </DashboardCard>
        </>
    );
}
