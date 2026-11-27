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
import { QUOTE_STATUS_LABELS, sales, type QuoteRow, type QuoteStatus } from "@/lib/api/sales";
import { formatDate } from "@/lib/date";
import { formatMoney, type Currency } from "@/lib/money";
import { useSession } from "@/session/SessionProvider";
import { QUOTE_BASE, QUOTE_STATUS_COLOR } from "./QuoteEditor";

/** Teklif listesi: durum ve arama süzgeçleri. */
export default function QuoteList() {
    const router = useRouter();
    const { hasPermission } = useSession();
    const allowed = hasPermission("satis.read");
    const canCreate = hasPermission("satis.manage");

    const [status, setStatus] = useState<QuoteStatus | "">("");
    const [searchInput, setSearchInput] = useState("");
    const search = useDebounced(searchInput, 300);
    const [page, setPage] = useState(0);
    const [pageSize, setPageSize] = useState(25);
    const [result, setResult] = useState<{ items: QuoteRow[]; total: number } | null>(null);
    const [error, setError] = useState<ApiError | null>(null);

    useEffect(() => {
        if (!allowed) return;
        let cancelled = false;
        sales.quotes.list({ status: status || undefined, search: search || undefined, page: page + 1, pageSize }).then(
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

    if (!allowed) return <Alert severity="warning">Teklifleri görüntülemek için yetkiniz yok.</Alert>;

    return (
        <>
            <PageHeader title="Teklifler" subtitle="Müşterilere verilen teklifler ve revizyonları" />
            <DashboardCard
                action={
                    canCreate ? (
                        <Button component={Link} href={`${QUOTE_BASE}/yeni`} variant="contained">
                            Yeni teklif
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
                            setStatus(e.target.value as QuoteStatus | "");
                            setPage(0);
                        }}
                        sx={{ minWidth: 170 }}
                    >
                        <MenuItem value="">Tümü</MenuItem>
                        {(Object.keys(QUOTE_STATUS_LABELS) as QuoteStatus[]).map((s) => (
                            <MenuItem key={s} value={s}>
                                {QUOTE_STATUS_LABELS[s]}
                            </MenuItem>
                        ))}
                    </TextField>
                    <TextField
                        size="small"
                        label="Ara"
                        placeholder="Teklif no veya müşteri"
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
                        Gösterilecek teklif yok.
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
                                        <TableCell>Geçerlilik</TableCell>
                                        <TableCell>Temsilci</TableCell>
                                        <TableCell align="right">Toplam</TableCell>
                                    </TableRow>
                                </TableHead>
                                <TableBody>
                                    {result.items.map((q) => (
                                        <TableRow key={q.id} hover sx={{ cursor: "pointer" }} onClick={() => router.push(`${QUOTE_BASE}/${q.id}`)}>
                                            <TableCell>
                                                {q.number}
                                                {q.revision > 1 && <Chip size="small" variant="outlined" label={`Rev. ${q.revision}`} sx={{ ml: 0.5 }} />}
                                            </TableCell>
                                            <TableCell>{formatDate(q.date)}</TableCell>
                                            <TableCell>{q.customerName}</TableCell>
                                            <TableCell>
                                                <Chip size="small" color={QUOTE_STATUS_COLOR[q.status]} variant={q.status === "draft" ? "outlined" : "filled"} label={QUOTE_STATUS_LABELS[q.status]} />
                                            </TableCell>
                                            <TableCell>{formatDate(q.validUntil)}</TableCell>
                                            <TableCell>{q.salesRepName ?? "—"}</TableCell>
                                            <TableCell align="right">{formatMoney(q.total, q.currency as Currency)}</TableCell>
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
