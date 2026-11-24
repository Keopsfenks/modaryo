"use client";

import { useCallback, useEffect, useRef, useState } from "react";
import Alert from "@mui/material/Alert";
import Button from "@mui/material/Button";
import Chip from "@mui/material/Chip";
import Dialog from "@mui/material/Dialog";
import DialogActions from "@mui/material/DialogActions";
import DialogContent from "@mui/material/DialogContent";
import DialogContentText from "@mui/material/DialogContentText";
import DialogTitle from "@mui/material/DialogTitle";
import Skeleton from "@mui/material/Skeleton";
import Stack from "@mui/material/Stack";
import Table from "@mui/material/Table";
import TableBody from "@mui/material/TableBody";
import TableCell from "@mui/material/TableCell";
import TableContainer from "@mui/material/TableContainer";
import TableHead from "@mui/material/TableHead";
import TableRow from "@mui/material/TableRow";
import TextField from "@mui/material/TextField";
import FormAlert from "@/components/auth/FormAlert";
import PageHeader from "@/components/common/PageHeader";
import DashboardCard from "@/components/dashboard/DashboardCard";
import { catalog } from "@/lib/api/catalog";
import { errorMessage } from "@/lib/api/errorMessages";
import { formatQuantity, inventory, type Count } from "@/lib/api/inventory";
import { parseAmount } from "@/lib/money";
import { useSession } from "@/session/SessionProvider";

type Entry = { productId: string; variantId: string | null; code: string; name: string; expected: number; counted: string };

const keyOf = (productId: string, variantId: string | null) => `${productId}:${variantId ?? ""}`;
const toEntries = (count: Count): Entry[] =>
    count.lines.map((l) => ({ productId: l.productId, variantId: l.variantId, code: l.productCode, name: l.productName, expected: l.expectedQty, counted: l.countedQty === null ? "" : formatQuantity(l.countedQty) }));

const parseCounted = (text: string): number | null => (text.trim() === "" ? null : parseAmount(text, { maxDecimals: 4 }));

const STATUS_LABELS = { open: "Açık", posted: "Kapatıldı", cancelled: "İptal" } as const;

/** Sayım sayfası: sayılan miktar girişi, barkod okutma (her okutma sayımı 1 artırır), fark vurgusu ve onaylı kapatma. */
export default function CountSheet({ id }: { id: string }) {
    const { hasPermission } = useSession();
    const canCount = hasPermission("stok.sayim");
    const scanRef = useRef<HTMLInputElement>(null);

    const [count, setCount] = useState<Count | null>(null);
    const [entries, setEntries] = useState<Entry[]>([]);
    const [scan, setScan] = useState("");
    const [error, setError] = useState<string | null>(null);
    const [notice, setNotice] = useState<string | null>(null);
    const [confirming, setConfirming] = useState(false);
    const [busy, setBusy] = useState(false);

    const load = useCallback(async () => {
        try {
            const dto = await inventory.count(id);
            setCount(dto);
            setEntries(toEntries(dto));
        } catch (e) {
            setError(errorMessage(e));
        }
    }, [id]);

    useEffect(() => {
        if (!canCount) return;
        // eslint-disable-next-line react-hooks/set-state-in-effect -- ilk yükleme
        void load();
    }, [canCount, load]);

    if (!canCount) return <Alert severity="warning">Stok sayımı için yetkiniz yok.</Alert>;

    const editable = count?.status === "open";
    const diff = (e: Entry): number | null => {
        const counted = parseCounted(e.counted);
        return counted === null ? null : Math.round((counted - e.expected) * 1e6) / 1e6;
    };
    const differing = entries.filter((e) => (diff(e) ?? 0) !== 0);

    const setCounted = (key: string, text: string) => setEntries((current) => current.map((e) => (keyOf(e.productId, e.variantId) === key ? { ...e, counted: text } : e)));

    async function onScan(code: string) {
        const text = code.trim();
        if (!text) return;
        setScan("");
        setError(null);
        setNotice(null);
        try {
            const items = await catalog.searchItems(text, 5);
            const hit = items.find((i) => entries.some((e) => keyOf(e.productId, e.variantId) === keyOf(i.productId, i.variantId)));
            if (!hit) {
                setError(`"${text}" barkoduna ait kalem bu sayımda yok.`);
                return;
            }
            const key = keyOf(hit.productId, hit.variantId);
            setEntries((current) =>
                current.map((e) => {
                    if (keyOf(e.productId, e.variantId) !== key) return e;
                    const now = parseCounted(e.counted) ?? 0;
                    return { ...e, counted: formatQuantity(now + 1) };
                }),
            );
            setNotice(`${hit.code} · ${hit.name} okutuldu.`);
        } catch (e) {
            setError(errorMessage(e));
        } finally {
            scanRef.current?.focus();
        }
    }

    function linesPayload() {
        const lines = [];
        for (const e of entries) {
            const counted = parseCounted(e.counted);
            if (e.counted.trim() !== "" && (counted === null || counted < 0)) {
                setError(`${e.code} için sayılan miktar geçersiz.`);
                return null;
            }
            lines.push({ productId: e.productId, variantId: e.variantId, countedQty: counted });
        }
        return lines;
    }

    async function save() {
        const lines = linesPayload();
        if (!lines) return false;
        setBusy(true);
        setError(null);
        try {
            await inventory.setCountLines(id, lines);
            setNotice("Sayılan miktarlar kaydedildi.");
            return true;
        } catch (e) {
            setError(errorMessage(e));
            return false;
        } finally {
            setBusy(false);
        }
    }

    async function post() {
        setConfirming(false);
        if (!(await save())) return;
        setBusy(true);
        try {
            await inventory.postCount(id);
            setNotice("Sayım kapatıldı; farklar stoğa yansıtıldı.");
            await load();
        } catch (e) {
            setError(errorMessage(e));
        } finally {
            setBusy(false);
        }
    }

    async function cancel() {
        setBusy(true);
        setError(null);
        try {
            await inventory.cancelCount(id);
            await load();
        } catch (e) {
            setError(errorMessage(e));
        } finally {
            setBusy(false);
        }
    }

    return (
        <>
            <PageHeader title={count ? `Sayım ${count.number}` : "Sayım"} subtitle={count?.name ?? undefined} />
            <DashboardCard
                action={count && <Chip label={STATUS_LABELS[count.status]} color={count.status === "open" ? "primary" : "default"} />}
            >
                <FormAlert error={error} />
                {notice && !error && <Alert severity="success" sx={{ mb: 2 }}>{notice}</Alert>}
                {!count ? (
                    !error && <Skeleton variant="rounded" height={160} />
                ) : (
                    <Stack spacing={2}>
                        {editable && (
                            <TextField
                                inputRef={scanRef}
                                size="small"
                                label="Barkod okut"
                                helperText="Barkodu okutup Enter'a basın; her okutma sayılan miktarı 1 artırır."
                                value={scan}
                                onChange={(e) => setScan(e.target.value)}
                                onKeyDown={(e) => {
                                    if (e.key === "Enter") {
                                        e.preventDefault();
                                        void onScan(scan);
                                    }
                                }}
                                sx={{ maxWidth: 360 }}
                                autoFocus
                            />
                        )}
                        <TableContainer>
                            <Table size="small">
                                <TableHead>
                                    <TableRow>
                                        <TableCell>Kod</TableCell>
                                        <TableCell>Ürün</TableCell>
                                        <TableCell align="right">Beklenen</TableCell>
                                        <TableCell sx={{ width: 140 }}>Sayılan</TableCell>
                                        <TableCell align="right">Fark</TableCell>
                                    </TableRow>
                                </TableHead>
                                <TableBody>
                                    {entries.map((e) => {
                                        const d = diff(e);
                                        const key = keyOf(e.productId, e.variantId);
                                        return (
                                            <TableRow key={key} data-diff={d !== null && d !== 0 ? "true" : undefined} sx={d !== null && d !== 0 ? { bgcolor: d < 0 ? "error.light" : "warning.light" } : undefined}>
                                                <TableCell>{e.code}</TableCell>
                                                <TableCell>{e.name}</TableCell>
                                                <TableCell align="right">{formatQuantity(e.expected)}</TableCell>
                                                <TableCell>
                                                    <TextField
                                                        size="small"
                                                        value={e.counted}
                                                        disabled={!editable}
                                                        onChange={(ev) => setCounted(key, ev.target.value)}
                                                        slotProps={{ htmlInput: { "aria-label": `${e.code} sayılan`, inputMode: "decimal" } }}
                                                    />
                                                </TableCell>
                                                <TableCell align="right">{d === null ? "—" : d > 0 ? `+${formatQuantity(d)}` : formatQuantity(d)}</TableCell>
                                            </TableRow>
                                        );
                                    })}
                                </TableBody>
                            </Table>
                        </TableContainer>
                        {editable && (
                            <Stack direction="row" spacing={1}>
                                <Button variant="outlined" disabled={busy} onClick={() => void save()}>
                                    Kaydet
                                </Button>
                                <Button variant="contained" disabled={busy} onClick={() => setConfirming(true)}>
                                    Sayımı kapat
                                </Button>
                                <Button color="error" disabled={busy} onClick={() => void cancel()}>
                                    Sayımı iptal et
                                </Button>
                            </Stack>
                        )}
                    </Stack>
                )}
            </DashboardCard>

            <Dialog open={confirming} onClose={() => setConfirming(false)}>
                <DialogTitle>Sayımı kapat</DialogTitle>
                <DialogContent>
                    <DialogContentText>
                        {differing.length > 0 ? `${differing.length} kalemde fark var; farklar stok hareketi olarak yazılacak. ` : "Fark yok. "}
                        Sayılmayan kalemler değişmez. Kapatılan sayım geri alınamaz. Devam edilsin mi?
                    </DialogContentText>
                </DialogContent>
                <DialogActions>
                    <Button onClick={() => setConfirming(false)}>Vazgeç</Button>
                    <Button variant="contained" onClick={() => void post()}>
                        Onayla ve kapat
                    </Button>
                </DialogActions>
            </Dialog>
        </>
    );
}
