"use client";

import { useEffect, useState } from "react";
import Alert from "@mui/material/Alert";
import Box from "@mui/material/Box";
import Button from "@mui/material/Button";
import Dialog from "@mui/material/Dialog";
import DialogActions from "@mui/material/DialogActions";
import DialogContent from "@mui/material/DialogContent";
import DialogTitle from "@mui/material/DialogTitle";
import MenuItem from "@mui/material/MenuItem";
import Paper from "@mui/material/Paper";
import Stack from "@mui/material/Stack";
import TextField from "@mui/material/TextField";
import Typography from "@mui/material/Typography";
import { errorMessage } from "@/lib/api/errorMessages";
import { pos, type ShiftDto, type ShiftReport } from "@/lib/api/pos";
import { money, parseNumber, round2 } from "./format";

function Row({ label, value, bold }: { label: string; value: string; bold?: boolean }) {
    return (
        <Stack direction="row" sx={{ justifyContent: "space-between" }}>
            <Typography sx={{ fontWeight: bold ? 700 : 400 }}>{label}</Typography>
            <Typography sx={{ fontWeight: bold ? 700 : 400 }}>{value}</Typography>
        </Stack>
    );
}

/** Kasa raporu: vardiya toplamları, beklenen/sayılan nakit ve fark. */
export function ShiftReportView({ report }: { report: ShiftReport }) {
    const t = report.totals;
    const diff = report.shift.difference;
    return (
        <Stack spacing={1}>
            <Typography variant="h6">Kasa raporu</Typography>
            <Row label="Satış sayısı" value={String(t.salesCount)} />
            <Row label="Satış toplamı" value={money(t.salesTotal)} />
            <Row label="Nakit (para üstü düşülmüş)" value={money(t.cashSalesNet)} />
            <Row label="Kart" value={money(t.cardSales)} />
            <Row label="Veresiye" value={money(t.onAccountSales)} />
            <Row label="İadeler" value={`${t.returnsCount} · ${money(t.returnsTotal)}`} />
            <Row label="Nakit iade" value={money(t.cashRefunds)} />
            <Row label="Kasa girişi / çıkışı" value={`${money(t.cashIn)} / ${money(t.cashOut)}`} />
            <Row label="İptal edilen" value={`${t.cancelledCount} · ${money(t.cancelledTotal)}`} />
            {report.vatSummary.map((v) => (
                <Row key={v.rate} label={`KDV %${v.rate}`} value={money(v.vat)} />
            ))}
            <Row label="Açılış nakdi" value={money(report.shift.openingCash)} />
            <Row label="Beklenen nakit" value={money(report.shift.expectedCash ?? t.expectedCash)} bold />
            {report.shift.countedCash !== null && <Row label="Sayılan nakit" value={money(report.shift.countedCash)} bold />}
            {diff !== null && (
                <Stack direction="row" sx={{ justifyContent: "space-between" }}>
                    <Typography sx={{ fontWeight: 700 }}>Fark</Typography>
                    <Typography aria-label="Fark" sx={{ fontWeight: 700 }} color={diff === 0 ? "success.main" : "error.main"}>
                        {money(diff)}
                    </Typography>
                </Stack>
            )}
        </Stack>
    );
}

type ShiftCloseProps = { shift: ShiftDto; onClosed: (report: ShiftReport) => void; onCancel: () => void };

/** Vardiya kapanışı: sayılan nakit girilir, fark (sayılan - beklenen) anında gösterilir; kapanınca kasa raporu görünür. */
export default function ShiftClose({ shift, onClosed, onCancel }: ShiftCloseProps) {
    const [report, setReport] = useState<ShiftReport | null>(null);
    const [counted, setCounted] = useState("");
    const [note, setNote] = useState("");
    const [busy, setBusy] = useState(false);
    const [error, setError] = useState<string | null>(null);
    const [closed, setClosed] = useState<ShiftReport | null>(null);

    useEffect(() => {
        let active = true;
        pos.shiftReport(shift.id).then(
            (r) => active && setReport(r),
            (e) => active && setError(errorMessage(e)),
        );
        return () => {
            active = false;
        };
    }, [shift.id]);

    const expected = report?.totals.expectedCash ?? null;
    const countedV = parseNumber(counted);
    const difference = expected !== null && countedV !== null ? round2(countedV - expected) : null;

    const close = async () => {
        if (countedV === null) return;
        setBusy(true);
        setError(null);
        try {
            const result = await pos.closeShift(shift.id, countedV, note.trim() === "" ? null : note.trim());
            setClosed(result);
        } catch (e) {
            setError(errorMessage(e));
        } finally {
            setBusy(false);
        }
    };

    if (closed) {
        return (
            <Box sx={{ maxWidth: 560, mx: "auto", p: 3 }}>
                <Paper sx={{ p: 3 }}>
                    <ShiftReportView report={closed} />
                    <Stack direction="row" spacing={1} sx={{ mt: 3 }}>
                        <Button variant="outlined" onClick={() => window.print()}>
                            Yazdır
                        </Button>
                        <Button variant="contained" onClick={() => onClosed(closed)}>
                            Tamam
                        </Button>
                    </Stack>
                </Paper>
            </Box>
        );
    }

    return (
        <Box sx={{ maxWidth: 560, mx: "auto", p: 3 }}>
            <Paper sx={{ p: 3 }}>
                <Stack spacing={2}>
                    <Typography variant="h5">Vardiyayı kapat</Typography>
                    {error && <Alert severity="error">{error}</Alert>}
                    <Stack direction="row" sx={{ justifyContent: "space-between", alignItems: "baseline" }}>
                        <Typography color="text.secondary">Beklenen nakit</Typography>
                        <Typography variant="h5" aria-label="Beklenen nakit">
                            {expected === null ? "…" : money(expected)}
                        </Typography>
                    </Stack>
                    <TextField label="Sayılan nakit" value={counted} onChange={(e) => setCounted(e.target.value)} autoFocus slotProps={{ htmlInput: { inputMode: "decimal", style: { fontSize: 24 } } }} />
                    {difference !== null && (
                        <Stack direction="row" sx={{ justifyContent: "space-between", alignItems: "baseline" }}>
                            <Typography color="text.secondary">Fark</Typography>
                            <Typography variant="h4" aria-label="Fark" color={difference === 0 ? "success.main" : "error.main"}>
                                {money(difference)}
                            </Typography>
                        </Stack>
                    )}
                    <TextField label="Not" value={note} onChange={(e) => setNote(e.target.value)} multiline minRows={2} slotProps={{ htmlInput: { maxLength: 500 } }} />
                    <Stack direction="row" spacing={1}>
                        <Button onClick={onCancel}>Vazgeç</Button>
                        <Button variant="contained" color="warning" disabled={busy || countedV === null || expected === null} onClick={() => void close()}>
                            {busy ? "Kapatılıyor..." : "Vardiyayı kapat"}
                        </Button>
                    </Stack>
                </Stack>
            </Paper>
        </Box>
    );
}

/** Açık vardiyada kasa girişi/çıkışı (pos.cashmove). */
export function CashMoveDialog({ shift, onClose }: { shift: ShiftDto; onClose: () => void }) {
    const [kind, setKind] = useState<"in" | "out">("in");
    const [amount, setAmount] = useState("");
    const [reason, setReason] = useState("");
    const [busy, setBusy] = useState(false);
    const [error, setError] = useState<string | null>(null);
    const [warnings, setWarnings] = useState<string[]>([]);
    const amountV = parseNumber(amount);

    const save = async () => {
        if (amountV === null || amountV <= 0) return;
        setBusy(true);
        setError(null);
        try {
            const result = await pos.cashMove(shift.id, kind, amountV, reason.trim());
            if (result.warnings.length > 0) setWarnings(result.warnings);
            else onClose();
        } catch (e) {
            setError(errorMessage(e));
        } finally {
            setBusy(false);
        }
    };

    return (
        <Dialog open onClose={onClose} fullWidth maxWidth="xs">
            <DialogTitle>Kasa giriş / çıkış</DialogTitle>
            <DialogContent>
                <Stack spacing={2} sx={{ pt: 1 }}>
                    {error && <Alert severity="error">{error}</Alert>}
                    {warnings.length > 0 && <Alert severity="warning">Kasadaki nakit eksiye düşüyor; hareket kaydedildi.</Alert>}
                    <TextField select label="Tür" value={kind} onChange={(e) => setKind(e.target.value as "in" | "out")}>
                        <MenuItem value="in">Kasa girişi</MenuItem>
                        <MenuItem value="out">Kasa çıkışı</MenuItem>
                    </TextField>
                    <TextField label="Tutar" value={amount} onChange={(e) => setAmount(e.target.value)} slotProps={{ htmlInput: { inputMode: "decimal" } }} />
                    <TextField label="Açıklama" value={reason} onChange={(e) => setReason(e.target.value)} />
                </Stack>
            </DialogContent>
            <DialogActions>
                <Button onClick={onClose}>{warnings.length > 0 ? "Kapat" : "Vazgeç"}</Button>
                {warnings.length === 0 && (
                    <Button variant="contained" disabled={busy || amountV === null || amountV <= 0 || reason.trim() === ""} onClick={() => void save()}>
                        Kaydet
                    </Button>
                )}
            </DialogActions>
        </Dialog>
    );
}
