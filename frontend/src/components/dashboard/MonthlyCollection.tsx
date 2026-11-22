"use client";

import Box from "@mui/material/Box";
import Typography from "@mui/material/Typography";
import { IconCurrencyLira } from "@tabler/icons-react";
import type { ApexOptions } from "apexcharts";
import Chart, { baseChartOptions, useChartMode } from "@/components/charts/Chart";
import { monthlyCollection } from "@/data/sampleDashboard";
import { formatCurrency } from "@/lib/format";
import { brandColors } from "@/theme/theme";
import DashboardCard from "./DashboardCard";
import TrendBadge from "./TrendBadge";

export default function MonthlyCollection() {
    const mode = useChartMode();
    const base = baseChartOptions(mode);
    const options: ApexOptions = {
        ...base,
        chart: { ...base.chart, sparkline: { enabled: true } },
        colors: [brandColors.secondary],
        stroke: { curve: "smooth", width: 2 },
        fill: { type: "solid", opacity: 0.05 },
        tooltip: { ...base.tooltip, y: { formatter: (value) => formatCurrency(value) } },
    };

    return (
        <DashboardCard
            title="Aylık Tahsilat"
            action={
                <Box
                    sx={{
                        width: 40,
                        height: 40,
                        borderRadius: "50%",
                        bgcolor: "secondary.main",
                        color: "#fff",
                        display: "grid",
                        placeItems: "center",
                    }}
                >
                    <IconCurrencyLira size={22} />
                </Box>
            }
            footer={<Chart type="area" height={60} options={options} series={[{ name: "Tahsilat", data: monthlyCollection.series }]} />}
        >
            <Typography variant="h3" sx={{ mb: 1, mt: -2 }}>
                {formatCurrency(monthlyCollection.total)}
            </Typography>
            <TrendBadge percent={monthlyCollection.changePercent} caption="geçen ay" />
        </DashboardCard>
    );
}
