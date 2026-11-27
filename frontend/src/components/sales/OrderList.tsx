"use client";

import { useEffect, useState } from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import Alert from "@mui/material/Alert";
import Button from "@mui/material/Button";
import Chip from "@mui/material/Chip";
import MenuItem from "@mui/material/MenuItem";
import Skeleton from "@mui/material/Skeleton";
import Stack from "@mui/material/Stack";
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
import { INVOICE_STATE_LABELS, ORDER_STATUS_LABELS, sales, type OrderRow, type OrderStatus } from "@/lib/api/sales";
import { formatDate } from "@/lib/date";
import { formatMoney, type Currency } from "@/lib/money";
import { useSession } from "@/session/SessionProvider";
import { ORDER_BASE, ORDER_STATUS_COLOR } from "./OrderDetail";

/** Satış siparişleri: durum ve arama süzgeçleri. */
export default function OrderList() {
    const router = useRouter();
    const { hasPermission } = useSession();
    const allowed = hasPermission("satis.read");
    const canCreate = hasPermission("satis.manage");

    const [status, setStatus] = useState<OrderStatus | "">("");
    const [searchInput, setSearchInput] = useState("");
    const search = useDebounced(searchInput, 300);
    const [page, setPage] = useState(0);
    const [pageSize, setPageSize] = useState(25);
    const [result, setResult] = useState<{ items: OrderRow[]; total: number } | null>(null);
    const [error, setError] = useState<ApiError | null>(null);

    useEffect(() => {
        if (!allowed) return;
        let cancelled = false;
        sales.orders.list({ status: status || undefined, search: search || undefined, page: page + 1, pageSize }).then(
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
    }, [allowed, status, search, page, pageSize]);

    if (!allowed) return <Alert severity="warning">Siparişleri görüntülemek için yetkiniz yok.</Alert>;

    return (
        <>
            <PageHeader title="Siparişler" subtitle="Müşteri siparişleri, onay durumları ve faturalama" />
            <DashboardCard
                action={
                    canCreate ? (
                        <Button component={Link} href={`${ORDER_BASE}/yeni`} variant="contained">
                            Yeni sipariş
                        </Button>
                    ) : undefined
                }
            >
                <Stack direction={{ xs: "column", md: "row" }} spacing={2} sx={{ mb: 2 }}>
                    <TextField
                        select
                        size="small"
                        label="Durum"
                        value={status}
                        onChange={(e) => {
                            setStatus(e.target.value as OrderStatus | "");
                            setPage(0);
                        }}
                        sx={{ minWidth: 200 }}
                    >
                        <MenuItem value="">Tümü</MenuItem>
                        {(Object.keys(ORDER_STATUS_LABELS) as OrderStatus[]).map((s) => (
                            <MenuItem key={s} value={s}>
                                {ORDER_STATUS_LABELS[s]}
                            </MenuItem>
                        ))}
                    </TextField>
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
                </Stack>
                <FormAlert error={error} />
                {result === null ? (
                    <Skeleton variant="rounded" height={160} />
                ) : result.total === 0 && !error ? (
                    <Typography color="text.secondary" sx={{ py: 4, textAlign: "center" }}>
                        Gösterilecek sipariş yok.
                    </Typography>
                ) : (
                    <>
                        <TableContainer>
                            <Table size="small" sx={{ minWidth: 760 }}>
                                <TableHead>
                                    <TableRow>
                                        <TableCell>Numara</TableCell>
                                        <TableCell>Tarih</TableCell>
                                        <TableCell>Müşteri</TableCell>
                                        <TableCell>Durum</TableCell>
                                        <TableCell>Fatura</TableCell>
                                        <TableCell>Temsilci</TableCell>
                                        <TableCell align="right">Toplam</TableCell>
                                    </TableRow>
                                </TableHead>
                                <TableBody>
                                    {result.items.map((o) => (
                                        <TableRow key={o.id} hover sx={{ cursor: "pointer" }} onClick={() => router.push(`${ORDER_BASE}/${o.id}`)}>
                                            <TableCell>{o.number}</TableCell>
                                            <TableCell>{formatDate(o.date)}</TableCell>
                                            <TableCell>{o.customerName}</TableCell>
                                            <TableCell>
                                                <Chip size="small" color={ORDER_STATUS_COLOR[o.status]} variant={o.status === "draft" ? "outlined" : "filled"} label={ORDER_STATUS_LABELS[o.status]} />
                                            </TableCell>
                                            <TableCell>{INVOICE_STATE_LABELS[o.invoiceState]}</TableCell>
                                            <TableCell>{o.salesRepName ?? "—"}</TableCell>
                                            <TableCell align="right">{formatMoney(o.total, o.currency as Currency)}</TableCell>
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
                            rowsPerPageOptions={[25, 50, 100]}
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
