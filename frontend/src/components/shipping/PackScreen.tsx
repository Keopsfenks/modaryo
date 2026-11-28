"use client";

import { useEffect, useMemo, useState } from "react";
import { useRouter } from "next/navigation";
import Alert from "@mui/material/Alert";
import Box from "@mui/material/Box";
import Button from "@mui/material/Button";
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
import { sales, type ShipmentDto } from "@/lib/api/sales";
import { computeDesi, shipping, type ParcelInput } from "@/lib/api/shipping";
import { useSession } from "@/session/SessionProvider";
import DispatchDialog from "./DispatchDialog";

type DraftParcel = { key: number; weight: string; width: string; length: string; height: string; contents: Record<string, number> };

const num = (s: string) => {
    const n = Number(s.trim().replace(",", "."));
    return Number.isFinite(n) ? n : 0;
};
const fmt = (n: number) => n.toLocaleString("tr-TR", { maximumFractionDigits: 4 });
const fmtDesi = (n: number) => n.toLocaleString("tr-TR", { minimumFractionDigits: 1, maximumFractionDigits: 1 });
const round = (n: number) => Math.round(n * 1e4) / 1e4;
const asText = (n: number) => (n ? String(n).replace(".", ",") : "");

function initialParcels(shipment: ShipmentDto): DraftParcel[] {
    if (shipment.parcels.length === 0) return [{ key: 1, weight: "", width: "", length: "", height: "", contents: {} }];
    return shipment.parcels.map((p, i) => ({
        key: i + 1,
        weight: asText(p.weightKg),
        width: asText(p.widthCm),
        length: asText(p.lengthCm),
        height: asText(p.heightCm),
        contents: Object.fromEntries(p.contents.map((c) => [c.shipmentLineId, c.quantity])),
    }));
}

/**
 * Paketleme ekranı: koli kartlarında ağırlık ve ebat girilir, desi canlı hesaplanır (en x boy x yükseklik / 3000, 0,1'e yukarı).
 * Kalemler koliye sürüklenerek ya da seçilip "Koliye ekle" ile yerleştirilir; kaydedince sevk "Paketlendi" olur.
 */
export default function PackScreen({ shipmentId }: { shipmentId: string }) {
    const router = useRouter();
    const { hasPermission } = useSession();
    const allowed = hasPermission("sevkiyat.read");
    const [shipment, setShipment] = useState<ShipmentDto | null>(null);
    const [parcels, setParcels] = useState<DraftParcel[]>([]);
    const [selected, setSelected] = useState<string | null>(null);
    const [error, setError] = useState<ApiError | string | null>(null);
    const [saved, setSaved] = useState(false);
    const [busy, setBusy] = useState(false);
    const [dispatching, setDispatching] = useState(false);

    useEffect(() => {
        if (!allowed) return;
        let cancelled = false;
        sales.shipments.get(shipmentId).then(
            (s) => {
                if (cancelled) return;
                setShipment(s);
                setParcels(initialParcels(s));
            },
            (e: unknown) => {
                if (cancelled) return;
                if (!(e instanceof ApiError)) throw e;
                setError(e);
            },
        );
        return () => {
            cancelled = true;
        };
    }, [allowed, shipmentId]);

    const remaining = useMemo(() => {
        const map: Record<string, number> = {};
        for (const l of (shipment?.lines ?? []).filter((x) => !x.isService)) map[l.id] = l.quantity;
        for (const p of parcels) for (const [id, q] of Object.entries(p.contents)) map[id] = round((map[id] ?? 0) - q);
        return map;
    }, [shipment, parcels]);

    if (!allowed) return <Alert severity="warning">Paketleme ekranını görüntülemek için yetkiniz yok.</Alert>;
    if (!shipment) return error ? <FormAlert error={error} /> : <Skeleton variant="rounded" height={240} />;

    const editable = hasPermission("sevkiyat.pack") && (shipment.status === "picking" || shipment.status === "packed");
    const desiOf = (p: DraftParcel) => computeDesi(num(p.width), num(p.length), num(p.height));
    const totalDesi = round(parcels.reduce((s, p) => s + desiOf(p), 0) * 10) / 10;
    const totalWeight = round(parcels.reduce((s, p) => s + num(p.weight), 0));
    const lineName = (id: string) => shipment.lines.find((l) => l.id === id)?.description ?? "";

    const patch = (key: number, change: Partial<DraftParcel>) => {
        setSaved(false);
        setParcels((cur) => cur.map((p) => (p.key === key ? { ...p, ...change } : p)));
    };
    const addTo = (key: number, lineId: string) => {
        const left = remaining[lineId] ?? 0;
        if (left <= 0) return;
        setSaved(false);
        setParcels((cur) => cur.map((p) => (p.key === key ? { ...p, contents: { ...p.contents, [lineId]: round((p.contents[lineId] ?? 0) + left) } } : p)));
    };
    const removeFrom = (key: number, lineId: string) =>
        setParcels((cur) =>
            cur.map((p) => {
                if (p.key !== key) return p;
                const next = { ...p.contents };
                delete next[lineId];
                return { ...p, contents: next };
            }),
        );

    async function save() {
        setError(null);
        setSaved(false);
        const open = shipment!.lines.filter((l) => !l.isService).filter((l) => (remaining[l.id] ?? 0) > 0);
        if (open.length > 0) return setError(`Bazı kalemler koliye yerleştirilmemiş: ${open.map((l) => `${l.description} (${fmt(remaining[l.id])})`).join(", ")}.`);
        if (shipment!.lines.some((l) => (remaining[l.id] ?? 0) < 0)) return setError("Bir kalem sevk miktarından fazla koliye konmuş.");
        for (const [i, p] of parcels.entries()) {
            if (Object.keys(p.contents).length === 0) return setError(`Koli ${i + 1} boş; kalem ekleyin ya da koliyi silin.`);
            if (num(p.weight) <= 0 || num(p.width) <= 0 || num(p.length) <= 0 || num(p.height) <= 0) return setError(`Koli ${i + 1} için ağırlık ve ebatları girin.`);
        }
        const body: ParcelInput[] = parcels.map((p) => ({
            weightKg: num(p.weight),
            widthCm: num(p.width),
            lengthCm: num(p.length),
            heightCm: num(p.height),
            contents: Object.entries(p.contents).map(([shipmentLineId, qty]) => ({ shipmentLineId, qty })),
        }));
        setBusy(true);
        try {
            setShipment(await shipping.shipments.parcels(shipment!.id, body));
            setSaved(true);
        } catch (e) {
            if (!(e instanceof ApiError)) throw e;
            setError(errorMessage(e));
        } finally {
            setBusy(false);
        }
    }

    return (
        <>
            <PageHeader title={`Paketleme ${shipment.number}`} subtitle={`${shipment.customerName} · Sipariş ${shipment.orderNumber}`} />
            <Stack spacing={2}>
                <FormAlert error={error} />
                {!editable && <Alert severity="info">Bu sevk paketlenebilir durumda değil ya da paketleme yetkiniz yok.</Alert>}
                {saved && <Alert severity="success">Paketleme kaydedildi; sevk hazır.</Alert>}
                <Paper variant="outlined" sx={{ p: 2 }}>
                    <Typography variant="subtitle1" sx={{ mb: 1 }}>
                        Kalemler
                    </Typography>
                    <Stack direction="row" spacing={1} sx={{ flexWrap: "wrap", gap: 1 }}>
                        {shipment.lines.filter((l) => !l.isService).map((l) => {
                            const left = remaining[l.id] ?? 0;
                            return (
                                <Chip
                                    key={l.id}
                                    component="button"
                                    clickable={editable}
                                    color={selected === l.id ? "primary" : left <= 0 ? "success" : "default"}
                                    variant={selected === l.id ? "filled" : "outlined"}
                                    draggable={editable && left > 0}
                                    onDragStart={(e) => e.dataTransfer.setData("text/plain", l.id)}
                                    onClick={() => editable && setSelected((cur) => (cur === l.id ? null : l.id))}
                                    aria-label={`${l.description} kalemini seç`}
                                    aria-pressed={selected === l.id}
                                    label={`${l.description} · kalan ${fmt(left)} / ${fmt(l.quantity)} ${l.unit}`}
                                    sx={{ minHeight: 40 }}
                                />
                            );
                        })}
                    </Stack>
                </Paper>

                <Box sx={{ display: "grid", gridTemplateColumns: { xs: "1fr", md: "repeat(2, 1fr)" }, gap: 2 }}>
                    {parcels.map((p, i) => {
                        const n = i + 1;
                        const field = (label: string, key: "weight" | "width" | "length" | "height", unit: string) => (
                            <TextField
                                size="small"
                                label={`${label} (${unit})`}
                                value={p[key]}
                                disabled={!editable}
                                onChange={(e) => patch(p.key, { [key]: e.target.value })}
                                slotProps={{ htmlInput: { inputMode: "decimal", "aria-label": `Koli ${n} ${label.toLowerCase()} (${unit})` } }}
                            />
                        );
                        return (
                            <Paper
                                key={p.key}
                                variant="outlined"
                                sx={{ p: 2 }}
                                onDragOver={(e) => editable && e.preventDefault()}
                                onDrop={(e) => {
                                    e.preventDefault();
                                    const lineId = e.dataTransfer.getData("text/plain");
                                    if (editable && lineId) addTo(p.key, lineId);
                                }}
                            >
                                <Stack spacing={1.5}>
                                    <Stack direction="row" sx={{ alignItems: "center", justifyContent: "space-between" }}>
                                        <Typography variant="h6">Koli {n}</Typography>
                                        <Typography variant="subtitle1" data-testid={`parcel-${n}-desi`}>
                                            {`${fmtDesi(desiOf(p))} desi`}
                                        </Typography>
                                    </Stack>
                                    <Stack direction="row" spacing={1}>
                                        {field("Ağırlık", "weight", "kg")}
                                        {field("En", "width", "cm")}
                                        {field("Boy", "length", "cm")}
                                        {field("Yükseklik", "height", "cm")}
                                    </Stack>
                                    <Stack spacing={0.5}>
                                        {Object.entries(p.contents).map(([lineId, qty]) => (
                                            <Stack key={lineId} direction="row" sx={{ alignItems: "center", justifyContent: "space-between" }}>
                                                <Typography variant="body2">{`${lineName(lineId)} × ${fmt(qty)}`}</Typography>
                                                {editable && (
                                                    <Button size="small" color="error" aria-label={`${lineName(lineId)} çıkar (Koli ${n})`} onClick={() => removeFrom(p.key, lineId)}>
                                                        Çıkar
                                                    </Button>
                                                )}
                                            </Stack>
                                        ))}
                                        {Object.keys(p.contents).length === 0 && (
                                            <Typography variant="body2" color="text.secondary">
                                                Kalemleri buraya sürükleyin ya da kalemi seçip &quot;Koliye ekle&quot; düğmesine basın.
                                            </Typography>
                                        )}
                                    </Stack>
                                    {editable && (
                                        <Stack direction="row" spacing={1}>
                                            <Button size="small" variant="outlined" aria-label={`Koliye ekle (Koli ${n})`} disabled={!selected || (remaining[selected] ?? 0) <= 0} onClick={() => selected && addTo(p.key, selected)}>
                                                Koliye ekle
                                            </Button>
                                            {parcels.length > 1 && (
                                                <Button
                                                    size="small"
                                                    color="error"
                                                    aria-label={`Koliyi sil (Koli ${n})`}
                                                    onClick={() => setParcels((cur) => cur.filter((x) => x.key !== p.key))}
                                                >
                                                    Koliyi sil
                                                </Button>
                                            )}
                                        </Stack>
                                    )}
                                </Stack>
                            </Paper>
                        );
                    })}
                </Box>

                <Stack direction="row" spacing={2} sx={{ alignItems: "center", flexWrap: "wrap", rowGap: 1 }}>
                    {editable && (
                        <Button variant="outlined" onClick={() => setParcels((cur) => [...cur, { key: Math.max(0, ...cur.map((x) => x.key)) + 1, weight: "", width: "", length: "", height: "", contents: {} }])}>
                            Koli ekle
                        </Button>
                    )}
                    <Typography>
                        Toplam: {parcels.length} koli · {fmt(totalWeight)} kg · <span data-testid="total-desi">{fmtDesi(totalDesi)}</span> desi
                    </Typography>
                    <Box sx={{ flexGrow: 1 }} />
                    {editable && (
                        <Button variant="contained" disabled={busy} onClick={() => void save()}>
                            Paketlemeyi kaydet
                        </Button>
                    )}
                    {shipment.status === "packed" && hasPermission("sevkiyat.ship") && (
                        <Button variant="contained" color="success" onClick={() => setDispatching(true)}>
                            Sevk et
                        </Button>
                    )}
                </Stack>
            </Stack>
            <DispatchDialog
                open={dispatching}
                shipments={[{ id: shipment.id, number: shipment.number, customerName: shipment.customerName }]}
                onClose={() => setDispatching(false)}
                onDone={() => router.push("/sevkiyat")}
            />
        </>
    );
}
