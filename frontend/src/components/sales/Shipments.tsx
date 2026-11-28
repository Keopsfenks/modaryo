"use client";

import { useEffect, useState } from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import Alert from "@mui/material/Alert";
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
import { SHIPMENT_STATUS_LABELS, sales, type ShipmentRow, type ShipmentStatus } from "@/lib/api/sales";
import { formatDate } from "@/lib/date";
import { useSession } from "@/session/SessionProvider";

export const SHIPMENT_BASE = "/satis/sevkler";

export const SHIPMENT_STATUS_COLOR: Record<ShipmentStatus, "default" | "info" | "success" | "error" | "warning"> = {
    draft: "default",
    picking: "warning",
    packed: "warning",
    shipped: "info",
    delivered: "success",
    deliveryFailed: "error",
    returned: "error",
    cancelled: "error",
};

/** Sevk listesi: durum ve arama süzgeçleri; satır sevk detayına gider. */
export default function Shipments() {
    const router = useRouter();
    const { hasPermission } = useSession();
    const allowed = hasPermission("satis.read") || hasPermission("sevkiyat.read");
    const [status, setStatus] = useState<ShipmentStatus | "">("");
    const [searchInput, setSearchInput] = useState("");
    const search = useDebounced(searchInput, 300);
    const [page, setPage] = useState(0);
    const [pageSize, setPageSize] = useState(25);
    const [result, setResult] = useState<{ items: ShipmentRow[]; total: number } | null>(null);
    const [error, setError] = useState<ApiError | null>(null);

    useEffect(() => {
        if (!allowed) return;
        let cancelled = false;
        sales.shipments.list({ status: status || undefined, search: search || undefined, page: page + 1, pageSize }).then(
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

    if (!allowed) return <Alert severity="warning">Sevkleri görüntülemek için yetkiniz yok.</Alert>;

    return (
        <>
            <PageHeader title="Sevkler" subtitle="Siparişlerden çıkan sevk irsaliyeleri ve teslim durumları" />
            <DashboardCard>
                <Stack direction={{ xs: "column", md: "row" }} spacing={2} sx={{ mb: 2 }}>
                    <TextField
                        select
                        size="small"
                        label="Durum"
                        value={status}
                        onChange={(e) => {
                            setStatus(e.target.value as ShipmentStatus | "");
                            setPage(0);
                        }}
                        sx={{ minWidth: 200 }}
                    >
                        <MenuItem value="">Tümü</MenuItem>
                        {(Object.keys(SHIPMENT_STATUS_LABELS) as ShipmentStatus[]).map((s) => (
                            <MenuItem key={s} value={s}>
                                {SHIPMENT_STATUS_LABELS[s]}
                            </MenuItem>
                        ))}
                    </TextField>
                    <TextField
                        size="small"
                        label="Ara"
                        placeholder="Sevk no, sipariş no veya müşteri"
                        value={searchInput}
                        onChange={(e) => {
                            setSearchInput(e.target.value);
                            setPage(0);
                        }}
                        sx={{ minWidth: 280 }}
                    />
                </Stack>
                <FormAlert error={error} />
                {result === null ? (
                    <Skeleton variant="rounded" height={160} />
                ) : result.total === 0 && !error ? (
                    <Typography color="text.secondary" sx={{ py: 4, textAlign: "center" }}>
                        Gösterilecek sevk yok.
                    </Typography>
                ) : (
                    <>
                        <TableContainer>
                            <Table size="small" sx={{ minWidth: 720 }}>
                                <TableHead>
                                    <TableRow>
                                        <TableCell>Numara</TableCell>
                                        <TableCell>Tarih</TableCell>
                                        <TableCell>Müşteri</TableCell>
                                        <TableCell>Sipariş</TableCell>
                                        <TableCell>Durum</TableCell>
                                        <TableCell>Fatura</TableCell>
                                    </TableRow>
                                </TableHead>
                                <TableBody>
                                    {result.items.map((s) => (
                                        <TableRow key={s.id} hover sx={{ cursor: "pointer" }} onClick={() => router.push(`${SHIPMENT_BASE}/${s.id}`)}>
                                            <TableCell>{s.number}</TableCell>
                                            <TableCell>{formatDate(s.date)}</TableCell>
                                            <TableCell>{s.customerName}</TableCell>
                                            <TableCell onClick={(e) => e.stopPropagation()}>
                                                <Link href={`/satis/siparisler/${s.salesOrderId}`}>{s.orderNumber}</Link>
                                            </TableCell>
                                            <TableCell>
                                                <Chip size="small" color={SHIPMENT_STATUS_COLOR[s.status]} label={SHIPMENT_STATUS_LABELS[s.status]} />
                                            </TableCell>
                                            <TableCell onClick={(e) => e.stopPropagation()}>
                                                {s.invoiceId ? <Link href={`/satis/faturalar/${s.invoiceId}`}>Faturayı aç</Link> : "—"}
                                            </TableCell>
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
