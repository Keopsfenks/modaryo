import type { Metadata } from "next";
import Box from "@mui/material/Box";
import Grid from "@mui/material/Grid";
import Stack from "@mui/material/Stack";
import PageHeader from "@/components/common/PageHeader";
import CriticalStock from "@/components/dashboard/CriticalStock";
import KpiTiles from "@/components/dashboard/KpiTiles";
import MonthlyCollection from "@/components/dashboard/MonthlyCollection";
import RecentOrders from "@/components/dashboard/RecentOrders";
import RevenueOverview from "@/components/dashboard/RevenueOverview";
import SalesByChannel from "@/components/dashboard/SalesByChannel";
import { criticalStock, kpis, recentOrders } from "@/data/sampleDashboard";

export const metadata: Metadata = {
    title: "Genel Bakış",
};

export default function DashboardPage() {
    return (
        <Box>
            <PageHeader title="Genel Bakış" subtitle="İşletmenizin güncel durumu" />
            <Grid container spacing={3}>
                <Grid size={12}>
                    <KpiTiles items={kpis} />
                </Grid>

                <Grid size={{ xs: 12, lg: 8 }}>
                    <RevenueOverview />
                </Grid>
                <Grid size={{ xs: 12, lg: 4 }}>
                    {/* İki kart sütunu doldurur; her kart kendi içerik yüksekliğinden küçülmez */}
                    <Stack spacing={3} sx={{ height: "100%", "& > .MuiCard-root": { flex: "1 0 auto", height: "auto" } }}>
                        <SalesByChannel />
                        <MonthlyCollection />
                    </Stack>
                </Grid>

                <Grid size={{ xs: 12, lg: 8 }}>
                    <RecentOrders orders={recentOrders} />
                </Grid>
                <Grid size={{ xs: 12, lg: 4 }}>
                    <CriticalStock items={criticalStock} />
                </Grid>
            </Grid>
        </Box>
    );
}
