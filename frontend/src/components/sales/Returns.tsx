"use client";

import { useCallback, useEffect, useState } from "react";
import Link from "next/link";
import Alert from "@mui/material/Alert";
import Button from "@mui/material/Button";
import Chip from "@mui/material/Chip";
import Skeleton from "@mui/material/Skeleton";
import Table from "@mui/material/Table";
import TableBody from "@mui/material/TableBody";
import TableCell from "@mui/material/TableCell";
import TableContainer from "@mui/material/TableContainer";
import TableHead from "@mui/material/TableHead";
import TablePagination from "@mui/material/TablePagination";
import TableRow from "@mui/material/TableRow";
import Typography from "@mui/material/Typography";
import FormAlert from "@/components/auth/FormAlert";
import PageHeader from "@/components/common/PageHeader";
import DashboardCard from "@/components/dashboard/DashboardCard";
import ReasonDialog from "@/components/purchasing/ReasonDialog";
import { ApiError } from "@/lib/api/client";
import { sales, type ReturnRow } from "@/lib/api/sales";
import { formatDate } from "@/lib/date";
import { useSession } from "@/session/SessionProvider";

/** Müşteri iadeleri: liste ve iptal. İadeler satış faturası ekranındaki "İade al" ile başlatılır. */
export default function Returns() {
    const { hasPermission } = useSession();
    const allowed = hasPermission("satis.read");
    const canCancel = hasPermission("satis.return");
    const [page, setPage] = useState(0);
    const [pageSize, setPageSize] = useState(25);
    const [result, setResult] = useState<{ items: ReturnRow[]; total: number } | null>(null);
    const [error, setError] = useState<ApiError | null>(null);
    const [cancelling, setCancelling] = useState<ReturnRow | null>(null);

    const load = useCallback(async () => {
        try {
            const r = await sales.returns.list({ page: page + 1, pageSize });
            setError(null);
            setResult(r);
        } catch (e) {
            if (!(e instanceof ApiError)) throw e;
            setError(e);
            setResult((c) => c ?? { items: [], total: 0 });
        }
    }, [page, pageSize]);

    useEffect(() => {
        if (!allowed) return;
        // eslint-disable-next-line react-hooks/set-state-in-effect -- liste yükleme
        void load();
    }, [allowed, load]);

    if (!allowed) return <Alert severity="warning">İadeleri görüntülemek için yetkiniz yok.</Alert>;

    return (
        <>
            <PageHeader title="İadeler" subtitle="Müşteri iadeleri; satış faturasından başlatılır" />
            <DashboardCard>
                <FormAlert error={error} />
                {result === null ? (
                    <Skeleton variant="rounded" height={160} />
                ) : result.total === 0 && !error ? (
                    <Typography color="text.secondary" sx={{ py: 4, textAlign: "center" }}>
                        Gösterilecek iade yok.
                    </Typography>
                ) : (
                    <>
                        <TableContainer>
                            <Table size="small" sx={{ minWidth: 640 }}>
                                <TableHead>
                                    <TableRow>
                                        <TableCell>Numara</TableCell>
                                        <TableCell>Tarih</TableCell>
                                        <TableCell>Müşteri</TableCell>
                                        <TableCell>Fatura</TableCell>
                                        <TableCell>Durum</TableCell>
                                        <TableCell />
                                    </TableRow>
                                </TableHead>
                                <TableBody>
                                    {result.items.map((r) => (
                                        <TableRow key={r.id}>
                                            <TableCell>{r.number}</TableCell>
                                            <TableCell>{formatDate(r.date)}</TableCell>
                                            <TableCell>{r.customerName}</TableCell>
                                            <TableCell>
                                                <Link href={`/satis/faturalar/${r.invoiceId}`}>{r.invoiceNumber ?? "Fatura"}</Link>
                                            </TableCell>
                                            <TableCell>
                                                <Chip size="small" color={r.status === "cancelled" ? "error" : "success"} label={r.status === "cancelled" ? "İptal" : "Kayıtlı"} />
                                            </TableCell>
                                            <TableCell align="right">
                                                {canCancel && r.status !== "cancelled" && (
                                                    <Button size="small" color="error" onClick={() => setCancelling(r)}>
                                                        İptal et
                                                    </Button>
                                                )}
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
            <ReasonDialog
                open={cancelling !== null}
                title="İadeyi iptal et"
                text="Stok girişi geri alınır ve taslak iade faturası silinir. Kesilmiş iade faturası varsa önce o fatura iptal edilmelidir."
                confirmLabel="İptal et"
                onClose={() => setCancelling(null)}
                onConfirm={async (reason) => {
                    if (!cancelling) return;
                    await sales.returns.cancel(cancelling.id, reason);
                    setCancelling(null);
                    await load();
                }}
            />
        </>
    );
}
