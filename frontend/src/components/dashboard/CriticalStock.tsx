"use client";

import Box from "@mui/material/Box";
import LinearProgress from "@mui/material/LinearProgress";
import Stack from "@mui/material/Stack";
import Typography from "@mui/material/Typography";
import type { StockAlert } from "@/data/sampleDashboard";
import { formatNumber } from "@/lib/format";
import DashboardCard from "./DashboardCard";

export default function CriticalStock({ items }: { items: StockAlert[] }) {
    return (
        <DashboardCard title="Kritik Stok" subtitle="Minimum seviyenin altındaki ürünler">
            <Stack spacing={3}>
                {items.map((item) => {
                    const ratio = Math.min(100, Math.round((item.quantity / item.minimum) * 100));
                    const color = ratio < 30 ? "error" : "warning";
                    return (
                        <Box key={item.sku}>
                            <Box sx={{ display: "flex", justifyContent: "space-between", gap: 2, mb: 0.5 }}>
                                <Box sx={{ minWidth: 0 }}>
                                    <Typography variant="subtitle2" color="text.primary" noWrap sx={{ fontWeight: 600 }}>
                                        {item.name}
                                    </Typography>
                                    <Typography variant="body2" color="text.secondary">
                                        {item.sku} · {item.warehouse}
                                    </Typography>
                                </Box>
                                <Typography variant="subtitle2" sx={{ color: `${color}.main`, fontWeight: 600, whiteSpace: "nowrap" }}>
                                    {formatNumber(item.quantity)} / {formatNumber(item.minimum)}
                                </Typography>
                            </Box>
                            <LinearProgress
                                variant="determinate"
                                value={ratio}
                                color={color}
                                aria-label={`${item.name} stok seviyesi`}
                                sx={{ height: 6, borderRadius: 3, bgcolor: `${color}.light` }}
                            />
                        </Box>
                    );
                })}
            </Stack>
        </DashboardCard>
    );
}
