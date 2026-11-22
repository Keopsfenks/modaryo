"use client";

import Box from "@mui/material/Box";
import Typography from "@mui/material/Typography";
import type { ApexOptions } from "apexcharts";
import Chart, { baseChartOptions, useChartMode } from "@/components/charts/Chart";
import { salesByChannel } from "@/data/sampleDashboard";
import { formatCompactCurrency, formatCurrency } from "@/lib/format";
import { brandColors } from "@/theme/theme";
import DashboardCard from "./DashboardCard";
import TrendBadge from "./TrendBadge";

const colors = [brandColors.primary, brandColors.secondary, brandColors.warning];

export default function SalesByChannel() {
    const mode = useChartMode();
    const base = baseChartOptions(mode);
    const options: ApexOptions = {
        ...base,
        labels: salesByChannel.labels,
        colors,
        stroke: { show: false },
        plotOptions: { pie: { startAngle: 0, endAngle: 360, donut: { size: "75%" } } },
        tooltip: { ...base.tooltip, fillSeriesColor: false, y: { formatter: (value) => formatCurrency(value) } },
    };

    return (
        <DashboardCard title="Kanal Bazlı Satış">
            <Box sx={{ display: "flex", alignItems: "center", gap: 2 }}>
                <Box sx={{ flexGrow: 1, minWidth: 0 }}>
                    <Typography variant="h3" sx={{ mb: 1 }}>
                        {formatCompactCurrency(salesByChannel.total)}
                    </Typography>
                    <TrendBadge percent={salesByChannel.changePercent} caption="geçen yıl" />
                    <Box sx={{ mt: 2.5, display: "flex", flexWrap: "wrap", gap: 1.5 }}>
                        {salesByChannel.labels.map((label, index) => (
                            <Box key={label} sx={{ display: "flex", alignItems: "center", gap: 0.75 }}>
                                <Box sx={{ width: 8, height: 8, borderRadius: "50%", bgcolor: colors[index] }} />
                                <Typography variant="subtitle2" color="text.secondary">
                                    {label}
                                </Typography>
                            </Box>
                        ))}
                    </Box>
                </Box>
                <Box sx={{ width: 130, flexShrink: 0 }}>
                    <Chart type="donut" height={130} options={options} series={salesByChannel.values} />
                </Box>
            </Box>
        </DashboardCard>
    );
}
