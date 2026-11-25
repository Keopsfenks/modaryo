"use client";

import { useEffect, useState } from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import Alert from "@mui/material/Alert";
import Button from "@mui/material/Button";
import Chip from "@mui/material/Chip";
import FormControlLabel from "@mui/material/FormControlLabel";
import MenuItem from "@mui/material/MenuItem";
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
import { PO_STATUS_LABELS, purchasing, type PoRow, type PoStatus } from "@/lib/api/purchasing";
import { formatDate } from "@/lib/date";
import { formatMoney, type Currency } from "@/lib/money";
import { useSession } from "@/session/SessionProvider";
import { PO_BASE, STATUS_COLOR } from "./OrderDetail";

/** Satın alma siparişleri: durum, geciken ve arama süzgeçleri. Tutarlar fiyat.alis olmadan gelmez. */
export default function OrderList() {
    const router = useRouter();
    const { hasPermission } = useSession();
    const allowed = hasPermission("satinalma.read");
    const canCreate = hasPermission("satinalma.manage") && hasPermission("fiyat.alis");
    const showPrices = hasPermission("fiyat.alis");

    const [status, setStatus] = useState<PoStatus | "">("");
    const [overdue, setOverdue] = useState(false);
    const [searchInput, setSearchInput] = useState("");
    const search = useDebounced(searchInput, 300);
    const [page, setPage] = useState(0);
    const [pageSize, setPageSize] = useState(25);
    const [result, setResult] = useState<{ items: PoRow[]; total: number } | null>(null);
    const [error, setError] = useState<ApiError | null>(null);

    useEffect(() => {
        if (!allowed) return;
        let cancelled = false;
        purchasing.orders.list({ status: status || undefined, overdue: overdue || undefined, search: search || undefined, page: page + 1, pageSize }).then(
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
    }, [allowed, status, overdue, search, page, pageSize]);

    if (!allowed) return <Alert severity="warning">Siparişleri görüntülemek için yetkiniz yok.</Alert>;
    const reset = () => setPage(0);

    return (
        <>
            <PageHeader title="Satın alma siparişleri" subtitle="Tedarikçilere verilen siparişler ve bekleyen teslimatlar" />
            <DashboardCard
                action={
                    canCreate ? (
                        <Button component={Link} href={`${PO_BASE}/yeni`} variant="contained">
                            Yeni sipariş
                        </Button>
                    ) : undefined
                }
            >
                <Stack direction={{ xs: "column", md: "row" }} spacing={2} sx={{ mb: 2, alignItems: { md: "center" } }}>
                    <TextField
                        select
                        size="small"
                        label="Durum"
                        value={status}
                        onChange={(e) => {
                            setStatus(e.target.value as PoStatus | "");
                            reset();
                        }}
                        sx={{ minWidth: 170 }}
                    >
                        <MenuItem value="">Tümü</MenuItem>
                        {(Object.keys(PO_STATUS_LABELS) as PoStatus[]).map((s) => (
                            <MenuItem key={s} value={s}>
                                {PO_STATUS_LABELS[s]}
                            </MenuItem>
                        ))}
                    </TextField>
                    <TextField
                        size="small"
                        label="Ara"
                        placeholder="Sipariş no veya tedarikçi"
                        value={searchInput}
                        onChange={(e) => {
                            setSearchInput(e.target.value);
                            reset();
                        }}
                        sx={{ minWidth: 240 }}
                    />
                    <FormControlLabel
                        control={
                            <Switch
                                checked={overdue}
                                onChange={(e) => {
                                    setOverdue(e.target.checked);
                                    reset();
                                }}
                            />
                        }
                        label="Yalnızca geciken"
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
                                        <TableCell>Tedarikçi</TableCell>
                                        <TableCell>Durum</TableCell>
                                        <TableCell>Beklenen</TableCell>
                                        <TableCell align="right">Satır</TableCell>
                                        {showPrices && <TableCell align="right">Toplam</TableCell>}
                                    </TableRow>
                                </TableHead>
                                <TableBody>
                                    {result.items.map((o) => (
                                        <TableRow key={o.id} hover sx={{ cursor: "pointer" }} onClick={() => router.push(`${PO_BASE}/${o.id}`)}>
                                            <TableCell>{o.number}</TableCell>
                                            <TableCell>{formatDate(o.date)}</TableCell>
                                            <TableCell>{o.supplierName}</TableCell>
                                            <TableCell>
                                                <Chip size="small" color={STATUS_COLOR[o.status]} variant={o.status === "draft" ? "outlined" : "filled"} label={PO_STATUS_LABELS[o.status]} />
                                                {o.overdue && <Chip size="small" color="error" label="Gecikti" sx={{ ml: 0.5 }} />}
                                            </TableCell>
                                            <TableCell>{formatDate(o.expectedDate)}</TableCell>
                                            <TableCell align="right">{o.lineCount}</TableCell>
                                            {showPrices && <TableCell align="right">{o.total === null ? "—" : formatMoney(o.total, o.currency as Currency)}</TableCell>}
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
