import { useEffect, useState } from "react";
import Alert from "@mui/material/Alert";
import Box from "@mui/material/Box";
import Button from "@mui/material/Button";
import Table from "@mui/material/Table";
import TableBody from "@mui/material/TableBody";
import TableCell from "@mui/material/TableCell";
import TableContainer from "@mui/material/TableContainer";
import TableHead from "@mui/material/TableHead";
import TableRow from "@mui/material/TableRow";
import Typography from "@mui/material/Typography";
import FormAlert from "@/components/auth/FormAlert";
import DashboardCard from "@/components/dashboard/DashboardCard";
import { ApiError } from "@/lib/api/client";
import { erp, type CurrentAccountDto, type LedgerEntryType, type StatementRow } from "@/lib/api/erp";
import { formatDate, todayIso } from "@/lib/date";
import { formatMoney } from "@/lib/money";
import CancelEntryDialog from "./CancelEntryDialog";
import EntryForm from "./EntryForm";

const MAX_ROWS = 100;

const TYPE_LABELS: Record<LedgerEntryType, string> = {
    opening: "Açılış",
    debitNote: "Borç dekontu",
    creditNote: "Alacak dekontu",
};

type MovementsTabProps = {
    account: CurrentAccountDto;
    /** cari.movements izni: hareket formu ve iptal butonu. */
    canMove: boolean;
    /** Hareket eklenip iptal edildikten sonra güncel kartla (başlık bakiyesi için) çağrılır. */
    onChanged: (account: CurrentAccountDto) => void;
};

/** Hareket formu ve son 100 hareketin listesi (en yeni üstte). */
export default function MovementsTab({ account, canMove, onChanged }: MovementsTabProps) {
    const [rows, setRows] = useState<StatementRow[] | null>(null);
    const [loadError, setLoadError] = useState<ApiError | null>(null);
    const [version, setVersion] = useState(0);
    const [limitExceeded, setLimitExceeded] = useState(false);
    const [cancelling, setCancelling] = useState<StatementRow | null>(null);

    useEffect(() => {
        let ignore = false;
        erp.statement(account.id, "1900-01-01", todayIso()).then(
            (result) => {
                if (ignore) return;
                setRows(result.rows.slice(-MAX_ROWS).reverse());
                setLoadError(null);
            },
            (e) => {
                if (ignore) return;
                if (!(e instanceof ApiError)) throw e;
                setLoadError(e);
            },
        );
        return () => {
            ignore = true;
        };
    }, [account.id, version]);

    const refresh = async () => {
        setVersion((v) => v + 1);
        onChanged(await erp.getAccount(account.id));
    };

    return (
        <Box sx={{ display: "flex", flexDirection: "column", gap: 3 }}>
            {canMove &&
                (account.isActive ? (
                    <DashboardCard title="Yeni hareket">
                        {limitExceeded && (
                            <Alert severity="warning" sx={{ mb: 2 }}>
                                Bu hareketle bakiye kredi limitini aştı. Hareket kaydedildi.
                            </Alert>
                        )}
                        <EntryForm
                            account={account}
                            onAdded={(result) => {
                                setLimitExceeded(result.creditLimitExceeded);
                                void refresh();
                            }}
                        />
                    </DashboardCard>
                ) : (
                    <Alert severity="info">Pasif cari hesaba hareket girilemez. Hareket girmek için kartı etkinleştirin.</Alert>
                ))}

            <DashboardCard title="Hareketler" subtitle={`Son ${MAX_ROWS} hareket`}>
                <FormAlert error={loadError} />
                {rows && rows.length === 0 ? (
                    <Typography color="text.secondary">Henüz hareket yok</Typography>
                ) : (
                    <TableContainer>
                        <Table size="small">
                            <TableHead>
                                <TableRow>
                                    <TableCell>Tarih</TableCell>
                                    <TableCell>Tür</TableCell>
                                    <TableCell>Belge no</TableCell>
                                    <TableCell>Açıklama</TableCell>
                                    <TableCell align="right">Borç</TableCell>
                                    <TableCell align="right">Alacak</TableCell>
                                    <TableCell>Vade</TableCell>
                                    {canMove && <TableCell />}
                                </TableRow>
                            </TableHead>
                            <TableBody>
                                {rows?.map((r) => {
                                    const struck = r.cancelled ? { textDecoration: "line-through" } : undefined;
                                    return (
                                        <TableRow key={r.id}>
                                            <TableCell sx={struck}>{formatDate(r.date)}</TableCell>
                                            <TableCell sx={struck}>{TYPE_LABELS[r.type]}</TableCell>
                                            <TableCell sx={struck}>{r.documentNo}</TableCell>
                                            <TableCell>
                                                <Box component="span" sx={struck}>
                                                    {r.description}
                                                </Box>
                                                {r.cancelled && (
                                                    <Typography variant="caption" color="error" component="div">
                                                        İptal nedeni: {r.cancelReason}
                                                    </Typography>
                                                )}
                                            </TableCell>
                                            <TableCell align="right" sx={struck}>
                                                {r.debit > 0 ? formatMoney(r.debit, account.currency) : ""}
                                            </TableCell>
                                            <TableCell align="right" sx={struck}>
                                                {r.credit > 0 ? formatMoney(r.credit, account.currency) : ""}
                                            </TableCell>
                                            <TableCell sx={struck}>{formatDate(r.dueDate)}</TableCell>
                                            {canMove && (
                                                <TableCell align="right">
                                                    {!r.cancelled && (
                                                        <Button size="small" color="error" onClick={() => setCancelling(r)}>
                                                            İptal et
                                                        </Button>
                                                    )}
                                                </TableCell>
                                            )}
                                        </TableRow>
                                    );
                                })}
                            </TableBody>
                        </Table>
                    </TableContainer>
                )}
            </DashboardCard>

            {cancelling && (
                <CancelEntryDialog
                    accountId={account.id}
                    entry={cancelling}
                    onClose={() => setCancelling(null)}
                    onCancelled={() => {
                        setCancelling(null);
                        void refresh();
                    }}
                />
            )}
        </Box>
    );
}
