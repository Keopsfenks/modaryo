"use client";

import { useEffect, useState } from "react";
import Alert from "@mui/material/Alert";
import Button from "@mui/material/Button";
import ButtonGroup from "@mui/material/ButtonGroup";
import MenuItem from "@mui/material/MenuItem";
import Skeleton from "@mui/material/Skeleton";
import Stack from "@mui/material/Stack";
import Table from "@mui/material/Table";
import TableBody from "@mui/material/TableBody";
import TableCell from "@mui/material/TableCell";
import TableContainer from "@mui/material/TableContainer";
import TableHead from "@mui/material/TableHead";
import TableRow from "@mui/material/TableRow";
import TextField from "@mui/material/TextField";
import Typography from "@mui/material/Typography";
import PageHeader from "@/components/common/PageHeader";
import DashboardCard from "@/components/dashboard/DashboardCard";
import { errorMessage } from "@/lib/api/errorMessages";
import { AGING_BUCKETS, treasury, type AgingBucket, type AgingReport as Report } from "@/lib/api/treasury";
import { CURRENCIES, formatMoney, type Currency } from "@/lib/money";
import { useSession } from "@/session/SessionProvider";

const BUCKET_LABELS: Record<AgingBucket, string> = {
    current: "Vadesi gelmemiş",
    "1-30": "1-30 gün",
    "31-60": "31-60 gün",
    "61-90": "61-90 gün",
    "90+": "90+ gün",
};

/** Yaşlandırma: cari ve para birimi başına vade gecikme dilimleri (alacaklar / borçlar). */
export default function AgingReport() {
    const { hasPermission } = useSession();
    const canRead = hasPermission("finans.read");
    const [direction, setDirection] = useState<"receivable" | "payable">("receivable");
    const [currency, setCurrency] = useState<Currency | "">("");
    const [asOf, setAsOf] = useState("");
    const [report, setReport] = useState<Report | null>(null);
    const [error, setError] = useState<string | null>(null);

    useEffect(() => {
        if (!canRead) return;
        let cancelled = false;
        treasury.aging({ direction, currency: currency || undefined, asOf: asOf || undefined }).then(
            (r) => {
                if (cancelled) return;
                setReport(r);
                setError(null);
            },
            (e) => {
                if (cancelled) return;
                setError(errorMessage(e));
                setReport({ asOf: "", direction, rows: [], totals: [] });
            },
        );
        return () => {
            cancelled = true;
        };
    }, [canRead, direction, currency, asOf]);

    if (!canRead) return <Alert severity="warning">Yaşlandırma raporu için yetkiniz yok.</Alert>;

    return (
        <>
            <PageHeader title="Yaşlandırma" subtitle="Açık kalemler vadesine göre gecikme gününe bölünür; döviz tutarlar TL'ye çevrilmez." />
            <DashboardCard>
                <Stack spacing={2}>
                    <Stack direction={{ xs: "column", md: "row" }} spacing={2} sx={{ alignItems: { md: "center" } }}>
                        <ButtonGroup aria-label="Yön">
                            <Button variant={direction === "receivable" ? "contained" : "outlined"} onClick={() => setDirection("receivable")}>
                                Alacaklar
                            </Button>
                            <Button variant={direction === "payable" ? "contained" : "outlined"} onClick={() => setDirection("payable")}>
                                Borçlar
                            </Button>
                        </ButtonGroup>
                        <TextField select size="small" label="Para birimi" value={currency} onChange={(e) => setCurrency(e.target.value as Currency | "")} sx={{ minWidth: 140 }}>
                            <MenuItem value="">Tümü</MenuItem>
                            {CURRENCIES.map((c) => (
                                <MenuItem key={c} value={c}>
                                    {c}
                                </MenuItem>
                            ))}
                        </TextField>
                        <TextField size="small" type="date" label="Tarih itibarıyla" value={asOf} onChange={(e) => setAsOf(e.target.value)} slotProps={{ inputLabel: { shrink: true } }} />
                    </Stack>
                    {error && <Alert severity="error">{error}</Alert>}
                    {report === null ? (
                        <Skeleton variant="rounded" height={160} />
                    ) : report.rows.length === 0 ? (
                        <Typography color="text.secondary" sx={{ py: 3 }}>
                            Açık kalem yok.
                        </Typography>
                    ) : (
                        <TableContainer>
                            <Table size="small">
                                <TableHead>
                                    <TableRow>
                                        <TableCell>Kod</TableCell>
                                        <TableCell>Cari</TableCell>
                                        {AGING_BUCKETS.map((b) => (
                                            <TableCell key={b} align="right">
                                                {BUCKET_LABELS[b]}
                                            </TableCell>
                                        ))}
                                        <TableCell align="right">Toplam</TableCell>
                                    </TableRow>
                                </TableHead>
                                <TableBody>
                                    {report.rows.map((r) => (
                                        <TableRow key={`${r.currentAccountId}-${r.currency}`} hover>
                                            <TableCell>{r.code}</TableCell>
                                            <TableCell>{r.name}</TableCell>
                                            {AGING_BUCKETS.map((b) => (
                                                <TableCell key={b} align="right">
                                                    {formatMoney(r.buckets[b] ?? 0, r.currency)}
                                                </TableCell>
                                            ))}
                                            <TableCell align="right">{formatMoney(r.total, r.currency)}</TableCell>
                                        </TableRow>
                                    ))}
                                    {report.totals.map((t) => (
                                        <TableRow key={`total-${t.currency}`} sx={{ "& td": { fontWeight: 600 } }}>
                                            <TableCell colSpan={2}>Genel toplam ({t.currency})</TableCell>
                                            {AGING_BUCKETS.map((b) => (
                                                <TableCell key={b} align="right">
                                                    {formatMoney(t.buckets[b] ?? 0, t.currency)}
                                                </TableCell>
                                            ))}
                                            <TableCell align="right">{formatMoney(t.total, t.currency)}</TableCell>
                                        </TableRow>
                                    ))}
                                </TableBody>
                            </Table>
                        </TableContainer>
                    )}
                </Stack>
            </DashboardCard>
        </>
    );
}
