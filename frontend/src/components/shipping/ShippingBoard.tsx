"use client";

import { useCallback, useEffect, useMemo, useState } from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import Alert from "@mui/material/Alert";
import Box from "@mui/material/Box";
import Button from "@mui/material/Button";
import Checkbox from "@mui/material/Checkbox";
import Chip from "@mui/material/Chip";
import Paper from "@mui/material/Paper";
import Skeleton from "@mui/material/Skeleton";
import Stack from "@mui/material/Stack";
import TextField from "@mui/material/TextField";
import Typography from "@mui/material/Typography";
import FormAlert from "@/components/auth/FormAlert";
import PageHeader from "@/components/common/PageHeader";
import { ApiError } from "@/lib/api/client";
import { errorMessage } from "@/lib/api/errorMessages";
import { identity, type BranchDto } from "@/lib/api/identity";
import { sales, type ShipmentDto, type ShipmentStatus } from "@/lib/api/sales";
import { shipping } from "@/lib/api/shipping";
import { formatDate } from "@/lib/date";
import { useSession } from "@/session/SessionProvider";
import DispatchDialog from "./DispatchDialog";
import ShipmentActions from "./ShipmentActions";
import TrackingLink from "./TrackingLink";

const COLUMNS: { status: ShipmentStatus; title: string }[] = [
    { status: "draft", title: "Taslak" },
    { status: "picking", title: "Toplanıyor" },
    { status: "packed", title: "Paketlendi" },
    { status: "shipped", title: "Yolda" },
    { status: "delivered", title: "Teslim edildi" },
    { status: "deliveryFailed", title: "Teslim edilemedi" },
];

const MAX_CARDS = 200;

/**
 * Sevkiyat panosu: sevkleri durumuna göre kanban sütunlarında gösterir (tarih ve şube süzgeçli).
 * Taslak kartlar seçilip tek toplama listesine, paketlenmiş kartlar seçilip toplu sevke çevrilebilir.
 */
export default function ShippingBoard() {
    const router = useRouter();
    const { hasPermission } = useSession();
    const allowed = hasPermission("sevkiyat.read");
    const [cards, setCards] = useState<ShipmentDto[] | null>(null);
    const [error, setError] = useState<ApiError | string | null>(null);
    const [date, setDate] = useState("");
    const [branchId, setBranchId] = useState("");
    const [branches, setBranches] = useState<BranchDto[]>([]);
    const [selected, setSelected] = useState<string[]>([]);
    const [dispatching, setDispatching] = useState(false);
    const [busy, setBusy] = useState(false);

    const load = useCallback(async () => {
        try {
            // Liste satırları kalem/koli/takip taşımaz; kartlar için her sevkin ayrıntısı okunur.
            const { items } = await sales.shipments.list({ pageSize: MAX_CARDS });
            const open = items.filter((r) => r.status !== "cancelled" && r.status !== "returned");
            const details = await Promise.all(open.map((r) => sales.shipments.get(r.id).catch(() => null)));
            setCards(details.filter((d): d is ShipmentDto => d !== null));
            setError(null);
        } catch (e) {
            if (!(e instanceof ApiError)) throw e;
            setError(e);
            setCards((c) => c ?? []);
        }
    }, []);

    useEffect(() => {
        if (!allowed) return;
        // eslint-disable-next-line react-hooks/set-state-in-effect -- ilk yükleme
        void load();
        identity.branches.list().then(
            (b) => setBranches(b.filter((x) => x.isActive)),
            () => undefined,
        );
    }, [allowed, load]);

    const visible = useMemo(() => (cards ?? []).filter((c) => (!date || c.date === date) && (!branchId || c.branchId === branchId)), [cards, date, branchId]);
    const byId = useMemo(() => new Map(visible.map((c) => [c.id, c])), [visible]);
    const chosen = selected.map((id) => byId.get(id)).filter((c): c is ShipmentDto => c !== undefined);
    const allDraft = chosen.length > 0 && chosen.every((c) => c.status === "draft");
    const allPacked = chosen.length > 0 && chosen.every((c) => c.status === "packed");

    const replace = (updated: ShipmentDto) => {
        setCards((cur) => (cur ?? []).map((c) => (c.id === updated.id ? updated : c)));
        setSelected((cur) => cur.filter((id) => id !== updated.id));
    };
    const toggle = (id: string) => setSelected((cur) => (cur.includes(id) ? cur.filter((x) => x !== id) : [...cur, id]));

    async function pickList() {
        setBusy(true);
        setError(null);
        try {
            const list = await shipping.pickLists.batch(chosen.map((c) => c.id));
            router.push(`/sevkiyat/toplama/${list.id}`);
        } catch (e) {
            if (!(e instanceof ApiError)) throw e;
            setError(errorMessage(e));
        } finally {
            setBusy(false);
        }
    }

    if (!allowed) return <Alert severity="warning">Sevkiyat panosunu görüntülemek için yetkiniz yok.</Alert>;

    return (
        <>
            <PageHeader title="Sevkiyat panosu" subtitle="Toplama, paketleme, sevk ve teslim aşamalarındaki sevkler" />
            <Stack direction={{ xs: "column", md: "row" }} spacing={2} sx={{ mb: 2, alignItems: { md: "center" } }}>
                <TextField size="small" type="date" label="Tarih" value={date} onChange={(e) => setDate(e.target.value)} slotProps={{ inputLabel: { shrink: true } }} />
                {branches.length > 1 && (
                    <TextField
                        select
                        size="small"
                        label="Şube"
                        value={branchId}
                        onChange={(e) => setBranchId(e.target.value)}
                        sx={{ minWidth: 200 }}
                        slotProps={{ select: { native: true }, htmlInput: { "aria-label": "Şube" }, inputLabel: { shrink: true } }}
                    >
                        <option value="">Tüm şubeler</option>
                        {branches.map((b) => (
                            <option key={b.id} value={b.id}>
                                {b.name}
                            </option>
                        ))}
                    </TextField>
                )}
                <Box sx={{ flexGrow: 1 }} />
                {hasPermission("sevkiyat.pick") && (
                    <Button variant="outlined" disabled={!allDraft || busy} onClick={() => void pickList()}>
                        Toplama listesi
                    </Button>
                )}
                {hasPermission("sevkiyat.ship") && (
                    <Button variant="contained" disabled={!allPacked || busy} onClick={() => setDispatching(true)}>
                        Toplu sevk
                    </Button>
                )}
            </Stack>
            <FormAlert error={error} />
            {cards === null ? (
                <Skeleton variant="rounded" height={240} />
            ) : (
                <Box sx={{ display: "flex", gap: 2, overflowX: "auto", pb: 2, alignItems: "flex-start" }}>
                    {COLUMNS.map((col) => {
                        const items = visible.filter((c) => c.status === col.status);
                        return (
                            <Box key={col.status} component="section" aria-label={col.title} sx={{ minWidth: 280, width: 280, flexShrink: 0 }}>
                                <Stack direction="row" spacing={1} sx={{ alignItems: "center", mb: 1 }}>
                                    <Typography variant="h6">{col.title}</Typography>
                                    <Chip size="small" label={items.length} />
                                </Stack>
                                <Stack spacing={1.5}>
                                    {items.length === 0 && (
                                        <Typography variant="body2" color="text.secondary">
                                            Sevk yok.
                                        </Typography>
                                    )}
                                    {items.map((c) => (
                                        <Paper key={c.id} variant="outlined" sx={{ p: 1.5 }}>
                                            <Stack spacing={0.5}>
                                                <Stack direction="row" sx={{ alignItems: "center" }}>
                                                    {(c.status === "draft" || c.status === "packed") && (
                                                        <Checkbox size="small" sx={{ ml: -1 }} checked={selected.includes(c.id)} onChange={() => toggle(c.id)} slotProps={{ input: { "aria-label": `${c.number} seç` } }} />
                                                    )}
                                                    <Link href={`/satis/sevkler/${c.id}`}>{c.number}</Link>
                                                </Stack>
                                                <Typography variant="subtitle2">{c.customerName}</Typography>
                                                <Typography variant="caption" color="text.secondary">
                                                    {formatDate(c.date)} · Sipariş {c.orderNumber}
                                                </Typography>
                                                <Stack direction="row" spacing={1} sx={{ flexWrap: "wrap" }}>
                                                    <Typography variant="body2">{`${c.lines.length} kalem`}</Typography>
                                                    {c.parcels.length > 0 && <Typography variant="body2">{`${c.parcels.length} koli`}</Typography>}
                                                </Stack>
                                                {c.carrierName && <Typography variant="caption">{c.carrierName}</Typography>}
                                                <TrackingLink trackingNo={c.trackingNo} url={c.trackingUrl} />
                                                {c.customerEmailMissing && c.status === "packed" && (
                                                    <Typography variant="caption" color="warning.main">
                                                        Müşterinin e-postası yok; sevk bildirimi gitmeyecek.
                                                    </Typography>
                                                )}
                                                <ShipmentActions shipment={c} onChange={replace} />
                                            </Stack>
                                        </Paper>
                                    ))}
                                </Stack>
                            </Box>
                        );
                    })}
                </Box>
            )}
            <DispatchDialog
                open={dispatching}
                shipments={chosen.map((c) => ({ id: c.id, number: c.number, customerName: c.customerName }))}
                onClose={() => setDispatching(false)}
                onDone={(shipped) => shipped.forEach(replace)}
            />
        </>
    );
}
