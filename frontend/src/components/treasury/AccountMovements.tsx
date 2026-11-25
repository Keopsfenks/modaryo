"use client";

import { useCallback, useEffect, useState } from "react";
import Link from "next/link";
import Alert from "@mui/material/Alert";
import Box from "@mui/material/Box";
import Button from "@mui/material/Button";
import Dialog from "@mui/material/Dialog";
import DialogActions from "@mui/material/DialogActions";
import DialogContent from "@mui/material/DialogContent";
import DialogTitle from "@mui/material/DialogTitle";
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
import { ApiError } from "@/lib/api/client";
import { errorMessage } from "@/lib/api/errorMessages";
import { ACCOUNT_KIND_LABELS, treasury, type AccountMovements as Movements, type FinanceDocumentKind, type MovementRow } from "@/lib/api/treasury";
import { formatDate } from "@/lib/date";
import { formatMoney, type Currency } from "@/lib/money";
import { useSession } from "@/session/SessionProvider";

const KIND_LABELS: Record<FinanceDocumentKind, string> = {
    collection: "Tahsilat",
    payment: "Ödeme",
    transfer: "Virman",
    expense: "Masraf",
    income: "Gelir",
    instrumentEvent: "Çek/senet",
    journalVoucher: "Mahsup fişi",
};

const CANCEL_PERMISSION: Partial<Record<FinanceDocumentKind, string>> = {
    collection: "tahsilat.manage",
    payment: "odeme.manage",
    transfer: "virman.manage",
    expense: "masraf.manage",
    income: "masraf.manage",
};

/** Hesap detayı: bakiye, tarih aralığı ve yürüyen bakiyeli hareketler; belge iptali. */
export default function AccountMovements({ id }: { id: string }) {
    const { hasPermission } = useSession();
    const canRead = hasPermission("finans.read");

    const [from, setFrom] = useState("");
    const [to, setTo] = useState("");
    const [page, setPage] = useState(0);
    const [pageSize, setPageSize] = useState(25);
    const [data, setData] = useState<Movements | null>(null);
    const [error, setError] = useState<ApiError | string | null>(null);
    const [cancelling, setCancelling] = useState<MovementRow | null>(null);
    const [reason, setReason] = useState("");
    const [reload, setReload] = useState(0);

    const load = useCallback(() => treasury.movements(id, { from: from || undefined, to: to || undefined, page: page + 1, pageSize }), [id, from, to, page, pageSize]);

    useEffect(() => {
        if (!canRead) return;
        let ignore = false;
        load().then(
            (result) => {
                if (ignore) return;
                setData(result);
                setError(null);
            },
            (e) => {
                if (ignore) return;
                if (!(e instanceof ApiError)) throw e;
                setError(e);
            },
        );
        return () => {
            ignore = true;
        };
    }, [canRead, load, reload]);

    if (!canRead) return <Alert severity="warning">Hesap hareketlerini görmek için yetkiniz yok.</Alert>;

    const currency = (data?.account.currency ?? "TRY") as Currency;

    async function confirmCancel() {
        if (!cancelling) return;
        const target = cancelling;
        try {
            await treasury.cancelDocument(target.documentId, reason.trim());
            setCancelling(null);
            setReason("");
            setReload((n) => n + 1);
        } catch (e) {
            setCancelling(null);
            setError(errorMessage(e));
        }
    }

    return (
        <>
            <PageHeader title={data?.account.name ?? "Hesap"} subtitle={data ? `${ACCOUNT_KIND_LABELS[data.account.kind]} · ${data.account.currency}` : undefined} />
            <FormAlert error={error} />
            <Stack spacing={3}>
                <DashboardCard>
                    <Box sx={{ display: "flex", flexWrap: "wrap", gap: 5, alignItems: "flex-end" }}>
                        <div>
                            <Typography variant="caption" color="text.secondary" component="div">
                                Bakiye
                            </Typography>
                            <Typography variant="h5" component="div" sx={data && data.account.balance < 0 ? { color: "error.main" } : undefined}>
                                {data ? formatMoney(data.account.balance, currency) : "—"}
                            </Typography>
                        </div>
                        <TextField size="small" type="date" label="Başlangıç" value={from} onChange={(e) => { setFrom(e.target.value); setPage(0); }} slotProps={{ inputLabel: { shrink: true } }} />
                        <TextField size="small" type="date" label="Bitiş" value={to} onChange={(e) => { setTo(e.target.value); setPage(0); }} slotProps={{ inputLabel: { shrink: true } }} />
                        <Button component={Link} href="/finans/kasa-banka" variant="text">
                            Hesaplara dön
                        </Button>
                    </Box>
                </DashboardCard>
                <DashboardCard title="Hareketler">
                    {data === null ? (
                        <Skeleton variant="rounded" height={160} />
                    ) : (
                        <>
                            <TableContainer>
                                <Table size="small">
                                    <TableHead>
                                        <TableRow>
                                            <TableCell>Tarih</TableCell>
                                            <TableCell>Belge</TableCell>
                                            <TableCell>Açıklama</TableCell>
                                            <TableCell align="right">Tutar</TableCell>
                                            <TableCell align="right">Bakiye</TableCell>
                                            <TableCell />
                                        </TableRow>
                                    </TableHead>
                                    <TableBody>
                                        <TableRow>
                                            <TableCell colSpan={4}>Açılış bakiyesi</TableCell>
                                            <TableCell align="right">{formatMoney(data.openingBalance, currency)}</TableCell>
                                            <TableCell />
                                        </TableRow>
                                        {data.rows.map((r) => {
                                            const permission = r.documentKind ? CANCEL_PERMISSION[r.documentKind] : undefined;
                                            return (
                                                <TableRow key={r.id} sx={r.isCancelled ? { "& td": { textDecoration: "line-through", color: "text.disabled" } } : undefined}>
                                                    <TableCell>{formatDate(r.date)}</TableCell>
                                                    <TableCell>{[r.documentKind ? KIND_LABELS[r.documentKind] : null, r.documentNumber].filter(Boolean).join(" ")}</TableCell>
                                                    <TableCell>{r.description}</TableCell>
                                                    <TableCell align="right" sx={r.amount < 0 ? { color: "error.main" } : undefined}>
                                                        {formatMoney(r.amount, currency)}
                                                    </TableCell>
                                                    <TableCell align="right">{formatMoney(r.balance, currency)}</TableCell>
                                                    <TableCell align="right">
                                                        {permission && !r.isCancelled && hasPermission(permission) && (
                                                            <Button size="small" color="error" aria-label={`${r.documentNumber ?? "Belge"} iptal`} onClick={() => setCancelling(r)}>
                                                                İptal
                                                            </Button>
                                                        )}
                                                    </TableCell>
                                                </TableRow>
                                            );
                                        })}
                                    </TableBody>
                                </Table>
                            </TableContainer>
                            <TablePagination
                                component="div"
                                count={data.total}
                                page={page}
                                rowsPerPage={pageSize}
                                rowsPerPageOptions={[25, 50, 100]}
                                labelRowsPerPage="Sayfa başına"
                                onPageChange={(_, p) => setPage(p)}
                                onRowsPerPageChange={(e) => {
                                    setPageSize(Number(e.target.value));
                                    setPage(0);
                                }}
                            />
                        </>
                    )}
                </DashboardCard>
            </Stack>

            <Dialog open={cancelling !== null} onClose={() => setCancelling(null)} fullWidth maxWidth="xs">
                <DialogTitle>Belge iptal edilsin mi?</DialogTitle>
                <DialogContent>
                    <TextField autoFocus fullWidth margin="dense" label="İptal nedeni" value={reason} onChange={(e) => setReason(e.target.value)} />
                </DialogContent>
                <DialogActions>
                    <Button onClick={() => setCancelling(null)}>Vazgeç</Button>
                    <Button color="error" variant="contained" disabled={!reason.trim()} onClick={() => void confirmCancel()}>
                        İptal et
                    </Button>
                </DialogActions>
            </Dialog>
        </>
    );
}
