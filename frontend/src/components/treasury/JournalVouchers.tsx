"use client";

import { useCallback, useEffect, useState } from "react";
import Alert from "@mui/material/Alert";
import Button from "@mui/material/Button";
import Chip from "@mui/material/Chip";
import Dialog from "@mui/material/Dialog";
import DialogActions from "@mui/material/DialogActions";
import DialogContent from "@mui/material/DialogContent";
import DialogTitle from "@mui/material/DialogTitle";
import Skeleton from "@mui/material/Skeleton";
import Table from "@mui/material/Table";
import TableBody from "@mui/material/TableBody";
import TableCell from "@mui/material/TableCell";
import TableContainer from "@mui/material/TableContainer";
import TableHead from "@mui/material/TableHead";
import TableRow from "@mui/material/TableRow";
import TextField from "@mui/material/TextField";
import Typography from "@mui/material/Typography";
import FormAlert from "@/components/auth/FormAlert";
import PageHeader from "@/components/common/PageHeader";
import DashboardCard from "@/components/dashboard/DashboardCard";
import { errorMessage } from "@/lib/api/errorMessages";
import { treasury, type VoucherDto } from "@/lib/api/treasury";
import { formatDate } from "@/lib/date";
import { formatMoney } from "@/lib/money";
import { useSession } from "@/session/SessionProvider";
import JournalVoucherForm from "./JournalVoucherForm";

/** Mahsup fişleri sayfası: yeni fiş girişi ve son fişler; fiş iptali neden ister. */
export default function JournalVouchers() {
    const { hasPermission } = useSession();
    const canManage = hasPermission("muhasebe.manage");
    const canRead = canManage || hasPermission("finans.read") || hasPermission("muhasebe.export");

    const [vouchers, setVouchers] = useState<VoucherDto[] | null>(null);
    const [error, setError] = useState<string | null>(null);
    const [cancelling, setCancelling] = useState<VoucherDto | null>(null);
    const [reason, setReason] = useState("");

    const load = useCallback(async () => {
        try {
            setVouchers(await treasury.journalVouchers({ includeCancelled: true }));
            setError(null);
        } catch (e) {
            setError(errorMessage(e));
            setVouchers((v) => v ?? []);
        }
    }, []);

    useEffect(() => {
        if (!canRead) return;
        // eslint-disable-next-line react-hooks/set-state-in-effect -- ilk yükleme
        void load();
    }, [canRead, load]);

    if (!canRead) return <Alert severity="warning">Mahsup fişleri için yetkiniz yok.</Alert>;

    async function cancel() {
        if (!cancelling || !reason.trim()) return;
        try {
            await treasury.cancelJournalVoucher(cancelling.id, reason.trim());
            setCancelling(null);
            setReason("");
            await load();
        } catch (e) {
            setCancelling(null);
            setError(errorMessage(e));
        }
    }

    return (
        <>
            <PageHeader title="Mahsup fişleri" subtitle="Elle yevmiye fişi; cari bağlı satırlar cari hesaba da işlenir." />
            <div style={{ display: "flex", flexDirection: "column", gap: 24 }}>
                {canManage && <JournalVoucherForm onSaved={() => void load()} />}
                <DashboardCard title="Fişler">
                    <FormAlert error={error} />
                    {vouchers === null ? (
                        <Skeleton variant="rounded" height={120} />
                    ) : vouchers.length === 0 ? (
                        <Typography color="text.secondary">Henüz mahsup fişi yok.</Typography>
                    ) : (
                        <TableContainer>
                            <Table size="small">
                                <TableHead>
                                    <TableRow>
                                        <TableCell>Fiş no</TableCell>
                                        <TableCell>Tarih</TableCell>
                                        <TableCell>Açıklama</TableCell>
                                        <TableCell align="right">Tutar</TableCell>
                                        <TableCell>Kaynak</TableCell>
                                        <TableCell />
                                    </TableRow>
                                </TableHead>
                                <TableBody>
                                    {vouchers.map((v) => (
                                        <TableRow key={v.id} hover sx={v.isCancelled ? { opacity: 0.6 } : undefined}>
                                            <TableCell>{v.number}</TableCell>
                                            <TableCell>{formatDate(v.date)}</TableCell>
                                            <TableCell>{v.description}</TableCell>
                                            <TableCell align="right">{formatMoney(v.totalDebit, "TRY")}</TableCell>
                                            <TableCell>{v.externalCaller ? `Sistem: ${v.externalCaller}` : "Elle"}</TableCell>
                                            <TableCell align="right">
                                                {v.isCancelled ? (
                                                    <Chip size="small" label="İptal" />
                                                ) : (
                                                    canManage && (
                                                        <Button size="small" color="error" onClick={() => setCancelling(v)}>
                                                            İptal et
                                                        </Button>
                                                    )
                                                )}
                                            </TableCell>
                                        </TableRow>
                                    ))}
                                </TableBody>
                            </Table>
                        </TableContainer>
                    )}
                </DashboardCard>
            </div>
            <Dialog open={!!cancelling} onClose={() => setCancelling(null)} fullWidth maxWidth="xs">
                <DialogTitle>{cancelling?.number} iptal edilsin mi?</DialogTitle>
                <DialogContent>
                    <TextField autoFocus fullWidth label="İptal nedeni" value={reason} onChange={(e) => setReason(e.target.value)} sx={{ mt: 1 }} />
                </DialogContent>
                <DialogActions>
                    <Button onClick={() => setCancelling(null)}>Vazgeç</Button>
                    <Button color="error" disabled={!reason.trim()} onClick={() => void cancel()}>
                        İptal et
                    </Button>
                </DialogActions>
            </Dialog>
        </>
    );
}
