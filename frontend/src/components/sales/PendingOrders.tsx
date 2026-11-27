"use client";

import { useEffect, useState } from "react";
import Link from "next/link";
import Alert from "@mui/material/Alert";
import FormControlLabel from "@mui/material/FormControlLabel";
import Skeleton from "@mui/material/Skeleton";
import Stack from "@mui/material/Stack";
import Switch from "@mui/material/Switch";
import Table from "@mui/material/Table";
import TableBody from "@mui/material/TableBody";
import TableCell from "@mui/material/TableCell";
import TableContainer from "@mui/material/TableContainer";
import TableHead from "@mui/material/TableHead";
import TablePagination from "@mui/material/TablePagination";
import TableRow from "@mui/material/TableRow";
import TextField from "@mui/material/TextField";
import Typography from "@mui/material/Typography";
import FormAlert from "@/components/auth/FormAlert";
import PageHeader from "@/components/common/PageHeader";
import DashboardCard from "@/components/dashboard/DashboardCard";
import { useDebounced } from "@/components/pricing/useDebounced";
import { ApiError } from "@/lib/api/client";
import { sales, type PendingLine } from "@/lib/api/sales";
import { useSession } from "@/session/SessionProvider";
import { ORDER_BASE } from "./OrderDetail";

const qty = (n: number) => n.toLocaleString("tr-TR", { maximumFractionDigits: 4 });

/** Bekleyen siparişler: sevk edilecek ve faturalanacak açık miktarlar. */
export default function PendingOrders() {
    const { hasPermission } = useSession();
    const allowed = hasPermission("satis.read");
    const [toShip, setToShip] = useState(false);
    const [toInvoice, setToInvoice] = useState(false);
    const [searchInput, setSearchInput] = useState("");
    const search = useDebounced(searchInput, 300);
    const [page, setPage] = useState(0);
    const [pageSize, setPageSize] = useState(50);
    const [result, setResult] = useState<{ items: PendingLine[]; total: number } | null>(null);
    const [error, setError] = useState<ApiError | null>(null);

    useEffect(() => {
        if (!allowed) return;
        let cancelled = false;
        sales.orders.pending({ toShip: toShip || undefined, toInvoice: toInvoice || undefined, search: search || undefined, page: page + 1, pageSize }).then(
            (r) => {
                if (cancelled) return;
                setError(null);
                setResult(r);
            },
            (e: unknown) => {
                if (cancelled) return;
                if (!(e instanceof ApiError)) throw e;
                setError(e);
                setResult((c) => c ?? { items: [], total: 0 });
            },
        );
        return () => {
            cancelled = true;
        };
    }, [allowed, toShip, toInvoice, search, page, pageSize]);

    if (!allowed) return <Alert severity="warning">Bekleyen siparişleri görüntülemek için yetkiniz yok.</Alert>;

    return (
        <>
            <PageHeader title="Bekleyen siparişler" subtitle="Onaylı siparişlerde sevk edilecek ve faturalanacak açık miktarlar" />
            <DashboardCard>
                <Stack direction={{ xs: "column", md: "row" }} spacing={2} sx={{ mb: 2, alignItems: { md: "center" } }}>
                    <TextField
                        size="small"
                        label="Ara"
                        placeholder="Sipariş no veya müşteri"
                        value={searchInput}
                        onChange={(e) => {
                            setSearchInput(e.target.value);
                            setPage(0);
                        }}
                        sx={{ minWidth: 240 }}
                    />
                    <FormControlLabel
                        control={
                            <Switch
                                checked={toShip}
                                onChange={(e) => {
                                    setToShip(e.target.checked);
                                    setPage(0);
                                }}
                            />
                        }
                        label="Yalnızca sevk edilecek"
                    />
                    <FormControlLabel
                        control={
                            <Switch
                                checked={toInvoice}
                                onChange={(e) => {
                                    setToInvoice(e.target.checked);
                                    setPage(0);
                                }}
                            />
                        }
                        label="Yalnızca faturalanacak"
                    />
                </Stack>
                <FormAlert error={error} />
                {result === null ? (
                    <Skeleton variant="rounded" height={160} />
                ) : result.total === 0 && !error ? (
                    <Typography color="text.secondary" sx={{ py: 4, textAlign: "center" }}>
                        Bekleyen sipariş satırı yok.
                    </Typography>
                ) : (
                    <>
                        <TableContainer>
                            <Table size="small" sx={{ minWidth: 800 }}>
                                <TableHead>
                                    <TableRow>
                                        <TableCell>Sipariş</TableCell>
                                        <TableCell>Müşteri</TableCell>
                                        <TableCell>Kalem</TableCell>
                                        <TableCell align="right">Sipariş</TableCell>
                                        <TableCell align="right">Sevk edilecek</TableCell>
                                        <TableCell align="right">Faturalanacak</TableCell>
                                    </TableRow>
                                </TableHead>
                                <TableBody>
                                    {result.items.map((l) => (
                                        <TableRow key={l.lineId} hover>
                                            <TableCell>
                                                <Link href={`${ORDER_BASE}/${l.orderId}`}>{l.orderNumber}</Link>
                                            </TableCell>
                                            <TableCell>{l.customerName}</TableCell>
                                            <TableCell>{l.productName}</TableCell>
                                            <TableCell align="right">
                                                {qty(l.quantity)} {l.unit}
                                            </TableCell>
                                            <TableCell align="right">{qty(l.openToShip)}</TableCell>
                                            <TableCell align="right">{qty(l.openToInvoice)}</TableCell>
                                        </TableRow>
                                    ))}
                                </TableBody>
                            </Table>
                        </TableContainer>
                        <TablePagination
                            component="div"
                            count={result.total}
                            page={page}
                            rowsPerPage={pageSize}
                            rowsPerPageOptions={[50, 100]}
                            onPageChange={(_, p) => setPage(p)}
                            onRowsPerPageChange={(e) => {
                                setPageSize(Number(e.target.value));
                                setPage(0);
                            }}
                            labelRowsPerPage="Sayfa başına"
                        />
                    </>
                )}
            </DashboardCard>
        </>
    );
}
