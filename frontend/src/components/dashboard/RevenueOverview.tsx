"use client";

import { useState } from "react";
import Link from "next/link";
import Box from "@mui/material/Box";
import Button from "@mui/material/Button";
import Grid from "@mui/material/Grid";
import MenuItem from "@mui/material/MenuItem";
import Select from "@mui/material/Select";
import Typography from "@mui/material/Typography";
import { IconGridDots } from "@tabler/icons-react";
import type { ApexOptions } from "apexcharts";
import Chart, { baseChartOptions, useChartMode } from "@/components/charts/Chart";
import { revenuePeriods } from "@/data/sampleDashboard";
import { formatCompactCurrency, formatCurrency } from "@/lib/format";
import { brandColors } from "@/theme/theme";
import DashboardCard from "./DashboardCard";

export default function RevenueOverview() {
    const mode = useChartMode();
    const [periodId, setPeriodId] = useState(revenuePeriods[0].id);
    const period = revenuePeriods.find((p) => p.id === periodId) ?? revenuePeriods[0];

    const totalIncome = period.income.reduce((sum, v) => sum + v, 0);
    const lastIncome = period.income[period.income.length - 1];
    const lastExpense = period.expense[period.expense.length - 1];

    const base = baseChartOptions(mode);
    const options: ApexOptions = {
        ...base,
        colors: [brandColors.primary, brandColors.secondary],
        plotOptions: {
            bar: { columnWidth: "42%", borderRadius: 5, borderRadiusApplication: "end" },
        },
        stroke: { show: true, width: 4, colors: ["transparent"] },
        xaxis: { categories: period.months, axisBorder: { show: false }, axisTicks: { show: false } },
        yaxis: { labels: { formatter: (value) => formatCompactCurrency(value) } },
        tooltip: { ...base.tooltip, y: { formatter: (value) => formatCurrency(value) } },
    };

    const series = [
        { name: "Gelir", data: period.income },
        { name: "Gider", data: period.expense },
    ];

    return (
        <DashboardCard
            title="Gelir / Gider"
            subtitle="Aylık finansal özet"
            action={
                <Select
                    size="small"
                    value={periodId}
                    onChange={(event) => setPeriodId(event.target.value)}
                    inputProps={{ "aria-label": "Dönem seç" }}
                >
                    {revenuePeriods.map((p) => (
                        <MenuItem key={p.id} value={p.id}>
                            {p.label}
                        </MenuItem>
                    ))}
                </Select>
            }
        >
            <Grid container spacing={3}>
                <Grid size={{ xs: 12, md: 8 }}>
                    <Chart type="bar" height={320} options={options} series={series} />
                </Grid>
                <Grid size={{ xs: 12, md: 4 }}>
                    <Box sx={{ display: "flex", alignItems: "center", gap: 2, mb: 4, mt: { md: 2 } }}>
                        <Box
                            sx={{
                                width: 40,
                                height: 40,
                                borderRadius: "8px",
                                bgcolor: "primary.light",
                                color: "primary.main",
                                display: "grid",
                                placeItems: "center",
                                flexShrink: 0,
                            }}
                        >
                            <IconGridDots size={22} />
                        </Box>
                        <Box>
                            <Typography variant="h4">{formatCurrency(totalIncome)}</Typography>
                            <Typography variant="subtitle1" color="text.secondary">
                                Dönem toplam gelir
                            </Typography>
                        </Box>
                    </Box>

                    <LegendRow color="primary.main" label="Son ay gelir" value={formatCurrency(lastIncome)} />
                    <LegendRow color="secondary.main" label="Son ay gider" value={formatCurrency(lastExpense)} />

                    <Button variant="contained" fullWidth component={Link} href="/finans/raporlar" sx={{ mt: 2 }}>
                        Raporu Görüntüle
                    </Button>
                </Grid>
            </Grid>
        </DashboardCard>
    );
}

function LegendRow({ color, label, value }: { color: string; label: string; value: string }) {
    return (
        <Box sx={{ display: "flex", alignItems: "baseline", gap: 1.5, mb: 3 }}>
            <Box sx={{ width: 8, height: 8, borderRadius: "50%", bgcolor: color, flexShrink: 0 }} />
            <Box>
                <Typography variant="subtitle1" color="text.secondary">
                    {label}
                </Typography>
                <Typography variant="h5">{value}</Typography>
            </Box>
        </Box>
    );
}
