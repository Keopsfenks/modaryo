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
import { inventory, type StockDocumentKind, type WarehouseDto } from "@/lib/api/inventory";
import { useSession } from "@/session/SessionProvider";
import LineItemsGrid, { applyServerLineError, toDocumentLine, validateLines, type LineDraft } from "./LineItemsGrid";

const KIND_LABELS: Record<StockDocumentKind, string> = { receipt: "Giriş fişi", issue: "Çıkış fişi", opening: "Açılış fişi" };

const today = (): string => new Date().toISOString().slice(0, 10);

/** Stok fişi (giriş, çıkış, açılış) oluşturma formu. Yetersiz stok gibi sunucu hataları ilgili satırda gösterilir. */
export default function StockDocumentForm() {
    const router = useRouter();
    const { hasPermission } = useSession();
    const canManage = hasPermission("stok.manage");
    const showCost = hasPermission("maliyet.read");

    const [warehouses, setWarehouses] = useState<WarehouseDto[]>([]);
    const [kind, setKind] = useState<StockDocumentKind>("receipt");
    const [date, setDate] = useState(today());
    const [warehouseId, setWarehouseId] = useState("");
    const [reason, setReason] = useState("");
    const [lines, setLines] = useState<LineDraft[]>([]);
    const [error, setError] = useState<ApiError | string | null>(null);
    const [busy, setBusy] = useState(false);

    useEffect(() => {
        if (!canManage) return;
        inventory.warehouses().then((list) => {
            const active = list.filter((w) => w.isActive);
            setWarehouses(active);
            setWarehouseId((current) => current || (active.find((w) => w.isDefault) ?? active[0])?.id || "");
        }, () => undefined);
    }, [canManage]);

    if (!canManage) return <Alert severity="warning">Stok fişi oluşturmak için yetkiniz yok.</Alert>;

    const mode = kind === "issue" ? "out" : "in";

    async function submit(event: React.FormEvent) {
        event.preventDefault();
        if (busy) return;
        setError(null);
        if (!warehouseId) return setError("Depo seçilmelidir.");
        if (lines.length === 0) return setError("En az bir satır eklenmelidir.");
        const found = validateLines(lines, mode === "in");
        if (Object.keys(found).length > 0) {
            setLines(lines.map((l) => ({ ...l, error: found[l.key] ?? null })));
            return setError("Satırlardaki hataları düzeltin.");
        }

        setBusy(true);
        try {
            const created = await inventory.createDocument({
                kind,
                date,
                warehouseId,
                reason: reason.trim() || null,
                lines: lines.map((l) => toDocumentLine(l, mode === "in" && showCost)),
            });
            router.push(`/stok/fisler?belge=${created.id}`);
        } catch (e) {
            if (!(e instanceof ApiError)) throw e;
            const applied = applyServerLineError(lines, e.message);
            setLines(applied.matched ? applied.lines : lines.map((l) => ({ ...l, error: null })));
            setError(applied.matched ? "Bazı satırlarda hata var." : errorMessage(e));
        } finally {
            setBusy(false);
        }
    }

    return (
        <>
            <PageHeader title="Yeni stok fişi" subtitle="Giriş, çıkış ya da açılış fişi; belge ve hareketler tek işlemde yazılır." />
            <DashboardCard>
                <form noValidate onSubmit={submit}>
                    <Stack spacing={2}>
                        <FormAlert error={error} />
                        <Stack direction={{ xs: "column", md: "row" }} spacing={2}>
                            <TextField select size="small" label="Fiş türü" value={kind} onChange={(e) => setKind(e.target.value as StockDocumentKind)} sx={{ minWidth: 180 }}>
                                {(Object.keys(KIND_LABELS) as StockDocumentKind[]).map((k) => (
                                    <MenuItem key={k} value={k}>
                                        {KIND_LABELS[k]}
                                    </MenuItem>
                                ))}
                            </TextField>
                            <TextField size="small" type="date" label="Tarih" value={date} onChange={(e) => setDate(e.target.value)} slotProps={{ inputLabel: { shrink: true } }} />
                            <TextField select size="small" label="Depo" value={warehouseId} onChange={(e) => setWarehouseId(e.target.value)} sx={{ minWidth: 220 }}>
                                {warehouses.map((w) => (
                                    <MenuItem key={w.id} value={w.id}>
                                        {w.name}
                                    </MenuItem>
                                ))}
                            </TextField>
                            <TextField size="small" label="Açıklama" value={reason} onChange={(e) => setReason(e.target.value)} sx={{ flexGrow: 1 }} />
                        </Stack>
                        <LineItemsGrid lines={lines} onChange={setLines} showCost={showCost} warehouseId={warehouseId} mode={mode} />
                        <div>
                            <Button type="submit" variant="contained" disabled={busy}>
                                {busy ? "Kaydediliyor..." : "Kaydet"}
                            </Button>
                        </div>
                    </Stack>
                </form>
            </DashboardCard>
        </>
    );
}
