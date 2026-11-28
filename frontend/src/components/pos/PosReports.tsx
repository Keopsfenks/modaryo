"use client";

import { useEffect, useState } from "react";
import Link from "next/link";
import Alert from "@mui/material/Alert";
import Box from "@mui/material/Box";
import Button from "@mui/material/Button";
import MenuItem from "@mui/material/MenuItem";
import Paper from "@mui/material/Paper";
import Stack from "@mui/material/Stack";
import Table from "@mui/material/Table";
import TableBody from "@mui/material/TableBody";
import TableCell from "@mui/material/TableCell";
import TableHead from "@mui/material/TableHead";
import TableRow from "@mui/material/TableRow";
import TextField from "@mui/material/TextField";
import Typography from "@mui/material/Typography";
import { errorMessage } from "@/lib/api/errorMessages";
import { pos, type DayReport, type RegisterRow } from "@/lib/api/pos";
import { useSession } from "@/session/SessionProvider";
import { money } from "./format";

const today = () => new Date().toLocaleDateString("sv-SE");

/** Gün sonu raporu: kasa ve gün seçilir; ödeme yöntemleri, KDV, iadeler, kampanya indirimi ve kasiyer toplamları gelir. */
export default function PosReports() {
    const { hasPermission } = useSession();
    const [registers, setRegisters] = useState<RegisterRow[]>([]);
    const [registerId, setRegisterId] = useState("");
    const [date, setDate] = useState(today);
    const [report, setReport] = useState<DayReport | null>(null);
    const [error, setError] = useState<string | null>(null);
    const [busy, setBusy] = useState(false);

    useEffect(() => {
        let active = true;
        pos.registers().then(
            (r) => {
                if (!active) return;
                setRegisters(r);
                if (r.length > 0) setRegisterId((cur) => cur || r[0].id);
            },
            (e) => active && setError(errorMessage(e)),
        );
        return () => {
            active = false;
        };
    }, []);

    if (!hasPermission("pos.reports")) {
        return (
            <Box sx={{ p: 4 }}>
                <Alert severity="warning">Raporları görmek için yetkiniz yok.</Alert>
            </Box>
        );
    }

    const run = async () => {
        setBusy(true);
        setError(null);
        try {
            setReport(await pos.dayReport(registerId, date));
        } catch (e) {
            setReport(null);
            setError(errorMessage(e));
        } finally {
            setBusy(false);
        }
    };

    const row = (label: string, value: string) => (
        <TableRow>
            <TableCell>{label}</TableCell>
            <TableCell align="right">{value}</TableCell>
        </TableRow>
    );

    return (
        <Box sx={{ maxWidth: 960, mx: "auto", p: 3 }}>
            <Stack spacing={2}>
                <Stack direction="row" sx={{ justifyContent: "space-between", alignItems: "center" }}>
                    <Typography variant="h4">Kasa raporları</Typography>
                    <Button component={Link} href="/pos">
                        Kasaya dön
                    </Button>
                </Stack>
                {error && <Alert severity="error">{error}</Alert>}
                <Paper sx={{ p: 2 }}>
                    <Stack direction={{ xs: "column", sm: "row" }} spacing={2}>
                        <TextField select label="Kasa" value={registerId} onChange={(e) => setRegisterId(e.target.value)} sx={{ minWidth: 220 }}>
                            {registers.map((r) => (
                                <MenuItem key={r.id} value={r.id}>
                                    {r.name} ({r.code})
                                </MenuItem>
                            ))}
                        </TextField>
                        <TextField type="date" label="Gün" value={date} onChange={(e) => setDate(e.target.value)} slotProps={{ inputLabel: { shrink: true } }} />
                        <Button variant="contained" disabled={busy || !registerId || !date} onClick={() => void run()} sx={{ minHeight: 56 }}>
                            Gün sonu raporu
                        </Button>
                    </Stack>
                </Paper>
                {report && (
                    <Paper sx={{ p: 2 }}>
                        <Table size="small">
                            <TableBody>
                                {row("Satış sayısı", String(report.salesCount))}
                                {row("Satış toplamı", money(report.salesTotal))}
                                {row("Nakit (net)", money(report.cashNet))}
                                {row("Kart", money(report.card))}
                                {row("Veresiye", money(report.onAccount))}
                                {row("İade mahsubu", money(report.returnCredit))}
                                {row("Verilen para üstü", money(report.changeGiven))}
                                {row("İptal", `${report.cancelledCount} · ${money(report.cancelledTotal)}`)}
                                {row("İadeler", `${report.returnsCount} · ${money(report.returnsTotal)}`)}
                                {row("Nakit iade", money(report.cashRefunds))}
                                {row("Kart iadesi", money(report.cardRefunds))}
                                {row("Kampanya indirimi", money(report.campaignDiscount))}
                                {row("Net satış", money(report.netSales))}
                                {row("İlk fiş", report.firstReceipt ?? "-")}
                                {row("Son fiş", report.lastReceipt ?? "-")}
                            </TableBody>
                        </Table>
                        <Typography variant="subtitle1" sx={{ mt: 2 }}>
                            KDV özeti
                        </Typography>
                        <Table size="small">
                            <TableHead>
                                <TableRow>
                                    <TableCell>Oran</TableCell>
                                    <TableCell align="right">Matrah</TableCell>
                                    <TableCell align="right">KDV</TableCell>
                                </TableRow>
                            </TableHead>
                            <TableBody>
                                {report.vatSummary.map((v) => (
                                    <TableRow key={v.rate}>
                                        <TableCell>%{v.rate}</TableCell>
                                        <TableCell align="right">{money(v.base)}</TableCell>
                                        <TableCell align="right">{money(v.vat)}</TableCell>
                                    </TableRow>
                                ))}
                            </TableBody>
                        </Table>
                        <Typography variant="subtitle1" sx={{ mt: 2 }}>
                            Kasiyerler
                        </Typography>
                        <Table size="small">
                            <TableHead>
                                <TableRow>
                                    <TableCell>Kasiyer</TableCell>
                                    <TableCell align="right">Satış</TableCell>
                                    <TableCell align="right">Tutar</TableCell>
                                    <TableCell align="right">İptal</TableCell>
                                    <TableCell align="right">İade</TableCell>
                                </TableRow>
                            </TableHead>
                            <TableBody>
                                {report.cashiers.map((c) => (
                                    <TableRow key={c.cashierUserId}>
                                        <TableCell>{c.cashierName ?? c.cashierUserId}</TableCell>
                                        <TableCell align="right">{c.salesCount}</TableCell>
                                        <TableCell align="right">{money(c.salesTotal)}</TableCell>
                                        <TableCell align="right">{c.cancelledCount}</TableCell>
                                        <TableCell align="right">{`${c.returnsCount} · ${money(c.returnsTotal)}`}</TableCell>
                                    </TableRow>
                                ))}
                            </TableBody>
                        </Table>
                    </Paper>
                )}
            </Stack>
        </Box>
    );
}
