"use client";

import { useEffect, useMemo, useState } from "react";
import Alert from "@mui/material/Alert";
import Button from "@mui/material/Button";
import ButtonGroup from "@mui/material/ButtonGroup";
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
import type { ApexOptions } from "apexcharts";
import Chart, { baseChartOptions, useChartMode } from "@/components/charts/Chart";
import PageHeader from "@/components/common/PageHeader";
import DashboardCard from "@/components/dashboard/DashboardCard";
import { errorMessage } from "@/lib/api/errorMessages";
import { treasury, type CashFlowReport } from "@/lib/api/treasury";
import { formatDate } from "@/lib/date";
import { formatMoney } from "@/lib/money";
import { useSession } from "@/session/SessionProvider";

const MAX_PERIODS = { day: 90, week: 26 } as const;

/** Nakit akışı: giren/çıkan sütunları ve bakiye çizgisi (TL). Dönem sayısı günlük en çok 90, haftalık en çok 26. */
export default function CashFlowChart() {
    const { hasPermission } = useSession();
    const canRead = hasPermission("finans.read");
    const mode = useChartMode();
    const [granularity, setGranularity] = useState<"day" | "week">("week");
    const [periods, setPeriods] = useState("12");
    const [report, setReport] = useState<CashFlowReport | null>(null);
    const [error, setError] = useState<string | null>(null);

    const count = Math.min(Math.max(Number.parseInt(periods, 10) || 1, 1), MAX_PERIODS[granularity]);

    useEffect(() => {
        if (!canRead) return;
        let cancelled = false;
        treasury.cashFlow({ granularity, periods: count }).then(
            (r) => {
                if (cancelled) return;
                setReport(r);
                setError(null);
            },
            (e) => {
                if (cancelled) return;
                setError(errorMessage(e));
            },
        );
        return () => {
            cancelled = true;
        };
    }, [canRead, granularity, count]);

    const series = useMemo(
        () => [
            { name: "Giren", type: "column", data: report?.periods.map((p) => p.inflow.total) ?? [] },
            { name: "Çıkan", type: "column", data: report?.periods.map((p) => p.outflow.total) ?? [] },
            { name: "Bakiye", type: "line", data: report?.periods.map((p) => p.closing) ?? [] },
        ],
        [report],
    );

    const options: ApexOptions = useMemo(() => {
        const base = baseChartOptions(mode);
        return {
            ...base,
            chart: { ...base.chart, type: "line", stacked: false },
            colors: ["#13DEB9", "#FA896B", "#5D87FF"],
            stroke: { width: [0, 0, 3], curve: "smooth" },
            plotOptions: { bar: { columnWidth: "45%", borderRadius: 4 } },
            xaxis: { categories: report?.periods.map((p) => formatDate(p.start)) ?? [] },
            yaxis: { labels: { formatter: (v: number) => formatMoney(v, "TRY") } },
            legend: { show: true, position: "top" },
        };
    }, [mode, report]);

    if (!canRead) return <Alert severity="warning">Nakit akışı raporu için yetkiniz yok.</Alert>;

    return (
        <>
            <PageHeader title="Nakit akışı" subtitle="Açık vade satırları ve çek/senetlerin vadesine göre beklenen giriş ve çıkışlar (TL)." />
            <Stack spacing={3}>
                <DashboardCard>
                    <Stack spacing={2}>
                        <Stack direction={{ xs: "column", md: "row" }} spacing={2} sx={{ alignItems: { md: "center" } }}>
                            <ButtonGroup aria-label="Aralık">
                                <Button variant={granularity === "day" ? "contained" : "outlined"} onClick={() => setGranularity("day")}>
                                    Günlük
                                </Button>
                                <Button variant={granularity === "week" ? "contained" : "outlined"} onClick={() => setGranularity("week")}>
                                    Haftalık
                                </Button>
                            </ButtonGroup>
                            <TextField
                                size="small"
                                label={granularity === "day" ? "Gün sayısı" : "Hafta sayısı"}
                                value={periods}
                                onChange={(e) => setPeriods(e.target.value)}
                                helperText={`En çok ${MAX_PERIODS[granularity]}`}
                                slotProps={{ htmlInput: { inputMode: "numeric" } }}
                                sx={{ width: 140 }}
                            />
                        </Stack>
                        {error && <Alert severity="error">{error}</Alert>}
                        {report === null ? (
                            <Skeleton variant="rounded" height={320} />
                        ) : (
                            <>
                                <Typography>
                                    Açılış bakiyesi (kasa + banka, bugünkü kurla): <strong>{formatMoney(report.opening, "TRY")}</strong>
                                </Typography>
                                <Chart type="line" height={320} series={series} options={options} />
                            </>
                        )}
                    </Stack>
                </DashboardCard>
                {report && report.periods.length > 0 && (
                    <DashboardCard title="Dönem ayrıntısı">
                        <TableContainer>
                            <Table size="small">
                                <TableHead>
                                    <TableRow>
                                        <TableCell>Dönem</TableCell>
                                        <TableCell align="right">Giren</TableCell>
                                        <TableCell align="right">Çıkan</TableCell>
                                        <TableCell align="right">Net</TableCell>
                                        <TableCell align="right">Bakiye</TableCell>
                                    </TableRow>
                                </TableHead>
                                <TableBody>
                                    {report.periods.map((p) => (
                                        <TableRow key={p.start} hover>
                                            <TableCell>{granularity === "day" ? formatDate(p.start) : `${formatDate(p.start)} - ${formatDate(p.end)}`}</TableCell>
                                            <TableCell align="right">{formatMoney(p.inflow.total, "TRY")}</TableCell>
                                            <TableCell align="right">{formatMoney(p.outflow.total, "TRY")}</TableCell>
                                            <TableCell align="right">{formatMoney(p.net, "TRY")}</TableCell>
                                            <TableCell align="right">{formatMoney(p.closing, "TRY")}</TableCell>
                                        </TableRow>
                                    ))}
                                </TableBody>
                            </Table>
                        </TableContainer>
                    </DashboardCard>
                )}
            </Stack>
        </>
    );
}
