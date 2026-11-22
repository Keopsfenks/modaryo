"use client";

import Link from "next/link";
import Avatar from "@mui/material/Avatar";
import Box from "@mui/material/Box";
import Button from "@mui/material/Button";
import Chip from "@mui/material/Chip";
import Table from "@mui/material/Table";
import TableBody from "@mui/material/TableBody";
import TableCell from "@mui/material/TableCell";
import TableContainer from "@mui/material/TableContainer";
import TableHead from "@mui/material/TableHead";
import TableRow from "@mui/material/TableRow";
import Typography from "@mui/material/Typography";
import type { Order, OrderStatus, PaletteColor } from "@/data/sampleDashboard";
import { formatCurrency, formatDate } from "@/lib/format";
import DashboardCard from "./DashboardCard";

const statusMeta: Record<OrderStatus, { label: string; color: PaletteColor }> = {
    hazirlaniyor: { label: "Hazırlanıyor", color: "warning" },
    kargoda: { label: "Kargoda", color: "info" },
    teslim: { label: "Teslim Edildi", color: "success" },
    iptal: { label: "İptal", color: "error" },
};

function initials(name: string) {
    return name
        .split(" ")
        .slice(0, 2)
        .map((part) => part.charAt(0))
        .join("")
        .toLocaleUpperCase("tr-TR");
}

export default function RecentOrders({ orders }: { orders: Order[] }) {
    return (
        <DashboardCard
            title="Son Siparişler"
            subtitle="Son 48 saatte oluşturulan satış siparişleri"
            action={
                <Button size="small" component={Link} href="/satis/siparisler">
                    Tümü
                </Button>
            }
        >
            <TableContainer sx={{ mx: -1 }}>
                <Table size="small" sx={{ minWidth: 620, "& td, & th": { px: 1, py: 1.5 } }}>
                    <TableHead>
                        <TableRow>
                            <TableCell>Müşteri</TableCell>
                            <TableCell>Sipariş No</TableCell>
                            <TableCell>Tarih</TableCell>
                            <TableCell>Durum</TableCell>
                            <TableCell align="right">Tutar</TableCell>
                        </TableRow>
                    </TableHead>
                    <TableBody>
                        {orders.map((order) => {
                            const status = statusMeta[order.status];
                            return (
                                <TableRow key={order.id} hover>
                                    <TableCell>
                                        <Box sx={{ display: "flex", alignItems: "center", gap: 1.5 }}>
                                            <Avatar
                                                sx={{
                                                    width: 36,
                                                    height: 36,
                                                    fontSize: "0.8rem",
                                                    bgcolor: "primary.light",
                                                    color: "primary.main",
                                                }}
                                            >
                                                {initials(order.customer)}
                                            </Avatar>
                                            <Box>
                                                <Typography variant="subtitle2" color="text.primary" sx={{ fontWeight: 600 }}>
                                                    {order.customer}
                                                </Typography>
                                                <Typography variant="body2" color="text.secondary">
                                                    {order.city}
                                                </Typography>
                                            </Box>
                                        </Box>
                                    </TableCell>
                                    <TableCell>
                                        <Typography variant="subtitle2" color="text.secondary">
                                            {order.id}
                                        </Typography>
                                    </TableCell>
                                    <TableCell>
                                        <Typography variant="subtitle2" color="text.secondary">
                                            {formatDate(order.date)}
                                        </Typography>
                                    </TableCell>
                                    <TableCell>
                                        <Chip
                                            label={status.label}
                                            size="small"
                                            sx={{ bgcolor: `${status.color}.light`, color: `${status.color}.main` }}
                                        />
                                    </TableCell>
                                    <TableCell align="right">
                                        <Typography variant="subtitle2" color="text.primary" sx={{ fontWeight: 600 }}>
                                            {formatCurrency(order.amount)}
                                        </Typography>
                                    </TableCell>
                                </TableRow>
                            );
                        })}
                    </TableBody>
                </Table>
            </TableContainer>
        </DashboardCard>
    );
}
