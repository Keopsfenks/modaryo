"use client";

import { useCallback, useEffect, useRef, useState } from "react";
import { useRouter } from "next/navigation";
import { IconVolume, IconVolumeOff } from "@tabler/icons-react";
import Alert from "@mui/material/Alert";
import Box from "@mui/material/Box";
import Button from "@mui/material/Button";
import Chip from "@mui/material/Chip";
import Dialog from "@mui/material/Dialog";
import DialogActions from "@mui/material/DialogActions";
import DialogContent from "@mui/material/DialogContent";
import DialogTitle from "@mui/material/DialogTitle";
import IconButton from "@mui/material/IconButton";
import LinearProgress from "@mui/material/LinearProgress";
import Paper from "@mui/material/Paper";
import Skeleton from "@mui/material/Skeleton";
import Stack from "@mui/material/Stack";
import TextField from "@mui/material/TextField";
import Typography from "@mui/material/Typography";
import FormAlert from "@/components/auth/FormAlert";
import LotSerialDialog from "@/components/inventory/LotSerialDialog";
import PageHeader from "@/components/common/PageHeader";
import { ApiError } from "@/lib/api/client";
import { errorMessage } from "@/lib/api/errorMessages";
import { shipping, type PickLineDto, type PickListDto } from "@/lib/api/shipping";
import { useSession } from "@/session/SessionProvider";
import { beep } from "./pickSound";

const fmt = (n: number) => n.toLocaleString("tr-TR", { maximumFractionDigits: 4 });
const round = (n: number) => Math.round(n * 1e4) / 1e4;
const picked = (l: PickLineDto) => l.pickedQuantity ?? 0;
const isSerial = (l: PickLineDto) => l.trackingMode === "serial";
const same = (a: string, b: string | null | undefined) => !!b && a.toLowerCase() === b.toLowerCase();

/** Okutulan değerin satırla eşleşmesi: barkod (ürün, varyant, birim) ya da ürün/varyant kodu. Birim barkodu, satır birimine çevrilmiş adımı döndürür. */
function matchStep(l: PickLineDto, code: string): number | null {
    const hit = l.barcodes.find((b) => same(code, b.value));
    if (hit) return hit.kind === "unit" && l.factor > 0 ? round(hit.unitFactor / l.factor) : 1;
    return same(code, l.code) || same(code, l.variantCode) ? 1 : null;
}

/**
 * Toplama ekranı (tablet öncelikli): büyük satırlar, her zaman odakta bir okutma alanı (klavye kamasıyla çalışan barkod okuyucu).
 * Okutulan kod satırın kodunu bulursa toplanan miktar 1 artar; listede olmayan ya da dolu kalem hata sesi ve uyarı verir.
 * Eksik toplama tamamlanmadan önce onay ister; fark siparişin açık miktarında kalır.
 */
export default function PickScreen({ pickListId }: { pickListId: string }) {
    const router = useRouter();
    const { hasPermission } = useSession();
    const allowed = hasPermission("sevkiyat.read");
    const canPick = hasPermission("sevkiyat.pick");
    const [list, setList] = useState<PickListDto | null>(null);
    const [error, setError] = useState<ApiError | string | null>(null);
    const [scanError, setScanError] = useState<string | null>(null);
    const [lastScan, setLastScan] = useState<string | null>(null);
    const [scan, setScan] = useState("");
    const [muted, setMuted] = useState(false);
    const [confirm, setConfirm] = useState(false);
    const [busy, setBusy] = useState(false);
    const [serialLine, setSerialLine] = useState<string | null>(null);
    const [serialScan, setSerialScan] = useState<Record<string, string>>({});
    const inputRef = useRef<HTMLInputElement>(null);
    // Okutmalar peş peşe gelebilir: en güncel liste ve sıra bir ref'te tutulur.
    const latest = useRef<PickListDto | null>(null);
    const queue = useRef<Promise<void>>(Promise.resolve());
    const mutedRef = useRef(false);

    const apply = useCallback((next: PickListDto) => {
        latest.current = next;
        setList(next);
    }, []);

    useEffect(() => {
        mutedRef.current = muted;
    }, [muted]);

    useEffect(() => {
        if (!allowed) return;
        let cancelled = false;
        shipping.pickLists.get(pickListId).then(
            (l) => !cancelled && apply(l),
            (e: unknown) => {
                if (cancelled) return;
                if (!(e instanceof ApiError)) throw e;
                setError(e);
            },
        );
        return () => {
            cancelled = true;
        };
    }, [allowed, pickListId, apply]);

    const refocus = () => inputRef.current?.focus();
    const sound = (kind: "error" | "ok") => {
        if (!mutedRef.current) beep(kind);
    };

    const setPicked = useCallback(
        async (line: PickLineDto, quantity: number) => {
            const next = await shipping.pickLists.updateLine(pickListId, line.id, { pickedQty: Math.min(Math.max(0, round(quantity)), line.quantity) });
            apply(next);
        },
        [pickListId, apply],
    );

    // Seri takipli satırda miktar seri numaralarının sayısıdır; her değişiklik tüm seri listesini gönderir.
    const setSerials = useCallback(
        async (line: PickLineDto, serials: string[]) => {
            apply(await shipping.pickLists.updateLine(pickListId, line.id, { pickedQty: serials.length, serials }));
        },
        [pickListId, apply],
    );

    function guarded(action: () => Promise<void>) {
        queue.current = queue.current.then(async () => {
            try {
                await action();
            } catch (e) {
                if (!(e instanceof ApiError)) throw e;
                sound("error");
                setScanError(errorMessage(e));
            } finally {
                refocus();
            }
        });
    }

    function onScan(raw: string) {
        const code = raw.trim();
        setScan("");
        if (!code || !canPick) return;
        guarded(async () => {
            const current = latest.current;
            if (!current) return;
            const matches = current.lines.filter((l) => matchStep(l, code) !== null);
            if (matches.length === 0) {
                sound("error");
                setLastScan(null);
                return setScanError(`Bu kalem toplama listesinde yok: ${code}`);
            }
            const line = matches.find((l) => picked(l) < l.quantity);
            if (!line) {
                sound("error");
                return setScanError(`${matches[0].description} için gereken miktar zaten toplandı.`);
            }
            if (isSerial(line)) {
                // Seri takipli kalem barkodla sayılmaz: seri numarası okutulur.
                setLastScan(null);
                document.getElementById(`serial-scan-${line.id}`)?.focus();
                sound("ok");
                return setScanError(`${line.description} seri takipli: seri numarasını okutun.`);
            }
            setScanError(null);
            await setPicked(line, picked(line) + (matchStep(line, code) ?? 1));
            setLastScan(line.description);
            sound("ok");
        });
    }

    function change(line: PickLineDto, quantity: number) {
        setScanError(null);
        if (isSerial(line)) {
            const current = line.pickedSerials ?? [];
            if (quantity <= 0) return guarded(() => setSerials(line, []));
            if (quantity < current.length) return guarded(() => setSerials(line, current.slice(0, quantity)));
            return setSerialLine(line.id);
        }
        guarded(() => setPicked(line, quantity));
    }

    function addSerial(line: PickLineDto, raw: string) {
        const serial = raw.trim();
        setSerialScan((cur) => ({ ...cur, [line.id]: "" }));
        if (!serial) return;
        guarded(async () => {
            const fresh = latest.current?.lines.find((l) => l.id === line.id) ?? line;
            const current = fresh.pickedSerials ?? [];
            if (current.some((x) => same(serial, x))) {
                sound("error");
                return setScanError(`${serial} seri numarası zaten okutuldu.`);
            }
            if (current.length >= fresh.quantity) {
                sound("error");
                return setScanError(`${fresh.description} için gereken miktar zaten toplandı.`);
            }
            setScanError(null);
            await setSerials(fresh, [...current, serial]);
            setLastScan(`${fresh.description} (${serial})`);
            sound("ok");
        });
    }

    async function complete(force: boolean) {
        setBusy(true);
        setError(null);
        try {
            const done = await shipping.pickLists.complete(pickListId, force);
            setConfirm(false);
            apply(done);
            router.push(done.shipments.length === 1 ? `/sevkiyat/paketleme/${done.shipments[0].id}` : "/sevkiyat");
        } catch (e) {
            if (!(e instanceof ApiError)) throw e;
            setConfirm(false);
            setError(errorMessage(e));
        } finally {
            setBusy(false);
        }
    }

    if (!allowed) return <Alert severity="warning">Toplama listesini görüntülemek için yetkiniz yok.</Alert>;
    if (!list) return error ? <FormAlert error={error} /> : <Skeleton variant="rounded" height={240} />;

    const editable = canPick && list.status === "open";
    const serialTarget = serialLine ? (list.lines.find((l) => l.id === serialLine) ?? null) : null;
    const shortages = list.lines.filter((l) => picked(l) < l.quantity);
    const total = list.lines.reduce((s, l) => s + l.quantity, 0);
    const done = list.lines.reduce((s, l) => s + Math.min(picked(l), l.quantity), 0);

    return (
        <>
            <PageHeader title={`Toplama ${list.number}`} subtitle={`${list.warehouseName} · ${list.shipments.map((s) => `${s.number} (${s.customerName})`).join(", ")}`} />
            <Stack spacing={2}>
                <FormAlert error={error} />
                {list.status !== "open" && <Alert severity="info">Bu toplama listesi {list.status === "completed" ? "tamamlandı" : "iptal edildi"}.</Alert>}
                <Stack direction="row" spacing={2} sx={{ alignItems: "center" }}>
                    <TextField
                        fullWidth
                        autoFocus
                        inputRef={inputRef}
                        label="Barkod / kod okut"
                        value={scan}
                        disabled={!editable}
                        onChange={(e) => setScan(e.target.value)}
                        onKeyDown={(e) => {
                            if (e.key === "Enter") {
                                e.preventDefault();
                                onScan(scan);
                            }
                        }}
                        onBlur={() => {
                            // Dokunmatik kullanımda odak kaybolursa, açık pencere yoksa okutma alanına döner.
                            if (!confirm && editable)
                                setTimeout(() => {
                                    const active = document.activeElement;
                                    if (!active || active === document.body) refocus();
                                }, 0);
                        }}
                        slotProps={{ htmlInput: { autoComplete: "off", inputMode: "none", style: { fontSize: "1.5rem" } } }}
                    />
                    <IconButton size="large" aria-label={muted ? "Sesi aç" : "Sesi kapat"} aria-pressed={muted} onClick={() => setMuted((m) => !m)}>
                        {muted ? <IconVolumeOff size={32} /> : <IconVolume size={32} />}
                    </IconButton>
                </Stack>
                {scanError && (
                    <Alert severity="error" sx={{ fontSize: "1.1rem" }}>
                        {scanError}
                    </Alert>
                )}
                {!scanError && lastScan && (
                    <Alert severity="success" role="status">
                        {lastScan} toplandı.
                    </Alert>
                )}
                <Box>
                    <Typography variant="body2" color="text.secondary">
                        {`Toplam ${fmt(done)} / ${fmt(total)}`}
                    </Typography>
                    <LinearProgress variant="determinate" value={total === 0 ? 0 : (done / total) * 100} sx={{ height: 10, borderRadius: 1 }} />
                </Box>
                <Stack spacing={1.5}>
                    {list.lines.map((l) => {
                        const full = picked(l) >= l.quantity;
                        return (
                            <Paper key={l.id} variant="outlined" sx={{ p: 2, minHeight: 88, borderColor: full ? "success.main" : "divider", borderWidth: full ? 2 : 1 }}>
                                <Stack direction="row" spacing={2} sx={{ alignItems: "center" }}>
                                    <Box sx={{ flexGrow: 1, minWidth: 0 }}>
                                        <Typography variant="h6">{l.description}</Typography>
                                        <Stack direction="row" spacing={1} sx={{ flexWrap: "wrap", rowGap: 0.5 }}>
                                            <Chip size="small" label={l.code} />
                                            {l.variantLabel && <Chip size="small" variant="outlined" label={l.variantLabel} />}
                                            {isSerial(l) && l.pickedSerials?.map((sn) => <Chip key={sn} size="small" color="success" variant="outlined" label={`SN ${sn}`} />)}
                                            {l.location && <Chip size="small" variant="outlined" label={`Konum: ${l.location}`} />}
                                            {l.suggestedLots?.map((lot) => (
                                                <Chip key={lot.lotNo} size="small" variant="outlined" label={`Lot ${lot.lotNo}: ${fmt(lot.quantity)}`} />
                                            ))}
                                        </Stack>
                                    </Box>
                                    {isSerial(l) && editable && (
                                        <TextField
                                            size="small"
                                            label="Seri no okut"
                                            value={serialScan[l.id] ?? ""}
                                            onChange={(e) => setSerialScan((cur) => ({ ...cur, [l.id]: e.target.value }))}
                                            onKeyDown={(e) => {
                                                if (e.key === "Enter") {
                                                    e.preventDefault();
                                                    addSerial(l, serialScan[l.id] ?? "");
                                                }
                                            }}
                                            slotProps={{ htmlInput: { id: `serial-scan-${l.id}`, "aria-label": `${l.description} seri no okut`, autoComplete: "off", inputMode: "none" } }}
                                            sx={{ width: 170 }}
                                        />
                                    )}
                                    <Typography variant="h4" sx={{ minWidth: 96, textAlign: "center" }}>
                                        {`${fmt(picked(l))} / ${fmt(l.quantity)}`}
                                    </Typography>
                                    <Typography variant="body2" color="text.secondary">
                                        {l.unit}
                                    </Typography>
                                    {editable && (
                                        <Stack direction="row" spacing={1}>
                                            <Button variant="outlined" sx={{ minWidth: 64, minHeight: 64, fontSize: "1.5rem" }} aria-label={`${l.description} azalt`} disabled={picked(l) <= 0} onClick={() => change(l, picked(l) - 1)}>
                                                −
                                            </Button>
                                            <Button variant="outlined" sx={{ minWidth: 64, minHeight: 64, fontSize: "1.5rem" }} aria-label={`${l.description} artır`} disabled={full} onClick={() => change(l, picked(l) + 1)}>
                                                +
                                            </Button>
                                            <Button variant={full ? "outlined" : "contained"} color="success" sx={{ minHeight: 64, minWidth: 96 }} aria-label={`${l.description} tamam`} onClick={() => change(l, full ? 0 : l.quantity)}>
                                                {full ? "Geri al" : "Tamam"}
                                            </Button>
                                        </Stack>
                                    )}
                                </Stack>
                            </Paper>
                        );
                    })}
                </Stack>
                {editable && (
                    <Button
                        variant="contained"
                        size="large"
                        sx={{ minHeight: 64, fontSize: "1.2rem" }}
                        disabled={busy}
                        onClick={() => (shortages.length > 0 ? setConfirm(true) : void complete(false))}
                    >
                        Toplamayı tamamla
                    </Button>
                )}
            </Stack>

            {serialTarget && (
                <LotSerialDialog
                    open
                    mode="out"
                    trackingMode="serial"
                    productId={serialTarget.productId}
                    variantId={serialTarget.variantId}
                    warehouseId={serialTarget.warehouseId}
                    quantity={serialTarget.quantity}
                    title={`${serialTarget.description} · seri numaraları`}
                    initialSerials={serialTarget.pickedSerials ?? []}
                    onClose={() => setSerialLine(null)}
                    onSave={(r) => guarded(() => setSerials(serialTarget, r.serials ?? []))}
                />
            )}

            <Dialog open={confirm} onClose={() => setConfirm(false)} fullWidth maxWidth="sm">
                <DialogTitle>Eksik toplanan satırlar var</DialogTitle>
                <DialogContent>
                    <Typography sx={{ mb: 1 }}>Eksik kalan miktar sevkten düşer ve siparişin açık miktarında kalır.</Typography>
                    <Stack component="ul" spacing={0.5} sx={{ m: 0, pl: 2 }}>
                        {shortages.map((l) => (
                            <li key={l.id}>
                                {l.description}: {fmt(picked(l))} / {fmt(l.quantity)} {l.unit}
                            </li>
                        ))}
                    </Stack>
                </DialogContent>
                <DialogActions>
                    <Button size="large" onClick={() => setConfirm(false)}>
                        Vazgeç
                    </Button>
                    <Button size="large" variant="contained" disabled={busy} onClick={() => void complete(true)}>
                        Yine de tamamla
                    </Button>
                </DialogActions>
            </Dialog>
        </>
    );
}
