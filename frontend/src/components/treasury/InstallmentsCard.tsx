"use client";

import { useCallback, useEffect, useState } from "react";
import Button from "@mui/material/Button";
import Dialog from "@mui/material/Dialog";
import DialogActions from "@mui/material/DialogActions";
import DialogContent from "@mui/material/DialogContent";
import DialogTitle from "@mui/material/DialogTitle";
import MenuItem from "@mui/material/MenuItem";
import Stack from "@mui/material/Stack";
import Table from "@mui/material/Table";
import TableBody from "@mui/material/TableBody";
import TableCell from "@mui/material/TableCell";
import TableContainer from "@mui/material/TableContainer";
import TableHead from "@mui/material/TableHead";
import TableRow from "@mui/material/TableRow";
import TextField from "@mui/material/TextField";
import Typography from "@mui/material/Typography";
import FormAlert from "@/components/auth/FormAlert";
import DashboardCard from "@/components/dashboard/DashboardCard";
import { ApiError } from "@/lib/api/client";
import { errorMessage } from "@/lib/api/errorMessages";
import type { CurrentAccountDto } from "@/lib/api/erp";
import { treasury, type OpenItemForAllocation } from "@/lib/api/treasury";
import { formatDate } from "@/lib/date";
import { formatMoney } from "@/lib/money";
import { useSession } from "@/session/SessionProvider";
import AllocationPicker, { type AllocationChange } from "./AllocationPicker";

const SOURCE_LABELS: Record<OpenItemForAllocation["source"], string> = {
    manual: "Manuel",
    invoice: "Fatura",
    payment: "Tahsilat/Ödeme",
    check: "Çek",
    note: "Senet",
    voucher: "Mahsup",
};

const nameOf = (i: OpenItemForAllocation) => i.documentNo ?? i.description ?? "Kalem";

/** Vade sekmesi: taksit başına tutar ve kalan; karşı yöndeki bir kalemle elle eşleştirme. */
export default function InstallmentsCard({ account }: { account: CurrentAccountDto }) {
    const { hasPermission } = useSession();
    const canAllocate = hasPermission("tahsilat.manage") || hasPermission("odeme.manage");

    const [items, setItems] = useState<OpenItemForAllocation[] | null>(null);
    const [error, setError] = useState<ApiError | string | null>(null);
    const [target, setTarget] = useState<OpenItemForAllocation | null>(null);
    const [sourceId, setSourceId] = useState("");
    const [change, setChange] = useState<AllocationChange>({ allocations: [], total: 0, valid: true });
    const [dialogError, setDialogError] = useState<ApiError | string | null>(null);
    const [reload, setReload] = useState(0);

    const load = useCallback(() => treasury.openItems(account.id), [account.id]);

    useEffect(() => {
        let ignore = false;
        load().then(
            (result) => {
                if (ignore) return;
                setItems(result);
                setError(null);
            },
            (e) => {
                if (ignore) return;
                if (!(e instanceof ApiError)) throw e;
                setError(e);
                setItems((c) => c ?? []);
            },
        );
        return () => {
            ignore = true;
        };
    }, [load, reload]);

    const sources = target ? (items ?? []).filter((i) => i.direction !== target.direction && i.allocatable > 0) : [];
    const source = sources.find((s) => s.dueLineId === sourceId);

    async function save() {
        if (!source) return setDialogError("Eşleştirilecek karşı kalemi seçin.");
        if (change.allocations.length === 0 || !change.valid) return setDialogError("Geçerli bir eşleştirme tutarı girin.");
        try {
            await treasury.allocate({ creditLineId: source.dueLineId, allocations: change.allocations });
            setTarget(null);
            setSourceId("");
            setReload((n) => n + 1);
        } catch (e) {
            setDialogError(errorMessage(e));
        }
    }

    const lines = (items ?? []).filter((i) => i.open > 0);

    return (
        <>
            <DashboardCard title="Taksitler">
                <FormAlert error={error} />
                {items && lines.length === 0 ? (
                    <Typography color="text.secondary">Açık taksit yok</Typography>
                ) : (
                    items && (
                        <TableContainer>
                            <Table size="small">
                                <TableHead>
                                    <TableRow>
                                        <TableCell>Belge</TableCell>
                                        <TableCell>Tür</TableCell>
                                        <TableCell>Taksit</TableCell>
                                        <TableCell>Vade</TableCell>
                                        <TableCell align="right">Tutar</TableCell>
                                        <TableCell align="right">Kalan</TableCell>
                                        {canAllocate && <TableCell />}
                                    </TableRow>
                                </TableHead>
                                <TableBody>
                                    {lines.map((i) => (
                                        <TableRow key={i.dueLineId}>
                                            <TableCell>{nameOf(i)}</TableCell>
                                            <TableCell>{SOURCE_LABELS[i.source]}</TableCell>
                                            <TableCell>{i.sequence}</TableCell>
                                            <TableCell>{formatDate(i.dueDate)}</TableCell>
                                            <TableCell align="right">{formatMoney(i.amount, account.currency)}</TableCell>
                                            <TableCell align="right">{formatMoney(i.open, account.currency)}</TableCell>
                                            {canAllocate && (
                                                <TableCell align="right">
                                                    <Button
                                                        size="small"
                                                        aria-label={`${nameOf(i)} taksit ${i.sequence} eşleştir`}
                                                        onClick={() => {
                                                            setDialogError(null);
                                                            setSourceId("");
                                                            setTarget(i);
                                                        }}
                                                    >
                                                        Eşleştir
                                                    </Button>
                                                </TableCell>
                                            )}
                                        </TableRow>
                                    ))}
                                </TableBody>
                            </Table>
                        </TableContainer>
                    )
                )}
            </DashboardCard>

            <Dialog open={target !== null} onClose={() => setTarget(null)} fullWidth maxWidth="sm">
                <DialogTitle>Elle eşleştir</DialogTitle>
                <DialogContent>
                    {target && (
                        <Stack spacing={2} sx={{ pt: 1 }}>
                            <FormAlert error={dialogError} />
                            <TextField select size="small" label="Karşı kalem (tahsilat / ödeme / avans)" value={sourceId} onChange={(e) => setSourceId(e.target.value)} helperText={sources.length === 0 ? "Eşleştirilebilecek karşı kalem yok." : undefined}>
                                {sources.map((s) => (
                                    <MenuItem key={s.dueLineId} value={s.dueLineId}>
                                        {nameOf(s)} · {formatDate(s.date)} · {formatMoney(s.allocatable, account.currency)}
                                    </MenuItem>
                                ))}
                            </TextField>
                            <AllocationPicker items={[target]} budget={source ? source.allocatable : null} currency={account.currency} onChange={setChange} />
                        </Stack>
                    )}
                </DialogContent>
                <DialogActions>
                    <Button onClick={() => setTarget(null)}>Vazgeç</Button>
                    <Button variant="contained" disabled={!source} onClick={() => void save()}>
                        Eşleştir
                    </Button>
                </DialogActions>
            </Dialog>
        </>
    );
}
