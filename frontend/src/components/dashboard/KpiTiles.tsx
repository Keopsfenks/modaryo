"use client";

import Box from "@mui/material/Box";
import Grid from "@mui/material/Grid";
import Typography from "@mui/material/Typography";
import {
    IconBox,
    IconCash,
    IconFileInvoice,
    IconShoppingCart,
    IconUsers,
    IconUsersGroup,
    type Icon,
} from "@tabler/icons-react";
import type { Kpi } from "@/data/sampleDashboard";
import { formatCompactCurrency, formatNumber } from "@/lib/format";

const kpiIcons: Record<Kpi["key"], Icon> = {
    employees: IconUsersGroup,
    customers: IconUsers,
    openOrders: IconShoppingCart,
    stockItems: IconBox,
    monthlyRevenue: IconCash,
    pendingInvoices: IconFileInvoice,
};

export default function KpiTiles({ items }: { items: Kpi[] }) {
    return (
        <Grid container spacing={3}>
            {items.map((kpi) => {
                const KpiIcon = kpiIcons[kpi.key];
                return (
                    <Grid key={kpi.key} size={{ xs: 6, md: 4, lg: 2 }}>
                        <Box
                            sx={{
                                bgcolor: `${kpi.color}.light`,
                                borderRadius: "12px",
                                textAlign: "center",
                                py: 3,
                                px: 1,
                                height: "100%",
                            }}
                        >
                            <Box sx={{ color: `${kpi.color}.main`, display: "flex", justifyContent: "center", mb: 1 }}>
                                <KpiIcon size={42} stroke={1.4} />
                            </Box>
                            <Typography variant="subtitle1" sx={{ color: `${kpi.color}.main`, fontWeight: 600 }}>
                                {kpi.label}
                            </Typography>
                            <Typography variant="h4" sx={{ color: `${kpi.color}.main`, mt: 0.5 }}>
                                {kpi.kind === "currency" ? formatCompactCurrency(kpi.value) : formatNumber(kpi.value)}
                            </Typography>
                        </Box>
                    </Grid>
                );
            })}
        </Grid>
    );
}
